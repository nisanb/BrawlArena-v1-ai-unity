using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Invector.IK;
using Invector.vCharacterController;
using Invector.vItemManager;
using Invector.vMelee;
using Invector.vShooter;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

namespace BrawlArena.EditorAutomation
{
    /// <summary>
    /// Builds a dormant Thorn stack directly from Modular RPG Bow02. The bow
    /// presentation is visual/IK-only: Brawl retains arrow pooling, launch,
    /// targeting, damage, Super, health, ammo, and match authority.
    /// </summary>
    public static class InvectorThornMigrationBuilder
    {
        public const string Root = "Assets/Generated/InvectorMigration/Thorn/";
        public const string OverrideControllerPath =
            Root + "Controllers/ThornInvectorPilot.overrideController";
        public const string PilotPrefabPath =
            Root + "Prefabs/ThornInvectorPilot.prefab";
        public const string ProductionHumanPrefabPath =
            Root + "Prefabs/ThornInvectorHuman.prefab";
        public const string ProductionAIPrefabPath =
            "Assets/BrawlArena/Prefabs/Invector/ThornInvectorAI.prefab";
        public const string WeaponPrefabPath =
            Root + "Weapons/ThornBowPresentation.prefab";
        public const string WeaponIKAdjustPath =
            Root + "IK/ThornBowIKAdjust.asset";
        public const string WeaponIKAdjustListPath =
            Root + "IK/ThornBowIKAdjustList.asset";

        public const string ThornPath =
            "Assets/ModularRPGHeroesPBR/Prefabs/BasicCharacters/Bow02.prefab";
        public const string ThornGuid = "5c2dcf339ba1a9c4ca7a6feb3d8f8760";
        public const string ThornAvatarPath =
            "Assets/ModularRPGHeroesPBR/Mesh/DefaultCharacter.fbx";
        public const string ThornAvatarGuid = "47388b002c80e8b49827d81977725b78";
        public const string ThornControllerPath =
            "Assets/ModularRPGHeroesPBR/Animators/Bow.controller";
        public const string ThornControllerGuid = "2784b5a781ff58f48bb37a4e5d40f565";
        public const string WeaponsMaterialPath =
            "Assets/ModularRPGHeroesPBR/Material/RegularPBR/Weapons.mat";
        public const string WeaponsMaterialGuid = "532c7a2f80133ed41b42e2b7a36ecc44";
        // Mixamo bow attacks (action-MMO experiment); the carry pose is
        // deliberately untouched — the bow support solver owns the nock
        // contact and was tuned against the vendor pistol layer.
        public const string BasicAttackClipPath =
            "Assets/ThirdParty/Mixamo/Thorn/Mixamo_Thorn_Shoot.fbx";
        public const string BasicAttackClipGuid =
            "8218d8bc0588e5942815630a083baef6";
        public const string SuperAttackClipPath =
            "Assets/ThirdParty/Mixamo/Thorn/Mixamo_Thorn_PowerShot.fbx";
        public const string SuperAttackClipGuid =
            "9f9b0eb35c671c74eadd7c332c507675";

        public const string BasicAttackOverrideSourceName = "WeakAttack_UnarmedA";
        public const string SuperAttackOverrideSourceName = "StrongAttack_PunchA";
        public const string BasicAttackClipName = "Mixamo_Thorn_Shoot";
        public const string SuperAttackClipName = "Mixamo_Thorn_PowerShot";
        // The vendor pistol layer is static across locomotion. Thorn's bow
        // support solver owns the right-hand nock contact outside attacks.

        public const string RosterId = "thorn";
        public const string WeaponCategory = "BrawlWizardBow";
        public const string AuthoredBowName = "Bow2";
        public const string AuthoredArrowName = "Arrow2";
        public const string LeftWeaponSocketName = "weaponShield_l";
        public const string RightWeaponSocketName = "weaponShield_r";
        public const string WeaponPresentationName = "ThornBowPresentation";
        public const string BowVisualName = "BowVisual";
        public const string NockPointName = "NockPoint";
        public const string SpellOriginName = "SpellOrigin";
        public const string BowStringName = "BowString";
        public const string StringTopName = "StringTop";
        public const string StringRestName = "StringRest";
        public const string StringBottomName = "StringBottom";
        public const float IKReachInset = 0.01f;
        public const float BowStringWidth = 0.012f;
        public static readonly Vector3 CarryPresentationLocalPosition =
            Vector3.zero;
        public static readonly Vector3 RelaxedSupportHandOffset =
            new Vector3(-0.036176f, -0.074161f, -0.176044f);
        public static readonly Vector3 SupportHintNockLocalPosition =
            new Vector3(-0.157592f, 0.123637f, 0.005433f);
        public static readonly Vector3 PreviewWeaponHandLocalPosition =
            new Vector3(-0.35f, 0.55f, 0.15f);
        public static readonly Vector3 PreviewWeaponHintLocalPosition =
            new Vector3(-0.55f, 0.78f, 0.15f);
        public static readonly Vector3 PreviewWeaponHandLocalEuler =
            new Vector3(51.31f, 70.32f, 262.74f);
        public static readonly Vector3 PreviewSupportHandLocalPosition =
            new Vector3(0.40f, 0.85f, 0.18f);
        public static readonly Vector3 PreviewSupportHintLocalPosition =
            new Vector3(0.55f, 1.02f, 0.15f);
        public static readonly Vector3 PreviewSupportHandLocalEuler =
            new Vector3(309.10f, 250.40f, 277.50f);

        static readonly Color ThornMuzzleColor =
            new Color(0x9B / 255f, 0xE3 / 255f, 0x6B / 255f, 1f);

        [MenuItem("Brawl Arena/Invector Migration/Build Thorn Bow Variants")]
        static void BuildFromMenu()
        {
            Debug.Log(BuildThornPilotAssetsSafely());
        }

        public static string BuildThornPilotAssetsSafely()
        {
            Scene originalScene = SceneManager.GetActiveScene();
            bool originalSceneWasDirty =
                originalScene.IsValid() && originalScene.isDirty;
            try
            {
                ValidatePrerequisites();
                EnsureFolder(Root + "Controllers");
                EnsureFolder(Root + "Prefabs");
                EnsureFolder(Root + "Weapons");
                EnsureFolder(Root + "IK");
                EnsureFolder("Assets/BrawlArena/Prefabs/Invector");

                AnimatorController lifecycleController =
                    InvectorMigrationPilotBuilder.BuildLifecycleController();
                AnimatorOverrideController overrideController =
                    InvectorMigrationPilotBuilder.BuildOverrideController(
                        lifecycleController,
                        OverrideControllerPath,
                        "ThornInvectorPilot");
                ConfigureAttackOverrides(overrideController);

                // The shared pilot helper currently configures its presenter once
                // with the historical staff category. Keep a temporary alias only
                // while that topology is assembled, immediately replace the root
                // presenter with the bow category/left-hand contract, then remove
                // the alias before validating or creating production variants.
                BuildBowIKAssets(true);
                BuildBowPresentationPrefab();
                GameObject pilot = InvectorMigrationPilotBuilder.BuildPilotPrefab(
                    overrideController,
                    ThornPath,
                    "ThornInvectorPilot_DISABLED",
                    AuthoredBowName,
                    WeaponPrefabPath,
                    WeaponIKAdjustListPath,
                    WeaponPresentationName,
                    PilotPrefabPath,
                    true);
                pilot = NormalizeBowPresenter(pilot);
                FinalizeBowCategory();
                CloseTransientPresentation(pilot);
                ValidatePilot(pilot);

                GameObject human =
                    InvectorMigrationPilotBuilder.BuildProductionHumanPrefab(
                        pilot,
                        "ThornInvectorHuman_DISABLED",
                        ProductionHumanPrefabPath,
                        RosterId);
                CloseTransientPresentation(human);
                ValidateHumanPrefab(human);

                GameObject ai = InvectorMigrationPilotBuilder.BuildProductionAIPrefab(
                    pilot,
                    "ThornInvectorAI_DISABLED",
                    ProductionAIPrefabPath,
                    RosterId);
                CloseTransientPresentation(ai);
                ValidateAIPrefab(ai);

                AssetDatabase.SaveAssets();
                CloseTransientPresentation(pilot);
                CloseTransientPresentation(human);
                CloseTransientPresentation(ai);
                ValidatePilot(pilot);
                ValidateHumanPrefab(human);
                ValidateAIPrefab(ai);
                return "Built the dormant Bow02-derived Thorn pilot plus inactive production-human and AI variants.";
            }
            finally
            {
                // A failed build must not leave the shared staff category on the
                // project-owned bow asset. An incomplete pilot will then remain
                // fail-closed until the next successful rebuild.
                FinalizeBowCategory();
                AssetDatabase.SaveAssets();
                if (originalSceneWasDirty && originalScene.IsValid() &&
                    originalScene.isLoaded)
                {
                    EditorSceneManager.MarkSceneDirty(originalScene);
                }
            }
        }

