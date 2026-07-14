using System;
using System.Collections;
using System.Linq;
using Invector.vItemManager;
using Invector.vMelee;
using Invector.vShooter;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace BrawlArena.EditorAutomation.Tests
{
    public sealed class InvectorTempestCombatPresentationPlayModeTests
    {
        const string ProofSceneName = "InvectorTempestCombatPresentationProof";
        const string SessionPrefix =
            "BrawlArena.InvectorTempestCombatPresentation.";
        const string OriginalScenePathKey = SessionPrefix + "OriginalScenePath";
        const string OriginalSceneDirtyKey = SessionPrefix + "OriginalSceneDirty";
        const string FixtureReadyKey = SessionPrefix + "FixtureReady";
        const string CadenceProvenKey = SessionPrefix + "CadenceProven";
        const string SuperProvenKey = SessionPrefix + "SuperProven";
        const string AuthorityProvenKey = SessionPrefix + "AuthorityProven";
        const string GateClosedKey = SessionPrefix + "GateClosed";
        const string SetupEvidenceKey = SessionPrefix + "SetupEvidence";
        const string CadenceEvidenceKey = SessionPrefix + "CadenceEvidence";
        const string SuperEvidenceKey = SessionPrefix + "SuperEvidence";
        const string AuthorityEvidenceKey = SessionPrefix + "AuthorityEvidence";
        const float TempestCooldown = 0.82f;
        const int ActivationPollFrames = 180;
        const int CooldownPollFrames = 240;
        const int PresentationPollFrames = 180;
        const float ActivationTimeoutSeconds = 6f;
        const float CooldownTimeoutSeconds = 4f;
        const float PresentationTimeoutSeconds = 4f;

        [UnityTest]
        [Category("InvectorTempestCombatPresentation")]
        [Category("InvectorTempestPresentation")]
        public IEnumerator ProductionTempestPresentsRapidCastCadenceAndChargedSuperWithoutVendorCombat()
        {
            Scene original = SceneManager.GetActiveScene();
            SessionState.SetString(OriginalScenePathKey, original.path);
            SessionState.SetBool(OriginalSceneDirtyKey, original.isDirty);
            ClearResultEvidence();

            yield return new EnterPlayMode();
            for (int frame = 0;
                 frame < ActivationPollFrames && !EditorApplication.isPlaying;
                 frame++)
            {
                yield return null;
            }
            Assert.That(EditorApplication.isPlaying, Is.True,
                "Unity did not complete the bounded Tempest combat proof Play-mode transition.");

            Scene originalPlayScene = SceneManager.GetActiveScene();
            if (originalPlayScene.IsValid() && originalPlayScene.isLoaded)
            {
                GameObject[] roots = originalPlayScene.GetRootGameObjects();
                for (int i = 0; i < roots.Length; i++)
                    roots[i].SetActive(false);
            }

            Scene proofScene = SceneManager.CreateScene(ProofSceneName);
            SceneManager.SetActiveScene(proofScene);
            Time.timeScale = 1f;
            BuildGround(proofScene);
            BuildCamera(proofScene);

            string setupEvidence = string.Empty;
            BrawlerController actor = null;
            GameObject productionPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(
                InvectorTempestMigrationBuilder.ProductionHumanPrefabPath);
            if (productionPrefab != null)
            {
                try
                {
                    actor = GameFlow.Spawn(
                        BuildPresentationOnlyTempestDefinition(productionPrefab),
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
            InvectorBrawlerAnimationDriver driver = actor != null
                ? actor.GetComponent<InvectorBrawlerAnimationDriver>()
                : null;
            InvectorShooterMeleeInputAdapter input = actor != null
                ? actor.GetComponent<InvectorShooterMeleeInputAdapter>()
                : null;
            InvectorBrawlerWeaponPresentation presenter = actor != null
                ? actor.GetComponent<InvectorBrawlerWeaponPresentation>()
                : null;
            BrawlInvectorThirdPersonController controller = actor != null
                ? actor.GetComponent<BrawlInvectorThirdPersonController>()
                : null;

            float activationDeadline =
                Time.realtimeSinceStartup + ActivationTimeoutSeconds;
            for (int frame = 0;
                 frame < ActivationPollFrames &&
                 Time.realtimeSinceStartup < activationDeadline &&
                 !(gate != null && gate.IsRuntimeActive &&
                   driver != null && driver.PresentationRequestsEnabled &&
                   input != null && input.RuntimeSchedulingEnabled &&
                   presenter != null && presenter.RuntimeEnabled);
                 frame++)
            {
                yield return null;
            }

            PlayerBrawlerInput physicalInput = actor != null
                ? actor.GetComponent<PlayerBrawlerInput>()
                : null;
            InvectorBrawlerPrefabIdentity identity = actor != null
                ? actor.GetComponent<InvectorBrawlerPrefabIdentity>()
                : null;
            bool fixtureReady = actor != null && gate != null && gate.IsRuntimeActive &&
                driver != null && driver.PresentationRequestsEnabled &&
                input != null && input.RuntimeSchedulingEnabled &&
                presenter != null && presenter.RuntimeEnabled &&
                controller != null &&
                actor.gameObject.scene == proofScene &&
                actor.gameObject.activeInHierarchy &&
                identity != null && identity.Matches(
                    InvectorTempestMigrationBuilder.RosterId,
                    InvectorBrawlerPrefabRole.Human) &&
                string.Equals(
                    AssetDatabase.GetAssetPath(productionPrefab),
                    InvectorTempestMigrationBuilder.ProductionHumanPrefabPath,
                    StringComparison.Ordinal) &&
                actor.GetComponents<PlayerBrawlerInput>().Length == 1 &&
                physicalInput != null && physicalInput.enabled &&
                input.MovementFeedMode == InvectorMovementFeedMode.BufferedMotor &&
                !input.ProjectMoveActionOwnedByAdapter &&
                Mathf.Approximately(actor.attackCooldown, TempestCooldown);

            if (!fixtureReady && string.IsNullOrEmpty(setupEvidence))
            {
                setupEvidence = gate != null
                    ? gate.FailureMessage
                    : "The production Tempest runtime gate was not assembled.";
            }

            bool cadenceProven = false;
            bool superProven = false;
            bool authorityProven = false;
            string cadenceEvidence = "The live Tempest fixture was unavailable.";
            string superEvidence = "The live Tempest fixture was unavailable.";
            string authorityEvidence = "The live Tempest fixture was unavailable.";

            if (fixtureReady)
            {
                vShooterManager shooter = actor.GetComponent<vShooterManager>();
                vAmmoManager ammo = actor.GetComponent<vAmmoManager>();
                BrawlInvectorMeleePresentationManager melee =
                    actor.GetComponent<BrawlInvectorMeleePresentationManager>();
                vCollectShooterMeleeControl collector =
                    actor.GetComponent<vCollectShooterMeleeControl>();
                Health projectHealth = actor.GetComponent<Health>();

                int schedulerBaseline = input.SchedulerCompleteCount;
                int weakBaseline = input.WeakAttackRequestCount;
                int strongBaseline = input.StrongAttackRequestCount;
                int basicBaseline = driver.BasicAttackRequestCount;
                int superBaseline = driver.SuperRequestCount;
                int meleeWriteBaseline = controller.MeleePresentationWriteCount;
                int attackUseBaseline = actor.AttacksUsed;
                int superUseBaseline = actor.SupersUsed;
                int aimBaseline = presenter.AimRequestCount;
                int aimReleaseBaseline = presenter.AimReleaseCount;
                int animationFailureBaseline = actor.AnimationPresentationFailureCount;
                int weaponFailureBaseline = actor.WeaponPresentationFailureCount;
                int presenterFaultBaseline = presenter.RuntimeFaultCount;
                int suppressedVendorPathBaseline = input.SuppressedVendorPathCount;
                int blockedDamageBaseline = melee != null
                    ? melee.BlockedDamageHitCount
                    : int.MinValue;
                int suppressedWindowBaseline = melee != null
                    ? melee.SuppressedAttackWindowCount
                    : int.MinValue;
                int extraAmmoBaseline = shooter != null
                    ? shooter.ExtraAmmo
                    : int.MinValue;
                string ammoBaseline = AmmoSignature(ammo);
                float projectHealthBaseline = projectHealth != null
                    ? projectHealth.Current
                    : float.NaN;
                float vendorHealthBaseline = controller.currentHealth;
                bool vendorDeadBaseline = controller.isDead;
                float vendorStaminaBaseline = controller.InternalMotorStamina;

                for (int frame = 0; frame < 8; frame++)
                    yield return null;

                float firstCastAt = Time.time;
                bool firstAccepted = actor.TryAttackDirection(Vector3.forward);
                int attacksAfterFirst = actor.AttacksUsed;
                int basicAfterFirst = driver.BasicAttackRequestCount;
                int weakAfterFirst = input.WeakAttackRequestCount;
                int writesAfterFirst = controller.MeleePresentationWriteCount;
                bool immediateRejected =
                    !actor.TryAttackDirection(Vector3.right);
                bool rejectionKeptCountersStable =
                    actor.AttacksUsed == attacksAfterFirst &&
                    driver.BasicAttackRequestCount == basicAfterFirst &&
                    input.WeakAttackRequestCount == weakAfterFirst &&
                    controller.MeleePresentationWriteCount == writesAfterFirst;

                float cooldownDeadline =
                    Time.realtimeSinceStartup + CooldownTimeoutSeconds;
                int cooldownFrames = 0;
                for (;
                     cooldownFrames < CooldownPollFrames &&
                     Time.realtimeSinceStartup < cooldownDeadline &&
                     actor.CooldownFraction > 0f;
                     cooldownFrames++)
                {
                    yield return null;
                }

                float secondCastAt = Time.time;
                bool configuredCooldownElapsed = actor.CooldownFraction <= 0f &&
                    secondCastAt - firstCastAt + 0.001f >= TempestCooldown;
                bool secondAccepted =
                    actor.TryAttackDirection(Vector3.forward);

                cadenceProven = firstAccepted && immediateRejected &&
                    rejectionKeptCountersStable && configuredCooldownElapsed &&
                    secondAccepted &&
                    actor.AttacksUsed == attackUseBaseline + 2 &&
                    driver.BasicAttackRequestCount == basicBaseline + 2 &&
                    input.WeakAttackRequestCount == weakBaseline + 2 &&
                    controller.MeleePresentationWriteCount ==
                        meleeWriteBaseline + 2 &&
                    controller.LastPresentationAttackId ==
                        input.PresentationAttackId;
                cadenceEvidence = string.Format(
                    "first={0}, immediateRejected={1}, rejectionStable={2}, " +
                    "elapsed={3:F3}s, cooldownFrames={4}, second={5}, " +
                    "attacks={6}->{7}, basic={8}->{9}, weak={10}->{11}, writes={12}->{13}",
                    firstAccepted,
                    immediateRejected,
                    rejectionKeptCountersStable,
                    secondCastAt - firstCastAt,
                    cooldownFrames,
                    secondAccepted,
                    attackUseBaseline,
                    actor.AttacksUsed,
                    basicBaseline,
                    driver.BasicAttackRequestCount,
                    weakBaseline,
                    input.WeakAttackRequestCount,
                    meleeWriteBaseline,
                    controller.MeleePresentationWriteCount);

                Health chargeTarget = BuildChargeTarget(proofScene);
                float targetHealthBefore = chargeTarget.Current;
                float chargeDamage = actor.maxSuperCharge /
                    Mathf.Max(0.0001f, actor.superChargeFromDamageDealt) + 1f;
                float appliedChargeDamage = chargeTarget.TakeDamage(
                    chargeDamage, actor.gameObject);
                bool chargedThroughBrawlHealth = appliedChargeDamage > 0f &&
                    chargeTarget.Current < targetHealthBefore && actor.SuperReady;

                int strongBeforeSuper = input.StrongAttackRequestCount;
                int superBeforePresentation = driver.SuperRequestCount;
                int writesBeforeSuper = controller.MeleePresentationWriteCount;
                float superDeadline =
                    Time.realtimeSinceStartup + PresentationTimeoutSeconds;
                bool superAccepted = false;
                int superPollFrames = 0;
                for (;
                     superPollFrames < PresentationPollFrames &&
                     Time.realtimeSinceStartup < superDeadline &&
                     !superAccepted;
                     superPollFrames++)
                {
                    superAccepted = actor.TrySuperDirection(Vector3.right);
                    if (!superAccepted) yield return null;
                }

                float settleDeadline =
                    Time.realtimeSinceStartup + PresentationTimeoutSeconds;
                int settleFrames = 0;
                for (;
                     settleFrames < PresentationPollFrames &&
                     Time.realtimeSinceStartup < settleDeadline &&
                     presenter.AimPresented;
                     settleFrames++)
                {
                    yield return null;
                }

                superProven = chargedThroughBrawlHealth && superAccepted &&
                    !actor.SuperReady && actor.SupersUsed == superUseBaseline + 1 &&
                    driver.SuperRequestCount == superBeforePresentation + 1 &&
                    input.StrongAttackRequestCount == strongBeforeSuper + 1 &&
                    controller.MeleePresentationWriteCount == writesBeforeSuper + 1 &&
                    controller.LastPresentationAttackId ==
                        input.PresentationAttackId &&
                    driver.SuperRequestCount == superBaseline + 1 &&
                    input.StrongAttackRequestCount == strongBaseline + 1;
                superEvidence = string.Format(
                    "brawlDamage={0:F3}, chargeReady={1}, accepted={2}, pollFrames={3}, " +
                    "settleFrames={4}, supers={5}->{6}, strong={7}->{8}, " +
                    "superPresentation={9}->{10}",
                    appliedChargeDamage,
                    chargedThroughBrawlHealth,
                    superAccepted,
                    superPollFrames,
                    settleFrames,
                    superUseBaseline,
                    actor.SupersUsed,
                    strongBeforeSuper,
                    input.StrongAttackRequestCount,
                    superBeforePresentation,
                    driver.SuperRequestCount);

                bool vendorPostureStayedDormant = shooter != null &&
                    !shooter.enabled && shooter.rWeapon == null &&
                    shooter.lWeapon == null && shooter.weaponIKAdjustList == null &&
                    !shooter.isReloadingWeapon &&
                    shooter.ExtraAmmo == extraAmmoBaseline &&
                    ammo != null && !ammo.enabled &&
                    ammo.ammoListData == null && ammo.itemManager == null &&
                    AmmoSignature(ammo) == ammoBaseline &&
                    melee != null && !melee.enabled &&
                    melee.Members != null && melee.Members.Count == 0 &&
                    melee.leftWeapon == null && melee.rightWeapon == null &&
                    melee.BlockedDamageHitCount == blockedDamageBaseline &&
                    collector != null && !collector.enabled &&
                    !HasForbiddenVendorDamageComponent(actor.gameObject);
                bool projectPresentationStayedClean = projectHealth != null &&
                    Mathf.Approximately(projectHealth.Current, projectHealthBaseline) &&
                    actor.AnimationPresentationFailureCount ==
                        animationFailureBaseline &&
                    actor.WeaponPresentationFailureCount == weaponFailureBaseline &&
                    presenter.RuntimeFaultCount == presenterFaultBaseline &&
                    presenter.AimRequestCount == aimBaseline + 6 &&
                    presenter.AimReleaseCount == aimReleaseBaseline + 3 &&
                    !presenter.AimPresented;
                bool vendorResourcesStayedPinned = Mathf.Approximately(
                        controller.currentHealth, vendorHealthBaseline) &&
                    controller.isDead == vendorDeadBaseline &&
                    Mathf.Approximately(
                        controller.InternalMotorStamina, vendorStaminaBaseline) &&
                    controller.IsInternalMotorStaminaPinned;
                bool inputAndSchedulerStayedOwnedByBrawl =
                    input.SchedulerCompleteCount > schedulerBaseline &&
                    input.SchedulerStartCount == input.SchedulerCompleteCount &&
                    input.InputUpdateCount == 0 && input.MoveReadCount == 0 &&
                    input.SuppressedVendorPathCount ==
                        suppressedVendorPathBaseline &&
                    input.WeakAttackRequestCount == weakBaseline + 2 &&
                    input.StrongAttackRequestCount == strongBaseline + 1;

                authorityProven = vendorPostureStayedDormant &&
                    projectPresentationStayedClean &&
                    vendorResourcesStayedPinned &&
                    inputAndSchedulerStayedOwnedByBrawl;
                authorityEvidence = string.Format(
                    "scheduler={0}->{1}, physicalReads={2}/{3}, vendorPaths={4}->{5}, " +
                    "aim={6}->{7}, releases={8}->{9}, meleeWindows={10}->{11}, " +
                    "blockedDamage={12}->{13}, ammo='{14}', vendorHealth={15:F3}->{16:F3}, " +
                    "vendorStamina={17:F3}->{18:F3}",
                    schedulerBaseline,
                    input.SchedulerCompleteCount,
                    input.InputUpdateCount,
                    input.MoveReadCount,
                    suppressedVendorPathBaseline,
                    input.SuppressedVendorPathCount,
                    aimBaseline,
                    presenter.AimRequestCount,
                    aimReleaseBaseline,
                    presenter.AimReleaseCount,
                    suppressedWindowBaseline,
                    melee != null ? melee.SuppressedAttackWindowCount : int.MinValue,
                    blockedDamageBaseline,
                    melee != null ? melee.BlockedDamageHitCount : int.MinValue,
                    ammoBaseline,
                    vendorHealthBaseline,
                    controller.currentHealth,
                    vendorStaminaBaseline,
                    controller.InternalMotorStamina);
            }

            bool gateClosed = false;
            if (gate != null)
            {
                gate.Deactivate();
                gateClosed = actor != null && !actor.gameObject.activeSelf &&
                    !gate.IsRuntimeActive && gate.IsDormantConfigured &&
                    input != null && !input.RuntimeSchedulingEnabled &&
                    driver != null && !driver.PresentationRequestsEnabled &&
                    presenter != null && presenter.IsDormantConfigured &&
                    !presenter.HasRuntimeSolvers;
            }

            SessionState.SetBool(FixtureReadyKey, fixtureReady);
            SessionState.SetBool(CadenceProvenKey, cadenceProven);
            SessionState.SetBool(SuperProvenKey, superProven);
            SessionState.SetBool(AuthorityProvenKey, authorityProven);
            SessionState.SetBool(GateClosedKey, gateClosed);
            SessionState.SetString(SetupEvidenceKey, setupEvidence);
            SessionState.SetString(CadenceEvidenceKey, cadenceEvidence);
            SessionState.SetString(SuperEvidenceKey, superEvidence);
            SessionState.SetString(AuthorityEvidenceKey, authorityEvidence);

            yield return new ExitPlayMode();

            string expectedPath = SessionState.GetString(
                OriginalScenePathKey, string.Empty);
            bool expectedDirty = SessionState.GetBool(
                OriginalSceneDirtyKey, false);
            bool recordedFixtureReady = SessionState.GetBool(FixtureReadyKey, false);
            bool recordedCadence = SessionState.GetBool(CadenceProvenKey, false);
            bool recordedSuper = SessionState.GetBool(SuperProvenKey, false);
            bool recordedAuthority = SessionState.GetBool(AuthorityProvenKey, false);
            bool recordedGateClosed = SessionState.GetBool(GateClosedKey, false);
            string recordedSetupEvidence = SessionState.GetString(
                SetupEvidenceKey, string.Empty);
            string recordedCadenceEvidence = SessionState.GetString(
                CadenceEvidenceKey, string.Empty);
            string recordedSuperEvidence = SessionState.GetString(
                SuperEvidenceKey, string.Empty);
            string recordedAuthorityEvidence = SessionState.GetString(
                AuthorityEvidenceKey, string.Empty);
            ClearAllSessionState();

            Scene restored = SceneManager.GetActiveScene();
            Assert.That(recordedFixtureReady, Is.True,
                "The disposable production-human Tempest fixture was not live. " +
                recordedSetupEvidence);
            Assert.That(recordedCadence, Is.True,
                "Tempest did not accept/reject/accept against the exact 0.82s cadence. " +
                recordedCadenceEvidence);
            Assert.That(recordedSuper, Is.True,
                "The public Brawl damage path did not charge and present one Tempest Super. " +
                recordedSuperEvidence);
            Assert.That(recordedAuthority, Is.True,
                "Brawl presentation advanced while a vendor combat resource changed. " +
                recordedAuthorityEvidence);
            Assert.That(recordedGateClosed, Is.True,
                "The Tempest production runtime gate did not return fully dormant.");
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

            ClearAllSessionState();
        }

        static BrawlerDefinition BuildPresentationOnlyTempestDefinition(
            GameObject productionPrefab)
        {
            // The live proof is deliberately presentation-only. Null projectile
            // and VFX references keep pools and payload damage outside its scope.
            return new BrawlerDefinition
            {
                id = InvectorTempestMigrationBuilder.RosterId,
                displayName = "Tempest",
                role = "Stormcaller",
                invectorHumanPrefab = productionPrefab,
                maxHealth = 88f,
                damage = 17f,
                attackRange = 9.5f,
                attackRadius = 1.15f,
                cooldown = TempestCooldown,
                hitDelay = 0.32f,
                moveLock = 0.36f,
                moveSpeed = 5.55f,
                autoAimRange = 12f,
                projectileSpeed = 21f,
                specialty = SpellSpecialty.ForSchool(SpellSchool.Storm),
                superName = "EYE OF THE STORM",
                superStyle = BrawlerSuperStyle.ProjectileBlast,
                superDamageMultiplier = 1.62f,
                superRange = 11f,
                superKnockback = 5f,
                superProjectileSpeed = 26.25f,
                superProjectileBlastRadius = 2.3f,
            };
        }

        static Health BuildChargeTarget(Scene scene)
        {
            GameObject target = new GameObject("Tempest Brawl Charge Target");
            target.SetActive(false);
            SceneManager.MoveGameObjectToScene(target, scene);
            target.transform.position = new Vector3(100f, 0f, 100f);
            Health health = target.AddComponent<Health>();
            health.SetMax(1000f);
            InvectorCutoverTestMotor motor =
                target.AddComponent<InvectorCutoverTestMotor>();
            InvectorCutoverTestAnimationDriver animation =
                target.AddComponent<InvectorCutoverTestAnimationDriver>();
            BrawlerController controller = target.AddComponent<BrawlerController>();
            controller.SetMotor(motor);
            controller.SetAnimationDriver(animation);
            controller.team = TeamId.Red;
            controller.moveSpeed = 0f;
            // Run Awake/OnEnable once so the production Health.Damaged binding
            // exists, then keep this direct-charge fixture out of Update loops.
            target.SetActive(true);
            target.SetActive(false);
            return health;
        }

        static void BuildGround(Scene scene)
        {
            GameObject ground = GameObject.CreatePrimitive(PrimitiveType.Cube);
            ground.name = "Tempest Combat Presentation Ground";
            ground.layer = CombatPhysics.GroundLayer;
            ground.transform.position = new Vector3(0f, -0.5f, 0f);
            ground.transform.localScale = new Vector3(20f, 1f, 20f);
            SceneManager.MoveGameObjectToScene(ground, scene);
        }

        static void BuildCamera(Scene scene)
        {
            GameObject cameraRoot = new GameObject(
                "Tempest Combat Presentation Camera",
                typeof(Camera),
                typeof(AudioListener));
            cameraRoot.tag = "MainCamera";
            cameraRoot.transform.position = new Vector3(0f, 8f, -10f);
            cameraRoot.transform.rotation = Quaternion.Euler(30f, 0f, 0f);
            SceneManager.MoveGameObjectToScene(cameraRoot, scene);
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

        static void ClearResultEvidence()
        {
            SessionState.EraseBool(FixtureReadyKey);
            SessionState.EraseBool(CadenceProvenKey);
            SessionState.EraseBool(SuperProvenKey);
            SessionState.EraseBool(AuthorityProvenKey);
            SessionState.EraseBool(GateClosedKey);
            SessionState.EraseString(SetupEvidenceKey);
            SessionState.EraseString(CadenceEvidenceKey);
            SessionState.EraseString(SuperEvidenceKey);
            SessionState.EraseString(AuthorityEvidenceKey);
        }

        static void ClearAllSessionState()
        {
            SessionState.EraseString(OriginalScenePathKey);
            SessionState.EraseBool(OriginalSceneDirtyKey);
            ClearResultEvidence();
        }
    }
}
