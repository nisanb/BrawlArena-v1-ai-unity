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
        public void TwoPreviewSceneBuildsPreserveRimeAndThornGuidsAndCallerScene()
        {
            string[] before = GeneratedPaths.Select(AssetDatabase.AssetPathToGUID).ToArray();
            string thornHumanBefore = AssetDatabase.AssetPathToGUID(
                InvectorThornMigrationBuilder.ProductionHumanPrefabPath);
            string thornAIBefore = AssetDatabase.AssetPathToGUID(
                InvectorThornMigrationBuilder.ProductionAIPrefabPath);
            Assert.That(before.All(value => !string.IsNullOrEmpty(value)), Is.True);

            Scene sceneBefore = SceneManager.GetActiveScene();
            string pathBefore = sceneBefore.path;
            bool dirtyBefore = sceneBefore.isDirty;

            Assert.DoesNotThrow(() => InvectorRimeMigrationBuilder.BuildRimePilotAssetsSafely());
            Assert.DoesNotThrow(() => InvectorRimeMigrationBuilder.BuildRimePilotAssetsSafely());

            string[] after = GeneratedPaths.Select(AssetDatabase.AssetPathToGUID).ToArray();
            Assert.That(after, Is.EqualTo(before));
            Assert.That(AssetDatabase.AssetPathToGUID(
                InvectorThornMigrationBuilder.ProductionHumanPrefabPath),
                Is.EqualTo(thornHumanBefore));
            Assert.That(AssetDatabase.AssetPathToGUID(
                InvectorThornMigrationBuilder.ProductionAIPrefabPath),
                Is.EqualTo(thornAIBefore));

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
                id = "thorn",
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
        public void GeneratedRosterPinsAllInvectorHumanAndAIRoles()
        {
            // Three-hero roster (frost, thorn, bastion).
            BrawlerDefinition[] roster = ArenaSceneBuilder.BuildRosterFromExistingAssets();
            BrawlerDefinition rime = roster.Single(value => value.id == "frost");
            BrawlerDefinition thorn = roster.Single(value => value.id == "thorn");
            BrawlerDefinition bastion = roster.Single(value => value.id == "bastion");
            Assert.That(rime.invectorHumanPrefab, Is.SameAs(Require(
                InvectorRimeMigrationBuilder.ProductionHumanPrefabPath)));
            Assert.That(rime.invectorAIPrefab, Is.SameAs(Require(
                InvectorRimeMigrationBuilder.ProductionAIPrefabPath)));
            Assert.That(thorn.invectorHumanPrefab, Is.SameAs(Require(
                InvectorThornMigrationBuilder.ProductionHumanPrefabPath)));
            Assert.That(thorn.invectorAIPrefab, Is.SameAs(Require(
                InvectorThornMigrationBuilder.ProductionAIPrefabPath)));
            Assert.That(bastion.invectorHumanPrefab, Is.SameAs(Require(
                InvectorBastionMigrationBuilder.ProductionHumanPrefabPath)));
            Assert.That(bastion.invectorAIPrefab, Is.SameAs(Require(
                InvectorBastionMigrationBuilder.ProductionAIPrefabPath)));

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
