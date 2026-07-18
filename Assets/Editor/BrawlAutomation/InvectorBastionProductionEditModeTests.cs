using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace BrawlArena.EditorAutomation.Tests
{
    public sealed class InvectorBastionProductionEditModeTests
    {
        static readonly string[] GeneratedPaths =
        {
            InvectorBastionMigrationBuilder.OverrideControllerPath,
            InvectorBastionMigrationBuilder.WeaponIKAdjustPath,
            InvectorBastionMigrationBuilder.WeaponIKAdjustListPath,
            InvectorBastionMigrationBuilder.WeaponPrefabPath,
            InvectorBastionMigrationBuilder.PilotPrefabPath,
            InvectorBastionMigrationBuilder.ProductionHumanPrefabPath,
            InvectorBastionMigrationBuilder.ProductionAIPrefabPath,
        };

        GameObject temporaryRoot;

        [TearDown]
        public void TearDown()
        {
            if (temporaryRoot != null) Object.DestroyImmediate(temporaryRoot);
        }

        [Test]
        [Category("InvectorProductionBastion")]
        public void BastionDerivedPilotAndBothProductionVariantsPassBuilderAudits()
        {
            Assert.DoesNotThrow(InvectorBastionMigrationBuilder.ValidatePrerequisites);
            Assert.DoesNotThrow(() => InvectorBastionMigrationBuilder.ValidatePilot(
                Require(InvectorBastionMigrationBuilder.PilotPrefabPath)));
            Assert.DoesNotThrow(() => InvectorBastionMigrationBuilder.ValidateHumanPrefab(
                Require(InvectorBastionMigrationBuilder.ProductionHumanPrefabPath)));
            Assert.DoesNotThrow(() => InvectorBastionMigrationBuilder.ValidateAIPrefab(
                Require(InvectorBastionMigrationBuilder.ProductionAIPrefabPath)));
        }

        [Test]
        [Category("InvectorProductionBastion")]
        public void TwoPreviewSceneBuildsAreIdempotentAndPreserveCallerScene()
        {
            string[] before = GeneratedPaths.Select(AssetDatabase.AssetPathToGUID).ToArray();
            Assert.That(before.All(value => !string.IsNullOrEmpty(value)), Is.True);

            Scene sceneBefore = SceneManager.GetActiveScene();
            string pathBefore = sceneBefore.path;
            bool dirtyBefore = sceneBefore.isDirty;

            Assert.DoesNotThrow(() => InvectorBastionMigrationBuilder.BuildBastionPilotAssetsSafely());
            Assert.DoesNotThrow(() => InvectorBastionMigrationBuilder.BuildBastionPilotAssetsSafely());

            string[] after = GeneratedPaths.Select(AssetDatabase.AssetPathToGUID).ToArray();
            Assert.That(after, Is.EqualTo(before));

            Scene sceneAfter = SceneManager.GetActiveScene();
            Assert.That(sceneAfter.path, Is.EqualTo(pathBefore));
            Assert.That(sceneAfter.isDirty, Is.EqualTo(dirtyBefore));
        }

        [Test]
        [Category("InvectorProductionBastion")]
        public void ProductionVariantsAreInactiveDormantAndBrawlerControllerCompatible()
        {
            GameObject human = Require(InvectorBastionMigrationBuilder.ProductionHumanPrefabPath);
            GameObject ai = Require(InvectorBastionMigrationBuilder.ProductionAIPrefabPath);

            Assert.That(human.activeSelf, Is.False);
            Assert.That(ai.activeSelf, Is.False);
            Assert.That(human.GetComponent<BrawlerController>(), Is.Not.Null);
            Assert.That(ai.GetComponent<BrawlerController>(), Is.Not.Null);
            Assert.That(human.GetComponent<Health>(), Is.Not.Null);
            Assert.That(ai.GetComponent<Health>(), Is.Not.Null);
            Assert.That(human.GetComponent<PlayerBrawlerInput>(), Is.Not.Null);
            Assert.That(ai.GetComponent<AIBrawler>(), Is.Not.Null);
        }

        [Test]
        [Category("InvectorProductionBastion")]
        public void ExactPrefabIdentityRejectsWrongRosterAndRoleBeforeClone()
        {
            GameObject human = Require(InvectorBastionMigrationBuilder.ProductionHumanPrefabPath);
            GameObject ai = Require(InvectorBastionMigrationBuilder.ProductionAIPrefabPath);
            var definition = new BrawlerDefinition
            {
                id = "thorn",
                displayName = "Wrong Bastion Assignment",
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

            definition.id = InvectorBastionMigrationBuilder.RosterId;
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
        [Category("InvectorProductionBastion")]
        public void OverrideControllerHasExactlyThreeSwordShieldSlots()
        {
            GameObject pilot = Require(InvectorBastionMigrationBuilder.PilotPrefabPath);
            Animator animator = pilot.GetComponent<Animator>();
            var overrides = animator.runtimeAnimatorController as AnimatorOverrideController;
            Assert.That(overrides, Is.Not.Null);

            var slots = new List<KeyValuePair<AnimationClip, AnimationClip>>(overrides.overridesCount);
            overrides.GetOverrides(slots);
            KeyValuePair<AnimationClip, AnimationClip>[] active = slots
                .Where(value => value.Key != null && value.Value != null && value.Value != value.Key)
                .ToArray();

            Assert.That(active.Length, Is.EqualTo(3));
            Assert.That(active.Any(value =>
                value.Key.name == InvectorBastionMigrationBuilder.BasicAttackOverrideSourceName &&
                value.Value.name == InvectorBastionMigrationBuilder.BasicAttackClipName), Is.True);
            Assert.That(active.Any(value =>
                value.Key.name == InvectorBastionMigrationBuilder.SuperAttackOverrideSourceName &&
                value.Value.name == InvectorBastionMigrationBuilder.SuperAttackClipName), Is.True);
            Assert.That(active.Any(value =>
                value.Key.name == InvectorBastionMigrationBuilder.CarryPoseOverrideSourceName &&
                value.Value.name == InvectorBastionMigrationBuilder.CarryPoseClipName), Is.True);
        }

        [Test]
        [Category("InvectorProductionBastion")]
        public void GeneratedRosterPinsBastionInvectorHumanAndAIRoles()
        {
            BrawlerDefinition[] roster = ArenaSceneBuilder.BuildRosterFromExistingAssets();
            BrawlerDefinition bastion = roster.Single(value => value.id == "bastion");

            Assert.That(bastion.role, Is.EqualTo("Vanguard"));
            Assert.That(bastion.invectorHumanPrefab, Is.SameAs(Require(
                InvectorBastionMigrationBuilder.ProductionHumanPrefabPath)));
            Assert.That(bastion.invectorAIPrefab, Is.SameAs(Require(
                InvectorBastionMigrationBuilder.ProductionAIPrefabPath)));
        }

        static GameObject Require(string path)
        {
            GameObject asset = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            Assert.That(asset, Is.Not.Null, "Missing generated Bastion asset " + path + ".");
            return asset;
        }
    }
}
