using System;
using UnityEngine;
using UnityEngine.AI;

namespace BrawlArena
{
    /// <summary>
    /// Authored per-hero movement mass for the souls-style motor. Weight
    /// scales how sluggishly the body gathers and sheds speed; the impulse
    /// channel gives rolls, attack lunges, and hit shoves real momentum that
    /// decays instead of teleporting.
    /// </summary>
    [Serializable]
    public sealed class HeavyMotorProfile
    {
        /// <summary>1 = standard fighter; above 1 is more ponderous.</summary>
        public float weight = 1f;
        /// <summary>Planar acceleration toward move intent, u/s^2 at weight 1.</summary>
        public float acceleration = 20f;
        /// <summary>Planar deceleration when intent stops, u/s^2 at weight 1.</summary>
        public float deceleration = 26f;
        /// <summary>Facing slew rate; combat aim snaps are still instant.</summary>
        public float turnRateDegreesPerSecond =
            MobileCombatRules.CombatTurnRateDegreesPerSecond;
        /// <summary>Exponential decay rate (1/s) of the impulse channel.</summary>
        public float impulseDamping = 5.5f;

        public HeavyMotorProfile Clone()
        {
            return (HeavyMotorProfile)MemberwiseClone();
        }
    }

    /// <summary>
    /// Souls-style brawler motor. Movement carries momentum: intent
    /// accelerates the body along a steering vector instead of setting
    /// velocity, direction reversals plant through near-zero speed before
    /// building back up, and a damped impulse channel layers rolls, attack
    /// lunges, and hit shoves on top of steering. BrawlerController writes
    /// world-space intent every Update; the next fixed step consumes it.
    /// A single rotation owner (Rigidbody.MoveRotation) slews facing at the
    /// profile turn rate; aim facing held through HoldAimFacing wins over
    /// movement facing while its window is active. Death is a physics
    /// posture: SetCorpseMode(true) parks the body kinematic with its capsule
    /// off so corpses can never be shoved.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(Rigidbody))]
    [RequireComponent(typeof(CapsuleCollider))]
    public sealed class HeavyBrawlerMotor : MonoBehaviour, IBrawlerMotor
    {
        const float MinimumMoveSpeed = 0.1f;
        const float ExternalDisplacementSkin = 0.06f;
        const float GroundProbeLift = 0.1f;
        const float GroundProbeDistance = 0.35f;
        const float ImpulseSleepThreshold = 0.05f;

        [SerializeField]
        HeavyMotorProfile profile = new HeavyMotorProfile();

        Rigidbody body;
        CapsuleCollider capsule;

        float initializedRequestedMoveSpeed = 1f;
        bool initialized;
        bool suspended;

        Vector3 intentDirection;
        float intentSpeed;
        bool intentMovementAllowed;

        Vector3 steeringVelocity;
        Vector3 impulseVelocity;

        Vector3 aimFacingDirection;
        float aimFacingHoldUntil;

        Vector3 pendingFaceDirection;
        bool pendingFace;
        bool pendingFaceImmediate;

        int externalDisplacementDepth;
        Vector3 pendingExternalDisplacement;
        bool externalDisplacementEndPending;

        bool corpseMode;
        bool corpseSavedKinematic;
        bool corpseSavedUseGravity;
        bool corpseSavedCapsuleEnabled;

        public HeavyMotorProfile Profile => profile;

        public Vector3 Velocity
        {
            get
            {
                EnsureComponents();
                if (body == null || body.isKinematic) return Vector3.zero;
                return body.linearVelocity;
            }
        }

        public float CollisionRadius
        {
            get
            {
                EnsureComponents();
                if (capsule == null) return 0.65f;
                Vector3 scale = capsule.transform.lossyScale;
                float planarScale = Mathf.Max(Mathf.Abs(scale.x), Mathf.Abs(scale.z));
                return Mathf.Max(0.35f, capsule.radius * planarScale);
            }
        }

        public bool IsGrounded
        {
            get
            {
                EnsureComponents();
                if (body == null || capsule == null || !capsule.enabled) return false;
                return Physics.Raycast(
                    body.position + Vector3.up * GroundProbeLift, Vector3.down,
                    GroundProbeDistance, Physics.AllLayers,
                    QueryTriggerInteraction.Ignore);
            }
        }

