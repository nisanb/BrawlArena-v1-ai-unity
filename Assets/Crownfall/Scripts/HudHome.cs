using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using Crownfall.UI;

namespace Crownfall
{
    /// Menu-layer screens: the boot splash, the login (profile forge) screen and
    /// the home hub. The hub keeps the 3D podium champion visible behind it;
    /// boot and login run over a full painted backdrop.
    public partial class HUDController
    {
        TMP_Text hubCoins, hubGems, hubTrophies, hubLevelNum, hubChampName, hubPlayerName;
        Image hubXpFill, hubChampIcon;
        GameObject inboxBadge, giftBadge;
        TMP_Text inboxBadgeText;
        int coinsShown = -1, gemsShown = -1;

        // entrance choreography targets
        RectTransform hubProfileRect, hubTrophyRect, hubPillsRect, hubPlayRect,
            hubEventRect, hubChampPlateRect, hubLeftRail1, hubLeftRail2, hubRightRail1, hubRightRail2;

        TMP_InputField loginInput;
        readonly List<(int idx, Image ring)> loginEmblems = new List<(int, Image)>();
        RectTransform loginCardRect;
        Image bootBarFill;

        // ================================================================ backdrop

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

            // loading bar
            var barBg = Img("LoadBg", t, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0.5f, 0f),
                new Vector2(0, 150), new Vector2(640, 30), barBgBasic, new Color(0.06f, 0.05f, 0.1f, 0.95f));
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
            if (MatchManager.I != null && MatchManager.I.State == MatchState.Menu) ShowMenuLayer();
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

