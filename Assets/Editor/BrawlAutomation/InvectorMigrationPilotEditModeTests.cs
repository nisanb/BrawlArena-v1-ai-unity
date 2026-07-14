using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using Invector;
using Invector.vCharacterController;
using Invector.vItemManager;
using Invector.vMelee;
using Invector.vShooter;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;

namespace BrawlArena.EditorAutomation.Tests
{
    public sealed class InvectorMigrationPilotEditModeTests
    {
        const string ProjectActionsPath = "Assets/InputSystem_Actions.inputactions";
        const string ProjectActionsGuid = "052faaac586de48259a63d0c4782560b";
        const string MoveActionId = "351f2ccd-1f9f-44bf-9bec-d62ac5c5f408";
        const string TagManagerPath = "ProjectSettings/TagManager.asset";
        const string InputActionsContract =
            "Phase 3B must consume the project Player/Move action for keyboard and gamepad movement.";
        const string ControllerSourcePath =
            "Assets/Scripts/Brawl/Integration/Invector/BrawlInvectorThirdPersonController.cs";
        const string InputAdapterSourcePath =
            "Assets/Scripts/Brawl/Integration/Invector/InvectorShooterMeleeInputAdapter.cs";
        const string MotorSourcePath =
            "Assets/Scripts/Brawl/Integration/Invector/InvectorBrawlerMotor.cs";
        const string AnimationDriverSourcePath =
            "Assets/Scripts/Brawl/Integration/Invector/InvectorBrawlerAnimationDriver.cs";
        const string LabControllerSourcePath =
            "Assets/Scripts/Brawl/Integration/Invector/InvectorPhase3BLabController.cs";
        const string MeleePresentationManagerSourcePath =
            "Assets/Scripts/Brawl/Integration/Invector/BrawlInvectorMeleePresentationManager.cs";
        const string LifecyclePresentationSourcePath =
            "Assets/Scripts/Brawl/Integration/Invector/BrawlInvectorLifecyclePresentation.cs";
        const string LifecycleMarkerSourcePath =
            "Assets/Scripts/Brawl/Integration/Invector/BrawlInvectorLifecycleStateMarker.cs";

        const BindingFlags InstanceMembers =
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        const BindingFlags StaticMembers =
            BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;

        static readonly string[] ExpectedLayers =
        {
            "Base Layer", "RightArm", "LeftArm", "OnlyArms",
            "UpperBody", "UnderBody", "Shot", "FullBody",
        };

        [Test]
        public void RequiredLayersSourceAssetsAndInputActionAssetMatchTheApprovedBaselines()
        {
            Assert.That(LayerMask.LayerToName(8), Is.EqualTo("Ground"));
            Assert.That(LayerMask.LayerToName(9), Is.EqualTo("WorldBlocker"));
            Assert.That(LayerMask.LayerToName(10), Is.EqualTo("BrawlerHitbox"));
            Assert.That(LayerMask.LayerToName(11), Is.EqualTo("Projectile"));
            Assert.That(LayerMask.LayerToName(12), Is.EqualTo("VFX"));
            Assert.That(LayerMask.LayerToName(13), Is.Empty);
            Assert.That(LayerMask.LayerToName(15), Is.Empty);
            Assert.That(LayerMask.LayerToName(23), Is.EqualTo("InvectorPlayer"));

            Assert.That(AssetDatabase.AssetPathToGUID(InvectorMigrationPilotBuilder.TemplatePath),
                Is.EqualTo(InvectorMigrationPilotBuilder.TemplateGuid).IgnoreCase);
            Assert.That(AssetDatabase.AssetPathToGUID(InvectorMigrationPilotBuilder.CombinedControllerPath),
                Is.EqualTo(InvectorMigrationPilotBuilder.CombinedControllerGuid).IgnoreCase);
            Assert.That(AssetDatabase.AssetPathToGUID(InvectorMigrationPilotBuilder.CinderPath),
                Is.EqualTo(InvectorMigrationPilotBuilder.CinderGuid).IgnoreCase);
            Assert.That(AssetDatabase.AssetPathToGUID(InvectorMigrationPilotBuilder.DeathClipPath),
                Is.EqualTo(InvectorMigrationPilotBuilder.DeathClipGuid).IgnoreCase);
            Assert.That(AssetDatabase.AssetPathToGUID(InvectorMigrationPilotBuilder.VictoryClipPath),
                Is.EqualTo(InvectorMigrationPilotBuilder.VictoryClipGuid).IgnoreCase);

            string tagManagerSource = ReadProjectSource(TagManagerPath);
            Assert.That(Regex.IsMatch(tagManagerSource, @"(?m)^\s*tags:\s*\[\]\s*$"), Is.True,
                "The deterministic project-settings contract is an empty serialized custom-tag list; " +
                "live editor tag-table contents are deliberately outside this test.");

            Assert.That(AssetDatabase.AssetPathToGUID(ProjectActionsPath),
                Is.EqualTo(ProjectActionsGuid).IgnoreCase, InputActionsContract);
            var actions = AssetDatabase.LoadAssetAtPath<InputActionAsset>(ProjectActionsPath);
            Assert.That(actions, Is.Not.Null, InputActionsContract);
            InputAction move = actions.FindAction("Player/Move", true);
            Assert.That(move.id, Is.EqualTo(Guid.Parse(MoveActionId)), InputActionsContract);
            Assert.That(move.type, Is.EqualTo(InputActionType.Value), InputActionsContract);
            Assert.That(move.expectedControlType, Is.EqualTo("Vector2"), InputActionsContract);
            Assert.That(move.bindings.Any(binding => binding.path == "<Gamepad>/leftStick"), Is.True,
                InputActionsContract);
            Assert.That(move.bindings.Any(binding => binding.path == "<Keyboard>/w"), Is.True,
                InputActionsContract);
            Assert.That(EditorBuildSettings.TryGetConfigObject(
                "com.unity.input.settings.actions", out InputActionAsset configuredActions), Is.True,
                InputActionsContract);
            Assert.That(configuredActions, Is.SameAs(actions), InputActionsContract);
            Assert.DoesNotThrow(InvectorMigrationPilotBuilder.ValidatePrerequisites);
        }

        [Test]
        public void GeneratedOutputsExistWithDistinctGuids()
        {
            Scene original = SceneManager.GetActiveScene();
            bool originalDirty = original.isDirty;
            string[] guids = OutputGuids();
            Assert.That(guids, Has.All.Not.Empty);
            Assert.That(guids.Distinct().Count(), Is.EqualTo(guids.Length));
            Assert.That(PrefabFingerprint(), Is.Not.Empty);
            Assert.That(SceneManager.GetActiveScene(), Is.EqualTo(original));
            Assert.That(original.isDirty, Is.EqualTo(originalDirty));
        }