        public bool IsInitialized => initialized;
        public bool IsSuspended => suspended;
        public bool IsCorpseMode => corpseMode;
        public int ExternalDisplacementDepth => externalDisplacementDepth;
        public bool ExternalDisplacementEndPending => externalDisplacementEndPending;
        public Vector3 PendingExternalDisplacement => pendingExternalDisplacement;
        public Vector3 SteeringVelocity => steeringVelocity;
        public Vector3 ImpulseVelocity => impulseVelocity;
        public bool IsAimFacingHeld =>
            Time.time < aimFacingHoldUntil &&
            aimFacingDirection.sqrMagnitude > 0.0001f;
        public Vector3 AimFacingDirection => aimFacingDirection;
        public Vector3 IntentDirection => intentDirection;
        public float IntentSpeed => intentSpeed;
        public bool IntentMovementAllowed => intentMovementAllowed;

        /// <summary>Assembler-facing tuning; call before Initialize.</summary>
        public void ConfigureProfile(HeavyMotorProfile configuredProfile)
        {
            if (configuredProfile == null)
                throw new ArgumentNullException(nameof(configuredProfile));
            if (configuredProfile.weight <= 0f ||
                configuredProfile.acceleration <= 0f ||
                configuredProfile.deceleration <= 0f ||
                configuredProfile.turnRateDegreesPerSecond <= 0f ||
                configuredProfile.impulseDamping <= 0f)
            {
                throw new ArgumentException(
                    "Heavy motor profile values must be positive.",
                    nameof(configuredProfile));
            }
            profile = configuredProfile;
        }

        public void Initialize(float moveSpeed)
        {
            EnsureComponents();
            if (!IsFinite(moveSpeed) || moveSpeed <= 0f)
                throw new ArgumentOutOfRangeException(nameof(moveSpeed));
            if (initialized)
            {
                if (!Mathf.Approximately(initializedRequestedMoveSpeed, moveSpeed))
                {
                    throw new InvalidOperationException(
                        "The initialized heavy motor cannot adopt a different move speed.");
                }
                return;
            }

            initializedRequestedMoveSpeed = Mathf.Max(MinimumMoveSpeed, moveSpeed);
            initialized = true;
            suspended = false;
            if (!corpseMode) ConfigureDynamicBody();
        }

        /// <summary>
        /// Stores the freshest world intent. The motor accelerates toward it
        /// on the next fixed step; it never becomes velocity directly.
        /// </summary>
        public void SetPlanarIntent(
            Vector3 worldDirection, float speed, bool movementAllowed)
        {
            if (!IsFinite(worldDirection))
                throw new ArgumentException("Movement intent must be finite.", nameof(worldDirection));
            if (!IsFinite(speed) || speed < 0f)
                throw new ArgumentOutOfRangeException(nameof(speed));

            worldDirection.y = 0f;
            intentDirection = Vector3.ClampMagnitude(worldDirection, 1f);
            intentSpeed = speed;
            intentMovementAllowed = movementAllowed;
        }

        /// <summary>
        /// Adds a decaying world-space impulse: roll bursts, melee step-in
        /// lunges, and victim shoves. Total travel is roughly
        /// impulse / (impulseDamping * weight). Ignored on corpses.
        /// </summary>
        public void AddImpulse(Vector3 worldImpulse)
        {
            if (!IsFinite(worldImpulse))
                throw new ArgumentException("Impulse must be finite.", nameof(worldImpulse));
            if (corpseMode || suspended) return;
            worldImpulse.y = 0f;
            impulseVelocity += worldImpulse / Mathf.Max(0.1f, profile.weight);
        }

        public void Face(Vector3 worldDirection, bool immediate)
        {
            EnsureComponents();
            if (!IsFinite(worldDirection))
                throw new ArgumentException("Facing direction must be finite.", nameof(worldDirection));

            worldDirection.y = 0f;
            if (worldDirection.sqrMagnitude <= 0.001f) return;

            pendingFaceDirection = worldDirection.normalized;
            pendingFace = true;
            pendingFaceImmediate |= immediate;

            // Roll/spawn snaps are synchronous and become the sole facing
            // authority: an explicit instant snap always ends any aim hold.
            if (immediate && !corpseMode)
            {
                ClearAimHold();
                SnapRotation(Quaternion.LookRotation(pendingFaceDirection, Vector3.up));
                pendingFace = false;
                pendingFaceImmediate = false;
            }
        }

