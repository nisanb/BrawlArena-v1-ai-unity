using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using Invector.vCharacterController;
using NUnit.Framework;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace BrawlArena.EditorAutomation.Tests
{
    public sealed class InvectorBrawlerMotorEditModeTests
    {
        const BindingFlags InstanceMembers =
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

        readonly List<GameObject> objects = new List<GameObject>();
        readonly List<Scene> previewScenes = new List<Scene>();

        [TearDown]
        public void TearDown()
        {
            for (int i = objects.Count - 1; i >= 0; i--)
            {
                if (objects[i] != null) Object.DestroyImmediate(objects[i]);
            }
            objects.Clear();

            for (int i = previewScenes.Count - 1; i >= 0; i--)
            {
                if (previewScenes[i].IsValid() && previewScenes[i].isLoaded)
                    EditorSceneManager.ClosePreviewScene(previewScenes[i]);
            }
            previewScenes.Clear();
        }

        [Test]
        public void ConcreteMotorImplementsPhysicalContractWithoutAnotherScheduler()
        {
            Assert.IsTrue(typeof(IBrawlerMotor).IsAssignableFrom(
                typeof(InvectorBrawlerMotor)));
            Assert.IsNotNull(typeof(InvectorBrawlerMotor).GetCustomAttribute<
                DisallowMultipleComponent>());

            string[] forbiddenMessages =
            {
                "Update", "FixedUpdate", "LateUpdate", "OnAnimatorMove",
            };
            foreach (string message in forbiddenMessages)
            {
                Assert.IsNull(typeof(InvectorBrawlerMotor).GetMethod(
                    message, InstanceMembers | BindingFlags.DeclaredOnly),
                    message + " would create a second scheduler or writer.");
            }
        }

        [Test]
        public void DormantConfigurationBindsExactRootWithoutActivatingAuthority()
        {
            Fixture fixture = CreateFixture("DormantInvectorMotor");
            Vector3 position = fixture.Root.transform.position;
            Quaternion rotation = fixture.Root.transform.rotation;

            Assert.IsTrue(fixture.Motor.IsDormantConfigured);
            Assert.AreEqual(Vector3.zero, fixture.Motor.Velocity);
            Vector3 capsuleScale = fixture.Capsule.transform.lossyScale;
            float expectedRadius = Mathf.Max(
                0.35f,
                fixture.Capsule.radius * Mathf.Max(
                    Mathf.Abs(capsuleScale.x), Mathf.Abs(capsuleScale.z)));
            Assert.AreEqual(expectedRadius, fixture.Motor.CollisionRadius, 0.0001f);
            Assert.IsFalse(fixture.Motor.IsGrounded);
            Assert.AreEqual(position, fixture.Root.transform.position);
            Assert.AreEqual(rotation, fixture.Root.transform.rotation);
            AssertSerializedReference(
                fixture.Motor, "configuredController", fixture.Controller);
            AssertSerializedReference(
                fixture.Motor, "configuredScheduler", fixture.Scheduler);
            AssertSerializedReference(fixture.Motor, "configuredBody", fixture.Body);
            AssertSerializedReference(
                fixture.Motor, "configuredCapsule", fixture.Capsule);

            fixture.Controller.isStrafing = true;
            fixture.Controller.isSprinting = false;
            fixture.Controller.OnCrouch = new UnityEngine.Events.UnityEvent();
            fixture.Controller.OnStandUp = new UnityEngine.Events.UnityEvent();
            fixture.Controller.isCrouching = true;
            fixture.Motor.Initialize(4f);
            Assert.IsTrue(fixture.Motor.IsInitialized);
            Assert.Throws<InvalidOperationException>(() =>
                fixture.Motor.ResetRuntimeTrace());
            Assert.IsFalse(fixture.Controller.isStrafing);
            Assert.IsFalse(fixture.Controller.isSprinting);
            Assert.IsFalse(fixture.Controller.isCrouching);
            InvokeNonPublic(fixture.Motor, "ReturnDormant");
            Assert.IsFalse(fixture.Motor.IsInitialized);
            Assert.DoesNotThrow(() => fixture.Motor.ResetRuntimeTrace());
            Assert.IsTrue(fixture.Controller.isStrafing);
            Assert.IsFalse(fixture.Controller.isSprinting);
            Assert.IsTrue(fixture.Controller.isCrouching);

            fixture.Controller.isStrafing = false;
            fixture.Controller.isSprinting = true;
            fixture.Controller.isCrouching = false;
            fixture.Motor.Initialize(4f);
            Assert.IsFalse(fixture.Controller.isSprinting);
            InvokeNonPublic(fixture.Motor, "ReturnDormant");
            Assert.IsFalse(fixture.Controller.isStrafing);
            Assert.IsTrue(fixture.Controller.isSprinting);
            Assert.IsFalse(fixture.Controller.isCrouching);
            Assert.IsTrue(fixture.Motor.IsDormantConfigured);
        }

        [Test]
        public void ConfigurationRejectsCrossRootAndCompetingPhysicalAuthorities()
        {
            Fixture fixture = CreateFixture("AuthorityRoot");
            Fixture other = CreateFixture("OtherRoot");

            Assert.Throws<ArgumentException>(() => fixture.Motor.ConfigureDormant(
                fixture.Controller, other.Scheduler, fixture.Body, fixture.Capsule));

            InvectorCutoverTestMotor competing =
                fixture.Root.AddComponent<InvectorCutoverTestMotor>();
            Assert.IsNotNull(competing);
            Assert.Throws<InvalidOperationException>(() => fixture.Motor.ConfigureDormant(
                fixture.Controller, fixture.Scheduler, fixture.Body, fixture.Capsule));
            Object.DestroyImmediate(competing);

            CharacterController characterController =
                fixture.Root.AddComponent<CharacterController>();
            Assert.IsNotNull(characterController);
            Assert.Throws<InvalidOperationException>(() => fixture.Motor.ConfigureDormant(
                fixture.Controller, fixture.Scheduler, fixture.Body, fixture.Capsule));
        }

        [TestCase(true, false)]
        [TestCase(false, true)]
        public void InitializeRejectsMissingMovementProfilesWithoutOpeningMotor(
            bool removeFreeProfile, bool removeStrafeProfile)
        {
            Fixture fixture = CreateFixture("MissingMovementProfiles");
            if (removeFreeProfile) fixture.Controller.freeSpeed = null;
            if (removeStrafeProfile) fixture.Controller.strafeSpeed = null;

            InvalidOperationException exception = Assert.Throws<InvalidOperationException>(
                () => fixture.Motor.Initialize(4f));

            StringAssert.Contains("free and strafe movement profiles", exception.Message);
            Assert.IsFalse(fixture.Motor.IsInitialized);
        }

        [Test]
        public void FacadeCanSelectInitializeAndLockTheSameRootInvectorMotor()
        {
            Fixture fixture = CreateFixture("FacadeInvectorMotor");
            fixture.Root.AddComponent<Health>().SetMax(100f);
            BrawlerController brawler = fixture.Root.AddComponent<BrawlerController>();
            brawler.moveSpeed = 6.25f;

            brawler.SetMotor(fixture.Motor);
            InvokeNonPublic(brawler, "InitializeMotor");

            Assert.AreSame(fixture.Motor, brawler.Motor);
            Assert.AreEqual(6.25f,
                GetField<float>(fixture.Motor, "initializedMoveSpeed"), 0.0001f);
            Assert.Throws<InvalidOperationException>(() =>
                brawler.SetMotor(fixture.Motor));
        }

        [Test]
        public void AdapterDefaultsToLabFeedAndSelectsReciprocalMotorOnlyWhileDormant()
        {
            Fixture fixture = CreateFixture("BufferedAdapterBridge");
            Fixture other = CreateFixture("OtherBufferedAdapterBridge");

            Assert.AreEqual(InvectorMovementFeedMode.LabProjectAction,
                fixture.Scheduler.MovementFeedMode);
            Assert.IsFalse(fixture.Scheduler.HasConfiguredMotorBridge);

            fixture.Scheduler.ConfigureMotorBridge(fixture.Motor);

            Assert.AreEqual(InvectorMovementFeedMode.LabProjectAction,
                fixture.Scheduler.MovementFeedMode);
            Assert.IsTrue(fixture.Scheduler.HasConfiguredMotorBridge);
            AssertSerializedReference(
                fixture.Scheduler, "configuredMotor", fixture.Motor);
            fixture.Scheduler.SelectMovementFeedMode(
                InvectorMovementFeedMode.BufferedMotor);
            Assert.AreEqual(InvectorMovementFeedMode.BufferedMotor,
                fixture.Scheduler.MovementFeedMode);
            Assert.Throws<InvalidOperationException>(() =>
                other.Scheduler.SelectMovementFeedMode(
                    InvectorMovementFeedMode.BufferedMotor));
            Assert.Throws<ArgumentException>(() =>
                other.Scheduler.ConfigureMotorBridge(fixture.Motor));

            fixture.Scheduler.enabled = true;
            Assert.Throws<InvalidOperationException>(() =>
                fixture.Scheduler.ConfigureMotorBridge(fixture.Motor));
            Assert.Throws<InvalidOperationException>(() =>
                fixture.Scheduler.SelectMovementFeedMode(
                    InvectorMovementFeedMode.BufferedMotor));
            fixture.Scheduler.enabled = false;
        }

        [Test]
        public void SchedulerBridgeRemainsReadyDuringItsOwnedOpenStep()
        {
            Fixture fixture = CreateLiveFixture("OpenScheduledStepReadiness", 5f);
            PropertyInfo readiness = typeof(InvectorBrawlerMotor).GetProperty(
                "IsReadyForSchedulerBridge",
                BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.IsNotNull(readiness);

            Assert.IsTrue((bool)readiness.GetValue(fixture.Motor));
            InvokeNonPublic(fixture.Motor, "PrepareScheduledStep");
            Assert.IsTrue((bool)readiness.GetValue(fixture.Motor),
                "Vendor FixedUpdate re-enters adapter readiness through ControlRotation while the owned step is open.");
            InvokeNonPublic(fixture.Motor, "CompleteScheduledStep");
            Assert.IsTrue((bool)readiness.GetValue(fixture.Motor));
        }

        [Test]
        public void WorldIntentIsBufferedNormalizedAndAppliedOnlyAtScheduledBoundary()
        {
            Fixture fixture = CreateLiveFixture("BufferedWorldIntent", 5f);
            Vector3 position = fixture.Body.position;

            fixture.Motor.SetPlanarIntent(
                new Vector3(0.6f, 20f, 0.8f), 2.5f, true);

            Assert.AreEqual(new Vector3(0.6f, 0f, 0.8f),
                fixture.Motor.BufferedWorldIntent);
            Assert.AreEqual(Vector3.zero, fixture.Controller.input);
            Assert.AreEqual(position, fixture.Body.position);

            PrepareAndComplete(fixture.Motor, () =>
            {
                AssertVectorApproximately(
                    new Vector3(0.3f, 0f, 0.4f), fixture.Controller.input);
                Assert.AreEqual(vThirdPersonMotor.LocomotionType.OnlyFree,
                    fixture.Controller.locomotionType);
                Assert.IsTrue(fixture.Controller.moveToDirectionInFree);
                Assert.IsTrue(fixture.Controller.rotateByWorld);
                Assert.IsTrue(fixture.Controller.lockSetMoveSpeed);
                Assert.AreEqual(5f, fixture.Controller.moveSpeed, 0.0001f);
                Assert.That(fixture.Controller.inputMagnitude,
                    Is.EqualTo(0.5f).Within(0.0001f),
                    "Locked physical speed must still feed the Invector locomotion animation scalars.");
            });

            fixture.Motor.SetPlanarIntent(Vector3.right, 99f, false);
            PrepareAndComplete(fixture.Motor, () =>
                Assert.AreEqual(Vector3.zero, fixture.Controller.input));
            Assert.AreEqual(2, fixture.Motor.ScheduledPrepareCount);
            Assert.AreEqual(2, fixture.Motor.ScheduledCompleteCount);
        }

        [Test]
        public void NestedDisplacementLocksConsumesOnceAndRestoresExactlyOnce()
        {
            Fixture fixture = CreateLiveFixture("NestedDisplacement", 5f);
            fixture.Controller.lockMovement = false;
            fixture.Controller.lockRotation = false;
            Vector3 start = fixture.Body.position;

            fixture.Motor.BeginExternalDisplacement();
            fixture.Motor.BeginExternalDisplacement();
            fixture.Motor.Displace(new Vector3(1f, 4f, 2f), true);

            Assert.AreEqual(2, fixture.Motor.ExternalDisplacementDepth);
            Assert.IsTrue(fixture.Controller.lockMovement);
            Assert.IsTrue(fixture.Controller.lockRotation);
            Assert.AreEqual(new Vector3(1f, 0f, 2f),
                fixture.Motor.PendingExternalDisplacement);

            PrepareAndComplete(fixture.Motor, null);
            Vector3 afterFirstStep = fixture.Body.position;
            AssertVectorApproximately(start + new Vector3(1f, 0f, 2f), afterFirstStep);
            Assert.AreEqual(Vector3.zero, fixture.Motor.PendingExternalDisplacement);
            Assert.AreEqual(1, fixture.Motor.AppliedDisplacementCount);

            PrepareAndComplete(fixture.Motor, null);
            AssertVectorApproximately(afterFirstStep, fixture.Body.position);
            Assert.AreEqual(1, fixture.Motor.AppliedDisplacementCount,
                "A buffered displacement must be consumed by one scheduled step only.");

            fixture.Motor.EndExternalDisplacement();
            Assert.AreEqual(1, fixture.Motor.ExternalDisplacementDepth);
            Assert.IsTrue(fixture.Controller.lockMovement);
            Assert.IsTrue(fixture.Controller.lockRotation);

            fixture.Motor.EndExternalDisplacement();
            Assert.AreEqual(0, fixture.Motor.ExternalDisplacementDepth);
            Assert.IsFalse(fixture.Controller.lockMovement);
            Assert.IsFalse(fixture.Controller.lockRotation);
            fixture.Motor.EndExternalDisplacement();
            Assert.IsFalse(fixture.Controller.lockMovement);
            Assert.IsFalse(fixture.Controller.lockRotation);

            fixture.Motor.SetPlanarIntent(Vector3.right, 5f, true);
            Vector3 finalDeltaStart = fixture.Body.position;
            fixture.Motor.BeginExternalDisplacement();
            fixture.Motor.Displace(Vector3.forward * 0.75f, true);
            fixture.Motor.EndExternalDisplacement();

            Assert.AreEqual(0, fixture.Motor.ExternalDisplacementDepth);
            Assert.IsTrue(fixture.Motor.ExternalDisplacementEndPending);
            Assert.IsTrue(fixture.Controller.lockMovement);
            Assert.IsTrue(fixture.Controller.lockRotation);

            PrepareAndComplete(fixture.Motor, () =>
            {
                AssertVectorApproximately(
                    finalDeltaStart + Vector3.forward * 0.75f,
                    fixture.Body.position);
                Assert.AreEqual(Vector3.zero, fixture.Controller.input,
                    "Ordinary intent must stay suppressed while the final delta is consumed.");
                Assert.IsTrue(fixture.Controller.lockMovement);
                Assert.IsTrue(fixture.Controller.lockRotation);
            });

            Assert.IsFalse(fixture.Motor.ExternalDisplacementEndPending);
            Assert.IsFalse(fixture.Controller.lockMovement);
            Assert.IsFalse(fixture.Controller.lockRotation);
            PrepareAndComplete(fixture.Motor, () =>
                Assert.AreEqual(Vector3.right, fixture.Controller.input,
                    "Ordinary intent may resume only on the following scheduled step."));
        }

        [Test]
        public void SweepConstraintStopsBufferedDisplacementBeforeObstacle()
        {
            Fixture fixture = CreateLiveFixture("SweptDisplacement", 5f);
            fixture.Capsule.radius = 0.5f;
            fixture.Capsule.height = 2f;

            GameObject wall = GameObject.CreatePrimitive(PrimitiveType.Cube);
            wall.name = "SweepWall";
            wall.transform.position = new Vector3(2f, 0f, 0f);
            wall.transform.localScale = new Vector3(0.2f, 3f, 3f);
            SceneManager.MoveGameObjectToScene(wall, fixture.Scene);
            objects.Add(wall);
            Physics.SyncTransforms();

            float allowed = fixture.Motor.ConstrainExternalDisplacement(
                Vector3.right, 3f);

            Assert.That(allowed, Is.GreaterThanOrEqualTo(0f));
            Assert.That(allowed, Is.LessThan(3f));
        }

        [Test]
        public void TeleportConstraintFailsClosedWhenNoNavMeshPointExists()
        {
            Fixture fixture = CreateLiveFixture("OffMeshTeleportConstraint", 5f);
            Vector3 current = fixture.Body.position;

            Vector3 constrained = fixture.Motor.ConstrainTeleportDestination(
                new Vector3(100000f, 100000f, 100000f), 0.1f);

            AssertVectorApproximately(current, constrained);
        }

        [Test]
        public void StopStaysDynamicAndTeleportPreservesOwnedDisplacement()
        {
            Fixture fixture = CreateLiveFixture("StopTeleport", 5f);
            RigidbodyConstraints runtimeConstraints = fixture.Body.constraints;
            Quaternion initialRotation = fixture.Body.rotation;

            fixture.Controller.lockRotation = true;
            fixture.Motor.Face(Vector3.right, false);
            PrepareAndComplete(fixture.Motor, () =>
                Assert.IsTrue(fixture.Controller.lockRotation));
            Assert.IsTrue(fixture.Controller.lockRotation,
                "Deferred facing must restore a pre-existing rotation lock.");
            Assert.That(Quaternion.Angle(initialRotation, fixture.Body.rotation),
                Is.GreaterThan(0.1f));
            fixture.Controller.lockRotation = false;

            fixture.Motor.Face(Vector3.back, true);
            Assert.That(Quaternion.Angle(
                Quaternion.LookRotation(Vector3.back), fixture.Body.rotation),
                Is.LessThan(0.01f),
                "Immediate facing must update a live body synchronously.");
            initialRotation = fixture.Body.rotation;

            fixture.Motor.Face(Vector3.right, false);
            fixture.Motor.Stop(true);
            Assert.IsTrue(fixture.Motor.IsSuspended);
            Assert.IsFalse(fixture.Body.isKinematic);
            Assert.IsTrue(fixture.Capsule.enabled);
            Assert.AreEqual(runtimeConstraints, fixture.Body.constraints);
            PrepareAndComplete(fixture.Motor, null);
            Assert.That(Quaternion.Angle(initialRotation, fixture.Body.rotation),
                Is.LessThan(0.01f),
                "Stop must clear a facing request queued before suspension.");

            fixture.Motor.BeginExternalDisplacement();
            Vector3 destination = new Vector3(4f, 1f, -3f);
            fixture.Motor.Face(Vector3.left, false);
            fixture.Motor.Teleport(destination);

            Assert.IsFalse(fixture.Motor.IsSuspended);
            Assert.AreEqual(1, fixture.Motor.ExternalDisplacementDepth,
                "Teleport must not consume displacement ownership.");
            AssertVectorApproximately(destination, fixture.Body.position);
            Assert.AreEqual(Vector3.zero, fixture.Motor.Velocity);
            Assert.IsTrue(fixture.Controller.lockMovement);
            Assert.IsTrue(fixture.Controller.lockRotation);
            PrepareAndComplete(fixture.Motor, null);
            Assert.That(Quaternion.Angle(initialRotation, fixture.Body.rotation),
                Is.LessThan(0.01f),
                "Teleport must clear a facing request queued for the old position.");

            fixture.Motor.EndExternalDisplacement();
            Assert.AreEqual(0, fixture.Motor.ExternalDisplacementDepth);
            Assert.IsFalse(fixture.Controller.lockMovement);
            Assert.IsFalse(fixture.Controller.lockRotation);

            fixture.Motor.Stop(false);
            Assert.IsFalse(fixture.Motor.IsSuspended);
            Assert.IsFalse(fixture.Body.isKinematic);
            Assert.IsTrue(fixture.Capsule.enabled);

            fixture.Body.linearVelocity = new Vector3(2f, 3f, 4f);
            fixture.Motor.enabled = false;
            InvokeNonPublic(fixture.Motor, "OnDisable");
            Assert.IsFalse(fixture.Scheduler.RuntimeSchedulingEnabled,
                "Disabling the selected motor must fail the scheduler closed.");
            Assert.IsFalse(fixture.Scheduler.enabled);
            Assert.AreEqual(Vector3.zero, fixture.Body.linearVelocity);
        }

        [Test]
        public void ProductionMotorSourceContainsNoPhysicalInputReader()
        {
            string source = ReadProjectFile(
                "Assets/Scripts/Brawl/Integration/Invector/InvectorBrawlerMotor.cs");
            string[] forbiddenReaders =
            {
                "using UnityEngine.InputSystem",
                "InputAction",
                "BrawlHUD",
                "Joystick",
                "Keyboard.current",
                "Gamepad.current",
                "UnityEngine.Input",
                "ReadValue<",
                "GetAxis(",
                "GetButton(",
            };
            foreach (string reader in forbiddenReaders)
                StringAssert.DoesNotContain(reader, source);

            StringAssert.Contains("class InvectorBrawlerMotor : MonoBehaviour, IBrawlerMotor", source);
            StringAssert.Contains("public void SetPlanarIntent(", source);
            StringAssert.Contains("internal void PrepareScheduledStep()", source);
            StringAssert.Contains("internal void CompleteScheduledStep()", source);
            StringAssert.Contains("configuredBody.SweepTest(", source);
            StringAssert.Contains("configuredController.rotateByWorld = true;", source);
            StringAssert.Contains("configuredController.moveToDirectionInFree = true;", source);
            StringAssert.DoesNotContain("void FixedUpdate(", source);
            StringAssert.DoesNotContain("void Update(", source);
            StringAssert.DoesNotContain("void LateUpdate(", source);
        }

        [Test]
        public void BufferedAdapterSourceSkipsPhysicalReadersAndBracketsInheritedScheduler()
        {
            string source = ReadProjectFile(
                "Assets/Scripts/Brawl/Integration/Invector/InvectorShooterMeleeInputAdapter.cs");
            string update = ExtractMethodBody(
                source, "protected override void Update()");
            string moveInput = ExtractMethodBody(
                source, "public override void MoveInput()");
            string controlRotation = ExtractMethodBody(
                source, "public override void ControlRotation()");
            string fixedUpdate = ExtractMethodBody(
                source, "protected override void FixedUpdate()");

            AssertBefore(update,
                "movementFeedMode == InvectorMovementFeedMode.BufferedMotor",
                "inputUpdateCount++");
            StringAssert.DoesNotContain("BrawlHUD", update);
            StringAssert.DoesNotContain("InputAction", update);

            AssertBefore(moveInput,
                "movementFeedMode == InvectorMovementFeedMode.BufferedMotor",
                "BrawlHUD hud = BrawlHUD.Instance;");
            AssertBefore(moveInput,
                "movementFeedMode == InvectorMovementFeedMode.BufferedMotor",
                "action.ReadValue<Vector2>()");

            AssertBefore(controlRotation,
                "movementFeedMode == InvectorMovementFeedMode.BufferedMotor",
                "Transform reference = movementReference.transform;");
            AssertBefore(controlRotation,
                "cc.rotateTarget = null;", "cc.SetInputDirection(cc.input);");
            AssertBefore(controlRotation,
                "cc.SetInputDirection(cc.input);", "cc.ControlRotationType();");
            StringAssert.Contains("cc.ControlRotationType();", controlRotation);

            AssertBefore(fixedUpdate,
                "try", "configuredMotor.PrepareScheduledStep();");
            AssertBefore(fixedUpdate,
                "configuredMotor.PrepareScheduledStep();", "base.FixedUpdate();");
            AssertBefore(fixedUpdate, "base.FixedUpdate();", "finally");
            AssertBefore(fixedUpdate,
                "finally", "configuredMotor.CompleteScheduledStep();");
            AssertBefore(fixedUpdate,
                "catch (Exception exception)", "FailClosed(");
        }

        Fixture CreateFixture(string name)
        {
            Scene previewScene = EditorSceneManager.NewPreviewScene();
            previewScenes.Add(previewScene);
            var root = new GameObject(name);
            root.SetActive(false);
            SceneManager.MoveGameObjectToScene(root, previewScene);
            objects.Add(root);

            Animator animator = root.AddComponent<Animator>();
            animator.enabled = false;
            Rigidbody body = root.AddComponent<Rigidbody>();
            CapsuleCollider capsule = root.AddComponent<CapsuleCollider>();
            var controller = root.AddComponent<BrawlInvectorThirdPersonController>();
            var scheduler = root.AddComponent<InvectorShooterMeleeInputAdapter>();
            var motor = root.AddComponent<InvectorBrawlerMotor>();

            // AddComponent has no serialized template data. Supply the two
            // vendor profiles that every generated/live controller owns so
            // core scheduler tests exercise a realistic controller contract.
            controller.freeSpeed = new vThirdPersonMotor.vMovementSpeed();
            controller.strafeSpeed = new vThirdPersonMotor.vMovementSpeed();
            controller.freeSpeed.Init();
            controller.strafeSpeed.Init();

            body.isKinematic = true;
            body.useGravity = false;
            body.constraints = RigidbodyConstraints.FreezeAll;
            body.interpolation = RigidbodyInterpolation.None;
            body.collisionDetectionMode = CollisionDetectionMode.Discrete;
            capsule.enabled = false;
            controller.enabled = false;
            scheduler.enabled = false;
            motor.ConfigureDormant(controller, scheduler, body, capsule);

            return new Fixture(
                previewScene, root, controller, scheduler, motor, body, capsule);
        }

        Fixture CreateLiveFixture(string name, float moveSpeed)
        {
            Fixture fixture = CreateFixture(name);
            fixture.Root.SetActive(true);
            fixture.Body.constraints =
                RigidbodyConstraints.FreezeRotationX |
                RigidbodyConstraints.FreezeRotationZ;
            fixture.Body.isKinematic = false;
            fixture.Body.useGravity = true;
            fixture.Capsule.enabled = true;
            fixture.Controller.enabled = true;
            fixture.Scheduler.enabled = true;
            SetField(fixture.Scheduler, "runtimeSchedulingEnabled", true);
            fixture.Motor.enabled = true;
            fixture.Motor.Initialize(moveSpeed);
            return fixture;
        }

        static void PrepareAndComplete(
            InvectorBrawlerMotor motor, Action duringScheduledStep)
        {
            InvokeNonPublic(motor, "PrepareScheduledStep");
            duringScheduledStep?.Invoke();
            InvokeNonPublic(motor, "CompleteScheduledStep");
        }

        static object InvokeNonPublic(
            object target, string methodName, params object[] arguments)
        {
            MethodInfo method = target.GetType().GetMethod(
                methodName, BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.IsNotNull(method, "Missing non-public method: " + methodName);
            return method.Invoke(target, arguments);
        }

        static T GetField<T>(object target, string fieldName)
        {
            FieldInfo field = target.GetType().GetField(
                fieldName, BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.IsNotNull(field, "Missing private field: " + fieldName);
            return (T)field.GetValue(target);
        }

        static void SetField(object target, string fieldName, object value)
        {
            FieldInfo field = target.GetType().GetField(
                fieldName, BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.IsNotNull(field, "Missing private field: " + fieldName);
            field.SetValue(target, value);
        }

        static void AssertSerializedReference(
            object target, string fieldName, Object expected)
        {
            FieldInfo field = target.GetType().GetField(
                fieldName, BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.IsNotNull(field, "Missing serialized field: " + fieldName);
            Assert.IsTrue(field.IsDefined(typeof(SerializeField), false));
            Assert.AreSame(expected, field.GetValue(target));
        }

        static void AssertVectorApproximately(Vector3 expected, Vector3 actual)
        {
            Assert.That(Vector3.Distance(expected, actual), Is.LessThan(0.0001f),
                "Expected " + expected + " but was " + actual + ".");
        }

        static string ReadProjectFile(string relativePath)
        {
            string projectRoot = Directory.GetParent(Application.dataPath)?.FullName;
            Assert.IsNotNull(projectRoot);
            return File.ReadAllText(Path.Combine(projectRoot, relativePath));
        }

        static string ExtractMethodBody(string source, string signature)
        {
            int signatureIndex = source.IndexOf(
                signature, StringComparison.Ordinal);
            Assert.That(signatureIndex, Is.GreaterThanOrEqualTo(0),
                "Missing source method: " + signature);
            int openingBrace = source.IndexOf('{', signatureIndex);
            Assert.That(openingBrace, Is.GreaterThanOrEqualTo(0));

            int depth = 0;
            for (int i = openingBrace; i < source.Length; i++)
            {
                if (source[i] == '{') depth++;
                else if (source[i] == '}' && --depth == 0)
                    return source.Substring(openingBrace, i - openingBrace + 1);
            }

            Assert.Fail("Unterminated source method: " + signature);
            return string.Empty;
        }

        static void AssertBefore(string source, string earlier, string later)
        {
            int earlierIndex = source.IndexOf(earlier, StringComparison.Ordinal);
            int laterIndex = source.IndexOf(later, StringComparison.Ordinal);
            Assert.That(earlierIndex, Is.GreaterThanOrEqualTo(0),
                "Missing source token: " + earlier);
            Assert.That(laterIndex, Is.GreaterThan(earlierIndex),
                earlier + " must occur before " + later + ".");
        }

        sealed class Fixture
        {
            public Fixture(
                Scene scene,
                GameObject root,
                BrawlInvectorThirdPersonController controller,
                InvectorShooterMeleeInputAdapter scheduler,
                InvectorBrawlerMotor motor,
                Rigidbody body,
                CapsuleCollider capsule)
            {
                Scene = scene;
                Root = root;
                Controller = controller;
                Scheduler = scheduler;
                Motor = motor;
                Body = body;
                Capsule = capsule;
            }

            public Scene Scene { get; }
            public GameObject Root { get; }
            public BrawlInvectorThirdPersonController Controller { get; }
            public InvectorShooterMeleeInputAdapter Scheduler { get; }
            public InvectorBrawlerMotor Motor { get; }
            public Rigidbody Body { get; }
            public CapsuleCollider Capsule { get; }
        }
    }
}
