using System;
using System.Collections;
using System.IO;
using System.Linq;
using System.Reflection;
using Invector.vCharacterController;
using Invector.vItemManager;
using Invector.vMelee;
using Invector.vShooter;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEditor.TestTools.TestRunner.Api;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace BrawlArena.EditorAutomation.Tests
{
    public sealed class InvectorMigrationPhase3BPlayModeTests
    {
        [UnityTest]
        [Category("InvectorPhase3B")]
        public IEnumerator LiveLabKeepsOneInputSchedulerAnimatorAuthorityAndReturnsDormant()
        {
            Scene original = SceneManager.GetActiveScene();
            string originalPath = original.path;
            bool originalDirty = original.isDirty;

            yield return new EnterPlayMode();
            Scene playLab = EditorSceneManager.LoadSceneInPlayMode(
                InvectorPhase3BLabController.LabScenePath,
                new LoadSceneParameters(LoadSceneMode.Single));
            yield return null;
            yield return new WaitForFixedUpdate();

            InvectorPhase3BLabController gate = InvectorPhase3BLabController.LiveInstance;
            Assert.That(gate != null, Is.True,
                "The isolated live gate did not register its runtime instance.");
            Assert.That(gate.gameObject.scene, Is.EqualTo(playLab),
                "The registered live gate does not belong to the loaded migration scene.");
            Assert.That(playLab.IsValid() && playLab.isLoaded, Is.True,
                "The isolated migration scene did not finish loading.");
            Assert.That(playLab.path, Is.EqualTo(InvectorPhase3BLabController.LabScenePath),
                "The live gate was not loaded from the isolated migration scene.");

            bool activated = gate.Activated && gate.AuthorityChecksPassed && gate.IsLiveGateReady;
            bool oneInput = gate.StaticSceneOwnershipValidated && gate.InputAdapter != null;
            bool oneDriver = gate.StaticSceneOwnershipValidated && gate.AnimationDriver != null;
            bool actionFeedSelected =
                gate.InputAdapter.MovementFeedMode == InvectorMovementFeedMode.LabProjectAction;
            bool bufferedMotorStayedDormant = gate.Motor != null &&
                                               !gate.Motor.enabled &&
                                               !gate.Motor.IsInitialized;
            bool animatorRegistry = gate.Controller.HasRegisteredAnimatorStateInfos &&
                                    gate.Controller.RegisteredAnimatorStateInfoLayerCount == 8;
            bool actionWasProjectWide = gate.InputAdapter.ProjectMoveActionUsesProjectWideLifecycle;
            float healthBaseline = gate.Controller.currentHealth;
            BrawlInvectorMeleePresentationManager meleePresentation =
                gate.MeleePresentationManager;
            bool meleeFirewallStartedClean = meleePresentation != null &&
                                             !meleePresentation.enabled &&
                                             meleePresentation.Members.Count == 0 &&
                                             meleePresentation.leftWeapon == null &&
                                             meleePresentation.rightWeapon == null &&
                                             meleePresentation.SuppressedAttackWindowCount == 0 &&
                                             meleePresentation.BlockedDamageHitCount == 0 &&
                                             gate.Controller.MeleePresentationWriteCount == 0 &&
                                             gate.Controller.LastPresentationAttackId == 0;

            var gamepad = InputSystem.AddDevice<Gamepad>("Phase3BAutomatedGamepad");
            gamepad.MakeCurrent();
            Vector3 movementStart = gate.PilotRoot.transform.position;
            InputSystem.QueueStateEvent(gamepad, new GamepadState { leftStick = Vector2.up });
            yield return null;
            for (int i = 0; i < 30; i++)
                yield return new WaitForFixedUpdate();
            InputSystem.QueueStateEvent(gamepad, new GamepadState { leftStick = Vector2.zero });
            for (int frame = 0;
                 frame < 10 && gate.InputAdapter.LastMoveIntent.sqrMagnitude >= 0.0001f;
                 frame++)
            {
                yield return null;
            }
            yield return new WaitForFixedUpdate();

            Vector3 movementDelta = gate.PilotRoot.transform.position - movementStart;
            movementDelta.y = 0f;
            bool movementObserved = movementDelta.magnitude > 0.05f;
            bool neutralObserved = gate.InputAdapter.LastMoveIntent.sqrMagnitude < 0.0001f;

            gate.RequestBasicAttackProbe();
            for (int frame = 0;
                 frame < 600 && !(gate.ObservedBasicAttackState && !gate.InputAdapter.isAttacking);
                 frame++)
            {
                yield return null;
            }
            bool basicObserved = gate.ObservedBasicAttackState &&
                                 gate.AnimationDriver.BasicAttackRequestCount == 1 &&
                                 gate.InputAdapter.WeakAttackRequestCount == 1 &&
                                 gate.Controller.MeleePresentationWriteCount == 1;
            int meleeWindowsBeforeSuper = meleePresentation.SuppressedAttackWindowCount;
            int meleeWindowEnablesBeforeSuper = meleePresentation.SuppressedAttackWindowEnableCount;
            int meleeWindowDisablesBeforeSuper = meleePresentation.SuppressedAttackWindowDisableCount;
            gate.RequestSuperProbe();
            for (int frame = 0;
                 frame < 600 && !(gate.ObservedSuperState && !gate.InputAdapter.isAttacking);
                 frame++)
            {
                yield return null;
            }
            bool superObserved = gate.ObservedSuperState &&
                                 gate.AnimationDriver.SuperRequestCount == 1 &&
                                 gate.InputAdapter.StrongAttackRequestCount == 1 &&
                                 gate.Controller.MeleePresentationWriteCount == 2;
            bool attackCallbacksBalanced =
                gate.InputAdapter.AttackEnableCallbackCount > 0 &&
                gate.InputAdapter.AttackEnableCallbackCount ==
                    gate.InputAdapter.AttackDisableCallbackCount;
            int superWindowCount =
                meleePresentation.SuppressedAttackWindowCount - meleeWindowsBeforeSuper;
            int superWindowEnableCount =
                meleePresentation.SuppressedAttackWindowEnableCount - meleeWindowEnablesBeforeSuper;
            int superWindowDisableCount =
                meleePresentation.SuppressedAttackWindowDisableCount - meleeWindowDisablesBeforeSuper;
            bool superStayedBehindMeleeFirewall =
                superWindowCount > 0 &&
                superWindowEnableCount > 0 &&
                superWindowEnableCount == superWindowDisableCount &&
                superWindowCount == superWindowEnableCount + superWindowDisableCount &&
                meleePresentation.SuppressedListAttackWindowCount > 0 &&
                meleePresentation.SuppressedSingleAttackWindowCount == 0 &&
                meleePresentation.BlockedDamageHitCount == 0 &&
                meleePresentation.Members.Count == 0 &&
                meleePresentation.leftWeapon == null &&
                meleePresentation.rightWeapon == null;

            gate.RequestHitReactionProbe();
            for (int frame = 0;
                 frame < 600 && !gate.ObservedHitReactionTransition;
                 frame++)
            {
                yield return null;
            }
            bool recoilObserved = gate.ObservedHitReactionTransition &&
                                  gate.ObservedHitReactionStateHash != 0 &&
                                  gate.AnimationDriver.HitReactionRequestCount == 1 &&
                                  gate.InputAdapter.RecoilRequestCount == 1 &&
                                  gate.Controller.RecoilPresentationWriteCount == 1;

            bool countersEqual = gate.InputAdapter.SchedulerCompleteCount > 0 &&
                                 gate.InputAdapter.SchedulerStartCount == gate.InputAdapter.SchedulerCompleteCount &&
                                 gate.Controller.MotorUpdateCount == gate.InputAdapter.SchedulerCompleteCount &&
                                 gate.Controller.LocomotionUpdateCount == gate.InputAdapter.SchedulerCompleteCount &&
                                 gate.InputAdapter.RotationUpdateCount == gate.InputAdapter.SchedulerCompleteCount &&
                                 gate.Controller.RotationControlCount == gate.InputAdapter.SchedulerCompleteCount &&
                                 gate.Controller.AnimatorUpdateCount == gate.InputAdapter.SchedulerCompleteCount &&
                                 gate.InputAdapter.InputUpdateCount == gate.InputAdapter.MoveReadCount &&
                                 gate.Motor.ScheduledPrepareCount == 0 &&
                                 gate.Motor.ScheduledCompleteCount == 0;
            bool resourcesInert = Mathf.Approximately(gate.Controller.currentHealth, healthBaseline) &&
                                  gate.Controller.IsInternalMotorStaminaPinned &&
                                  meleePresentation.BlockedDamageHitCount == 0;
            bool noSuppressedPaths = gate.InputAdapter.SuppressedVendorPathCount == 0 &&
                                     gate.InputAdapter.ExternalFixedUpdateSubscriberCount == 0;

            InputSystem.RemoveDevice(gamepad);
            GameObject pilot = gate.PilotRoot;
            Rigidbody body = pilot.GetComponent<Rigidbody>();
            CapsuleCollider capsule = pilot.GetComponent<CapsuleCollider>();
            Animator animator = pilot.GetComponent<Animator>();
            InvectorBrawlerMotor motor = pilot.GetComponent<InvectorBrawlerMotor>();
            int fullPresentationResetsBeforeShutdown =
                gate.Controller.FullPresentationResetCount;
            int attackResetCallbacksBeforeProbe =
                gate.InputAdapter.AttackResetCallbackCount;
            int controllerAttackResetsBeforeProbe =
                gate.Controller.MeleeAttackTriggerResetCount;
            gate.InputAdapter.TriggerStrongAttack();
            bool immediateMeleeTriggerQueued =
                gate.Controller.HasPendingMeleePresentationTrigger;
            gate.InputAdapter.ResetAttackTriggers();
            bool attackResetCallbackRearmedPendingPresentation =
                gate.Controller.HasPendingMeleePresentationTrigger &&
                gate.InputAdapter.AttackResetCallbackCount ==
                    attackResetCallbacksBeforeProbe + 1 &&
                gate.Controller.MeleeAttackTriggerResetCount ==
                    controllerAttackResetsBeforeProbe + 1;
            gate.InputAdapter.OnRecoil(7);
            bool immediateRecoilTriggerQueued =
                gate.Controller.HasPendingRecoilPresentationTrigger &&
                !gate.Controller.HasPendingMeleePresentationTrigger &&
                gate.Controller.LastPresentationRecoilId == 7 &&
                animator.GetInteger(vAnimatorParameters.RecoilID) == 7;
            gate.DeactivateLabInstance();
            bool attackPresentationResidueCleared =
                immediateMeleeTriggerQueued &&
                attackResetCallbackRearmedPendingPresentation &&
                immediateRecoilTriggerQueued &&
                !gate.Controller.HasPendingMeleePresentationTrigger &&
                !gate.Controller.HasPendingRecoilPresentationTrigger &&
                gate.Controller.FullPresentationResetCount ==
                    fullPresentationResetsBeforeShutdown + 1 &&
                gate.Controller.LastPresentationAttackId == 0 &&
                gate.Controller.LastPresentationRecoilId == 0 &&
                animator.GetInteger(vAnimatorParameters.AttackID) == 0 &&
                animator.GetInteger(vAnimatorParameters.RecoilID) == 0;
            bool actionLifecycleRestored =
                gate.InputAdapter.ProjectMoveActionEnabled == actionWasProjectWide &&
                !gate.InputAdapter.ProjectMoveActionOwnedByAdapter;
            bool dormantAfterShutdown = !gate.Activated && !pilot.activeSelf &&
                                        !gate.InputAdapter.enabled &&
                                        gate.InputAdapter.MovementFeedMode ==
                                            InvectorMovementFeedMode.LabProjectAction &&
                                        actionLifecycleRestored &&
                                        motor.IsDormantConfigured && !motor.IsInitialized && !motor.enabled &&
                                        !gate.AnimationDriver.PresentationRequestsEnabled &&
                                        !gate.Controller.enabled && !animator.enabled && !capsule.enabled &&
                                        body.isKinematic && !body.useGravity &&
                                        body.constraints == RigidbodyConstraints.FreezeAll &&
                                        body.interpolation == RigidbodyInterpolation.None &&
                                        body.collisionDetectionMode == CollisionDetectionMode.Discrete &&
                                        body.linearVelocity == Vector3.zero && body.angularVelocity == Vector3.zero;

            yield return new ExitPlayMode();

            Scene restored = SceneManager.GetSceneByPath(originalPath);
            Scene restoredLab = SceneManager.GetSceneByPath(InvectorPhase3BLabController.LabScenePath);
            if (restored.IsValid() && restored.isLoaded)
                SceneManager.SetActiveScene(restored);
            if (restoredLab.IsValid() && restoredLab.isLoaded)
                EditorSceneManager.CloseScene(restoredLab, true);

            Assert.That(activated, Is.True, "The isolated live gate did not activate cleanly.");
            Assert.That(oneInput, Is.True, "The lab did not retain exactly one project input authority.");
            Assert.That(oneDriver, Is.True, "The lab did not retain exactly one animation driver.");
            Assert.That(actionFeedSelected, Is.True, "The Phase 3B lab did not retain its project-action feed.");
            Assert.That(bufferedMotorStayedDormant, Is.True,
                "The Phase 3B project-action path activated the buffered motor.");
            Assert.That(animatorRegistry, Is.True, "The complete eight-layer Animator listener registry is absent.");
            Assert.That(meleeFirewallStartedClean, Is.True,
                "The project melee presentation firewall did not start empty and reset.");
            Assert.That(movementObserved, Is.True, "Player/Move gamepad input did not move the pilot.");
            Assert.That(neutralObserved, Is.True, "Neutral Player/Move input did not clear intent.");
            Assert.That(basicObserved, Is.True, "The basic presentation request did not enter and release.");
            Assert.That(superObserved, Is.True, "The super presentation request did not enter and release.");
            Assert.That(attackCallbacksBalanced, Is.True,
                "Attack enable/disable callbacks diverged.");
            Assert.That(superStayedBehindMeleeFirewall, Is.True,
                "The super animation window escaped or bypassed the project melee presentation firewall.");
            Assert.That(recoilObserved, Is.True, "No audited FullBody recoil state was observed.");
            Assert.That(countersEqual, Is.True, "The one-scheduler trace diverged.");
            Assert.That(resourcesInert, Is.True, "An Invector gameplay resource changed.");
            Assert.That(noSuppressedPaths, Is.True, "A forbidden vendor path or scheduler subscriber ran.");
            Assert.That(dormantAfterShutdown, Is.True, "The live stack did not return to dormant state.");
            Assert.That(attackPresentationResidueCleared, Is.True,
                "Scheduler teardown retained an attack ID or one-shot presentation residue.");
            Assert.That(restored.IsValid() && restored.isLoaded, Is.True);
            Assert.That(restored.isDirty, Is.EqualTo(originalDirty),
                "The user's original scene dirty state changed.");
        }

        [UnityTest]
        [Category("InvectorPhase3CBufferedMotor")]
        public IEnumerator LiveBufferedMotorUsesOneSchedulerWithoutPhysicalInputAndReturnsDormant()
        {
            const float moveSpeed = 4f;
            Scene original = SceneManager.GetActiveScene();
            string originalPath = original.path;
            bool originalDirty = original.isDirty;

            yield return new EnterPlayMode();
            Scene playLab = EditorSceneManager.LoadSceneInPlayMode(
                InvectorPhase3BLabController.LabScenePath,
                new LoadSceneParameters(LoadSceneMode.Single));
            yield return null;
            yield return new WaitForFixedUpdate();

            InvectorPhase3BLabController gate = InvectorPhase3BLabController.LiveInstance;
            Assert.That(gate, Is.Not.Null);
            Assert.That(playLab.IsValid() && playLab.isLoaded, Is.True);
            Assert.That(gate.InputAdapter.MovementFeedMode,
                Is.EqualTo(InvectorMovementFeedMode.LabProjectAction));
            Assert.That(gate.Motor.IsInitialized, Is.False,
                "The ordinary Phase 3B action path must leave the buffered motor dormant.");

            bool actionWasProjectWide =
                gate.InputAdapter.ProjectMoveActionUsesProjectWideLifecycle;
            gate.DeactivateLabInstance();
            bool cleanTransitionDormant = gate.Motor.IsDormantConfigured &&
                                          gate.InputAdapter.IsDormantConfigured &&
                                          !gate.PilotRoot.activeSelf;

            gate.ActivateBufferedMotorPath(moveSpeed);
            yield return null;
            yield return new WaitForFixedUpdate();

            bool activated = gate.Activated && gate.AuthorityChecksPassed &&
                             gate.IsLiveGateReady && gate.Motor.IsInitialized &&
                             gate.Motor.isActiveAndEnabled &&
                             gate.InputAdapter.MovementFeedMode ==
                                 InvectorMovementFeedMode.BufferedMotor;
            bool noPhysicalReader = !gate.InputAdapter.ProjectMoveActionOwnedByAdapter &&
                                    gate.InputAdapter.InputUpdateCount == 0 &&
                                    gate.InputAdapter.MoveReadCount == 0;
            float healthBaseline = gate.Controller.currentHealth;
            Animator animator = gate.PilotRoot.GetComponent<Animator>();
            BrawlInvectorMeleePresentationManager meleePresentation =
                gate.MeleePresentationManager;

            Vector3 movementStart = gate.PilotRoot.transform.position;
            gate.Motor.SetPlanarIntent(Vector3.forward, moveSpeed, true);
            for (int i = 0; i < 30; i++)
                yield return new WaitForFixedUpdate();
            bool locomotionAnimatorObserved =
                animator.GetFloat(vAnimatorParameters.InputMagnitude) > 0.1f &&
                animator.GetFloat(vAnimatorParameters.InputVertical) > 0.1f;

            gate.Motor.SetPlanarIntent(Vector3.zero, 0f, true);
            for (int frame = 0; frame < 20; frame++)
            {
                yield return new WaitForFixedUpdate();
                Vector3 settlingVelocity = gate.Motor.Velocity;
                settlingVelocity.y = 0f;
                if (gate.Controller.input.sqrMagnitude < 0.0001f &&
                    settlingVelocity.magnitude < 0.05f)
                {
                    break;
                }
            }
            Vector3 settledPosition = gate.PilotRoot.transform.position;
            yield return new WaitForFixedUpdate();
            yield return new WaitForFixedUpdate();
            Vector3 movementDelta = gate.PilotRoot.transform.position - movementStart;
            movementDelta.y = 0f;
            Vector3 neutralPlanarVelocity = gate.Motor.Velocity;
            neutralPlanarVelocity.y = 0f;
            Vector3 neutralDrift = gate.PilotRoot.transform.position - settledPosition;
            neutralDrift.y = 0f;
            bool worldMovementObserved = movementDelta.z > 0.05f &&
                                         Mathf.Abs(movementDelta.x) < movementDelta.z;
            bool neutralObserved = gate.Controller.input.sqrMagnitude < 0.0001f &&
                                   gate.Motor.BufferedWorldIntent.sqrMagnitude < 0.0001f &&
                                   neutralPlanarVelocity.magnitude < 0.05f &&
                                   neutralDrift.magnitude < 0.02f;
            bool countersEqual = gate.InputAdapter.SchedulerCompleteCount > 0 &&
                                 gate.InputAdapter.SchedulerStartCount ==
                                     gate.InputAdapter.SchedulerCompleteCount &&
                                 gate.Motor.ScheduledPrepareCount ==
                                     gate.InputAdapter.SchedulerCompleteCount &&
                                 gate.Motor.ScheduledCompleteCount ==
                                     gate.InputAdapter.SchedulerCompleteCount &&
                                 gate.Controller.MotorUpdateCount ==
                                     gate.InputAdapter.SchedulerCompleteCount &&
                                 gate.Controller.LocomotionUpdateCount ==
                                     gate.InputAdapter.SchedulerCompleteCount &&
                                 gate.InputAdapter.RotationUpdateCount ==
                                     gate.InputAdapter.SchedulerCompleteCount &&
                                 gate.Controller.RotationControlCount ==
                                     gate.InputAdapter.SchedulerCompleteCount &&
                                 gate.Controller.AnimatorUpdateCount ==
                                     gate.InputAdapter.SchedulerCompleteCount;
            bool resourcesInert = Mathf.Approximately(
                                      gate.Controller.currentHealth, healthBaseline) &&
                                  gate.Controller.IsInternalMotorStaminaPinned;
            bool noForbiddenPath = gate.InputAdapter.SuppressedVendorPathCount == 0 &&
                                   gate.InputAdapter.ExternalFixedUpdateSubscriberCount == 0 &&
                                   gate.InputAdapter.InputUpdateCount == 0 &&
                                   gate.InputAdapter.MoveReadCount == 0 &&
                                   gate.Controller.MeleePresentationWriteCount == 0 &&
                                   gate.Controller.RecoilPresentationWriteCount == 0 &&
                                   gate.Controller.LastPresentationAttackId == 0 &&
                                   gate.Controller.LastPresentationRecoilId == 0 &&
                                   !gate.Controller.HasPendingMeleePresentationTrigger &&
                                   !gate.Controller.HasPendingRecoilPresentationTrigger &&
                                   meleePresentation != null &&
                                   meleePresentation.SuppressedAttackWindowCount == 0 &&
                                   meleePresentation.BlockedDamageHitCount == 0 &&
                                   meleePresentation.Members.Count == 0 &&
                                   meleePresentation.leftWeapon == null &&
                                   meleePresentation.rightWeapon == null;

            GameObject pilot = gate.PilotRoot;
            Rigidbody body = pilot.GetComponent<Rigidbody>();
            CapsuleCollider capsule = pilot.GetComponent<CapsuleCollider>();
            gate.DeactivateLabInstance();
            bool actionLifecycleRestored =
                gate.InputAdapter.ProjectMoveActionEnabled == actionWasProjectWide &&
                !gate.InputAdapter.ProjectMoveActionOwnedByAdapter;
            bool dormantAfterShutdown = !gate.Activated && !pilot.activeSelf &&
                                        gate.InputAdapter.MovementFeedMode ==
                                            InvectorMovementFeedMode.LabProjectAction &&
                                        gate.Motor.IsDormantConfigured &&
                                        !gate.Motor.IsInitialized && !gate.Motor.enabled &&
                                        !gate.InputAdapter.enabled && actionLifecycleRestored &&
                                        !gate.AnimationDriver.PresentationRequestsEnabled &&
                                        !gate.Controller.enabled && !animator.enabled && !capsule.enabled &&
                                        body.isKinematic && !body.useGravity &&
                                        body.constraints == RigidbodyConstraints.FreezeAll &&
                                        body.interpolation == RigidbodyInterpolation.None &&
                                        body.collisionDetectionMode == CollisionDetectionMode.Discrete &&
                                        body.linearVelocity == Vector3.zero &&
                                        body.angularVelocity == Vector3.zero;

            yield return new ExitPlayMode();

            Scene restored = SceneManager.GetSceneByPath(originalPath);
            Scene restoredLab = SceneManager.GetSceneByPath(InvectorPhase3BLabController.LabScenePath);
            if (restored.IsValid() && restored.isLoaded)
                SceneManager.SetActiveScene(restored);
            if (restoredLab.IsValid() && restoredLab.isLoaded)
                EditorSceneManager.CloseScene(restoredLab, true);

            Assert.That(cleanTransitionDormant, Is.True,
                "The action lab did not become dormant before switching feeds.");
            Assert.That(activated, Is.True,
                "The buffered motor lab did not activate its reciprocal same-root bridge.");
            Assert.That(noPhysicalReader, Is.True,
                "The buffered motor path adopted or executed a physical input reader.");
            Assert.That(worldMovementObserved, Is.True,
                "Buffered world-space forward intent did not move the pilot forward.");
            Assert.That(locomotionAnimatorObserved, Is.True,
                "The buffered scheduler did not publish nonzero Invector locomotion parameters.");
            Assert.That(neutralObserved, Is.True,
                "Neutral buffered intent did not clear the controller at the fixed boundary.");
            Assert.That(countersEqual, Is.True,
                "The adapter, motor, controller, and Animator scheduler traces diverged.");
            Assert.That(resourcesInert, Is.True, "Invector locomotion stamina changed.");
            Assert.That(noForbiddenPath, Is.True,
                "A physical input, suppressed vendor path, or second scheduler executed.");
            Assert.That(dormantAfterShutdown, Is.True,
                "The buffered path did not restore the original action-feed dormant posture.");
            Assert.That(restored.IsValid() && restored.isLoaded, Is.True);
            Assert.That(restored.isDirty, Is.EqualTo(originalDirty),
                "The user's original scene dirty state changed.");
        }

        [UnityTest]
        [Category("InvectorPhase3DWeaponIK")]
        public IEnumerator LiveWeaponIKPresentationIsVisualOnlySelectiveAndTeardownSafe()
        {
            const int MaximumPollFrames = 600;
            const int IKPollFrames = 60;
            const float ReopenMoveSpeed = 4f;
            Scene original = SceneManager.GetActiveScene();
            string originalPath = original.path;
            bool originalDirty = original.isDirty;

            bool labReady = false;
            bool projectIKDataComplete = false;
            bool fourIKStatesApplied = false;
            bool supportHandIKApplied = false;
            bool aimAndZeroReleaseWorked = false;
            bool muzzleResolved = false;
            bool oneEmissionPerMuzzleCall = false;
            bool selectiveLayersValid = false;
            bool forbiddenWeaponDamageComponentsAbsent = false;
            bool lifecycleSuppressionSafe = false;
            bool vendorResourcesInert = false;
            bool immediateCloseNormalized = false;
            bool noDeferredPresentationAfterReopen = false;
            bool dormantAfterShutdown = false;
            bool actionWasProjectWide = false;
            string lifecycleEvidence = string.Empty;
            string ikEvidence = string.Empty;

            yield return new EnterPlayMode();
            weaponIKWarningOrErrorCount = 0;
            firstWeaponIKWarningOrError = string.Empty;
            Application.logMessageReceived += CaptureWeaponIKWarningOrError;
            try
            {
                Scene playLab = EditorSceneManager.LoadSceneInPlayMode(
                    InvectorPhase3BLabController.LabScenePath,
                    new LoadSceneParameters(LoadSceneMode.Single));
                yield return null;
                yield return new WaitForFixedUpdate();

                InvectorPhase3BLabController gate =
                    InvectorPhase3BLabController.LiveInstance;
                GameObject pilot = gate != null ? gate.PilotRoot : null;
                InvectorBrawlerWeaponPresentation presenter =
                    gate != null ? gate.WeaponPresentation : null;
                BrawlInvectorThirdPersonController controller =
                    gate != null ? gate.Controller : null;
                InvectorBrawlerAnimationDriver driver =
                    gate != null ? gate.AnimationDriver : null;
                InvectorShooterMeleeInputAdapter input =
                    gate != null ? gate.InputAdapter : null;
                BrawlInvectorMeleePresentationManager melee =
                    gate != null ? gate.MeleePresentationManager : null;
                Animator animator = pilot != null ? pilot.GetComponent<Animator>() : null;
                Rigidbody body = pilot != null ? pilot.GetComponent<Rigidbody>() : null;
                CapsuleCollider capsule =
                    pilot != null ? pilot.GetComponent<CapsuleCollider>() : null;
                vShooterManager shooter =
                    pilot != null ? pilot.GetComponent<vShooterManager>() : null;
                vAmmoManager ammoManager =
                    pilot != null ? pilot.GetComponent<vAmmoManager>() : null;
                vCollectShooterMeleeControl collector = pilot != null
                    ? pilot.GetComponent<vCollectShooterMeleeControl>()
                    : null;
                BrawlerHitProxy proxy = pilot != null
                    ? pilot.GetComponentInChildren<BrawlerHitProxy>(true)
                    : null;
                ParticleSystem[] muzzleEffects = pilot != null
                    ? pilot.GetComponentsInChildren<ParticleSystem>(true)
                        .Where(effect => effect.gameObject.name == "BrawlMuzzleVfx")
                        .ToArray()
                    : Array.Empty<ParticleSystem>();
                Component[] components = pilot != null
                    ? pilot.GetComponentsInChildren<Component>(true)
                        .Where(component => component != null)
                        .ToArray()
                    : Array.Empty<Component>();

                labReady = gate != null && playLab.IsValid() && playLab.isLoaded &&
                           gate.gameObject.scene == playLab && gate.IsLiveGateReady &&
                           presenter != null && presenter.IsConfigured &&
                           presenter.LabRuntimeEnabled && presenter.isActiveAndEnabled &&
                           controller != null && driver != null && input != null &&
                           melee != null && animator != null && body != null &&
                           capsule != null && shooter != null && ammoManager != null &&
                           collector != null && proxy != null && muzzleEffects.Length == 1;

                if (labReady)
                {
                    actionWasProjectWide =
                        input.ProjectMoveActionUsesProjectWideLifecycle;
                    vWeaponIKAdjustList ikList = presenter.ProjectIKAdjustList;
                    vWeaponIKAdjust weaponIK = ikList != null
                        ? ikList.GetWeaponIK(presenter.WeaponCategory)
                        : null;
                    string ikListPath = ikList != null
                        ? AssetDatabase.GetAssetPath(ikList)
                        : string.Empty;
                    projectIKDataComplete =
                        ikList != null &&
                        ikListPath.StartsWith(
                            "Assets/Generated/InvectorMigration/Cinder/IK/",
                            StringComparison.Ordinal) &&
                        presenter.WeaponCategory == "BrawlWizardStaff" &&
                        weaponIK != null && weaponIK.HasAllDefaultStates() &&
                        weaponIK.GetIKAdjust(
                            vWeaponIKAdjust.StandingState,
                            presenter.WeaponHeldInLeftHand) != null &&
                        weaponIK.GetIKAdjust(
                            vWeaponIKAdjust.StandingAimingState,
                            presenter.WeaponHeldInLeftHand) != null &&
                        weaponIK.GetIKAdjust(
                            vWeaponIKAdjust.CrouchingState,
                            presenter.WeaponHeldInLeftHand) != null &&
                        weaponIK.GetIKAdjust(
                            vWeaponIKAdjust.CrouchingAimingState,
                            presenter.WeaponHeldInLeftHand) != null;

                    float healthBaseline = controller.currentHealth;
                    bool deadBaseline = controller.isDead;
                    float staminaBaseline = controller.InternalMotorStamina;
                    int extraAmmoBaseline = shooter.ExtraAmmo;
                    bool shooterEnabledBaseline = shooter.enabled;
                    bool shooterReloadingBaseline = shooter.isReloadingWeapon;
                    UnityEngine.Object rightWeaponBaseline = shooter.rWeapon;
                    UnityEngine.Object leftWeaponBaseline = shooter.lWeapon;
                    string ammoBaseline = AmmoSignature(ammoManager);
                    bool ammoManagerEnabledBaseline = ammoManager.enabled;
                    bool collectorEnabledBaseline = collector.enabled;
                    int meleeWindowsBaseline = melee.SuppressedAttackWindowCount;
                    int meleeDamageBaseline = melee.BlockedDamageHitCount;
                    int suppressedVendorPathsBaseline = input.SuppressedVendorPathCount;
                    int supportSuppressionBaseline =
                        presenter.SupportHandSuppressionCount;
                    int invalidPoseBaseline = presenter.InvalidPoseCount;
                    int runtimeFaultBaseline = presenter.RuntimeFaultCount;
                    int appliedIKBaseline = presenter.AppliedIKPassCount;
                    int aimRequestsBaseline = presenter.AimRequestCount;
                    int aimReleasesBaseline = presenter.AimReleaseCount;

                    for (int frame = 0; frame < 5; frame++)
                        yield return null;

                    controller.isCrouching = false;
                    presenter.PresentAim(Vector3.zero);
                    bool standingResolved = ReferenceEquals(
                        ResolveCurrentIKAdjustForProof(presenter),
                        weaponIK.GetIKAdjust(
                            vWeaponIKAdjust.StandingState,
                            presenter.WeaponHeldInLeftHand));
                    int standingPassBaseline = presenter.AppliedIKPassCount;
                    for (int frame = 0;
                         frame < IKPollFrames &&
                         presenter.AppliedIKPassCount == standingPassBaseline;
                         frame++)
                    {
                        yield return null;
                    }
                    bool standingApplied =
                        presenter.AppliedIKPassCount > standingPassBaseline;

                    presenter.PresentAim(Vector3.forward);
                    bool standingAimAccepted = presenter.AimPresented &&
                        Vector3.Dot(presenter.PresentedAimDirection, Vector3.forward) > 0.999f;
                    bool standingAimResolved = ReferenceEquals(
                        ResolveCurrentIKAdjustForProof(presenter),
                        weaponIK.GetIKAdjust(
                            vWeaponIKAdjust.StandingAimingState,
                            presenter.WeaponHeldInLeftHand));
                    int standingAimPassBaseline = presenter.AppliedIKPassCount;
                    for (int frame = 0;
                         frame < IKPollFrames &&
                         presenter.AppliedIKPassCount == standingAimPassBaseline;
                         frame++)
                    {
                        yield return null;
                    }
                    bool standingAimApplied =
                        presenter.AppliedIKPassCount > standingAimPassBaseline;

                    controller.isCrouching = true;
                    presenter.PresentAim(Vector3.zero);
                    bool crouchingResolved = ReferenceEquals(
                        ResolveCurrentIKAdjustForProof(presenter),
                        weaponIK.GetIKAdjust(
                            vWeaponIKAdjust.CrouchingState,
                            presenter.WeaponHeldInLeftHand));
                    int crouchingPassBaseline = presenter.AppliedIKPassCount;
                    for (int frame = 0;
                         frame < IKPollFrames &&
                         presenter.AppliedIKPassCount == crouchingPassBaseline;
                         frame++)
                    {
                        yield return null;
                    }
                    bool crouchingApplied =
                        presenter.AppliedIKPassCount > crouchingPassBaseline;

                    presenter.PresentAim(Vector3.right);
                    bool crouchingAimAccepted = presenter.AimPresented &&
                        Vector3.Dot(presenter.PresentedAimDirection, Vector3.right) > 0.999f;
                    bool crouchingAimResolved = ReferenceEquals(
                        ResolveCurrentIKAdjustForProof(presenter),
                        weaponIK.GetIKAdjust(
                            vWeaponIKAdjust.CrouchingAimingState,
                            presenter.WeaponHeldInLeftHand));
                    int crouchingAimPassBaseline = presenter.AppliedIKPassCount;
                    for (int frame = 0;
                         frame < IKPollFrames &&
                         presenter.AppliedIKPassCount == crouchingAimPassBaseline;
                         frame++)
                    {
                        yield return null;
                    }
                    bool crouchingAimApplied =
                        presenter.AppliedIKPassCount > crouchingAimPassBaseline;
                    presenter.PresentAim(Vector3.zero);

                    fourIKStatesApplied = standingResolved && standingAimResolved &&
                        crouchingResolved && crouchingAimResolved;
                    supportHandIKApplied =
                        presenter.AppliedIKPassCount > appliedIKBaseline &&
                        presenter.SupportHandSuppressionCount == supportSuppressionBaseline &&
                        presenter.RuntimeFaultCount == runtimeFaultBaseline &&
                        presenter.LastSuppression ==
                            InvectorWeaponPresentationSuppression.None;
                    ikEvidence = string.Format(
                        "resolved={0}/{1}/{2}/{3}; applied={4}/{5}/{6}/{7}, total={8}; " +
                        "suppressed={9}; invalid={10}->{11}; supportSuppressed={12}->{13}; " +
                        "faults={14}->{15}; gated={16}; aim={17}->{18}; release={19}->{20}; last={21}",
                        standingResolved, standingAimResolved,
                        crouchingResolved, crouchingAimResolved,
                        standingApplied, standingAimApplied,
                        crouchingApplied, crouchingAimApplied,
                        presenter.AppliedIKPassCount - appliedIKBaseline,
                        presenter.SuppressedIKPassCount,
                        invalidPoseBaseline, presenter.InvalidPoseCount,
                        supportSuppressionBaseline,
                        presenter.SupportHandSuppressionCount,
                        runtimeFaultBaseline, presenter.RuntimeFaultCount,
                        presenter.GatedLateUpdateCount,
                        aimRequestsBaseline, presenter.AimRequestCount,
                        aimReleasesBaseline, presenter.AimReleaseCount,
                        presenter.LastSuppression);
                    aimAndZeroReleaseWorked = standingAimAccepted &&
                        crouchingAimAccepted && !presenter.AimPresented &&
                        presenter.PresentedAimDirection == Vector3.zero &&
                        presenter.AimRequestCount == aimRequestsBaseline + 5 &&
                        presenter.AimReleaseCount == aimReleasesBaseline + 2;

                    muzzleResolved = presenter.TryGetMuzzlePosition(
                        out Vector3 muzzlePosition) && IsFinite(muzzlePosition);
                    int muzzleRequestsBaseline =
                        presenter.MuzzlePresentationRequestCount;
                    int muzzleEmissionsBaseline = presenter.MuzzleEmissionCount;
                    if (muzzleResolved)
                    {
                        presenter.PresentMuzzle(muzzlePosition, Vector3.forward);
                        presenter.PresentMuzzle(muzzlePosition, Vector3.right);
                    }
                    oneEmissionPerMuzzleCall = muzzleResolved &&
                        presenter.MuzzlePresentationRequestCount ==
                            muzzleRequestsBaseline + 2 &&
                        presenter.MuzzleEmissionCount == muzzleEmissionsBaseline + 2;

                    Transform visualRoot = pilot.GetComponentsInChildren<Transform>(true)
                        .FirstOrDefault(item => item.name == "CinderStaffPresentation");
                    Transform staffVisual = pilot.GetComponentsInChildren<Transform>(true)
                        .FirstOrDefault(item => item.name == "StaffVisual");
                    Transform spellOrigin = pilot.GetComponentsInChildren<Transform>(true)
                        .FirstOrDefault(item => item.name == "SpellOrigin");
                    Transform muzzleVfx = pilot.GetComponentsInChildren<Transform>(true)
                        .FirstOrDefault(item => item.name == "BrawlMuzzleVfx");
                    selectiveLayersValid = pilot.layer ==
                            LayerMask.NameToLayer("InvectorPlayer") &&
                        pilot.GetComponentsInChildren<Transform>(true)
                            .Where(item => item != pilot.transform)
                            .All(item => item.gameObject.layer != pilot.layer) &&
                        proxy.gameObject.layer ==
                            CombatPhysics.BrawlerHitboxLayer &&
                        proxy.IsConfigured && !proxy.enabled &&
                        proxy.TriggerCollider.isTrigger &&
                        !proxy.TriggerCollider.enabled &&
                        visualRoot != null && visualRoot.gameObject.layer == 0 &&
                        staffVisual != null && staffVisual.gameObject.layer == 0 &&
                        spellOrigin != null && spellOrigin.gameObject.layer ==
                            LayerMask.NameToLayer("VFX") &&
                        muzzleVfx != null && muzzleVfx.gameObject.layer ==
                            LayerMask.NameToLayer("VFX") &&
                        pilot.GetComponentsInChildren<Renderer>(true)
                            .Where(renderer => !(renderer is ParticleSystemRenderer))
                            .All(renderer => renderer.gameObject.layer == 0);

                    forbiddenWeaponDamageComponentsAbsent =
                        !components.Any(component =>
                            IsForbiddenWeaponDamageComponent(
                                component.GetType().Name));

                    controller.isCrouching = false;
                    presenter.PresentAim(Vector3.forward);
                    int deathSuppressionsBaseline =
                        presenter.SuppressedIKPassCount;
                    driver.PlayDeath();
                    for (int frame = 0;
                         frame < MaximumPollFrames &&
                         !(driver.LastLifecycleStateEntered ==
                               BrawlInvectorLifecyclePresentation.Death &&
                           presenter.SuppressedIKPassCount >
                               deathSuppressionsBaseline &&
                           !presenter.AimPresented);
                         frame++)
                    {
                        yield return null;
                    }
                    bool deathSuppressed =
                        driver.LastLifecycleStateEntered ==
                            BrawlInvectorLifecyclePresentation.Death &&
                        presenter.SuppressedIKPassCount >
                            deathSuppressionsBaseline &&
                        !presenter.AimPresented;

                    presenter.PresentAim(Vector3.right);
                    int respawnSuppressionsBaseline =
                        presenter.SuppressedIKPassCount;
                    int respawnResetsBaseline = presenter.RespawnResetCount;
                    driver.PlayRespawn();
                    for (int frame = 0;
                         frame < MaximumPollFrames &&
                         !(driver.LastLifecycleStateEntered ==
                               BrawlInvectorLifecyclePresentation.Respawn &&
                           driver.LastLifecycleStateExited ==
                               BrawlInvectorLifecyclePresentation.Respawn &&
                           presenter.SuppressedIKPassCount >
                               respawnSuppressionsBaseline);
                         frame++)
                    {
                        yield return null;
                    }
                    presenter.ResetForRespawn();
                    bool respawnNormalized =
                        driver.LastLifecycleStateEntered ==
                            BrawlInvectorLifecyclePresentation.Respawn &&
                        driver.LastLifecycleStateExited ==
                            BrawlInvectorLifecyclePresentation.Respawn &&
                        presenter.SuppressedIKPassCount >
                            respawnSuppressionsBaseline &&
                        presenter.RespawnResetCount == respawnResetsBaseline + 1 &&
                        !presenter.AimPresented && presenter.Visible &&
                        muzzleEffects.All(effect => !effect.IsAlive(true));

                    presenter.PresentAim(Vector3.left);
                    int victorySuppressionsBaseline =
                        presenter.SuppressedIKPassCount;
                    driver.PlayVictory();
                    for (int frame = 0;
                         frame < MaximumPollFrames &&
                         !(driver.LastLifecycleStateEntered ==
                               BrawlInvectorLifecyclePresentation.Victory &&
                           presenter.SuppressedIKPassCount >
                               victorySuppressionsBaseline &&
                           !presenter.AimPresented);
                         frame++)
                    {
                        yield return null;
                    }
                    bool victorySuppressed =
                        driver.LastLifecycleStateEntered ==
                            BrawlInvectorLifecyclePresentation.Victory &&
                        presenter.SuppressedIKPassCount >
                            victorySuppressionsBaseline &&
                        !presenter.AimPresented;
                    lifecycleSuppressionSafe = deathSuppressed &&
                        respawnNormalized && victorySuppressed &&
                        presenter.RuntimeFaultCount == runtimeFaultBaseline;
                    lifecycleEvidence = string.Format(
                        "death={0}; respawn={1}; victory={2}; entered={3}; exited={4}; " +
                        "applied={5}; suppressed={6}; invalid={7}; aim={8}; resets={9}; " +
                        "faults={10}; last={11}",
                        deathSuppressed,
                        respawnNormalized,
                        victorySuppressed,
                        driver.LifecycleStateEnterCount,
                        driver.LifecycleStateExitCount,
                        presenter.AppliedIKPassCount,
                        presenter.SuppressedIKPassCount,
                        presenter.InvalidPoseCount,
                        presenter.AimPresented,
                        presenter.RespawnResetCount,
                        presenter.RuntimeFaultCount,
                        presenter.LastSuppression);

                    vendorResourcesInert =
                        Mathf.Approximately(controller.currentHealth, healthBaseline) &&
                        controller.isDead == deadBaseline &&
                        Mathf.Approximately(
                            controller.InternalMotorStamina, staminaBaseline) &&
                        controller.IsInternalMotorStaminaPinned &&
                        shooter.ExtraAmmo == extraAmmoBaseline &&
                        shooter.enabled == shooterEnabledBaseline &&
                        shooter.isReloadingWeapon == shooterReloadingBaseline &&
                        shooter.rWeapon == rightWeaponBaseline &&
                        shooter.lWeapon == leftWeaponBaseline &&
                        AmmoSignature(ammoManager) == ammoBaseline &&
                        ammoManager.enabled == ammoManagerEnabledBaseline &&
                        collector.enabled == collectorEnabledBaseline &&
                        melee.SuppressedAttackWindowCount == meleeWindowsBaseline &&
                        melee.BlockedDamageHitCount == meleeDamageBaseline &&
                        input.SuppressedVendorPathCount ==
                            suppressedVendorPathsBaseline;

                    int hierarchyCountBeforeClose =
                        pilot.GetComponentsInChildren<Transform>(true).Length;
                    presenter.PresentAim(Vector3.back);
                    if (muzzleResolved)
                        presenter.PresentMuzzle(muzzlePosition, Vector3.back);
                    bool presentationQueuedBeforeClose = presenter.AimPresented &&
                        muzzleEffects.Any(effect => effect.IsAlive(true));
                    gate.DeactivateLabInstance();
                    immediateCloseNormalized = presentationQueuedBeforeClose &&
                        presenter.IsDormantConfigured && !presenter.LabRuntimeEnabled &&
                        !presenter.enabled && !presenter.AimPresented &&
                        presenter.PresentedAimDirection == Vector3.zero &&
                        presenter.RuntimeHelperCount == 0 &&
                        muzzleEffects.All(effect => !effect.IsAlive(true)) &&
                        pilot.GetComponentsInChildren<Transform>(true).Length ==
                            hierarchyCountBeforeClose &&
                        !pilot.activeSelf;

                    gate.ActivateBufferedMotorPath(ReopenMoveSpeed);
                    yield return null;
                    for (int frame = 0; frame < 10; frame++)
                        yield return new WaitForFixedUpdate();
                    noDeferredPresentationAfterReopen = gate.IsLiveGateReady &&
                        presenter.LabRuntimeEnabled && presenter.isActiveAndEnabled &&
                        !presenter.AimPresented &&
                        presenter.PresentedAimDirection == Vector3.zero &&
                        presenter.AimRequestCount == 0 &&
                        presenter.MuzzlePresentationRequestCount == 0 &&
                        presenter.MuzzleEmissionCount == 0 &&
                        presenter.RuntimeHelperCount == 0 &&
                        presenter.RuntimeFaultCount == 0 &&
                        muzzleEffects.All(effect => !effect.IsAlive(true)) &&
                        driver.LifecycleStateEnterCount == 0 &&
                        driver.LifecycleStateExitCount == 0;

                    gate.DeactivateLabInstance();
                    dormantAfterShutdown = !gate.Activated && !pilot.activeSelf &&
                        presenter.IsDormantConfigured && !presenter.enabled &&
                        presenter.RuntimeHelperCount == 0 &&
                        !presenter.AimPresented &&
                        muzzleEffects.All(effect => !effect.IsAlive(true)) &&
                        !input.enabled && !driver.PresentationRequestsEnabled &&
                        input.MovementFeedMode ==
                            InvectorMovementFeedMode.LabProjectAction &&
                        input.ProjectMoveActionEnabled == actionWasProjectWide &&
                        !input.ProjectMoveActionOwnedByAdapter &&
                        body.isKinematic && !body.useGravity && !capsule.enabled;
                }

                if (gate != null && gate.Activated)
                    gate.DeactivateLabInstance();
            }
            finally
            {
                Application.logMessageReceived -= CaptureWeaponIKWarningOrError;
            }

            int warningOrErrorCount = weaponIKWarningOrErrorCount;
            string firstWarningOrError = firstWeaponIKWarningOrError;
            yield return new ExitPlayMode();

            Scene restored = SceneManager.GetSceneByPath(originalPath);
            Scene restoredLab = SceneManager.GetSceneByPath(
                InvectorPhase3BLabController.LabScenePath);
            if (restored.IsValid() && restored.isLoaded)
                SceneManager.SetActiveScene(restored);
            if (restoredLab.IsValid() && restoredLab.isLoaded)
                EditorSceneManager.CloseScene(restoredLab, true);

            Assert.That(labReady, Is.True,
                "The generated weapon/IK lab did not activate its complete visual-only stack.");
            Assert.That(projectIKDataComplete, Is.True,
                "The presenter did not use the project BrawlWizardStaff list with four complete states.");
            Assert.That(fourIKStatesApplied, Is.True,
                "Standing/crouching and aim/release did not resolve all four guarded IK states. " +
                ikEvidence);
            Assert.That(supportHandIKApplied, Is.True,
                "No clean support-hand IK pass was applied, or support-hand/fault diagnostics changed. " +
                ikEvidence);
            Assert.That(aimAndZeroReleaseWorked, Is.True,
                "PresentAim did not accept finite direction or release cleanly on Vector3.zero.");
            Assert.That(muzzleResolved, Is.True,
                "The project presenter did not resolve its configured muzzle position.");
            Assert.That(oneEmissionPerMuzzleCall, Is.True,
                "PresentMuzzle did not emit exactly one visual effect for each presentation call.");
            Assert.That(selectiveLayersValid, Is.True,
                "Root, hit proxy, weapon visual, or VFX layers violated the selective-layer contract.");
            Assert.That(forbiddenWeaponDamageComponentsAbsent, Is.True,
                "A vendor projectile, weapon, hitbox, or damage component entered the lab hierarchy.");
            Assert.That(lifecycleSuppressionSafe, Is.True,
                "Death, Respawn, or Victory did not suppress and normalize visual IK safely. " +
                lifecycleEvidence);
            Assert.That(vendorResourcesInert, Is.True,
                "Weapon presentation mutated vendor health, stamina, ammo, reload, melee, or collector state.");
            Assert.That(immediateCloseNormalized, Is.True,
                "Immediate gate close retained an aim, muzzle effect, helper, or hierarchy mutation.");
            Assert.That(noDeferredPresentationAfterReopen, Is.True,
                "Aim, muzzle, helper, or lifecycle presentation leaked through lab reopen.");
            Assert.That(dormantAfterShutdown, Is.True,
                "The reopened weapon/IK lab did not return to its dormant action-feed posture.");
            Assert.That(warningOrErrorCount, Is.Zero,
                "Weapon/IK live proof emitted a warning/error: " + firstWarningOrError);
            Assert.That(restored.IsValid() && restored.isLoaded, Is.True);
            Assert.That(restored.isDirty, Is.EqualTo(originalDirty),
                "The user's original scene dirty state changed.");
        }

        [UnityTest]
        [Category("InvectorPhase3DLifecycle")]
        public IEnumerator LiveLifecyclePresentationTransitionsAreSemanticInertAndTeardownSafe()
        {
            const int MaximumPollFrames = 600;
            const int HoldFixedFrames = 15;
            const int CapsuleStabilizationFixedFrames = 60;
            const float ReopenMoveSpeed = 4f;
            int neutralFullBodyState = 0;
            Scene original = SceneManager.GetActiveScene();
            string originalPath = original.path;
            bool originalDirty = original.isDirty;

            bool labReady = false;
            bool combatInterleavingCleared = false;
            bool deathEntered = false;
            bool deathHeld = false;
            bool respawnReturnedNeutral = false;
            bool victoryEntered = false;
            bool victoryHeld = false;
            bool lifecycleTraceCountsMatch = false;
            bool vendorResourcesInert = false;
            bool bodyCapsuleAndTransformInert = false;
            bool ammoAndManagerCountersInert = false;
            bool immediateLifecycleQueued = false;
            bool immediateCloseNormalized = false;
            bool noDeferredLifecycleAfterReopen = false;
            bool dormantAfterShutdown = false;
            bool actionWasProjectWide = false;
            string bodyCapsuleTransformEvidence = string.Empty;

            yield return new EnterPlayMode();
            lifecycleWarningOrErrorCount = 0;
            firstLifecycleWarningOrError = string.Empty;
            Application.logMessageReceived += CaptureLifecycleWarningOrError;
            try
            {
                Scene playLab = EditorSceneManager.LoadSceneInPlayMode(
                    InvectorPhase3BLabController.LabScenePath,
                    new LoadSceneParameters(LoadSceneMode.Single));
                yield return null;
                yield return new WaitForFixedUpdate();

                InvectorPhase3BLabController gate = InvectorPhase3BLabController.LiveInstance;
                GameObject pilot = gate != null ? gate.PilotRoot : null;
                Animator animator = pilot != null ? pilot.GetComponent<Animator>() : null;
                BrawlInvectorThirdPersonController controller =
                    pilot != null ? pilot.GetComponent<BrawlInvectorThirdPersonController>() : null;
                InvectorShooterMeleeInputAdapter input =
                    pilot != null ? pilot.GetComponent<InvectorShooterMeleeInputAdapter>() : null;
                InvectorBrawlerAnimationDriver driver =
                    pilot != null ? pilot.GetComponent<InvectorBrawlerAnimationDriver>() : null;
                Rigidbody body = pilot != null ? pilot.GetComponent<Rigidbody>() : null;
                CapsuleCollider capsule = pilot != null ? pilot.GetComponent<CapsuleCollider>() : null;
                vShooterManager shooter = pilot != null ? pilot.GetComponent<vShooterManager>() : null;
                vAmmoManager ammoManager = pilot != null ? pilot.GetComponent<vAmmoManager>() : null;
                BrawlInvectorMeleePresentationManager melee =
                    pilot != null ? pilot.GetComponent<BrawlInvectorMeleePresentationManager>() : null;
                int fullBodyLayer = animator != null ? animator.GetLayerIndex("FullBody") : -1;

                labReady = gate != null && playLab.IsValid() && playLab.isLoaded &&
                           gate.gameObject.scene == playLab && gate.IsLiveGateReady &&
                           pilot != null && animator != null && controller != null &&
                           input != null && driver != null && body != null && capsule != null &&
                           shooter != null && ammoManager != null && melee != null &&
                           fullBodyLayer >= 0;

                if (labReady)
                {
                    actionWasProjectWide = input.ProjectMoveActionUsesProjectWideLifecycle;
                    for (int frame = 0; frame < CapsuleStabilizationFixedFrames; frame++)
                        yield return new WaitForFixedUpdate();
                    neutralFullBodyState =
                        animator.GetCurrentAnimatorStateInfo(fullBodyLayer).fullPathHash;

                    float healthBaseline = controller.currentHealth;
                    bool deadBaseline = controller.isDead;
                    float staminaBaseline = controller.InternalMotorStamina;
                    bool animatorDeadBaseline = animator.GetBool(vAnimatorParameters.IsDead);
                    bool bodyKinematicBaseline = body.isKinematic;
                    bool bodyGravityBaseline = body.useGravity;
                    bool bodyCollisionBaseline = body.detectCollisions;
                    RigidbodyConstraints bodyConstraintsBaseline = body.constraints;
                    RigidbodyInterpolation bodyInterpolationBaseline = body.interpolation;
                    CollisionDetectionMode bodyCollisionModeBaseline =
                        body.collisionDetectionMode;
                    bool capsuleEnabledBaseline = capsule.enabled;
                    bool capsuleTriggerBaseline = capsule.isTrigger;
                    float capsuleRadiusBaseline = capsule.radius;
                    float capsuleHeightBaseline = capsule.height;
                    Vector3 capsuleCenterBaseline = capsule.center;
                    int capsuleDirectionBaseline = capsule.direction;
                    float configuredCapsuleHeightBaseline = controller.capsuleHeight;
                    float configuredCapsuleThicknessBaseline = controller.capsuleThickness;
                    Vector3 configuredCapsuleOffsetBaseline = controller.capsuleOffset;
                    float configuredStepHeightBaseline = controller.stepHeight;
                    Vector3 positionBaseline = pilot.transform.position;
                    Quaternion rotationBaseline = pilot.transform.rotation;
                    Vector3 scaleBaseline = pilot.transform.localScale;
                    int extraAmmoBaseline = shooter.ExtraAmmo;
                    bool shooterEnabledBaseline = shooter.enabled;
                    bool shooterReloadingBaseline = shooter.isReloadingWeapon;
                    UnityEngine.Object rightWeaponBaseline = shooter.rWeapon;
                    UnityEngine.Object leftWeaponBaseline = shooter.lWeapon;
                    string ammoBaseline = AmmoSignature(ammoManager);
                    bool ammoManagerEnabledBaseline = ammoManager.enabled;
                    int meleeWindowBaseline = melee.SuppressedAttackWindowCount;
                    int meleeWindowEnableBaseline = melee.SuppressedAttackWindowEnableCount;
                    int meleeWindowDisableBaseline = melee.SuppressedAttackWindowDisableCount;
                    int meleeListWindowBaseline = melee.SuppressedListAttackWindowCount;
                    int meleeSingleWindowBaseline = melee.SuppressedSingleAttackWindowCount;
                    int meleeDamageBaseline = melee.BlockedDamageHitCount;
                    int suppressedVendorPathBaseline = input.SuppressedVendorPathCount;
                    int lifecycleWritesBaseline = controller.LifecyclePresentationWriteCount;
                    int lifecycleEntersBaseline = driver.LifecycleStateEnterCount;
                    int lifecycleExitsBaseline = driver.LifecycleStateExitCount;
                    int deathRequestsBaseline = driver.DeathRequestCount;
                    int respawnRequestsBaseline = driver.RespawnRequestCount;
                    int victoryRequestsBaseline = driver.VictoryRequestCount;
                    int droppedLifecycleBaseline = driver.DroppedLifecycleRequestCount;
                    int lifecycleFaultBaseline = driver.LifecycleFaultCount;

                    driver.PlaySuper();
                    bool meleeQueued = controller.HasPendingMeleePresentationTrigger;
                    driver.PlayHitReaction();
                    bool recoilQueued = !controller.HasPendingMeleePresentationTrigger &&
                                        controller.HasPendingRecoilPresentationTrigger;
                    driver.PlayDeath();
                    combatInterleavingCleared = meleeQueued && recoilQueued &&
                        !controller.HasPendingMeleePresentationTrigger &&
                        !controller.HasPendingRecoilPresentationTrigger &&
                        controller.LastPresentationAttackId == 0 &&
                        controller.LastPresentationRecoilId == 0 &&
                        !input.isAttacking;

                    for (int frame = 0;
                         frame < MaximumPollFrames &&
                         !IsCurrentState(animator, fullBodyLayer,
                             BrawlInvectorLifecycleParameters.DeathState);
                         frame++)
                    {
                        yield return null;
                    }
                    deathEntered =
                        IsCurrentState(animator, fullBodyLayer,
                            BrawlInvectorLifecycleParameters.DeathState) &&
                        driver.LastLifecycleStateEntered ==
                            BrawlInvectorLifecyclePresentation.Death &&
                        driver.LifecycleStateEnterCount == lifecycleEntersBaseline + 1 &&
                        !controller.HasPendingLifecyclePresentationTrigger;
                    int deathEnterCount = driver.LifecycleStateEnterCount;
                    for (int frame = 0; frame < HoldFixedFrames; frame++)
                        yield return new WaitForFixedUpdate();
                    deathHeld = deathEntered &&
                        IsCurrentState(animator, fullBodyLayer,
                            BrawlInvectorLifecycleParameters.DeathState) &&
                        driver.LifecycleStateEnterCount == deathEnterCount;

                    driver.PlayRespawn();
                    for (int frame = 0;
                         frame < MaximumPollFrames &&
                         !(driver.LastLifecycleStateEntered ==
                               BrawlInvectorLifecyclePresentation.Respawn &&
                           driver.LastLifecycleStateExited ==
                               BrawlInvectorLifecyclePresentation.Respawn &&
                           IsCurrentState(animator, fullBodyLayer, neutralFullBodyState));
                         frame++)
                    {
                        yield return null;
                    }
                    respawnReturnedNeutral =
                        driver.LastLifecycleStateEntered ==
                            BrawlInvectorLifecyclePresentation.Respawn &&
                        driver.LastLifecycleStateExited ==
                            BrawlInvectorLifecyclePresentation.Respawn &&
                        IsCurrentState(animator, fullBodyLayer, neutralFullBodyState) &&
                        !controller.HasPendingLifecyclePresentationTrigger;

                    driver.PlayVictory();
                    for (int frame = 0;
                         frame < MaximumPollFrames &&
                         !IsCurrentState(animator, fullBodyLayer,
                             BrawlInvectorLifecycleParameters.VictoryState);
                         frame++)
                    {
                        yield return null;
                    }
                    victoryEntered =
                        IsCurrentState(animator, fullBodyLayer,
                            BrawlInvectorLifecycleParameters.VictoryState) &&
                        driver.LastLifecycleStateEntered ==
                            BrawlInvectorLifecyclePresentation.Victory &&
                        driver.LifecycleStateEnterCount == lifecycleEntersBaseline + 3 &&
                        !controller.HasPendingLifecyclePresentationTrigger;
                    int victoryEnterCount = driver.LifecycleStateEnterCount;
                    for (int frame = 0; frame < HoldFixedFrames; frame++)
                        yield return new WaitForFixedUpdate();
                    victoryHeld = victoryEntered &&
                        IsCurrentState(animator, fullBodyLayer,
                            BrawlInvectorLifecycleParameters.VictoryState) &&
                        driver.LifecycleStateEnterCount == victoryEnterCount;

                    lifecycleTraceCountsMatch =
                        controller.LifecyclePresentationWriteCount ==
                            lifecycleWritesBaseline + 3 &&
                        driver.DeathRequestCount == deathRequestsBaseline + 1 &&
                        driver.RespawnRequestCount == respawnRequestsBaseline + 1 &&
                        driver.VictoryRequestCount == victoryRequestsBaseline + 1 &&
                        driver.LifecycleStateEnterCount == lifecycleEntersBaseline + 3 &&
                        driver.LifecycleStateExitCount == lifecycleExitsBaseline + 2 &&
                        driver.DroppedLifecycleRequestCount == droppedLifecycleBaseline &&
                        driver.LifecycleFaultCount == lifecycleFaultBaseline &&
                        string.IsNullOrEmpty(driver.LastLifecycleFault) &&
                        driver.LastLifecycleRequest ==
                            BrawlInvectorLifecyclePresentation.Victory;

                    vendorResourcesInert =
                        Mathf.Approximately(controller.currentHealth, healthBaseline) &&
                        controller.isDead == deadBaseline &&
                        Mathf.Approximately(controller.InternalMotorStamina, staminaBaseline) &&
                        controller.IsInternalMotorStaminaPinned &&
                        animator.GetBool(vAnimatorParameters.IsDead) == animatorDeadBaseline;
                    bodyCapsuleAndTransformInert =
                        body.isKinematic == bodyKinematicBaseline &&
                        body.useGravity == bodyGravityBaseline &&
                        body.detectCollisions == bodyCollisionBaseline &&
                        body.constraints == bodyConstraintsBaseline &&
                        body.interpolation == bodyInterpolationBaseline &&
                        body.collisionDetectionMode == bodyCollisionModeBaseline &&
                        capsule.enabled == capsuleEnabledBaseline &&
                        capsule.isTrigger == capsuleTriggerBaseline &&
                        Mathf.Approximately(
                            controller.capsuleHeight, configuredCapsuleHeightBaseline) &&
                        Mathf.Approximately(
                            controller.capsuleThickness, configuredCapsuleThicknessBaseline) &&
                        controller.capsuleOffset == configuredCapsuleOffsetBaseline &&
                        Mathf.Approximately(controller.stepHeight, configuredStepHeightBaseline) &&
                        Mathf.Approximately(
                            capsule.radius, configuredCapsuleThicknessBaseline * 0.5f) &&
                        Mathf.Approximately(
                            capsule.height,
                            configuredCapsuleHeightBaseline *
                                (1f - configuredStepHeightBaseline)) &&
                        Vector3.Distance(
                            capsule.center,
                            configuredCapsuleOffsetBaseline * configuredCapsuleHeightBaseline +
                            Vector3.up * configuredStepHeightBaseline *
                                configuredCapsuleHeightBaseline * 0.5f) < 0.0001f &&
                        capsule.direction == capsuleDirectionBaseline &&
                        Vector3.Distance(pilot.transform.position, positionBaseline) < 0.02f &&
                        Quaternion.Angle(pilot.transform.rotation, rotationBaseline) < 0.1f &&
                        pilot.transform.localScale == scaleBaseline;
                    bodyCapsuleTransformEvidence = string.Format(
                        "body(k={0}/{1},g={2}/{3},c={4}/{5},constraints={6}/{7},interp={8}/{9},mode={10}/{11}); " +
                        "capsule(e={12}/{13},t={14}/{15},r={16}/{17},h={18}/{19},center={20}/{21},dir={22}/{23}); " +
                        "positionDelta={24:F6}, rotationDelta={25:F6}, scale={26}/{27}, rootMotion={28}",
                        body.isKinematic, bodyKinematicBaseline,
                        body.useGravity, bodyGravityBaseline,
                        body.detectCollisions, bodyCollisionBaseline,
                        body.constraints, bodyConstraintsBaseline,
                        body.interpolation, bodyInterpolationBaseline,
                        body.collisionDetectionMode, bodyCollisionModeBaseline,
                        capsule.enabled, capsuleEnabledBaseline,
                        capsule.isTrigger, capsuleTriggerBaseline,
                        capsule.radius, capsuleRadiusBaseline,
                        capsule.height, capsuleHeightBaseline,
                        capsule.center, capsuleCenterBaseline,
                        capsule.direction, capsuleDirectionBaseline,
                        Vector3.Distance(pilot.transform.position, positionBaseline),
                        Quaternion.Angle(pilot.transform.rotation, rotationBaseline),
                        pilot.transform.localScale, scaleBaseline,
                        animator.applyRootMotion);
                    ammoAndManagerCountersInert =
                        shooter.ExtraAmmo == extraAmmoBaseline &&
                        shooter.enabled == shooterEnabledBaseline &&
                        shooter.isReloadingWeapon == shooterReloadingBaseline &&
                        shooter.rWeapon == rightWeaponBaseline &&
                        shooter.lWeapon == leftWeaponBaseline &&
                        AmmoSignature(ammoManager) == ammoBaseline &&
                        ammoManager.enabled == ammoManagerEnabledBaseline &&
                        melee.SuppressedAttackWindowCount == meleeWindowBaseline &&
                        melee.SuppressedAttackWindowEnableCount ==
                            meleeWindowEnableBaseline &&
                        melee.SuppressedAttackWindowDisableCount ==
                            meleeWindowDisableBaseline &&
                        melee.SuppressedListAttackWindowCount == meleeListWindowBaseline &&
                        melee.SuppressedSingleAttackWindowCount == meleeSingleWindowBaseline &&
                        melee.BlockedDamageHitCount == meleeDamageBaseline &&
                        input.SuppressedVendorPathCount == suppressedVendorPathBaseline;

                    int presentationResetsBeforeImmediateClose =
                        controller.FullPresentationResetCount;
                    int markerEntersBeforeImmediateClose = driver.LifecycleStateEnterCount;
                    driver.PlayRespawn();
                    immediateLifecycleQueued =
                        controller.HasPendingLifecyclePresentationTrigger &&
                        controller.LastLifecyclePresentation ==
                            BrawlInvectorLifecyclePresentation.Respawn &&
                        controller.LifecyclePresentationWriteCount ==
                            lifecycleWritesBaseline + 4 &&
                        driver.LifecycleStateEnterCount == markerEntersBeforeImmediateClose;
                    gate.DeactivateLabInstance();
                    immediateCloseNormalized = immediateLifecycleQueued &&
                        !controller.HasPendingLifecyclePresentationTrigger &&
                        !controller.HasPendingMeleePresentationTrigger &&
                        !controller.HasPendingRecoilPresentationTrigger &&
                        controller.LastLifecyclePresentation ==
                            BrawlInvectorLifecyclePresentation.None &&
                        controller.LastPresentationAttackId == 0 &&
                        controller.LastPresentationRecoilId == 0 &&
                        controller.FullPresentationResetCount ==
                            presentationResetsBeforeImmediateClose + 1 &&
                        !driver.PresentationRequestsEnabled && !input.enabled;

                    gate.ActivateBufferedMotorPath(ReopenMoveSpeed);
                    yield return null;
                    for (int frame = 0; frame < 30; frame++)
                        yield return new WaitForFixedUpdate();
                    noDeferredLifecycleAfterReopen = gate.IsLiveGateReady &&
                        IsCurrentState(animator, fullBodyLayer, neutralFullBodyState) &&
                        driver.LifecycleStateEnterCount == 0 &&
                        driver.LifecycleStateExitCount == 0 &&
                        controller.LifecyclePresentationWriteCount == 0 &&
                        controller.LastLifecyclePresentation ==
                            BrawlInvectorLifecyclePresentation.None &&
                        !controller.HasPendingLifecyclePresentationTrigger;

                    gate.DeactivateLabInstance();
                    dormantAfterShutdown = !gate.Activated && !pilot.activeSelf &&
                        !input.enabled && !driver.PresentationRequestsEnabled &&
                        input.MovementFeedMode == InvectorMovementFeedMode.LabProjectAction &&
                        input.ProjectMoveActionEnabled == actionWasProjectWide &&
                        !input.ProjectMoveActionOwnedByAdapter &&
                        body.isKinematic && !body.useGravity && !capsule.enabled;
                }

                if (gate != null && gate.Activated)
                    gate.DeactivateLabInstance();
            }
            finally
            {
                Application.logMessageReceived -= CaptureLifecycleWarningOrError;
            }

            int warningOrErrorCount = lifecycleWarningOrErrorCount;
            string firstWarningOrError = firstLifecycleWarningOrError;
            yield return new ExitPlayMode();

            Scene restored = SceneManager.GetSceneByPath(originalPath);
            Scene restoredLab = SceneManager.GetSceneByPath(
                InvectorPhase3BLabController.LabScenePath);
            if (restored.IsValid() && restored.isLoaded)
                SceneManager.SetActiveScene(restored);
            if (restoredLab.IsValid() && restoredLab.isLoaded)
                EditorSceneManager.CloseScene(restoredLab, true);

            Assert.That(labReady, Is.True,
                "The generated lifecycle lab did not activate with its complete stack.");
            Assert.That(combatInterleavingCleared, Is.True,
                "Death did not clear pending Super/recoil presentation state.");
            Assert.That(deathEntered && deathHeld, Is.True,
                "Death did not enter and hold its project FullBody lifecycle state.");
            Assert.That(respawnReturnedNeutral, Is.True,
                "Respawn did not enter and exit to the stable neutral FullBody baseline.");
            Assert.That(victoryEntered && victoryHeld, Is.True,
                "Victory did not enter and hold its project FullBody lifecycle state.");
            Assert.That(lifecycleTraceCountsMatch, Is.True,
                "Semantic lifecycle requests, controller writes, and state markers diverged.");
            Assert.That(vendorResourcesInert, Is.True,
                "Lifecycle presentation mutated vendor health, death, or stamina state.");
            Assert.That(bodyCapsuleAndTransformInert, Is.True,
                "Lifecycle presentation mutated the Rigidbody, capsule, or actor transform. " +
                bodyCapsuleTransformEvidence);
            Assert.That(ammoAndManagerCountersInert, Is.True,
                "Lifecycle presentation mutated ammo, shooter state, or melee-manager counters.");
            Assert.That(immediateCloseNormalized, Is.True,
                "Immediate gate close retained lifecycle or combat presentation residue.");
            Assert.That(noDeferredLifecycleAfterReopen, Is.True,
                "A lifecycle trigger survived close and fired when the lab reopened.");
            Assert.That(dormantAfterShutdown, Is.True,
                "The reopened lifecycle lab did not return to its dormant action-feed posture.");
            Assert.That(warningOrErrorCount, Is.Zero,
                "Lifecycle live proof emitted a warning/error: " + firstWarningOrError);
            Assert.That(restored.IsValid() && restored.isLoaded, Is.True);
            Assert.That(restored.isDirty, Is.EqualTo(originalDirty),
                "The user's original scene dirty state changed.");
        }

        static int lifecycleWarningOrErrorCount;
        static string firstLifecycleWarningOrError = string.Empty;
        static int weaponIKWarningOrErrorCount;
        static string firstWeaponIKWarningOrError = string.Empty;

        static void CaptureLifecycleWarningOrError(
            string condition,
            string stackTrace,
            LogType type)
        {
            if (type != LogType.Warning && type != LogType.Error &&
                type != LogType.Assert && type != LogType.Exception)
            {
                return;
            }

            lifecycleWarningOrErrorCount++;
            if (string.IsNullOrEmpty(firstLifecycleWarningOrError))
                firstLifecycleWarningOrError = type + ": " + condition;
        }

        static void CaptureWeaponIKWarningOrError(
            string condition,
            string stackTrace,
            LogType type)
        {
            if (type != LogType.Warning && type != LogType.Error &&
                type != LogType.Assert && type != LogType.Exception)
            {
                return;
            }

            weaponIKWarningOrErrorCount++;
            if (string.IsNullOrEmpty(firstWeaponIKWarningOrError))
                firstWeaponIKWarningOrError = type + ": " + condition;
        }

        static bool IsFinite(Vector3 value)
        {
            return !float.IsNaN(value.x) && !float.IsInfinity(value.x) &&
                   !float.IsNaN(value.y) && !float.IsInfinity(value.y) &&
                   !float.IsNaN(value.z) && !float.IsInfinity(value.z);
        }

        static object ResolveCurrentIKAdjustForProof(
            InvectorBrawlerWeaponPresentation presenter)
        {
            MethodInfo resolver = typeof(InvectorBrawlerWeaponPresentation).GetMethod(
                "ResolveCurrentIKAdjust",
                BindingFlags.Instance | BindingFlags.NonPublic);
            return resolver != null ? resolver.Invoke(presenter, null) : null;
        }

        static bool IsForbiddenWeaponDamageComponent(string typeName)
        {
            switch (typeName)
            {
                case "vShooterWeapon":
                case "vProjectileControl":
                case "vProjectileInstantiate":
                case "vObjectDamage":
                case "vDamageSender":
                case "vDamageReceiver":
                case "vHitBox":
                case "vMeleeAttackObject":
                    return true;
                default:
                    return false;
            }
        }

        static bool IsCurrentState(Animator animator, int layer, int stateHash)
        {
            if (animator == null || layer < 0 || layer >= animator.layerCount ||
                animator.IsInTransition(layer))
            {
                return false;
            }

            return animator.GetCurrentAnimatorStateInfo(layer).fullPathHash == stateHash;
        }

        static string AmmoSignature(vAmmoManager manager)
        {
            if (manager == null || manager.ammos == null)
                return "<null>";

            var signature = new System.Text.StringBuilder();
            signature.Append(manager.ammos.Count);
            for (int i = 0; i < manager.ammos.Count; i++)
            {
                vAmmo ammo = manager.ammos[i];
                signature.Append('|');
                if (ammo == null)
                {
                    signature.Append("null");
                    continue;
                }
                signature.Append(ammo.ammoID).Append(':').Append(ammo.count);
            }
            return signature.ToString();
        }

    }

    [InitializeOnLoad]
    public static class InvectorMigrationPhase3BTestResultRecorder
    {
        public const string ResultPath = "Temp/InvectorMigrationPhase3BEditModeResults.xml";
        public const string FocusedResultPath = "Temp/InvectorMigrationPilotEditModeResults.xml";
        public const string FullEditModeResultPath = "Temp/BrawlArenaFullEditModeResults.xml";
        public const string InvectorOnlyCutoverResultPath =
            "Temp/InvectorOnlyCutoverEditModeResults.xml";
        public const string BasicAttackChargesResultPath =
            "Temp/BasicAttackChargesEditModeResults.xml";
        public const string CombatCadenceReadabilityResultPath =
            "Temp/CombatCadenceReadabilityEditModeResults.xml";
        public const string Task2CombatRegressionResultPath =
            "Temp/Task2CombatRegressionEditModeResults.xml";
        public const string ControlZoneMatchLoopResultPath =
            "Temp/ControlZoneMatchLoopEditModeResults.xml";
        public const string Task3MatchRegressionResultPath =
            "Temp/Task3MatchRegressionEditModeResults.xml";
        public const string Phase3CCBufferedMotorResultPath =
            "Temp/Phase3CCBufferedMotorEditModeResults.xml";
        public const string Phase3DBLifecycleResultPath =
            "Temp/Phase3DBLifecycleEditModeResults.xml";
        public const string Phase3DCWeaponIKResultPath =
            "Temp/Phase3DCWeaponIKEditModeResults.xml";
        public const string ProductionHumanCinderResultPath =
            "Temp/Phase3EProductionHumanCinderEditModeResults.xml";
        public const string Phase3GAIHardeningResultPath =
            "Temp/Phase3GAIHardeningEditModeResults.xml";
        public const string RimeProductionResultPath =
            "Temp/Phase4RimeProductionEditModeResults.xml";
        public const string TempestProductionResultPath =
            "Temp/Phase5TempestProductionEditModeResults.xml";
        public const string TempestCombatResultPath =
            "Temp/Phase5TempestCombatEditModeResults.xml";
        public const string TempestPresentationResultPath =
            "Temp/Phase5TempestPresentationEditModeResults.xml";
        public const string ThornProductionResultPath =
            "Temp/Phase6ThornProductionEditModeResults.xml";
        public const string ThornPresentationResultPath =
            "Temp/Phase6ThornPresentationEditModeResults.xml";
        const string TargetMethod =
            "LiveLabKeepsOneInputSchedulerAnimatorAuthorityAndReturnsDormant";
        const string Phase3CCBufferedMotorTargetMethod =
            "LiveBufferedMotorUsesOneSchedulerWithoutPhysicalInputAndReturnsDormant";
        const string Phase3DBLifecycleTargetMethod =
            "LiveLifecyclePresentationTransitionsAreSemanticInertAndTeardownSafe";
        const string Phase3DCWeaponIKTargetMethod =
            "LiveWeaponIKPresentationIsVisualOnlySelectiveAndTeardownSafe";
        const string ProductionHumanCinderTargetMethod =
            "LiveContextGatedHumanCinderPreservesBrawlAuthorityAndTeardown";
        const string Phase3GAIHardeningTargetMethod =
            "RuntimePlannerRepathsOnceThenFailsClosedWithBoundedRetry";
        const string TempestPresentationTargetMethod =
            "ProductionTempestStaff03AppliesEveryGuardedIKPoseAndClosesSafely";
        const string ThornPresentationTargetMethod =
            "ProductionThornResolvesBowRecordsAndPresentsBrawlArrowsWithoutVendorCombat";
        const string InvectorOnlyCutoverFixture =
            "BrawlArena.EditorAutomation.Tests.InvectorOnlyCutoverEditModeTests";
        const string BasicAttackChargesFixture =
            "BrawlArena.EditorAutomation.Tests.BasicAttackChargesEditModeTests";
        const string CombatCadenceReadabilityFixture =
            "BrawlArena.EditorAutomation.Tests.CombatCadenceReadabilityEditModeTests";
        const string Task2CombatRegressionFixture =
            "BrawlArena.EditorAutomation.CombatObjectPoolEditModeTests";
        const string ControlZoneMatchLoopFixture =
            "BrawlArena.EditorAutomation.Tests.ControlZoneMatchLoopEditModeTests";
        const string Task3MatchRegressionFixture =
            "BrawlArena.EditorAutomation.GameplayMechanicsEditModeTests";
        const string FocusedFixture =
            "BrawlArena.EditorAutomation.Tests.InvectorMigrationPilotEditModeTests";
        const string RimeProductionFixture =
            "BrawlArena.EditorAutomation.Tests.InvectorRimeProductionEditModeTests";
        const string TempestProductionFixture =
            "BrawlArena.EditorAutomation.Tests.InvectorTempestProductionEditModeTests";
        const string TempestCombatFixture =
            "BrawlArena.EditorAutomation.Tests.InvectorTempestCombatEditModeTests";
        const string ThornProductionFixture =
            "BrawlArena.EditorAutomation.Tests.InvectorThornProductionEditModeTests";
        const string RestoreDirtyKey = "BrawlArena.InvectorPhase3B.RestoreDirty";
        const string RestoreScenePathKey = "BrawlArena.InvectorPhase3B.RestoreScenePath";
        const string ActiveRunKindKey = "BrawlArena.InvectorPhase3B.ActiveRunKind";
        const string ResultRecordedKey = "BrawlArena.InvectorPhase3B.ResultRecorded";
        const string LiveRunKind = "live";
        const string FocusedRunKind = "focused";
        const string FullRunKind = "full";
        const string InvectorOnlyCutoverRunKind = "invector-only-cutover";
        const string BasicAttackChargesRunKind = "basic-attack-charges";
        const string CombatCadenceReadabilityRunKind =
            "combat-cadence-readability";
        const string Task2CombatRegressionRunKind = "task2-combat-regression";
        const string ControlZoneMatchLoopRunKind = "control-zone-match-loop";
        const string Task3MatchRegressionRunKind = "task3-match-regression";
        const string Phase3CCRunKind = "phase3cc";
        const string Phase3DBLifecycleRunKind = "phase3db-lifecycle";
        const string Phase3DCWeaponIKRunKind = "phase3dc-weapon-ik";
        const string ProductionHumanCinderRunKind =
            "phase3e-production-human-cinder";
        const string Phase3GAIHardeningRunKind =
            "phase3g-ai-hardening";
        const string RimeProductionRunKind =
            "phase4-rime-production";
        const string TempestProductionRunKind =
            "phase5-tempest-production";
        const string TempestCombatRunKind =
            "phase5-tempest-combat";
        const string TempestPresentationRunKind =
            "phase5-tempest-presentation";
        const string ThornProductionRunKind =
            "phase6-thorn-production";
        const string ThornPresentationRunKind =
            "phase6-thorn-presentation";

        static readonly Recorder Callback = new Recorder();
        static TestRunnerApi activeApi;

        static InvectorMigrationPhase3BTestResultRecorder()
        {
            TestRunnerApi.RegisterTestCallback(Callback, 100);
            // RunFinished can arrive while Unity is still leaving Play mode. A
            // domain reload then drops its delayCall, so resume only a
            // post-result restoration here. The false value used while a test
            // is running prevents an EnterPlayMode reload from restoring early.
            if (SessionState.GetBool(ResultRecordedKey, false))
                ScheduleDirtySceneRestore();
        }

        public static string RunSafelyAgainstCurrentScene()
        {
            return RunSafelyAgainstCurrentScene(new Filter
            {
                testMode = UnityEditor.TestTools.TestRunner.Api.TestMode.EditMode,
                categoryNames = new[] { "InvectorPhase3B" },
            }, LiveRunKind);
        }

        public static string RunFocusedPilotEditModeSafely()
        {
            return RunSafelyAgainstCurrentScene(new Filter
            {
                testMode = UnityEditor.TestTools.TestRunner.Api.TestMode.EditMode,
                testNames = new[] { FocusedFixture },
            }, FocusedRunKind);
        }

        public static string RunFullEditModeSafely()
        {
            return RunSafelyAgainstCurrentScene(new Filter
            {
                testMode = UnityEditor.TestTools.TestRunner.Api.TestMode.EditMode,
            }, FullRunKind);
        }

        public static string RunInvectorOnlyCutoverSafely()
        {
            return RunSafelyAgainstCurrentScene(new Filter
            {
                testMode = UnityEditor.TestTools.TestRunner.Api.TestMode.EditMode,
                categoryNames = new[] { "InvectorOnlyCutover" },
            }, InvectorOnlyCutoverRunKind);
        }

        public static string RunBasicAttackChargesSafely()
        {
            return RunSafelyAgainstCurrentScene(new Filter
            {
                testMode = UnityEditor.TestTools.TestRunner.Api.TestMode.EditMode,
                categoryNames = new[] { "BasicAttackCharges" },
            }, BasicAttackChargesRunKind);
        }

        public static string RunCombatCadenceReadabilitySafely()
        {
            return RunSafelyAgainstCurrentScene(new Filter
            {
                testMode = UnityEditor.TestTools.TestRunner.Api.TestMode.EditMode,
                categoryNames = new[] { "CombatCadenceReadability" },
            }, CombatCadenceReadabilityRunKind);
        }

        public static string RunTask2CombatRegressionSafely()
        {
            return RunSafelyAgainstCurrentScene(new Filter
            {
                testMode = UnityEditor.TestTools.TestRunner.Api.TestMode.EditMode,
                testNames = new[]
                {
                    Task2CombatRegressionFixture,
                    "BrawlArena.EditorAutomation.CombatRuntimeCorrectnessEditModeTests",
                    "BrawlArena.EditorAutomation.RpgCombatSliceEditModeTests",
                },
            }, Task2CombatRegressionRunKind);
        }

        public static string RunControlZoneMatchLoopSafely()
        {
            return RunSafelyAgainstCurrentScene(new Filter
            {
                testMode = UnityEditor.TestTools.TestRunner.Api.TestMode.EditMode,
                categoryNames = new[] { "ControlZoneMatchLoop" },
            }, ControlZoneMatchLoopRunKind);
        }

        public static string RunTask3MatchRegressionSafely()
        {
            return RunSafelyAgainstCurrentScene(new Filter
            {
                testMode = UnityEditor.TestTools.TestRunner.Api.TestMode.EditMode,
                testNames = new[]
                {
                    Task3MatchRegressionFixture,
                    "BrawlArena.EditorAutomation.ArenaLayoutEditModeTests",
                    "BrawlArena.EditorAutomation.BrawlerAnimationPresentationIsolationEditModeTests",
                    "BrawlArena.EditorAutomation.MatchProgressionEditModeTests",
                    "BrawlArena.EditorAutomation.MatchVictoryPresentationIsolationEditModeTests",
                    "BrawlArena.EditorAutomation.BalanceTelemetryEditModeTests",
                },
            }, Task3MatchRegressionRunKind);
        }

        public static string RunPhase3CCBufferedMotorSafely()
        {
            return RunSafelyAgainstCurrentScene(new Filter
            {
                testMode = UnityEditor.TestTools.TestRunner.Api.TestMode.EditMode,
                categoryNames = new[] { "InvectorPhase3CBufferedMotor" },
            }, Phase3CCRunKind);
        }

        public static string RunPhase3DBLifecycleSafely()
        {
            return RunSafelyAgainstCurrentScene(new Filter
            {
                testMode = UnityEditor.TestTools.TestRunner.Api.TestMode.EditMode,
                categoryNames = new[] { "InvectorPhase3DLifecycle" },
            }, Phase3DBLifecycleRunKind);
        }

        public static string RunPhase3DCWeaponIKSafely()
        {
            return RunSafelyAgainstCurrentScene(new Filter
            {
                testMode = UnityEditor.TestTools.TestRunner.Api.TestMode.EditMode,
                categoryNames = new[] { "InvectorPhase3DWeaponIK" },
            }, Phase3DCWeaponIKRunKind);
        }

        public static string RunProductionHumanCinderSafely()
        {
            return RunSafelyAgainstCurrentScene(new Filter
            {
                testMode = UnityEditor.TestTools.TestRunner.Api.TestMode.EditMode,
                categoryNames = new[] { "InvectorProductionHumanCinder" },
            }, ProductionHumanCinderRunKind);
        }

        public static string RunPhase3GAIHardeningSafely()
        {
            return RunSafelyAgainstCurrentScene(new Filter
            {
                testMode = UnityEditor.TestTools.TestRunner.Api.TestMode.EditMode,
                categoryNames = new[] { "InvectorAIHardening" },
            }, Phase3GAIHardeningRunKind);
        }

        public static string RunRimeProductionSafely()
        {
            return RunSafelyAgainstCurrentScene(new Filter
            {
                testMode = UnityEditor.TestTools.TestRunner.Api.TestMode.EditMode,
                categoryNames = new[] { "InvectorProductionRime" },
            }, RimeProductionRunKind);
        }

        public static string RunTempestProductionSafely()
        {
            return RunSafelyAgainstCurrentScene(new Filter
            {
                testMode = UnityEditor.TestTools.TestRunner.Api.TestMode.EditMode,
                categoryNames = new[] { "InvectorProductionTempest" },
            }, TempestProductionRunKind);
        }

        public static string RunTempestCombatSafely()
        {
            return RunSafelyAgainstCurrentScene(new Filter
            {
                testMode = UnityEditor.TestTools.TestRunner.Api.TestMode.EditMode,
                categoryNames = new[] { "InvectorTempestCombat" },
            }, TempestCombatRunKind);
        }

        public static string RunTempestPresentationSafely()
        {
            return RunSafelyAgainstCurrentScene(new Filter
            {
                testMode = UnityEditor.TestTools.TestRunner.Api.TestMode.EditMode,
                categoryNames = new[] { "InvectorTempestPresentation" },
            }, TempestPresentationRunKind);
        }

        public static string RunThornProductionSafely()
        {
            return RunSafelyAgainstCurrentScene(new Filter
            {
                testMode = UnityEditor.TestTools.TestRunner.Api.TestMode.EditMode,
                categoryNames = new[] { "InvectorProductionThorn" },
            }, ThornProductionRunKind);
        }

        public static string RunThornPresentationSafely()
        {
            return RunSafelyAgainstCurrentScene(new Filter
            {
                testMode = UnityEditor.TestTools.TestRunner.Api.TestMode.EditMode,
                categoryNames = new[] { "InvectorThornPresentation" },
            }, ThornPresentationRunKind);
        }

        static string RunSafelyAgainstCurrentScene(Filter filter, string runKind)
        {
            if (!string.IsNullOrEmpty(SessionState.GetString(ActiveRunKindKey, string.Empty)))
                throw new InvalidOperationException("An Invector migration test run is already awaiting dirty-scene restoration.");

            Scene original = SceneManager.GetActiveScene();
            SessionState.SetBool(RestoreDirtyKey, original.isDirty);
            SessionState.SetString(RestoreScenePathKey, original.path);
            SessionState.SetString(ActiveRunKindKey, runKind);
            SessionState.SetBool(ResultRecordedKey, false);
            if (original.isDirty)
            {
                MethodInfo clearDirtiness = typeof(EditorSceneManager).GetMethod(
                    "ClearSceneDirtiness",
                    BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
                if (clearDirtiness == null)
                    throw new MissingMethodException(nameof(EditorSceneManager), "ClearSceneDirtiness");
                clearDirtiness.Invoke(null, new object[] { original });
            }

            try
            {
                activeApi = ScriptableObject.CreateInstance<TestRunnerApi>();
                string runId = activeApi.Execute(new ExecutionSettings(filter));
                Debug.Log("INVECTOR_TEST_STARTED kind=" + runKind + " runId=" + runId);
                return runId;
            }
            catch
            {
                RestoreDirtySceneNow();
                throw;
            }
        }

        public static void RestoreDirtySceneNow()
        {
            bool restoreDirty = SessionState.GetBool(RestoreDirtyKey, false);
            string scenePath = SessionState.GetString(RestoreScenePathKey, string.Empty);
            if (!restoreDirty || string.IsNullOrEmpty(scenePath))
            {
                SessionState.EraseBool(RestoreDirtyKey);
                SessionState.EraseString(RestoreScenePathKey);
                SessionState.EraseString(ActiveRunKindKey);
                SessionState.EraseBool(ResultRecordedKey);
                EditorApplication.update -= RestoreDirtySceneWhenReady;
                activeApi = null;
                return;
            }

            // RunFinished is commonly raised after isPlaying turns false but
            // while isPlayingOrWillChangePlaymode still reflects the exit
            // transition. At that point the original scene is already loaded
            // and may be marked safely; deferring there can strand SessionState
            // because the pending delayCall is lost to the final domain reload.
            if (EditorApplication.isPlaying)
            {
                ScheduleDirtySceneRestore();
                return;
            }

            Scene scene = SceneManager.GetSceneByPath(scenePath);
            if (!scene.IsValid() || !scene.isLoaded)
            {
                ScheduleDirtySceneRestore();
                return;
            }

            EditorSceneManager.MarkSceneDirty(scene);
            SessionState.EraseBool(RestoreDirtyKey);
            SessionState.EraseString(RestoreScenePathKey);
            SessionState.EraseString(ActiveRunKindKey);
            SessionState.EraseBool(ResultRecordedKey);
            EditorApplication.update -= RestoreDirtySceneWhenReady;
            activeApi = null;
        }

        static void ScheduleDirtySceneRestore()
        {
            // Re-adding a delayCall from inside itself can be discarded when
            // Unity clears that invocation list. EditorApplication.update
            // survives ordinary frames, and the static constructor re-arms it
            // from SessionState after a domain reload.
            EditorApplication.update -= RestoreDirtySceneWhenReady;
            EditorApplication.update += RestoreDirtySceneWhenReady;
        }

        static void RestoreDirtySceneWhenReady()
        {
            if (EditorApplication.isPlaying) return;
            EditorApplication.update -= RestoreDirtySceneWhenReady;
            RestoreDirtySceneNow();
        }

        sealed class Recorder : IErrorCallbacks
        {
            public void RunStarted(ITestAdaptor testsToRun) { }
            public void TestStarted(ITestAdaptor test) { }
            public void TestFinished(ITestResultAdaptor result) { }

            public void RunFinished(ITestResultAdaptor result)
            {
                try
                {
                    string runKind = SessionState.GetString(ActiveRunKindKey, string.Empty);
                    string target = ResolveTarget(runKind);
                    if (!string.IsNullOrEmpty(runKind) &&
                        (runKind == FullRunKind || ContainsTarget(result, target)))
                    {
                        string path = ResolveResultPath(runKind);
                        Directory.CreateDirectory(Path.GetDirectoryName(path));
                        TestRunnerApi.SaveResultToFile(result, path);
                        Debug.Log(string.Format(
                            "INVECTOR_TEST_RESULT kind={0} pass={1} fail={2} skip={3} inconclusive={4} duration={5:F3}s path={6}",
                            runKind,
                            result.PassCount,
                            result.FailCount,
                            result.SkipCount,
                            result.InconclusiveCount,
                            result.Duration,
                            path));
                        SessionState.SetBool(ResultRecordedKey, true);
                    }
                }
                finally
                {
                    RestoreDirtySceneNow();
                }
            }

            public void OnError(string message)
            {
                SessionState.SetBool(ResultRecordedKey, true);
                RestoreDirtySceneNow();
                Debug.LogError("PHASE3B_TEST_ERROR " + message);
            }

            static string ResolveTarget(string runKind)
            {
                if (runKind == FocusedRunKind) return FocusedFixture;
                if (runKind == InvectorOnlyCutoverRunKind) return InvectorOnlyCutoverFixture;
                if (runKind == BasicAttackChargesRunKind) return BasicAttackChargesFixture;
                if (runKind == CombatCadenceReadabilityRunKind)
                    return CombatCadenceReadabilityFixture;
                if (runKind == Task2CombatRegressionRunKind)
                    return Task2CombatRegressionFixture;
                if (runKind == ControlZoneMatchLoopRunKind)
                    return ControlZoneMatchLoopFixture;
                if (runKind == Task3MatchRegressionRunKind)
                    return Task3MatchRegressionFixture;
                if (runKind == Phase3CCRunKind) return Phase3CCBufferedMotorTargetMethod;
                if (runKind == Phase3DBLifecycleRunKind) return Phase3DBLifecycleTargetMethod;
                if (runKind == Phase3DCWeaponIKRunKind) return Phase3DCWeaponIKTargetMethod;
                if (runKind == ProductionHumanCinderRunKind) return ProductionHumanCinderTargetMethod;
                if (runKind == Phase3GAIHardeningRunKind) return Phase3GAIHardeningTargetMethod;
                if (runKind == RimeProductionRunKind) return RimeProductionFixture;
                if (runKind == TempestProductionRunKind) return TempestProductionFixture;
                if (runKind == TempestCombatRunKind) return TempestCombatFixture;
                if (runKind == TempestPresentationRunKind) return TempestPresentationTargetMethod;
                if (runKind == ThornProductionRunKind) return ThornProductionFixture;
                if (runKind == ThornPresentationRunKind) return ThornPresentationTargetMethod;
                return TargetMethod;
            }

            static string ResolveResultPath(string runKind)
            {
                if (runKind == FullRunKind) return FullEditModeResultPath;
                if (runKind == FocusedRunKind) return FocusedResultPath;
                if (runKind == InvectorOnlyCutoverRunKind) return InvectorOnlyCutoverResultPath;
                if (runKind == BasicAttackChargesRunKind) return BasicAttackChargesResultPath;
                if (runKind == CombatCadenceReadabilityRunKind)
                    return CombatCadenceReadabilityResultPath;
                if (runKind == Task2CombatRegressionRunKind)
                    return Task2CombatRegressionResultPath;
                if (runKind == ControlZoneMatchLoopRunKind)
                    return ControlZoneMatchLoopResultPath;
                if (runKind == Task3MatchRegressionRunKind)
                    return Task3MatchRegressionResultPath;
                if (runKind == Phase3CCRunKind) return Phase3CCBufferedMotorResultPath;
                if (runKind == Phase3DBLifecycleRunKind) return Phase3DBLifecycleResultPath;
                if (runKind == Phase3DCWeaponIKRunKind) return Phase3DCWeaponIKResultPath;
                if (runKind == ProductionHumanCinderRunKind) return ProductionHumanCinderResultPath;
                if (runKind == Phase3GAIHardeningRunKind) return Phase3GAIHardeningResultPath;
                if (runKind == RimeProductionRunKind) return RimeProductionResultPath;
                if (runKind == TempestProductionRunKind) return TempestProductionResultPath;
                if (runKind == TempestCombatRunKind) return TempestCombatResultPath;
                if (runKind == TempestPresentationRunKind) return TempestPresentationResultPath;
                if (runKind == ThornProductionRunKind) return ThornProductionResultPath;
                if (runKind == ThornPresentationRunKind) return ThornPresentationResultPath;
                return ResultPath;
            }

            static bool ContainsTarget(ITestResultAdaptor result, string target)
            {
                if (result.FullName.EndsWith(target, StringComparison.Ordinal))
                    return true;
                return result.HasChildren && result.Children.Any(child => ContainsTarget(child, target));
            }
        }
    }
}
