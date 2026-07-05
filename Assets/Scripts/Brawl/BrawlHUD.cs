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

        AttackButtonWidget attackButton;
        Image cooldownOverlay;
        Text timerText;
        Text blueScoreText;
        Text redScoreText;
        Text centerText;
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
        MatchState prevState = MatchState.Intro;

        static Sprite circleSprite;
        static Font uiFont;

        void Awake()
        {
            Instance = this;
            uiFont = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
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

            // Scores
            if (mm.BlueScore != lastBlue)
            {
                lastBlue = mm.BlueScore;
                blueScoreText.text = lastBlue.ToString();
            }
            if (mm.RedScore != lastRed)
            {
                lastRed = mm.RedScore;
                redScoreText.text = lastRed.ToString();
            }

            // Intro -> FIGHT! flash
            if (prevState == MatchState.Intro && mm.State == MatchState.Playing)
                fightFlashUntil = Time.time + 0.8f;
            prevState = mm.State;

            if (mm.State == MatchState.Intro)
            {
                centerText.gameObject.SetActive(true);
                centerText.text = "GET READY...";
            }
            else if (Time.time < fightFlashUntil)
            {
                centerText.gameObject.SetActive(true);
                centerText.text = "FIGHT!";
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
            if (player != null && cooldownOverlay != null)
                cooldownOverlay.fillAmount = player.CooldownFraction;

            // Editor convenience: R to restart after the match ends.
            if (mm.State == MatchState.Ended && Keyboard.current != null &&
                Keyboard.current.rKey.wasPressedThisFrame)
                Restart();
        }

        void OnMatchEnded(TeamId? winner)
        {
            bannerRoot.SetActive(true);
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

            BuildJoystick(root);
            BuildAttackButton(root);
            BuildTopBar(root);

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

        void BuildTopBar(Transform root)
        {
            timerText = MakeText("Timer", root, "2:30", 56, Color.white, TextAnchor.MiddleCenter, true);
            var trt = timerText.rectTransform;
            trt.anchorMin = trt.anchorMax = new Vector2(0.5f, 1f);
            trt.anchoredPosition = new Vector2(0f, -55f);
            trt.sizeDelta = new Vector2(240f, 80f);

            blueScoreText = MakeScoreChip(root, TeamId.Blue, new Vector2(-210f, -55f));
            redScoreText = MakeScoreChip(root, TeamId.Red, new Vector2(210f, -55f));
        }

        Text MakeScoreChip(Transform root, TeamId team, Vector2 pos)
        {
            var chip = NewRect(team + "Chip", root);
            var rt = (RectTransform)chip.transform;
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 1f);
            rt.anchoredPosition = pos;
            rt.sizeDelta = new Vector2(150f, 66f);
            var img = chip.AddComponent<Image>();
            img.sprite = GetCircleSprite();
            img.color = TeamUtil.Color(team);
            img.raycastTarget = false;

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
