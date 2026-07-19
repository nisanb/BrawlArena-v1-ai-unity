using UnityEngine;
using UnityEngine.InputSystem;

namespace BrawlArena
{
    /// <summary>
    /// Feeds the local player's intent into BrawlerController. A cast gesture
    /// commits exactly one spell on release: tap for friendly auto-aim, or drag
    /// for a manual direction with a world-space range preview.
    /// </summary>
    [RequireComponent(typeof(BrawlerController))]
    public class PlayerBrawlerInput : MonoBehaviour
    {
        [Header("Manual Aim")]
        [Tooltip("Required action-button drag, measured in 1920-wide reference pixels.")]
        [Min(1f)] public float manualAimDeadzone = 42f;

        BrawlerController self;
        Camera cam;
        Vector3 currentMoveDirection;
        Vector3 gestureForward;
        Vector3 gestureRight;
        Vector3 latchedManualDirection;
        bool attackGestureActive;
        bool manualAimLatched;
        LineRenderer aimPreview;
        Material aimPreviewMaterial;

        void Awake()
        {
            self = GetComponent<BrawlerController>();
            CreateAimPreview();
        }

        void Start()
        {
            cam = Camera.main;
        }

        void Update()
        {
            Vector2 input = Vector2.zero;
            var hud = BrawlHUD.Instance;
            if (hud != null && hud.Joystick != null) input = hud.Joystick.Value;

            var kb = Keyboard.current;
            if (kb != null)
            {
                Vector2 keys = Vector2.zero;
                if (kb.wKey.isPressed || kb.upArrowKey.isPressed) keys.y += 1f;
                if (kb.sKey.isPressed || kb.downArrowKey.isPressed) keys.y -= 1f;
                if (kb.dKey.isPressed || kb.rightArrowKey.isPressed) keys.x += 1f;
                if (kb.aKey.isPressed || kb.leftArrowKey.isPressed) keys.x -= 1f;
                if (keys.sqrMagnitude > 0.01f) input = keys;
            }
            input = Vector2.ClampMagnitude(input, 1f);

            Vector3 world = new Vector3(input.x, 0f, input.y);
            if (cam != null)
            {
                float yaw = cam.transform.eulerAngles.y;
                world = Quaternion.Euler(0f, yaw, 0f) * world;
            }
            self.SetMoveInput(world);
            currentMoveDirection = world;

            UpdateAttack(hud, kb);
            UpdateSuper(hud, kb);
            UpdateWardStep(hud, kb);
        }

        void UpdateAttack(BrawlHUD hud, Keyboard kb)
        {
            if (hud != null)
            {
                if (hud.ConsumeAttackCancelled())
                    ResetAttackGesture();

                if (hud.ConsumeAttackPressed())
                    BeginAttackGesture();

                bool released = hud.ConsumeAttackReleased(out Vector2 releasedDrag);
                if (released)
                {
                    if (attackGestureActive && self.CanAct)
                    {
                        if (TryResolveAttackAim(releasedDrag, out Vector3 releasedDirection))
                            self.TryAttackDirection(releasedDirection);
                        else
                            AttackAutoWithActionFallback();
                    }
                    ResetAttackGesture();
                }
                else if (attackGestureActive && hud.AttackHeld &&
                         hud.TryGetAttackAimDrag(out Vector2 liveDrag))
                {
                    if (TryResolveAttackAim(liveDrag, out Vector3 heldDirection))
                        ShowAimPreview(heldDirection);
                    else
                        HideAimPreview();
                }
            }

            if (!self.CanAct && attackGestureActive) ResetAttackGesture();

            if (kb != null && (kb.spaceKey.wasPressedThisFrame || kb.jKey.wasPressedThisFrame))
                AttackAutoWithActionFallback();
        }

        /// <summary>
        /// Auto-aim first; in the action camera style an on-cooldown-free miss
        /// (no target in range) fires toward planar camera-forward instead, so
        /// attacking always does something in third person.
        /// </summary>
        void AttackAutoWithActionFallback()
        {
            if (self.TryAttackAuto()) return;
            if (BrawlCamera.ActiveStyle != BrawlCameraStyle.ActionThirdPerson) return;
            if (!self.BasicAttackReady) return;
            GetCurrentCameraBasis(out Vector3 forward, out _);
            self.TryAttackDirection(forward);
        }

        void UpdateWardStep(BrawlHUD hud, Keyboard kb)
        {
            bool pressed = hud != null && hud.ConsumeWardStepPressed();
            if (kb != null &&
                (kb.leftShiftKey.wasPressedThisFrame || kb.rightShiftKey.wasPressedThisFrame))
                pressed = true;
            if (!pressed) return;

            Vector3 direction = currentMoveDirection.sqrMagnitude > 0.01f
                ? currentMoveDirection
                : transform.forward;
            if (!self.TryWardStep(direction) && hud != null)
                hud.ShowWardStepFailure();
        }