        public static void ValidatePrerequisites()
        {
            InvectorMigrationPilotBuilder.ValidatePrerequisites();
            RequireGuid(ThornPath, ThornGuid, "Bow02 character source");
            RequireGuid(ThornAvatarPath, ThornAvatarGuid, "Bow02 Humanoid Avatar");
            RequireGuid(ThornControllerPath, ThornControllerGuid, "Modular RPG Bow controller");
            RequireGuid(WeaponsMaterialPath, WeaponsMaterialGuid, "Modular RPG weapons material");
            RequireGuid(BasicAttackClipPath, BasicAttackClipGuid, "Mixamo_Thorn_Shoot clip");
            RequireGuid(SuperAttackClipPath, SuperAttackClipGuid, "Mixamo_Thorn_PowerShot clip");

            GameObject thorn = AssetDatabase.LoadAssetAtPath<GameObject>(ThornPath);
            Animator animator = thorn != null ? thorn.GetComponent<Animator>() : null;
            Transform leftSocket = thorn != null
                ? FindDescendant(thorn.transform, LeftWeaponSocketName)
                : null;
            Transform rightSocket = thorn != null
                ? FindDescendant(thorn.transform, RightWeaponSocketName)
                : null;
            Transform bow = thorn != null
                ? FindDescendant(thorn.transform, AuthoredBowName)
                : null;
            Transform arrow = thorn != null
                ? FindDescendant(thorn.transform, AuthoredArrowName)
                : null;
            Renderer bowRenderer = bow != null ? bow.GetComponent<Renderer>() : null;
            Renderer arrowRenderer = arrow != null ? arrow.GetComponent<Renderer>() : null;
            Material expectedMaterial = AssetDatabase.LoadAssetAtPath<Material>(
                WeaponsMaterialPath);
            bool arrowBoundsValid = arrow != null && bow != null &&
                TryDeriveArrowEndpoints(
                    arrow,
                    bow,
                    out _,
                    out _,
                    out _,
                    out _);
            bool bowBoundsValid = bow != null && TryDeriveBowStringAnchors(
                bow, out _, out _, out _);

            if (thorn == null || animator == null || !animator.isHuman ||
                animator.avatar == null || !animator.avatar.isValid ||
                !string.Equals(
                    AssetDatabase.GetAssetPath(animator.avatar),
                    ThornAvatarPath,
                    StringComparison.Ordinal) ||
                !string.Equals(
                    AssetDatabase.GetAssetPath(animator.runtimeAnimatorController),
                    ThornControllerPath,
                    StringComparison.Ordinal) ||
                leftSocket == null || rightSocket == null || bow == null ||
                arrow == null || bow.parent != leftSocket || arrow.parent != rightSocket ||
                !bow.gameObject.activeSelf || !arrow.gameObject.activeSelf ||
                !arrowBoundsValid || !bowBoundsValid ||
                bowRenderer == null || arrowRenderer == null ||
                bowRenderer.sharedMaterials.Length != 1 ||
                arrowRenderer.sharedMaterials.Length != 1 ||
                bowRenderer.sharedMaterial != expectedMaterial ||
                arrowRenderer.sharedMaterial != expectedMaterial)
            {
                throw new InvalidOperationException(
                    "Thorn must remain the pinned Humanoid Bow02 source with Bow2 under weaponShield_l and Arrow2 under weaponShield_r.");
            }
        }

        public static void ValidatePilot(GameObject prefab)
        {
            RequireAsset(prefab, PilotPrefabPath, "Thorn pilot");
            if (prefab.activeSelf ||
                prefab.layer != InvectorMigrationPilotBuilder.InvectorPlayerLayer ||
                prefab.tag != "Player")
            {
                throw new InvalidOperationException(
                    "The Thorn pilot root is not in its inactive selective-layer posture.");
            }

            GameObject source = PrefabUtility.GetCorrespondingObjectFromSource(prefab);
            if (source == null || !string.Equals(
                    AssetDatabase.GetAssetPath(source),
                    ThornPath,
                    StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    "The Thorn pilot must derive directly from the pinned Bow02 character source.");
            }

            Animator animator = RequireExactlyOneRoot<Animator>(prefab);
            var overrides =
                animator.runtimeAnimatorController as AnimatorOverrideController;
            if (animator.enabled || animator.applyRootMotion || !animator.isHuman ||
                overrides == null || !string.Equals(
                    AssetDatabase.GetAssetPath(overrides),
                    OverrideControllerPath,
                    StringComparison.Ordinal) ||
                !string.Equals(
                    AssetDatabase.GetAssetPath(overrides.runtimeAnimatorController),
                    InvectorMigrationPilotBuilder.LifecycleControllerPath,
                    StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    "The Thorn pilot Animator is not using the Thorn override over the shared lifecycle controller.");
            }
            ValidateAttackOverrides(overrides);

            RequireExactlyOneRoot<Rigidbody>(prefab);
            RequireExactlyOneRoot<CapsuleCollider>(prefab);
            RequireExactlyOneRoot<BrawlInvectorThirdPersonController>(prefab);
            RequireExactlyOneRoot<InvectorShooterMeleeInputAdapter>(prefab);
            RequireExactlyOneRoot<InvectorBrawlerMotor>(prefab);
            RequireExactlyOneRoot<InvectorBrawlerAnimationDriver>(prefab);
            InvectorBrawlerWeaponPresentation presenter =
                RequireExactlyOneRoot<InvectorBrawlerWeaponPresentation>(prefab);
            RequireExactlyOneRoot<vShooterManager>(prefab);
            RequireExactlyOneRoot<BrawlInvectorMeleePresentationManager>(prefab);
            RequireExactlyOneRoot<vAmmoManager>(prefab);
            RequireExactlyOneRoot<vCollectShooterMeleeControl>(prefab);

            if (!presenter.IsDormantConfigured || presenter.HasRuntimeSolvers ||
                !presenter.WeaponHeldInLeftHand ||
                !string.Equals(
                    presenter.WeaponCategory,
                    WeaponCategory,
                    StringComparison.Ordinal) ||
                !string.Equals(
                    AssetDatabase.GetAssetPath(presenter.ProjectIKAdjustList),
                    WeaponIKAdjustListPath,
                    StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    "The Thorn bow presenter is not dormant, left-hand-held, and pinned to its separate bow category/list.");
            }

            ValidateBowIKData(presenter.ProjectIKAdjustList);
            ValidateThornVisuals(prefab);
            ValidateDormantVendorFirewall(prefab);
            ValidateSelectiveLayers(prefab);
            ValidateAuthorityOwners(prefab, false);
            if (prefab.GetComponentsInChildren<Health>(true).Length != 0 ||
                prefab.GetComponentsInChildren<BrawlerController>(true).Length != 0 ||
                prefab.GetComponentsInChildren<PlayerBrawlerInput>(true).Length != 0 ||
                prefab.GetComponentsInChildren<AIBrawler>(true).Length != 0 ||
                prefab.GetComponentsInChildren<NavMeshAgent>(true).Length != 0 ||
                prefab.GetComponentsInChildren<CharacterController>(true).Length != 0)
            {
                throw new InvalidOperationException(
                    "The dormant Thorn pilot contains a production facade, input, AI, or duplicate movement authority.");
            }
        }

        public static void ValidateHumanPrefab(GameObject prefab)
        {
            ValidateProductionVariantSource(prefab, ProductionHumanPrefabPath);
            RequireIdentity(prefab, InvectorBrawlerPrefabRole.Human);
            RequireExactlyOneRoot<Health>(prefab);
            RequireExactlyOneRoot<BrawlerController>(prefab);
            RequireExactlyOneRoot<PlayerBrawlerInput>(prefab);
            RequireExactlyOneRoot<InvectorHumanRuntimeGate>(prefab);
            RequireExactlyOneRoot<InvectorBrawlerMotor>(prefab);
            RequireExactlyOneRoot<InvectorBrawlerAnimationDriver>(prefab);
            RequireExactlyOneRoot<InvectorBrawlerWeaponPresentation>(prefab);
            ValidateThornVisuals(prefab);
            ValidateDormantVendorFirewall(prefab);
            ValidateSelectiveLayers(prefab);

            ValidateAuthorityOwners(prefab, false);
            if (prefab.GetComponentsInChildren<AIBrawler>(true).Length != 0 ||
                prefab.GetComponentsInChildren<InvectorAIRuntimeGate>(true).Length != 0 ||
                prefab.GetComponentsInChildren<NavMeshAgent>(true).Length != 0 ||
                prefab.GetComponentsInChildren<CharacterController>(true).Length != 0)
            {
                throw new InvalidOperationException(
                    "The Thorn human variant contains an AI or duplicate movement authority.");
            }
        }

