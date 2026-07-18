using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace BrawlArena
{
    /// <summary>
    /// Semantic presentation bridge into Invector's coordinated public combat
    /// APIs. Gameplay, damage, stamina, and lifecycle remain Brawl-owned.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class InvectorBrawlerAnimationDriver : MonoBehaviour, IBrawlerAnimationDriver
    {
        [SerializeField, HideInInspector]
        BrawlInvectorThirdPersonController controller;

        [SerializeField, HideInInspector]
        InvectorShooterMeleeInputAdapter input;

        [SerializeField, HideInInspector]
        bool presentationRequestsEnabled;

        [SerializeField, Min(0)]
        int hitReactionId;

        int basicAttackRequestCount;
        int superRequestCount;
        int hitReactionRequestCount;
        int deathRequestCount;
        int respawnRequestCount;
        int victoryRequestCount;
        int droppedLifecycleRequestCount;
        int lifecycleFaultCount;
        int lifecycleStateEnterCount;
        int lifecycleStateExitCount;
        BrawlInvectorLifecyclePresentation lastLifecycleRequest;
        BrawlInvectorLifecyclePresentation lastLifecycleStateEntered;
        BrawlInvectorLifecyclePresentation lastLifecycleStateExited;
        string lastLifecycleFault = string.Empty;
        Coroutine hitStopRoutine;

        const string WeakAttackClipName = "WeakAttack_UnarmedA";
        const string StrongAttackClipName = "StrongAttack_PunchA";

        public bool PresentationRequestsEnabled => presentationRequestsEnabled;
        public int BasicAttackRequestCount => basicAttackRequestCount;
        public int SuperRequestCount => superRequestCount;
        public int HitReactionRequestCount => hitReactionRequestCount;
        public int DeathRequestCount => deathRequestCount;
        public int RespawnRequestCount => respawnRequestCount;
        public int VictoryRequestCount => victoryRequestCount;
        public int DroppedLifecycleRequestCount => droppedLifecycleRequestCount;
        public int LifecycleFaultCount => lifecycleFaultCount;
        public int LifecycleStateEnterCount => lifecycleStateEnterCount;
        public int LifecycleStateExitCount => lifecycleStateExitCount;
        public BrawlInvectorLifecyclePresentation LastLifecycleRequest =>
            lastLifecycleRequest;
        public BrawlInvectorLifecyclePresentation LastLifecycleStateEntered =>
            lastLifecycleStateEntered;
        public BrawlInvectorLifecyclePresentation LastLifecycleStateExited =>
            lastLifecycleStateExited;
        public string LastLifecycleFault => lastLifecycleFault;

        public bool IsDormantConfigured =>
            HasSameRootReferences && !presentationRequestsEnabled && !enabled;

        /// <summary>Builder-facing, edit-safe dormant configuration.</summary>
        public void ConfigureDormant(
            BrawlInvectorThirdPersonController configuredController,
            InvectorShooterMeleeInputAdapter configuredInput)
        {
            if (configuredController == null)
            {
                throw new ArgumentNullException(nameof(configuredController));
            }
            if (configuredInput == null)
            {
                throw new ArgumentNullException(nameof(configuredInput));
            }
            if (configuredController.gameObject != gameObject ||
                configuredInput.gameObject != gameObject)
            {
                throw new ArgumentException(
                    "The Invector animation driver, controller, and input adapter must share one root.");
            }

            controller = configuredController;
            input = configuredInput;
            presentationRequestsEnabled = false;
            hitReactionId = 0;
            ResetRuntimeTrace();
            enabled = false;
        }

        public void ResetRuntimeTrace()
        {
            if (presentationRequestsEnabled)
            {
                throw new InvalidOperationException(
                    "Close the Invector presentation gate before resetting its runtime trace.");
            }

            basicAttackRequestCount = 0;
            superRequestCount = 0;
            hitReactionRequestCount = 0;
            deathRequestCount = 0;
            respawnRequestCount = 0;
            victoryRequestCount = 0;
            droppedLifecycleRequestCount = 0;
            lifecycleFaultCount = 0;
            lifecycleStateEnterCount = 0;
            lifecycleStateExitCount = 0;
            lastLifecycleRequest = BrawlInvectorLifecyclePresentation.None;
            lastLifecycleStateEntered = BrawlInvectorLifecyclePresentation.None;
            lastLifecycleStateExited = BrawlInvectorLifecyclePresentation.None;
            lastLifecycleFault = string.Empty;
        }

        /// <summary>
        /// Phase 3B activation switch. It cannot be opened until the isolated
        /// scheduler and complete presentation stack are already live.
        /// </summary>
        public void SetPresentationRequestsEnabled(bool value)
        {
            if (!value)
            {
                presentationRequestsEnabled = false;
                enabled = false;
                return;
            }

            if (!HasSameRootReferences || !input.IsPresentationStackReady)
            {
                throw new InvalidOperationException(
                    "Invector presentation requests require a configured, active, same-root stack.");
            }
            if (!Application.isPlaying)
            {
                throw new InvalidOperationException(
                    "Invector presentation can only be enabled in Play mode; prefab assets must remain dormant.");
            }

            presentationRequestsEnabled = true;
            enabled = true;
        }

        /// <summary>
        /// Intentional permanent no-op: the adapter's single FixedUpdate
        /// scheduler owns Invector locomotion animation.
        /// </summary>
        public void TickLocomotion(float normalizedSpeed) { }

        public void PlayBasicAttack()
        {
            RequirePresentationStack(nameof(PlayBasicAttack));
            basicAttackRequestCount++;
            input.TriggerWeakAttack();
        }

        public void PlaySuper()
        {
            RequirePresentationStack(nameof(PlaySuper));
            superRequestCount++;
            input.TriggerStrongAttack();
        }

        public void PlayHitReaction()
        {
            RequirePresentationStack(nameof(PlayHitReaction));
            hitReactionRequestCount++;
            input.OnRecoil(hitReactionId);
        }

        public void PlayDeath()
        {
            deathRequestCount++;
            RequestLifecycle(BrawlInvectorLifecyclePresentation.Death);
        }

        public void PlayRespawn()
        {
            respawnRequestCount++;
            RequestLifecycle(BrawlInvectorLifecyclePresentation.Respawn);
        }

        public void PlayVictory()
        {
            victoryRequestCount++;
            RequestLifecycle(BrawlInvectorLifecyclePresentation.Victory);
        }

        /// <summary>
        /// Resolves the authored clip currently overriding the weak/strong
        /// attack slot and scales its length into a hit-timing estimate.
        /// Any missing link in that chain returns the fallback unmodified.
        /// </summary>
        public float GetAttackImpactDelay(bool strongAttack, float fallbackSeconds)
        {
            if (!HasSameRootReferences) return fallbackSeconds;

            Animator animator = controller.PresentationAnimator;
            if (animator == null) return fallbackSeconds;
            if (!(animator.runtimeAnimatorController is AnimatorOverrideController overrides))
                return fallbackSeconds;

            string originalClipName = strongAttack ? StrongAttackClipName : WeakAttackClipName;
            AnimationClip resolved = ResolveOverriddenClip(overrides, originalClipName);
            float clipLength = resolved != null ? resolved.length : 0f;
            return MobileCombatRules.ResolveAnimationImpactDelay(clipLength, fallbackSeconds);
        }

        /// <summary>
        /// Hit-stop freezes the Animator without touching Time.timeScale.
        /// Re-entrant calls simply restart the pause window; the latest call
        /// always wins over one already in flight.
        /// </summary>
        public void PauseAnimation(float seconds)
        {
            if (seconds <= 0f || !HasSameRootReferences || !isActiveAndEnabled) return;

            if (hitStopRoutine != null) StopCoroutine(hitStopRoutine);
            hitStopRoutine = StartCoroutine(HitStopRoutine(seconds));
        }

        IEnumerator HitStopRoutine(float seconds)
        {
            controller.SetAnimatorSpeed(0f);
            yield return new WaitForSeconds(seconds);
            controller.SetAnimatorSpeed(1f);
            hitStopRoutine = null;
        }

        static AnimationClip ResolveOverriddenClip(AnimatorOverrideController overrides,
            string originalClipName)
        {
            var overridePairs = new List<KeyValuePair<AnimationClip, AnimationClip>>(
                overrides.overridesCount);
            overrides.GetOverrides(overridePairs);
            for (int i = 0; i < overridePairs.Count; i++)
            {
                AnimationClip original = overridePairs[i].Key;
                if (original == null || original.name != originalClipName) continue;
                return overridePairs[i].Value != null ? overridePairs[i].Value : original;
            }
            return null;
        }

        internal void NotifyLifecycleStateEntered(
            BrawlInvectorLifecyclePresentation presentation)
        {
            lifecycleStateEnterCount++;
            lastLifecycleStateEntered = presentation;
            controller?.MarkLifecyclePresentationConsumed(presentation);
        }

        internal void NotifyLifecycleStateExited(
            BrawlInvectorLifecyclePresentation presentation)
        {
            lifecycleStateExitCount++;
            lastLifecycleStateExited = presentation;
        }

        void OnDisable()
        {
            bool schedulerStillOpen = input != null && input.RuntimeSchedulingEnabled;
            if (input != null)
                input.RuntimeGateClosed -= HandleRuntimeGateClosed;
            presentationRequestsEnabled = false;
            if (hitStopRoutine != null)
            {
                StopCoroutine(hitStopRoutine);
                hitStopRoutine = null;
            }
            controller?.SetAnimatorSpeed(1f);
            if (schedulerStillOpen && controller != null)
                controller.ResetMeleePresentation();
        }

        void OnEnable()
        {
            if (input != null)
                input.RuntimeGateClosed += HandleRuntimeGateClosed;
        }

        void HandleRuntimeGateClosed()
        {
            presentationRequestsEnabled = false;
            enabled = false;
        }

        bool HasSameRootReferences =>
            controller != null && input != null &&
            controller.gameObject == gameObject && input.gameObject == gameObject;

        void RequirePresentationStack(string request)
        {
            if (!presentationRequestsEnabled || !isActiveAndEnabled ||
                !HasSameRootReferences || !input.IsPresentationStackReady)
            {
                throw new InvalidOperationException(
                    request + " is unavailable until the isolated Invector presentation gate passes.");
            }
        }

        void RequestLifecycle(BrawlInvectorLifecyclePresentation presentation)
        {
            lastLifecycleRequest = presentation;
            if (!presentationRequestsEnabled || !isActiveAndEnabled ||
                !HasSameRootReferences || !input.IsPresentationStackReady)
            {
                droppedLifecycleRequestCount++;
                return;
            }

            try
            {
                input.PrepareLifecyclePresentation();
                controller.TriggerLifecyclePresentation(presentation);
            }
            catch (Exception exception)
            {
                lifecycleFaultCount++;
                lastLifecycleFault = exception.GetType().Name + ": " + exception.Message;
            }
        }
    }
}
