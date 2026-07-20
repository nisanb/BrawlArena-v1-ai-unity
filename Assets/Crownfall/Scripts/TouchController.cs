using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.InputSystem.EnhancedTouch;
using TMPro;
using ETouch = UnityEngine.InputSystem.EnhancedTouch.Touch;

namespace Crownfall
{
    /// Mobile touch controls, souls layout:
    ///  - left half: floating joystick (move)
    ///  - right half drag: camera orbit
    ///  - buttons: ATTACK (tap light / hold heavy), DODGE (tap roll / hold sprint),
    ///    BLOCK hold (Knight) or HEAVY, LOCK toggle, AUTO demo toggle.
    /// Feeds the same CombatMotor API as PlayerController.
    [DefaultExecutionOrder(-5)] // drivers act before the motor consumes buffers
    public class TouchController : MonoBehaviour
    {
        [Header("Wired by forge")]
        public Sprite circleSprite;
        public Sprite btnRound;      // tintable round button face
        public Sprite joyRing;       // joystick base ring
        public Sprite iconAttack;
        public Sprite iconDodge;
        public Sprite iconBlock;
        public Sprite iconLock;
        public Sprite iconAuto;
        public TMP_FontAsset font;

        public static bool forceEnableForTesting;

        const float HoldHeavySeconds = 0.24f;
        const float HoldSprintSeconds = 0.25f;
        const float JoyRadius = 110f;

        static readonly Color AttackCol = new Color(1f, 0.42f, 0.34f);
        static readonly Color DodgeCol = new Color(0.4f, 0.68f, 1f);
        static readonly Color BlockCol = new Color(0.72f, 0.78f, 0.9f);
        static readonly Color LockCol = new Color(1f, 0.82f, 0.35f);
        static readonly Color AutoCol = new Color(0.45f, 0.85f, 0.45f);

        Canvas canvas;
        RectTransform joyBase, joyKnob;
        RectTransform attackBtn, dodgeBtn, blockBtn, lockBtn, autoBtn;
        Image attackImg, dodgeImg, blockImg, lockImg;
        Image blockIconImg;
        TMP_Text blockLabel;
        Vector2 joyRestPos;

        int moveTouchId = -1, cameraTouchId = -1;
        int attackTouchId = -1, dodgeTouchId = -1, blockTouchId = -1;
        Vector2 moveAnchor;
        Vector2 moveDir01;
        float attackDownTime, dodgeDownTime;
        bool heavyFired, sprintHeld;

        void Start()
        {
            if (!Application.isMobilePlatform && !forceEnableForTesting)
            {
                enabled = false;
                return;
            }
            EnhancedTouchSupport.Enable();
            BuildUi();
        }

        void OnEnable()
        {
            if (Application.isMobilePlatform || forceEnableForTesting)
                EnhancedTouchSupport.Enable();
        }

        // ------------------------------------------------------------------ ui

        void BuildUi()
        {
            var go = new GameObject("Touch Canvas", typeof(Canvas), typeof(CanvasScaler));
            go.transform.SetParent(transform, false);
            canvas = go.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 11;
            var scaler = go.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920, 1080);
            scaler.matchWidthOrHeight = 0.5f;
            var root = go.GetComponent<RectTransform>();

            // one action button: tinted round face + centered icon + small label
            RectTransform Btn(string name, Vector2 anchor, Vector2 pos, float size, Sprite face,
                Color tint, Sprite icon, string label, float labelSize, out Image faceImg, out Image iconImg)
            {
                var b = new GameObject(name, typeof(RectTransform));
                var rt = b.GetComponent<RectTransform>();
                rt.SetParent(root, false);
                rt.anchorMin = rt.anchorMax = anchor;
                rt.pivot = new Vector2(0.5f, 0.5f);
                rt.anchoredPosition = pos;
                rt.sizeDelta = new Vector2(size, size);
                faceImg = b.AddComponent<Image>();
                faceImg.sprite = face != null ? face : circleSprite;
                faceImg.type = Image.Type.Simple;
                faceImg.color = tint;
                faceImg.raycastTarget = false;

                iconImg = null;
                if (icon != null)
                {
                    var ic = new GameObject("I", typeof(RectTransform)).GetComponent<RectTransform>();
                    ic.SetParent(rt, false);
                    ic.anchorMin = ic.anchorMax = ic.pivot = new Vector2(0.5f, 0.5f);
                    ic.anchoredPosition = new Vector2(0f, size * 0.04f);
                    ic.sizeDelta = new Vector2(size * 0.52f, size * 0.52f);
                    iconImg = ic.gameObject.AddComponent<Image>();
                    iconImg.sprite = icon;
                    iconImg.preserveAspect = true;
                    iconImg.raycastTarget = false;
                }
                if (!string.IsNullOrEmpty(label))
                {
                    var t = new GameObject("L", typeof(RectTransform)).GetComponent<RectTransform>();
                    t.SetParent(rt, false);
                    t.anchorMin = new Vector2(0.5f, 0f); t.anchorMax = new Vector2(0.5f, 0f);
                    t.pivot = new Vector2(0.5f, 1f);
                    t.anchoredPosition = new Vector2(0f, size * 0.14f);
                    t.sizeDelta = new Vector2(size * 1.3f, size * 0.24f);
                    var tmp = t.gameObject.AddComponent<TextMeshProUGUI>();
                    if (font != null) tmp.font = font;
                    tmp.text = label;
                    tmp.fontSize = labelSize;
                    tmp.color = new Color(1f, 1f, 1f, 0.92f);
                    tmp.alignment = TextAlignmentOptions.Center;
                    tmp.raycastTarget = false;
                }
                return rt;
            }

