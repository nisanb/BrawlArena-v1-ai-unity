using System;
using Invector.vItemManager;
using Invector.vMelee;
using Invector.vShooter;
using UnityEngine;

namespace BrawlArena
{
    /// <summary>
    /// Transactionally opens the dedicated Cinder AI topology. AIBrawler keeps
    /// tactical ownership and is enabled last; the child NavMeshAgent plans
    /// only, while the existing buffered Invector stack owns physical motion
    /// and presentation. Brawl health, combat, and lifecycle remain authoritative.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class InvectorAIRuntimeGate : MonoBehaviour
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
        [SerializeField, HideInInspector] AIBrawler ai;
        [SerializeField, HideInInspector] InvectorBrawlerNavigation navigation;

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
            !ai.enabled && !navigation.enabled && navigation.IsDormantConfigured &&
            !facade.enabled && !health.enabled && !animator.enabled &&
            !controller.enabled && !input.enabled &&
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
            hitProxy.TriggerCollider != null && ai != null && navigation != null &&
            navigation.PlannerAgent != null && AllRootReferencesMatch &&
            hitProxy.transform.IsChildOf(transform) &&
            navigation.PlannerAgent.transform.IsChildOf(transform) &&
            input.HasConfiguredMotorBridge;

        bool AllRootReferencesMatch =>
            facade.gameObject == gameObject && health.gameObject == gameObject &&
            controller.gameObject == gameObject && input.gameObject == gameObject &&
            motor.gameObject == gameObject && animationDriver.gameObject == gameObject &&
            weaponPresenter.gameObject == gameObject && animator.gameObject == gameObject &&
            body.gameObject == gameObject && capsule.gameObject == gameObject &&
            ai.gameObject == gameObject && navigation.gameObject == gameObject;

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
            AIBrawler configuredAI,
            InvectorBrawlerNavigation configuredNavigation)
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
            ai = RequireRoot(configuredAI, nameof(configuredAI));
            navigation = RequireRoot(
                configuredNavigation, nameof(configuredNavigation));

            if (configuredHitProxy == null ||
                !configuredHitProxy.transform.IsChildOf(transform) ||
                configuredHitProxy.TriggerCollider == null)
            {
                throw new ArgumentException(
                    "The production AI hit proxy must be one configured child of this root.",
                    nameof(configuredHitProxy));
            }
            hitProxy = configuredHitProxy;

            if (navigation.PlannerAgent == null ||
                navigation.PlannerAgent.gameObject == gameObject ||
                !navigation.PlannerAgent.transform.IsChildOf(transform))
            {
                throw new ArgumentException(
                    "The production AI navigator must bind one dedicated child planner.",
                    nameof(configuredNavigation));
            }

            shooterManager = RequireRootComponent<vShooterManager>();
            meleeManager = RequireRootComponent<BrawlInvectorMeleePresentationManager>();
            ammoManager = RequireRootComponent<vAmmoManager>();
            collectControl = RequireRootComponent<vCollectShooterMeleeControl>();

            runtimeActive = false;
            activationInProgress = false;
            closing = false;
            failureMessage = string.Empty;
            ai.enabled = false;
            navigation.enabled = false;
            enabled = false;
        }

        /// <summary>
        /// Opens the configured AI stack synchronously. Any exception closes
        /// every partially opened authority before escaping to the assembler.
        /// </summary>
        public void Activate(float moveSpeed)
        {
            if (!Application.isPlaying)
                throw new InvalidOperationException(
                    "The production Invector AI gate can open only in Play mode.");
            if (runtimeActive || activationInProgress)
                throw new InvalidOperationException(
                    "The production Invector AI gate is already active or opening.");
            if (!IsFinite(moveSpeed) || moveSpeed <= 0f)
                throw new ArgumentOutOfRangeException(nameof(moveSpeed));
            if (!IsDormantConfigured)
                throw new InvalidOperationException(
                    "The production Invector AI gate did not enter its builder-owned dormant posture.");

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
                navigation.enabled = true;
                enabled = true;
                gameObject.SetActive(true);

                motor.Initialize(moveSpeed);
                input.RuntimeGateClosed += HandleUnexpectedSchedulerClosure;
                input.SetRuntimeSchedulingEnabled(true);
                animationDriver.SetPresentationRequestsEnabled(true);
                if (!weaponPresenter.EnableRuntime())
                    throw new InvalidOperationException(
                        "The production AI weapon presenter refused the validated runtime stack.");

                navigation.OpenPlanner(body.position);

                // The tactical producer is deliberately last. No AI intent can
                // enter until planning, physics, scheduling, and presentation
                // consumers have all opened successfully.
                ai.enabled = true;
                runtimeActive = true;
            }
            catch (Exception exception)
            {
                failureMessage = exception.GetType().Name + ": " + exception.Message;
                CloseRuntime(true);
                throw new InvalidOperationException(
                    "The production Cinder/Invector AI activation failed closed: " +
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
            navigation.ResetRuntimeTrace();
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
                // The tactical producer is always closed first.
                if (ai != null) ai.enabled = false;

                if (input != null)
                    input.RuntimeGateClosed -= HandleUnexpectedSchedulerClosure;

                if (facade != null && facade.gameObject.activeInHierarchy)
                {
                    facade.SetMoveInput(Vector3.zero);
                    facade.CancelOffensiveActions();
                }

                if (navigation != null) navigation.ClosePlanner();
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
            {
                throw new ArgumentException(
                    "The production AI runtime authority must live on this root.",
                    parameterName);
            }
            return value;
        }

        T RequireRootComponent<T>() where T : Component
        {
            T[] values = GetComponents<T>();
            if (values.Length != 1)
            {
                throw new InvalidOperationException(
                    "The production AI runtime gate requires exactly one root " +
                    typeof(T).Name + ".");
            }
            return values[0];
        }

        static bool IsFinite(float value) =>
            !float.IsNaN(value) && !float.IsInfinity(value);
    }
}
