using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

namespace BrawlArena.EditorAutomation.Tests
{
    [Category("CombatCadenceReadability")]
    public sealed class CombatCadenceReadabilityEditModeTests
    {
        readonly List<GameObject> objects = new List<GameObject>();
        readonly List<Object> assets = new List<Object>();

        [SetUp]
        public void SetUp()
        {
            DestroyPools();
        }

        [TearDown]
        public void TearDown()
        {
            DestroyPools();
            for (int i = objects.Count - 1; i >= 0; i--)
                if (objects[i] != null) Object.DestroyImmediate(objects[i]);
            for (int i = assets.Count - 1; i >= 0; i--)
                if (assets[i] != null) Object.DestroyImmediate(assets[i]);
            objects.Clear();
            assets.Clear();
        }

        [Test]
        public void ProductionRosterUsesCanonicalThreeToFiveDirectHitMatrix()
        {
            BrawlerDefinition[] roster = ArenaSceneBuilder.BuildRosterFromExistingAssets();
            // Three-hero roster: frost, thorn, bastion. Bastion's matrix row
            // and column follow the same canonical hit-count rule, widened
            // only when bastion is the defender since its tank-tier 150 HP
            // deliberately costs low/medium-damage attackers extra hits.
            Assert.That(roster.Length, Is.EqualTo(3));

            float[] health = { 112f, 96f, 150f };
            float[] damage = { 23f, 30f, 26f };
            int[,] expected =
            {
                { 5, 5, 7 },
                { 4, 4, 5 },
                { 5, 4, 6 },
            };

            for (int attacker = 0; attacker < roster.Length; attacker++)
            {
                Assert.That(roster[attacker].damage,
                    Is.EqualTo(damage[attacker]).Within(0.0001f));
                Assert.That(roster[attacker].maxHealth,
                    Is.EqualTo(health[attacker]).Within(0.0001f));
                for (int defender = 0; defender < roster.Length; defender++)
                {
                    int hits = Mathf.CeilToInt(
                        roster[defender].maxHealth / roster[attacker].damage);
                    Assert.That(hits, Is.EqualTo(expected[attacker, defender]),
                        roster[attacker].displayName + " -> " +
                        roster[defender].displayName);

                    bool bastionDefends = defender == 2;
                    Assert.That(hits, bastionDefends ? Is.InRange(3, 7) : Is.InRange(3, 5),
                        roster[attacker].displayName + " -> " + roster[defender].displayName +
                        (bastionDefends
                            ? " must stay within the widened tank direct-hit band."
                            : " must stay within the canonical direct-hit band."));
                }
            }

            Assert.That(roster[0].maxHealth, Is.GreaterThan(roster[1].maxHealth),
                "Rime remains the toughest controller.");
            Assert.That(roster[0].damage, Is.LessThan(roster[1].damage),
                "Rime remains the lower direct-damage caster.");
            Assert.That(roster[1].damage, Is.GreaterThan(roster[0].damage));
            Assert.That(roster[1].attackRange, Is.GreaterThan(roster[0].attackRange),
                "Thorn outranges Rime.");
            Assert.That(roster[2].maxHealth, Is.GreaterThan(roster[0].maxHealth),
                "Bastion is the tankiest roster member.");
            Assert.That(roster[2].attackRange, Is.LessThan(roster[1].attackRange),
                "Bastion must close distance; Thorn keeps range.");
        }

        [Test]
        public void SpecialtyAndSuperBoundariesCannotSkipTheThreeHitFloorOrOneShot()
        {
            BrawlerDefinition[] roster = ArenaSceneBuilder.BuildRosterFromExistingAssets();
            float minimumHealthyRosterHp = float.MaxValue;
            for (int i = 0; i < roster.Length; i++)
                minimumHealthyRosterHp = Mathf.Min(minimumHealthyRosterHp,
                    roster[i].maxHealth);

            for (int i = 0; i < roster.Length; i++)
            {
                Assert.That(roster[i].damage * 2f,
                    Is.LessThan(minimumHealthyRosterHp),
                    roster[i].displayName + " two ordinary hits must not cross the healthy-roster floor.");
                Assert.That(Mathf.CeilToInt(minimumHealthyRosterHp / roster[i].damage),
                    Is.InRange(3, 5));

                float directSuper = roster[i].damage * roster[i].superDamageMultiplier;
                Assert.That(directSuper, Is.LessThan(minimumHealthyRosterHp),
                    roster[i].displayName + " direct Super one-shot boundary");
            }
        }

