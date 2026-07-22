using System.Collections;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using Crownfall.UI;

namespace Crownfall
{
    /// The whole game UI, rebuilt 2026-07-22 on the Crownfall.UI framework:
    /// UiRouter screen stack + UiTween juice engine. This core file owns the
    /// forge-wired theme (sprites/fonts), the canvas, the widget kit and the
    /// MatchState → screen routing. Screens live in the Hud*.cs partials:
    ///   HudHome.cs      — boot splash, login, home hub
    ///   HudChampions.cs — champion select
    ///   HudFight.cs     — fight HUD (bars, feed, announce, target frame)
    ///   HudMeta.cs      — shop / inbox / gift / settings / battle / pause modals
    ///   HudOnline.cs    — matchmaking lobby + fight ping
    ///   HudResult.cs    — end-of-match ceremony
    public partial class HUDController : MonoBehaviour
    {
        [Header("Wired by forge — fonts")]
        public TMP_FontAsset fontBig;
        public TMP_FontAsset fontMid;
        public TMP_FontAsset fontSmall;

        [Header("Wired by forge — bars")]
        public Sprite barBgBasic;
        public Sprite barFillBasic;
        public Sprite bar4Bg;
        public Sprite bar4FillRed;
        public Sprite bar4FillWhite;
        public Sprite bar4Divider;
        public Sprite bar4Gloss;

        [Header("Wired by forge — frames & plates")]
        public Sprite frameRound;
        public Sprite frameCircle;
        public Sprite bannerNavy;
        public Sprite plateRound;
        public Sprite popupNavy;
        public Sprite ribbonBlue;
        public Sprite ribbonOrange;
        public Sprite ribbonYellow;
        public Sprite cardKnight;
        public Sprite cardWarbrand;
        public Sprite cardDuelist;
        public Sprite cardMage;
        public Sprite profileRing;
        public Sprite profileInner;
        public Sprite trapBlue;
        public Sprite trapOrange;

        [Header("Wired by forge — buttons")]
        public Sprite btnGreen;
        public Sprite btnBlue;
        public Sprite btnYellow;
        public Sprite btnRed;
        public Sprite btnGray;
        public Sprite btnCircle;

        [Header("Wired by forge — switch")]
        public Sprite switchOn;
        public Sprite switchOff;
        public Sprite knobOn;
        public Sprite knobOff;
        public Sprite knobWhite;

        [Header("Wired by forge — icons")]
        public Sprite iconCrown;
        public Sprite icoShield;
        public Sprite icoAxe;
        public Sprite icoSword;
        public Sprite icoWand;
        public Sprite icoSkill;
        public Sprite icoPlay;
        public Sprite icoMovie;
        public Sprite icoGear;
        public Sprite icoPower;
        public Sprite icoPause;
        public Sprite icoHome;
        public Sprite icoRefresh;
        public Sprite icoTarget;
        public Sprite icoSkull;
        public Sprite icoTimer;
        public Sprite icoVolume;
        public Sprite icoCamera;
        public Sprite icoShake;
        public Sprite icoClose;
        public Sprite icoCheck;
        public Sprite icoBack;

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

