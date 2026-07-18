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
using UnityEngine.SceneManagement;

namespace BrawlArena.EditorAutomation
{
    /// <summary>
    /// Builds Bastion from the generated BastionWarrior source while reusing
    /// the proven project-owned Invector topology. No lab or caller scene is
    /// opened, saved, closed, or reloaded by this builder.
    /// </summary>
    public static class InvectorBastionMigrationBuilder
    {
        public const string Root = "Assets/Generated/InvectorMigration/Bastion/";
        public const string OverrideControllerPath =
            Root + "Controllers/BastionInvectorPilot.overrideController";
        public const string PilotPrefabPath = Root + "Prefabs/BastionInvectorPilot.prefab";
        public const string ProductionHumanPrefabPath =
            Root + "Prefabs/BastionInvectorHuman.prefab";
        public const string ProductionAIPrefabPath =
            "Assets/BrawlArena/Prefabs/Invector/BastionInvectorAI.prefab";
        public const string WeaponPrefabPath =
            Root + "Weapons/BastionSwordPresentation.prefab";
        public const string WeaponIKAdjustPath = Root + "IK/BastionSwordIKAdjust.asset";
        public const string WeaponIKAdjustListPath = Root + "IK/BastionSwordIKAdjustList.asset";

        // BastionWarrior.prefab is generated fresh by WarriorAssetBuilder in
        // this editor domain, so unlike Rime/Thorn's pinned vendor GUIDs its
        // GUID cannot be pre-committed here; ValidatePrerequisites checks its
        // authored shape instead of a fixed GUID.
        public const string BastionPath = "Assets/Generated/Warriors/Prefabs/BastionWarrior.prefab";
        public const string RosterId = "bastion";
        public const string AuthoredWeaponName = "Sword1_R";
        public const string WeaponPresentationName = "BastionSwordPresentation";
        public const string WeaponCategory = "BrawlWarriorSword";

        public const string BasicAttackOverrideSourceName = "WeakAttack_UnarmedA";
        public const string SuperAttackOverrideSourceName = "StrongAttack_PunchA";
        public const string CarryPoseOverrideSourceName = "Idle@Pistol";
        public const string BasicAttackClipPath =
            "Assets/ModularRPGHeroesPBR/Animations/SwordShield/NormalAttack01_SwordShield.fbx";
        public const string BasicAttackClipGuid = "e160663af1b56d44e94a7ceb3007c110";
        public const string BasicAttackClipName = "NormalAttack01_SwordShield";
        public const string SuperAttackClipPath =
            "Assets/ModularRPGHeroesPBR/Animations/SwordShield/NormalAttack02_SwordShield.fbx";
        public const string SuperAttackClipGuid = "dfbba31277e926e44b3def7cde351a13";
        public const string SuperAttackClipName = "NormalAttack02_SwordShield";
        public const string CarryPoseClipPath =
            "Assets/ModularRPGHeroesPBR/Animations/SwordShield/Idle_SwordShield.fbx";
        public const string CarryPoseClipGuid = "6383a08b0ddb19543948954c6e075143";
        public const string CarryPoseClipName = "Idle_SwordShield";

        static readonly Color BastionMuzzleColor = new Color(0xC9 / 255f, 0xD8 / 255f, 0xE6 / 255f, 1f);

        [MenuItem("Brawl Arena/Invector Migration/Build Bastion Sword Variants")]
        static void BuildFromMenu()
        {
            Debug.Log(BuildBastionPilotAssetsSafely());
        }

        public static string BuildBastionPilotAssetsSafely()
        {
            Scene originalScene = SceneManager.GetActiveScene();
            bool originalSceneWasDirty = originalScene.IsValid() && originalScene.isDirty;
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
                        "BastionInvectorPilot");
                ConfigureAttackOverrides(overrideController);

                // The shared pilot helper currently configures its presenter
                // once with the historical staff category. Keep a temporary
                // alias only while that topology is assembled, immediately
                // normalize the root presenter onto the sword category
                // afterward, then remove the alias before validating or
                // creating production variants.
                InvectorMigrationPilotBuilder.BuildWeaponIKAssets(
                    WeaponIKAdjustPath,
                    WeaponIKAdjustListPath,
                    "BastionSwordIKAdjust",
                    "BastionSwordIKAdjustList");
                AddSwordCategoryAlias();
                InvectorMigrationPilotBuilder.BuildWeaponPresentationPrefab(
                    BastionPath,
                    AuthoredWeaponName,
                    WeaponPresentationName,
                    WeaponPrefabPath,
                    BastionMuzzleColor);