        [Test]
        public void RosterProfilesKeepTeamThreatTierSplashAndWallRulesIndependent()
        {
            BrawlerDefinition[] roster = ArenaSceneBuilder.BuildRosterFromExistingAssets();
            var threats = new HashSet<ProjectileThreatType>();
            for (int i = 0; i < roster.Length; i++)
            {
                ProjectileReadabilityProfile profile =
                    roster[i].projectileReadability.Sanitized(roster[i].id,
                        roster[i].specialty.school);
                Assert.IsTrue(profile.configured);
                Assert.That(profile.accent.a, Is.GreaterThanOrEqualTo(0.45f));
                Assert.That(profile.trailWidth, Is.InRange(0.025f, 0.18f));

                // Bastion is melee (no projectilePrefab); its readability
                // profile is never presented in flight, so it is exempt from
                // the distinct-silhouette requirement below.
                if (roster[i].projectilePrefab == null) continue;
                Assert.IsTrue(threats.Add(profile.threat),
                    "Every projectile-armed roster member's launch silhouette is distinct.");
            }

            GameObject projectileObject = new GameObject("ReadabilityLease");
            objects.Add(projectileObject);
            ProjectileReadabilityLease lease =
                ProjectileReadabilityLease.GetOrCreate(projectileObject);
            lease.Configure(TeamId.Blue, roster[0].projectileReadability,
                ProjectileAttackTier.Basic, 2f,
                ProjectileWorldInteraction.StopsOnWorld);

            Assert.That(lease.AttackTier, Is.EqualTo(ProjectileAttackTier.Basic),
                "Tier is explicit and must not be inferred from splash radius.");
            Assert.That(lease.SplashRadius, Is.EqualTo(2f));
            Assert.That(lease.WorldInteraction,
                Is.EqualTo(ProjectileWorldInteraction.StopsOnWorld));
            Assert.IsTrue(lease.WorldRuleCueVisible);
            Assert.IsFalse(lease.SuperCueVisible);
            Assert.IsTrue(lease.SplashCueVisible);
        }

