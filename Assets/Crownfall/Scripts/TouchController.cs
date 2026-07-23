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
        public Sprite iconSkill;
        public TMP_FontAsset font;

        public static bool forceEnableForTesting;

        /// True once the on-screen controls are built and acting as the control
        /// scheme (now every platform). PlayerController/MatchManager read this to
        /// hand the mouse to the buttons instead of mouse-look, and to keep the
        /// cursor free during a fight so the buttons are clickable on PC.
        public static bool Active;
        /// The touch joystick is currently steering — the keyboard driver defers
        /// movement while this is set.
        public static bool JoystickActive;

        const float HoldHeavySeconds = 0.24f;
        const float HoldSprintSeconds = 0.25f;
        const float JoyRadius = 110f;

        static readonly Color AttackCol = new Color(1f, 0.42f, 0.34f);
        static readonly Color DodgeCol = new Color(0.4f, 0.68f, 1f);
        static readonly Color BlockCol = new Color(0.72f, 0.78f, 0.9f);
        static readonly Color LockCol = new Color(1f, 0.82f, 0.35f);
        static readonly Color AutoCol = new Color(0.45f, 0.85f, 0.45f);
        static readonly Color SkillCol = new Color(0.72f, 0.48f, 1f);

        Canvas canvas;
        RectTransform joyBase, joyKnob;
        RectTransform attackBtn, dodgeBtn, blockBtn, lockBtn, autoBtn, skillBtn;
        Image attackImg, dodgeImg, blockImg, lockImg, skillImg, skillCover;
        Image attackRing, dodgeRing, blockRing, lockRing, skillRing;
        Image blockIconImg, skillIconImg;
        TMP_Text blockLabel;
        Vector2 joyRestPos;
        bool skillWasReady;

        int moveTouchId = -1, cameraTouchId = -1;
        int attackTouchId = -1, dodgeTouchId = -1, blockTouchId = -1;
        Vector2 moveAnchor;
        Vector2 moveDir01;
        float attackDownTime, dodgeDownTime;
        bool heavyFired, sprintHeld;

        void Start()
        {
            // The on-screen mobile controls now show on EVERY platform (owner wants
            // the mobile layout available on PC too). On desktop the mouse operates
            // them directly (mouse-as-finger, see PollMouse) — no global TouchSimulation.
            Active = true;
            EnhancedTouchSupport.Enable();
            BuildUi();
        }

        void OnEnable() => EnhancedTouchSupport.Enable();

        void OnDestroy()
        {
            Active = false;
            JoystickActive = false;
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

            // one action button: designed dark circle face inside a themed ring
            // border, class-colored glyph + small label. The face renders at
            // natural color (alpha only) — color coding lives on the glyph.
            RectTransform Btn(string name, Vector2 anchor, Vector2 pos, float size, Sprite face,
                Color tint, Sprite icon, Color iconTint, string label, float labelSize, out Image faceImg,
                out Image iconImg, out Image ringImg)
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
                faceImg.color = face == btnRound ? new Color(1f, 1f, 1f, tint.a) : tint;
                faceImg.raycastTarget = false;

                ringImg = null;
                // action buttons only — the joystick base IS a ring and the knob stays plain
                if (joyRing != null && face == btnRound && (icon != null || !string.IsNullOrEmpty(label)))
                {
                    var ring = new GameObject("Ring", typeof(RectTransform)).GetComponent<RectTransform>();
                    ring.SetParent(rt, false);
                    ring.anchorMin = ring.anchorMax = ring.pivot = new Vector2(0.5f, 0.5f);
                    ring.anchoredPosition = Vector2.zero;
                    ring.sizeDelta = new Vector2(size * 1.12f, size * 1.12f);
                    ringImg = ring.gameObject.AddComponent<Image>();
                    ringImg.sprite = joyRing;
                    ringImg.type = Image.Type.Simple;
                    ringImg.color = new Color(1f, 1f, 1f, 0.55f);
                    ringImg.raycastTarget = false;
                }

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
                    // Simple-type renders white on iOS/Metal; Sliced renders fine
                    iconImg.type = Image.Type.Sliced;
                    iconImg.preserveAspect = false;
                    iconImg.raycastTarget = false;
                    iconImg.color = iconTint;
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
                new Color(1f, 1f, 1f, 0.35f), null, Color.white, null, 0f, out _, out _, out _);
            joyKnob = Btn("JoyKnob", new Vector2(0f, 0f), joyRestPos, 118f, btnRound,
                new Color(1f, 1f, 1f, 0.65f), null, Color.white, null, 0f, out _, out _, out _);

            attackBtn = Btn("Attack", new Vector2(1f, 0f), new Vector2(-205f, 240f), 216f, btnRound,
                Color.white, iconAttack, AttackCol, null, 0f, out attackImg, out _, out attackRing);
            dodgeBtn = Btn("Dodge", new Vector2(1f, 0f), new Vector2(-450f, 185f), 168f, btnRound,
                Color.white, iconDodge, DodgeCol, null, 0f, out dodgeImg, out _, out dodgeRing);
            blockBtn = Btn("Block", new Vector2(1f, 0f), new Vector2(-210f, 480f), 158f, btnRound,
                Color.white, iconBlock, BlockCol, "BLOCK", 24f, out blockImg, out blockIconImg, out blockRing);
            blockLabel = blockBtn.GetComponentInChildren<TMP_Text>();
            skillBtn = Btn("Skill", new Vector2(1f, 0f), new Vector2(-448f, 400f), 158f, btnRound,
                Color.white, iconSkill, SkillCol, "SKILL", 22f, out skillImg, out skillIconImg, out skillRing);
            // radial cooldown shade that sweeps away as the skill recharges
            var cover = new GameObject("Cooldown", typeof(RectTransform)).GetComponent<RectTransform>();
            cover.SetParent(skillBtn, false);
            cover.anchorMin = Vector2.zero; cover.anchorMax = Vector2.one;
            cover.offsetMin = cover.offsetMax = Vector2.zero;
            skillCover = cover.gameObject.AddComponent<Image>();
            skillCover.sprite = btnRound;
            skillCover.type = Image.Type.Filled;
            skillCover.fillMethod = Image.FillMethod.Radial360;
            skillCover.fillOrigin = (int)Image.Origin360.Top;
            skillCover.fillClockwise = false;
            // functional cooldown shade (state overlay, not a widget face)
            skillCover.color = new Color(0f, 0f, 0f, 0.55f);
            skillCover.raycastTarget = false;
            skillCover.fillAmount = 0f;

            lockBtn = Btn("Lock", new Vector2(1f, 0f), new Vector2(-655f, 300f), 116f, btnRound,
                Color.white, iconLock, LockCol, null, 0f, out lockImg, out _, out lockRing);
            autoBtn = Btn("Auto", new Vector2(0f, 1f), new Vector2(126f, -172f), 116f, btnRound,
                Color.white, iconAuto, AutoCol, "AUTO", 20f, out _, out _, out _);

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

            // combat cluster fades when autopiloting / dead; the designed faces
            // stay at natural color — only alpha moves
            float clusterAlpha = driving ? 1f : 0.4f;
            float ringAlpha = 0.55f * clusterAlpha;
            attackImg.color = Fade(Color.white, clusterAlpha);
            dodgeImg.color = Fade(Color.white, clusterAlpha);
            blockImg.color = Fade(Color.white, clusterAlpha);
            lockImg.color = Fade(Color.white, clusterAlpha);
            if (attackRing != null) attackRing.color = Fade(Color.white, ringAlpha);
            if (dodgeRing != null) dodgeRing.color = Fade(Color.white, ringAlpha);
            if (blockRing != null) blockRing.color = Fade(Color.white, ringAlpha);
            if (lockRing != null) lockRing.color = Fade(Color.white, ringAlpha);
            if (motor != null)
            {
                bool ready = motor.SkillReady;
                skillImg.color = Fade(Color.white, clusterAlpha);
                if (skillIconImg != null)
                    skillIconImg.color = Fade(ready ? SkillCol : new Color(0.5f, 0.45f, 0.6f), clusterAlpha);
                skillCover.fillAmount = 1f - motor.SkillReadiness;
                // gold ring + pulse while the skill is up, quiet while recharging
                if (skillRing != null)
                    skillRing.color = ready ? Fade(new Color(1f, 0.85f, 0.35f), clusterAlpha) : Fade(Color.white, ringAlpha);
                if (ready != skillWasReady)
                {
                    skillWasReady = ready;
                    if (ready) Crownfall.UI.UiTween.PulseForever(skillBtn, 0.98f, 1.07f, 0.9f);
                    else Crownfall.UI.UiTween.StopLoop(skillBtn);
                }
            }
            if (motor != null && blockLabel != null)
            {
                blockLabel.text = motor.Kit.canBlock ? "BLOCK" : "HEAVY";
                if (blockIconImg != null)
                    blockIconImg.sprite = motor.Kit.canBlock ? iconBlock : iconAttack;
            }

            foreach (var touch in ETouch.activeTouches)
                ProcessPointer(touch.touchId, touch.screenPosition, touch.phase, touch.delta, mm, motor, driving);

            // desktop: drive the very same controls with the mouse (mouse = one finger)
            if (!Application.isMobilePlatform)
                PollMouse(mm, motor, driving);

            EndLostTouches();

            JoystickActive = driving && moveTouchId != -1;
            if (driving)
            {
                // Movement ownership: while the joystick is engaged the touch layer
                // steers; otherwise let the keyboard driver own it (PC), and only the
                // pure-touch path (no keyboard) zeroes it on release.
                if (moveTouchId != -1)
                {
                    Vector3 world = Vector3.zero;
                    var cam = OrbitCamera.I;
                    if (moveDir01.sqrMagnitude > 0.001f && cam != null)
                        world = (cam.PlanarForward * moveDir01.y + cam.PlanarRight * moveDir01.x);
                    bool sprint = sprintHeld && dodgeTouchId != -1 &&
                                  Time.unscaledTime - dodgeDownTime >= HoldSprintSeconds;
                    motor.SetMoveInput(world * Mathf.Clamp01(moveDir01.magnitude), sprint);
                }
                else if (UnityEngine.InputSystem.Keyboard.current == null)
                {
                    motor.SetMoveInput(Vector3.zero, false);
                }

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
            else if (motor != null && !mm.Autopilot && UnityEngine.InputSystem.Keyboard.current == null)
            {
                motor.SetMoveInput(Vector3.zero, false);
                motor.SetBlock(false);
            }
        }

        // A single mouse acts as one finger on desktop, routed through the exact
        // same pointer logic as a real touch.
        const int MouseId = 900;
        bool mouseWasDown;

        void PollMouse(MatchManager mm, CombatMotor motor, bool driving)
        {
            var mouse = UnityEngine.InputSystem.Mouse.current;
            if (mouse == null) return;
            Vector2 pos = mouse.position.ReadValue();
            Vector2 delta = mouse.delta.ReadValue();
            bool down = mouse.leftButton.isPressed;
            if (down && !mouseWasDown)
                ProcessPointer(MouseId, pos, UnityEngine.InputSystem.TouchPhase.Began, delta, mm, motor, driving);
            else if (down)
                ProcessPointer(MouseId, pos, UnityEngine.InputSystem.TouchPhase.Moved, delta, mm, motor, driving);
            else if (mouseWasDown)
                ProcessPointer(MouseId, pos, UnityEngine.InputSystem.TouchPhase.Ended, delta, mm, motor, driving);
            mouseWasDown = down;
        }

        void ProcessPointer(int id, Vector2 pos, UnityEngine.InputSystem.TouchPhase phase, Vector2 delta,
            MatchManager mm, CombatMotor motor, bool driving)
        {
            switch (phase)
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
                    else if (Hit(skillBtn, pos)) { motor.RequestSkill(); }
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
                    else if (id == cameraTouchId && phase == UnityEngine.InputSystem.TouchPhase.Moved)
                    {
                        float sens = 0.24f * (1080f / Screen.height) * CrownfallSettings.Sensitivity;
                        OrbitCamera.I?.AddOrbitInput(new Vector2(delta.x * sens, delta.y * sens));
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
            var mouseDev = UnityEngine.InputSystem.Mouse.current;
            if (mouseDev != null && mouseDev.leftButton.isPressed) live.Add(MouseId);
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
