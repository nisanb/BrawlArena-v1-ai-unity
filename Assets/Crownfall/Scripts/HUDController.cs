using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

namespace Crownfall
{
    /// Builds the whole HUD in code at startup from Layer Lab sprites + LilitaOne
    /// fonts (wired by the forge), then binds to match events. The home-hub
    /// half (menu, shop, inbox, gifts, champions) lives in HUDControllerHub.cs.
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

        static readonly Color Gold = new Color(1f, 0.85f, 0.35f);
        static readonly Color AzureCol = new Color(0.35f, 0.65f, 1f);
        static readonly Color CrimsonCol = new Color(1f, 0.36f, 0.3f);
        static readonly Color PlateDark = new Color(0.05f, 0.055f, 0.1f, 0.78f);

        Canvas canvas;
        RectTransform root;

        GameObject fightHudRoot;
        GameObject settingsPanel;
        GameObject pausePanel;
        GameObject pauseBtn;
        GameObject classPanel;
        GameObject resultPanel;
        Image shakeSwitchBg, shakeKnob;
        TMP_Text resultTitle, resultSub;
        Image resultIcon;
        GameObject rewardsRow;
        TMP_Text rewardCoinsText, rewardXpText, rewardTrophyText, levelUpText;
        TMP_Text scoreAzureText, scoreCrimsonText, timerText;
        TMP_Text announceText;
        TMP_Text playerName;
        Image portraitIcon;
        GameObject targetFrame;
        TMP_Text targetName;
        Image targetIcon;
        Image targetFill, targetGhost;
        float targetShown = 1f, targetGhostShown = 1f;
        CombatMotor shownTarget;
        Image hpFill, hpGhost, stFill;
        Image damageFlash;
        RectTransform lockOnMarker;
        RectTransform feedContainer;
        GameObject autopilotTag;

        readonly List<(RectTransform rt, float dieAt)> feedEntries = new List<(RectTransform, float)>();

        class AllyRow { public CombatMotor motor; public Image fill; public TMP_Text label; public Image icon; }
        readonly List<AllyRow> allyRows = new List<AllyRow>();

        Coroutine announceRoutine;
        float hpShown = 1f, ghostShown = 1f, stShown = 1f;

        Sprite IconFor(ClassId id) => id switch
        {
            ClassId.Knight => icoShield,
            ClassId.Greatsword => icoAxe,
            ClassId.Duelist => icoSword,
            _ => icoWand,
        };

        Sprite CardFor(ClassId id) => id switch
        {
            ClassId.Knight => cardKnight,
            ClassId.Greatsword => cardWarbrand,
            ClassId.Duelist => cardDuelist,
            _ => cardMage,
        };

