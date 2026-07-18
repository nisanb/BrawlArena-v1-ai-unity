using System;
using Invector.vCharacterController;
using UnityEngine;
using UnityEngine.AI;

namespace BrawlArena
{
    /// <summary>
    /// Rigidbody-backed Brawl motor for the project-owned Invector stack.
    /// BrawlerController writes world-space intent into this component, while
    /// InvectorShooterMeleeInputAdapter remains the sole fixed-step scheduler.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class InvectorBrawlerMotor : MonoBehaviour, IBrawlerMotor
    {
        const float MinimumMoveSpeed = 0.1f;
        const float ExternalDisplacementSkin = 0.06f;

        [SerializeField, HideInInspector]
        BrawlInvectorThirdPersonController configuredController;

        [SerializeField, HideInInspector]
        InvectorShooterMeleeInputAdapter configuredScheduler;

        [SerializeField, HideInInspector]
        Rigidbody configuredBody;

        [SerializeField, HideInInspector]
        CapsuleCollider configuredCapsule;

        [SerializeField, HideInInspector]
        InvectorBrawlerNavigation configuredNavigationPlanner;

        float initializedMoveSpeed = 1f;
        float initializedRequestedMoveSpeed = 1f;
        Vector3 bufferedWorldIntent;
        float bufferedRequestedSpeed;
        bool bufferedMovementAllowed;
        bool initialized;
        bool suspended;

        int externalDisplacementDepth;
        Vector3 pendingExternalDisplacement;
        bool externalDisplacementEndPending;
        bool externalDisplacementOwnsLocks;
        bool externalSavedLockMovement;
        bool externalSavedLockRotation;

        Vector3 pendingFacingDirection;
        bool pendingFacing;
        bool pendingFacingImmediate;
        bool scheduledFacingOwnsRotationLock;
        bool scheduledFacingSavedRotationLock;

        bool controllerConfigurationCaptured;
        vThirdPersonMotor.LocomotionType savedLocomotionType;
        bool savedMoveToDirectionInFree;
        bool savedRotateByWorld;
        bool savedLockSetMoveSpeed;
        float savedMoveSpeed;
        float savedSpeedMultiplier;
        bool savedIsStrafing;
        bool savedIsSprinting;
        bool savedIsCrouching;

        bool scheduledStepOpen;
        int scheduledPrepareCount;
        int scheduledCompleteCount;
        int appliedDisplacementCount;

        public Vector3 Velocity
        {
            get
            {
                if (configuredBody == null || configuredBody.isKinematic)
                    return Vector3.zero;
                return configuredBody.linearVelocity;
            }
        }

        public float CollisionRadius
        {
            get
            {
                if (configuredCapsule == null) return 0.65f;
                Vector3 scale = configuredCapsule.transform.lossyScale;
                float planarScale = Mathf.Max(Mathf.Abs(scale.x), Mathf.Abs(scale.z));
                return Mathf.Max(0.35f, configuredCapsule.radius * planarScale);
            }
        }

        public bool IsGrounded =>
            HasConfiguredReferences && configuredController.enabled &&
            configuredCapsule.enabled && configuredController.isGrounded;

        public bool IsDormantConfigured =>
            HasConfiguredReferences && !enabled && !initialized && !suspended &&
            externalDisplacementDepth == 0 && !externalDisplacementEndPending &&
            !scheduledStepOpen &&
            bufferedWorldIntent == Vector3.zero &&
            pendingExternalDisplacement == Vector3.zero &&
            !configuredController.enabled && !configuredScheduler.enabled &&
            configuredBody.isKinematic && !configuredBody.useGravity &&
            configuredBody.constraints == RigidbodyConstraints.FreezeAll &&
            !configuredCapsule.enabled &&
            (!HasConfiguredNavigationPlanner ||
             configuredNavigationPlanner.IsDormantConfigured);

        public bool IsSuspended => suspended;
        public bool IsInitialized => initialized;
        public int ExternalDisplacementDepth => externalDisplacementDepth;
        public bool ExternalDisplacementEndPending => externalDisplacementEndPending;
        public Vector3 BufferedWorldIntent => bufferedWorldIntent;
        public Vector3 PendingExternalDisplacement => pendingExternalDisplacement;
        public int ScheduledPrepareCount => scheduledPrepareCount;
        public int ScheduledCompleteCount => scheduledCompleteCount;
        public int AppliedDisplacementCount => appliedDisplacementCount;
        public bool HasConfiguredNavigationPlanner =>
            configuredNavigationPlanner != null &&
            configuredNavigationPlanner.gameObject == gameObject;
        internal bool IsScheduledStepOpen => scheduledStepOpen;

        internal bool IsReadyForSchedulerBridge =>
            initialized && enabled && HasConfiguredReferences;

        internal bool IsConfiguredForScheduler(
            InvectorShooterMeleeInputAdapter scheduler)
        {
            return scheduler != null && HasConfiguredReferences &&
                   configuredScheduler == scheduler;
        }

        /// <summary>
        /// Builder-facing configuration. It binds the exact project scheduler,
        /// controller, and body on this root without activating any of them.
        /// </summary>
        public void ConfigureDormant(
            BrawlInvectorThirdPersonController controller,
            InvectorShooterMeleeInputAdapter scheduler,
            Rigidbody body,
            CapsuleCollider capsule)
        {
            RequireSameRoot(controller, nameof(controller));
            RequireSameRoot(scheduler, nameof(scheduler));
            RequireSameRoot(body, nameof(body));
            RequireSameRoot(capsule, nameof(capsule));
            RequireSolePhysicalAuthority();

            configuredController = controller;
            configuredScheduler = scheduler;
            configuredBody = body;
            configuredCapsule = capsule;
            configuredNavigationPlanner = null;
            controllerConfigurationCaptured = false;
            scheduledPrepareCount = 0;
            scheduledCompleteCount = 0;
            appliedDisplacementCount = 0;
            ResetRuntimeState(true);
            enabled = false;
        }

        /// <summary>
        /// Optionally binds the project-owned, transform-neutral AI planner.
        /// Human production variants deliberately leave this reference empty.
        /// </summary>
        public void ConfigureNavigationPlanner(InvectorBrawlerNavigation navigation)
        {
            RequireSameRoot(navigation, nameof(navigation));
            if (!navigation.IsDormantConfigured)
            {
                throw new InvalidOperationException(
                    "Configure the Invector navigation planner in its dormant posture first.");
            }

            InvectorBrawlerNavigation[] planners =
                GetComponents<InvectorBrawlerNavigation>();
            if (planners.Length != 1 || planners[0] != navigation)
            {
                throw new InvalidOperationException(
                    "The Invector AI motor requires exactly one selected root navigation planner.");
            }

            configuredNavigationPlanner = navigation;
        }

        public void Initialize(float moveSpeed)
        {
            EnsureConfigured();
            if (!IsFinite(moveSpeed) || moveSpeed <= 0f)
                throw new ArgumentOutOfRangeException(nameof(moveSpeed));
            if (initialized)
            {
                if (!Mathf.Approximately(initializedRequestedMoveSpeed, moveSpeed))
                {
                    throw new InvalidOperationException(
                        "The initialized Invector motor cannot adopt a different move speed.");
                }
                return;
            }
            if (configuredController.freeSpeed == null ||
                configuredController.strafeSpeed == null)
            {
                throw new InvalidOperationException(
                    "The configured Invector controller requires both free and strafe movement profiles.");
            }

            CaptureControllerConfiguration();
            initializedRequestedMoveSpeed = moveSpeed;
            initializedMoveSpeed = Mathf.Max(MinimumMoveSpeed, moveSpeed);
            initialized = true;
            suspended = false;
            ConfigureControllerForWorldIntent();
            ClearControllerIntent();
        }

        /// <summary>
        /// Clears equality evidence between isolated activations. Callers must
        /// first return the motor and scheduler to their dormant state.
        /// </summary>
        public void ResetRuntimeTrace()
        {
            EnsureConfigured();
            if (enabled || initialized || scheduledStepOpen ||
                configuredScheduler.RuntimeSchedulingEnabled)
            {
                throw new InvalidOperationException(
                    "Return the Invector motor and scheduler dormant before resetting runtime trace.");
            }

            scheduledPrepareCount = 0;
            scheduledCompleteCount = 0;
            appliedDisplacementCount = 0;
        }

        /// <summary>
        /// Stores project-produced world intent. It performs no input polling
        /// and does not move the body outside the approved fixed scheduler.
        /// </summary>
        public void SetPlanarIntent(
            Vector3 worldDirection, float speed, bool movementAllowed)
        {
            EnsureConfigured();
            if (!IsFinite(worldDirection))
                throw new ArgumentException("Movement intent must be finite.", nameof(worldDirection));
            if (!IsFinite(speed) || speed < 0f)
                throw new ArgumentOutOfRangeException(nameof(speed));

            worldDirection.y = 0f;
            bufferedWorldIntent = Vector3.ClampMagnitude(worldDirection, 1f);
            bufferedRequestedSpeed = speed;
            bufferedMovementAllowed = movementAllowed;
        }

        public void Face(Vector3 worldDirection, bool immediate)
        {
            EnsureConfigured();
            if (!IsFinite(worldDirection))
                throw new ArgumentException("Facing direction must be finite.", nameof(worldDirection));

            worldDirection.y = 0f;
            if (worldDirection.sqrMagnitude <= 0.001f) return;

            pendingFacingDirection = worldDirection.normalized;
            pendingFacing = true;
            pendingFacingImmediate |= immediate;

            // The immediate contract remains synchronous for a live body. A
            // dormant prefab never moves merely because intent was buffered.
            if (immediate && CanWriteLiveBody())
            {
                ApplyFacing(true);
                pendingFacing = false;
                pendingFacingImmediate = false;
            }
        }

        public float ConstrainExternalDisplacement(Vector3 direction, float distance)
        {
            EnsureConfigured();
            if (!IsFinite(direction))
                throw new ArgumentException("Displacement direction must be finite.", nameof(direction));
            if (!IsFinite(distance) || distance < 0f)
                throw new ArgumentOutOfRangeException(nameof(distance));
            if (distance <= 0f || direction.sqrMagnitude <= 0.000001f ||
                !CanSweepLiveBody())
                return distance;

            if (!configuredBody.SweepTest(
                    direction.normalized, out RaycastHit hit, distance,
                    QueryTriggerInteraction.Ignore))
                return distance;

            return Mathf.Min(distance,
                Mathf.Max(0f, hit.distance - ExternalDisplacementSkin));
        }

        public Vector3 ConstrainTeleportDestination(Vector3 position, float sampleRadius)
        {
            EnsureConfigured();
            if (!IsFinite(position))
                throw new ArgumentException("Teleport destination must be finite.", nameof(position));
            if (!IsFinite(sampleRadius) || sampleRadius < 0f)
                throw new ArgumentOutOfRangeException(nameof(sampleRadius));

            float radius = Mathf.Max(
                sampleRadius,
                configuredCapsule != null
                    ? configuredCapsule.radius
                    : ExternalDisplacementSkin);
            if (NavMesh.SamplePosition(
                    position, out NavMeshHit hit, radius, NavMesh.AllAreas))
                return hit.position;

            // Ward steps and other teleport-style actions must never strand an
            // Invector body outside the arena's traversable surface. Remaining
            // in place is the only safe result when no NavMesh point is found.
            return configuredBody.position;
        }

        public void BeginExternalDisplacement()
        {
            EnsureConfigured();
            if (externalDisplacementEndPending)
            {
                externalDisplacementEndPending = false;
                externalDisplacementDepth = 1;
                return;
            }
            if (externalDisplacementDepth++ > 0) return;

            externalSavedLockMovement = configuredController.lockMovement;
            externalSavedLockRotation = configuredController.lockRotation;
            externalDisplacementOwnsLocks = true;
            configuredController.lockMovement = true;
            configuredController.lockRotation = true;
            ClearConfiguredPlannerPath();
        }

        public void Displace(Vector3 displacement, bool keepGrounded)
        {
            EnsureConfigured();
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
                // Update-driven Ward Step can enqueue its final delta and end
                // ownership before the next fixed scheduler tick. Retain the
                // movement/rotation locks through that one consuming tick.
                externalDisplacementEndPending = true;
                return;
            }
            RestoreExternalDisplacementLocks();
            SynchronizeConfiguredPlanner(false);
        }

