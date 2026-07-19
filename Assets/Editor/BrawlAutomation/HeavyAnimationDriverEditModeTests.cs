using NUnit.Framework;
using UnityEditor.Animations;
using UnityEngine;
using UnityEngine.TestTools;

namespace BrawlArena.EditorAutomation
{
    /// <summary>
    /// Contract tests for the souls-style animation driver: authored impact
    /// timing, the death latch, idempotent respawn, full-body base-layer
    /// attack commitment, the melee lunge hand-off to the heavy motor, and
    /// loud failure accounting.
    /// </summary>
    public class HeavyAnimationDriverEditModeTests
    {
        GameObject actor;
        Animator animator;
        HeavyAnimationDriver driver;
        AnimatorController controller;

        [SetUp]
        public void SetUp()
        {
            actor = new GameObject("HeavyDriverTestActor");
            animator = actor.AddComponent<Animator>();
            controller = BuildContractController();
            animator.runtimeAnimatorController = controller;
            driver = actor.AddComponent<HeavyAnimationDriver>();
            animator.Update(0f);
        }

        [TearDown]
        public void TearDown()
        {
            if (actor != null) Object.DestroyImmediate(actor);
            if (controller != null) Object.DestroyImmediate(controller);
        }

        /// <summary>
        /// In-memory controller matching the generated contract: base layer
        /// with Locomotion/Die/Victory/VictoryMaintain/Dash/AttackPrimary/
        /// AttackSuper, masked upper layer with Empty/GetHit.
        /// </summary>
        static AnimatorController BuildContractController()
        {
            var built = new AnimatorController();
            built.AddParameter(HeavyHeroBuilder.SpeedParameter,
                AnimatorControllerParameterType.Float);
            built.AddParameter(HeavyHeroBuilder.MoveXParameter,
                AnimatorControllerParameterType.Float);
            built.AddParameter(HeavyHeroBuilder.MoveZParameter,
                AnimatorControllerParameterType.Float);
            built.AddParameter(HeavyHeroBuilder.AttackSpeedParameter,
                AnimatorControllerParameterType.Float);
            built.AddLayer(HeavyHeroBuilder.BaseLayerName);
            built.AddLayer(HeavyHeroBuilder.UpperBodyLayerName);

            AnimatorStateMachine baseMachine = built.layers[0].stateMachine;
            AnimatorState locomotion = baseMachine.AddState(
                HeavyHeroBuilder.LocomotionStateName);
            baseMachine.defaultState = locomotion;
            baseMachine.AddState(HeavyHeroBuilder.DieStateName);
            baseMachine.AddState(HeavyHeroBuilder.VictoryStateName);
            baseMachine.AddState(HeavyHeroBuilder.VictoryMaintainStateName);
            baseMachine.AddState(HeavyHeroBuilder.DashStateName);
            baseMachine.AddState(HeavyHeroBuilder.AttackPrimaryStateName);
            baseMachine.AddState(HeavyHeroBuilder.AttackSuperStateName);

            AnimatorStateMachine upperMachine = built.layers[1].stateMachine;
            AnimatorState empty = upperMachine.AddState(
                HeavyHeroBuilder.EmptyStateName);
            upperMachine.defaultState = empty;
            upperMachine.AddState(HeavyHeroBuilder.GetHitStateName);
            return built;
        }

        [Test]
        public void GetAttackImpactDelayReturnsAuthoredProfileTimings()
        {
            driver.Configure(new HeavyAnimationProfile
            {
                primaryImpactDelay = 0.34f,
                superImpactDelay = 0.5f,
            });

            Assert.That(driver.GetAttackImpactDelay(false, 0.1f),
                Is.EqualTo(0.34f).Within(0.0001f));
            Assert.That(driver.GetAttackImpactDelay(true, 0.1f),
                Is.EqualTo(0.5f).Within(0.0001f));

            driver.Configure(new HeavyAnimationProfile());
            Assert.That(driver.GetAttackImpactDelay(false, 0.27f),
                Is.EqualTo(0.27f).Within(0.0001f),
                "An unauthored delay must fall back unmodified.");
        }

