using System;
using System.Collections;
using System.Reflection;
using Invector;
using Invector.vCharacterController;
using Invector.vEventSystems;
using Invector.vMelee;
using Invector.vShooter;
using UnityEngine;
using UnityEngine.InputSystem;

namespace BrawlArena
{
    public enum InvectorMovementFeedMode
    {
        LabProjectAction = 0,
        BufferedMotor = 1,
    }

    /// <summary>
    /// Input System boundary for the combined Invector shooter/melee stack.
    /// Phase 3A installs it disabled. Phase 3B must explicitly open the runtime
    /// scheduler gate after enabling and validating the isolated lab stack.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class InvectorShooterMeleeInputAdapter : vShooterMeleeInput
    {
        [SerializeField, HideInInspector]
        BrawlInvectorThirdPersonController configuredController;

        [SerializeField, HideInInspector]
        vShooterManager configuredShooterManager;

        [SerializeField, HideInInspector]
        BrawlInvectorMeleePresentationManager configuredMeleeManager;

        [SerializeField, HideInInspector]
        InputActionAsset projectInputActions;

        [SerializeField, HideInInspector]
        BrawlCamera movementReference;

        [SerializeField, HideInInspector]
        InvectorBrawlerMotor configuredMotor;

        [SerializeField, HideInInspector]
        InvectorMovementFeedMode movementFeedMode =
            InvectorMovementFeedMode.LabProjectAction;

        [SerializeField, HideInInspector, Min(0)]
        int presentationAttackId;

        [SerializeField, HideInInspector]
        bool runtimeSchedulingEnabled;

        bool controllerInitialized;
        InputAction moveAction;
        bool moveActionEnabledByAdapter;

        int inputUpdateCount;
        int moveReadCount;
        int schedulerStartCount;
        int schedulerCompleteCount;
        int rotationUpdateCount;
        int suppressedVendorPathCount;
        int weakAttackRequestCount;
        int strongAttackRequestCount;
        int recoilRequestCount;
        int attackEnableCallbackCount;
        int attackDisableCallbackCount;
        int attackResetCallbackCount;
        string lastSuppressedVendorPath = string.Empty;
        Vector2 lastMoveIntent;

        static readonly FieldInfo FixedUpdateEventField = typeof(vThirdPersonInput).GetField(
            "onFixedUpdate",
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);

        const string MoveActionPath = "Player/Move";

        public bool RuntimeSchedulingEnabled => runtimeSchedulingEnabled;
        public InvectorMovementFeedMode MovementFeedMode => movementFeedMode;
        public int PresentationAttackId => presentationAttackId;
        public bool HasConfiguredMotorBridge =>
            configuredMotor != null &&
            configuredMotor.gameObject == gameObject &&
            configuredMotor.IsConfiguredForScheduler(this);

        public int InputUpdateCount => inputUpdateCount;
        public int MoveReadCount => moveReadCount;
        public int SchedulerStartCount => schedulerStartCount;
        public int SchedulerCompleteCount => schedulerCompleteCount;
        public int RotationUpdateCount => rotationUpdateCount;
        public int SuppressedVendorPathCount => suppressedVendorPathCount;
        public int WeakAttackRequestCount => weakAttackRequestCount;
        public int StrongAttackRequestCount => strongAttackRequestCount;
        public int RecoilRequestCount => recoilRequestCount;
        public int AttackEnableCallbackCount => attackEnableCallbackCount;
        public int AttackDisableCallbackCount => attackDisableCallbackCount;
        public int AttackResetCallbackCount => attackResetCallbackCount;
        public bool IsPresentationAttackWindowOpen => isAttacking;
        public string LastSuppressedVendorPath => lastSuppressedVendorPath;
        public Vector2 LastMoveIntent => lastMoveIntent;
        public bool HasProjectMoveAction => ResolveProjectMoveAction(false) != null;
        public bool ProjectMoveActionEnabled => ResolveProjectMoveAction(false)?.enabled == true;
        public bool ProjectMoveActionOwnedByAdapter => moveActionEnabledByAdapter;
        public bool ProjectMoveActionUsesProjectWideLifecycle =>
            ProjectMoveActionEnabled && !moveActionEnabledByAdapter;
        public int ExternalFixedUpdateSubscriberCount => GetExternalFixedUpdateSubscriberCount();

        public event Action RuntimeGateClosed;

        public bool IsDormantConfigured =>
            HasConfiguredReferences && HasSelectedMovementFeedConfiguration &&
            presentationAttackId >= 0 && !runtimeSchedulingEnabled && !enabled;

        public bool IsRuntimeStackReady =>
            runtimeSchedulingEnabled && isActiveAndEnabled && CanRunMovementStack();

        public bool HasBrawlCameraMovementReference =>
            movementReference != null &&
            movementReference.isActiveAndEnabled &&
            movementReference.gameObject.scene.IsValid() &&
            gameObject.scene.IsValid() &&
            movementReference.gameObject.scene == gameObject.scene;

        public bool IsPresentationStackReady =>
            IsRuntimeStackReady &&
            presentationAttackId >= 0 &&
            !configuredShooterManager.enabled &&
            !configuredMeleeManager.enabled &&
            configuredMeleeManager.Members != null &&
            configuredMeleeManager.Members.Count == 0 &&
            configuredMeleeManager.leftWeapon == null &&
            configuredMeleeManager.rightWeapon == null;

        /// <summary>
        /// Hip-fire input in the vendor implementation reads GenericInput.
        /// Brawl combat remains authoritative, so this bridge can never infer it.
        /// </summary>
        public override bool isAimingByHipFire => false;

        /// <summary>Builder-facing, edit-safe dormant configuration.</summary>
        public void ConfigureDormant(
            BrawlInvectorThirdPersonController controller,
            vShooterManager shooter,
            BrawlInvectorMeleePresentationManager melee,
            InputActionAsset inputActions,
            int configuredPresentationAttackId = 0)
        {
            RequireSameRoot(controller, nameof(controller));
            RequireSameRoot(shooter, nameof(shooter));
            RequireSameRoot(melee, nameof(melee));
            if (inputActions == null)
            {
                throw new ArgumentNullException(nameof(inputActions));
            }
            if (configuredPresentationAttackId < 0)
                throw new ArgumentOutOfRangeException(nameof(configuredPresentationAttackId));

            configuredController = controller;
            configuredShooterManager = shooter;
            configuredMeleeManager = melee;
            projectInputActions = inputActions;
            presentationAttackId = configuredPresentationAttackId;
            moveAction = null;
            moveActionEnabledByAdapter = false;
            if (ResolveProjectMoveAction(true) == null)
            {
                throw new InvalidOperationException(
                    "The configured Input Action asset must contain the Player/Move Vector2 action.");
            }

            // Keep both the project references and the vendor-facing fields in
            // sync without running any vendor initialization/input path.
            SynchronizeVendorReferences();

            movementReference = null;
            configuredMotor = null;
            movementFeedMode = InvectorMovementFeedMode.LabProjectAction;
            runtimeSchedulingEnabled = false;
            controllerInitialized = false;
            lockInput = true;
            lockMoveInput = true;
            lockMeleeInput = true;
            lockShooterInput = true;
            isAimingByInput = false;
            isBlocking = false;
            updateIK = false;
            DisableLegacyGenericInputs();
            ClearMovementIntent();
            ResetRuntimeTrace();
            enabled = false;
        }

        /// <summary>
        /// Records the production-safe world-intent bridge without changing
        /// the selected feed. The existing isolated lab therefore remains on
        /// its project-action feed until a caller explicitly selects another
        /// mode while the scheduler is dormant.
        /// </summary>
        public void ConfigureMotorBridge(InvectorBrawlerMotor motor)
        {
            RequireSameRoot(motor, nameof(motor));
            if (runtimeSchedulingEnabled || enabled)
            {
                throw new InvalidOperationException(
                    "The Invector movement feed can only be configured while its scheduler is dormant.");
            }
            if (!motor.IsConfiguredForScheduler(this))
            {
                throw new ArgumentException(
                    "The buffered motor must be configured back to this exact scheduler before selection.",
                    nameof(motor));
            }

            DisableProjectMoveActionIfOwned();
            ClearMovementIntent();
            configuredMotor = motor;
            lockInput = true;
            lockMoveInput = true;
        }

        /// <summary>
        /// Chooses the already-configured movement feed while the adapter is
        /// dormant. Runtime mode switching would risk two physical readers or
        /// changing controller intent between fixed-step boundaries.
        /// </summary>
        public void SelectMovementFeedMode(InvectorMovementFeedMode mode)
        {
            if (runtimeSchedulingEnabled || enabled)
            {
                throw new InvalidOperationException(
                    "The Invector movement feed can only be selected while its scheduler is dormant.");
            }
            if (!Enum.IsDefined(typeof(InvectorMovementFeedMode), mode))
            {
                throw new ArgumentOutOfRangeException(nameof(mode));
            }
            if (mode == InvectorMovementFeedMode.LabProjectAction)
            {
                if (!HasProjectMoveAction)
                {
                    throw new InvalidOperationException(
                        "The lab movement feed requires the configured Player/Move action.");
                }
            }
            else if (!HasConfiguredMotorBridge)
            {
                throw new InvalidOperationException(
                    "The buffered movement feed requires a reciprocal same-root motor bridge.");
            }

            DisableProjectMoveActionIfOwned();
            ClearMovementIntent();
            movementFeedMode = mode;
            lockInput = true;
            lockMoveInput = true;
        }

        /// <summary>
        /// Clears Phase 3B evidence counters before the isolated lab opens the
        /// scheduler. Runtime evidence is intentionally retained after a
        /// fail-closed event.
        /// </summary>
        public void ResetRuntimeTrace()
        {
            if (runtimeSchedulingEnabled)
            {
                throw new InvalidOperationException(
                    "Stop the Invector scheduler before resetting its runtime trace.");
            }

            inputUpdateCount = 0;
            moveReadCount = 0;
            schedulerStartCount = 0;
            schedulerCompleteCount = 0;
            rotationUpdateCount = 0;
            suppressedVendorPathCount = 0;
            weakAttackRequestCount = 0;
            strongAttackRequestCount = 0;
            recoilRequestCount = 0;
            attackEnableCallbackCount = 0;
            attackDisableCallbackCount = 0;
            attackResetCallbackCount = 0;
            lastSuppressedVendorPath = string.Empty;
            lastMoveIntent = Vector2.zero;
        }

        /// <summary>
        /// Phase 3B activation switch. Merely enabling this component is not
        /// enough: every scheduler callback also checks this serialized gate.
        /// </summary>
        public void SetRuntimeSchedulingEnabled(bool value)
        {
            if (!value)
            {
                FailClosedWithoutLog();
                return;
            }

            if (!HasConfiguredReferences)
            {
                throw new InvalidOperationException(
                    "The Invector input adapter must be configured on one root before scheduling can be enabled.");
            }
            if (!Application.isPlaying)
            {
                throw new InvalidOperationException(
                    "The Invector scheduler gate can only be opened in Play mode; prefab assets must remain dormant.");
            }

            SynchronizeVendorReferences();
            string activationFailure = DescribeActivationPrerequisiteFailure();
            if (!string.IsNullOrEmpty(activationFailure))
            {
                FailClosedWithoutLog();
                throw new InvalidOperationException(
                    "The Invector scheduler cannot open: " + activationFailure);
            }

            EnsureControllerInitialized();
            configuredController.EnsureApprovedAnimatorStateInfosRegistered();
            if (!CanRunMovementStack())
            {
                FailClosedWithoutLog();
                throw new InvalidOperationException(
                    "The Invector scheduler failed its post-initialization movement-stack check.");
            }

            runtimeSchedulingEnabled = true;
            if (movementFeedMode == InvectorMovementFeedMode.LabProjectAction)
            {
                EnableProjectMoveAction();
                lockInput = false;
                lockMoveInput = false;
            }
            else
            {
                DisableProjectMoveActionIfOwned();
                lockInput = true;
                lockMoveInput = true;
            }
            lockMeleeInput = true;
            lockShooterInput = true;
            enabled = true;
        }

        /// <summary>
        /// Supplies BrawlCamera's stable-yaw frame without adopting an Invector
        /// camera or camera-input reader. A live scheduler cannot fall back to
        /// a tag-discovered camera or accept an unowned Transform.
        /// </summary>
        public void SetMovementReference(BrawlCamera reference)
        {
            movementReference = reference;
            if (runtimeSchedulingEnabled &&
                movementFeedMode == InvectorMovementFeedMode.LabProjectAction &&
                !HasBrawlCameraMovementReference)
            {
                FailClosed("The configured BrawlCamera movement reference became unavailable.");
            }
        }

        /// <summary>
        /// Pure input arbitration retained as a small parity-test seam: an
        /// active keyboard direction overrides the mobile joystick.
        /// </summary>
        public static Vector2 ResolveMoveIntent(Vector2 hudIntent, Vector2 keyboardIntent)
        {
            Vector2 selected = keyboardIntent.sqrMagnitude > 0.01f
                ? keyboardIntent
                : hudIntent;
            return Vector2.ClampMagnitude(selected, 1f);
        }

        void OnEnable()
        {
            SynchronizeVendorReferences();
            if (!runtimeSchedulingEnabled)
            {
                ClearMovementIntent();
            }
            else
            {
                if (movementFeedMode == InvectorMovementFeedMode.LabProjectAction)
                {
                    EnableProjectMoveAction();
                }
            }
        }

        void OnDisable()
        {
            bool notifyRuntimeGateClosed = runtimeSchedulingEnabled;
            runtimeSchedulingEnabled = false;
            updateIK = false;
            DisableProjectMoveActionIfOwned();
            ClearMovementIntent();
            if (controllerInitialized && configuredController != null)
                configuredController.ResetMeleePresentation();
            controllerInitialized = false;
            isAttacking = false;
            if (movementFeedMode == InvectorMovementFeedMode.BufferedMotor &&
                HasConfiguredMotorBridge && configuredMotor.IsInitialized &&
                !configuredMotor.IsScheduledStepOpen)
            {
                configuredMotor.Stop(true);
            }
            if (notifyRuntimeGateClosed)
            {
                RuntimeGateClosed?.Invoke();
            }
        }

        protected override void Start()
        {
            SynchronizeVendorReferences();
            if (!runtimeSchedulingEnabled)
            {
                ClearMovementIntent();
                return;
            }

            if (!CanRunMovementStack())
            {
                FailClosed("Start found an incomplete or disabled movement stack.");
                return;
            }

            EnsureControllerInitialized();
        }

        protected override IEnumerator CharacterInit()
        {
            // Suppress vThirdPersonInput camera and HUD discovery.
            RecordSuppressedVendorPath(nameof(CharacterInit));
            yield break;
        }

        protected override void Update()
        {
            if (!CanExecuteScheduledFrame())
            {
                return;
            }

            if (movementFeedMode == InvectorMovementFeedMode.BufferedMotor)
            {
                // PlayerBrawlerInput is the sole production physical reader.
                // Its world intent reaches the controller at the fixed boundary.
                return;
            }

            inputUpdateCount++;
            InputHandle();
        }

        protected override void FixedUpdate()
        {
            if (!CanExecuteScheduledFrame())
            {
                return;
            }

            // The one approved scheduler call: vThirdPersonInput owns motor,
            // locomotion, rotation, and locomotion-animation updates exactly once.
            schedulerStartCount++;
            bool bufferedMotorFeed =
                movementFeedMode == InvectorMovementFeedMode.BufferedMotor;
            bool motorStepPrepared = false;
            try
            {
                if (bufferedMotorFeed)
                {
                    configuredMotor.PrepareScheduledStep();
                    motorStepPrepared = true;
                }

                try
                {
                    base.FixedUpdate();
                }
                finally
                {
                    try
                    {
                        if (motorStepPrepared && configuredMotor.IsScheduledStepOpen)
                            configuredMotor.CompleteScheduledStep();
                    }
                    finally
                    {
                        updateIK = false;
                    }
                }

                if (bufferedMotorFeed && !runtimeSchedulingEnabled)
                    configuredMotor.Stop(true);
                schedulerCompleteCount++;
            }
            catch (Exception exception)
            {
                updateIK = false;
                if (bufferedMotorFeed && configuredMotor != null)
                    configuredMotor.Stop(true);
                FailClosed("A scheduled frame threw " +
                    exception.GetType().Name + ": " + exception.Message);
            }
        }

        protected override void LateUpdate()
        {
            // Suppress melee parameter polling, camera control, shooter aim, and IK.
            updateIK = false;
        }

        public override void InputHandle()
        {
            if (!CanExecuteScheduledFrame() || cc.isDead || cc.ragdolled)
            {
                ClearMovementIntent();
                return;
            }

            if (movementFeedMode == InvectorMovementFeedMode.BufferedMotor)
            {
                return;
            }

            MoveInput();
        }

        public override void MoveInput()
        {
            if (!CanExecuteScheduledFrame())
            {
                ClearMovementIntent();
                return;
            }

            if (movementFeedMode == InvectorMovementFeedMode.BufferedMotor)
            {
                return;
            }

            Vector2 hudIntent = Vector2.zero;
            BrawlHUD hud = BrawlHUD.Instance;
            if (hud != null && hud.Joystick != null)
            {
                hudIntent = hud.Joystick.Value;
            }

            InputAction action = ResolveProjectMoveAction(true);
            Vector2 actionIntent = action.ReadValue<Vector2>();
            Vector2 input = ResolveMoveIntent(hudIntent, actionIntent);
            moveReadCount++;
            lastMoveIntent = input;
            cc.input = new Vector3(input.x, 0f, input.y);
            cc.ControlKeepDirection();
        }

        public override void ControlRotation()
        {
            if (!CanExecuteScheduledFrame())
            {
                return;
            }

            rotationUpdateCount++;

            if (movementFeedMode == InvectorMovementFeedMode.BufferedMotor)
            {
                // The motor has already supplied world-space moveDirection.
                // Re-projecting it through the camera would rotate intent twice.
                cc.rotateTarget = null;
                cc.SetInputDirection(cc.input);
                cc.ControlRotationType();
                return;
            }

            Transform reference = movementReference.transform;

            if (!lockUpdateMoveDirection && !cc.keepDirection)
            {
                cc.UpdateMoveDirection(reference);
            }

            cc.rotateTarget = reference;
            cc.ControlRotationType();
        }

        // All legacy GenericInput, action, camera, HUD, root-motion, and shooter
        // readers are explicit terminal overrides. Brawl gameplay calls semantic
        // APIs through its own controller/animation boundaries instead.
        public override Camera cameraMain { get => null; set { } }
        public override vControlAimCanvas controlAimCanvas => null;
        public override bool LockCamera { get => false; set { } }
        public override bool LockAiming { get => false; set { } }
        public override bool LockHipFireAiming { get => false; set { } }
        public override bool rotateToLockTargetConditions => false;
        public override void FindHUD()
        {
            RecordSuppressedVendorPath(nameof(FindHUD));
            hud = null;
        }
        public override void FindCamera()
        {
            RecordSuppressedVendorPath(nameof(FindCamera));
            tpCamera = null;
            _cameraMain = null;
            withoutMainCamera = true;
        }
        public override void UpdateHUD() { }
        public override void CameraInput() { RecordSuppressedVendorPath(nameof(CameraInput)); }
        public override void UpdateCameraStates() { RecordSuppressedVendorPath(nameof(UpdateCameraStates)); }
        public override void ChangeCameraState(string cameraState, bool useLerp = true) { RecordSuppressedVendorPath(nameof(ChangeCameraState)); }
        public override void ChangeCameraStateWithLerp(string cameraState) { RecordSuppressedVendorPath(nameof(ChangeCameraStateWithLerp)); }
        public override void ChangeCameraStateNoLerp(string cameraState) { RecordSuppressedVendorPath(nameof(ChangeCameraStateNoLerp)); }
        public override void ResetCameraState()
        {
            RecordSuppressedVendorPath(nameof(ResetCameraState));
            changeCameraState = false;
            customCameraState = string.Empty;
        }
        public override void ResetCameraAngleSmooth() { RecordSuppressedVendorPath(nameof(ResetCameraAngleSmooth)); }
        public override void ResetCameraAngleWithoutSmooth() { RecordSuppressedVendorPath(nameof(ResetCameraAngleWithoutSmooth)); }
        public override void ShowCursor(bool value) { RecordSuppressedVendorPath(nameof(ShowCursor)); }
        public override void LockCursor(bool value) { RecordSuppressedVendorPath(nameof(LockCursor)); }
        public override void SetLockBasicInput(bool value) { RecordSuppressedVendorPath(nameof(SetLockBasicInput)); }
        public override void SetLockAllInput(bool value)
        {
            RecordSuppressedVendorPath(nameof(SetLockAllInput));
            lockMeleeInput = true;
            lockShooterInput = true;
            lockCameraInput = true;
        }
        public override void SetLockCameraInput(bool value)
        {
            // Never invoke the vendor OnLockCamera/OnUnlockCamera UnityEvents.
            RecordSuppressedVendorPath(nameof(SetLockCameraInput));
            lockCameraInput = true;
        }
        public override void SetLockShooterInput(bool value)
        {
            // Never resolve or mutate the global vControlAimCanvas.
            RecordSuppressedVendorPath(nameof(SetLockShooterInput));
            lockShooterInput = true;
            isBlocking = false;
            isAimingByInput = false;
            isUsingScopeView = false;
            _aimTiming = 0f;
        }
        public override void SetAlwaysAim(bool value) { RecordSuppressedVendorPath(nameof(SetAlwaysAim)); }
        public override void OnAnimatorMoveEvent() { RecordSuppressedVendorPath(nameof(OnAnimatorMoveEvent)); }
        public override bool UseAnimatorMove { get => false; set { } }
        public override void EnableOnAnimatorMove() { RecordSuppressedVendorPath(nameof(EnableOnAnimatorMove)); }
        public override void DisableOnAnimatorMove() { RecordSuppressedVendorPath(nameof(DisableOnAnimatorMove)); }
        public override void SprintInput() { RecordSuppressedVendorPath(nameof(SprintInput)); }
        public override void CrouchInput() { RecordSuppressedVendorPath(nameof(CrouchInput)); }
        public override void StrafeInput() { RecordSuppressedVendorPath(nameof(StrafeInput)); }
        public override void JumpInput() { RecordSuppressedVendorPath(nameof(JumpInput)); }
        public override void RollInput() { RecordSuppressedVendorPath(nameof(RollInput)); }
        public override void MeleeWeakAttackInput() { RecordSuppressedVendorPath(nameof(MeleeWeakAttackInput)); }
        public override void MeleeStrongAttackInput() { RecordSuppressedVendorPath(nameof(MeleeStrongAttackInput)); }
        public override void BlockingInput() { RecordSuppressedVendorPath(nameof(BlockingInput)); }
        public override void AimInput() { RecordSuppressedVendorPath(nameof(AimInput)); }
        public override void ShotInput() { RecordSuppressedVendorPath(nameof(ShotInput)); }
        public override void HandleShotCount(vShooterWeapon weapon, bool weaponInput = true) { RecordSuppressedVendorPath(nameof(HandleShotCount)); }
        public override void DoShots() { RecordSuppressedVendorPath(nameof(DoShots)); }
        public override void TriggerShot() { RecordSuppressedVendorPath(nameof(TriggerShot)); }
        public override void ReloadInput() { RecordSuppressedVendorPath(nameof(ReloadInput)); }
        public override void SwitchCameraSideInput() { RecordSuppressedVendorPath(nameof(SwitchCameraSideInput)); }
        public override void SwitchCameraSide() { RecordSuppressedVendorPath(nameof(SwitchCameraSide)); }
        public override void ScopeViewInput() { RecordSuppressedVendorPath(nameof(ScopeViewInput)); }
        public override void EnableScopeView()
        {
            RecordSuppressedVendorPath(nameof(EnableScopeView));
            isUsingScopeView = false;
        }
        public override void DisableScopeView()
        {
            RecordSuppressedVendorPath(nameof(DisableScopeView));
            isUsingScopeView = false;
        }

        public override void CancelAiming()
        {
            RecordSuppressedVendorPath(nameof(CancelAiming));
            isAimingByInput = false;
            isReloading = false;
            isUsingScopeView = false;
            shootCountA = 0;
        }

        /// <summary>
        /// The melee state-machine callback may mark presentation state, but
        /// Brawl stamina remains authoritative and is never consumed here.
        /// </summary>
        public override void OnEnableAttack()
        {
            attackEnableCallbackCount++;
            isAttacking = true;
            configuredController?.MarkMeleePresentationConsumed();
            if (cc != null)
            {
                cc.isSprinting = false;
            }
        }

        /// <summary>
        /// Animator attack-window callbacks may end presentation state, but
        /// they cannot re-enter the vendor input loop or mutate resources.
        /// </summary>
        public override void OnDisableAttack()
        {
            attackDisableCallbackCount++;
            isAttacking = false;
        }

        /// <summary>
        /// Triggers are consumed by the Animator graph. Do not let state
        /// behaviours become an uncensused raw Animator writer on exit.
        /// </summary>
        public override void ResetAttackTriggers()
        {
            attackResetCallbackCount++;
            configuredController?.ResetMeleeAttackTriggers();
        }

        public override void BreakAttack(int breakAtkID)
        {
            throw new NotSupportedException(
                "Invector attack interruption is disabled; Brawl action and hit-reaction semantics remain authoritative.");
        }

        public override void TriggerWeakAttack()
        {
            RequirePresentationStack(nameof(TriggerWeakAttack));
            configuredController.TriggerMeleePresentation(
                presentationAttackId, false);
            weakAttackRequestCount++;
        }

        public override void TriggerStrongAttack()
        {
            RequirePresentationStack(nameof(TriggerStrongAttack));
            configuredController.TriggerMeleePresentation(
                presentationAttackId, true);
            strongAttackRequestCount++;
        }

        public override void OnRecoil(int recoilID)
        {
            RequirePresentationStack(nameof(OnRecoil));
            configuredController.TriggerRecoilPresentation(recoilID);
            recoilRequestCount++;
        }

        public override void OnReceiveAttack(vDamage damage, vIMeleeFighter attacker)
        {
            throw new NotSupportedException(
                "Invector damage reception is disabled; Brawl Health is the sole damage authority.");
        }

        internal void PrepareLifecyclePresentation()
        {
            RequirePresentationStack(nameof(PrepareLifecyclePresentation));
            isAttacking = false;
            isReloading = false;
            isAimingByInput = false;
            isUsingScopeView = false;
            shootCountA = 0;
        }

        InputAction ResolveProjectMoveAction(bool throwIfMissing)
        {
            if (moveAction != null)
            {
                return moveAction;
            }

            moveAction = projectInputActions?.FindAction(MoveActionPath, false);
            if (moveAction == null && throwIfMissing)
            {
                throw new InvalidOperationException(
                    "The Invector adapter requires the configured Player/Move Input Action.");
            }

            return moveAction;
        }

        void EnableProjectMoveAction()
        {
            InputAction action = ResolveProjectMoveAction(true);
            if (action.enabled)
                return;

            action.Enable();
            moveActionEnabledByAdapter = true;
        }

        void DisableProjectMoveActionIfOwned()
        {
            if (moveActionEnabledByAdapter && moveAction != null && moveAction.enabled)
            {
                moveAction.Disable();
            }
            moveActionEnabledByAdapter = false;
        }

        bool HasConfiguredReferences =>
            configuredController != null &&
            configuredShooterManager != null &&
            configuredMeleeManager != null &&
            configuredController.gameObject == gameObject &&
            configuredShooterManager.gameObject == gameObject &&
            configuredMeleeManager.gameObject == gameObject;

        void SynchronizeVendorReferences()
        {
            if (!HasConfiguredReferences)
            {
                return;
            }

            // vMeleeCombatInput.meleeManager is not serialized by the vendor,
            // so it must be restored from our serialized same-root reference.
            cc = configuredController;
            shooterManager = configuredShooterManager;
            meleeManager = configuredMeleeManager;
        }

        bool CanRunMovementStack()
        {
            if (!string.IsNullOrEmpty(DescribeActivationPrerequisiteFailure()) ||
                !controllerInitialized ||
                !configuredController.HasRegisteredAnimatorStateInfos)
                return false;

            return true;
        }

        string DescribeActivationPrerequisiteFailure()
        {
            if (!HasConfiguredReferences) return "same-root project references are incomplete";
            if (presentationAttackId < 0) return "the presentation attack ID is negative";
            if (!gameObject.activeInHierarchy) return "the pilot root is inactive";
            if (!configuredController.isActiveAndEnabled) return "the project controller is disabled";
            if (configuredShooterManager.enabled || configuredMeleeManager.enabled)
                return "a weapon or melee damage manager is enabled";
            if (configuredMeleeManager.Members == null ||
                configuredMeleeManager.Members.Count != 0 ||
                configuredMeleeManager.leftWeapon != null ||
                configuredMeleeManager.rightWeapon != null)
            {
                return "the melee presentation firewall contains a hit member or weapon";
            }
            if (movementFeedMode == InvectorMovementFeedMode.LabProjectAction)
            {
                InputAction action = ResolveProjectMoveAction(false);
                if (action == null) return "Player/Move is unavailable";
                if (!HasBrawlCameraMovementReference)
                    return "the same-scene BrawlCamera movement reference is inactive";
            }
            else
            {
                if (!HasConfiguredMotorBridge)
                    return "the same-root buffered motor bridge is unavailable";
                if (!configuredMotor.IsReadyForSchedulerBridge)
                    return "the buffered motor is disabled or has not been initialized";
            }
            int fixedSubscribers = ExternalFixedUpdateSubscriberCount;
            if (fixedSubscribers != 0) return "vendor onFixedUpdate has " + fixedSubscribers + " external subscriber(s)";

            Animator bodyAnimator = GetComponent<Animator>();
            if (bodyAnimator == null || !bodyAnimator.enabled) return "the root Animator is absent or disabled";
            Rigidbody body = GetComponent<Rigidbody>();
            if (!IsActivationSafe(body)) return "the Rigidbody is absent, kinematic, or frozen on every axis";
            CapsuleCollider capsule = GetComponent<CapsuleCollider>();
            if (capsule == null || !capsule.enabled) return "the root CapsuleCollider is absent or disabled";
            return string.Empty;
        }

        static bool IsActivationSafe(Rigidbody body)
        {
            return body != null &&
                   !body.isKinematic &&
                   (body.constraints & RigidbodyConstraints.FreezeAll) != RigidbodyConstraints.FreezeAll;
        }

        bool CanExecuteScheduledFrame()
        {
            if (!runtimeSchedulingEnabled)
            {
                ClearMovementIntent();
                return false;
            }

            if (!CanRunMovementStack())
            {
                FailClosed("A scheduled frame found an incomplete or disabled movement stack.");
                return false;
            }

            if (!controllerInitialized)
                EnsureControllerInitialized();

            return true;
        }

        void EnsureControllerInitialized()
        {
            if (controllerInitialized)
                return;

            // The only retained vendor initialization call. It binds motor,
            // collider, and Animator data without entering camera/HUD input.
            configuredController.Init();
            controllerInitialized = true;
            isInit = true;
        }

        void RequirePresentationStack(string request)
        {
            if (!IsPresentationStackReady)
            {
                throw new InvalidOperationException(
                    request + " requires the active, same-root Invector presentation stack.");
            }
        }

        void RequireSameRoot(Component component, string parameterName)
        {
            if (component == null)
            {
                throw new ArgumentNullException(parameterName);
            }
            if (component.gameObject != gameObject)
            {
                throw new ArgumentException(
                    "Every Invector bridge component must be installed on the same root.",
                    parameterName);
            }
        }

        void DisableLegacyGenericInputs()
        {
            if (horizontalInput != null) horizontalInput.useInput = false;
            if (verticalInput != null) verticalInput.useInput = false;
            if (sprintInput != null) sprintInput.useInput = false;
            if (crouchInput != null) crouchInput.useInput = false;
            if (strafeInput != null) strafeInput.useInput = false;
            if (jumpInput != null) jumpInput.useInput = false;
            if (rollInput != null) rollInput.useInput = false;
            if (rotateCameraXInput != null) rotateCameraXInput.useInput = false;
            if (rotateCameraYInput != null) rotateCameraYInput.useInput = false;
            if (cameraZoomInput != null) cameraZoomInput.useInput = false;
            if (weakAttackInput != null) weakAttackInput.useInput = false;
            if (strongAttackInput != null) strongAttackInput.useInput = false;
            if (blockInput != null) blockInput.useInput = false;
            if (aimInput != null) aimInput.useInput = false;
            if (shotInput != null) shotInput.useInput = false;
            if (reloadInput != null) reloadInput.useInput = false;
            if (switchCameraSideInput != null) switchCameraSideInput.useInput = false;
            if (scopeViewInput != null) scopeViewInput.useInput = false;
        }

        void ClearMovementIntent()
        {
            if (cc == null)
            {
                return;
            }
            cc.input = Vector3.zero;
            cc.inputSmooth = Vector3.zero;
            cc.moveDirection = Vector3.zero;
            cc.targetVelocity = Vector3.zero;
        }

        void FailClosedWithoutLog()
        {
            bool notifyRuntimeGateClosed = runtimeSchedulingEnabled || enabled;
            runtimeSchedulingEnabled = false;
            updateIK = false;
            lockInput = true;
            lockMoveInput = true;
            DisableProjectMoveActionIfOwned();
            ClearMovementIntent();
            if (controllerInitialized && configuredController != null)
                configuredController.ResetMeleePresentation();
            controllerInitialized = false;
            isAttacking = false;
            if (movementFeedMode == InvectorMovementFeedMode.BufferedMotor &&
                HasConfiguredMotorBridge && configuredMotor.IsInitialized &&
                !configuredMotor.IsScheduledStepOpen)
            {
                configuredMotor.Stop(true);
            }
            enabled = false;
            if (notifyRuntimeGateClosed)
            {
                RuntimeGateClosed?.Invoke();
            }
        }

        void FailClosed(string reason)
        {
            FailClosedWithoutLog();
            Debug.LogError("Invector input adapter failed closed: " + reason, this);
        }

        int GetExternalFixedUpdateSubscriberCount()
        {
            if (FixedUpdateEventField == null)
            {
                return int.MaxValue;
            }

            Delegate subscribers = FixedUpdateEventField.GetValue(this) as Delegate;
            return subscribers?.GetInvocationList().Length ?? 0;
        }

        void RecordSuppressedVendorPath(string path)
        {
            suppressedVendorPathCount++;
            lastSuppressedVendorPath = path;
        }

        bool HasSelectedMovementFeedConfiguration =>
            movementFeedMode == InvectorMovementFeedMode.LabProjectAction
                ? HasProjectMoveAction
                : HasConfiguredMotorBridge;
    }
}