            joyRestPos = new Vector2(300f, 300f);
            joyBase = Btn("JoyBase", new Vector2(0f, 0f), joyRestPos, 270f, joyRing,
                new Color(1f, 1f, 1f, 0.35f), null, null, 0f, out _, out _);
            joyKnob = Btn("JoyKnob", new Vector2(0f, 0f), joyRestPos, 118f, btnRound,
                new Color(1f, 1f, 1f, 0.65f), null, null, 0f, out _, out _);

            attackBtn = Btn("Attack", new Vector2(1f, 0f), new Vector2(-205f, 240f), 216f, btnRound,
                AttackCol, iconAttack, null, 0f, out attackImg, out _);
            dodgeBtn = Btn("Dodge", new Vector2(1f, 0f), new Vector2(-450f, 185f), 168f, btnRound,
                DodgeCol, iconDodge, null, 0f, out dodgeImg, out _);
            blockBtn = Btn("Block", new Vector2(1f, 0f), new Vector2(-210f, 480f), 158f, btnRound,
                BlockCol, iconBlock, "BLOCK", 24f, out blockImg, out blockIconImg);
            blockLabel = blockBtn.GetComponentInChildren<TMP_Text>();
            lockBtn = Btn("Lock", new Vector2(1f, 0f), new Vector2(-445f, 420f), 124f, btnRound,
                LockCol, iconLock, null, 0f, out lockImg, out _);
            autoBtn = Btn("Auto", new Vector2(0f, 1f), new Vector2(126f, -172f), 116f, btnRound,
                AutoCol, iconAuto, "AUTO", 20f, out _, out _);

            canvas.enabled = false; // hidden until a match starts (see Update)
        }

        // ------------------------------------------------------------------ frame

        void Update()
        {
            var mm = MatchManager.I;
            if (mm == null || canvas == null) return;

            // controls belong to the match only — never the home hub / menus
            bool inMatch = mm.State == MatchState.Countdown || mm.State == MatchState.Fighting;
            if (canvas.enabled != inMatch) canvas.enabled = inMatch;
            if (!inMatch) return;

            bool fighting = mm.State == MatchState.Fighting && !mm.Paused;
            var motor = mm.PlayerMotor;
            bool driving = fighting && !mm.Autopilot && motor != null && !motor.IsDead;

            // combat cluster fades when autopiloting / dead but keeps its tint
            float clusterAlpha = driving ? 1f : 0.4f;
            attackImg.color = Fade(AttackCol, clusterAlpha);
            dodgeImg.color = Fade(DodgeCol, clusterAlpha);
            blockImg.color = Fade(BlockCol, clusterAlpha);
            lockImg.color = Fade(LockCol, clusterAlpha);
            if (motor != null && blockLabel != null)
            {
                blockLabel.text = motor.Kit.canBlock ? "BLOCK" : "HEAVY";
                if (blockIconImg != null)
                    blockIconImg.sprite = motor.Kit.canBlock ? iconBlock : iconAttack;
            }

            foreach (var touch in ETouch.activeTouches)
                HandleTouch(touch, mm, motor, driving);

            EndLostTouches();

            if (driving)
            {
                Vector3 world = Vector3.zero;
                var cam = OrbitCamera.I;
                if (moveDir01.sqrMagnitude > 0.001f && cam != null)
                    world = (cam.PlanarForward * moveDir01.y + cam.PlanarRight * moveDir01.x);

                bool sprint = sprintHeld && dodgeTouchId != -1 &&
                              Time.unscaledTime - dodgeDownTime >= HoldSprintSeconds;
                motor.SetMoveInput(world * Mathf.Clamp01(moveDir01.magnitude), sprint);

                // hold-to-heavy on the attack button
                if (attackTouchId != -1 && !heavyFired &&
                    Time.unscaledTime - attackDownTime >= HoldHeavySeconds)
                {
                    heavyFired = true;
                    motor.RequestHeavy();
                }

                if (motor.Kit.canBlock)
                    motor.SetBlock(blockTouchId != -1);
            }
            else if (motor != null && !mm.Autopilot)
            {
                motor.SetMoveInput(Vector3.zero, false);
                motor.SetBlock(false);
            }
        }

