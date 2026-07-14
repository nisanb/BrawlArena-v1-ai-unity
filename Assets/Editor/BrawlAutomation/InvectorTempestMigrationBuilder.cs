using System;
using System.IO;
using System.Linq;
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
    /// Builds Tempest from the pinned StormWizard source while reusing the
    /// roster-neutral production topology proven by Cinder and Rime. No lab or
    /// caller scene is opened, saved, closed, or reloaded by this builder.
    /// </summary>
    public static class InvectorTempestMigrationBuilder
    {
        public const string Root = "Assets/Generated/InvectorMigration/Tempest/";
        public const string OverrideControllerPath =
            Root + "Controllers/TempestInvectorPilot.overrideController";
        public const string PilotPrefabPath =
            Root + "Prefabs/TempestInvectorPilot.prefab";
        public const string ProductionHumanPrefabPath =
            Root + "Prefabs/TempestInvectorHuman.prefab";
        public const string ProductionAIPrefabPath =
            "Assets/BrawlArena/Prefabs/Invector/TempestInvectorAI.prefab";
        public const string WeaponPrefabPath =
            Root + "Weapons/TempestStaffPresentation.prefab";
        public const string WeaponIKAdjustPath =
            Root + "IK/TempestStaffIKAdjust.asset";
        public const string WeaponIKAdjustListPath =
            Root + "IK/TempestStaffIKAdjustList.asset";

        public const string StormPath =
            "Assets/Generated/Wizards/Prefabs/StormWizard.prefab";
        public const string StormGuid = "855345f398366284ca65b631d3d06fa3";
        public const string StormStaffMaterialPath =
            "Assets/Generated/Wizards/Materials/StormStaff.mat";
        public const string StormStaffMaterialGuid =
            "49a89e4a8a5891d4994ea6967581d10b";
        public const string StormBodyMaterialPath =
            "Assets/Generated/Wizards/Materials/StormBody.mat";
        public const string StormBodyMaterialGuid =
            "3ce61b2388af4ee4d8c2905c76d91384";
        public const string WizardAvatarPath =
            "Assets/WizardPBR/Mesh/WizardBodyMesh.fbx";
        public const string WizardAvatarGuid =
            "172414bf2ce653048b23105e793fff98";
        public const string RosterId = "storm";
        public const string AuthoredStaffName = "Staff03";
        public const string WeaponPresentationName = "TempestStaffPresentation";

        static readonly Color StormMuzzleColor =
            new Color(0xB5 / 255f, 0x8C / 255f, 1f, 1f);
        // Staff03's source bind pose is nearly fully extended. Pull both IK
        // targets 1 cm toward their shoulders so the guarded two-bone solver
        // stays inside its reach skin instead of failing at the exact limit.
        static readonly Vector3 WeaponHandIKPositionOffset =
            new Vector3(-0.01f, 0f, 0f);
        static readonly Vector3 SupportHandIKPositionOffset =
            new Vector3(0.01f, 0f, 0f);

        [MenuItem("Brawl Arena/Invector Migration/Build Tempest Variants")]
        static void BuildFromMenu()
        {
            Debug.Log(BuildTempestPilotAssetsSafely());
        }

        public static string BuildTempestPilotAssetsSafely()
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
                        "TempestInvectorPilot");

                InvectorMigrationPilotBuilder.BuildWeaponIKAssets(
                    WeaponIKAdjustPath,
                    WeaponIKAdjustListPath,
                    "TempestStaffIKAdjust",
                    "TempestStaffIKAdjustList",
                    WeaponHandIKPositionOffset,
                    SupportHandIKPositionOffset);
                InvectorMigrationPilotBuilder.BuildWeaponPresentationPrefab(
                    StormPath,
                    AuthoredStaffName,
                    WeaponPresentationName,
                    WeaponPrefabPath,
                    StormMuzzleColor);

                GameObject pilot = InvectorMigrationPilotBuilder.BuildPilotPrefab(
                    overrideController,
                    StormPath,
                    "TempestInvectorPilot_DISABLED",
                    AuthoredStaffName,
                    WeaponPrefabPath,
                    WeaponIKAdjustListPath,
                    WeaponPresentationName,
                    PilotPrefabPath);
                CloseTransientPresentation(pilot);
                ValidatePilot(pilot);

                GameObject human =
                    InvectorMigrationPilotBuilder.BuildProductionHumanPrefab(
                        pilot,
                        "TempestInvectorHuman_DISABLED",
                        ProductionHumanPrefabPath,
                        RosterId);
                CloseTransientPresentation(human);
                ValidateHumanPrefab(human);

                GameObject ai = InvectorMigrationPilotBuilder.BuildProductionAIPrefab(
                    pilot,
                    "TempestInvectorAI_DISABLED",
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
                return "Built the dormant Tempest Invector pilot plus inactive production-human and AI variants.";
            }
            finally
            {
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
            RequireGuid(StormPath, StormGuid, "StormWizard source");
            RequireGuid(
                StormStaffMaterialPath,
                StormStaffMaterialGuid,
                "StormStaff material");
            RequireGuid(
                StormBodyMaterialPath,
                StormBodyMaterialGuid,
                "StormBody material");
            RequireGuid(WizardAvatarPath, WizardAvatarGuid, "shared wizard Avatar");

            GameObject storm = AssetDatabase.LoadAssetAtPath<GameObject>(StormPath);
            Animator animator = storm != null ? storm.GetComponent<Animator>() : null;
            Transform staff = storm != null
                ? FindDescendant(storm.transform, AuthoredStaffName)
                : null;
            Transform spellOrigin = staff != null
                ? FindDescendant(staff, "SpellOrigin")
                : null;
            Renderer staffRenderer = staff != null
                ? staff.GetComponent<Renderer>()
                : null;
            Transform bodyTransform = storm != null
                ? FindDescendant(storm.transform, "WizardBody")
                : null;
            Renderer bodyRenderer = bodyTransform != null
                ? bodyTransform.GetComponent<Renderer>()
                : null;
            Material expectedStaff = AssetDatabase.LoadAssetAtPath<Material>(
                StormStaffMaterialPath);
            Material expectedBody = AssetDatabase.LoadAssetAtPath<Material>(
                StormBodyMaterialPath);

            if (storm == null || animator == null || !animator.isHuman ||
                animator.avatar == null || !animator.avatar.isValid ||
                !string.Equals(
                    AssetDatabase.GetAssetPath(animator.avatar),
                    WizardAvatarPath,
                    StringComparison.Ordinal) ||
                staff == null || !staff.gameObject.activeSelf || spellOrigin == null ||
                staffRenderer == null || staffRenderer.sharedMaterials.Length != 1 ||
                staffRenderer.sharedMaterial != expectedStaff ||
                bodyRenderer == null || bodyRenderer.sharedMaterials.Length != 1 ||
                bodyRenderer.sharedMaterial != expectedBody)
            {
                throw new InvalidOperationException(
                    "StormWizard must retain its pinned Humanoid Avatar, StormBody, and active Staff03/SpellOrigin presentation.");
            }
        }

        public static void ValidatePilot(GameObject prefab)
        {
            RequireAsset(prefab, PilotPrefabPath, "Tempest pilot");
            if (prefab.activeSelf ||
                prefab.layer != InvectorMigrationPilotBuilder.InvectorPlayerLayer ||
                prefab.tag != "Player")
            {
                throw new InvalidOperationException(
                    "The Tempest pilot root is not in its inactive selective-layer posture.");
            }

            GameObject source = PrefabUtility.GetCorrespondingObjectFromSource(prefab);
            if (source == null || !string.Equals(
                    AssetDatabase.GetAssetPath(source),
                    StormPath,
                    StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    "The Tempest pilot must derive directly from the pinned StormWizard source.");
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
                    "The Tempest pilot Animator is not using the Tempest override over the shared lifecycle controller.");
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
                    "The Tempest staff presenter or its isolated IK list is not dormant and pinned.");
            }

            ValidateTempestVisuals(prefab);
            if (prefab.GetComponentsInChildren<Health>(true).Length != 0 ||
                prefab.GetComponentsInChildren<BrawlerController>(true).Length != 0 ||
                prefab.GetComponentsInChildren<PlayerBrawlerInput>(true).Length != 0 ||
                prefab.GetComponentsInChildren<AIBrawler>(true).Length != 0 ||
                prefab.GetComponentsInChildren<NavMeshAgent>(true).Length != 0 ||
                prefab.GetComponentsInChildren<CharacterController>(true).Length != 0)
            {
                throw new InvalidOperationException(
                    "The dormant Tempest pilot contains a production facade, input, AI, or duplicate movement authority.");
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
            ValidateTempestVisuals(prefab);

            if (prefab.GetComponentsInChildren<AIBrawler>(true).Length != 0 ||
                prefab.GetComponentsInChildren<InvectorAIRuntimeGate>(true).Length != 0 ||
                prefab.GetComponentsInChildren<NavMeshAgent>(true).Length != 0 ||
                prefab.GetComponentsInChildren<CharacterController>(true).Length != 0)
            {
                throw new InvalidOperationException(
                    "The Tempest human variant contains an AI or duplicate movement authority.");
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
            ValidateTempestVisuals(prefab);

            NavMeshAgent[] agents = prefab.GetComponentsInChildren<NavMeshAgent>(true);
            if (agents.Length != 1 || agents[0].transform.parent != prefab.transform ||
                agents[0].enabled || agents[0].autoTraverseOffMeshLink ||
                !string.Equals(
                    agents[0].name,
                    InvectorMigrationPilotBuilder.ProductionAIPlannerName,
                    StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    "The Tempest AI variant does not contain one dormant child-only planner.");
            }

            if (prefab.GetComponentsInChildren<PlayerBrawlerInput>(true).Length != 0 ||
                prefab.GetComponentsInChildren<InvectorHumanRuntimeGate>(true).Length != 0 ||
                prefab.GetComponentsInChildren<CharacterController>(true).Length != 0 ||
                prefab.GetComponentsInChildren<Invector.vCharacterController.AI.vSimpleMeleeAI_Motor>(true).Length != 0)
            {
                throw new InvalidOperationException(
                    "The Tempest AI variant contains a human, CharacterController, or vendor AI authority.");
            }
        }

        static void ValidateProductionVariantSource(
            GameObject prefab,
            string expectedPath)
        {
            RequireAsset(prefab, expectedPath, "Tempest production variant");
            if (prefab.activeSelf ||
                PrefabUtility.GetPrefabAssetType(prefab) != PrefabAssetType.Variant)
            {
                throw new InvalidOperationException(
                    "A Tempest production variant must remain an inactive prefab variant.");
            }

            GameObject source = PrefabUtility.GetCorrespondingObjectFromSource(prefab);
            if (source == null || !string.Equals(
                    AssetDatabase.GetAssetPath(source),
                    PilotPrefabPath,
                    StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    "A Tempest production variant must derive directly from the validated Tempest pilot.");
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
                    "The Tempest production prefab identity changed.");
            }
        }

        static void ValidateTempestVisuals(GameObject prefab)
        {
            GameObject storm = AssetDatabase.LoadAssetAtPath<GameObject>(StormPath);
            Transform expectedBodyTransform = storm != null
                ? FindDescendant(storm.transform, "WizardBody")
                : null;
            Renderer expectedBody = expectedBodyTransform != null
                ? expectedBodyTransform.GetComponent<Renderer>()
                : null;
            Transform actualBodyTransform =
                FindDescendant(prefab.transform, "WizardBody");
            Renderer actualBody = actualBodyTransform != null
                ? actualBodyTransform.GetComponent<Renderer>()
                : null;
            Transform expectedStaff = storm != null
                ? FindDescendant(storm.transform, AuthoredStaffName)
                : null;
            Renderer expectedStaffRenderer = expectedStaff != null
                ? expectedStaff.GetComponent<Renderer>()
                : null;
            Transform presentation =
                FindDescendant(prefab.transform, WeaponPresentationName);
            Transform staffVisual = presentation != null
                ? FindDescendant(presentation, "StaffVisual")
                : null;
            Renderer actualStaffRenderer = staffVisual != null
                ? staffVisual.GetComponent<Renderer>()
                : null;
            Transform muzzle = presentation != null
                ? FindDescendant(presentation, "SpellOrigin")
                : null;
            Transform schoolAura = prefab.transform.Find("SchoolAura");
            ParticleSystem[] effects = presentation != null
                ? presentation.GetComponentsInChildren<ParticleSystem>(true)
                : Array.Empty<ParticleSystem>();

            bool bodyMatches = expectedBody != null && actualBody != null &&
                expectedBody.sharedMaterials.SequenceEqual(actualBody.sharedMaterials);
            bool staffMatches =
                expectedStaffRenderer != null && actualStaffRenderer != null &&
                expectedStaffRenderer.sharedMaterials.SequenceEqual(
                    actualStaffRenderer.sharedMaterials);
            Color actualMuzzleColor = effects.Length == 1
                ? effects[0].main.startColor.color
                : Color.clear;
            if (!bodyMatches || !staffMatches || presentation == null ||
                FindDescendant(prefab.transform, AuthoredStaffName) != null ||
                muzzle == null || muzzle.gameObject.layer != 12 ||
                FindDescendant(presentation, "SupportHandTarget") == null ||
                FindDescendant(presentation, "SupportHintTarget") == null ||
                schoolAura == null || schoolAura.gameObject.activeSelf ||
                effects.Length != 1 || effects[0].main.playOnAwake ||
                !Approximately(actualMuzzleColor, StormMuzzleColor))
            {
                throw new InvalidOperationException(
                    "The Tempest StormBody, Staff03 presentation, SpellOrigin, IK targets, disabled aura, or purple muzzle effect changed.");
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

        static void RequireGuid(string path, string expectedGuid, string label)
        {
            if (!string.Equals(
                    AssetDatabase.AssetPathToGUID(path),
                    expectedGuid,
                    StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    "The pinned " + label + " GUID changed. Re-audit Tempest before rebuilding.");
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
                    "The Tempest prefab requires exactly one root " +
                    typeof(T).Name + ".");
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
                throw new InvalidOperationException(
                    "Invalid asset folder path: " + path);
            EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, name);
        }
    }
}