        public static void ValidateAIPrefab(GameObject prefab)
        {
            ValidateProductionVariantSource(prefab, ProductionAIPrefabPath);
            RequireIdentity(prefab, InvectorBrawlerPrefabRole.AI);
            RequireExactlyOneRoot<Health>(prefab);
            RequireExactlyOneRoot<BrawlerController>(prefab);
            RequireExactlyOneRoot<AIBrawler>(prefab);
            RequireExactlyOneRoot<InvectorBrawlerNavigation>(prefab);
            RequireExactlyOneRoot<InvectorAIRuntimeGate>(prefab);
            RequireExactlyOneRoot<InvectorBrawlerMotor>(prefab);
            RequireExactlyOneRoot<InvectorBrawlerAnimationDriver>(prefab);
            RequireExactlyOneRoot<InvectorBrawlerWeaponPresentation>(prefab);
            ValidateThornVisuals(prefab);
            ValidateDormantVendorFirewall(prefab);
            ValidateSelectiveLayers(prefab);
            ValidateAuthorityOwners(prefab, true);

            NavMeshAgent[] agents = prefab.GetComponentsInChildren<NavMeshAgent>(true);
            if (agents.Length != 1 || agents[0].transform.parent != prefab.transform ||
                agents[0].enabled || agents[0].autoTraverseOffMeshLink ||
                !string.Equals(
                    agents[0].name,
                    InvectorMigrationPilotBuilder.ProductionAIPlannerName,
                    StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    "The Thorn AI variant does not contain one dormant child-only planner.");
            }

            if (prefab.GetComponentsInChildren<PlayerBrawlerInput>(true).Length != 0 ||
                prefab.GetComponentsInChildren<InvectorHumanRuntimeGate>(true).Length != 0 ||
                prefab.GetComponentsInChildren<CharacterController>(true).Length != 0 ||
                prefab.GetComponentsInChildren<
                    Invector.vCharacterController.AI.vSimpleMeleeAI_Motor>(true).Length != 0)
            {
                throw new InvalidOperationException(
                    "The Thorn AI variant contains a human, CharacterController, or vendor AI authority.");
            }
        }

        internal static void ConfigureAttackOverrides(AnimatorOverrideController controller)
        {
            InvectorMigrationPilotBuilder.ConfigurePresentationOverrides(
                controller,
                new[]
                {
                    BasicAttackOverrideSourceName,
                    SuperAttackOverrideSourceName,
                },
                new[]
                {
                    BasicAttackClipPath,
                    SuperAttackClipPath,
                },
                new[]
                {
                    BasicAttackClipName,
                    SuperAttackClipName,
                });
            InvectorMigrationPilotBuilder.ConfigureLocomotionOverrides(controller);
            ValidateAttackOverrides(controller);
        }

        static void ValidateAttackOverrides(AnimatorOverrideController controller)
        {
            if (controller == null)
                throw new InvalidOperationException("The Thorn override controller is missing.");

            var overrides = new List<KeyValuePair<AnimationClip, AnimationClip>>(
                controller.overridesCount);
            controller.GetOverrides(overrides);
            KeyValuePair<AnimationClip, AnimationClip>[] active = overrides
                .Where(value => value.Key != null && value.Value != null &&
                                value.Value != value.Key &&
                                !InvectorMigrationPilotBuilder.IsLocomotionOverrideSource(
                                    value.Key.name))
                .ToArray();
            KeyValuePair<AnimationClip, AnimationClip>[] basic = active
                .Where(value => string.Equals(
                    value.Key.name,
                    BasicAttackOverrideSourceName,
                    StringComparison.Ordinal))
                .ToArray();
            KeyValuePair<AnimationClip, AnimationClip>[] super = active
                .Where(value => string.Equals(
                    value.Key.name,
                    SuperAttackOverrideSourceName,
                    StringComparison.Ordinal))
                .ToArray();
            if (active.Length != 2 || basic.Length != 1 || super.Length != 1 ||
                !string.Equals(
                    AssetDatabase.GetAssetPath(basic[0].Value),
                    BasicAttackClipPath,
                    StringComparison.Ordinal) ||
                !string.Equals(
                    AssetDatabase.GetAssetPath(super[0].Value),
                    SuperAttackClipPath,
                    StringComparison.Ordinal) ||
                !string.Equals(
                    basic[0].Value.name,
                    BasicAttackClipName,
                    StringComparison.Ordinal) ||
                !string.Equals(
                    super[0].Value.name,
                    SuperAttackClipName,
                    StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    "The Thorn AOC must contain only WeakAttack_UnarmedA -> " + BasicAttackClipName +
                    " and StrongAttack_PunchA -> " + SuperAttackClipName + ".");
            }
        }

        static void BuildBowIKAssets(bool includeStaffCompatibilityAlias)
        {
            ComputeArmInsets(
                out Vector3 leftHandInset,
                out Vector3 rightHandInset);
            vWeaponIKAdjust adjust =
                AssetDatabase.LoadAssetAtPath<vWeaponIKAdjust>(WeaponIKAdjustPath);
            if (adjust == null)
            {
                adjust = ScriptableObject.CreateInstance<vWeaponIKAdjust>();
                adjust.name = "ThornBowIKAdjust";
                AssetDatabase.CreateAsset(adjust, WeaponIKAdjustPath);
            }

            adjust.weaponCategories.Clear();
            adjust.weaponCategories.Add(WeaponCategory);
            if (includeStaffCompatibilityAlias)
            {
                adjust.weaponCategories.Add(
                    InvectorMigrationPilotBuilder.WeaponCategory);
            }
            ReplaceDefaultIKStates(
                adjust.ikAdjustsLeft,
                leftHandInset,
                Vector3.zero,
                true);
            ReplaceDefaultIKStates(
                adjust.ikAdjustsRight,
                rightHandInset,
                leftHandInset);
            EditorUtility.SetDirty(adjust);

            vWeaponIKAdjustList list =
                AssetDatabase.LoadAssetAtPath<vWeaponIKAdjustList>(
                    WeaponIKAdjustListPath);
            if (list == null)
            {
                list = ScriptableObject.CreateInstance<vWeaponIKAdjustList>();
                list.name = "ThornBowIKAdjustList";
                AssetDatabase.CreateAsset(list, WeaponIKAdjustListPath);
            }

            list.ikTargetPositionOffsetR = Vector3.zero;
            list.ikTargetRotationOffsetR = Vector3.zero;
            list.ikTargetPositionOffsetL = Vector3.zero;
            list.ikTargetRotationOffsetL = Vector3.zero;
            list.weaponIKAdjusts.Clear();
            list.weaponIKAdjusts.Add(adjust);
            EditorUtility.SetDirty(list);
        }

        static void FinalizeBowCategory()
        {
            vWeaponIKAdjust adjust =
                AssetDatabase.LoadAssetAtPath<vWeaponIKAdjust>(WeaponIKAdjustPath);
            if (adjust == null) return;
            adjust.weaponCategories.Clear();
            adjust.weaponCategories.Add(WeaponCategory);
            EditorUtility.SetDirty(adjust);
        }

        static void ReplaceDefaultIKStates(
            List<IKAdjust> states,
            Vector3 weaponHandInset,
            Vector3 supportHandInset,
            bool relaxedCarry = false)
        {
            states.Clear();
            for (int i = 0; i < vWeaponIKAdjust.defaultNames.Length; i++)
            {
                var state = new IKAdjust(vWeaponIKAdjust.defaultNames[i]);
                state.weaponHandOffset.position = weaponHandInset;
                bool aiming = string.Equals(
                        state.name,
                        vWeaponIKAdjust.StandingAimingState,
                        StringComparison.Ordinal) ||
                    string.Equals(
                        state.name,
                        vWeaponIKAdjust.CrouchingAimingState,
                        StringComparison.Ordinal);
                state.supportHandOffset.position = relaxedCarry && !aiming
                    ? RelaxedSupportHandOffset
                    : supportHandInset;
                states.Add(state);
            }
        }

