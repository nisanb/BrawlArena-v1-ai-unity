using UnityEngine;
using UnityEngine.InputSystem;

namespace Crownfall
{
    /// Reads mouse/keyboard through the Input System and feeds the CombatMotor.
    /// Hold LMB for a heavy; tap for a light. RMB blocks (Knight) or heavies.
    [RequireComponent(typeof(CombatMotor))]
    [DefaultExecutionOrder(-5)] // drivers act before the motor consumes buffers
    public class PlayerController : MonoBehaviour
    {
        public CombatMotor Motor { get; private set; }

        float lmbDownTime = -1f;
        bool heavyFired;
        const float HoldHeavyThreshold = 0.24f;

        void Awake()
        {
            Motor = GetComponent<CombatMotor>();
        }

        void Update()
        {
            var mm = MatchManager.I;
            if (mm == null || Motor.IsDead) return;

            if (mm.State != MatchState.Fighting || mm.Autopilot)
            {
                Motor.SetMoveInput(Vector3.zero, false);
                Motor.SetBlock(false);
                return;
            }

            var kb = Keyboard.current;
            var mouse = Mouse.current;
            if (kb == null || mouse == null) return;

            // --- movement, camera relative
            Vector2 wasd = Vector2.zero;
            if (kb.wKey.isPressed) wasd.y += 1f;
            if (kb.sKey.isPressed) wasd.y -= 1f;
            if (kb.dKey.isPressed) wasd.x += 1f;
            if (kb.aKey.isPressed) wasd.x -= 1f;

            Vector3 move = Vector3.zero;
            var cam = OrbitCamera.I;
            if (wasd.sqrMagnitude > 0.01f)
            {
                Vector3 fwd = cam != null ? cam.PlanarForward : Vector3.forward;
                Vector3 right = cam != null ? cam.PlanarRight : Vector3.right;
                move = (fwd * wasd.y + right * wasd.x).normalized;
            }

            bool sprint = kb.leftShiftKey.isPressed;
            Motor.SetMoveInput(move, sprint);

            // --- attacks: LMB tap = light, LMB hold = heavy
            if (mouse.leftButton.wasPressedThisFrame)
            {
                lmbDownTime = Time.unscaledTime;
                heavyFired = false;
            }
            if (mouse.leftButton.isPressed && lmbDownTime > 0f && !heavyFired &&
                Time.unscaledTime - lmbDownTime >= HoldHeavyThreshold)
            {
                heavyFired = true;
                Motor.RequestHeavy();
            }
            if (mouse.leftButton.wasReleasedThisFrame && lmbDownTime > 0f && !heavyFired)
            {
                Motor.RequestLight();
                lmbDownTime = -1f;
            }

            // --- RMB: block for shield classes, heavy for the rest
            if (Motor.Kit.canBlock)
            {
                Motor.SetBlock(mouse.rightButton.isPressed);
            }
            else
            {
                Motor.SetBlock(false);
                if (mouse.rightButton.wasPressedThisFrame) Motor.RequestHeavy();
            }

            // --- roll
            if (kb.spaceKey.wasPressedThisFrame)
                Motor.RequestRoll(move.sqrMagnitude > 0.01f ? move : -transform.forward);

            // --- lock-on
            if (kb.qKey.wasPressedThisFrame || mouse.middleButton.wasPressedThisFrame)
                ToggleLockOn();
            if (kb.tabKey.wasPressedThisFrame && Motor.LockTarget != null)
                CycleLockOn();

            if (Motor.LockTarget != null)
            {
                var t = Motor.LockTarget;
                bool tooFar = (t.transform.position - transform.position).sqrMagnitude > 24f * 24f;
                if (t.IsDead || tooFar || t.IsConcealedFrom(Motor)) AcquireOrClear();
            }
        }

        public void ToggleLockOn()
        {
            if (Motor.LockTarget != null) { Motor.LockTarget = null; return; }
            AcquireOrClear();
        }

        void AcquireOrClear()
        {
            Motor.LockTarget = FindTarget(null);
        }

        void CycleLockOn()
        {
            Motor.LockTarget = FindTarget(Motor.LockTarget) ?? Motor.LockTarget;
        }

        CombatMotor FindTarget(CombatMotor exclude)
        {
            var mm = MatchManager.I;
            if (mm == null) return null;
            Vector3 camFwd = OrbitCamera.I != null ? OrbitCamera.I.PlanarForward : transform.forward;

            CombatMotor best = null;
            float bestScore = float.MinValue;
            foreach (var enemy in mm.AliveEnemiesOf(Motor.Identity.team))
            {
                if (enemy == exclude) continue;
                if (enemy.IsConcealedFrom(Motor)) continue;
                Vector3 to = enemy.transform.position - transform.position;
                float dist = to.magnitude;
                if (dist > 22f) continue;
                float angle = Vector3.Angle(camFwd, to);
                float score = -dist - angle * 0.12f;
                if (score > bestScore) { bestScore = score; best = enemy; }
            }
            return best;
        }
    }
}