        public void Stop(bool suspend)
        {
            EnsureConfigured();
            ReleaseScheduledFacingLock();
            scheduledStepOpen = false;
            RestoreExternalDisplacementLocks();
            externalDisplacementDepth = 0;
            externalDisplacementEndPending = false;
            pendingExternalDisplacement = Vector3.zero;
            suspended = suspend;
            ClearPendingFacing();
            ClearBufferedIntent();
            ClearControllerIntent();
            ClearDynamicBodyVelocity();
            ClearConfiguredPlannerPath();
            SynchronizeConfiguredPlanner(false);
        }

        public void Teleport(Vector3 position)
        {
            EnsureConfigured();
            if (!IsFinite(position))
                throw new ArgumentException("Teleport destination must be finite.", nameof(position));

            // Respawn/dash teleports are synchronous. They clear logical
            // suspension but deliberately preserve displacement nesting and
            // its saved lock state until the owning caller ends it.
            configuredBody.position = position;
            transform.position = position;
            suspended = false;
            pendingExternalDisplacement = Vector3.zero;
            if (externalDisplacementEndPending)
            {
                externalDisplacementEndPending = false;
                RestoreExternalDisplacementLocks();
            }
            ClearPendingFacing();
            ClearBufferedIntent();
            ClearControllerIntent();
            ClearDynamicBodyVelocity();
            if (HasConfiguredNavigationPlanner)
                configuredNavigationPlanner.WarpPlanner(position);
        }

