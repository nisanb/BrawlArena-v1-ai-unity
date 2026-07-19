using System;
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
    /// Builds Rime from the pinned FrostWizard source while reusing the proven
    /// project-owned Invector topology. No lab or caller scene is opened,
    /// saved, closed, or reloaded by this builder.
    /// </summary>
    public static class InvectorRimeMigrationBuilder
    {
        public const string Root = "Assets/Generated/InvectorMigration/Rime/";
        public const string OverrideControllerPath =
            Root + "Controllers/RimeInvectorPilot.overrideController";
        public const string PilotPrefabPath = Root + "Prefabs/RimeInvectorPilot.prefab";
        public const string ProductionHumanPrefabPath =
            Root + "Prefabs/RimeInvectorHuman.prefab";
        public const string ProductionAIPrefabPath =
            "Assets/BrawlArena/Prefabs/Invector/RimeInvectorAI.prefab";
        public const string WeaponPrefabPath =
            Root + "Weapons/RimeStaffPresentation.prefab";
        public const string WeaponIKAdjustPath = Root + "IK/RimeStaffIKAdjust.asset";
        public const string WeaponIKAdjustListPath = Root + "IK/RimeStaffIKAdjustList.asset";

        public const string FrostPath = "Assets/Generated/Wizards/Prefabs/FrostWizard.prefab";
        public const string FrostGuid = "a3aacd6b08313df408cb496bd9f1434b";
        public const string RosterId = "frost";
        public const string AuthoredStaffName = "Staff02";
        public const string WeaponPresentationName = "RimeStaffPresentation";

        static readonly Color FrostMuzzleColor = new Color(0x75 / 255f, 0xF0 / 255f, 1f, 1f);
        static readonly Vector3 WeaponHandIKPositionOffset =
            new Vector3(-22.731770f, -21.091560f, 7.045807f);
        static readonly Vector3 SupportTargetLocalPosition =
            new Vector3(11.038170000f, -5.882828000f, -8.197099000f);
        static readonly Vector3 SupportTargetLocalEuler =
            new Vector3(1.051649000f, 199.302300000f, 22.067450000f);
        static readonly Vector3 SupportHintLocalPosition =
            new Vector3(13.846820000f, -29.315950000f, 3.461801000f);
        static readonly Vector3 SupportHintLocalEuler =
            new Vector3(34.890250000f, 80.836430000f, 250.304900000f);
        static readonly Vector3 StaffVisualLocalPosition =
            new Vector3(8.895547000f, -3.444080000f, -0.427311900f);
        static readonly Vector3 StaffVisualLocalEuler =
            new Vector3(271.682000000f, 262.627100000f, 97.171430000f);

        [MenuItem("Brawl Arena/Invector Migration/Build Phase 4 Rime Variants")]
        static void BuildFromMenu()
        {
            Debug.Log(BuildRimePilotAssetsSafely());
        }

        public static string BuildRimePilotAssetsSafely()
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
                        "RimeInvectorPilot");
                InvectorMigrationPilotBuilder
                    .ConfigureWizardPresentationOverrides(overrideController);
                InvectorMigrationPilotBuilder
                    .ConfigureLocomotionOverrides(overrideController);

                InvectorMigrationPilotBuilder.BuildWeaponIKAssets(
                    WeaponIKAdjustPath,
                    WeaponIKAdjustListPath,
                    "RimeStaffIKAdjust",
                    "RimeStaffIKAdjustList",
                    WeaponHandIKPositionOffset,
                    Vector3.zero);
                InvectorMigrationPilotBuilder.BuildWeaponPresentationPrefab(
                    FrostPath,
                    AuthoredStaffName,
                    WeaponPresentationName,
                    WeaponPrefabPath,
                    FrostMuzzleColor,
                    SupportTargetLocalPosition,
                    SupportTargetLocalEuler,
                    SupportHintLocalPosition,
                    SupportHintLocalEuler,
                    StaffVisualLocalPosition,
                    StaffVisualLocalEuler);

                GameObject pilot = InvectorMigrationPilotBuilder.BuildPilotPrefab(
                    overrideController,
                    FrostPath,
                    "RimeInvectorPilot_DISABLED",
                    AuthoredStaffName,
                    WeaponPrefabPath,
                    WeaponIKAdjustListPath,
                    WeaponPresentationName,
                    PilotPrefabPath);
                CloseTransientPresentation(pilot);
                ValidatePilot(pilot);

                GameObject human = InvectorMigrationPilotBuilder.BuildProductionHumanPrefab(
                    pilot,
                    "RimeInvectorHuman_DISABLED",
                    ProductionHumanPrefabPath,
                    RosterId);
                CloseTransientPresentation(human);
                ValidateHumanPrefab(human);

                GameObject ai = InvectorMigrationPilotBuilder.BuildProductionAIPrefab(
                    pilot,
                    "RimeInvectorAI_DISABLED",
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
                return "Built the dormant Rime Invector pilot plus inactive production-human and AI variants.";
            }
            finally
            {
                if (originalSceneWasDirty && originalScene.IsValid() && originalScene.isLoaded)
                    EditorSceneManager.MarkSceneDirty(originalScene);
            }
        }

        public static void ValidatePrerequisites()
        {
            InvectorMigrationPilotBuilder.ValidatePrerequisites();
            if (!string.Equals(
                    AssetDatabase.AssetPathToGUID(FrostPath), FrostGuid, StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    "The pinned FrostWizard source GUID changed. Re-audit Rime before rebuilding.");
            }

            GameObject frost = AssetDatabase.LoadAssetAtPath<GameObject>(FrostPath);
            Animator animator = frost != null ? frost.GetComponent<Animator>() : null;
            Transform staff = frost != null
                ? FindDescendant(frost.transform, AuthoredStaffName)
                : null;
            if (frost == null || animator == null || !animator.isHuman ||
                animator.avatar == null || !animator.avatar.isValid || staff == null ||
                FindDescendant(staff, "SpellOrigin") == null)
            {
                throw new InvalidOperationException(
                    "FrostWizard must retain its valid Humanoid Avatar and Staff02/SpellOrigin hierarchy.");
            }
        }

        public static void ValidatePilot(GameObject prefab)
        {
            RequireAsset(prefab, PilotPrefabPath, "Rime pilot");
            if (prefab.activeSelf || prefab.layer != InvectorMigrationPilotBuilder.InvectorPlayerLayer ||
                prefab.tag != "Player")
            {
                throw new InvalidOperationException(
                    "The Rime pilot root is not in its inactive selective-layer posture.");
            }

            GameObject source = PrefabUtility.GetCorrespondingObjectFromSource(prefab);
            if (source == null || !string.Equals(
                    AssetDatabase.GetAssetPath(source), FrostPath, StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    "The Rime pilot must derive directly from the pinned FrostWizard source.");
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
                    "The Rime pilot Animator is not using the Rime override over the shared lifecycle controller.");
            }

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
                    AssetDatabase.GetAssetPath(presenter.ProjectIKAdjustList),
                    WeaponIKAdjustListPath,
                    StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    "The Rime staff presenter or its isolated IK list is not dormant and pinned.");
            }

            ValidateRimeVisuals(prefab);
            if (prefab.GetComponentsInChildren<Health>(true).Length != 0 ||
                prefab.GetComponentsInChildren<BrawlerController>(true).Length != 0 ||
                prefab.GetComponentsInChildren<PlayerBrawlerInput>(true).Length != 0 ||
                prefab.GetComponentsInChildren<AIBrawler>(true).Length != 0 ||
                prefab.GetComponentsInChildren<NavMeshAgent>(true).Length != 0 ||
                prefab.GetComponentsInChildren<CharacterController>(true).Length != 0)
            {
                throw new InvalidOperationException(
                    "The dormant Rime pilot contains a production facade, input, AI, or duplicate movement authority.");
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
            ValidateRimeVisuals(prefab);

            if (prefab.GetComponentsInChildren<AIBrawler>(true).Length != 0 ||
                prefab.GetComponentsInChildren<InvectorAIRuntimeGate>(true).Length != 0 ||
                prefab.GetComponentsInChildren<NavMeshAgent>(true).Length != 0 ||
                prefab.GetComponentsInChildren<CharacterController>(true).Length != 0)
            {
                throw new InvalidOperationException(
                    "The Rime human variant contains an AI or duplicate movement authority.");
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
            ValidateRimeVisuals(prefab);

            NavMeshAgent[] agents = prefab.GetComponentsInChildren<NavMeshAgent>(true);
            if (agents.Length != 1 || agents[0].transform.parent != prefab.transform ||
                agents[0].enabled || agents[0].autoTraverseOffMeshLink ||
                !string.Equals(
                    agents[0].name,
                    InvectorMigrationPilotBuilder.ProductionAIPlannerName,
                    StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    "The Rime AI variant does not contain one dormant child-only planner.");
            }

            if (prefab.GetComponentsInChildren<PlayerBrawlerInput>(true).Length != 0 ||
                prefab.GetComponentsInChildren<InvectorHumanRuntimeGate>(true).Length != 0 ||
                prefab.GetComponentsInChildren<CharacterController>(true).Length != 0 ||
                prefab.GetComponentsInChildren<Invector.vCharacterController.AI.vSimpleMeleeAI_Motor>(true).Length != 0)
            {
                throw new InvalidOperationException(
                    "The Rime AI variant contains a human, CharacterController, or vendor AI authority.");
            }
        }

        static void ValidateProductionVariantSource(GameObject prefab, string expectedPath)
        {
            RequireAsset(prefab, expectedPath, "Rime production variant");
            if (prefab.activeSelf || PrefabUtility.GetPrefabAssetType(prefab) != PrefabAssetType.Variant)
                throw new InvalidOperationException("A Rime production variant must remain an inactive prefab variant.");

            GameObject source = PrefabUtility.GetCorrespondingObjectFromSource(prefab);
            if (source == null || !string.Equals(
                    AssetDatabase.GetAssetPath(source), PilotPrefabPath, StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    "A Rime production variant must derive directly from the validated Rime pilot.");
            }
        }

        static void RequireIdentity(GameObject prefab, InvectorBrawlerPrefabRole role)
        {
            InvectorBrawlerPrefabIdentity identity =
                RequireExactlyOneRoot<InvectorBrawlerPrefabIdentity>(prefab);
            if (!identity.Matches(RosterId, role))
                throw new InvalidOperationException("The Rime production prefab identity changed.");
        }

        static void ValidateRimeVisuals(GameObject prefab)
        {
            GameObject frost = AssetDatabase.LoadAssetAtPath<GameObject>(FrostPath);
            Renderer expectedBody = FindDescendant(frost.transform, "WizardBody")
                .GetComponent<Renderer>();
            Renderer actualBody = FindDescendant(prefab.transform, "WizardBody")
                .GetComponent<Renderer>();
            Transform expectedStaff = FindDescendant(frost.transform, AuthoredStaffName);
            Renderer expectedStaffRenderer = expectedStaff.GetComponent<Renderer>();
            Transform presentation = FindDescendant(prefab.transform, WeaponPresentationName);
            Transform staffVisual = presentation != null
                ? FindDescendant(presentation, "StaffVisual")
                : null;
            Renderer actualStaffRenderer = staffVisual != null
                ? staffVisual.GetComponent<Renderer>()
                : null;
            Transform muzzle = presentation != null
                ? FindDescendant(presentation, "SpellOrigin")
                : null;
            ParticleSystem[] effects = presentation != null
                ? presentation.GetComponentsInChildren<ParticleSystem>(true)
                : Array.Empty<ParticleSystem>();

            bool bodyMatches = expectedBody != null && actualBody != null &&
                expectedBody.sharedMaterials.SequenceEqual(actualBody.sharedMaterials);
            bool staffMatches = expectedStaffRenderer != null && actualStaffRenderer != null &&
                expectedStaffRenderer.sharedMaterials.SequenceEqual(actualStaffRenderer.sharedMaterials);
            Color actualMuzzleColor = effects.Length == 1
                ? effects[0].main.startColor.color
                : Color.clear;
            if (!bodyMatches || !staffMatches || presentation == null ||
                FindDescendant(prefab.transform, AuthoredStaffName) != null ||
                muzzle == null || muzzle.gameObject.layer != 12 ||
                FindDescendant(presentation, "SupportHandTarget") == null ||
                FindDescendant(presentation, "SupportHintTarget") == null ||
                effects.Length != 1 || effects[0].main.playOnAwake ||
                !Approximately(actualMuzzleColor, FrostMuzzleColor))
            {
                throw new InvalidOperationException(
                    "The Rime FrostBody, Staff02 presentation, SpellOrigin, IK targets, or cyan muzzle effect changed.");
            }
        }

        static bool Approximately(Color left, Color right)
        {
            return Mathf.Abs(left.r - right.r) < 0.01f &&
                   Mathf.Abs(left.g - right.g) < 0.01f &&
                   Mathf.Abs(left.b - right.b) < 0.01f &&
                   Mathf.Abs(left.a - right.a) < 0.01f;
        }

        static void CloseTransientPresentation(GameObject prefab)
        {
            if (prefab == null) return;
            InvectorBrawlerWeaponPresentation presenter =
                prefab.GetComponent<InvectorBrawlerWeaponPresentation>();
            if (presenter != null) presenter.DisableLabRuntime();
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
                    "The Rime prefab requires exactly one root " + typeof(T).Name + ".");
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