        [Test]
        public void PrefabHasOneDormantProjectAdapterStackAndNoStockOrForbiddenTemplateSubsystems()
        {
            GameObject prefab = RequirePilotPrefab();
            Assert.DoesNotThrow(() => InvectorMigrationPilotBuilder.ValidateGeneratedPrefab(prefab));
            Assert.That(prefab.activeSelf, Is.False);
            Assert.That(prefab.layer, Is.EqualTo(23));
            Assert.That(prefab.tag, Is.EqualTo("Player"));
            Transform[] childTransforms = prefab.GetComponentsInChildren<Transform>(true)
                .Where(transform => transform != prefab.transform)
                .ToArray();
            Assert.That(childTransforms.All(transform =>
                transform.GetComponent<BrawlerHitProxy>() != null
                    ? transform.gameObject.layer == CombatPhysics.BrawlerHitboxLayer
                    : transform.name == "SpellOrigin" || transform.name == "BrawlMuzzleVfx"
                        ? transform.gameObject.layer == LayerMask.NameToLayer("VFX")
                        : transform.gameObject.layer == 0), Is.True,
                "Only the root, selective hit proxy, and muzzle VFX may leave Cinder's visual layer.");

            Animator animator = prefab.GetComponent<Animator>();
            Assert.That(animator.enabled, Is.False);
            Assert.That(animator.applyRootMotion, Is.False);
            Assert.That(animator.avatar, Is.Not.Null);
            Assert.That(animator.avatar.isValid, Is.True);
            Assert.That(animator.avatar.isHuman, Is.True);
            Assert.That(AssetDatabase.GetAssetPath(animator.avatar),
                Is.EqualTo("Assets/WizardPBR/Mesh/WizardBodyMesh.fbx"));

            Rigidbody rigidbody = prefab.GetComponent<Rigidbody>();
            Assert.That(rigidbody.isKinematic, Is.True);
            Assert.That(rigidbody.useGravity, Is.False);
            Assert.That(rigidbody.constraints, Is.EqualTo(RigidbodyConstraints.FreezeAll));
            Assert.That(rigidbody.interpolation, Is.EqualTo(RigidbodyInterpolation.None));
            Assert.That(rigidbody.collisionDetectionMode, Is.EqualTo(CollisionDetectionMode.Discrete));
            Assert.That(prefab.GetComponent<CapsuleCollider>().enabled, Is.False);

            var controller = prefab.GetComponent<BrawlInvectorThirdPersonController>();
            Assert.That(controller, Is.Not.Null);
            Assert.That(prefab.GetComponentsInChildren<vThirdPersonController>(true), Has.Length.EqualTo(1));
            Assert.That(controller.GetType(), Is.EqualTo(typeof(BrawlInvectorThirdPersonController)));
            Assert.That(prefab.GetComponents<Component>()
                .Count(component => component != null && component.GetType() == typeof(vThirdPersonController)),
                Is.Zero, "The stock controller must not coexist with the project controller subclass.");
            Assert.That(controller.enabled, Is.False);
            Assert.That(controller.MeleePresentationWriteCount, Is.Zero);
            Assert.That(controller.RecoilPresentationWriteCount, Is.Zero);
            Assert.That(controller.MeleeAttackTriggerResetCount, Is.Zero);
            Assert.That(controller.FullPresentationResetCount, Is.Zero);
            Assert.That(controller.HasPendingMeleePresentationTrigger, Is.False);
            Assert.That(controller.HasPendingRecoilPresentationTrigger, Is.False);
            Assert.That(controller.isImmortal, Is.True);
            Assert.That(controller.customFixedTimeStep, Is.EqualTo(vThirdPersonMotor.CustomFixedTimeStep.Default));
            Assert.That(controller.groundLayer.value, Is.EqualTo(1 << 8));
            Assert.That(controller.autoCrouchLayer.value, Is.Zero);
            Assert.That(controller.stopMoveLayer.value, Is.Zero);

            var input = prefab.GetComponent<InvectorShooterMeleeInputAdapter>();
            Assert.That(input, Is.Not.Null);
            Assert.That(prefab.GetComponentsInChildren<vThirdPersonInput>(true), Has.Length.EqualTo(1));
            Assert.That(input.GetType(), Is.EqualTo(typeof(InvectorShooterMeleeInputAdapter)));
            Assert.That(prefab.GetComponents<Component>()
                .Count(component => component != null && component.GetType() == typeof(vShooterMeleeInput)),
                Is.Zero, "The stock legacy-input component must be replaced, not retained beside the adapter.");
            Assert.That(input.enabled, Is.False);
            Assert.That(input.RuntimeSchedulingEnabled, Is.False);
            Assert.That(input.IsRuntimeStackReady, Is.False);
            Assert.That(input.IsPresentationStackReady, Is.False);
            Assert.That(input.HasBrawlCameraMovementReference, Is.False);
            Assert.That(input.IsDormantConfigured, Is.True);
            Assert.That(input.HasProjectMoveAction, Is.True);
            Assert.That(input.HasConfiguredMotorBridge, Is.True);
            Assert.That(input.MovementFeedMode, Is.EqualTo(InvectorMovementFeedMode.LabProjectAction));
            Assert.That(input.PresentationAttackId, Is.Zero);
            Assert.That(input.ProjectMoveActionEnabled, Is.False);
            Assert.That(input.ExternalFixedUpdateSubscriberCount, Is.Zero);
            Assert.That(input.cc, Is.SameAs(controller));
            Assert.That(input.horizontalInput.useInput, Is.False);
            Assert.That(input.verticalInput.useInput, Is.False);
            Assert.That(input.sprintInput.useInput, Is.False);
            Assert.That(input.crouchInput.useInput, Is.False);
            Assert.That(input.strafeInput.useInput, Is.False);
            Assert.That(input.jumpInput.useInput, Is.False);
            Assert.That(input.rollInput.useInput, Is.False);
            Assert.That(input.weakAttackInput.useInput, Is.False);
            Assert.That(input.strongAttackInput.useInput, Is.False);
            Assert.That(input.blockInput.useInput, Is.False);
            Assert.That(input.aimInput.useInput, Is.False);
            Assert.That(input.shotInput.useInput, Is.False);
            Assert.That(input.reloadInput.useInput, Is.False);
            Assert.That(input.switchCameraSideInput.useInput, Is.False);
            Assert.That(input.scopeViewInput.useInput, Is.False);

            var motor = prefab.GetComponent<InvectorBrawlerMotor>();
            Assert.That(motor, Is.Not.Null);
            Assert.That(prefab.GetComponentsInChildren<InvectorBrawlerMotor>(true), Has.Length.EqualTo(1));
            Assert.That(motor.enabled, Is.False);
            Assert.That(motor.IsInitialized, Is.False);
            Assert.That(motor.IsDormantConfigured, Is.True);
            IBrawlerMotor[] motors = prefab.GetComponents<MonoBehaviour>()
                .OfType<IBrawlerMotor>()
                .ToArray();
            Assert.That(motors, Has.Length.EqualTo(1));
            Assert.That(motors[0], Is.SameAs(motor));

            var shooter = prefab.GetComponent<vShooterManager>();
            foreach (string fieldName in new[]
            {
                nameof(vShooterManager.useCancelReload),
                nameof(vShooterManager.useAmmoDisplay),
                nameof(vShooterManager.applyRecoilToCamera),
                nameof(vShooterManager.useLockOn),
                nameof(vShooterManager.useLockOnMeleeOnly),
                nameof(vShooterManager.hipfireShot),
                nameof(vShooterManager.alwaysAiming),
            })
                AssertSerializedPublicBooleanField(typeof(vShooterManager), fieldName);

            Assert.That(shooter.enabled, Is.False);
            Assert.That(shooter.damageLayer.value, Is.Zero);
            Assert.That(shooter.blockAimLayer.value, Is.Zero);
            Assert.That(shooter.useCancelReload, Is.False);
            Assert.That(shooter.useAmmoDisplay, Is.False);
            Assert.That(shooter.applyRecoilToCamera, Is.False);
            Assert.That(shooter.useLockOn, Is.False);
            Assert.That(shooter.useLockOnMeleeOnly, Is.False);
            Assert.That(shooter.hipfireShot, Is.False);
            Assert.That(shooter.alwaysAiming, Is.False);
            Assert.That(shooter.weaponIKAdjustList, Is.Null);
            Assert.That(shooter.rWeapon, Is.Null);
            Assert.That(shooter.lWeapon, Is.Null);
            Assert.That(shooter.AllAmmoInfinity, Is.False);
            Assert.That(shooter.tpCamera, Is.Null);
            Assert.That(shooter.ammoDisplayR, Is.Null);
            Assert.That(shooter.ammoDisplayL, Is.Null);
            Assert.That(input.shooterManager, Is.SameAs(shooter));

            var melee = prefab.GetComponent<BrawlInvectorMeleePresentationManager>();
            Assert.That(melee, Is.Not.Null);
            Assert.That(prefab.GetComponentsInChildren<vMeleeManager>(true), Has.Length.EqualTo(1));
            Assert.That(prefab.GetComponents<Component>()
                .Count(component => component != null && component.GetType() == typeof(vMeleeManager)),
                Is.Zero, "The stock melee manager must be replaced by the presentation firewall.");
            Assert.That(melee.enabled, Is.False);
            Assert.That(melee.Members, Is.Empty);
            Assert.That(melee.leftWeapon, Is.Null);
            Assert.That(melee.rightWeapon, Is.Null);
            Assert.That(melee.defaultStaminaCost, Is.Zero);
            Assert.That(melee.defaultStaminaRecoveryDelay, Is.Zero);
            Assert.That(melee.SuppressedAttackWindowCount, Is.Zero);
            Assert.That(melee.BlockedDamageHitCount, Is.Zero);

            var driver = prefab.GetComponent<InvectorBrawlerAnimationDriver>();
            Assert.That(driver, Is.Not.Null);
            Assert.That(driver.enabled, Is.False);
            Assert.That(driver.PresentationRequestsEnabled, Is.False);
            Assert.That(driver.IsDormantConfigured, Is.True);
            IBrawlerAnimationDriver[] animationDrivers = prefab.GetComponents<MonoBehaviour>()
                .OfType<IBrawlerAnimationDriver>()
                .ToArray();
            Assert.That(animationDrivers, Has.Length.EqualTo(1));
            Assert.That(animationDrivers[0], Is.SameAs(driver));

            var weaponPresenter =
                prefab.GetComponent<InvectorBrawlerWeaponPresentation>();
            Assert.That(weaponPresenter, Is.Not.Null);
            Assert.That(weaponPresenter.enabled, Is.False);
            Assert.That(weaponPresenter.IsConfigured, Is.True);
            Assert.That(weaponPresenter.IsDormantConfigured, Is.True);
            Assert.That(weaponPresenter.LabRuntimeEnabled, Is.False);
            Assert.That(weaponPresenter.HasRuntimeSolvers, Is.False);
            Assert.That(weaponPresenter.RuntimeHelperCount, Is.Zero);
            Assert.That(weaponPresenter.ProjectIKAdjustList, Is.Not.Null);
            Assert.That(AssetDatabase.GetAssetPath(weaponPresenter.ProjectIKAdjustList),
                Is.EqualTo(InvectorMigrationPilotBuilder.WeaponIKAdjustListPath));
            Assert.That(weaponPresenter.WeaponCategory,
                Is.EqualTo(InvectorMigrationPilotBuilder.WeaponCategory));
            Assert.That(weaponPresenter.WeaponHeldInLeftHand, Is.False);
            IBrawlerWeaponPresentation[] weaponPresenters =
                prefab.GetComponents<MonoBehaviour>()
                    .OfType<IBrawlerWeaponPresentation>()
                    .ToArray();
            Assert.That(weaponPresenters, Has.Length.EqualTo(1));
            Assert.That(weaponPresenters[0], Is.SameAs(weaponPresenter));

            var ikList = AssetDatabase.LoadAssetAtPath<vWeaponIKAdjustList>(
                InvectorMigrationPilotBuilder.WeaponIKAdjustListPath);
            var ikAdjust = AssetDatabase.LoadAssetAtPath<vWeaponIKAdjust>(
                InvectorMigrationPilotBuilder.WeaponIKAdjustPath);
            GameObject weaponPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(
                InvectorMigrationPilotBuilder.WeaponPrefabPath);
            Assert.That(ikList, Is.Not.Null);
            Assert.That(ikAdjust, Is.Not.Null);
            Assert.That(weaponPrefab, Is.Not.Null);
            Assert.That(ikList.weaponIKAdjusts, Has.Count.EqualTo(1));
            Assert.That(ikList.weaponIKAdjusts[0], Is.SameAs(ikAdjust));
            CollectionAssert.AreEqual(
                new[] { InvectorMigrationPilotBuilder.WeaponCategory },
                ikAdjust.weaponCategories);
            Assert.That(ikAdjust.HasAllDefaultStates(), Is.True);
            CollectionAssert.AreEqual(
                vWeaponIKAdjust.defaultNames,
                ikAdjust.ikAdjustsLeft.Select(adjust => adjust.name).ToArray());
            CollectionAssert.AreEqual(
                vWeaponIKAdjust.defaultNames,
                ikAdjust.ikAdjustsRight.Select(adjust => adjust.name).ToArray());

            BrawlerHitProxy[] proxies =
                prefab.GetComponentsInChildren<BrawlerHitProxy>(true);
            Assert.That(proxies, Has.Length.EqualTo(1));
            Assert.That(proxies[0].transform.parent, Is.SameAs(prefab.transform));
            Assert.That(proxies[0].enabled, Is.False);
            Assert.That(proxies[0].IsConfigured, Is.True);
            Assert.That(proxies[0].TriggerCollider.enabled, Is.False);
            Assert.That(proxies[0].TriggerCollider.isTrigger, Is.True);
            Assert.That(prefab.GetComponentsInChildren<SphereCollider>(true),
                Has.Length.EqualTo(1));

            AssertSerializedReference(input, "configuredController", controller);
            AssertSerializedReference(input, "configuredShooterManager", shooter);
            AssertSerializedReference(input, "configuredMeleeManager", melee);
            AssertSerializedFieldType(
                typeof(InvectorShooterMeleeInputAdapter), "configuredMeleeManager",
                typeof(BrawlInvectorMeleePresentationManager));
            AssertSerializedReference(input, "projectInputActions",
                AssetDatabase.LoadAssetAtPath<InputActionAsset>(ProjectActionsPath));
            AssertSerializedReference(input, "movementReference", null);
            AssertSerializedReference(input, "configuredMotor", motor);
            AssertSerializedFieldType(
                typeof(InvectorShooterMeleeInputAdapter), "movementReference", typeof(BrawlCamera));
            AssertSerializedReference(motor, "configuredController", controller);
            AssertSerializedReference(motor, "configuredScheduler", input);
            AssertSerializedReference(motor, "configuredBody", rigidbody);
            AssertSerializedReference(motor, "configuredCapsule", prefab.GetComponent<CapsuleCollider>());
            AssertSerializedReference(driver, "controller", controller);
            AssertSerializedReference(driver, "input", input);

            var ammo = prefab.GetComponent<vAmmoManager>();
            Assert.That(ammo.enabled, Is.False);
            Assert.That(ammo.ammoListData, Is.Null);
            Assert.That(ammo.itemManager, Is.Null);
            Assert.That(ammo.ammos, Is.Empty);
            Assert.That(prefab.GetComponent<vCollectShooterMeleeControl>().enabled, Is.False);
            Assert.That(prefab.GetComponents<Behaviour>().All(behaviour => !behaviour.enabled), Is.True,
                "Every retained root scheduler/writer Behaviour must remain disabled in Phase 3A.");

            Assert.That(prefab.GetComponentsInChildren<Rigidbody>(true), Has.Length.EqualTo(1));
            Assert.That(prefab.GetComponentsInChildren<CapsuleCollider>(true), Has.Length.EqualTo(1));
            Assert.That(prefab.GetComponentsInChildren<CharacterController>(true), Is.Empty);
            Assert.That(prefab.GetComponentsInChildren<NavMeshAgent>(true), Is.Empty);
            Assert.That(prefab.GetComponentsInChildren<CharacterJoint>(true), Is.Empty);
            Assert.That(prefab.GetComponentsInChildren<Camera>(true), Is.Empty);
            Assert.That(prefab.GetComponentsInChildren<AudioListener>(true), Is.Empty);
            Assert.That(prefab.GetComponentsInChildren<Canvas>(true), Is.Empty);
            Assert.That(prefab.GetComponentsInChildren<EventSystem>(true), Is.Empty);

            string[] typeNames = prefab.GetComponentsInChildren<Component>(true)
                .Where(component => component != null)
                .Select(component => component.GetType().Name)
                .ToArray();
            foreach (string forbidden in new[]
            {
                "vRagdoll", "vDamageReceiver", "vHitBox", "vMeleeAttackObject",
                "vShooterWeapon", "vProjectileControl", "vObjectDamage", "vDamageSender",
                "vThirdPersonCamera", "vLockOnShooter", "vGenericAction", "vLadderAction",
                "BrawlerController", "PlayerBrawlerInput", "Health",
            })
                CollectionAssert.DoesNotContain(typeNames, forbidden);
        }

