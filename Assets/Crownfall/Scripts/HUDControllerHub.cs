using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

namespace Crownfall
{
    /// Home-hub half of the HUD: Brawl-style main menu (profile, resources,
    /// rails, PLAY), the shop/inbox/gift popups and the champions screen.
    public partial class HUDController
    {
        [Header("Wired by forge — hub")]
        public Sprite resourcePill;
        public Sprite resourceBtnGreen;
        public Sprite resourceAdd;
        public Sprite iconCoinBar;
        public Sprite iconGemBar;
        public Sprite alertDot;
        public Sprite squareBlue;
        public Sprite menuShop;
        public Sprite menuCards;
        public Sprite menuInbox;
        public Sprite menuGift;
        public Sprite menuTrophy;
        public Sprite iconChestGold;
        public Sprite iconCoinBig;
        public Sprite iconPouch;
        public Sprite levelBadge;
        public Sprite focusFrame;

        GameObject hubPanel;
        GameObject shopPanel, inboxPanel, giftPanel;
        TMP_Text hubCoins, hubGems, hubTrophies, hubLevelNum, hubChampName;
        Image hubXpFill, hubChampIcon;
        GameObject inboxBadge, giftBadge;
        TMP_Text inboxBadgeText;
        TMP_Text giftStateText, giftRewardText;
        GameObject giftOpenBtn;
        readonly List<(int idx, GameObject dot)> newsDots = new List<(int, GameObject)>();
        readonly List<(Button btn, int cost, TMP_Text label)> shopBuyButtons =
            new List<(Button, int, TMP_Text)>();
        RectTransform champFocus;
        readonly List<Vector2> champCardPos = new List<Vector2>();

        void PlayClick() => GameEffects.I?.PlayUi(GameEffects.I.uiTick, 0.35f);

        Button MakeClickable(Image img, UnityEngine.Events.UnityAction onClick)
        {
            img.raycastTarget = true;
            var b = img.gameObject.AddComponent<Button>();
            var colors = b.colors;
            colors.highlightedColor = new Color(1.14f, 1.14f, 1.08f);
            colors.pressedColor = new Color(0.74f, 0.74f, 0.74f);
            b.colors = colors;
            b.onClick.AddListener(PlayClick);
            b.onClick.AddListener(onClick);
            return b;
        }

        // ================================================================ home hub

        void BuildHomeHub()
        {
            hubPanel = Rect("HomeHub", root, Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f),
                Vector2.zero, Vector2.zero).gameObject;

            // soft edge vignettes keep plates readable over the bright arena
            Img("TopDim", hubPanel.transform, new Vector2(0f, 1f), Vector2.one, new Vector2(0.5f, 1f),
                Vector2.zero, new Vector2(0, 150), null, new Color(0.02f, 0.02f, 0.05f, 0.36f));
            Img("BottomDim", hubPanel.transform, Vector2.zero, new Vector2(1f, 0f), new Vector2(0.5f, 0f),
                Vector2.zero, new Vector2(0, 210), null, new Color(0.02f, 0.02f, 0.05f, 0.36f));

