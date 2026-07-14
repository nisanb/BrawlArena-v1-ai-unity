using System;
using System.Linq;
using Invector.vCharacterController;
using Invector.vMelee;
using Invector.vShooter;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;

namespace BrawlArena
{
    /// <summary>
    /// Activates and continuously audits the one runnable Invector instance in
    /// the disposable Phase 3B scene. This component refuses every other scene
    /// and is never installed on the dormant prefab or a production actor.
    /// </summary>
    [DefaultExecutionOrder(-10000)]
    [DisallowMultipleComponent]
    public sealed class InvectorPhase3BLabController : MonoBehaviour
    {
        public const string LabScenePath = "Assets/Scenes/InvectorMigrationLab.unity";

        public static InvectorPhase3BLabController LiveInstance { get; private set; }

        [SerializeField, HideInInspector]
        GameObject pilotRoot;

        [SerializeField, HideInInspector]
        BrawlCamera brawlCamera;

        bool activated;
        bool authorityChecksPassed;
        bool healthBaselineCaptured;
        bool basicAttackPending;
        bool superPending;
        bool hitReactionPending;
        bool observedBasicAttackState;
        bool observedSuperState;
        bool observedHitReactionTransition;
        bool staticSceneOwnershipValidated;
        string failureMessage = string.Empty;
        float fixedDeltaTimeAtActivation;
        float healthAtActivation;
        Vector3 positionAtActivation;
        float maximumPlanarDisplacement;
        int authorityValidationPassCount;
        int groundedObservationCount;
        int observedHitReactionStateHash;

        static readonly int[] ApprovedHitReactionStateHashes =
        {
            Animator.StringToHash("FullBody.Hit Recoil.recoil_hard"),
            Animator.StringToHash("FullBody.Hit Recoil.recoil_low"),
            Animator.StringToHash("FullBody.Hit Recoil.recoil_unarmed"),
        };

        BrawlInvectorThirdPersonController controller;
        InvectorShooterMeleeInputAdapter input;
        InvectorBrawlerMotor motor;
        InvectorBrawlerAnimationDriver animationDriver;
        InvectorBrawlerWeaponPresentation weaponPresentation;
        Animator animator;
        Rigidbody body;
        CapsuleCollider capsule;
        vShooterManager shooterManager;
        BrawlInvectorMeleePresentationManager meleeManager;

        public bool Activated => activated;
        public bool AuthorityChecksPassed => authorityChecksPassed && string.IsNullOrEmpty(failureMessage);
        public string FailureMessage => failureMessage;
        public float MaximumPlanarDisplacement => maximumPlanarDisplacement;
        public int AuthorityValidationPassCount => authorityValidationPassCount;
        public int GroundedObservationCount => groundedObservationCount;
        public bool ObservedBasicAttackState => observedBasicAttackState;
        public bool ObservedSuperState => observedSuperState;
        public bool ObservedHitReactionTransition => observedHitReactionTransition;
        public int ObservedHitReactionStateHash => observedHitReactionStateHash;
        public bool StaticSceneOwnershipValidated => staticSceneOwnershipValidated;
        public GameObject PilotRoot => pilotRoot;
        public BrawlCamera CameraAuthority => brawlCamera;
        public BrawlInvectorThirdPersonController Controller => controller;
        public InvectorShooterMeleeInputAdapter InputAdapter => input;
        public InvectorBrawlerMotor Motor => motor;
        public InvectorBrawlerAnimationDriver AnimationDriver => animationDriver;
        public InvectorBrawlerWeaponPresentation WeaponPresentation => weaponPresentation;
        public BrawlInvectorMeleePresentationManager MeleePresentationManager => meleeManager;

        public bool IsLiveGateReady =>
            Activated && AuthorityChecksPassed &&
            input != null && input.IsRuntimeStackReady && input.IsPresentationStackReady &&
            (input.MovementFeedMode == InvectorMovementFeedMode.LabProjectAction ||
             motor != null && motor.isActiveAndEnabled && motor.IsInitialized) &&
            animationDriver != null && animationDriver.PresentationRequestsEnabled &&
            weaponPresentation != null && weaponPresentation.LabRuntimeEnabled;