        static void BuildBowPresentationPrefab()
        {
            GameObject character = AssetDatabase.LoadAssetAtPath<GameObject>(ThornPath);
            Scene previewScene = EditorSceneManager.NewPreviewScene();
            try
            {
                var instance = (GameObject)PrefabUtility.InstantiatePrefab(
                    character, previewScene);
                Animator animator = instance.GetComponent<Animator>();
                Transform leftSocket = FindDescendant(
                    instance.transform, LeftWeaponSocketName);
                Transform rightSocket = FindDescendant(
                    instance.transform, RightWeaponSocketName);
                Transform bow = FindDescendant(instance.transform, AuthoredBowName);
                Transform arrow = FindDescendant(instance.transform, AuthoredArrowName);
                Transform leftHand = animator != null && animator.isHuman
                    ? animator.GetBoneTransform(HumanBodyBones.LeftHand)
                    : null;
                Transform rightHand = animator != null && animator.isHuman
                    ? animator.GetBoneTransform(HumanBodyBones.RightHand)
                    : null;
                Transform rightLowerArm = animator != null && animator.isHuman
                    ? animator.GetBoneTransform(HumanBodyBones.RightLowerArm)
                    : null;
                if (leftSocket == null || rightSocket == null || bow == null ||
                    arrow == null || bow.parent != leftSocket ||
                    arrow.parent != rightSocket || leftHand == null || rightHand == null ||
                    rightLowerArm == null)
                {
                    throw new InvalidOperationException(
                        "Bow02 must expose Bow2 on weaponShield_l, Arrow2 on weaponShield_r, and a valid Humanoid right arm.");
                }

                var weaponRoot = new GameObject(WeaponPresentationName);
                SceneManager.MoveGameObjectToScene(weaponRoot, previewScene);
                weaponRoot.transform.SetParent(leftHand, false);
                weaponRoot.layer = 0;

                GameObject bowVisual = UnityEngine.Object.Instantiate(
                    bow.gameObject, weaponRoot.transform, true);
                bowVisual.name = BowVisualName;
                SetLayerRecursively(bowVisual, 0);
                bowVisual.SetActive(true);

                // Bow02 has no authored SpellOrigin or independently deformable
                // string. The bow owns the contact point: use the midpoint of
                // its string as the nock, then preserve Arrow2's authored local
                // nock-to-tip vector and right-hand grip relation around it.
                DeriveArrowEndpoints(
                    arrow,
                    bowVisual.transform,
                    out _,
                    out _,
                    out Vector3 sourceArrowNock,
                    out Vector3 sourceArrowTip);
                DeriveBowStringAnchors(
                    bowVisual.transform,
                    out Vector3 stringTopPosition,
                    out Vector3 stringRestPosition,
                    out Vector3 stringBottomPosition);
                Transform nock = CreateAnchor(
                    weaponRoot.transform,
                    NockPointName,
                    stringRestPosition,
                    arrow.rotation);
                Transform muzzle = CreateAnchor(
                    nock,
                    SpellOriginName,
                    stringRestPosition + (sourceArrowTip - sourceArrowNock),
                    arrow.rotation);
                nock.gameObject.layer = 0;
                muzzle.gameObject.layer = 12;

                Transform stringTop = CreateAnchor(
                    weaponRoot.transform,
                    StringTopName,
                    stringTopPosition,
                    weaponRoot.transform.rotation);
                Transform stringRest = CreateAnchor(
                    weaponRoot.transform,
                    StringRestName,
                    stringRestPosition,
                    weaponRoot.transform.rotation);
                Transform stringBottom = CreateAnchor(
                    weaponRoot.transform,
                    StringBottomName,
                    stringBottomPosition,
                    weaponRoot.transform.rotation);
                stringTop.gameObject.layer = 0;
                stringRest.gameObject.layer = 0;
                stringBottom.gameObject.layer = 0;
                LineRenderer bowString = CreateBowString(
                    weaponRoot.transform,
                    stringTop,
                    stringRest,
                    stringBottom);

                Vector3 nockDelta = stringRestPosition - sourceArrowNock;
                Transform supportHand = CreateAnchor(
                    weaponRoot.transform,
                    "SupportHandTarget",
                    rightHand.position + nockDelta,
                    rightHand.rotation);
                Transform supportHint = CreateAnchor(
                    weaponRoot.transform,
                    "SupportHintTarget",
                    rightLowerArm.position + nockDelta,
                    rightLowerArm.rotation);
                supportHand.gameObject.layer = 0;
                supportHint.gameObject.layer = 0;

                var muzzleEffect = new GameObject("BrawlMuzzleVfx");
                muzzleEffect.layer = 12;
                muzzleEffect.transform.SetParent(muzzle, false);
                ParticleSystem particles = muzzleEffect.AddComponent<ParticleSystem>();
                // A fresh ParticleSystem starts playing immediately; duration
                // can only be written while it is fully stopped.
                particles.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
                var main = particles.main;
                main.playOnAwake = false;
                main.loop = false;
                main.duration = 0.12f;
                main.startLifetime = 0.1f;
                main.startSpeed = 1.8f;
                main.startSize = 0.11f;
                main.startColor = ThornMuzzleColor;
                main.maxParticles = 10;
                var emission = particles.emission;
                emission.rateOverTime = 0f;
                emission.SetBursts(new[] { new ParticleSystem.Burst(0f, 7) });
                var shape = particles.shape;
                shape.shapeType = ParticleSystemShapeType.Cone;
                shape.angle = 9f;
                shape.radius = 0.01f;
                particles.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

                GameObject saved = PrefabUtility.SaveAsPrefabAsset(
                    weaponRoot, WeaponPrefabPath, out bool success);
                if (!success || saved == null)
                {
                    throw new InvalidOperationException(
                        "Unity failed to save " + WeaponPrefabPath + ".");
                }
            }
            finally
            {
                EditorSceneManager.ClosePreviewScene(previewScene);
            }
        }

        static GameObject NormalizeBowPresenter(GameObject prefab)
        {
            if (prefab == null)
                throw new ArgumentNullException(nameof(prefab));
            string path = AssetDatabase.GetAssetPath(prefab);
            GameObject contents = PrefabUtility.LoadPrefabContents(path);
            try
            {
                Animator animator = contents.GetComponent<Animator>();
                BrawlInvectorThirdPersonController controller =
                    contents.GetComponent<BrawlInvectorThirdPersonController>();
                InvectorBrawlerWeaponPresentation presenter =
                    contents.GetComponent<InvectorBrawlerWeaponPresentation>();
                InvectorBowPresentationRig bowRig =
                    contents.GetComponent<InvectorBowPresentationRig>();
                if (bowRig == null)
                    bowRig = contents.AddComponent<InvectorBowPresentationRig>();
                Transform visual = FindDescendant(
                    contents.transform, WeaponPresentationName);
                Transform muzzle = visual != null
                    ? FindDescendant(visual, SpellOriginName)
                    : null;
                Transform nock = visual != null
                    ? FindDescendant(visual, NockPointName)
                    : null;
                Transform bowVisual = visual != null
                    ? FindDescendant(visual, BowVisualName)
                    : null;
                Transform arrow = FindDescendant(
                    contents.transform, AuthoredArrowName);
                Transform rightHand = animator != null && animator.isHuman
                    ? animator.GetBoneTransform(HumanBodyBones.RightHand)
                    : null;
                Transform rightLowerArm = animator != null && animator.isHuman
                    ? animator.GetBoneTransform(HumanBodyBones.RightLowerArm)
                    : null;
                Transform stringTop = visual != null
                    ? FindDescendant(visual, StringTopName)
                    : null;
                Transform stringRest = visual != null
                    ? FindDescendant(visual, StringRestName)
                    : null;
                Transform stringBottom = visual != null
                    ? FindDescendant(visual, StringBottomName)
                    : null;
                LineRenderer bowString = visual != null
                    ? visual.GetComponentInChildren<LineRenderer>(true)
                    : null;
                Transform supportHand = visual != null
                    ? FindDescendant(visual, "SupportHandTarget")
                    : null;
                Transform supportHint = visual != null
                    ? FindDescendant(visual, "SupportHintTarget")
                    : null;
                ParticleSystem[] effects = visual != null
                    ? visual.GetComponentsInChildren<ParticleSystem>(true)
                    : Array.Empty<ParticleSystem>();
                vWeaponIKAdjustList list =
                    AssetDatabase.LoadAssetAtPath<vWeaponIKAdjustList>(
                        WeaponIKAdjustListPath);
                if (animator == null || controller == null || presenter == null ||
                    visual == null || muzzle == null || nock == null ||
                    bowVisual == null || arrow == null || rightHand == null ||
                    rightLowerArm == null ||
                    stringTop == null ||
                    stringRest == null || stringBottom == null || bowString == null ||
                    supportHand == null || supportHint == null ||
                    effects.Length != 1 || list == null)
                {
                    throw new InvalidOperationException(
                        "The assembled Thorn pilot lost its bow presentation references.");
                }

                GameObject handOwnedArrow = UnityEngine.Object.Instantiate(
                    arrow.gameObject,
                    rightHand,
                    true);
                handOwnedArrow.name = AuthoredArrowName;
                UnityEngine.Object.DestroyImmediate(arrow.gameObject);
                arrow = handOwnedArrow.transform;
                DeriveArrowEndpoints(
                    arrow,
                    bowVisual,
                    out Vector3 arrowNockLocalPoint,
                    out Vector3 arrowTipLocalPoint,
                    out Vector3 sourceArrowNock,
                    out Vector3 sourceArrowTip);
                UnityEngine.Object.DestroyImmediate(supportHand.gameObject);
                UnityEngine.Object.DestroyImmediate(supportHint.gameObject);
                supportHand = new GameObject("SupportHandTarget").transform;
                supportHint = new GameObject("SupportHintTarget").transform;
                supportHand.SetParent(nock, false);
                supportHint.SetParent(nock, false);
                Vector3 nockDelta = nock.position - sourceArrowNock;
                supportHand.SetPositionAndRotation(
                    rightHand.position + nockDelta,
                    rightHand.rotation);
                supportHint.SetPositionAndRotation(
                    rightLowerArm.position + nockDelta,
                    rightLowerArm.rotation);
                supportHint.localPosition = SupportHintNockLocalPosition;

                Vector3 expectedNock = stringRest.position;
                Vector3 expectedTip = expectedNock +
                    (sourceArrowTip - sourceArrowNock);
                if (Vector3.Distance(nock.position, expectedNock) >= 0.0001f ||
                    Vector3.Distance(muzzle.position, expectedTip) >= 0.0001f)
                {
                    throw new InvalidOperationException(
                        "The generated Thorn nock/tip anchors no longer match the bow string rest and Arrow2 mesh vector.");
                }

                bowRig.Configure(
                    arrow,
                    nock,
                    arrowNockLocalPoint,
                    arrowTipLocalPoint,
                    bowString,
                    stringTop,
                    stringRest,
                    stringBottom);

                presenter.Configure(
                    animator,
                    controller,
                    visual,
                    muzzle,
                    supportHand,
                    supportHint,
                    list,
                    WeaponCategory,
                    true,
                    effects);
                presenter.ConfigurePreviewPose(
                    PreviewWeaponHandLocalPosition,
                    PreviewWeaponHintLocalPosition,
                    PreviewWeaponHandLocalEuler,
                    PreviewSupportHandLocalPosition,
                    PreviewSupportHintLocalPosition,
                    PreviewSupportHandLocalEuler,
                    true);
                visual.localPosition = CarryPresentationLocalPosition;
                GameObject saved = PrefabUtility.SaveAsPrefabAsset(
                    contents, path, out bool success);
                if (!success || saved == null)
                {
                    throw new InvalidOperationException(
                        "Unity failed to normalize the Thorn bow presenter.");
                }
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(contents);
            }
            return AssetDatabase.LoadAssetAtPath<GameObject>(path);
        }

