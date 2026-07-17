#if UNITY_EDITOR
using UnityEngine;

namespace BrawlArena.EditorAutomation.Tests
{
    /// <summary>
    /// Lightweight editor-test authority used only for disposable gameplay
    /// fixtures that are not exercising the concrete Invector motor itself.
    /// Production prefabs must always use <see cref="InvectorBrawlerMotor"/>.
    /// </summary>
    [AddComponentMenu("")]
    public sealed class InvectorCutoverTestMotor : MonoBehaviour, IBrawlerMotor
    {
        public Vector3 Velocity { get; private set; }
        public float CollisionRadius => 0.5f;
        public bool IsGrounded => true;

        public void Initialize(float moveSpeed)
        {
            Velocity = Vector3.zero;
        }

        public void SetPlanarIntent(
            Vector3 worldDirection,
            float speed,
            bool movementAllowed)
        {
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
            transform.position = position;
            Velocity = Vector3.zero;
        }
    }

    /// <summary>
    /// Semantic no-op presenter for disposable editor test brawlers.
    /// </summary>
    [AddComponentMenu("")]
    public sealed class InvectorCutoverTestAnimationDriver :
        MonoBehaviour,
        IBrawlerAnimationDriver
    {
        public int VictoryCalls { get; private set; }

        public void TickLocomotion(float normalizedSpeed)
        {
        }

        public void PlayBasicAttack()
        {
        }

        public void PlaySuper()
        {
        }

        public void PlayHitReaction()
        {
        }

        public void PlayDeath()
        {
        }

        public void PlayRespawn()
        {
        }

        public void PlayVictory()
        {
            VictoryCalls++;
        }
    }
}
#endif