        /// <summary>
        /// Called only by InvectorShooterMeleeInputAdapter immediately before
        /// its one inherited FixedUpdate scheduler invocation.
        /// </summary>
        internal void PrepareScheduledStep()
        {
            EnsureScheduledStackReady();
            if (scheduledStepOpen)
                throw new InvalidOperationException("The Invector motor already has an open scheduled step.");

            scheduledStepOpen = true;
            scheduledPrepareCount++;
            try
            {
                ConfigureControllerForWorldIntent();
                ConsumeExternalDisplacement();

                Vector3 scheduledInput = Vector3.zero;
                if (!suspended && !ExternalDisplacementActive && bufferedMovementAllowed)
                {
                    float speedScale = bufferedRequestedSpeed /
                        Mathf.Max(MinimumMoveSpeed, initializedMoveSpeed);
                    scheduledInput = Vector3.ClampMagnitude(
                        bufferedWorldIntent * Mathf.Max(0f, speedScale), 1f);
                }

                configuredController.input = scheduledInput;
                configuredController.moveDirection = scheduledInput;
                if (scheduledInput == Vector3.zero)
                    configuredController.targetVelocity = Vector3.zero;

                // lockSetMoveSpeed preserves Brawl's exact physical speed, so
                // vendor ControlLocomotionType skips both of its speed helpers.
                // Populate controller-owned locomotion scalars here; the one
                // inherited scheduler still performs the Animator writes.
                configuredController.SetAnimatorMoveSpeed(
                    configuredController.freeSpeed);

                if (pendingFacing && !ExternalDisplacementActive)
                {
                    scheduledFacingSavedRotationLock = configuredController.lockRotation;
                    configuredController.lockRotation = true;
                    scheduledFacingOwnsRotationLock = true;
                    ApplyFacing(pendingFacingImmediate);
                }
            }
            catch
            {
                scheduledPrepareCount--;
                ReleaseScheduledFacingLock();
                scheduledStepOpen = false;
                throw;
            }
        }

