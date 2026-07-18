using System.Collections;
using System.IO;
using NUnit.Framework;
using Unity.AI.Navigation;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace BrawlArena.EditorAutomation.Tests
{
    [Category("ControlZoneMatchLoop")]
    public sealed class ControlZoneMatchLoopEditModeTests
    {
        const string OriginalScenePathKey = "BrawlArena.ControlZone.OriginalScenePath";
        const string OriginalSceneDirtyKey = "BrawlArena.ControlZone.OriginalSceneDirty";

        [TearDown]
        public void TearDown()
        {
            MatchSetup.Mode = GameMode.ControlZone;
            MatchSetup.CharacterIndex = -1;
            MatchSetup.FromMenu = false;
        }

        [Test]
        public void PrimaryRulesAndLineupsAreExactStableAndThreeVersusThree()
        {
            Assert.That((int)GameMode.Knockout, Is.Zero);
            Assert.That((int)GameMode.GemGrab, Is.EqualTo(1));
            Assert.That((int)GameMode.ControlZone, Is.EqualTo(2));
            Assert.That(MatchSetup.Mode, Is.EqualTo(GameMode.ControlZone));
            Assert.That(new ProgressData().selectedMode, Is.EqualTo((int)GameMode.ControlZone));
            Assert.That(ControlZoneRules.TeamSize, Is.EqualTo(3));
            Assert.That(ControlZoneRules.RegulationDuration, Is.EqualTo(180f));
            Assert.That(ControlZoneRules.ScoreLimit, Is.EqualTo(90));
            Assert.That(ControlZoneRules.RegulationRadius, Is.EqualTo(7f));
            Assert.That(ControlZoneRules.OvertimeRadius, Is.EqualTo(12f));
            Assert.That(ControlZoneRules.RespawnDelay, Is.EqualTo(6f));
            Assert.That(ControlZoneRules.SpawnProtectionDuration, Is.EqualTo(1.75f));
            Assert.That(ArenaLayout.ActiveTeamSize(GameMode.ControlZone), Is.EqualTo(3));
            Assert.That(ArenaLayout.ActiveTeamSize(GameMode.Knockout), Is.EqualTo(5));

            CollectionAssert.AreEqual(new[] { 1, 2, 3 },
                MatchLineupPlanner.BuildRotatedTeamDefinitionIndices(4, 3, 1, 2));
            CollectionAssert.AreEqual(new[] { 3, 0, 1 },
                MatchLineupPlanner.BuildRotatedTeamDefinitionIndices(4, 3, 3, 0));
            CollectionAssert.AreEqual(new[] { 1, 2, 3 },
                MatchLineupPlanner.BuildRotatedTeamDefinitionIndices(4, 3, 1, 2),
                "Identical selection inputs must never depend on a hidden random seed.");
        }

        [Test]
        public void ScoreClockRequiresOneUninterruptedSecondAndResetsEveryBreak()
        {
            var clock = new ControlZoneScoreClock();
            Assert.That(clock.Advance(ControlZoneState.BlueControlled, 0.6f, out _), Is.Zero);
            Assert.That(clock.Progress, Is.EqualTo(0.6f).Within(0.0001f));
            Assert.That(clock.Advance(ControlZoneState.Contested, 0.7f, out _), Is.Zero);
            Assert.That(clock.Progress, Is.Zero);
            Assert.That(clock.Advance(ControlZoneState.BlueControlled, 0.6f, out _), Is.Zero);
            Assert.That(clock.Advance(ControlZoneState.Empty, 0.6f, out _), Is.Zero);
            Assert.That(clock.Progress, Is.Zero);
            Assert.That(clock.Advance(ControlZoneState.BlueControlled, 0.6f, out _), Is.Zero);
            Assert.That(clock.Advance(ControlZoneState.RedControlled, 0.6f, out _), Is.Zero);
            Assert.That(clock.Progress, Is.EqualTo(0.6f).Within(0.0001f));
            Assert.That(clock.Advance(ControlZoneState.RedControlled, 0.4f,
                out TeamId scoring), Is.EqualTo(1));
            Assert.That(scoring, Is.EqualTo(TeamId.Red));
            Assert.That(clock.Progress, Is.Zero.Within(0.0001f));
        }

        [Test]
        public void PureOccupancyScoreExpiryAndOvertimeRadiusSeamsAreClamped()
        {
            Assert.That(ControlZoneRules.ResolveState(0, 0), Is.EqualTo(ControlZoneState.Empty));
            Assert.That(ControlZoneRules.ResolveState(1, 0),
                Is.EqualTo(ControlZoneState.BlueControlled));
            Assert.That(ControlZoneRules.ResolveState(0, 2),
                Is.EqualTo(ControlZoneState.RedControlled));
            Assert.That(ControlZoneRules.ResolveState(1, 1),
                Is.EqualTo(ControlZoneState.Contested));
            Assert.That(ControlZoneRules.ApplyScore(7, 200), Is.EqualTo(90));
            Assert.That(ControlZoneRules.ResolveRegulationResult(12, 7),
                Is.EqualTo(ControlZoneRegulationResult.BlueWin));
            Assert.That(ControlZoneRules.ResolveRegulationResult(7, 12),
                Is.EqualTo(ControlZoneRegulationResult.RedWin));
            Assert.That(ControlZoneRules.ResolveRegulationResult(12, 12),
                Is.EqualTo(ControlZoneRegulationResult.Overtime));
            Assert.That(ControlZoneRules.OvertimeRadiusAt(0f, 7f, 12f, 1f),
                Is.EqualTo(7f));
            Assert.That(ControlZoneRules.OvertimeRadiusAt(3.5f, 7f, 12f, 1f),
                Is.EqualTo(10.5f));
            Assert.That(ControlZoneRules.OvertimeRadiusAt(100f, 7f, 12f, 1f),
                Is.EqualTo(12f));
        }

        [Test]
        public void PureSpawnPlannerRejectsOccupiedPreferredAndNeverLeavesPrimarySlots()
        {
            var candidates = new[]
            {
                new MatchSpawnCandidate(true, false, false, 10f),
                new MatchSpawnCandidate(true, false, true, 100f),
                new MatchSpawnCandidate(true, false, false, 9f),
                new MatchSpawnCandidate(true, false, false, 1000f),
                new MatchSpawnCandidate(true, false, false, 1000f),
            };

            Assert.That(MatchSpawnPlanner.SelectSlot(candidates, 3, 1, 0), Is.EqualTo(0));
            Assert.That(MatchSpawnPlanner.SelectSlot(candidates, 3, 2, 0), Is.EqualTo(2));
            candidates[0] = new MatchSpawnCandidate(true, true, false, 10f);
            Assert.That(MatchSpawnPlanner.SelectSlot(candidates, 3, 1, 0), Is.EqualTo(2));
        }

        [Test]
        public void AiHudAndFlowExposeThePrimaryModeWithoutAParallelAuthority()
        {
            Assert.That(AIBrawler.ResolveModeObjective(GameMode.ControlZone, false),
                Is.EqualTo(AIBrawlerObjective.ControlZone));
            Assert.That(AIBrawler.ResolveModeObjective(GameMode.GemGrab, false),
                Is.EqualTo(AIBrawlerObjective.GemGrab));
            Assert.That(AIBrawler.ResolveModeObjective(GameMode.Knockout, false),
                Is.EqualTo(AIBrawlerObjective.Combat));
            Assert.That(AIBrawler.ResolveModeObjective(GameMode.ControlZone, true),
                Is.EqualTo(AIBrawlerObjective.Retreat));

            string hud = ReadProjectSource("Assets/Scripts/Brawl/BrawlHUD.cs");
            StringAssert.Contains("CONTROL ZONE", hud);
            StringAssert.Contains("NEXT POINT WINS", hud);
            StringAssert.Contains("protectionRoot", hud);
            StringAssert.Contains("SpawnProtectionRemaining", hud);
            string flow = ReadProjectSource("Assets/Scripts/Brawl/GameFlow.cs");
            StringAssert.Contains("ArenaLayout.ActiveTeamSize(MatchSetup.Mode)", flow);
            StringAssert.Contains("BuildRotatedTeamDefinitionIndices", flow);
            StringAssert.DoesNotContain("new System.Random", flow);
        }

        [UnityTest]
        public IEnumerator ProductionHumanAndAiProveProtectionOccupancyAndExactRespawn()
        {
            Scene original = SceneManager.GetActiveScene();
            SessionState.SetString(OriginalScenePathKey, original.path);
            SessionState.SetBool(OriginalSceneDirtyKey, original.isDirty);
            yield return new EnterPlayMode();

            string originalPath = SessionState.GetString(OriginalScenePathKey, string.Empty);
            Scene originalPlayScene = SceneManager.GetSceneByPath(originalPath);
            if (originalPlayScene.IsValid() && originalPlayScene.isLoaded)
            {
                foreach (GameObject root in originalPlayScene.GetRootGameObjects())
                    root.SetActive(false);
            }

            Scene proofScene = SceneManager.CreateScene("ControlZoneProductionProof");
            SceneManager.SetActiveScene(proofScene);
            GameObject ground = GameObject.CreatePrimitive(PrimitiveType.Cube);
            ground.name = "Control Zone Proof Ground";
            ground.layer = CombatPhysics.GroundLayer;
            ground.transform.position = new Vector3(0f, -0.5f, 0f);
            ground.transform.localScale = new Vector3(70f, 1f, 70f);
            SceneManager.MoveGameObjectToScene(ground, proofScene);

            GameObject navigationRoot = new GameObject("Control Zone Proof NavMesh");
            SceneManager.MoveGameObjectToScene(navigationRoot, proofScene);
            NavMeshSurface surface = navigationRoot.AddComponent<NavMeshSurface>();
            surface.collectObjects = CollectObjects.All;
            surface.useGeometry = UnityEngine.AI.NavMeshCollectGeometry.PhysicsColliders;
            surface.layerMask = 1 << CombatPhysics.GroundLayer;
            surface.BuildNavMesh();

            GameObject cameraRoot = new GameObject("Control Zone Proof Camera");
            SceneManager.MoveGameObjectToScene(cameraRoot, proofScene);
            cameraRoot.tag = "MainCamera";
            cameraRoot.AddComponent<Camera>();
            cameraRoot.AddComponent<AudioListener>();

            Transform[] blueSpawns = CreateRuntimeSpawns(proofScene, TeamId.Blue);
            Transform[] redSpawns = CreateRuntimeSpawns(proofScene, TeamId.Red);
            GameObject matchRoot = new GameObject("Control Zone Proof Match");
            matchRoot.SetActive(false);
            SceneManager.MoveGameObjectToScene(matchRoot, proofScene);
            MatchManager manager = matchRoot.AddComponent<MatchManager>();
            manager.mode = GameMode.ControlZone;
            manager.autoStart = true;
            manager.introDuration = 0f;
            manager.blueSpawns = blueSpawns;
            manager.redSpawns = redSpawns;
            ControlZoneManager zone = matchRoot.AddComponent<ControlZoneManager>();
            matchRoot.SetActive(true);

            GameObject humanPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(
                InvectorMigrationPilotBuilder.ProductionHumanPrefabPath);
            GameObject aiPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(
                InvectorMigrationPilotBuilder.ProductionAIPrefabPath);
            Assert.That(humanPrefab, Is.Not.Null);
            Assert.That(aiPrefab, Is.Not.Null);
            var definition = new BrawlerDefinition
            {
                id = "fire",
                displayName = "Cinder",
                role = "Pyromancer",
                invectorHumanPrefab = humanPrefab,
                invectorAIPrefab = aiPrefab,
                maxHealth = 100f,
                damage = 20f,
                moveSpeed = 4.9f,
                attackRange = 2.2f,
                attackRadius = 1.5f,
                autoAimRange = 4f,
                cooldown = 0.08f,
                hitDelay = 0.02f,
                moveLock = 0.05f,
                specialty = SpellSpecialty.ForSchool(SpellSchool.Fire),
            };

            BrawlerController human = GameFlow.Spawn(definition, TeamId.Blue,
                Vector3.zero, true, 1f, BrawlerAssemblyContext.ProductionHumanInvector);
            BrawlerController ai = GameFlow.Spawn(definition, TeamId.Red,
                new Vector3(0f, 0f, 20f), false, 1f,
                BrawlerAssemblyContext.ProductionAIInvector);
            human.ConfigureMatchSpawnSlot(0);
            ai.ConfigureMatchSpawnSlot(0);
            human.GetComponent<PlayerBrawlerInput>().enabled = false;
            ai.GetComponent<AIBrawler>().enabled = false;

            for (int frame = 0; frame < 180 &&
                 (manager.State != MatchState.Playing || manager.GetBrawlers().Count < 2);
                 frame++)
                yield return null;
            yield return new WaitForFixedUpdate();

            InvectorHumanRuntimeGate humanGate = human.GetComponent<InvectorHumanRuntimeGate>();
            InvectorAIRuntimeGate aiGate = ai.GetComponent<InvectorAIRuntimeGate>();
            InvectorBrawlerMotor humanMotor = human.GetComponent<InvectorBrawlerMotor>();
            InvectorBrawlerMotor aiMotor = ai.GetComponent<InvectorBrawlerMotor>();
            Assert.That(manager.State, Is.EqualTo(MatchState.Playing));
            Assert.That(manager.mode, Is.EqualTo(GameMode.ControlZone));
            Assert.That(manager.ActiveTeamSize, Is.EqualTo(3));
            Assert.That(humanGate.IsRuntimeActive, Is.True, humanGate.FailureMessage);
            Assert.That(aiGate.IsRuntimeActive, Is.True, aiGate.FailureMessage);
            Assert.That(human.GetComponent<InvectorBrawlerPrefabIdentity>()
                .Matches("fire", InvectorBrawlerPrefabRole.Human), Is.True);
            Assert.That(ai.GetComponent<InvectorBrawlerPrefabIdentity>()
                .Matches("fire", InvectorBrawlerPrefabRole.AI), Is.True);
            Assert.That(human.Motor, Is.SameAs(humanMotor));
            Assert.That(ai.Motor, Is.SameAs(aiMotor));
            Assert.That(human.GetComponents<CharacterController>(), Is.Empty);
            Assert.That(ai.GetComponents<CharacterController>(), Is.Empty);
            Assert.That(human.GetComponent<InvectorShooterMeleeInputAdapter>().InputUpdateCount,
                Is.Zero);

            int presentationChildren = zone.transform.childCount;
            zone.BeginOvertime();
            zone.Tick(100f, true);
            Assert.That(zone.CurrentRadius, Is.EqualTo(12f).Within(0.0001f));
            Assert.That(zone.RenderedRadius, Is.EqualTo(12f).Within(0.0001f));
            Assert.That(zone.transform.childCount, Is.EqualTo(presentationChildren));
            Assert.That(zone.GetComponentsInChildren<Collider>(true), Is.Empty);
            foreach (LineRenderer line in zone.GetComponentsInChildren<LineRenderer>(true))
                Assert.That(line.gameObject.layer, Is.EqualTo(CombatPhysics.VfxLayer));
            zone.BeginRegulation();

            humanMotor.Teleport(Vector3.zero);
            aiMotor.Teleport(new Vector3(0f, 0f, 20f));
            // Establish the post-teleport grounded baseline before proving
            // that spawn protection rejects gameplay knockback. The vendor
            // motor may apply a millimetre-scale grounding correction on its
            // first scheduled physics step; that is not combat displacement.
            yield return new WaitForFixedUpdate();
            human.BeginSpawnProtection(ControlZoneRules.SpawnProtectionDuration);
            float protectedHealth = human.Health.Current;
            float sourceCharge = ai.SuperCharge;
            Vector3 protectedPosition = human.transform.position;
            Assert.That(human.Health.TakeDamage(25f, ai.gameObject), Is.Zero);
            human.ApplySpellBurn(ai, 20f, 2f, 0.5f);
            human.ApplySpellPoison(ai, 20f, 2f, 0.5f);
            human.ApplySpellSlow(ai, 0.5f, 2f);
            human.ApplyKnockback(Vector3.right, 3f);
            yield return new WaitForFixedUpdate();
            Assert.That(human.Health.Current, Is.EqualTo(protectedHealth));
            Assert.That(ai.SuperCharge, Is.EqualTo(sourceCharge));
            Assert.That(Vector3.Distance(human.transform.position, protectedPosition),
                Is.LessThan(0.01f));
            Assert.That(human.IsBurning || human.IsPoisoned || human.IsSlowed, Is.False);
            Assert.That(human.IsSpawnProtected && human.CanContestObjective, Is.True);
            Assert.That(human.SpawnProtectionCueVisible, Is.True);
            Transform cue = human.transform.Find("Brawl Spawn Protection");
            Assert.That(cue, Is.Not.Null);
            Assert.That(cue.gameObject.layer, Is.EqualTo(CombatPhysics.VfxLayer));
            Assert.That(cue.GetComponentsInChildren<Collider>(true), Is.Empty);
            for (int frame = 0; frame < 20 && zone.BlueOccupants == 0; frame++)
                yield return null;
            Assert.That(zone.BlueOccupants, Is.EqualTo(1),
                "Spawn-protected living actors still count in the zone.");
            Assert.That(zone.State, Is.EqualTo(ControlZoneState.BlueControlled));

            Assert.That(human.TryAttackAuto(), Is.False);
            Assert.That(human.IsSpawnProtected, Is.True);
            Assert.That(human.TryAttackDirection(Vector3.zero), Is.False);
            Assert.That(human.IsSpawnProtected, Is.True);
            Assert.That(human.TryWardStep(Vector3.right), Is.True);
            Assert.That(human.IsSpawnProtected, Is.True);
            human.ResetForMatchLifecycle();
            humanMotor.Teleport(Vector3.zero);

            human.BeginSpawnProtection(ControlZoneRules.SpawnProtectionDuration);
            Assert.That(human.TryAttackDirection(Vector3.forward), Is.True);
            Assert.That(human.IsSpawnProtected, Is.False);
            human.BeginSpawnProtection(ControlZoneRules.SpawnProtectionDuration);
            Assert.That(human.TryAttackDirection(Vector3.forward), Is.False);
            Assert.That(human.IsSpawnProtected, Is.True,
                "Cooldown-rejected offense must retain protection.");
            human.ClearSpawnProtection();
            for (int frame = 0; frame < 120 && !human.BasicAttackReady; frame++)
                yield return null;

            human.BeginSpawnProtection(ControlZoneRules.SpawnProtectionDuration);
            Assert.That(human.TrySuperDirection(Vector3.forward), Is.False);
            Assert.That(human.IsSpawnProtected, Is.True);
            human.ClearSpawnProtection();
            human.maxSuperCharge = 1f;
            Assert.That(human.Health.TakeDamage(5f, ai.gameObject), Is.GreaterThan(0f));
            Assert.That(human.SuperReady, Is.True);
            human.BeginSpawnProtection(ControlZoneRules.SpawnProtectionDuration);
            Assert.That(human.TrySuperDirection(Vector3.forward), Is.True);
            Assert.That(human.IsSpawnProtected, Is.False);
            for (int frame = 0; frame < 120 && !human.CanAct; frame++)
                yield return null;

            human.ApplySpellBurn(ai, 10f, 2f, 0.5f);
            human.ApplySpellPoison(ai, 10f, 2f, 0.5f);
            human.ApplySpellSlow(ai, 0.5f, 2f);
            Assert.That(human.IsBurning && human.IsPoisoned && human.IsSlowed, Is.True);
            aiMotor.Teleport(blueSpawns[0].position);
            int blueBeforeKo = manager.BlueScore;
            int redBeforeKo = manager.RedScore;
            float deathStartedAt = Time.time;
            human.Health.TakeDamage(human.Health.Current + 10f, ai.gameObject);
            Assert.That(human.IsDead && human.IsRespawning, Is.True);
            float respawnDeadline = Time.realtimeSinceStartup + 8f;
            while (human.IsRespawning && Time.realtimeSinceStartup < respawnDeadline)
                yield return null;
            float respawnElapsed = Time.time - deathStartedAt;

            Assert.That(human.IsDead || human.IsRespawning, Is.False);
            Assert.That(respawnElapsed, Is.InRange(5.8f, 6.8f));
            Assert.That(manager.BlueScore, Is.EqualTo(blueBeforeKo));
            Assert.That(manager.RedScore, Is.EqualTo(redBeforeKo),
                "Control Zone KOs must not alter objective score.");
            Assert.That(human.MatchSpawnSlot, Is.Zero);
            Assert.That(Vector3.Distance(human.transform.position, blueSpawns[0].position),
                Is.GreaterThan(1.75f));
            bool usedPrimaryFallback =
                Vector3.Distance(human.transform.position, blueSpawns[1].position) < 0.4f ||
                Vector3.Distance(human.transform.position, blueSpawns[2].position) < 0.4f;
            Assert.That(usedPrimaryFallback, Is.True,
                "Occupied slot zero must fall back only to primary slots one/two.");
            Assert.That(human.IsBurning || human.IsPoisoned || human.IsSlowed, Is.False);
            Assert.That(human.IsSpawnProtected, Is.True);
            float protectionObservedAt = Time.time;
            float protectionDeadline = Time.realtimeSinceStartup + 3f;
            while (human.IsSpawnProtected &&
                   Time.realtimeSinceStartup < protectionDeadline)
                yield return null;
            float protectionElapsed = Time.time - protectionObservedAt;
            Assert.That(human.IsSpawnProtected, Is.False);
            Assert.That(protectionElapsed, Is.InRange(1.45f, 2.1f));

            human.BeginSpawnProtection(ControlZoneRules.SpawnProtectionDuration);
            manager.DeclareWinner(TeamId.Blue);
            Assert.That(manager.State, Is.EqualTo(MatchState.Ended));
            Assert.That(human.IsSpawnProtected, Is.False);
            Assert.That(human.AnimationPresentationFailureCount, Is.Zero);
            Assert.That(human.WeaponPresentationFailureCount, Is.Zero);
            Assert.That(ai.AnimationPresentationFailureCount, Is.Zero);
            Assert.That(ai.WeaponPresentationFailureCount, Is.Zero);

            humanGate.Deactivate();
            aiGate.Deactivate();
            yield return new ExitPlayMode();

            string expectedPath = SessionState.GetString(OriginalScenePathKey, string.Empty);
            bool expectedDirty = SessionState.GetBool(OriginalSceneDirtyKey, false);
            SessionState.EraseString(OriginalScenePathKey);
            SessionState.EraseBool(OriginalSceneDirtyKey);
            Scene restored = SceneManager.GetActiveScene();
            Assert.That(restored.path, Is.EqualTo(expectedPath));
            Assert.That(restored.isDirty, Is.EqualTo(expectedDirty));
        }

        static Transform[] CreateRuntimeSpawns(Scene scene, TeamId team)
        {
            var result = new Transform[ArenaLayout.TeamSize];
            for (int i = 0; i < result.Length; i++)
            {
                var spawn = new GameObject(team + " Proof Spawn " + i);
                SceneManager.MoveGameObjectToScene(spawn, scene);
                spawn.transform.position = ArenaLayout.SpawnPosition(team, i);
                result[i] = spawn.transform;
            }
            return result;
        }

        static string ReadProjectSource(string relativePath)
        {
            string root = Directory.GetParent(Application.dataPath)?.FullName;
            Assert.That(root, Is.Not.Null);
            return File.ReadAllText(Path.Combine(root, relativePath));
        }
    }
}
