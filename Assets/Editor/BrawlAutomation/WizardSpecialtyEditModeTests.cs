using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;

namespace BrawlArena.EditorAutomation
{
    public class WizardSpecialtyEditModeTests
    {
        [TestCase("arcane", SpellSchool.Arcane, "SANCTUARY NOVA")]
        [TestCase("fire", SpellSchool.Fire, "INFERNO COMET")]
        [TestCase("frost", SpellSchool.Frost, "ABSOLUTE ZERO")]
        [TestCase("storm", SpellSchool.Storm, "TEMPEST CHAIN")]
        [TestCase("earth", SpellSchool.Earth, "TECTONIC WAVE")]
        [TestCase("void", SpellSchool.Poison, "TOXIC BLOOM")]
        public void WizardIdsResolveSchoolAndSuper(string id, SpellSchool expectedSchool,
            string expectedSuper)
        {
            var definition = new BrawlerDefinition { id = id };

            definition.EnsureSuperConfiguration();

            Assert.AreEqual(expectedSchool, definition.specialty.school);
            Assert.AreEqual(expectedSuper, definition.superName);
            Assert.Greater(definition.superDamageMultiplier, 1f);
        }

        [Test]
        public void LegacySerializedWizardPayloadsReceiveNewRpgRoles()
        {
            var healer = new BrawlerDefinition
            {
                id = "arcane",
                role = "Arcane Savant",
                specialty = SpellSpecialty.ForSchool(SpellSchool.Arcane),
                superName = "ARCANE OVERLOAD",
            };
            var poison = new BrawlerDefinition
            {
                id = "void",
                displayName = "Nyx",
                role = "Voidweaver",
                specialty = SpellSpecialty.ForSchool(SpellSchool.Void),
                superName = "EVENT HORIZON",
            };

            healer.EnsureSuperConfiguration();
            poison.EnsureSuperConfiguration();

            Assert.AreEqual("Lifeweaver", healer.role);
            Assert.Greater(healer.specialty.allyHealFraction, 0f);
            Assert.Greater(healer.specialty.ritualHealFraction, 0f);
            Assert.AreEqual("Mire", poison.displayName);
            Assert.AreEqual("Plagueweaver", poison.role);
            Assert.AreEqual(SpellSchool.Poison, poison.specialty.school);
            Assert.Greater(poison.specialty.poisonDamageFraction, 0f);
        }

        [Test]
        public void AuthoredSpecialtyPayloadIsBounded()
        {
            var specialty = new SpellSpecialty
            {
                school = SpellSchool.Storm,
                burnDamageFraction = 4f,
                burnDuration = 20f,
                burnTickInterval = 0.01f,
                slowMultiplier = 0.01f,
                slowDuration = 20f,
                chainTargets = 99,
                chainRange = 99f,
                chainDamageMultiplier = 5f,
                knockback = 20f,
                sustainFraction = 2f,
                voidPullDistance = 20f,
                poisonDamageFraction = 5f,
                poisonDuration = 20f,
                poisonTickInterval = 0.01f,
                groundEffectRadius = 20f,
                groundEffectDuration = 20f,
                groundBurnFraction = 5f,
                allyHealFraction = 5f,
                allyHealRadius = 50f,
                ritualHealFraction = 5f,
            }.Sanitized();

            Assert.That(specialty.burnDamageFraction, Is.InRange(0f, 1f));
            Assert.That(specialty.burnDuration, Is.InRange(0f, 6f));
            Assert.That(specialty.burnTickInterval, Is.InRange(0.2f, 2f));
            Assert.That(specialty.slowMultiplier, Is.InRange(0.25f, 1f));
            Assert.That(specialty.slowDuration, Is.InRange(0f, 4f));
            Assert.That(specialty.chainTargets, Is.InRange(0, 3));
            Assert.That(specialty.chainRange, Is.InRange(0f, 7f));
            Assert.That(specialty.chainDamageMultiplier, Is.InRange(0f, 1f));
            Assert.That(specialty.knockback, Is.InRange(0f, 4f));
            Assert.That(specialty.sustainFraction, Is.InRange(0f, 0.5f));
            Assert.That(specialty.voidPullDistance, Is.InRange(0f, 3f));
            Assert.That(specialty.poisonDamageFraction, Is.InRange(0f, 1f));
            Assert.That(specialty.poisonDuration, Is.InRange(0f, 8f));
            Assert.That(specialty.poisonTickInterval, Is.InRange(0.2f, 2f));
            Assert.That(specialty.groundEffectRadius, Is.InRange(0f, 5f));
            Assert.That(specialty.groundEffectDuration, Is.InRange(0f, 8f));
            Assert.That(specialty.groundBurnFraction, Is.InRange(0f, 1f));
            Assert.That(specialty.allyHealFraction, Is.InRange(0f, 2f));
            Assert.That(specialty.allyHealRadius, Is.InRange(0f, 12f));
            Assert.That(specialty.ritualHealFraction, Is.InRange(0f, 1f));
        }