        /// <summary>
        /// Completes the adapter-owned scheduled step and releases only the
        /// transient explicit-facing lock acquired by this motor.
        /// </summary>
        internal void CompleteScheduledStep()
        {
            if (!scheduledStepOpen)
                throw new InvalidOperationException("The Invector motor has no open scheduled step to complete.");

            ReleaseScheduledFacingLock();
            pendingFacing = false;
            pendingFacingImmediate = false;
            if (externalDisplacementEndPending)
            {
                externalDisplacementEndPending = false;
                RestoreExternalDisplacementLocks();
            }
            scheduledStepOpen = false;
            scheduledCompleteCount++;
            SynchronizeConfiguredPlanner(false);
        }

        void ClearConfiguredPlannerPath()
        {
            if (!HasConfiguredNavigationPlanner ||
                !configuredNavigationPlanner.RuntimePlanningOpen)
                return;
            configuredNavigationPlanner.ClearPath();
        }

        void SynchronizeConfiguredPlanner(bool clearPath)
        {
            if (!HasConfiguredNavigationPlanner || configuredBody == null ||
                !configuredNavigationPlanner.RuntimePlanningOpen)
                return;
            configuredNavigationPlanner.SynchronizePlannerPosition(
                configuredBody.position, clearPath);
        }

        void ReleaseScheduledFacingLock()
        {
            if (scheduledFacingOwnsRotationLock && externalDisplacementDepth == 0)
                configuredController.lockRotation = scheduledFacingSavedRotationLock;
            scheduledFacingOwnsRotationLock = false;
            scheduledFacingSavedRotationLock = false;
        }

