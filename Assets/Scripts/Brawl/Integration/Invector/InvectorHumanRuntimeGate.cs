using System;
using Invector.vItemManager;
using Invector.vMelee;
using Invector.vShooter;
using UnityEngine;

namespace BrawlArena
{
    /// <summary>
    /// Transactional production activation for the one approved human Cinder
    /// stack. It opens only the buffered Invector scheduler, locomotion,
    /// animation-presentation, and visual weapon gates; Brawl retains input,
    /// health, combat, camera, and match authority.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class InvectorHumanRuntimeGate : MonoBehaviour
    {
        [SerializeField, HideInInspector] BrawlerController facade;
        [SerializeField, HideInInspector] Health health;
        [SerializeField, HideInInspector] BrawlInvectorThirdPersonController controller;
        [SerializeField, HideInInspector] InvectorShooterMeleeInputAdapter input;
        [SerializeField, HideInInspector] InvectorBrawlerMotor motor;
        [SerializeField, HideInInspector] InvectorBrawlerAnimationDriver animationDriver;
        [SerializeField, HideInInspector] InvectorBrawlerWeaponPresentation weaponPresenter;
        [SerializeField, HideInInspector] Animator animator;
        [SerializeField, HideInInspector] Rigidbody body;
        [SerializeField, HideInInspector] CapsuleCollider capsule;
        [SerializeField, HideInInspector] BrawlerHitProxy hitProxy;
        [SerializeField, HideInInspector] PlayerBrawlerInput playerInput;

        [SerializeField, HideInInspector] vShooterManager shooterManager;
        [SerializeField, HideInInspector]
        BrawlInvectorMeleePresentationManager meleeManager;
        [SerializeField, HideInInspector] vAmmoManager ammoManager;
        [SerializeField, HideInInspector]
        vCollectShooterMeleeControl collectControl;
        bool runtimeActive;
        bool activationInProgress;
        bool closing;
        string failureMessage = string.Empty;

        public bool IsRuntimeActive => runtimeActive;
        public string FailureMessage => failureMessage;

        public bool IsDormantConfigured =>
            HasCompleteConfiguration && !gameObject.activeSelf && !enabled &&
            !runtimeActive && !activationInProgress && !closing &&
            !facade.enabled && !health.enabled && !playerInput.enabled &&
            !animator.enabled && !controller.enabled && !input.enabled &&
            !input.RuntimeSchedulingEnabled && !motor.enabled &&
            motor.IsDormantConfigured && !animationDriver.enabled &&
            animationDriver.IsDormantConfigured && !weaponPresenter.enabled &&
            weaponPresenter.IsDormantConfigured && !body.useGravity &&
            body.isKinematic && body.constraints == RigidbodyConstraints.FreezeAll &&
            !capsule.enabled && !hitProxy.enabled &&
            !hitProxy.TriggerCollider.enabled && ManagersAreInert;

        bool HasCompleteConfiguration =>
            facade != null && health != null && controller != null && input != null &&
            motor != null && animationDriver != null && weaponPresenter != null &&
            animator != null && body != null && capsule != null && hitProxy != null &&
            hitProxy.TriggerCollider != null && playerInput != null &&
            AllRootReferencesMatch && hitProxy.transform.IsChildOf(transform) &&
            input.HasConfiguredMotorBridge;

        bool AllRootReferencesMatch =>
            facade.gameObject == gameObject && health.gameObject == gameObject &&
            controller.gameObject == gameObject && input.gameObject == gameObject &&
            motor.gameObject == gameObject && animationDriver.gameObject == gameObject &&
            weaponPresenter.gameObject == gameObject && animator.gameObject == gameObject &&
            body.gameObject == gameObject && capsule.gameObject == gameObject &&
            playerInput.gameObject == gameObject;

        bool ManagersAreInert =>
            shooterManager != null && !shooterManager.enabled &&
            meleeManager != null && !meleeManager.enabled &&
            ammoManager != null && !ammoManager.enabled &&
            collectControl != null && !collectControl.enabled;

        /// <summary>Builder-only dormant reference binding.</summary>
        public void ConfigureDormant(
            BrawlerController configuredFacade,
            Health configuredHealth,
            BrawlInvectorThirdPersonController configuredController,
            InvectorShooterMeleeInputAdapter configuredInput,
            InvectorBrawlerMotor configuredMotor,
            InvectorBrawlerAnimationDriver configuredAnimationDriver,
            InvectorBrawlerWeaponPresentation configuredWeaponPresenter,
            Animator configuredAnimator,
            Rigidbody configuredBody,
            CapsuleCollider configuredCapsule,
            BrawlerHitProxy configuredHitProxy,
            PlayerBrawlerInput configuredPlayerInput)
        {
            facade = RequireRoot(configuredFacade, nameof(configuredFacade));
            health = RequireRoot(configuredHealth, nameof(configuredHealth));
            controller = RequireRoot(configuredController, nameof(configuredController));
            input = RequireRoot(configuredInput, nameof(configuredInput));
            motor = RequireRoot(configuredMotor, nameof(configuredMotor));
            animationDriver = RequireRoot(
                configuredAnimationDriver, nameof(configuredAnimationDriver));
            weaponPresenter = RequireRoot(
                configuredWeaponPresenter, nameof(configuredWeaponPresenter));
            animator = RequireRoot(configuredAnimator, nameof(configuredAnimator));
            body = RequireRoot(configuredBody, nameof(configuredBody));
            capsule = RequireRoot(configuredCapsule, nameof(configuredCapsule));
            playerInput = RequireRoot(configuredPlayerInput, nameof(configuredPlayerInput));

            if (configuredHitProxy == null ||
                !configuredHitProxy.transform.IsChildOf(transform) ||
                configuredHitProxy.TriggerCollider == null)
            {
                throw new ArgumentException(
                    "The production hit proxy must be one configured child of this root.",
                    nameof(configuredHitProxy));
            }
            hitProxy = configuredHitProxy;

            shooterManager = RequireRootComponent<vShooterManager>();
            meleeManager = RequireRootComponent<BrawlInvectorMeleePresentationManager>();
            ammoManager = RequireRootComponent<vAmmoManager>();
            collectControl = RequireRootComponent<vCollectShooterMeleeControl>();

            runtimeActive = false;
            activationInProgress = false;
            closing = false;
            failureMessage = string.Empty;
            enabled = false;
        }

        /// <summary>
        /// Opens the already configured stack synchronously. Any failure closes
        /// every opened authority before it escapes to the transactional assembler.
        /// </summary>
        public void Activate(float moveSpeed)
        {
            if (!Application.isPlaying)
                throw new InvalidOperationException(
                    "The production Invector gate can open only in Play mode.");
            if (runtimeActive || activationInProgress)
                throw new InvalidOperationException(
                    "The production Invector gate is already active or opening.");
            if (!IsFinite(moveSpeed) || moveSpeed <= 0f)
                throw new ArgumentOutOfRangeException(nameof(moveSpeed));
            if (!IsDormantConfigured)
                throw new InvalidOperationException(
                    "The production Invector gate did not enter its builder-owned dormant posture.");

            activationInProgress = true;
            failureMessage = string.Empty;
            try
            {
                ResetRuntimeEvidence();
                input.SelectMovementFeedMode(InvectorMovementFeedMode.BufferedMotor);
                input.SetMovementReference(null);

                animator.applyRootMotion = false;
                animator.enabled = true;
                controller.useRootMotion = false;
                controller.enabled = true;

                body.isKinematic = false;
                body.linearVelocity = Vector3.zero;
                body.angularVelocity = Vector3.zero;
                body.useGravity = true;
                body.constraints = RigidbodyConstraints.FreezeRotationX |
                                   RigidbodyConstraints.FreezeRotationZ;
                body.interpolation = RigidbodyInterpolation.Interpolate;
                body.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
                capsule.enabled = true;

                health.enabled = true;
                facade.enabled = true;
                motor.enabled = true;
                enabled = true;
                gameObject.SetActive(true);

                motor.Initialize(moveSpeed);
                input.RuntimeGateClosed += HandleUnexpectedSchedulerClosure;
                input.SetRuntimeSchedulingEnabled(true);
                animationDriver.SetPresentationRequestsEnabled(true);
                if (!weaponPresenter.EnableRuntime())
                    throw new InvalidOperationException(
                        "The production weapon presenter refused the validated runtime stack.");

                // Brawl's physical reader is deliberately last. Until all
                // downstream authorities are live, no gameplay intent can enter.
                playerInput.enabled = true;
                runtimeActive = true;
            }
            catch (Exception exception)
            {
                failureMessage = exception.GetType().Name + ": " + exception.Message;
                CloseRuntime(true);
                throw new InvalidOperationException(
                    "The production Cinder/Invector activation failed closed: " +
                    failureMessage, exception);
            }
            finally
            {
                activationInProgress = false;
            }
        }

        public void Deactivate()
        {
            CloseRuntime(true);
        }

        void ResetRuntimeEvidence()
        {
            controller.ResetRuntimeTrace();
            input.ResetRuntimeTrace();
            motor.ResetRuntimeTrace();
            animationDriver.ResetRuntimeTrace();
            weaponPresenter.ResetRuntimeTrace();
            meleeManager.ResetPresentationTrace();
        }

        void HandleUnexpectedSchedulerClosure()
        {
            if (closing || activationInProgress || !runtimeActive) return;
            failureMessage = "The buffered Invector scheduler closed unexpectedly.";
            CloseRuntime(true);
        }

        void CloseRuntime(bool deactivateRoot)
        {
            if (closing) return;
            closing = true;
            try
            {
                if (input != null)
                    input.RuntimeGateClosed -= HandleUnexpectedSchedulerClosure;

                if (playerInput != null) playerInput.enabled = false;
                if (facade != null && facade.gameObject.activeInHierarchy)
                {
                    facade.SetMoveInput(Vector3.zero);
                    facade.CancelOffensiveActions();
                }

                if (weaponPresenter != null)
                {
                    if (weaponPresenter.RuntimeEnabled)
                        weaponPresenter.SetVisible(false);
                    weaponPresenter.DisableRuntime();
                }
                if (animationDriver != null)
                    animationDriver.SetPresentationRequestsEnabled(false);
                if (input != null)
                {
                    input.SetRuntimeSchedulingEnabled(false);
                    input.SetMovementReference(null);
                }
                if (motor != null) motor.ReturnDormant();

                if (hitProxy != null)
                {
                    hitProxy.enabled = false;
                    if (hitProxy.TriggerCollider != null)
                        hitProxy.TriggerCollider.enabled = false;
                }
                if (capsule != null) capsule.enabled = false;
                if (controller != null) controller.enabled = false;
                if (animator != null)
                {
                    animator.applyRootMotion = false;
                    animator.enabled = false;
                }
                if (facade != null) facade.enabled = false;
                if (health != null) health.enabled = false;

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

                runtimeActive = false;
                enabled = false;
                if (deactivateRoot && gameObject.activeSelf)
                    gameObject.SetActive(false);
            }
            finally
            {
                closing = false;
            }
        }

        void OnDisable()
        {
            if (!closing && runtimeActive)
                CloseRuntime(false);
        }

        void OnDestroy()
        {
            if (!closing && runtimeActive)
                CloseRuntime(false);
        }

        T RequireRoot<T>(T value, string parameterName) where T : Component
        {
            if (value == null) throw new ArgumentNullException(parameterName);
            if (value.gameObject != gameObject)
                throw new ArgumentException(
                    "The production runtime authority must live on this root.",
                    parameterName);
            return value;
        }

        T RequireRootComponent<T>() where T : Component
        {
            T[] values = GetComponents<T>();
            if (values.Length != 1)
                throw new InvalidOperationException(
                    "The production runtime gate requires exactly one root " +
                    typeof(T).Name + ".");
            return values[0];
        }

        static bool IsFinite(float value) =>
            !float.IsNaN(value) && !float.IsInfinity(value);
    }
}