                GameObject pilot = InvectorMigrationPilotBuilder.BuildPilotPrefab(
                    overrideController,
                    BastionPath,
                    "BastionInvectorPilot_DISABLED",
                    AuthoredWeaponName,
                    WeaponPrefabPath,
                    WeaponIKAdjustListPath,
                    WeaponPresentationName,
                    PilotPrefabPath);
                pilot = NormalizeSwordPresenter(pilot);
                FinalizeSwordCategory();
                CloseTransientPresentation(pilot);
                ValidatePilot(pilot);

                GameObject human = InvectorMigrationPilotBuilder.BuildProductionHumanPrefab(
                    pilot,
                    "BastionInvectorHuman_DISABLED",
                    ProductionHumanPrefabPath,
                    RosterId);
                CloseTransientPresentation(human);
                ValidateHumanPrefab(human);

                GameObject ai = InvectorMigrationPilotBuilder.BuildProductionAIPrefab(
                    pilot,
                    "BastionInvectorAI_DISABLED",
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
                return "Built the dormant Bastion Invector pilot plus inactive production-human and AI variants.";
            }
            finally
            {
                // A failed build must not leave the shared staff category on
                // the isolated Bastion IK data. An incomplete pilot will then
                // remain fail-closed until the next successful rebuild.
                FinalizeSwordCategory();
                AssetDatabase.SaveAssets();
                if (originalSceneWasDirty && originalScene.IsValid() && originalScene.isLoaded)
                    EditorSceneManager.MarkSceneDirty(originalScene);
            }
        }

        public static void ValidatePrerequisites()
        {
            InvectorMigrationPilotBuilder.ValidatePrerequisites();
            RequireGuid(BasicAttackClipPath, BasicAttackClipGuid, "NormalAttack01_SwordShield clip");
            RequireGuid(SuperAttackClipPath, SuperAttackClipGuid, "NormalAttack02_SwordShield clip");
            RequireGuid(CarryPoseClipPath, CarryPoseClipGuid, "Idle_SwordShield clip");

            GameObject bastion = AssetDatabase.LoadAssetAtPath<GameObject>(BastionPath);
            Animator animator = bastion != null ? bastion.GetComponent<Animator>() : null;
            Transform weapon = bastion != null
                ? FindDescendant(bastion.transform, AuthoredWeaponName)
                : null;
            if (bastion == null || animator == null || !animator.isHuman ||
                animator.avatar == null || !animator.avatar.isValid || weapon == null ||
                !weapon.gameObject.activeSelf || FindDescendant(weapon, "SpellOrigin") == null)
            {
                throw new InvalidOperationException(
                    "BastionWarrior must retain its valid Humanoid Avatar and Sword1_R/SpellOrigin hierarchy.");
            }
        }