            // starting champion emblems
            Txt("ChampLbl", card.transform, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, 0.5f),
                new Vector2(-255, -486), new Vector2(360, 30), "CHOOSE YOUR CHAMPION", fontSmall, 19,
                new Color(1f, 1f, 1f, 0.65f), TextAlignmentOptions.Left);
            loginEmblems.Clear();
            for (int i = 0; i < 4; i++)
            {
                int idx = i;
                var kit = ClassKits.Get((ClassId)i);
                float x = -192 + i * 128;
                var ring = Img("Em" + i, card.transform, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f),
                    new Vector2(0.5f, 0.5f), new Vector2(x, -566), new Vector2(104, 104), frameCircle,
                    new Color(0.1f, 0.12f, 0.22f, 0.98f), true);
                ring.type = Image.Type.Simple;
                MakeClickable(ring, () =>
                {
                    CrownfallMeta.SelectedClass = idx;
                    RefreshLoginEmblems();
                    Burst(fxSparklePrefab, ring.rectTransform, Vector2.zero, 0.6f);
                });
                Icon("I", ring.transform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                    new Vector2(0, 12), new Vector2(52, 52), IconFor(kit.id), Color.white);
                Txt("N", ring.transform, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0.5f, 1f),
                    new Vector2(0, 8), new Vector2(130, 26), kit.displayName.ToUpper(), fontSmall, 14,
                    new Color(0.9f, 0.92f, 1f));
                loginEmblems.Add((idx, ring));
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
            foreach (var (idx, ring) in loginEmblems)
            {
                bool sel = idx == CrownfallMeta.SelectedClass;
                ring.color = sel ? new Color(0.55f, 0.42f, 0.12f, 1f) : new Color(0.1f, 0.12f, 0.22f, 0.98f);
                ring.rectTransform.localScale = Vector3.one * (sel ? 1.12f : 1f);
            }
        }

        // ================================================================ home hub

        void BuildHomeHub()
        {
            hubScreen = MakePanel("HomeHub");
            var t = hubScreen.Go.transform;

            // soft edge vignettes keep plates readable over the bright arena
            Img("TopDim", t, new Vector2(0f, 1f), Vector2.one, new Vector2(0.5f, 1f),
                Vector2.zero, new Vector2(0, 150), null, new Color(0.02f, 0.02f, 0.05f, 0.36f));
            Img("BottomDim", t, Vector2.zero, new Vector2(1f, 0f), new Vector2(0.5f, 0f),
                Vector2.zero, new Vector2(0, 210), null, new Color(0.02f, 0.02f, 0.05f, 0.36f));

            // -- wordmark, top center
            Icon("Crown", t, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0.5f, 1f),
                new Vector2(0, -10), new Vector2(52, 52), iconCrown, Color.white);
            Txt("Wordmark", t, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0.5f, 1f),
                new Vector2(0, -58), new Vector2(700, 48), "CROWNFALL ARENA", fontBig, 34, Gold);

            // -- profile chip, top left (Brawl-style: level badge + name + XP)
            var profile = Img("Profile", t, new Vector2(0f, 1f), new Vector2(0f, 1f),
                new Vector2(0f, 1f), new Vector2(24, -20), new Vector2(340, 100), panelNavy, Color.white);
            hubProfileRect = profile.rectTransform;
            Icon("LvBadge", profile.transform, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(0.5f, 0.5f),
                new Vector2(46, 2), new Vector2(64, 80), levelBadge, Color.white);
            hubLevelNum = Txt("LvNum", profile.transform, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f),
                new Vector2(0.5f, 0.5f), new Vector2(46, 4), new Vector2(60, 44), "1", fontMid, 27, Color.white);
            hubPlayerName = Txt("PName", profile.transform, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(0f, 1f),
                new Vector2(94, -14), new Vector2(230, 34), "CHAMPION", fontSmall, 23, Color.white,
                TextAlignmentOptions.Left);
            var xpBg = Img("XpBg", profile.transform, new Vector2(0f, 0f), new Vector2(0f, 0f), new Vector2(0f, 0f),
                new Vector2(94, 18), new Vector2(210, 20), lvlBg, Color.white);
            hubXpFill = MakeFill(xpBg.rectTransform, lvlFill, Color.white, new Vector2(210, 20));

            // -- trophy road pill docked right of the profile (Brawl-style)
            var road = Img("TrophyRoad", t, new Vector2(0f, 1f), new Vector2(0f, 1f),
                new Vector2(0f, 1f), new Vector2(376, -20), new Vector2(252, 74), plateRound, PlateDark);
            hubTrophyRect = road.rectTransform;
            Icon("TIco", road.transform, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(0.5f, 0.5f),
                new Vector2(42, 2), new Vector2(52, 52), icoTrophyBig, Color.white);
            hubTrophies = Txt("TCount", road.transform, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f),
                new Vector2(0f, 0.5f), new Vector2(80, 10), new Vector2(150, 40), "0", fontMid, 29, Gold,
                TextAlignmentOptions.Left);
            Txt("TLbl", road.transform, new Vector2(0f, 0f), new Vector2(0f, 0f), new Vector2(0f, 0f),
                new Vector2(82, 8), new Vector2(160, 24), "TROPHY ROAD", fontSmall, 13,
                new Color(1f, 1f, 1f, 0.6f), TextAlignmentOptions.Left);

            // -- resources, top right
            var pills = Rect("Pills", t, Vector2.one, Vector2.one, Vector2.one, Vector2.zero, Vector2.zero);
            hubPillsRect = pills;
            var gear = Icon("SettingsBtn", pills, Vector2.one, Vector2.one, Vector2.one,
                new Vector2(-24, -22), new Vector2(62, 62), btnCircle, Color.white);
            MakeClickable(gear, OpenSettings);
            Icon("G", gear.transform, Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f),
                new Vector2(0, 2), new Vector2(30, 30), icoGear, Color.white);
            hubGems = ResourcePill(pills, new Vector2(-102, -26), iconGemBar, "0");
            hubCoins = ResourcePill(pills, new Vector2(-322, -26), iconCoinBar, "0");

            // -- tall vertical tabs pinned bottom-left (Brawl-style SHOP | CHAMPIONS)
            VerticalTab(t, new Vector2(26, 24), menuShop, "SHOP", OpenShop, out var shopTab);
            hubLeftRail1 = shopTab;
            VerticalTab(t, new Vector2(188, 24), menuCards, "CHAMPS",
                () => MatchManager.I?.OpenClassSelect(), out var champTab);
            hubLeftRail2 = champTab;

            // -- quest-style stack on the right edge, above the PLAY cluster
            SideButton(t, new Vector2(-24, 428), menuInbox, "INBOX", OpenInbox, out var inboxSide);
            hubRightRail1 = inboxSide.rectTransform;
            inboxBadge = Badge(inboxSide.transform, out inboxBadgeText);
            SideButton(t, new Vector2(-24, 302), menuGift, "GIFTS",
                () => router.OpenModal(giftModal), out var giftSide);
            hubRightRail2 = giftSide.rectTransform;
            giftBadge = Badge(giftSide.transform, out var giftBadgeText);
            giftBadgeText.text = "!";

            // -- champion plate, bottom center (tap to change)
            var champPlate = Img("ChampPlate", t, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f),
                new Vector2(0.5f, 0f), new Vector2(0, 118), new Vector2(300, 62), trapBlue, Color.white);
            hubChampPlateRect = champPlate.rectTransform;
            MakeClickable(champPlate, () => MatchManager.I?.OpenClassSelect());
            hubChampIcon = Icon("CIco", champPlate.transform, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f),
                new Vector2(0f, 0.5f), new Vector2(20, 0), new Vector2(34, 34), icoShield, Color.white);
            hubChampName = Txt("CName", champPlate.transform, Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f),
                new Vector2(12, 1), new Vector2(-70, -8), "KNIGHT", fontMid, 28, Color.white);

            // -- event/mode card docked onto the PLAY cluster (Brawl-style)
            var evt = Img("EventCard", t, new Vector2(1f, 0f), new Vector2(1f, 0f),
                new Vector2(1f, 0f), new Vector2(-438, 30), new Vector2(320, 132), cardEventBg, Color.white);
            evt.type = Image.Type.Simple;
            hubEventRect = evt.rectTransform;
            MakeClickable(evt, OpenPlayMenu);
            Icon("EIco", evt.transform, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(0f, 0.5f),
                new Vector2(18, 6), new Vector2(48, 48), icoSword, Gold);
            Txt("EName", evt.transform, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0.5f, 1f),
                new Vector2(40, -22), new Vector2(-96, 32), "10-KILL BRAWL", fontSmall, 23, Color.white,
                TextAlignmentOptions.Left);
            Txt("ESub", evt.transform, new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(0.5f, 0f),
                new Vector2(40, 26), new Vector2(-96, 28), "Sundered Crown  ·  3v3",
                fontSmall, 16, new Color(1f, 0.9f, 0.6f), TextAlignmentOptions.Left);

            // PLAY gets a slow-spinning ray halo behind it
            var playRays = Icon("PlayRays", t, new Vector2(1f, 0f), new Vector2(1f, 0f), new Vector2(0.5f, 0.5f),
                new Vector2(-224, 96), new Vector2(560, 560), fxRays, new Color(1f, 0.85f, 0.4f, 0.14f));
            var play = MenuButton(t, Vector2.zero, new Vector2(400, 132), "PLAY", 56,
                btnYellow, icoPlay, OpenPlayMenu);
            hubPlayRect = play.GetComponent<RectTransform>();
            hubPlayRect.SetAnchor(new Vector2(1f, 0f), new Vector2(-224, 96));

            // -- demo above the tabs + version line
            MenuButton(t, Vector2.zero, new Vector2(252, 60), "WATCH DEMO", 20,
                btnBlue, icoMovie, () => MatchManager.I?.StartDemo())
                .GetComponent<RectTransform>().SetAnchor(new Vector2(0f, 0f), new Vector2(152, 296));
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
                UiTween.SlideIn(hubRightRail1, new Vector2(180f, 0f), 0.36f, UiTween.Ease.CubicOut, 0.1f);
                UiTween.SlideIn(hubRightRail2, new Vector2(180f, 0f), 0.36f, UiTween.Ease.CubicOut, 0.17f);
                UiTween.SlideIn(hubEventRect, new Vector2(0f, -220f), 0.4f, UiTween.Ease.CubicOut, 0.14f);
                UiTween.PopIn(hubChampPlateRect, 0.34f, 0.2f);
                UiTween.PopIn(hubPlayRect, 0.42f, 0.24f);
                UiTween.SpinForever(playRays.rectTransform, 18f);
            };
            hubScreen.OnHide = () => UiTween.StopLoop(hubPlayRect);
        }

        TMP_Text ResourcePill(Transform parent, Vector2 pos, Sprite icon, string initial)
        {
            var pill = Img("Pill", parent, Vector2.one, Vector2.one, Vector2.one,
                pos, new Vector2(206, 54), resourcePill, new Color(0.07f, 0.08f, 0.14f, 0.95f));
            Icon("I", pill.transform, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(0.5f, 0.5f),
                new Vector2(24, 1), new Vector2(46, 46), icon, Color.white);
            var count = Txt("N", pill.transform, Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f),
                new Vector2(6, 1), new Vector2(-92, -8), initial, fontMid, 25, Color.white);
            var plus = Icon("Plus", pill.transform, new Vector2(1f, 0.5f), new Vector2(1f, 0.5f),
                new Vector2(0.5f, 0.5f), new Vector2(-24, 0), new Vector2(38, 38), resourceBtnGreen, Color.white);
            MakeClickable(plus, OpenShop);
            Icon("A", plus.transform, Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f),
                Vector2.zero, new Vector2(20, 20), resourceAdd, Color.white);
            return count;
        }

        /// Brawl-style tall vertical tab, pinned to the bottom-left corner.
        void VerticalTab(Transform parent, Vector2 pos, Sprite icon, string label,
            UnityEngine.Events.UnityAction onClick, out RectTransform rect)
        {
            var btn = Img("Tab_" + label, parent, Vector2.zero, Vector2.zero, Vector2.zero,
                pos, new Vector2(150, 236), squareBlue, Color.white);
            rect = btn.rectTransform;
            MakeClickable(btn, onClick);
            Icon("I", btn.transform, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0.5f, 0.5f),
                new Vector2(0, -78), new Vector2(92, 92), icon, Color.white);
            Txt("L", btn.transform, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0.5f, 0f),
                new Vector2(0, 16), new Vector2(144, 32), label, fontSmall, 22, Color.white);
        }

        /// Compact square button for the right-edge quest stack.
        void SideButton(Transform parent, Vector2 pos, Sprite icon, string label,
            UnityEngine.Events.UnityAction onClick, out Image btn)
        {
            btn = Img("Side_" + label, parent, new Vector2(1f, 0f), new Vector2(1f, 0f), new Vector2(1f, 0f),
                pos, new Vector2(108, 108), squareBlue, Color.white);
            MakeClickable(btn, onClick);
            Icon("I", btn.transform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                new Vector2(0, 12), new Vector2(58, 58), icon, Color.white);
            Txt("L", btn.transform, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0.5f, 0f),
                new Vector2(0, 6), new Vector2(104, 24), label, fontSmall, 15, Color.white);
        }

        GameObject Badge(Transform parent, out TMP_Text label)
        {
            var dot = Img("Badge", parent, Vector2.one, Vector2.one, new Vector2(0.5f, 0.5f),
                new Vector2(-8, -8), new Vector2(36, 36), alertDot, new Color(1f, 0.27f, 0.25f));
            dot.type = Image.Type.Simple;
            label = Txt("N", dot.transform, Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f),
                new Vector2(0, 1), new Vector2(-4, -4), "1", fontSmall, 19, Color.white);
            return dot.gameObject;
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
            UiTween.FillTo(hubXpFill, Mathf.Clamp01(
                CrownfallMeta.Xp / (float)CrownfallMeta.XpForLevel(CrownfallMeta.Level)));

            var kit = ClassKits.Get((ClassId)CrownfallMeta.SelectedClass);
            hubChampName.text = kit.displayName.ToUpper();
            hubChampIcon.sprite = IconFor(kit.id);

            int unread = CrownfallMeta.UnreadNews;
            inboxBadge.SetActive(unread > 0);
            inboxBadgeText.text = unread.ToString();
            giftBadge.SetActive(CrownfallMeta.GiftReady);

            RefreshShopAffordability();
            RefreshChampFocus();
        }
    }
}
