using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace BrawlArena.EditorAutomation
{
    /// <summary>
    /// One-call gameplay probe. While a match is running (autopilot is fine),
    /// takes over the player brawler with a ScriptedBrawlerDriver, records
    /// contact sheets and telemetry via GameplayProbeRecorder, then restores
    /// AI control and writes a done marker. Never reads physical input.
    /// </summary>
    public static class GameplayProbe
    {
        static string scenarioJson;
        static string outputDir;
        static ScriptedBrawlerDriver driver;
        static GameplayProbeRecorder recorder;
        static AIBrawler suspendedAI;
        static double finishedAt;
        static bool running;

        public static void Run(string scenarioPath)
        {
            if (!EditorApplication.isPlaying)
                throw new InvalidOperationException("GameplayProbe requires play mode.");
            if (running)
                throw new InvalidOperationException("GameplayProbe already running.");

            scenarioJson = File.ReadAllText(scenarioPath);
            var scenario = JsonUtility.FromJson<ScriptedBrawlerDriver.ProbeScenario>(scenarioJson);
            string name = scenario != null && !string.IsNullOrEmpty(scenario.name)
                ? scenario.name
                : Path.GetFileNameWithoutExtension(scenarioPath);
            outputDir = Path.Combine(
                Directory.GetParent(Application.dataPath).FullName,
                "Automation", "probe_" + name);
            if (Directory.Exists(outputDir)) Directory.Delete(outputDir, true);
            Directory.CreateDirectory(outputDir);

            running = true;
            finishedAt = -1.0;
            EditorApplication.update += Tick;
            Debug.Log("[GameplayProbe] armed '" + name + "', output=" + outputDir);
        }

        static void Tick()
        {
            if (!EditorApplication.isPlaying)
            {
                Cleanup(false);
                return;
            }

            if (driver == null)
            {
                var mm = MatchManager.Instance;
                if (mm == null || !mm.IsCombatActive) return;
                // Autopilot matches have no IsPlayer brawler; fall back to the
                // seat the human would occupy (Blue primary slot 0).
                var brawlers = mm.GetBrawlers();
                var player = brawlers.FirstOrDefault(b =>
                        b != null && b.IsPlayer && !b.IsDead && !b.IsRespawning)
                    ?? brawlers.FirstOrDefault(b =>
                        b != null && b.team == TeamId.Blue && b.MatchSpawnSlot == 0 &&
                        !b.IsDead && !b.IsRespawning);
                if (player == null) return;

                try
                {
                    suspendedAI = player.GetComponent<AIBrawler>();
                    if (suspendedAI != null) suspendedAI.enabled = false;

                    driver = player.gameObject.AddComponent<ScriptedBrawlerDriver>();
                    if (driver == null)
                        throw new InvalidOperationException(
                            "AddComponent<ScriptedBrawlerDriver> returned null.");
                    driver.LoadScenario(scenarioJson);
                    recorder = player.gameObject.AddComponent<GameplayProbeRecorder>();
                    recorder.subject = player;
                    recorder.StartRecording(outputDir);
                    Debug.Log("[GameplayProbe] took over player '" + player.name + "'");
                }
                catch (Exception e)
                {
                    Debug.LogError("[GameplayProbe] takeover failed: " + e.Message);
                    Cleanup(false);
                }
                return;
            }

            if (driver.IsFinished && finishedAt < 0.0)
                finishedAt = EditorApplication.timeSinceStartup;
            if (finishedAt > 0.0 && EditorApplication.timeSinceStartup - finishedAt > 1.0)
                Cleanup(true);
        }

        static void Cleanup(bool completed)
        {
            EditorApplication.update -= Tick;
            if (recorder != null)
            {
                recorder.StopRecording();
                UnityEngine.Object.Destroy(recorder);
            }
            if (driver != null) UnityEngine.Object.Destroy(driver);
            if (suspendedAI != null) suspendedAI.enabled = true;
            if (completed && outputDir != null)
                File.WriteAllText(Path.Combine(outputDir, "done.json"), "{\"completed\":true}");
            Debug.Log("[GameplayProbe] " + (completed
                ? "completed, output=" + outputDir
                : "aborted (play mode ended or error)"));
            driver = null;
            recorder = null;
            suspendedAI = null;
            running = false;
        }
    }
}