        /// <summary>Builder-facing configuration for the generated lab only.</summary>
        public void Configure(GameObject configuredPilot, BrawlCamera configuredCamera)
        {
            if (configuredPilot == null)
                throw new ArgumentNullException(nameof(configuredPilot));
            if (configuredCamera == null)
                throw new ArgumentNullException(nameof(configuredCamera));
            if (configuredPilot.scene != gameObject.scene || configuredCamera.gameObject.scene != gameObject.scene)
            {
                throw new ArgumentException(
                    "The Phase 3B controller, pilot, and BrawlCamera must share the generated lab scene.");
            }

            pilotRoot = configuredPilot;
            brawlCamera = configuredCamera;
        }

        void Start()
        {
            if (!Application.isPlaying)
                return;

            try
            {
                if (LiveInstance != null && LiveInstance != this)
                    throw new InvalidOperationException("A second Phase 3B live gate already exists.");

                LiveInstance = this;
                ActivateLabInstance(InvectorMovementFeedMode.LabProjectAction, 0f);
            }
            catch (Exception exception)
            {
                FailClosed("Activation failed: " + exception.Message);
                Debug.LogException(exception, this);
            }
        }

        void LateUpdate()
        {
            if (!activated || !string.IsNullOrEmpty(failureMessage))
                return;

            try
            {
                ValidateContinuousAuthority();
                SamplePresentationAndMovement();
                authorityChecksPassed = true;
                authorityValidationPassCount++;
            }
            catch (Exception exception)
            {
                FailClosed("Continuous authority check failed: " + exception.Message);
                Debug.LogException(exception, this);
            }
        }

        void OnDestroy()
        {
            if (Application.isPlaying && activated)
                DeactivateLabInstance();

            if (Application.isPlaying && fixedDeltaTimeAtActivation > 0f &&
                !Mathf.Approximately(Time.fixedDeltaTime, fixedDeltaTimeAtActivation))
            {
                Time.fixedDeltaTime = fixedDeltaTimeAtActivation;
            }

            if (LiveInstance == this)
                LiveInstance = null;
        }

        void OnDisable()
        {
            if (Application.isPlaying && activated)
                DeactivateLabInstance();
        }

        /// <summary>
        /// Deterministic Play-mode rollback to the Phase 3A dormant instance
        /// state. The prefab asset is never changed.
        /// </summary>
        public void DeactivateLabInstance()
        {
            DeactivatePilotStack();
            activated = false;
            authorityChecksPassed = false;
        }

        /// <summary>
        /// Reopens the already-generated lab on the production-safe buffered
        /// world-intent feed. The ordinary Phase 3B Start path remains on the
        /// project action and must be deactivated cleanly before this probe.
        /// </summary>
        public void ActivateBufferedMotorPath(float moveSpeed)
        {
            if (!Application.isPlaying)
                throw new InvalidOperationException("The buffered motor lab can only activate in Play mode.");
            if (LiveInstance != this)
                throw new InvalidOperationException("Only the registered generated lab gate may activate the motor path.");
            if (activated)
                throw new InvalidOperationException("Deactivate the current lab feed before selecting the buffered motor.");
            if (!string.IsNullOrEmpty(failureMessage))
                throw new InvalidOperationException("A failed lab gate cannot be reopened in the same Play-mode session.");
            if (float.IsNaN(moveSpeed) || float.IsInfinity(moveSpeed) || moveSpeed <= 0f)
                throw new ArgumentOutOfRangeException(nameof(moveSpeed));

            try
            {
                ActivateLabInstance(InvectorMovementFeedMode.BufferedMotor, moveSpeed);
            }
            catch (Exception exception)
            {
                FailClosed("Buffered motor activation failed: " + exception.Message);
                throw;
            }
        }

