using System;
using System.Collections;
using System.Linq;
using System.Reflection;
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
    /// <summary>
    /// Bounded live proof for the currently authored Thorn stack. This gate
    /// certifies topology, semantic request cadence, exact AttackID-0 bow clip
    /// mapping, mesh-derived string/arrow staging, Brawl projectile ownership,
    /// vendor isolation, and clean application of all four IK records. Rendered
    /// animation quality and target-device presentation remain manual gates.
    /// </summary>
    public sealed class InvectorThornBowPresentationPlayModeTests
    {
        const string ProofSceneName = "InvectorThornBowPresentationProof";
        const string SessionPrefix = "BrawlArena.InvectorThornPresentation.";
        const string OriginalScenePathKey = SessionPrefix + "OriginalScenePath";
        const string OriginalSceneDirtyKey = SessionPrefix + "OriginalSceneDirty";
        const string FixtureReadyKey = SessionPrefix + "FixtureReady";
        const string TopologyProvenKey = SessionPrefix + "TopologyProven";
        const string IKRecordsProvenKey = SessionPrefix + "IKRecordsProven";
        const string BowRigProvenKey = SessionPrefix + "BowRigProven";
        const string AnimationMappingProvenKey =
            SessionPrefix + "AnimationMappingProven";
        const string CadenceProvenKey = SessionPrefix + "CadenceProven";
        const string SuperProvenKey = SessionPrefix + "SuperProven";
        const string ProjectilesProvenKey = SessionPrefix + "ProjectilesProven";
        const string AuthorityProvenKey = SessionPrefix + "AuthorityProven";
        const string GateClosedKey = SessionPrefix + "GateClosed";
        const string SetupEvidenceKey = SessionPrefix + "SetupEvidence";
        const string TopologyEvidenceKey = SessionPrefix + "TopologyEvidence";
        const string IKEvidenceKey = SessionPrefix + "IKEvidence";
        const string BowRigEvidenceKey = SessionPrefix + "BowRigEvidence";
        const string AnimationMappingEvidenceKey =
            SessionPrefix + "AnimationMappingEvidence";
        const string CadenceEvidenceKey = SessionPrefix + "CadenceEvidence";
        const string SuperEvidenceKey = SessionPrefix + "SuperEvidence";
        const string ProjectileEvidenceKey = SessionPrefix + "ProjectileEvidence";
        const string AuthorityEvidenceKey = SessionPrefix + "AuthorityEvidence";

        const string Arrow01Path =
            "Assets/ModularRPGHeroesPBR/Prefabs/Weapons/Arrow01.prefab";
        const string Arrow02Path =
            "Assets/ModularRPGHeroesPBR/Prefabs/Weapons/Arrow02.prefab";
        const float ThornCooldown = 1.1f;
        const int ActivationPollFrames = 180;
        const int CooldownPollFrames = 300;
        const int PresentationPollFrames = 240;
        const int PosePollFrames = 30;
        const float ActivationTimeoutSeconds = 6f;
        const float CooldownTimeoutSeconds = 5f;
        const float PresentationTimeoutSeconds = 5f;
        const float BasicReleaseDelay = 0.48f;
        const float SuperReleaseDelay = 0.14f;
        const float ReleaseTimingTolerance = 0.12f;

        static readonly int WeakAttackStateHash = Animator.StringToHash(
            "FullBody.Attacks.WeakAttacks.Unarmed.A");
        static readonly int StrongAttackStateHash = Animator.StringToHash(
            "FullBody.Attacks.StrongAttacks.Unarmed.A");

        static readonly MethodInfo ResolveCurrentIKAdjustMethod =
            typeof(InvectorBrawlerWeaponPresentation).GetMethod(
                "ResolveCurrentIKAdjust",
                BindingFlags.Instance | BindingFlags.NonPublic);

        [UnityTest]
        [Category("InvectorThornPresentation")]
        public IEnumerator ProductionThornResolvesBowRecordsAndPresentsBrawlArrowsWithoutVendorCombat()
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
            BrawlerDefinition definition = ArenaSceneBuilder.BuildRoster()
                .SingleOrDefault(candidate =>
                    candidate.id == InvectorThornMigrationBuilder.RosterId);
            GameObject productionPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(
                InvectorThornMigrationBuilder.ProductionHumanPrefabPath);
            BrawlerController actor = null;
            if (definition == null)
            {
                setupEvidence = "The generated roster has no Thorn definition.";
            }
            else if (productionPrefab == null)
            {
                setupEvidence = "The generated production-human Thorn prefab is missing.";
            }
            else if (definition.invectorHumanPrefab != productionPrefab)
            {
                setupEvidence =
                    "The Thorn definition does not reference the exact production-human prefab.";
            }
            else
            {
                try
                {
                    actor = GameFlow.Spawn(
                        definition,
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

            InvectorHumanRuntimeGate gate = actor != null
                ? actor.GetComponent<InvectorHumanRuntimeGate>()
                : null;
            Animator animator = actor != null ? actor.GetComponent<Animator>() : null;
            InvectorBrawlerAnimationDriver driver = actor != null
                ? actor.GetComponent<InvectorBrawlerAnimationDriver>()
                : null;
            InvectorShooterMeleeInputAdapter input = actor != null
                ? actor.GetComponent<InvectorShooterMeleeInputAdapter>()
                : null;
            InvectorBrawlerWeaponPresentation presenter = actor != null
                ? actor.GetComponent<InvectorBrawlerWeaponPresentation>()
                : null;
            InvectorBowPresentationRig bowRig = actor != null
                ? actor.GetComponent<InvectorBowPresentationRig>()
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
                   animator != null && animator.isInitialized &&
                   driver != null && driver.PresentationRequestsEnabled &&
                   input != null && input.RuntimeSchedulingEnabled &&
                   presenter != null && presenter.RuntimeEnabled);
                 frame++)
            {
                yield return null;
            }

            if (gate != null && gate.IsRuntimeActive && animator != null &&
                animator.isInitialized && presenter != null &&
                presenter.RuntimeEnabled)
            {
                for (int frame = 0; frame < 5; frame++)
                    yield return new WaitForFixedUpdate();
                yield return null;
            }

            PlayerBrawlerInput physicalInput = actor != null
                ? actor.GetComponent<PlayerBrawlerInput>()
                : null;
            InvectorBrawlerPrefabIdentity identity = actor != null
                ? actor.GetComponent<InvectorBrawlerPrefabIdentity>()
                : null;
            bool fixtureReady = actor != null && gate != null && gate.IsRuntimeActive &&
                animator != null && animator.isInitialized &&
                driver != null && driver.PresentationRequestsEnabled &&
                input != null && input.RuntimeSchedulingEnabled &&
                presenter != null && presenter.RuntimeEnabled &&
                bowRig != null && bowRig.RuntimeEnabled && bowRig.IsConfigured &&
                controller != null && actor.gameObject.scene == proofScene &&
                actor.gameObject.activeInHierarchy &&
                identity != null && identity.Matches(
                    InvectorThornMigrationBuilder.RosterId,
                    InvectorBrawlerPrefabRole.Human) &&
                string.Equals(
                    AssetDatabase.GetAssetPath(productionPrefab),
                    InvectorThornMigrationBuilder.ProductionHumanPrefabPath,
                    StringComparison.Ordinal) &&
                actor.GetComponents<PlayerBrawlerInput>().Length == 1 &&
                physicalInput != null && physicalInput.enabled &&
                input.MovementFeedMode == InvectorMovementFeedMode.BufferedMotor &&
                !input.ProjectMoveActionOwnedByAdapter &&
                Mathf.Approximately(actor.attackCooldown, ThornCooldown);

            if (!fixtureReady && string.IsNullOrEmpty(setupEvidence))
            {
                setupEvidence = gate != null
                    ? gate.FailureMessage
                    : "The production Thorn runtime gate was not assembled.";
            }

            bool topologyProven = false;
            bool ikRecordsProven = false;
            bool bowRigProven = false;
            bool animationMappingProven = false;
            bool cadenceProven = false;
            bool superProven = false;
            bool projectilesProven = false;
            bool authorityProven = false;
            string topologyEvidence = "The live Thorn fixture was unavailable.";
            string ikEvidence = "The live Thorn fixture was unavailable.";
            string bowRigEvidence = "The live Thorn fixture was unavailable.";
            string animationMappingEvidence =
                "The live Thorn fixture was unavailable.";
            string cadenceEvidence = "The live Thorn fixture was unavailable.";
            string superEvidence = "The live Thorn fixture was unavailable.";
            string projectileEvidence = "The live Thorn fixture was unavailable.";
            string authorityEvidence = "The live Thorn fixture was unavailable.";

            if (fixtureReady)
            {
                topologyProven = ProveExactBowTopology(
                    actor, presenter, out topologyEvidence);

                var bowProbe = new BowRigProbe();
                yield return ProveBowRigStaging(
                    presenter, bowRig, bowProbe);
                bowRigProven = bowProbe.Proven;
                bowRigEvidence = bowProbe.Evidence;

                vWeaponIKAdjustList ikList = presenter.ProjectIKAdjustList;
                vWeaponIKAdjust weaponIK = ikList != null
                    ? ikList.GetWeaponIK(presenter.WeaponCategory)
                    : null;
                bool ikDataPinned = ikList != null && weaponIK != null &&
                    presenter.WeaponHeldInLeftHand &&
                    string.Equals(
                        presenter.WeaponCategory,
                        InvectorThornMigrationBuilder.WeaponCategory,
                        StringComparison.Ordinal) &&
                    string.Equals(
                        AssetDatabase.GetAssetPath(ikList),
                        InvectorThornMigrationBuilder.WeaponIKAdjustListPath,
                        StringComparison.Ordinal) &&
                    ResolveCurrentIKAdjustMethod != null;

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
                if (ikDataPinned)
                {
                    for (int i = 0; i < poses.Length; i++)
                        yield return ProvePoseResolution(
                            poses[i], presenter, controller, weaponIK);
                }
                presenter.PresentAim(Vector3.zero);
                controller.isCrouching = false;
                ikRecordsProven = ikDataPinned && poses.All(probe =>
                    probe.Resolved && probe.LateUpdateObserved && probe.Applied &&
                    probe.Unsuppressed && probe.FaultStable);
                ikEvidence = string.Join(
                    " | ", poses.Select(probe => probe.Evidence));

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
                int muzzlePositionBaseline = presenter.MuzzlePositionRequestCount;
                int muzzlePresentationBaseline =
                    presenter.MuzzlePresentationRequestCount;
                int muzzleEmissionBaseline = presenter.MuzzleEmissionCount;
                int animationFailureBaseline =
                    actor.AnimationPresentationFailureCount;
                int weaponFailureBaseline = actor.WeaponPresentationFailureCount;
                int presenterFaultBaseline = presenter.RuntimeFaultCount;
                int suppressedVendorPathBaseline = input.SuppressedVendorPathCount;
                int blockedDamageBaseline = melee != null
                    ? melee.BlockedDamageHitCount
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
                int weakBowReleaseBaseline = bowRig.ReleaseCount;
                var weakAnimation = new AttackAnimationProbe(
                    "Basic",
                    WeakAttackStateHash,
                    InvectorThornMigrationBuilder.BasicAttackClipName,
                    InvectorThornMigrationBuilder.BasicAttackClipPath,
                    BasicReleaseDelay);

                float firstCastAt = Time.time;
                bool firstAccepted = actor.TryAttackDirection(Vector3.forward);
                int attacksAfterFirst = actor.AttacksUsed;
                int basicAfterFirst = driver.BasicAttackRequestCount;
                int weakAfterFirst = input.WeakAttackRequestCount;
                int writesAfterFirst = controller.MeleePresentationWriteCount;
                bool immediateRejected = !actor.TryAttackDirection(Vector3.right);
                bool rejectionKeptCountersStable =
                    actor.AttacksUsed == attacksAfterFirst &&
                    driver.BasicAttackRequestCount == basicAfterFirst &&
                    input.WeakAttackRequestCount == weakAfterFirst &&
                    controller.MeleePresentationWriteCount == writesAfterFirst;

                if (firstAccepted)
                {
                    yield return ProveAttackAnimationAndRelease(
                        animator,
                        bowRig,
                        weakBowReleaseBaseline,
                        firstCastAt,
                        weakAnimation);
                }

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
                    secondCastAt - firstCastAt + 0.001f >= ThornCooldown;
                bool secondAccepted = actor.TryAttackDirection(Vector3.forward);
                float basicSettleDeadline =
                    Time.realtimeSinceStartup + PresentationTimeoutSeconds;
                for (int frame = 0;
                     frame < PresentationPollFrames &&
                     Time.realtimeSinceStartup < basicSettleDeadline &&
                     presenter.MuzzleEmissionCount < muzzleEmissionBaseline + 2;
                     frame++)
                {
                    yield return null;
                }

                cadenceProven = firstAccepted && immediateRejected &&
                    rejectionKeptCountersStable && configuredCooldownElapsed &&
                    secondAccepted &&
                    actor.AttacksUsed == attackUseBaseline + 2 &&
                    driver.BasicAttackRequestCount == basicBaseline + 2 &&
                    input.WeakAttackRequestCount == weakBaseline + 2 &&
                    controller.MeleePresentationWriteCount ==
                        meleeWriteBaseline + 2;
                cadenceEvidence = string.Format(
                    "first={0}, immediateRejected={1}, rejectionStable={2}, " +
                    "elapsed={3:F3}s, cooldownFrames={4}, second={5}, " +
                    "attacks={6}->{7}, basic={8}->{9}, weak={10}->{11}",
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
                    input.WeakAttackRequestCount);

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
                int strongBowReleaseBaseline = bowRig.ReleaseCount;
                var strongAnimation = new AttackAnimationProbe(
                    "Super",
                    StrongAttackStateHash,
                    InvectorThornMigrationBuilder.SuperAttackClipName,
                    InvectorThornMigrationBuilder.SuperAttackClipPath,
                    SuperReleaseDelay);
                float superDeadline =
                    Time.realtimeSinceStartup + PresentationTimeoutSeconds;
                bool superAccepted = false;
                float superAcceptedAt = 0f;
                int superPollFrames = 0;
                for (;
                     superPollFrames < PresentationPollFrames &&
                     Time.realtimeSinceStartup < superDeadline &&
                     !superAccepted;
                     superPollFrames++)
                {
                    superAccepted = actor.TrySuperDirection(Vector3.right);
                    if (superAccepted) superAcceptedAt = Time.time;
                    if (!superAccepted) yield return null;
                }

                if (superAccepted)
                {
                    yield return ProveAttackAnimationAndRelease(
                        animator,
                        bowRig,
                        strongBowReleaseBaseline,
                        superAcceptedAt,
                        strongAnimation);
                }

                float superSettleDeadline =
                    Time.realtimeSinceStartup + PresentationTimeoutSeconds;
                int superSettleFrames = 0;
                for (;
                     superSettleFrames < PresentationPollFrames &&
                     Time.realtimeSinceStartup < superSettleDeadline &&
                     (presenter.MuzzleEmissionCount < muzzleEmissionBaseline + 3 ||
                      presenter.AimPresented);
                     superSettleFrames++)
                {
                    yield return null;
                }

                superProven = chargedThroughBrawlHealth && superAccepted &&
                    !actor.SuperReady && actor.SupersUsed == superUseBaseline + 1 &&
                    driver.SuperRequestCount == superBeforePresentation + 1 &&
                    input.StrongAttackRequestCount == strongBeforeSuper + 1 &&
                    controller.MeleePresentationWriteCount == writesBeforeSuper + 1 &&
                    driver.SuperRequestCount == superBaseline + 1 &&
                    input.StrongAttackRequestCount == strongBaseline + 1;
                superEvidence = string.Format(
                    "brawlDamage={0:F3}, charged={1}, accepted={2}, pollFrames={3}, " +
                    "settleFrames={4}, supers={5}->{6}, strong={7}->{8}, " +
                    "presentation={9}->{10}",
                    appliedChargeDamage,
                    chargedThroughBrawlHealth,
                    superAccepted,
                    superPollFrames,
                    superSettleFrames,
                    superUseBaseline,
                    actor.SupersUsed,
                    strongBeforeSuper,
                    input.StrongAttackRequestCount,
                    superBeforePresentation,
                    driver.SuperRequestCount);

                animationMappingProven = weakAnimation.Proven &&
                    strongAnimation.Proven;
                animationMappingEvidence = weakAnimation.Evidence + " | " +
                    strongAnimation.Evidence;

                bool exactProjectileAssets = actor.projectilePrefab != null &&
                    actor.superProjectilePrefab != null &&
                    actor.projectilePrefab == definition.projectilePrefab &&
                    actor.superProjectilePrefab == definition.superProjectilePrefab &&
                    string.Equals(
                        AssetDatabase.GetAssetPath(actor.projectilePrefab),
                        Arrow01Path,
                        StringComparison.Ordinal) &&
                    string.Equals(
                        AssetDatabase.GetAssetPath(actor.superProjectilePrefab),
                        Arrow02Path,
                        StringComparison.Ordinal) &&
                    actor.superStyle == BrawlerSuperStyle.ProjectileBlast;
                bool arrow01Clone = HasSceneProjectileClone(
                    proofScene, actor.projectilePrefab);
                bool arrow02Clone = HasSceneProjectileClone(
                    proofScene, actor.superProjectilePrefab);
                projectilesProven = exactProjectileAssets && arrow01Clone &&
                    arrow02Clone &&
                    presenter.MuzzlePositionRequestCount ==
                        muzzlePositionBaseline + 3 &&
                    presenter.MuzzlePresentationRequestCount ==
                        muzzlePresentationBaseline + 3 &&
                    presenter.MuzzleEmissionCount == muzzleEmissionBaseline + 3;
                projectileEvidence = string.Format(
                    "assets={0}, Arrow01Clone={1}, Arrow02Clone={2}, " +
                    "muzzlePosition={3}->{4}, presentation={5}->{6}, emission={7}->{8}",
                    exactProjectileAssets,
                    arrow01Clone,
                    arrow02Clone,
                    muzzlePositionBaseline,
                    presenter.MuzzlePositionRequestCount,
                    muzzlePresentationBaseline,
                    presenter.MuzzlePresentationRequestCount,
                    muzzleEmissionBaseline,
                    presenter.MuzzleEmissionCount);

                bool vendorPostureStayedDormant = shooter != null &&
                    !shooter.enabled && shooter.rWeapon == null &&
                    shooter.lWeapon == null && shooter.weaponIKAdjustList == null &&
                    !shooter.isReloadingWeapon &&
                    shooter.ExtraAmmo == extraAmmoBaseline &&
                    shooter.damageLayer.value == 0 &&
                    shooter.blockAimLayer.value == 0 &&
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
                    "scheduler={0}->{1}, reads={2}/{3}, vendorPaths={4}->{5}, " +
                    "aim={6}->{7}, releases={8}->{9}, ammo='{10}', " +
                    "vendorHealth={11:F3}->{12:F3}, vendorStamina={13:F3}->{14:F3}",
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
                    bowRig != null && bowRig.IsDormantConfigured &&
                    !presenter.HasRuntimeSolvers &&
                    presenter.RuntimeHelperCount == 0;
            }

            SessionState.SetBool(FixtureReadyKey, fixtureReady);
            SessionState.SetBool(TopologyProvenKey, topologyProven);
            SessionState.SetBool(IKRecordsProvenKey, ikRecordsProven);
            SessionState.SetBool(BowRigProvenKey, bowRigProven);
            SessionState.SetBool(
                AnimationMappingProvenKey, animationMappingProven);
            SessionState.SetBool(CadenceProvenKey, cadenceProven);
            SessionState.SetBool(SuperProvenKey, superProven);
            SessionState.SetBool(ProjectilesProvenKey, projectilesProven);
            SessionState.SetBool(AuthorityProvenKey, authorityProven);
            SessionState.SetBool(GateClosedKey, gateClosed);
            SessionState.SetString(SetupEvidenceKey, setupEvidence);
            SessionState.SetString(TopologyEvidenceKey, topologyEvidence);
            SessionState.SetString(IKEvidenceKey, ikEvidence);
            SessionState.SetString(BowRigEvidenceKey, bowRigEvidence);
            SessionState.SetString(
                AnimationMappingEvidenceKey, animationMappingEvidence);
            SessionState.SetString(CadenceEvidenceKey, cadenceEvidence);
            SessionState.SetString(SuperEvidenceKey, superEvidence);
            SessionState.SetString(ProjectileEvidenceKey, projectileEvidence);
            SessionState.SetString(AuthorityEvidenceKey, authorityEvidence);

            yield return new ExitPlayMode();

            string expectedPath = SessionState.GetString(
                OriginalScenePathKey, string.Empty);
            bool expectedDirty = SessionState.GetBool(
                OriginalSceneDirtyKey, false);
            bool recordedFixture = SessionState.GetBool(FixtureReadyKey, false);
            bool recordedTopology = SessionState.GetBool(TopologyProvenKey, false);
            bool recordedIK = SessionState.GetBool(IKRecordsProvenKey, false);
            bool recordedBowRig = SessionState.GetBool(BowRigProvenKey, false);
            bool recordedAnimationMapping = SessionState.GetBool(
                AnimationMappingProvenKey, false);
            bool recordedCadence = SessionState.GetBool(CadenceProvenKey, false);
            bool recordedSuper = SessionState.GetBool(SuperProvenKey, false);
            bool recordedProjectiles = SessionState.GetBool(
                ProjectilesProvenKey, false);
            bool recordedAuthority = SessionState.GetBool(AuthorityProvenKey, false);
            bool recordedGateClosed = SessionState.GetBool(GateClosedKey, false);
            string recordedSetupEvidence = SessionState.GetString(
                SetupEvidenceKey, string.Empty);
            string recordedTopologyEvidence = SessionState.GetString(
                TopologyEvidenceKey, string.Empty);
            string recordedIKEvidence = SessionState.GetString(
                IKEvidenceKey, string.Empty);
            string recordedBowRigEvidence = SessionState.GetString(
                BowRigEvidenceKey, string.Empty);
            string recordedAnimationMappingEvidence = SessionState.GetString(
                AnimationMappingEvidenceKey, string.Empty);
            string recordedCadenceEvidence = SessionState.GetString(
                CadenceEvidenceKey, string.Empty);
            string recordedSuperEvidence = SessionState.GetString(
                SuperEvidenceKey, string.Empty);
            string recordedProjectileEvidence = SessionState.GetString(
                ProjectileEvidenceKey, string.Empty);
            string recordedAuthorityEvidence = SessionState.GetString(
                AuthorityEvidenceKey, string.Empty);
            ClearAllSessionState();

            Scene restored = SceneManager.GetActiveScene();
            Assert.That(recordedFixture, Is.True,
                "The disposable production-human Thorn fixture was not live. " +
                recordedSetupEvidence);
            Assert.That(recordedTopology, Is.True,
                "The live BowVisual/Arrow2/NockPoint topology changed. " +
                recordedTopologyEvidence);
            Assert.That(recordedIK, Is.True,
                "All four configured BrawlWizardBow records must resolve and apply through " +
                "the live presenter's current-state selector. " + recordedIKEvidence);
            Assert.That(recordedBowRig, Is.True,
                "The generated Arrow2/string staging contract did not draw, release, " +
                "restore, or follow visibility deterministically. " +
                recordedBowRigEvidence);
            Assert.That(recordedAnimationMapping, Is.True,
                "The AttackID-0 weak/strong states did not play the exact Thorn bow " +
                "clips and release the staged Arrow2 at Brawl timing. " +
                recordedAnimationMappingEvidence);
            Assert.That(recordedCadence, Is.True,
                "Thorn did not preserve accept/reject/accept at its 1.1s cadence. " +
                recordedCadenceEvidence);
            Assert.That(recordedSuper, Is.True,
                "The Brawl damage path did not charge and present one Explosive Arrow. " +
                recordedSuperEvidence);
            Assert.That(recordedProjectiles, Is.True,
                "Exact Brawl Arrow01/Arrow02 projectile clones and muzzle emissions " +
                "were not observed. " + recordedProjectileEvidence);
            Assert.That(recordedAuthority, Is.True,
                "Thorn presentation advanced while a vendor combat/resource path changed. " +
                recordedAuthorityEvidence);
            Assert.That(recordedGateClosed, Is.True,
                "The Thorn production runtime gate did not return fully dormant.");
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

        static IEnumerator ProveBowRigStaging(
            InvectorBrawlerWeaponPresentation presenter,
            InvectorBowPresentationRig rig,
            BowRigProbe probe)
        {
            if (presenter == null || rig == null || !rig.IsConfigured ||
                !rig.RuntimeEnabled || rig.ArrowVisual == null ||
                rig.BowString == null)
            {
                probe.Evidence = "The root bow rig was not live and configured.";
                yield break;
            }

            Transform arrow = rig.ArrowVisual;
            Vector3 authoredLocalPosition = arrow.localPosition;
            Quaternion authoredLocalRotation = arrow.localRotation;
            int aimBaseline = rig.AimStageCount;
            int releaseBaseline = rig.ReleaseCount;
            int restoreBaseline = rig.ArrowRestoreCount;
            bool idleRest = arrow.gameObject.activeSelf &&
                LinePointMatches(
                    rig.BowString,
                    1,
                    rig.StringRestAnchor.position);

            presenter.PresentAim(Vector3.forward);
            yield return null;
            bool drawStaged = rig.AimStaged &&
                rig.AimStageCount == aimBaseline + 1 &&
                arrow.gameObject.activeSelf &&
                Vector3.Distance(
                    arrow.TransformPoint(rig.ArrowNockLocalPoint),
                    rig.NockPoint.position) < 0.0001f &&
                LinePointMatches(
                    rig.BowString,
                    0,
                    rig.StringTopAnchor.position) &&
                LinePointMatches(
                    rig.BowString,
                    1,
                    rig.NockPoint.position) &&
                LinePointMatches(
                    rig.BowString,
                    2,
                    rig.StringBottomAnchor.position);

            presenter.PresentMuzzle(
                rig.NockPoint.position,
                Vector3.forward);
            bool releasedImmediately = rig.ReleasePending && !rig.AimStaged &&
                rig.ReleaseCount == releaseBaseline + 1 &&
                !arrow.gameObject.activeSelf &&
                LinePointMatches(
                    rig.BowString,
                    1,
                    rig.StringRestAnchor.position);

            for (int frame = 0;
                 frame < PresentationPollFrames && rig.ReleasePending;
                 frame++)
            {
                yield return null;
            }
            bool restored = !rig.ReleasePending && arrow.gameObject.activeSelf &&
                rig.ArrowRestoreCount == restoreBaseline + 1 &&
                Vector3.Distance(
                    arrow.localPosition,
                    authoredLocalPosition) < 0.0001f &&
                Quaternion.Angle(
                    arrow.localRotation,
                    authoredLocalRotation) < 0.01f;

            presenter.SetVisible(false);
            bool hidden = !presenter.Visible && !rig.PresentationVisible &&
                !arrow.gameObject.activeSelf &&
                !rig.AimStaged && !rig.ReleasePending;
            presenter.SetVisible(true);
            bool visibleRestored = presenter.Visible && rig.PresentationVisible &&
                arrow.gameObject.activeSelf &&
                LinePointMatches(
                    rig.BowString,
                    1,
                    rig.StringRestAnchor.position);
            presenter.PresentAim(Vector3.zero);

            probe.Proven = idleRest && drawStaged && releasedImmediately &&
                restored && hidden && visibleRestored;
            probe.Evidence = string.Format(
                "idle={0}, draw={1}, release={2}, restored={3}, " +
                "hidden={4}, visible={5}, aim={6}->{7}, releaseCount={8}->{9}, " +
                "restore={10}->{11}",
                idleRest,
                drawStaged,
                releasedImmediately,
                restored,
                hidden,
                visibleRestored,
                aimBaseline,
                rig.AimStageCount,
                releaseBaseline,
                rig.ReleaseCount,
                restoreBaseline,
                rig.ArrowRestoreCount);
        }

        static IEnumerator ProveAttackAnimationAndRelease(
            Animator animator,
            InvectorBowPresentationRig rig,
            int releaseBaseline,
            float acceptedAt,
            AttackAnimationProbe probe)
        {
            int fullBodyLayer = animator != null
                ? animator.GetLayerIndex("FullBody")
                : -1;
            float deadline = Time.realtimeSinceStartup + PresentationTimeoutSeconds;
            for (int frame = 0;
                 frame < PresentationPollFrames &&
                 Time.realtimeSinceStartup < deadline &&
                 !(probe.StateObserved && probe.ClipObserved &&
                   probe.ReleaseObserved);
                 frame++)
            {
                if (fullBodyLayer >= 0)
                {
                    AnimatorStateInfo current =
                        animator.GetCurrentAnimatorStateInfo(fullBodyLayer);
                    AnimatorStateInfo next = animator.IsInTransition(fullBodyLayer)
                        ? animator.GetNextAnimatorStateInfo(fullBodyLayer)
                        : default;
                    probe.StateObserved |=
                        current.fullPathHash == probe.ExpectedStateHash ||
                        next.fullPathHash == probe.ExpectedStateHash;
                    probe.ClipObserved |=
                        ContainsClip(
                            animator.GetCurrentAnimatorClipInfo(fullBodyLayer),
                            probe.ExpectedClipName,
                            probe.ExpectedClipPath) ||
                        (animator.IsInTransition(fullBodyLayer) && ContainsClip(
                            animator.GetNextAnimatorClipInfo(fullBodyLayer),
                            probe.ExpectedClipName,
                            probe.ExpectedClipPath));
                }

                if (!probe.ReleaseObserved && rig != null &&
                    rig.ReleaseCount > releaseBaseline)
                {
                    probe.ReleaseObserved = true;
                    probe.ReleaseElapsed = Time.time - acceptedAt;
                    probe.ArrowHiddenAtRelease = rig.ArrowVisual != null &&
                        !rig.ArrowVisual.gameObject.activeSelf;
                    probe.StringRestAtRelease = rig.BowString != null &&
                        LinePointMatches(
                            rig.BowString,
                            1,
                            rig.StringRestAnchor.position);
                }
                yield return null;
            }

            probe.TimingMatched = probe.ReleaseObserved &&
                Mathf.Abs(probe.ReleaseElapsed - probe.ExpectedReleaseDelay) <=
                ReleaseTimingTolerance;
            probe.Proven = fullBodyLayer >= 0 && probe.StateObserved &&
                probe.ClipObserved && probe.ReleaseObserved &&
                probe.ArrowHiddenAtRelease && probe.StringRestAtRelease &&
                probe.TimingMatched;
            probe.Evidence = string.Format(
                "{0}: layer={1}, state={2}, clip={3}, release={4}, " +
                "elapsed={5:F3}/{6:F3}, timing={7}, arrowHidden={8}, stringRest={9}",
                probe.Label,
                fullBodyLayer,
                probe.StateObserved,
                probe.ClipObserved,
                probe.ReleaseObserved,
                probe.ReleaseElapsed,
                probe.ExpectedReleaseDelay,
                probe.TimingMatched,
                probe.ArrowHiddenAtRelease,
                probe.StringRestAtRelease);
        }

        static bool ContainsClip(
            AnimatorClipInfo[] clips,
            string expectedName,
            string expectedPath)
        {
            for (int i = 0; i < clips.Length; i++)
            {
                AnimationClip clip = clips[i].clip;
                if (clip != null && string.Equals(
                        clip.name, expectedName, StringComparison.Ordinal) &&
                    string.Equals(
                        AssetDatabase.GetAssetPath(clip),
                        expectedPath,
                        StringComparison.Ordinal))
                {
                    return true;
                }
            }
            return false;
        }

        static bool LinePointMatches(
            LineRenderer line,
            int index,
            Vector3 expectedWorldPosition)
        {
            return line != null && index >= 0 && index < line.positionCount &&
                Vector3.Distance(
                    line.transform.TransformPoint(line.GetPosition(index)),
                    expectedWorldPosition) < 0.0001f;
        }

        static IEnumerator ProvePoseResolution(
            PoseProbe probe,
            InvectorBrawlerWeaponPresentation presenter,
            BrawlInvectorThirdPersonController controller,
            vWeaponIKAdjust weaponIK)
        {
            controller.isCrouching = probe.Crouching;
            presenter.PresentAim(probe.AimDirection);

            object expected = weaponIK.GetIKAdjust(
                probe.StateName, presenter.WeaponHeldInLeftHand);
            object resolved = ResolveCurrentIKAdjustMethod != null
                ? ResolveCurrentIKAdjustMethod.Invoke(presenter, null)
                : null;
            int lateUpdateBaseline = presenter.GatedLateUpdateCount;
            int appliedBaseline = presenter.AppliedIKPassCount;
            int suppressedBaseline = presenter.SuppressedIKPassCount;
            int invalidBaseline = presenter.InvalidPoseCount;
            int faultBaseline = presenter.RuntimeFaultCount;

            for (int frame = 0;
                 frame < PosePollFrames &&
                 presenter.GatedLateUpdateCount == lateUpdateBaseline;
                 frame++)
            {
                yield return null;
                probe.Frames = frame + 1;
            }

            probe.Resolved = expected != null && ReferenceEquals(resolved, expected) &&
                controller.isCrouching == probe.Crouching &&
                presenter.AimPresented == probe.Aiming;
            probe.LateUpdateObserved =
                presenter.GatedLateUpdateCount > lateUpdateBaseline;
            probe.Applied = presenter.AppliedIKPassCount > appliedBaseline;
            probe.Unsuppressed =
                presenter.SuppressedIKPassCount == suppressedBaseline &&
                presenter.InvalidPoseCount == invalidBaseline;
            probe.FaultStable = presenter.RuntimeFaultCount == faultBaseline;
            probe.Evidence = string.Format(
                "{0}: resolved={1}, late={2}, appliedClean={3}/{4}, " +
                "faultStable={5}, frames={6}, applied={7}->{8}, " +
                "suppressed={9}->{10}, invalid={11}->{12}, last={13}",
                probe.StateName,
                probe.Resolved,
                probe.LateUpdateObserved,
                probe.Applied,
                probe.Unsuppressed,
                probe.FaultStable,
                probe.Frames,
                appliedBaseline,
                presenter.AppliedIKPassCount,
                suppressedBaseline,
                presenter.SuppressedIKPassCount,
                invalidBaseline,
                presenter.InvalidPoseCount,
                presenter.LastSuppression);
        }

        static bool ProveExactBowTopology(
            BrawlerController actor,
            InvectorBrawlerWeaponPresentation presenter,
            out string evidence)
        {
            GameObject presentationAsset = AssetDatabase.LoadAssetAtPath<GameObject>(
                InvectorThornMigrationBuilder.WeaponPrefabPath);
            Transform assetNock = presentationAsset != null
                ? FindDescendant(
                    presentationAsset.transform,
                    InvectorThornMigrationBuilder.NockPointName)
                : null;
            Transform leftSocket = FindDescendant(
                actor.transform, InvectorThornMigrationBuilder.LeftWeaponSocketName);
            Transform rightSocket = FindDescendant(
                actor.transform, InvectorThornMigrationBuilder.RightWeaponSocketName);
            Transform presentation = FindDescendant(
                actor.transform, InvectorThornMigrationBuilder.WeaponPresentationName);
            Transform bow = presentation != null
                ? FindDescendant(
                    presentation, InvectorThornMigrationBuilder.BowVisualName)
                : null;
            Transform arrow = FindDescendant(
                actor.transform, InvectorThornMigrationBuilder.AuthoredArrowName);
            Transform nock = presentation != null
                ? FindDescendant(
                    presentation, InvectorThornMigrationBuilder.NockPointName)
                : null;
            Transform muzzle = presentation != null
                ? FindDescendant(
                    presentation,
                    InvectorThornMigrationBuilder.SpellOriginName)
                : null;
            Transform stringTop = presentation != null
                ? FindDescendant(
                    presentation,
                    InvectorThornMigrationBuilder.StringTopName)
                : null;
            Transform stringRest = presentation != null
                ? FindDescendant(
                    presentation,
                    InvectorThornMigrationBuilder.StringRestName)
                : null;
            Transform stringBottom = presentation != null
                ? FindDescendant(
                    presentation,
                    InvectorThornMigrationBuilder.StringBottomName)
                : null;
            LineRenderer[] strings = presentation != null
                ? presentation.GetComponentsInChildren<LineRenderer>(true)
                : Array.Empty<LineRenderer>();
            InvectorBowPresentationRig rig = presenter.BowPresentationRig;
            Transform supportHand = presentation != null
                ? FindDescendant(presentation, "SupportHandTarget")
                : null;
            Transform supportHint = presentation != null
                ? FindDescendant(presentation, "SupportHintTarget")
                : null;
            Renderer bowRenderer = bow != null ? bow.GetComponent<Renderer>() : null;
            Renderer arrowRenderer = arrow != null
                ? arrow.GetComponent<Renderer>()
                : null;
            ParticleSystem[] effects = presentation != null
                ? presentation.GetComponentsInChildren<ParticleSystem>(true)
                : Array.Empty<ParticleSystem>();

            bool nockMatchesAsset = assetNock != null && nock != null &&
                Vector3.Distance(assetNock.localPosition, nock.localPosition) < 0.0001f &&
                Quaternion.Angle(
                    assetNock.localRotation, nock.localRotation) < 0.01f;

            var faults = new System.Collections.Generic.List<string>();
            if (!presenter.WeaponHeldInLeftHand) faults.Add("heldInLeftHand");
            if (!string.Equals(
                    presenter.WeaponCategory,
                    InvectorThornMigrationBuilder.WeaponCategory,
                    StringComparison.Ordinal)) faults.Add("weaponCategory");
            if (leftSocket == null) faults.Add("leftSocket");
            if (rightSocket == null) faults.Add("rightSocket");
            if (presentation == null || presentation.parent != leftSocket)
                faults.Add("presentationParent");
            if (bow == null || !bow.gameObject.activeInHierarchy) faults.Add("bowActive");
            if (FindDescendant(
                    actor.transform,
                    InvectorThornMigrationBuilder.AuthoredBowName) != null)
                faults.Add("authoredBowStillPresent");
            if (arrow == null || arrow.parent != rightSocket ||
                !arrow.gameObject.activeInHierarchy) faults.Add("arrowSocketActive");
            if (nock == null || !nockMatchesAsset) faults.Add("nockAssetPose");
            if (rig == null || rig.gameObject != actor.gameObject ||
                !rig.IsConfigured || !rig.RuntimeEnabled) faults.Add("rigConfigured");
            if (rig == null || rig.ArrowVisual != arrow || rig.NockPoint != nock)
                faults.Add("rigReferences");
            if (muzzle == null || muzzle.parent != nock) faults.Add("muzzleParent");
            if (rig != null && arrow != null && nock != null && Vector3.Distance(
                    nock.position,
                    arrow.TransformPoint(rig.ArrowNockLocalPoint)) >= 0.0001f)
                faults.Add("arrowNockAlignment");
            if (rig != null && arrow != null && muzzle != null && Vector3.Distance(
                    muzzle.position,
                    arrow.TransformPoint(rig.ArrowTipLocalPoint)) >= 0.0001f)
                faults.Add("arrowTipAlignment");
            if (muzzle != null && nock != null &&
                (muzzle.position - nock.position).sqrMagnitude <= 0.000001f)
                faults.Add("muzzleNockSeparation");
            if (strings.Length != 1 || rig == null || rig.BowString != strings[0])
                faults.Add("stringRenderer");
            if (stringTop == null || stringRest == null || stringBottom == null)
                faults.Add("stringAnchors");
            if (rig == null || rig.StringTopAnchor != stringTop ||
                rig.StringRestAnchor != stringRest ||
                rig.StringBottomAnchor != stringBottom) faults.Add("rigStringAnchors");
            if (strings.Length != 1 || strings[0].positionCount != 3 ||
                strings[0].useWorldSpace) faults.Add("stringTopology");
            if (strings.Length == 1 && stringTop != null && stringRest != null &&
                stringBottom != null &&
                (!LinePointMatches(strings[0], 0, stringTop.position) ||
                 !LinePointMatches(strings[0], 1, stringRest.position) ||
                 !LinePointMatches(strings[0], 2, stringBottom.position)))
                faults.Add("stringPointAlignment");
            if (supportHand == null || supportHint == null) faults.Add("supportTargets");
            if (bowRenderer == null || arrowRenderer == null ||
                !string.Equals(
                    AssetDatabase.GetAssetPath(bowRenderer.sharedMaterial),
                    InvectorThornMigrationBuilder.WeaponsMaterialPath,
                    StringComparison.Ordinal) ||
                !string.Equals(
                    AssetDatabase.GetAssetPath(arrowRenderer.sharedMaterial),
                    InvectorThornMigrationBuilder.WeaponsMaterialPath,
                    StringComparison.Ordinal)) faults.Add("weaponMaterials");
            if (effects.Length != 1 || effects[0].main.playOnAwake ||
                effects[0].main.loop) faults.Add("muzzleEffects");

            bool proven = faults.Count == 0;
            evidence = proven
                ? "all topology conditions held"
                : "failed: " + string.Join(", ", faults.ToArray());
            return proven;
        }

        static Health BuildChargeTarget(Scene scene)
        {
            GameObject target = new GameObject("Thorn Brawl Charge Target");
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
            target.SetActive(true);
            target.SetActive(false);
            return health;
        }

        static void BuildGround(Scene scene)
        {
            GameObject ground = GameObject.CreatePrimitive(PrimitiveType.Cube);
            ground.name = "Thorn Bow Presentation Ground";
            ground.layer = CombatPhysics.GroundLayer;
            ground.transform.position = new Vector3(0f, -0.5f, 0f);
            ground.transform.localScale = new Vector3(20f, 1f, 20f);
            SceneManager.MoveGameObjectToScene(ground, scene);
        }

        static void BuildCamera(Scene scene)
        {
            GameObject cameraRoot = new GameObject(
                "Thorn Bow Presentation Camera",
                typeof(Camera),
                typeof(AudioListener));
            cameraRoot.tag = "MainCamera";
            cameraRoot.transform.position = new Vector3(0f, 8f, -10f);
            cameraRoot.transform.rotation = Quaternion.Euler(30f, 0f, 0f);
            SceneManager.MoveGameObjectToScene(cameraRoot, scene);
        }

        static bool HasSceneProjectileClone(Scene scene, GameObject prefab)
        {
            if (prefab == null) return false;
            string expectedName = prefab.name + "(Clone)";
            Projectile[] projectiles = UnityEngine.Object.FindObjectsByType<Projectile>(
                FindObjectsInactive.Include,
                FindObjectsSortMode.None);
            for (int i = 0; i < projectiles.Length; i++)
            {
                Projectile projectile = projectiles[i];
                if (projectile != null && projectile.gameObject.scene == scene &&
                    string.Equals(
                        projectile.gameObject.name,
                        expectedName,
                        StringComparison.Ordinal))
                {
                    return true;
                }
            }
            return false;
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
                case "vShooterWeaponBase":
                case "vProjectileControl":
                case "vProjectileInstantiate":
                case "vObjectDamage":
                case "vDamageSender":
                case "vDamageReceiver":
                case "vHitBox":
                case "vMeleeAttackObject":
                case "vHealthController":
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
            SessionState.EraseBool(TopologyProvenKey);
            SessionState.EraseBool(IKRecordsProvenKey);
            SessionState.EraseBool(BowRigProvenKey);
            SessionState.EraseBool(AnimationMappingProvenKey);
            SessionState.EraseBool(CadenceProvenKey);
            SessionState.EraseBool(SuperProvenKey);
            SessionState.EraseBool(ProjectilesProvenKey);
            SessionState.EraseBool(AuthorityProvenKey);
            SessionState.EraseBool(GateClosedKey);
            SessionState.EraseString(SetupEvidenceKey);
            SessionState.EraseString(TopologyEvidenceKey);
            SessionState.EraseString(IKEvidenceKey);
            SessionState.EraseString(BowRigEvidenceKey);
            SessionState.EraseString(AnimationMappingEvidenceKey);
            SessionState.EraseString(CadenceEvidenceKey);
            SessionState.EraseString(SuperEvidenceKey);
            SessionState.EraseString(ProjectileEvidenceKey);
            SessionState.EraseString(AuthorityEvidenceKey);
        }

        static void ClearAllSessionState()
        {
            SessionState.EraseString(OriginalScenePathKey);
            SessionState.EraseBool(OriginalSceneDirtyKey);
            ClearResultEvidence();
        }

        sealed class BowRigProbe
        {
            public bool Proven;
            public string Evidence = string.Empty;
        }

        sealed class AttackAnimationProbe
        {
            public readonly string Label;
            public readonly int ExpectedStateHash;
            public readonly string ExpectedClipName;
            public readonly string ExpectedClipPath;
            public readonly float ExpectedReleaseDelay;
            public bool StateObserved;
            public bool ClipObserved;
            public bool ReleaseObserved;
            public bool ArrowHiddenAtRelease;
            public bool StringRestAtRelease;
            public bool TimingMatched;
            public float ReleaseElapsed;
            public bool Proven;
            public string Evidence = string.Empty;

            public AttackAnimationProbe(
                string label,
                int expectedStateHash,
                string expectedClipName,
                string expectedClipPath,
                float expectedReleaseDelay)
            {
                Label = label;
                ExpectedStateHash = expectedStateHash;
                ExpectedClipName = expectedClipName;
                ExpectedClipPath = expectedClipPath;
                ExpectedReleaseDelay = expectedReleaseDelay;
            }
        }

        sealed class PoseProbe
        {
            public readonly string StateName;
            public readonly bool Crouching;
            public readonly Vector3 AimDirection;
            public readonly bool Aiming;
            public bool Resolved;
            public bool LateUpdateObserved;
            public bool Applied;
            public bool Unsuppressed;
            public bool FaultStable;
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
