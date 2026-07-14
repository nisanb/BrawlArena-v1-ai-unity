using System;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;

namespace BrawlArena.EditorAutomation
{
    public class CombatRuntimeCorrectnessEditModeTests
    {
        GameObject managerObject;
        GameObject healthObject;
        int previousTargetFrameRate;

        [OneTimeSetUp]
        public void CaptureTargetFrameRate()
        {
            previousTargetFrameRate = Application.targetFrameRate;
        }

        [OneTimeTearDown]
        public void RestoreTargetFrameRate()
        {
            Application.targetFrameRate = previousTargetFrameRate;
        }

        [SetUp]
        public void SetUp()
        {
            managerObject = new GameObject("CombatTestMatchManager");
            var manager = managerObject.AddComponent<MatchManager>();
            // Plain EditMode AddComponent does not run MonoBehaviour.Awake.
            typeof(MatchManager).GetMethod("Awake", BindingFlags.Instance | BindingFlags.NonPublic)
                ?.Invoke(manager, null);
            healthObject = new GameObject("CombatTestHealth");
            healthObject.AddComponent<Health>().SetMax(30f);
        }

        [TearDown]
        public void TearDown()
        {
            if (healthObject != null) UnityEngine.Object.DestroyImmediate(healthObject);
            if (managerObject != null) UnityEngine.Object.DestroyImmediate(managerObject);
            Application.targetFrameRate = previousTargetFrameRate;
        }

        [Test]
        public void TakeDamageReportsAppliedDeltaAndNotOverkill()
        {
            Health health = healthObject.GetComponent<Health>();
            float reported = -1f;
            health.Damaged += (amount, _) => reported = amount;

            float applied = health.TakeDamage(80f, null);

            Assert.AreEqual(30f, applied);
            Assert.AreEqual(30f, reported);
            Assert.AreEqual(0f, health.Current);
        }

        [Test]
        public void TakeDamageIsRejectedAfterMatchEnd()
        {
            Health health = healthObject.GetComponent<Health>();
            MatchManager.Instance.DeclareWinner(TeamId.Blue);

            float applied = health.TakeDamage(10f, null);

            Assert.AreEqual(0f, applied);
            Assert.AreEqual(30f, health.Current);
        }

        [TestCase(nameof(BrawlerController.TryAttackDirection))]
        [TestCase(nameof(BrawlerController.TrySuperDirection))]
        public void DirectionalCombatApiHasExpectedContract(string methodName)
        {
            var method = typeof(BrawlerController).GetMethod(methodName, new[] { typeof(Vector3) });

            Assert.NotNull(method);
            Assert.AreEqual(typeof(bool), method.ReturnType);
        }

        [Test]
        public void SegmentSphereIntersectionFindsFirstContact()
        {
            bool hit = CombatPhysics.TryIntersectSegmentSphere(Vector3.zero, Vector3.right,
                10f, new Vector3(5f, 0f, 0f), 1f, out float distance);

            Assert.IsTrue(hit);
            Assert.That(distance, Is.EqualTo(4f).Within(0.0001f));
        }

        [Test]
        public void CapsuleMathIncludesPointBlankAndTipTargets()
        {
            Vector3 start = new Vector3(0f, 1f, 0f);
            Vector3 end = new Vector3(0f, 1f, 3f);

            Assert.IsTrue(CombatPhysics.PointInsideCapsule(start, start, end, 0.5f));
            Assert.IsTrue(CombatPhysics.PointInsideCapsule(end + Vector3.forward * 0.4f,
                start, end, 0.5f));
            Assert.IsFalse(CombatPhysics.PointInsideCapsule(new Vector3(2f, 1f, 1f),
                start, end, 0.5f));
        }

        [Test]
        public void NamedCombatLayersAreAvailable()
        {
            Assert.GreaterOrEqual(CombatPhysics.GroundLayer, 0);
            Assert.GreaterOrEqual(CombatPhysics.WorldBlockerLayer, 0);
            Assert.GreaterOrEqual(CombatPhysics.BrawlerHitboxLayer, 0);
            Assert.GreaterOrEqual(CombatPhysics.ProjectileLayer, 0);
            Assert.GreaterOrEqual(CombatPhysics.VfxLayer, 0);
        }

        [TestCase("Floor1")]
        [TestCase("Floor2")]
        [TestCase("Floor1 (17)")]
        public void RuntimeOptimizerRecognizesOnlyFloorTileObjects(string objectName)
        {
            Assert.IsTrue(ArenaRuntimeOptimizer.IsFloorTileName(objectName));
            Assert.IsFalse(ArenaRuntimeOptimizer.IsFloorTileName("GroundCollider"));
            Assert.IsFalse(ArenaRuntimeOptimizer.IsFloorTileName("FloorLamp"));
        }
    }
}