            // -- wordmark, top center
            Icon("Crown", hubPanel.transform, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0.5f, 1f),
                new Vector2(0, -10), new Vector2(52, 52), iconCrown, Color.white);
            Txt("Wordmark", hubPanel.transform, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0.5f, 1f),
                new Vector2(0, -58), new Vector2(700, 48), "CROWNFALL ARENA", fontBig, 34, Gold);

            // -- profile plate, top left
            var profile = Img("Profile", hubPanel.transform, new Vector2(0f, 1f), new Vector2(0f, 1f),
                new Vector2(0f, 1f), new Vector2(24, -20), new Vector2(330, 96), plateRound, PlateDark);
            Icon("LvBadge", profile.transform, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(0.5f, 0.5f),
                new Vector2(44, 2), new Vector2(64, 80), levelBadge, Color.white);
            hubLevelNum = Txt("LvNum", profile.transform, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f),
                new Vector2(0.5f, 0.5f), new Vector2(44, 4), new Vector2(60, 44), "1", fontMid, 27, Color.white);
            Txt("PName", profile.transform, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(0f, 1f),
                new Vector2(92, -12), new Vector2(220, 34), "CHAMPION", fontSmall, 24, Color.white,
                TextAlignmentOptions.Left);
            var xpBg = Img("XpBg", profile.transform, new Vector2(0f, 0f), new Vector2(0f, 0f), new Vector2(0f, 0f),
                new Vector2(92, 16), new Vector2(206, 18), barBgBasic, new Color(0.03f, 0.03f, 0.05f, 1f));
            hubXpFill = MakeFill(xpBg.rectTransform, barFillBasic, AzureCol, new Vector2(206, 18));

            var trophyPlate = Img("TrophyPlate", hubPanel.transform, new Vector2(0f, 1f), new Vector2(0f, 1f),
                new Vector2(0f, 1f), new Vector2(24, -126), new Vector2(220, 58), plateRound, PlateDark);
            Icon("TIco", trophyPlate.transform, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(0f, 0.5f),
                new Vector2(12, 0), new Vector2(40, 40), menuTrophy, Color.white);
            hubTrophies = Txt("TCount", trophyPlate.transform, Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f),
                new Vector2(34, 1), new Vector2(-76, -8), "0", fontMid, 28, Gold, TextAlignmentOptions.Left);

            // -- resources, top right
            var gear = Icon("SettingsBtn", hubPanel.transform, Vector2.one, Vector2.one, Vector2.one,
                new Vector2(-24, -22), new Vector2(62, 62), btnCircle, Color.white);
            MakeClickable(gear, OpenSettings);
            Icon("G", gear.transform, Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f),
                new Vector2(0, 2), new Vector2(30, 30), icoGear, Color.white);

            hubGems = ResourcePill(hubPanel.transform, new Vector2(-102, -26), iconGemBar, "0");
            hubCoins = ResourcePill(hubPanel.transform, new Vector2(-322, -26), iconCoinBar, "0");

            // -- left rail
            RailButton(hubPanel.transform, new Vector2(0f, 0.5f), new Vector2(26, 136), menuShop, 76,
                "SHOP", 21, OpenShop, out _);
            RailButton(hubPanel.transform, new Vector2(0f, 0.5f), new Vector2(26, -22), menuCards, 72,
                "CHAMPIONS", 16, () => MatchManager.I?.OpenClassSelect(), out _);

            // -- right rail
            RailButton(hubPanel.transform, new Vector2(1f, 0.5f), new Vector2(-154, 136), menuInbox, 70,
                "INBOX", 21, OpenInbox, out var inboxRail);
            inboxBadge = Badge(inboxRail.transform, out inboxBadgeText);
            RailButton(hubPanel.transform, new Vector2(1f, 0.5f), new Vector2(-154, -22), menuGift, 70,
                "GIFTS", 21, OpenGift, out var giftRail);
            giftBadge = Badge(giftRail.transform, out var giftBadgeText);
            giftBadgeText.text = "!";

            // -- champion plate, bottom center (tap to change)
            var champPlate = Img("ChampPlate", hubPanel.transform, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f),
                new Vector2(0.5f, 0f), new Vector2(0, 118), new Vector2(300, 62), trapBlue, Color.white);
            MakeClickable(champPlate, () => MatchManager.I?.OpenClassSelect());
            hubChampIcon = Icon("CIco", champPlate.transform, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f),
                new Vector2(0f, 0.5f), new Vector2(20, 0), new Vector2(34, 34), icoShield, Color.white);
            hubChampName = Txt("CName", champPlate.transform, Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f),
                new Vector2(12, 1), new Vector2(-70, -8), "KNIGHT", fontMid, 28, Color.white);

            // -- event card + PLAY, bottom right
            var evt = Img("EventCard", hubPanel.transform, new Vector2(1f, 0f), new Vector2(1f, 0f),
                new Vector2(1f, 0f), new Vector2(-24, 174), new Vector2(370, 92), plateRound, PlateDark);
            Icon("EIco", evt.transform, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(0f, 0.5f),
                new Vector2(14, 0), new Vector2(44, 44), icoSword, Gold);
            Txt("EName", evt.transform, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0.5f, 1f),
                new Vector2(34, -12), new Vector2(-84, 32), "10-KILL BRAWL", fontSmall, 24, Color.white,
                TextAlignmentOptions.Left);
            Txt("ESub", evt.transform, new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(0.5f, 0f),
                new Vector2(34, 12), new Vector2(-84, 28), "Sundered Crown  ·  3 v 3  ·  5:00",
                fontSmall, 17, new Color(1f, 0.9f, 0.6f), TextAlignmentOptions.Left);

            MenuButton(hubPanel.transform, Vector2.zero, new Vector2(400, 132), "PLAY", 56,
                btnYellow, icoPlay, () => MatchManager.I?.StartMatch())
                .GetComponent<RectTransform>().SetAnchor(new Vector2(1f, 0f), new Vector2(-224, 96));

            // -- demo + version, bottom left
            MenuButton(hubPanel.transform, Vector2.zero, new Vector2(252, 66), "WATCH DEMO", 21,
                btnBlue, icoMovie, () => MatchManager.I?.StartDemo())
                .GetComponent<RectTransform>().SetAnchor(new Vector2(0f, 0f), new Vector2(150, 70));
            Txt("Version", hubPanel.transform, Vector2.zero, Vector2.zero, Vector2.zero,
                new Vector2(26, 12), new Vector2(500, 30), "Crownfall Arena  ·  build " + Application.version,
                fontSmall, 16, new Color(1f, 1f, 1f, 0.45f), TextAlignmentOptions.Left);
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

        void RailButton(Transform parent, Vector2 anchor, Vector2 pos, Sprite icon, float iconSize,
            string label, float labelSize, UnityEngine.Events.UnityAction onClick, out Image btn)
        {
            btn = Img("Rail_" + label, parent, anchor, anchor, new Vector2(0f, 0.5f),
                pos, new Vector2(128, 128), squareBlue, Color.white);
            MakeClickable(btn, onClick);
            Icon("I", btn.transform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                new Vector2(0, 16), new Vector2(iconSize, iconSize), icon, Color.white);
            Txt("L", btn.transform, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0.5f, 0f),
                new Vector2(0, 8), new Vector2(122, 28), label, fontSmall, labelSize, Color.white);
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
            if (hubPanel == null) return;
            hubCoins.text = CrownfallMeta.Coins.ToString();
            hubGems.text = CrownfallMeta.Gems.ToString();
            hubTrophies.text = CrownfallMeta.Trophies.ToString();
            hubLevelNum.text = CrownfallMeta.Level.ToString();
            hubXpFill.fillAmount = Mathf.Clamp01(
                CrownfallMeta.Xp / (float)CrownfallMeta.XpForLevel(CrownfallMeta.Level));

            var kit = ClassKits.Get((ClassId)CrownfallMeta.SelectedClass);
            hubChampName.text = kit.displayName.ToUpper();
            hubChampIcon.sprite = IconFor(kit.id);

            int unread = CrownfallMeta.UnreadNews;
            inboxBadge.SetActive(unread > 0);
            inboxBadgeText.text = unread.ToString();
            giftBadge.SetActive(CrownfallMeta.GiftReady);

            foreach (var (btn, cost, label) in shopBuyButtons)
            {
                bool can = CrownfallMeta.Gems >= cost;
                btn.interactable = can;
                label.color = can ? Color.white : new Color(1f, 1f, 1f, 0.45f);
            }
            foreach (var (idx, dot) in newsDots)
                dot.SetActive(!CrownfallMeta.IsNewsRead(idx));
            if (champFocus != null && CrownfallMeta.SelectedClass < champCardPos.Count)
                champFocus.anchoredPosition = champCardPos[CrownfallMeta.SelectedClass];
        }

        // ================================================================ shop

        void OpenShop() { shopPanel.SetActive(true); RefreshHub(); }
        void OpenInbox() { inboxPanel.SetActive(true); RefreshHub(); }

        Image PopupShell(string title, Sprite ribbon, Vector2 size, out GameObject panel)
        {
            panel = Rect(title, root, Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f),
                Vector2.zero, Vector2.zero).gameObject;
            Img("Dim", panel.transform, Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f),
                Vector2.zero, Vector2.zero, null, new Color(0.02f, 0.02f, 0.05f, 0.72f), true);
            var frame = Img("Frame", panel.transform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                new Vector2(0.5f, 0.5f), new Vector2(0, -20), size, popupNavy, Color.white, true);
            var rib = Img("Ribbon", frame.transform, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f),
                new Vector2(0.5f, 0.5f), new Vector2(0, 26), new Vector2(Mathf.Min(size.x - 160f, 480f), 116),
                ribbon, Color.white);
            Txt("T", rib.transform, Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f),
                new Vector2(0, 8), new Vector2(-120, -50), title.ToUpper(), fontMid, 40, Color.white);
            var closeImg = Icon("CloseBtn", frame.transform, new Vector2(1f, 1f), new Vector2(1f, 1f),
                new Vector2(0.5f, 0.5f), new Vector2(-26, 8), new Vector2(64, 64), btnCircle, Color.white);
            var p = panel;
            MakeClickable(closeImg, () => p.SetActive(false));
            Icon("X", closeImg.transform, Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f),
                new Vector2(0, 2), new Vector2(24, 24), icoClose, Color.white);
            return frame;
        }

        void BuildShop()
        {
            var frame = PopupShell("Shop", ribbonBlue, new Vector2(880, 560), out shopPanel);

            ShopCard(frame.transform, new Vector2(-280, -16), "COIN STASH", iconCoinBig, "+150", 10,
                () => { CrownfallMeta.AddCoins(150); ShowToast("+150 COINS"); });
            ShopCard(frame.transform, new Vector2(0, -16), "GOLD POUCH", iconPouch, "+400", 24,
                () => { CrownfallMeta.AddCoins(400); ShowToast("+400 COINS"); });
            ShopCard(frame.transform, new Vector2(280, -16), "GOLDEN CHEST", iconChestGold, "40-220?", 16,
                () =>
                {
                    int roll = Random.Range(40, 221);
                    CrownfallMeta.AddCoins(roll);
                    ShowToast($"CHEST PAID OUT  +{roll} COINS");
                });

            Txt("Hint", frame.transform, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0.5f, 0f),
                new Vector2(0, 20), new Vector2(700, 30), "earn gems from level-ups and free gifts",
                fontSmall, 18, new Color(1f, 1f, 1f, 0.55f));
            shopPanel.SetActive(false);
        }

        void ShopCard(Transform parent, Vector2 pos, string name, Sprite icon, string amount, int gemCost,
            UnityEngine.Events.UnityAction grant)
        {
            var card = Img("Item_" + name, parent, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                new Vector2(0.5f, 0.5f), pos, new Vector2(250, 330), frameRound,
                new Color(0.09f, 0.11f, 0.2f, 0.96f));
            Txt("N", card.transform, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0.5f, 1f),
                new Vector2(0, -14), new Vector2(230, 32), name, fontSmall, 22, Gold);
            Icon("I", card.transform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                new Vector2(0, 34), new Vector2(128, 128), icon, Color.white);
            Txt("A", card.transform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                new Vector2(0, -58), new Vector2(220, 34), amount, fontMid, 27, Color.white);

            var buy = Img("Buy", card.transform, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f),
                new Vector2(0.5f, 0f), new Vector2(0, 16), new Vector2(190, 62), btnGreen, Color.white);
            var b = MakeClickable(buy, () =>
            {
                if (CrownfallMeta.SpendGems(gemCost))
                {
                    grant();
                    GameEffects.I?.PlayUi(GameEffects.I.killDing, 0.5f);
                }
                else ShowToast("NOT ENOUGH GEMS");
            });
            Icon("G", buy.transform, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(0f, 0.5f),
                new Vector2(26, 2), new Vector2(30, 30), iconGemBar, Color.white);
            var priceLabel = Txt("P", buy.transform, Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f),
                new Vector2(16, 2), new Vector2(-60, -10), gemCost.ToString(), fontMid, 27, Color.white);
            shopBuyButtons.Add((b, gemCost, priceLabel));
        }

        // ================================================================ inbox

        void BuildInbox()
        {
            var frame = PopupShell("Inbox", ribbonBlue, new Vector2(760, 580), out inboxPanel);

            for (int i = 0; i < CrownfallMeta.News.Length; i++)
            {
                int idx = i;
                var (title, body) = CrownfallMeta.News[i];
                var row = Img("News" + i, frame.transform, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f),
                    new Vector2(0.5f, 1f), new Vector2(0, -96 - i * 142), new Vector2(660, 128),
                    plateRound, new Color(0.09f, 0.11f, 0.2f, 0.96f));
                MakeClickable(row, () => { CrownfallMeta.MarkNewsRead(idx); RefreshHub(); });
                var dot = Img("Dot", row.transform, new Vector2(0f, 1f), new Vector2(0f, 1f),
                    new Vector2(0.5f, 0.5f), new Vector2(24, -24), new Vector2(20, 20), alertDot,
                    new Color(1f, 0.27f, 0.25f));
                dot.type = Image.Type.Simple;
                newsDots.Add((idx, dot.gameObject));
                Txt("T", row.transform, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0.5f, 1f),
                    new Vector2(24, -10), new Vector2(-84, 32), title, fontSmall, 23, Gold,
                    TextAlignmentOptions.Left);
                var b = Txt("B", row.transform, new Vector2(0f, 0f), new Vector2(1f, 1f), new Vector2(0.5f, 0.5f),
                    new Vector2(12, -18), new Vector2(-60, -52), body, fontSmall, 19,
                    new Color(0.92f, 0.92f, 0.98f), TextAlignmentOptions.TopLeft);
                b.enableWordWrapping = true;
            }
            inboxPanel.SetActive(false);
        }

        // ================================================================ gifts

        void OpenGift()
        {
            giftPanel.SetActive(true);
            giftRewardText.gameObject.SetActive(false);
            RefreshGift();
        }

        void RefreshGift()
        {
            bool ready = CrownfallMeta.GiftReady;
            giftOpenBtn.SetActive(ready);
            if (ready) giftStateText.text = "A gift awaits, champion!";
            else
            {
                var t = CrownfallMeta.GiftTimeLeft;
                giftStateText.text = $"next gift in  {(int)t.TotalHours}h {t.Minutes:00}m";
            }
        }

        void BuildGift()
        {
            var frame = PopupShell("Free Gift", ribbonOrange, new Vector2(560, 540), out giftPanel);

            Icon("Chest", frame.transform, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0.5f, 1f),
                new Vector2(0, -86), new Vector2(230, 196), iconChestGold, Color.white);
            giftStateText = Txt("State", frame.transform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                new Vector2(0.5f, 0.5f), new Vector2(0, -50), new Vector2(480, 40), "", fontSmall, 24,
                Color.white);
            giftRewardText = Txt("Reward", frame.transform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                new Vector2(0.5f, 0.5f), new Vector2(0, -96), new Vector2(480, 44), "", fontMid, 30, Gold);
            giftRewardText.gameObject.SetActive(false);

            giftOpenBtn = MenuButton(frame.transform, new Vector2(0, -180), new Vector2(280, 84),
                "OPEN", 32, btnGreen, null, () =>
                {
                    var (c, g) = CrownfallMeta.ClaimGift();
                    if (c <= 0) { RefreshGift(); return; }
                    giftRewardText.text = g > 0 ? $"+{c} COINS   +{g} GEMS" : $"+{c} COINS";
                    giftRewardText.gameObject.SetActive(true);
                    GameEffects.I?.PlayUi(GameEffects.I.uiVictory, 0.55f);
                    RefreshGift();
                    RefreshHub();
                }).gameObject;

            giftPanel.SetActive(false);
        }

        // ================================================================ champions

        void BuildChampions()
        {
            classPanel = Rect("Champions", root, Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f),
                Vector2.zero, Vector2.zero).gameObject;
            Img("Dim", classPanel.transform, Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f),
                Vector2.zero, Vector2.zero, null, new Color(0.03f, 0.03f, 0.06f, 0.55f));

            var ribbon = Img("Ribbon", classPanel.transform, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f),
                new Vector2(0.5f, 1f), new Vector2(0, -30), new Vector2(760, 148), ribbonYellow, Color.white);
            Txt("Title", ribbon.transform, Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f),
                new Vector2(0, 12), new Vector2(-220, -66), "CHAMPIONS", fontMid, 48, Color.white);

            MenuButton(classPanel.transform, Vector2.zero, new Vector2(180, 68), "BACK", 24,
                btnGray, icoBack, () => MatchManager.I?.BackToMenu())
                .GetComponent<RectTransform>().SetAnchor(new Vector2(0f, 1f), new Vector2(126, -60));

            champCardPos.Clear();
            for (int i = 0; i < 4; i++)
            {
                int idx = i;
                var kit = ClassKits.Get((ClassId)i);
                var pos = new Vector2(-540 + i * 360, -66);
                champCardPos.Add(pos);
                var card = Img("Class" + i, classPanel.transform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                    new Vector2(0.5f, 0.5f), pos, new Vector2(300, 350), CardFor(kit.id), Color.white, true);
                card.type = Image.Type.Simple;
                MakeClickable(card, () => { MatchManager.I?.SelectChampion(idx); RefreshHub(); });

                Icon("Ico", card.transform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                    new Vector2(0, 90), new Vector2(112, 112), IconFor(kit.id), Color.white);
                Txt("N", card.transform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                    new Vector2(0, 10), new Vector2(280, 58), kit.displayName.ToUpper(), fontMid, 34, Color.white);
                var blurb = Txt("B", card.transform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                    new Vector2(0.5f, 0.5f), new Vector2(0, -54), new Vector2(250, 96), kit.blurb, fontSmall, 20,
                    new Color(0.93f, 0.93f, 0.99f));
                blurb.enableWordWrapping = true;
                Txt("S", card.transform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                    new Vector2(0, -118), new Vector2(240, 38),
                    $"HP {kit.maxHealth:0}  ·  DMG {kit.lightDamage:0}", fontSmall, 18,
                    new Color(1f, 0.92f, 0.65f));
            }

            // gold focus glow hops onto whichever card is the current pick
            var focus = Icon("Focus", classPanel.transform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                new Vector2(0.5f, 0.5f), champCardPos[0], new Vector2(344, 398), focusFrame, Gold);
            focus.preserveAspect = false;
            champFocus = focus.rectTransform;

            Txt("Hint", classPanel.transform, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0.5f, 0f),
                new Vector2(0, 74), new Vector2(1200, 34), "pick your champion  ·  they take the podium and fight for you",
                fontSmall, 20, new Color(0.85f, 0.88f, 0.95f));
            string controls = Application.isMobilePlatform
                ? "left thumb: move  ·  right drag: camera  ·  ATTACK tap = light, hold = heavy  ·  DODGE tap = roll, hold = sprint"
                : "WASD move  ·  LMB attack (hold = heavy)  ·  RMB block / heavy  ·  SPACE roll  ·  SHIFT sprint  ·  Q lock-on  ·  F1 autopilot";
            Txt("Controls", classPanel.transform, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0.5f, 0f),
                new Vector2(0, 38), new Vector2(1700, 34), controls,
                fontSmall, 18, new Color(0.72f, 0.75f, 0.83f));
        }

        // ================================================================ toast

        TMP_Text toastText;
        Image toastPlate;
        Coroutine toastRoutine;

        void BuildToast()
        {
            toastPlate = Img("Toast", root, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0.5f, 1f),
                new Vector2(0, -200), new Vector2(560, 64), plateRound, new Color(0.05f, 0.055f, 0.1f, 0.92f));
            toastText = Txt("T", toastPlate.transform, Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f),
                new Vector2(0, 1), new Vector2(-30, -10), "", fontMid, 26, Gold);
            toastPlate.gameObject.SetActive(false);
        }

        public void ShowToast(string msg)
        {
            if (toastRoutine != null) StopCoroutine(toastRoutine);
            toastRoutine = StartCoroutine(ToastRoutine(msg));
        }

        System.Collections.IEnumerator ToastRoutine(string msg)
        {
            toastText.text = msg;
            toastPlate.gameObject.SetActive(true);
            float t = 0f;
            const float life = 1.9f;
            while (t < life)
            {
                t += Time.unscaledDeltaTime;
                float a = Mathf.Clamp01(t / 0.15f);
                if (t > life - 0.35f) a = Mathf.Clamp01((life - t) / 0.35f);
                toastPlate.color = new Color(0.05f, 0.055f, 0.1f, 0.92f * a);
                toastText.alpha = a;
                yield return null;
            }
            toastPlate.gameObject.SetActive(false);
        }
    }
}
