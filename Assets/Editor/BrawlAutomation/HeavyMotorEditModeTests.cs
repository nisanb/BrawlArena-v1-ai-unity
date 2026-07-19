using System;
using NUnit.Framework;
using UnityEngine;

namespace BrawlArena.EditorAutomation
{
    /// <summary>
    /// Contract tests for the souls-style heavy motor: initialization
    /// posture, corpse mode, aim-hold facing, external displacement
    /// ownership, teleport semantics, and the momentum/impulse model that
    /// distinguishes it from the retired instant-velocity motor.
    /// </summary>
    public class HeavyMotorEditModeTests
    {
        GameObject bodyObject;
        HeavyBrawlerMotor motor;

        [SetUp]
        public void SetUp()
        {
            bodyObject = new GameObject("HeavyMotorTestBody");
            bodyObject.AddComponent<Rigidbody>();
            bodyObject.AddComponent<CapsuleCollider>();
            motor = bodyObject.AddComponent<HeavyBrawlerMotor>();
        }

        [TearDown]
        public void TearDown()
        {
            if (bodyObject != null) UnityEngine.Object.DestroyImmediate(bodyObject);
        }

        [Test]
        public void InitializeEnforcesDynamicPostureAndRejectsSpeedChanges()
        {
            motor.Initialize(5f);

            var body = bodyObject.GetComponent<Rigidbody>();
            Assert.IsTrue(motor.IsInitialized);
            Assert.IsFalse(body.isKinematic);
            Assert.IsTrue(body.useGravity);
            Assert.AreEqual(RigidbodyConstraints.FreezeRotation, body.constraints);
            Assert.AreEqual(RigidbodyInterpolation.Interpolate, body.interpolation);

            Assert.DoesNotThrow(() => motor.Initialize(5f),
                "Re-initializing with the same speed is idempotent.");
            Assert.Throws<InvalidOperationException>(() => motor.Initialize(6f));
            Assert.Throws<ArgumentOutOfRangeException>(() => motor.Initialize(-1f));
        }

        [Test]
        public void CorpseModeParksTheBodyAndOnlySetCorpseModeFalseRestores()
        {
            motor.Initialize(5f);
            var body = bodyObject.GetComponent<Rigidbody>();
            var capsule = bodyObject.GetComponent<CapsuleCollider>();

            motor.SetCorpseMode(true);
            Assert.IsTrue(motor.IsCorpseMode);
            Assert.IsTrue(body.isKinematic);
            Assert.IsFalse(body.useGravity);
            Assert.IsFalse(capsule.enabled);

            // Teleport while a corpse must relocate the body without waking it.
            motor.Teleport(new Vector3(3f, 0f, 4f));
            Assert.IsTrue(motor.IsCorpseMode);
            Assert.IsTrue(body.isKinematic);
            Assert.AreEqual(new Vector3(3f, 0f, 4f), bodyObject.transform.position);

            motor.SetCorpseMode(false);
            Assert.IsFalse(motor.IsCorpseMode);
            Assert.IsFalse(body.isKinematic);
            Assert.IsTrue(body.useGravity);
            Assert.IsTrue(capsule.enabled);
        }

        [Test]
        public void HoldAimFacingSnapsNowAndWinsUntilAnImmediateFaceClearsIt()
        {
            motor.Initialize(5f);

            motor.HoldAimFacing(Vector3.right, 10f);
            Assert.IsTrue(motor.IsAimFacingHeld);
            Assert.That(Vector3.Angle(bodyObject.transform.forward, Vector3.right),
                Is.LessThan(0.5f), "The aim hold snap must be observable immediately.");

            motor.Face(Vector3.back, true);
            Assert.IsFalse(motor.IsAimFacingHeld,
                "An explicit instant snap always ends any aim hold.");
            Assert.That(Vector3.Angle(bodyObject.transform.forward, Vector3.back),
                Is.LessThan(0.5f));
        }

        [Test]
        public void ExternalDisplacementNestingQueuesOneFlushTickAfterEnd()
        {
            motor.Initialize(5f);

            motor.BeginExternalDisplacement();
            motor.BeginExternalDisplacement();
            Assert.AreEqual(2, motor.ExternalDisplacementDepth);

            motor.Displace(new Vector3(1f, 0f, 0f), true);
            motor.EndExternalDisplacement();
            Assert.AreEqual(1, motor.ExternalDisplacementDepth);
            Assert.IsFalse(motor.ExternalDisplacementEndPending);

            motor.EndExternalDisplacement();
            Assert.AreEqual(0, motor.ExternalDisplacementDepth);
            Assert.IsTrue(motor.ExternalDisplacementEndPending,
                "A queued delta at final End must hold one flush tick.");

            // A new owner re-absorbs the still-pending flush tick.
            motor.BeginExternalDisplacement();
            Assert.AreEqual(1, motor.ExternalDisplacementDepth);
            Assert.IsFalse(motor.ExternalDisplacementEndPending);
        }

