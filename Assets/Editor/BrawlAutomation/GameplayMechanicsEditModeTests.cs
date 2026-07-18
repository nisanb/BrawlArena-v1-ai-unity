using NUnit.Framework;
using System.Reflection;
using UnityEngine;

namespace BrawlArena.EditorAutomation
{
    public class GameplayMechanicsEditModeTests
    {
        [TestCase("frost", "ABSOLUTE ZERO", BrawlerSuperStyle.Burst)]
        [TestCase("bastion", "AEGIS SHOCKWAVE", BrawlerSuperStyle.Burst)]
        [TestCase("thorn", "EXPLOSIVE ARROW", BrawlerSuperStyle.ProjectileBlast)]
        [TestCase("unknown-legacy-id", "POWER BURST", BrawlerSuperStyle.Burst)]
        public void DefaultSuperConfigurationDefinesEveryBrawler(string id, string expectedName,
            BrawlerSuperStyle expectedStyle)
        {
            var definition = new BrawlerDefinition { id = id };

            definition.EnsureSuperConfiguration();

            Assert.AreEqual(expectedName, definition.superName);
            Assert.AreEqual(expectedStyle, definition.superStyle);
            Assert.Greater(definition.superDamageMultiplier, 1f);
            Assert.Greater(definition.superRange, 0f);
        }

        [Test]
        public void AuthoredSuperConfigurationIsNotOverwritten()
        {
            var definition = new BrawlerDefinition
            {
                id = "frost",
                superName = "AUTHORED TEST SUPER",
                superStyle = BrawlerSuperStyle.Dash,
                superDamageMultiplier = 3f,
                superRange = 9f,
            };

            definition.EnsureSuperConfiguration();

            Assert.AreEqual("AUTHORED TEST SUPER", definition.superName);
            Assert.AreEqual(BrawlerSuperStyle.Dash, definition.superStyle);
            Assert.AreEqual(3f, definition.superDamageMultiplier);
            Assert.AreEqual(9f, definition.superRange);
        }

        [Test]
        public void KnockoutConfiguredTargetSupportsMultiPushMatches()
        {
            var go = new GameObject("MatchRuleTest");
            try
            {
                var manager = go.AddComponent<MatchManager>();
                manager.ConfigureMode(GameMode.Knockout);
                Assert.AreEqual(8, manager.scoreToWin);
            }
            finally
            {
                Object.DestroyImmediate(go);
            }
        }

        [Test]
        public void BrawlerArcaneFlowStartsAtSixty()
        {
            var go = new GameObject("BaseEnergyTest");
            try
            {
                go.AddComponent<Tests.InvectorCutoverTestMotor>();
                go.AddComponent<Tests.InvectorCutoverTestAnimationDriver>();
                var brawler = go.AddComponent<BrawlerController>();
                typeof(BrawlerController).GetMethod("Awake",
                    BindingFlags.Instance | BindingFlags.NonPublic).Invoke(brawler, null);

                Assert.AreEqual(60f, brawler.maxStamina);
                Assert.AreEqual(60f, brawler.WardFlow);
                Assert.AreEqual(20f, brawler.wardStepCost);
            }
            finally
            {
                Object.DestroyImmediate(go);
            }
        }
    }
}
