using System.IO;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace BrawlArena.EditorAutomation
{
    [ExecuteAlways]
    [AddComponentMenu("")]
    public sealed class BrawlerPreviewAwakeMutationProbe : MonoBehaviour
    {
        public static int MutationCount { get; private set; }

        public static void ResetMutationCount()
        {
            MutationCount = 0;
        }

        void Awake()
        {
            MutationCount++;
            AudioSource source = gameObject.AddComponent<AudioSource>();
            source.playOnAwake = false;
        }
    }

    public class BrawlerPreviewAdapterEditModeTests
    {
        static readonly (string id, string path)[] ProductionHumans =
        {
            ("frost", "Assets/Generated/InvectorMigration/Rime/Prefabs/RimeInvectorHuman.prefab"),
            ("thorn", "Assets/Generated/InvectorMigration/Thorn/Prefabs/ThornInvectorHuman.prefab"),
        };

        [Test]
        public void ProductionHumansResolveAndShareExactLifecycleController()
        {
            RuntimeAnimatorController sharedController = null;
            foreach ((string id, string path) in ProductionHumans)
            {
                BrawlerDefinition definition = LoadDefinition(id, path);

                GameObject resolved = BrawlerPreviewAdapter.ResolvePrefab(definition);

                Assert.AreSame(definition.invectorHumanPrefab, resolved);
                Assert.IsFalse(resolved.activeSelf);
                var overrides = resolved.GetComponent<Animator>().runtimeAnimatorController
                    as AnimatorOverrideController;
                Assert.NotNull(overrides, id + " must use an AnimatorOverrideController");
                if (sharedController == null)
                    sharedController = overrides.runtimeAnimatorController;
                else
                    Assert.AreSame(sharedController, overrides.runtimeAnimatorController,
                        id + " does not use the exact shared lifecycle controller asset");
            }
        }

        [Test]
        public void OnlyThornOwnsTheBuilderAuthoredMenuPose()
        {
            foreach ((string id, string path) in ProductionHumans)
            {
                GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                Assert.NotNull(prefab, path);
                InvectorBrawlerWeaponPresentation presenter =
                    prefab.GetComponent<InvectorBrawlerWeaponPresentation>();
                Assert.NotNull(presenter, id);
                Assert.AreEqual(id == "thorn", presenter.HasAuthoredPreviewPose,
                    id + " authored-preview posture changed");
                Assert.AreEqual(id == "thorn", presenter.HideAuthoredArrowInPreview,
                    id + " preview arrow posture changed");
            }
        }

        [Test]
        public void PreparedPreviewEnablesOnlyAnimatorAndProjectPresentationBoundary()
        {
            BrawlerDefinition definition = LoadDefinition(ProductionHumans[0].id,
                ProductionHumans[0].path);
            GameObject preview = Object.Instantiate(definition.invectorHumanPrefab);
            try
            {
                Assert.IsFalse(preview.activeSelf);

                BrawlerPreviewAdapter.Prepare(preview, definition);
                BrawlerPreviewAdapter.ShowIdle(preview, definition, 0.3f);
                BrawlerPreviewAdapter.ShowVictory(preview, definition);

                Assert.IsTrue(preview.activeSelf);
                Animator animator = preview.GetComponent<Animator>();
                InvectorBrawlerWeaponPresentation presenter =
                    preview.GetComponent<InvectorBrawlerWeaponPresentation>();
                Assert.IsTrue(animator.enabled);
                Assert.IsFalse(animator.applyRootMotion);
                Assert.NotNull(presenter);
                Assert.IsTrue(presenter.enabled);
                Assert.IsTrue(presenter.PreviewEnabled);
                Assert.IsFalse(presenter.RuntimeEnabled);
                foreach (Behaviour behaviour in preview.GetComponentsInChildren<Behaviour>(true))
                    Assert.AreEqual(
                        behaviour == animator || behaviour == presenter,
                        behaviour.enabled,
                        behaviour.GetType().FullName);
                foreach (Collider collider in preview.GetComponentsInChildren<Collider>(true))
                    Assert.IsFalse(collider.enabled, collider.GetType().FullName);
                foreach (Rigidbody body in preview.GetComponentsInChildren<Rigidbody>(true))
                {
                    Assert.IsTrue(body.isKinematic);
                    Assert.IsFalse(body.useGravity);
                    Assert.IsFalse(body.detectCollisions);
                    Assert.AreEqual(RigidbodyConstraints.FreezeAll, body.constraints);
                }
            }
            finally
            {
                Object.DestroyImmediate(preview);
            }
        }

        [Test]
        public void PrepareRequarantinesComponentsAddedByDisabledAwake()
        {
            BrawlerDefinition definition = LoadDefinition(ProductionHumans[0].id,
                ProductionHumans[0].path);
            GameObject preview = Object.Instantiate(definition.invectorHumanPrefab);
            try
            {
                BrawlerPreviewAwakeMutationProbe.ResetMutationCount();
                var probe = preview.AddComponent<BrawlerPreviewAwakeMutationProbe>();
                Assert.IsFalse(preview.activeSelf);
                Assert.Zero(BrawlerPreviewAwakeMutationProbe.MutationCount,
                    "Awake ran before the dormant preview activation boundary.");
                Assert.IsEmpty(preview.GetComponentsInChildren<AudioSource>(true));

                BrawlerPreviewAdapter.Prepare(preview, definition);

                Assert.GreaterOrEqual(BrawlerPreviewAwakeMutationProbe.MutationCount, 1,
                    "The regression probe did not mutate the component graph during activation.");
                Assert.IsFalse(probe.enabled);
                AudioSource[] activationSources = preview.GetComponentsInChildren<AudioSource>(true);
                Assert.IsNotEmpty(activationSources,
                    "The Awake mutation did not add its default-enabled AudioSource.");
                foreach (AudioSource source in activationSources)
                    Assert.IsFalse(source.enabled,
                        "An activation-time AudioSource escaped the second quarantine pass.");

                Animator animator = preview.GetComponent<Animator>();
                InvectorBrawlerWeaponPresentation presenter =
                    preview.GetComponent<InvectorBrawlerWeaponPresentation>();
                Assert.IsTrue(animator.enabled);
                Assert.NotNull(presenter);
                Assert.IsTrue(presenter.PreviewEnabled);
                Assert.IsFalse(presenter.RuntimeEnabled);
                foreach (Behaviour behaviour in preview.GetComponentsInChildren<Behaviour>(true))
                    Assert.AreEqual(
                        behaviour == animator || behaviour == presenter,
                        behaviour.enabled,
                        behaviour.GetType().FullName);
            }
            finally
            {
                BrawlerPreviewAwakeMutationProbe.ResetMutationCount();
                Object.DestroyImmediate(preview);
            }
        }

        [Test]
        public void ResolverRejectsMissingOrMismatchedHumanData()
        {
            Assert.Throws<System.InvalidOperationException>(() =>
                BrawlerPreviewAdapter.ResolvePrefab(new BrawlerDefinition { id = "frost" }));

            BrawlerDefinition mismatched = LoadDefinition("thorn", ProductionHumans[0].path);
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

        static BrawlerDefinition LoadDefinition(string id, string path)
        {
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            Assert.NotNull(prefab, path);
            return new BrawlerDefinition
            {
                id = id,
                invectorHumanPrefab = prefab,
            };
        }
    }
}