        [Test]
        public void MeleePresentationManagerTerminatesAttackWindowsAndFailsClosedOnDamage()
        {
            var root = new GameObject("Melee Presentation Firewall Test");
            try
            {
                var melee = root.AddComponent<BrawlInvectorMeleePresentationManager>();
                melee.enabled = false;
                melee.Members.Clear();
                melee.leftWeapon = null;
                melee.rightWeapon = null;

                melee.SetActiveAttack(
                    new List<string> { "RightHand" },
                    vAttackType.Unarmed,
                    true,
                    7,
                    11,
                    13,
                    true,
                    true,
                    2f,
                    "ForbiddenVendorDamage");
                melee.SetActiveAttack(
                    "LeftLowerArm",
                    vAttackType.MeleeWeapon,
                    false,
                    9,
                    17,
                    19,
                    true,
                    true,
                    3f,
                    "ForbiddenWeaponDamage");

                Assert.That(melee.SuppressedAttackWindowCount, Is.EqualTo(2));
                Assert.That(melee.SuppressedAttackWindowEnableCount, Is.EqualTo(1));
                Assert.That(melee.SuppressedAttackWindowDisableCount, Is.EqualTo(1));
                Assert.That(melee.SuppressedListAttackWindowCount, Is.EqualTo(1));
                Assert.That(melee.SuppressedSingleAttackWindowCount, Is.EqualTo(1));
                Assert.That(melee.Members, Is.Empty);
                Assert.That(melee.leftWeapon, Is.Null);
                Assert.That(melee.rightWeapon, Is.Null);

                var hitInfo = new vHitInfo(null, null, null, Vector3.zero);
                Assert.Throws<NotSupportedException>(() => melee.OnDamageHit(ref hitInfo));
                Assert.That(melee.BlockedDamageHitCount, Is.EqualTo(1));

                melee.ResetPresentationTrace();
                Assert.That(melee.SuppressedAttackWindowCount, Is.Zero);
                Assert.That(melee.SuppressedAttackWindowEnableCount, Is.Zero);
                Assert.That(melee.SuppressedAttackWindowDisableCount, Is.Zero);
                Assert.That(melee.SuppressedListAttackWindowCount, Is.Zero);
                Assert.That(melee.SuppressedSingleAttackWindowCount, Is.Zero);
                Assert.That(melee.BlockedDamageHitCount, Is.Zero);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(root);
            }
        }

        [Test]
        public void DormantSemanticDriverDropsLifecyclePresentationWithoutThrowingOrWritingGraphState()
        {
            GameObject prefab = RequirePilotPrefab();
            Scene preview = EditorSceneManager.NewPreviewScene();
            try
            {
                var instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab, preview);
                Assert.That(instance.activeSelf, Is.False);

                var driver = instance.GetComponent<InvectorBrawlerAnimationDriver>();
                var input = instance.GetComponent<InvectorShooterMeleeInputAdapter>();
                var motor = instance.GetComponent<InvectorBrawlerMotor>();
                var controller = instance.GetComponent<BrawlInvectorThirdPersonController>();
                var shooter = instance.GetComponent<vShooterManager>();
                Animator animator = instance.GetComponent<Animator>();
                Rigidbody body = instance.GetComponent<Rigidbody>();
                Vector3 inputBefore = controller.input;
                Vector3 positionBefore = instance.transform.position;
                Quaternion rotationBefore = instance.transform.rotation;

                Assert.That(driver.IsDormantConfigured, Is.True);
                Assert.That(input.IsDormantConfigured, Is.True);
                Assert.That(input.HasConfiguredMotorBridge, Is.True);
                Assert.That(input.MovementFeedMode, Is.EqualTo(InvectorMovementFeedMode.LabProjectAction));
                Assert.That(motor.IsDormantConfigured, Is.True);
                Assert.That(motor.IsInitialized, Is.False);
                Assert.DoesNotThrow(() => driver.TickLocomotion(1f));
                Assert.That(controller.input, Is.EqualTo(inputBefore));
                Assert.That(instance.transform.position, Is.EqualTo(positionBefore));
                Assert.That(instance.transform.rotation, Is.EqualTo(rotationBefore));
                Assert.That(animator.enabled, Is.False);
                Assert.That(controller.enabled, Is.False);
                Assert.That(controller.MeleePresentationWriteCount, Is.Zero);
                Assert.That(controller.RecoilPresentationWriteCount, Is.Zero);
                Assert.That(controller.LifecyclePresentationWriteCount, Is.Zero);
                Assert.That(controller.LastPresentationAttackId, Is.Zero);
                Assert.That(controller.LastPresentationRecoilId, Is.Zero);
                Assert.That(controller.LastLifecyclePresentation,
                    Is.EqualTo(BrawlInvectorLifecyclePresentation.None));
                Assert.That(input.enabled, Is.False);
                Assert.That(motor.enabled, Is.False);
                Assert.That(driver.enabled, Is.False);

                MethodInfo activationSafety = typeof(InvectorShooterMeleeInputAdapter)
                    .GetMethod("IsActivationSafe", StaticMembers);
                Assert.That(activationSafety, Is.Not.Null,
                    "The project adapter must own the Rigidbody activation predicate.");
                Assert.That(activationSafety.DeclaringType,
                    Is.EqualTo(typeof(InvectorShooterMeleeInputAdapter)));
                Assert.That((bool)activationSafety.Invoke(null, new object[] { body }), Is.False,
                    "The builder's kinematic FreezeAll body must never pass the live scheduler gate.");
                body.isKinematic = false;
                body.constraints = RigidbodyConstraints.FreezeRotation;
                Assert.That((bool)activationSafety.Invoke(null, new object[] { body }), Is.True);
                body.constraints = RigidbodyConstraints.FreezeAll;
                Assert.That((bool)activationSafety.Invoke(null, new object[] { body }), Is.False);
                body.isKinematic = true;

                var cameraObject = new GameObject("Explicit BrawlCamera Authority");
                SceneManager.MoveGameObjectToScene(cameraObject, preview);
                var brawlCamera = cameraObject.AddComponent<BrawlCamera>();
                input.SetMovementReference(brawlCamera);
                Assert.That(input.HasBrawlCameraMovementReference, Is.True,
                    "Only an active BrawlCamera in the adapter's scene may supply movement yaw.");
                brawlCamera.enabled = false;
                Assert.That(input.HasBrawlCameraMovementReference, Is.False);
                input.SetMovementReference(null);

                input.LockCamera = true;
                input.LockAiming = true;
                input.LockHipFireAiming = true;
                Assert.That(input.LockCamera, Is.False);
                Assert.That(input.LockAiming, Is.False);
                Assert.That(input.LockHipFireAiming, Is.False);
                Assert.That(input.rotateToLockTargetConditions, Is.False);
                Assert.That(input.cameraMain, Is.Null);
                Assert.That(input.controlAimCanvas, Is.Null);
                Assert.DoesNotThrow(() => input.SetLockBasicInput(false));
                Assert.DoesNotThrow(() => input.SetLockAllInput(false));
                Assert.DoesNotThrow(() => input.SetLockCameraInput(false));
                Assert.DoesNotThrow(() => input.SetLockShooterInput(false));
                Assert.DoesNotThrow(() => input.SetAlwaysAim(true));
                Assert.DoesNotThrow(input.EnableScopeView);
                Assert.DoesNotThrow(input.DisableScopeView);
                Assert.That(shooter.alwaysAiming, Is.False,
                    "External Invector events cannot claim aim/camera authority through the dormant adapter.");

                Assert.Throws<InvalidOperationException>(driver.PlayBasicAttack);
                Assert.Throws<InvalidOperationException>(driver.PlaySuper);
                Assert.Throws<InvalidOperationException>(driver.PlayHitReaction);
                Assert.DoesNotThrow(driver.PlayDeath);
                Assert.DoesNotThrow(driver.PlayRespawn);
                Assert.DoesNotThrow(driver.PlayVictory);
                Assert.That(driver.DeathRequestCount, Is.EqualTo(1));
                Assert.That(driver.RespawnRequestCount, Is.EqualTo(1));
                Assert.That(driver.VictoryRequestCount, Is.EqualTo(1));
                Assert.That(driver.DroppedLifecycleRequestCount, Is.EqualTo(3),
                    "A dormant presentation stack must account for lifecycle requests without throwing.");
                Assert.That(driver.LifecycleFaultCount, Is.Zero,
                    "A closed gate is an intentional dropped request, not a runtime presentation fault.");
                Assert.That(driver.LastLifecycleRequest,
                    Is.EqualTo(BrawlInvectorLifecyclePresentation.Victory));

                Assert.Throws<InvalidOperationException>(input.TriggerWeakAttack);
                Assert.Throws<InvalidOperationException>(input.TriggerStrongAttack);
                Assert.Throws<InvalidOperationException>(() => input.OnRecoil(0));
                Assert.Throws<NotSupportedException>(() => input.OnReceiveAttack(null, null));
                Assert.Throws<InvalidOperationException>(() => input.SetRuntimeSchedulingEnabled(true));
                Assert.Throws<InvalidOperationException>(() => driver.SetPresentationRequestsEnabled(true));

                Assert.That(controller.FallDamageConditions(), Is.False);
                Assert.DoesNotThrow(controller.StaminaRecovery);
                Assert.Throws<NotSupportedException>(() => controller.TakeDamage(null));
                Assert.Throws<NotSupportedException>(() => controller.AddHealth(1));
                Assert.Throws<NotSupportedException>(() => controller.ChangeHealth(1));
                Assert.Throws<NotSupportedException>(() => controller.ResetHealth(1f));
                Assert.Throws<NotSupportedException>(controller.ResetHealth);
                Assert.Throws<NotSupportedException>(() => controller.ChangeMaxHealth(1));
                Assert.Throws<NotSupportedException>(() => controller.SetHealthRecovery(1f));
                Assert.Throws<NotSupportedException>(() => controller.ReduceStamina(1f, false));
                Assert.Throws<NotSupportedException>(() => controller.ChangeStamina(1));
                Assert.Throws<NotSupportedException>(() => controller.ChangeMaxStamina(1));

                Assert.That(driver.PresentationRequestsEnabled, Is.False);
                Assert.That(input.RuntimeSchedulingEnabled, Is.False);
                Assert.That(input.WeakAttackRequestCount, Is.Zero);
                Assert.That(input.StrongAttackRequestCount, Is.Zero);
                Assert.That(controller.MeleePresentationWriteCount, Is.Zero);
                Assert.That(controller.RecoilPresentationWriteCount, Is.Zero);
                Assert.That(controller.LifecyclePresentationWriteCount, Is.Zero);
                Assert.That(controller.LastPresentationAttackId, Is.Zero);
                Assert.That(controller.LastPresentationRecoilId, Is.Zero);
                Assert.That(controller.LastLifecyclePresentation,
                    Is.EqualTo(BrawlInvectorLifecyclePresentation.None));
                Assert.That(controller.HasPendingMeleePresentationTrigger, Is.False);
                Assert.That(controller.HasPendingRecoilPresentationTrigger, Is.False);
                Assert.That(controller.HasPendingLifecyclePresentationTrigger, Is.False);
                Assert.That(motor.IsDormantConfigured, Is.True);
                Assert.That(animator.enabled, Is.False);
                Assert.That(controller.enabled, Is.False);
                Assert.That(instance.activeSelf, Is.False);
            }
            finally
            {
                EditorSceneManager.ClosePreviewScene(preview);
            }
        }

