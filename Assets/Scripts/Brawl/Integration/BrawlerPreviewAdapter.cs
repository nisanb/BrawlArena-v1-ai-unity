using System;
using UnityEngine;

namespace BrawlArena
{
    /// <summary>
    /// Fail-closed boundary for menu and editor previews of production Invector
    /// humans. Preview clones never open the gameplay assembly gate: every
    /// behaviour, collider, and physics body is neutralized while the dormant
    /// clone is still inactive, then only its Animator is enabled.
    /// </summary>
    public static class BrawlerPreviewAdapter
    {
        const string SharedLifecycleControllerName = "CinderInvectorPilot";

        static readonly string[] SharedControllerLayers =
        {
            "Base Layer",
            "RightArm",
            "LeftArm",
            "OnlyArms",
            "UpperBody",
            "UnderBody",
            "Shot",
            "FullBody",
        };

        /// <summary>Returns only the exact dormant Invector Human assigned to this definition.</summary>
        public static GameObject ResolvePrefab(BrawlerDefinition definition)
        {
            if (definition == null)
                throw new ArgumentNullException(nameof(definition));
            if (string.IsNullOrWhiteSpace(definition.id))
                throw new InvalidOperationException("A preview definition requires an exact roster id.");

            GameObject prefab = definition.invectorHumanPrefab;
            if (prefab == null)
                throw new InvalidOperationException(
                    $"Brawler '{definition.id}' has no production Invector Human preview prefab.");
            if (prefab.scene.IsValid())
                throw new InvalidOperationException(
                    $"Brawler '{definition.id}' preview must reference a prefab asset, not a scene object.");
            if (prefab.activeSelf)
                throw new InvalidOperationException(
                    $"Brawler '{definition.id}' Invector Human prefab must remain dormant.");

            ValidateTopology(prefab, definition);
            return prefab;
        }

        /// <summary>
        /// Neutralizes and activates an inactive clone. The clone is deactivated
        /// again if its Animator does not expose the audited lifecycle contract.
        /// </summary>
        public static void Prepare(GameObject preview, BrawlerDefinition definition)
        {
            if (preview == null)
                throw new ArgumentNullException(nameof(preview));
            if (!preview.scene.IsValid())
                throw new InvalidOperationException("Prepare requires an instantiated preview clone.");
            if (preview.activeSelf)
                throw new InvalidOperationException(
                    "The Invector preview clone must be inactive until it has been neutralized.");

            try
            {
                Animator animator = ValidateTopology(preview, definition);
                Neutralize(preview);

                // Unity invokes Awake on disabled MonoBehaviours when their
                // inactive GameObject first activates. Those callbacks can add
                // new default-enabled components (BrawlerController adds an
                // AudioSource), so quarantine the completed component graph a
                // second time before allowing the Animator to run.
                preview.SetActive(true);
                Neutralize(preview);

                animator.applyRootMotion = false;
                animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
                animator.updateMode = AnimatorUpdateMode.Normal;
                animator.enabled = true;
                animator.Rebind();
                animator.Update(0f);

                ValidateLiveLifecycle(animator);
                ValidateOnlyAnimatorEnabled(preview, animator);
            }
            catch
            {
                FailClosed(preview);
                throw;
            }
        }

        /// <summary>Restores and evaluates the controller's zero-input idle presentation.</summary>
        public static void ShowIdle(GameObject preview, BrawlerDefinition definition,
            float sampleSeconds = 0f)
        {
            try
            {
                Animator animator = RequirePreparedAnimator(preview, definition);
                animator.Rebind();
                animator.Update(0f);
                if (sampleSeconds > 0f)
                    animator.Update(sampleSeconds);
                ValidateOnlyAnimatorEnabled(preview, animator);
            }
            catch
            {
                FailClosed(preview);
                throw;
            }
        }

        /// <summary>Requests victory through the shared lifecycle trigger.</summary>
        public static void ShowVictory(GameObject preview, BrawlerDefinition definition)
        {
            try
            {
                Animator animator = RequirePreparedAnimator(preview, definition);
                animator.ResetTrigger(BrawlInvectorLifecycleParameters.VictoryTrigger);
                animator.SetTrigger(BrawlInvectorLifecycleParameters.VictoryTrigger);
                ValidateOnlyAnimatorEnabled(preview, animator);
            }
            catch
            {
                FailClosed(preview);
                throw;
            }
        }

        static Animator RequirePreparedAnimator(GameObject preview, BrawlerDefinition definition)
        {
            if (preview == null)
                throw new ArgumentNullException(nameof(preview));
            if (!preview.scene.IsValid() || !preview.activeSelf)
                throw new InvalidOperationException("The Invector preview is not an active prepared clone.");

            Animator animator = ValidateTopology(preview, definition);
            if (!animator.enabled || animator.applyRootMotion)
                throw new InvalidOperationException("The Invector preview Animator is not preview-safe.");

            ValidateLiveLifecycle(animator);
            ValidateOnlyAnimatorEnabled(preview, animator);
            return animator;
        }