        static void ValidateBowIKData(vWeaponIKAdjustList list)
        {
            ComputeArmInsets(
                out Vector3 leftHandInset,
                out Vector3 rightHandInset);
            vWeaponIKAdjust adjust = list != null
                ? list.GetWeaponIK(WeaponCategory)
                : null;
            if (list == null || adjust == null ||
                list.weaponIKAdjusts.Count != 1 ||
                list.weaponIKAdjusts[0] != adjust ||
                adjust.weaponCategories.Count != 1 ||
                !string.Equals(
                    adjust.weaponCategories[0],
                    WeaponCategory,
                    StringComparison.Ordinal) ||
                list.GetWeaponIK(InvectorMigrationPilotBuilder.WeaponCategory) != null ||
                !adjust.HasAllDefaultStates() ||
                adjust.ikAdjustsLeft.Count != 4 ||
                adjust.ikAdjustsRight.Count != 4 ||
                list.ikTargetPositionOffsetR != Vector3.zero ||
                list.ikTargetRotationOffsetR != Vector3.zero ||
                list.ikTargetPositionOffsetL != Vector3.zero ||
                list.ikTargetRotationOffsetL != Vector3.zero ||
                !ValidateIKStateOffsets(
                    adjust.ikAdjustsLeft,
                    leftHandInset,
                    Vector3.zero,
                    true) ||
                !ValidateIKStateOffsets(
                    adjust.ikAdjustsRight,
                    rightHandInset,
                    leftHandInset))
            {
                throw new InvalidOperationException(
                    "The Thorn IK list must contain one complete BrawlWizardBow adjustment with deterministic inward reach corrections and zero global offsets.");
            }
        }

        static bool ValidateIKStateOffsets(
            IReadOnlyList<IKAdjust> states,
            Vector3 expectedWeaponInset,
            Vector3 expectedSupportInset,
            bool relaxedCarry = false)
        {
            if (states == null || states.Count != vWeaponIKAdjust.defaultNames.Length)
                return false;
            for (int i = 0; i < states.Count; i++)
            {
                IKAdjust state = states[i];
                bool aiming = state != null &&
                    (string.Equals(
                         state.name,
                         vWeaponIKAdjust.StandingAimingState,
                         StringComparison.Ordinal) ||
                     string.Equals(
                         state.name,
                         vWeaponIKAdjust.CrouchingAimingState,
                         StringComparison.Ordinal));
                Vector3 requiredSupportInset = relaxedCarry && !aiming
                    ? RelaxedSupportHandOffset
                    : expectedSupportInset;
                if (state == null || !string.Equals(
                        state.name,
                        vWeaponIKAdjust.defaultNames[i],
                        StringComparison.Ordinal) ||
                    !Approximately(
                        state.weaponHandOffset.position,
                        expectedWeaponInset) ||
                    state.weaponHandOffset.eulerAngles != Vector3.zero ||
                    state.weaponHintOffset.position != Vector3.zero ||
                    state.weaponHintOffset.eulerAngles != Vector3.zero ||
                    !Approximately(
                        state.supportHandOffset.position,
                        requiredSupportInset) ||
                    state.supportHandOffset.eulerAngles != Vector3.zero ||
                    state.supportHintOffset.position != Vector3.zero ||
                    state.supportHintOffset.eulerAngles != Vector3.zero ||
                    state.spineOffset.spine != Vector2.zero ||
                    state.spineOffset.head != Vector2.zero)
                {
                    return false;
                }
            }
            return true;
        }

