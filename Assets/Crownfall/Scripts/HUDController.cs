using System.Collections;
using UnityEngine;
using Crownfall.UI;

namespace Crownfall
{
    /// The arena scene's fight UI, built on the shared UiKit theme/widget layer.
    /// Since the 2026-07 menu split, every menu-layer screen (boot, login, hub,
    /// champion select, shop, inbox, gifts, settings) lives in the standalone
    /// menu scene (MenuHud); this class owns only what a running match needs.
    /// Screens live in the Hud*.cs partials:
    ///   HudFight.cs     — fight HUD (bars, feed, announce, target frame)
    ///   HudMeta.cs      — pause modal + pause button
    ///   HudOnline.cs    — matchmaking overlay + fight ping
    ///   HudResult.cs    — end-of-match ceremony
    public partial class HUDController : UiKit
    {
        UiPanel fightScreen;
        UiPanel pauseModal, onlineModal, resultModal;

        void Start()
        {
            BuildCanvas();
            BuildFightHud();
            BuildVersus();
            BuildPause();
            BuildResult();
            BuildOnlinePanel();
            BuildToast();

            var mm = MatchManager.I;
            if (mm != null)
            {
                mm.StateChanged += OnStateChanged;
                mm.PausedChanged += p => { if (p) router.OpenModal(pauseModal); else router.CloseModal(pauseModal); };
                mm.ScoreChanged += OnScoreChanged;
                mm.CountdownTick += n => Pop(n > 0 ? n.ToString() : "FIGHT!", n > 0 ? Color.white : Gold, n > 0 ? 0.9f : 1.2f);
                mm.Announce += msg => Pop(msg, Gold, 1.6f);
                mm.KillFeed += OnKill;
                mm.MatchEndedEvent += OnEnded;
                OnStateChanged(mm.State);
                StartCoroutine(ConsumeLaunch(mm));
            }
        }

        /// Start the match the menu scene asked for. Runs a frame late so every
        /// scene component has finished its own Start first.
        IEnumerator ConsumeLaunch(MatchManager mm)
        {
            yield return null;
            switch (CrownfallLaunch.Consume())
            {
                case LaunchKind.Offline:
                    ShowVersus(() => mm.StartMatch());
                    break;
                case LaunchKind.Demo:
                    ShowVersus(() => mm.StartDemo());
                    break;
                case LaunchKind.Online:
                    OpenOnlinePanel();
                    break;
                // None: opened directly (editor / automation probe) — stay in the
                // Menu showcase state and let AutoStart or the harness drive.
            }
        }

        // ================================================================ routing

        void OnStateChanged(MatchState s)
        {
            router.CloseAllModals();
            switch (s)
            {
                case MatchState.Countdown:
                case MatchState.Fighting:
                case MatchState.Ended:
                    if (router.Current != fightScreen) router.Show(fightScreen);
                    if (s != MatchState.Ended) BindPlayer();
                    break;
                // Menu: arena idles on the champion showcase (no screens) until a
                // launch request or AutoStart arrives. ClassSelect no longer
                // exists as an arena screen — champion picks happen in the menu.
            }
            pauseBtn.SetActive(s == MatchState.Fighting);
        }

        void OnScoreChanged(int a, int c)
        {
            scoreAzureText.text = a.ToString();
            scoreCrimsonText.text = c.ToString();
            UiTween.Punch(scoreAzureText.rectTransform, 0.25f, 0.3f);
            UiTween.Punch(scoreCrimsonText.rectTransform, 0.25f, 0.3f);
        }

        // ================================================================ frame

        void Update()
        {
            var mm = MatchManager.I;
            if (mm == null) return;

            // Escape: single owner (MatchManager no longer handles it). Closing
            // the online modal exits to the menu via its OnHide; ESC while
            // fighting toggles pause whether or not the pause modal is up; the
            // end-of-match result modal is terminal and cannot be dismissed.
            var kb = UnityEngine.InputSystem.Keyboard.current;
            if (kb != null && kb.escapeKey.wasPressedThisFrame)
            {
                if (router.IsOpen(onlineModal)) router.CloseModal(onlineModal);
                else if (mm.State == MatchState.Fighting &&
                         (router.IsOpen(pauseModal) || !router.IsModalOpen)) mm.TogglePause();
                else if (mm.State != MatchState.Ended) router.Back();
            }

            UpdateOnlineHud();
            TickFight(mm);
        }
    }
}
