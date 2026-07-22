using System;
using System.Collections.Generic;
using ExitGames.Client.Photon;
using Photon.Pun;
using Photon.Realtime;
using UnityEngine;

namespace Crownfall
{
    public enum NetPhase { Idle, Connecting, InLobby, Matchmaking, InRoom, Starting, InMatch, Failed }

    /// Connection + matchmaking service. Owns the Photon lifecycle: connect,
    /// quick-match into a 6-player room, ready-up, then hands the start order to
    /// NetMatchLink (master assigns team/slot by join order, AI backfills the
    /// rest). Offline practice runs the exact same flow through PUN's offline
    /// mode so the whole codepath is testable without a server.
    public class CrownfallNet : MonoBehaviourPunCallbacks
    {
        public static CrownfallNet I { get; private set; }

        public NetPhase Phase { get; private set; } = NetPhase.Idle;
        public string StatusLine { get; private set; } = "";
        /// True when the running/starting match is a networked one (incl. offline-mode smoke runs).
        public bool IsOnlineMatch { get; private set; }
        public int PingMs => PhotonNetwork.IsConnected ? PhotonNetwork.GetPing() : 0;

        public event Action PhaseChanged;
        public event Action RosterChanged;

        public struct RosterEntry
        {
            public string name;
            public int classIdx;
            public bool ready;
            public bool isMe;
            public int actorNumber;
        }

        const string PropClass = "cls";
        const string PropReady = "rdy";
        const byte MaxPlayers = 6;

        bool autoOnline;
        string nickOverride;
        float nextAutoAt;
        float autoSoloReadyAt;

        /// Unattended clients (probes, -autoonline builds) join and then wait for
        /// company before readying, so two bots always meet in one room instead
        /// of each instantly starting a solo match.
        public void EnableAutoOnline() { autoOnline = true; }

        void Awake()
        {
            if (I != null && I != this) { Destroy(gameObject); return; }
            I = this;
            PhotonNetwork.AutomaticallySyncScene = false; // single-scene game

            // unattended player builds: -autoonline joins+readies by itself,
            // -nick=Name overrides the profile name (two clients on one machine
            // share PlayerPrefs and would otherwise collide)
            foreach (var arg in System.Environment.GetCommandLineArgs())
            {
                if (arg == "-autoonline") autoOnline = true;
                else if (arg.StartsWith("-nick=")) nickOverride = arg.Substring(6);
            }
        }

        float soloStartAt = -1f;

        void Update()
        {
            if (autoOnline && Time.unscaledTime >= nextAutoAt)
            {
                nextAutoAt = Time.unscaledTime + 1f;
                if (Phase == NetPhase.Idle) QuickMatch();
                else if (Phase == NetPhase.InRoom)
                {
                    if (autoSoloReadyAt <= 0f) autoSoloReadyAt = Time.unscaledTime + 60f;
                    bool hasCompany = PhotonNetwork.CurrentRoom != null &&
                                      PhotonNetwork.CurrentRoom.PlayerCount >= 2;
                    if (hasCompany || PhotonNetwork.OfflineMode ||
                        Time.unscaledTime >= autoSoloReadyAt)
                        SetReady(true);
                }
            }

            // start evaluation is time-driven, not just callback-driven, so a
            // lone ready player waits a grace window for challengers to matchmake
            // in before fighting AI alone (offline mode starts immediately)
            if (Phase == NetPhase.InRoom && PhotonNetwork.IsMasterClient)
            {
                if (!AllReady()) { soloStartAt = -1f; return; }
                bool full = PhotonNetwork.CurrentRoom.PlayerCount >= 2;
                if (full || PhotonNetwork.OfflineMode) { StartMatchNow(); return; }
                if (soloStartAt < 0f)
                {
                    soloStartAt = Time.unscaledTime + SoloGraceSeconds;
                    SetPhase(NetPhase.InRoom, "Searching for challengers...");
                }
                else if (Time.unscaledTime >= soloStartAt) StartMatchNow();
            }
        }

