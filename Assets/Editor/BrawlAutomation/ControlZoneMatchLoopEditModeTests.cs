using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
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

        readonly List<GameObject> created = new List<GameObject>();

        [TearDown]
        public void TearDown()
        {
            MatchSetup.Mode = GameMode.ControlZone;
            MatchSetup.CharacterIndex = -1;
            MatchSetup.FromMenu = false;
            for (int i = created.Count - 1; i >= 0; i--)
                if (created[i] != null) Object.DestroyImmediate(created[i]);
            created.Clear();
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
            StringAssert.Contains("BuildRoleBalancedLineup", flow);
            StringAssert.DoesNotContain("new System.Random", flow);
        }

        [Test]
        public void ComebackRespawnAndExperienceHelpersScaleWithScoreGap()
        {
            Assert.That(ControlZoneRules.IsTrailing(10, 25), Is.True);
            Assert.That(ControlZoneRules.IsTrailing(11, 25), Is.False);
            Assert.That(ControlZoneRules.IsLeading(25, 10), Is.True);
            Assert.That(ControlZoneRules.IsLeading(24, 10), Is.False);

            Assert.That(ControlZoneRules.RespawnDelaySeconds(10, 25),
                Is.EqualTo(ControlZoneRules.TrailingRespawnDelay));
            Assert.That(ControlZoneRules.RespawnDelaySeconds(11, 25),
                Is.EqualTo(ControlZoneRules.RespawnDelay));
            Assert.That(ControlZoneRules.RespawnDelaySeconds(20, 20),
                Is.EqualTo(ControlZoneRules.RespawnDelay));

            Assert.That(ControlZoneRules.LeadingRegulationKnockoutPoints, Is.EqualTo(1));
            Assert.That(ControlZoneRules.RegulationKnockoutPointsFor(10, 25),
                Is.EqualTo(ControlZoneRules.RegulationKnockoutPoints),
                "A trailing team keeps the full KO bonus.");
            Assert.That(ControlZoneRules.RegulationKnockoutPointsFor(20, 20),
                Is.EqualTo(ControlZoneRules.RegulationKnockoutPoints));
            Assert.That(ControlZoneRules.RegulationKnockoutPointsFor(24, 10),
                Is.EqualTo(ControlZoneRules.RegulationKnockoutPoints),
                "A lead below the comeback gap must not reduce the KO bonus.");
            Assert.That(ControlZoneRules.RegulationKnockoutPointsFor(25, 10),
                Is.EqualTo(ControlZoneRules.LeadingRegulationKnockoutPoints),
                "A team at the comeback gap earns only the reduced KO bonus.");

            Assert.That(ControlZoneRules.ApplyTrailingKnockoutXpMultiplier(40, true),
                Is.EqualTo(50));
            Assert.That(ControlZoneRules.ApplyTrailingKnockoutXpMultiplier(40, false),
                Is.EqualTo(40));
            Assert.That(ControlZoneRules.ApplyLeadingExperienceBoxMultiplier(25, true),
                Is.EqualTo(19));
            Assert.That(ControlZoneRules.ApplyLeadingExperienceBoxMultiplier(25, false),
                Is.EqualTo(25));
        }

        [Test]
        public void RoleBalancedLineupIsDeterministicAndCoversEveryRoleSlot()
        {
            var roster = new[]
            {
                new BrawlerDefinition { id = "frost" },
                new BrawlerDefinition { id = "thorn" },
                new BrawlerDefinition { id = "bastion" },
            };

            CollectionAssert.AreEqual(new[] { 0, 1, 2 },
                MatchLineupPlanner.BuildRoleBalancedLineup(roster, 0, 3));
            CollectionAssert.AreEqual(new[] { 1, 2, 0 },
                MatchLineupPlanner.BuildRoleBalancedLineup(roster, 1, 3));
            CollectionAssert.AreEqual(new[] { 2, 0, 1 },
                MatchLineupPlanner.BuildRoleBalancedLineup(roster, 2, 3));
            CollectionAssert.AreEqual(new[] { 0, 1, 2, 0, 1 },
                MatchLineupPlanner.BuildRoleBalancedLineup(roster, 0, 5),
                "Team sizes larger than the roster must wrap around, not repeat the pin.");
            CollectionAssert.AreEqual(
                MatchLineupPlanner.BuildRoleBalancedLineup(roster, 1, 3),
                MatchLineupPlanner.BuildRoleBalancedLineup(roster, 1, 3),
                "Identical inputs must never depend on a hidden random seed.");
        }

        [Test]
        public void OpponentLineupKeepsTheFullRoleSpreadAndVariesOnlyTheOrder()
        {
            var roster = new[]
            {
                new BrawlerDefinition { id = "frost", role = "Mage" },
                new BrawlerDefinition { id = "thorn", role = "Archer" },
                new BrawlerDefinition { id = "bastion", role = "Vanguard" },
            };
            int[] ally = MatchLineupPlanner.BuildRoleBalancedLineup(roster, 0, 3);
            CollectionAssert.AreEqual(new[] { 0, 1, 2 }, ally);

            CollectionAssert.AreEqual(
                MatchLineupPlanner.BuildOpponentLineup(roster, ally, 3, 7),
                MatchLineupPlanner.BuildOpponentLineup(roster, ally, 3, 7),
                "A fixed seed must always reproduce the identical opponent comp.");

            for (int seed = 0; seed <= 20; seed++)
            {
                int[] opponent = MatchLineupPlanner.BuildOpponentLineup(roster, ally, 3, seed);
                Assert.That(opponent.Length, Is.EqualTo(3));

                bool mirrorsAlly = opponent[0] == ally[0] && opponent[1] == ally[1] &&
                    opponent[2] == ally[2];
                Assert.That(mirrorsAlly, Is.False,
                    $"seed {seed} must not produce a pure mirror of the ally lineup.");

                CollectionAssert.AreEquivalent(new[] { 0, 1, 2 }, opponent,
                    $"seed {seed}: a one-hero-per-role roster must field every role " +
                    "exactly once — variety lives in slot order, never in composition.");

                foreach (int defIndex in opponent)
                    Assert.That(defIndex, Is.InRange(0, roster.Length - 1));

                var counts = new Dictionary<int, int>();
                foreach (int defIndex in opponent)
                    counts[defIndex] = counts.TryGetValue(defIndex, out int c) ? c + 1 : 1;
                foreach (int count in counts.Values)
                    Assert.That(count, Is.LessThanOrEqualTo(2),
                        $"seed {seed} must never field more than two copies of one hero.");
            }

            // Oversized teams wrap the roster: every role stays covered and the
            // two-copy cap still holds because composition mirrors the ally comp.
            int[] allyFive = MatchLineupPlanner.BuildRoleBalancedLineup(roster, 0, 5);
            for (int seed = 0; seed <= 5; seed++)
            {
                int[] opponentFive = MatchLineupPlanner.BuildOpponentLineup(
                    roster, allyFive, 5, seed);
                Assert.That(opponentFive.Length, Is.EqualTo(5));
                foreach (int defIndex in new[] { 0, 1, 2 })
                    CollectionAssert.Contains(opponentFive, defIndex,
                        $"seed {seed}: wrapped teams must still cover every role.");
            }
        }

        [Test]
        public void AiRoleResolvesFromProjectilePresenceAndRangeThreshold()
        {
            Assert.That(AIBrawler.ResolveRole(false, 2.8f), Is.EqualTo(AIRole.Warrior));
            Assert.That(AIBrawler.ResolveRole(false, 20f), Is.EqualTo(AIRole.Warrior));
            Assert.That(AIBrawler.ResolveRole(true, 7.9f), Is.EqualTo(AIRole.Mage));
            Assert.That(AIBrawler.ResolveRole(true, 8f), Is.EqualTo(AIRole.Archer));
            Assert.That(AIBrawler.ResolveRole(true, 10.5f), Is.EqualTo(AIRole.Archer));
        }

        [Test]
        public void GemCarrierHoldPositionStopsShortOfHomeInsteadOfRetreatingFully()
        {
            Vector3 current = new Vector3(10f, 0f, 0f);
            Vector3 home = new Vector3(0f, 0f, 0f);

            Vector3 hold = AIBrawler.GemCarrierHoldPosition(current, home, 0.4f);

            Assert.That(hold, Is.EqualTo(new Vector3(6f, 0f, 0f)));
            Assert.That(Vector3.Distance(hold, home), Is.GreaterThan(0.01f),
                "A leading carrier must hold midfield, not sprint all the way home.");
            Assert.That(AIBrawler.GemCarrierHoldPosition(current, home, 2f),
                Is.EqualTo(home), "The fraction must clamp to [0,1].");
        }

        [Test]
        public void ControlZoneKnockoutsScoreTheFullBonusUntilTheLeaderHitsTheComebackGap()
        {
            MatchManager manager = CreateManager();
            BrawlerController attacker = CreateHero("KoAttacker", TeamId.Blue);
            BrawlerController victim = CreateHero("KoVictim", TeamId.Red);
            manager.Register(attacker);
            manager.Register(victim);
            BeginPlaying(manager);

            manager.ReportKO(victim, attacker.gameObject);
            Assert.That(manager.BlueScore, Is.EqualTo(ControlZoneRules.RegulationKnockoutPoints));
            Assert.That(manager.RedScore, Is.Zero);

            manager.ReportKO(victim, attacker.gameObject);
            Assert.That(manager.BlueScore,
                Is.EqualTo(ControlZoneRules.RegulationKnockoutPoints * 2),
                "Below the comeback gap every valid regulation KO awards the full zone bonus.");

            // Push Blue exactly to the comeback gap: the leading team's KOs now
            // award only the reduced anti-snowball bonus while the lead holds.
            manager.AddControlZoneScore(TeamId.Blue,
                ControlZoneRules.ComebackScoreDeficit - manager.BlueScore);
            Assert.That(manager.BlueScore, Is.EqualTo(ControlZoneRules.ComebackScoreDeficit));

            manager.ReportKO(victim, attacker.gameObject);
            Assert.That(manager.BlueScore,
                Is.EqualTo(ControlZoneRules.ComebackScoreDeficit +
                           ControlZoneRules.LeadingRegulationKnockoutPoints),
                "A team leading by the comeback gap earns only the reduced KO bonus.");

            manager.ReportKO(attacker, victim.gameObject);
            Assert.That(manager.RedScore,
                Is.EqualTo(ControlZoneRules.RegulationKnockoutPoints),
                "The trailing team keeps the full KO bonus while behind.");
        }

        [Test]
        public void ControlZoneKnockoutsScoreNothingOnceOvertimeBegins()
        {
            MatchManager manager = CreateManager();
            BrawlerController attacker = CreateHero("OvertimeKoAttacker", TeamId.Blue);
            BrawlerController victim = CreateHero("OvertimeKoVictim", TeamId.Red);
            manager.Register(attacker);
            manager.Register(victim);
            BeginPlaying(manager);

            // Force the regulation timer to expire with tied scores so the
            // match enters Overtime through the public API only.
            manager.AdvanceActiveMatch(manager.matchDuration + 1f);
            Assert.That(manager.State, Is.EqualTo(MatchState.Overtime));

            manager.ReportKO(victim, attacker.gameObject);
            Assert.That(manager.BlueScore, Is.Zero);
            Assert.That(manager.RedScore, Is.Zero,
                "Overtime KOs must not score; the zone tick decides overtime instead.");
        }

        MatchManager CreateManager()
        {
            var go = new GameObject("ControlZoneMatchLoopTestManager");
            created.Add(go);
            MatchManager manager = go.AddComponent<MatchManager>();
            if (MatchManager.Instance != manager) InvokePrivate(manager, "Awake");
            // Awake() re-derives matchDuration from ConfigureMode, so the
            // short test duration must be applied after it, not before.
            manager.autoStart = false;
            manager.introDuration = 0f;
            manager.matchDuration = 1f;
            return manager;
        }

        BrawlerController CreateHero(string name, TeamId team)
        {
            var go = new GameObject(name);
            created.Add(go);
            go.AddComponent<Health>().SetMax(100f);
            go.AddComponent<BrawlFacadeTestMotor>();
            go.AddComponent<BrawlFacadeTestAnimationDriver>();
            BrawlerController brawler = go.AddComponent<BrawlerController>();
            if (brawler.Health == null) InvokePrivate(brawler, "Awake");
            brawler.team = team;
            return brawler;
        }

        static void BeginPlaying(MatchManager manager)
        {
            manager.BeginMatch();
            InvokePrivate(manager, "Update");
            Assert.That(manager.State, Is.EqualTo(MatchState.Playing));
        }

        static void InvokePrivate(object target, string method)
        {
            target.GetType().GetMethod(method, BindingFlags.Instance | BindingFlags.NonPublic)
                ?.Invoke(target, null);
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

            GameObject humanPrefab = HeavyHeroBuilder.LoadHumanPrefab("frost");
            GameObject aiPrefab = HeavyHeroBuilder.LoadAIPrefab("frost");
            Assert.That(humanPrefab, Is.Not.Null);
            Assert.That(aiPrefab, Is.Not.Null);
            var definition = new BrawlerDefinition
            {
                id = "frost",
                displayName = "Rime",
                role = "Cryomancer",
                humanBodyPrefab = humanPrefab,
                aiBodyPrefab = aiPrefab,
                maxHealth = 100f,
                damage = 20f,
                moveSpeed = 4.9f,
                attackRange = 2.2f,
                attackRadius = 1.5f,
                autoAimRange = 4f,
                cooldown = 0.08f,
                hitDelay = 0.02f,
                moveLock = 0.05f,
                specialty = SpellSpecialty.ForSchool(SpellSchool.Frost),
            };

            // Default context resolves by prefab identity — the same path the
            // live GameFlow spawn uses for the TopDown roster.
            BrawlerController human = GameFlow.Spawn(definition, TeamId.Blue,
                Vector3.zero, true, 1f);
            BrawlerController ai = GameFlow.Spawn(definition, TeamId.Red,
                new Vector3(0f, 0f, 20f), false, 1f);
            human.ConfigureMatchSpawnSlot(0);
            ai.ConfigureMatchSpawnSlot(0);
            human.GetComponent<PlayerBrawlerInput>().enabled = false;
            ai.GetComponent<AIBrawler>().enabled = false;

            for (int frame = 0; frame < 180 &&
                 (manager.State != MatchState.Playing || manager.GetBrawlers().Count < 2);
                 frame++)
                yield return null;
            yield return new WaitForFixedUpdate();

            HeavyBrawlerMotor humanMotor = human.GetComponent<HeavyBrawlerMotor>();
            HeavyBrawlerMotor aiMotor = ai.GetComponent<HeavyBrawlerMotor>();
            HeavyBrawlerIdentity humanIdentity =
                human.GetComponent<HeavyBrawlerIdentity>();
            HeavyBrawlerIdentity aiIdentity = ai.GetComponent<HeavyBrawlerIdentity>();
            Assert.That(manager.State, Is.EqualTo(MatchState.Playing));
            Assert.That(manager.mode, Is.EqualTo(GameMode.ControlZone));
            Assert.That(manager.ActiveTeamSize, Is.EqualTo(3));
            Assert.That(humanIdentity, Is.Not.Null);
            Assert.That(aiIdentity, Is.Not.Null);
            Assert.That(humanIdentity.heroId, Is.EqualTo("frost"));
            Assert.That(humanIdentity.isHumanVariant, Is.True);
            Assert.That(aiIdentity.heroId, Is.EqualTo("frost"));
            Assert.That(aiIdentity.isHumanVariant, Is.False);
            Assert.That(humanMotor, Is.Not.Null);
            Assert.That(aiMotor, Is.Not.Null);
            Assert.That(human.Motor, Is.SameAs(humanMotor));
            Assert.That(ai.Motor, Is.SameAs(aiMotor));
            Assert.That(human.GetComponent<HeavyAnimationDriver>(), Is.Not.Null);
            Assert.That(ai.GetComponent<HeavyAnimationDriver>(), Is.Not.Null);
            Assert.That(human.GetComponents<CharacterController>(), Is.Empty);
            Assert.That(ai.GetComponents<CharacterController>(), Is.Empty);

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
            // that spawn protection rejects gameplay knockback. Gravity may
            // apply a millimetre-scale settling correction on the first fixed
            // step; that is not combat displacement.
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
            // Corpse mode: the dead body parks kinematic with its capsule off
            // so it can neither be shoved nor block the living.
            Assert.That(humanMotor.IsCorpseMode, Is.True);
            Assert.That(human.GetComponent<Rigidbody>().isKinematic, Is.True);
            Assert.That(human.GetComponent<CapsuleCollider>().enabled, Is.False);
            float respawnDeadline = Time.realtimeSinceStartup + 8f;
            while (human.IsRespawning && Time.realtimeSinceStartup < respawnDeadline)
                yield return null;
            float respawnElapsed = Time.time - deathStartedAt;

            Assert.That(human.IsDead || human.IsRespawning, Is.False);
            Assert.That(humanMotor.IsCorpseMode, Is.False,
                "Respawn must restore the dynamic physics posture.");
            Assert.That(human.GetComponent<Rigidbody>().isKinematic, Is.False);
            Assert.That(human.GetComponent<CapsuleCollider>().enabled, Is.True);
            Assert.That(respawnElapsed, Is.InRange(5.8f, 6.8f));
            Assert.That(manager.BlueScore, Is.EqualTo(blueBeforeKo));
            Assert.That(manager.RedScore,
                Is.EqualTo(redBeforeKo + ControlZoneRules.RegulationKnockoutPoints),
                "Regulation Control Zone KOs must award the fixed zone bonus to the scoring team.");
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
            Assert.That(human.GetComponent<HeavyAnimationDriver>().LifecycleFailureCount,
                Is.Zero);
            Assert.That(ai.GetComponent<HeavyAnimationDriver>().LifecycleFailureCount,
                Is.Zero);

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
