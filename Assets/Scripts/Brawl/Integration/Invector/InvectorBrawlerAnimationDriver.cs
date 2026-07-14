using System;
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