        static void ValidateThornVisuals(GameObject prefab)
        {
            GameObject thorn = AssetDatabase.LoadAssetAtPath<GameObject>(ThornPath);
            Animator sourceAnimator = thorn != null
                ? thorn.GetComponent<Animator>()
                : null;
            Transform sourceBow = thorn != null
                ? FindDescendant(thorn.transform, AuthoredBowName)
                : null;
            Transform sourceArrow = thorn != null
                ? FindDescendant(thorn.transform, AuthoredArrowName)
                : null;
            Renderer sourceBowRenderer = sourceBow != null
                ? sourceBow.GetComponent<Renderer>()
                : null;
            Renderer sourceArrowRenderer = sourceArrow != null
                ? sourceArrow.GetComponent<Renderer>()
                : null;

            Animator animator = prefab.GetComponent<Animator>();
            InvectorBrawlerWeaponPresentation presenter =
                prefab.GetComponent<InvectorBrawlerWeaponPresentation>();
            Transform leftSocket = FindDescendant(
                prefab.transform, LeftWeaponSocketName);
            Transform rightSocket = FindDescendant(
                prefab.transform, RightWeaponSocketName);
            Transform arrow = FindDescendant(
                prefab.transform, AuthoredArrowName);
            Renderer arrowRenderer = arrow != null
                ? arrow.GetComponent<Renderer>()
                : null;
            Transform presentation = FindDescendant(
                prefab.transform, WeaponPresentationName);
            Transform bowVisual = presentation != null
                ? FindDescendant(presentation, BowVisualName)
                : null;
            Renderer bowRenderer = bowVisual != null
                ? bowVisual.GetComponent<Renderer>()
                : null;
            Transform nock = presentation != null
                ? FindDescendant(presentation, NockPointName)
                : null;
            Transform muzzle = presentation != null
                ? FindDescendant(presentation, SpellOriginName)
                : null;
            Transform stringTop = presentation != null
                ? FindDescendant(presentation, StringTopName)
                : null;
            Transform stringRest = presentation != null
                ? FindDescendant(presentation, StringRestName)
                : null;
            Transform stringBottom = presentation != null
                ? FindDescendant(presentation, StringBottomName)
                : null;
            LineRenderer[] bowStrings = presentation != null
                ? presentation.GetComponentsInChildren<LineRenderer>(true)
                : Array.Empty<LineRenderer>();
            LineRenderer bowString = bowStrings.Length == 1
                ? bowStrings[0]
                : null;
            InvectorBowPresentationRig[] bowRigs =
                prefab.GetComponentsInChildren<InvectorBowPresentationRig>(true);
            InvectorBowPresentationRig bowRig = bowRigs.Length == 1
                ? bowRigs[0]
                : null;
            Transform supportHand = presentation != null
                ? FindDescendant(presentation, "SupportHandTarget")
                : null;
            Transform supportHint = presentation != null
                ? FindDescendant(presentation, "SupportHintTarget")
                : null;
            Transform leftHand = animator != null && animator.isHuman
                ? animator.GetBoneTransform(HumanBodyBones.LeftHand)
                : null;
            Transform rightHand = animator != null && animator.isHuman
                ? animator.GetBoneTransform(HumanBodyBones.RightHand)
                : null;
            Transform rightLowerArm = animator != null && animator.isHuman
                ? animator.GetBoneTransform(HumanBodyBones.RightLowerArm)
                : null;
            ParticleSystem[] effects = presentation != null
                ? presentation.GetComponentsInChildren<ParticleSystem>(true)
                : Array.Empty<ParticleSystem>();

            bool bowMatches = sourceBowRenderer != null && bowRenderer != null &&
                sourceBowRenderer.sharedMaterials.SequenceEqual(
                    bowRenderer.sharedMaterials);
            bool arrowMatches = sourceArrowRenderer != null && arrowRenderer != null &&
                sourceArrowRenderer.sharedMaterials.SequenceEqual(
                    arrowRenderer.sharedMaterials);
            Vector3 expectedArrowNockLocal = Vector3.zero;
            Vector3 expectedArrowTipLocal = Vector3.zero;
            Vector3 expectedNockPosition = Vector3.zero;
            Vector3 expectedTipPosition = Vector3.zero;
            bool arrowEndpointsDerived = arrow != null && bowVisual != null &&
                TryDeriveArrowEndpoints(
                    arrow,
                    bowVisual,
                    out expectedArrowNockLocal,
                    out expectedArrowTipLocal,
                    out expectedNockPosition,
                    out expectedTipPosition);
            Vector3 stagedNockPosition = Vector3.zero;
            Vector3 stagedTipPosition = Vector3.zero;
            bool stagedArrowAlignment = arrowEndpointsDerived &&
                rightHand != null && supportHand != null;
            if (stagedArrowAlignment)
            {
                stagedNockPosition = supportHand.TransformPoint(
                    rightHand.InverseTransformPoint(expectedNockPosition));
                stagedTipPosition = supportHand.TransformPoint(
                    rightHand.InverseTransformPoint(expectedTipPosition));
            }
            Vector3 expectedStringTop = Vector3.zero;
            Vector3 expectedStringRest = Vector3.zero;
            Vector3 expectedStringBottom = Vector3.zero;
            bool stringAnchorsDerived = bowVisual != null &&
                TryDeriveBowStringAnchors(
                    bowVisual,
                    out expectedStringTop,
                    out expectedStringRest,
                    out expectedStringBottom);
            bool lineMatchesAnchors = bowString != null &&
                bowString.positionCount == 3 && !bowString.useWorldSpace &&
                stringTop != null && stringRest != null && stringBottom != null &&
                Vector3.Distance(
                    bowString.transform.TransformPoint(bowString.GetPosition(0)),
                    stringTop.position) < 0.0001f &&
                Vector3.Distance(
                    bowString.transform.TransformPoint(bowString.GetPosition(1)),
                    stringRest.position) < 0.0001f &&
                Vector3.Distance(
                    bowString.transform.TransformPoint(bowString.GetPosition(2)),
                    stringBottom.position) < 0.0001f;
            Color actualMuzzleColor = effects.Length == 1
                ? effects[0].main.startColor.color
                : Color.clear;

            var failures = new List<string>();
            void Require(bool condition, string invariant)
            {
                if (!condition) failures.Add(invariant);
            }

            Require(sourceAnimator != null, "source Animator");
            Require(leftSocket != null, "left weapon socket");
            Require(leftHand != null, "left Humanoid hand");
            Require(rightSocket != null, "right weapon socket");
            Require(presentation != null, "bow presentation root");
            Require(presentation != null && presentation.parent == leftHand,
                "left-held presentation parent");
            Require(presentation != null && Approximately(
                    presentation.localPosition,
                    CarryPresentationLocalPosition),
                "calibrated left-hand carry position");
            Require(FindDescendants(prefab.transform, AuthoredBowName).Length == 0,
                "removed inherited Bow2");
            Require(bowVisual != null, "BowVisual");
            Require(bowMatches, "BowVisual materials");
            Require(arrow != null, "Arrow2");
            Require(arrow != null && arrow.parent == rightHand,
                "right-held Arrow2 parent");
            Require(arrow != null && arrow.gameObject.activeSelf,
                "authored Arrow2 active state");
            Require(arrowMatches, "Arrow2 materials");
            Require(arrowEndpointsDerived, "mesh-derived Arrow2 endpoints");
            Require(nock != null, "NockPoint");
            Require(nock != null && stagedArrowAlignment &&
                    Vector3.Distance(nock.position, stagedNockPosition) < 0.0001f,
                "support-solved NockPoint position");
            Require(nock != null && stringRest != null &&
                    Vector3.Distance(nock.position, stringRest.position) < 0.0001f,
                "bow-owned string-rest NockPoint");
            Require(nock != null && arrow != null &&
                    Quaternion.Angle(nock.rotation, arrow.rotation) < 0.01f,
                "NockPoint rotation");
            Require(muzzle != null, "SpellOrigin");
            Require(muzzle != null && muzzle.parent == nock,
                "SpellOrigin parent");
            Require(muzzle != null && stagedArrowAlignment &&
                    Vector3.Distance(muzzle.position, stagedTipPosition) < 0.0001f,
                "support-solved SpellOrigin tip position");
            Require(muzzle != null && arrow != null &&
                    Quaternion.Angle(muzzle.rotation, arrow.rotation) < 0.01f,
                "SpellOrigin rotation");
            Require(stringAnchorsDerived, "mesh-derived string anchors");
            Require(stringTop != null &&
                    Vector3.Distance(stringTop.position, expectedStringTop) < 0.0001f,
                "StringTop position");
            Require(stringRest != null &&
                    Vector3.Distance(stringRest.position, expectedStringRest) < 0.0001f,
                "StringRest position");
            Require(stringBottom != null &&
                    Vector3.Distance(stringBottom.position, expectedStringBottom) < 0.0001f,
                "StringBottom position");
            Require(bowString != null, "BowString LineRenderer");
            Require(lineMatchesAnchors, "BowString anchor positions");
            Require(bowString != null &&
                    Mathf.Approximately(bowString.startWidth, BowStringWidth) &&
                    Mathf.Approximately(bowString.endWidth, BowStringWidth),
                "BowString width");
            Require(bowString != null &&
                    bowString.sharedMaterial ==
                    AssetDatabase.LoadAssetAtPath<Material>(WeaponsMaterialPath),
                "BowString material");
            Require(bowString != null &&
                    bowString.shadowCastingMode == ShadowCastingMode.Off,
                "BowString shadow posture");
            Require(bowRigs.Length == 1 && bowRig != null &&
                    bowRig.gameObject == prefab,
                "single root bow rig");
            Require(bowRig != null && bowRig.IsDormantConfigured,
                "dormant configured bow rig");
            Require(presenter != null && presenter.HasAuthoredPreviewPose,
                "authored dormant menu preview pose");
            Require(bowRig != null && bowRig.ArrowVisual == arrow,
                "bow rig Arrow2 reference");
            Require(bowRig != null && bowRig.NockPoint == nock,
                "bow rig NockPoint reference");
            Require(bowRig != null && bowRig.BowString == bowString,
                "bow rig BowString reference");
            Require(bowRig != null && bowRig.StringTopAnchor == stringTop,
                "bow rig StringTop reference");
            Require(bowRig != null && bowRig.StringRestAnchor == stringRest,
                "bow rig StringRest reference");
            Require(bowRig != null && bowRig.StringBottomAnchor == stringBottom,
                "bow rig StringBottom reference");
            Require(bowRig != null && Approximately(
                    bowRig.ArrowNockLocalPoint, expectedArrowNockLocal),
                "bow rig Arrow2 nock local point");
            Require(bowRig != null && Approximately(
                    bowRig.ArrowTipLocalPoint, expectedArrowTipLocal),
                "bow rig Arrow2 tip local point");
            Require(supportHand != null, "SupportHandTarget");
            Require(supportHint != null, "SupportHintTarget");
            Require(supportHand != null && supportHand.parent == nock,
                "nock-relative SupportHandTarget");
            Require(supportHint != null && supportHint.parent == nock &&
                    Approximately(
                        supportHint.localPosition,
                        SupportHintNockLocalPosition),
                "nock-relative calibrated SupportHintTarget");
            Require(rightHand != null, "Humanoid right hand");
            Require(rightLowerArm != null, "Humanoid right lower arm");
            Require(effects.Length == 1, "single muzzle effect");
            Require(effects.Length == 1 && !effects[0].main.playOnAwake,
                "muzzle effect play-on-awake posture");
            Require(effects.Length == 1 && !effects[0].main.loop,
                "muzzle effect loop posture");
            Require(Approximately(actualMuzzleColor, ThornMuzzleColor),
                "muzzle effect color");

            if (failures.Count != 0)
            {
                throw new InvalidOperationException(
                    "The Thorn Bow2/Arrow2 presentation changed: " +
                    string.Join(", ", failures) + ".");
            }
        }

        static void ValidateDormantVendorFirewall(GameObject prefab)
        {
            vShooterManager shooter = prefab.GetComponent<vShooterManager>();
            vAmmoManager ammo = prefab.GetComponent<vAmmoManager>();
            vCollectShooterMeleeControl collector =
                prefab.GetComponent<vCollectShooterMeleeControl>();
            BrawlInvectorMeleePresentationManager melee =
                prefab.GetComponent<BrawlInvectorMeleePresentationManager>();
            bool managerPosture = shooter != null && !shooter.enabled &&
                shooter.rWeapon == null && shooter.lWeapon == null &&
                shooter.weaponIKAdjustList == null &&
                shooter.damageLayer.value == 0 && shooter.blockAimLayer.value == 0 &&
                !shooter.useCancelReload && !shooter.useAmmoDisplay &&
                !shooter.applyRecoilToCamera && !shooter.useLockOn &&
                !shooter.useLockOnMeleeOnly && !shooter.hipfireShot &&
                !shooter.alwaysAiming && !shooter.AllAmmoInfinity &&
                ammo != null && !ammo.enabled && ammo.ammoListData == null &&
                ammo.itemManager == null && ammo.ammos != null && ammo.ammos.Count == 0 &&
                collector != null && !collector.enabled &&
                melee != null && !melee.enabled && melee.Members.Count == 0 &&
                melee.leftWeapon == null && melee.rightWeapon == null &&
                Mathf.Approximately(melee.defaultStaminaCost, 0f) &&
                Mathf.Approximately(melee.defaultStaminaRecoveryDelay, 0f);
            if (!managerPosture || prefab.GetComponentsInChildren<Component>(true)
                    .Where(component => component != null)
                    .Any(component => IsForbiddenPhysicalCombatType(
                        component.GetType().Name)))
            {
                throw new InvalidOperationException(
                    "The Thorn hierarchy activated or retained a vendor bow, ammo, projectile, melee, hitbox, or damage authority.");
            }
        }

