using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using Crownfall.UI;

namespace Crownfall
{
    /// Menu meta modals: shop, inbox, free gift, settings (with logout) and the
    /// battle mode chooser that launches the arena scene.
    public partial class MenuHud
    {
        readonly List<(int idx, GameObject dot)> newsDots = new List<(int, GameObject)>();
        readonly List<(Button btn, int cost, TMP_Text label)> shopBuyButtons =
            new List<(Button, int, TMP_Text)>();
        TMP_Text giftStateText, giftRewardText;
        GameObject giftOpenBtn;
        RectTransform giftChestRect;
        Image shakeSwitchBg, shakeKnob;
        GameObject logoutBtn;

        // ================================================================ shop

        void BuildShop()
        {
            var frame = ModalShell("Shop", new Vector2(960, 830), out shopModal);

            ShopCard(frame, new Vector2(-300, 88), "COIN STASH", cardShopBlue, shopCoinSmall, "+150", 10,
                () => { CrownfallMeta.AddCoins(150); ShowToast("+150 COINS"); });
            ShopCard(frame, new Vector2(0, 88), "GOLD POUCH", cardShopYellow, shopCoinBig, "+400", 24,
                () => { CrownfallMeta.AddCoins(400); ShowToast("+400 COINS"); });
            ShopCard(frame, new Vector2(300, 88), "GOLDEN CHEST", cardShopPurple, shopChest, "40-220?", 16,
                () =>
                {
                    int roll = Random.Range(40, 221);
                    CrownfallMeta.AddCoins(roll);
                    ShowToast($"CHEST PAID OUT  +{roll} COINS");
                });

            // -- sigil rack: coin-priced profile cosmetics
            Icon("SigDivL", frame, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                new Vector2(-210, -136), new Vector2(130, 20), dividerL, new Color(1f, 1f, 1f, 0.6f));
            Txt("SigTitle", frame, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                new Vector2(0, -136), new Vector2(240, 34), "SIGILS", fontMid, 26, Gold);
            Icon("SigDivR", frame, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                new Vector2(210, -136), new Vector2(130, 20), dividerR, new Color(1f, 1f, 1f, 0.6f));

            var cat = SigilCatalog;
            var sigilFrames = new[] { itemBlue, itemGreen, itemPurple, itemRed, itemYellow };
            for (int i = 1; i < cat.Length; i++)   // 0 = free class default, not sold
            {
                float x = -300 + (i - 1) * 150;
                SigilCard(frame, new Vector2(x, -252), i, cat[i].name, cat[i].cost,
                    sigilFrames[(i - 1) % sigilFrames.Length]);
            }

            Txt("Hint", frame, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0.5f, 0f),
                new Vector2(0, 18), new Vector2(760, 28), "sigils crown your profile  ·  earn gems from level-ups and gifts",
                fontSmall, 16, new Color(1f, 1f, 1f, 0.55f));
        }

        readonly List<(Button btn, TMP_Text label, int index, int cost)> sigilButtons =
            new List<(Button, TMP_Text, int, int)>();