        public void HoldAimFacing(Vector3 worldDir, float seconds)
        {
            EnsureComponents();
            if (!IsFinite(worldDir))
                throw new ArgumentException("Aim direction must be finite.", nameof(worldDir));
            if (!IsFinite(seconds))
                throw new ArgumentOutOfRangeException(nameof(seconds));

            worldDir.y = 0f;
            if (worldDir.sqrMagnitude <= 0.001f) return;

            aimFacingDirection = worldDir.normalized;
            aimFacingHoldUntil = Time.time + Mathf.Max(0f, seconds);
            if (!corpseMode)
                SnapRotation(Quaternion.LookRotation(aimFacingDirection, Vector3.up));
        }

        public float ConstrainExternalDisplacement(Vector3 direction, float distance)
        {
            EnsureComponents();
            if (!IsFinite(direction))
                throw new ArgumentException("Displacement direction must be finite.", nameof(direction));
            if (!IsFinite(distance) || distance < 0f)
                throw new ArgumentOutOfRangeException(nameof(distance));
            if (distance <= 0f || direction.sqrMagnitude <= 0.000001f ||
                !CanSweepLiveBody())
                return distance;

            if (!body.SweepTest(
                    direction.normalized, out RaycastHit hit, distance,
                    QueryTriggerInteraction.Ignore))
                return distance;

            return Mathf.Min(distance,
                Mathf.Max(0f, hit.distance - ExternalDisplacementSkin));
        }

        public Vector3 ConstrainTeleportDestination(Vector3 position, float sampleRadius)
        {
            EnsureComponents();
            if (!IsFinite(position))
                throw new ArgumentException("Teleport destination must be finite.", nameof(position));
            if (!IsFinite(sampleRadius) || sampleRadius < 0f)
                throw new ArgumentOutOfRangeException(nameof(sampleRadius));

            float radius = Mathf.Max(
                sampleRadius,
                capsule != null ? capsule.radius : ExternalDisplacementSkin);
            if (NavMesh.SamplePosition(
                    position, out NavMeshHit hit, radius, NavMesh.AllAreas))
                return hit.position;

            // Teleport-style actions must never strand a body outside the
            // arena's traversable surface. Remaining in place is the only
            // safe result when no NavMesh point is found.
            return body != null ? body.position : transform.position;
        }

        public void BeginExternalDisplacement()
        {
            if (externalDisplacementEndPending)
            {
                // A new owner re-absorbs the still-pending flush tick.
                externalDisplacementEndPending = false;
                externalDisplacementDepth = 1;
                return;
            }
            externalDisplacementDepth++;
        }

        public void Displace(Vector3 displacement, bool keepGrounded)
        {
            if (!IsFinite(displacement))
                throw new ArgumentException("Displacement must be finite.", nameof(displacement));

            if (keepGrounded) displacement.y = 0f;
            pendingExternalDisplacement += displacement;
        }

        public void EndExternalDisplacement()
        {
            if (externalDisplacementDepth <= 0) return;
            if (--externalDisplacementDepth > 0) return;
            if (pendingExternalDisplacement.sqrMagnitude > 0.000001f)
            {
                // Update-driven displacement can enqueue its final delta and
                // end ownership before the next fixed tick. Movement intent
                // stays suppressed through that one consuming tick.
                externalDisplacementEndPending = true;
            }
        }

        public void Stop(bool suspend)
        {
            EnsureComponents();
            suspended = suspend;
            externalDisplacementDepth = 0;
            externalDisplacementEndPending = false;
            pendingExternalDisplacement = Vector3.zero;
            ClearPendingFace();
            ClearAimHold();
            ClearIntent();
            ClearMomentum();
            ClearDynamicBodyVelocity();
        }

