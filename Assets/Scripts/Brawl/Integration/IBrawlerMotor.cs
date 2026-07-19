using UnityEngine;

namespace BrawlArena
{
    /// <summary>
    /// Owns physical movement for one brawler body. The gameplay facade keeps
    /// intent, action timing, and lifecycle decisions while the selected motor
    /// translates those decisions to its concrete controller implementation.
    /// </summary>
    public interface IBrawlerMotor
    {
        Vector3 Velocity { get; }
        float CollisionRadius { get; }
        bool IsGrounded { get; }

        void Initialize(float moveSpeed);
        void SetPlanarIntent(Vector3 worldDirection, float speed, bool movementAllowed);
        void Face(Vector3 worldDirection, bool immediate);
        float ConstrainExternalDisplacement(Vector3 direction, float distance);
        Vector3 ConstrainTeleportDestination(Vector3 position, float sampleRadius);
        void BeginExternalDisplacement();
        void Displace(Vector3 displacement, bool keepGrounded);
        void EndExternalDisplacement();
        void Stop(bool suspend);
        void Teleport(Vector3 position);

        /// <summary>
        /// Death posture. True switches the body kinematic, disables its
        /// collider, and suspends motor ticking so a corpse can never be
        /// shoved or block the living. False restores the dynamic posture.
        /// Teleport while in corpse mode must succeed without restoring it;
        /// SetCorpseMode(false) is the only restore path. Default no-op keeps
        /// legacy implementations compiling untouched.
        /// </summary>
        void SetCorpseMode(bool corpse)
        {
        }

        /// <summary>
        /// Snaps facing to worldDir immediately and keeps aim facing winning
        /// over movement facing for the given duration, so a committed attack
        /// visibly faces its shot while the body keeps moving. Default no-op
        /// keeps legacy implementations compiling untouched.
        /// </summary>
        void HoldAimFacing(Vector3 worldDir, float seconds)
        {
        }
    }
}
