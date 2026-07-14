using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;

namespace BrawlArena.EditorAutomation
{
    public class RpgCombatSliceEditModeTests
    {
        readonly List<GameObject> objects = new List<GameObject>();
        MatchManager manager;

        [SetUp]
        public void SetUp()
        {
            DestroyPoolsAndHazards();
            GameObject managerObject = new GameObject("RpgCombatTestManager");
            objects.Add(managerObject);
            manager = managerObject.AddComponent<MatchManager>();
            InvokePrivate(manager, "Awake");
        }

        [TearDown]
        public void TearDown()
        {
            DestroyPoolsAndHazards();
            for (int i = objects.Count - 1; i >= 0; i--)
                if (objects[i] != null) Object.DestroyImmediate(objects[i]);
            objects.Clear();
            manager = null;
        }

        [Test]
        public void ManualProjectileAcquiresForwardEnemyAndIgnoresAlliesAndRearTargets()
        {
            BrawlerController owner = CreateBrawler("Owner", TeamId.Blue, Vector3.zero);
            CreateBrawler("Ally", TeamId.Blue, new Vector3(0f, 0f, 3f));
            BrawlerController forwardEnemy = CreateBrawler("ForwardEnemy", TeamId.Red,
                new Vector3(1f, 0f, 6f));
            CreateBrawler("RearEnemy", TeamId.Red, new Vector3(0f, 0f, -2f));
            Projectile projectile = CreateProjectile(new Vector3(0f, 1f, 0f));

            projectile.Launch(owner, Vector3.forward, 10f, 12f, null,
                0f, 0f, 0f, default, null, 10f, null);

            Assert.AreSame(forwardEnemy, projectile.HomingTarget);
        }

        [Test]
        public void LockedProjectileTurnsAtBoundedRateAndPoolResetClearsTarget()
        {
            BrawlerController owner = CreateBrawler("Owner", TeamId.Blue, Vector3.zero);
            BrawlerController enemy = CreateBrawler("Enemy", TeamId.Red,
                new Vector3(6f, 0f, 6f));
            GameObject prefab = new GameObject("HomingProjectilePrefab");
            prefab.AddComponent<Projectile>();
            prefab.SetActive(false);
            objects.Add(prefab);

            Projectile projectile = CombatObjectPool.SpawnProjectile(prefab,
                new Vector3(0f, 1f, 0f), Quaternion.identity);
            projectile.Launch(owner, Vector3.forward, 10f, 12f, null,
                0f, 0f, 0f, default, null, 12f, enemy);
            InvokePrivate(projectile, "UpdateHoming", 0.1f);

            float turn = Vector3.Angle(Vector3.forward, projectile.TravelDirection);
            Assert.That(turn, Is.GreaterThan(0.1f));
            Assert.That(turn, Is.LessThanOrEqualTo(
                Projectile.DefaultHomingTurnRate * 0.1f + 0.05f));
            Assert.AreSame(enemy, projectile.HomingTarget);

            projectile.Despawn();
            Projectile reused = CombatObjectPool.SpawnProjectile(prefab,
                Vector3.one, Quaternion.identity);
            Assert.AreSame(projectile, reused);
            Assert.IsNull(reused.HomingTarget);
            Assert.AreEqual(Vector3.forward, reused.TravelDirection);
        }

        [Test]
        public void BurnAndPoisonAreIndependentAndRejectFriendlySources()
        {
            BrawlerController source = CreateBrawler("Source", TeamId.Blue, Vector3.zero);
            BrawlerController enemy = CreateBrawler("Enemy", TeamId.Red, Vector3.forward);
            BrawlerController ally = CreateBrawler("Ally", TeamId.Blue, Vector3.right);

            enemy.ApplySpellBurn(source, 18f, 2.4f, 0.6f);
            enemy.ApplySpellPoison(source, 25f, 4f, 0.4f);
            ally.ApplySpellPoison(source, 25f, 4f, 0.4f);

            Assert.IsTrue(enemy.IsBurning);
            Assert.IsTrue(enemy.IsPoisoned);
            StringAssert.Contains("BURN", enemy.ActiveStatusLabel);
            StringAssert.Contains("POISON", enemy.ActiveStatusLabel);
            Assert.IsFalse(ally.IsPoisoned);
            Assert.That(ReadField<int>(enemy, "poisonTicksRemaining"), Is.InRange(1, 20));
            Assert.That(ReadField<float>(enemy, "poisonDamagePerTick"),
                Is.LessThanOrEqualTo(enemy.Health.Max * 0.1f));
        }

        [Test]
        public void RefreshingActiveDamageStatusesPreservesScheduledTick()
        {
            BrawlerController source = CreateBrawler("Source", TeamId.Blue, Vector3.zero);
            BrawlerController enemy = CreateBrawler("Enemy", TeamId.Red, Vector3.forward);

            enemy.ApplySpellBurn(source, 18f, 2.4f, 0.6f);
            enemy.ApplySpellPoison(source, 25f, 4f, 0.7f);
            float burnTickBefore = ReadField<float>(enemy, "burnNextTickAt");
            float poisonTickBefore = ReadField<float>(enemy, "poisonNextTickAt");

            // Ground hazards refresh more frequently than the default burn tick.
            // A refresh must extend the status without postponing damage forever.
            enemy.ApplySpellBurn(source, 18f, 2.4f, 0.6f);
            enemy.ApplySpellPoison(source, 25f, 4f, 0.7f);

            Assert.AreEqual(burnTickBefore,
                ReadField<float>(enemy, "burnNextTickAt"), 0.0001f);
            Assert.AreEqual(poisonTickBefore,
                ReadField<float>(enemy, "poisonNextTickAt"), 0.0001f);
        }

