using System;
using System.Linq;
using Invector.IK;
using Invector.vShooter;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace BrawlArena.EditorAutomation.Tests
{
    public sealed class InvectorTempestProductionEditModeTests
    {
        static readonly string[] GeneratedPaths =
        {
            InvectorTempestMigrationBuilder.OverrideControllerPath,
            InvectorTempestMigrationBuilder.WeaponIKAdjustPath,
            InvectorTempestMigrationBuilder.WeaponIKAdjustListPath,
            InvectorTempestMigrationBuilder.WeaponPrefabPath,
            InvectorTempestMigrationBuilder.PilotPrefabPath,
            InvectorTempestMigrationBuilder.ProductionHumanPrefabPath,
            InvectorTempestMigrationBuilder.ProductionAIPrefabPath,
        };

        GameObject temporaryRoot;

        [TearDown]
        public void TearDown()
        {
            if (temporaryRoot != null) Object.DestroyImmediate(temporaryRoot);
        }

        [Test]
        [Category("InvectorProductionTempest")]
        public void StormStaff03SourceAndAllTempestVariantsPassPinnedBuilderAudits()
        {
            Assert.That(AssetDatabase.AssetPathToGUID(
                    InvectorTempestMigrationBuilder.StormPath),
                Is.EqualTo(InvectorTempestMigrationBuilder.StormGuid));
            Assert.That(AssetDatabase.AssetPathToGUID(
                    InvectorTempestMigrationBuilder.StormStaffMaterialPath),
                Is.EqualTo(InvectorTempestMigrationBuilder.StormStaffMaterialGuid));
            Assert.That(AssetDatabase.AssetPathToGUID(
                    InvectorTempestMigrationBuilder.StormBodyMaterialPath),
                Is.EqualTo(InvectorTempestMigrationBuilder.StormBodyMaterialGuid));
            Assert.That(AssetDatabase.AssetPathToGUID(
                    InvectorTempestMigrationBuilder.WizardAvatarPath),
                Is.EqualTo(InvectorTempestMigrationBuilder.WizardAvatarGuid));

            GameObject storm = Require(InvectorTempestMigrationBuilder.StormPath);
            Animator animator = storm.GetComponent<Animator>();
            Transform staff03 = Find(storm.transform, "Staff03");
            Transform spellOrigin = Find(staff03, "SpellOrigin");
            Assert.That(animator, Is.Not.Null);
            Assert.That(animator.isHuman, Is.True);
            Assert.That(animator.avatar, Is.Not.Null);
            Assert.That(AssetDatabase.GetAssetPath(animator.avatar),
                Is.EqualTo(InvectorTempestMigrationBuilder.WizardAvatarPath));
            Assert.That(staff03, Is.Not.Null);
            Assert.That(staff03.gameObject.activeSelf, Is.True);
            Assert.That(Find(storm.transform, "Staff01").gameObject.activeSelf, Is.False);
            Assert.That(Find(storm.transform, "Staff02").gameObject.activeSelf, Is.False);
            Assert.That(AssetDatabase.GetAssetPath(
                    staff03.GetComponent<Renderer>().sharedMaterial),
                Is.EqualTo(InvectorTempestMigrationBuilder.StormStaffMaterialPath));
            Assert.That(spellOrigin, Is.Not.Null);
            Assert.That(spellOrigin.localPosition.x, Is.EqualTo(0f).Within(0.0001f));
            Assert.That(spellOrigin.localPosition.y,
                Is.EqualTo(-15.185798f).Within(0.0001f));
            Assert.That(spellOrigin.localPosition.z,
                Is.EqualTo(44.064484f).Within(0.0001f));

            Assert.DoesNotThrow(InvectorTempestMigrationBuilder.ValidatePrerequisites);
            Assert.DoesNotThrow(() => InvectorTempestMigrationBuilder.ValidatePilot(
                Require(InvectorTempestMigrationBuilder.PilotPrefabPath)));
            Assert.DoesNotThrow(() => InvectorTempestMigrationBuilder.ValidateHumanPrefab(
                Require(InvectorTempestMigrationBuilder.ProductionHumanPrefabPath)));
            Assert.DoesNotThrow(() => InvectorTempestMigrationBuilder.ValidateAIPrefab(
                Require(InvectorTempestMigrationBuilder.ProductionAIPrefabPath)));

            GameObject pilot = Require(InvectorTempestMigrationBuilder.PilotPrefabPath);
            AnimatorOverrideController overrides = pilot.GetComponent<Animator>()
                .runtimeAnimatorController as AnimatorOverrideController;
            Assert.That(overrides, Is.Not.Null);
            Assert.That(AssetDatabase.GetAssetPath(overrides),
                Is.EqualTo(InvectorTempestMigrationBuilder.OverrideControllerPath));
            Assert.That(AssetDatabase.GetAssetPath(overrides.runtimeAnimatorController),
                Is.EqualTo(InvectorMigrationPilotBuilder.LifecycleControllerPath));

            vWeaponIKAdjust adjust = AssetDatabase.LoadAssetAtPath<vWeaponIKAdjust>(
                InvectorTempestMigrationBuilder.WeaponIKAdjustPath);
            vWeaponIKAdjustList adjustList =
                AssetDatabase.LoadAssetAtPath<vWeaponIKAdjustList>(
                    InvectorTempestMigrationBuilder.WeaponIKAdjustListPath);
            Assert.That(adjust, Is.Not.Null);
            Assert.That(adjustList, Is.Not.Null);
            Assert.That(adjustList.ikTargetPositionOffsetR, Is.EqualTo(Vector3.zero));
            Assert.That(adjustList.ikTargetPositionOffsetL, Is.EqualTo(Vector3.zero));
            Assert.That(adjust.ikAdjustsRight.Count, Is.EqualTo(4));
            Assert.That(adjust.ikAdjustsLeft.Count, Is.EqualTo(4));
            foreach (IKAdjust state in adjust.ikAdjustsRight.Concat(adjust.ikAdjustsLeft))
            {
                Assert.That(state.weaponHandOffset.position,
                    Is.EqualTo(new Vector3(-0.01f, 0f, 0f)));
                Assert.That(state.supportHandOffset.position,
                    Is.EqualTo(new Vector3(0.01f, 0f, 0f)));
                Assert.That(state.weaponHintOffset.position, Is.EqualTo(Vector3.zero));
                Assert.That(state.supportHintOffset.position, Is.EqualTo(Vector3.zero));
            }
        }

        [Test]
        [Category("InvectorProductionTempest")]
        public void TwoPreviewSceneBuildsPreserveTempestRimeCinderGuidsAndCallerScene()
        {
            string[] before = GeneratedPaths
                .Select(AssetDatabase.AssetPathToGUID)
                .ToArray();
            string cinderHumanBefore = AssetDatabase.AssetPathToGUID(
                InvectorMigrationPilotBuilder.ProductionHumanPrefabPath);
            string cinderAIBefore = AssetDatabase.AssetPathToGUID(
                InvectorMigrationPilotBuilder.ProductionAIPrefabPath);
            string rimeHumanBefore = AssetDatabase.AssetPathToGUID(
                InvectorRimeMigrationBuilder.ProductionHumanPrefabPath);
            string rimeAIBefore = AssetDatabase.AssetPathToGUID(
                InvectorRimeMigrationBuilder.ProductionAIPrefabPath);
            Assert.That(before.All(value => !string.IsNullOrEmpty(value)), Is.True);

            Scene sceneBefore = SceneManager.GetActiveScene();
            string pathBefore = sceneBefore.path;
            bool dirtyBefore = sceneBefore.isDirty;

            Assert.DoesNotThrow(() =>
                InvectorTempestMigrationBuilder.BuildTempestPilotAssetsSafely());
            Assert.DoesNotThrow(() =>
                InvectorTempestMigrationBuilder.BuildTempestPilotAssetsSafely());

            string[] after = GeneratedPaths
                .Select(AssetDatabase.AssetPathToGUID)
                .ToArray();
            Assert.That(after, Is.EqualTo(before));
            Assert.That(AssetDatabase.AssetPathToGUID(
                    InvectorMigrationPilotBuilder.ProductionHumanPrefabPath),
                Is.EqualTo(cinderHumanBefore));
            Assert.That(AssetDatabase.AssetPathToGUID(
                    InvectorMigrationPilotBuilder.ProductionAIPrefabPath),
                Is.EqualTo(cinderAIBefore));
            Assert.That(AssetDatabase.AssetPathToGUID(
                    InvectorRimeMigrationBuilder.ProductionHumanPrefabPath),
                Is.EqualTo(rimeHumanBefore));
            Assert.That(AssetDatabase.AssetPathToGUID(
                    InvectorRimeMigrationBuilder.ProductionAIPrefabPath),
                Is.EqualTo(rimeAIBefore));

            Scene sceneAfter = SceneManager.GetActiveScene();
            Assert.That(sceneAfter.path, Is.EqualTo(pathBefore));
            Assert.That(sceneAfter.isDirty, Is.EqualTo(dirtyBefore));
        }

        [Test]
        [Category("InvectorProductionTempest")]
        public void ExactPrefabIdentityRejectsWrongRosterAndRoleBeforeClone()
        {
            GameObject human = Require(
                InvectorTempestMigrationBuilder.ProductionHumanPrefabPath);
            GameObject ai = Require(
                InvectorTempestMigrationBuilder.ProductionAIPrefabPath);
            var definition = new BrawlerDefinition
            {
                id = InvectorRimeMigrationBuilder.RosterId,
                displayName = "Wrong Tempest Assignment",
                invectorHumanPrefab = human,
                invectorAIPrefab = ai,
            };
            int before = Object.FindObjectsByType<BrawlerController>(
                FindObjectsInactive.Include, FindObjectsSortMode.None).Length;

            Assert.Throws<InvalidOperationException>(() =>
                BrawlerCharacterAssembly.Assemble(
                    definition,
                    TeamId.Blue,
                    Vector3.zero,
                    true,
                    1f,
                    BrawlerAssemblyContext.ProductionHumanInvector));
            Assert.Throws<InvalidOperationException>(() =>
                BrawlerCharacterAssembly.Assemble(
                    definition,
                    TeamId.Blue,
                    Vector3.zero,
                    false,
                    1f,
                    BrawlerAssemblyContext.ProductionAIInvector));

            definition.id = InvectorTempestMigrationBuilder.RosterId;
            definition.invectorHumanPrefab = ai;
            Assert.Throws<InvalidOperationException>(() =>
                BrawlerCharacterAssembly.Assemble(
                    definition,
                    TeamId.Blue,
                    Vector3.zero,
                    true,
                    1f,
                    BrawlerAssemblyContext.ProductionHumanInvector));
            definition.invectorHumanPrefab = human;
            Assert.Throws<NotSupportedException>(() =>
                BrawlerCharacterAssembly.Assemble(
                    definition,
                    TeamId.Blue,
                    Vector3.zero,
                    false,
                    1f,
                    BrawlerAssemblyContext.ProductionHumanInvector));
            Assert.Throws<NotSupportedException>(() =>
                BrawlerCharacterAssembly.Assemble(
                    definition,
                    TeamId.Blue,
                    Vector3.zero,
                    true,
                    1f,
                    BrawlerAssemblyContext.ProductionAIInvector));

            int after = Object.FindObjectsByType<BrawlerController>(
                FindObjectsInactive.Include, FindObjectsSortMode.None).Length;
            Assert.That(after, Is.EqualTo(before));
        }

        [Test]
        [Category("InvectorProductionTempest")]
        public void GeneratedRosterPinsAllFourInvectorHumanAndAIRoles()
        {
            BrawlerDefinition[] roster = ArenaSceneBuilder.BuildRosterFromExistingAssets();
            BrawlerDefinition cinder = roster.Single(value => value.id == "fire");
            BrawlerDefinition rime = roster.Single(value => value.id == "frost");
            BrawlerDefinition tempest = roster.Single(value => value.id == "storm");
            BrawlerDefinition thorn = roster.Single(value => value.id == "thorn");
            Assert.That(cinder.invectorHumanPrefab, Is.Not.Null);
            Assert.That(cinder.invectorAIPrefab, Is.Not.Null);
            Assert.That(rime.invectorHumanPrefab, Is.Not.Null);
            Assert.That(rime.invectorAIPrefab, Is.Not.Null);
            Assert.That(tempest.invectorHumanPrefab, Is.SameAs(Require(
                InvectorTempestMigrationBuilder.ProductionHumanPrefabPath)));
            Assert.That(tempest.invectorAIPrefab, Is.SameAs(Require(
                InvectorTempestMigrationBuilder.ProductionAIPrefabPath)));
            Assert.That(thorn.invectorHumanPrefab, Is.SameAs(Require(
                InvectorThornMigrationBuilder.ProductionHumanPrefabPath)));
            Assert.That(thorn.invectorAIPrefab, Is.SameAs(Require(
                InvectorThornMigrationBuilder.ProductionAIPrefabPath)));
            Assert.That(roster.Where(value =>
                    value.id != "fire" && value.id != "frost" &&
                    value.id != "storm" && value.id != "thorn")
                .All(value => value.invectorHumanPrefab == null &&
                              value.invectorAIPrefab == null), Is.True);

            Assert.That(roster.All(value => value.invectorHumanPrefab != null &&
                                            value.invectorAIPrefab != null), Is.True);
        }

        [Test]
        [Category("InvectorProductionTempest")]
        public void GenericContextsAndExactTempestRolesAreInvectorOnly()
        {
            GameObject human = Require(
                InvectorTempestMigrationBuilder.ProductionHumanPrefabPath);
            GameObject ai = Require(
                InvectorTempestMigrationBuilder.ProductionAIPrefabPath);
            Assert.That(human.GetComponent<InvectorBrawlerPrefabIdentity>()
                    .Matches(
                        InvectorTempestMigrationBuilder.RosterId,
                        InvectorBrawlerPrefabRole.Human),
                Is.True);
            Assert.That(ai.GetComponent<InvectorBrawlerPrefabIdentity>()
                    .Matches(
                        InvectorTempestMigrationBuilder.RosterId,
                        InvectorBrawlerPrefabRole.AI),
                Is.True);
            Assert.That((int)BrawlerAssemblyContext.ProductionHumanInvector,
                Is.Not.EqualTo((int)BrawlerAssemblyContext.ProductionAIInvector));
            Assert.That((int)BrawlerAssemblyContext.ProductionAIInvector,
                Is.GreaterThan((int)BrawlerAssemblyContext.Default));
        }

        static GameObject Require(string path)
        {
            GameObject asset = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            Assert.That(asset, Is.Not.Null,
                "Missing generated Tempest asset " + path + ".");
            return asset;
        }

        static Transform Find(Transform root, string name)
        {
            if (root == null) return null;
            return root.GetComponentsInChildren<Transform>(true)
                .FirstOrDefault(value =>
                    string.Equals(value.name, name, StringComparison.Ordinal));
        }
    }
}
