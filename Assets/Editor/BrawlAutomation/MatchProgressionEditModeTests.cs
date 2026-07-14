using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;

namespace BrawlArena.EditorAutomation
{
    public class MatchProgressionEditModeTests
    {
        readonly List<GameObject> created = new List<GameObject>();
        int previousTargetFrameRate;

        [SetUp]
        public void SetUp()
        {
            previousTargetFrameRate = Application.targetFrameRate;
        }

        [TearDown]
        public void TearDown()
        {
            for (int i = created.Count - 1; i >= 0; i--)
                if (created[i] != null) Object.DestroyImmediate(created[i]);
            created.Clear();
            Application.targetFrameRate = previousTargetFrameRate;
        }

        [Test]
        public void ExperienceUsesCurrentLevelThresholdAndCarriesOverflow()
        {
            BrawlerController hero = CreateHero("ThresholdHero", TeamId.Blue);
            HeroMatchProgression progression = AddProgression(hero);

            Assert.AreEqual(1, progression.Level);
            Assert.AreEqual(0, progression.Experience);
            Assert.AreEqual(60, progression.ExperienceToNext);
            Assert.AreEqual(0f, progression.Experience01);

            progression.AddExperience(59);
            Assert.AreEqual(1, progression.Level);
            Assert.AreEqual(59, progression.Experience);

            progression.AddExperience(16);
            Assert.AreEqual(2, progression.Level);
            Assert.AreEqual(15, progression.Experience);
            Assert.AreEqual(90, progression.ExperienceToNext);
            Assert.That(progression.Experience01, Is.EqualTo(15f / 90f).Within(0.0001f));
        }

        [Test]
        public void LevelSixCapsExperienceAndRaisesEveryLevelEvent()
        {
            BrawlerController hero = CreateHero("MaxLevelHero", TeamId.Blue);
            HeroMatchProgression progression = AddProgression(hero);
            var levels = new List<int>();
            progression.LeveledUp += levels.Add;

            progression.AddExperience(2000);

            Assert.AreEqual(HeroMatchProgression.MaxLevel, progression.Level);
            Assert.AreEqual(0, progression.Experience);
            Assert.AreEqual(0, progression.ExperienceToNext);
            Assert.AreEqual(1f, progression.Experience01);
            CollectionAssert.AreEqual(new[] { 2, 3, 4, 5, 6 }, levels);
            Assert.IsFalse(progression.AddExperience(25));
        }

        [Test]
        public void LevelStatsDeriveFromBaselineAndHealthOnlyGainsMaxDelta()
        {
            BrawlerController hero = CreateHero("StatsHero", TeamId.Blue,
                maxHealth: 100f, attackDamage: 20f, moveSpeed: 5f, staminaRegen: 10f);
            HeroMatchProgression progression = AddProgression(hero);
            hero.Health.TakeDamage(30f, null);

            progression.AddExperience(60);

            Assert.AreEqual(2, progression.Level);
            Assert.AreEqual(106f, hero.Health.Max);
            Assert.AreEqual(76f, hero.Health.Current,
                "A level-up should add only the six newly gained max-health points.");
            Assert.AreEqual(21f, hero.attackDamage);
            Assert.That(hero.moveSpeed, Is.EqualTo(5.1f).Within(0.0001f));
            Assert.That(hero.staminaRegenPerSec, Is.EqualTo(10.5f).Within(0.0001f));
            Assert.AreEqual(60f, hero.maxStamina);

            progression.AddExperience(90);

            Assert.AreEqual(3, progression.Level);
            Assert.AreEqual(112f, hero.Health.Max);
            Assert.AreEqual(82f, hero.Health.Current);
            Assert.AreEqual(22f, hero.attackDamage);
            Assert.That(hero.moveSpeed, Is.EqualTo(5.2f).Within(0.0001f));
            Assert.That(hero.staminaRegenPerSec, Is.EqualTo(11f).Within(0.0001f));
            Assert.AreEqual(60f, hero.maxStamina);
        }

        [Test]
        public void RegisterAddsLevelOneProgressionAndBeginMatchEnsuresSystem()
        {
            MatchManager manager = CreateManager();
            BrawlerController hero = CreateHero("RegisteredHero", TeamId.Blue);
            HeroMatchProgression preexisting = AddProgression(hero);
            preexisting.AddExperience(60);
            Assert.AreEqual(2, preexisting.Level);

            manager.Register(hero);

            HeroMatchProgression registered = hero.GetComponent<HeroMatchProgression>();
            Assert.NotNull(registered);
            Assert.AreEqual(1, registered.Level);
            Assert.IsNull(manager.GetComponent<MatchExperienceSystem>());

            manager.BeginMatch();

            Assert.NotNull(manager.GetComponent<MatchExperienceSystem>());
            Assert.AreEqual(1, registered.Level);
            Assert.AreEqual(0, registered.Experience);
        }

        [Test]
        public void EnemyKnockoutAwardsFortyExperienceButInvalidKillsDoNot()
        {
            MatchManager manager = CreateManager();
            BrawlerController attacker = CreateHero("Attacker", TeamId.Blue);
            BrawlerController enemy = CreateHero("Enemy", TeamId.Red);
            BrawlerController ally = CreateHero("Ally", TeamId.Blue);
            manager.Register(attacker);
            manager.Register(enemy);
            manager.Register(ally);
            BeginPlaying(manager);

            HeroMatchProgression progression = attacker.GetComponent<HeroMatchProgression>();
            manager.ReportKO(enemy, attacker.gameObject);
            Assert.AreEqual(40, progression.Experience);

            manager.ReportKO(ally, attacker.gameObject);
            manager.ReportKO(enemy, null);
            Assert.AreEqual(40, progression.Experience);
        }

