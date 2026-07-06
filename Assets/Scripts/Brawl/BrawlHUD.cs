using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.UI;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace BrawlArena
{
    /// <summary>
    /// Mobile brawl HUD, built entirely in code at Awake: floating joystick
    /// zone (left), attack button with cooldown ring (right), score/timer bar
    /// (top), intro + respawn + end-of-match overlays.
    /// </summary>
    public class BrawlHUD : MonoBehaviour
    {
        public static BrawlHUD Instance { get; private set; }

        public VirtualJoystick Joystick { get; private set; }
        public bool AttackHeld => attackButton != null && attackButton.Held;
        public bool SprintHeld => sprintButton != null && sprintButton.Held;

        AttackButtonWidget attackButton;
        AttackButtonWidget sprintButton;
        Image staminaFill;
        GameObject gameplayRoot;
        Image cooldownOverlay;
        Text timerText;
        Text blueScoreText;
        Text redScoreText;
        GameObject blueGemIcon;
        GameObject redGemIcon;
        Text centerText;
        UiTheme theme;
        Text respawnText;
        Text bannerTitle;
        Text bannerSub;
        GameObject bannerRoot;
        GameObject respawnRoot;
        BrawlerController player;

        int lastBlue = -1;
        int lastRed = -1;
        int lastSeconds = -1;
        int lastRespawnTenths = -1;
        float respawnEndsAt;
        float fightFlashUntil;
        MatchState prevState = MatchState.Waiting;

        static Sprite circleSprite;
        static Font uiFont;

        void Awake()
        {
            Instance = this;
            uiFont = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            // Direct lookup instead of UiTheme.Instance: both Awake in the same
            // frame and the order is not guaranteed.
            theme = FindFirstObjectByType<UiTheme>();
            EnsureEventSystem();
            Build();
        }

        void OnDestroy()
        {
            if (Instance == this) Instance = null;
            if (MatchManager.Instance != null) MatchManager.Instance.MatchEnded -= OnMatchEnded;
        }

        void Start()
        {
            var pi = FindFirstObjectByType<PlayerBrawlerInput>();
            if (pi != null) player = pi.GetComponent<BrawlerController>();
            if (MatchManager.Instance != null) MatchManager.Instance.MatchEnded += OnMatchEnded;
        }

        public bool ConsumeAttackPressed()
        {
            return attackButton != null && attackButton.ConsumePressed();
        }

        /// <summary>Show/hide the combat controls (joystick, buttons, top bar).</summary>
        public void SetGameplayVisible(bool visible)
        {
            if (gameplayRoot != null) gameplayRoot.SetActive(visible);
        }

        public void ShowRespawn(float duration)
        {
            respawnEndsAt = Time.time + duration;
            if (respawnRoot != null) respawnRoot.SetActive(true);
        }

        public void HideRespawn()
        {
            if (respawnRoot != null) respawnRoot.SetActive(false);
        }

        void Update()
        {
            var mm = MatchManager.Instance;
            if (mm == null) return;

            // Timer
            int secs = Mathf.CeilToInt(mm.TimeRemaining);
            if (secs != lastSeconds)
            {
                lastSeconds = secs;
                timerText.text = $"{secs / 60}:{secs % 60:00}";
            }

            // Scores: KOs in Knockout, held gems in Gem Grab.
            bool gemMode = mm.mode == GameMode.GemGrab && GemGrabManager.Instance != null;
            var gems = GemGrabManager.Instance;
            int blueValue = gemMode ? gems.TeamGems(TeamId.Blue) : mm.BlueScore;
            int redValue = gemMode ? gems.TeamGems(TeamId.Red) : mm.RedScore;
            if (blueValue != lastBlue)
            {
                lastBlue = blueValue;
                blueScoreText.text = blueValue.ToString();
            }
            if (redValue != lastRed)
            {
                lastRed = redValue;
                redScoreText.text = redValue.ToString();
            }
            if (blueGemIcon != null && blueGemIcon.activeSelf != gemMode)
            {
                blueGemIcon.SetActive(gemMode);
                redGemIcon.SetActive(gemMode);
            }

            // Intro -> FIGHT! flash
            if (prevState == MatchState.Intro && mm.State == MatchState.Playing)
                fightFlashUntil = Time.time + 0.8f;
            prevState = mm.State;

            if (mm.State == MatchState.Intro)
            {
                centerText.gameObject.SetActive(true);
                centerText.text = "GET READY...";
                centerText.color = Color.white;
            }
            else if (Time.time < fightFlashUntil)
            {
                centerText.gameObject.SetActive(true);
                centerText.text = "FIGHT!";
                centerText.color = Color.white;
            }
            else if (gemMode && gems.CountdownTeam.HasValue && mm.State == MatchState.Playing)
            {
                centerText.gameObject.SetActive(true);
                int secs2 = Mathf.CeilToInt(gems.CountdownRemaining);
                centerText.text = (gems.CountdownTeam.Value == TeamId.Blue ? "BLUE" : "RED") +
                                  " WINS IN " + secs2;
                centerText.color = TeamUtil.Color(gems.CountdownTeam.Value);
            }
            else
            {
                centerText.gameObject.SetActive(false);
            }

            // Respawn countdown (change-guarded to avoid per-frame string churn)
            if (respawnRoot != null && respawnRoot.activeSelf)
            {
                int tenths = Mathf.Max(0, Mathf.CeilToInt((respawnEndsAt - Time.time) * 10f));
                if (tenths != lastRespawnTenths)
                {
                    lastRespawnTenths = tenths;
                    respawnText.text = $"RESPAWNING IN {tenths / 10f:0.0}";
                }
            }

            // Attack cooldown ring
            if (player == null)
            {
                var pi = FindFirstObjectByType<PlayerBrawlerInput>();
                if (pi != null) player = pi.GetComponent<BrawlerController>();
            }
            if (player != null && cooldownOverlay != null)
                cooldownOverlay.fillAmount = player.CooldownFraction;

            // Stamina bar
            if (player != null && staminaFill != null)
            {
                float frac = player.maxStamina > 0f ? player.Stamina / player.maxStamina : 0f;
                staminaFill.fillAmount = frac;
                staminaFill.color = frac < 0.3f
                    ? new Color(1f, 0.5f, 0.25f)
                    : new Color(1f, 0.9f, 0.4f);
            }

            // Editor convenience: R to restart after the match ends.
            if (mm.State == MatchState.Ended && Keyboard.current != null &&
                Keyboard.current.rKey.wasPressedThisFrame)
                Restart();
        }

        string rewardLine;

        /// <summary>End-of-match payout line shown under the banner title.</summary>
        public void ShowRewards(string line)
        {
            rewardLine = line;
            if (bannerRoot != null && bannerRoot.activeSelf) ApplyBannerSub();
        }

        void ApplyBannerSub()
        {
            bannerSub.text = string.IsNullOrEmpty(rewardLine)
                ? "TAP TO PLAY AGAIN"
                : rewardLine + "\nTAP TO PLAY AGAIN";
        }

        void OnMatchEnded(TeamId? winner)
        {
            bannerRoot.SetActive(true);
            ApplyBannerSub();
            HideRespawn();
            TeamId playerTeam = player != null ? player.team : TeamId.Blue;
            if (!winner.HasValue)
            {
                bannerTitle.text = "DRAW";
                bannerTitle.color = Color.white;
            }
            else if (winner.Value == playerTeam)
            {
                bannerTitle.text = "VICTORY!";
                bannerTitle.color = new Color(1f, 0.85f, 0.25f);
            }
            else
            {
                bannerTitle.text = "DEFEAT";
                bannerTitle.color = new Color(1f, 0.35f, 0.3f);
            }
        }

        void Restart()
        {
            var scene = SceneManager.GetActiveScene();
            if (scene.buildIndex >= 0) SceneManager.LoadScene(scene.buildIndex);
            else SceneManager.LoadScene(scene.name);
        }

        // ---------------- construction ----------------

        void EnsureEventSystem()
        {
            if (FindFirstObjectByType<EventSystem>() != null) return;
            new GameObject("EventSystem", typeof(EventSystem), typeof(InputSystemUIInputModule));
        }

        void Build()
        {
            var canvasGo = new GameObject("HUDCanvas", typeof(RectTransform), typeof(Canvas),
                typeof(CanvasScaler), typeof(GraphicRaycaster));
            canvasGo.transform.SetParent(transform, false);
            var canvas = canvasGo.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 10;
            var scaler = canvasGo.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.matchWidthOrHeight = 0.5f;
            Transform root = canvasGo.transform;

            gameplayRoot = NewRect("GameplayRoot", root);
            StretchRect((RectTransform)gameplayRoot.transform);
            Transform gameplay = gameplayRoot.transform;

            BuildJoystick(gameplay);
            BuildCameraDragZone(gameplay);
            BuildAttackButton(gameplay);
            BuildSprintControls(gameplay);
            BuildTopBar(gameplay);
            BuildKillFeed(gameplay);
            MinimapView.Create(gameplay, theme, 300f);

            centerText = MakeText("CenterText", root, "", 96, Color.white, TextAnchor.MiddleCenter, true);
            var crt = centerText.rectTransform;
            crt.anchorMin = crt.anchorMax = new Vector2(0.5f, 0.62f);
            crt.sizeDelta = new Vector2(1200f, 160f);

            BuildRespawnOverlay(root);
            BuildEndBanner(root);
        }

        void BuildJoystick(Transform root)
        {
            var zone = NewRect("JoystickZone", root);
            var zrt = (RectTransform)zone.transform;
            zrt.anchorMin = Vector2.zero;
            zrt.anchorMax = new Vector2(0.5f, 0.85f);
            zrt.offsetMin = Vector2.zero;
            zrt.offsetMax = Vector2.zero;
            var zoneImg = zone.AddComponent<Image>();
            zoneImg.color = new Color(1f, 1f, 1f, 0f);

            var joyBase = NewRect("JoyBase", zone.transform);
            var baseImg = joyBase.AddComponent<Image>();
            baseImg.sprite = GetCircleSprite();
            baseImg.color = new Color(1f, 1f, 1f, 0.22f);
            baseImg.raycastTarget = false;
            var brt = (RectTransform)joyBase.transform;
            brt.sizeDelta = new Vector2(250f, 250f);

            var knob = NewRect("JoyKnob", joyBase.transform);
            var knobImg = knob.AddComponent<Image>();
            knobImg.sprite = GetCircleSprite();
            knobImg.color = new Color(1f, 1f, 1f, 0.55f);
            knobImg.raycastTarget = false;
            var krt = (RectTransform)knob.transform;
            krt.sizeDelta = new Vector2(110f, 110f);

            // Nested canvas so per-frame knob movement rebatches only this subtree.
            joyBase.AddComponent<Canvas>();

            Joystick = zone.AddComponent<VirtualJoystick>();
            Joystick.baseRect = brt;
            Joystick.knobRect = krt;
            Joystick.radius = 105f;
            // VirtualJoystick.Awake ran before baseRect was assigned, so hide it here.
            joyBase.SetActive(false);
        }

        void BuildCameraDragZone(Transform root)
        {
            // Right half of the screen orbits the camera. Added before the
            // attack/sprint buttons so they stay on top for raycasts.
            var zone = NewRect("CameraDragZone", root);
            var rt = (RectTransform)zone.transform;
            rt.anchorMin = new Vector2(0.5f, 0f);
            rt.anchorMax = new Vector2(1f, 0.85f);
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;
            var img = zone.AddComponent<Image>();
            img.color = new Color(1f, 1f, 1f, 0f);
            zone.AddComponent<CameraDragZone>();
        }

        void BuildKillFeed(Transform root)
        {
            var feed = NewRect("KillFeed", root);
            var rt = (RectTransform)feed.transform;
            rt.anchorMin = rt.anchorMax = new Vector2(0f, 1f);
            rt.pivot = new Vector2(0f, 1f);
            rt.anchoredPosition = new Vector2(24f, -110f);
            rt.sizeDelta = new Vector2(430f, 240f);
            feed.AddComponent<KillFeed>();
        }

        void BuildAttackButton(Transform root)
        {
            var btn = NewRect("AttackButton", root);
            var rt = (RectTransform)btn.transform;
            rt.anchorMin = rt.anchorMax = new Vector2(1f, 0f);
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = new Vector2(-210f, 210f);
            rt.sizeDelta = new Vector2(240f, 240f);

            var img = btn.AddComponent<Image>();
            img.sprite = GetCircleSprite();
            img.color = new Color(0.95f, 0.35f, 0.25f, 0.9f);
            attackButton = btn.AddComponent<AttackButtonWidget>();

            var label = MakeText("Label", btn.transform, "ATTACK", 36, new Color(1f, 1f, 1f, 0.95f), TextAnchor.MiddleCenter, true);
            StretchRect(label.rectTransform);
            label.raycastTarget = false;

            var overlay = NewRect("Cooldown", btn.transform);
            StretchRect((RectTransform)overlay.transform);
            // Nested canvas: the radial fill changes every frame after each attack.
            overlay.AddComponent<Canvas>();
            cooldownOverlay = overlay.AddComponent<Image>();
            cooldownOverlay.sprite = GetCircleSprite();
            cooldownOverlay.color = new Color(0f, 0f, 0f, 0.55f);
            cooldownOverlay.raycastTarget = false;
            cooldownOverlay.type = Image.Type.Filled;
            cooldownOverlay.fillMethod = Image.FillMethod.Radial360;
            cooldownOverlay.fillOrigin = (int)Image.Origin360.Top;
            cooldownOverlay.fillClockwise = false;
            cooldownOverlay.fillAmount = 0f;
        }

        void BuildSprintControls(Transform root)
        {
            var btn = NewRect("SprintButton", root);
            var rt = (RectTransform)btn.transform;
            rt.anchorMin = rt.anchorMax = new Vector2(1f, 0f);
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = new Vector2(-430f, 150f);
            rt.sizeDelta = new Vector2(150f, 150f);

            var img = btn.AddComponent<Image>();
            img.sprite = GetCircleSprite();
            img.color = new Color(0.3f, 0.6f, 0.95f, 0.85f);
            sprintButton = btn.AddComponent<AttackButtonWidget>();

            var label = MakeText("Label", btn.transform, "SPRINT", 26, new Color(1f, 1f, 1f, 0.95f), TextAnchor.MiddleCenter, true);
            StretchRect(label.rectTransform);
            label.raycastTarget = false;

            // Stamina bar above the button cluster.
            var barBg = NewRect("StaminaBg", root);
            var brt = (RectTransform)barBg.transform;
            brt.anchorMin = brt.anchorMax = new Vector2(1f, 0f);
            brt.pivot = new Vector2(0.5f, 0.5f);
            brt.anchoredPosition = new Vector2(-300f, 350f);
            brt.sizeDelta = new Vector2(330f, 16f);
            var bgImg = barBg.AddComponent<Image>();
            bgImg.color = new Color(0f, 0f, 0f, 0.5f);
            bgImg.raycastTarget = false;

            var fillGo = NewRect("StaminaFill", barBg.transform);
            var frt = (RectTransform)fillGo.transform;
            StretchRect(frt);
            frt.offsetMin = new Vector2(2f, 2f);
            frt.offsetMax = new Vector2(-2f, -2f);
            fillGo.AddComponent<Canvas>();
            staminaFill = fillGo.AddComponent<Image>();
            staminaFill.sprite = GetWhiteSprite();
            staminaFill.color = new Color(1f, 0.9f, 0.4f);
            staminaFill.raycastTarget = false;
            staminaFill.type = Image.Type.Filled;
            staminaFill.fillMethod = Image.FillMethod.Horizontal;
        }

        void BuildTopBar(Transform root)
        {
            timerText = MakeText("Timer", root, "2:30", 56, Color.white, TextAnchor.MiddleCenter, true);
            var trt = timerText.rectTransform;
            trt.anchorMin = trt.anchorMax = new Vector2(0.5f, 1f);
            trt.anchoredPosition = new Vector2(0f, -55f);
            trt.sizeDelta = new Vector2(240f, 80f);

            blueScoreText = MakeScoreChip(root, TeamId.Blue, new Vector2(-210f, -55f), out blueGemIcon);
            redScoreText = MakeScoreChip(root, TeamId.Red, new Vector2(210f, -55f), out redGemIcon);
        }

        Text MakeScoreChip(Transform root, TeamId team, Vector2 pos, out GameObject gemIcon)
        {
            var chip = NewRect(team + "Chip", root);
            var rt = (RectTransform)chip.transform;
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 1f);
            rt.anchoredPosition = pos;
            rt.sizeDelta = new Vector2(150f, 66f);
            var img = chip.AddComponent<Image>();
            if (theme != null && theme.labelChip != null)
            {
                img.sprite = theme.labelChip;
                img.type = Image.Type.Sliced;
            }
            else
            {
                img.sprite = GetCircleSprite();
            }
            img.color = TeamUtil.Color(team);
            img.raycastTarget = false;

            // Gem icon shown only in Gem Grab, outward of each chip.
            gemIcon = null;
            if (theme != null && theme.gemIcon != null)
            {
                gemIcon = NewRect("GemIcon", chip.transform);
                var grt = (RectTransform)gemIcon.transform;
                float side = pos.x < 0f ? -1f : 1f;
                grt.anchorMin = grt.anchorMax = new Vector2(0.5f, 0.5f);
                grt.anchoredPosition = new Vector2(side * 95f, 0f);
                grt.sizeDelta = new Vector2(52f, 60f);
                var gi = gemIcon.AddComponent<Image>();
                gi.sprite = theme.gemIcon;
                gi.preserveAspect = true;
                gi.raycastTarget = false;
                gemIcon.SetActive(false);
            }

            var txt = MakeText("Score", chip.transform, "0", 44, Color.white, TextAnchor.MiddleCenter, true);
            StretchRect(txt.rectTransform);
            txt.raycastTarget = false;
            return txt;
        }

        void BuildRespawnOverlay(Transform root)
        {
            respawnRoot = NewRect("RespawnOverlay", root);
            var rt = (RectTransform)respawnRoot.transform;
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.35f);
            rt.sizeDelta = new Vector2(900f, 100f);
            respawnText = MakeText("Text", respawnRoot.transform, "", 52, new Color(1f, 1f, 1f, 0.9f), TextAnchor.MiddleCenter, true);
            StretchRect(respawnText.rectTransform);
            respawnRoot.SetActive(false);
        }

        void BuildEndBanner(Transform root)
        {
            bannerRoot = NewRect("EndBanner", root);
            StretchRect((RectTransform)bannerRoot.transform);

            var dim = bannerRoot.AddComponent<Image>();
            dim.color = new Color(0f, 0f, 0f, 0.65f);
            var button = bannerRoot.AddComponent<Button>();
            button.transition = Selectable.Transition.None;
            button.onClick.AddListener(Restart);

            bannerTitle = MakeText("Title", bannerRoot.transform, "VICTORY!", 130, new Color(1f, 0.85f, 0.25f), TextAnchor.MiddleCenter, true);
            var brt = bannerTitle.rectTransform;
            brt.anchorMin = brt.anchorMax = new Vector2(0.5f, 0.58f);
            brt.sizeDelta = new Vector2(1400f, 200f);
            bannerTitle.raycastTarget = false;

            bannerSub = MakeText("Sub", bannerRoot.transform, "TAP TO PLAY AGAIN", 42, new Color(1f, 1f, 1f, 0.85f), TextAnchor.MiddleCenter, true);
            var srt = bannerSub.rectTransform;
            srt.anchorMin = srt.anchorMax = new Vector2(0.5f, 0.4f);
            srt.sizeDelta = new Vector2(1000f, 80f);
            bannerSub.raycastTarget = false;

            // Back to the main menu, when the menu scene is in the build list.
            if (Application.CanStreamedLevelBeLoaded("MainMenu"))
            {
                var menuBtn = NewRect("MenuButton", bannerRoot.transform);
                var mrt = (RectTransform)menuBtn.transform;
                mrt.anchorMin = mrt.anchorMax = new Vector2(0.5f, 0.26f);
                mrt.sizeDelta = new Vector2(320f, 96f);
                var img = menuBtn.AddComponent<Image>();
                if (theme != null && theme.buttonBlue != null)
                {
                    img.sprite = theme.buttonBlue;
                    img.type = Image.Type.Sliced;
                }
                else
                {
                    img.color = new Color(0.2f, 0.45f, 0.9f, 0.95f);
                }
                var btn = menuBtn.AddComponent<Button>();
                btn.onClick.AddListener(() => SceneManager.LoadScene("MainMenu"));
                var label = MakeText("Label", menuBtn.transform, "MENU", 40, Color.white, TextAnchor.MiddleCenter, true);
                StretchRect(label.rectTransform);
                label.raycastTarget = false;
            }

            bannerRoot.SetActive(false);
        }

        // ---------------- helpers ----------------

        static GameObject NewRect(string name, Transform parent)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            return go;
        }

        static void StretchRect(RectTransform rt)
        {
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;
        }

        static Text MakeText(string name, Transform parent, string content, int size, Color color, TextAnchor anchor, bool outline)
        {
            var go = NewRect(name, parent);
            var txt = go.AddComponent<Text>();
            txt.font = uiFont;
            txt.text = content;
            txt.fontSize = size;
            txt.fontStyle = FontStyle.Bold;
            txt.color = color;
            txt.alignment = anchor;
            txt.horizontalOverflow = HorizontalWrapMode.Overflow;
            txt.verticalOverflow = VerticalWrapMode.Overflow;
            if (outline)
            {
                var o = go.AddComponent<Outline>();
                o.effectColor = new Color(0f, 0f, 0f, 0.75f);
                o.effectDistance = new Vector2(2.5f, -2.5f);
            }
            return txt;
        }

        static Sprite whiteSprite;

        static Sprite GetWhiteSprite()
        {
            if (whiteSprite != null) return whiteSprite;
            var tex = Texture2D.whiteTexture;
            whiteSprite = Sprite.Create(tex, new Rect(0f, 0f, tex.width, tex.height), new Vector2(0.5f, 0.5f), 100f);
            return whiteSprite;
        }

        static Sprite GetCircleSprite()
        {
            if (circleSprite != null) return circleSprite;
            const int size = 128;
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
            tex.name = "GeneratedCircle";
            float r = size / 2f - 1f;
            Vector2 c = new Vector2(size / 2f, size / 2f);
            var px = new Color[size * size];
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float d = Vector2.Distance(new Vector2(x + 0.5f, y + 0.5f), c);
                    float a = Mathf.Clamp01(r - d + 0.5f);
                    px[y * size + x] = new Color(1f, 1f, 1f, a);
                }
            }
            tex.SetPixels(px);
            tex.Apply();
            circleSprite = Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), 100f);
            return circleSprite;
        }
    }

    /// <summary>Pointer-state widget for the attack button (multi-touch safe).</summary>
    public class AttackButtonWidget : MonoBehaviour, IPointerDownHandler, IPointerUpHandler
    {
        int pressCount;
        bool pressed;

        public bool Held => pressCount > 0;

        public bool ConsumePressed()
        {
            bool v = pressed;
            pressed = false;
            return v;
        }

        public void OnPointerDown(PointerEventData e)
        {
            pressCount++;
            pressed = true;
        }

        public void OnPointerUp(PointerEventData e)
        {
            pressCount = Mathf.Max(0, pressCount - 1);
        }
    }
}