        [Test]
        public void ProjectileCloneReuseOverwritesAllGeneratedStateWithoutTouchingVendorMaterial()
        {
            BrawlerDefinition[] roster = ArenaSceneBuilder.BuildRosterFromExistingAssets();
            GameObject prefab = new GameObject("ReadableProjectilePrefab");
            prefab.SetActive(false);
            objects.Add(prefab);
            prefab.AddComponent<Projectile>();
            GameObject vendorVisual = GameObject.CreatePrimitive(PrimitiveType.Quad);
            vendorVisual.name = "Vendor Visual";
            vendorVisual.transform.SetParent(prefab.transform, false);
            Object.DestroyImmediate(vendorVisual.GetComponent<Collider>());
            Material vendorMaterial = new Material(Shader.Find("Sprites/Default"));
            vendorMaterial.color = new Color(0.16f, 0.27f, 0.41f, 1f);
            assets.Add(vendorMaterial);
            vendorVisual.GetComponent<MeshRenderer>().sharedMaterial = vendorMaterial;
            Color authoredVendorColor = vendorMaterial.color;

            Projectile first = CombatObjectPool.SpawnProjectile(prefab,
                Vector3.zero, Quaternion.identity);
            ProjectileReadabilityLease firstLease =
                ProjectileReadabilityLease.GetOrCreate(first.gameObject);
            firstLease.Configure(TeamId.Blue, roster[0].projectileReadability,
                ProjectileAttackTier.Basic, 2f,
                ProjectileWorldInteraction.StopsOnWorld);
            Assert.That(firstLease.AttackTier, Is.EqualTo(ProjectileAttackTier.Basic));
            Assert.That(firstLease.SplashRadius, Is.EqualTo(2f));
            Assert.That(firstLease.WorldInteraction,
                Is.EqualTo(ProjectileWorldInteraction.StopsOnWorld));
            firstLease.Configure(TeamId.Blue, roster[0].projectileReadability,
                ProjectileAttackTier.Basic, 0f,
                ProjectileWorldInteraction.StopsOnWorld);
            Color firstTeam = firstLease.TeamCueColor;
            Color firstThreat = firstLease.ThreatCueColor;
            int generatedChildCount = CountGeneratedChildren(first.transform);
            Assert.IsTrue(firstLease.TrajectoryEmitting);
            Assert.IsFalse(firstLease.SuperCueVisible);
            Assert.IsFalse(firstLease.SplashCueVisible);
            Assert.IsTrue(firstLease.WorldRuleCueVisible);
            AssertGeneratedChildrenUseLayer(first.transform,
                CombatPhysics.ProjectileLayer);
            Assert.That(vendorMaterial.color, Is.EqualTo(authoredVendorColor));

            TrailRenderer trail = first.transform.Find("Brawl Trajectory Trail")
                .GetComponent<TrailRenderer>();
            trail.AddPosition(Vector3.zero);
            trail.AddPosition(Vector3.one);
            first.Despawn();

            Assert.IsFalse(firstLease.IsConfigured);
            Assert.IsFalse(firstLease.TrajectoryEmitting);
            Assert.AreEqual(0, trail.positionCount);
            AssertGeneratedRenderersReset(first.transform);

            Projectile reused = CombatObjectPool.SpawnProjectile(prefab,
                Vector3.one, Quaternion.identity);
            Assert.AreSame(first, reused);
            ProjectileReadabilityLease reusedLease =
                ProjectileReadabilityLease.GetOrCreate(reused.gameObject);
            reusedLease.Configure(TeamId.Red, roster[1].projectileReadability,
                ProjectileAttackTier.Super, 0f,
                ProjectileWorldInteraction.StopsOnWorld);
            Assert.That(reusedLease.AttackTier, Is.EqualTo(ProjectileAttackTier.Super));
            Assert.That(reusedLease.SplashRadius, Is.Zero);
            reusedLease.Configure(TeamId.Red, roster[1].projectileReadability,
                ProjectileAttackTier.Super, 2.6f,
                ProjectileWorldInteraction.StopsOnWorld);

            Assert.That(CountGeneratedChildren(reused.transform),
                Is.EqualTo(generatedChildCount), "Reuse must not grow the rig.");
            Assert.That(reusedLease.SourceTeam, Is.EqualTo(TeamId.Red));
            Assert.That(reusedLease.Threat,
                Is.EqualTo(ProjectileThreatType.Precision));
            Assert.That(reusedLease.AttackTier, Is.EqualTo(ProjectileAttackTier.Super));
            Assert.That(reusedLease.SplashRadius, Is.EqualTo(2.6f));
            Assert.IsTrue(reusedLease.SuperCueVisible);
            Assert.IsTrue(reusedLease.SplashCueVisible);
            Assert.IsTrue(reusedLease.WorldRuleCueVisible);
            AssertGeneratedChildrenUseLayer(reused.transform,
                CombatPhysics.ProjectileLayer);
            Assert.That(reusedLease.TeamCueColor, Is.Not.EqualTo(firstTeam));
            Assert.That(reusedLease.ThreatCueColor, Is.Not.EqualTo(firstThreat));
            Transform splash = reused.transform.Find("Brawl Splash Radius");
            Assert.That(splash.lossyScale.x, Is.EqualTo(2.6f).Within(0.001f));
            Assert.That(splash.lossyScale.z, Is.EqualTo(2.6f).Within(0.001f));
            Assert.That(vendorMaterial.color, Is.EqualTo(authoredVendorColor));
            Assert.AreSame(vendorMaterial,
                reused.transform.Find("Vendor Visual").GetComponent<MeshRenderer>()
                    .sharedMaterial);
            reused.Despawn();
        }

