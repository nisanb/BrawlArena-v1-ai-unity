using System;
using System.Collections.Generic;
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
    /// Shared hero-assembly library: builds the dormant per-hero Invector
    /// pilot, its inactive production-human and AI variants, from a validated
    /// character source. The Cinder-only reference pilot and its lab scene
    /// were retired with the fire roster slot; every roster builder (Rime,
    /// Thorn, ...) now calls the generic statics below directly. Production
    /// selection remains an explicit GameFlow context gate and is disabled in
    /// the generated Arena.
    /// </summary>
    public static class InvectorMigrationPilotBuilder
    {
        public const string Root = "Assets/Generated/InvectorMigration/Shared/";
        public const string LifecycleControllerPath =
            Root + "Controllers/BrawlSharedInvectorLifecycle.controller";
        public const string ProductionAIPlannerName =
            "InvectorNavigationPlanner_DISABLED";
        public const string WeaponCategory = "BrawlWizardStaff";

        public const string TemplatePath =
            "Assets/Invector-3rdPersonController/Shooter/Prefabs/Player/vShooterMelee_NoInventory.prefab";
        public const string CombinedControllerPath =
            "Assets/Invector-3rdPersonController/Shooter/Animator/Invector@ShooterMelee.controller";
        public const string DeathClipPath = "Assets/Generated/Wizards/Clips/Die.anim";
        public const string VictoryClipPath = "Assets/Generated/Wizards/Clips/VictoryStart.anim";
        public const string ProjectActionsPath = "Assets/InputSystem_Actions.inputactions";

        public const string TemplateGuid = "80dd0462ab7502b48a7fe99ea1cd882a";
        public const string CombinedControllerGuid = "87885946b43e2d1449e1d5aa2042f8a8";
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

        static readonly string[] CombinedLayerNames =
        {
            "Base Layer", "RightArm", "LeftArm", "OnlyArms",
            "UpperBody", "UnderBody", "Shot", "FullBody",
        };

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

        internal static void BuildWeaponPresentationPrefab(
            string characterPath,
            string authoredStaffName,
            string presentationName,
            string destinationPath,
            Color muzzleColor)
        {
            BuildWeaponPresentationPrefabCore(
                characterPath,
                authoredStaffName,
                presentationName,
                destinationPath,
                muzzleColor,
                null,
                null,
                null,
                null);
        }

        internal static void BuildWeaponPresentationPrefab(
            string characterPath,
            string authoredStaffName,
            string presentationName,
            string destinationPath,
            Color muzzleColor,
            Vector3 supportTargetLocalPosition,
            Vector3 supportTargetLocalEuler,
            Vector3 supportHintLocalPosition,
            Vector3 supportHintLocalEuler,
            Vector3 staffVisualLocalPosition,
            Vector3 staffVisualLocalEuler)
        {
            BuildWeaponPresentationPrefabCore(
                characterPath,
                authoredStaffName,
                presentationName,
                destinationPath,
                muzzleColor,
                supportTargetLocalPosition,
                supportTargetLocalEuler,
                supportHintLocalPosition,
                supportHintLocalEuler,
                staffVisualLocalPosition,
                staffVisualLocalEuler);
        }

        static void BuildWeaponPresentationPrefabCore(
            string characterPath,
            string authoredStaffName,
            string presentationName,
            string destinationPath,
            Color muzzleColor,
            Vector3? supportTargetLocalPosition,
            Vector3? supportTargetLocalEuler,
            Vector3? supportHintLocalPosition,
            Vector3? supportHintLocalEuler,
            Vector3? staffVisualLocalPosition = null,
            Vector3? staffVisualLocalEuler = null)
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
                Transform rightHand = animator != null && animator.isHuman
                    ? animator.GetBoneTransform(HumanBodyBones.RightHand)
                    : null;
                if (staff == null || staff.parent == null || leftHand == null ||
                    leftLowerArm == null || rightHand == null)
                {
                    throw new InvalidOperationException(
                        characterPath + " must expose " + authoredStaffName +
                        " plus valid Humanoid hands for its weapon presentation asset.");
                }

                var weaponRoot = new GameObject(presentationName);
                SceneManager.MoveGameObjectToScene(weaponRoot, previewScene);
                // Staffs in the RPG source prefabs sit under a pelvis-level
                // `Weapon` staging socket. Capture their authored world pose
                // relative to the actual weapon hand so the generated visual
                // follows that hand in every animation instead of floating
                // with the pelvis.
                weaponRoot.transform.SetParent(rightHand, false);
                weaponRoot.layer = 0;

                GameObject staffVisual = UnityEngine.Object.Instantiate(
                    staff.gameObject, weaponRoot.transform, true);
                staffVisual.name = "StaffVisual";
                if (staffVisualLocalPosition.HasValue)
                    staffVisual.transform.localPosition = staffVisualLocalPosition.Value;
                if (staffVisualLocalEuler.HasValue)
                    staffVisual.transform.localEulerAngles = staffVisualLocalEuler.Value;
                SetLayerRecursively(staffVisual, 0);
                staffVisual.SetActive(true);

                // Author the grip reference on the actual staff surface. A helper
                // placed at the hand bone would always report a perfect grip even
                // when the mesh is visibly floating, so it cannot be used as
                // contact evidence.
                if (!GameplayProbeRecorder.TryClosestMeshPoint(
                        staffVisual.transform, rightHand.position,
                        out Vector3 authoredGripPoint))
                {
                    throw new InvalidOperationException(
                        authoredStaffName + " has no readable staff mesh for its grip anchor.");
                }
                Transform gripAnchor = CreateAnchor(
                    staffVisual.transform,
                    "WeaponGripAnchor",
                    authoredGripPoint,
                    rightHand.rotation);
                gripAnchor.gameObject.layer = 0;

                Transform muzzle = FindDescendant(staffVisual.transform, "SpellOrigin");
                if (muzzle == null)
                    throw new InvalidOperationException(
                        "The " + authoredStaffName + " visual lost its SpellOrigin socket.");
                muzzle.gameObject.layer = 12;

                Transform supportHand = CreateAnchor(
                    weaponRoot.transform, "SupportHandTarget", leftHand.position, leftHand.rotation);
                Transform supportHint = CreateAnchor(
                    weaponRoot.transform, "SupportHintTarget", leftLowerArm.position, leftLowerArm.rotation);
                if (supportTargetLocalPosition.HasValue)
                    supportHand.localPosition = supportTargetLocalPosition.Value;
                if (supportTargetLocalEuler.HasValue)
                    supportHand.localEulerAngles = supportTargetLocalEuler.Value;
                if (supportHintLocalPosition.HasValue)
                    supportHint.localPosition = supportHintLocalPosition.Value;
                if (supportHintLocalEuler.HasValue)
                    supportHint.localEulerAngles = supportHintLocalEuler.Value;
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
            RequireGuid(DeathClipPath, DeathClipGuid, "shared hero death clip");
            RequireGuid(VictoryClipPath, VictoryClipGuid, "shared hero victory clip");
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
            var combined = AssetDatabase.LoadAssetAtPath<AnimatorController>(CombinedControllerPath);
            var deathClip = AssetDatabase.LoadAssetAtPath<AnimationClip>(DeathClipPath);
            var victoryClip = AssetDatabase.LoadAssetAtPath<AnimationClip>(VictoryClipPath);
            var inputActions = AssetDatabase.LoadAssetAtPath<InputActionAsset>(ProjectActionsPath);
            if (template == null || combined == null || deathClip == null ||
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
                    "The generated " + label +
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
                    "The generated " + label +
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
                EnsureAssetFolder(Root + "Controllers");
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
                controller.name = "BrawlSharedInvectorLifecycle";
                EditorUtility.SetDirty(controller);
                AssetDatabase.SaveAssets();
            }

            if (NormalizeLifecycleOverlay(fullBody))
            {
                EditorUtility.SetDirty(controller);
                AssetDatabase.SaveAssets();
            }

            bool attackTagsChanged;
            int attackStates = NormalizePresentationAttackTags(
                controller, out attackTagsChanged);
            if (attackStates != 2)
                throw new InvalidOperationException(
                    "The lifecycle controller must expose exactly the weak and strong " +
                    "AttackID-0 states used by staff and bow presentation.");
            if (attackTagsChanged)
            {
                EditorUtility.SetDirty(controller);
                AssetDatabase.SaveAssets();
            }

            ValidateGeneratedLifecycleController(controller);
            return controller;
        }

        static void EnsureAssetFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;
            string normalized = path.Replace('\\', '/').TrimEnd('/');
            int split = normalized.LastIndexOf('/');
            if (split <= 0 || split == normalized.Length - 1)
                throw new InvalidOperationException("Invalid asset folder path: " + path);
            string parent = normalized.Substring(0, split);
            string name = normalized.Substring(split + 1);
            EnsureAssetFolder(parent);
            AssetDatabase.CreateFolder(parent, name);
        }

        static int NormalizePresentationAttackTags(
            AnimatorController controller,
            out bool changed)
        {
            changed = false;
            int matches = 0;
            foreach (AnimatorControllerLayer layer in controller.layers)
                NormalizePresentationAttackTags(
                    layer.stateMachine, ref matches, ref changed);
            return matches;
        }

        static void NormalizePresentationAttackTags(
            AnimatorStateMachine machine,
            ref int matches,
            ref bool changed)
        {
            foreach (ChildAnimatorState child in machine.states)
            {
                string motionName =
                    child.state.motion != null ? child.state.motion.name : string.Empty;
                if (motionName != WizardBasicAttackOverrideSourceName &&
                    motionName != WizardSuperAttackOverrideSourceName)
                    continue;
                matches++;
                if (string.IsNullOrEmpty(child.state.tag)) continue;
                child.state.tag = string.Empty;
                changed = true;
            }
            foreach (ChildAnimatorStateMachine child in machine.stateMachines)
                NormalizePresentationAttackTags(
                    child.stateMachine, ref matches, ref changed);
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
        // Mixamo two-hand casts (action-MMO experiment); the carry pose stays
        // on the MagicWand idle because the staff support-hand IK was tuned
        // against it.
        public const string WizardBasicAttackClipPath =
            "Assets/ThirdParty/Mixamo/Frost/Mixamo_Frost_Cast1.fbx";
        public const string WizardBasicAttackClipName = "Mixamo_Frost_Cast1";
        public const string WizardSuperAttackClipPath =
            "Assets/ThirdParty/Mixamo/Frost/Mixamo_Frost_Cast2.fbx";
        public const string WizardSuperAttackClipName = "Mixamo_Frost_Cast2";
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

        // ---------------- Mixamo locomotion/reaction overrides ----------------

        const string MixamoRoot = "Assets/ThirdParty/Mixamo/";

        // Vendor source clip name -> Mixamo replacement. Unlike the attack
        // slots these sources may appear in several layers/blend trees of the
        // shared graph, so they are overridden per-occurrence rather than
        // through the exactly-one guard in ConfigurePresentationOverrides.
        static readonly (string Source, string Path, string Clip)[] LocomotionOverrideMap =
        {
            ("Idle", MixamoRoot + "Locomotion/Mixamo_Idle.fbx", "Mixamo_Idle"),
            ("Walk", MixamoRoot + "Locomotion/Mixamo_Walk.fbx", "Mixamo_Walk"),
            ("Run", MixamoRoot + "Locomotion/Mixamo_Run.fbx", "Mixamo_Run"),
            ("Sprint", MixamoRoot + "Locomotion/Mixamo_Sprint.fbx", "Mixamo_Sprint"),
            ("NewRollv2", MixamoRoot + "Locomotion/Mixamo_Roll.fbx", "Mixamo_Roll"),
            ("Hit Small Front", MixamoRoot + "Reactions/Mixamo_HitSmall.fbx", "Mixamo_HitSmall"),
            ("Hit Big Front", MixamoRoot + "Reactions/Mixamo_HitBig.fbx", "Mixamo_HitBig"),
            ("Recoil_Low", MixamoRoot + "Reactions/Mixamo_HitSmall.fbx", "Mixamo_HitSmall"),
            ("Recoil_Hard", MixamoRoot + "Reactions/Mixamo_HitBig.fbx", "Mixamo_HitBig"),
        };

        /// <summary>True for vendor sources owned by the shared Mixamo locomotion pass — hero attack validators ignore these pairs.</summary>
        internal static bool IsLocomotionOverrideSource(string sourceName)
        {
            for (int i = 0; i < LocomotionOverrideMap.Length; i++)
                if (string.Equals(LocomotionOverrideMap[i].Source, sourceName, StringComparison.Ordinal))
                    return true;
            return false;
        }

        /// <summary>
        /// Replaces the vendor locomotion, roll, and hit-reaction clips with
        /// the retargeted Mixamo set on every hero's override controller.
        /// </summary>
        internal static void ConfigureLocomotionOverrides(AnimatorOverrideController controller)
        {
            if (controller == null)
                throw new ArgumentNullException(nameof(controller));

            var overrides = new List<KeyValuePair<AnimationClip, AnimationClip>>(
                controller.overridesCount);
            controller.GetOverrides(overrides);

            for (int m = 0; m < LocomotionOverrideMap.Length; m++)
            {
                var entry = LocomotionOverrideMap[m];
                AnimationClip replacement = RequirePresentationClip(entry.Path, entry.Clip);
                int matched = 0;
                for (int i = 0; i < overrides.Count; i++)
                {
                    AnimationClip source = overrides[i].Key;
                    if (source == null ||
                        !string.Equals(source.name, entry.Source, StringComparison.Ordinal))
                        continue;
                    overrides[i] = new KeyValuePair<AnimationClip, AnimationClip>(
                        source, replacement);
                    matched++;
                }
                if (matched == 0)
                    throw new InvalidOperationException(
                        "The shared lifecycle graph exposes no '" + entry.Source +
                        "' locomotion source clip.");
            }

            controller.ApplyOverrides(overrides);
            EditorUtility.SetDirty(controller);
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

        internal static GameObject BuildPilotPrefab(
            AnimatorOverrideController overrideController,
            string characterPath,
            string pilotName,
            string authoredStaffName,
            string weaponPrefabPath,
            string weaponIKAdjustListPath,
            string weaponPresentationName,
            string destinationPath,
            bool weaponHeldInLeftHand = false)
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
                    weaponPresentationName,
                    weaponHeldInLeftHand);
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
            string weaponPresentationName,
            bool weaponHeldInLeftHand)
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
            Transform weaponParent = animator != null && animator.isHuman
                ? animator.GetBoneTransform(
                    weaponHeldInLeftHand
                        ? HumanBodyBones.LeftHand
                        : HumanBodyBones.RightHand)
                : null;
            if (weaponParent == null)
                throw new InvalidOperationException(
                    "The pilot has no valid hand parent for its weapon presentation.");
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
                    "The generated weapon presentation hierarchy is incomplete.");
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
                weaponHeldInLeftHand,
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
    }
}