        [Test]
        public void ProjectAdaptersOwnEveryDangerousPathAndSourceContainsNoCompetingAuthorityCalls()
        {
            foreach (string method in new[]
            {
                "Start", "CharacterInit", "Update", "FixedUpdate", "LateUpdate",
                "InputHandle", "MoveInput", "ControlRotation", "FindHUD", "FindCamera", "UpdateHUD",
                "CameraInput", "UpdateCameraStates", "ChangeCameraState", "ChangeCameraStateWithLerp",
                "ChangeCameraStateNoLerp", "ResetCameraState", "ResetCameraAngleSmooth",
                "ResetCameraAngleWithoutSmooth", "ShowCursor", "LockCursor", "SetLockBasicInput",
                "SetLockAllInput", "SetLockCameraInput", "SetLockShooterInput", "SetAlwaysAim",
                "OnAnimatorMoveEvent", "EnableOnAnimatorMove", "DisableOnAnimatorMove", "SprintInput",
                "CrouchInput", "StrafeInput", "JumpInput",
                "RollInput", "MeleeWeakAttackInput", "MeleeStrongAttackInput", "BlockingInput",
                "AimInput", "ShotInput", "HandleShotCount", "DoShots", "TriggerShot", "ReloadInput",
                "SwitchCameraSideInput", "SwitchCameraSide", "ScopeViewInput", "CancelAiming",
                "EnableScopeView", "DisableScopeView", "TriggerWeakAttack", "TriggerStrongAttack",
                "OnRecoil", "OnReceiveAttack", "OnEnableAttack", "OnDisableAttack",
                "ResetAttackTriggers", "BreakAttack", "SetMovementReference", "ResetRuntimeTrace",
                "ConfigureMotorBridge", "SelectMovementFeedMode", "PrepareLifecyclePresentation",
            })
            {
                AssertDeclaresMethod(typeof(InvectorShooterMeleeInputAdapter), method);
            }

            foreach (string property in new[]
            {
                "UseAnimatorMove", "isAimingByHipFire", "cameraMain", "controlAimCanvas",
                "LockCamera", "LockAiming", "LockHipFireAiming", "rotateToLockTargetConditions",
            })
                AssertDeclaresProperty(typeof(InvectorShooterMeleeInputAdapter), property);

            AssertDeclaresMethodSignature(
                typeof(InvectorShooterMeleeInputAdapter), "SetMovementReference", typeof(BrawlCamera));
            AssertDeclaresMethodSignature(
                typeof(InvectorShooterMeleeInputAdapter), "ConfigureMotorBridge", typeof(InvectorBrawlerMotor));
            AssertDeclaresMethodSignature(
                typeof(InvectorShooterMeleeInputAdapter), "SelectMovementFeedMode",
                typeof(InvectorMovementFeedMode));
            AssertDeclaresMethodSignature(
                typeof(InvectorShooterMeleeInputAdapter), "ConfigureDormant",
                typeof(BrawlInvectorThirdPersonController),
                typeof(vShooterManager),
                typeof(BrawlInvectorMeleePresentationManager),
                typeof(InputActionAsset),
                typeof(int));
            AssertDeclaresMethod(typeof(BrawlInvectorThirdPersonController), "SetCustomFixedTimeStep");
            AssertDeclaresMethod(typeof(BrawlInvectorThirdPersonController), "Start");
            AssertDeclaresMethod(typeof(BrawlInvectorThirdPersonController), "UpdateMotor");
            AssertDeclaresMethod(typeof(BrawlInvectorThirdPersonController), "ControlLocomotionType");
            AssertDeclaresMethod(typeof(BrawlInvectorThirdPersonController), "ControlRotationType");
            AssertDeclaresMethod(typeof(BrawlInvectorThirdPersonController), "UpdateAnimator");
            AssertDeclaresMethod(typeof(BrawlInvectorThirdPersonController), "CheckStamina");
            AssertDeclaresMethod(typeof(BrawlInvectorThirdPersonController), "StaminaRecovery");
            AssertDeclaresMethod(typeof(BrawlInvectorThirdPersonController), "HealthRecovery");
            AssertDeclaresMethod(typeof(BrawlInvectorThirdPersonController), "FallDamageConditions");
            AssertDeclaresMethod(typeof(BrawlInvectorThirdPersonController), "OnTriggerStay");
            AssertDeclaresMethod(typeof(BrawlInvectorThirdPersonController), "OnTriggerExit");
            AssertDeclaresProperty(typeof(BrawlInvectorThirdPersonController), "canRecoverHealth");
            AssertDeclaresMethodSignature(
                typeof(BrawlInvectorThirdPersonController), "TakeDamage", typeof(vDamage));
            AssertDeclaresMethodSignature(
                typeof(BrawlInvectorThirdPersonController), "AddHealth", typeof(int));
            AssertDeclaresMethodSignature(
                typeof(BrawlInvectorThirdPersonController), "ChangeHealth", typeof(int));
            AssertDeclaresMethodSignature(
                typeof(BrawlInvectorThirdPersonController), "ResetHealth", typeof(float));
            AssertDeclaresMethodSignature(
                typeof(BrawlInvectorThirdPersonController), "ResetHealth");
            AssertDeclaresMethodSignature(
                typeof(BrawlInvectorThirdPersonController), "ChangeMaxHealth", typeof(int));
            AssertDeclaresMethodSignature(
                typeof(BrawlInvectorThirdPersonController), "SetHealthRecovery", typeof(float));
            AssertDeclaresMethodSignature(
                typeof(BrawlInvectorThirdPersonController), "ReduceStamina", typeof(float), typeof(bool));
            AssertDeclaresMethodSignature(
                typeof(BrawlInvectorThirdPersonController), "ChangeStamina", typeof(int));
            AssertDeclaresMethodSignature(
                typeof(BrawlInvectorThirdPersonController), "ChangeMaxStamina", typeof(int));

            string adapterSource = ReadProjectSource(InputAdapterSourcePath);
            MatchCollection baseCalls = Regex.Matches(adapterSource,
                @"\bbase\s*\.\s*(?<method>[A-Za-z_][A-Za-z0-9_]*)\s*\(");
            string[] allowedBaseCalls =
            {
                "FixedUpdate",
            };
            string[] baseCallMethods = baseCalls.Cast<Match>()
                .Select(match => match.Groups["method"].Value)
                .ToArray();
            Assert.That(baseCallMethods.Count(method => method == "FixedUpdate"), Is.EqualTo(1),
                "The audited motor/Animator scheduler must execute exactly once.");
            Assert.That(baseCallMethods.All(allowedBaseCalls.Contains), Is.True,
                "Only the one audited motor/Animator scheduler may call a vendor base.");
            Assert.That(Regex.IsMatch(adapterSource, @"\bbase\s*\.\s*Start\s*\("), Is.False,
                "The project adapter must not enter the vendor initialization chain.");
            StringAssert.DoesNotContain("Ignore Ragdoll", adapterSource,
                "The adapter must not carry a tag literal or dependency for the vendor aim helper.");
            Assert.That(Regex.IsMatch(adapterSource, @"\b(?:CompareTag|FindAnyObjectByType|" +
                @"FindFirstObjectByType|FindObjectsByType|FindObjectOfType)\s*(?:<|\()"), Is.False,
                "The project adapter must not discover tag-, camera-, HUD-, or UI-owned scene globals.");
            Assert.That(Regex.IsMatch(adapterSource, @"\bCamera\s*\.\s*main\b"), Is.False,
                "BrawlCamera is the only approved movement-yaw authority.");
            Assert.That(Regex.IsMatch(adapterSource,
                @"\bCursor\s*\.\s*(?:visible|lockState)\b"), Is.False,
                "The adapter's terminal cursor overrides must not mutate process-global cursor state.");
            Assert.That(Regex.IsMatch(adapterSource,
                @"\bInput\s*\.\s*Get(?:Axis|AxisRaw|Button|ButtonDown|ButtonUp|Key|KeyDown|KeyUp)\s*\("),
                Is.False, "The adapter must never enter the legacy UnityEngine.Input API.");
            Assert.That(Regex.IsMatch(adapterSource, @"\bKeyboard\s*\.\s*current\b"), Is.False,
                "Phase 3B movement must flow through the configured project Input Action.");
            Assert.That(Regex.IsMatch(adapterSource,
                @"projectInputActions\s*\?*\.\s*FindAction\s*\(\s*MoveActionPath"), Is.True,
                "The adapter must resolve the project Player/Move action.");
            Assert.That(Regex.IsMatch(adapterSource,
                @"action\s*\.\s*ReadValue\s*<\s*Vector2\s*>\s*\("), Is.True,
                "The one project adapter must read Player/Move exactly at its movement boundary.");
            AssertDeclaresProperty(typeof(InvectorShooterMeleeInputAdapter),
                "ProjectMoveActionOwnedByAdapter");
            AssertDeclaresProperty(typeof(InvectorShooterMeleeInputAdapter),
                "ProjectMoveActionUsesProjectWideLifecycle");
            Assert.That(Regex.IsMatch(adapterSource,
                @"\b(?:horizontalInput|verticalInput|sprintInput|crouchInput|strafeInput|jumpInput|" +
                @"rollInput|weakAttackInput|strongAttackInput|blockInput|aimInput|shotInput|reloadInput|" +
                @"switchCameraSideInput|scopeViewInput)\s*\.\s*Get(?:Axis|AxisRaw|Button|ButtonDown|ButtonUp)\s*\("),
                Is.False, "No retained adapter path may poll an Invector GenericInput.");
            Assert.That(Regex.IsMatch(adapterSource,
                @"\b(?:cc|configuredController)\s*\.\s*(?:UpdateAnimator|TakeDamage|ChangeHealth|ResetHealth)\s*\("),
                Is.False, "The adapter may not become a second Animator or health authority.");
            Assert.That(Regex.IsMatch(adapterSource,
                @"\b(?:animator|bodyAnimator)\s*\.\s*(?:SetBool|SetFloat|SetInteger|SetTrigger|ResetTrigger|" +
                @"Play|CrossFade|CrossFadeInFixedTime)\s*\("), Is.False,
                "The adapter may inspect readiness but must not write Animator state directly.");
            Assert.That(Regex.IsMatch(adapterSource,
                @"\b(?:shooterManager|configuredShooterManager)\s*\.\s*(?:Shoot|DoShots|TriggerShot|HandleShotCount)\s*\("),
                Is.False, "The locomotion adapter may not execute Invector shooting.");
            StringAssert.DoesNotContain("CancelReload", adapterSource,
                "Strong-attack presentation must never enter the shooter reload manager.");
            Assert.That(Regex.Matches(adapterSource,
                    @"configuredController\s*\.\s*TriggerMeleePresentation\s*\(")
                .Count, Is.EqualTo(2),
                "Weak and strong presentation must each use the project-owned Animator boundary.");
            Assert.That(Regex.Matches(adapterSource,
                    @"configuredController\s*\.\s*TriggerRecoilPresentation\s*\(")
                .Count, Is.EqualTo(1),
                "Recoil presentation must use the project-owned Animator boundary.");
            Assert.That(Regex.IsMatch(adapterSource,
                @"configuredController\s*\?\s*\.\s*ResetMeleeAttackTriggers\s*\(\s*\)"),
                Is.True,
                "Animator attack-state exit cleanup must remain inside the project controller boundary.");
            Assert.That(Regex.IsMatch(adapterSource,
                @"\b(?:currentStamina|currentStaminaRecoveryDelay|ammoManager|ammoHandle|AllAmmoInfinity)\b"),
                Is.False, "The adapter must not read or mutate a second gameplay resource authority.");
            Assert.That(Regex.IsMatch(adapterSource,
                @"\bBrawlCamera\s+movementReference\b"), Is.True,
                "The movement-yaw reference must be typed as the project camera authority.");
            Assert.That(Regex.IsMatch(adapterSource,
                @"movementReference\s*\.\s*gameObject\s*\.\s*scene\s*==\s*gameObject\s*\.\s*scene"),
                Is.True, "The BrawlCamera authority must live in the adapter's exact scene.");
            Assert.That(Regex.IsMatch(adapterSource,
                @"!\s*body\s*\.\s*isKinematic"), Is.True);
            Assert.That(Regex.IsMatch(adapterSource,
                @"body\s*\.\s*constraints\s*&\s*RigidbodyConstraints\s*\.\s*FreezeAll"), Is.True,
                "The scheduler gate must reject the builder's fully frozen Rigidbody.");

            string controllerSource = ReadProjectSource(ControllerSourcePath);
            Assert.That(Regex.IsMatch(controllerSource, @"\bCompareTag\s*\("), Is.False);
            Assert.That(Regex.IsMatch(controllerSource, @"\bbase\s*\.\s*OnTrigger(?:Stay|Exit)\s*\("), Is.False);
            Assert.That(Regex.IsMatch(controllerSource, @"\bTime\s*\.\s*fixedDeltaTime\b"), Is.False,
                "The project controller cannot become a global timestep authority.");
            Assert.That(Regex.IsMatch(controllerSource, @"[""']AutoCrouch[""']"), Is.False,
                "The project controller may document the vendor path but must not carry a tag literal.");
            string[] controllerBaseCalls = Regex.Matches(controllerSource,
                    @"\bbase\s*\.\s*(?<method>[A-Za-z_][A-Za-z0-9_]*)\s*\(")
                .Cast<Match>()
                .Select(match => match.Groups["method"].Value)
                .ToArray();
            CollectionAssert.AreEquivalent(
                new[] { "UpdateMotor", "ControlLocomotionType", "ControlRotationType", "UpdateAnimator" },
                controllerBaseCalls,
                "Only the four traced operations inside the one retained scheduler may re-enter vendor code.");
            Assert.That(Regex.IsMatch(controllerSource, @"\bbase\s*\.\s*Start\s*\("), Is.False,
                "The inherited health-only Start path must remain suppressed.");
            Assert.That(Regex.IsMatch(controllerSource,
                @"protected\s+override\s+bool\s+canRecoverHealth\s*=>\s*false"), Is.True);
            Assert.That(Regex.IsMatch(controllerSource,
                @"public\s+override\s+bool\s+FallDamageConditions\s*\(\s*\)\s*\{\s*return\s+false\s*;"),
                Is.True, "Inherited fall damage must be terminally suppressed.");
            Assert.That(Regex.IsMatch(controllerSource,
                @"\bcurrentStamina\s*=\s*maxStamina\s*;"), Is.True,
                "Invector's private locomotion stamina must be pinned rather than spent.");
            AssertDeclaresProperty(typeof(BrawlInvectorThirdPersonController), "InternalMotorStamina");
            AssertDeclaresProperty(typeof(BrawlInvectorThirdPersonController), "IsInternalMotorStaminaPinned");
            AssertDeclaresProperty(typeof(BrawlInvectorThirdPersonController), "RegisteredAnimatorStateInfoLayerCount");
            AssertDeclaresProperty(typeof(BrawlInvectorThirdPersonController), "HasRegisteredAnimatorStateInfos");
            AssertDeclaresProperty(typeof(BrawlInvectorThirdPersonController), "MeleePresentationWriteCount");
            AssertDeclaresProperty(typeof(BrawlInvectorThirdPersonController), "RecoilPresentationWriteCount");
            AssertDeclaresProperty(typeof(BrawlInvectorThirdPersonController), "LifecyclePresentationWriteCount");
            AssertDeclaresProperty(typeof(BrawlInvectorThirdPersonController), "MeleeAttackTriggerResetCount");
            AssertDeclaresProperty(typeof(BrawlInvectorThirdPersonController), "FullPresentationResetCount");
            AssertDeclaresProperty(typeof(BrawlInvectorThirdPersonController), "LastPresentationAttackId");
            AssertDeclaresProperty(typeof(BrawlInvectorThirdPersonController), "LastPresentationRecoilId");
            AssertDeclaresProperty(typeof(BrawlInvectorThirdPersonController),
                "HasPendingMeleePresentationTrigger");
            AssertDeclaresProperty(typeof(BrawlInvectorThirdPersonController),
                "HasPendingRecoilPresentationTrigger");
            AssertDeclaresProperty(typeof(BrawlInvectorThirdPersonController),
                "LastLifecyclePresentation");
            AssertDeclaresProperty(typeof(BrawlInvectorThirdPersonController),
                "HasPendingLifecyclePresentationTrigger");
            AssertDeclaresMethodSignature(
                typeof(BrawlInvectorThirdPersonController), "TriggerMeleePresentation",
                typeof(int), typeof(bool));
            AssertDeclaresMethodSignature(
                typeof(BrawlInvectorThirdPersonController), "TriggerRecoilPresentation",
                typeof(int));
            AssertDeclaresMethodSignature(
                typeof(BrawlInvectorThirdPersonController), "TriggerLifecyclePresentation",
                typeof(BrawlInvectorLifecyclePresentation));
            AssertDeclaresMethodSignature(
                typeof(BrawlInvectorThirdPersonController), "MarkLifecyclePresentationConsumed",
                typeof(BrawlInvectorLifecyclePresentation));
            AssertDeclaresMethod(typeof(BrawlInvectorThirdPersonController),
                "ResetMeleeAttackTriggers");
            AssertDeclaresMethod(typeof(BrawlInvectorThirdPersonController), "ResetMeleePresentation");
            Assert.That(Regex.IsMatch(controllerSource,
                @"animator\s*\.\s*SetInteger\s*\(\s*vAnimatorParameters\s*\.\s*AttackID\s*,\s*attackId\s*\)"),
                Is.True, "The controller must use the vendor AttackID hash, never a raw parameter name.");
            Assert.That(Regex.Matches(controllerSource,
                    @"animator\s*\.\s*ResetTrigger\s*\(\s*vAnimatorParameters\s*\.\s*WeakAttack\s*\)")
                .Count, Is.EqualTo(3));
            Assert.That(Regex.Matches(controllerSource,
                    @"animator\s*\.\s*ResetTrigger\s*\(\s*vAnimatorParameters\s*\.\s*StrongAttack\s*\)")
                .Count, Is.EqualTo(3));
            Assert.That(Regex.Matches(controllerSource,
                    @"animator\s*\.\s*ResetTrigger\s*\(\s*vAnimatorParameters\s*\.\s*(?:TriggerRecoil|ResetState)\s*\)")
                .Count, Is.EqualTo(2),
                "Full teardown must clear both retained recoil one-shot triggers.");
            AssertDeclaresMethod(typeof(BrawlInvectorThirdPersonController),
                "EnsureApprovedAnimatorStateInfosRegistered");
            AssertDeclaresMethod(typeof(BrawlInvectorThirdPersonController), "OnDisable");
            Assert.That(Regex.IsMatch(controllerSource,
                @"protected\s+override\s+void\s+Start\s*\(\s*\)\s*\{\s*" +
                @"EnsureApprovedAnimatorStateInfosRegistered\s*\(\s*\)\s*;\s*\}"), Is.True,
                "Project Start must retain Animator state listeners without entering vendor health Start.");

            string driverSource = ReadProjectSource(AnimationDriverSourcePath);
            Assert.That(Regex.IsMatch(driverSource,
                @"\b(?:animator|controller)\s*\.\s*(?:SetBool|SetFloat|SetInteger|SetTrigger|ResetTrigger|" +
                @"Play|CrossFade|CrossFadeInFixedTime|UpdateAnimator)\s*\("), Is.False,
                "The semantic bridge must use approved public Invector APIs, never raw graph writes.");
            Assert.That(Regex.IsMatch(driverSource,
                @"\b(?:controller|input)\s*\.\s*(?:TakeDamage|ChangeHealth|ResetHealth|Shoot|TriggerShot|DoShots)\s*\("),
                Is.False, "Presentation requests must not mutate health or execute shooter damage.");
            AssertDeclaresProperty(typeof(InvectorBrawlerAnimationDriver), "DeathRequestCount");
            AssertDeclaresProperty(typeof(InvectorBrawlerAnimationDriver), "RespawnRequestCount");
            AssertDeclaresProperty(typeof(InvectorBrawlerAnimationDriver), "VictoryRequestCount");
            AssertDeclaresProperty(typeof(InvectorBrawlerAnimationDriver), "DroppedLifecycleRequestCount");
            AssertDeclaresProperty(typeof(InvectorBrawlerAnimationDriver), "LifecycleFaultCount");
            AssertDeclaresMethodSignature(
                typeof(InvectorBrawlerAnimationDriver), "NotifyLifecycleStateEntered",
                typeof(BrawlInvectorLifecyclePresentation));
            AssertDeclaresMethodSignature(
                typeof(InvectorBrawlerAnimationDriver), "NotifyLifecycleStateExited",
                typeof(BrawlInvectorLifecyclePresentation));

            string lifecycleRequestSource = ExtractMethodSource(driverSource, "RequestLifecycle");
            Assert.That(Regex.IsMatch(lifecycleRequestSource,
                @"\b(?:Animator|CrossFade|Play|SetBool|SetFloat|SetInteger|SetTrigger|ResetTrigger|" +
                @"isDead|currentHealth|TakeDamage|ResetHealth|Ragdoll|ActionState)\b"), Is.False,
                "The semantic lifecycle request path must not expose graph names or vendor lifecycle authority.");
            StringAssert.Contains("input.PrepareLifecyclePresentation()", lifecycleRequestSource);
            StringAssert.Contains("controller.TriggerLifecyclePresentation(presentation)", lifecycleRequestSource);
            foreach (string lifecycleMethod in new[] { "PlayDeath", "PlayRespawn", "PlayVictory" })
            {
                string semanticSource = ExtractMethodSource(driverSource, lifecycleMethod);
                Assert.That(Regex.IsMatch(semanticSource,
                    @"\b(?:Animator|CrossFade|SetBool|SetFloat|SetInteger|SetTrigger|ResetTrigger|" +
                    @"isDead|currentHealth|TakeDamage|ResetHealth|Ragdoll|ActionState)\b"), Is.False,
                    lifecycleMethod + " must remain a semantic-only request.");
                StringAssert.Contains("RequestLifecycle", semanticSource);
            }
            foreach (string rawGraphName in new[]
            {
                BrawlInvectorLifecycleParameters.DeathTriggerName,
                BrawlInvectorLifecycleParameters.RespawnTriggerName,
                BrawlInvectorLifecycleParameters.VictoryTriggerName,
                BrawlInvectorLifecycleParameters.DeathFullPath,
                BrawlInvectorLifecycleParameters.RespawnFullPath,
                BrawlInvectorLifecycleParameters.VictoryFullPath,
            })
            {
                StringAssert.DoesNotContain(rawGraphName, driverSource,
                    "Raw lifecycle graph names belong only to the project graph contract/builder.");
            }

            string controllerLifecycleSource =
                ExtractMethodSource(controllerSource, "TriggerLifecyclePresentation");
            Assert.That(Regex.IsMatch(controllerLifecycleSource,
                @"\b(?:isDead|currentHealth|TakeDamage|ResetHealth|Ragdoll|ActionState|" +
                @"CrossFade|CrossFadeInFixedTime)\b"), Is.False,
                "Lifecycle presentation must never enter vendor health, death, ragdoll, or action state.");
            StringAssert.Contains("BrawlInvectorLifecycleParameters.TriggerHash", controllerLifecycleSource);
            StringAssert.Contains("animator.SetTrigger(triggerHash)", controllerLifecycleSource,
                "The one approved Animator owner must write only the cached project trigger hash.");
            Assert.That(Regex.IsMatch(controllerLifecycleSource, @"Animator\s*\.\s*StringToHash"), Is.False);
            foreach (string rawTriggerName in new[]
            {
                BrawlInvectorLifecycleParameters.DeathTriggerName,
                BrawlInvectorLifecycleParameters.RespawnTriggerName,
                BrawlInvectorLifecycleParameters.VictoryTriggerName,
            })
                StringAssert.DoesNotContain(rawTriggerName, controllerSource);

            string prepareLifecycleSource =
                ExtractMethodSource(adapterSource, "PrepareLifecyclePresentation");
            Assert.That(Regex.IsMatch(prepareLifecycleSource,
                @"\b(?:Animator|SetBool|SetFloat|SetInteger|SetTrigger|ResetTrigger|CrossFade|" +
                @"isDead|currentHealth|TakeDamage|ResetHealth|Ragdoll|ActionState)\b"), Is.False,
                "Adapter lifecycle preparation may clear presentation flags only.");

            string lifecycleMarkerSource = ReadProjectSource(LifecycleMarkerSourcePath);
            string enterMarkerSource = ExtractMethodSource(lifecycleMarkerSource, "OnStateEnter");
            string exitMarkerSource = ExtractMethodSource(lifecycleMarkerSource, "OnStateExit");
            foreach (string markerSource in new[] { enterMarkerSource, exitMarkerSource })
            {
                Assert.That(Regex.IsMatch(markerSource,
                    @"\.(?:SetBool|SetFloat|SetInteger|SetTrigger|ResetTrigger|Play|CrossFade|" +
                    @"CrossFadeInFixedTime)\s*\("), Is.False,
                    "The lifecycle marker is trace-only and may not become another Animator writer.");
                Assert.That(Regex.IsMatch(markerSource,
                    @"\b(?:isDead|currentHealth|TakeDamage|ResetHealth|Ragdoll|ActionState|" +
                    @"Rigidbody|Collider)\b"), Is.False,
                    "The lifecycle marker may not mutate gameplay, vendor lifecycle, or physics.");
            }

            Type[] attackWindowSignature =
            {
                typeof(List<string>), typeof(vAttackType), typeof(bool), typeof(int), typeof(int),
                typeof(int), typeof(bool), typeof(bool), typeof(float), typeof(string),
            };
            AssertDeclaresMethodSignature(
                typeof(BrawlInvectorMeleePresentationManager),
                "SetActiveAttack",
                attackWindowSignature);
            attackWindowSignature[0] = typeof(string);
            AssertDeclaresMethodSignature(
                typeof(BrawlInvectorMeleePresentationManager),
                "SetActiveAttack",
                attackWindowSignature);
            AssertDeclaresMethodSignature(
                typeof(BrawlInvectorMeleePresentationManager),
                "OnDamageHit",
                typeof(vHitInfo).MakeByRefType());
            AssertDeclaresMethod(
                typeof(BrawlInvectorMeleePresentationManager), "ResetPresentationTrace");

            string meleePresentationSource = ReadProjectSource(MeleePresentationManagerSourcePath);
            Assert.That(Regex.IsMatch(meleePresentationSource,
                @"\bbase\s*\.\s*(?:SetActiveAttack|OnDamageHit)\s*\("), Is.False,
                "The presentation firewall must terminate every vendor melee damage entry point.");
            Assert.That(Regex.IsMatch(meleePresentationSource,
                @"\.(?:SetActiveDamage|ApplyDamage)\s*\("), Is.False,
                "The presentation firewall must not activate a hit source or apply damage.");
            StringAssert.Contains("throw new NotSupportedException", meleePresentationSource,
                "A direct melee damage callback must fail closed.");

            string labSource = ReadProjectSource(LabControllerSourcePath);
            CollectionAssert.IsSubsetOf(new[]
            {
                "FullBody.Hit Recoil.recoil_hard",
                "FullBody.Hit Recoil.recoil_low",
                "FullBody.Hit Recoil.recoil_unarmed",
            }, Regex.Matches(labSource, @"FullBody\.Hit Recoil\.recoil_[a-z]+")
                .Cast<Match>().Select(match => match.Value).Distinct().ToArray(),
                "Hit-reaction evidence must accept only the audited FullBody recoil states.");

            string motorSource = ReadProjectSource(MotorSourcePath);
            StringAssert.Contains("internal void ReturnDormant()", motorSource);
            StringAssert.DoesNotContain("void FixedUpdate(", motorSource,
                "The selected motor must not become a second fixed scheduler.");

            int schedulerClose = labSource.IndexOf(
                "input.SetRuntimeSchedulingEnabled(false);", StringComparison.Ordinal);
            int motorDormant = labSource.IndexOf(
                "motor.ReturnDormant();", schedulerClose + 1, StringComparison.Ordinal);
            int feedRestore = labSource.IndexOf(
                "input.SelectMovementFeedMode(InvectorMovementFeedMode.LabProjectAction);",
                motorDormant + 1,
                StringComparison.Ordinal);
            int bodyFreeze = labSource.IndexOf(
                "body.isKinematic = true;", feedRestore + 1, StringComparison.Ordinal);
            Assert.That(schedulerClose, Is.GreaterThanOrEqualTo(0));
            Assert.That(motorDormant, Is.GreaterThan(schedulerClose),
                "Close the one scheduler before returning its motor dormant.");
            Assert.That(feedRestore, Is.GreaterThan(motorDormant),
                "Restore the serialized lab feed only after the motor releases runtime state.");
            Assert.That(bodyFreeze, Is.GreaterThan(feedRestore),
                "Return the motor and feed dormant before freezing the Rigidbody.");
        }