        public void RequestBasicAttackProbe()
        {
            RequireLiveGate(nameof(RequestBasicAttackProbe));
            basicAttackPending = true;
            animationDriver.PlayBasicAttack();
        }

        public void RequestSuperProbe()
        {
            RequireLiveGate(nameof(RequestSuperProbe));
            superPending = true;
            animationDriver.PlaySuper();
        }

        public void RequestHitReactionProbe()
        {
            RequireLiveGate(nameof(RequestHitReactionProbe));
            hitReactionPending = true;
            animationDriver.PlayHitReaction();
        }

        public string EvidenceSummary()
        {
            return string.Format(
                "activated={0}; authority={1}; failure='{2}'; inputUpdates={3}; moveReads={4}; " +
                "scheduler={5}/{6}; motor={7}; locomotion={8}; adapterRotation={9}; " +
                "controllerRotation={10}; animator={11}; suppressedVendorPaths={12}; " +
                "externalFixedSubscribers={13}; groundedSamples={14}; maxPlanarDisplacement={15:F3}; " +
                "internalStamina={16:F3}; staminaPinned={17}; basicObserved={18}; " +
                 "superObserved={19}; recoilObserved={20}; recoilStateHash={21}; " +
                 "moveActionOwned={22}; projectWideAction={23}; feed={24}; " +
                 "bufferedMotorInitialized={25}; motorSchedule={26}/{27}; " +
                 "meleeWindows={28}/{29}/{30}; blockedMeleeHits={31}; " +
                 "weaponGate={32}; weaponAim={33}/{34}; muzzle={35}/{36}; ik={37}/{38}; " +
                 "weaponDrops={39}; weaponFaults={40}",
                activated,
                AuthorityChecksPassed,
                failureMessage,
                input?.InputUpdateCount ?? 0,
                input?.MoveReadCount ?? 0,
                input?.SchedulerStartCount ?? 0,
                input?.SchedulerCompleteCount ?? 0,
                controller?.MotorUpdateCount ?? 0,
                controller?.LocomotionUpdateCount ?? 0,
                input?.RotationUpdateCount ?? 0,
                controller?.RotationControlCount ?? 0,
                controller?.AnimatorUpdateCount ?? 0,
                input?.SuppressedVendorPathCount ?? 0,
                input?.ExternalFixedUpdateSubscriberCount ?? -1,
                groundedObservationCount,
                maximumPlanarDisplacement,
                controller?.InternalMotorStamina ?? -1f,
                controller != null && controller.IsInternalMotorStaminaPinned,
                observedBasicAttackState,
                observedSuperState,
                observedHitReactionTransition,
                observedHitReactionStateHash,
                input != null && input.ProjectMoveActionOwnedByAdapter,
                input != null && input.ProjectMoveActionUsesProjectWideLifecycle,
                 input != null ? input.MovementFeedMode.ToString() : "none",
                 motor != null && motor.IsInitialized,
                 motor?.ScheduledPrepareCount ?? 0,
                 motor?.ScheduledCompleteCount ?? 0,
                 meleeManager?.SuppressedAttackWindowCount ?? 0,
                 meleeManager?.SuppressedAttackWindowEnableCount ?? 0,
                 meleeManager?.SuppressedAttackWindowDisableCount ?? 0,
                 meleeManager?.BlockedDamageHitCount ?? 0,
                 weaponPresentation != null && weaponPresentation.LabRuntimeEnabled,
                 weaponPresentation?.AimRequestCount ?? 0,
                 weaponPresentation?.AimReleaseCount ?? 0,
                 weaponPresentation?.MuzzlePresentationRequestCount ?? 0,
                 weaponPresentation?.MuzzleEmissionCount ?? 0,
                 weaponPresentation?.AppliedIKPassCount ?? 0,
                 weaponPresentation?.SuppressedIKPassCount ?? 0,
                 weaponPresentation?.DroppedRequestCount ?? 0,
                 weaponPresentation?.RuntimeFaultCount ?? 0);
        }

