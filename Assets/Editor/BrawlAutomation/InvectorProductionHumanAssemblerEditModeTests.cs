using System;
using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.AI;
using Object = UnityEngine.Object;

namespace BrawlArena.EditorAutomation.Tests
{
    public sealed class InvectorProductionHumanAssemblerEditModeTests
    {
        const string AssemblerSourcePath =
            "Assets/Scripts/Brawl/Integration/Invector/InvectorHumanBrawlerCharacterAssembler.cs";
        const string GateSourcePath =
            "Assets/Scripts/Brawl/Integration/Invector/InvectorHumanRuntimeGate.cs";

        GameObject temporaryRoot;

        [TearDown]
        public void TearDown()
        {
            if (temporaryRoot != null) Object.DestroyImmediate(temporaryRoot);
        }

        [Test]
        [Category("InvectorProductionHumanCinder")]
        public void GeneratedProductionVariantIsDormantSelectiveAndBuilderValidated()
        {
            GameObject prefab = RequireProductionPrefab();

            Assert.DoesNotThrow(() =>
                InvectorMigrationPilotBuilder.ValidateProductionHumanPrefab(prefab));
            Assert.That(prefab.activeSelf, Is.False);
            Assert.That(prefab.layer, Is.EqualTo(InvectorMigrationPilotBuilder.InvectorPlayerLayer));
            Assert.That(prefab.GetComponents<Health>(), Has.Length.EqualTo(1));
            Assert.That(prefab.GetComponents<BrawlerController>(), Has.Length.EqualTo(1));
            Assert.That(prefab.GetComponents<PlayerBrawlerInput>(), Has.Length.EqualTo(1));
            Assert.That(prefab.GetComponents<InvectorHumanRuntimeGate>(), Has.Length.EqualTo(1));
            Assert.That(prefab.GetComponents<InvectorBrawlerMotor>(), Has.Length.EqualTo(1));
            Assert.That(prefab.GetComponents<InvectorBrawlerAnimationDriver>(), Has.Length.EqualTo(1));
            Assert.That(prefab.GetComponents<InvectorBrawlerWeaponPresentation>(), Has.Length.EqualTo(1));
            Assert.That(prefab.GetComponentsInChildren<CharacterController>(true), Is.Empty);
            Assert.That(prefab.GetComponentsInChildren<NavMeshAgent>(true), Is.Empty);

            InvectorShooterMeleeInputAdapter input =
                prefab.GetComponent<InvectorShooterMeleeInputAdapter>();
            Assert.That(input.MovementFeedMode, Is.EqualTo(InvectorMovementFeedMode.BufferedMotor));
            Assert.That(input.ProjectMoveActionOwnedByAdapter, Is.False);
            Assert.That(input.InputUpdateCount, Is.Zero);
            Assert.That(input.MoveReadCount, Is.Zero);
            Assert.That(prefab.GetComponent<InvectorHumanRuntimeGate>().IsDormantConfigured, Is.True);

            BrawlerHitProxy proxy = prefab.GetComponentInChildren<BrawlerHitProxy>(true);
            Assert.That(proxy, Is.Not.Null);
            Assert.That(proxy.gameObject.layer, Is.EqualTo(CombatPhysics.BrawlerHitboxLayer));
            Assert.That(proxy.enabled, Is.False);
            Assert.That(proxy.TriggerCollider.enabled, Is.False);
            Assert.That(prefab.GetComponentsInChildren<Transform>(true)
                .Where(value => value != prefab.transform && value.gameObject.layer ==
                    InvectorMigrationPilotBuilder.InvectorPlayerLayer), Is.Empty,
                "Layer 23 must remain root-only in the production variant.");
        }

        [Test]
        [Category("InvectorProductionHumanCinder")]
        public void ProductionAssetIsAStableVariantOfTheDormantPilot()
        {
            GameObject prefab = RequireProductionPrefab();
            Assert.That(PrefabUtility.GetPrefabAssetType(prefab), Is.EqualTo(PrefabAssetType.Variant));
            GameObject source = PrefabUtility.GetCorrespondingObjectFromSource(prefab);
            Assert.That(source, Is.Not.Null);
            Assert.That(AssetDatabase.GetAssetPath(source),
                Is.EqualTo(InvectorMigrationPilotBuilder.PrefabPath));
            Assert.That(AssetDatabase.AssetPathToGUID(
                InvectorMigrationPilotBuilder.ProductionHumanPrefabPath),
                Is.EqualTo("6aaadd902b169c74098d8c2bfa77ea0a").IgnoreCase);
        }

