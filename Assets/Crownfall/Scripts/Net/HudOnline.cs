using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using Crownfall.UI;

namespace Crownfall
{
    /// Online UI: matchmaking / room modal with roster + ready-up, and the
    /// in-match ping readout.
    public partial class HUDController
    {
        TMP_Text onlineStatus, onlinePing;
        TMP_Text fightPing;
        TMP_InputField nameInput;
        Button readyBtn;
        TMP_Text readyLabel;
        RectTransform readyRect;
        bool localReady;

        class RosterRow
        {
            public GameObject root;
            public TMP_Text name, cls;
            public Image readyDot;
        }
        readonly List<RosterRow> rosterRows = new List<RosterRow>();

        void BuildOnlinePanel()
        {
            var frame = ModalShell("Online", new Vector2(900, 660), out onlineModal);
            onlineModal.OnHide += () =>
            {
                if (readyRect != null) UiTween.StopLoop(readyRect);
                // the arena has no menu screens anymore — ANY exit from
                // matchmaking (X, Escape, LEAVE) must return to the menu scene,
                // except when the modal closes because the match is starting
                var net = CrownfallNet.I;
                if (net != null && net.Phase != NetPhase.InMatch && net.Phase != NetPhase.Starting)
                {
                    net.CancelToMenu();
                    CrownfallLaunch.ToMenu();
                }
            };

            // -- nickname
            Txt("NameLbl", frame, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(0f, 1f),
                new Vector2(70, -104), new Vector2(220, 30), "BATTLE NAME", fontSmall, 18,
                new Color(1f, 1f, 1f, 0.65f), TextAlignmentOptions.Left);
            var nameBg = Img("NameBg", frame, new Vector2(0f, 1f), new Vector2(0f, 1f),
                new Vector2(0f, 1f), new Vector2(66, -134), new Vector2(340, 62), inputBg, Color.white);
            nameInput = BuildInput(nameBg.rectTransform, CrownfallMeta.PlayerName, icoAccount);
            nameInput.onEndEdit.AddListener(v => CrownfallMeta.PlayerName = v);

            // -- status + ping
            onlineStatus = Txt("Status", frame, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f),
                new Vector2(0.5f, 1f), new Vector2(120, -120), new Vector2(400, 60), "",
                fontSmall, 22, Gold);
            onlinePing = Txt("Ping", frame, new Vector2(1f, 1f), new Vector2(1f, 1f),
                new Vector2(1f, 1f), new Vector2(-72, -110), new Vector2(160, 30), "",
                fontSmall, 17, new Color(1f, 1f, 1f, 0.55f), TextAlignmentOptions.Right);

            // -- roster (six fixed rows, toggled by occupancy)
            for (int i = 0; i < 6; i++)
            {
                var row = new RosterRow();
                var bg = Img("Row" + i, frame, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f),
                    new Vector2(0.5f, 1f), new Vector2(0, -224 - i * 52), new Vector2(680, 46),
                    rowNavy, Color.white);
                row.root = bg.gameObject;
                row.name = Txt("N", bg.transform, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f),
                    new Vector2(0f, 0.5f), new Vector2(20, 0), new Vector2(300, 36), "",
                    fontSmall, 21, Color.white, TextAlignmentOptions.Left);
                row.cls = Txt("C", bg.transform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                    new Vector2(0.5f, 0.5f), new Vector2(110, 0), new Vector2(220, 36), "",
                    fontSmall, 19, new Color(0.75f, 0.85f, 1f));
                row.readyDot = Icon("R", bg.transform, new Vector2(1f, 0.5f), new Vector2(1f, 0.5f),
                    new Vector2(0.5f, 0.5f), new Vector2(-36, 0), new Vector2(30, 30), icoCheck,
                    new Color(0.4f, 1f, 0.45f));
                row.root.SetActive(false);
                rosterRows.Add(row);
            }

            // -- ready / cancel
            readyBtn = MenuButton(frame, new Vector2(-170, -256), new Vector2(300, 92),
                "READY", 32, btnGreen, icoCheck, ToggleReady);
            readyLabel = readyBtn.GetComponentInChildren<TMP_Text>();
            readyRect = readyBtn.GetComponent<RectTransform>();
            MenuButton(frame, new Vector2(180, -256), new Vector2(300, 92), "LEAVE", 32,
                btnRed, icoClose, () => router.CloseModal(onlineModal)); // OnHide exits to menu

            if (CrownfallNet.I != null)
            {
                CrownfallNet.I.PhaseChanged += RefreshOnlinePanel;
                CrownfallNet.I.RosterChanged += RefreshOnlinePanel;
            }
        }

        void OpenOnlinePanel()
        {
            localReady = false;
            router.OpenModal(onlineModal);
            CrownfallNet.I?.QuickMatch();
            RefreshOnlinePanel();
        }

        void ToggleReady()
        {
            localReady = !localReady;
            CrownfallNet.I?.SetReady(localReady);
            RefreshOnlinePanel();
        }

        void RefreshOnlinePanel()
        {
            if (onlineModal == null || CrownfallNet.I == null) return;
            var net = CrownfallNet.I;

            if (net.Phase == NetPhase.InMatch)
            {
                router.CloseModal(onlineModal);
                return;
            }
            if (!onlineModal.Active) return;

            onlineStatus.text = net.Phase switch
            {
                NetPhase.Connecting => "Connecting...",
                NetPhase.Matchmaking => net.StatusLine,
                NetPhase.InRoom => "Waiting for champions...",
                NetPhase.Starting => "Starting!",
                NetPhase.Failed => net.StatusLine,
                _ => "",
            };
            onlineStatus.color = net.Phase == NetPhase.Failed ? new Color(1f, 0.45f, 0.4f) : Gold;
            onlinePing.text = net.Phase == NetPhase.InRoom || net.Phase == NetPhase.Starting
                ? $"ping {net.PingMs}ms" : "";
            readyLabel.text = localReady ? "UNREADY" : "READY";
            readyBtn.interactable = net.Phase == NetPhase.InRoom;
            if (net.Phase == NetPhase.InRoom && !localReady) UiTween.PulseForever(readyRect, 0.99f, 1.04f, 1.1f);
            else UiTween.StopLoop(readyRect);

            var roster = net.Roster();
            for (int i = 0; i < rosterRows.Count; i++)
            {
                bool has = i < roster.Count;
                rosterRows[i].root.SetActive(has);
                if (!has) continue;
                var e = roster[i];
                rosterRows[i].name.text = (e.isMe ? "» " : "") + e.name;
                rosterRows[i].cls.text = ClassKits.Get((ClassId)Mathf.Clamp(e.classIdx, 0, 3)).displayName;
                rosterRows[i].readyDot.enabled = e.ready;
            }
        }

        /// Small live ping readout during a networked fight (built lazily under the timer).
        void UpdateOnlineHud()
        {
            var net = CrownfallNet.I;
            bool online = net != null && net.IsOnlineMatch && net.Phase == NetPhase.InMatch;
            if (fightPing == null && online && fightScreen != null)
            {
                fightPing = Txt("Ping", fightScreen.Go.transform, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f),
                    new Vector2(0.5f, 1f), new Vector2(0, -152), new Vector2(200, 26), "",
                    fontSmall, 15, new Color(1f, 1f, 1f, 0.5f));
            }
            if (fightPing != null)
            {
                bool show = online && !Photon.Pun.PhotonNetwork.OfflineMode;
                fightPing.gameObject.SetActive(show);
                if (show) fightPing.text = $"ping {net.PingMs}ms";
            }
        }
    }
}