        /// <summary>
        /// Clears scheduler-owned state before the lab/assembler freezes the
        /// Rigidbody and returns the generated pilot to its dormant posture.
        /// </summary>
        internal void ReturnDormant()
        {
            ResetRuntimeState(true);
            enabled = false;
        }

        void OnDisable()
        {
            if (HasConfiguredReferences)
            {
                if (configuredScheduler.RuntimeSchedulingEnabled)
                    configuredScheduler.SetRuntimeSchedulingEnabled(false);
                ResetRuntimeState(true);
            }
        }

        void ConsumeExternalDisplacement()
        {
            Vector3 displacement = pendingExternalDisplacement;
            pendingExternalDisplacement = Vector3.zero;
            if (displacement.sqrMagnitude <= 0.000001f) return;

            float distance = displacement.magnitude;
            Vector3 direction = displacement / distance;
            float constrainedDistance = ConstrainExternalDisplacement(direction, distance);
            configuredBody.position += direction * constrainedDistance;
            appliedDisplacementCount++;
        }

        /// <summary>
        /// Non-immediate facing is bounded at the shared combat turn rate
        /// instead of an exponential Slerp, so a committed swing reads as a
        /// real weighted turn rather than a snap. Immediate remains a true
        /// instant snap for spawn/teleport and Ward Step.
        /// </summary>
        void ApplyFacing(bool immediate)
        {
            Quaternion target = Quaternion.LookRotation(
                pendingFacingDirection, Vector3.up);
            Quaternion rotation = immediate
                ? target
                : Quaternion.RotateTowards(
                    configuredBody.rotation, target,
                    MobileCombatRules.CombatTurnRateDegreesPerSecond * Time.fixedDeltaTime);
            configuredBody.rotation = rotation;
            transform.rotation = rotation;
        }

        void ConfigureControllerForWorldIntent()
        {
            configuredController.locomotionType = vThirdPersonMotor.LocomotionType.OnlyFree;
            configuredController.moveToDirectionInFree = true;
            configuredController.rotateByWorld = true;
            configuredController.isStrafing = false;
            configuredController.isSprinting = false;
            configuredController.isCrouching = false;
            configuredController.lockSetMoveSpeed = true;
            configuredController.moveSpeed = initializedMoveSpeed;
            configuredController.speedMultiplier = 1f;
        }

        void CaptureControllerConfiguration()
        {
            if (controllerConfigurationCaptured) return;
            savedLocomotionType = configuredController.locomotionType;
            savedMoveToDirectionInFree = configuredController.moveToDirectionInFree;
            savedRotateByWorld = configuredController.rotateByWorld;
            savedLockSetMoveSpeed = configuredController.lockSetMoveSpeed;
            savedMoveSpeed = configuredController.moveSpeed;
            savedSpeedMultiplier = configuredController.speedMultiplier;
            savedIsStrafing = configuredController.isStrafing;
            savedIsSprinting = configuredController.isSprinting;
            savedIsCrouching = configuredController.isCrouching;
            controllerConfigurationCaptured = true;
        }

        void RestoreControllerConfiguration()
        {
            if (!controllerConfigurationCaptured) return;
            configuredController.locomotionType = savedLocomotionType;
            configuredController.moveToDirectionInFree = savedMoveToDirectionInFree;
            configuredController.rotateByWorld = savedRotateByWorld;
            configuredController.lockSetMoveSpeed = savedLockSetMoveSpeed;
            configuredController.moveSpeed = savedMoveSpeed;
            configuredController.speedMultiplier = savedSpeedMultiplier;
            configuredController.isStrafing = savedIsStrafing;
            configuredController.isSprinting = savedIsSprinting;
            configuredController.isCrouching = savedIsCrouching;
            controllerConfigurationCaptured = false;
        }

        void ResetRuntimeState(bool restoreControllerConfiguration)
        {
            if (scheduledFacingOwnsRotationLock && externalDisplacementDepth == 0)
                configuredController.lockRotation = scheduledFacingSavedRotationLock;
            scheduledFacingOwnsRotationLock = false;
            scheduledFacingSavedRotationLock = false;
            RestoreExternalDisplacementLocks();

            scheduledStepOpen = false;
            initialized = false;
            suspended = false;
            externalDisplacementDepth = 0;
            externalDisplacementEndPending = false;
            pendingExternalDisplacement = Vector3.zero;
            pendingFacing = false;
            pendingFacingImmediate = false;
            pendingFacingDirection = Vector3.zero;
            ClearBufferedIntent();
            ClearControllerIntent();
            ClearDynamicBodyVelocity();
            if (restoreControllerConfiguration)
                RestoreControllerConfiguration();
        }

