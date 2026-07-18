using System;
using Invector;
using Invector.vCharacterController;
using Invector.vMelee;
using UnityEngine;

namespace BrawlArena
{
    /// <summary>
    /// Project-owned controller boundary for the Invector pilot.
    /// It preserves generic action trigger events without depending on the
    /// vendor AutoCrouch tag, which is intentionally absent from BrawlArena.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class BrawlInvectorThirdPersonController : vThirdPersonController
    {
        static readonly int WeakAttackState = Animator.StringToHash(
            "FullBody.Attacks.WeakAttacks.Unarmed.A");
        static readonly int StrongAttackState = Animator.StringToHash(
            "FullBody.Attacks.StrongAttacks.Unarmed.A");

        const string ResourceAuthorityMessage =
            "BrawlArena owns health, death, and Ward Flow; the Invector pilot controller cannot mutate those resources.";

        int motorUpdateCount;
        int locomotionUpdateCount;
        int rotationControlCount;
        int animatorUpdateCount;
        int meleePresentationWriteCount;
        int recoilPresentationWriteCount;
        int meleeAttackTriggerResetCount;
        int fullPresentationResetCount;
        int lifecyclePresentationWriteCount;
        int lastPresentationAttackId;
        int lastPresentationRecoilId;
        BrawlInvectorLifecyclePresentation lastLifecyclePresentation;
        bool hasPendingMeleePresentationTrigger;
        int pendingMeleePresentationTriggerHash;
        int pendingMeleePresentationStateHash;
        bool hasPendingRecoilPresentationTrigger;
        bool hasPendingLifecyclePresentationTrigger;

        public int MotorUpdateCount => motorUpdateCount;
        public int LocomotionUpdateCount => locomotionUpdateCount;
        public int RotationControlCount => rotationControlCount;
        public int AnimatorUpdateCount => animatorUpdateCount;
        public int MeleePresentationWriteCount => meleePresentationWriteCount;
        public int RecoilPresentationWriteCount => recoilPresentationWriteCount;
        public int MeleeAttackTriggerResetCount => meleeAttackTriggerResetCount;
        public int FullPresentationResetCount => fullPresentationResetCount;
        public int LifecyclePresentationWriteCount => lifecyclePresentationWriteCount;
        public int LastPresentationAttackId => lastPresentationAttackId;
        public int LastPresentationRecoilId => lastPresentationRecoilId;
        public BrawlInvectorLifecyclePresentation LastLifecyclePresentation =>
            lastLifecyclePresentation;
        public bool HasPendingMeleePresentationTrigger => hasPendingMeleePresentationTrigger;
        public bool HasPendingRecoilPresentationTrigger => hasPendingRecoilPresentationTrigger;
        public bool HasPendingLifecyclePresentationTrigger =>
            hasPendingLifecyclePresentationTrigger;
        /// <summary>Read-only Animator access for presentation-only queries (clip lookups, hit-stop).</summary>
        internal Animator PresentationAnimator => animator;
        public float InternalMotorStamina => currentStamina;
        public bool IsInternalMotorStaminaPinned => Mathf.Approximately(currentStamina, maxStamina);
        public int RegisteredAnimatorStateInfoLayerCount =>
            animatorStateInfos?.stateInfos?.Length ?? 0;
        public bool HasRegisteredAnimatorStateInfos =>
            animator != null &&
            animatorStateInfos != null &&
            animatorStateInfos.animator == animator &&
            animatorStateInfos.stateInfos != null &&
            animatorStateInfos.stateInfos.Length == animator.layerCount;

        public void ResetRuntimeTrace()
        {
            motorUpdateCount = 0;
            locomotionUpdateCount = 0;
            rotationControlCount = 0;
            animatorUpdateCount = 0;
            meleePresentationWriteCount = 0;
            recoilPresentationWriteCount = 0;
            meleeAttackTriggerResetCount = 0;
            fullPresentationResetCount = 0;
            lifecyclePresentationWriteCount = 0;
            lastPresentationAttackId = 0;
            lastPresentationRecoilId = 0;
            lastLifecyclePresentation = BrawlInvectorLifecyclePresentation.None;
            hasPendingMeleePresentationTrigger = false;
            pendingMeleePresentationTriggerHash = 0;
            pendingMeleePresentationStateHash = 0;
            hasPendingRecoilPresentationTrigger = false;
            hasPendingLifecyclePresentationTrigger = false;
        }

        /// <summary>
        /// Project-owned melee presentation write. It reproduces only the two
        /// graph writes from vMeleeCombatInput and deliberately never enters
        /// vShooterMeleeInput's reload-manager override.
        /// </summary>
        internal void TriggerMeleePresentation(int attackId, bool strong)
        {
            if (attackId < 0)
                throw new ArgumentOutOfRangeException(nameof(attackId));
            if (animator == null || !HasRegisteredAnimatorStateInfos)
            {
                throw new InvalidOperationException(
                    "Initialize the approved Invector Animator stack before requesting melee presentation.");
            }

            animator.SetInteger(vAnimatorParameters.AttackID, attackId);
            pendingMeleePresentationTriggerHash = strong
                ? vAnimatorParameters.StrongAttack
                : vAnimatorParameters.WeakAttack;
            pendingMeleePresentationStateHash = strong
                ? StrongAttackState
                : WeakAttackState;
            animator.SetTrigger(pendingMeleePresentationTriggerHash);
            lastPresentationAttackId = attackId;
            hasPendingMeleePresentationTrigger = true;
            meleePresentationWriteCount++;
        }

        /// <summary>
        /// Project-owned recoil graph write matching the audited five writes
        /// from vMeleeCombatInput without entering any vendor combat method.
        /// </summary>
        internal void TriggerRecoilPresentation(int recoilId)
        {
            if (recoilId < 0)
                throw new ArgumentOutOfRangeException(nameof(recoilId));
            if (animator == null || !HasRegisteredAnimatorStateInfos)
            {
                throw new InvalidOperationException(
                    "Initialize the approved Invector Animator stack before requesting recoil presentation.");
            }

            animator.SetInteger(vAnimatorParameters.RecoilID, recoilId);
            animator.SetTrigger(vAnimatorParameters.TriggerRecoil);
            animator.SetTrigger(vAnimatorParameters.ResetState);

            // A hit landing mid-swing must not erase this body's own already
            // committed attack presentation. Simplification: the only state we
            // track here is "an attack trigger is still in flight"; only clear
            // the weak/strong triggers when that is not the case, so a victim
            // who is also mid-attack keeps their own swing playing out.
            if (!hasPendingMeleePresentationTrigger)
            {
                animator.ResetTrigger(vAnimatorParameters.WeakAttack);
                animator.ResetTrigger(vAnimatorParameters.StrongAttack);
                pendingMeleePresentationTriggerHash = 0;
                pendingMeleePresentationStateHash = 0;
            }
            lastPresentationRecoilId = recoilId;
            hasPendingRecoilPresentationTrigger = true;
            recoilPresentationWriteCount++;
        }

        /// <summary>Hit-stop support: freezes/resumes Animator playback without touching Time.timeScale.</summary>
        internal void SetAnimatorSpeed(float speed)
        {
            if (animator != null) animator.speed = speed;
        }

        /// <summary>
        /// Mirrors the vendor attack-state exit cleanup while keeping every
        /// raw graph write inside the project controller boundary.
        /// </summary>
        internal void ResetMeleeAttackTriggers()
        {
            if (animator != null)
            {
                animator.ResetTrigger(vAnimatorParameters.WeakAttack);
                animator.ResetTrigger(vAnimatorParameters.StrongAttack);
                RearmPendingMeleePresentation();
            }

            meleeAttackTriggerResetCount++;
        }

        internal void MarkMeleePresentationConsumed()
        {
            if (!hasPendingMeleePresentationTrigger || animator == null)
                return;
            int fullBodyLayer = animator.GetLayerIndex("FullBody");
            if (fullBodyLayer < 0) return;
            AnimatorStateInfo current =
                animator.GetCurrentAnimatorStateInfo(fullBodyLayer);
            AnimatorStateInfo next = animator.IsInTransition(fullBodyLayer)
                ? animator.GetNextAnimatorStateInfo(fullBodyLayer)
                : default;
            if (current.fullPathHash != pendingMeleePresentationStateHash &&
                next.fullPathHash != pendingMeleePresentationStateHash)
                return;

            hasPendingMeleePresentationTrigger = false;
            pendingMeleePresentationTriggerHash = 0;
            pendingMeleePresentationStateHash = 0;
        }

        internal void RearmPendingMeleePresentation()
        {
            if (animator != null && hasPendingMeleePresentationTrigger &&
                pendingMeleePresentationTriggerHash != 0)
            {
                animator.SetTrigger(pendingMeleePresentationTriggerHash);
            }
        }

        /// <summary>
        /// Enters the project-owned FullBody lifecycle overlay. This path never
        /// writes vendor isDead, health, ragdoll, ActionState, or component
        /// lifecycle state.
        /// </summary>
        internal void TriggerLifecyclePresentation(
            BrawlInvectorLifecyclePresentation presentation)
        {
            int triggerHash = BrawlInvectorLifecycleParameters.TriggerHash(presentation);
            if (triggerHash == 0)
                throw new ArgumentOutOfRangeException(nameof(presentation));
            if (animator == null || !HasRegisteredAnimatorStateInfos)
            {
                throw new InvalidOperationException(
                    "Initialize the approved Invector Animator stack before requesting lifecycle presentation.");
            }

            ClearPresentationGraphState();
            ClearActionPresentationTrace();
            animator.SetTrigger(triggerHash);
            lastLifecyclePresentation = presentation;
            hasPendingLifecyclePresentationTrigger = true;
            lifecyclePresentationWriteCount++;
        }

        internal void MarkLifecyclePresentationConsumed(
            BrawlInvectorLifecyclePresentation presentation)
        {
            if (presentation == lastLifecyclePresentation)
                hasPendingLifecyclePresentationTrigger = false;
        }

        /// <summary>
        /// Clears project-owned one-shot trigger residue before the scheduler
        /// closes so a dormant/reopened pilot cannot emit a deferred attack.
        /// </summary>
        internal void ResetMeleePresentation()
        {
            if (animator != null)
                ClearPresentationGraphState();

            ClearActionPresentationTrace();
            lastLifecyclePresentation = BrawlInvectorLifecyclePresentation.None;
            hasPendingLifecyclePresentationTrigger = false;
            fullPresentationResetCount++;
        }

        void ClearPresentationGraphState()
        {
            animator.ResetTrigger(vAnimatorParameters.WeakAttack);
            animator.ResetTrigger(vAnimatorParameters.StrongAttack);
            animator.ResetTrigger(vAnimatorParameters.TriggerRecoil);
            animator.ResetTrigger(vAnimatorParameters.ResetState);
            animator.ResetTrigger(BrawlInvectorLifecycleParameters.DeathTrigger);
            animator.ResetTrigger(BrawlInvectorLifecycleParameters.RespawnTrigger);
            animator.ResetTrigger(BrawlInvectorLifecycleParameters.VictoryTrigger);
            animator.SetInteger(vAnimatorParameters.AttackID, 0);
            animator.SetInteger(vAnimatorParameters.RecoilID, 0);
        }

        void ClearActionPresentationTrace()
        {
            lastPresentationAttackId = 0;
            lastPresentationRecoilId = 0;
            hasPendingMeleePresentationTrigger = false;
            pendingMeleePresentationTriggerHash = 0;
            pendingMeleePresentationStateHash = 0;
            hasPendingRecoilPresentationTrigger = false;
        }

        /// <summary>
        /// BrawlArena owns its physics timestep. Invector must never change the
        /// global value when this controller wakes, regardless of copied data.
        /// </summary>
        public override void SetCustomFixedTimeStep()
        {
            customFixedTimeStep = CustomFixedTimeStep.Default;
        }

        /// <summary>
        /// Vendor Start enters the inherited health lifecycle before it
        /// registers Animator state listeners. Keep the health path suppressed
        /// while retaining the listener registration required by the combined
        /// locomotion/melee/shooter graph.
        /// </summary>
        protected override void Start()
        {
            EnsureApprovedAnimatorStateInfosRegistered();
        }

        public void EnsureApprovedAnimatorStateInfosRegistered()
        {
            if (animator == null)
            {
                throw new System.InvalidOperationException(
                    "Initialize the approved Invector motor before registering Animator state listeners.");
            }

            if (!HasRegisteredAnimatorStateInfos)
                RegisterAnimatorStateInfos();

            if (!HasRegisteredAnimatorStateInfos)
            {
                throw new System.InvalidOperationException(
                    "The approved Invector Animator state listener registry is incomplete.");
            }
        }

        /// <summary>
        /// The dormant prefab can be disabled before vendor Init has cached its
        /// collider. Preserve the two useful vendor shutdown operations without
        /// dereferencing that uninitialized cache.
        /// </summary>
        protected override void OnDisable()
        {
            if (Application.isPlaying &&
                (hasPendingMeleePresentationTrigger || hasPendingRecoilPresentationTrigger ||
                 hasPendingLifecyclePresentationTrigger || lastPresentationAttackId != 0 ||
                 lastPresentationRecoilId != 0 ||
                 lastLifecyclePresentation != BrawlInvectorLifecyclePresentation.None))
            {
                ResetMeleePresentation();
            }
            if (_capsuleCollider != null)
                SetFullCapsuleHeight();
            animatorStateInfos?.RemoveListener();
        }

        /// <summary>
        /// Phase 3B trace points wrap, but never duplicate, the three controller
        /// operations scheduled by the adapter's one base FixedUpdate call.
        /// </summary>
        public override void UpdateMotor()
        {
            motorUpdateCount++;
            base.UpdateMotor();
        }

        public override void ControlLocomotionType()
        {
            locomotionUpdateCount++;
            base.ControlLocomotionType();
        }

        public override void ControlRotationType()
        {
            rotationControlCount++;
            base.ControlRotationType();
        }

        public override void UpdateAnimator()
        {
            animatorUpdateCount++;
            base.UpdateAnimator();
            RearmPendingMeleePresentation();
        }

        /// <summary>
        /// The retained Invector motor scheduler calls these three virtual
        /// methods every fixed frame. Keep its private locomotion stamina full
        /// and inert, and never let its inherited health recovery become a
        /// second gameplay resource loop.
        /// </summary>
        protected override void CheckStamina()
        {
            PinInternalStamina();
        }

        public override void StaminaRecovery()
        {
            PinInternalStamina();
        }

        protected override bool canRecoverHealth => false;

        protected override void HealthRecovery()
        {
            // Brawl Health is the only recovery authority.
            currentHealthRecoveryDelay = 0f;
            inHealthRecovery = false;
        }

        /// <summary>
        /// Fall damage is checked from UpdateMotor. It must not reach the
        /// inherited vHealthController while Brawl Health owns damage.
        /// </summary>
        public override bool FallDamageConditions()
        {
            return false;
        }

        /// <summary>
        /// Public vendor resource APIs fail visibly instead of silently
        /// creating a second health, death, or stamina authority.
        /// </summary>
        public override void TakeDamage(vDamage damage)
        {
            throw ResourceAuthorityException(nameof(TakeDamage));
        }

        public override void AddHealth(int value)
        {
            throw ResourceAuthorityException(nameof(AddHealth));
        }

        public override void ChangeHealth(int value)
        {
            throw ResourceAuthorityException(nameof(ChangeHealth));
        }

        public override void ResetHealth(float health)
        {
            throw ResourceAuthorityException(nameof(ResetHealth));
        }

        public override void ResetHealth()
        {
            throw ResourceAuthorityException(nameof(ResetHealth));
        }

        public override void ChangeMaxHealth(int value)
        {
            throw ResourceAuthorityException(nameof(ChangeMaxHealth));
        }

        public override void SetHealthRecovery(float value)
        {
            throw ResourceAuthorityException(nameof(SetHealthRecovery));
        }

        public override void ReduceStamina(float value, bool accumulative)
        {
            throw ResourceAuthorityException(nameof(ReduceStamina));
        }

        public override void ChangeStamina(int value)
        {
            throw ResourceAuthorityException(nameof(ChangeStamina));
        }

        public override void ChangeMaxStamina(int value)
        {
            throw ResourceAuthorityException(nameof(ChangeMaxStamina));
        }

        protected override void OnTriggerStay(Collider other)
        {
            // Deliberately bypass vThirdPersonController.CheckForAutoCrouch.
            onActionStay.Invoke(other);
        }

        protected override void OnTriggerExit(Collider other)
        {
            // Deliberately bypass vThirdPersonController.AutoCrouchExit.
            onActionExit.Invoke(other);
        }

        void PinInternalStamina()
        {
            currentStamina = maxStamina;
            currentStaminaRecoveryDelay = 0f;
            finishStaminaOnSprint = false;
        }

        static NotSupportedException ResourceAuthorityException(string operation)
        {
            return new NotSupportedException(operation + ": " + ResourceAuthorityMessage);
        }
    }
}
