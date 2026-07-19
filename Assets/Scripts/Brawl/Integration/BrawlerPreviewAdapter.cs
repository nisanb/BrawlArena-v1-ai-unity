using System;
using UnityEngine;

namespace BrawlArena
{
    /// <summary>
    /// Fail-closed boundary for menu and editor previews of the generated
    /// top-down hero prefabs. Preview clones never run gameplay: every
    /// behaviour, collider, and physics body is neutralized, then only the
    /// Animator plays the generated controller's Locomotion/Victory
    /// presentation.
    /// </summary>
    public static class BrawlerPreviewAdapter
    {
        const string LocomotionStateName = "Locomotion";
        const string VictoryStateName = "Victory";
        const float VictoryBlendSeconds = 0.08f;

        static readonly int LocomotionStateHash =
            Animator.StringToHash(LocomotionStateName);
        static readonly int VictoryStateHash =
            Animator.StringToHash(VictoryStateName);

        /// <summary>Returns only the exact top-down Human prefab assigned to this definition.</summary>
        public static GameObject ResolvePrefab(BrawlerDefinition definition)
        {
            if (definition == null)
                throw new ArgumentNullException(nameof(definition));
            if (string.IsNullOrWhiteSpace(definition.id))
                throw new InvalidOperationException("A preview definition requires an exact roster id.");

            GameObject prefab = definition.humanBodyPrefab;
            if (prefab == null)
                throw new InvalidOperationException(
                    $"Brawler '{definition.id}' has no production top-down Human preview prefab.");
            if (prefab.scene.IsValid())
                throw new InvalidOperationException(
                    $"Brawler '{definition.id}' preview must reference a prefab asset, not a scene object.");

            ValidateTopology(prefab, definition);
            return prefab;
        }

        /// <summary>
        /// Neutralizes an instantiated clone and starts its idle presentation.
        /// The generated prefabs ship as ACTIVE roots, so the clone may already
        /// be active (and, in Play mode, may already have run Awake) when it
        /// arrives here; the clone is quarantined either way before the
        /// Animator is allowed to run, and deactivated again on any failure.
        /// </summary>
        public static void Prepare(GameObject preview, BrawlerDefinition definition)
        {
            if (preview == null)
                throw new ArgumentNullException(nameof(preview));
            if (!preview.scene.IsValid())
                throw new InvalidOperationException("Prepare requires an instantiated preview clone.");

            try
            {
                Animator animator = ValidateTopology(preview, definition);
                Neutralize(preview);

                // Unity invokes Awake on behaviours when an inactive clone
                // first activates. Those callbacks can add new default-enabled
                // components (BrawlerController adds an AudioSource), so
                // quarantine the completed component graph a second time after
                // activation.
                if (!preview.activeSelf)
                {
                    preview.SetActive(true);
                    Neutralize(preview);
                }

                animator.applyRootMotion = false;
                animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
                animator.updateMode = AnimatorUpdateMode.Normal;
                animator.enabled = true;
                animator.Rebind();
                animator.Play(LocomotionStateHash, 0, 0f);
                animator.Update(0f);

                ValidateLivePresentation(animator);
                ValidateOnlyPreviewBehaviours(preview, animator);
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
                animator.Play(LocomotionStateHash, 0, 0f);
                animator.Update(0f);
                if (sampleSeconds > 0f)
                    animator.Update(sampleSeconds);
                ValidateOnlyPreviewBehaviours(preview, animator);
            }
            catch
            {
                FailClosed(preview);
                throw;
            }
        }

        /// <summary>Crossfades into the generated Victory one-shot (then its looping maintain state).</summary>
        public static void ShowVictory(GameObject preview, BrawlerDefinition definition)
        {
            try
            {
                Animator animator = RequirePreparedAnimator(preview, definition);
                animator.CrossFadeInFixedTime(VictoryStateHash, VictoryBlendSeconds, 0, 0f);
                animator.Update(0f);
                ValidateOnlyPreviewBehaviours(preview, animator);
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
                throw new InvalidOperationException("The top-down preview is not an active prepared clone.");

            Animator animator = ValidateTopology(preview, definition);
            if (!animator.enabled || animator.applyRootMotion)
                throw new InvalidOperationException("The top-down preview Animator is not preview-safe.");

            ValidateLivePresentation(animator);
            ValidateOnlyPreviewBehaviours(preview, animator);
            return animator;
        }

        static Animator ValidateTopology(GameObject root, BrawlerDefinition definition)
        {
            if (definition == null)
                throw new ArgumentNullException(nameof(definition));
            if (string.IsNullOrWhiteSpace(definition.id))
                throw new InvalidOperationException("A preview definition requires an exact roster id.");

            HeavyBrawlerIdentity[] identities =
                root.GetComponentsInChildren<HeavyBrawlerIdentity>(true);
            if (identities.Length != 1 || identities[0].gameObject != root ||
                !string.Equals(identities[0].heroId, definition.id, StringComparison.Ordinal) ||
                !identities[0].isHumanVariant)
            {
                throw new InvalidOperationException(
                    $"Brawler '{definition.id}' preview requires one exact root top-down Human identity.");
            }

            Animator[] animators = root.GetComponentsInChildren<Animator>(true);
            if (animators.Length != 1 || animators[0].gameObject != root)
                throw new InvalidOperationException(
                    $"Brawler '{definition.id}' preview requires one exact root Animator.");

            Animator animator = animators[0];
            if (animator.runtimeAnimatorController == null)
                throw new InvalidOperationException(
                    $"Brawler '{definition.id}' preview requires its generated top-down controller.");

            return animator;
        }

        static void ValidateLivePresentation(Animator animator)
        {
            if (animator.layerCount < 1 ||
                !animator.HasState(0, LocomotionStateHash) ||
                !animator.HasState(0, VictoryStateHash))
            {
                throw new InvalidOperationException(
                    "The top-down preview controller is missing the generated Locomotion/Victory states.");
            }
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

        static void ValidateOnlyPreviewBehaviours(GameObject preview, Animator expectedAnimator)
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
