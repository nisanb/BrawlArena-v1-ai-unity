using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using Crownfall.UI;

namespace Crownfall
{
    /// Menu-layer screens: the painted-stage backdrop, the boot splash, the
    /// login (profile forge) screen and the home hub. The hub mirrors the GUI
    /// Pro pack's own Lobby assembly: profile plate top-left, currency pills
    /// top-right, side rails, event card and the big yellow BATTLE button, with
    /// the real 3D champion standing on the painted podium between them.
    public partial class MenuHud
    {
        TMP_Text hubCoins, hubGems, hubTrophies, hubLevelNum, hubChampName, hubPlayerName, hubXpText;
        TMP_Text hubModeName, hubModeSub;
        Image hubModeCard, hubModeIcon;
        Image hubXpFill, hubChampIcon, hubSigilIcon;
        GameObject inboxBadge, giftBadge, questBadge;
        TMP_Text inboxBadgeText, questBadgeText;
        int coinsShown = -1, gemsShown = -1;

        // entrance choreography targets
        RectTransform hubProfileRect, hubTrophyRect, hubPillsRect, hubPlayRect,
            hubEventRect, hubChampPlateRect, hubLeftRail1, hubLeftRail2,
            hubRightRail0, hubRightRail1, hubRightRail2;

        // the pack's ResourceBar/UserInfo plates are WHITE BASES (no color
        // suffix) that its own demo tints navy — matched to ListFrame02 navy
        static readonly Color PillNavy = new Color(0.13f, 0.17f, 0.30f, 0.97f);

        TMP_InputField loginInput;
        readonly List<(int idx, Image card, Image glow)> loginEmblems = new List<(int, Image, Image)>();
        RectTransform loginCardRect;
        Image bootBarFill;

        // ================================================================ backdrop

        /// The painted podium stage (Background_02 — the pack Lobby's own set),
        /// rendered on a camera-space canvas BEHIND the 3D champion, with a
        /// slow ray burst and a warm glow lifting the podium.
        void BuildBackdropCanvas()
        {
            var go = new GameObject("Backdrop Canvas", typeof(Canvas), typeof(CanvasScaler));
            go.transform.SetParent(transform, false);
            var bg = go.GetComponent<Canvas>();
            bg.renderMode = RenderMode.ScreenSpaceCamera;
            bg.worldCamera = menuCamera != null ? menuCamera : Camera.main;
            bg.planeDistance = 90f;
            var scaler = go.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920, 1080);
            scaler.matchWidthOrHeight = 0.5f;
            var bgRoot = go.GetComponent<RectTransform>();

            var stage = Img("Stage", bgRoot, Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f),
                Vector2.zero, Vector2.zero, bgStage, Color.white);
            stage.type = Image.Type.Simple;