        [Test]
        public void EndWithoutQueuedDeltaNeedsNoFlushTick()
        {
            motor.Initialize(5f);

            motor.BeginExternalDisplacement();
            motor.EndExternalDisplacement();

            Assert.AreEqual(0, motor.ExternalDisplacementDepth);
            Assert.IsFalse(motor.ExternalDisplacementEndPending);
        }

        [Test]
        public void TeleportIsSynchronousClearsSuspensionAndPreservesDisplacementOwnership()
        {
            motor.Initialize(5f);
            motor.Stop(true);
            Assert.IsTrue(motor.IsSuspended);

            motor.BeginExternalDisplacement();
            motor.Displace(Vector3.forward, true);
            motor.Teleport(new Vector3(-2f, 0f, 7f));

            Assert.AreEqual(new Vector3(-2f, 0f, 7f), bodyObject.transform.position);
            Assert.IsFalse(motor.IsSuspended);
            Assert.AreEqual(1, motor.ExternalDisplacementDepth,
                "Displacement nesting depth is preserved for the owning caller.");
            Assert.AreEqual(Vector3.zero, motor.PendingExternalDisplacement);
        }

        [Test]
        public void ConstraintQueriesPassOpenSpaceAndRejectNonFiniteInput()
        {
            motor.Initialize(5f);

            Assert.That(motor.ConstrainExternalDisplacement(Vector3.forward, 3f),
                Is.EqualTo(3f).Within(0.001f),
                "Open space must not shorten a displacement.");
            Assert.Throws<ArgumentException>(() => motor.ConstrainExternalDisplacement(
                new Vector3(float.NaN, 0f, 0f), 1f));
            Assert.Throws<ArgumentOutOfRangeException>(() =>
                motor.ConstrainExternalDisplacement(Vector3.forward, -1f));
            Assert.Throws<ArgumentException>(() => motor.SetPlanarIntent(
                new Vector3(float.PositiveInfinity, 0f, 0f), 1f, true));
            Assert.Throws<ArgumentOutOfRangeException>(() => motor.SetPlanarIntent(
                Vector3.forward, -2f, true));
        }

        [Test]
        public void ImpulseChannelScalesByWeightAndRefusesCorpses()
        {
            motor.ConfigureProfile(new HeavyMotorProfile
            {
                weight = 2f,
                acceleration = 20f,
                deceleration = 26f,
                turnRateDegreesPerSecond = 540f,
                impulseDamping = 5.5f,
            });
            motor.Initialize(5f);

            motor.AddImpulse(Vector3.forward * 4f);
            Assert.That(motor.ImpulseVelocity.z, Is.EqualTo(2f).Within(0.001f),
                "A heavier body takes less velocity from the same impulse.");

            motor.Stop(false);
            Assert.AreEqual(Vector3.zero, motor.ImpulseVelocity,
                "Stop clears all momentum.");

            motor.SetCorpseMode(true);
            motor.AddImpulse(Vector3.forward * 4f);
            Assert.AreEqual(Vector3.zero, motor.ImpulseVelocity,
                "A corpse can never be shoved through the impulse channel.");
        }

        [Test]
        public void ConfigureProfileRejectsNonPositiveTuning()
        {
            Assert.Throws<ArgumentNullException>(() => motor.ConfigureProfile(null));
            Assert.Throws<ArgumentException>(() => motor.ConfigureProfile(
                new HeavyMotorProfile { weight = 0f }));
            Assert.Throws<ArgumentException>(() => motor.ConfigureProfile(
                new HeavyMotorProfile { acceleration = -1f }));
        }

        [Test]
        public void PerHeroMotorProfilesEncodeTheWeightIdentity()
        {
            HeavyMotorProfile bastion = BrawlerCharacterAssembly.BuildHeavyMotorProfile("bastion");
            HeavyMotorProfile frost = BrawlerCharacterAssembly.BuildHeavyMotorProfile("frost");
            HeavyMotorProfile thorn = BrawlerCharacterAssembly.BuildHeavyMotorProfile("thorn");

            Assert.Greater(bastion.weight, frost.weight,
                "The vanguard must be the most ponderous hero.");
            Assert.Greater(frost.weight, thorn.weight,
                "The archer must be the nimblest hero.");
            Assert.Less(bastion.turnRateDegreesPerSecond, thorn.turnRateDegreesPerSecond);
            Assert.Less(bastion.acceleration, thorn.acceleration);
        }
    }
}