        const float SoloGraceSeconds = 10f;

        bool AllReady()
        {
            var roster = Roster();
            if (roster.Count == 0) return false;
            foreach (var e in roster)
                if (!e.ready) return false;
            return true;
        }

        void StartMatchNow()
        {
            SetPhase(NetPhase.Starting, "Starting...");
            // a running match must leave matchmaking or latecomers join mid-fight
            if (PhotonNetwork.CurrentRoom != null)
            {
                PhotonNetwork.CurrentRoom.IsOpen = false;
                PhotonNetwork.CurrentRoom.IsVisible = false;
            }
            NetMatchLink.I?.MasterStartMatch(Roster());
        }

        void SetPhase(NetPhase p, string status)
        {
            Phase = p;
            StatusLine = status;
            PhaseChanged?.Invoke();
        }

        // ------------------------------------------------------------------ public API

        public bool HasAppId
        {
            get
            {
                var appId = PhotonNetwork.PhotonServerSettings != null
                    ? PhotonNetwork.PhotonServerSettings.AppSettings.AppIdRealtime : null;
                return !string.IsNullOrEmpty(appId);
            }
        }

        /// Entry from the hub. offlineSmoke forces PUN offline mode (loopback,
        /// no AppId needed) — used for practice-vs-AI through the online path
        /// and for automated verification.
        public void QuickMatch(bool offlineSmoke = false)
        {
            if (Phase != NetPhase.Idle && Phase != NetPhase.Failed) return;

            if (!PhotonNetwork.InRoom) ClearReadyProp(); // belt & braces vs stale rdy
            IsOnlineMatch = true;
            PhotonNetwork.NickName = string.IsNullOrEmpty(nickOverride)
                ? CrownfallMeta.PlayerName : nickOverride;

            if (offlineSmoke || !HasAppId)
            {
                if (!offlineSmoke)
                {
                    SetPhase(NetPhase.Failed, "No Photon AppId configured");
                    IsOnlineMatch = false;
                    return;
                }
                // NB: the OfflineMode setter fires OnConnectedToMaster SYNCHRONOUSLY,
                // so the phase must be set before the assignment or the callback
                // sees Idle and never joins
                SetPhase(NetPhase.Connecting, "Starting offline room...");
                PhotonNetwork.OfflineMode = true;
                return;
            }

            PhotonNetwork.OfflineMode = false;
            SetPhase(NetPhase.Connecting, "Connecting...");
            if (!PhotonNetwork.IsConnected) PhotonNetwork.ConnectUsingSettings();
            else JoinRandom();
        }

        public void CancelToMenu()
        {
            IsOnlineMatch = false;
            if (PhotonNetwork.InRoom) PhotonNetwork.LeaveRoom();
            else if (PhotonNetwork.IsConnected && !PhotonNetwork.OfflineMode) PhotonNetwork.Disconnect();
            PhotonNetwork.OfflineMode = false;
            ClearReadyProp();
            SetPhase(NetPhase.Idle, "");
        }

        /// Photon persists local custom properties for the whole client session
        /// and re-publishes them inside the next room-join op — a stale
        /// rdy=true would auto-ready us in every room after the first.
        static void ClearReadyProp()
        {
            PhotonNetwork.RemovePlayerCustomProperties(new[] { PropReady });
        }

        public void SetReady(bool ready)
        {
            if (!PhotonNetwork.InRoom) return;
            PhotonNetwork.LocalPlayer.SetCustomProperties(new Hashtable { { PropReady, ready } });
            // offline mode doesn't reliably echo property callbacks; the Update
            // start-evaluator picks the change up on the next frame
            if (PhotonNetwork.OfflineMode) RosterChanged?.Invoke();
        }