        [Header("Wired by forge — redesign 2026-07")]
        public Sprite bgLayer1;          // Background_09_Purple1 (full-screen backdrop)
        public Sprite bgGlowTop;         // Background_09_Purple3_GlowTop
        public Sprite bgGlowBottom;      // Background_09_Purple4_GlowBottom
        public Sprite dimNavy;           // Background_ScreenDimed_Navy
        public Sprite dimBlack;          // Background_ScreenDimed_Black
        public Sprite screenGlow;        // Background_ScreenGlow
        public Sprite slideNavy;         // Popup_Slide02_Single_Navy (modal frame)
        public Sprite slideTopBar;       // Popup_Slide02_Single_Navy_TopBar
        public Sprite slideTopGlow;      // Popup_Slide02_Single_Navy_TopGlow
        public Sprite panelNavy;         // PanelFrame02_Round_Single_Navy
        public Sprite cardChampBlue;     // CardFrame08_Single_Blue
        public Sprite cardChampPurple;   // CardFrame08_Single_Purple
        public Sprite cardChampFocus;    // CardFrame08_Focus
        public Sprite cardChampGlow;     // CardFrame08_Glow_1
        public Sprite cardEventBg;       // CardFrame06_Bg_Blue
        public Sprite cardShopBlue;      // CardFrame06_Bg_Blue
        public Sprite cardShopYellow;    // CardFrame06_Bg_Yellow
        public Sprite cardShopPurple;    // CardFrame06_Bg_Purple
        public Sprite inputBg;           // InputField01_Bg_n
        public Sprite icoAccount;        // InputField_Icon_Account
        public Sprite flagPurple;        // Title_Flag01_Purple
        public Sprite flagBlue;          // Title_Flag01_Blue
        public Sprite dividerL;          // Title_Line_Divider_Left
        public Sprite dividerR;          // Title_Line_Divider_Right
        public Sprite lvlBg;             // Slider_Level02_Bg
        public Sprite lvlFill;           // Slider_Level02_Fill_Blue
        public Sprite fxGlow;            // fx_glow
        public Sprite fxCircleGlow;      // fx_circle_glow
        public Sprite fxRays;            // fx_rotate_line
        public Sprite fxStar;            // fx_star_yellow
        public Sprite icoStatHp;         // Icon_StatsIcon_Hp01
        public Sprite icoStatDmg;        // Icon_StatsIcon_Damage
        public Sprite icoStatSpd;        // Icon_StatsIcon_Speed
        public Sprite icoTrophyBig;      // Icon_ImageIcon_Trophy_l
        public Sprite icoStar;           // Icon_ImageIcon_Star01_l
        public Sprite shopCoinSmall;     // ShopItem_Coin_2
        public Sprite shopCoinBig;       // ShopItem_Coin_4
        public Sprite shopChest;         // ShopItem_SpecialChest_Purple
        public Sprite icoMedalGold;      // Icon_ImageIcon_Medal_Gold
        public Sprite icoGemGold;        // Icon_ImageIcon_GemGold
        public GameObject fxSparklePrefab;   // Fx_Sparkle_Star01_CustomColor_Yellow
        public GameObject fxConfettiPrefab;  // Fx_Spread_Star01
        public GameObject fxRotateLightPrefab; // Fx_Rotate_Light01

        internal static readonly Color Gold = new Color(1f, 0.85f, 0.35f);
        internal static readonly Color AzureCol = new Color(0.35f, 0.65f, 1f);
        internal static readonly Color CrimsonCol = new Color(1f, 0.36f, 0.3f);
        internal static readonly Color PlateDark = new Color(0.05f, 0.055f, 0.1f, 0.78f);
        internal static readonly Color InkDark = new Color(0.07f, 0.08f, 0.14f, 0.95f);

        Canvas canvas;
        RectTransform root;

        readonly UiRouter router = new UiRouter();
        UiPanel bootScreen, loginScreen, hubScreen, champScreen, fightScreen;
        UiPanel battleModal, onlineModal, shopModal, inboxModal, giftModal,
            settingsModal, pauseModal, resultModal;

        static bool bootPlayed; // splash only once per app run, not on every hub return

        Sprite IconFor(ClassId id) => id switch
        {
            ClassId.Knight => icoShield,
            ClassId.Greatsword => icoAxe,
            ClassId.Duelist => icoSword,
            _ => icoWand,
        };

        /// Cosmetic sigil catalog sold in the shop. Index 0 = the free class
        /// icon default; the order is persisted in PlayerPrefs, never reorder.
        internal (string name, Sprite sprite, int cost)[] SigilCatalog => new[]
        {
            ("CLASS", IconFor((ClassId)CrownfallMeta.SelectedClass), 0),
            ("CROWN", iconCrown, 250),
            ("STAR", icoStar, 150),
            ("MEDAL", icoMedalGold, 400),
            ("SKULL", icoSkull, 300),
            ("GOLD GEM", icoGemGold, 600),
        };

        internal Sprite SigilSprite(int index)
        {
            var cat = SigilCatalog;
            return cat[Mathf.Clamp(index, 0, cat.Length - 1)].sprite;
        }