        public static void ValidatePilot(GameObject prefab)
        {
            RequireAsset(prefab, PilotPrefabPath, "Bastion pilot");
            if (prefab.activeSelf || prefab.layer != InvectorMigrationPilotBuilder.InvectorPlayerLayer ||
                prefab.tag != "Player")
            {
                throw new InvalidOperationException(
                    "The Bastion pilot root is not in its inactive selective-layer posture.");
            }

            GameObject source = PrefabUtility.GetCorrespondingObjectFromSource(prefab);
            if (source == null || !string.Equals(
                    AssetDatabase.GetAssetPath(source), BastionPath, StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    "The Bastion pilot must derive directly from the generated BastionWarrior source.");
            }

            Animator animator = RequireExactlyOneRoot<Animator>(prefab);
            var overrides = animator.runtimeAnimatorController as AnimatorOverrideController;
            if (animator.enabled || animator.applyRootMotion || !animator.isHuman ||
                overrides == null || !string.Equals(
                    AssetDatabase.GetAssetPath(overrides), OverrideControllerPath,
                    StringComparison.Ordinal) ||
                !string.Equals(
                    AssetDatabase.GetAssetPath(overrides.runtimeAnimatorController),
                    InvectorMigrationPilotBuilder.LifecycleControllerPath,
                    StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    "The Bastion pilot Animator is not using the Bastion override over the shared lifecycle controller.");
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
                !string.Equals(
                    presenter.WeaponCategory, WeaponCategory, StringComparison.Ordinal) ||
                !string.Equals(
                    AssetDatabase.GetAssetPath(presenter.ProjectIKAdjustList),
                    WeaponIKAdjustListPath,
                    StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    "The Bastion sword presenter or its isolated IK list is not dormant and pinned.");
            }

            ValidateSwordIKData(presenter.ProjectIKAdjustList);
            ValidateBastionVisuals(prefab);
            if (prefab.GetComponentsInChildren<Health>(true).Length != 0 ||
                prefab.GetComponentsInChildren<BrawlerController>(true).Length != 0 ||
                prefab.GetComponentsInChildren<PlayerBrawlerInput>(true).Length != 0 ||
                prefab.GetComponentsInChildren<AIBrawler>(true).Length != 0 ||
                prefab.GetComponentsInChildren<NavMeshAgent>(true).Length != 0 ||
                prefab.GetComponentsInChildren<CharacterController>(true).Length != 0)
            {
                throw new InvalidOperationException(
                    "The dormant Bastion pilot contains a production facade, input, AI, or duplicate movement authority.");
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
            ValidateBastionVisuals(prefab);

            if (prefab.GetComponentsInChildren<AIBrawler>(true).Length != 0 ||
                prefab.GetComponentsInChildren<InvectorAIRuntimeGate>(true).Length != 0 ||
                prefab.GetComponentsInChildren<NavMeshAgent>(true).Length != 0 ||
                prefab.GetComponentsInChildren<CharacterController>(true).Length != 0)
            {
                throw new InvalidOperationException(
                    "The Bastion human variant contains an AI or duplicate movement authority.");
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
            ValidateBastionVisuals(prefab);

            NavMeshAgent[] agents = prefab.GetComponentsInChildren<NavMeshAgent>(true);
            if (agents.Length != 1 || agents[0].transform.parent != prefab.transform ||
                agents[0].enabled || agents[0].autoTraverseOffMeshLink ||
                !string.Equals(
                    agents[0].name,
                    InvectorMigrationPilotBuilder.ProductionAIPlannerName,
                    StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    "The Bastion AI variant does not contain one dormant child-only planner.");
            }

            if (prefab.GetComponentsInChildren<PlayerBrawlerInput>(true).Length != 0 ||
                prefab.GetComponentsInChildren<InvectorHumanRuntimeGate>(true).Length != 0 ||
                prefab.GetComponentsInChildren<CharacterController>(true).Length != 0 ||
                prefab.GetComponentsInChildren<Invector.vCharacterController.AI.vSimpleMeleeAI_Motor>(true).Length != 0)
            {
                throw new InvalidOperationException(
                    "The Bastion AI variant contains a human, CharacterController, or vendor AI authority.");
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
                    CarryPoseOverrideSourceName,
                },
                new[]
                {
                    BasicAttackClipPath,
                    SuperAttackClipPath,
                    CarryPoseClipPath,
                },
                new[]
                {
                    BasicAttackClipName,
                    SuperAttackClipName,
                    CarryPoseClipName,
                });
            ValidateAttackOverrides(controller);
        }

        static void ValidateAttackOverrides(AnimatorOverrideController controller)
        {
            if (controller == null)
                throw new InvalidOperationException("The Bastion override controller is missing.");

            var overrides = new List<KeyValuePair<AnimationClip, AnimationClip>>(
                controller.overridesCount);
            controller.GetOverrides(overrides);
            KeyValuePair<AnimationClip, AnimationClip>[] active = overrides
                .Where(value => value.Key != null && value.Value != null &&
                                value.Value != value.Key)
                .ToArray();
            KeyValuePair<AnimationClip, AnimationClip>[] basic = active
                .Where(value => string.Equals(
                    value.Key.name, BasicAttackOverrideSourceName, StringComparison.Ordinal))
                .ToArray();
            KeyValuePair<AnimationClip, AnimationClip>[] super = active
                .Where(value => string.Equals(
                    value.Key.name, SuperAttackOverrideSourceName, StringComparison.Ordinal))
                .ToArray();
            KeyValuePair<AnimationClip, AnimationClip>[] carry = active
                .Where(value => string.Equals(
                    value.Key.name, CarryPoseOverrideSourceName, StringComparison.Ordinal))
                .ToArray();
            if (active.Length != 3 || basic.Length != 1 || super.Length != 1 || carry.Length != 1 ||
                !string.Equals(
                    AssetDatabase.GetAssetPath(basic[0].Value),
                    BasicAttackClipPath,
                    StringComparison.Ordinal) ||
                !string.Equals(
                    AssetDatabase.GetAssetPath(super[0].Value),
                    SuperAttackClipPath,
                    StringComparison.Ordinal) ||
                !string.Equals(
                    AssetDatabase.GetAssetPath(carry[0].Value),
                    CarryPoseClipPath,
                    StringComparison.Ordinal) ||
                !string.Equals(
                    basic[0].Value.name, BasicAttackClipName, StringComparison.Ordinal) ||
                !string.Equals(
                    super[0].Value.name, SuperAttackClipName, StringComparison.Ordinal) ||
                !string.Equals(
                    carry[0].Value.name, CarryPoseClipName, StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    "The Bastion AOC must contain only WeakAttack_UnarmedA -> NormalAttack01_SwordShield, " +
                    "StrongAttack_PunchA -> NormalAttack02_SwordShield, and Idle@Pistol -> Idle_SwordShield.");
            }
        }

        static void ValidateProductionVariantSource(GameObject prefab, string expectedPath)
        {
            RequireAsset(prefab, expectedPath, "Bastion production variant");
            if (prefab.activeSelf || PrefabUtility.GetPrefabAssetType(prefab) != PrefabAssetType.Variant)
                throw new InvalidOperationException("A Bastion production variant must remain an inactive prefab variant.");

            GameObject source = PrefabUtility.GetCorrespondingObjectFromSource(prefab);
            if (source == null || !string.Equals(
                    AssetDatabase.GetAssetPath(source), PilotPrefabPath, StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    "A Bastion production variant must derive directly from the validated Bastion pilot.");
            }
        }

        static void RequireIdentity(GameObject prefab, InvectorBrawlerPrefabRole role)
        {
            InvectorBrawlerPrefabIdentity identity =
                RequireExactlyOneRoot<InvectorBrawlerPrefabIdentity>(prefab);
            if (!identity.Matches(RosterId, role))
                throw new InvalidOperationException("The Bastion production prefab identity changed.");
        }

        static void ValidateBastionVisuals(GameObject prefab)
        {
            GameObject bastion = AssetDatabase.LoadAssetAtPath<GameObject>(BastionPath);
            Transform expectedWeapon = FindDescendant(bastion.transform, AuthoredWeaponName);
            Renderer expectedWeaponRenderer = expectedWeapon != null
                ? expectedWeapon.GetComponent<Renderer>()
                : null;
            Transform presentation = FindDescendant(prefab.transform, WeaponPresentationName);
            // The shared builder always names the instantiated visual clone
            // "StaffVisual" regardless of weapon type; that literal lives in
            // InvectorMigrationPilotBuilder.BuildWeaponPresentationPrefabCore,
            // which this builder must not edit.
            Transform weaponVisual = presentation != null
                ? FindDescendant(presentation, "StaffVisual")
                : null;
            Renderer actualWeaponRenderer = weaponVisual != null
                ? weaponVisual.GetComponent<Renderer>()
                : null;
            Transform muzzle = presentation != null
                ? FindDescendant(presentation, "SpellOrigin")
                : null;
            ParticleSystem[] effects = presentation != null
                ? presentation.GetComponentsInChildren<ParticleSystem>(true)
                : Array.Empty<ParticleSystem>();

            bool weaponMatches = expectedWeaponRenderer != null && actualWeaponRenderer != null &&
                expectedWeaponRenderer.sharedMaterials.SequenceEqual(actualWeaponRenderer.sharedMaterials);
            Color actualMuzzleColor = effects.Length == 1
                ? effects[0].main.startColor.color
                : Color.clear;
            if (!weaponMatches || presentation == null ||
                FindDescendant(prefab.transform, AuthoredWeaponName) != null ||
                muzzle == null || muzzle.gameObject.layer != 12 ||
                FindDescendant(presentation, "SupportHandTarget") == null ||
                FindDescendant(presentation, "SupportHintTarget") == null ||
                effects.Length != 1 || effects[0].main.playOnAwake ||
                !Approximately(actualMuzzleColor, BastionMuzzleColor))
            {
                throw new InvalidOperationException(
                    "The Bastion BastionWarrior sword presentation, SpellOrigin, IK targets, or steel muzzle effect changed.");
            }
        }

        static bool Approximately(Color left, Color right)
        {
            return Mathf.Abs(left.r - right.r) < 0.01f &&
                   Mathf.Abs(left.g - right.g) < 0.01f &&
                   Mathf.Abs(left.b - right.b) < 0.01f &&
                   Mathf.Abs(left.a - right.a) < 0.01f;
        }

        static void AddSwordCategoryAlias()
        {
            vWeaponIKAdjust adjust = AssetDatabase.LoadAssetAtPath<vWeaponIKAdjust>(WeaponIKAdjustPath);
            if (adjust == null) return;
            if (!adjust.weaponCategories.Contains(WeaponCategory))
                adjust.weaponCategories.Add(WeaponCategory);
            EditorUtility.SetDirty(adjust);
        }

        static void FinalizeSwordCategory()
        {
            vWeaponIKAdjust adjust = AssetDatabase.LoadAssetAtPath<vWeaponIKAdjust>(WeaponIKAdjustPath);
            if (adjust == null) return;
            adjust.weaponCategories.Clear();
            adjust.weaponCategories.Add(WeaponCategory);
            EditorUtility.SetDirty(adjust);
        }

        static GameObject NormalizeSwordPresenter(GameObject prefab)
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
                Transform visual = FindDescendant(contents.transform, WeaponPresentationName);
                Transform muzzle = visual != null
                    ? FindDescendant(visual, "SpellOrigin")
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
                    AssetDatabase.LoadAssetAtPath<vWeaponIKAdjustList>(WeaponIKAdjustListPath);
                if (animator == null || controller == null || presenter == null ||
                    visual == null || muzzle == null || supportHand == null ||
                    supportHint == null || effects.Length != 1 || list == null)
                {
                    throw new InvalidOperationException(
                        "The assembled Bastion pilot lost its sword presentation references.");
                }

                presenter.Configure(
                    animator,
                    controller,
                    visual,
                    muzzle,
                    supportHand,
                    supportHint,
                    list,
                    WeaponCategory,
                    false,
                    effects);

                GameObject saved = PrefabUtility.SaveAsPrefabAsset(contents, path, out bool success);
                if (!success || saved == null)
                {
                    throw new InvalidOperationException(
                        "Unity failed to normalize the Bastion sword presenter.");
                }
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(contents);
            }
            return AssetDatabase.LoadAssetAtPath<GameObject>(path);
        }

        static void ValidateSwordIKData(vWeaponIKAdjustList list)
        {
            vWeaponIKAdjust adjust = list != null ? list.GetWeaponIK(WeaponCategory) : null;
            if (list == null || adjust == null ||
                list.weaponIKAdjusts.Count != 1 || list.weaponIKAdjusts[0] != adjust ||
                adjust.weaponCategories.Count != 1 ||
                !string.Equals(adjust.weaponCategories[0], WeaponCategory, StringComparison.Ordinal) ||
                list.GetWeaponIK(InvectorMigrationPilotBuilder.WeaponCategory) != null ||
                !adjust.HasAllDefaultStates() ||
                adjust.ikAdjustsLeft.Count != 4 || adjust.ikAdjustsRight.Count != 4 ||
                list.ikTargetPositionOffsetR != Vector3.zero || list.ikTargetRotationOffsetR != Vector3.zero ||
                list.ikTargetPositionOffsetL != Vector3.zero || list.ikTargetRotationOffsetL != Vector3.zero)
            {
                throw new InvalidOperationException(
                    "The Bastion IK list must contain one complete BrawlWarriorSword adjustment with neutral zero offsets.");
            }
        }

        static void CloseTransientPresentation(GameObject prefab)
        {
            if (prefab == null) return;
            InvectorBrawlerWeaponPresentation presenter =
                prefab.GetComponent<InvectorBrawlerWeaponPresentation>();
            if (presenter != null) presenter.DisableLabRuntime();
        }

        static void RequireGuid(string path, string expectedGuid, string label)
        {
            if (!string.Equals(
                    AssetDatabase.AssetPathToGUID(path), expectedGuid, StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    "The pinned " + label + " GUID changed. Re-audit Bastion before rebuilding.");
            }
        }

        static void RequireAsset(GameObject prefab, string expectedPath, string label)
        {
            if (prefab == null || !string.Equals(
                    AssetDatabase.GetAssetPath(prefab), expectedPath, StringComparison.Ordinal))
            {
                throw new InvalidOperationException(label + " is missing or saved at an unexpected path.");
            }
        }

        static T RequireExactlyOneRoot<T>(GameObject root) where T : Component
        {
            T[] components = root.GetComponentsInChildren<T>(true);
            if (components.Length != 1 || components[0].gameObject != root)
            {
                throw new InvalidOperationException(
                    "The Bastion prefab requires exactly one root " + typeof(T).Name + ".");
            }
            return components[0];
        }

        static Transform FindDescendant(Transform root, string name)
        {
            if (root == null) return null;
            return root.GetComponentsInChildren<Transform>(true)
                .FirstOrDefault(candidate =>
                    string.Equals(candidate.name, name, StringComparison.Ordinal));
        }

        static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;
            string normalized = path.Replace('\\', '/').TrimEnd('/');
            string parent = Path.GetDirectoryName(normalized)?.Replace('\\', '/');
            string name = Path.GetFileName(normalized);
            if (string.IsNullOrEmpty(parent) || string.IsNullOrEmpty(name))
                throw new InvalidOperationException("Invalid asset folder path: " + path);
            EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, name);
        }
    }
}