        static Animator ValidateTopology(GameObject root, BrawlerDefinition definition)
        {
            if (definition == null)
                throw new ArgumentNullException(nameof(definition));
            if (string.IsNullOrWhiteSpace(definition.id))
                throw new InvalidOperationException("A preview definition requires an exact roster id.");

            InvectorBrawlerPrefabIdentity[] identities =
                root.GetComponentsInChildren<InvectorBrawlerPrefabIdentity>(true);
            if (identities.Length != 1 || identities[0].gameObject != root ||
                !identities[0].Matches(definition.id, InvectorBrawlerPrefabRole.Human))
            {
                throw new InvalidOperationException(
                    $"Brawler '{definition.id}' preview requires one exact root Human identity.");
            }

            Animator[] animators = root.GetComponentsInChildren<Animator>(true);
            if (animators.Length != 1 || animators[0].gameObject != root)
                throw new InvalidOperationException(
                    $"Brawler '{definition.id}' preview requires one exact root Animator.");

            Animator animator = animators[0];
            if (!(animator.runtimeAnimatorController is AnimatorOverrideController overrideController))
                throw new InvalidOperationException(
                    $"Brawler '{definition.id}' preview requires its production AnimatorOverrideController.");

            RuntimeAnimatorController sharedController = overrideController.runtimeAnimatorController;
            if (sharedController == null || sharedController is AnimatorOverrideController ||
                !string.Equals(sharedController.name, SharedLifecycleControllerName,
                    StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    $"Brawler '{definition.id}' preview is not based on the shared lifecycle controller.");
            }

            return animator;
        }

        static void ValidateLiveLifecycle(Animator animator)
        {
            if (animator.layerCount != SharedControllerLayers.Length)
                throw new InvalidOperationException("The Invector preview controller layer contract changed.");
            for (int i = 0; i < SharedControllerLayers.Length; i++)
            {
                if (!string.Equals(animator.GetLayerName(i), SharedControllerLayers[i],
                        StringComparison.Ordinal))
                    throw new InvalidOperationException("The Invector preview controller layer contract changed.");
            }

            RequireTrigger(animator, BrawlInvectorLifecycleParameters.DeathTriggerName);
            RequireTrigger(animator, BrawlInvectorLifecycleParameters.RespawnTriggerName);
            RequireTrigger(animator, BrawlInvectorLifecycleParameters.VictoryTriggerName);

            int fullBodyLayer = animator.GetLayerIndex("FullBody");
            if (fullBodyLayer < 0 ||
                !animator.HasState(fullBodyLayer, BrawlInvectorLifecycleParameters.DeathState) ||
                !animator.HasState(fullBodyLayer, BrawlInvectorLifecycleParameters.RespawnState) ||
                !animator.HasState(fullBodyLayer, BrawlInvectorLifecycleParameters.VictoryState))
            {
                throw new InvalidOperationException(
                    "The Invector preview controller is missing the shared lifecycle states.");
            }
        }

        static void RequireTrigger(Animator animator, string triggerName)
        {
            foreach (AnimatorControllerParameter parameter in animator.parameters)
            {
                if (parameter.type == AnimatorControllerParameterType.Trigger &&
                    string.Equals(parameter.name, triggerName, StringComparison.Ordinal))
                    return;
            }

            throw new InvalidOperationException(
                $"The Invector preview controller is missing lifecycle trigger '{triggerName}'.");
        }

        static void Neutralize(GameObject preview)
        {
            foreach (Behaviour behaviour in preview.GetComponentsInChildren<Behaviour>(true))
                behaviour.enabled = false;
            foreach (Collider collider in preview.GetComponentsInChildren<Collider>(true))
                collider.enabled = false;
            foreach (Collider2D collider in preview.GetComponentsInChildren<Collider2D>(true))
                collider.enabled = false;

            foreach (Rigidbody body in preview.GetComponentsInChildren<Rigidbody>(true))
            {
                if (!body.isKinematic)
                {
                    body.linearVelocity = Vector3.zero;
                    body.angularVelocity = Vector3.zero;
                }
                body.useGravity = false;
                body.detectCollisions = false;
                body.collisionDetectionMode = CollisionDetectionMode.Discrete;
                body.interpolation = RigidbodyInterpolation.None;
                body.constraints = RigidbodyConstraints.FreezeAll;
                body.isKinematic = true;
            }

            foreach (Rigidbody2D body in preview.GetComponentsInChildren<Rigidbody2D>(true))
            {
                body.simulated = false;
                body.linearVelocity = Vector2.zero;
                body.angularVelocity = 0f;
                body.gravityScale = 0f;
                body.bodyType = RigidbodyType2D.Kinematic;
                body.constraints = RigidbodyConstraints2D.FreezeAll;
            }
        }

        static void ValidateOnlyAnimatorEnabled(GameObject preview, Animator expectedAnimator)
        {
            foreach (Behaviour behaviour in preview.GetComponentsInChildren<Behaviour>(true))
            {
                if (behaviour == expectedAnimator)
                {
                    if (!behaviour.enabled)
                        throw new InvalidOperationException("The preview Animator was disabled unexpectedly.");
                }
                else if (behaviour.enabled)
                {
                    throw new InvalidOperationException(
                        $"Preview activation enabled forbidden behaviour '{behaviour.GetType().FullName}'.");
                }
            }

            foreach (Collider collider in preview.GetComponentsInChildren<Collider>(true))
                if (collider.enabled)
                    throw new InvalidOperationException("Preview activation enabled a forbidden Collider.");
            foreach (Collider2D collider in preview.GetComponentsInChildren<Collider2D>(true))
                if (collider.enabled)
                    throw new InvalidOperationException("Preview activation enabled a forbidden Collider2D.");
        }

        static void FailClosed(GameObject preview)
        {
            if (preview == null) return;
            Neutralize(preview);
            preview.SetActive(false);
        }
    }
}
