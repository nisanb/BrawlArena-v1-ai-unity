#if UNITY_EDITOR
using UnityEngine;

namespace BrawlArena.EditorAutomation.Tests
{
    /// <summary>
    /// Lightweight editor-test authority used only for disposable gameplay
    /// fixtures that are not exercising the concrete heavy motor itself.
    /// Production prefabs must always use <see cref="HeavyBrawlerMotor"/>.
    /// </summary>
    [AddComponentMenu("")]
    public sealed class BrawlFacadeTestMotor : MonoBehaviour, IBrawlerMotor
    {
        public Vector3 Velocity { get; private set; }
        public float CollisionRadius => 0.5f;
        public bool IsGrounded => true;

        public bool IsCorpseMode { get; private set; }
        public int CorpseModeCalls { get; private set; }
        public int HoldAimFacingCalls { get; private set; }
        public Vector3 LastAimFacingDirection { get; private set; }
        public float LastAimFacingSeconds { get; private set; }
        public int TeleportCalls { get; private set; }
        public Vector3 LastTeleportPosition { get; private set; }

        public void Initialize(float moveSpeed)
        {
            Velocity = Vector3.zero;
        }

        public void SetPlanarIntent(
            Vector3 worldDirection,
            float speed,
            bool movementAllowed)
        {
            if (IsCorpseMode)
            {
                Velocity = Vector3.zero;
                return;
            }
            Vector3 planar = Vector3.ProjectOnPlane(
                worldDirection, Vector3.up);
            Velocity = movementAllowed && planar.sqrMagnitude > 0.0001f
                ? planar.normalized * Mathf.Max(0f, speed)
                : Vector3.zero;
        }

        public void Face(Vector3 worldDirection, bool immediate)
        {
            Vector3 planar = Vector3.ProjectOnPlane(
                worldDirection, Vector3.up);
            if (planar.sqrMagnitude > 0.0001f)
                transform.rotation = Quaternion.LookRotation(planar.normalized);
        }

        public float ConstrainExternalDisplacement(
            Vector3 direction,
            float distance)
        {
            return Mathf.Max(0f, distance);
        }

        public Vector3 ConstrainTeleportDestination(
            Vector3 position,
            float sampleRadius)
        {
            return position;
        }

        public void BeginExternalDisplacement()
        {
        }

        public void Displace(Vector3 displacement, bool keepGrounded)
        {
            transform.position += keepGrounded
                ? Vector3.ProjectOnPlane(displacement, Vector3.up)
                : displacement;
        }

        public void EndExternalDisplacement()
        {
        }

        public void Stop(bool suspend)
        {
            Velocity = Vector3.zero;
        }

        public void Teleport(Vector3 position)
        {
            // Teleport works in every posture and never exits corpse mode;
            // SetCorpseMode(false) is the only restore path.
            transform.position = position;
            Velocity = Vector3.zero;
            TeleportCalls++;
            LastTeleportPosition = position;
        }

        public void SetCorpseMode(bool corpse)
        {
            CorpseModeCalls++;
            IsCorpseMode = corpse;
            if (corpse) Velocity = Vector3.zero;
        }

        public void HoldAimFacing(Vector3 worldDir, float seconds)
        {
            HoldAimFacingCalls++;
            Vector3 planar = Vector3.ProjectOnPlane(worldDir, Vector3.up);
            LastAimFacingDirection = planar.sqrMagnitude > 0.0001f
                ? planar.normalized
                : Vector3.zero;
            LastAimFacingSeconds = seconds;
            if (LastAimFacingDirection != Vector3.zero)
                transform.rotation = Quaternion.LookRotation(LastAimFacingDirection);
        }
    }

    /// <summary>
    /// Semantic no-op presenter for disposable editor test brawlers.
    /// </summary>
    [AddComponentMenu("")]
    public sealed class BrawlFacadeTestAnimationDriver :
        MonoBehaviour,
        IBrawlerAnimationDriver
    {
        public int VictoryCalls { get; private set; }
        public int BasicAttackCalls { get; private set; }
        public int SuperCalls { get; private set; }
        public int HitReactionCalls { get; private set; }
        public int DeathCalls { get; private set; }
        public int RespawnCalls { get; private set; }
        public int DashCalls { get; private set; }
        public Vector3 LastDashDirection { get; private set; }
        public int PauseAnimationCalls { get; private set; }
        public float LastPauseSeconds { get; private set; }

        public void TickLocomotion(float normalizedSpeed)
        {
        }

        public void PlayBasicAttack()
        {
            BasicAttackCalls++;
        }

        public void PlaySuper()
        {
            SuperCalls++;
        }

        public void PlayHitReaction()
        {
            HitReactionCalls++;
        }

        public void PlayDeath()
        {
            DeathCalls++;
        }

        public void PlayRespawn()
        {
            RespawnCalls++;
        }

        public void PlayVictory()
        {
            VictoryCalls++;
        }

        public void PlayDash(Vector3 worldDir)
        {
            DashCalls++;
            LastDashDirection = worldDir;
        }

        public float GetAttackImpactDelay(bool strongAttack, float fallbackSeconds)
        {
            return fallbackSeconds;
        }

        public void PauseAnimation(float seconds)
        {
            PauseAnimationCalls++;
            LastPauseSeconds = seconds;
        }
    }
}
#endif
