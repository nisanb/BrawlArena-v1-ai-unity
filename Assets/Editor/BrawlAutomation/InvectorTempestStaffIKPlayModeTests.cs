using System;
using System.Collections;
using System.Linq;
using System.Reflection;
using Invector.vItemManager;
using Invector.vShooter;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace BrawlArena.EditorAutomation.Tests
{
    public sealed class InvectorTempestStaffIKPlayModeTests
    {
        const string ProofSceneName = "InvectorTempestStaffIKProof";
        const string SessionPrefix = "BrawlArena.InvectorTempestStaffIK.";
        const string OriginalScenePathKey = SessionPrefix + "OriginalScenePath";
        const string OriginalSceneDirtyKey = SessionPrefix + "OriginalSceneDirty";
        const int ActivationPollFrames = 120;
        const int PosePollFrames = 90;

        static readonly MethodInfo ResolveCurrentIKAdjustMethod =
            typeof(InvectorBrawlerWeaponPresentation).GetMethod(
                "ResolveCurrentIKAdjust",
                BindingFlags.Instance | BindingFlags.NonPublic);

        [UnityTest]
        [Category("InvectorTempestPresentation")]
        public IEnumerator ProductionTempestStaff03AppliesEveryGuardedIKPoseAndClosesSafely()
        {
            Scene original = SceneManager.GetActiveScene();
            SessionState.SetString(OriginalScenePathKey, original.path);
            SessionState.SetBool(OriginalSceneDirtyKey, original.isDirty);

            bool fixtureReady = false;
            bool tempestIdentityAndDataPinned = false;
            bool fourCleanPosePasses = false;
            bool vendorCombatStayedInert = false;
            bool gateClosedSafely = false;
            string setupEvidence = string.Empty;
            string poseEvidence = string.Empty;

            yield return new EnterPlayMode();
            for (int frame = 0;
                 frame < ActivationPollFrames && !EditorApplication.isPlaying;
                 frame++)
            {
                yield return null;
            }
            Assert.That(EditorApplication.isPlaying, Is.True,
                "Unity did not complete the bounded Tempest proof Play-mode transition.");

            Scene originalPlayScene = SceneManager.GetActiveScene();
            if (originalPlayScene.IsValid() && originalPlayScene.isLoaded)
            {
                GameObject[] roots = originalPlayScene.GetRootGameObjects();
                for (int i = 0; i < roots.Length; i++)
                    roots[i].SetActive(false);
            }

            Scene proofScene = SceneManager.CreateScene(ProofSceneName);
            SceneManager.SetActiveScene(proofScene);
            BuildGround(proofScene);
            BuildCamera(proofScene);

            GameObject productionPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(
                InvectorTempestMigrationBuilder.ProductionHumanPrefabPath);
            BrawlerController actor = null;
            if (productionPrefab != null)
            {
                try
                {
                    actor = GameFlow.Spawn(
                        BuildTempestDefinition(productionPrefab),
                        TeamId.Blue,
                        Vector3.zero,
                        true,
                        1f,
                        BrawlerAssemblyContext.ProductionHumanInvector);
                }
                catch (Exception exception)
                {
                    setupEvidence = exception.GetType().Name + ": " + exception.Message;
                }
            }
            else
            {
                setupEvidence = "The generated production-human Tempest prefab is missing.";
            }

            InvectorHumanRuntimeGate gate = actor != null
                ? actor.GetComponent<InvectorHumanRuntimeGate>()
                : null;
            Animator animator = actor != null ? actor.GetComponent<Animator>() : null;
            InvectorBrawlerWeaponPresentation presenter = actor != null
                ? actor.GetComponent<InvectorBrawlerWeaponPresentation>()
                : null;
            BrawlInvectorThirdPersonController controller = actor != null
                ? actor.GetComponent<BrawlInvectorThirdPersonController>()
                : null;

            for (int frame = 0;
                 frame < ActivationPollFrames &&
                 !(gate != null && gate.IsRuntimeActive &&
                   animator != null && animator.isInitialized &&
                   presenter != null && presenter.RuntimeEnabled);
                 frame++)
            {
                yield return null;
            }

            if (gate != null && gate.IsRuntimeActive && animator != null &&
                animator.isInitialized && presenter != null &&
                presenter.RuntimeEnabled && controller != null)
            {
                // Let the production Rigidbody/Animator settle on the disposable floor
                // before the bounded pose windows begin.
                for (int frame = 0; frame < 5; frame++)
                    yield return new WaitForFixedUpdate();
                yield return null;

                InvectorBrawlerPrefabIdentity identity =
                    actor.GetComponent<InvectorBrawlerPrefabIdentity>();
                vWeaponIKAdjustList ikList = presenter.ProjectIKAdjustList;
                vWeaponIKAdjust weaponIK = ikList != null
                    ? ikList.GetWeaponIK(presenter.WeaponCategory)
                    : null;
                fixtureReady = actor.gameObject.scene == proofScene &&
                    actor.gameObject.activeInHierarchy && presenter.IsConfigured &&
                    presenter.isActiveAndEnabled && presenter.Visible &&
                    presenter.HasRuntimeSolvers;
                tempestIdentityAndDataPinned =
                    identity != null && identity.Matches(
                        InvectorTempestMigrationBuilder.RosterId,
                        InvectorBrawlerPrefabRole.Human) &&
                    string.Equals(
                        AssetDatabase.GetAssetPath(ikList),
                        InvectorTempestMigrationBuilder.WeaponIKAdjustListPath,
                        StringComparison.Ordinal) &&
                    FindDescendant(
                        actor.transform,
                        InvectorTempestMigrationBuilder.WeaponPresentationName) != null &&
                    FindDescendant(actor.transform, "StaffVisual") != null &&
                    FindDescendant(actor.transform, "SupportHandTarget") != null &&
                    FindDescendant(actor.transform, "SupportHintTarget") != null &&
                    weaponIK != null &&
                    GetIKAdjust(weaponIK, vWeaponIKAdjust.StandingState, presenter) != null &&
                    GetIKAdjust(
                        weaponIK,
                        vWeaponIKAdjust.StandingAimingState,
                        presenter) != null &&
                    GetIKAdjust(weaponIK, vWeaponIKAdjust.CrouchingState, presenter) != null &&
                    GetIKAdjust(
                        weaponIK,
                        vWeaponIKAdjust.CrouchingAimingState,
                        presenter) != null &&
                    ResolveCurrentIKAdjustMethod != null;

                vShooterManager shooter = actor.GetComponent<vShooterManager>();
                vAmmoManager ammo = actor.GetComponent<vAmmoManager>();
                BrawlInvectorMeleePresentationManager melee =
                    actor.GetComponent<BrawlInvectorMeleePresentationManager>();
                Health health = actor.GetComponent<Health>();
                bool initialVendorPosture = shooter != null && !shooter.enabled &&
                    shooter.rWeapon == null && shooter.lWeapon == null &&
                    shooter.weaponIKAdjustList == null && !shooter.isReloadingWeapon &&
                    ammo != null && !ammo.enabled && ammo.ammoListData == null &&
                    ammo.itemManager == null &&
                    melee != null && !melee.enabled &&
                    !HasForbiddenVendorDamageComponent(actor.gameObject);
                int extraAmmoBaseline = shooter != null ? shooter.ExtraAmmo : int.MinValue;
                string ammoBaseline = AmmoSignature(ammo);
                int meleeWindowsBaseline = melee != null
                    ? melee.SuppressedAttackWindowCount
                    : int.MinValue;
                int meleeDamageBaseline = melee != null
                    ? melee.BlockedDamageHitCount
                    : int.MinValue;
                float projectHealthBaseline = health != null
                    ? health.Current
                    : float.NaN;
                float vendorHealthBaseline = controller.currentHealth;
                bool vendorDeadBaseline = controller.isDead;
                float staminaBaseline = controller.InternalMotorStamina;

                if (fixtureReady && tempestIdentityAndDataPinned)
                {
                    var poses = new[]
                    {
                        new PoseProbe(
                            vWeaponIKAdjust.StandingState,
                            false,
                            Vector3.zero),
                        new PoseProbe(
                            vWeaponIKAdjust.StandingAimingState,
                            false,
                            Vector3.forward),
                        new PoseProbe(
                            vWeaponIKAdjust.CrouchingState,
                            true,
                            Vector3.zero),
                        new PoseProbe(
                            vWeaponIKAdjust.CrouchingAimingState,
                            true,
                            Vector3.right),
                    };

                    for (int i = 0; i < poses.Length; i++)
                    {
                        yield return ProvePose(
                            poses[i], presenter, controller, weaponIK);
                    }

                    fourCleanPosePasses = poses.All(pose => pose.CleanApplied);
                    poseEvidence = string.Join(" | ", poses.Select(pose => pose.Evidence));
                }

                presenter.PresentAim(Vector3.zero);
                controller.isCrouching = false;
                vendorCombatStayedInert = initialVendorPosture &&
                    shooter != null && !shooter.enabled &&
                    shooter.rWeapon == null && shooter.lWeapon == null &&
                    shooter.weaponIKAdjustList == null && !shooter.isReloadingWeapon &&
                    shooter.ExtraAmmo == extraAmmoBaseline &&
                    ammo != null && !ammo.enabled &&
                    AmmoSignature(ammo) == ammoBaseline &&
                    melee != null && !melee.enabled &&
                    melee.SuppressedAttackWindowCount == meleeWindowsBaseline &&
                    melee.BlockedDamageHitCount == meleeDamageBaseline &&
                    health != null && Mathf.Approximately(
                        health.Current, projectHealthBaseline) &&
                    Mathf.Approximately(controller.currentHealth, vendorHealthBaseline) &&
                    controller.isDead == vendorDeadBaseline &&
                    Mathf.Approximately(
                        controller.InternalMotorStamina, staminaBaseline) &&
                    controller.IsInternalMotorStaminaPinned &&
                    !HasForbiddenVendorDamageComponent(actor.gameObject);
            }
            else if (string.IsNullOrEmpty(setupEvidence))
            {
                setupEvidence = gate != null
                    ? gate.FailureMessage
                    : "The production Tempest runtime gate was not assembled.";
            }

            if (gate != null)
            {
                gate.Deactivate();
                gateClosedSafely = !gate.IsRuntimeActive && gate.IsDormantConfigured &&
                    actor != null && !actor.gameObject.activeSelf &&
                    presenter != null && presenter.IsDormantConfigured &&
                    !presenter.HasRuntimeSolvers && presenter.RuntimeHelperCount == 0;
            }

            yield return new ExitPlayMode();

            string expectedPath = SessionState.GetString(
                OriginalScenePathKey, string.Empty);
            bool expectedDirty = SessionState.GetBool(
                OriginalSceneDirtyKey, false);
            SessionState.EraseString(OriginalScenePathKey);
            SessionState.EraseBool(OriginalSceneDirtyKey);
            Scene restored = SceneManager.GetActiveScene();

            Assert.That(fixtureReady, Is.True,
                "The disposable production-human Tempest fixture was not live. " +
                setupEvidence);
            Assert.That(tempestIdentityAndDataPinned, Is.True,
                "The fixture did not resolve the storm human, Staff03 presentation, and " +
                "Tempest-owned four-pose IK data.");
            Assert.That(fourCleanPosePasses, Is.True,
                "Every Tempest Staff03 pose must resolve and increment the actual guarded " +
                "LateUpdate pass without reach/hint rejection, support suppression, or fault. " +
                poseEvidence);
            Assert.That(vendorCombatStayedInert, Is.True,
                "The four-pose proof changed vendor shooter, ammo, melee/damage, health, " +
                "death, or stamina state.");
            Assert.That(gateClosedSafely, Is.True,
                "The production Tempest runtime gate retained a solver, helper, or live authority.");
            Assert.That(restored.path, Is.EqualTo(expectedPath));
            Assert.That(restored.isDirty, Is.EqualTo(expectedDirty),
                "The caller's original scene dirty state changed.");
        }

        [UnityTearDown]
        public IEnumerator RestorePlayModeAfterFailure()
        {
            if (Application.isPlaying)
            {
                Scene proofScene = SceneManager.GetSceneByName(ProofSceneName);
                if (proofScene.IsValid() && proofScene.isLoaded)
                {
                    GameObject[] roots = proofScene.GetRootGameObjects();
                    for (int i = 0; i < roots.Length; i++)
                    {
                        InvectorHumanRuntimeGate gate =
                            roots[i].GetComponent<InvectorHumanRuntimeGate>();
                        if (gate != null) gate.Deactivate();
                    }
                }
                yield return new ExitPlayMode();
            }

            SessionState.EraseString(OriginalScenePathKey);
            SessionState.EraseBool(OriginalSceneDirtyKey);
        }

        static IEnumerator ProvePose(
            PoseProbe probe,
            InvectorBrawlerWeaponPresentation presenter,
            BrawlInvectorThirdPersonController controller,
            vWeaponIKAdjust weaponIK)
        {
            controller.isCrouching = probe.Crouching;
            presenter.PresentAim(probe.AimDirection);

            object expected = GetIKAdjust(weaponIK, probe.StateName, presenter);
            object resolved = ResolveCurrentIKAdjustMethod != null
                ? ResolveCurrentIKAdjustMethod.Invoke(presenter, null)
                : null;
            probe.Resolved = expected != null && ReferenceEquals(resolved, expected) &&
                controller.isCrouching == probe.Crouching &&
                presenter.AimPresented == probe.Aiming;

            int appliedBaseline = presenter.AppliedIKPassCount;
            int suppressedBaseline = presenter.SuppressedIKPassCount;
            int supportSuppressionBaseline = presenter.SupportHandSuppressionCount;
            int invalidBaseline = presenter.InvalidPoseCount;
            int faultBaseline = presenter.RuntimeFaultCount;
            int lateUpdateBaseline = presenter.GatedLateUpdateCount;

            for (int frame = 0; frame < PosePollFrames; frame++)
            {
                yield return null;
                probe.Frames = frame + 1;
                if (presenter.AppliedIKPassCount > appliedBaseline ||
                    presenter.SuppressedIKPassCount > suppressedBaseline ||
                    presenter.SupportHandSuppressionCount > supportSuppressionBaseline ||
                    presenter.InvalidPoseCount > invalidBaseline ||
                    presenter.RuntimeFaultCount > faultBaseline)
                {
                    break;
                }
            }

            probe.CleanApplied = probe.Resolved &&
                presenter.GatedLateUpdateCount > lateUpdateBaseline &&
                presenter.AppliedIKPassCount > appliedBaseline &&
                presenter.SuppressedIKPassCount == suppressedBaseline &&
                presenter.SupportHandSuppressionCount == supportSuppressionBaseline &&
                presenter.InvalidPoseCount == invalidBaseline &&
                presenter.RuntimeFaultCount == faultBaseline &&
                presenter.LastSuppression == InvectorWeaponPresentationSuppression.None;
            probe.Evidence = string.Format(
                "{0}: resolved={1}, cleanApplied={2}, frames={3}, applied={4}->{5}, " +
                "suppressed={6}->{7}, support={8}->{9}, invalid={10}->{11}, " +
                "faults={12}->{13}, last={14}",
                probe.StateName,
                probe.Resolved,
                probe.CleanApplied,
                probe.Frames,
                appliedBaseline,
                presenter.AppliedIKPassCount,
                suppressedBaseline,
                presenter.SuppressedIKPassCount,
                supportSuppressionBaseline,
                presenter.SupportHandSuppressionCount,
                invalidBaseline,
                presenter.InvalidPoseCount,
                faultBaseline,
                presenter.RuntimeFaultCount,
                presenter.LastSuppression);
        }

        static BrawlerDefinition BuildTempestDefinition(GameObject productionPrefab)
        {
            return new BrawlerDefinition
            {
                id = InvectorTempestMigrationBuilder.RosterId,
                displayName = "Tempest",
                role = "Stormcaller",
                invectorHumanPrefab = productionPrefab,
                maxHealth = 100f,
                damage = 20f,
                moveSpeed = 5f,
                attackRange = 8f,
                attackRadius = 1.5f,
                cooldown = 0.5f,
                hitDelay = 0.1f,
                moveLock = 0.2f,
                autoAimRange = 12f,
                projectileSpeed = 18f,
                specialty = SpellSpecialty.ForSchool(SpellSchool.Storm),
            };
        }

        static object GetIKAdjust(
            vWeaponIKAdjust weaponIK,
            string stateName,
            InvectorBrawlerWeaponPresentation presenter)
        {
            return weaponIK != null
                ? weaponIK.GetIKAdjust(stateName, presenter.WeaponHeldInLeftHand)
                : null;
        }

        static void BuildGround(Scene scene)
        {
            GameObject ground = GameObject.CreatePrimitive(PrimitiveType.Cube);
            ground.name = "Tempest IK Proof Ground";
            ground.layer = CombatPhysics.GroundLayer;
            ground.transform.position = new Vector3(0f, -0.5f, 0f);
            ground.transform.localScale = new Vector3(20f, 1f, 20f);
            SceneManager.MoveGameObjectToScene(ground, scene);
        }

        static void BuildCamera(Scene scene)
        {
            GameObject cameraRoot = new GameObject(
                "Tempest IK Proof Camera", typeof(Camera), typeof(AudioListener));
            cameraRoot.tag = "MainCamera";
            cameraRoot.transform.position = new Vector3(0f, 8f, -10f);
            cameraRoot.transform.rotation = Quaternion.Euler(30f, 0f, 0f);
            SceneManager.MoveGameObjectToScene(cameraRoot, scene);
        }

        static Transform FindDescendant(Transform root, string name)
        {
            if (root == null) return null;
            return root.GetComponentsInChildren<Transform>(true)
                .FirstOrDefault(candidate => string.Equals(
                    candidate.name, name, StringComparison.Ordinal));
        }

        static bool HasForbiddenVendorDamageComponent(GameObject root)
        {
            if (root == null) return true;
            return root.GetComponentsInChildren<Component>(true)
                .Where(component => component != null)
                .Any(component => IsForbiddenVendorDamageType(
                    component.GetType().Name));
        }

        static bool IsForbiddenVendorDamageType(string typeName)
        {
            switch (typeName)
            {
                case "vShooterWeapon":
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

        static string AmmoSignature(vAmmoManager manager)
        {
            if (manager == null || manager.ammos == null) return "<null>";
            return string.Join(
                "|",
                manager.ammos.Select(ammo => ammo == null
                    ? "null"
                    : ammo.ammoID + ":" + ammo.count));
        }

        sealed class PoseProbe
        {
            public readonly string StateName;
            public readonly bool Crouching;
            public readonly Vector3 AimDirection;
            public readonly bool Aiming;
            public bool Resolved;
            public bool CleanApplied;
            public int Frames;
            public string Evidence = string.Empty;

            public PoseProbe(
                string stateName,
                bool crouching,
                Vector3 aimDirection)
            {
                StateName = stateName;
                Crouching = crouching;
                AimDirection = aimDirection;
                Aiming = aimDirection.sqrMagnitude > 0.000001f;
            }
        }
    }
}