        [Test]
        public void ForwardCheckpointsAreMirroredAndCannotBeFarmedByRecrossing()
        {
            MatchManager manager = CreateManager();
            BrawlerController blue = CreateHero("BlueAdvance", TeamId.Blue);
            BrawlerController red = CreateHero("RedAdvance", TeamId.Red);
            blue.transform.position = new Vector3(0f, 0f, -32f);
            red.transform.position = new Vector3(0f, 0f, 32f);
            manager.Register(blue);
            manager.Register(red);
            BeginPlaying(manager);
            MatchExperienceSystem system = manager.GetComponent<MatchExperienceSystem>();

            blue.transform.position = new Vector3(0f, 0f, -15f);
            red.transform.position = new Vector3(0f, 0f, 15f);
            system.EvaluateAdvancement();
            Assert.AreEqual(15, blue.GetComponent<HeroMatchProgression>().Experience);
            Assert.AreEqual(15, red.GetComponent<HeroMatchProgression>().Experience);

            blue.transform.position = new Vector3(0f, 0f, -25f);
            red.transform.position = new Vector3(0f, 0f, 25f);
            system.EvaluateAdvancement();
            blue.transform.position = new Vector3(0f, 0f, -15f);
            red.transform.position = new Vector3(0f, 0f, 15f);
            system.EvaluateAdvancement();
            Assert.AreEqual(15, blue.GetComponent<HeroMatchProgression>().Experience);
            Assert.AreEqual(15, red.GetComponent<HeroMatchProgression>().Experience);

            blue.transform.position = new Vector3(0f, 0f, 17f);
            red.transform.position = new Vector3(0f, 0f, -17f);
            system.EvaluateAdvancement();
            Assert.AreEqual(45, blue.GetComponent<HeroMatchProgression>().Experience);
            Assert.AreEqual(45, red.GetComponent<HeroMatchProgression>().Experience);
        }

        [Test]
        public void ExperienceBoxIsNonPhysicalAndCanOnlyBeLootedOnce()
        {
            MatchManager manager = CreateManager();
            BrawlerController hero = CreateHero("BoxLooter", TeamId.Blue);
            hero.transform.position = new Vector3(0f, 0f, -32f);
            manager.Register(hero);
            BeginPlaying(manager);
            MatchExperienceSystem system = manager.GetComponent<MatchExperienceSystem>();
            ExperienceBox box = system.NearestExperienceBox(hero.transform.position);
            Assert.NotNull(box);
            Assert.AreEqual(0, box.GetComponentsInChildren<Collider>(true).Length);

            hero.transform.position = box.transform.position;
            Assert.IsTrue(box.TryLoot(hero));
            Assert.AreEqual(25, hero.GetComponent<HeroMatchProgression>().Experience);
            Assert.IsFalse(box.TryLoot(hero));
            Assert.AreEqual(25, hero.GetComponent<HeroMatchProgression>().Experience);
        }

        [Test]
        public void ResetForMatchReturnsHeroToLevelOneBaseline()
        {
            BrawlerController hero = CreateHero("ResetHero", TeamId.Blue,
                maxHealth: 100f, attackDamage: 20f, moveSpeed: 5f, staminaRegen: 10f);
            HeroMatchProgression progression = AddProgression(hero);
            progression.AddExperience(60 + 90);
            Assert.AreEqual(3, progression.Level);

            progression.ResetForMatch();

            Assert.AreEqual(1, progression.Level);
            Assert.AreEqual(0, progression.Experience);
            Assert.AreEqual(100f, hero.Health.Max);
            Assert.AreEqual(100f, hero.Health.Current);
            Assert.AreEqual(20f, hero.attackDamage);
            Assert.AreEqual(5f, hero.moveSpeed);
            Assert.AreEqual(10f, hero.staminaRegenPerSec);
            Assert.AreEqual(60f, hero.maxStamina);
        }

        MatchManager CreateManager()
        {
            var go = new GameObject("MatchProgressionTestManager");
            created.Add(go);
            MatchManager manager = go.AddComponent<MatchManager>();
            manager.autoStart = false;
            manager.introDuration = 0f;
            if (MatchManager.Instance != manager) InvokePrivate(manager, "Awake");
            return manager;
        }

        BrawlerController CreateHero(string name, TeamId team, float maxHealth = 100f,
            float attackDamage = 20f, float moveSpeed = 5f, float staminaRegen = 10f)
        {
            var go = new GameObject(name);
            created.Add(go);
            Health health = go.AddComponent<Health>();
            health.SetMax(maxHealth);
            go.AddComponent<Tests.InvectorCutoverTestMotor>();
            go.AddComponent<Tests.InvectorCutoverTestAnimationDriver>();
            BrawlerController brawler = go.AddComponent<BrawlerController>();
            if (brawler.Health == null) InvokePrivate(brawler, "Awake");
            brawler.team = team;
            brawler.attackDamage = attackDamage;
            brawler.moveSpeed = moveSpeed;
            brawler.staminaRegenPerSec = staminaRegen;
            return brawler;
        }

        static HeroMatchProgression AddProgression(BrawlerController hero)
        {
            HeroMatchProgression progression = hero.gameObject.AddComponent<HeroMatchProgression>();
            progression.Initialize(hero);
            return progression;
        }

        static void BeginPlaying(MatchManager manager)
        {
            manager.BeginMatch();
            InvokePrivate(manager, "Update");
            Assert.AreEqual(MatchState.Playing, manager.State);
        }

        static void InvokePrivate(object target, string method)
        {
            target.GetType().GetMethod(method, BindingFlags.Instance | BindingFlags.NonPublic)
                ?.Invoke(target, null);
        }
    }
}