            var rays = Icon("Rays", bgRoot, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                new Vector2(0.5f, 0.5f), new Vector2(-20, 60), new Vector2(950, 950), fxRays,
                new Color(1f, 0.9f, 0.55f, 0.14f));
            UiTween.SpinForever(rays.rectTransform, 34f);
            Glow("PodiumGlow", bgRoot, new Vector2(-20, -150), 760, new Color(1f, 0.85f, 0.5f, 0.3f));
        }

        /// Full-screen painted backdrop (purple arena sky) with glow layers and a
        /// slow-spinning ray burst — shared look for boot + login.
        RectTransform Backdrop(Transform parent, out RectTransform rays)
        {
            var bg = Img("Bg", parent, Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f),
                Vector2.zero, Vector2.zero, bgLayer1, Color.white, true);
            bg.type = Image.Type.Simple;
            var glowTop = Img("GlowTop", parent, new Vector2(0f, 1f), Vector2.one, new Vector2(0.5f, 1f),
                Vector2.zero, new Vector2(0, 420), bgGlowTop, new Color(1f, 1f, 1f, 0.9f));
            glowTop.type = Image.Type.Simple;
            var glowBot = Img("GlowBot", parent, Vector2.zero, new Vector2(1f, 0f), new Vector2(0.5f, 0f),
                Vector2.zero, new Vector2(0, 420), bgGlowBottom, new Color(1f, 1f, 1f, 0.9f));
            glowBot.type = Image.Type.Simple;

            var raysImg = Icon("Rays", parent, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                new Vector2(0.5f, 0.5f), new Vector2(0, 190), new Vector2(1150, 1150), fxRays,
                new Color(1f, 0.85f, 0.45f, 0.16f));
            rays = raysImg.rectTransform;
            return bg.rectTransform;
        }

        // ================================================================ boot

        void BuildBoot()
        {
            bootScreen = MakePanel("Boot");
            var t = bootScreen.Go.transform;
            Backdrop(t, out var rays);

            Glow("CrownGlow", t, new Vector2(0, 210), 560, new Color(1f, 0.8f, 0.3f, 0.5f));
            var crown = Icon("Crown", t, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                new Vector2(0, 215), new Vector2(230, 230), iconCrown, Color.white);
            var word = Txt("Wordmark", t, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                new Vector2(0, 22), new Vector2(1500, 150), "CROWNFALL", fontBig, 128, Gold);
            word.characterSpacing = 8f;
            var sub = Txt("Sub", t, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                new Vector2(0, -78), new Vector2(900, 60), "A  R  E  N  A", fontMid, 44,
                new Color(0.92f, 0.94f, 1f));

            // loading bar: designed dark tube, white-by-design fill tinted gold
            var barBg = Img("LoadBg", t, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0.5f, 0f),
                new Vector2(0, 150), new Vector2(640, 30), barBgBasic, Color.white);
            bootBarFill = MakeFill(barBg.rectTransform, barFillBasic, Gold, new Vector2(640, 30));
            bootBarFill.fillAmount = 0f;
            var loadTxt = Txt("LoadTxt", t, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0.5f, 0f),
                new Vector2(0, 190), new Vector2(700, 32), "sharpening blades...", fontSmall, 20,
                new Color(1f, 1f, 1f, 0.6f));
            Txt("Ver", t, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0.5f, 0f),
                new Vector2(0, 108), new Vector2(600, 26), "build " + Application.version, fontSmall, 15,
                new Color(1f, 1f, 1f, 0.35f));

            bootScreen.OnShow = () =>
            {
                UiTween.SpinForever(rays, 22f);
                UiTween.Scale(crown.rectTransform, Vector3.one * 2.6f, Vector3.one, 0.55f, UiTween.Ease.BounceOut, 0.1f);
                UiTween.PopIn(word.rectTransform, 0.4f, 0.35f);
                UiTween.Fade(sub.gameObject.AddComponent<CanvasGroup>(), 0f, 1f, 0.5f, 0.6f);
                StartCoroutine(BootLoad(loadTxt));
            };
        }

        IEnumerator BootLoad(TMP_Text loadTxt)
        {
            string[] lines = { "sharpening blades...", "waking the champions...", "raising the arena..." };
            float t = 0f;
            const float dur = 2.1f;
            int li = 0;
            while (t < dur)
            {
                t += Time.unscaledDeltaTime;
                bootBarFill.fillAmount = Mathf.SmoothStep(0f, 1f, t / dur);
                int wantLine = Mathf.Min(lines.Length - 1, (int)(t / dur * lines.Length));
                if (wantLine != li) { li = wantLine; loadTxt.text = lines[li]; }
                yield return null;
            }
            PlaySting();
            ShowMenuLayer();
        }

        // ================================================================ login

        void BuildLogin()
        {
            loginScreen = MakePanel("Login");
            var t = loginScreen.Go.transform;
            Backdrop(t, out var rays);

            var card = Img("Card", t, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                new Vector2(0, -6), new Vector2(780, 850), panelNavy, Color.white, true);
            loginCardRect = card.rectTransform;
            loginScreen.Hero = loginCardRect;

            Glow("CrownGlow", card.transform, new Vector2(0, 328), 300, new Color(1f, 0.8f, 0.3f, 0.55f));
            Icon("Crown", card.transform, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0.5f, 0.5f),
                new Vector2(0, -88), new Vector2(150, 150), iconCrown, Color.white);
            var word = Txt("Wordmark", card.transform, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f),
                new Vector2(0.5f, 1f), new Vector2(0, -168), new Vector2(720, 92), "CROWNFALL", fontBig, 76, Gold);
            word.characterSpacing = 6f;

            // "forge your legend" between ribbon dividers
            Icon("DivL", card.transform, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0.5f, 0.5f),
                new Vector2(-238, -286), new Vector2(150, 22), dividerL, new Color(1f, 1f, 1f, 0.7f));
            var legend = Txt("Legend", card.transform, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f),
                new Vector2(0.5f, 0.5f), new Vector2(0, -286), new Vector2(330, 40), "", fontSmall, 24,
                new Color(0.95f, 0.9f, 0.75f));
            Icon("DivR", card.transform, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0.5f, 0.5f),
                new Vector2(238, -286), new Vector2(150, 22), dividerR, new Color(1f, 1f, 1f, 0.7f));

            // callsign entry
            Txt("NameLbl", card.transform, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, 0.5f),
                new Vector2(-255, -352), new Vector2(300, 30), "CALLSIGN", fontSmall, 19,
                new Color(1f, 1f, 1f, 0.65f), TextAlignmentOptions.Left);
            var inputImg = Img("NameBg", card.transform, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f),
                new Vector2(0.5f, 0.5f), new Vector2(-42, -406), new Vector2(430, 76), inputBg, Color.white, true);
            loginInput = BuildInput(inputImg.rectTransform, CrownfallMeta.PlayerName, icoAccount);

            var dice = Icon("Dice", card.transform, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f),
                new Vector2(0.5f, 0.5f), new Vector2(218, -406), new Vector2(70, 70), btnCircle, Color.white);
            MakeClickable(dice, () => loginInput.text = CrownfallMeta.RandomName());
            Icon("D", dice.transform, Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f),
                new Vector2(0, 2), new Vector2(30, 30), icoRefresh, Color.white);

            // starting champion emblems: designed rarity-colored mini cards,
            // gold pack glow behind the current pick (no tint-swapping)
            Txt("ChampLbl", card.transform, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, 0.5f),
                new Vector2(-255, -486), new Vector2(360, 30), "CHOOSE YOUR CHAMPION", fontSmall, 19,
                new Color(1f, 1f, 1f, 0.65f), TextAlignmentOptions.Left);
            loginEmblems.Clear();
            for (int i = 0; i < 5; i++)
            {
                int idx = i;
                var kit = ClassKits.Get((ClassId)i);
                float x = -244 + i * 122;
                var glow = Icon("Glow" + i, card.transform, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f),
                    new Vector2(0.5f, 0.5f), new Vector2(x, -566), new Vector2(142, 142), card3Glow, Gold);
                var emblem = Img("Em" + i, card.transform, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f),
                    new Vector2(0.5f, 0.5f), new Vector2(x, -566), new Vector2(100, 114), CardFor(i),
                    Color.white, true);
                MakeClickable(emblem, () =>
                {
                    CrownfallMeta.SelectedClass = idx;
                    RefreshLoginEmblems();
                    Burst(fxSparklePrefab, emblem.rectTransform, Vector2.zero, 0.6f);
                });
                Icon("I", emblem.transform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                    new Vector2(0, 14), new Vector2(52, 52), IconFor(kit.id), Color.white);
                Txt("N", emblem.transform, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0.5f, 1f),
                    new Vector2(0, 26), new Vector2(130, 26), kit.displayName.ToUpper(), fontSmall, 14,
                    Color.white);
                loginEmblems.Add((idx, emblem, glow));
            }

            // enter button
            var enter = MenuButton(card.transform, Vector2.zero, new Vector2(470, 112), "ENTER THE ARENA", 38,
                btnYellow, icoPlay, () =>
                {
                    CrownfallMeta.CompleteLogin(loginInput.text);
                    PlaySting();
                    Burst(fxConfettiPrefab, root, Vector2.zero, 1f);
                    ShowMenuLayer();
                });
            enter.GetComponent<RectTransform>().SetAnchor(new Vector2(0.5f, 0f), new Vector2(0, 118));
            var enterRect = enter.GetComponent<RectTransform>();

            Txt("Note", card.transform, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0.5f, 0f),
                new Vector2(0, 34), new Vector2(600, 28), "your legend is kept on this device",
                fontSmall, 15, new Color(1f, 1f, 1f, 0.4f));

            loginScreen.OnShow = () =>
            {
                UiTween.SpinForever(rays, 26f);
                loginInput.text = CrownfallMeta.PlayerName;
                RefreshLoginEmblems();
                UiTween.TypeText(legend, "FORGE YOUR LEGEND", 34f, 0.35f);
                UiTween.PulseForever(enterRect, 0.99f, 1.04f, 1.4f);
            };
            loginScreen.OnHide = () => UiTween.StopLoop(enterRect);
        }

        void RefreshLoginEmblems()
        {
            foreach (var (idx, card, glow) in loginEmblems)
            {
                bool sel = idx == CrownfallMeta.SelectedClass;
                glow.gameObject.SetActive(sel);
                card.rectTransform.localScale = Vector3.one * (sel ? 1.12f : 1f);
            }
        }

        // ================================================================ home hub

        void BuildHomeHub()
        {
            hubScreen = MakePanel("HomeHub");
            var t = hubScreen.Go.transform;

            // -- wordmark, top center
            Icon("Crown", t, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0.5f, 1f),
                new Vector2(0, -10), new Vector2(52, 52), iconCrown, Color.white);
            Txt("Wordmark", t, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0.5f, 1f),
                new Vector2(0, -58), new Vector2(700, 48), "CROWNFALL ARENA", fontBig, 34, Gold);

            // -- profile plate, top left: designed UserInfo01 strips + avatar +
            //    level shield + XP bar (composition from the pack's Lobby)
            var profile = Rect("Profile", t, new Vector2(0f, 1f), new Vector2(0f, 1f),
                new Vector2(0f, 1f), new Vector2(24, -16), new Vector2(390, 108));
            hubProfileRect = profile;
            var nameStrip = Img("NameStrip", profile, new Vector2(0f, 1f), new Vector2(0f, 1f),
                new Vector2(0f, 1f), new Vector2(92, -2), new Vector2(290, 58), userInfoTop, PillNavy);
            hubPlayerName = Txt("PName", nameStrip.transform, Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f),
                new Vector2(14, 1), new Vector2(-40, -12), "CHAMPION", fontSmall, 23, Color.white,
                TextAlignmentOptions.Left);
            var xpStrip = Img("XpStrip", profile, new Vector2(0f, 1f), new Vector2(0f, 1f),
                new Vector2(0f, 1f), new Vector2(92, -58), new Vector2(290, 46), userInfoBottom, PillNavy);
            var xpBg = Img("XpBg", xpStrip.transform, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f),
                new Vector2(0f, 0.5f), new Vector2(16, -1), new Vector2(190, 20), lvl3Bg, Color.white);
            hubXpFill = MakeFill(xpBg.rectTransform, lvl3Fill, Color.white, new Vector2(190, 20));
            hubXpText = Txt("XpT", xpStrip.transform, new Vector2(1f, 0.5f), new Vector2(1f, 0.5f),
                new Vector2(1f, 0.5f), new Vector2(-14, 0), new Vector2(70, 24), "", fontSmall, 13,
                new Color(1f, 1f, 1f, 0.75f), TextAlignmentOptions.Right);
            // avatar: designed blue profile frame + white silhouette + border ring
            var avatar = Img("Avatar", profile, new Vector2(0f, 1f), new Vector2(0f, 1f),
                new Vector2(0f, 1f), new Vector2(0, 0), new Vector2(100, 104), profileInner, Color.white);
            avatar.type = Image.Type.Simple;
            var silhouette = Icon("Head", avatar.transform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                new Vector2(0.5f, 0.5f), new Vector2(0, -4), new Vector2(64, 64), userInfoIcon,
                new Color(1f, 1f, 1f, 0.92f));
            silhouette.raycastTarget = false;
            Img("Ring", avatar.transform, Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f),
                Vector2.zero, new Vector2(8, 8), profileRing, Color.white).type = Image.Type.Simple;
            hubSigilIcon = Icon("Sigil", avatar.transform, new Vector2(1f, 0f), new Vector2(1f, 0f),
                new Vector2(0.5f, 0.5f), new Vector2(-10, 14), new Vector2(38, 38), iconCrown, Color.white);
            var lvShield = Img("LvBadge", profile, new Vector2(0f, 1f), new Vector2(0f, 1f),
                new Vector2(0.5f, 0.5f), new Vector2(14, -92), new Vector2(52, 64), lvl3Badge, Color.white);
            lvShield.type = Image.Type.Simple;
            hubLevelNum = Txt("LvNum", lvShield.transform, Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f),
                new Vector2(0, 4), new Vector2(-8, -20), "1", fontMid, 25, Color.white);

            // -- trophy pill docked right of the profile (designed resource bar)
            var road = Img("TrophyRoad", t, new Vector2(0f, 1f), new Vector2(0f, 1f),
                new Vector2(0f, 1f), new Vector2(440, -26), new Vector2(232, 62), resourcePill, PillNavy);
            hubTrophyRect = road.rectTransform;
            MakeClickable(road, OpenTrophyRoad);
            Icon("TIco", road.transform, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(0.5f, 0.5f),
                new Vector2(38, 4), new Vector2(56, 56), icoTrophyBig, Color.white);
            hubTrophies = Txt("TCount", road.transform, new Vector2(0f, 0.5f), new Vector2(1f, 0.5f),
                new Vector2(0.5f, 0.5f), new Vector2(42, 2), new Vector2(-110, 40), "0", fontMid, 29, Gold);

            // -- resources, top right: designed pills with colored + caps
            var pills = Rect("Pills", t, Vector2.one, Vector2.one, Vector2.one, Vector2.zero, Vector2.zero);
            hubPillsRect = pills;
            var gear = Img("SettingsBtn", pills, Vector2.one, Vector2.one, Vector2.one,
                new Vector2(-22, -18), new Vector2(74, 74), btnSquareNavy, Color.white);
            MakeClickable(gear, OpenSettings);
            Icon("G", gear.transform, Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f),
                new Vector2(0, 2), new Vector2(34, 34), icoGear, Color.white);
            hubGems = ResourcePill(pills, new Vector2(-112, -26), iconGemPurple, resourceBtnPurple, "0");
            hubCoins = ResourcePill(pills, new Vector2(-334, -26), iconCoinBar, resourceBtnYellow, "0");

            // -- vertical rails pinned bottom-left (pack Lobby: SHOP purple,
            //    second rail blue)
            VerticalTab(t, new Vector2(26, 24), btnSidePurple, menuShop, "SHOP", OpenShop, out var shopTab);
            hubLeftRail1 = shopTab;
            VerticalTab(t, new Vector2(164, 24), btnSideBlue, menuCards, "CHAMPS", OpenChampions, out var champTab);
            hubLeftRail2 = champTab;

            // -- designed navy square stack on the right edge, above PLAY
            SideButton(t, new Vector2(-24, 564), menuTrophy, "QUESTS", OpenQuests, out var questSide);
            hubRightRail0 = questSide.rectTransform;
            questBadge = Badge(questSide.transform, out questBadgeText);
            SideButton(t, new Vector2(-24, 436), menuInbox, "INBOX", OpenInbox, out var inboxSide);
            hubRightRail1 = inboxSide.rectTransform;
            inboxBadge = Badge(inboxSide.transform, out inboxBadgeText);
            SideButton(t, new Vector2(-24, 308), menuGift, "GIFTS",
                () => router.OpenModal(giftModal), out var giftSide);
            hubRightRail2 = giftSide.rectTransform;
            giftBadge = Badge(giftSide.transform, out var giftBadgeText);
            giftBadgeText.text = "!";

            // -- champion name plate under the podium (tap to change)
            var champPlate = Img("ChampPlate", t, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f),
                new Vector2(0.5f, 0f), new Vector2(-20, 150), new Vector2(300, 62), trapBlue, Color.white);
            hubChampPlateRect = champPlate.rectTransform;
            MakeClickable(champPlate, OpenChampions);
            hubChampIcon = Icon("CIco", champPlate.transform, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f),
                new Vector2(0f, 0.5f), new Vector2(20, 0), new Vector2(34, 34), icoShield, Color.white);
            hubChampName = Txt("CName", champPlate.transform, Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f),
                new Vector2(12, 1), new Vector2(-70, -8), "KNIGHT", fontMid, 28, Color.white);

            // -- event card docked onto the PLAY cluster: the SAME rarity card
            //    face the battle popup shows for the selected mode (mirrored
            //    identity, owner feedback 2026-07-22); tap opens the chooser
            var evt = Img("EventCard", t, new Vector2(1f, 0f), new Vector2(1f, 0f),
                new Vector2(1f, 0f), new Vector2(-460, 34), new Vector2(320, 132),
                ModeFace(CrownfallMeta.SelectedMode), Color.white);
            hubModeCard = evt;
            hubEventRect = evt.rectTransform;
            MakeClickable(evt, OpenPlayMenu);
            hubModeIcon = Icon("EIco", evt.transform, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f),
                new Vector2(0f, 0.5f), new Vector2(22, 0), new Vector2(58, 58),
                ModeIcon(CrownfallMeta.SelectedMode), Color.white);
            hubModeName = Txt("EName", evt.transform, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0.5f, 1f),
                new Vector2(52, -26), new Vector2(-126, 32), "10-KILL BRAWL", fontSmall, 20, Color.white,
                TextAlignmentOptions.Left);
            hubModeSub = Txt("ESub", evt.transform, new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(0.5f, 0f),
                new Vector2(52, 28), new Vector2(-126, 26), "",
                fontSmall, 14, new Color(1f, 0.9f, 0.6f), TextAlignmentOptions.Left);

            // -- BATTLE: the pack's big designed Button02 composite with a
            //    spinning ray halo, swords icon and pulse
            var playRays = Icon("PlayRays", t, new Vector2(1f, 0f), new Vector2(1f, 0f), new Vector2(0.5f, 0.5f),
                new Vector2(-235, 120), new Vector2(560, 560), fxRays, new Color(1f, 0.85f, 0.4f, 0.14f));
            var play = Img("Btn_BATTLE", t, new Vector2(1f, 0f), new Vector2(1f, 0f), new Vector2(1f, 0f),
                new Vector2(-24, 24), new Vector2(422, 196), btnBattleYellow, Color.white, true);
            MakeClickable(play, OpenPlayMenu);
            Icon("Swords", play.transform, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(0.5f, 0.5f),
                new Vector2(92, 14), new Vector2(112, 112), icoBattleSword != null ? icoBattleSword : icoSword,
                Color.white);
            Txt("L", play.transform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                new Vector2(58, 12), new Vector2(240, 90), "BATTLE", fontMid, 56, Color.white);
            hubPlayRect = play.rectTransform;

            // -- demo above the rails + version line
            MenuButton(t, Vector2.zero, new Vector2(240, 58), "WATCH DEMO", 19,
                btnBlue, icoMovie, LaunchDemo)
                .GetComponent<RectTransform>().SetAnchor(new Vector2(0f, 0f), new Vector2(146, 266));
            Txt("Version", t, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0.5f, 0f),
                new Vector2(0, 10), new Vector2(500, 26), "Crownfall Arena  ·  build " + Application.version,
                fontSmall, 15, new Color(1f, 1f, 1f, 0.45f));

            hubScreen.OnShow = () =>
            {
                RefreshHub();
                UiTween.SlideIn(hubProfileRect, new Vector2(-420f, 0f), 0.4f);
                UiTween.SlideIn(hubTrophyRect, new Vector2(-500f, 0f), 0.44f, UiTween.Ease.CubicOut, 0.08f);
                UiTween.SlideIn(hubPillsRect, new Vector2(420f, 0f), 0.4f, UiTween.Ease.CubicOut, 0.05f);
                UiTween.SlideIn(hubLeftRail1, new Vector2(0f, -300f), 0.4f, UiTween.Ease.CubicOut, 0.1f);
                UiTween.SlideIn(hubLeftRail2, new Vector2(0f, -300f), 0.4f, UiTween.Ease.CubicOut, 0.17f);
                UiTween.SlideIn(hubRightRail0, new Vector2(180f, 0f), 0.36f, UiTween.Ease.CubicOut, 0.05f);
                UiTween.SlideIn(hubRightRail1, new Vector2(180f, 0f), 0.36f, UiTween.Ease.CubicOut, 0.12f);
                UiTween.SlideIn(hubRightRail2, new Vector2(180f, 0f), 0.36f, UiTween.Ease.CubicOut, 0.19f);
                UiTween.SlideIn(hubEventRect, new Vector2(0f, -220f), 0.4f, UiTween.Ease.CubicOut, 0.14f);
                UiTween.PopIn(hubChampPlateRect, 0.34f, 0.2f);
                UiTween.PopIn(hubPlayRect, 0.42f, 0.24f);
                UiTween.PulseForever(hubPlayRect, 0.995f, 1.02f, 1.6f);
                UiTween.SpinForever(playRays.rectTransform, 18f);
            };
            hubScreen.OnHide = () => UiTween.StopLoop(hubPlayRect);
        }

        TMP_Text ResourcePill(Transform parent, Vector2 pos, Sprite icon, Sprite capBtn, string initial)
        {
            var pill = Img("Pill", parent, Vector2.one, Vector2.one, Vector2.one,
                pos, new Vector2(206, 54), resourcePill, PillNavy);
            Icon("I", pill.transform, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(0.5f, 0.5f),
                new Vector2(24, 1), new Vector2(46, 46), icon, Color.white);
            var count = Txt("N", pill.transform, Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f),
                new Vector2(6, 1), new Vector2(-92, -8), initial, fontMid, 25, Color.white);
            // the colored cap buttons are complete designs with an inset plus —
            // no extra glyph on top
            var plus = Img("Plus", pill.transform, new Vector2(1f, 0.5f), new Vector2(1f, 0.5f),
                new Vector2(0.5f, 0.5f), new Vector2(-26, 0), new Vector2(44, 48), capBtn, Color.white);
            MakeClickable(plus, OpenShop);
            return count;
        }

        /// Designed Button03 vertical rail button (icon over label), pinned to
        /// the bottom-left corner — the pack Lobby's own side-menu pattern.
        void VerticalTab(Transform parent, Vector2 pos, Sprite face, Sprite icon, string label,
            UnityEngine.Events.UnityAction onClick, out RectTransform rect)
        {
            var btn = Img("Tab_" + label, parent, Vector2.zero, Vector2.zero, Vector2.zero,
                pos, new Vector2(126, 204), face, Color.white);
            rect = btn.rectTransform;
            MakeClickable(btn, onClick);
            Icon("I", btn.transform, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0.5f, 0.5f),
                new Vector2(0, -68), new Vector2(76, 76), icon, Color.white);
            Txt("L", btn.transform, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0.5f, 0f),
                new Vector2(0, 14), new Vector2(120, 28), label, fontSmall, 19, Color.white);
        }

        /// Designed navy square button for the right-edge stack.
        void SideButton(Transform parent, Vector2 pos, Sprite icon, string label,
            UnityEngine.Events.UnityAction onClick, out Image btn)
        {
            btn = Img("Side_" + label, parent, new Vector2(1f, 0f), new Vector2(1f, 0f), new Vector2(1f, 0f),
                pos, new Vector2(112, 112), btnSquareNavy, Color.white);
            MakeClickable(btn, onClick);
            Icon("I", btn.transform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                new Vector2(0, 12), new Vector2(58, 58), icon, Color.white);
            Txt("L", btn.transform, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0.5f, 0f),
                new Vector2(0, 6), new Vector2(104, 24), label, fontSmall, 15, Color.white);
        }

        void RefreshHub()
        {
            if (hubScreen == null) return;

            // resource counters count up instead of snapping
            if (coinsShown < 0) { coinsShown = CrownfallMeta.Coins; hubCoins.text = coinsShown.ToString(); }
            else if (coinsShown != CrownfallMeta.Coins)
            {
                UiTween.CountUp(hubCoins, coinsShown, CrownfallMeta.Coins, 0.6f);
                coinsShown = CrownfallMeta.Coins;
            }
            if (gemsShown < 0) { gemsShown = CrownfallMeta.Gems; hubGems.text = gemsShown.ToString(); }
            else if (gemsShown != CrownfallMeta.Gems)
            {
                UiTween.CountUp(hubGems, gemsShown, CrownfallMeta.Gems, 0.6f);
                gemsShown = CrownfallMeta.Gems;
            }

            hubTrophies.text = CrownfallMeta.Trophies.ToString();
            hubLevelNum.text = CrownfallMeta.Level.ToString();
            hubPlayerName.text = CrownfallMeta.PlayerName.ToUpper();
            int xpNeed = CrownfallMeta.XpForLevel(CrownfallMeta.Level);
            hubXpText.text = $"{CrownfallMeta.Xp}/{xpNeed}";
            UiTween.FillTo(hubXpFill, Mathf.Clamp01(CrownfallMeta.Xp / (float)xpNeed));

            var kit = ClassKits.Get((ClassId)CrownfallMeta.SelectedClass);
            hubChampName.text = kit.displayName.ToUpper();
            hubChampIcon.sprite = IconFor(kit.id);
            hubSigilIcon.sprite = SigilSprite(CrownfallMeta.EquippedSigil);
            if (showcase != null) showcase.Show(CrownfallMeta.SelectedClass);

            var mode = GameModes.Selected;
            int modeIdx = Mathf.Clamp(CrownfallMeta.SelectedMode, 0, GameModes.All.Length - 1);
            hubModeCard.sprite = ModeFace(modeIdx);
            hubModeIcon.sprite = ModeIcon(modeIdx);
            hubModeName.text = mode.title;
            hubModeSub.text = ModeDetail(mode);

            int unread = CrownfallMeta.UnreadNews;
            inboxBadge.SetActive(unread > 0);
            inboxBadgeText.text = unread.ToString();
            giftBadge.SetActive(CrownfallMeta.GiftReady);
            int claimable = CrownfallQuests.ClaimableCount;
            questBadge.SetActive(claimable > 0);
            questBadgeText.text = claimable.ToString();

            RefreshShopAffordability();
            RefreshChampFocus();
        }
    }
}
