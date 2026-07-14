using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace BrawlArena.EditorAutomation.Tests
{
    [Category("InvectorTempestCombat")]
    public sealed class InvectorTempestCombatEditModeTests
    {
        readonly List<GameObject> created = new List<GameObject>();

        MatchManager manager;
        MatchManager previousManager;
        CombatObjectPool previousPool;
        Scene proofScene;
        int previousTargetFrameRate;

        [SetUp]
        public void SetUp()
        {
            previousTargetFrameRate = Application.targetFrameRate;
            previousManager = MatchManager.Instance;
            previousPool = ReadStaticField<CombatObjectPool>(
                typeof(CombatObjectPool), "instance");
            // EditMode Test Runner already supplies one isolated untitled scene.
            // Reuse it instead of trying to add a second unsaved scene.
            proofScene = SceneManager.GetActiveScene();

            GameObject poolObject = Track(new GameObject("TempestCombatObjectPool"));
            CombatObjectPool pool = poolObject.AddComponent<CombatObjectPool>();
            int sceneHandle = proofScene.handle;
            InvokePrivate(pool, "InitializeForScene", sceneHandle);

            GameObject managerObject = Track(new GameObject("TempestCombatMatchManager"));
            manager = managerObject.AddComponent<MatchManager>();
            SetStaticAutoPropertyBackingField(
                typeof(MatchManager), nameof(MatchManager.Instance), manager);
            SetAutoPropertyBackingField(manager, nameof(MatchManager.State), MatchState.Playing);
        }

        [TearDown]
        public void TearDown()
        {
            for (int i = created.Count - 1; i >= 0; i--)
            {
                if (created[i] != null) Object.DestroyImmediate(created[i]);
            }
            created.Clear();
            SetStaticAutoPropertyBackingField(
                typeof(MatchManager), nameof(MatchManager.Instance), previousManager);
            SetStaticField(typeof(CombatObjectPool), "instance", previousPool);
            Application.targetFrameRate = previousTargetFrameRate;
        }

        [Test]
        public void GeneratedStormDefinitionPinsRapidCastAndExplicitEyeOfTheStormPayload()
        {
            BrawlerDefinition tempest = ArenaSceneBuilder.BuildRosterFromExistingAssets()
                .Single(definition => definition.id == InvectorTempestMigrationBuilder.RosterId);

            Assert.That(tempest.displayName, Is.EqualTo("Tempest"));
            Assert.That(tempest.role, Is.EqualTo("Stormcaller"));
            Assert.That(tempest.maxHealth, Is.EqualTo(88f));
            Assert.That(tempest.damage, Is.EqualTo(26f));
            Assert.That(tempest.attackRange, Is.EqualTo(9.5f));
            Assert.That(tempest.cooldown, Is.EqualTo(0.82f));
            Assert.That(tempest.hitDelay, Is.EqualTo(0.32f));
            Assert.That(tempest.moveLock, Is.EqualTo(0.36f).Within(0.0001f));
            Assert.That(tempest.moveSpeed, Is.EqualTo(5.55f));
            Assert.That(tempest.autoAimRange, Is.EqualTo(12f));
            Assert.That(tempest.projectileSpeed, Is.EqualTo(21f));
            Assert.That(tempest.projectilePrefab, Is.Not.Null);
            Assert.That(tempest.projectileReadability.threat,
                Is.EqualTo(ProjectileThreatType.Chain));

            Assert.That(tempest.specialty.school, Is.EqualTo(SpellSchool.Storm));
            Assert.That(tempest.specialty.chainTargets, Is.EqualTo(2));
            Assert.That(tempest.specialty.chainRange, Is.EqualTo(4.25f));
            Assert.That(tempest.specialty.chainDamageMultiplier, Is.EqualTo(0.55f));

            Assert.That(tempest.superName, Is.EqualTo("EYE OF THE STORM"));
            Assert.That(tempest.superStyle, Is.EqualTo(BrawlerSuperStyle.ProjectileBlast));
            Assert.That(tempest.superDamageMultiplier, Is.EqualTo(1.62f));
            Assert.That(tempest.superRange, Is.EqualTo(11f));
            Assert.That(tempest.superKnockback, Is.EqualTo(5f));
            Assert.That(tempest.superProjectileSpeed, Is.EqualTo(26.25f));
            Assert.That(tempest.superProjectileBlastRadius, Is.EqualTo(2.3f));
            Assert.That(tempest.superProjectilePrefab, Is.Not.Null);
            Assert.That(tempest.superImpactVfx, Is.Not.Null);
            Assert.That(tempest.secondarySuperVfx, Is.Not.Null);
            Assert.That(tempest.invectorHumanPrefab, Is.Not.Null);
            Assert.That(tempest.invectorAIPrefab, Is.Not.Null);

            var fallback = new BrawlerDefinition { id = "storm" };
            fallback.EnsureSuperConfiguration();
            Assert.That(fallback.superName, Is.EqualTo("TEMPEST CHAIN"));
            Assert.That(fallback.superStyle, Is.EqualTo(BrawlerSuperStyle.ProjectileBlast));
            Assert.That(fallback.superDamageMultiplier, Is.EqualTo(1.55f));
            Assert.That(fallback.superRange, Is.EqualTo(14f));
            Assert.That(fallback.superKnockback, Is.EqualTo(4f));
            Assert.That(fallback.superProjectileSpeed, Is.EqualTo(29f));
            Assert.That(fallback.superProjectileBlastRadius, Is.EqualTo(2.1f));
            Assert.That(tempest.superName, Is.Not.EqualTo(fallback.superName));

            AssertRapidCastCadenceContract();
        }

        static void AssertRapidCastCadenceContract()
        {
            string projectRoot = Directory.GetParent(Application.dataPath)?.FullName;
            Assert.That(projectRoot, Is.Not.Null);
            string source = File.ReadAllText(Path.Combine(
                projectRoot, "Assets/Scripts/Brawl/BrawlerController.cs"));

            string automatic = Extract(source,
                "public bool TryAttackAuto()", "public bool TryAttack(BrawlerController target)");
            AssertOrdered(automatic,
                "if (!BasicAttackReady) return false;",
                "FindNearestReachableBasicTarget()");

            string begin = Extract(source,
                "bool BeginAttack(BrawlerController target, Vector3 worldDirection)",
                "public bool TrySuperAuto()");
            AssertOrdered(begin,
                "if (!BasicAttackReady) return false;",
                "if (!TryConsumeBasicAttackCharge()) return false;",
                "nextAttackTime = Time.time + attackCooldown;",
                "attackLockUntil = Time.time + attackMoveLock;",
                "AttacksUsed++;",
                "PresentWeaponAim(worldDirection);",
                "StartCoroutine(AttackRoutine(target, worldDirection))");

            string routine = Extract(source,
                "IEnumerator AttackRoutine(BrawlerController target, Vector3 worldDirection)",
                "IEnumerator SuperRoutine(BrawlerController target, Vector3 worldDirection)");
            AssertOrdered(routine,
                "TryPresent(AnimationPresentationOperation.PlayBasicAttack)",
                "yield return new WaitForSeconds(attackHitDelay);",
                "if (projectilePrefab != null) FireProjectile(target, worldDirection)",
                "finally",
                "PresentWeaponAim(Vector3.zero)",
                "attackRoutine = null;");
        }

        [Test]
        public void StormChainChoosesNearestVisibleEnemyAndRejectsTheExactRangeBoundary()
        {
            SpellSpecialty oneHop = SpellSpecialty.ForSchool(SpellSchool.Storm);
            oneHop.chainTargets = 1;
            BrawlerController source = CreateBrawler(
                "StormSource", TeamId.Blue, new Vector3(-3f, 0f, -3f));
            source.specialty = oneHop;
            BrawlerController first = CreateBrawler(
                "InitialVictim", TeamId.Red, Vector3.zero);
            BrawlerController blockedNearest = CreateBrawler(
                "BlockedNearest", TeamId.Red, new Vector3(2f, 0f, 0f));
            BrawlerController fartherVisible = CreateBrawler(
                "FartherVisibleCandidate", TeamId.Red, new Vector3(0f, 0f, 3.5f));
            BrawlerController visible = CreateBrawler(
                "VisibleCandidate", TeamId.Red, new Vector3(0f, 0f, 2.5f));

            Assert.That(CombatPhysics.WorldBlockerLayer, Is.GreaterThanOrEqualTo(0));
            GameObject blocker = Track(GameObject.CreatePrimitive(PrimitiveType.Cube));
            blocker.name = "TempestChainWorldBlocker";
            blocker.layer = CombatPhysics.WorldBlockerLayer;
            blocker.transform.position = new Vector3(1f, 1f, 0f);
            blocker.transform.localScale = new Vector3(0.5f, 2f, 0.75f);
            Physics.SyncTransforms();

            Assert.That(CombatPhysics.HasLineOfSight(
                first.CombatAimPoint, blockedNearest.CombatAimPoint), Is.False);
            Assert.That(CombatPhysics.HasLineOfSight(
                first.CombatAimPoint, visible.CombatAimPoint), Is.True);

            source.ApplySpellSpecialtyHit(first, 20f, first.CombatAimPoint);

            Assert.That(blockedNearest.Health.Current, Is.EqualTo(100f));
            Assert.That(fartherVisible.Health.Current, Is.EqualTo(100f),
                "The nearer visible target must win even when it appears later in roster order.");
            Assert.That(visible.Health.Current, Is.EqualTo(89f).Within(0.0001f));

            BrawlerController boundary = CreateBrawler(
                "BoundaryCandidate", TeamId.Red, new Vector3(4.25f, 0f, 0f));
            manager.GetBrawlers().Remove(blockedNearest);
            manager.GetBrawlers().Remove(fartherVisible);
            manager.GetBrawlers().Remove(visible);
            blocker.SetActive(false);
            Physics.SyncTransforms();

            source.ApplySpellSpecialtyHit(first, 20f, first.CombatAimPoint);
            Assert.That(boundary.Health.Current, Is.EqualTo(100f),
                "Chain range is a strict less-than boundary.");

            boundary.transform.position = new Vector3(4.24f, 0f, 0f);
            Physics.SyncTransforms();
            source.ApplySpellSpecialtyHit(first, 20f, first.CombatAimPoint);
            Assert.That(boundary.Health.Current, Is.EqualTo(89f).Within(0.0001f));
        }

        [Test]
        public void StormChainStopsOnInvulnerableHopAndDecaysFromActualAppliedDamage()
        {
            BrawlerController source = CreateBrawler(
                "StormSource", TeamId.Blue, new Vector3(-3f, 0f, 0f));
            source.specialty = SpellSpecialty.ForSchool(SpellSchool.Storm);
            BrawlerController first = CreateBrawler(
                "InitialVictim", TeamId.Red, Vector3.zero);
            BrawlerController firstHop = CreateBrawler(
                "FirstHop", TeamId.Red, new Vector3(2f, 0f, 0f), 1f);
            BrawlerController secondHop = CreateBrawler(
                "SecondHop", TeamId.Red, new Vector3(6f, 0f, 0f));

            firstHop.Health.Invulnerable = true;
            source.ApplySpellSpecialtyHit(first, 20f, first.CombatAimPoint);
            Assert.That(firstHop.Health.Current, Is.EqualTo(1f));
            Assert.That(secondHop.Health.Current, Is.EqualTo(100f),
                "A zero-applied hop must terminate the chain.");

            firstHop.Health.Invulnerable = false;
            source.ApplySpellSpecialtyHit(first, 3f, first.CombatAimPoint);
            Assert.That(firstHop.Health.Current, Is.EqualTo(0f).Within(0.0001f));
            Assert.That(secondHop.Health.Current, Is.EqualTo(99.45f).Within(0.0001f),
                "The next hop must decay from the 1 actually applied, not the 1.65 requested.");
        }

        [Test]
        public void PooledEyeOfTheStormProjectileRetainsBrawlProjectileBlastPayload()
        {
            BrawlerDefinition tempest = ArenaSceneBuilder.BuildRosterFromExistingAssets()
                .Single(definition => definition.id == InvectorTempestMigrationBuilder.RosterId);
            BrawlerController owner = CreateBrawler(
                "TempestProjectileOwner", TeamId.Blue, Vector3.zero);
            GameObject basicProjectile = CreatePrefab("TempestBasicProjectileProof");
            GameObject superProjectile = CreatePrefab("TempestSuperProjectileProof");
            basicProjectile.AddComponent<Projectile>();
            superProjectile.AddComponent<Projectile>();
            GameObject normalImpact = CreatePrefab("TempestNormalImpactProof");
            GameObject superImpact = CreatePrefab("TempestSuperImpactProof");
            GameObject secondaryImpact = CreatePrefab("TempestSecondaryImpactProof");

            owner.attackDamage = tempest.damage;
            owner.attackRange = tempest.attackRange;
            owner.autoAimRange = tempest.autoAimRange;
            owner.projectilePrefab = basicProjectile;
            owner.projectileSpeed = tempest.projectileSpeed;
            owner.specialty = tempest.specialty;
            owner.impactVfx = normalImpact;
            owner.secondaryImpactVfx = secondaryImpact;
            owner.superDamageMultiplier = tempest.superDamageMultiplier;
            owner.superKnockback = tempest.superKnockback;
            owner.superProjectileSpeed = tempest.superProjectileSpeed;
            owner.superProjectileBlastRadius = tempest.superProjectileBlastRadius;
            owner.superProjectilePrefab = superProjectile;
            owner.superImpactVfx = superImpact;

            InvokePrivate(owner, "FireSuperProjectile", null, Vector3.forward);

            CombatObjectPool pool = Object.FindFirstObjectByType<CombatObjectPool>(
                FindObjectsInactive.Include);
            Assert.That(pool, Is.Not.Null);
            Projectile shot = pool.GetComponentsInChildren<Projectile>(true)
                .Single(projectile => projectile.gameObject.activeSelf);
            SpellSpecialty payload = ReadField<SpellSpecialty>(shot, "specialty");

            Assert.That(ReadField<BrawlerController>(shot, "owner"), Is.SameAs(owner));
            Assert.That(ReadField<float>(shot, "damage"),
                Is.EqualTo(tempest.damage * tempest.superDamageMultiplier).Within(0.0001f));
            Assert.That(ReadField<float>(shot, "speed"), Is.EqualTo(26.25f));
            Assert.That(ReadField<GameObject>(shot, "impactVfx"), Is.SameAs(superImpact));
            Assert.That(ReadField<GameObject>(shot, "secondaryImpactVfx"),
                Is.SameAs(secondaryImpact));
            Assert.That(ReadField<float>(shot, "blastRadius"), Is.EqualTo(2.3f));
            Assert.That(ReadField<float>(shot, "knockback"), Is.EqualTo(5f));
            Assert.That(ReadField<float>(shot, "activeHitRadius"), Is.EqualTo(0.48f));
            Assert.That(ReadField<float>(shot, "remainingTravelDistance"),
                Is.EqualTo(float.PositiveInfinity));
            Assert.That(ReadField<bool>(shot, "launched"), Is.True);
            Assert.That(payload.school, Is.EqualTo(SpellSchool.Storm));
            Assert.That(payload.chainTargets, Is.EqualTo(2));
            Assert.That(payload.chainRange, Is.EqualTo(4.25f));
            Assert.That(payload.chainDamageMultiplier, Is.EqualTo(0.55f));
            Assert.That(shot.gameObject.layer, Is.EqualTo(CombatPhysics.ProjectileLayer));
            ProjectileReadabilityLease readability =
                shot.GetComponent<ProjectileReadabilityLease>();
            Assert.That(readability, Is.Not.Null);
            Assert.That(readability.Threat, Is.EqualTo(ProjectileThreatType.Chain));
            Assert.That(readability.AttackTier, Is.EqualTo(ProjectileAttackTier.Super));
            Assert.That(readability.SplashRadius, Is.EqualTo(2.3f));
            Assert.That(readability.WorldInteraction,
                Is.EqualTo(ProjectileWorldInteraction.StopsOnWorld));
        }

        BrawlerController CreateBrawler(
            string name, TeamId team, Vector3 position, float maxHealth = 100f)
        {
            GameObject root = Track(new GameObject(name));
            root.transform.position = position;
            Health health = root.AddComponent<Health>();
            InvokePrivate(health, "Awake");
            health.SetMax(maxHealth, true);
            root.AddComponent<InvectorCutoverTestMotor>();
            root.AddComponent<InvectorCutoverTestAnimationDriver>();
            BrawlerController brawler = root.AddComponent<BrawlerController>();
            SetAutoPropertyBackingField(brawler, nameof(BrawlerController.Health), health);
            brawler.team = team;
            manager.GetBrawlers().Add(brawler);
            return brawler;
        }

        GameObject CreatePrefab(string name)
        {
            GameObject prefab = Track(new GameObject(name));
            prefab.SetActive(false);
            return prefab;
        }

        GameObject Track(GameObject value)
        {
            created.Add(value);
            return value;
        }

        static T ReadField<T>(object target, string fieldName)
        {
            FieldInfo field = target.GetType().GetField(
                fieldName, BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(field, Is.Not.Null, "Missing field " + fieldName + ".");
            return (T)field.GetValue(target);
        }

        static T ReadStaticField<T>(Type type, string fieldName)
        {
            FieldInfo field = type.GetField(
                fieldName, BindingFlags.Static | BindingFlags.NonPublic);
            Assert.That(field, Is.Not.Null, "Missing static field " + fieldName + ".");
            return (T)field.GetValue(null);
        }

        static object InvokePrivate(object target, string methodName, params object[] arguments)
        {
            MethodInfo method = target.GetType().GetMethod(
                methodName, BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(method, Is.Not.Null, "Missing method " + methodName + ".");
            return method.Invoke(target, arguments);
        }

        static void SetAutoPropertyBackingField(object target, string propertyName, object value)
        {
            FieldInfo field = target.GetType().GetField(
                "<" + propertyName + ">k__BackingField",
                BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(field, Is.Not.Null, "Missing property " + propertyName + ".");
            field.SetValue(target, value);
        }

        static void SetStaticAutoPropertyBackingField(
            Type type, string propertyName, object value)
        {
            FieldInfo field = type.GetField(
                "<" + propertyName + ">k__BackingField",
                BindingFlags.Static | BindingFlags.NonPublic);
            Assert.That(field, Is.Not.Null, "Missing static property " + propertyName + ".");
            field.SetValue(null, value);
        }

        static void SetStaticField(Type type, string fieldName, object value)
        {
            FieldInfo field = type.GetField(
                fieldName, BindingFlags.Static | BindingFlags.NonPublic);
            Assert.That(field, Is.Not.Null, "Missing static field " + fieldName + ".");
            field.SetValue(null, value);
        }

        static string Extract(string source, string startMarker, string endMarker)
        {
            int start = source.IndexOf(startMarker, StringComparison.Ordinal);
            Assert.That(start, Is.GreaterThanOrEqualTo(0),
                "Missing source marker " + startMarker + ".");
            int end = source.IndexOf(
                endMarker, start + startMarker.Length, StringComparison.Ordinal);
            Assert.That(end, Is.GreaterThan(start),
                "Missing source marker " + endMarker + ".");
            return source.Substring(start, end - start);
        }

        static void AssertOrdered(string source, params string[] markers)
        {
            int prior = -1;
            for (int i = 0; i < markers.Length; i++)
            {
                int index = source.IndexOf(markers[i], prior + 1, StringComparison.Ordinal);
                Assert.That(index, Is.GreaterThan(prior),
                    "Expected ordered source marker " + markers[i] + ".");
                prior = index;
            }
        }

    }
}