        [Test]
        public void WizardSkillIdsAreDistinctAndSchoolSpecific()
        {
            string[] schools = { "arcane", "fire", "frost", "storm", "earth", "void" };
            var ids = new HashSet<string>();

            for (int i = 0; i < schools.Length; i++)
            {
                CharacterSkillDefinition[] skills = CharacterSkillBook.For(
                    new BrawlerDefinition { id = schools[i] });
                Assert.AreEqual(3, skills.Length);
                for (int j = 0; j < skills.Length; j++)
                    Assert.IsTrue(ids.Add(skills[j].id), "Duplicate wizard skill id: " + skills[j].id);
            }
        }

        [Test]
        public void SlowRejectsFriendlyTargetsAndArcaneSustainHealsCaster()
        {
            GameObject sourceGo = CreateWizard("Source", TeamId.Blue, out BrawlerController source);
            GameObject enemyGo = CreateWizard("Enemy", TeamId.Red, out BrawlerController enemy);
            GameObject allyGo = CreateWizard("Ally", TeamId.Blue, out BrawlerController ally);
            try
            {
                enemy.ApplySpellSlow(source, 0.5f, 2f);
                ally.ApplySpellSlow(source, 0.5f, 2f);
                Assert.AreEqual(0.5f, enemy.SpecialtyMoveMultiplier);
                Assert.AreEqual(1f, ally.SpecialtyMoveMultiplier);

                source.specialty = SpellSpecialty.ForSchool(SpellSchool.Arcane);
                source.Health.TakeDamage(20f, null);
                source.ApplySpellSpecialtyHit(enemy, 10f, enemy.CombatAimPoint);
                Assert.That(source.Health.Current, Is.EqualTo(80.8f).Within(0.001f));
            }
            finally
            {
                Object.DestroyImmediate(sourceGo);
                Object.DestroyImmediate(enemyGo);
                Object.DestroyImmediate(allyGo);
            }
        }

        static GameObject CreateWizard(string name, TeamId team, out BrawlerController controller)
        {
            var go = new GameObject(name);
            Health health = go.AddComponent<Health>();
            go.AddComponent<Tests.InvectorCutoverTestMotor>();
            Tests.InvectorCutoverTestAnimationDriver animation =
                go.AddComponent<Tests.InvectorCutoverTestAnimationDriver>();
            controller = go.AddComponent<BrawlerController>();
            controller.SetAnimationDriver(animation);
            controller.team = team;
            // Non-ExecuteInEditMode gameplay components do not receive Awake
            // automatically in EditMode tests. Invoke each component directly:
            // GameObject.SendMessage asserts ShouldRunBehaviour in EditMode.
            InvokeAwake(health);
            InvokeAwake(controller);
            return go;
        }

        static void InvokeAwake(object target)
        {
            MethodInfo awake = target.GetType().GetMethod("Awake",
                BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.NotNull(awake, target.GetType().Name + ".Awake");
            awake.Invoke(target, null);
        }
    }
}