        [Test]
        public void AnimatorOverrideRetainsVendorTopologyAndAddsOnlyTheProjectLifecycleOverlay()
        {
            GameObject prefab = RequirePilotPrefab();
            Animator animator = prefab.GetComponent<Animator>();
            var overrides = animator.runtimeAnimatorController as AnimatorOverrideController;
            Assert.That(overrides, Is.Not.Null);
            Assert.That(AssetDatabase.GetAssetPath(overrides),
                Is.EqualTo(InvectorMigrationPilotBuilder.OverrideControllerPath));

            var vendor = AssetDatabase.LoadAssetAtPath<AnimatorController>(
                InvectorMigrationPilotBuilder.CombinedControllerPath);
            var lifecycle = overrides.runtimeAnimatorController as AnimatorController;
            Assert.That(vendor, Is.Not.Null);
            Assert.That(lifecycle, Is.Not.Null);
            Assert.That(lifecycle, Is.Not.SameAs(vendor),
                "The Cinder AOC must never mutate or point directly at the vendor controller.");
            Assert.That(AssetDatabase.GetAssetPath(lifecycle),
                Is.EqualTo(InvectorMigrationPilotBuilder.LifecycleControllerPath));
            Assert.That(lifecycle.layers.Select(layer => layer.name), Is.EqualTo(ExpectedLayers));
            Assert.That(vendor.layers.Select(layer => layer.name), Is.EqualTo(ExpectedLayers));
            Assert.That(vendor.parameters, Has.Length.EqualTo(44));
            Assert.That(lifecycle.parameters, Has.Length.EqualTo(47));

            string[] requiredParameters =
            {
                "InputHorizontal", "InputVertical", "InputMagnitude", "InputDirection",
                "IsGrounded", "IsStrafing", "ActionState", "ResetState", "isDead",
                "WeakAttack", "StrongAttack", "AttackID", "IsBlocking",
                "IsAiming", "Shoot", "Reload", "LookAngleH", "LookAngleV",
            };
            string[] parameters = lifecycle.parameters.Select(parameter => parameter.name).ToArray();
            CollectionAssert.IsSubsetOf(requiredParameters, parameters);

            string[] lifecycleParameterNames =
            {
                BrawlInvectorLifecycleParameters.DeathTriggerName,
                BrawlInvectorLifecycleParameters.RespawnTriggerName,
                BrawlInvectorLifecycleParameters.VictoryTriggerName,
            };
            AnimatorControllerParameter[] projectParameters = lifecycle.parameters
                .Where(parameter => lifecycleParameterNames.Contains(parameter.name))
                .ToArray();
            Assert.That(projectParameters, Has.Length.EqualTo(3));
            Assert.That(projectParameters.All(parameter =>
                parameter.type == AnimatorControllerParameterType.Trigger), Is.True,
                "The lifecycle overlay may add only three project trigger parameters.");
            CollectionAssert.AreEqual(
                vendor.parameters.Select(ParameterSignature).ToArray(),
                lifecycle.parameters
                    .Where(parameter => !lifecycleParameterNames.Contains(parameter.name))
                    .Select(ParameterSignature)
                    .ToArray(),
                "Every vendor parameter and default must survive the project controller copy unchanged.");
            CollectionAssert.AreEqual(
                ControllerGraphFingerprint(vendor, excludeLifecycleOverlay: false),
                ControllerGraphFingerprint(lifecycle, excludeLifecycleOverlay: true),
                "Removing the project-owned lifecycle overlay must leave the exact vendor graph topology.");

            Assert.That(BrawlInvectorLifecycleParameters.DeathTrigger,
                Is.EqualTo(Animator.StringToHash(BrawlInvectorLifecycleParameters.DeathTriggerName)));
            Assert.That(BrawlInvectorLifecycleParameters.RespawnTrigger,
                Is.EqualTo(Animator.StringToHash(BrawlInvectorLifecycleParameters.RespawnTriggerName)));
            Assert.That(BrawlInvectorLifecycleParameters.VictoryTrigger,
                Is.EqualTo(Animator.StringToHash(BrawlInvectorLifecycleParameters.VictoryTriggerName)));
            Assert.That(BrawlInvectorLifecycleParameters.DeathState,
                Is.EqualTo(Animator.StringToHash(BrawlInvectorLifecycleParameters.DeathFullPath)));
            Assert.That(BrawlInvectorLifecycleParameters.RespawnState,
                Is.EqualTo(Animator.StringToHash(BrawlInvectorLifecycleParameters.RespawnFullPath)));
            Assert.That(BrawlInvectorLifecycleParameters.VictoryState,
                Is.EqualTo(Animator.StringToHash(BrawlInvectorLifecycleParameters.VictoryFullPath)));

            AnimatorStateMachine fullBody = lifecycle.layers[7].stateMachine;
            ChildAnimatorStateMachine[] lifecycleMachines = fullBody.stateMachines
                .Where(child => child.stateMachine.name ==
                    BrawlInvectorLifecycleParameters.StateMachineName)
                .ToArray();
            Assert.That(lifecycleMachines, Has.Length.EqualTo(1));
            AnimatorStateMachine lifecycleMachine = lifecycleMachines[0].stateMachine;
            Assert.That(lifecycleMachine.behaviours, Is.Empty,
                "Lifecycle authority belongs to Brawl; the project sub-state machine needs no vendor behaviour.");
            Assert.That(lifecycleMachine.stateMachines, Is.Empty);

            ChildAnimatorState[] lifecycleStates = lifecycleMachine.states;
            CollectionAssert.AreEquivalent(new[]
            {
                BrawlInvectorLifecycleParameters.DeathStateName,
                BrawlInvectorLifecycleParameters.RespawnStateName,
                BrawlInvectorLifecycleParameters.VictoryStateName,
            }, lifecycleStates.Select(child => child.state.name).ToArray());
            AnimatorState death = RequireState(
                lifecycleMachine, BrawlInvectorLifecycleParameters.DeathStateName);
            AnimatorState respawn = RequireState(
                lifecycleMachine, BrawlInvectorLifecycleParameters.RespawnStateName);
            AnimatorState victory = RequireState(
                lifecycleMachine, BrawlInvectorLifecycleParameters.VictoryStateName);
            Assert.That(lifecycleMachine.defaultState, Is.SameAs(respawn));

            AnimationClip deathClip = AssetDatabase.LoadAssetAtPath<AnimationClip>(
                InvectorMigrationPilotBuilder.DeathClipPath);
            AnimationClip victoryClip = AssetDatabase.LoadAssetAtPath<AnimationClip>(
                InvectorMigrationPilotBuilder.VictoryClipPath);
            AssertLifecycleState(
                death, deathClip, BrawlInvectorLifecyclePresentation.Death);
            AssertLifecycleState(
                respawn, null, BrawlInvectorLifecyclePresentation.Respawn);
            AssertLifecycleState(
                victory, victoryClip, BrawlInvectorLifecyclePresentation.Victory);
            AssertLifecycleClip(deathClip, InvectorMigrationPilotBuilder.DeathClipPath);
            AssertLifecycleClip(victoryClip, InvectorMigrationPilotBuilder.VictoryClipPath);

            Assert.That(death.transitions, Is.Empty,
                "Death must hold its event-free final pose until a semantic lifecycle request replaces it.");
            Assert.That(victory.transitions, Is.Empty,
                "Victory must hold its event-free final pose until a semantic lifecycle request replaces it.");
            Assert.That(respawn.transitions, Has.Length.EqualTo(1));
            AnimatorStateTransition respawnExit = respawn.transitions[0];
            Assert.That(respawnExit.isExit, Is.True);
            Assert.That(respawnExit.conditions, Is.Empty);
            Assert.That(respawnExit.hasExitTime, Is.True);
            Assert.That(respawnExit.exitTime, Is.EqualTo(0.01f).Within(0.0001f));
            Assert.That(respawnExit.hasFixedDuration, Is.True);
            Assert.That(respawnExit.duration, Is.EqualTo(0.05f).Within(0.0001f));
            Assert.That(respawnExit.offset, Is.EqualTo(0f).Within(0.0001f));
            Assert.That(respawnExit.interruptionSource,
                Is.EqualTo(TransitionInterruptionSource.None));
            Assert.That(respawnExit.orderedInterruption, Is.True);

            Assert.That(fullBody.anyStateTransitions, Has.Length.EqualTo(
                vendor.layers[7].stateMachine.anyStateTransitions.Length + 3));
            AssertLifecycleTransition(
                fullBody.anyStateTransitions[0], death,
                BrawlInvectorLifecycleParameters.DeathTriggerName, 0.1f);
            AssertLifecycleTransition(
                fullBody.anyStateTransitions[1], respawn,
                BrawlInvectorLifecycleParameters.RespawnTriggerName, 0.05f);
            AssertLifecycleTransition(
                fullBody.anyStateTransitions[2], victory,
                BrawlInvectorLifecycleParameters.VictoryTriggerName, 0.2f);

            var behaviourNames = new HashSet<string>();
            var stateTags = new HashSet<string>();
            int behaviourCount = 0;
            int resetAttackTriggerBehaviourCount = 0;
            foreach (AnimatorControllerLayer layer in lifecycle.layers)
            {
                CollectGraph(
                    layer.stateMachine,
                    behaviourNames,
                    stateTags,
                    ref behaviourCount,
                    ref resetAttackTriggerBehaviourCount);
            }

            Assert.That(behaviourCount, Is.GreaterThanOrEqualTo(100));
            Assert.That(resetAttackTriggerBehaviourCount, Is.GreaterThan(0),
                "The retained graph must preserve attack-state exits that call ResetAttackTriggers.");
            CollectionAssert.IsSubsetOf(new[]
            {
                "vAnimatorTag", "vAnimatorTagAdvanced", "vMeleeAttackControl",
            }, behaviourNames);
            CollectionAssert.Contains(behaviourNames, nameof(BrawlInvectorLifecycleStateMarker));
            CollectionAssert.Contains(stateTags, "HeadTrack");
        }

