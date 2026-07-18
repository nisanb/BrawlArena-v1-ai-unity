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
    // The Cinder-only Phase 3B/3C/3D lab-scene PlayMode tests were retired
    // with the fire roster slot and its dormant lab pilot. This file now
    // hosts only the shared harness test-result recorder used by the
    // automation runner's run_invector_test command across every roster.
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