        void Start()
        {
            BuildCanvas();
            BuildFightHud();
            BuildBoot();
            BuildLogin();
            BuildHomeHub();
            BuildChampions();
            BuildSettings();
            BuildPause();
            BuildResult();
            BuildShop();
            BuildInbox();
            BuildGift();
            BuildBattleModal();
            BuildOnlinePanel();
            BuildToast();

            CrownfallMeta.Changed += RefreshHub;
            RefreshHub();

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
            }
        }

        void OnDestroy()
        {
            // static event — must detach or scene reloads leak dead handlers
            CrownfallMeta.Changed -= RefreshHub;
        }

        // ================================================================ routing

        void OnStateChanged(MatchState s)
        {
            router.CloseAllModals();
            switch (s)
            {
                case MatchState.Menu:
                    ShowMenuLayer();
                    break;
                case MatchState.ClassSelect:
                    router.Show(champScreen);
                    break;
                case MatchState.Countdown:
                case MatchState.Fighting:
                case MatchState.Ended:
                    if (router.Current != fightScreen) router.Show(fightScreen);
                    if (s != MatchState.Ended) BindPlayer();
                    break;
            }
            pauseBtn.SetActive(s == MatchState.Fighting);
            if (s == MatchState.Menu) RefreshHub();
        }

        /// Menu-state front door: splash on first sight, then login until a
        /// profile exists, then the hub.
        void ShowMenuLayer()
        {
            if (!bootPlayed) { bootPlayed = true; router.Show(bootScreen); }
            else if (!CrownfallMeta.HasProfile) router.Show(loginScreen);
            else router.Show(hubScreen);
        }

        void OnScoreChanged(int a, int c)
        {
            scoreAzureText.text = a.ToString();
            scoreCrimsonText.text = c.ToString();
            UiTween.Punch(scoreAzureText.rectTransform, 0.25f, 0.3f);
            UiTween.Punch(scoreCrimsonText.rectTransform, 0.25f, 0.3f);
        }

        public void OpenSettings() => router.OpenModal(settingsModal);
        void OpenShop() { router.OpenModal(shopModal); RefreshHub(); }
        void OpenInbox() { router.OpenModal(inboxModal); RefreshHub(); }
        void OpenPlayMenu() => router.OpenModal(battleModal);

        // ================================================================ canvas

        void BuildCanvas()
        {
            var go = new GameObject("HUD Canvas", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            go.transform.SetParent(transform, false);
            canvas = go.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 10;
            var scaler = go.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920, 1080);
            scaler.matchWidthOrHeight = 0.5f;
            root = go.GetComponent<RectTransform>();
        }