        [Test]
        public void PooledOutcomeCueResetsAndReconfiguresWithoutGameplayComponents()
        {
            BrawlerDefinition[] roster = ArenaSceneBuilder.BuildRosterFromExistingAssets();
            ProjectileImpactReadability first =
                ProjectileReadabilityRuntime.SpawnImpactCue(Vector3.zero,
                    ProjectileImpactOutcome.WorldBlocked, TeamId.Blue,
                    roster[0].projectileReadability, ProjectileAttackTier.Basic, 0f);
            Assert.NotNull(first);
            Assert.IsTrue(first.IsConfigured);
            Assert.That(first.Outcome, Is.EqualTo(ProjectileImpactOutcome.WorldBlocked));
            Assert.IsEmpty(first.GetComponentsInChildren<Collider>(true));
            Assert.That(CountGeneratedChildren(first.transform), Is.EqualTo(2));
            AssertGeneratedChildrenUseLayer(first.transform,
                CombatPhysics.VfxLayer);
            GameObject firstObject = first.gameObject;
            Assert.IsTrue(CombatObjectPool.Release(firstObject));
            Assert.IsFalse(first.IsConfigured);
            Assert.That(first.SplashRadius, Is.Zero);
            AssertGeneratedRenderersReset(first.transform);

            ProjectileImpactReadability reused =
                ProjectileReadabilityRuntime.SpawnImpactCue(Vector3.right,
                    ProjectileImpactOutcome.RangeExpired, TeamId.Red,
                    roster[1].projectileReadability, ProjectileAttackTier.Super, 2.6f);
            Assert.AreSame(firstObject, reused.gameObject);
            Assert.That(reused.Outcome,
                Is.EqualTo(ProjectileImpactOutcome.RangeExpired));
            Assert.That(reused.SourceTeam, Is.EqualTo(TeamId.Red));
            Assert.That(reused.Threat, Is.EqualTo(ProjectileThreatType.Precision));
            Assert.That(reused.AttackTier, Is.EqualTo(ProjectileAttackTier.Super));
            Assert.That(reused.SplashRadius, Is.EqualTo(2.6f));
            Assert.That(CountGeneratedChildren(reused.transform), Is.EqualTo(2),
                "Impact reuse must not duplicate generated renderers.");
            AssertGeneratedChildrenUseLayer(reused.transform,
                CombatPhysics.VfxLayer);
            LineRenderer footprint = reused.transform.Find("Brawl Impact Footprint")
                .GetComponent<LineRenderer>();
            Vector3 point = footprint.GetPosition(0);
            Assert.That(new Vector2(point.x, point.z).magnitude,
                Is.EqualTo(2.6f).Within(0.001f),
                "The warning footprint must use the authoritative blast radius.");
            Assert.IsEmpty(reused.GetComponentsInChildren<Collider>(true));
            CombatObjectPool.Release(reused.gameObject);
        }

        static int CountGeneratedChildren(Transform root)
        {
            int count = 0;
            for (int i = 0; i < root.childCount; i++)
                if (root.GetChild(i).name.StartsWith("Brawl ")) count++;
            return count;
        }

        static void AssertGeneratedRenderersReset(Transform root)
        {
            Renderer[] renderers = root.GetComponentsInChildren<Renderer>(true);
            var block = new MaterialPropertyBlock();
            for (int i = 0; i < renderers.Length; i++)
            {
                if (!renderers[i].name.StartsWith("Brawl ")) continue;
                block.Clear();
                renderers[i].GetPropertyBlock(block);
                Assert.IsTrue(block.isEmpty, renderers[i].name + " property block");
                Assert.IsFalse(renderers[i].enabled, renderers[i].name + " visibility");
            }
        }

        static void AssertGeneratedChildrenUseLayer(Transform root, int expectedLayer)
        {
            for (int i = 0; i < root.childCount; i++)
            {
                Transform child = root.GetChild(i);
                if (!child.name.StartsWith("Brawl ")) continue;
                Assert.That(child.gameObject.layer, Is.EqualTo(expectedLayer),
                    child.name + " layer");
            }
        }

        static void DestroyPools()
        {
            CombatObjectPool[] pools = Object.FindObjectsByType<CombatObjectPool>(
                FindObjectsInactive.Include, FindObjectsSortMode.None);
            for (int i = 0; i < pools.Length; i++)
                if (pools[i] != null) Object.DestroyImmediate(pools[i].gameObject);
        }
    }
}