        void ActivateLabInstance(InvectorMovementFeedMode feedMode, float bufferedMoveSpeed)
        {
            if (!string.Equals(gameObject.scene.path, LabScenePath, StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    "The Phase 3B activation component is restricted to " + LabScenePath + ".");
            }
            if (pilotRoot == null || brawlCamera == null || pilotRoot.scene != gameObject.scene ||
                brawlCamera.gameObject.scene != gameObject.scene)
            {
                throw new InvalidOperationException("The generated lab references are incomplete or cross-scene.");
            }
            if (pilotRoot.activeSelf)
            {
                throw new InvalidOperationException(
                    "The lab pilot must be serialized inactive and activated only by the Play-mode gate.");
            }

            CachePilotStack();
            staticSceneOwnershipValidated = false;
            ValidateStaticSceneOwnership();
            staticSceneOwnershipValidated = true;

            fixedDeltaTimeAtActivation = Time.fixedDeltaTime;
            positionAtActivation = pilotRoot.transform.position;
            maximumPlanarDisplacement = 0f;
            authorityValidationPassCount = 0;
            groundedObservationCount = 0;
            healthBaselineCaptured = false;
            basicAttackPending = false;
            superPending = false;
            hitReactionPending = false;
            observedBasicAttackState = false;
            observedSuperState = false;
            observedHitReactionTransition = false;
            observedHitReactionStateHash = 0;
            failureMessage = string.Empty;

            controller.ResetRuntimeTrace();
            input.ResetRuntimeTrace();
            motor.ResetRuntimeTrace();
            animationDriver.ResetRuntimeTrace();
            weaponPresentation.ResetRuntimeTrace();
            meleeManager.ResetPresentationTrace();
            input.SelectMovementFeedMode(feedMode);

            animator.applyRootMotion = false;
            animator.enabled = true;
            controller.useRootMotion = false;
            if (feedMode == InvectorMovementFeedMode.LabProjectAction)
            {
                controller.locomotionType = vThirdPersonMotor.LocomotionType.OnlyFree;
                controller.moveToDirectionInFree = true;
                controller.isStrafing = false;
                controller.isSprinting = false;
                controller.isCrouching = false;
            }
            controller.enabled = true;

            body.isKinematic = false;
            body.linearVelocity = Vector3.zero;
            body.angularVelocity = Vector3.zero;
            body.useGravity = true;
            body.constraints = RigidbodyConstraints.FreezeRotationX | RigidbodyConstraints.FreezeRotationZ;
            body.interpolation = RigidbodyInterpolation.Interpolate;
            body.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
            capsule.enabled = true;

            // The managers remain disabled data providers. This permits the
            // guarded trigger APIs without running weapon, damage, ammo, or IK
            // lifecycles in Phase 3B.
            shooterManager.enabled = false;
            meleeManager.enabled = false;

            pilotRoot.SetActive(true);
            brawlCamera.SetTarget(pilotRoot.transform);
            input.SetMovementReference(
                feedMode == InvectorMovementFeedMode.LabProjectAction
                    ? brawlCamera
                    : null);
            if (feedMode == InvectorMovementFeedMode.BufferedMotor)
            {
                motor.enabled = true;
                motor.Initialize(bufferedMoveSpeed);
            }
            else if (motor.enabled || motor.IsInitialized)
            {
                throw new InvalidOperationException(
                    "The Phase 3B project-action path must not activate the buffered motor.");
            }
            input.SetRuntimeSchedulingEnabled(true);
            animationDriver.SetPresentationRequestsEnabled(true);
            if (!weaponPresentation.EnableLabRuntime())
                throw new InvalidOperationException(
                    "The project weapon-presentation gate refused the validated lab stack.");

            activated = true;
            ValidateContinuousAuthority();
            authorityChecksPassed = true;
            Debug.Log("Isolated Invector lab gate activated with " + feedMode + ".", this);
        }

