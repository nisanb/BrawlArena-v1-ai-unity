using UnityEngine;
using Crownfall.UI;

namespace Crownfall
{
    /// The standalone menu scene's UI host (CrownfallMenu.unity). Composition
    /// follows the GUI Pro pack's own Lobby assembly (see Preview/Lobby.png):
    /// painted podium backdrop, real 3D champion, designed composites for every
    /// widget. Screens live in the Menu*.cs partials:
    ///   MenuHome.cs      — backdrop canvas, boot splash, login, home hub
    ///   MenuChampions.cs — champion roster with portrait cards
    ///   MenuMeta.cs      — shop / inbox / gift / settings / battle modals
    public partial class MenuHud : UiKit
    {
        [Header("Wired by forge — scene")]
        public MenuShowcase showcase;
        public Camera menuCamera;

        UiPanel bootScreen, loginScreen, hubScreen, champScreen;
        UiPanel shopModal, inboxModal, giftModal, settingsModal, battleModal, questModal, trophyRoadModal;

        static bool bootPlayed; // splash only once per app run, not per return

        void Start()
        {
            // the menu is the boot scene now — saved volume/sensitivity/shake
            // must be live before any UI sound or settings modal is built
            CrownfallSettings.Load();
            BuildBackdropCanvas();
            BuildCanvas("Menu Canvas");
            BuildBoot();
            BuildLogin();
            BuildHomeHub();
            BuildChampions();
            BuildShop();
            BuildInbox();
            BuildGift();
            BuildSettings();
            BuildBattleModal();
            BuildQuests();
            BuildTrophyRoad();
            BuildToast();

            CrownfallMeta.Changed += RefreshHub;
            CrownfallQuests.Changed += RefreshHub;
            RefreshHub();
            ShowMenuLayer();
        }

        void OnDestroy()
        {
            // static events — must detach or scene reloads leak dead handlers
            CrownfallMeta.Changed -= RefreshHub;
            CrownfallQuests.Changed -= RefreshHub;
        }

        /// Menu front door: splash on first sight, then login until a profile
        /// exists, then the hub.
        internal void ShowMenuLayer()
        {
            if (!bootPlayed) { bootPlayed = true; router.Show(bootScreen); }
            else if (!CrownfallMeta.HasProfile) router.Show(loginScreen);
            else router.Show(hubScreen);
        }

        void OpenShop() { router.OpenModal(shopModal); RefreshHub(); }
        void OpenInbox() { router.OpenModal(inboxModal); RefreshHub(); }
        void OpenQuests() { router.OpenModal(questModal); RefreshHub(); }
        void OpenTrophyRoad() { router.OpenModal(trophyRoadModal); RefreshHub(); }
        void OpenPlayMenu() => router.OpenModal(battleModal);
        public void OpenSettings() => router.OpenModal(settingsModal);

        void OpenChampions()
        {
            router.Show(champScreen);
        }

        // one visual identity per game mode, shared by the hub event card and
        // the battle popup so the outside always mirrors the inside
        internal Sprite ModeFace(int idx) => idx switch
        {
            0 => card3Blue,
            1 => card3Orange,
            _ => card3Purple,
        };

        internal Sprite ModeIcon(int idx) => idx switch
        {
            0 => icoBattleSword,
            1 => icoEnergy,
            _ => icoTrophyBig,
        };

        internal static string ModeDetail(GameMode m) =>
            $"FIRST TO {m.killTarget}  ·  {(int)m.duration / 60}:{(int)m.duration % 60:00}";

        // ---------------------------------------------------------------- play

        void LaunchOffline() => CrownfallLaunch.ToArena(LaunchKind.Offline);
        void LaunchDemo() => CrownfallLaunch.ToArena(LaunchKind.Demo);
        void LaunchOnline() => CrownfallLaunch.ToArena(LaunchKind.Online);

        // ---------------------------------------------------------------- frame

        void Update()
        {
            var kb = UnityEngine.InputSystem.Keyboard.current;
            if (kb != null && kb.escapeKey.wasPressedThisFrame) router.Back();
            if (router.IsOpen(giftModal)) RefreshGift();
        }
    }
}