        [Test]
        public void RetainedComponentsHaveNoMissingOrTemplateHierarchyReferences()
        {
            GameObject prefab = RequirePilotPrefab();
            foreach (Component component in prefab.GetComponentsInChildren<Component>(true))
            {
                Assert.That(component, Is.Not.Null, "Prefab contains a missing component.");
                var serialized = new SerializedObject(component);
                SerializedProperty property = serialized.GetIterator();
                while (property.Next(true))
                {
                    if (property.propertyType != SerializedPropertyType.ObjectReference)
                        continue;

                    UnityEngine.Object value = property.objectReferenceValue;
                    if (value == null)
                    {
                        Assert.That(property.objectReferenceInstanceIDValue, Is.Zero,
                            component.GetType().Name + "." + property.propertyPath + " is a missing reference.");
                        continue;
                    }

                    if (value is GameObject || value is Component)
                    {
                        Assert.That(AssetDatabase.GetAssetPath(value),
                            Is.Not.EqualTo(InvectorMigrationPilotBuilder.TemplatePath),
                            component.GetType().Name + "." + property.propertyPath +
                            " references the vendor template hierarchy.");
                    }
                }
            }
        }

        [Test]
        public void GeneratedLabSceneOpensAdditivelyIsIsolatedAndIsExcludedFromBuildSettings()
        {
            Scene original = SceneManager.GetActiveScene();
            bool originalDirty = original.isDirty;
            Scene lab = default;
            try
            {
                lab = EditorSceneManager.OpenScene(InvectorMigrationPilotBuilder.ScenePath, OpenSceneMode.Additive);
                Assert.That(lab.IsValid() && lab.isLoaded, Is.True);
                GameObject[] objects = lab.GetRootGameObjects()
                    .SelectMany(root => root.GetComponentsInChildren<Transform>(true))
                    .Select(transform => transform.gameObject)
                    .ToArray();

                GameObject pilot = objects.Single(gameObject => gameObject.name == "CinderInvectorPilot_DISABLED");
                Assert.That(pilot.activeSelf, Is.False);
                InvectorBrawlerMotor motor = pilot.GetComponent<InvectorBrawlerMotor>();
                InvectorShooterMeleeInputAdapter input =
                    pilot.GetComponent<InvectorShooterMeleeInputAdapter>();
                Assert.That(motor, Is.Not.Null);
                Assert.That(objects.SelectMany(gameObject =>
                    gameObject.GetComponents<InvectorBrawlerMotor>()).Single(), Is.SameAs(motor));
                Assert.That(motor.IsDormantConfigured, Is.True);
                Assert.That(motor.IsInitialized, Is.False);
                Assert.That(input.HasConfiguredMotorBridge, Is.True);
                Assert.That(input.MovementFeedMode,
                    Is.EqualTo(InvectorMovementFeedMode.LabProjectAction));
                Assert.That(objects.SelectMany(gameObject => gameObject.GetComponents<Camera>()).Count(),
                    Is.EqualTo(1));
                Assert.That(objects.SelectMany(gameObject => gameObject.GetComponents<AudioListener>()).Count(),
                    Is.EqualTo(1));
                BrawlCamera brawlCamera = objects.SelectMany(gameObject => gameObject.GetComponents<BrawlCamera>())
                    .Single();
                Assert.That(brawlCamera.target, Is.SameAs(pilot.transform));
                Assert.That(brawlCamera.movementAutoTurn, Is.False);
                Assert.That(brawlCamera.obstructionMask.value, Is.EqualTo(1 << 9));
                InvectorPhase3BLabController labController = objects
                    .SelectMany(gameObject => gameObject.GetComponents<InvectorPhase3BLabController>())
                    .Single();
                Assert.That(labController.PilotRoot, Is.SameAs(pilot));
                Assert.That(labController.CameraAuthority, Is.SameAs(brawlCamera));
                Assert.That(objects.Single(gameObject => gameObject.name == "Ground").layer, Is.EqualTo(8));
                Assert.That(objects.Single(gameObject => gameObject.name == "Step").layer, Is.EqualTo(8));
                Assert.That(objects.Single(gameObject => gameObject.name == "Slope").layer, Is.EqualTo(8));
                Assert.That(objects.Single(gameObject => gameObject.name == "Collision Wall").layer, Is.EqualTo(9));
                Assert.That(Physics.GetIgnoreLayerCollision(pilot.layer, 8), Is.False);
                Assert.That(Physics.GetIgnoreLayerCollision(pilot.layer, 9), Is.False);
                Assert.That(objects.SelectMany(gameObject => gameObject.GetComponents<EventSystem>()), Is.Empty);
                Assert.That(objects.SelectMany(gameObject => gameObject.GetComponents<Component>())
                    .Where(component => component != null)
                    .Any(component => component.GetType().Name == "vThirdPersonCamera"), Is.False);
                Assert.That(EditorBuildSettings.scenes.Any(scene => scene.path == InvectorMigrationPilotBuilder.ScenePath),
                    Is.False);
                Assert.That(SceneManager.GetActiveScene(), Is.EqualTo(original));
            }
            finally
            {
                if (lab.IsValid() && lab.isLoaded)
                    EditorSceneManager.CloseScene(lab, true);
            }

            Assert.That(SceneManager.GetActiveScene(), Is.EqualTo(original));
            Assert.That(original.isDirty, Is.EqualTo(originalDirty));
        }