        void CachePilotStack()
        {
            controller = RequireExactlyOne<BrawlInvectorThirdPersonController>(pilotRoot);
            input = RequireExactlyOne<InvectorShooterMeleeInputAdapter>(pilotRoot);
            motor = RequireExactlyOne<InvectorBrawlerMotor>(pilotRoot);
            animationDriver = RequireExactlyOne<InvectorBrawlerAnimationDriver>(pilotRoot);
            weaponPresentation = RequireExactlyOne<InvectorBrawlerWeaponPresentation>(pilotRoot);
            animator = RequireExactlyOne<Animator>(pilotRoot);
            body = RequireExactlyOne<Rigidbody>(pilotRoot);
            capsule = RequireExactlyOne<CapsuleCollider>(pilotRoot);
            shooterManager = RequireExactlyOne<vShooterManager>(pilotRoot);
            meleeManager = RequireExactlyOne<BrawlInvectorMeleePresentationManager>(pilotRoot);
        }

        void ValidateStaticSceneOwnership()
        {
            GameObject[] roots = gameObject.scene.GetRootGameObjects();
            Component[] components = roots
                .SelectMany(root => root.GetComponentsInChildren<Component>(true))
                .Where(component => component != null)
                .ToArray();

            RequireCount(components.OfType<BrawlCamera>(), 1, "BrawlCamera");
            RequireCount(components.OfType<Camera>(), 1, "Camera");
            RequireCount(components.OfType<AudioListener>(), 1, "AudioListener");
            RequireCount(components.OfType<InvectorPhase3BLabController>(), 1, nameof(InvectorPhase3BLabController));
            RequireCount(components.OfType<InvectorShooterMeleeInputAdapter>(), 1, nameof(InvectorShooterMeleeInputAdapter));
            RequireCount(components.OfType<InvectorBrawlerMotor>(), 1, nameof(InvectorBrawlerMotor));
            RequireCount(components.OfType<BrawlInvectorThirdPersonController>(), 1, nameof(BrawlInvectorThirdPersonController));
            RequireCount(components.OfType<vThirdPersonInput>(), 1, nameof(vThirdPersonInput));
            RequireCount(components.OfType<vThirdPersonController>(), 1, nameof(vThirdPersonController));
            RequireCount(components.OfType<vMeleeManager>(), 1, nameof(vMeleeManager));
            RequireCount(components.OfType<BrawlInvectorMeleePresentationManager>(), 1,
                nameof(BrawlInvectorMeleePresentationManager));
            RequireCount(components.OfType<Animator>(), 1, "Animator");
            RequireCount(components.OfType<InvectorBrawlerWeaponPresentation>(), 1,
                nameof(InvectorBrawlerWeaponPresentation));
            RequireCount(components.OfType<BrawlerHitProxy>(), 1, nameof(BrawlerHitProxy));
            RequireCount(components.OfType<Rigidbody>(), 1, "Rigidbody");
            RequireCount(components.OfType<CapsuleCollider>(), 1, "CapsuleCollider");

            if (components.OfType<PlayerBrawlerInput>().Any() ||
                components.OfType<CharacterController>().Any() ||
                components.OfType<NavMeshAgent>().Any() ||
                components.OfType<PlayerInput>().Any() ||
                components.OfType<EventSystem>().Any() ||
                components.OfType<Health>().Any() ||
                components.OfType<BrawlerController>().Any())
            {
                throw new InvalidOperationException(
                    "The lab contains a competing input, movement, UI, health, or facade authority.");
            }
            if (components.Any(component => component.GetType() == typeof(vMeleeManager)))
            {
                throw new InvalidOperationException(
                    "The stock melee manager must be replaced by the project presentation firewall.");
            }
            RequireCount(components.OfType<MonoBehaviour>().OfType<IBrawlerAnimationDriver>(), 1,
                nameof(IBrawlerAnimationDriver));
            RequireCount(components.OfType<MonoBehaviour>().OfType<IBrawlerMotor>(), 1,
                nameof(IBrawlerMotor));
            RequireCount(components.OfType<MonoBehaviour>().OfType<IBrawlerWeaponPresentation>(), 1,
                nameof(IBrawlerWeaponPresentation));

            string[] forbiddenTypeNames =
            {
                "vThirdPersonCamera", "vLockOnShooter", "vDamageReceiver", "vHitBox",
                "vMeleeAttackObject", "vShooterWeapon", "vProjectileControl",
                "vObjectDamage", "vDamageSender",
            };
            string forbidden = components.Select(component => component.GetType().Name)
                .FirstOrDefault(forbiddenTypeNames.Contains);
            if (forbidden != null)
                throw new InvalidOperationException("The live lab unexpectedly contains " + forbidden + ".");

            int groundLayer = LayerMask.NameToLayer("Ground");
            int worldBlockerLayer = LayerMask.NameToLayer("WorldBlocker");
            if (groundLayer < 0 || Physics.GetIgnoreLayerCollision(pilotRoot.layer, groundLayer))
                throw new InvalidOperationException("InvectorPlayer must collide with the Brawl Ground layer.");
            if (worldBlockerLayer < 0 || Physics.GetIgnoreLayerCollision(pilotRoot.layer, worldBlockerLayer))
                throw new InvalidOperationException("InvectorPlayer must collide with the Brawl WorldBlocker layer.");
            if (!input.HasProjectMoveAction)
                throw new InvalidOperationException("The project Player/Move Input Action is unavailable.");
            if (!input.HasConfiguredMotorBridge || !input.IsDormantConfigured ||
                !motor.IsDormantConfigured)
            {
                throw new InvalidOperationException(
                    "The reciprocal same-root motor bridge did not enter the lab dormant.");
            }
            if (input.ExternalFixedUpdateSubscriberCount != 0)
                throw new InvalidOperationException("The vendor onFixedUpdate event has an external subscriber.");
            if (shooterManager.enabled || meleeManager.enabled)
                throw new InvalidOperationException("Phase 3B weapon/damage managers must remain disabled.");
            BrawlerHitProxy proxy = components.OfType<BrawlerHitProxy>().Single();
            if (!weaponPresentation.IsDormantConfigured ||
                weaponPresentation.HasRuntimeSolvers || !proxy.IsConfigured ||
                proxy.enabled || proxy.TriggerCollider.enabled)
            {
                throw new InvalidOperationException(
                    "The weapon presenter or selective hit proxy did not enter the lab dormant.");
            }
            if (meleeManager.Members.Count != 0 || meleeManager.leftWeapon != null ||
                meleeManager.rightWeapon != null)
            {
                throw new InvalidOperationException(
                    "The melee presentation firewall must not retain hit members or weapons.");
            }
        }

