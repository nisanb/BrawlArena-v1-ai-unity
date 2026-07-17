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
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;

namespace BrawlArena.EditorAutomation
{
    /// <summary>
    /// Builds the dormant Cinder pilot, its inactive production-human and AI
    /// variants, and the isolated Phase 3B runtime lab. Production selection
    /// remains an explicit GameFlow context gate and is disabled in the
    /// generated Arena.
    /// </summary>
    public static class InvectorMigrationPilotBuilder
    {
        public const string Root = "Assets/Generated/InvectorMigration/Cinder/";
        public const string LifecycleControllerPath =
            Root + "Controllers/CinderInvectorPilot.controller";
        public const string OverrideControllerPath = Root + "Controllers/CinderInvectorPilot.overrideController";
        public const string PrefabPath = Root + "Prefabs/CinderInvectorPilot.prefab";
        public const string ProductionHumanPrefabPath =
            Root + "Prefabs/CinderInvectorHuman.prefab";
        public const string ProductionAIPrefabPath =
            "Assets/BrawlArena/Prefabs/Invector/CinderInvectorAI.prefab";
        public const string ProductionAIPlannerName =
            "InvectorNavigationPlanner_DISABLED";
        public const string WeaponPrefabPath = Root + "Weapons/CinderStaffPresentation.prefab";
        public const string WeaponIKAdjustPath = Root + "IK/CinderStaffIKAdjust.asset";
        public const string WeaponIKAdjustListPath = Root + "IK/CinderStaffIKAdjustList.asset";
        public const string ScenePath = "Assets/Scenes/InvectorMigrationLab.unity";
        public const string WeaponCategory = "BrawlWizardStaff";

        public const string TemplatePath =
            "Assets/Invector-3rdPersonController/Shooter/Prefabs/Player/vShooterMelee_NoInventory.prefab";
        public const string CombinedControllerPath =
            "Assets/Invector-3rdPersonController/Shooter/Animator/Invector@ShooterMelee.controller";
        public const string CinderPath = "Assets/Generated/Wizards/Prefabs/FireWizard.prefab";
        public const string DeathClipPath = "Assets/Generated/Wizards/Clips/Die.anim";
        public const string VictoryClipPath = "Assets/Generated/Wizards/Clips/VictoryStart.anim";
        public const string ProjectActionsPath = "Assets/InputSystem_Actions.inputactions";

        public const string TemplateGuid = "80dd0462ab7502b48a7fe99ea1cd882a";
        public const string CombinedControllerGuid = "87885946b43e2d1449e1d5aa2042f8a8";
        public const string CinderGuid = "dbddf6f451e7c90449940027455ee166";
        public const string DeathClipGuid = "0cf8fc0f929385941b5832a35cb74630";
        public const string VictoryClipGuid = "e4715cf696aba3649b0e9624be8bbc1f";
        public const string ProjectActionsGuid = "052faaac586de48259a63d0c4782560b";
        public const int InvectorPlayerLayer = 23;
        public const string InvectorPlayerLayerName = "InvectorPlayer";

        static readonly Type[] SourceRootComponentTypes =
        {
            typeof(Rigidbody),
            typeof(CapsuleCollider),
            typeof(vThirdPersonController),
            typeof(vShooterManager),
            typeof(vAmmoManager),
            typeof(vMeleeManager),
            typeof(vCollectShooterMeleeControl),
            typeof(vShooterMeleeInput),
        };

        static readonly Type[] RetainedRootBehaviourTypes =
        {
            typeof(BrawlInvectorThirdPersonController),
            typeof(vShooterManager),
            typeof(vAmmoManager),
            typeof(BrawlInvectorMeleePresentationManager),
            typeof(vCollectShooterMeleeControl),
            typeof(InvectorShooterMeleeInputAdapter),
            typeof(InvectorBrawlerMotor),
            typeof(InvectorBrawlerAnimationDriver),
            typeof(InvectorBrawlerWeaponPresentation),
        };

        static readonly string[] CombinedLayerNames =
        {
            "Base Layer", "RightArm", "LeftArm", "OnlyArms",
            "UpperBody", "UnderBody", "Shot", "FullBody",
        };

        [MenuItem("Brawl Arena/Invector Migration/Build Phase 3B Cinder Lab")]
        static void BuildFromMenu()
        {
            Debug.Log(BuildPilotAssets());
        }

        [MenuItem("Brawl Arena/Invector Migration/Build Phase 3G Cinder AI Variant")]
        static void BuildProductionAIFromMenu()
        {
            Debug.Log(BuildProductionAIPrefabSafely());
        }

        public static string BuildPilotAssets()
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

                AnimatorController lifecycleController = BuildLifecycleController();
                AnimatorOverrideController overrideController =
                    BuildOverrideController(lifecycleController);
                ConfigureWizardPresentationOverrides(overrideController);
                BuildWeaponIKAssets();
                BuildWeaponPresentationPrefab();
                GameObject prefab = BuildPilotPrefab(overrideController);
                prefab.GetComponent<InvectorBrawlerWeaponPresentation>()
                    .DisableLabRuntime();
                ValidateGeneratedPrefab(prefab);

                GameObject productionHumanPrefab =
                    BuildProductionHumanPrefab(prefab);
                productionHumanPrefab.GetComponent<InvectorBrawlerWeaponPresentation>()
                    .DisableLabRuntime();
                ValidateProductionHumanPrefab(productionHumanPrefab);

                GameObject productionAIPrefab =
                    BuildProductionAIPrefab(prefab);
                productionAIPrefab.GetComponent<InvectorBrawlerWeaponPresentation>()
                    .DisableLabRuntime();
                ValidateProductionAIPrefab(productionAIPrefab);

                // The Phase 3B lab is edit-time authoring: EditorSceneManager
                // cannot create or save scenes during play mode, so play-mode
                // roster rebuilds keep the existing lab scene asset.
                if (Application.isPlaying)
                    Debug.Log("[InvectorMigrationPilotBuilder] play mode: kept the existing Phase 3B lab scene.");
                else
                    BuildLabScene(prefab);

                // Saving/instantiating a prefab can leave nonserialized managed
                // helper references on Unity's in-memory persistent object. They
                // are not asset data, but normalize that cache before auditing
                // the dormant prefab and before a second idempotence build.
                prefab.GetComponent<InvectorBrawlerWeaponPresentation>()
                    .DisableLabRuntime();