        void SigilCard(Transform parent, Vector2 pos, int index, string name, int cost, Sprite itemFrame)
        {
            // designed rarity-colored ItemFrame slot instead of a tinted plate
            var card = Img("Sigil_" + name, parent, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                new Vector2(0.5f, 0.5f), pos, new Vector2(140, 196), itemFrame, Color.white);
            Glow("G", card.transform, new Vector2(0, 34), 96, new Color(1f, 0.9f, 0.5f, 0.25f));
            Icon("I", card.transform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                new Vector2(0, 34), new Vector2(64, 64), SigilSprite(index), Color.white);
            Txt("N", card.transform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                new Vector2(0, -22), new Vector2(134, 26), name, fontSmall, 15, Color.white);

            var buy = Img("Buy", card.transform, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f),
                new Vector2(0.5f, 0f), new Vector2(0, 12), new Vector2(118, 48), btnGreen, Color.white);
            TMP_Text label = null;
            var b = MakeClickable(buy, () =>
            {
                if (CrownfallMeta.OwnsSigil(index))
                {
                    CrownfallMeta.EquippedSigil = index;
                    ShowToast(name + "  EQUIPPED");
                }
                else if (CrownfallMeta.UnlockSigil(index, cost))
                {
                    CrownfallMeta.EquippedSigil = index;
                    Burst(fxSparklePrefab, (RectTransform)card.transform, Vector2.zero, 0.7f);
                    GameEffects.I?.PlayUi(GameEffects.I.killDing, 0.5f);
                    ShowToast(name + "  UNLOCKED");
                }
                else ShowToast("NOT ENOUGH COINS");
            });
            label = Txt("P", buy.transform, Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f),
                new Vector2(0, 2), new Vector2(-10, -8), cost.ToString(), fontSmall, 17, Color.white);
            sigilButtons.Add((b, label, index, cost));
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
            foreach (var (btn, label, index, cost) in sigilButtons)
            {
                bool owned = CrownfallMeta.OwnsSigil(index);
                bool equipped = owned && CrownfallMeta.EquippedSigil == index;
                bool can = owned || CrownfallMeta.Coins >= cost;
                btn.interactable = !equipped && can;
                label.text = equipped ? "ON" : owned ? "EQUIP" : cost.ToString();
                label.color = equipped ? Gold : can ? Color.white : new Color(1f, 1f, 1f, 0.45f);
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
                // designed navy list rows — the pack's ListFrame02
                var row = Img("News" + i, frame, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f),
                    new Vector2(0.5f, 1f), new Vector2(0, -100 - i * 146), new Vector2(670, 130),
                    rowNavy, Color.white);
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

        // ================================================================ quests

        class QuestRowUi
        {
            public Image fill;
            public TMP_Text progress;
            public Button claimBtn;
            public TMP_Text claimLabel;
        }
        readonly List<QuestRowUi> questRows = new List<QuestRowUi>();

        /// Daily quests, following the pack's Missions screen: designed rows
        /// with an item-frame icon socket, progress tube and claim button.
        void BuildQuests()
        {
            var frame = ModalShell("Quests", new Vector2(840, 680), out questModal);

            var frames = new[] { itemBlue, itemPurple, itemYellow };
            var icons = new[] { icoPlay, icoTrophyBig, icoBattleSword };
            questRows.Clear();
            for (int i = 0; i < CrownfallQuests.Defs.Length; i++)
            {
                int idx = i;
                var q = CrownfallQuests.Defs[i];
                var ui = new QuestRowUi();
                var row = Img("Quest_" + q.id, frame, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f),
                    new Vector2(0.5f, 1f), new Vector2(0, -108 - i * 168), new Vector2(730, 152),
                    rowNavy, Color.white);

                var slot = Img("Slot", row.transform, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f),
                    new Vector2(0f, 0.5f), new Vector2(16, 0), new Vector2(122, 128), frames[i % frames.Length],
                    Color.white);
                Icon("I", slot.transform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                    new Vector2(0.5f, 0.5f), new Vector2(0, 6), new Vector2(64, 64), icons[i % icons.Length],
                    Color.white);

                Txt("T", row.transform, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(0f, 1f),
                    new Vector2(154, -16), new Vector2(360, 32), q.title, fontSmall, 24, Color.white,
                    TextAlignmentOptions.Left);
                Txt("D", row.transform, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(0f, 1f),
                    new Vector2(154, -50), new Vector2(360, 26), q.desc, fontSmall, 17,
                    new Color(1f, 1f, 1f, 0.65f), TextAlignmentOptions.Left);

                var barBg = Img("PB", row.transform, new Vector2(0f, 0f), new Vector2(0f, 0f),
                    new Vector2(0f, 0f), new Vector2(154, 22), new Vector2(250, 20), bar4Bg, Color.white);
                ui.fill = MakeFill(barBg.rectTransform, bar4FillGreen, Color.white, new Vector2(250, 20));
                ui.progress = Txt("PT", row.transform, new Vector2(0f, 0f), new Vector2(0f, 0f),
                    new Vector2(0f, 0f), new Vector2(414, 20), new Vector2(90, 26), "0/0", fontSmall, 17,
                    new Color(1f, 1f, 1f, 0.8f), TextAlignmentOptions.Left);

                // reward stack, right side above the claim button
                float rx = -104;
                Icon("RC", row.transform, new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(0.5f, 0.5f),
                    new Vector2(rx - 36, -30), new Vector2(34, 34), iconCoinBig, Color.white);
                Txt("RCt", row.transform, new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(0f, 0.5f),
                    new Vector2(rx - 12, -30), new Vector2(90, 30), "+" + q.coins, fontSmall, 20, Gold,
                    TextAlignmentOptions.Left);
                if (q.gems > 0)
                {
                    Icon("RG", row.transform, new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(0.5f, 0.5f),
                        new Vector2(rx - 36, -64), new Vector2(30, 30), icoGemGold, Color.white);
                    Txt("RGt", row.transform, new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(0f, 0.5f),
                        new Vector2(rx - 12, -64), new Vector2(90, 28), "+" + q.gems, fontSmall, 18,
                        new Color(0.8f, 0.9f, 1f), TextAlignmentOptions.Left);
                }

                var claim = Img("Claim", row.transform, new Vector2(1f, 0f), new Vector2(1f, 0f),
                    new Vector2(1f, 0f), new Vector2(-18, 16), new Vector2(150, 58), btnGreen, Color.white);
                ui.claimBtn = MakeClickable(claim, () =>
                {
                    var def = CrownfallQuests.Defs[idx];
                    if (CrownfallQuests.Claim(def))
                    {
                        Burst(fxSparklePrefab, (RectTransform)row.transform, Vector2.zero, 0.8f);
                        GameEffects.I?.PlayUi(GameEffects.I.killDing, 0.5f);
                        ShowToast(def.gems > 0 ? $"+{def.coins} COINS  +{def.gems} GEMS" : $"+{def.coins} COINS");
                        RefreshQuests();
                        RefreshHub();
                    }
                });
                ui.claimLabel = Txt("L", claim.transform, Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f),
                    new Vector2(0, 2), new Vector2(-14, -10), "CLAIM", fontSmall, 20, Color.white);
                questRows.Add(ui);

                var rowRect = row.rectTransform;
                int stagger = i;
                questModal.OnShow += () => UiTween.SlideIn(rowRect, new Vector2(90f, 0f), 0.3f,
                    UiTween.Ease.CubicOut, 0.06f * stagger);
            }

            Txt("Hint", frame, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0.5f, 0f),
                new Vector2(0, 20), new Vector2(720, 30), "new quests every day  ·  demo matches don't count",
                fontSmall, 17, new Color(1f, 1f, 1f, 0.65f));

            questModal.OnShow += RefreshQuests;
        }

        void RefreshQuests()
        {
            for (int i = 0; i < questRows.Count && i < CrownfallQuests.Defs.Length; i++)
            {
                var q = CrownfallQuests.Defs[i];
                var ui = questRows[i];
                int p = Mathf.Min(CrownfallQuests.Progress(q.id), q.target);
                ui.fill.fillAmount = q.target > 0 ? (float)p / q.target : 0f;
                ui.progress.text = $"{p}/{q.target}";
                bool claimed = CrownfallQuests.IsClaimed(q.id);
                bool can = CrownfallQuests.CanClaim(q);
                ui.claimBtn.interactable = can;
                ui.claimLabel.text = claimed ? "DONE" : "CLAIM";
                ui.claimLabel.color = claimed ? new Color(1f, 1f, 1f, 0.55f) : Color.white;
            }
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

            // logout returns to the login screen
            logoutBtn = MenuButton(frame, new Vector2(0, -268), new Vector2(260, 66), "LOG OUT", 22,
                btnRed, icoPower, () =>
                {
                    CrownfallMeta.Logout();
                    router.CloseAllModals();
                    ShowMenuLayer();
                    ShowToast("LOGGED OUT");
                }).gameObject;
        }

        // ================================================================ battle

        RectTransform modeFocusGlow;
        readonly List<Vector2> modeCardPos = new List<Vector2>();
        readonly List<RectTransform> modeCards = new List<RectTransform>();

        /// Event chooser: one designed rarity-colored card per game mode —
        /// the same face the hub's event card wears — with every icon and line
        /// contained inside the card artwork. Gold glow + scale marks the pick.
        void BuildBattleModal()
        {
            var frame = ModalShell("Battle", new Vector2(900, 700), out battleModal);

            Txt("Pick", frame, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0.5f, 1f),
                new Vector2(0, -92), new Vector2(600, 30), "CHOOSE YOUR EVENT", fontSmall, 19,
                new Color(1f, 1f, 1f, 0.65f));

            var glowImg = Icon("FocusGlow", frame, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(330, 330), card3Glow, Gold);
            glowImg.raycastTarget = false;
            modeFocusGlow = glowImg.rectTransform;

            modeCardPos.Clear();
            modeCards.Clear();
            for (int i = 0; i < GameModes.All.Length && i < 3; i++)
            {
                int idx = i;
                var mode = GameModes.All[i];
                var pos = new Vector2(-272 + i * 272, 56);
                modeCardPos.Add(pos);
                var card = Img("Mode" + i, frame, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                    new Vector2(0.5f, 0.5f), pos, new Vector2(238, 300), ModeFace(i), Color.white, true);
                modeCards.Add(card.rectTransform);
                MakeClickable(card, () =>
                {
                    CrownfallMeta.SelectedMode = idx;
                    RefreshModeFocus();
                    Burst(fxSparklePrefab, card.rectTransform, Vector2.zero, 0.6f);
                });
                Glow("G", card.transform, new Vector2(0, 62), 140, new Color(1f, 1f, 1f, 0.25f));
                Icon("I", card.transform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                    new Vector2(0.5f, 0.5f), new Vector2(0, 62), new Vector2(88, 88), ModeIcon(i), Color.white);
                var name = Txt("N", card.transform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                    new Vector2(0.5f, 0.5f), new Vector2(0, -22), new Vector2(196, 60), mode.title,
                    fontMid, 22, Color.white);
                name.enableAutoSizing = true;
                name.fontSizeMax = 22;
                name.fontSizeMin = 14;
                Txt("T", card.transform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                    new Vector2(0.5f, 0.5f), new Vector2(0, -72), new Vector2(196, 28),
                    $"FIRST TO {mode.killTarget}", fontSmall, 17, Gold);
                Txt("D", card.transform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                    new Vector2(0.5f, 0.5f), new Vector2(0, -100), new Vector2(196, 26),
                    $"3v3  ·  {(int)mode.duration / 60}:{(int)mode.duration % 60:00}", fontSmall, 14,
                    new Color(1f, 1f, 1f, 0.7f));
            }

            MenuButton(frame, new Vector2(-190, -232), new Vector2(350, 100), "ONLINE", 34,
                btnYellow, icoPlay, LaunchOnline);
            MenuButton(frame, new Vector2(190, -232), new Vector2(350, 100), "VS AI", 34,
                btnBlue, icoSword, LaunchOffline);

            battleModal.OnShow += RefreshModeFocus;
        }

        void RefreshModeFocus()
        {
            if (modeFocusGlow == null || modeCardPos.Count == 0) return;
            int idx = Mathf.Clamp(CrownfallMeta.SelectedMode, 0, modeCardPos.Count - 1);
            modeFocusGlow.anchoredPosition = modeCardPos[idx];
            for (int i = 0; i < modeCards.Count; i++)
                modeCards[i].localScale = Vector3.one * (i == idx ? 1.06f : 1f);
            UiTween.Punch(modeFocusGlow, 0.1f, 0.3f);
        }
    }
}