        void ValidateContinuousAuthority()
        {
            if (!pilotRoot.activeInHierarchy || !controller.isActiveAndEnabled ||
                !input.IsRuntimeStackReady || !input.IsPresentationStackReady ||
                !animationDriver.PresentationRequestsEnabled || !animator.isActiveAndEnabled ||
                !weaponPresentation.LabRuntimeEnabled ||
                !weaponPresentation.isActiveAndEnabled || !capsule.enabled || body.isKinematic)
            {
                throw new InvalidOperationException("The approved live locomotion/Animator stack is no longer complete.");
            }
            if (shooterManager.enabled || meleeManager.enabled)
                throw new InvalidOperationException("A weapon or damage manager became an active Behaviour.");
            if (weaponPresentation.RuntimeHelperCount != 0 ||
                weaponPresentation.RuntimeFaultCount != 0 ||
                weaponPresentation.GateEnableFailureCount != 0)
            {
                throw new InvalidOperationException(
                    "The project weapon presenter created helpers or recorded a runtime fault.");
            }
            if (meleeManager.Members.Count != 0 || meleeManager.leftWeapon != null ||
                meleeManager.rightWeapon != null)
            {
                throw new InvalidOperationException(
                    "A melee hit member or weapon appeared behind the presentation firewall.");
            }
            if (meleeManager.BlockedDamageHitCount != 0)
                throw new InvalidOperationException("An Invector melee damage callback reached the firewall.");
            if (animator.applyRootMotion || controller.useRootMotion)
                throw new InvalidOperationException("Root motion became a second movement authority.");
            if (!Mathf.Approximately(Time.fixedDeltaTime, fixedDeltaTimeAtActivation))
                throw new InvalidOperationException("The global fixed timestep changed during the lab run.");
            if (input.ExternalFixedUpdateSubscriberCount != 0)
                throw new InvalidOperationException("A second fixed scheduler subscriber appeared.");
            if (input.SuppressedVendorPathCount != 0)
            {
                throw new InvalidOperationException(
                    "A suppressed vendor path executed: " + input.LastSuppressedVendorPath + ".");
            }
            if (input.SchedulerStartCount != input.SchedulerCompleteCount ||
                controller.MotorUpdateCount != input.SchedulerCompleteCount ||
                controller.LocomotionUpdateCount != input.SchedulerCompleteCount ||
                input.RotationUpdateCount != input.SchedulerCompleteCount ||
                controller.RotationControlCount != input.SchedulerCompleteCount ||
                controller.AnimatorUpdateCount != input.SchedulerCompleteCount)
            {
                throw new InvalidOperationException(
                    "The one-scheduler motor/locomotion/rotation/Animator trace diverged.");
            }

            if (input.MovementFeedMode == InvectorMovementFeedMode.LabProjectAction)
            {
                if (!input.ProjectMoveActionEnabled)
                    throw new InvalidOperationException("The project Player/Move action is not enabled.");
                if (input.InputUpdateCount != input.MoveReadCount)
                {
                    throw new InvalidOperationException(
                        "The project input reader executed more or fewer than once per Update.");
                }
                if (motor.enabled || motor.IsInitialized ||
                    motor.ScheduledPrepareCount != 0 || motor.ScheduledCompleteCount != 0)
                {
                    throw new InvalidOperationException(
                        "The Phase 3B project-action path activated the buffered motor.");
                }
            }
            else
            {
                if (!motor.isActiveAndEnabled || !motor.IsInitialized)
                    throw new InvalidOperationException("The buffered motor is not live with its scheduler.");
                if (input.ProjectMoveActionOwnedByAdapter ||
                    input.InputUpdateCount != 0 || input.MoveReadCount != 0)
                {
                    throw new InvalidOperationException(
                        "The buffered motor path executed or adopted a physical input reader.");
                }
                if (motor.ScheduledPrepareCount != input.SchedulerCompleteCount ||
                    motor.ScheduledCompleteCount != input.SchedulerCompleteCount)
                {
                    throw new InvalidOperationException(
                        "The buffered motor prepare/complete trace diverged from the one scheduler.");
                }
            }

            if (input.SchedulerCompleteCount > 0)
            {
                if (!healthBaselineCaptured)
                {
                    healthAtActivation = controller.currentHealth;
                    healthBaselineCaptured = true;
                }
                else if (!Mathf.Approximately(controller.currentHealth, healthAtActivation))
                {
                    throw new InvalidOperationException("The inert Invector health mirror changed during locomotion.");
                }

                if (!controller.IsInternalMotorStaminaPinned)
                    throw new InvalidOperationException("Invector's private locomotion stamina was not pinned.");

                if (animator.updateMode != AnimatorUpdateMode.Fixed)
                    throw new InvalidOperationException("Invector Animator scheduling left fixed-update mode.");
            }

            if (controller.MeleePresentationWriteCount !=
                input.WeakAttackRequestCount + input.StrongAttackRequestCount)
            {
                throw new InvalidOperationException(
                    "The censused melee presentation writes diverged from semantic attack requests.");
            }
        }