        UiPanel MakePanel(string name)
        {
            var rt = Rect(name, root, Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f),
                Vector2.zero, Vector2.zero);
            var group = rt.gameObject.AddComponent<CanvasGroup>();
            var p = new UiPanel { Name = name, Go = rt.gameObject, Group = group };
            rt.gameObject.SetActive(false);
            return p;
        }

        // ================================================================ widgets

        internal RectTransform Rect(string name, Transform parent, Vector2 anchorMin, Vector2 anchorMax,
            Vector2 pivot, Vector2 anchoredPos, Vector2 size)
        {
            var go = new GameObject(name, typeof(RectTransform));
            var rt = go.GetComponent<RectTransform>();
            rt.SetParent(parent, false);
            rt.anchorMin = anchorMin;
            rt.anchorMax = anchorMax;
            rt.pivot = pivot;
            rt.anchoredPosition = anchoredPos;
            rt.sizeDelta = size;
            return rt;
        }

        internal Image Img(string name, Transform parent, Vector2 anchorMin, Vector2 anchorMax, Vector2 pivot,
            Vector2 pos, Vector2 size, Sprite sprite, Color color, bool raycast = false)
        {
            var rt = Rect(name, parent, anchorMin, anchorMax, pivot, pos, size);
            var img = rt.gameObject.AddComponent<Image>();
            img.sprite = sprite;
            img.color = color;
            img.raycastTarget = raycast;
            if (sprite != null) img.type = Image.Type.Sliced;
            return img;
        }

        internal Image Icon(string name, Transform parent, Vector2 anchorMin, Vector2 anchorMax, Vector2 pivot,
            Vector2 pos, Vector2 size, Sprite sprite, Color color)
        {
            var img = Img(name, parent, anchorMin, anchorMax, pivot, pos, size, sprite, color);
            // Image.Type.Simple renders these icons as solid WHITE on iOS/Metal
            // (only Sliced images render there) and preserveAspect breaks the
            // geometry, so draw icons as Sliced and fit aspect by sizing the rect.
            img.type = Image.Type.Sliced;
            img.preserveAspect = false;
            if (sprite != null && size.x > 0f && size.y > 0f && pivot == new Vector2(0.5f, 0.5f))
            {
                float spriteAspect = sprite.rect.width / sprite.rect.height;
                float boxAspect = size.x / size.y;
                img.rectTransform.sizeDelta = spriteAspect > boxAspect
                    ? new Vector2(size.x, size.x / spriteAspect)
                    : new Vector2(size.y * spriteAspect, size.y);
            }
            return img;
        }

        internal TMP_Text Txt(string name, Transform parent, Vector2 anchorMin, Vector2 anchorMax, Vector2 pivot,
            Vector2 pos, Vector2 size, string text, TMP_FontAsset font, float fontSize, Color color,
            TextAlignmentOptions align = TextAlignmentOptions.Center)
        {
            var rt = Rect(name, parent, anchorMin, anchorMax, pivot, pos, size);
            var t = rt.gameObject.AddComponent<TextMeshProUGUI>();
            if (font != null) t.font = font;
            t.text = text;
            t.fontSize = fontSize;
            t.color = color;
            t.alignment = align;
            t.raycastTarget = false;
            return t;
        }

        internal Image Bar(string name, Transform parent, Vector2 pos, Vector2 size, Sprite bg, Sprite fill,
            Color fillColor, out Image ghost)
        {
            var bgImg = Img(name + "Bg", parent, Vector2.zero, Vector2.zero, new Vector2(0, 0.5f), pos, size,
                bg, new Color(0.08f, 0.07f, 0.1f, 0.92f));
            ghost = MakeFill(bgImg.rectTransform, fill, new Color(1f, 0.9f, 0.8f, 0.85f), size);
            var f = MakeFill(bgImg.rectTransform, fill, fillColor, size);
            return f;
        }

        /// Segmented Basic04 bar: dark bg, warm ghost, pre-colored fill sprite,
        /// quarter divider ticks and a glass gloss across the whole tube.
        internal Image ProBar(string name, Transform parent, Vector2 pos, Vector2 size, Sprite fill, Color fillTint,
            out Image ghost)
        {
            var bgImg = Img(name + "Bg", parent, Vector2.zero, Vector2.zero, new Vector2(0, 0.5f), pos, size,
                bar4Bg, new Color(0.07f, 0.065f, 0.1f, 0.94f));
            ghost = MakeFill(bgImg.rectTransform, bar4FillWhite, new Color(1f, 0.9f, 0.8f, 0.85f), size);
            var f = MakeFill(bgImg.rectTransform, fill, fillTint, size);
            float innerW = size.x - 6f;
            for (int i = 1; i <= 3; i++)
            {
                var tick = Icon("Tick" + i, bgImg.transform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                    new Vector2(0.5f, 0.5f), new Vector2(-innerW / 2f + innerW * 0.25f * i, 0),
                    new Vector2(6, size.y - 8f), bar4Divider, new Color(0f, 0f, 0f, 0.45f));
                tick.preserveAspect = false;
            }
            Img("Gloss", bgImg.transform, Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f),
                Vector2.zero, new Vector2(-4, -4), bar4Gloss, new Color(1f, 1f, 1f, 0.18f));
            return f;
        }

        internal Image MakeFill(RectTransform bg, Sprite sprite, Color color, Vector2 size)
        {
            var img = Img("Fill", bg, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                Vector2.zero, size - new Vector2(6, 6), sprite, color);
            img.type = Image.Type.Filled;
            img.fillMethod = Image.FillMethod.Horizontal;
            img.fillOrigin = 0;
            img.fillAmount = 1f;
            return img;
        }

        void PlayClick() => GameEffects.I?.PlayUi(GameEffects.I.uiTick, 0.35f);
        void PlaySting() => GameEffects.I?.PlayUi(GameEffects.I.uiVictory, 0.55f);

        internal Button MakeClickable(Image img, UnityEngine.Events.UnityAction onClick)
        {
            img.raycastTarget = true;
            var b = img.gameObject.AddComponent<Button>();
            var colors = b.colors;
            colors.highlightedColor = new Color(1.14f, 1.14f, 1.08f);
            colors.pressedColor = new Color(0.74f, 0.74f, 0.74f);
            b.colors = colors;
            var rt = img.rectTransform;
            b.onClick.AddListener(() => UiTween.Punch(rt));
            b.onClick.AddListener(PlayClick);
            b.onClick.AddListener(onClick);
            return b;
        }

        /// Layer Lab bevel button: pre-colored face sprite, white outline label,
        /// optional picto icon left of the text, squash-punch feedback on press.
        internal Button MenuButton(Transform parent, Vector2 pos, Vector2 size, string label, float fontSize,
            Sprite face, Sprite icon, UnityEngine.Events.UnityAction onClick)
        {
            var img = Img("Btn_" + label, parent, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                new Vector2(0.5f, 0.5f), pos, size, face != null ? face : frameRound,
                face != null ? Color.white : new Color(0.13f, 0.17f, 0.28f, 0.97f), true);
            var b = MakeClickable(img, onClick);

            float textShift = 0f;
            if (icon != null)
            {
                float iconSize = fontSize * 1.25f;
                Icon("I", img.transform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                    new Vector2(-size.x / 2f + iconSize * 0.85f + 10f, 3f), new Vector2(iconSize, iconSize),
                    icon, Color.white);
                textShift = iconSize * 0.55f;
            }
            Txt("L", img.transform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                new Vector2(textShift, 3f), size - new Vector2(16 + textShift * 2f, 10), label, fontMid,
                fontSize, Color.white);
            return b;
        }

        internal Slider MakeSlider(Transform parent, Vector2 pos, float width, float min, float max, float initial,
            UnityEngine.Events.UnityAction<float> onChanged)
        {
            var bg = Img("SliderBg", parent, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                new Vector2(0.5f, 0.5f), pos, new Vector2(width, 28), barBgBasic,
                new Color(0.08f, 0.08f, 0.14f, 0.95f), true);
            var slider = bg.gameObject.AddComponent<Slider>();

            var fillArea = Rect("FillArea", bg.transform, Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f),
                Vector2.zero, new Vector2(-14, -12));
            var fill = Img("Fill", fillArea, Vector2.zero, new Vector2(0f, 1f), new Vector2(0.5f, 0.5f),
                Vector2.zero, new Vector2(10, 0), barFillBasic, Gold);
            var handleArea = Rect("HandleArea", bg.transform, Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f),
                Vector2.zero, new Vector2(-24, 0));
            var handle = Icon("Handle", handleArea, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f),
                new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(44, 44), knobWhite, Color.white);
            handle.raycastTarget = true;

            slider.fillRect = fill.rectTransform;
            slider.handleRect = handle.rectTransform;
            slider.targetGraphic = handle;
            slider.minValue = min;
            slider.maxValue = max;
            slider.value = initial;
            slider.onValueChanged.AddListener(onChanged);
            return slider;
        }

        internal TMP_InputField BuildInput(RectTransform bg, string initial, Sprite leftIcon = null)
        {
            float leftPad = 20f;
            if (leftIcon != null)
            {
                Icon("Ico", bg, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(0.5f, 0.5f),
                    new Vector2(34, 0), new Vector2(34, 34), leftIcon, new Color(1f, 1f, 1f, 0.8f));
                leftPad = 62f;
            }
            var input = bg.gameObject.AddComponent<TMP_InputField>();
            var area = Rect("TextArea", bg, Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f),
                new Vector2(leftPad / 2f, 0), new Vector2(-leftPad - 20f, -12));
            area.gameObject.AddComponent<RectMask2D>();
            var text = Txt("Text", area, Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f),
                Vector2.zero, Vector2.zero, "", fontSmall, 24, Color.white, TextAlignmentOptions.Left);
            input.textViewport = area;
            input.textComponent = (TextMeshProUGUI)text;
            input.characterLimit = 16;
            input.text = initial;
            return input;
        }

        /// Modal chrome: navy dim, Slide02 frame with top-bar + glow title strip
        /// and a circular close button. Returns the frame; panel.Hero is set so
        /// the router pops it on open.
        internal RectTransform ModalShell(string title, Vector2 size, out UiPanel panel,
            bool closable = true)
        {
            panel = MakePanel("Modal_" + title);
            var dim = Img("Dim", panel.Go.transform, Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f),
                Vector2.zero, Vector2.zero, dimNavy, new Color(1f, 1f, 1f, 0.96f), true);
            dim.type = Image.Type.Simple;

            var frame = Img("Frame", panel.Go.transform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                new Vector2(0.5f, 0.5f), new Vector2(0, -14), size, slideNavy, Color.white, true);
            panel.Hero = frame.rectTransform;

            Img("TopGlow", frame.transform, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0.5f, 1f),
                new Vector2(0, 26), new Vector2(size.x + 120f, 130), slideTopGlow, new Color(1f, 1f, 1f, 0.85f));
            var bar = Img("TopBar", frame.transform, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f),
                new Vector2(0.5f, 0.5f), new Vector2(0, -6), new Vector2(size.x - 90f, 74), slideTopBar, Color.white);
            Txt("T", bar.transform, Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f),
                new Vector2(0, 4), new Vector2(-90, -14), title.ToUpper(), fontMid, 38, Color.white);

            if (closable)
            {
                var closeImg = Icon("CloseBtn", frame.transform, new Vector2(1f, 1f), new Vector2(1f, 1f),
                    new Vector2(0.5f, 0.5f), new Vector2(-30, 6), new Vector2(64, 64), btnCircle, Color.white);
                var captured = panel;
                MakeClickable(closeImg, () => router.CloseModal(captured));
                Icon("X", closeImg.transform, Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f),
                    new Vector2(0, 2), new Vector2(24, 24), icoClose, Color.white);
            }
            return frame.rectTransform;
        }

        /// Soft radial glow plate (fx_glow), for planting behind hero elements.
        internal Image Glow(string name, Transform parent, Vector2 pos, float size, Color color)
        {
            var g = Icon(name, parent, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                pos, new Vector2(size, size), fxGlow, color);
            g.raycastTarget = false;
            return g;
        }

        /// Spawn a Layer Lab canvas particle prefab (sparkle burst, confetti) at
        /// an anchored position, self-destructing. Null-safe: garnish only.
        internal void Burst(GameObject prefab, Transform parent, Vector2 anchoredPos, float scale = 1f)
        {
            if (prefab == null) return;
            var go = Instantiate(prefab, parent != null ? parent : root);
            var t = go.transform;
            t.localPosition = new Vector3(anchoredPos.x, anchoredPos.y, 0f);
            t.localScale = t.localScale * scale;
            Destroy(go, 4f);
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

        IEnumerator ToastRoutine(string msg)
        {
            toastText.text = msg;
            toastPlate.gameObject.SetActive(true);
            UiTween.SlideIn(toastPlate.rectTransform, new Vector2(0, 70f), 0.3f, UiTween.Ease.BackOut);
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

        // ================================================================ frame

        void Update()
        {
            var mm = MatchManager.I;
            if (mm == null) return;

            // Escape/back: modal first, then per-screen behavior
            var kb = UnityEngine.InputSystem.Keyboard.current;
            if (kb != null && kb.escapeKey.wasPressedThisFrame)
            {
                if (mm.State == MatchState.Fighting && !router.IsModalOpen) mm.TogglePause();
                else router.Back();
            }

            UpdateOnlineHud();
            if (router.IsOpen(giftModal)) RefreshGift();
            TickFight(mm);
        }
    }

    static class HudRectExtensions
    {
        /// Re-anchor a built control (used for corner-pinned menu buttons).
        public static void SetAnchor(this RectTransform rt, Vector2 anchor, Vector2 pos)
        {
            rt.anchorMin = anchor;
            rt.anchorMax = anchor;
            rt.anchoredPosition = pos;
        }
    }
}