        static void AssertSerializedReference(
            Component owner,
            string propertyPath,
            UnityEngine.Object expected)
        {
            var serialized = new SerializedObject(owner);
            SerializedProperty property = serialized.FindProperty(propertyPath);
            Assert.That(property, Is.Not.Null,
                owner.GetType().Name + " must retain serialized property " + propertyPath + ".");
            Assert.That(property.propertyType, Is.EqualTo(SerializedPropertyType.ObjectReference));
            Assert.That(property.objectReferenceValue, Is.SameAs(expected),
                owner.GetType().Name + "." + propertyPath + " must remain local to the dormant root.");
        }

        static void AssertSerializedPublicBooleanField(Type owner, string fieldName)
        {
            FieldInfo field = owner.GetField(fieldName, InstanceMembers);
            Assert.That(field, Is.Not.Null, owner.Name + " must expose serialized field " + fieldName + ".");
            Assert.That(field.FieldType, Is.EqualTo(typeof(bool)));
            Assert.That(field.IsPublic, Is.True,
                owner.Name + "." + fieldName + " must remain a public serialized vendor field.");
            Assert.That(field.IsNotSerialized, Is.False);
        }

        static void AssertSerializedFieldType(Type owner, string fieldName, Type expectedType)
        {
            FieldInfo field = owner.GetField(fieldName, InstanceMembers);
            Assert.That(field, Is.Not.Null, owner.Name + " must declare serialized field " + fieldName + ".");
            Assert.That(field.FieldType, Is.EqualTo(expectedType));
            Assert.That(field.IsPublic || field.GetCustomAttribute<SerializeField>() != null, Is.True,
                owner.Name + "." + fieldName + " must remain serialized.");
        }

        static void AssertDeclaresMethod(Type owner, string methodName)
        {
            MethodInfo method = owner.GetMethod(methodName, InstanceMembers);
            Assert.That(method, Is.Not.Null, owner.Name + " must expose " + methodName + ".");
            Assert.That(method.DeclaringType, Is.EqualTo(owner),
                owner.Name + " must terminate or own " + methodName + " instead of inheriting a vendor path.");
        }

        static void AssertDeclaresMethodSignature(
            Type owner,
            string methodName,
            params Type[] parameterTypes)
        {
            MethodInfo method = owner.GetMethod(
                methodName,
                InstanceMembers,
                binder: null,
                types: parameterTypes,
                modifiers: null);
            Assert.That(method, Is.Not.Null,
                owner.Name + " must expose " + methodName + " with the audited signature.");
            Assert.That(method.DeclaringType, Is.EqualTo(owner),
                owner.Name + " must terminate or own " + methodName + " instead of inheriting a vendor path.");
        }

        static void AssertDeclaresProperty(Type owner, string propertyName)
        {
            PropertyInfo property = owner.GetProperty(propertyName, InstanceMembers);
            Assert.That(property, Is.Not.Null, owner.Name + " must expose " + propertyName + ".");
            MethodInfo accessor = property.GetMethod ?? property.SetMethod;
            Assert.That(accessor, Is.Not.Null);
            Assert.That(accessor.DeclaringType, Is.EqualTo(owner),
                owner.Name + " must terminate or own " + propertyName + " instead of inheriting it.");
        }

        static string ReadProjectSource(string relativePath)
        {
            string fullPath = Path.GetFullPath(relativePath);
            Assert.That(File.Exists(fullPath), Is.True, "Missing project source: " + relativePath);
            return File.ReadAllText(fullPath);
        }

        static string ExtractMethodSource(string source, string methodName)
        {
            Match declaration = Regex.Match(
                source,
                @"\b" + Regex.Escape(methodName) + @"\s*\([^;{}]*\)\s*\{");
            Assert.That(declaration.Success, Is.True,
                "Could not find the declared method body for " + methodName + ".");

            int openingBrace = source.IndexOf('{', declaration.Index);
            int depth = 0;
            for (int i = openingBrace; i < source.Length; i++)
            {
                if (source[i] == '{') depth++;
                else if (source[i] == '}') depth--;
                if (depth == 0)
                    return source.Substring(declaration.Index, i - declaration.Index + 1);
            }

            Assert.Fail("Method body for " + methodName + " has unbalanced braces.");
            return string.Empty;
        }

        static string ParameterSignature(AnimatorControllerParameter parameter)
        {
            return string.Join("|",
                parameter.name,
                parameter.type,
                parameter.defaultBool,
                parameter.defaultInt,
                parameter.defaultFloat.ToString("R", System.Globalization.CultureInfo.InvariantCulture));
        }