        [Test]
        public void BasicAttackIsAFullBodyBaseLayerCommitment()
        {
            driver.PlayBasicAttack();
            animator.Update(0.3f);

            Assert.AreEqual(HeavyHeroBuilder.AttackPrimaryStateName,
                driver.CurrentBaseStateName,
                "The committed swing must interrupt base-layer locomotion.");
            Assert.AreEqual(0, driver.LifecycleFailureCount);
        }

        [Test]
        public void DeathThenRespawnAlwaysReachesLocomotionAndIsIdempotent()
        {
            driver.PlayDeath();
            Assert.IsTrue(driver.DeathPresented);

            driver.PlayRespawn();
            Assert.IsFalse(driver.DeathPresented);
            Assert.AreEqual(HeavyHeroBuilder.LocomotionStateName,
                driver.CurrentBaseStateName);

            Assert.DoesNotThrow(driver.PlayRespawn,
                "Respawn is idempotent.");
            Assert.AreEqual(HeavyHeroBuilder.LocomotionStateName,
                driver.CurrentBaseStateName);
            Assert.AreEqual(0, driver.LifecycleFailureCount);
        }

        [Test]
        public void DeathLatchRefusesEveryOverlayWithoutCountingFailures()
        {
            driver.PlayDeath();
            animator.Update(0.2f);

            driver.PlayBasicAttack();
            driver.PlaySuper();
            driver.PlayHitReaction();
            driver.PlayVictory();
            driver.PlayDash(Vector3.forward);
            animator.Update(0.2f);

            Assert.AreEqual(HeavyHeroBuilder.DieStateName, driver.CurrentBaseStateName,
                "No overlay may interrupt an in-flight death presentation.");
            Assert.AreEqual(0, driver.LifecycleFailureCount,
                "Latched refusals are correct behavior, not failures.");
        }

        [Test]
        public void MeleeLungeFeedsTheHeavyMotorImpulseChannel()
        {
            actor.AddComponent<Rigidbody>();
            actor.AddComponent<CapsuleCollider>();
            var motor = actor.AddComponent<HeavyBrawlerMotor>();
            motor.Initialize(5f);
            actor.transform.rotation = Quaternion.LookRotation(Vector3.right);
            driver.Configure(new HeavyAnimationProfile { lungeImpulse = 5.5f });

            driver.PlayBasicAttack();

            Assert.That(motor.ImpulseVelocity.x, Is.EqualTo(5.5f).Within(0.01f),
                "The swing must carry the body forward through the motor.");

            driver.Configure(new HeavyAnimationProfile { lungeImpulse = 0f });
            motor.Stop(false);
            driver.PlayBasicAttack();
            Assert.AreEqual(Vector3.zero, motor.ImpulseVelocity,
                "A zero-lunge kit stays planted.");
        }

        [Test]
        public void MissingAnimatorFailsLoudOncePerKindAndKeepsCounting()
        {
            var bare = new GameObject("DriverWithoutAnimator");
            try
            {
                var bareDriver = bare.AddComponent<HeavyAnimationDriver>();
                LogAssert.Expect(LogType.Error,
                    new System.Text.RegularExpressions.Regex(
                        "PlayBasicAttack.AnimatorUnavailable"));

                bareDriver.PlayBasicAttack();
                bareDriver.PlayBasicAttack();

                Assert.AreEqual(2, bareDriver.LifecycleFailureCount,
                    "Every failure counts even though only the first logs.");
            }
            finally
            {
                Object.DestroyImmediate(bare);
            }
        }

        [Test]
        public void PerHeroAnimationProfilesEncodeTheSoulsCadence()
        {
            Assert.Less(
                BrawlerCharacterAssembly.ResolveHeavyAttackStateSpeed("bastion"), 1f,
                "Bastion's swing plays slower than authored: the greatsword read.");
            Assert.Greater(
                BrawlerCharacterAssembly.ResolveHeavyLungeImpulse("bastion"), 0f,
                "The melee kit must step into its swing.");
            Assert.AreEqual(0f,
                BrawlerCharacterAssembly.ResolveHeavyLungeImpulse("frost"),
                "Caster shots stay planted.");
            Assert.AreEqual(0f,
                BrawlerCharacterAssembly.ResolveHeavyLungeImpulse("thorn"),
                "Archer shots stay planted.");
        }
    }
}