        void SamplePresentationAndMovement()
        {
            Vector3 displacement = pilotRoot.transform.position - positionAtActivation;
            displacement.y = 0f;
            maximumPlanarDisplacement = Mathf.Max(maximumPlanarDisplacement, displacement.magnitude);
            if (controller.isGrounded)
                groundedObservationCount++;

            if (input.isAttacking)
            {
                if (basicAttackPending)
                {
                    observedBasicAttackState = true;
                    basicAttackPending = false;
                }
                if (superPending)
                {
                    observedSuperState = true;
                    superPending = false;
                }
            }

            if (hitReactionPending && TryGetApprovedHitReactionStateHash(out int stateHash))
            {
                observedHitReactionTransition = true;
                observedHitReactionStateHash = stateHash;
                hitReactionPending = false;
            }
        }

        bool TryGetApprovedHitReactionStateHash(out int stateHash)
        {
            for (int layer = 0; layer < animator.layerCount; layer++)
            {
                int current = animator.GetCurrentAnimatorStateInfo(layer).fullPathHash;
                if (ApprovedHitReactionStateHashes.Contains(current))
                {
                    stateHash = current;
                    return true;
                }

                if (animator.IsInTransition(layer))
                {
                    int next = animator.GetNextAnimatorStateInfo(layer).fullPathHash;
                    if (ApprovedHitReactionStateHashes.Contains(next))
                    {
                        stateHash = next;
                        return true;
                    }
                }
            }

            stateHash = 0;
            return false;
        }