                AssetDatabase.SaveAssets();
                ValidateGeneratedPrefab(prefab);
                ValidateProductionHumanPrefab(productionHumanPrefab);
                ValidateProductionAIPrefab(productionAIPrefab);
                return "Built the dormant Cinder/Invector pilot, inactive production-human and AI variants, and isolated Phase 3B live scheduler lab.";
            }
            finally
            {
                if (originalSceneWasDirty && originalScene.IsValid() && originalScene.isLoaded)
                    EditorSceneManager.MarkSceneDirty(originalScene);
            }
        }

        /// <summary>
        /// Rebuilds only the Phase 3G AI variant from the existing validated
        /// pilot. The operation uses a preview scene and never changes, saves,
        /// closes, or reloads the caller's active scene.
        /// </summary>
        public static string BuildProductionAIPrefabSafely()
        {
            Scene originalScene = SceneManager.GetActiveScene();
            bool originalSceneWasDirty = originalScene.IsValid() && originalScene.isDirty;
            try
            {
                ValidatePrerequisites();
                EnsureFolder("Assets/BrawlArena/Prefabs/Invector");

                GameObject pilotPrefab =
                    AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
                if (pilotPrefab == null)
                {
                    throw new InvalidOperationException(
                        "Build and validate the dormant Cinder pilot before the Phase 3G AI variant.");
                }

                // Editor tests can leave non-serialized IK solver objects on
                // the loaded prefab representation even though the asset is
                // dormant. Close that transient presentation state before the
                // source audit; this does not modify serialized prefab data.
                pilotPrefab.GetComponent<InvectorBrawlerWeaponPresentation>()
                    .DisableLabRuntime();
                ValidateGeneratedPrefab(pilotPrefab);
                GameObject productionAIPrefab =
                    BuildProductionAIPrefab(pilotPrefab);
                productionAIPrefab.GetComponent<InvectorBrawlerWeaponPresentation>()
                    .DisableLabRuntime();
                ValidateProductionAIPrefab(productionAIPrefab);
                AssetDatabase.SaveAssets();
                ValidateProductionAIPrefab(productionAIPrefab);
                return "Built the inactive Phase 3G Cinder Invector AI prefab variant.";
            }
            finally
            {
                if (originalSceneWasDirty && originalScene.IsValid() && originalScene.isLoaded)
                    EditorSceneManager.MarkSceneDirty(originalScene);
            }
        }

        static void BuildWeaponIKAssets()
        {
            BuildWeaponIKAssets(
                WeaponIKAdjustPath,
                WeaponIKAdjustListPath,
                "CinderStaffIKAdjust",
                "CinderStaffIKAdjustList");
        }

        internal static void BuildWeaponIKAssets(
            string adjustPath,
            string adjustListPath,
            string adjustName,
            string adjustListName)
        {
            BuildWeaponIKAssets(
                adjustPath,
                adjustListPath,
                adjustName,
                adjustListName,
                Vector3.zero,
                Vector3.zero);
        }

        internal static void BuildWeaponIKAssets(
            string adjustPath,
            string adjustListPath,
            string adjustName,
            string adjustListName,
            Vector3 weaponHandPositionOffset,
            Vector3 supportHandPositionOffset)
        {
            vWeaponIKAdjust adjust =
                AssetDatabase.LoadAssetAtPath<vWeaponIKAdjust>(adjustPath);
            if (adjust == null)
            {
                adjust = ScriptableObject.CreateInstance<vWeaponIKAdjust>();
                adjust.name = adjustName;
                AssetDatabase.CreateAsset(adjust, adjustPath);
            }

            adjust.weaponCategories.Clear();
            adjust.weaponCategories.Add(WeaponCategory);
            ReplaceDefaultIKStates(
                adjust.ikAdjustsLeft,
                weaponHandPositionOffset,
                supportHandPositionOffset);
            ReplaceDefaultIKStates(
                adjust.ikAdjustsRight,
                weaponHandPositionOffset,
                supportHandPositionOffset);
            EditorUtility.SetDirty(adjust);

            vWeaponIKAdjustList list =
                AssetDatabase.LoadAssetAtPath<vWeaponIKAdjustList>(adjustListPath);
            if (list == null)
            {
                list = ScriptableObject.CreateInstance<vWeaponIKAdjustList>();
                list.name = adjustListName;
                AssetDatabase.CreateAsset(list, adjustListPath);
            }

            list.ikTargetPositionOffsetR = Vector3.zero;
            list.ikTargetRotationOffsetR = Vector3.zero;
            list.ikTargetPositionOffsetL = Vector3.zero;
            list.ikTargetRotationOffsetL = Vector3.zero;
            list.weaponIKAdjusts.Clear();
            list.weaponIKAdjusts.Add(adjust);
            EditorUtility.SetDirty(list);
        }

        static void ReplaceDefaultIKStates(
            List<IKAdjust> states,
            Vector3 weaponHandPositionOffset,
            Vector3 supportHandPositionOffset)
        {
            states.Clear();
            AddIKState(states, vWeaponIKAdjust.StandingState,
                weaponHandPositionOffset, supportHandPositionOffset);
            AddIKState(states, vWeaponIKAdjust.StandingAimingState,
                weaponHandPositionOffset, supportHandPositionOffset);
            AddIKState(states, vWeaponIKAdjust.CrouchingState,
                weaponHandPositionOffset, supportHandPositionOffset);
            AddIKState(states, vWeaponIKAdjust.CrouchingAimingState,
                weaponHandPositionOffset, supportHandPositionOffset);
        }

        static void AddIKState(
            List<IKAdjust> states,
            string stateName,
            Vector3 weaponHandPositionOffset,
            Vector3 supportHandPositionOffset)
        {
            var state = new IKAdjust(stateName);
            state.weaponHandOffset.position = weaponHandPositionOffset;
            state.supportHandOffset.position = supportHandPositionOffset;
            states.Add(state);
        }

        static void BuildWeaponPresentationPrefab()
        {
            BuildWeaponPresentationPrefab(
                CinderPath,
                "Staff01",
                "CinderStaffPresentation",
                WeaponPrefabPath,
                new Color(1f, 0.48f, 0.08f, 1f));
        }

        internal static void BuildWeaponPresentationPrefab(
            string characterPath,
            string authoredStaffName,
            string presentationName,
            string destinationPath,
            Color muzzleColor)
        {
            GameObject character = AssetDatabase.LoadAssetAtPath<GameObject>(characterPath);
            Scene previewScene = EditorSceneManager.NewPreviewScene();
            try
            {
                var instance = (GameObject)PrefabUtility.InstantiatePrefab(character, previewScene);
                Animator animator = instance.GetComponent<Animator>();
                Transform staff = FindDescendant(instance.transform, authoredStaffName);
                Transform leftHand = animator != null && animator.isHuman
                    ? animator.GetBoneTransform(HumanBodyBones.LeftHand)
                    : null;
                Transform leftLowerArm = animator != null && animator.isHuman
                    ? animator.GetBoneTransform(HumanBodyBones.LeftLowerArm)
                    : null;
                if (staff == null || staff.parent == null || leftHand == null || leftLowerArm == null)
                {
                    throw new InvalidOperationException(
                        characterPath + " must expose " + authoredStaffName +
                        " plus a valid Humanoid left arm for its weapon presentation asset.");
                }

                var weaponRoot = new GameObject(presentationName);
                SceneManager.MoveGameObjectToScene(weaponRoot, previewScene);
                weaponRoot.transform.SetParent(staff.parent, false);
                weaponRoot.layer = 0;

                GameObject staffVisual = UnityEngine.Object.Instantiate(
                    staff.gameObject, weaponRoot.transform, false);
                staffVisual.name = "StaffVisual";
                SetLayerRecursively(staffVisual, 0);
                staffVisual.SetActive(true);

                Transform muzzle = FindDescendant(staffVisual.transform, "SpellOrigin");
                if (muzzle == null)
                    throw new InvalidOperationException(
                        "The " + authoredStaffName + " visual lost its SpellOrigin socket.");
                muzzle.gameObject.layer = 12;

                Transform supportHand = CreateAnchor(
                    weaponRoot.transform, "SupportHandTarget", leftHand.position, leftHand.rotation);
                Transform supportHint = CreateAnchor(
                    weaponRoot.transform, "SupportHintTarget", leftLowerArm.position, leftLowerArm.rotation);
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
                main.startSpeed = 1.4f;
                main.startSize = 0.13f;
                main.startColor = muzzleColor;
                main.maxParticles = 12;
                var emission = particles.emission;
                emission.rateOverTime = 0f;
                emission.SetBursts(new[] { new ParticleSystem.Burst(0f, 8) });
                var shape = particles.shape;
                shape.shapeType = ParticleSystemShapeType.Cone;
                shape.angle = 14f;
                shape.radius = 0.015f;
                particles.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

                GameObject saved = PrefabUtility.SaveAsPrefabAsset(
                    weaponRoot, destinationPath, out bool success);
                if (!success || saved == null)
                    throw new InvalidOperationException("Unity failed to save " + destinationPath + ".");
            }
            finally
            {
                EditorSceneManager.ClosePreviewScene(previewScene);
            }
        }

        static Transform CreateAnchor(
            Transform parent,
            string name,
            Vector3 worldPosition,
            Quaternion worldRotation)
        {
            var anchor = new GameObject(name).transform;
            anchor.SetParent(parent, false);
            anchor.position = worldPosition;
            anchor.rotation = worldRotation;
            return anchor;
        }

        public static void ValidatePrerequisites()
        {
            RequireGuid(TemplatePath, TemplateGuid, "Invector no-inventory template");
            RequireGuid(CombinedControllerPath, CombinedControllerGuid, "combined Animator controller");
            RequireGuid(CinderPath, CinderGuid, "generated Cinder prefab");
            RequireGuid(DeathClipPath, DeathClipGuid, "generated Cinder death clip");
            RequireGuid(VictoryClipPath, VictoryClipGuid, "generated Cinder victory clip");
            RequireGuid(ProjectActionsPath, ProjectActionsGuid, "project Input Action asset");

            RequireLayer(8, "Ground");
            RequireLayer(9, "WorldBlocker");
            RequireLayer(10, "BrawlerHitbox");
            RequireLayer(11, "Projectile");
            RequireLayer(12, "VFX");
            RequireLayer(InvectorPlayerLayer, InvectorPlayerLayerName);
            if (!string.IsNullOrEmpty(LayerMask.LayerToName(13)) ||
                !string.IsNullOrEmpty(LayerMask.LayerToName(15)))
            {
                throw new InvalidOperationException(
                    "Phase 3A requires ambiguous upstream layers 13 and 15 to remain unnamed.");
            }

            var template = AssetDatabase.LoadAssetAtPath<GameObject>(TemplatePath);
            var cinder = AssetDatabase.LoadAssetAtPath<GameObject>(CinderPath);
            var combined = AssetDatabase.LoadAssetAtPath<AnimatorController>(CombinedControllerPath);
            var deathClip = AssetDatabase.LoadAssetAtPath<AnimationClip>(DeathClipPath);
            var victoryClip = AssetDatabase.LoadAssetAtPath<AnimationClip>(VictoryClipPath);
            var inputActions = AssetDatabase.LoadAssetAtPath<InputActionAsset>(ProjectActionsPath);
            if (template == null || cinder == null || combined == null || deathClip == null ||
                victoryClip == null || inputActions == null)
                throw new InvalidOperationException("A required Phase 3B source asset could not be loaded.");
            InputAction moveAction = inputActions.FindAction("Player/Move", false);
            if (moveAction == null || moveAction.type != InputActionType.Value ||
                !string.Equals(moveAction.expectedControlType, "Vector2", StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    "Phase 3B requires the project Player/Move Vector2 Input Action.");
            }

            foreach (Type type in SourceRootComponentTypes)
            {
                if (template.GetComponent(type) == null)
                    throw new InvalidOperationException("Template root no longer contains " + type.FullName + ".");
            }

            Animator cinderAnimator = cinder.GetComponent<Animator>();
            if (cinderAnimator == null || cinderAnimator.avatar == null ||
                !cinderAnimator.avatar.isValid || !cinderAnimator.avatar.isHuman)
            {
                throw new InvalidOperationException("Cinder must retain one valid Humanoid Avatar.");
            }

            if (combined.layers.Length != CombinedLayerNames.Length ||
                !combined.layers.Select(layer => layer.name).SequenceEqual(CombinedLayerNames) ||
                combined.parameters.Length != 44)
            {
                throw new InvalidOperationException(
                    "The Invector combined Animator graph signature changed; re-audit before rebuilding.");
            }

            ValidateLifecycleClip(deathClip, "death");
            ValidateLifecycleClip(victoryClip, "victory");
        }

        static void ValidateLifecycleClip(AnimationClip clip, string label)
        {
            if (clip == null || clip.legacy || !clip.humanMotion || clip.isLooping ||
                clip.wrapMode != WrapMode.Once ||
                AnimationUtility.GetAnimationClipSettings(clip).loopTime ||
                AnimationUtility.GetAnimationEvents(clip).Length != 0)
            {
                throw new InvalidOperationException(
                    "The generated Cinder " + label +
                    " clip must remain Humanoid, non-looping, and event-free.");
            }

            var serialized = new SerializedObject(clip);
            SerializedProperty settings = serialized.FindProperty("m_AnimationClipSettings");
            SerializedProperty genericRoot = serialized.FindProperty("m_HasGenericRootTransform");
            SerializedProperty motionFloats = serialized.FindProperty("m_HasMotionFloatCurves");
            if (settings == null || genericRoot == null || motionFloats == null ||
                !settings.FindPropertyRelative("m_KeepOriginalOrientation").boolValue ||
                !settings.FindPropertyRelative("m_KeepOriginalPositionY").boolValue ||
                !settings.FindPropertyRelative("m_KeepOriginalPositionXZ").boolValue ||
                genericRoot.boolValue || motionFloats.boolValue)
            {
                throw new InvalidOperationException(
                    "The generated Cinder " + label +
                    " clip must retain baked root orientation and position settings.");
            }
        }

        public static void ValidateGeneratedLifecycleController(AnimatorController controller)
        {
            if (controller == null ||
                !string.Equals(
                    AssetDatabase.GetAssetPath(controller),
                    LifecycleControllerPath,
                    StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    "The generated lifecycle controller must use the pinned project-owned path.");
            }

            var vendor = AssetDatabase.LoadAssetAtPath<AnimatorController>(CombinedControllerPath);
            if (vendor == null || controller == vendor)
                throw new InvalidOperationException("The lifecycle graph must not mutate the vendor controller.");
            if (controller.layers.Length != CombinedLayerNames.Length ||
                !controller.layers.Select(layer => layer.name).SequenceEqual(CombinedLayerNames))
            {
                throw new InvalidOperationException(
                    "The generated lifecycle controller no longer retains the vendor layer signature.");
            }

            AnimatorControllerParameter[] generatedParameters = controller.parameters;
            if (generatedParameters.Length != vendor.parameters.Length + 3)
                throw new InvalidOperationException("The lifecycle controller must contain exactly 47 parameters.");
            foreach (AnimatorControllerParameter vendorParameter in vendor.parameters)
            {
                AnimatorControllerParameter[] matches = generatedParameters
                    .Where(parameter => parameter.name == vendorParameter.name)
                    .ToArray();
                if (matches.Length != 1 || !ParametersMatch(matches[0], vendorParameter))
                {
                    throw new InvalidOperationException(
                        "Vendor Animator parameter changed in the lifecycle copy: " + vendorParameter.name + ".");
                }
            }

            ValidateLifecycleTriggerParameter(
                generatedParameters,
                BrawlInvectorLifecycleParameters.DeathTriggerName);
            ValidateLifecycleTriggerParameter(
                generatedParameters,
                BrawlInvectorLifecycleParameters.RespawnTriggerName);
            ValidateLifecycleTriggerParameter(
                generatedParameters,
                BrawlInvectorLifecycleParameters.VictoryTriggerName);

            AnimatorStateMachine fullBody = controller.layers
                .Single(layer => layer.name == "FullBody").stateMachine;
            ChildAnimatorStateMachine[] lifecycleMatches = fullBody.stateMachines
                .Where(child => child.stateMachine.name == BrawlInvectorLifecycleParameters.StateMachineName)
                .ToArray();
            if (lifecycleMatches.Length != 1)
                throw new InvalidOperationException("The FullBody layer must contain one BrawlLifecycle machine.");

            AnimatorStateMachine lifecycle = lifecycleMatches[0].stateMachine;
            if (lifecycle.stateMachines.Length != 0 || lifecycle.anyStateTransitions.Length != 0 ||
                lifecycle.entryTransitions.Length != 0 || lifecycle.behaviours.Length != 0 ||
                fullBody.GetStateMachineTransitions(lifecycle).Length != 0)
            {
                throw new InvalidOperationException(
                    "The project lifecycle machine contains an unexpected nested graph or behaviour.");
            }
            if (lifecycle.states.Length != 3)
                throw new InvalidOperationException("The project lifecycle machine must contain exactly three states.");

            AnimatorState death = ValidateLifecycleState(
                lifecycle,
                BrawlInvectorLifecycleParameters.DeathStateName,
                AssetDatabase.LoadAssetAtPath<AnimationClip>(DeathClipPath),
                BrawlInvectorLifecyclePresentation.Death,
                false);
            AnimatorState respawn = ValidateLifecycleState(
                lifecycle,
                BrawlInvectorLifecycleParameters.RespawnStateName,
                null,
                BrawlInvectorLifecyclePresentation.Respawn,
                true);
            AnimatorState victory = ValidateLifecycleState(
                lifecycle,
                BrawlInvectorLifecycleParameters.VictoryStateName,
                AssetDatabase.LoadAssetAtPath<AnimationClip>(VictoryClipPath),
                BrawlInvectorLifecyclePresentation.Victory,
                false);
            if (lifecycle.defaultState != respawn)
                throw new InvalidOperationException("The lifecycle machine default state must be Respawn.");

            AnimatorStateTransition[] generatedAnyState = fullBody.anyStateTransitions;
            AnimatorStateTransition[] vendorAnyState = vendor.layers
                .Single(layer => layer.name == "FullBody").stateMachine.anyStateTransitions;
            if (generatedAnyState.Length != vendorAnyState.Length + 3)
                throw new InvalidOperationException("The FullBody AnyState transition count changed.");

            ValidateLifecycleTransition(
                generatedAnyState[0], death,
                BrawlInvectorLifecycleParameters.DeathTriggerName, 0.1f);
            ValidateLifecycleTransition(
                generatedAnyState[1], respawn,
                BrawlInvectorLifecycleParameters.RespawnTriggerName, 0.05f);
            ValidateLifecycleTransition(
                generatedAnyState[2], victory,
                BrawlInvectorLifecycleParameters.VictoryTriggerName, 0.2f);
            for (int i = 0; i < vendorAnyState.Length; i++)
            {
                if (!string.Equals(
                    TransitionFingerprint(generatedAnyState[i + 3]),
                    TransitionFingerprint(vendorAnyState[i]),
                    StringComparison.Ordinal))
                {
                    throw new InvalidOperationException(
                        "Vendor FullBody AnyState transition order or settings changed at index " + i + ".");
                }
            }
        }

        static void ValidateLifecycleTriggerParameter(
            IEnumerable<AnimatorControllerParameter> parameters,
            string parameterName)
        {
            AnimatorControllerParameter[] matches = parameters
                .Where(parameter => parameter.name == parameterName)
                .ToArray();
            if (matches.Length != 1 || matches[0].type != AnimatorControllerParameterType.Trigger)
            {
                throw new InvalidOperationException(
                    "The lifecycle graph requires one Trigger parameter named " + parameterName + ".");
            }
        }

        static bool ParametersMatch(
            AnimatorControllerParameter left,
            AnimatorControllerParameter right)
        {
            return left.name == right.name && left.type == right.type &&
                   left.defaultBool == right.defaultBool && left.defaultInt == right.defaultInt &&
                   Mathf.Approximately(left.defaultFloat, right.defaultFloat);
        }

        static AnimatorState ValidateLifecycleState(
            AnimatorStateMachine lifecycle,
            string stateName,
            Motion expectedMotion,
            BrawlInvectorLifecyclePresentation expectedPresentation,
            bool expectsExit)
        {
            AnimatorState[] matches = lifecycle.states
                .Where(child => child.state.name == stateName)
                .Select(child => child.state)
                .ToArray();
            if (matches.Length != 1)
                throw new InvalidOperationException("The lifecycle graph requires one " + stateName + " state.");

            AnimatorState state = matches[0];
            var markers = state.behaviours.OfType<BrawlInvectorLifecycleStateMarker>().ToArray();
            if (state.motion != expectedMotion || !Mathf.Approximately(state.speed, 1f) ||
                state.writeDefaultValues || state.iKOnFeet || state.behaviours.Length != 1 ||
                markers.Length != 1 || markers[0].Presentation != expectedPresentation)
            {
                throw new InvalidOperationException(
                    "Lifecycle state settings or marker changed for " + stateName + ".");
            }

            if (!expectsExit)
            {
                if (state.transitions.Length != 0)
                    throw new InvalidOperationException(stateName + " must hold without an automatic exit.");
                return state;
            }

            if (state.transitions.Length != 1)
                throw new InvalidOperationException("Respawn must have exactly one exit transition.");
            AnimatorStateTransition exit = state.transitions[0];
            if (!exit.isExit || exit.conditions.Length != 0 || !exit.hasExitTime ||
                !Mathf.Approximately(exit.exitTime, 0.01f) ||
                !exit.hasFixedDuration || !Mathf.Approximately(exit.duration, 0.05f) ||
                !Mathf.Approximately(exit.offset, 0f) ||
                exit.interruptionSource != TransitionInterruptionSource.None ||
                !exit.orderedInterruption)
            {
                throw new InvalidOperationException("Respawn exit transition settings changed.");
            }
            return state;
        }

        static void ValidateLifecycleTransition(
            AnimatorStateTransition transition,
            AnimatorState expectedDestination,
            string expectedTrigger,
            float expectedDuration)
        {
            AnimatorCondition[] conditions = transition.conditions;
            if (transition.destinationState != expectedDestination || transition.isExit ||
                transition.hasExitTime || !transition.hasFixedDuration ||
                !Mathf.Approximately(transition.duration, expectedDuration) ||
                !Mathf.Approximately(transition.offset, 0f) || transition.canTransitionToSelf ||
                transition.interruptionSource != TransitionInterruptionSource.None ||
                !transition.orderedInterruption || conditions.Length != 1 ||
                conditions[0].mode != AnimatorConditionMode.If ||
                !string.Equals(conditions[0].parameter, expectedTrigger, StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    "Lifecycle AnyState transition settings changed for " + expectedTrigger + ".");
            }
        }

        static string TransitionFingerprint(AnimatorStateTransition transition)
        {
            string destination = transition.destinationState != null
                ? "state:" + transition.destinationState.name
                : transition.destinationStateMachine != null
                    ? "machine:" + transition.destinationStateMachine.name
                    : transition.isExit ? "exit" : "none";
            string conditions = string.Join(",", transition.conditions.Select(condition =>
                condition.mode + ":" + condition.parameter + ":" + condition.threshold));
            return destination + "|" + transition.hasExitTime + "|" + transition.exitTime + "|" +
                   transition.hasFixedDuration + "|" + transition.duration + "|" + transition.offset + "|" +
                   transition.canTransitionToSelf + "|" + transition.interruptionSource + "|" +
                   transition.orderedInterruption + "|" + conditions;
        }

        internal static AnimatorController BuildLifecycleController()
        {
            var controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(LifecycleControllerPath);
            if (controller == null)
            {
                if (!AssetDatabase.CopyAsset(CombinedControllerPath, LifecycleControllerPath))
                    throw new InvalidOperationException("Could not copy the combined Animator controller.");
                AssetDatabase.ImportAsset(
                    LifecycleControllerPath,
                    ImportAssetOptions.ForceSynchronousImport | ImportAssetOptions.ForceUpdate);
                controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(LifecycleControllerPath);
            }
            if (controller == null)
                throw new InvalidOperationException("The project lifecycle Animator controller is unavailable.");

            bool hasLifecycleParameter = controller.parameters.Any(parameter =>
                parameter.name == BrawlInvectorLifecycleParameters.DeathTriggerName ||
                parameter.name == BrawlInvectorLifecycleParameters.RespawnTriggerName ||
                parameter.name == BrawlInvectorLifecycleParameters.VictoryTriggerName);
            AnimatorStateMachine fullBody = controller.layers
                .Single(layer => layer.name == "FullBody").stateMachine;
            bool hasLifecycleMachine = fullBody.stateMachines.Any(child =>
                child.stateMachine.name == BrawlInvectorLifecycleParameters.StateMachineName);

            if (!hasLifecycleParameter && !hasLifecycleMachine)
            {
                AddLifecycleOverlay(controller, fullBody);
                controller.name = "CinderInvectorPilot";
                EditorUtility.SetDirty(controller);
                AssetDatabase.SaveAssets();
            }

            if (NormalizeLifecycleOverlay(fullBody))
            {
                EditorUtility.SetDirty(controller);
                AssetDatabase.SaveAssets();
            }

            ValidateGeneratedLifecycleController(controller);
            return controller;
        }

        static bool NormalizeLifecycleOverlay(AnimatorStateMachine fullBody)
        {
            AnimatorStateMachine[] machines = fullBody.stateMachines
                .Where(child => child.stateMachine.name ==
                    BrawlInvectorLifecycleParameters.StateMachineName)
                .Select(child => child.stateMachine)
                .ToArray();
            if (machines.Length != 1)
                return false;

            AnimatorState[] respawnStates = machines[0].states
                .Where(child => child.state.name ==
                    BrawlInvectorLifecycleParameters.RespawnStateName)
                .Select(child => child.state)
                .ToArray();
            if (respawnStates.Length != 1 || respawnStates[0].transitions.Length != 1 ||
                !respawnStates[0].transitions[0].isExit)
            {
                return false;
            }

            AnimatorStateTransition exit = respawnStates[0].transitions[0];
            bool changed = !exit.hasExitTime || !Mathf.Approximately(exit.exitTime, 0.01f);
            if (!changed)
                return false;
            exit.hasExitTime = true;
            exit.exitTime = 0.01f;
            return true;
        }

        static void AddLifecycleOverlay(
            AnimatorController controller,
            AnimatorStateMachine fullBody)
        {
            controller.AddParameter(
                BrawlInvectorLifecycleParameters.DeathTriggerName,
                AnimatorControllerParameterType.Trigger);
            controller.AddParameter(
                BrawlInvectorLifecycleParameters.RespawnTriggerName,
                AnimatorControllerParameterType.Trigger);
            controller.AddParameter(
                BrawlInvectorLifecycleParameters.VictoryTriggerName,
                AnimatorControllerParameterType.Trigger);

            AnimatorStateTransition[] vendorAnyStateTransitions =
                fullBody.anyStateTransitions.ToArray();
            AnimatorStateMachine lifecycle = fullBody.AddStateMachine(
                BrawlInvectorLifecycleParameters.StateMachineName,
                new Vector3(700f, 525f, 0f));
            AnimatorState death = AddLifecycleState(
                lifecycle,
                BrawlInvectorLifecycleParameters.DeathStateName,
                AssetDatabase.LoadAssetAtPath<AnimationClip>(DeathClipPath),
                BrawlInvectorLifecyclePresentation.Death,
                new Vector3(300f, 100f, 0f));
            AnimatorState respawn = AddLifecycleState(
                lifecycle,
                BrawlInvectorLifecycleParameters.RespawnStateName,
                null,
                BrawlInvectorLifecyclePresentation.Respawn,
                new Vector3(300f, 210f, 0f));
            AnimatorState victory = AddLifecycleState(
                lifecycle,
                BrawlInvectorLifecycleParameters.VictoryStateName,
                AssetDatabase.LoadAssetAtPath<AnimationClip>(VictoryClipPath),
                BrawlInvectorLifecyclePresentation.Victory,
                new Vector3(300f, 320f, 0f));
            lifecycle.defaultState = respawn;

            AnimatorStateTransition respawnExit = respawn.AddExitTransition();
            respawnExit.hasExitTime = true;
            respawnExit.exitTime = 0.01f;
            respawnExit.hasFixedDuration = true;
            respawnExit.duration = 0.05f;
            respawnExit.offset = 0f;
            respawnExit.interruptionSource = TransitionInterruptionSource.None;
            respawnExit.orderedInterruption = true;

            AnimatorStateTransition deathTransition = AddLifecycleAnyStateTransition(
                fullBody, death, BrawlInvectorLifecycleParameters.DeathTriggerName, 0.1f);
            AnimatorStateTransition respawnTransition = AddLifecycleAnyStateTransition(
                fullBody, respawn, BrawlInvectorLifecycleParameters.RespawnTriggerName, 0.05f);
            AnimatorStateTransition victoryTransition = AddLifecycleAnyStateTransition(
                fullBody, victory, BrawlInvectorLifecycleParameters.VictoryTriggerName, 0.2f);
            fullBody.anyStateTransitions = new[]
            {
                deathTransition,
                respawnTransition,
                victoryTransition,
            }.Concat(vendorAnyStateTransitions).ToArray();
        }

        static AnimatorState AddLifecycleState(
            AnimatorStateMachine machine,
            string stateName,
            Motion motion,
            BrawlInvectorLifecyclePresentation presentation,
            Vector3 position)
        {
            AnimatorState state = machine.AddState(stateName, position);
            state.motion = motion;
            state.writeDefaultValues = false;
            state.tag = string.Empty;
            var marker = state.AddStateMachineBehaviour<BrawlInvectorLifecycleStateMarker>();
            marker.Configure(presentation);
            return state;
        }

        static AnimatorStateTransition AddLifecycleAnyStateTransition(
            AnimatorStateMachine machine,
            AnimatorState destination,
            string triggerName,
            float duration)
        {
            AnimatorStateTransition transition = machine.AddAnyStateTransition(destination);
            transition.hasExitTime = false;
            transition.hasFixedDuration = true;
            transition.duration = duration;
            transition.offset = 0f;
            transition.interruptionSource = TransitionInterruptionSource.None;
            transition.orderedInterruption = true;
            transition.canTransitionToSelf = false;
            transition.AddCondition(AnimatorConditionMode.If, 0f, triggerName);
            return transition;
        }

        static AnimatorOverrideController BuildOverrideController(
            AnimatorController lifecycleController)
        {
            return BuildOverrideController(
                lifecycleController,
                OverrideControllerPath,
                "CinderInvectorPilot");
        }

        internal static AnimatorOverrideController BuildOverrideController(
            AnimatorController lifecycleController,
            string destinationPath,
            string controllerName)
        {
            var controller = AssetDatabase.LoadAssetAtPath<AnimatorOverrideController>(destinationPath);
            if (controller == null)
            {
                controller = new AnimatorOverrideController(lifecycleController)
                {
                    name = controllerName,
                };
                AssetDatabase.CreateAsset(controller, destinationPath);
            }
            else
            {
                controller.runtimeAnimatorController = lifecycleController;
                controller.name = controllerName;
                EditorUtility.SetDirty(controller);
            }

            return controller;
        }

        public const string WizardBasicAttackOverrideSourceName = "WeakAttack_UnarmedA";
        public const string WizardSuperAttackOverrideSourceName = "StrongAttack_PunchA";
        public const string CarryPoseOverrideSourceName = "Idle@Pistol";
        public const string WizardBasicAttackClipPath =
            "Assets/ModularRPGHeroesPBR/Animations/MagicWand/Attack01_MagicWand.fbx";
        public const string WizardBasicAttackClipName = "Attack01_MagicWand";
        public const string WizardSuperAttackClipPath =
            "Assets/ModularRPGHeroesPBR/Animations/MagicWand/Attack02_MagicWand.fbx";
        public const string WizardSuperAttackClipName = "Attack02_MagicWand";
        public const string WizardCarryPoseClipPath =
            "Assets/ModularRPGHeroesPBR/Animations/MagicWand/Idle_MagicWand.fbx";
        public const string WizardCarryPoseClipName = "Idle_MagicWand";

        /// <summary>
        /// Shared staff-wizard presentation overrides: MagicWand attack clips
        /// replace the vendor unarmed AttackID-0 sources, and the MagicWand
        /// idle replaces the vendor pistol upper-body carry pose.
        /// </summary>
        internal static void ConfigureWizardPresentationOverrides(
            AnimatorOverrideController controller)
        {
            ConfigurePresentationOverrides(
                controller,
                new[]
                {
                    WizardBasicAttackOverrideSourceName,
                    WizardSuperAttackOverrideSourceName,
                    CarryPoseOverrideSourceName,
                },
                new[]
                {
                    WizardBasicAttackClipPath,
                    WizardSuperAttackClipPath,
                    WizardCarryPoseClipPath,
                },
                new[]
                {
                    WizardBasicAttackClipName,
                    WizardSuperAttackClipName,
                    WizardCarryPoseClipName,
                });
        }

        internal static void ConfigurePresentationOverrides(
            AnimatorOverrideController controller,
            string[] sourceNames,
            string[] clipPaths,
            string[] clipNames)
        {
            if (controller == null)
                throw new ArgumentNullException(nameof(controller));

            var replacements = new AnimationClip[sourceNames.Length];
            for (int i = 0; i < sourceNames.Length; i++)
                replacements[i] = RequirePresentationClip(clipPaths[i], clipNames[i]);

            var overrides = new List<KeyValuePair<AnimationClip, AnimationClip>>(
                controller.overridesCount);
            controller.GetOverrides(overrides);

            var sourceCounts = new int[sourceNames.Length];
            for (int i = 0; i < overrides.Count; i++)
            {
                AnimationClip source = overrides[i].Key;
                AnimationClip replacement = null;
                if (source != null)
                {
                    for (int j = 0; j < sourceNames.Length; j++)
                    {
                        if (!string.Equals(source.name, sourceNames[j], StringComparison.Ordinal))
                            continue;
                        sourceCounts[j]++;
                        replacement = replacements[j];
                        break;
                    }
                }
                overrides[i] = new KeyValuePair<AnimationClip, AnimationClip>(
                    source, replacement);
            }

            for (int j = 0; j < sourceCounts.Length; j++)
            {
                if (sourceCounts[j] != 1)
                {
                    throw new InvalidOperationException(
                        "The shared lifecycle graph no longer exposes exactly one '" +
                        sourceNames[j] + "' presentation source clip.");
                }
            }

            controller.ApplyOverrides(overrides);
            EditorUtility.SetDirty(controller);
        }

        static AnimationClip RequirePresentationClip(string path, string clipName)
        {
            AnimationClip clip = AssetDatabase.LoadAllAssetsAtPath(path)
                .OfType<AnimationClip>()
                .Where(value => !value.name.StartsWith("__preview__", StringComparison.Ordinal))
                .SingleOrDefault(value => string.Equals(
                    value.name, clipName, StringComparison.Ordinal));
            if (clip == null)
            {
                throw new InvalidOperationException(
                    "The pinned animation asset '" + path +
                    "' does not contain clip '" + clipName + "'.");
            }
            return clip;
        }

        static GameObject BuildPilotPrefab(AnimatorOverrideController overrideController)
        {
            return BuildPilotPrefab(
                overrideController,
                CinderPath,
                "CinderInvectorPilot_DISABLED",
                "Staff01",
                WeaponPrefabPath,
                WeaponIKAdjustListPath,
                "CinderStaffPresentation",
                PrefabPath);
        }

        internal static GameObject BuildPilotPrefab(
            AnimatorOverrideController overrideController,
            string characterPath,
            string pilotName,
            string authoredStaffName,
            string weaponPrefabPath,
            string weaponIKAdjustListPath,
            string weaponPresentationName,
            string destinationPath)
        {
            var template = AssetDatabase.LoadAssetAtPath<GameObject>(TemplatePath);
            var character = AssetDatabase.LoadAssetAtPath<GameObject>(characterPath);
            Scene previewScene = EditorSceneManager.NewPreviewScene();
            try
            {
                var instance = (GameObject)PrefabUtility.InstantiatePrefab(character, previewScene);
                instance.SetActive(false);
                instance.name = pilotName;
                instance.tag = "Player";
                instance.layer = InvectorPlayerLayer;

                Animator animator = instance.GetComponent<Animator>();
                animator.enabled = false;
                animator.applyRootMotion = false;
                animator.runtimeAnimatorController = overrideController;

                Transform aura = instance.transform.Find("SchoolAura");
                if (aura != null)
                    aura.gameObject.SetActive(false);

                var referenceMap = new Dictionary<UnityEngine.Object, UnityEngine.Object>
                {
                    { template, instance },
                    { template.transform, instance.transform },
                    { template.GetComponent<Animator>(), animator },
                };
                var copies = new List<(Component source, Component target)>();
                foreach (Type sourceType in SourceRootComponentTypes)
                {
                    Component source = template.GetComponent(sourceType);
                    Type destinationType = DestinationTypeFor(sourceType);
                    Component target = instance.GetComponent(destinationType);
                    if (target == null)
                        target = instance.AddComponent(destinationType);

                    // Build the complete map before copying/remapping so references
                    // to either stock component resolve to the project subclass.
                    referenceMap.Add(source, target);
                    copies.Add((source, target));
                }

                foreach ((Component source, Component target) in copies)
                {
                    if (source is Rigidbody sourceRigidbody && target is Rigidbody targetRigidbody)
                    {
                        targetRigidbody.mass = sourceRigidbody.mass;
                        targetRigidbody.linearDamping = sourceRigidbody.linearDamping;
                        targetRigidbody.angularDamping = sourceRigidbody.angularDamping;
                        targetRigidbody.maxAngularVelocity = sourceRigidbody.maxAngularVelocity;
                    }
                    else if (source is CapsuleCollider sourceCapsule && target is CapsuleCollider targetCapsule)
                    {
                        targetCapsule.center = sourceCapsule.center;
                        targetCapsule.radius = sourceCapsule.radius;
                        targetCapsule.height = sourceCapsule.height;
                        targetCapsule.direction = sourceCapsule.direction;
                        targetCapsule.isTrigger = sourceCapsule.isTrigger;
                        targetCapsule.sharedMaterial = sourceCapsule.sharedMaterial;
                    }
                    else if (source.GetType() != target.GetType())
                    {
                        EditorUtility.CopySerializedManagedFieldsOnly(source, target);
                    }
                    else
                    {
                        EditorUtility.CopySerialized(source, target);
                    }
                }

                foreach ((Component _, Component target) in copies)
                    RemapTemplateReferences(target, template, referenceMap);

                var controller = instance.GetComponent<BrawlInvectorThirdPersonController>();
                var shooter = instance.GetComponent<vShooterManager>();
                var melee = instance.GetComponent<BrawlInvectorMeleePresentationManager>();
                var input = instance.GetComponent<InvectorShooterMeleeInputAdapter>();
                var inputActions = AssetDatabase.LoadAssetAtPath<InputActionAsset>(ProjectActionsPath);
                input.ConfigureDormant(controller, shooter, melee, inputActions, 0);

                var motor = instance.GetComponent<InvectorBrawlerMotor>();
                if (motor == null)
                    motor = instance.AddComponent<InvectorBrawlerMotor>();
                motor.ConfigureDormant(
                    controller,
                    input,
                    instance.GetComponent<Rigidbody>(),
                    instance.GetComponent<CapsuleCollider>());
                input.ConfigureMotorBridge(motor);
                input.SelectMovementFeedMode(InvectorMovementFeedMode.LabProjectAction);

                var animationDriver = instance.GetComponent<InvectorBrawlerAnimationDriver>();
                if (animationDriver == null)
                    animationDriver = instance.AddComponent<InvectorBrawlerAnimationDriver>();
                animationDriver.ConfigureDormant(controller, input);

                ConfigureWeaponPresentation(
                    instance,
                    animator,
                    controller,
                    authoredStaffName,
                    weaponPrefabPath,
                    weaponIKAdjustListPath,
                    weaponPresentationName);
                ConfigureHitProxy(instance);

                ConfigureStaticSafety(instance);
                AuditNoTemplateHierarchyReferences(instance, template);
                instance.SetActive(false);

                GameObject saved = PrefabUtility.SaveAsPrefabAsset(instance, destinationPath, out bool success);
                if (!success || saved == null)
                    throw new InvalidOperationException("Unity failed to save " + destinationPath + ".");
                return saved;
            }
            finally
            {
                EditorSceneManager.ClosePreviewScene(previewScene);
            }
        }

        /// <summary>
        /// Creates the production-human topology as a prefab variant of the
        /// already validated dormant pilot. Building from a fresh pilot
        /// instance avoids accumulating component or property overrides while
        /// SaveAsPrefabAsset preserves the production asset GUID on repeat.
        /// </summary>
        static GameObject BuildProductionHumanPrefab(GameObject pilotPrefab)
        {
            return BuildProductionHumanPrefab(
                pilotPrefab,
                "CinderInvectorHuman_DISABLED",
                ProductionHumanPrefabPath,
                "fire");
        }

        internal static GameObject BuildProductionHumanPrefab(
            GameObject pilotPrefab,
            string instanceName,
            string destinationPath,
            string rosterId)
        {
            if (pilotPrefab == null)
                throw new ArgumentNullException(nameof(pilotPrefab));

            Scene previewScene = EditorSceneManager.NewPreviewScene();
            try
            {
                var instance = (GameObject)PrefabUtility.InstantiatePrefab(
                    pilotPrefab, previewScene);
                if (instance == null)
                {
                    throw new InvalidOperationException(
                        "Unity failed to instantiate the validated Invector pilot.");
                }

                instance.SetActive(false);
                instance.name = instanceName;

                Health health = instance.GetComponent<Health>();
                if (health == null) health = instance.AddComponent<Health>();

                BrawlerController facade = instance.GetComponent<BrawlerController>();
                if (facade == null) facade = instance.AddComponent<BrawlerController>();

                PlayerBrawlerInput playerInput =
                    instance.GetComponent<PlayerBrawlerInput>();
                if (playerInput == null)
                    playerInput = instance.AddComponent<PlayerBrawlerInput>();

                InvectorHumanRuntimeGate runtimeGate =
                    instance.GetComponent<InvectorHumanRuntimeGate>();
                if (runtimeGate == null)
                    runtimeGate = instance.AddComponent<InvectorHumanRuntimeGate>();

                InvectorBrawlerPrefabIdentity identity =
                    instance.GetComponent<InvectorBrawlerPrefabIdentity>();
                if (identity == null)
                    identity = instance.AddComponent<InvectorBrawlerPrefabIdentity>();
                identity.ConfigureDormant(rosterId, InvectorBrawlerPrefabRole.Human);

                var controller =
                    instance.GetComponent<BrawlInvectorThirdPersonController>();
                var input =
                    instance.GetComponent<InvectorShooterMeleeInputAdapter>();
                var motor = instance.GetComponent<InvectorBrawlerMotor>();
                var animationDriver =
                    instance.GetComponent<InvectorBrawlerAnimationDriver>();
                var weaponPresenter =
                    instance.GetComponent<InvectorBrawlerWeaponPresentation>();
                var animator = instance.GetComponent<Animator>();
                var body = instance.GetComponent<Rigidbody>();
                var capsule = instance.GetComponent<CapsuleCollider>();
                BrawlerHitProxy hitProxy =
                    instance.GetComponentsInChildren<BrawlerHitProxy>(true).Single();

                facade.SetMotor(motor);
                facade.SetAnimationDriver(animationDriver);
                facade.SetWeaponPresentation(weaponPresenter);

                input.SelectMovementFeedMode(InvectorMovementFeedMode.BufferedMotor);
                input.SetMovementReference(null);

                // The asset remains inert. The runtime assembler configures
                // definition data while inactive, then the project gate opens
                // the already-censused authorities in production order.
                ConfigureStaticSafety(instance);
                health.enabled = false;
                facade.enabled = false;
                playerInput.enabled = false;

                runtimeGate.ConfigureDormant(
                    facade,
                    health,
                    controller,
                    input,
                    motor,
                    animationDriver,
                    weaponPresenter,
                    animator,
                    body,
                    capsule,
                    hitProxy,
                    playerInput);
                runtimeGate.enabled = false;
                instance.SetActive(false);

                GameObject saved = PrefabUtility.SaveAsPrefabAsset(
                    instance, destinationPath, out bool success);
                if (!success || saved == null)
                {
                    throw new InvalidOperationException(
                        "Unity failed to save " + destinationPath + ".");
                }
                return saved;
            }
            finally
            {
                EditorSceneManager.ClosePreviewScene(previewScene);
            }
        }

        /// <summary>
        /// Creates the production-AI topology as a direct prefab variant of
        /// the validated dormant pilot. The NavMeshAgent is a child planner
        /// only: position, rotation, and off-mesh traversal writes stay off so
        /// the root Invector Rigidbody motor remains the sole transform owner.
        /// </summary>
        static GameObject BuildProductionAIPrefab(GameObject pilotPrefab)
        {
            return BuildProductionAIPrefab(
                pilotPrefab,
                "CinderInvectorAI_DISABLED",
                ProductionAIPrefabPath,
                "fire");
        }

        internal static GameObject BuildProductionAIPrefab(
            GameObject pilotPrefab,
            string instanceName,
            string destinationPath,
            string rosterId)
        {
            if (pilotPrefab == null)
                throw new ArgumentNullException(nameof(pilotPrefab));

            Scene previewScene = EditorSceneManager.NewPreviewScene();
            try
            {
                var instance = (GameObject)PrefabUtility.InstantiatePrefab(
                    pilotPrefab, previewScene);
                if (instance == null)
                {
                    throw new InvalidOperationException(
                        "Unity failed to instantiate the validated Invector pilot.");
                }

                instance.SetActive(false);
                instance.name = instanceName;

                Health health = instance.GetComponent<Health>();
                if (health == null) health = instance.AddComponent<Health>();

                BrawlerController facade = instance.GetComponent<BrawlerController>();
                if (facade == null) facade = instance.AddComponent<BrawlerController>();

                AIBrawler ai = instance.GetComponent<AIBrawler>();
                if (ai == null) ai = instance.AddComponent<AIBrawler>();

                InvectorBrawlerNavigation navigation =
                    instance.GetComponent<InvectorBrawlerNavigation>();
                if (navigation == null)
                    navigation = instance.AddComponent<InvectorBrawlerNavigation>();

                InvectorAIRuntimeGate runtimeGate =
                    instance.GetComponent<InvectorAIRuntimeGate>();
                if (runtimeGate == null)
                    runtimeGate = instance.AddComponent<InvectorAIRuntimeGate>();

                InvectorBrawlerPrefabIdentity identity =
                    instance.GetComponent<InvectorBrawlerPrefabIdentity>();
                if (identity == null)
                    identity = instance.AddComponent<InvectorBrawlerPrefabIdentity>();
                identity.ConfigureDormant(rosterId, InvectorBrawlerPrefabRole.AI);

                var plannerObject = new GameObject(ProductionAIPlannerName);
                plannerObject.layer = 0;
                plannerObject.transform.SetParent(instance.transform, false);
                plannerObject.transform.localPosition = Vector3.zero;
                plannerObject.transform.localRotation = Quaternion.identity;
                plannerObject.transform.localScale = Vector3.one;
                NavMeshAgent planner = plannerObject.AddComponent<NavMeshAgent>();
                CapsuleCollider capsule = instance.GetComponent<CapsuleCollider>();
                planner.radius = capsule.radius;
                planner.height = capsule.height;
                planner.baseOffset = 0f;
                planner.speed = 3.5f;
                planner.acceleration = 40f;
                planner.angularSpeed = 0f;
                planner.stoppingDistance = 0.5f;
                planner.autoBraking = true;
                planner.autoRepath = true;
                planner.autoTraverseOffMeshLink = false;
                planner.updatePosition = false;
                planner.updateRotation = false;
                planner.updateUpAxis = false;
                planner.areaMask = NavMesh.AllAreas;
                planner.obstacleAvoidanceType = ObstacleAvoidanceType.HighQualityObstacleAvoidance;
                planner.enabled = false;

                var controller =
                    instance.GetComponent<BrawlInvectorThirdPersonController>();
                var input =
                    instance.GetComponent<InvectorShooterMeleeInputAdapter>();
                var motor = instance.GetComponent<InvectorBrawlerMotor>();
                var animationDriver =
                    instance.GetComponent<InvectorBrawlerAnimationDriver>();
                var weaponPresenter =
                    instance.GetComponent<InvectorBrawlerWeaponPresentation>();
                var animator = instance.GetComponent<Animator>();
                var body = instance.GetComponent<Rigidbody>();
                BrawlerHitProxy hitProxy =
                    instance.GetComponentsInChildren<BrawlerHitProxy>(true).Single();

                facade.SetMotor(motor);
                facade.SetAnimationDriver(animationDriver);
                facade.SetWeaponPresentation(weaponPresenter);
                ai.SetNavigation(navigation);

                input.SelectMovementFeedMode(InvectorMovementFeedMode.BufferedMotor);
                input.SetMovementReference(null);
                navigation.ConfigureDormant(planner);
                motor.ConfigureNavigationPlanner(navigation);

                // The asset remains inert. The runtime assembler configures
                // definition data while inactive, then the AI gate opens the
                // already-censused authorities and enables AIBrawler last.
                ConfigureStaticSafety(instance);
                health.enabled = false;
                facade.enabled = false;
                ai.enabled = false;
                navigation.enabled = false;

                runtimeGate.ConfigureDormant(
                    facade,
                    health,
                    controller,
                    input,
                    motor,
                    animationDriver,
                    weaponPresenter,
                    animator,
                    body,
                    capsule,
                    hitProxy,
                    ai,
                    navigation);
                runtimeGate.enabled = false;
                instance.SetActive(false);

                GameObject saved = PrefabUtility.SaveAsPrefabAsset(
                    instance, destinationPath, out bool success);
                if (!success || saved == null)
                {
                    throw new InvalidOperationException(
                        "Unity failed to save " + destinationPath + ".");
                }
                return saved;
            }
            finally
            {
                EditorSceneManager.ClosePreviewScene(previewScene);
            }
        }

        internal static void ConfigureWeaponPresentation(
            GameObject instance,
            Animator animator,
            BrawlInvectorThirdPersonController controller,
            string authoredStaffName,
            string weaponPrefabPath,
            string weaponIKAdjustListPath,
            string weaponPresentationName)
        {
            GameObject weaponPrefab =
                AssetDatabase.LoadAssetAtPath<GameObject>(weaponPrefabPath);
            vWeaponIKAdjustList ikAdjustList =
                AssetDatabase.LoadAssetAtPath<vWeaponIKAdjustList>(weaponIKAdjustListPath);
            if (weaponPrefab == null || ikAdjustList == null)
                throw new InvalidOperationException("The generated weapon presentation assets are missing.");

            Transform authoredStaff = FindDescendant(instance.transform, authoredStaffName);
            if (authoredStaff == null || authoredStaff.parent == null)
                throw new InvalidOperationException(
                    "The pilot lost its " + authoredStaffName + " parent socket.");
            Transform weaponParent = authoredStaff.parent;
            UnityEngine.Object.DestroyImmediate(authoredStaff.gameObject);

            var visual = (GameObject)PrefabUtility.InstantiatePrefab(
                weaponPrefab, instance.scene);
            visual.name = weaponPresentationName;
            visual.transform.SetParent(weaponParent, false);
            visual.transform.localPosition = Vector3.zero;
            visual.transform.localRotation = Quaternion.identity;
            visual.transform.localScale = Vector3.one;

            Transform muzzle = FindDescendant(visual.transform, "SpellOrigin");
            Transform supportHand = FindDescendant(visual.transform, "SupportHandTarget");
            Transform supportHint = FindDescendant(visual.transform, "SupportHintTarget");
            ParticleSystem[] muzzleEffects =
                visual.GetComponentsInChildren<ParticleSystem>(true);
            if (muzzle == null || supportHand == null || supportHint == null ||
                muzzleEffects.Length != 1)
            {
                throw new InvalidOperationException(
                    "The generated staff presentation hierarchy is incomplete.");
            }

            InvectorBrawlerWeaponPresentation presenter =
                instance.GetComponent<InvectorBrawlerWeaponPresentation>();
            if (presenter == null)
                presenter = instance.AddComponent<InvectorBrawlerWeaponPresentation>();
            presenter.Configure(
                animator,
                controller,
                visual.transform,
                muzzle,
                supportHand,
                supportHint,
                ikAdjustList,
                WeaponCategory,
                false,
                muzzleEffects);
        }

        static void ConfigureHitProxy(GameObject instance)
        {
            Transform existing = instance.transform.Find("BrawlHitProxy");
            GameObject proxyObject;
            if (existing == null)
            {
                proxyObject = new GameObject("BrawlHitProxy");
                proxyObject.transform.SetParent(instance.transform, false);
            }
            else
            {
                proxyObject = existing.gameObject;
            }

            SphereCollider sphere = proxyObject.GetComponent<SphereCollider>();
            if (sphere == null) sphere = proxyObject.AddComponent<SphereCollider>();
            BrawlerHitProxy proxy = proxyObject.GetComponent<BrawlerHitProxy>();
            if (proxy == null) proxy = proxyObject.AddComponent<BrawlerHitProxy>();
            proxy.Configure(new Vector3(0f, 1f, 0f), 0.65f);
            sphere.enabled = false;
            proxy.enabled = false;
        }

        static Type DestinationTypeFor(Type sourceType)
        {
            if (sourceType == typeof(vThirdPersonController))
                return typeof(BrawlInvectorThirdPersonController);
            if (sourceType == typeof(vMeleeManager))
                return typeof(BrawlInvectorMeleePresentationManager);
            if (sourceType == typeof(vShooterMeleeInput))
                return typeof(InvectorShooterMeleeInputAdapter);
            return sourceType;
        }

        static void ConfigureStaticSafety(GameObject instance)
        {
            Animator animator = instance.GetComponent<Animator>();
            animator.enabled = false;
            animator.applyRootMotion = false;

            Rigidbody rigidbody = instance.GetComponent<Rigidbody>();
            rigidbody.isKinematic = true;
            rigidbody.useGravity = false;
            rigidbody.constraints = RigidbodyConstraints.FreezeAll;
            rigidbody.interpolation = RigidbodyInterpolation.None;
            rigidbody.collisionDetectionMode = CollisionDetectionMode.Discrete;

            CapsuleCollider capsule = instance.GetComponent<CapsuleCollider>();
            capsule.enabled = false;

            BrawlInvectorThirdPersonController controller =
                instance.GetComponent<BrawlInvectorThirdPersonController>();
            controller.enabled = false;
            controller.isImmortal = true;
            controller.customFixedTimeStep = vThirdPersonMotor.CustomFixedTimeStep.Default;
            controller.groundLayer = 1 << 8;
            controller.autoCrouchLayer = 0;
            controller.stopMoveLayer = 0;
            controller.UseAutoCrouch(false);

            vShooterManager shooter = instance.GetComponent<vShooterManager>();
            shooter.enabled = false;
            shooter.damageLayer = 0;
            shooter.blockAimLayer = 0;
            shooter.useCancelReload = false;
            shooter.useAmmoDisplay = false;
            shooter.applyRecoilToCamera = false;
            shooter.useLockOn = false;
            shooter.useLockOnMeleeOnly = false;
            shooter.hipfireShot = false;
            shooter.alwaysAiming = false;
            shooter.weaponIKAdjustList = null;
            shooter.rWeapon = null;
            shooter.lWeapon = null;
            shooter.AllAmmoInfinity = false;

            BrawlInvectorMeleePresentationManager melee =
                instance.GetComponent<BrawlInvectorMeleePresentationManager>();
            melee.enabled = false;
            melee.Members.Clear();
            melee.leftWeapon = null;
            melee.rightWeapon = null;
            melee.defaultStaminaCost = 0f;
            melee.defaultStaminaRecoveryDelay = 0f;
            if (melee.hitProperties != null)
            {
                melee.hitProperties.hitDamageTags.Clear();
                melee.hitProperties.useRecoil = false;
                melee.hitProperties.hitRecoilLayer = 0;
            }

            vAmmoManager ammo = instance.GetComponent<vAmmoManager>();
            ammo.enabled = false;
            ammo.ammoListData = null;
            ammo.itemManager = null;
            ammo.ammos.Clear();
            instance.GetComponent<vCollectShooterMeleeControl>().enabled = false;
            instance.GetComponent<InvectorShooterMeleeInputAdapter>().enabled = false;
            instance.GetComponent<InvectorBrawlerMotor>().enabled = false;
            instance.GetComponent<InvectorBrawlerAnimationDriver>().enabled = false;
            instance.GetComponent<InvectorBrawlerWeaponPresentation>().DisableLabRuntime();
        }

        static void RemapTemplateReferences(
            Component target,
            GameObject templateRoot,
            IReadOnlyDictionary<UnityEngine.Object, UnityEngine.Object> referenceMap)
        {
            var serialized = new SerializedObject(target);
            SerializedProperty property = serialized.GetIterator();
            while (property.Next(true))
            {
                if (property.propertyType != SerializedPropertyType.ObjectReference)
                    continue;

                UnityEngine.Object value = property.objectReferenceValue;
                if (value == null)
                    continue;
                if (referenceMap.TryGetValue(value, out UnityEngine.Object replacement))
                {
                    property.objectReferenceValue = replacement;
                    continue;
                }

                if (BelongsToHierarchy(value, templateRoot))
                    property.objectReferenceValue = null;
            }

            serialized.ApplyModifiedPropertiesWithoutUndo();
        }

        static void AuditNoTemplateHierarchyReferences(GameObject instance, GameObject templateRoot)
        {
            foreach (Component component in instance.GetComponents<Component>())
            {
                if (component == null)
                    throw new InvalidOperationException("Pilot root contains a missing component.");

                var serialized = new SerializedObject(component);
                SerializedProperty property = serialized.GetIterator();
                while (property.Next(true))
                {
                    if (property.propertyType != SerializedPropertyType.ObjectReference)
                        continue;
                    if (BelongsToHierarchy(property.objectReferenceValue, templateRoot))
                    {
                        throw new InvalidOperationException(
                            component.GetType().Name + "." + property.propertyPath +
                            " still references the vendor template hierarchy.");
                    }
                }
            }
        }

        static bool BelongsToHierarchy(UnityEngine.Object value, GameObject root)
        {
            if (value == null)
                return false;
            GameObject valueObject = value as GameObject;
            if (value is Component component)
                valueObject = component.gameObject;
            return valueObject != null &&
                   (valueObject == root || valueObject.transform.IsChildOf(root.transform));
        }

        static void BuildLabScene(GameObject prefab)
        {
            Scene alreadyLoaded = SceneManager.GetSceneByPath(ScenePath);
            if (alreadyLoaded.IsValid() && alreadyLoaded.isLoaded)
                throw new InvalidOperationException("Close the existing migration lab scene before rebuilding it.");

            Scene originalActiveScene = SceneManager.GetActiveScene();
            bool originalSceneWasDirty = originalActiveScene.IsValid() && originalActiveScene.isDirty;
            Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
            try
            {
                SceneManager.SetActiveScene(scene);
                var lab = new GameObject("InvectorMigrationLab");
                SceneManager.MoveGameObjectToScene(lab, scene);

                var cameraRoot = new GameObject("Lab Camera (Brawl-Owned)");
                cameraRoot.transform.SetParent(lab.transform, false);
                cameraRoot.transform.position = new Vector3(0f, 4.8f, -6.4f);
                cameraRoot.transform.rotation = Quaternion.Euler(37f, 0f, 0f);
                Camera camera = cameraRoot.AddComponent<Camera>();
                camera.clearFlags = CameraClearFlags.SolidColor;
                camera.backgroundColor = new Color(0.025f, 0.035f, 0.055f);
                cameraRoot.AddComponent<AudioListener>();
                BrawlCamera brawlCamera = cameraRoot.AddComponent<BrawlCamera>();
                brawlCamera.offset = new Vector3(0f, 4.8f, -6.4f);
                brawlCamera.smoothTime = 0.06f;
                brawlCamera.movementAutoTurn = false;
                brawlCamera.avoidObstructions = true;
                brawlCamera.obstructionMask = 1 << 9;

                var lightRoot = new GameObject("Lab Key Light");
                lightRoot.transform.SetParent(lab.transform, false);
                lightRoot.transform.rotation = Quaternion.Euler(42f, -28f, 0f);
                Light light = lightRoot.AddComponent<Light>();
                light.type = LightType.Directional;
                light.intensity = 1.2f;

                CreateCourseBlock(lab.transform, "Ground", 8,
                    new Vector3(0f, -0.15f, 0f), new Vector3(12f, 0.3f, 12f), Quaternion.identity);
                CreateCourseBlock(lab.transform, "Step", 8,
                    new Vector3(0f, 0.1f, 2.8f), new Vector3(2.4f, 0.2f, 0.9f), Quaternion.identity);
                CreateCourseBlock(lab.transform, "Slope", 8,
                    new Vector3(3.1f, 0.35f, 2.7f), new Vector3(2.2f, 0.25f, 3.2f),
                    Quaternion.Euler(-11f, 0f, 0f));
                CreateCourseBlock(lab.transform, "Collision Wall", 9,
                    new Vector3(0f, 1f, 5.7f), new Vector3(4.5f, 2f, 0.35f), Quaternion.identity);

                var pilot = (GameObject)PrefabUtility.InstantiatePrefab(prefab, scene);
                pilot.name = "CinderInvectorPilot_DISABLED";
                pilot.transform.SetParent(lab.transform, false);
                pilot.SetActive(false);
                brawlCamera.target = pilot.transform;

                var labController = lab.AddComponent<InvectorPhase3BLabController>();
                labController.Configure(pilot, brawlCamera);

                if (!EditorSceneManager.SaveScene(scene, ScenePath))
                    throw new InvalidOperationException("Unity failed to save " + ScenePath + ".");
            }
            finally
            {
                if (originalActiveScene.IsValid() && originalActiveScene.isLoaded)
                {
                    SceneManager.SetActiveScene(originalActiveScene);
                    if (originalSceneWasDirty)
                        EditorSceneManager.MarkSceneDirty(originalActiveScene);
                }
                if (scene.IsValid() && scene.isLoaded)
                    EditorSceneManager.CloseScene(scene, true);
            }
        }

        static GameObject CreateCourseBlock(
            Transform parent,
            string name,
            int layer,
            Vector3 position,
            Vector3 scale,
            Quaternion rotation)
        {
            GameObject block = GameObject.CreatePrimitive(PrimitiveType.Cube);
            block.name = name;
            block.layer = layer;
            block.transform.SetParent(parent, false);
            block.transform.localPosition = position;
            block.transform.localRotation = rotation;
            block.transform.localScale = scale;
            return block;
        }

        public static void ValidateGeneratedPrefab(GameObject prefab)
        {
            if (prefab == null || prefab.activeSelf)
                throw new InvalidOperationException("Phase 3A pilot root must exist and remain inactive.");
            if (prefab.layer != InvectorPlayerLayer || prefab.tag != "Player")
                throw new InvalidOperationException("Pilot root layer/tag disposition changed.");

            Animator[] animators = prefab.GetComponentsInChildren<Animator>(true);
            if (animators.Length != 1 || animators[0].transform != prefab.transform ||
                animators[0].enabled || animators[0].applyRootMotion ||
                animators[0].avatar == null || !animators[0].avatar.isValid || !animators[0].avatar.isHuman)
            {
                throw new InvalidOperationException("Pilot Animator/Avatar static-safety contract failed.");
            }

            var overrides = animators[0].runtimeAnimatorController as AnimatorOverrideController;
            var lifecycleController = overrides != null
                ? overrides.runtimeAnimatorController as AnimatorController
                : null;
            if (overrides == null ||
                !string.Equals(
                    AssetDatabase.GetAssetPath(overrides),
                    OverrideControllerPath,
                    StringComparison.Ordinal) ||
                lifecycleController == null ||
                !string.Equals(
                    AssetDatabase.GetAssetPath(lifecycleController),
                    LifecycleControllerPath,
                    StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    "Pilot Animator must use the pinned override over the project lifecycle controller.");
            }
            ValidateGeneratedLifecycleController(lifecycleController);

            RequireExactlyOne<Rigidbody>(prefab);
            RequireExactlyOne<CapsuleCollider>(prefab);
            RequireExactlyOne<vThirdPersonController>(prefab);
            RequireExactlyOne<BrawlInvectorThirdPersonController>(prefab);
            RequireExactlyOne<vThirdPersonInput>(prefab);
            RequireExactlyOne<InvectorShooterMeleeInputAdapter>(prefab);
            RequireExactlyOne<InvectorBrawlerMotor>(prefab);
            RequireExactlyOne<InvectorBrawlerAnimationDriver>(prefab);
            RequireExactlyOne<InvectorBrawlerWeaponPresentation>(prefab);
            RequireExactlyOne<vShooterManager>(prefab);
            RequireExactlyOne<vMeleeManager>(prefab);
            RequireExactlyOne<BrawlInvectorMeleePresentationManager>(prefab);
            RequireExactlyOne<vAmmoManager>(prefab);
            RequireExactlyOne<vCollectShooterMeleeControl>(prefab);

            if (prefab.GetComponents<Component>().Any(component =>
                    component != null && component.GetType() == typeof(vThirdPersonController)) ||
                prefab.GetComponents<Component>().Any(component =>
                    component != null && component.GetType() == typeof(vShooterMeleeInput)) ||
                prefab.GetComponents<Component>().Any(component =>
                    component != null && component.GetType() == typeof(vMeleeManager)))
            {
                throw new InvalidOperationException(
                    "Pilot must use only the project-owned controller, input, and melee presentation subclasses.");
            }

            if (prefab.GetComponents<MonoBehaviour>().OfType<IBrawlerAnimationDriver>().Count() != 1)
                throw new InvalidOperationException("Pilot must contain exactly one animation-driver authority.");
            if (prefab.GetComponents<MonoBehaviour>().OfType<IBrawlerMotor>().Count() != 1)
                throw new InvalidOperationException("Pilot must contain exactly one physical motor authority.");
            if (prefab.GetComponents<MonoBehaviour>().OfType<IBrawlerWeaponPresentation>().Count() != 1)
                throw new InvalidOperationException("Pilot must contain exactly one weapon-presentation authority.");

            var projectController = prefab.GetComponent<BrawlInvectorThirdPersonController>();
            var projectInput = prefab.GetComponent<InvectorShooterMeleeInputAdapter>();
            var projectMotor = prefab.GetComponent<InvectorBrawlerMotor>();
            var projectDriver = prefab.GetComponent<InvectorBrawlerAnimationDriver>();
            var weaponPresenter = prefab.GetComponent<InvectorBrawlerWeaponPresentation>();
            var shooter = prefab.GetComponent<vShooterManager>();
            var melee = prefab.GetComponent<BrawlInvectorMeleePresentationManager>();
            var ammo = prefab.GetComponent<vAmmoManager>();
            var dormancyFaults = new List<string>();
            if (projectController.freeSpeed == null) dormancyFaults.Add("controller.freeSpeed");
            if (projectController.strafeSpeed == null) dormancyFaults.Add("controller.strafeSpeed");
            if (projectController.autoCrouchLayer.value != 0) dormancyFaults.Add("controller.autoCrouchLayer");
            if (projectController.customFixedTimeStep != vThirdPersonMotor.CustomFixedTimeStep.Default)
                dormancyFaults.Add("controller.customFixedTimeStep");
            if (!projectInput.IsDormantConfigured) dormancyFaults.Add("input.IsDormantConfigured");
            if (!projectInput.HasProjectMoveAction) dormancyFaults.Add("input.HasProjectMoveAction");
            if (projectInput.PresentationAttackId != 0) dormancyFaults.Add("input.PresentationAttackId");
            if (!projectInput.HasConfiguredMotorBridge) dormancyFaults.Add("input.HasConfiguredMotorBridge");
            if (projectInput.MovementFeedMode != InvectorMovementFeedMode.LabProjectAction)
                dormancyFaults.Add("input.MovementFeedMode");
            // The move action lives on the shared project InputActionAsset, so
            // its global enabled flag is not evidence about this prefab: any
            // live actor (or a play-mode test host) enables it project-wide.
            // Dormancy requires that THIS adapter never enabled it itself.
            if (projectInput.ProjectMoveActionOwnedByAdapter)
                dormancyFaults.Add("input.ProjectMoveActionOwnedByAdapter");
            if (projectInput.ExternalFixedUpdateSubscriberCount != 0)
                dormancyFaults.Add("input.ExternalFixedUpdateSubscriberCount");
            if (!projectMotor.IsDormantConfigured) dormancyFaults.Add("motor.IsDormantConfigured");
            if (projectMotor.IsInitialized) dormancyFaults.Add("motor.IsInitialized");
            if (!projectDriver.IsDormantConfigured) dormancyFaults.Add("driver.IsDormantConfigured");
            if (!weaponPresenter.IsDormantConfigured) dormancyFaults.Add("weapon.IsDormantConfigured");
            if (weaponPresenter.HasRuntimeSolvers) dormancyFaults.Add("weapon.HasRuntimeSolvers");
            if (weaponPresenter.ProjectIKAdjustList == null) dormancyFaults.Add("weapon.ProjectIKAdjustList");
            if (!string.Equals(weaponPresenter.WeaponCategory, WeaponCategory, StringComparison.Ordinal))
                dormancyFaults.Add("weapon.WeaponCategory");
            if (weaponPresenter.WeaponHeldInLeftHand) dormancyFaults.Add("weapon.WeaponHeldInLeftHand");
            if (weaponPresenter.RuntimeHelperCount != 0) dormancyFaults.Add("weapon.RuntimeHelperCount");
            if (shooter.damageLayer.value != 0) dormancyFaults.Add("shooter.damageLayer");
            if (shooter.blockAimLayer.value != 0) dormancyFaults.Add("shooter.blockAimLayer");
            if (shooter.useCancelReload) dormancyFaults.Add("shooter.useCancelReload");
            if (shooter.useAmmoDisplay) dormancyFaults.Add("shooter.useAmmoDisplay");
            if (shooter.applyRecoilToCamera) dormancyFaults.Add("shooter.applyRecoilToCamera");
            if (shooter.useLockOn) dormancyFaults.Add("shooter.useLockOn");
            if (shooter.useLockOnMeleeOnly) dormancyFaults.Add("shooter.useLockOnMeleeOnly");
            if (shooter.hipfireShot) dormancyFaults.Add("shooter.hipfireShot");
            if (shooter.alwaysAiming) dormancyFaults.Add("shooter.alwaysAiming");
            if (shooter.weaponIKAdjustList != null) dormancyFaults.Add("shooter.weaponIKAdjustList");
            if (shooter.rWeapon != null) dormancyFaults.Add("shooter.rWeapon");
            if (shooter.lWeapon != null) dormancyFaults.Add("shooter.lWeapon");
            if (shooter.AllAmmoInfinity) dormancyFaults.Add("shooter.AllAmmoInfinity");
            if (melee.Members.Count != 0) dormancyFaults.Add("melee.Members");
            if (melee.leftWeapon != null) dormancyFaults.Add("melee.leftWeapon");
            if (melee.rightWeapon != null) dormancyFaults.Add("melee.rightWeapon");
            if (melee.SuppressedAttackWindowCount != 0) dormancyFaults.Add("melee.SuppressedAttackWindowCount");
            if (melee.BlockedDamageHitCount != 0) dormancyFaults.Add("melee.BlockedDamageHitCount");
            if (!Mathf.Approximately(melee.defaultStaminaCost, 0f)) dormancyFaults.Add("melee.defaultStaminaCost");
            if (!Mathf.Approximately(melee.defaultStaminaRecoveryDelay, 0f))
                dormancyFaults.Add("melee.defaultStaminaRecoveryDelay");
            if (ammo.ammoListData != null) dormancyFaults.Add("ammo.ammoListData");
            if (ammo.itemManager != null) dormancyFaults.Add("ammo.itemManager");
            if (ammo.ammos.Count != 0) dormancyFaults.Add("ammo.ammos");
            if (dormancyFaults.Count != 0)
            {
                throw new InvalidOperationException(
                    "Pilot dormant-safety contract failed: " +
                    string.Join(", ", dormancyFaults) + ".");
            }

            BrawlerHitProxy[] hitProxies = prefab.GetComponentsInChildren<BrawlerHitProxy>(true);
            Transform weaponVisual = FindDescendant(prefab.transform, "CinderStaffPresentation");
            Transform muzzle = weaponVisual != null
                ? FindDescendant(weaponVisual, "SpellOrigin")
                : null;
            Transform supportHand = weaponVisual != null
                ? FindDescendant(weaponVisual, "SupportHandTarget")
                : null;
            Transform supportHint = weaponVisual != null
                ? FindDescendant(weaponVisual, "SupportHintTarget")
                : null;
            ParticleSystem[] muzzleEffects = weaponVisual != null
                ? weaponVisual.GetComponentsInChildren<ParticleSystem>(true)
                : Array.Empty<ParticleSystem>();
            if (hitProxies.Length != 1 || hitProxies[0].transform.parent != prefab.transform ||
                hitProxies[0].enabled || !hitProxies[0].IsConfigured ||
                hitProxies[0].TriggerCollider.enabled ||
                weaponVisual == null || weaponVisual.gameObject.layer != 0 ||
                muzzle == null || muzzle.gameObject.layer != 12 ||
                supportHand == null || supportHand.gameObject.layer != 0 ||
                supportHint == null || supportHint.gameObject.layer != 0 ||
                muzzleEffects.Length != 1 || muzzleEffects[0].gameObject.layer != 12 ||
                muzzleEffects[0].main.playOnAwake)
            {
                throw new InvalidOperationException(
                    "Pilot weapon hierarchy, IK anchors, muzzle effect, or selective hit proxy changed.");
            }

            if (prefab.GetComponentsInChildren<CharacterController>(true).Length != 0 ||
                prefab.GetComponentsInChildren<NavMeshAgent>(true).Length != 0 ||
                prefab.GetComponentsInChildren<CharacterJoint>(true).Length != 0 ||
                prefab.GetComponentsInChildren<Camera>(true).Length != 0 ||
                prefab.GetComponentsInChildren<AudioListener>(true).Length != 0 ||
                prefab.GetComponentsInChildren<Canvas>(true).Length != 0)
            {
                throw new InvalidOperationException("Pilot contains a duplicate movement, camera, or UI authority.");
            }

            string[] forbiddenTypes =
            {
                "vRagdoll", "vDamageReceiver", "vHitBox", "vMeleeAttackObject",
                "vShooterWeapon", "vProjectileControl", "vObjectDamage", "vDamageSender",
                "vThirdPersonCamera", "vLockOnShooter", "vGenericAction", "vLadderAction",
                "BrawlerController", "PlayerBrawlerInput", "Health",
            };
            string[] presentTypes = prefab.GetComponentsInChildren<Component>(true)
                .Where(component => component != null)
                .Select(component => component.GetType().Name)
                .ToArray();
            string forbidden = forbiddenTypes.FirstOrDefault(presentTypes.Contains);
            if (forbidden != null)
                throw new InvalidOperationException("Pilot unexpectedly contains " + forbidden + ".");

            foreach (Behaviour behaviour in prefab.GetComponents<Behaviour>())
            {
                if (behaviour is Animator || RetainedRootBehaviourTypes.Contains(behaviour.GetType()))
                {
                    if (behaviour.enabled)
                        throw new InvalidOperationException(behaviour.GetType().Name + " must remain disabled.");
                }
            }
        }

        /// <summary>
        /// Audits the production-human variant without weakening the stricter
        /// lab-pilot contract above. The production asset adds only Brawl's
        /// compatibility facade and its closed runtime gate; every physical,
        /// input, animation, combat, and presentation authority remains inert.
        /// </summary>
        public static void ValidateProductionHumanPrefab(GameObject prefab)
        {
            if (prefab == null || prefab.activeSelf)
            {
                throw new InvalidOperationException(
                    "The production-human Invector prefab must exist and remain inactive.");
            }
            if (!string.Equals(
                    AssetDatabase.GetAssetPath(prefab),
                    ProductionHumanPrefabPath,
                    StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    "The production-human Invector prefab was saved at an unexpected path.");
            }
            if (PrefabUtility.GetPrefabAssetType(prefab) != PrefabAssetType.Variant)
            {
                throw new InvalidOperationException(
                    "The production-human Invector asset must remain a prefab variant.");
            }

            GameObject variantSource =
                PrefabUtility.GetCorrespondingObjectFromSource(prefab);
            if (variantSource == null ||
                !string.Equals(
                    AssetDatabase.GetAssetPath(variantSource),
                    PrefabPath,
                    StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    "The production-human Invector variant must derive directly from the validated pilot prefab.");
            }
            if (prefab.layer != InvectorPlayerLayer || prefab.tag != "Player")
            {
                throw new InvalidOperationException(
                    "The production-human root layer/tag disposition changed.");
            }

            RequireProductionExactlyOne<Rigidbody>(prefab);
            RequireProductionExactlyOne<CapsuleCollider>(prefab);
            RequireProductionExactlyOne<Animator>(prefab);
            RequireProductionExactlyOne<vThirdPersonController>(prefab);
            RequireProductionExactlyOne<BrawlInvectorThirdPersonController>(prefab);
            RequireProductionExactlyOne<vThirdPersonInput>(prefab);
            RequireProductionExactlyOne<InvectorShooterMeleeInputAdapter>(prefab);
            RequireProductionExactlyOne<InvectorBrawlerMotor>(prefab);
            RequireProductionExactlyOne<InvectorBrawlerAnimationDriver>(prefab);
            RequireProductionExactlyOne<InvectorBrawlerWeaponPresentation>(prefab);
            RequireProductionExactlyOne<vShooterManager>(prefab);
            RequireProductionExactlyOne<vMeleeManager>(prefab);
            RequireProductionExactlyOne<BrawlInvectorMeleePresentationManager>(prefab);
            RequireProductionExactlyOne<vAmmoManager>(prefab);
            RequireProductionExactlyOne<vCollectShooterMeleeControl>(prefab);
            RequireProductionExactlyOne<Health>(prefab);
            RequireProductionExactlyOne<BrawlerController>(prefab);
            RequireProductionExactlyOne<PlayerBrawlerInput>(prefab);
            RequireProductionExactlyOne<InvectorHumanRuntimeGate>(prefab);
            RequireProductionExactlyOne<InvectorBrawlerPrefabIdentity>(prefab);

            if (prefab.GetComponents<Component>().Any(component =>
                    component != null && component.GetType() == typeof(vThirdPersonController)) ||
                prefab.GetComponents<Component>().Any(component =>
                    component != null && component.GetType() == typeof(vShooterMeleeInput)) ||
                prefab.GetComponents<Component>().Any(component =>
                    component != null && component.GetType() == typeof(vMeleeManager)))
            {
                throw new InvalidOperationException(
                    "The production-human variant must retain only project-owned Invector subclasses.");
            }

            var animator = prefab.GetComponent<Animator>();
            var body = prefab.GetComponent<Rigidbody>();
            var capsule = prefab.GetComponent<CapsuleCollider>();
            var controller =
                prefab.GetComponent<BrawlInvectorThirdPersonController>();
            var input = prefab.GetComponent<InvectorShooterMeleeInputAdapter>();
            var motor = prefab.GetComponent<InvectorBrawlerMotor>();
            var animationDriver =
                prefab.GetComponent<InvectorBrawlerAnimationDriver>();
            var weaponPresenter =
                prefab.GetComponent<InvectorBrawlerWeaponPresentation>();
            var shooter = prefab.GetComponent<vShooterManager>();
            var melee = prefab.GetComponent<BrawlInvectorMeleePresentationManager>();
            var ammo = prefab.GetComponent<vAmmoManager>();
            var collect = prefab.GetComponent<vCollectShooterMeleeControl>();
            var health = prefab.GetComponent<Health>();
            var facade = prefab.GetComponent<BrawlerController>();
            var playerInput = prefab.GetComponent<PlayerBrawlerInput>();
            var runtimeGate = prefab.GetComponent<InvectorHumanRuntimeGate>();
            var identity = prefab.GetComponent<InvectorBrawlerPrefabIdentity>();

            if (animator.enabled || animator.applyRootMotion ||
                !animator.isHuman || animator.runtimeAnimatorController == null ||
                !body.isKinematic || body.useGravity ||
                body.constraints != RigidbodyConstraints.FreezeAll ||
                body.interpolation != RigidbodyInterpolation.None ||
                body.collisionDetectionMode != CollisionDetectionMode.Discrete ||
                capsule.enabled || controller.enabled || controller.useRootMotion ||
                !controller.isImmortal ||
                shooter.enabled || melee.enabled || ammo.enabled || collect.enabled)
            {
                throw new InvalidOperationException(
                    "The production-human Animator, physics body, controller, or vendor managers are not dormant.");
            }

            // Global InputAction.enabled state is shared project-wide and says
            // nothing about this prefab; dormancy only forbids the adapter
            // from having enabled the action itself.
            var humanFaults = new List<string>();
            if (!input.IsDormantConfigured) humanFaults.Add("input.IsDormantConfigured");
            if (input.RuntimeSchedulingEnabled) humanFaults.Add("input.RuntimeSchedulingEnabled");
            if (input.MovementFeedMode != InvectorMovementFeedMode.BufferedMotor)
                humanFaults.Add("input.MovementFeedMode");
            if (!input.HasConfiguredMotorBridge) humanFaults.Add("input.HasConfiguredMotorBridge");
            if (!input.HasProjectMoveAction) humanFaults.Add("input.HasProjectMoveAction");
            if (input.PresentationAttackId != 0) humanFaults.Add("input.PresentationAttackId");
            if (input.ProjectMoveActionOwnedByAdapter)
                humanFaults.Add("input.ProjectMoveActionOwnedByAdapter");
            if (input.ExternalFixedUpdateSubscriberCount != 0)
                humanFaults.Add("input.ExternalFixedUpdateSubscriberCount");
            if (!motor.IsDormantConfigured) humanFaults.Add("motor.IsDormantConfigured");
            if (motor.IsInitialized) humanFaults.Add("motor.IsInitialized");
            if (motor.HasConfiguredNavigationPlanner)
                humanFaults.Add("motor.HasConfiguredNavigationPlanner");
            if (!animationDriver.IsDormantConfigured) humanFaults.Add("driver.IsDormantConfigured");
            if (!weaponPresenter.IsDormantConfigured) humanFaults.Add("weapon.IsDormantConfigured");
            if (weaponPresenter.HasRuntimeSolvers) humanFaults.Add("weapon.HasRuntimeSolvers");
            if (!runtimeGate.IsDormantConfigured) humanFaults.Add("gate.IsDormantConfigured");
            if (runtimeGate.IsRuntimeActive) humanFaults.Add("gate.IsRuntimeActive");
            if (!string.IsNullOrEmpty(runtimeGate.FailureMessage)) humanFaults.Add("gate.FailureMessage");
            if (health.enabled) humanFaults.Add("health.enabled");
            if (facade.enabled) humanFaults.Add("facade.enabled");
            if (playerInput.enabled) humanFaults.Add("playerInput.enabled");
            if (runtimeGate.enabled) humanFaults.Add("gate.enabled");
            if (humanFaults.Count != 0)
            {
                throw new InvalidOperationException(
                    "Production-human dormant-safety contract failed: " +
                    string.Join(", ", humanFaults) + ".");
            }

            if (!identity.Matches("fire", InvectorBrawlerPrefabRole.Human))
            {
                throw new InvalidOperationException(
                    "The production-human Cinder prefab identity changed.");
            }

            if (!ReferenceEquals(facade.Motor, motor) ||
                !ReferenceEquals(facade.AnimationDriver, animationDriver) ||
                !ReferenceEquals(facade.WeaponPresentation, weaponPresenter))
            {
                throw new InvalidOperationException(
                    "The production-human BrawlerController did not retain the selected Invector authorities.");
            }

            if (shooter.damageLayer.value != 0 || shooter.blockAimLayer.value != 0 ||
                shooter.useCancelReload || shooter.useAmmoDisplay ||
                shooter.applyRecoilToCamera || shooter.useLockOn ||
                shooter.useLockOnMeleeOnly || shooter.hipfireShot ||
                shooter.alwaysAiming || shooter.weaponIKAdjustList != null ||
                shooter.rWeapon != null || shooter.lWeapon != null ||
                shooter.AllAmmoInfinity || melee.Members.Count != 0 ||
                melee.leftWeapon != null || melee.rightWeapon != null ||
                !Mathf.Approximately(melee.defaultStaminaCost, 0f) ||
                !Mathf.Approximately(melee.defaultStaminaRecoveryDelay, 0f) ||
                ammo.ammoListData != null || ammo.itemManager != null ||
                ammo.ammos.Count != 0)
            {
                throw new InvalidOperationException(
                    "The production-human vendor weapon, melee, ammo, or stamina firewall changed.");
            }

            BrawlerHitProxy[] hitProxies =
                prefab.GetComponentsInChildren<BrawlerHitProxy>(true);
            Transform weaponVisual =
                FindDescendant(prefab.transform, "CinderStaffPresentation");
            Transform muzzle = weaponVisual != null
                ? FindDescendant(weaponVisual, "SpellOrigin")
                : null;
            Transform supportHand = weaponVisual != null
                ? FindDescendant(weaponVisual, "SupportHandTarget")
                : null;
            Transform supportHint = weaponVisual != null
                ? FindDescendant(weaponVisual, "SupportHintTarget")
                : null;
            ParticleSystem[] muzzleEffects = weaponVisual != null
                ? weaponVisual.GetComponentsInChildren<ParticleSystem>(true)
                : Array.Empty<ParticleSystem>();
            bool childInheritedPhysicalLayer = prefab
                .GetComponentsInChildren<Transform>(true)
                .Any(candidate => candidate != prefab.transform &&
                                  candidate.gameObject.layer == InvectorPlayerLayer);
            if (hitProxies.Length != 1 ||
                hitProxies[0].transform.parent != prefab.transform ||
                !hitProxies[0].IsConfigured || hitProxies[0].enabled ||
                hitProxies[0].TriggerCollider.enabled ||
                weaponVisual == null || weaponVisual.gameObject.layer != 0 ||
                muzzle == null || muzzle.gameObject.layer != 12 ||
                supportHand == null || supportHand.gameObject.layer != 0 ||
                supportHint == null || supportHint.gameObject.layer != 0 ||
                muzzleEffects.Length != 1 || muzzleEffects[0].gameObject.layer != 12 ||
                muzzleEffects[0].main.playOnAwake || childInheritedPhysicalLayer)
            {
                throw new InvalidOperationException(
                    "The production-human selective layer, hit-proxy, weapon, or VFX topology changed.");
            }

            if (prefab.GetComponentsInChildren<CharacterController>(true).Length != 0 ||
                prefab.GetComponentsInChildren<NavMeshAgent>(true).Length != 0 ||
                prefab.GetComponentsInChildren<CharacterJoint>(true).Length != 0 ||
                prefab.GetComponentsInChildren<Camera>(true).Length != 0 ||
                prefab.GetComponentsInChildren<AudioListener>(true).Length != 0 ||
                prefab.GetComponentsInChildren<Canvas>(true).Length != 0)
            {
                throw new InvalidOperationException(
                    "The production-human variant contains a duplicate movement, navigation, camera, or UI authority.");
            }

            string[] forbiddenTypes =
            {
                "AIBrawler", "InvectorPhase3BLabController",
                "vRagdoll", "vDamageReceiver", "vHitBox", "vMeleeAttackObject",
                "vShooterWeapon", "vProjectileControl", "vObjectDamage", "vDamageSender",
                "vThirdPersonCamera", "vLockOnShooter", "vGenericAction", "vLadderAction",
            };
            string[] presentTypes = prefab.GetComponentsInChildren<Component>(true)
                .Where(component => component != null)
                .Select(component => component.GetType().Name)
                .ToArray();
            string forbidden = forbiddenTypes.FirstOrDefault(presentTypes.Contains);
            if (forbidden != null)
            {
                throw new InvalidOperationException(
                    "The production-human variant unexpectedly contains " + forbidden + ".");
            }
        }

        /// <summary>
        /// Audits the production-AI variant as one dormant Brawl tactical
        /// brain, one desired-velocity planner, and one Invector physical
        /// motor. The child NavMeshAgent must never own transform writes.
        /// </summary>
        public static void ValidateProductionAIPrefab(GameObject prefab)
        {
            if (prefab == null || prefab.activeSelf)
            {
                throw new InvalidOperationException(
                    "The production-AI Invector prefab must exist and remain inactive.");
            }
            if (!string.Equals(
                    AssetDatabase.GetAssetPath(prefab),
                    ProductionAIPrefabPath,
                    StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    "The production-AI Invector prefab was saved at an unexpected path.");
            }
            if (PrefabUtility.GetPrefabAssetType(prefab) != PrefabAssetType.Variant)
            {
                throw new InvalidOperationException(
                    "The production-AI Invector asset must remain a prefab variant.");
            }

            GameObject variantSource =
                PrefabUtility.GetCorrespondingObjectFromSource(prefab);
            if (variantSource == null ||
                !string.Equals(
                    AssetDatabase.GetAssetPath(variantSource),
                    PrefabPath,
                    StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    "The production-AI Invector variant must derive directly from the validated pilot prefab.");
            }
            if (prefab.layer != InvectorPlayerLayer || prefab.tag != "Player")
            {
                throw new InvalidOperationException(
                    "The production-AI root layer/tag disposition changed.");
            }

            RequireProductionAIExactlyOne<Rigidbody>(prefab, true);
            RequireProductionAIExactlyOne<CapsuleCollider>(prefab, true);
            RequireProductionAIExactlyOne<Animator>(prefab, true);
            RequireProductionAIExactlyOne<vThirdPersonController>(prefab, true);
            RequireProductionAIExactlyOne<BrawlInvectorThirdPersonController>(prefab, true);
            RequireProductionAIExactlyOne<vThirdPersonInput>(prefab, true);
            RequireProductionAIExactlyOne<InvectorShooterMeleeInputAdapter>(prefab, true);
            RequireProductionAIExactlyOne<InvectorBrawlerMotor>(prefab, true);
            RequireProductionAIExactlyOne<InvectorBrawlerAnimationDriver>(prefab, true);
            RequireProductionAIExactlyOne<InvectorBrawlerWeaponPresentation>(prefab, true);
            RequireProductionAIExactlyOne<vShooterManager>(prefab, true);
            RequireProductionAIExactlyOne<vMeleeManager>(prefab, true);
            RequireProductionAIExactlyOne<BrawlInvectorMeleePresentationManager>(prefab, true);
            RequireProductionAIExactlyOne<vAmmoManager>(prefab, true);
            RequireProductionAIExactlyOne<vCollectShooterMeleeControl>(prefab, true);
            RequireProductionAIExactlyOne<Health>(prefab, true);
            RequireProductionAIExactlyOne<BrawlerController>(prefab, true);
            RequireProductionAIExactlyOne<AIBrawler>(prefab, true);
            RequireProductionAIExactlyOne<InvectorBrawlerNavigation>(prefab, true);
            RequireProductionAIExactlyOne<InvectorAIRuntimeGate>(prefab, true);
            RequireProductionAIExactlyOne<InvectorBrawlerPrefabIdentity>(prefab, true);
            RequireProductionAIExactlyOne<NavMeshAgent>(prefab, false);

            if (prefab.GetComponents<Component>().Any(component =>
                    component != null && component.GetType() == typeof(vThirdPersonController)) ||
                prefab.GetComponents<Component>().Any(component =>
                    component != null && component.GetType() == typeof(vShooterMeleeInput)) ||
                prefab.GetComponents<Component>().Any(component =>
                    component != null && component.GetType() == typeof(vMeleeManager)))
            {
                throw new InvalidOperationException(
                    "The production-AI variant must retain only project-owned Invector subclasses.");
            }

            var animator = prefab.GetComponent<Animator>();
            var body = prefab.GetComponent<Rigidbody>();
            var capsule = prefab.GetComponent<CapsuleCollider>();
            var controller =
                prefab.GetComponent<BrawlInvectorThirdPersonController>();
            var input = prefab.GetComponent<InvectorShooterMeleeInputAdapter>();
            var motor = prefab.GetComponent<InvectorBrawlerMotor>();
            var animationDriver =
                prefab.GetComponent<InvectorBrawlerAnimationDriver>();
            var weaponPresenter =
                prefab.GetComponent<InvectorBrawlerWeaponPresentation>();
            var shooter = prefab.GetComponent<vShooterManager>();
            var melee = prefab.GetComponent<BrawlInvectorMeleePresentationManager>();
            var ammo = prefab.GetComponent<vAmmoManager>();
            var collect = prefab.GetComponent<vCollectShooterMeleeControl>();
            var health = prefab.GetComponent<Health>();
            var facade = prefab.GetComponent<BrawlerController>();
            var ai = prefab.GetComponent<AIBrawler>();
            var navigation = prefab.GetComponent<InvectorBrawlerNavigation>();
            var runtimeGate = prefab.GetComponent<InvectorAIRuntimeGate>();
            var identity = prefab.GetComponent<InvectorBrawlerPrefabIdentity>();
            NavMeshAgent planner = prefab.GetComponentInChildren<NavMeshAgent>(true);

            if (animator.enabled || animator.applyRootMotion ||
                !animator.isHuman || animator.runtimeAnimatorController == null ||
                !body.isKinematic || body.useGravity ||
                body.constraints != RigidbodyConstraints.FreezeAll ||
                body.interpolation != RigidbodyInterpolation.None ||
                body.collisionDetectionMode != CollisionDetectionMode.Discrete ||
                capsule.enabled || controller.enabled || controller.useRootMotion ||
                !controller.isImmortal ||
                shooter.enabled || melee.enabled || ammo.enabled || collect.enabled)
            {
                throw new InvalidOperationException(
                    "The production-AI Animator, physics body, controller, or vendor managers are not dormant.");
            }

            // Global InputAction.enabled state is shared project-wide and says
            // nothing about this prefab; dormancy only forbids the adapter
            // from having enabled the action itself.
            var aiFaults = new List<string>();
            if (!input.IsDormantConfigured) aiFaults.Add("input.IsDormantConfigured");
            if (input.RuntimeSchedulingEnabled) aiFaults.Add("input.RuntimeSchedulingEnabled");
            if (input.MovementFeedMode != InvectorMovementFeedMode.BufferedMotor)
                aiFaults.Add("input.MovementFeedMode");
            if (!input.HasConfiguredMotorBridge) aiFaults.Add("input.HasConfiguredMotorBridge");
            if (!input.HasProjectMoveAction) aiFaults.Add("input.HasProjectMoveAction");
            if (input.PresentationAttackId != 0) aiFaults.Add("input.PresentationAttackId");
            if (input.ProjectMoveActionOwnedByAdapter)
                aiFaults.Add("input.ProjectMoveActionOwnedByAdapter");
            if (input.ExternalFixedUpdateSubscriberCount != 0)
                aiFaults.Add("input.ExternalFixedUpdateSubscriberCount");
            if (!motor.IsDormantConfigured) aiFaults.Add("motor.IsDormantConfigured");
            if (motor.IsInitialized) aiFaults.Add("motor.IsInitialized");
            if (!motor.HasConfiguredNavigationPlanner)
                aiFaults.Add("motor.HasConfiguredNavigationPlanner");
            if (!animationDriver.IsDormantConfigured) aiFaults.Add("driver.IsDormantConfigured");
            if (!weaponPresenter.IsDormantConfigured) aiFaults.Add("weapon.IsDormantConfigured");
            if (weaponPresenter.HasRuntimeSolvers) aiFaults.Add("weapon.HasRuntimeSolvers");
            if (!navigation.IsDormantConfigured) aiFaults.Add("navigation.IsDormantConfigured");
            if (!runtimeGate.IsDormantConfigured) aiFaults.Add("gate.IsDormantConfigured");
            if (runtimeGate.IsRuntimeActive) aiFaults.Add("gate.IsRuntimeActive");
            if (!string.IsNullOrEmpty(runtimeGate.FailureMessage)) aiFaults.Add("gate.FailureMessage");
            if (health.enabled) aiFaults.Add("health.enabled");
            if (facade.enabled) aiFaults.Add("facade.enabled");
            if (ai.enabled) aiFaults.Add("ai.enabled");
            if (navigation.enabled) aiFaults.Add("navigation.enabled");
            if (runtimeGate.enabled) aiFaults.Add("gate.enabled");
            if (aiFaults.Count != 0)
            {
                throw new InvalidOperationException(
                    "Production-AI dormant-safety contract failed: " +
                    string.Join(", ", aiFaults) + ".");
            }

            if (!identity.Matches("fire", InvectorBrawlerPrefabRole.AI))
            {
                throw new InvalidOperationException(
                    "The production-AI Cinder prefab identity changed.");
            }

            if (!ReferenceEquals(facade.Motor, motor) ||
                !ReferenceEquals(facade.AnimationDriver, animationDriver) ||
                !ReferenceEquals(facade.WeaponPresentation, weaponPresenter) ||
                !ReferenceEquals(ai.Navigation, navigation) ||
                !ReferenceEquals(navigation.PlannerAgent, planner))
            {
                throw new InvalidOperationException(
                    "The production-AI facade, tactical brain, and planner references are not reciprocal.");
            }

            Component[] plannerComponents = planner.GetComponents<Component>();
            if (planner.transform.parent != prefab.transform ||
                !string.Equals(planner.name, ProductionAIPlannerName, StringComparison.Ordinal) ||
                planner.transform.localPosition != Vector3.zero ||
                planner.transform.localRotation != Quaternion.identity ||
                planner.transform.localScale != Vector3.one ||
                !planner.gameObject.activeSelf ||
                plannerComponents.Length != 2 ||
                plannerComponents.Any(component => component == null) ||
                planner.enabled ||
                planner.autoTraverseOffMeshLink || !planner.autoRepath ||
                !planner.autoBraking || planner.angularSpeed != 0f ||
                planner.acceleration < 40f ||
                !Mathf.Approximately(planner.baseOffset, 0f) ||
                !Mathf.Approximately(planner.stoppingDistance, 0.5f) ||
                planner.areaMask != NavMesh.AllAreas ||
                !Mathf.Approximately(planner.radius, capsule.radius) ||
                !Mathf.Approximately(planner.height, capsule.height))
            {
                throw new InvalidOperationException(
                    "The production-AI child NavMeshAgent is not a dormant non-transform-writing planner.");
            }

            if (prefab.GetComponents<MonoBehaviour>().OfType<IBrawlerMotor>().Count() != 1 ||
                prefab.GetComponents<MonoBehaviour>().OfType<IBrawlerNavigation>().Count() != 1 ||
                prefab.GetComponents<MonoBehaviour>().OfType<IBrawlerAnimationDriver>().Count() != 1 ||
                prefab.GetComponents<MonoBehaviour>().OfType<IBrawlerWeaponPresentation>().Count() != 1)
            {
                throw new InvalidOperationException(
                    "The production-AI variant must expose exactly one authority per project seam.");
            }

            if (shooter.damageLayer.value != 0 || shooter.blockAimLayer.value != 0 ||
                shooter.useCancelReload || shooter.useAmmoDisplay ||
                shooter.applyRecoilToCamera || shooter.useLockOn ||
                shooter.useLockOnMeleeOnly || shooter.hipfireShot ||
                shooter.alwaysAiming || shooter.weaponIKAdjustList != null ||
                shooter.rWeapon != null || shooter.lWeapon != null ||
                shooter.AllAmmoInfinity || melee.Members.Count != 0 ||
                melee.leftWeapon != null || melee.rightWeapon != null ||
                !Mathf.Approximately(melee.defaultStaminaCost, 0f) ||
                !Mathf.Approximately(melee.defaultStaminaRecoveryDelay, 0f) ||
                ammo.ammoListData != null || ammo.itemManager != null ||
                ammo.ammos.Count != 0)
            {
                throw new InvalidOperationException(
                    "The production-AI vendor weapon, melee, ammo, or stamina firewall changed.");
            }

            BrawlerHitProxy[] hitProxies =
                prefab.GetComponentsInChildren<BrawlerHitProxy>(true);
            Transform weaponVisual =
                FindDescendant(prefab.transform, "CinderStaffPresentation");
            Transform muzzle = weaponVisual != null
                ? FindDescendant(weaponVisual, "SpellOrigin")
                : null;
            Transform supportHand = weaponVisual != null
                ? FindDescendant(weaponVisual, "SupportHandTarget")
                : null;
            Transform supportHint = weaponVisual != null
                ? FindDescendant(weaponVisual, "SupportHintTarget")
                : null;
            ParticleSystem[] muzzleEffects = weaponVisual != null
                ? weaponVisual.GetComponentsInChildren<ParticleSystem>(true)
                : Array.Empty<ParticleSystem>();
            bool childInheritedPhysicalLayer = prefab
                .GetComponentsInChildren<Transform>(true)
                .Any(candidate => candidate != prefab.transform &&
                                  candidate.gameObject.layer == InvectorPlayerLayer);
            if (hitProxies.Length != 1 ||
                hitProxies[0].transform.parent != prefab.transform ||
                !hitProxies[0].IsConfigured || hitProxies[0].enabled ||
                hitProxies[0].TriggerCollider.enabled ||
                weaponVisual == null || weaponVisual.gameObject.layer != 0 ||
                muzzle == null || muzzle.gameObject.layer != 12 ||
                supportHand == null || supportHand.gameObject.layer != 0 ||
                supportHint == null || supportHint.gameObject.layer != 0 ||
                muzzleEffects.Length != 1 || muzzleEffects[0].gameObject.layer != 12 ||
                muzzleEffects[0].main.playOnAwake ||
                planner.gameObject.layer != 0 || childInheritedPhysicalLayer)
            {
                throw new InvalidOperationException(
                    "The production-AI selective layer, hit-proxy, planner, weapon, or VFX topology changed.");
            }

            if (prefab.GetComponentsInChildren<PlayerBrawlerInput>(true).Length != 0 ||
                prefab.GetComponentsInChildren<InvectorHumanRuntimeGate>(true).Length != 0 ||
                prefab.GetComponentsInChildren<CharacterController>(true).Length != 0 ||
                prefab.GetComponentsInChildren<NavMeshObstacle>(true).Length != 0 ||
                prefab.GetComponentsInChildren<CharacterJoint>(true).Length != 0 ||
                prefab.GetComponentsInChildren<Camera>(true).Length != 0 ||
                prefab.GetComponentsInChildren<AudioListener>(true).Length != 0 ||
                prefab.GetComponentsInChildren<Canvas>(true).Length != 0)
            {
                throw new InvalidOperationException(
                    "The production-AI variant contains a human input/gate or duplicate movement, camera, or UI authority.");
            }

            string[] forbiddenTypes =
            {
                "InvectorPhase3BLabController",
                "vSimpleMeleeAI_Motor", "vSimpleMeleeAI_Animator",
                "vSimpleMeleeAI_Controller", "vSimpleMeleeAI_Companion",
                "vSimpleMeleeAI_SphereSensor", "vSimpleMeleeAI_WeaponsControl",
                "vRagdoll", "vDamageReceiver", "vHitBox", "vMeleeAttackObject",
                "vShooterWeapon", "vProjectileControl", "vObjectDamage", "vDamageSender",
                "vThirdPersonCamera", "vLockOnShooter", "vGenericAction", "vLadderAction",
            };
            string[] presentTypes = prefab.GetComponentsInChildren<Component>(true)
                .Where(component => component != null)
                .Select(component => component.GetType().Name)
                .ToArray();
            string forbidden = forbiddenTypes.FirstOrDefault(presentTypes.Contains);
            if (forbidden != null)
            {
                throw new InvalidOperationException(
                    "The production-AI variant unexpectedly contains " + forbidden + ".");
            }
        }

        static void RequireProductionAIExactlyOne<T>(GameObject root, bool requireRoot)
            where T : Component
        {
            T[] components = root.GetComponentsInChildren<T>(true);
            if (components.Length != 1 ||
                (requireRoot && components[0].transform != root.transform) ||
                (!requireRoot && components[0].transform == root.transform))
            {
                throw new InvalidOperationException(
                    "Production-AI prefab must contain exactly one " +
                    (requireRoot ? "root " : "child ") + typeof(T).Name + ".");
            }
        }

        static void RequireProductionExactlyOne<T>(GameObject root)
            where T : Component
        {
            T[] components = root.GetComponentsInChildren<T>(true);
            if (components.Length != 1 || components[0].transform != root.transform)
            {
                throw new InvalidOperationException(
                    "Production-human prefab must contain exactly one root " +
                    typeof(T).Name + ".");
            }
        }

        static void RequireExactlyOne<T>(GameObject root) where T : Component
        {
            T[] components = root.GetComponentsInChildren<T>(true);
            if (components.Length != 1 || components[0].transform != root.transform)
                throw new InvalidOperationException("Pilot must contain exactly one root " + typeof(T).Name + ".");
        }

        static void RequireGuid(string path, string expected, string label)
        {
            string actual = AssetDatabase.AssetPathToGUID(path);
            if (!string.Equals(actual, expected, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException(
                    label + " GUID changed from " + expected + " to " + actual + "; re-audit first.");
            }
        }

        static void RequireLayer(int index, string expected)
        {
            string actual = LayerMask.LayerToName(index);
            if (!string.Equals(actual, expected, StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    "Layer " + index + " must be '" + expected + "' but is '" + actual + "'.");
            }
        }

        static Transform FindDescendant(Transform root, string name)
        {
            return root.GetComponentsInChildren<Transform>(true)
                .FirstOrDefault(candidate => candidate.name == name);
        }

        static void SetLayerRecursively(GameObject root, int layer)
        {
            foreach (Transform child in root.GetComponentsInChildren<Transform>(true))
                child.gameObject.layer = layer;
        }

        static void EnsureFolder(string path)
        {
            string normalized = path.TrimEnd('/').Replace('\\', '/');
            if (AssetDatabase.IsValidFolder(normalized))
                return;
            string parent = Path.GetDirectoryName(normalized)?.Replace('\\', '/');
            string name = Path.GetFileName(normalized);
            if (string.IsNullOrEmpty(parent) || string.IsNullOrEmpty(name))
                throw new InvalidOperationException("Invalid generated folder path: " + path);
            EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, name);
        }
    }
}
