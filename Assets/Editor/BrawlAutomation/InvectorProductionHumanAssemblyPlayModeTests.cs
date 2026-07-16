using System;
using System.Collections;
using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace BrawlArena.EditorAutomation.Tests
{
    public sealed class InvectorProductionHumanAssemblyPlayModeTests
    {
        const string OriginalScenePathSessionKey =
            "BrawlArena.InvectorPhase3E.OriginalScenePath";
        const string OriginalSceneDirtySessionKey =
            "BrawlArena.InvectorPhase3E.OriginalSceneDirty";

        static int observedKills;

        [UnityTest]
        [Category("InvectorProductionHumanCinder")]
        public IEnumerator LiveContextGatedHumanCinderPreservesBrawlAuthorityAndTeardown()
        {
            Scene original = SceneManager.GetActiveScene();
            SessionState.SetString(OriginalScenePathSessionKey, original.path);
            SessionState.SetBool(OriginalSceneDirtySessionKey, original.isDirty);

            yield return new EnterPlayMode();

            string originalPath = SessionState.GetString(
                OriginalScenePathSessionKey, string.Empty);
            Scene originalPlayScene = SceneManager.GetSceneByPath(originalPath);
            if (originalPlayScene.IsValid() && originalPlayScene.isLoaded)
            {
                GameObject[] originalRoots = originalPlayScene.GetRootGameObjects();
                for (int i = 0; i < originalRoots.Length; i++)
                    originalRoots[i].SetActive(false);
            }

            Scene proofScene = SceneManager.CreateScene("InvectorProductionHumanProof");
            SceneManager.SetActiveScene(proofScene);

            GameObject ground = GameObject.CreatePrimitive(PrimitiveType.Cube);
            ground.name = "Proof Ground";
            ground.layer = CombatPhysics.GroundLayer;
            ground.transform.position = new Vector3(0f, -0.5f, 0f);
            ground.transform.localScale = new Vector3(30f, 1f, 30f);
            SceneManager.MoveGameObjectToScene(ground, proofScene);

            GameObject cameraRoot = new GameObject("Proof Main Camera");
            SceneManager.MoveGameObjectToScene(cameraRoot, proofScene);
            cameraRoot.tag = "MainCamera";
            cameraRoot.transform.position = new Vector3(0f, 9f, -10f);
            cameraRoot.transform.rotation = Quaternion.Euler(30f, 0f, 0f);
            Camera proofCamera = cameraRoot.AddComponent<Camera>();
            cameraRoot.AddComponent<AudioListener>();
            BrawlCamera brawlCamera = cameraRoot.AddComponent<BrawlCamera>();

            GameObject blueSpawn = new GameObject("Blue Respawn");
            SceneManager.MoveGameObjectToScene(blueSpawn, proofScene);
            blueSpawn.transform.position = new Vector3(-3f, 0f, 0f);
            GameObject redSpawn = new GameObject("Red Respawn");
            SceneManager.MoveGameObjectToScene(redSpawn, proofScene);
            redSpawn.transform.position = new Vector3(5f, 0f, 0f);

            GameObject matchRoot = new GameObject("Proof Match");
            matchRoot.SetActive(false);
            SceneManager.MoveGameObjectToScene(matchRoot, proofScene);
            MatchManager manager = matchRoot.AddComponent<MatchManager>();
            manager.mode = GameMode.Knockout;
            manager.autoStart = true;
            manager.introDuration = 0f;
            manager.blueSpawns = new[] { blueSpawn.transform };
            manager.redSpawns = new[] { redSpawn.transform };
            matchRoot.SetActive(true);
            // Awake installs the selected secondary-mode defaults. These
            // proof-only timings are then explicit authored overrides rather
            // than an accidental dependency on the former global default.
            manager.respawnDelay = 0.5f;
            manager.matchDuration = 30f;
            manager.scoreToWin = 99;

            GameObject projectilePrefab = new GameObject("Proof Brawl Projectile Prefab");
            projectilePrefab.SetActive(false);
            SceneManager.MoveGameObjectToScene(projectilePrefab, proofScene);
            Projectile authoredProjectile = projectilePrefab.AddComponent<Projectile>();
            authoredProjectile.lifeTime = 2f;
            authoredProjectile.hitRadius = 0.3f;

            GameObject productionPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(
                InvectorMigrationPilotBuilder.ProductionHumanPrefabPath);
            Assert.That(productionPrefab, Is.Not.Null);
            var definition = new BrawlerDefinition
            {
                id = "fire",
                displayName = "Cinder",
                role = "Pyromancer",
                invectorHumanPrefab = productionPrefab,
                maxHealth = 92f,
                damage = 22f,
                moveSpeed = 4.9f,
                attackRange = 8f,
                attackRadius = 1.5f,
                cooldown = 0.08f,
                basicAttackReloadInterval = 0.18f,
                hitDelay = 0.05f,
                moveLock = 0.1f,
                autoAimRange = 12f,
                projectilePrefab = projectilePrefab,
                projectileSpeed = 18f,
                specialty = SpellSpecialty.ForSchool(SpellSchool.Fire),
            };

            GameObject targetRoot = BuildTargetFixture(proofScene, definition);
            Health targetHealth = targetRoot.GetComponent<Health>();

            int rootsBeforeInvalid = UnityEngine.Object.FindObjectsByType<BrawlerController>(
                FindObjectsInactive.Include, FindObjectsSortMode.None).Length;
            NotSupportedException invalidContextRejection = null;
            try
            {
                GameFlow.Spawn(definition, TeamId.Blue, Vector3.zero, false, 1f,
                    BrawlerAssemblyContext.ProductionHumanInvector);
            }
            catch (NotSupportedException exception)
            {
                invalidContextRejection = exception;
            }
            Assert.That(invalidContextRejection, Is.Not.Null);
            int rootsAfterInvalid = UnityEngine.Object.FindObjectsByType<BrawlerController>(
                FindObjectsInactive.Include, FindObjectsSortMode.None).Length;
            Assert.That(rootsAfterInvalid, Is.EqualTo(rootsBeforeInvalid));

            BrawlerController actor = GameFlow.Spawn(
                definition, TeamId.Blue, Vector3.zero, true, 1f,
                BrawlerAssemblyContext.ProductionHumanInvector);
            brawlCamera.SetTarget(actor.transform);

            for (int frame = 0;
                 frame < 120 && (manager.State != MatchState.Playing ||
                                 !actor.GetComponent<InvectorHumanRuntimeGate>().IsRuntimeActive);
                 frame++)
            {
                yield return null;
            }
            yield return new WaitForFixedUpdate();

            InvectorHumanRuntimeGate gate = actor.GetComponent<InvectorHumanRuntimeGate>();
            InvectorBrawlerMotor motor = actor.GetComponent<InvectorBrawlerMotor>();
            InvectorShooterMeleeInputAdapter input =
                actor.GetComponent<InvectorShooterMeleeInputAdapter>();
            InvectorBrawlerAnimationDriver driver =
                actor.GetComponent<InvectorBrawlerAnimationDriver>();
            InvectorBrawlerWeaponPresentation presenter =
                actor.GetComponent<InvectorBrawlerWeaponPresentation>();
            BrawlInvectorThirdPersonController invectorController =
                actor.GetComponent<BrawlInvectorThirdPersonController>();
            BrawlerHitProxy hitProxy = actor.GetComponentInChildren<BrawlerHitProxy>(true);
            PlayerBrawlerInput physicalInput = actor.GetComponent<PlayerBrawlerInput>();

            Assert.That(manager.State, Is.EqualTo(MatchState.Playing));
            Assert.That(gate.IsRuntimeActive, Is.True, gate.FailureMessage);
            Assert.That(actor.Motor, Is.SameAs(motor));
            Assert.That(actor.AnimationDriver, Is.SameAs(driver));
            Assert.That(actor.WeaponPresentation, Is.SameAs(presenter));
            Assert.That(actor.GetComponent<Health>(), Is.SameAs(actor.Health));
            Assert.That(actor.GetComponents<PlayerBrawlerInput>(), Has.Length.EqualTo(1));
            Assert.That(physicalInput.enabled, Is.True);
            Assert.That(actor.GetComponents<CharacterController>(), Is.Empty);
            Assert.That(input.MovementFeedMode,
                Is.EqualTo(InvectorMovementFeedMode.BufferedMotor));
            Assert.That(input.InputUpdateCount, Is.Zero);
            Assert.That(input.MoveReadCount, Is.Zero);
            Assert.That(input.ProjectMoveActionOwnedByAdapter, Is.False);
            Assert.That(input.ExternalFixedUpdateSubscriberCount, Is.Zero);
            Assert.That(actor.gameObject.layer,
                Is.EqualTo(InvectorMigrationPilotBuilder.InvectorPlayerLayer));
            Assert.That(hitProxy.gameObject.layer, Is.EqualTo(CombatPhysics.BrawlerHitboxLayer));
            Assert.That(hitProxy.enabled, Is.False);
            Assert.That(hitProxy.TriggerCollider.enabled, Is.False);
            Assert.That(proofCamera, Is.SameAs(Camera.main));

            Keyboard keyboard = InputSystem.AddDevice<Keyboard>(
                "Phase3EProductionHumanKeyboard");
            keyboard.MakeCurrent();
            Vector3 movementStart = actor.transform.position;
            InputSystem.QueueStateEvent(keyboard, new KeyboardState(Key.W));
            yield return null;
            for (int step = 0; step < 30; step++)
                yield return new WaitForFixedUpdate();
            InputSystem.QueueStateEvent(keyboard, new KeyboardState());
            for (int frame = 0;
                 frame < 10 && motor.BufferedWorldIntent.sqrMagnitude >= 0.0001f;
                 frame++)
            {
                yield return null;
            }
            yield return new WaitForFixedUpdate();

            Vector3 movement = actor.transform.position - movementStart;
            movement.y = 0f;
            Assert.That(movement.magnitude, Is.GreaterThan(0.05f));
            Assert.That(input.SchedulerCompleteCount, Is.GreaterThan(0));
            Assert.That(input.SchedulerStartCount, Is.EqualTo(input.SchedulerCompleteCount));
            Assert.That(motor.ScheduledPrepareCount, Is.EqualTo(input.SchedulerCompleteCount));
            Assert.That(motor.ScheduledCompleteCount, Is.EqualTo(input.SchedulerCompleteCount));
            Assert.That(invectorController.MotorUpdateCount,
                Is.EqualTo(input.SchedulerCompleteCount));
            Assert.That(invectorController.AnimatorUpdateCount,
                Is.EqualTo(input.SchedulerCompleteCount));
            Assert.That(input.InputUpdateCount, Is.Zero);
            Assert.That(input.MoveReadCount, Is.Zero);

            targetRoot.transform.position = actor.transform.position + Vector3.forward * 4f;
            float targetHealthBefore = targetHealth.Current;
            int aimBefore = presenter.AimRequestCount;
            int aimReleaseBefore = presenter.AimReleaseCount;
            int muzzleBefore = presenter.MuzzlePresentationRequestCount;
            int emissionBefore = presenter.MuzzleEmissionCount;
            Assert.That(actor.BasicAttackCharges,
                Is.EqualTo(MobileCombatRules.BasicAttackChargeCapacity));
            Assert.That(actor.TryAttackAuto(), Is.True);
            Assert.That(actor.BasicAttackCharges,
                Is.EqualTo(MobileCombatRules.BasicAttackChargeCapacity - 1));
            Assert.That(actor.TryAttackAuto(), Is.False,
                "The active cooldown must reject without double-spending a charge.");
            Assert.That(actor.BasicAttackCharges,
                Is.EqualTo(MobileCombatRules.BasicAttackChargeCapacity - 1));
            for (int frame = 0;
                 frame < 240 && Mathf.Approximately(targetHealth.Current, targetHealthBefore);
                 frame++)
            {
                yield return null;
            }

            Assert.That(targetHealth.Current, Is.LessThan(targetHealthBefore));
            Assert.That(driver.BasicAttackRequestCount, Is.EqualTo(1));
            Assert.That(input.WeakAttackRequestCount, Is.EqualTo(1));
            Assert.That(presenter.AimRequestCount, Is.EqualTo(aimBefore + 2));
            Assert.That(presenter.AimReleaseCount, Is.EqualTo(aimReleaseBefore + 1));
            Assert.That(presenter.MuzzlePresentationRequestCount, Is.EqualTo(muzzleBefore + 1));
            Assert.That(presenter.MuzzleEmissionCount, Is.EqualTo(emissionBefore + 1));
            Assert.That(presenter.AimPresented, Is.False);
            Assert.That(invectorController.currentHealth, Is.GreaterThan(0f));
            float vendorHealthBaseline = invectorController.currentHealth;

            int chargesBeforeSuper = actor.BasicAttackCharges;
            actor.maxSuperCharge = 1f;
            Assert.That(actor.SuperReady, Is.True,
                "Applied Brawl damage should have charged this low-threshold proof Super.");
            Assert.That(actor.TrySuperDirection(Vector3.forward), Is.True);
            Assert.That(actor.BasicAttackCharges, Is.EqualTo(chargesBeforeSuper),
                "The Brawl Super path must remain separate from basic charges.");
            for (int frame = 0;
                 frame < 120 && actor.BasicAttackCharges <
                     MobileCombatRules.BasicAttackChargeCapacity;
                 frame++)
            {
                yield return null;
            }
            Assert.That(actor.BasicAttackCharges,
                Is.EqualTo(MobileCombatRules.BasicAttackChargeCapacity),
                "The next basic charge should regenerate automatically.");

            observedKills = 0;
            manager.Kill += CountKill;
            int deathRequests = driver.DeathRequestCount;
            int respawnRequests = driver.RespawnRequestCount;
            int respawnResets = presenter.RespawnResetCount;
            float lethalApplied = actor.Health.TakeDamage(actor.Health.Max + 50f, targetRoot);
            Assert.That(lethalApplied, Is.EqualTo(actor.Health.Max).Within(0.001f));
            Assert.That(actor.Health.IsDead, Is.True);
            Assert.That(motor.IsSuspended, Is.True);

            for (int frame = 0;
                 frame < 300 && (actor.Health.IsDead ||
                                 driver.RespawnRequestCount == respawnRequests);
                 frame++)
            {
                yield return null;
            }

            Assert.That(observedKills, Is.EqualTo(1));
            Assert.That(manager.RedScore, Is.EqualTo(1));
            Assert.That(driver.DeathRequestCount, Is.EqualTo(deathRequests + 1));
            Assert.That(driver.RespawnRequestCount, Is.EqualTo(respawnRequests + 1));
            Assert.That(presenter.RespawnResetCount, Is.EqualTo(respawnResets + 1));
            Assert.That(actor.Health.IsDead, Is.False);
            Assert.That(actor.Health.Current, Is.EqualTo(actor.Health.Max).Within(0.001f));
            Assert.That(actor.BasicAttackCharges,
                Is.EqualTo(MobileCombatRules.BasicAttackChargeCapacity),
                "Respawn must restore the deterministic three-charge baseline.");
            Assert.That(actor.Health.Invulnerable, Is.True);
            Assert.That(actor.Health.TakeDamage(5f, targetRoot), Is.Zero);
            Assert.That(motor.IsSuspended, Is.False);
            Assert.That(Vector3.Distance(actor.transform.position, blueSpawn.transform.position),
                Is.LessThan(0.35f));
            Assert.That(invectorController.currentHealth,
                Is.EqualTo(vendorHealthBaseline).Within(0.001f));
            Assert.That(invectorController.isDead, Is.False);
            Assert.That(gate.IsRuntimeActive, Is.True, gate.FailureMessage);
            Assert.That(actor.AnimationPresentationFailureCount, Is.Zero);
            Assert.That(actor.WeaponPresentationFailureCount, Is.Zero);
            Assert.That(presenter.RuntimeFaultCount, Is.Zero);
            Assert.That(presenter.RuntimeHelperCount, Is.Zero);

            InputSystem.RemoveDevice(keyboard);
            gate.Deactivate();
            Assert.That(actor.gameObject.activeSelf, Is.False);
            Assert.That(gate.IsRuntimeActive, Is.False);
            Assert.That(gate.IsDormantConfigured, Is.True, gate.FailureMessage);
            Assert.That(input.RuntimeSchedulingEnabled, Is.False);
            Assert.That(driver.PresentationRequestsEnabled, Is.False);
            Assert.That(presenter.RuntimeEnabled, Is.False);
            Assert.That(presenter.HasRuntimeSolvers, Is.False);

            yield return new ExitPlayMode();

            string expectedOriginalPath = SessionState.GetString(
                OriginalScenePathSessionKey, string.Empty);
            bool expectedOriginalDirty = SessionState.GetBool(
                OriginalSceneDirtySessionKey, false);
            SessionState.EraseString(OriginalScenePathSessionKey);
            SessionState.EraseBool(OriginalSceneDirtySessionKey);
            Scene restored = SceneManager.GetActiveScene();
            Assert.That(restored.path, Is.EqualTo(expectedOriginalPath));
            Assert.That(restored.isDirty, Is.EqualTo(expectedOriginalDirty));
        }

        static GameObject BuildTargetFixture(Scene scene, BrawlerDefinition definition)
        {
            BrawlerController controller = GameFlow.Spawn(
                definition,
                TeamId.Red,
                new Vector3(0f, 0f, 5f),
                true,
                1f,
                BrawlerAssemblyContext.ProductionHumanInvector);
            Assert.That(controller, Is.Not.Null);
            Assert.That(controller.gameObject.scene, Is.EqualTo(scene));

            controller.gameObject.name = "Proof Red Target";
            Health health = controller.Health;
            Assert.That(health, Is.Not.Null);
            health.SetMax(200f);
            controller.displayName = "Proof Target";
            controller.attackDamage = 1f;
            controller.attackRange = 1f;
            controller.attackRadius = 0.65f;
            controller.autoAimRange = 1f;
            PlayerBrawlerInput physicalInput = controller.GetComponent<PlayerBrawlerInput>();
            Assert.That(physicalInput, Is.Not.Null);
            physicalInput.enabled = false;
            return controller.gameObject;
        }

        static void CountKill(BrawlerController victim, BrawlerController attacker)
        {
            observedKills++;
        }
    }
}