        static bool IsForbiddenPhysicalCombatType(string typeName)
        {
            switch (typeName)
            {
                case "vShooterWeapon":
                case "vShooterWeaponBase":
                case "vProjectileControl":
                case "vProjectileInstantiate":
                case "vObjectDamage":
                case "vDamageSender":
                case "vDamageReceiver":
                case "vHitBox":
                case "vMeleeAttackObject":
                    return true;
                default:
                    return false;
            }
        }

        static void ValidateSelectiveLayers(GameObject prefab)
        {
            BrawlerHitProxy[] proxies =
                prefab.GetComponentsInChildren<BrawlerHitProxy>(true);
            Transform presentation = FindDescendant(
                prefab.transform, WeaponPresentationName);
            Transform nock = presentation != null
                ? FindDescendant(presentation, NockPointName)
                : null;
            Transform muzzle = presentation != null
                ? FindDescendant(presentation, SpellOriginName)
                : null;
            Transform stringTop = presentation != null
                ? FindDescendant(presentation, StringTopName)
                : null;
            Transform stringRest = presentation != null
                ? FindDescendant(presentation, StringRestName)
                : null;
            Transform stringBottom = presentation != null
                ? FindDescendant(presentation, StringBottomName)
                : null;
            LineRenderer[] strings = presentation != null
                ? presentation.GetComponentsInChildren<LineRenderer>(true)
                : Array.Empty<LineRenderer>();
            ParticleSystem[] effects = presentation != null
                ? presentation.GetComponentsInChildren<ParticleSystem>(true)
                : Array.Empty<ParticleSystem>();
            bool valid = prefab.layer ==
                    InvectorMigrationPilotBuilder.InvectorPlayerLayer &&
                prefab.GetComponentsInChildren<Transform>(true)
                    .Where(item => item != prefab.transform)
                    .All(item => item.gameObject.layer != prefab.layer) &&
                proxies.Length == 1 && proxies[0].transform.parent == prefab.transform &&
                !proxies[0].enabled && proxies[0].TriggerCollider != null &&
                proxies[0].TriggerCollider.isTrigger &&
                !proxies[0].TriggerCollider.enabled &&
                proxies[0].gameObject.layer == CombatPhysics.BrawlerHitboxLayer &&
                presentation != null && presentation.gameObject.layer == 0 &&
                nock != null && nock.gameObject.layer == 0 &&
                muzzle != null && muzzle.gameObject.layer == 12 &&
                stringTop != null && stringTop.gameObject.layer == 0 &&
                stringRest != null && stringRest.gameObject.layer == 0 &&
                stringBottom != null && stringBottom.gameObject.layer == 0 &&
                strings.Length == 1 && strings[0].gameObject.layer == 0 &&
                effects.Length == 1 && effects[0].gameObject.layer == 12 &&
                prefab.GetComponentsInChildren<Renderer>(true)
                    .Where(renderer => !(renderer is ParticleSystemRenderer))
                    .All(renderer => renderer.gameObject.layer == 0);
            if (!valid)
            {
                throw new InvalidOperationException(
                    "The Thorn root/proxy/bow/nock/muzzle selective-layer contract changed.");
            }
        }

        static void ValidateProductionVariantSource(
            GameObject prefab,
            string expectedPath)
        {
            RequireAsset(prefab, expectedPath, "Thorn production variant");
            if (prefab.activeSelf ||
                PrefabUtility.GetPrefabAssetType(prefab) != PrefabAssetType.Variant)
            {
                throw new InvalidOperationException(
                    "A Thorn production variant must remain an inactive prefab variant.");
            }

            GameObject source = PrefabUtility.GetCorrespondingObjectFromSource(prefab);
            if (source == null || !string.Equals(
                    AssetDatabase.GetAssetPath(source),
                    PilotPrefabPath,
                    StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    "A Thorn production variant must derive directly from the validated Thorn pilot.");
            }
        }

        static void RequireIdentity(
            GameObject prefab,
            InvectorBrawlerPrefabRole role)
        {
            InvectorBrawlerPrefabIdentity identity =
                RequireExactlyOneRoot<InvectorBrawlerPrefabIdentity>(prefab);
            if (!identity.Matches(RosterId, role))
            {
                throw new InvalidOperationException(
                    "The Thorn production prefab identity changed.");
            }
        }

        static bool Approximately(Color left, Color right)
        {
            return Mathf.Abs(left.r - right.r) < 0.01f &&
                   Mathf.Abs(left.g - right.g) < 0.01f &&
                   Mathf.Abs(left.b - right.b) < 0.01f &&
                   Mathf.Abs(left.a - right.a) < 0.01f;
        }

        static bool Approximately(Vector3 left, Vector3 right)
        {
            return (left - right).sqrMagnitude < 0.00000001f;
        }

        static void ComputeArmInsets(
            out Vector3 leftHandInset,
            out Vector3 rightHandInset)
        {
            GameObject thorn = AssetDatabase.LoadAssetAtPath<GameObject>(ThornPath);
            Animator animator = thorn != null ? thorn.GetComponent<Animator>() : null;
            if (animator == null || !animator.isHuman)
            {
                throw new InvalidOperationException(
                    "The pinned Thorn source needs a valid Humanoid Animator for IK reach correction.");
            }

            leftHandInset = ComputeArmInset(
                animator,
                HumanBodyBones.LeftUpperArm,
                HumanBodyBones.LeftHand);
            rightHandInset = ComputeArmInset(
                animator,
                HumanBodyBones.RightUpperArm,
                HumanBodyBones.RightHand);
        }

        static Vector3 ComputeArmInset(
            Animator animator,
            HumanBodyBones upperArmBone,
            HumanBodyBones handBone)
        {
            Transform upperArm = animator.GetBoneTransform(upperArmBone);
            Transform hand = animator.GetBoneTransform(handBone);
            if (upperArm == null || hand == null)
            {
                throw new InvalidOperationException(
                    "The pinned Thorn Avatar lost a required Humanoid arm bone.");
            }

            Vector3 towardShoulder = upperArm.position - hand.position;
            if (!IsFinite(towardShoulder) ||
                towardShoulder.sqrMagnitude <= 0.000001f)
            {
                throw new InvalidOperationException(
                    "The pinned Thorn arm bind pose cannot define an inward IK reach correction.");
            }
            return Quaternion.Inverse(hand.rotation) *
                   (towardShoulder.normalized * IKReachInset);
        }

        static void DeriveArrowEndpoints(
            Transform arrow,
            Transform bowVisual,
            out Vector3 arrowNockLocal,
            out Vector3 arrowTipLocal,
            out Vector3 arrowNockWorld,
            out Vector3 arrowTipWorld)
        {
            if (!TryDeriveArrowEndpoints(
                    arrow,
                    bowVisual,
                    out arrowNockLocal,
                    out arrowTipLocal,
                    out arrowNockWorld,
                    out arrowTipWorld))
            {
                throw new InvalidOperationException(
                    "Bow02 Arrow2 needs a finite non-degenerate MeshFilter bounds for nock/tip derivation.");
            }
        }

        static bool TryDeriveArrowEndpoints(
            Transform arrow,
            Transform bowVisual,
            out Vector3 arrowNockLocal,
            out Vector3 arrowTipLocal,
            out Vector3 arrowNockWorld,
            out Vector3 arrowTipWorld)
        {
            arrowNockLocal = Vector3.zero;
            arrowTipLocal = Vector3.zero;
            arrowNockWorld = Vector3.zero;
            arrowTipWorld = Vector3.zero;
            if (!TryGetMeshAxisEndpoints(
                    arrow,
                    out Vector3 firstLocal,
                    out Vector3 secondLocal) ||
                bowVisual == null)
            {
                return false;
            }

            Vector3 firstWorld = arrow.TransformPoint(firstLocal);
            Vector3 secondWorld = arrow.TransformPoint(secondLocal);
            MeshFilter bowMeshFilter = bowVisual.GetComponent<MeshFilter>();
            Vector3 bowCenter = bowMeshFilter != null &&
                                bowMeshFilter.sharedMesh != null
                ? bowVisual.TransformPoint(bowMeshFilter.sharedMesh.bounds.center)
                : bowVisual.position;
            bool firstIsNock =
                (firstWorld - bowCenter).sqrMagnitude <=
                (secondWorld - bowCenter).sqrMagnitude;
            arrowNockLocal = firstIsNock ? firstLocal : secondLocal;
            arrowTipLocal = firstIsNock ? secondLocal : firstLocal;
            arrowNockWorld = firstIsNock ? firstWorld : secondWorld;
            arrowTipWorld = firstIsNock ? secondWorld : firstWorld;
            return IsFinite(arrowNockLocal) && IsFinite(arrowTipLocal) &&
                   IsFinite(arrowNockWorld) && IsFinite(arrowTipWorld) &&
                   (arrowTipLocal - arrowNockLocal).sqrMagnitude > 0.000001f;
        }