        void Start()
        {
            BuildCanvas();
            BuildFightHud();
            BuildHomeHub();
            BuildChampions();
            BuildSettingsPanel();
            BuildPause();
            BuildResultPanel();
            BuildShop();
            BuildInbox();
            BuildGift();
            BuildToast();

            CrownfallMeta.Changed += RefreshHub;
            RefreshHub();

            var mm = MatchManager.I;
            if (mm != null)
            {
                mm.StateChanged += OnStateChanged;
                mm.PausedChanged += p => pausePanel.SetActive(p);
                mm.ScoreChanged += (a, c) => { scoreAzureText.text = a.ToString(); scoreCrimsonText.text = c.ToString(); };
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

        // ================================================================== build

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

        RectTransform Rect(string name, Transform parent, Vector2 anchorMin, Vector2 anchorMax,
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

        Image Img(string name, Transform parent, Vector2 anchorMin, Vector2 anchorMax, Vector2 pivot,
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

        // pack icons and circular frames must never be 9-sliced
        Image Icon(string name, Transform parent, Vector2 anchorMin, Vector2 anchorMax, Vector2 pivot,
            Vector2 pos, Vector2 size, Sprite sprite, Color color)
        {
            var img = Img(name, parent, anchorMin, anchorMax, pivot, pos, size, sprite, color);
            img.type = Image.Type.Simple;
            img.preserveAspect = true;
            return img;
        }

        TMP_Text Txt(string name, Transform parent, Vector2 anchorMin, Vector2 anchorMax, Vector2 pivot,
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

        Image Bar(string name, Transform parent, Vector2 pos, Vector2 size, Sprite bg, Sprite fill,
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
        Image ProBar(string name, Transform parent, Vector2 pos, Vector2 size, Sprite fill, Color fillTint,
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

        Image MakeFill(RectTransform bg, Sprite sprite, Color color, Vector2 size)
        {
            var img = Img("Fill", bg, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                Vector2.zero, size - new Vector2(6, 6), sprite, color);
            img.type = Image.Type.Filled;
            img.fillMethod = Image.FillMethod.Horizontal;
            img.fillOrigin = 0;
            img.fillAmount = 1f;
            return img;
        }

        /// Layer Lab bevel button: pre-colored face sprite, white outline label,
        /// optional picto icon left of the text.
        Button MenuButton(Transform parent, Vector2 pos, Vector2 size, string label, float fontSize,
            Sprite face, Sprite icon, UnityEngine.Events.UnityAction onClick)
        {
            var img = Img("Btn_" + label, parent, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                new Vector2(0.5f, 0.5f), pos, size, face != null ? face : frameRound,
                face != null ? Color.white : new Color(0.13f, 0.17f, 0.28f, 0.97f), true);
            var b = img.gameObject.AddComponent<Button>();
            var colors = b.colors;
            colors.highlightedColor = new Color(1.12f, 1.12f, 1.05f);
            colors.pressedColor = new Color(0.72f, 0.72f, 0.72f);
            b.colors = colors;
            b.onClick.AddListener(PlayClick);
            b.onClick.AddListener(onClick);

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

        void BuildFightHud()
        {
            var fight = Rect("FightHud", root, Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f),
                Vector2.zero, Vector2.zero);
            fightHudRoot = fight.gameObject;

            // -- score banner: navy banner, team trapezoid plates, gold crown between scores
            var banner = Img("ScoreBanner", fight, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0.5f, 1f),
                new Vector2(0, -8), new Vector2(500, 96), bannerNavy, new Color(1f, 1f, 1f, 0.96f));
            var azPlate = Img("AzPlate", banner.transform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                new Vector2(0.5f, 0.5f), new Vector2(-163, 16), new Vector2(130, 36), trapBlue, Color.white);
            Txt("AzLabel", azPlate.transform, Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f),
                new Vector2(0, 1), new Vector2(-8, -6), "AZURE", fontSmall, 19, Color.white);
            var crPlate = Img("CrPlate", banner.transform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                new Vector2(0.5f, 0.5f), new Vector2(163, 16), new Vector2(150, 36), trapOrange, Color.white);
            Txt("CrLabel", crPlate.transform, Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f),
                new Vector2(0, 1), new Vector2(-8, -6), "CRIMSON", fontSmall, 19, Color.white);
            scoreAzureText = Txt("AzScore", banner.transform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                new Vector2(0.5f, 0.5f), new Vector2(-78, -14), new Vector2(90, 66), "0", fontMid, 46, Color.white);
            scoreCrimsonText = Txt("CrScore", banner.transform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                new Vector2(0.5f, 0.5f), new Vector2(78, -14), new Vector2(90, 66), "0", fontMid, 46, Color.white);
            Icon("Crown", banner.transform, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0.5f, 0.5f),
                new Vector2(0, -2), new Vector2(54, 54), iconCrown, Color.white);

            // -- timer plate under the banner
            var timerPlate = Img("TimerPlate", fight, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f),
                new Vector2(0.5f, 1f), new Vector2(0, -108), new Vector2(190, 42), plateRound, PlateDark);
            Icon("TimerIco", timerPlate.transform, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f),
                new Vector2(0f, 0.5f), new Vector2(14, 0), new Vector2(24, 24), icoTimer, Color.white);
            timerText = Txt("Timer", timerPlate.transform, Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f),
                new Vector2(12, 1), new Vector2(-40, -6), "5:00", fontSmall, 23, new Color(1f, 1f, 1f, 0.95f));

            // -- player panel: profile ring portrait with class icon, segmented HP
            var panel = Rect("PlayerPanel", fight, Vector2.zero, Vector2.zero, Vector2.zero,
                new Vector2(28, 26), new Vector2(560, 152));
            Icon("PortraitRing", panel, Vector2.zero, Vector2.zero, Vector2.zero,
                new Vector2(0, 2), new Vector2(104, 108), profileRing, Color.white);
            Icon("PortraitInner", panel, Vector2.zero, Vector2.zero, Vector2.zero,
                new Vector2(9, 15), new Vector2(86, 86), profileInner, Color.white);
            portraitIcon = Icon("PortraitIcon", panel, Vector2.zero, Vector2.zero, Vector2.zero,
                new Vector2(26, 32), new Vector2(52, 52), icoShield, new Color(0.16f, 0.22f, 0.42f));
            playerName = Txt("Name", panel, Vector2.zero, Vector2.zero, Vector2.zero,
                new Vector2(116, 106), new Vector2(340, 40), "KNIGHT", fontSmall, 26, Color.white,
                TextAlignmentOptions.Left);
            hpFill = ProBar("HP", panel, new Vector2(116, 76), new Vector2(430, 36), bar4FillWhite,
                new Color(0.42f, 0.88f, 0.34f), out hpGhost);
            stFill = Bar("Stamina", panel, new Vector2(116, 36), new Vector2(360, 24),
                barBgBasic, barFillBasic, new Color(1f, 0.8f, 0.25f), out _);

            // -- ally rows with mini class icons
            for (int i = 0; i < 2; i++)
            {
                var row = Rect("Ally" + i, fight, Vector2.zero, Vector2.zero, Vector2.zero,
                    new Vector2(28, 198 + i * 58), new Vector2(300, 52));
                var icon = Icon("AllyIcon", row, Vector2.zero, Vector2.zero, Vector2.zero,
                    new Vector2(0, 22), new Vector2(24, 24), icoShield, new Color(0.8f, 0.9f, 1f));
                var label = Txt("AllyName", row, Vector2.zero, Vector2.zero, Vector2.zero,
                    new Vector2(30, 24), new Vector2(270, 28), "Ally", fontSmall, 18, new Color(0.8f, 0.9f, 1f),
                    TextAlignmentOptions.Left);
                var fill = Bar("AllyHp", row, new Vector2(0, 10), new Vector2(240, 18),
                    barBgBasic, barFillBasic, AzureCol, out _);
                allyRows.Add(new AllyRow { fill = fill, label = label, icon = icon });
            }

            // -- target frame: the enemy you are locked onto or last damaged
            var tf = Img("TargetFrame", fight, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0.5f, 1f),
                new Vector2(0, -162), new Vector2(520, 88), frameRound, new Color(0.08f, 0.09f, 0.16f, 0.92f));
            targetFrame = tf.gameObject;
            var tCircle = Img("TCircle", tf.transform, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f),
                new Vector2(0.5f, 0.5f), new Vector2(46, 0), new Vector2(62, 62), frameCircle,
                new Color(0.32f, 0.12f, 0.12f, 0.98f));
            tCircle.type = Image.Type.Simple;
            targetIcon = Icon("TIcon", tCircle.transform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                new Vector2(0.5f, 0.5f), new Vector2(0, 1), new Vector2(34, 34), icoSword,
                new Color(1f, 0.92f, 0.85f));
            targetName = Txt("TName", tf.transform, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0.5f, 1f),
                new Vector2(24, -8), new Vector2(400, 32), "Vex  ·  Knight", fontSmall, 23, Color.white);
            var tBarBg = Img("TBarBg", tf.transform, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0.5f, 0f),
                new Vector2(24, 10), new Vector2(410, 26), bar4Bg, new Color(0.07f, 0.06f, 0.1f, 0.95f));
            targetGhost = MakeFill(tBarBg.rectTransform, bar4FillWhite, new Color(1f, 0.88f, 0.75f, 0.95f),
                new Vector2(410, 26));
            targetFill = MakeFill(tBarBg.rectTransform, bar4FillRed, Color.white, new Vector2(410, 26));
            targetFrame.SetActive(false);

            // -- kill feed
            feedContainer = Rect("KillFeed", fight, Vector2.one, Vector2.one, Vector2.one,
                new Vector2(-20, -110), new Vector2(430, 400));

            // -- announcement
            announceText = Txt("Announce", fight, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                new Vector2(0.5f, 0.5f), new Vector2(0, 130), new Vector2(1400, 220), "", fontBig, 130, Gold);
            announceText.gameObject.SetActive(false);

            // -- lock-on marker
            var marker = Icon("LockOn", fight, Vector2.zero, Vector2.zero, new Vector2(0.5f, 0.5f),
                Vector2.zero, new Vector2(64, 64), icoTarget, Gold);
            lockOnMarker = marker.rectTransform;
            lockOnMarker.gameObject.SetActive(false);

            // -- damage flash
            damageFlash = Img("DamageFlash", fight, Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f),
                Vector2.zero, Vector2.zero, null, new Color(0.8f, 0.05f, 0.05f, 0f));

            // -- autopilot tag
            var auto = Img("Autopilot", fight, new Vector2(1f, 0f), new Vector2(1f, 0f), new Vector2(1f, 0f),
                new Vector2(-24, 20), new Vector2(356, 42), plateRound, PlateDark);
            Icon("AutoIco", auto.transform, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(0f, 0.5f),
                new Vector2(14, 0), new Vector2(22, 22), icoPlay, Gold);
            Txt("AutoTxt", auto.transform, Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f),
                new Vector2(0, 1), new Vector2(-52, -8), "AUTOPILOT ON  [F1]", fontSmall, 20, Gold,
                TextAlignmentOptions.Right);
            autopilotTag = auto.gameObject;
            autopilotTag.SetActive(false);
        }

        public void OpenSettings() { settingsPanel.SetActive(true); }

        Slider MakeSlider(Transform parent, Vector2 pos, float width, float min, float max, float initial,
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

        void UpdateShakeSwitch()
        {
            bool on = CrownfallSettings.ShakeEnabled;
            shakeSwitchBg.sprite = on ? switchOn : switchOff;
            shakeKnob.sprite = on ? knobOn : knobOff;
            shakeKnob.rectTransform.anchoredPosition = new Vector2(on ? 28f : -28f, 3f);
        }

        void BuildSettingsPanel()
        {
            settingsPanel = Rect("Settings", root, Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f),
                Vector2.zero, Vector2.zero).gameObject;
            Img("Dim", settingsPanel.transform, Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f),
                Vector2.zero, Vector2.zero, null, new Color(0.02f, 0.02f, 0.05f, 0.72f), true);
            var frame = Img("Frame", settingsPanel.transform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                new Vector2(0.5f, 0.5f), new Vector2(0, -20), new Vector2(700, 600), popupNavy, Color.white, true);

            // blue ribbon straddling the popup's top edge
            var ribbon = Img("Ribbon", frame.transform, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f),
                new Vector2(0.5f, 0.5f), new Vector2(0, 26), new Vector2(480, 116), ribbonBlue, Color.white);
            Txt("T", ribbon.transform, Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f),
                new Vector2(0, 8), new Vector2(-120, -50), "SETTINGS", fontMid, 40, Color.white);

            var closeImg = Icon("CloseBtn", frame.transform, new Vector2(1f, 1f), new Vector2(1f, 1f),
                new Vector2(0.5f, 0.5f), new Vector2(-26, 8), new Vector2(64, 64), btnCircle, Color.white);
            closeImg.raycastTarget = true;
            closeImg.gameObject.AddComponent<Button>().onClick.AddListener(() => settingsPanel.SetActive(false));
            Icon("X", closeImg.transform, Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f),
                new Vector2(0, 2), new Vector2(24, 24), icoClose, Color.white);

            Icon("VolIco", frame.transform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                new Vector2(-260, 134), new Vector2(36, 36), icoVolume, Color.white);
            Txt("VolL", frame.transform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                new Vector2(-10, 134), new Vector2(440, 40), "VOLUME", fontSmall, 24, Color.white,
                TextAlignmentOptions.Left);
            MakeSlider(frame.transform, new Vector2(0, 88), 520, 0f, 1f, CrownfallSettings.Volume, v =>
            {
                CrownfallSettings.Volume = v;
                CrownfallSettings.Apply();
                CrownfallSettings.Save();
            });

            Icon("SensIco", frame.transform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                new Vector2(-260, 24), new Vector2(36, 36), icoCamera, Color.white);
            Txt("SensL", frame.transform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                new Vector2(-10, 24), new Vector2(440, 40), "CAMERA SENSITIVITY", fontSmall, 24, Color.white,
                TextAlignmentOptions.Left);
            MakeSlider(frame.transform, new Vector2(0, -22), 520, 0.4f, 2f, CrownfallSettings.Sensitivity, v =>
            {
                CrownfallSettings.Sensitivity = v;
                CrownfallSettings.Save();
            });

            Icon("ShakeIco", frame.transform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                new Vector2(-260, -96), new Vector2(36, 36), icoShake, Color.white);
            Txt("ShakeL", frame.transform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                new Vector2(-52, -96), new Vector2(356, 40), "SCREEN SHAKE", fontSmall, 24, Color.white,
                TextAlignmentOptions.Left);
            shakeSwitchBg = Img("ShakeSwitch", frame.transform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                new Vector2(0.5f, 0.5f), new Vector2(212, -96), new Vector2(112, 54), switchOn, Color.white, true);
            shakeKnob = Icon("Knob", shakeSwitchBg.transform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                new Vector2(0.5f, 0.5f), new Vector2(28, 3), new Vector2(48, 48), knobOn, Color.white);
            shakeSwitchBg.gameObject.AddComponent<Button>().onClick.AddListener(() =>
            {
                CrownfallSettings.ShakeEnabled = !CrownfallSettings.ShakeEnabled;
                CrownfallSettings.Save();
                UpdateShakeSwitch();
            });
            UpdateShakeSwitch();

            MenuButton(frame.transform, new Vector2(0, -212), new Vector2(300, 84), "CLOSE", 30,
                btnGreen, icoCheck, () => settingsPanel.SetActive(false));
            settingsPanel.SetActive(false);
        }

        void BuildPause()
        {
            var btnImg = Icon("PauseBtn", root, Vector2.one, Vector2.one, Vector2.one,
                new Vector2(-24, -22), new Vector2(68, 68), btnCircle, Color.white);
            btnImg.raycastTarget = true;
            btnImg.gameObject.AddComponent<Button>().onClick.AddListener(() => MatchManager.I?.TogglePause());
            Icon("L", btnImg.transform, Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f),
                new Vector2(0, 2), new Vector2(26, 26), icoPause, Color.white);
            pauseBtn = btnImg.gameObject;
            pauseBtn.SetActive(false);

            pausePanel = Rect("PausePanel", root, Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f),
                Vector2.zero, Vector2.zero).gameObject;
            Img("Dim", pausePanel.transform, Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f),
                Vector2.zero, Vector2.zero, null, new Color(0.02f, 0.02f, 0.05f, 0.7f), true);
            var frame = Img("Frame", pausePanel.transform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                new Vector2(0.5f, 0.5f), new Vector2(0, -40), new Vector2(460, 340), popupNavy, Color.white, true);
            var ribbon = Img("Ribbon", frame.transform, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f),
                new Vector2(0.5f, 0.5f), new Vector2(0, 22), new Vector2(400, 104), ribbonOrange, Color.white);
            Txt("T", ribbon.transform, Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f),
                new Vector2(0, 7), new Vector2(-110, -46), "PAUSED", fontMid, 38, Color.white);
            MenuButton(frame.transform, new Vector2(0, 40), new Vector2(350, 92), "RESUME", 32,
                btnGreen, icoPlay, () => MatchManager.I?.TogglePause());
            MenuButton(frame.transform, new Vector2(0, -74), new Vector2(350, 84), "QUIT MATCH", 26,
                btnRed, icoHome, () => MatchManager.I?.Restart());
            pausePanel.SetActive(false);
        }

        void BuildResultPanel()
        {
            resultPanel = Rect("Result", root, Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f),
                Vector2.zero, Vector2.zero).gameObject;
            Img("Dim", resultPanel.transform, Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f),
                Vector2.zero, Vector2.zero, null, new Color(0.02f, 0.02f, 0.05f, 0.72f));
            resultIcon = Icon("Ico", resultPanel.transform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                new Vector2(0.5f, 0.5f), new Vector2(0, 268), new Vector2(150, 150), iconCrown, Color.white);
            resultTitle = Txt("Title", resultPanel.transform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                new Vector2(0.5f, 0.5f), new Vector2(0, 110), new Vector2(1400, 220), "VICTORY", fontBig, 150, Gold);
            resultSub = Txt("Sub", resultPanel.transform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                new Vector2(0.5f, 0.5f), new Vector2(0, -22), new Vector2(900, 62), "", fontMid, 36, Color.white);

            // match rewards strip (hidden for demo matches)
            var rr = Img("Rewards", resultPanel.transform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                new Vector2(0.5f, 0.5f), new Vector2(0, -88), new Vector2(560, 62), plateRound, PlateDark);
            rewardsRow = rr.gameObject;
            Icon("CoinI", rr.transform, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(0f, 0.5f),
                new Vector2(24, 0), new Vector2(38, 38), iconCoinBig, Color.white);
            rewardCoinsText = Txt("CoinT", rr.transform, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f),
                new Vector2(0f, 0.5f), new Vector2(68, 1), new Vector2(110, 44), "+26", fontMid, 27, Gold,
                TextAlignmentOptions.Left);
            rewardXpText = Txt("XpT", rr.transform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                new Vector2(0.5f, 0.5f), new Vector2(10, 1), new Vector2(180, 44), "+40 XP", fontMid, 27,
                new Color(0.65f, 0.85f, 1f));
            Icon("TroI", rr.transform, new Vector2(1f, 0.5f), new Vector2(1f, 0.5f), new Vector2(1f, 0.5f),
                new Vector2(-118, 0), new Vector2(38, 38), menuTrophy, Color.white);
            rewardTrophyText = Txt("TroT", rr.transform, new Vector2(1f, 0.5f), new Vector2(1f, 0.5f),
                new Vector2(1f, 0.5f), new Vector2(-24, 1), new Vector2(90, 44), "+8", fontMid, 27, Gold,
                TextAlignmentOptions.Left);
            levelUpText = Txt("LvUp", resultPanel.transform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                new Vector2(0.5f, 0.5f), new Vector2(0, -136), new Vector2(600, 34),
                "LEVEL UP!  +10 GEMS", fontSmall, 22, new Color(0.55f, 1f, 0.6f));

            MenuButton(resultPanel.transform, new Vector2(0, -196), new Vector2(370, 96), "REMATCH", 34,
                btnGreen, icoRefresh, () => MatchManager.I?.Restart());
            Txt("Hint", resultPanel.transform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                new Vector2(0.5f, 0.5f), new Vector2(0, -262), new Vector2(400, 30), "press  [R]",
                fontSmall, 18, new Color(1f, 1f, 1f, 0.55f));
            resultPanel.SetActive(false);
        }

        // ================================================================== events

        void OnStateChanged(MatchState s)
        {
            hubPanel.SetActive(s == MatchState.Menu);
            classPanel.SetActive(s == MatchState.ClassSelect);
            fightHudRoot.SetActive(s == MatchState.Countdown || s == MatchState.Fighting || s == MatchState.Ended);
            pauseBtn.SetActive(s == MatchState.Fighting);
            if (s != MatchState.Fighting) pausePanel.SetActive(false);
            settingsPanel.SetActive(false);
            shopPanel.SetActive(false);
            inboxPanel.SetActive(false);
            giftPanel.SetActive(false);
            if (s == MatchState.Menu) RefreshHub();
            if (s == MatchState.Countdown || s == MatchState.Fighting) BindPlayer();
        }

        void BindPlayer()
        {
            var pm = MatchManager.I?.PlayerMotor;
            if (pm == null) return;
            playerName.text = pm.Kit.displayName.ToUpper();
            portraitIcon.sprite = IconFor(pm.Kit.id);
            hpShown = ghostShown = stShown = 1f;
            pm.Health.Damaged -= OnPlayerDamaged;
            pm.Health.Damaged += OnPlayerDamaged;

            allyRows.ForEach(r => r.motor = null);
            int i = 0;
            foreach (var m in FindObjectsByType<CombatMotor>(FindObjectsSortMode.InstanceID))
            {
                if (m == pm || m.Identity == null || m.Identity.team != Team.Azure || m.Identity.isPlayer) continue;
                if (i >= allyRows.Count) break;
                allyRows[i].motor = m;
                allyRows[i].label.text = $"{m.Identity.displayName}  ·  {m.Kit.displayName}";
                allyRows[i].icon.sprite = IconFor(m.Kit.id);
                i++;
            }
        }

        void OnPlayerDamaged(HitInfo hit, HitResult res)
        {
            if (res.damageDealt > 0.5f && !res.blocked)
                damageFlash.color = new Color(0.8f, 0.05f, 0.05f, 0.28f);
        }

        void OnKill(CombatantIdentity killer, CombatantIdentity victim)
        {
            string k = killer != null ? killer.displayName : "The Arena";
            Color kc = killer != null ? killer.TeamColor : Color.gray;
            var plate = Img("Feed", feedContainer, new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(1f, 1f),
                Vector2.zero, new Vector2(430, 38), plateRound, PlateDark);
            Icon("Skull", plate.transform, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(0f, 0.5f),
                new Vector2(12, 0), new Vector2(22, 22), icoSkull, new Color(1f, 1f, 1f, 0.85f));
            var entryText = Txt("T", plate.transform, Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f),
                new Vector2(-6, 1), new Vector2(-58, -6),
                $"<color=#{ColorUtility.ToHtmlStringRGB(kc)}>{k}</color>  >  <color=#{ColorUtility.ToHtmlStringRGB(victim.TeamColor)}>{victim.displayName}</color>",
                fontSmall, 21, Color.white, TextAlignmentOptions.Right);
            entryText.ForceMeshUpdate();
            plate.rectTransform.sizeDelta = new Vector2(entryText.preferredWidth + 70f, 38);
            feedEntries.Add((plate.rectTransform, Time.unscaledTime + 5f));
            if (feedEntries.Count > 6)
            {
                Destroy(feedEntries[0].rt.gameObject);
                feedEntries.RemoveAt(0);
            }
            Relayout();
        }

        void Relayout()
        {
            for (int i = 0; i < feedEntries.Count; i++)
            {
                int fromTop = feedEntries.Count - 1 - i;
                feedEntries[i].rt.anchoredPosition = new Vector2(0, -fromTop * 42);
            }
        }

        void OnEnded(Team winner)
        {
            bool won = winner == Team.Azure;
            resultPanel.SetActive(true);
            resultTitle.text = won ? "VICTORY" : "DEFEAT";
            resultTitle.color = won ? Gold : new Color(0.85f, 0.3f, 0.3f);
            resultIcon.sprite = won ? iconCrown : icoSkull;
            resultIcon.color = won ? Color.white : new Color(0.9f, 0.4f, 0.38f);
            resultSub.text = $"Azure {MatchManager.I.ScoreAzure}  —  {MatchManager.I.ScoreCrimson} Crimson";

            var r = MatchManager.I.LastRewards;
            rewardsRow.SetActive(r.Any);
            levelUpText.gameObject.SetActive(r.leveledUp);
            if (r.Any)
            {
                rewardCoinsText.text = $"+{r.coins}";
                rewardXpText.text = $"+{r.xp} XP";
                rewardTrophyText.text = r.trophies >= 0 ? $"+{r.trophies}" : r.trophies.ToString();
                rewardTrophyText.color = r.trophies >= 0 ? Gold : new Color(1f, 0.5f, 0.45f);
            }
        }

        void Pop(string msg, Color color, float scale)
        {
            if (announceRoutine != null) StopCoroutine(announceRoutine);
            announceRoutine = StartCoroutine(PopRoutine(msg, color, scale));
        }

        IEnumerator PopRoutine(string msg, Color color, float scale)
        {
            announceText.gameObject.SetActive(true);
            announceText.text = msg;

            // countdown digits live just under a second so each fades out before
            // the next fades in; announcements linger longer
            float life = msg.Length <= 2 ? 0.92f : 1.6f;
            float fadeIn = 0.16f;
            float fadeOut = 0.32f;

            float t = 0f;
            while (t < life)
            {
                t += Time.unscaledDeltaTime;
                float s = Mathf.Lerp(0.6f, scale, 1f - Mathf.Pow(1f - Mathf.Clamp01(t / 0.24f), 3f));
                s += 0.05f * Mathf.Clamp01(t / life); // slow drift upward
                announceText.rectTransform.localScale = Vector3.one * s;

                float alpha = Mathf.Clamp01(t / fadeIn);
                if (t > life - fadeOut) alpha = Mathf.Clamp01((life - t) / fadeOut);
                announceText.color = new Color(color.r, color.g, color.b, alpha);
                yield return null;
            }
            announceText.gameObject.SetActive(false);
        }

        // ================================================================== frame

        void Update()
        {
            var mm = MatchManager.I;
            if (mm == null) return;

            // keep the gift cooldown label live while its popup is open
            if (giftPanel != null && giftPanel.activeSelf) RefreshGift();

            // timer
            float tl = Mathf.Max(0f, mm.TimeLeft);
            timerText.text = mm.SuddenDeath ? "SUDDEN DEATH" : $"{(int)tl / 60}:{(int)tl % 60:00}";

            // bars
            var pm = mm.PlayerMotor;
            if (pm != null)
            {
                float hp = pm.Health.Max > 0 ? pm.Health.Current / pm.Health.Max : 0f;
                float st = pm.Stamina.Fraction;
                hpShown = Mathf.Lerp(hpShown, hp, 14f * Time.unscaledDeltaTime);
                ghostShown = Mathf.Lerp(ghostShown, hp, 3.2f * Time.unscaledDeltaTime);
                stShown = Mathf.Lerp(stShown, st, 14f * Time.unscaledDeltaTime);
                hpFill.fillAmount = hpShown;
                hpGhost.fillAmount = Mathf.Max(ghostShown, hpShown);
                stFill.fillAmount = stShown;
            }

            foreach (var row in allyRows)
            {
                bool has = row.motor != null;
                row.fill.transform.parent.parent.gameObject.SetActive(has);
                if (has)
                {
                    float f = row.motor.Health.Max > 0 ? row.motor.Health.Current / row.motor.Health.Max : 0f;
                    row.fill.fillAmount = Mathf.Lerp(row.fill.fillAmount, f, 12f * Time.unscaledDeltaTime);
                    row.fill.color = row.motor.IsDead ? new Color(0.4f, 0.4f, 0.45f) : AzureCol;
                }
            }

            // damage flash decay
            if (damageFlash.color.a > 0.001f)
            {
                var c = damageFlash.color;
                c.a = Mathf.MoveTowards(c.a, 0f, 0.6f * Time.unscaledDeltaTime);
                damageFlash.color = c;
            }

            // feed expiry
            for (int i = feedEntries.Count - 1; i >= 0; i--)
            {
                if (Time.unscaledTime > feedEntries[i].dieAt)
                {
                    Destroy(feedEntries[i].rt.gameObject);
                    feedEntries.RemoveAt(i);
                    Relayout();
                }
            }

            autopilotTag.SetActive(mm.Autopilot);

            // target frame: locked enemy, else last-damaged enemy for 6s
            CombatMotor frameTarget = null;
            if (pm != null && mm.State == MatchState.Fighting)
            {
                if (pm.LockTarget != null && !pm.LockTarget.IsDead) frameTarget = pm.LockTarget;
                else if (pm.LastEngagedEnemy != null && !pm.LastEngagedEnemy.IsDead &&
                         Time.time - pm.LastEngagedAt < 6f) frameTarget = pm.LastEngagedEnemy;
            }
            if (frameTarget != shownTarget)
            {
                shownTarget = frameTarget;
                if (shownTarget != null)
                {
                    targetName.text = $"{shownTarget.Identity.displayName}  ·  {shownTarget.Kit.displayName}";
                    targetIcon.sprite = IconFor(shownTarget.Kit.id);
                    float f0 = shownTarget.Health.Max > 0 ? shownTarget.Health.Current / shownTarget.Health.Max : 0f;
                    targetShown = targetGhostShown = f0;
                }
            }
            targetFrame.SetActive(shownTarget != null);
            if (shownTarget != null)
            {
                float tf = shownTarget.Health.Max > 0 ? shownTarget.Health.Current / shownTarget.Health.Max : 0f;
                targetShown = Mathf.Lerp(targetShown, tf, 16f * Time.unscaledDeltaTime);
                targetGhostShown = Mathf.Lerp(targetGhostShown, tf, 4f * Time.unscaledDeltaTime);
                targetFill.fillAmount = targetShown;
                targetGhost.fillAmount = Mathf.Max(targetGhostShown, targetShown);
            }

            // lock-on marker
            var target = pm != null ? pm.LockTarget : null;
            var cam = Camera.main;
            if (target != null && !target.IsDead && cam != null)
            {
                Vector3 sp = cam.WorldToScreenPoint(target.AimPoint + Vector3.up * 0.4f);
                if (sp.z > 0f)
                {
                    lockOnMarker.gameObject.SetActive(true);
                    lockOnMarker.position = sp;
                    float pulse = 1f + Mathf.Sin(Time.unscaledTime * 7f) * 0.08f;
                    lockOnMarker.localScale = Vector3.one * pulse;
                }
                else lockOnMarker.gameObject.SetActive(false);
            }
            else lockOnMarker.gameObject.SetActive(false);
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