        void RequireLiveGate(string request)
        {
            if (!IsLiveGateReady)
                throw new InvalidOperationException(request + " requires the validated Phase 3B live gate.");
        }

        void FailClosed(string reason)
        {
            if (!string.IsNullOrEmpty(failureMessage))
                return;

            failureMessage = reason;
            authorityChecksPassed = false;
            activated = false;

            DeactivatePilotStack();

            Debug.LogError("Phase 3B Invector lab failed closed: " + reason, this);
        }

        void DeactivatePilotStack()
        {
            if (weaponPresentation != null)
                weaponPresentation.DisableLabRuntime();
            if (input != null)
                input.SetRuntimeSchedulingEnabled(false);
            if (animationDriver != null)
                animationDriver.SetPresentationRequestsEnabled(false);
            if (motor != null)
                motor.ReturnDormant();
            if (input != null)
            {
                input.SelectMovementFeedMode(InvectorMovementFeedMode.LabProjectAction);
                input.SetMovementReference(null);
            }
            if (body != null)
            {
                if (!body.isKinematic)
                {
                    body.linearVelocity = Vector3.zero;
                    body.angularVelocity = Vector3.zero;
                }
                body.isKinematic = true;
                body.useGravity = false;
                body.constraints = RigidbodyConstraints.FreezeAll;
                body.interpolation = RigidbodyInterpolation.None;
                body.collisionDetectionMode = CollisionDetectionMode.Discrete;
            }
            if (capsule != null)
                capsule.enabled = false;
            if (controller != null)
                controller.enabled = false;
            if (animator != null)
                animator.enabled = false;
            if (pilotRoot != null && pilotRoot.activeSelf)
                pilotRoot.SetActive(false);
        }

        static T RequireExactlyOne<T>(GameObject root) where T : Component
        {
            T[] components = root.GetComponentsInChildren<T>(true);
            if (components.Length != 1 || components[0].gameObject != root)
                throw new InvalidOperationException("The lab pilot requires exactly one root " + typeof(T).Name + ".");
            return components[0];
        }

        static void RequireCount<T>(System.Collections.Generic.IEnumerable<T> values, int expected, string label)
        {
            int count = values.Count();
            if (count != expected)
                throw new InvalidOperationException("The lab requires " + expected + " " + label + " component(s), found " + count + ".");
        }
    }
}