        void HandleTouch(ETouch touch, MatchManager mm, CombatMotor motor, bool driving)
        {
            int id = touch.touchId;
            Vector2 pos = touch.screenPosition;

            switch (touch.phase)
            {
                case UnityEngine.InputSystem.TouchPhase.Began:
                    if (Hit(autoBtn, pos)) { mm.Autopilot = !mm.Autopilot; return; }
                    if (!driving)
                    {
                        // let UGUI handle menu taps; camera drag still allowed
                        if (pos.x > Screen.width * 0.55f) cameraTouchId = id;
                        return;
                    }
                    if (Hit(attackBtn, pos)) { attackTouchId = id; attackDownTime = Time.unscaledTime; heavyFired = false; }
                    else if (Hit(dodgeBtn, pos)) { dodgeTouchId = id; dodgeDownTime = Time.unscaledTime; sprintHeld = true; }
                    else if (Hit(blockBtn, pos))
                    {
                        blockTouchId = id;
                        if (!motor.Kit.canBlock) motor.RequestHeavy();
                    }
                    else if (Hit(lockBtn, pos))
                    {
                        var pc = motor.GetComponent<PlayerController>();
                        if (pc != null) pc.ToggleLockOn();
                    }
                    else if (pos.x < Screen.width * 0.45f && moveTouchId == -1)
                    {
                        moveTouchId = id;
                        moveAnchor = pos;
                        SetJoyVisual(pos, pos);
                    }
                    else if (cameraTouchId == -1)
                    {
                        cameraTouchId = id;
                    }
                    break;

                case UnityEngine.InputSystem.TouchPhase.Moved:
                case UnityEngine.InputSystem.TouchPhase.Stationary:
                    if (id == moveTouchId)
                    {
                        Vector2 delta = pos - moveAnchor;
                        float maxPix = JoyRadius * ScaleFactor();
                        if (delta.magnitude > maxPix)
                        {
                            // drag the anchor along so the stick follows the thumb
                            moveAnchor = pos - delta.normalized * maxPix;
                            delta = pos - moveAnchor;
                        }
                        moveDir01 = delta / Mathf.Max(1f, maxPix);
                        SetJoyVisual(moveAnchor, pos);
                    }
                    else if (id == cameraTouchId && touch.phase == UnityEngine.InputSystem.TouchPhase.Moved)
                    {
                        Vector2 d = touch.delta;
                        float sens = 0.24f * (1080f / Screen.height) * CrownfallSettings.Sensitivity;
                        OrbitCamera.I?.AddOrbitInput(new Vector2(d.x * sens, d.y * sens));
                    }
                    break;

                case UnityEngine.InputSystem.TouchPhase.Ended:
                case UnityEngine.InputSystem.TouchPhase.Canceled:
                    ReleaseTouch(id, motor, driving);
                    break;
            }
        }

        void ReleaseTouch(int id, CombatMotor motor, bool driving)
        {
            if (id == moveTouchId)
            {
                moveTouchId = -1;
                moveDir01 = Vector2.zero;
                SetJoyVisual(RestScreenPos(), RestScreenPos());
            }
            else if (id == cameraTouchId) cameraTouchId = -1;
            else if (id == attackTouchId)
            {
                if (driving && !heavyFired) motor.RequestLight();
                attackTouchId = -1;
            }
            else if (id == dodgeTouchId)
            {
                if (driving && Time.unscaledTime - dodgeDownTime < HoldSprintSeconds)
                {
                    Vector3 world = Vector3.zero;
                    var cam = OrbitCamera.I;
                    if (moveDir01.sqrMagnitude > 0.001f && cam != null)
                        world = cam.PlanarForward * moveDir01.y + cam.PlanarRight * moveDir01.x;
                    motor.RequestRoll(world);
                }
                sprintHeld = false;
                dodgeTouchId = -1;
            }
            else if (id == blockTouchId) blockTouchId = -1;
        }

        void EndLostTouches()
        {
            // safety net: if a tracked touch vanished without an Ended phase, clear it
            var live = new HashSet<int>();
            foreach (var t in ETouch.activeTouches) live.Add(t.touchId);
            var mm = MatchManager.I;
            var motor = mm != null ? mm.PlayerMotor : null;
            bool driving = mm != null && mm.State == MatchState.Fighting && !mm.Autopilot && motor != null;
            foreach (int id in new[] { moveTouchId, cameraTouchId, attackTouchId, dodgeTouchId, blockTouchId })
                if (id != -1 && !live.Contains(id))
                    ReleaseTouch(id, motor, driving && motor != null && !motor.IsDead);
        }

        // ------------------------------------------------------------------ helpers

        static Color Fade(Color c, float a) => new Color(c.r, c.g, c.b, a);

        bool Hit(RectTransform rt, Vector2 screenPos)
        {
            return RectTransformUtility.RectangleContainsScreenPoint(rt, screenPos, null);
        }

        float ScaleFactor() => canvas != null ? canvas.scaleFactor : 1f;

        Vector2 RestScreenPos() => joyRestPos * ScaleFactor();

        void SetJoyVisual(Vector2 baseScreen, Vector2 knobScreen)
        {
            float s = ScaleFactor();
            joyBase.anchoredPosition = baseScreen / s;
            Vector2 clamped = baseScreen + Vector2.ClampMagnitude(knobScreen - baseScreen, JoyRadius * s);
            joyKnob.anchoredPosition = clamped / s;
        }
    }
}
