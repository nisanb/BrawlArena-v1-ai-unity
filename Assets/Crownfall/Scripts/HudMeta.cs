using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using Crownfall.UI;

namespace Crownfall
{
    /// Meta-game modals: shop, inbox, free gift, settings (with logout),
    /// the battle mode chooser and the pause menu.
    public partial class HUDController
    {
        readonly List<(int idx, GameObject dot)> newsDots = new List<(int, GameObject)>();
        readonly List<(Button btn, int cost, TMP_Text label)> shopBuyButtons =
            new List<(Button, int, TMP_Text)>();
        TMP_Text giftStateText, giftRewardText;
        GameObject giftOpenBtn;
        RectTransform giftChestRect;
        Image shakeSwitchBg, shakeKnob;
        GameObject pauseBtn;
        GameObject logoutBtn;

        // ================================================================ shop

        void BuildShop()
        {
            var frame = ModalShell("Shop", new Vector2(920, 600), out shopModal);

            ShopCard(frame, new Vector2(-290, -30), "COIN STASH", cardShopBlue, shopCoinSmall, "+150", 10,
                () => { CrownfallMeta.AddCoins(150); ShowToast("+150 COINS"); });
            ShopCard(frame, new Vector2(0, -30), "GOLD POUCH", cardShopYellow, shopCoinBig, "+400", 24,
                () => { CrownfallMeta.AddCoins(400); ShowToast("+400 COINS"); });
            ShopCard(frame, new Vector2(290, -30), "GOLDEN CHEST", cardShopPurple, shopChest, "40-220?", 16,
                () =>
                {
                    int roll = Random.Range(40, 221);
                    CrownfallMeta.AddCoins(roll);
                    ShowToast($"CHEST PAID OUT  +{roll} COINS");
                });

            Txt("Hint", frame, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0.5f, 0f),
                new Vector2(0, 22), new Vector2(700, 30), "earn gems from level-ups and free gifts",
                fontSmall, 18, new Color(1f, 1f, 1f, 0.55f));
        }

        void ShopCard(Transform parent, Vector2 pos, string name, Sprite cardBg, Sprite icon, string amount,
            int gemCost, UnityEngine.Events.UnityAction grant)
        {
            var card = Img("Item_" + name, parent, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                new Vector2(0.5f, 0.5f), pos, new Vector2(262, 360), cardBg, Color.white);
            card.type = Image.Type.Simple;
            Txt("N", card.transform, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0.5f, 1f),
                new Vector2(0, -18), new Vector2(240, 32), name, fontSmall, 22, Gold);
            Icon("I", card.transform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                new Vector2(0, 34), new Vector2(150, 150), icon, Color.white);
            Txt("A", card.transform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                new Vector2(0, -66), new Vector2(220, 34), amount, fontMid, 27, Color.white);

