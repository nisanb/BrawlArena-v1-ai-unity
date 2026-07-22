using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Crownfall
{
    /// Online UI: the PLAY mode chooser (online vs practice), the matchmaking /
    /// room panel with roster + ready-up, and the in-match ping readout.
    public partial class HUDController
    {
        GameObject playMenuPanel;
        GameObject onlinePanel;
        TMP_Text onlineStatus, onlinePing;
        TMP_Text fightPing;
        TMP_InputField nameInput;
        Button readyBtn;
        TMP_Text readyLabel;
        bool localReady;

        class RosterRow
        {
            public GameObject root;
            public TMP_Text name, cls;
            public Image readyDot;
        }
        readonly List<RosterRow> rosterRows = new List<RosterRow>();

        void BuildPlayMenu()
        {
            var frame = PopupShell("Battle", ribbonYellow, new Vector2(660, 470), out playMenuPanel);

            Txt("Sub", frame.transform, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0.5f, 1f),
                new Vector2(0, -96), new Vector2(520, 34), "Sundered Crown  ·  3 v 3  ·  first to 10",
                fontSmall, 20, new Color(1f, 0.9f, 0.6f));

            MenuButton(frame.transform, new Vector2(0, 22), new Vector2(430, 108), "ONLINE MATCH", 38,
                btnYellow, icoPlay, () =>
                {
                    playMenuPanel.SetActive(false);
                    OpenOnlinePanel();
                });

            MenuButton(frame.transform, new Vector2(0, -110), new Vector2(430, 92), "VS AI", 32,
                btnBlue, icoSword, () =>
                {
                    playMenuPanel.SetActive(false);
                    MatchManager.I?.StartMatch();
                });

            playMenuPanel.SetActive(false);
        }

        void OpenPlayMenu()
        {
            playMenuPanel.SetActive(true);
        }

        void BuildOnlinePanel()
        {
            var frame = PopupShell("Online", ribbonBlue, new Vector2(880, 620), out onlinePanel);

            // -- nickname
            Txt("NameLbl", frame.transform, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(0f, 1f),
                new Vector2(70, -104), new Vector2(220, 30), "BATTLE NAME", fontSmall, 18,
                new Color(1f, 1f, 1f, 0.65f), TextAlignmentOptions.Left);
            var nameBg = Img("NameBg", frame.transform, new Vector2(0f, 1f), new Vector2(0f, 1f),
                new Vector2(0f, 1f), new Vector2(66, -134), new Vector2(320, 58), plateRound,
                new Color(0.05f, 0.06f, 0.11f, 0.95f));
            nameInput = BuildInput(nameBg.rectTransform, CrownfallMeta.PlayerName);
            nameInput.onEndEdit.AddListener(v => CrownfallMeta.PlayerName = v);

            // -- status + ping
            onlineStatus = Txt("Status", frame.transform, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f),
                new Vector2(0.5f, 1f), new Vector2(120, -120), new Vector2(400, 60), "",
                fontSmall, 22, Gold);
            onlinePing = Txt("Ping", frame.transform, new Vector2(1f, 1f), new Vector2(1f, 1f),
                new Vector2(1f, 1f), new Vector2(-72, -110), new Vector2(160, 30), "",
                fontSmall, 17, new Color(1f, 1f, 1f, 0.55f), TextAlignmentOptions.Right);

            // -- roster (six fixed rows, toggled by occupancy)
            for (int i = 0; i < 6; i++)
            {
                var row = new RosterRow();
                var bg = Img("Row" + i, frame.transform, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f),
                    new Vector2(0.5f, 1f), new Vector2(0, -214 - i * 52), new Vector2(660, 46),
                    plateRound, new Color(i % 2 == 0 ? 0.10f : 0.07f, 0.10f, 0.18f, 0.85f));
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
            readyBtn = MenuButton(frame.transform, new Vector2(-160, -246), new Vector2(300, 92),
                "READY", 32, btnGreen, icoCheck, ToggleReady);
            readyLabel = readyBtn.GetComponentInChildren<TMP_Text>();
            MenuButton(frame.transform, new Vector2(180, -246), new Vector2(300, 92), "LEAVE", 32,
                btnRed, icoClose, () =>
                {
                    CrownfallNet.I?.CancelToMenu();
                    onlinePanel.SetActive(false);
                });

            onlinePanel.SetActive(false);

            if (CrownfallNet.I != null)
            {
                CrownfallNet.I.PhaseChanged += RefreshOnlinePanel;
                CrownfallNet.I.RosterChanged += RefreshOnlinePanel;
            }
        }

        TMP_InputField BuildInput(RectTransform bg, string initial)
        {
            var input = bg.gameObject.AddComponent<TMP_InputField>();
            var area = Rect("TextArea", bg, Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f),
                Vector2.zero, new Vector2(-28, -12));
            area.gameObject.AddComponent<RectMask2D>();
            var text = Txt("Text", area, Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f),
                Vector2.zero, Vector2.zero, "", fontSmall, 22, Color.white, TextAlignmentOptions.Left);
            input.textViewport = area;
            input.textComponent = (TextMeshProUGUI)text;
            input.characterLimit = 16;
            input.text = initial;
            return input;
        }

        void OpenOnlinePanel()
        {
            localReady = false;
            onlinePanel.SetActive(true);
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
            if (onlinePanel == null || CrownfallNet.I == null) return;
            var net = CrownfallNet.I;

            if (net.Phase == NetPhase.InMatch)
            {
                onlinePanel.SetActive(false);
                return;
            }
            if (!onlinePanel.activeSelf) return;

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
            if (fightPing == null && online && fightHudRoot != null)
            {
                fightPing = Txt("Ping", fightHudRoot.transform, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f),
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
