using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

namespace BrawlArena.EditorAutomation
{
    public class SkillProgressEditModeTests
    {
        [SetUp]
        public void SetUp()
        {
            Seed("aria", 0);
        }

        [TearDown]
        public void TearDown()
        {
            Progress.ClearEditorTestData();
        }

        [Test]
        public void TryUpgradeSkillConsumesCharacterSkillPoints()
        {
            Seed("aria", 120);

            Assert.IsTrue(Progress.TryUpgradeSkill("aria", "arcane_edge"));
            Assert.AreEqual(1, Progress.GetSkillLevel("aria", "arcane_edge"));
            Assert.AreEqual(80, Progress.Get("aria").points);

            Assert.IsTrue(Progress.TryUpgradeSkill("aria", "arcane_edge"));
            Assert.AreEqual(2, Progress.GetSkillLevel("aria", "arcane_edge"));
            Assert.AreEqual(0, Progress.Get("aria").points);
            Assert.IsFalse(Progress.CanUpgradeSkill("aria", "arcane_edge"));
        }

        [Test]
        public void TryUpgradeSkillStopsAtMaxLevel()
        {
            Seed("aria", 1000, new CharacterSkillProgress { id = "arcane_edge", level = Progress.MaxSkillLevel });

            Assert.IsFalse(Progress.TryUpgradeSkill("aria", "arcane_edge"));
            Assert.AreEqual(Progress.MaxSkillLevel, Progress.GetSkillLevel("aria", "arcane_edge"));
            Assert.AreEqual(1000, Progress.Get("aria").points);
        }

        [Test]
        public void SkillBookAppliesLearnedBonuses()
        {
            Seed("aria", 0,
                new CharacterSkillProgress { id = "arcane_edge", level = 2 },
                new CharacterSkillProgress { id = "blade_tempo", level = 1 },
                new CharacterSkillProgress { id = "duelist_footwork", level = 3 });

            var go = new GameObject("SkillProgressTest");
            try
            {
                go.AddComponent<Health>().SetMax(100f);
                go.AddComponent<Tests.InvectorCutoverTestMotor>();
                go.AddComponent<Tests.InvectorCutoverTestAnimationDriver>();
                var ctrl = go.AddComponent<BrawlerController>();
                ctrl.attackDamage = 20f;
                ctrl.attackCooldown = 1f;
                ctrl.attackHitDelay = 0.5f;
                ctrl.moveSpeed = 5f;

                CharacterSkillBook.ApplyProgression(ctrl, new BrawlerDefinition { id = "aria" });

                Assert.AreEqual(22f, ctrl.attackDamage);
                Assert.That(ctrl.attackCooldown, Is.EqualTo(0.94f).Within(0.0001f));
                Assert.That(ctrl.attackHitDelay, Is.EqualTo(0.4865f).Within(0.0001f));
                Assert.That(ctrl.moveSpeed, Is.EqualTo(5.6f).Within(0.0001f));
            }
            finally
            {
                Object.DestroyImmediate(go);
            }
        }

        static void Seed(string characterId, int points, params CharacterSkillProgress[] skills)
        {
            var data = new ProgressData
            {
                coins = 1000,
                gems = 45,
                energy = 60,
                equippedCardMask = 7,
                characters = new List<CharacterProgress>
                {
                    new CharacterProgress
                    {
                        id = characterId,
                        level = 1,
                        points = points,
                        skills = new List<CharacterSkillProgress>(skills),
                    }
                }
            };
            Progress.UseEditorTestData(data);
        }
    }
}