        [Test]
        public void FireGroundHazardBurnsEnemiesButNeverAllies()
        {
            BrawlerController owner = CreateBrawler("FireOwner", TeamId.Blue, Vector3.zero);
            BrawlerController enemy = CreateBrawler("Enemy", TeamId.Red,
                new Vector3(1f, 0f, 0f));
            BrawlerController ally = CreateBrawler("Ally", TeamId.Blue,
                new Vector3(-1f, 0f, 0f));
            SpellSpecialty fire = SpellSpecialty.ForSchool(SpellSchool.Fire);

            GroundSpellHazard hazard = GroundSpellHazard.SpawnFire(owner, Vector3.zero,
                20f, fire);

            Assert.NotNull(hazard);
            Assert.That(hazard.Radius, Is.EqualTo(fire.groundEffectRadius).Within(0.001f));
            Assert.IsTrue(enemy.IsBurning);
            Assert.IsFalse(ally.IsBurning);
        }

        [Test]
        public void ArcaneHitHealsLowestWoundedAllyAndRitualHealsWholeTeam()
        {
            BrawlerController healer = CreateBrawler("Healer", TeamId.Blue, Vector3.zero);
            BrawlerController enemy = CreateBrawler("Enemy", TeamId.Red,
                new Vector3(0f, 0f, 4f));
            BrawlerController critical = CreateBrawler("Critical", TeamId.Blue,
                new Vector3(1f, 0f, 2f));
            BrawlerController scratched = CreateBrawler("Scratched", TeamId.Blue,
                new Vector3(-1f, 0f, 2f));
            critical.Health.TakeDamage(60f, enemy.gameObject);
            scratched.Health.TakeDamage(20f, enemy.gameObject);
            healer.specialty = SpellSpecialty.ForSchool(SpellSchool.Arcane);

            float criticalBefore = critical.Health.Current;
            float scratchedBefore = scratched.Health.Current;
            healer.ApplySpellSpecialtyHit(enemy, 20f, enemy.CombatAimPoint);

            Assert.That(critical.Health.Current,
                Is.EqualTo(criticalBefore + 20f * healer.specialty.allyHealFraction)
                    .Within(0.001f));
            Assert.AreEqual(scratchedBefore, scratched.Health.Current, 0.001f);

            criticalBefore = critical.Health.Current;
            scratchedBefore = scratched.Health.Current;
            float total = healer.ApplyArcaneRitualHeal(healer.CombatAimPoint, 8f);
            Assert.Greater(total, 0f);
            Assert.Greater(critical.Health.Current, criticalBefore);
            Assert.Greater(scratched.Health.Current, scratchedBefore);
        }

        BrawlerController CreateBrawler(string name, TeamId team, Vector3 position)
        {
            var gameObject = new GameObject(name);
            objects.Add(gameObject);
            gameObject.transform.position = position;
            Health health = gameObject.AddComponent<Health>();
            health.SetMax(100f);
            gameObject.AddComponent<Tests.InvectorCutoverTestMotor>();
            gameObject.AddComponent<Tests.InvectorCutoverTestAnimationDriver>();
            BrawlerController controller = gameObject.AddComponent<BrawlerController>();
            controller.team = team;
            InvokePrivate(controller, "Awake");
            manager.Register(controller);
            return controller;
        }

        Projectile CreateProjectile(Vector3 position)
        {
            var gameObject = new GameObject("TestProjectile");
            objects.Add(gameObject);
            gameObject.transform.position = position;
            return gameObject.AddComponent<Projectile>();
        }

        static T ReadField<T>(object target, string fieldName)
        {
            FieldInfo field = target.GetType().GetField(fieldName,
                BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.NotNull(field, fieldName);
            return (T)field.GetValue(target);
        }

        static void InvokePrivate(object target, string methodName, params object[] arguments)
        {
            MethodInfo method = target.GetType().GetMethod(methodName,
                BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.NotNull(method, methodName);
            method.Invoke(target, arguments);
        }

        static void DestroyPoolsAndHazards()
        {
            GroundSpellHazard[] hazards = Object.FindObjectsByType<GroundSpellHazard>(
                FindObjectsInactive.Include, FindObjectsSortMode.None);
            for (int i = 0; i < hazards.Length; i++)
                if (hazards[i] != null) Object.DestroyImmediate(hazards[i].gameObject);

            CombatObjectPool[] pools = Object.FindObjectsByType<CombatObjectPool>(
                FindObjectsInactive.Include, FindObjectsSortMode.None);
            for (int i = 0; i < pools.Length; i++)
                if (pools[i] != null) Object.DestroyImmediate(pools[i].gameObject);
        }
    }
}
