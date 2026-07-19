using System.IO;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace BrawlArena.EditorAutomation
{
    public class BrawlerPreviewAdapterEditModeTests
    {
        static readonly string[] ProductionHumanIds = { "frost", "thorn", "bastion" };

        [Test]
        public void ProductionHumansResolveActiveTopDownPrefabsWithMatchingIdentity()
        {
            foreach (string id in ProductionHumanIds)
            {
                BrawlerDefinition definition = LoadDefinition(id);

                GameObject resolved = BrawlerPreviewAdapter.ResolvePrefab(definition);

                Assert.AreSame(definition.humanBodyPrefab, resolved);
                Assert.IsTrue(resolved.activeSelf,
                    id + " TopDown prefabs ship active; no dormancy gates remain.");

                HeavyBrawlerIdentity identity =
                    resolved.GetComponent<HeavyBrawlerIdentity>();
                Assert.NotNull(identity, id + " must carry HeavyBrawlerIdentity.");
                Assert.AreEqual(id, identity.heroId);
                Assert.IsTrue(identity.isHumanVariant, id);

                Animator animator = resolved.GetComponent<Animator>();
                Assert.NotNull(animator, id);
                Assert.NotNull(animator.runtimeAnimatorController,
                    id + " must reference its generated weapon-family controller.");
                Assert.IsFalse(
                    animator.runtimeAnimatorController is AnimatorOverrideController,
                    id + " must use the generated controller directly, not a legacy override.");
            }
        }

        [Test]
        public void PreparedPreviewShowsLocomotionAndVictoryWithoutFreeFallingPhysics()
        {
            BrawlerDefinition definition = LoadDefinition(ProductionHumanIds[0]);
            GameObject preview = Object.Instantiate(definition.humanBodyPrefab);
            try
            {
                BrawlerPreviewAdapter.Prepare(preview, definition);

                Assert.IsTrue(preview.activeSelf);
                Animator animator = preview.GetComponent<Animator>();
                Assert.NotNull(animator);
                Assert.IsTrue(animator.enabled);
                Assert.IsFalse(animator.applyRootMotion);

                // The menu preview floats over UI with no ground beneath it:
                // its body must never be left free to fall under gravity.
                foreach (Rigidbody body in preview.GetComponentsInChildren<Rigidbody>(true))
                    Assert.IsTrue(body.isKinematic || !body.useGravity,
                        "A prepared preview body must not free-fall.");

                // A preview never owns live gameplay input.
                PlayerBrawlerInput input = preview.GetComponent<PlayerBrawlerInput>();
                if (input != null) Assert.IsFalse(input.enabled);

                BrawlerPreviewAdapter.ShowIdle(preview, definition, 0.3f);
                animator.Update(0f);
                Assert.IsTrue(
                    animator.GetCurrentAnimatorStateInfo(0).IsName("Locomotion"),
                    "ShowIdle must land the base layer in Locomotion.");

                BrawlerPreviewAdapter.ShowVictory(preview, definition);
                animator.Update(0.2f);
                AnimatorStateInfo current = animator.GetCurrentAnimatorStateInfo(0);
                bool presentingVictory = current.IsName("Victory") ||
                    current.IsName("VictoryMaintain") ||
                    (animator.IsInTransition(0) &&
                     animator.GetNextAnimatorStateInfo(0).IsName("Victory"));
                Assert.IsTrue(presentingVictory,
                    "ShowVictory must drive the base layer into the Victory chain.");
            }
            finally
            {
                Object.DestroyImmediate(preview);
            }
        }

        [Test]
        public void ResolverRejectsMissingOrMismatchedHumanData()
        {
            Assert.Throws<System.InvalidOperationException>(() =>
                BrawlerPreviewAdapter.ResolvePrefab(new BrawlerDefinition { id = "frost" }));

            BrawlerDefinition mismatched = new BrawlerDefinition
            {
                id = "thorn",
                humanBodyPrefab = LoadDefinition("frost").humanBodyPrefab,
            };
            Assert.Throws<System.InvalidOperationException>(() =>
                BrawlerPreviewAdapter.ResolvePrefab(mismatched));
        }

        [Test]
        public void PreviewCallersUseSemanticAdapterInsteadOfLegacyAnimatorNames()
        {
            string mainMenu = File.ReadAllText(
                Path.Combine(Application.dataPath, "Scripts/Brawl/MainMenuFlow.cs"));
            string portraits = File.ReadAllText(
                Path.Combine(Application.dataPath, "Editor/BrawlAutomation/PortraitStudio.cs"));

            StringAssert.Contains("BrawlerPreviewAdapter.ShowIdle", mainMenu);
            StringAssert.Contains("BrawlerPreviewAdapter.ShowVictory", mainMenu);
            StringAssert.DoesNotContain("CrossFadePreview", mainMenu);
            StringAssert.DoesNotContain("Victory_", mainMenu);
            StringAssert.DoesNotContain("Idle_", mainMenu);
            StringAssert.Contains("BrawlerPreviewAdapter.ShowIdle", portraits);
            StringAssert.DoesNotContain("ResolveIdleState", portraits);
            StringAssert.DoesNotContain("def.prefab", portraits);
            StringAssert.DoesNotContain("animator.Play(", portraits);
        }

        static BrawlerDefinition LoadDefinition(string id)
        {
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(
                HeavyHeroBuilder.PrefabPath(id, true));
            Assert.NotNull(prefab, "Missing TopDown human prefab for '" + id +
                "'. Run HeavyHeroBuilder.EnsureAssets first.");
            return new BrawlerDefinition
            {
                id = id,
                humanBodyPrefab = prefab,
            };
        }
    }
}