        void UpdateSuper(BrawlHUD hud, Keyboard kb)
        {
            if (hud != null && hud.ConsumeSuperReleased(out Vector2 superDrag))
            {
                bool succeeded = TryMakeWorldAim(superDrag, out Vector3 direction)
                    ? self.TrySuperDirection(direction)
                    : self.TrySuperAuto();
                if (!succeeded) hud.ShowSuperFailure();
            }

            if (kb != null && (kb.eKey.wasPressedThisFrame || kb.kKey.wasPressedThisFrame))
            {
                bool succeeded = self.TrySuperAuto();
                if (!succeeded && hud != null) hud.ShowSuperFailure();
            }
        }

        bool TryMakeWorldAim(Vector2 screenDrag, out Vector3 worldDirection)
        {
            GetCurrentCameraBasis(out Vector3 forward, out Vector3 right);
            return TryMakeWorldAim(screenDrag, forward, right, out worldDirection);
        }

        void BeginAttackGesture()
        {
            attackGestureActive = self.CanAct;
            manualAimLatched = false;
            latchedManualDirection = Vector3.zero;
            GetCurrentCameraBasis(out gestureForward, out gestureRight);
            HideAimPreview();
        }

        bool TryResolveAttackAim(Vector2 screenDrag, out Vector3 worldDirection)
        {
            if (TryMakeWorldAim(screenDrag, gestureForward, gestureRight, out Vector3 freshDirection))
            {
                manualAimLatched = true;
                latchedManualDirection = freshDirection;
            }

            worldDirection = latchedManualDirection;
            return manualAimLatched;
        }

        bool TryMakeWorldAim(Vector2 screenDrag, Vector3 forward, Vector3 right,
            out Vector3 worldDirection)
        {
            float referenceScale = 1920f / Mathf.Max(1f, Screen.width);
            if (screenDrag.magnitude * referenceScale < manualAimDeadzone)
            {
                worldDirection = Vector3.zero;
                return false;
            }

            forward.y = 0f;
            right.y = 0f;
            forward.Normalize();
            right.Normalize();

            worldDirection = right * screenDrag.x + forward * screenDrag.y;
            worldDirection.y = 0f;
            if (worldDirection.sqrMagnitude <= 0.0001f)
            {
                worldDirection = Vector3.zero;
                return false;
            }

            worldDirection.Normalize();
            return true;
        }

        void GetCurrentCameraBasis(out Vector3 forward, out Vector3 right)
        {
            if (cam == null) cam = Camera.main;
            forward = cam != null ? cam.transform.forward : Vector3.forward;
            right = cam != null ? cam.transform.right : Vector3.right;
            forward.y = 0f;
            right.y = 0f;
            forward = forward.sqrMagnitude > 0.0001f ? forward.normalized : Vector3.forward;
            right = right.sqrMagnitude > 0.0001f ? right.normalized : Vector3.right;
        }

        void CreateAimPreview()
        {
            var previewObject = new GameObject("AttackAimPreview", typeof(LineRenderer));
            previewObject.transform.SetParent(transform, false);
            aimPreview = previewObject.GetComponent<LineRenderer>();
            aimPreview.useWorldSpace = true;
            aimPreview.positionCount = 2;
            aimPreview.startWidth = 0.12f;
            aimPreview.endWidth = 0.035f;
            aimPreview.numCapVertices = 4;
            aimPreview.alignment = LineAlignment.View;
            aimPreview.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            aimPreview.receiveShadows = false;
            aimPreview.startColor = new Color(0.25f, 0.9f, 1f, 0.9f);
            aimPreview.endColor = new Color(0.25f, 0.72f, 1f, 0.12f);

            Shader shader = Shader.Find("Sprites/Default");
            if (shader == null) shader = Shader.Find("Universal Render Pipeline/Unlit");
            if (shader != null)
            {
                aimPreviewMaterial = new Material(shader) { name = "AttackAimPreview_Runtime" };
                aimPreview.material = aimPreviewMaterial;
            }
            HideAimPreview();
        }

        void ShowAimPreview(Vector3 direction)
        {
            if (aimPreview == null || !self.CanAct) return;
            direction.y = 0f;
            if (direction.sqrMagnitude <= 0.0001f)
            {
                HideAimPreview();
                return;
            }

            direction.Normalize();
            Vector3 origin = self.AttackPreviewOrigin + Vector3.up * 0.04f;
            float distance = self.GetAttackPreviewDistance(direction);
            aimPreview.SetPosition(0, origin);
            aimPreview.SetPosition(1, origin + direction * distance);
            aimPreview.enabled = distance > 0.01f;
        }

        void HideAimPreview()
        {
            if (aimPreview != null) aimPreview.enabled = false;
        }

        void ResetAttackGesture()
        {
            attackGestureActive = false;
            manualAimLatched = false;
            latchedManualDirection = Vector3.zero;
            HideAimPreview();
        }

        void OnDisable()
        {
            ResetAttackGesture();
        }

        void OnApplicationFocus(bool hasFocus)
        {
            if (!hasFocus) ResetAttackGesture();
        }

        void OnApplicationPause(bool paused)
        {
            if (paused) ResetAttackGesture();
        }

        void OnDestroy()
        {
            if (aimPreviewMaterial != null) Destroy(aimPreviewMaterial);
        }
    }
}