        public void Teleport(Vector3 position)
        {
            EnsureComponents();
            if (!IsFinite(position))
                throw new ArgumentException("Teleport destination must be finite.", nameof(position));

            // Respawn teleports are synchronous and must work while the body
            // is kinematic. They clear logical suspension but never exit
            // corpse mode — SetCorpseMode(false) is the only restore path.
            // Displacement nesting depth is preserved for the owning caller.
            if (body != null) body.position = position;
            transform.position = position;
            suspended = false;
            pendingExternalDisplacement = Vector3.zero;
            externalDisplacementEndPending = false;
            ClearPendingFace();
            ClearAimHold();
            ClearIntent();
            ClearMomentum();
            ClearDynamicBodyVelocity();
        }

        public void SetCorpseMode(bool corpse)
        {
            EnsureComponents();
            if (corpse == corpseMode) return;

            if (corpse)
            {
                if (body != null)
                {
                    corpseSavedKinematic = body.isKinematic;
                    corpseSavedUseGravity = body.useGravity;
                    ClearDynamicBodyVelocity();
                    body.isKinematic = true;
                    body.useGravity = false;
                }
                corpseSavedCapsuleEnabled = capsule != null && capsule.enabled;
                if (capsule != null) capsule.enabled = false;
                ClearPendingFace();
                ClearAimHold();
                ClearIntent();
                ClearMomentum();
                corpseMode = true;
                return;
            }

            corpseMode = false;
            if (capsule != null) capsule.enabled = corpseSavedCapsuleEnabled;
            if (body != null)
            {
                body.isKinematic = corpseSavedKinematic;
                body.useGravity = corpseSavedUseGravity;
                ClearDynamicBodyVelocity();
            }
        }

        void FixedUpdate()
        {
            if (!initialized || corpseMode) return;
            EnsureComponents();
            if (body == null) return;

            ConsumeExternalDisplacement();
            bool displacementActive = ExternalDisplacementActive;
            StepMomentum(displacementActive, Time.fixedDeltaTime);
            ApplyFacing(displacementActive);

            // The flush tick after EndExternalDisplacement has now consumed
            // its pending delta; ordinary intent resumes next tick.
            if (externalDisplacementEndPending && externalDisplacementDepth == 0)
                externalDisplacementEndPending = false;
        }

        void ConsumeExternalDisplacement()
        {
            Vector3 displacement = pendingExternalDisplacement;
            pendingExternalDisplacement = Vector3.zero;
            if (displacement.sqrMagnitude <= 0.000001f) return;

            float distance = displacement.magnitude;
            Vector3 direction = displacement / distance;
            float constrainedDistance = ConstrainExternalDisplacement(direction, distance);
            body.position += direction * constrainedDistance;
        }

        /// <summary>
        /// The weight model. Steering velocity chases the intent vector with
        /// bounded acceleration, so a full reversal decays through near-zero
        /// (the plant) before building speed again, and diagonal corrections
        /// carve visible arcs. The impulse channel decays exponentially on
        /// top. Y is preserved for gravity.
        /// </summary>
        void StepMomentum(bool displacementActive, float deltaTime)
        {
            if (body.isKinematic) return;

            Vector3 targetVelocity = Vector3.zero;
            if (!suspended && !displacementActive && intentMovementAllowed)
                targetVelocity = intentDirection * Mathf.Max(0f, intentSpeed);

            bool gainingSpeed = targetVelocity.sqrMagnitude >
                                steeringVelocity.sqrMagnitude;
            float rate = (gainingSpeed ? profile.acceleration : profile.deceleration) /
                         Mathf.Max(0.1f, profile.weight);
            steeringVelocity = Vector3.MoveTowards(
                steeringVelocity, targetVelocity, rate * deltaTime);

            impulseVelocity *= Mathf.Exp(-profile.impulseDamping * deltaTime);
            if (impulseVelocity.sqrMagnitude <
                ImpulseSleepThreshold * ImpulseSleepThreshold)
                impulseVelocity = Vector3.zero;

            Vector3 planar = steeringVelocity + impulseVelocity;
            Vector3 current = body.linearVelocity;
            body.linearVelocity = new Vector3(planar.x, current.y, planar.z);
            // Solver contacts can still accumulate angular velocity even with
            // frozen rotation constraints on older serialized bodies; rotation
            // is exclusively MoveRotation-owned, so discard it.
            if (body.angularVelocity.sqrMagnitude > 0f)
                body.angularVelocity = Vector3.zero;
        }