        void RestoreExternalDisplacementLocks()
        {
            if (configuredController == null || !externalDisplacementOwnsLocks) return;
            configuredController.lockMovement = externalSavedLockMovement;
            configuredController.lockRotation = externalSavedLockRotation;
            externalDisplacementOwnsLocks = false;
            externalSavedLockMovement = false;
            externalSavedLockRotation = false;
        }

        void ClearBufferedIntent()
        {
            bufferedWorldIntent = Vector3.zero;
            bufferedRequestedSpeed = 0f;
            bufferedMovementAllowed = false;
        }

        void ClearPendingFacing()
        {
            pendingFacing = false;
            pendingFacingImmediate = false;
            pendingFacingDirection = Vector3.zero;
        }

        void ClearControllerIntent()
        {
            if (configuredController == null) return;
            configuredController.input = Vector3.zero;
            configuredController.inputSmooth = Vector3.zero;
            configuredController.moveDirection = Vector3.zero;
            configuredController.targetVelocity = Vector3.zero;
            configuredController.verticalSpeed = 0f;
            configuredController.horizontalSpeed = 0f;
            configuredController.inputMagnitude = 0f;
        }

        void ClearDynamicBodyVelocity()
        {
            if (configuredBody == null || configuredBody.isKinematic) return;
            configuredBody.linearVelocity = Vector3.zero;
            configuredBody.angularVelocity = Vector3.zero;
        }

        bool CanWriteLiveBody()
        {
            return initialized && enabled && gameObject.activeInHierarchy &&
                   configuredController.enabled && configuredScheduler.enabled &&
                   configuredScheduler.RuntimeSchedulingEnabled &&
                   configuredCapsule.enabled && !configuredBody.isKinematic;
        }

        bool CanSweepLiveBody()
        {
            return gameObject.activeInHierarchy && configuredCapsule.enabled &&
                   !configuredCapsule.isTrigger && !configuredBody.isKinematic;
        }

        bool ExternalDisplacementActive =>
            externalDisplacementDepth > 0 || externalDisplacementEndPending;

        void EnsureScheduledStackReady()
        {
            EnsureConfigured();
            if (!initialized)
                throw new InvalidOperationException("Initialize the Invector motor before scheduling it.");
            if (!CanWriteLiveBody() ||
                (configuredBody.constraints & RigidbodyConstraints.FreezeAll) ==
                RigidbodyConstraints.FreezeAll)
            {
                throw new InvalidOperationException(
                    "The Invector motor scheduler requires one active controller, scheduler, capsule, and dynamic Rigidbody.");
            }
        }

        void EnsureConfigured()
        {
            if (!HasConfiguredReferences)
                throw new InvalidOperationException(
                    "Configure the Invector motor with exact same-root project components first.");
        }

        bool HasConfiguredReferences =>
            configuredController != null && configuredController.gameObject == gameObject &&
            configuredScheduler != null && configuredScheduler.gameObject == gameObject &&
            configuredBody != null && configuredBody.gameObject == gameObject &&
            configuredCapsule != null && configuredCapsule.gameObject == gameObject;

        void RequireSolePhysicalAuthority()
        {
            MonoBehaviour[] behaviours = GetComponents<MonoBehaviour>();
            for (int i = 0; i < behaviours.Length; i++)
            {
                if (behaviours[i] != this && behaviours[i] is IBrawlerMotor)
                {
                    throw new InvalidOperationException(
                        "The Invector root already contains another IBrawlerMotor authority.");
                }
            }

            if (GetComponent<CharacterController>() != null)
                throw new InvalidOperationException(
                    "An Invector Rigidbody motor cannot share its root with CharacterController.");

            Component[] components = GetComponents<Component>();
            for (int i = 0; i < components.Length; i++)
            {
                if (components[i] != null &&
                    components[i].GetType().FullName == "UnityEngine.AI.NavMeshAgent")
                {
                    throw new InvalidOperationException(
                        "An Invector Rigidbody motor cannot share Transform ownership with NavMeshAgent.");
                }
            }
        }

        void RequireSameRoot(Component component, string parameterName)
        {
            if (component == null) throw new ArgumentNullException(parameterName);
            if (component.gameObject != gameObject)
            {
                throw new ArgumentException(
                    "Every Invector motor component must be installed on the same root.",
                    parameterName);
            }
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