        public void SetClass(int classIdx)
        {
            if (!PhotonNetwork.InRoom) return;
            PhotonNetwork.LocalPlayer.SetCustomProperties(new Hashtable { { PropClass, classIdx } });
        }

        public List<RosterEntry> Roster()
        {
            var list = new List<RosterEntry>();
            if (!PhotonNetwork.InRoom) return list;
            foreach (var p in PhotonNetwork.PlayerList)
            {
                list.Add(new RosterEntry
                {
                    name = string.IsNullOrEmpty(p.NickName) ? $"Player {p.ActorNumber}" : p.NickName,
                    classIdx = p.CustomProperties.TryGetValue(PropClass, out var c) ? (int)c : 0,
                    ready = p.CustomProperties.TryGetValue(PropReady, out var r) && (bool)r,
                    isMe = p.IsLocal,
                    actorNumber = p.ActorNumber,
                });
            }
            list.Sort((a, b) => a.actorNumber.CompareTo(b.actorNumber));
            return list;
        }

        /// Called by NetMatchLink once the start RPC lands on this client.
        public void MarkInMatch() => SetPhase(NetPhase.InMatch, "");

        /// Match over / back to hub: drop the room but keep the connection.
        public void LeaveMatch()
        {
            IsOnlineMatch = false;
            if (PhotonNetwork.InRoom) PhotonNetwork.LeaveRoom();
            if (PhotonNetwork.OfflineMode) PhotonNetwork.OfflineMode = false;
            ClearReadyProp();
            SetPhase(NetPhase.Idle, "");
        }

        // ------------------------------------------------------------------ callbacks

        public override void OnConnectedToMaster()
        {
            if (Phase == NetPhase.Connecting || Phase == NetPhase.Matchmaking) JoinRandom();
        }

        void JoinRandom()
        {
            SetPhase(NetPhase.Matchmaking, "Finding a match...");
            if (PhotonNetwork.OfflineMode)
            {
                // JoinRandomOrCreateRoom is not emulated offline — create directly
                PhotonNetwork.CreateRoom("offline");
                return;
            }
            PhotonNetwork.JoinRandomOrCreateRoom(
                roomOptions: new RoomOptions { MaxPlayers = MaxPlayers },
                expectedMaxPlayers: MaxPlayers);
        }

        public override void OnJoinedRoom()
        {
            SetClass(CrownfallMeta.SelectedClass);
            SetPhase(NetPhase.InRoom, "In room");
            RosterChanged?.Invoke();
        }

        public override void OnPlayerEnteredRoom(Player newPlayer) => RosterChanged?.Invoke();
        public override void OnPlayerLeftRoom(Player otherPlayer)
        {
            RosterChanged?.Invoke();
            // mid-match departures: their fighter is destroyed by PUN; the master
            // re-enables the AI understudy for that slot via NetMatchLink.
            if (Phase == NetPhase.InMatch && PhotonNetwork.IsMasterClient)
                NetMatchLink.I?.MasterHandleLeaver(otherPlayer.ActorNumber);
        }

        public override void OnPlayerPropertiesUpdate(Player targetPlayer, Hashtable changedProps)
        {
            RosterChanged?.Invoke(); // start evaluation happens in Update
        }

        public override void OnDisconnected(DisconnectCause cause)
        {
            bool wanted = cause == DisconnectCause.DisconnectByClientLogic;
            IsOnlineMatch = false;
            SetPhase(wanted ? NetPhase.Idle : NetPhase.Failed,
                wanted ? "" : $"Disconnected: {cause}");
            RosterChanged?.Invoke();
        }

        public override void OnJoinRandomFailed(short returnCode, string message)
        {
            SetPhase(NetPhase.Failed, $"Matchmaking failed: {message}");
        }

        public override void OnMasterClientSwitched(Player newMasterClient)
        {
            // AI understudies are driven by whoever is master — rebind on handover
            if (Phase == NetPhase.InMatch) MatchManager.I?.NetRefreshAiOwnership();
        }
    }
}