        static void DeriveBowStringAnchors(
            Transform bowVisual,
            out Vector3 top,
            out Vector3 rest,
            out Vector3 bottom)
        {
            if (!TryDeriveBowStringAnchors(
                    bowVisual, out top, out rest, out bottom))
            {
                throw new InvalidOperationException(
                    "Bow02 Bow2 needs a finite non-degenerate MeshFilter bounds for string derivation.");
            }
        }

        static bool TryDeriveBowStringAnchors(
            Transform bowVisual,
            out Vector3 top,
            out Vector3 rest,
            out Vector3 bottom)
        {
            top = Vector3.zero;
            rest = Vector3.zero;
            bottom = Vector3.zero;
            if (!TryGetMeshAxisEndpoints(
                    bowVisual,
                    out Vector3 firstLocal,
                    out Vector3 secondLocal))
            {
                return false;
            }

            Vector3 firstWorld = bowVisual.TransformPoint(firstLocal);
            Vector3 secondWorld = bowVisual.TransformPoint(secondLocal);
            bool firstIsTop = firstWorld.y >= secondWorld.y;
            top = firstIsTop ? firstWorld : secondWorld;
            bottom = firstIsTop ? secondWorld : firstWorld;
            rest = Vector3.Lerp(top, bottom, 0.5f);
            return IsFinite(top) && IsFinite(rest) && IsFinite(bottom) &&
                   (top - bottom).sqrMagnitude > 0.000001f;
        }

        static bool TryGetMeshAxisEndpoints(
            Transform visual,
            out Vector3 first,
            out Vector3 second)
        {
            first = Vector3.zero;
            second = Vector3.zero;
            MeshFilter filter = visual != null ? visual.GetComponent<MeshFilter>() : null;
            Mesh mesh = filter != null ? filter.sharedMesh : null;
            if (mesh == null) return false;

            Bounds bounds = mesh.bounds;
            Vector3 size = bounds.size;
            if (!IsFinite(bounds.center) || !IsFinite(size)) return false;
            int axis = LongestAxis(size);
            float extent = Axis(bounds.extents, axis);
            if (!IsFinite(extent) || extent <= 0.0001f) return false;
            first = bounds.center;
            second = bounds.center;
            SetAxis(ref first, axis, Axis(bounds.center, axis) - extent);
            SetAxis(ref second, axis, Axis(bounds.center, axis) + extent);
            return IsFinite(first) && IsFinite(second);
        }

        static int LongestAxis(Vector3 size)
        {
            if (size.x >= size.y && size.x >= size.z) return 0;
            return size.y >= size.z ? 1 : 2;
        }

        static float Axis(Vector3 value, int axis)
        {
            return axis == 0 ? value.x : axis == 1 ? value.y : value.z;
        }

        static void SetAxis(ref Vector3 value, int axis, float component)
        {
            if (axis == 0) value.x = component;
            else if (axis == 1) value.y = component;
            else value.z = component;
        }

        static LineRenderer CreateBowString(
            Transform parent,
            Transform top,
            Transform rest,
            Transform bottom)
        {
            var stringObject = new GameObject(BowStringName);
            stringObject.layer = 0;
            stringObject.transform.SetParent(parent, false);
            LineRenderer line = stringObject.AddComponent<LineRenderer>();
            line.useWorldSpace = false;
            line.loop = false;
            line.positionCount = 3;
            line.startWidth = BowStringWidth;
            line.endWidth = BowStringWidth;
            line.numCapVertices = 2;
            line.numCornerVertices = 0;
            line.alignment = LineAlignment.View;
            line.textureMode = LineTextureMode.Stretch;
            line.shadowCastingMode = ShadowCastingMode.Off;
            line.receiveShadows = false;
            line.lightProbeUsage = LightProbeUsage.Off;
            line.reflectionProbeUsage = ReflectionProbeUsage.Off;
            line.sharedMaterial = AssetDatabase.LoadAssetAtPath<Material>(
                WeaponsMaterialPath);
            line.SetPosition(
                0, line.transform.InverseTransformPoint(top.position));
            line.SetPosition(
                1, line.transform.InverseTransformPoint(rest.position));
            line.SetPosition(
                2, line.transform.InverseTransformPoint(bottom.position));
            return line;
        }

        static void ValidateAuthorityOwners(GameObject prefab, bool requiresNavigation)
        {
            MonoBehaviour[] behaviours =
                prefab.GetComponentsInChildren<MonoBehaviour>(true);
            MonoBehaviour[] motors = behaviours
                .Where(value => value is IBrawlerMotor)
                .ToArray();
            MonoBehaviour[] animations = behaviours
                .Where(value => value is IBrawlerAnimationDriver)
                .ToArray();
            MonoBehaviour[] navigation = behaviours
                .Where(value => value is IBrawlerNavigation)
                .ToArray();
            bool valid = motors.Length == 1 &&
                motors[0] is InvectorBrawlerMotor &&
                motors[0].gameObject == prefab &&
                animations.Length == 1 &&
                animations[0] is InvectorBrawlerAnimationDriver &&
                animations[0].gameObject == prefab &&
                (requiresNavigation
                    ? navigation.Length == 1 &&
                      navigation[0] is InvectorBrawlerNavigation &&
                      navigation[0].gameObject == prefab
                    : navigation.Length == 0);
            if (!valid)
            {
                throw new InvalidOperationException(
                    "The Thorn production variant does not have exactly one role-appropriate Invector motor, navigation, and animation authority.");
            }
        }

        static bool IsFinite(Vector3 value)
        {
            return IsFinite(value.x) && IsFinite(value.y) && IsFinite(value.z);
        }

        static bool IsFinite(float value)
        {
            return !float.IsNaN(value) && !float.IsInfinity(value);
        }

        static void CloseTransientPresentation(GameObject prefab)
        {
            if (prefab == null) return;
            InvectorBrawlerWeaponPresentation presenter =
                prefab.GetComponent<InvectorBrawlerWeaponPresentation>();
            if (presenter != null) presenter.DisableLabRuntime();
        }

        static Transform CreateAnchor(
            Transform parent,
            string name,
            Vector3 worldPosition,
            Quaternion worldRotation)
        {
            var anchor = new GameObject(name);
            anchor.transform.SetParent(parent, false);
            anchor.transform.SetPositionAndRotation(worldPosition, worldRotation);
            return anchor.transform;
        }

        static void SetLayerRecursively(GameObject root, int layer)
        {
            foreach (Transform item in root.GetComponentsInChildren<Transform>(true))
                item.gameObject.layer = layer;
        }

        static void RequireGuid(string path, string expectedGuid, string label)
        {
            if (!string.Equals(
                    AssetDatabase.AssetPathToGUID(path),
                    expectedGuid,
                    StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    "The pinned " + label +
                    " GUID changed. Re-audit Thorn before rebuilding.");
            }
        }

        static void RequireAsset(GameObject prefab, string expectedPath, string label)
        {
            if (prefab == null || !string.Equals(
                    AssetDatabase.GetAssetPath(prefab),
                    expectedPath,
                    StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    label + " is missing or saved at an unexpected path.");
            }
        }

        static T RequireExactlyOneRoot<T>(GameObject root) where T : Component
        {
            T[] components = root.GetComponentsInChildren<T>(true);
            if (components.Length != 1 || components[0].gameObject != root)
            {
                throw new InvalidOperationException(
                    "The Thorn prefab requires exactly one root " +
                    typeof(T).Name + ".");
            }
            return components[0];
        }

        static Transform FindDescendant(Transform root, string name)
        {
            if (root == null) return null;
            return root.GetComponentsInChildren<Transform>(true)
                .FirstOrDefault(candidate => string.Equals(
                    candidate.name, name, StringComparison.Ordinal));
        }

        static Transform[] FindDescendants(Transform root, string name)
        {
            if (root == null) return Array.Empty<Transform>();
            return root.GetComponentsInChildren<Transform>(true)
                .Where(candidate => string.Equals(
                    candidate.name, name, StringComparison.Ordinal))
                .ToArray();
        }

        static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;
            string normalized = path.Replace('\\', '/').TrimEnd('/');
            string parent = Path.GetDirectoryName(normalized)?.Replace('\\', '/');
            string name = Path.GetFileName(normalized);
            if (string.IsNullOrEmpty(parent) || string.IsNullOrEmpty(name))
            {
                throw new InvalidOperationException(
                    "Invalid asset folder path: " + path);
            }
            EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, name);
        }
    }
}
