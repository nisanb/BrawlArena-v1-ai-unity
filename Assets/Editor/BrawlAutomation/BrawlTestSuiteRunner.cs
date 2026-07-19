using System;
using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEditor.TestTools.TestRunner.Api;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace BrawlArena.EditorAutomation
{
    /// <summary>
    /// NUnit suite runner behind the automation channel's run_test_suite
    /// command (run_invector_test remains a legacy alias). Snapshots the
    /// active scene's dirty state through SessionState so both the recorded
    /// result and the dirty-flag restoration survive the domain reloads the
    /// Unity TestRunner performs mid-run.
    /// </summary>
    [InitializeOnLoad]
    public static class BrawlTestSuiteRunner
    {
        public const string FullEditModeSuite = "full-editmode";
        // Path is load-bearing harness surface: verification tooling reads
        // Temp/BrawlArenaFullEditModeResults.xml after a full-editmode run.
        public const string FullEditModeResultPath =
            "Temp/BrawlArenaFullEditModeResults.xml";

        const string RestoreDirtyKey = "BrawlArena.BrawlTestSuite.RestoreDirty";
        const string RestoreScenePathKey = "BrawlArena.BrawlTestSuite.RestoreScenePath";
        const string ActiveSuiteKey = "BrawlArena.BrawlTestSuite.ActiveSuite";
        const string ResultRecordedKey = "BrawlArena.BrawlTestSuite.ResultRecorded";
        const string CategoryPrefix = "category:";
        const string FixturePrefix = "fixture:";

        static readonly Recorder Callback = new Recorder();
        static TestRunnerApi activeApi;

        static BrawlTestSuiteRunner()
        {
            TestRunnerApi.RegisterTestCallback(Callback, 100);
            // RunFinished can arrive while Unity is still leaving Play mode. A
            // domain reload then drops its delayCall, so resume only a
            // post-result restoration here. The false value used while a run
            // is active prevents an EnterPlayMode reload from restoring early.
            if (SessionState.GetBool(ResultRecordedKey, false))
                ScheduleDirtySceneRestore();
        }

        /// <summary>
        /// Starts an EditMode test run. Supported suites: "full-editmode"
        /// (default when empty) runs every EditMode test; "category:&lt;Name&gt;"
        /// filters by NUnit category; "fixture:&lt;FullTypeName&gt;" runs one
        /// fixture. Results are written to <see cref="ResolveResultPath"/>.
        /// </summary>
        public static string Run(string suite)
        {
            string requested = string.IsNullOrWhiteSpace(suite)
                ? FullEditModeSuite
                : suite.Trim();
            Filter filter = ResolveFilter(requested);

            if (!string.IsNullOrEmpty(SessionState.GetString(ActiveSuiteKey, string.Empty)))
                throw new InvalidOperationException(
                    "A test-suite run is already awaiting dirty-scene restoration.");

            Scene original = SceneManager.GetActiveScene();
            SessionState.SetBool(RestoreDirtyKey, original.isDirty);
            SessionState.SetString(RestoreScenePathKey, original.path);
            SessionState.SetString(ActiveSuiteKey, requested);
            SessionState.SetBool(ResultRecordedKey, false);
            if (original.isDirty)
            {
                MethodInfo clearDirtiness = typeof(EditorSceneManager).GetMethod(
                    "ClearSceneDirtiness",
                    BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
                if (clearDirtiness == null)
                    throw new MissingMethodException(
                        nameof(EditorSceneManager), "ClearSceneDirtiness");
                clearDirtiness.Invoke(null, new object[] { original });
            }

            try
            {
                activeApi = ScriptableObject.CreateInstance<TestRunnerApi>();
                string runId = activeApi.Execute(new ExecutionSettings(filter));
                Debug.Log("BRAWL_TEST_SUITE_STARTED suite=" + requested +
                          " runId=" + runId);
                return "suite '" + requested + "' started, runId=" + runId +
                       ", results land at " + ResolveResultPath(requested);
            }
            catch
            {
                RestoreDirtySceneNow();
                throw;
            }
        }

        static Filter ResolveFilter(string suite)
        {
            if (suite == FullEditModeSuite)
                return new Filter { testMode = TestMode.EditMode };
            if (suite.StartsWith(CategoryPrefix, StringComparison.Ordinal))
            {
                string category = suite.Substring(CategoryPrefix.Length);
                if (string.IsNullOrWhiteSpace(category))
                    throw new ArgumentException(
                        "The category suite requires a NUnit category name.");
                return new Filter
                {
                    testMode = TestMode.EditMode,
                    categoryNames = new[] { category },
                };
            }
            if (suite.StartsWith(FixturePrefix, StringComparison.Ordinal))
            {
                string fixture = suite.Substring(FixturePrefix.Length);
                if (string.IsNullOrWhiteSpace(fixture))
                    throw new ArgumentException(
                        "The fixture suite requires a fixture full type name.");
                return new Filter
                {
                    testMode = TestMode.EditMode,
                    testNames = new[] { fixture },
                };
            }
            throw new ArgumentException(
                "unknown test suite '" + suite + "'; supported: " +
                FullEditModeSuite + ", " + CategoryPrefix + "<NUnitCategory>, " +
                FixturePrefix + "<FixtureFullName>");
        }

        public static string ResolveResultPath(string suite)
        {
            if (suite == FullEditModeSuite) return FullEditModeResultPath;
            var sb = new System.Text.StringBuilder("Temp/BrawlArenaSuite-");
            foreach (char c in suite)
            {
                sb.Append(char.IsLetterOrDigit(c) || c == '-' || c == '_'
                    ? c
                    : '-');
            }
            sb.Append("Results.xml");
            return sb.ToString();
        }

        public static void RestoreDirtySceneNow()
        {
            bool restoreDirty = SessionState.GetBool(RestoreDirtyKey, false);
            string scenePath = SessionState.GetString(RestoreScenePathKey, string.Empty);
            if (!restoreDirty || string.IsNullOrEmpty(scenePath))
            {
                ClearSessionState();
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
            ClearSessionState();
        }

        static void ClearSessionState()
        {
            SessionState.EraseBool(RestoreDirtyKey);
            SessionState.EraseString(RestoreScenePathKey);
            SessionState.EraseString(ActiveSuiteKey);
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
                    string suite = SessionState.GetString(ActiveSuiteKey, string.Empty);
                    if (!string.IsNullOrEmpty(suite))
                    {
                        string path = ResolveResultPath(suite);
                        Directory.CreateDirectory(Path.GetDirectoryName(path));
                        TestRunnerApi.SaveResultToFile(result, path);
                        Debug.Log(string.Format(
                            "BRAWL_TEST_SUITE_RESULT suite={0} pass={1} fail={2} skip={3} inconclusive={4} duration={5:F3}s path={6}",
                            suite,
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
                Debug.LogError("BRAWL_TEST_SUITE_ERROR " + message);
            }
        }
    }
}