            var buy = Img("Buy", card.transform, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f),
                new Vector2(0.5f, 0f), new Vector2(0, 20), new Vector2(196, 62), btnGreen, Color.white);
            var b = MakeClickable(buy, () =>
            {
                if (CrownfallMeta.SpendGems(gemCost))
                {
                    grant();
                    Burst(fxSparklePrefab, card.rectTransform, Vector2.zero, 0.8f);
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

        void RefreshShopAffordability()
        {
            foreach (var (btn, cost, label) in shopBuyButtons)
            {
                bool can = CrownfallMeta.Gems >= cost;
                btn.interactable = can;
                label.color = can ? Color.white : new Color(1f, 1f, 1f, 0.45f);
            }
            foreach (var (idx, dot) in newsDots)
                dot.SetActive(!CrownfallMeta.IsNewsRead(idx));
        }

        // ================================================================ inbox

        void BuildInbox()
        {
            var frame = ModalShell("Inbox", new Vector2(780, 620), out inboxModal);

            for (int i = 0; i < CrownfallMeta.News.Length; i++)
            {
                int idx = i;
                var (title, body) = CrownfallMeta.News[i];
                var row = Img("News" + i, frame, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f),
                    new Vector2(0.5f, 1f), new Vector2(0, -100 - i * 146), new Vector2(670, 130),
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

                var rowRect = row.rectTransform;
                int stagger = i;
                inboxModal.OnShow += () => UiTween.SlideIn(rowRect, new Vector2(90f, 0f), 0.3f,
                    UiTween.Ease.CubicOut, 0.06f * stagger);
            }
        }

        // ================================================================ gifts

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
            var frame = ModalShell("Free Gift", new Vector2(580, 580), out giftModal);

            var chest = Icon("Chest", frame, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0.5f, 1f),
                new Vector2(0, -92), new Vector2(230, 196), iconChestGold, Color.white);
            giftChestRect = chest.rectTransform;
            giftStateText = Txt("State", frame, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                new Vector2(0.5f, 0.5f), new Vector2(0, -60), new Vector2(500, 40), "", fontSmall, 24,
                Color.white);
            giftRewardText = Txt("Reward", frame, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                new Vector2(0.5f, 0.5f), new Vector2(0, -106), new Vector2(500, 44), "", fontMid, 30, Gold);
            giftRewardText.gameObject.SetActive(false);

            giftOpenBtn = MenuButton(frame, new Vector2(0, -190), new Vector2(280, 84),
                "OPEN", 32, btnGreen, null, () =>
                {
                    var (c, g) = CrownfallMeta.ClaimGift();
                    if (c <= 0) { RefreshGift(); return; }
                    giftRewardText.text = g > 0 ? $"+{c} COINS   +{g} GEMS" : $"+{c} COINS";
                    giftRewardText.gameObject.SetActive(true);
                    UiTween.PopIn(giftRewardText.rectTransform, 0.35f);
                    UiTween.Punch(giftChestRect, 0.3f, 0.4f);
                    Burst(fxConfettiPrefab, giftChestRect, Vector2.zero, 0.9f);
                    PlaySting();
                    RefreshGift();
                    RefreshHub();
                }).gameObject;

            giftModal.OnShow += () =>
            {
                giftRewardText.gameObject.SetActive(false);
                RefreshGift();
                UiTween.StopLoop(giftChestRect);
                UiTween.BobForever(giftChestRect, 7f, 1.8f);
            };
            giftModal.OnHide += () => UiTween.StopLoop(giftChestRect);
        }

        // ================================================================ settings

        void UpdateShakeSwitch()
        {
            bool on = CrownfallSettings.ShakeEnabled;
            shakeSwitchBg.sprite = on ? switchOn : switchOff;
            shakeKnob.sprite = on ? knobOn : knobOff;
            shakeKnob.rectTransform.anchoredPosition = new Vector2(on ? 28f : -28f, 3f);
        }

        void BuildSettings()
        {
            var frame = ModalShell("Settings", new Vector2(720, 680), out settingsModal);

            Icon("VolIco", frame, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                new Vector2(-260, 168), new Vector2(36, 36), icoVolume, Color.white);
            Txt("VolL", frame, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                new Vector2(-10, 168), new Vector2(440, 40), "VOLUME", fontSmall, 24, Color.white,
                TextAlignmentOptions.Left);
            MakeSlider(frame, new Vector2(0, 122), 520, 0f, 1f, CrownfallSettings.Volume, v =>
            {
                CrownfallSettings.Volume = v;
                CrownfallSettings.Apply();
                CrownfallSettings.Save();
            });

            Icon("SensIco", frame, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                new Vector2(-260, 56), new Vector2(36, 36), icoCamera, Color.white);
            Txt("SensL", frame, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                new Vector2(-10, 56), new Vector2(440, 40), "CAMERA SENSITIVITY", fontSmall, 24, Color.white,
                TextAlignmentOptions.Left);
            MakeSlider(frame, new Vector2(0, 10), 520, 0.4f, 2f, CrownfallSettings.Sensitivity, v =>
            {
                CrownfallSettings.Sensitivity = v;
                CrownfallSettings.Save();
            });

            Icon("ShakeIco", frame, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                new Vector2(-260, -64), new Vector2(36, 36), icoShake, Color.white);
            Txt("ShakeL", frame, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                new Vector2(-52, -64), new Vector2(356, 40), "SCREEN SHAKE", fontSmall, 24, Color.white,
                TextAlignmentOptions.Left);
            shakeSwitchBg = Img("ShakeSwitch", frame, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                new Vector2(0.5f, 0.5f), new Vector2(212, -64), new Vector2(112, 54), switchOn, Color.white, true);
            shakeKnob = Icon("Knob", shakeSwitchBg.transform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                new Vector2(0.5f, 0.5f), new Vector2(28, 3), new Vector2(48, 48), knobOn, Color.white);
            shakeSwitchBg.gameObject.AddComponent<Button>().onClick.AddListener(() =>
            {
                CrownfallSettings.ShakeEnabled = !CrownfallSettings.ShakeEnabled;
                CrownfallSettings.Save();
                UpdateShakeSwitch();
            });
            UpdateShakeSwitch();

            MenuButton(frame, new Vector2(0, -170), new Vector2(300, 84), "CLOSE", 30,
                btnGreen, icoCheck, () => router.CloseModal(settingsModal));

            // logout returns to the login screen; only offered from the hub
            logoutBtn = MenuButton(frame, new Vector2(0, -268), new Vector2(260, 66), "LOG OUT", 22,
                btnRed, icoPower, () =>
                {
                    CrownfallMeta.Logout();
                    router.CloseAllModals();
                    ShowMenuLayer();
                    ShowToast("LOGGED OUT");
                }).gameObject;

            settingsModal.OnShow += () =>
                logoutBtn.SetActive(MatchManager.I == null || MatchManager.I.State == MatchState.Menu);
        }

        // ================================================================ battle

        void BuildBattleModal()
        {
            var frame = ModalShell("Battle", new Vector2(680, 500), out battleModal);

            Txt("Sub", frame, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0.5f, 1f),
                new Vector2(0, -96), new Vector2(520, 34), "Sundered Crown  ·  3 v 3  ·  first to 10",
                fontSmall, 20, new Color(1f, 0.9f, 0.6f));

            MenuButton(frame, new Vector2(0, 6), new Vector2(440, 108), "ONLINE MATCH", 38,
                btnYellow, icoPlay, () =>
                {
                    router.CloseModal(battleModal);
                    OpenOnlinePanel();
                });

            MenuButton(frame, new Vector2(0, -126), new Vector2(440, 92), "VS AI", 32,
                btnBlue, icoSword, () =>
                {
                    router.CloseModal(battleModal);
                    MatchManager.I?.StartMatch();
                });
        }

        // ================================================================ pause

        void BuildPause()
        {
            var btnImg = Icon("PauseBtn", root, Vector2.one, Vector2.one, Vector2.one,
                new Vector2(-24, -22), new Vector2(68, 68), btnCircle, Color.white);
            MakeClickable(btnImg, () => MatchManager.I?.TogglePause());
            Icon("L", btnImg.transform, Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f),
                new Vector2(0, 2), new Vector2(26, 26), icoPause, Color.white);
            pauseBtn = btnImg.gameObject;
            pauseBtn.SetActive(false);

            var frame = ModalShell("Paused", new Vector2(480, 380), out pauseModal, closable: false);
            MenuButton(frame, new Vector2(0, 26), new Vector2(350, 92), "RESUME", 32,
                btnGreen, icoPlay, () => MatchManager.I?.TogglePause());
            MenuButton(frame, new Vector2(0, -88), new Vector2(350, 84), "QUIT MATCH", 26,
                btnRed, icoHome, () => MatchManager.I?.Restart());
        }
    }
}