        [Test]
        [Category("InvectorProductionHumanCinder")]
        public void InvalidProductionContextsFailBeforeInstantiation()
        {
            GameObject prefab = RequireProductionPrefab();
            var definition = new BrawlerDefinition
            {
                id = "fire",
                displayName = "Cinder",
                invectorHumanPrefab = prefab,
            };
            int before = Object.FindObjectsByType<BrawlerController>(
                FindObjectsInactive.Include, FindObjectsSortMode.None).Length;

            Assert.Throws<NotSupportedException>(() =>
                BrawlerCharacterAssembly.Assemble(definition, TeamId.Blue, Vector3.zero,
                    false, 1f, BrawlerAssemblyContext.ProductionHumanInvector));
            definition.id = "frost";
            Assert.Throws<InvalidOperationException>(() =>
                BrawlerCharacterAssembly.Assemble(definition, TeamId.Blue, Vector3.zero,
                    true, 1f, BrawlerAssemblyContext.ProductionHumanInvector));
            definition.id = "fire";
            definition.invectorHumanPrefab = null;
            Assert.Throws<InvalidOperationException>(() =>
                BrawlerCharacterAssembly.Assemble(definition, TeamId.Blue, Vector3.zero,
                    true, 1f, BrawlerAssemblyContext.ProductionHumanInvector));

            int after = Object.FindObjectsByType<BrawlerController>(
                FindObjectsInactive.Include, FindObjectsSortMode.None).Length;
            Assert.That(after, Is.EqualTo(before));
        }

        [Test]
        [Category("InvectorProductionHumanCinder")]
        public void GeneratedRosterAssignsExactCinderRimeTempestThornInvectorVariants()
        {
            BrawlerDefinition[] roster = ArenaSceneBuilder.BuildRosterFromExistingAssets();
            Assert.That(roster, Is.Not.Empty);
            BrawlerDefinition cinder = roster.Single(value => value.id == "fire");
            Assert.That(cinder.invectorHumanPrefab, Is.SameAs(RequireProductionPrefab()));
            BrawlerDefinition rime = roster.Single(value => value.id == "frost");
            Assert.That(rime.invectorHumanPrefab, Is.SameAs(
                AssetDatabase.LoadAssetAtPath<GameObject>(
                    InvectorRimeMigrationBuilder.ProductionHumanPrefabPath)));
            BrawlerDefinition tempest = roster.Single(value => value.id == "storm");
            Assert.That(tempest.invectorHumanPrefab, Is.SameAs(
                AssetDatabase.LoadAssetAtPath<GameObject>(
                    InvectorTempestMigrationBuilder.ProductionHumanPrefabPath)));
            BrawlerDefinition thorn = roster.Single(value => value.id == "thorn");
            Assert.That(thorn.invectorHumanPrefab, Is.SameAs(
                AssetDatabase.LoadAssetAtPath<GameObject>(
                    InvectorThornMigrationBuilder.ProductionHumanPrefabPath)));
            Assert.That(roster.Where(value => value.id != "fire" &&
                                               value.id != "frost" &&
                                               value.id != "storm" &&
                                               value.id != "thorn")
                .All(value => value.invectorHumanPrefab == null), Is.True);

            Assert.That(roster.All(value => value.invectorHumanPrefab != null &&
                                            value.invectorAIPrefab != null), Is.True);
        }

        [Test]
        [Category("InvectorProductionHumanCinder")]
        public void ProductionSourceKeepsCombatAndPhysicalInputBehindBrawlBoundaries()
        {
            string assembler = System.IO.File.ReadAllText(AssemblerSourcePath);
            string gate = System.IO.File.ReadAllText(GateSourcePath);

            StringAssert.DoesNotContain("SetLayerRecursively", assembler);
            StringAssert.DoesNotContain(".Shoot(", assembler);
            StringAssert.DoesNotContain(".Reload(", assembler);
            StringAssert.DoesNotContain("TakeDamage(", assembler);
            StringAssert.DoesNotContain("currentHealth =", gate);
            StringAssert.DoesNotContain(".Shoot(", gate);
            StringAssert.DoesNotContain(".Reload(", gate);
            StringAssert.DoesNotContain("InputSystem", gate);
            StringAssert.Contains("InvectorMovementFeedMode.BufferedMotor", gate);
            StringAssert.Contains("playerInput.enabled = true", gate);
        }

        static GameObject RequireProductionPrefab()
        {
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(
                InvectorMigrationPilotBuilder.ProductionHumanPrefabPath);
            Assert.That(prefab, Is.Not.Null,
                "Run InvectorMigrationPilotBuilder.BuildPilotAssets first.");
            return prefab;
        }
    }
}