        /// <summary>
        /// Single rotation owner. Aim facing wins while its hold window is
        /// active, then an explicit one-shot Face request, then movement
        /// facing toward the steering direction so the body visibly leans
        /// through carve arcs. All rotation goes through
        /// Rigidbody.MoveRotation at the profile turn rate.
        /// </summary>
        void ApplyFacing(bool displacementActive)
        {
            bool faceRequested = pendingFace;
            Vector3 faceDirection = pendingFaceDirection;
            bool faceImmediate = pendingFaceImmediate;
            pendingFace = false;
            pendingFaceImmediate = false;

            if (displacementActive) return;

            Vector3 target;
            bool snap = false;
            if (IsAimFacingHeld)
            {
                target = aimFacingDirection;
            }
            else if (faceRequested)
            {
                target = faceDirection;
                snap = faceImmediate;
            }
            else
            {
                if (suspended || !intentMovementAllowed) return;
                // Face where the body is actually going once moving; fall
                // back to raw intent from a standstill so turns begin
                // immediately.
                Vector3 heading = steeringVelocity;
                heading.y = 0f;
                if (heading.sqrMagnitude <= 0.04f) heading = intentDirection;
                if (heading.sqrMagnitude <= 0.0001f) return;
                target = heading.normalized;
            }

            Quaternion goal = Quaternion.LookRotation(target, Vector3.up);
            body.MoveRotation(snap
                ? goal
                : Quaternion.RotateTowards(
                    body.rotation, goal,
                    profile.turnRateDegreesPerSecond * Time.fixedDeltaTime));
        }

        /// <summary>
        /// Synchronous snap for spawn/roll/aim commits. MoveRotation is the
        /// physics write; the transform is synchronized as well so the snap
        /// is observable before the next simulation step.
        /// </summary>
        void SnapRotation(Quaternion goal)
        {
            if (body != null && !body.isKinematic) body.MoveRotation(goal);
            transform.rotation = goal;
        }

        void ConfigureDynamicBody()
        {
            if (body == null) return;
            body.isKinematic = false;
            body.useGravity = true;
            // All rotation axes frozen: this motor is the single rotation
            // owner via MoveRotation, and a free Y axis lets collision
            // impulses leave the body spinning once move input stops.
            body.constraints = RigidbodyConstraints.FreezeRotation;
            body.interpolation = RigidbodyInterpolation.Interpolate;
            body.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
        }

        void ClearIntent()
        {
            intentDirection = Vector3.zero;
            intentSpeed = 0f;
            intentMovementAllowed = false;
        }

        void ClearMomentum()
        {
            steeringVelocity = Vector3.zero;
            impulseVelocity = Vector3.zero;
        }

        void ClearPendingFace()
        {
            pendingFace = false;
            pendingFaceImmediate = false;
            pendingFaceDirection = Vector3.zero;
        }

        void ClearAimHold()
        {
            aimFacingDirection = Vector3.zero;
            aimFacingHoldUntil = 0f;
        }

        void ClearDynamicBodyVelocity()
        {
            if (body == null || body.isKinematic) return;
            body.linearVelocity = Vector3.zero;
            body.angularVelocity = Vector3.zero;
        }

        bool CanSweepLiveBody()
        {
            return body != null && capsule != null &&
                   gameObject.activeInHierarchy && capsule.enabled &&
                   !capsule.isTrigger && !body.isKinematic;
        }

        bool ExternalDisplacementActive =>
            externalDisplacementDepth > 0 || externalDisplacementEndPending;

        void EnsureComponents()
        {
            if (body == null) body = GetComponent<Rigidbody>();
            if (capsule == null) capsule = GetComponent<CapsuleCollider>();
        }

        static bool IsFinite(Vector3 value)
        {
            return IsFinite(value.x) && IsFinite(value.y) && IsFinite(value.z);
        }

        static bool IsFinite(float value)
        {
            return !float.IsNaN(value) && !float.IsInfinity(value);
        }
    }
}
