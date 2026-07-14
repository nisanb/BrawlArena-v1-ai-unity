using System;
using System.Collections.Generic;
using System.Linq;
using Invector.IK;
using Invector.vShooter;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace BrawlArena.EditorAutomation.Tests
{
    public sealed class InvectorThornProductionEditModeTests
    {
        static readonly string[] GeneratedPaths =
        {
            InvectorThornMigrationBuilder.OverrideControllerPath,
            InvectorThornMigrationBuilder.WeaponIKAdjustPath,
            InvectorThornMigrationBuilder.WeaponIKAdjustListPath,
            InvectorThornMigrationBuilder.WeaponPrefabPath,
            InvectorThornMigrationBuilder.PilotPrefabPath,
            InvectorThornMigrationBuilder.ProductionHumanPrefabPath,
            InvectorThornMigrationBuilder.ProductionAIPrefabPath,
        };

        static readonly string[] DefaultIKStates =
        {
            vWeaponIKAdjust.StandingState,
            vWeaponIKAdjust.StandingAimingState,
            vWeaponIKAdjust.CrouchingState,
            vWeaponIKAdjust.CrouchingAimingState,
        };

        [Test]
        [Category("InvectorProductionThorn")]
        public void Bow02SourceAndAllThornVariantsPassPinnedBuilderAudits()
        {
            Assert.That(AssetDatabase.AssetPathToGUID(
                    InvectorThornMigrationBuilder.ThornPath),
                Is.EqualTo(InvectorThornMigrationBuilder.ThornGuid));
            Assert.That(AssetDatabase.AssetPathToGUID(
                    InvectorThornMigrationBuilder.ThornAvatarPath),
                Is.EqualTo(InvectorThornMigrationBuilder.ThornAvatarGuid));
            Assert.That(AssetDatabase.AssetPathToGUID(
                    InvectorThornMigrationBuilder.ThornControllerPath),
                Is.EqualTo(InvectorThornMigrationBuilder.ThornControllerGuid));
            Assert.That(AssetDatabase.AssetPathToGUID(
                    InvectorThornMigrationBuilder.WeaponsMaterialPath),
                Is.EqualTo(InvectorThornMigrationBuilder.WeaponsMaterialGuid));
            Assert.That(AssetDatabase.AssetPathToGUID(
                    InvectorThornMigrationBuilder.BasicAttackClipPath),
                Is.EqualTo(InvectorThornMigrationBuilder.BasicAttackClipGuid));
            Assert.That(AssetDatabase.AssetPathToGUID(
                    InvectorThornMigrationBuilder.SuperAttackClipPath),
                Is.EqualTo(InvectorThornMigrationBuilder.SuperAttackClipGuid));

            GameObject thorn = Require(InvectorThornMigrationBuilder.ThornPath);
            Animator sourceAnimator = thorn.GetComponent<Animator>();
            Transform leftSocket = Find(
                thorn.transform, InvectorThornMigrationBuilder.LeftWeaponSocketName);
            Transform rightSocket = Find(
                thorn.transform, InvectorThornMigrationBuilder.RightWeaponSocketName);
            Transform bow = Find(
                thorn.transform, InvectorThornMigrationBuilder.AuthoredBowName);
            Transform arrow = Find(
                thorn.transform, InvectorThornMigrationBuilder.AuthoredArrowName);
            Assert.That(sourceAnimator, Is.Not.Null);
            Assert.That(sourceAnimator.isHuman, Is.True);
            Assert.That(sourceAnimator.avatar, Is.Not.Null);
            Assert.That(AssetDatabase.GetAssetPath(sourceAnimator.avatar),
                Is.EqualTo(InvectorThornMigrationBuilder.ThornAvatarPath));
            Assert.That(AssetDatabase.GetAssetPath(
                    sourceAnimator.runtimeAnimatorController),
                Is.EqualTo(InvectorThornMigrationBuilder.ThornControllerPath));
            Assert.That(bow.parent, Is.SameAs(leftSocket));
            Assert.That(arrow.parent, Is.SameAs(rightSocket));
            Assert.That(bow.gameObject.activeSelf, Is.True);
            Assert.That(arrow.gameObject.activeSelf, Is.True);
            AssertVector(bow.localPosition,
                new Vector3(-0.24858005f, -0.0071552824f, 0.0025561317f));
            AssertVector(arrow.localPosition,
                new Vector3(0.50246066f, -0.040514484f, 0.023017777f));
            Assert.That(Quaternion.Angle(bow.localRotation, Quaternion.identity),
                Is.LessThan(0.001f));
            Assert.That(Quaternion.Angle(arrow.localRotation, Quaternion.identity),
                Is.LessThan(0.001f));
            Assert.That(AssetDatabase.GetAssetPath(
                    bow.GetComponent<Renderer>().sharedMaterial),
                Is.EqualTo(InvectorThornMigrationBuilder.WeaponsMaterialPath));
            Assert.That(AssetDatabase.GetAssetPath(
                    arrow.GetComponent<Renderer>().sharedMaterial),
                Is.EqualTo(InvectorThornMigrationBuilder.WeaponsMaterialPath));

            Assert.DoesNotThrow(InvectorThornMigrationBuilder.ValidatePrerequisites);
            Assert.DoesNotThrow(() => InvectorThornMigrationBuilder.ValidatePilot(
                Require(InvectorThornMigrationBuilder.PilotPrefabPath)));
            Assert.DoesNotThrow(() => InvectorThornMigrationBuilder.ValidateHumanPrefab(
                Require(InvectorThornMigrationBuilder.ProductionHumanPrefabPath)));
            Assert.DoesNotThrow(() => InvectorThornMigrationBuilder.ValidateAIPrefab(
                Require(InvectorThornMigrationBuilder.ProductionAIPrefabPath)));

            GameObject pilot = Require(InvectorThornMigrationBuilder.PilotPrefabPath);
            AnimatorOverrideController overrides = pilot.GetComponent<Animator>()
                .runtimeAnimatorController as AnimatorOverrideController;
            Assert.That(overrides, Is.Not.Null);
            Assert.That(AssetDatabase.GetAssetPath(overrides),
                Is.EqualTo(InvectorThornMigrationBuilder.OverrideControllerPath));
            Assert.That(AssetDatabase.GetAssetPath(overrides.runtimeAnimatorController),
                Is.EqualTo(InvectorMigrationPilotBuilder.LifecycleControllerPath));
            AssertExactAttackOverrides(overrides);

            InvectorBrawlerWeaponPresentation presenter =
                pilot.GetComponent<InvectorBrawlerWeaponPresentation>();
            Assert.That(presenter, Is.Not.Null);
            Assert.That(presenter.IsDormantConfigured, Is.True);
            Assert.That(presenter.HasRuntimeSolvers, Is.False);
            Assert.That(presenter.WeaponHeldInLeftHand, Is.True);
            Assert.That(presenter.WeaponCategory,
                Is.EqualTo(InvectorThornMigrationBuilder.WeaponCategory));
            Assert.That(AssetDatabase.GetAssetPath(presenter.ProjectIKAdjustList),
                Is.EqualTo(InvectorThornMigrationBuilder.WeaponIKAdjustListPath));
            InvectorBowPresentationRig bowRig = presenter.BowPresentationRig;
            Assert.That(bowRig, Is.Not.Null);
            Assert.That(bowRig.gameObject, Is.SameAs(pilot));
            Assert.That(bowRig.IsDormantConfigured, Is.True);

            vWeaponIKAdjust adjust = AssetDatabase.LoadAssetAtPath<vWeaponIKAdjust>(
                InvectorThornMigrationBuilder.WeaponIKAdjustPath);
            vWeaponIKAdjustList list =
                AssetDatabase.LoadAssetAtPath<vWeaponIKAdjustList>(
                    InvectorThornMigrationBuilder.WeaponIKAdjustListPath);
            Assert.That(adjust, Is.Not.Null);
            Assert.That(list, Is.Not.Null);
            Assert.That(list.weaponIKAdjusts, Is.EqualTo(new[] { adjust }));
            Assert.That(adjust.weaponCategories,
                Is.EqualTo(new[] { InvectorThornMigrationBuilder.WeaponCategory }));
            Assert.That(list.GetWeaponIK(InvectorThornMigrationBuilder.WeaponCategory),
                Is.SameAs(adjust));
            Assert.That(list.GetWeaponIK(InvectorMigrationPilotBuilder.WeaponCategory),
                Is.Null);
            Assert.That(adjust.ikAdjustsRight.Select(value => value.name),
                Is.EqualTo(DefaultIKStates));
            Assert.That(adjust.ikAdjustsLeft.Select(value => value.name),
                Is.EqualTo(DefaultIKStates));
            AssertVector(list.ikTargetPositionOffsetR, Vector3.zero);
            AssertVector(list.ikTargetRotationOffsetR, Vector3.zero);
            AssertVector(list.ikTargetPositionOffsetL, Vector3.zero);
            AssertVector(list.ikTargetRotationOffsetL, Vector3.zero);
            Animator pilotAnimator = pilot.GetComponent<Animator>();
            Vector3 leftInset = ExpectedArmInset(
                pilotAnimator,
                HumanBodyBones.LeftUpperArm,
                HumanBodyBones.LeftHand);
            Vector3 rightInset = ExpectedArmInset(
                pilotAnimator,
                HumanBodyBones.RightUpperArm,
                HumanBodyBones.RightHand);
            foreach (IKAdjust state in adjust.ikAdjustsLeft)
            {
                AssertVector(state.weaponHandOffset.position, leftInset);
                AssertVector(state.weaponHandOffset.eulerAngles, Vector3.zero);
                AssertVector(state.weaponHintOffset.position, Vector3.zero);
                AssertVector(state.weaponHintOffset.eulerAngles, Vector3.zero);
                AssertVector(state.supportHandOffset.position, rightInset);
                AssertVector(state.supportHandOffset.eulerAngles, Vector3.zero);
                AssertVector(state.supportHintOffset.position, Vector3.zero);
                AssertVector(state.supportHintOffset.eulerAngles, Vector3.zero);
                Assert.That(state.spineOffset.spine, Is.EqualTo(Vector2.zero));
                Assert.That(state.spineOffset.head, Is.EqualTo(Vector2.zero));
            }
            foreach (IKAdjust state in adjust.ikAdjustsRight)
            {
                AssertVector(state.weaponHandOffset.position, rightInset);
                AssertVector(state.weaponHandOffset.eulerAngles, Vector3.zero);
                AssertVector(state.weaponHintOffset.position, Vector3.zero);
                AssertVector(state.weaponHintOffset.eulerAngles, Vector3.zero);
                AssertVector(state.supportHandOffset.position, leftInset);
                AssertVector(state.supportHandOffset.eulerAngles, Vector3.zero);
                AssertVector(state.supportHintOffset.position, Vector3.zero);
                AssertVector(state.supportHintOffset.eulerAngles, Vector3.zero);
                Assert.That(state.spineOffset.spine, Is.EqualTo(Vector2.zero));
                Assert.That(state.spineOffset.head, Is.EqualTo(Vector2.zero));
            }

            Transform pilotLeft = Find(
                pilot.transform, InvectorThornMigrationBuilder.LeftWeaponSocketName);
            Transform pilotRight = Find(
                pilot.transform, InvectorThornMigrationBuilder.RightWeaponSocketName);
            Transform presentation = Find(
                pilot.transform, InvectorThornMigrationBuilder.WeaponPresentationName);
            Transform bowVisual = Find(
                presentation, InvectorThornMigrationBuilder.BowVisualName);
            Transform rightHandArrow = Find(
                pilot.transform, InvectorThornMigrationBuilder.AuthoredArrowName);
            Transform nock = Find(
                presentation, InvectorThornMigrationBuilder.NockPointName);
            Transform muzzle = Find(
                presentation, InvectorThornMigrationBuilder.SpellOriginName);
            Transform stringTop = Find(
                presentation, InvectorThornMigrationBuilder.StringTopName);
            Transform stringRest = Find(
                presentation, InvectorThornMigrationBuilder.StringRestName);
            Transform stringBottom = Find(
                presentation, InvectorThornMigrationBuilder.StringBottomName);
            LineRenderer bowString = presentation
                .GetComponentsInChildren<LineRenderer>(true).Single();
            Assert.That(presentation.parent, Is.SameAs(pilotLeft));
            Assert.That(bowVisual, Is.Not.Null);
            Assert.That(Find(pilot.transform,
                InvectorThornMigrationBuilder.AuthoredBowName), Is.Null);
            Assert.That(rightHandArrow.parent, Is.SameAs(pilotRight));
            Assert.That(rightHandArrow.gameObject.activeSelf, Is.True);
            Assert.That(nock, Is.Not.Null);
            Assert.That(muzzle.parent, Is.SameAs(nock));
            Assert.That(Vector3.Distance(
                    nock.position,
                    rightHandArrow.TransformPoint(bowRig.ArrowNockLocalPoint)),
                Is.LessThan(0.0001f));
            Assert.That(Vector3.Distance(
                    muzzle.position,
                    rightHandArrow.TransformPoint(bowRig.ArrowTipLocalPoint)),
                Is.LessThan(0.0001f));
            Assert.That((muzzle.position - nock.position).sqrMagnitude,
                Is.GreaterThan(0.000001f));
            Assert.That(Quaternion.Angle(nock.rotation, rightHandArrow.rotation),
                Is.LessThan(0.001f));
            Assert.That(Quaternion.Angle(muzzle.rotation, rightHandArrow.rotation),
                Is.LessThan(0.001f));
            Assert.That(bowRig.ArrowVisual, Is.SameAs(rightHandArrow));
            Assert.That(bowRig.NockPoint, Is.SameAs(nock));
            Assert.That(bowRig.BowString, Is.SameAs(bowString));
            Assert.That(bowRig.StringTopAnchor, Is.SameAs(stringTop));
            Assert.That(bowRig.StringRestAnchor, Is.SameAs(stringRest));
            Assert.That(bowRig.StringBottomAnchor, Is.SameAs(stringBottom));
            Assert.That(bowString.positionCount, Is.EqualTo(3));
            Assert.That(bowString.useWorldSpace, Is.False);
            Assert.That(bowString.startWidth,
                Is.EqualTo(InvectorThornMigrationBuilder.BowStringWidth).Within(0.0001f));
            Assert.That(bowString.endWidth,
                Is.EqualTo(InvectorThornMigrationBuilder.BowStringWidth).Within(0.0001f));
            AssertLinePoint(bowString, 0, stringTop.position);
            AssertLinePoint(bowString, 1, stringRest.position);
            AssertLinePoint(bowString, 2, stringBottom.position);
            Assert.That(AssetDatabase.GetAssetPath(bowString.sharedMaterial),
                Is.EqualTo(InvectorThornMigrationBuilder.WeaponsMaterialPath));
            AssertBowBoundsAnchors(
                bowVisual, stringTop, stringRest, stringBottom);
            Assert.That(presentation.GetComponentsInChildren<ParticleSystem>(true)
                .Single().main.playOnAwake, Is.False);
        }

        [Test]
        [Category("InvectorProductionThorn")]
        public void SerializedBowRigRestoresAuthoredArrowAfterFreshPrefabInstantiation()
        {
            Scene previewScene = EditorSceneManager.NewPreviewScene();
            try
            {
                GameObject instance = (GameObject)PrefabUtility.InstantiatePrefab(
                    Require(InvectorThornMigrationBuilder.PilotPrefabPath),
                    previewScene);
                InvectorBowPresentationRig rig =
                    instance.GetComponent<InvectorBowPresentationRig>();
                Transform arrow = Find(
                    instance.transform,
                    InvectorThornMigrationBuilder.AuthoredArrowName);
                Assert.That(rig, Is.Not.Null);
                Assert.That(rig.IsDormantConfigured, Is.True,
                    "A freshly deserialized Thorn rig must retain its authored Arrow2 baseline.");
                Assert.That(arrow, Is.Not.Null);

                Vector3 authoredPosition = arrow.localPosition;
                Quaternion authoredRotation = arrow.localRotation;
                bool authoredActive = arrow.gameObject.activeSelf;
                arrow.localPosition += new Vector3(0.25f, -0.15f, 0.4f);
                arrow.localRotation = Quaternion.Euler(25f, -15f, 35f);
                arrow.gameObject.SetActive(!authoredActive);

                Assert.DoesNotThrow(rig.DisableRuntime);
                AssertVector(arrow.localPosition, authoredPosition);
                Assert.That(Quaternion.Angle(
                        arrow.localRotation, authoredRotation),
                    Is.LessThan(0.001f));
                Assert.That(arrow.gameObject.activeSelf, Is.EqualTo(authoredActive));
                Assert.That(rig.IsDormantConfigured, Is.True);
            }
            finally
            {
                EditorSceneManager.ClosePreviewScene(previewScene);
            }
        }

        [Test]
        [Category("InvectorProductionThorn")]
        public void TwoPreviewSceneBuildsPreserveThornAndExistingRosterGuidsAndCallerScene()
        {
            string[] before = GeneratedPaths
                .Select(AssetDatabase.AssetPathToGUID)
                .ToArray();
            string sourceBefore = AssetDatabase.AssetPathToGUID(
                InvectorThornMigrationBuilder.ThornPath);
            string[] priorRosterBefore =
            {
                AssetDatabase.AssetPathToGUID(
                    InvectorMigrationPilotBuilder.ProductionHumanPrefabPath),
                AssetDatabase.AssetPathToGUID(
                    InvectorMigrationPilotBuilder.ProductionAIPrefabPath),
                AssetDatabase.AssetPathToGUID(
                    InvectorRimeMigrationBuilder.ProductionHumanPrefabPath),
                AssetDatabase.AssetPathToGUID(
                    InvectorRimeMigrationBuilder.ProductionAIPrefabPath),
                AssetDatabase.AssetPathToGUID(
                    InvectorTempestMigrationBuilder.ProductionHumanPrefabPath),
                AssetDatabase.AssetPathToGUID(
                    InvectorTempestMigrationBuilder.ProductionAIPrefabPath),
            };
            Assert.That(before.All(value => !string.IsNullOrEmpty(value)), Is.True);
            Assert.That(priorRosterBefore.All(
                value => !string.IsNullOrEmpty(value)), Is.True);

            Scene sceneBefore = SceneManager.GetActiveScene();
            string pathBefore = sceneBefore.path;
            bool dirtyBefore = sceneBefore.isDirty;

            Assert.DoesNotThrow(() =>
                InvectorThornMigrationBuilder.BuildThornPilotAssetsSafely());
            Assert.DoesNotThrow(() =>
                InvectorThornMigrationBuilder.BuildThornPilotAssetsSafely());

            Assert.That(GeneratedPaths.Select(AssetDatabase.AssetPathToGUID),
                Is.EqualTo(before));
            Assert.That(AssetDatabase.AssetPathToGUID(
                    InvectorThornMigrationBuilder.ThornPath),
                Is.EqualTo(sourceBefore));
            string[] priorRosterAfter =
            {
                AssetDatabase.AssetPathToGUID(
                    InvectorMigrationPilotBuilder.ProductionHumanPrefabPath),
                AssetDatabase.AssetPathToGUID(
                    InvectorMigrationPilotBuilder.ProductionAIPrefabPath),
                AssetDatabase.AssetPathToGUID(
                    InvectorRimeMigrationBuilder.ProductionHumanPrefabPath),
                AssetDatabase.AssetPathToGUID(
                    InvectorRimeMigrationBuilder.ProductionAIPrefabPath),
                AssetDatabase.AssetPathToGUID(
                    InvectorTempestMigrationBuilder.ProductionHumanPrefabPath),
                AssetDatabase.AssetPathToGUID(
                    InvectorTempestMigrationBuilder.ProductionAIPrefabPath),
            };
            Assert.That(priorRosterAfter, Is.EqualTo(priorRosterBefore));

            Scene sceneAfter = SceneManager.GetActiveScene();
            Assert.That(sceneAfter.path, Is.EqualTo(pathBefore));
            Assert.That(sceneAfter.isDirty, Is.EqualTo(dirtyBefore));
        }

        [Test]
        [Category("InvectorProductionThorn")]
        public void ExactPrefabIdentityRejectsWrongRosterAndRoleBeforeClone()
        {
            GameObject human = Require(
                InvectorThornMigrationBuilder.ProductionHumanPrefabPath);
            GameObject ai = Require(
                InvectorThornMigrationBuilder.ProductionAIPrefabPath);
            var definition = new BrawlerDefinition
            {
                id = InvectorTempestMigrationBuilder.RosterId,
                displayName = "Wrong Thorn Assignment",
                invectorHumanPrefab = human,
                invectorAIPrefab = ai,
            };
            int before = Object.FindObjectsByType<BrawlerController>(
                FindObjectsInactive.Include, FindObjectsSortMode.None).Length;

            Assert.Throws<InvalidOperationException>(() =>
                BrawlerCharacterAssembly.Assemble(
                    definition, TeamId.Blue, Vector3.zero, true, 1f,
                    BrawlerAssemblyContext.ProductionHumanInvector));
            Assert.Throws<InvalidOperationException>(() =>
                BrawlerCharacterAssembly.Assemble(
                    definition, TeamId.Blue, Vector3.zero, false, 1f,
                    BrawlerAssemblyContext.ProductionAIInvector));

            definition.id = InvectorThornMigrationBuilder.RosterId;
            definition.invectorHumanPrefab = ai;
            Assert.Throws<InvalidOperationException>(() =>
                BrawlerCharacterAssembly.Assemble(
                    definition, TeamId.Blue, Vector3.zero, true, 1f,
                    BrawlerAssemblyContext.ProductionHumanInvector));
            definition.invectorHumanPrefab = human;
            Assert.Throws<NotSupportedException>(() =>
                BrawlerCharacterAssembly.Assemble(
                    definition, TeamId.Blue, Vector3.zero, false, 1f,
                    BrawlerAssemblyContext.ProductionHumanInvector));
            Assert.Throws<NotSupportedException>(() =>
                BrawlerCharacterAssembly.Assemble(
                    definition, TeamId.Blue, Vector3.zero, true, 1f,
                    BrawlerAssemblyContext.ProductionAIInvector));

            int after = Object.FindObjectsByType<BrawlerController>(
                FindObjectsInactive.Include, FindObjectsSortMode.None).Length;
            Assert.That(after, Is.EqualTo(before));
        }

        [Test]
        [Category("InvectorProductionThorn")]
        public void GeneratedRosterKeepsThornBrawlArrowAuthoritative()
        {
            BrawlerDefinition[] roster = ArenaSceneBuilder.BuildRosterFromExistingAssets();
            BrawlerDefinition cinder = roster.Single(value => value.id == "fire");
            BrawlerDefinition rime = roster.Single(value => value.id == "frost");
            BrawlerDefinition tempest = roster.Single(value => value.id == "storm");
            BrawlerDefinition thorn = roster.Single(value =>
                value.id == InvectorThornMigrationBuilder.RosterId);
            Assert.That(cinder.invectorHumanPrefab, Is.Not.Null);
            Assert.That(cinder.invectorAIPrefab, Is.Not.Null);
            Assert.That(rime.invectorHumanPrefab, Is.Not.Null);
            Assert.That(rime.invectorAIPrefab, Is.Not.Null);
            Assert.That(tempest.invectorHumanPrefab, Is.Not.Null);
            Assert.That(tempest.invectorAIPrefab, Is.Not.Null);

            Assert.That(thorn.invectorHumanPrefab,
                Is.SameAs(Require(
                    InvectorThornMigrationBuilder.ProductionHumanPrefabPath)));
            Assert.That(thorn.invectorAIPrefab,
                Is.SameAs(Require(
                    InvectorThornMigrationBuilder.ProductionAIPrefabPath)));
            Assert.That(thorn.projectilePrefab, Is.Not.Null);
            Assert.That(thorn.projectilePrefab.name, Is.EqualTo("Arrow01"));
            Assert.That(thorn.damage, Is.EqualTo(23f));
            Assert.That(thorn.hitDelay,
                Is.EqualTo(0.48f).Within(0.0001f));
            Assert.That(thorn.superStyle,
                Is.EqualTo(BrawlerSuperStyle.ProjectileBlast));
            Assert.That(thorn.superName, Is.EqualTo("EXPLOSIVE ARROW"));
            Assert.That(thorn.superDamageMultiplier, Is.EqualTo(1.85f));
            Assert.That(thorn.superProjectilePrefab, Is.Not.Null);
            Assert.That(thorn.superProjectilePrefab.name, Is.EqualTo("Arrow02"));
        }

        [Test]
        [Category("InvectorProductionThorn")]
        public void DefaultAndExplicitContextsRetainExactThornRolePrefabs()
        {
            GameObject human = Require(
                InvectorThornMigrationBuilder.ProductionHumanPrefabPath);
            GameObject ai = Require(
                InvectorThornMigrationBuilder.ProductionAIPrefabPath);
            Assert.That(human.GetComponent<InvectorBrawlerPrefabIdentity>()
                    .Matches(
                        InvectorThornMigrationBuilder.RosterId,
                        InvectorBrawlerPrefabRole.Human),
                Is.True);
            Assert.That(ai.GetComponent<InvectorBrawlerPrefabIdentity>()
                    .Matches(
                        InvectorThornMigrationBuilder.RosterId,
                        InvectorBrawlerPrefabRole.AI),
                Is.True);
            Assert.That(BrawlerAssemblyContext.Default,
                Is.Not.EqualTo(BrawlerAssemblyContext.ProductionHumanInvector));
            Assert.That(BrawlerAssemblyContext.ProductionHumanInvector,
                Is.Not.EqualTo(BrawlerAssemblyContext.ProductionAIInvector));

            BrawlerDefinition thorn = ArenaSceneBuilder.BuildRosterFromExistingAssets()
                .Single(value => value.id == InvectorThornMigrationBuilder.RosterId);
            Assert.That(thorn.invectorHumanPrefab, Is.SameAs(human));
            Assert.That(thorn.invectorAIPrefab, Is.SameAs(ai));
        }

        static GameObject Require(string path)
        {
            GameObject asset = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            Assert.That(asset, Is.Not.Null,
                "Missing generated Thorn asset " + path + ".");
            return asset;
        }

        static Transform Find(Transform root, string name)
        {
            if (root == null) return null;
            return root.GetComponentsInChildren<Transform>(true)
                .FirstOrDefault(value => string.Equals(
                    value.name, name, StringComparison.Ordinal));
        }

        static void AssertExactAttackOverrides(
            AnimatorOverrideController controller)
        {
            var overrides = new List<KeyValuePair<AnimationClip, AnimationClip>>(
                controller.overridesCount);
            controller.GetOverrides(overrides);
            KeyValuePair<AnimationClip, AnimationClip>[] active = overrides
                .Where(value => value.Key != null && value.Value != null &&
                                value.Key != value.Value)
                .ToArray();
            Assert.That(active, Has.Length.EqualTo(2));
            KeyValuePair<AnimationClip, AnimationClip> basic = active.Single(value =>
                value.Key.name ==
                InvectorThornMigrationBuilder.BasicAttackOverrideSourceName);
            KeyValuePair<AnimationClip, AnimationClip> super = active.Single(value =>
                value.Key.name ==
                InvectorThornMigrationBuilder.SuperAttackOverrideSourceName);
            Assert.That(basic.Value.name,
                Is.EqualTo(InvectorThornMigrationBuilder.BasicAttackClipName));
            Assert.That(super.Value.name,
                Is.EqualTo(InvectorThornMigrationBuilder.SuperAttackClipName));
            Assert.That(AssetDatabase.GetAssetPath(basic.Value),
                Is.EqualTo(InvectorThornMigrationBuilder.BasicAttackClipPath));
            Assert.That(AssetDatabase.GetAssetPath(super.Value),
                Is.EqualTo(InvectorThornMigrationBuilder.SuperAttackClipPath));
        }

        static Vector3 ExpectedArmInset(
            Animator animator,
            HumanBodyBones upperArmBone,
            HumanBodyBones handBone)
        {
            Transform upperArm = animator.GetBoneTransform(upperArmBone);
            Transform hand = animator.GetBoneTransform(handBone);
            Assert.That(upperArm, Is.Not.Null);
            Assert.That(hand, Is.Not.Null);
            Vector3 towardShoulder = upperArm.position - hand.position;
            Assert.That(towardShoulder.sqrMagnitude, Is.GreaterThan(0.000001f));
            Vector3 inset = Quaternion.Inverse(hand.rotation) *
                (towardShoulder.normalized *
                 InvectorThornMigrationBuilder.IKReachInset);
            Assert.That(inset.magnitude,
                Is.EqualTo(InvectorThornMigrationBuilder.IKReachInset)
                    .Within(0.0001f));
            return inset;
        }

        static void AssertLinePoint(
            LineRenderer line,
            int index,
            Vector3 expectedWorldPosition)
        {
            Assert.That(Vector3.Distance(
                    line.transform.TransformPoint(line.GetPosition(index)),
                    expectedWorldPosition),
                Is.LessThan(0.0001f));
        }

        static void AssertBowBoundsAnchors(
            Transform bowVisual,
            Transform top,
            Transform rest,
            Transform bottom)
        {
            MeshFilter filter = bowVisual.GetComponent<MeshFilter>();
            Assert.That(filter, Is.Not.Null);
            Assert.That(filter.sharedMesh, Is.Not.Null);
            Bounds bounds = filter.sharedMesh.bounds;
            int axis = bounds.size.x >= bounds.size.y &&
                       bounds.size.x >= bounds.size.z
                ? 0
                : bounds.size.y >= bounds.size.z ? 1 : 2;
            Vector3 first = bounds.center;
            Vector3 second = bounds.center;
            float center = axis == 0
                ? bounds.center.x
                : axis == 1 ? bounds.center.y : bounds.center.z;
            float extent = axis == 0
                ? bounds.extents.x
                : axis == 1 ? bounds.extents.y : bounds.extents.z;
            SetAxis(ref first, axis, center - extent);
            SetAxis(ref second, axis, center + extent);
            first = bowVisual.TransformPoint(first);
            second = bowVisual.TransformPoint(second);
            Vector3 expectedTop = first.y >= second.y ? first : second;
            Vector3 expectedBottom = first.y >= second.y ? second : first;
            Assert.That(Vector3.Distance(top.position, expectedTop),
                Is.LessThan(0.0001f));
            Assert.That(Vector3.Distance(bottom.position, expectedBottom),
                Is.LessThan(0.0001f));
            Assert.That(Vector3.Distance(
                    rest.position,
                    Vector3.Lerp(expectedTop, expectedBottom, 0.5f)),
                Is.LessThan(0.0001f));
        }

        static void SetAxis(ref Vector3 value, int axis, float component)
        {
            if (axis == 0) value.x = component;
            else if (axis == 1) value.y = component;
            else value.z = component;
        }

        static void AssertVector(Vector3 actual, Vector3 expected)
        {
            Assert.That(actual.x, Is.EqualTo(expected.x).Within(0.0001f));
            Assert.That(actual.y, Is.EqualTo(expected.y).Within(0.0001f));
            Assert.That(actual.z, Is.EqualTo(expected.z).Within(0.0001f));
        }
    }
}