        static string[] ControllerGraphFingerprint(
            AnimatorController controller,
            bool excludeLifecycleOverlay)
        {
            var entries = new List<string>();
            for (int layerIndex = 0; layerIndex < controller.layers.Length; layerIndex++)
            {
                AnimatorControllerLayer layer = controller.layers[layerIndex];
                entries.Add(string.Join("|",
                    "LAYER",
                    layerIndex,
                    layer.name,
                    layer.blendingMode,
                    layer.defaultWeight.ToString("R", System.Globalization.CultureInfo.InvariantCulture),
                    layer.iKPass,
                    layer.syncedLayerIndex,
                    layer.syncedLayerAffectsTiming,
                    AssetDatabase.GetAssetPath(layer.avatarMask)));
                CollectGraphFingerprint(
                    layer.stateMachine,
                    layer.name,
                    excludeLifecycleOverlay,
                    entries);
            }

            return entries.OrderBy(entry => entry, StringComparer.Ordinal).ToArray();
        }

        static void CollectGraphFingerprint(
            AnimatorStateMachine machine,
            string path,
            bool excludeLifecycleOverlay,
            ICollection<string> entries)
        {
            entries.Add(string.Join("|",
                "MACHINE",
                path,
                machine.name,
                machine.defaultState != null ? machine.defaultState.name : string.Empty,
                machine.entryTransitions.Length,
                string.Join(",", machine.behaviours
                    .Select(behaviour => behaviour.GetType().FullName)
                    .OrderBy(name => name, StringComparer.Ordinal))));

            foreach (ChildAnimatorState child in machine.states)
            {
                AnimatorState state = child.state;
                string statePath = path + "." + state.name;
                entries.Add(string.Join("|",
                    "STATE",
                    statePath,
                    MotionSignature(state.motion),
                    state.tag,
                    state.speed.ToString("R", System.Globalization.CultureInfo.InvariantCulture),
                    state.cycleOffset.ToString("R", System.Globalization.CultureInfo.InvariantCulture),
                    state.mirror,
                    state.iKOnFeet,
                    state.writeDefaultValues,
                    string.Join(",", state.behaviours
                        .Select(behaviour => behaviour.GetType().FullName)
                        .OrderBy(name => name, StringComparer.Ordinal))));
                foreach (AnimatorStateTransition transition in state.transitions)
                    entries.Add("STATE_TRANSITION|" + statePath + "|" + TransitionSignature(transition));
            }

            foreach (AnimatorStateTransition transition in machine.anyStateTransitions)
            {
                if (excludeLifecycleOverlay && IsLifecycleTransition(transition))
                    continue;
                entries.Add("ANY_TRANSITION|" + path + "|" + TransitionSignature(transition));
            }

            foreach (ChildAnimatorStateMachine child in machine.stateMachines)
            {
                if (excludeLifecycleOverlay &&
                    child.stateMachine.name == BrawlInvectorLifecycleParameters.StateMachineName)
                {
                    continue;
                }

                entries.Add("CHILD_MACHINE_TRANSITIONS|" + path + "." + child.stateMachine.name + "|" +
                    machine.GetStateMachineTransitions(child.stateMachine).Length);
                CollectGraphFingerprint(
                    child.stateMachine,
                    path + "." + child.stateMachine.name,
                    excludeLifecycleOverlay,
                    entries);
            }
        }

        static string MotionSignature(Motion motion)
        {
            if (motion == null) return string.Empty;
            string assetPath = AssetDatabase.GetAssetPath(motion);
            if (motion is BlendTree && assetPath.EndsWith(".controller", StringComparison.OrdinalIgnoreCase))
                assetPath = "<owning-controller>";
            return motion.GetType().FullName + ":" + assetPath + ":" + motion.name;
        }

        static bool IsLifecycleTransition(AnimatorStateTransition transition)
        {
            return transition.conditions.Any(condition =>
                condition.parameter == BrawlInvectorLifecycleParameters.DeathTriggerName ||
                condition.parameter == BrawlInvectorLifecycleParameters.RespawnTriggerName ||
                condition.parameter == BrawlInvectorLifecycleParameters.VictoryTriggerName);
        }

        static string TransitionSignature(AnimatorStateTransition transition)
        {
            string destination = transition.isExit
                ? "EXIT"
                : transition.destinationState != null
                    ? "STATE:" + transition.destinationState.name
                    : transition.destinationStateMachine != null
                        ? "MACHINE:" + transition.destinationStateMachine.name
                        : "NONE";
            string conditions = string.Join(",", transition.conditions.Select(condition =>
                condition.parameter + ":" + condition.mode + ":" +
                condition.threshold.ToString("R", System.Globalization.CultureInfo.InvariantCulture)));
            return string.Join("|",
                destination,
                conditions,
                transition.hasExitTime,
                transition.exitTime.ToString("R", System.Globalization.CultureInfo.InvariantCulture),
                transition.hasFixedDuration,
                transition.duration.ToString("R", System.Globalization.CultureInfo.InvariantCulture),
                transition.offset.ToString("R", System.Globalization.CultureInfo.InvariantCulture),
                transition.canTransitionToSelf,
                transition.interruptionSource,
                transition.orderedInterruption);
        }

        static AnimatorState RequireState(AnimatorStateMachine machine, string stateName)
        {
            AnimatorState[] matches = machine.states
                .Where(child => child.state.name == stateName)
                .Select(child => child.state)
                .ToArray();
            Assert.That(matches, Has.Length.EqualTo(1),
                machine.name + " must contain exactly one " + stateName + " state.");
            return matches[0];
        }

        static void AssertLifecycleState(
            AnimatorState state,
            Motion expectedMotion,
            BrawlInvectorLifecyclePresentation expectedPresentation)
        {
            Assert.That(state.motion, Is.SameAs(expectedMotion));
            Assert.That(state.tag, Is.Empty,
                "Project lifecycle states must not adopt vendor semantic tags.");
            Assert.That(state.writeDefaultValues, Is.False);
            Assert.That(state.iKOnFeet, Is.False);
            Assert.That(state.speed, Is.EqualTo(1f).Within(0.0001f));
            Assert.That(state.behaviours, Has.Length.EqualTo(1),
                state.name + " must carry only the trace-only project marker.");
            Assert.That(state.behaviours[0], Is.TypeOf<BrawlInvectorLifecycleStateMarker>());
            var marker = (BrawlInvectorLifecycleStateMarker)state.behaviours[0];
            Assert.That(marker.Presentation, Is.EqualTo(expectedPresentation));
        }

        static void AssertLifecycleClip(AnimationClip clip, string expectedPath)
        {
            Assert.That(clip, Is.Not.Null);
            Assert.That(AssetDatabase.GetAssetPath(clip), Is.EqualTo(expectedPath));
            Assert.That(clip.legacy, Is.False);
            Assert.That(clip.humanMotion, Is.True);
            Assert.That(clip.isLooping, Is.False);
            Assert.That(clip.wrapMode, Is.EqualTo(WrapMode.Once));
            Assert.That(AnimationUtility.GetAnimationEvents(clip), Is.Empty,
                "Lifecycle clips must not invoke gameplay or vendor component callbacks.");

            var serialized = new SerializedObject(clip);
            SerializedProperty settings = serialized.FindProperty("m_AnimationClipSettings");
            Assert.That(settings, Is.Not.Null);
            Assert.That(settings.FindPropertyRelative("m_LoopTime").boolValue, Is.False);
            Assert.That(settings.FindPropertyRelative("m_KeepOriginalOrientation").boolValue, Is.True);
            Assert.That(settings.FindPropertyRelative("m_KeepOriginalPositionY").boolValue, Is.True);
            Assert.That(settings.FindPropertyRelative("m_KeepOriginalPositionXZ").boolValue, Is.True);
            Assert.That(serialized.FindProperty("m_HasGenericRootTransform").boolValue, Is.False);
            Assert.That(serialized.FindProperty("m_HasMotionFloatCurves").boolValue, Is.False);
            Assert.That(AnimationUtility.GetCurveBindings(clip).Count(binding =>
                binding.type == typeof(Animator) &&
                (binding.propertyName.StartsWith("RootT.", StringComparison.Ordinal) ||
                 binding.propertyName.StartsWith("RootQ.", StringComparison.Ordinal))), Is.EqualTo(7),
                "The pinned Humanoid root-pose curves changed; re-audit the baked settings and live transform proof.");
        }

        static void AssertLifecycleTransition(
            AnimatorStateTransition transition,
            AnimatorState expectedDestination,
            string expectedTrigger,
            float expectedDuration)
        {
            Assert.That(transition.destinationState, Is.SameAs(expectedDestination));
            Assert.That(transition.destinationStateMachine, Is.Null);
            Assert.That(transition.isExit, Is.False);
            Assert.That(transition.conditions, Has.Length.EqualTo(1));
            Assert.That(transition.conditions[0].parameter, Is.EqualTo(expectedTrigger));
            Assert.That(transition.conditions[0].mode, Is.EqualTo(AnimatorConditionMode.If));
            Assert.That(transition.conditions[0].threshold, Is.EqualTo(0f).Within(0.0001f));
            Assert.That(transition.hasExitTime, Is.False);
            Assert.That(transition.hasFixedDuration, Is.True);
            Assert.That(transition.duration, Is.EqualTo(expectedDuration).Within(0.0001f));
            Assert.That(transition.offset, Is.EqualTo(0f).Within(0.0001f));
            Assert.That(transition.canTransitionToSelf, Is.False);
            Assert.That(transition.interruptionSource, Is.EqualTo(TransitionInterruptionSource.None));
            Assert.That(transition.orderedInterruption, Is.True);
        }

        static void CollectGraph(
            AnimatorStateMachine machine,
            ISet<string> behaviourNames,
            ISet<string> stateTags,
            ref int behaviourCount,
            ref int resetAttackTriggerBehaviourCount)
        {
            foreach (StateMachineBehaviour behaviour in machine.behaviours)
            {
                behaviourNames.Add(behaviour.GetType().Name);
                behaviourCount++;
                if (behaviour is vMeleeAttackControl melee && melee.resetAttackTrigger)
                    resetAttackTriggerBehaviourCount++;
            }

            foreach (ChildAnimatorState child in machine.states)
            {
                if (!string.IsNullOrEmpty(child.state.tag))
                    stateTags.Add(child.state.tag);
                foreach (StateMachineBehaviour behaviour in child.state.behaviours)
                {
                    behaviourNames.Add(behaviour.GetType().Name);
                    behaviourCount++;
                    if (behaviour is vMeleeAttackControl melee && melee.resetAttackTrigger)
                        resetAttackTriggerBehaviourCount++;
                }
            }

            foreach (ChildAnimatorStateMachine child in machine.stateMachines)
            {
                CollectGraph(
                    child.stateMachine,
                    behaviourNames,
                    stateTags,
                    ref behaviourCount,
                    ref resetAttackTriggerBehaviourCount);
            }
        }

        static GameObject RequirePilotPrefab()
        {
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(InvectorMigrationPilotBuilder.PrefabPath);
            Assert.That(prefab, Is.Not.Null, "Run the Phase 3A migration pilot builder first.");
            return prefab;
        }

        static string[] OutputGuids()
        {
            return new[]
            {
                AssetDatabase.AssetPathToGUID(InvectorMigrationPilotBuilder.LifecycleControllerPath),
                AssetDatabase.AssetPathToGUID(InvectorMigrationPilotBuilder.OverrideControllerPath),
                AssetDatabase.AssetPathToGUID(InvectorMigrationPilotBuilder.PrefabPath),
                AssetDatabase.AssetPathToGUID(InvectorMigrationPilotBuilder.ProductionHumanPrefabPath),
                AssetDatabase.AssetPathToGUID(InvectorMigrationPilotBuilder.WeaponPrefabPath),
                AssetDatabase.AssetPathToGUID(InvectorMigrationPilotBuilder.WeaponIKAdjustPath),
                AssetDatabase.AssetPathToGUID(InvectorMigrationPilotBuilder.WeaponIKAdjustListPath),
                AssetDatabase.AssetPathToGUID(InvectorMigrationPilotBuilder.ScenePath),
            };
        }

        static string PrefabFingerprint()
        {
            GameObject prefab = RequirePilotPrefab();
            return string.Join("|", prefab.GetComponentsInChildren<Component>(true)
                .Where(component => component != null)
                .Select(component => component.GetType().FullName + "@" +
                    AnimationUtility.CalculateTransformPath(component.transform, prefab.transform)));
        }
    }
}
