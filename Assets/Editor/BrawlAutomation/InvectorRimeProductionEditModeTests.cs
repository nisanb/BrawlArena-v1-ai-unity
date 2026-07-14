using System;
using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace BrawlArena.EditorAutomation.Tests
{
    public sealed class InvectorRimeProductionEditModeTests
    {
        static readonly string[] GeneratedPaths =
        {
            InvectorRimeMigrationBuilder.OverrideControllerPath,
            InvectorRimeMigrationBuilder.WeaponIKAdjustPath,
            InvectorRimeMigrationBuilder.WeaponIKAdjustListPath,
            InvectorRimeMigrationBuilder.WeaponPrefabPath,
            InvectorRimeMigrationBuilder.PilotPrefabPath,
            InvectorRimeMigrationBuilder.ProductionHumanPrefabPath,
            InvectorRimeMigrationBuilder.ProductionAIPrefabPath,
        };

        GameObject temporaryRoot;

        [TearDown]
        public void TearDown()
        {
            if (temporaryRoot != null) Object.DestroyImmediate(temporaryRoot);
        }

        [Test]
        [Category("InvectorProductionRime")]
        public void FrostDerivedPilotAndBothProductionVariantsPassBuilderAudits()
        {
            Assert.DoesNotThrow(InvectorRimeMigrationBuilder.ValidatePrerequisites);
            Assert.DoesNotThrow(() => InvectorRimeMigrationBuilder.ValidatePilot(
                Require(InvectorRimeMigrationBuilder.PilotPrefabPath)));
            Assert.DoesNotThrow(() => InvectorRimeMigrationBuilder.ValidateHumanPrefab(
                Require(InvectorRimeMigrationBuilder.ProductionHumanPrefabPath)));
            Assert.DoesNotThrow(() => InvectorRimeMigrationBuilder.ValidateAIPrefab(
                Require(InvectorRimeMigrationBuilder.ProductionAIPrefabPath)));
        }

        [Test]
        [Category("InvectorProductionRime")]
        public void TwoPreviewSceneBuildsPreserveRimeAndCinderGuidsAndCallerScene()
        {
            string[] before = GeneratedPaths.Select(AssetDatabase.AssetPathToGUID).ToArray();
            string cinderHumanBefore = AssetDatabase.AssetPathToGUID(
                InvectorMigrationPilotBuilder.ProductionHumanPrefabPath);
            string cinderAIBefore = AssetDatabase.AssetPathToGUID(
                InvectorMigrationPilotBuilder.ProductionAIPrefabPath);
            Assert.That(before.All(value => !string.IsNullOrEmpty(value)), Is.True);

            Scene sceneBefore = SceneManager.GetActiveScene();
            string pathBefore = sceneBefore.path;
            bool dirtyBefore = sceneBefore.isDirty;

            Assert.DoesNotThrow(() => InvectorRimeMigrationBuilder.BuildRimePilotAssetsSafely());
            Assert.DoesNotThrow(() => InvectorRimeMigrationBuilder.BuildRimePilotAssetsSafely());

            string[] after = GeneratedPaths.Select(AssetDatabase.AssetPathToGUID).ToArray();
            Assert.That(after, Is.EqualTo(before));
            Assert.That(AssetDatabase.AssetPathToGUID(
                InvectorMigrationPilotBuilder.ProductionHumanPrefabPath),
                Is.EqualTo(cinderHumanBefore));
            Assert.That(AssetDatabase.AssetPathToGUID(
                InvectorMigrationPilotBuilder.ProductionAIPrefabPath),
                Is.EqualTo(cinderAIBefore));

            Scene sceneAfter = SceneManager.GetActiveScene();
            Assert.That(sceneAfter.path, Is.EqualTo(pathBefore));
            Assert.That(sceneAfter.isDirty, Is.EqualTo(dirtyBefore));
        }

        [Test]
        [Category("InvectorProductionRime")]
        public void ExactPrefabIdentityRejectsWrongRosterAndRoleBeforeClone()
        {
            GameObject human = Require(InvectorRimeMigrationBuilder.ProductionHumanPrefabPath);
            GameObject ai = Require(InvectorRimeMigrationBuilder.ProductionAIPrefabPath);
            var definition = new BrawlerDefinition
            {
                id = "fire",
                displayName = "Wrong Rime Assignment",
                invectorHumanPrefab = human,
                invectorAIPrefab = ai,
            };
            int before = Object.FindObjectsByType<BrawlerController>(
                FindObjectsInactive.Include, FindObjectsSortMode.None).Length;

            Assert.Throws<InvalidOperationException>(() =>
                BrawlerCharacterAssembly.Assemble(definition, TeamId.Blue, Vector3.zero,
                    true, 1f, BrawlerAssemblyContext.ProductionHumanInvector));
            Assert.Throws<InvalidOperationException>(() =>
                BrawlerCharacterAssembly.Assemble(definition, TeamId.Blue, Vector3.zero,
                    false, 1f, BrawlerAssemblyContext.ProductionAIInvector));

            definition.id = InvectorRimeMigrationBuilder.RosterId;
            definition.invectorHumanPrefab = ai;
            Assert.Throws<InvalidOperationException>(() =>
                BrawlerCharacterAssembly.Assemble(definition, TeamId.Blue, Vector3.zero,
                    true, 1f, BrawlerAssemblyContext.ProductionHumanInvector));
            definition.invectorHumanPrefab = human;
            Assert.Throws<NotSupportedException>(() =>
                BrawlerCharacterAssembly.Assemble(definition, TeamId.Blue, Vector3.zero,
                    false, 1f, BrawlerAssemblyContext.ProductionHumanInvector));
            Assert.Throws<NotSupportedException>(() =>
                BrawlerCharacterAssembly.Assemble(definition, TeamId.Blue, Vector3.zero,
                    true, 1f, BrawlerAssemblyContext.ProductionAIInvector));

            int after = Object.FindObjectsByType<BrawlerController>(
                FindObjectsInactive.Include, FindObjectsSortMode.None).Length;
            Assert.That(after, Is.EqualTo(before));
        }

        [Test]
        [Category("InvectorProductionRime")]
        public void GeneratedRosterPinsAllFourInvectorHumanAndAIRoles()
        {
            BrawlerDefinition[] roster = ArenaSceneBuilder.BuildRosterFromExistingAssets();
            BrawlerDefinition cinder = roster.Single(value => value.id == "fire");
            BrawlerDefinition rime = roster.Single(value => value.id == "frost");
            BrawlerDefinition tempest = roster.Single(value => value.id == "storm");
            BrawlerDefinition thorn = roster.Single(value => value.id == "thorn");
            Assert.That(cinder.invectorHumanPrefab, Is.Not.Null);
            Assert.That(cinder.invectorAIPrefab, Is.Not.Null);
            Assert.That(rime.invectorHumanPrefab, Is.SameAs(Require(
                InvectorRimeMigrationBuilder.ProductionHumanPrefabPath)));
            Assert.That(rime.invectorAIPrefab, Is.SameAs(Require(
                InvectorRimeMigrationBuilder.ProductionAIPrefabPath)));
            Assert.That(tempest.invectorHumanPrefab, Is.SameAs(Require(
                InvectorTempestMigrationBuilder.ProductionHumanPrefabPath)));
            Assert.That(tempest.invectorAIPrefab, Is.SameAs(Require(
                InvectorTempestMigrationBuilder.ProductionAIPrefabPath)));
            Assert.That(thorn.invectorHumanPrefab, Is.SameAs(Require(
                InvectorThornMigrationBuilder.ProductionHumanPrefabPath)));
            Assert.That(thorn.invectorAIPrefab, Is.SameAs(Require(
                InvectorThornMigrationBuilder.ProductionAIPrefabPath)));
            Assert.That(roster.Where(value => value.id != "fire" &&
                                               value.id != "frost" &&
                                               value.id != "storm" &&
                                               value.id != "thorn")
                .All(value => value.invectorHumanPrefab == null &&
                              value.invectorAIPrefab == null), Is.True);

            Assert.That(roster.All(value => value.invectorHumanPrefab != null &&
                                            value.invectorAIPrefab != null), Is.True);
        }

        [Test]
        [Category("InvectorProductionRime")]
        public void GenericContextsHaveStableDistinctRoleValues()
        {
            Assert.That((int)BrawlerAssemblyContext.ProductionHumanInvector,
                Is.Not.EqualTo((int)BrawlerAssemblyContext.ProductionAIInvector));
            Assert.That((int)BrawlerAssemblyContext.ProductionAIInvector,
                Is.GreaterThan((int)BrawlerAssemblyContext.Default));
        }

        static GameObject Require(string path)
        {
            GameObject asset = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            Assert.That(asset, Is.Not.Null, "Missing generated Rime asset " + path + ".");
            return asset;
        }
    }
}
