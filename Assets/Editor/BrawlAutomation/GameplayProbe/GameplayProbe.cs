using System;
using System.Collections.Generic;
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
        const string PendingPlayScenarioKey =
            "BrawlArena.GameplayProbe.PendingPlayScenario";
        internal const string PendingPlayCharacterIndexKey =
            "BrawlArena.GameplayProbe.PendingPlayCharacterIndex";

        [Serializable]
        sealed class FailureMarker
        {
            public bool completed;
            public string error;
        }

        static string scenarioJson;
        static string outputDir;
        static ScriptedBrawlerDriver driver;
        static GameplayProbeRecorder recorder;
        static readonly List<AIBrawler> suspendedAIs = new List<AIBrawler>();
        static Health protectedHealth;
        static bool previousInvulnerable;
        static string rosterId;
        static bool relaxedPresentationGate;
        static BrawlerController securedCandidate;
        static BrawlerController readyCandidate;
        static double readySince = -1.0;
        static double finishedAt;
        static bool running;

        [InitializeOnLoadMethod]
        static void InitializePendingPlay()
        {
            EditorApplication.playModeStateChanged -= OnPlayModeStateChanged;
            EditorApplication.playModeStateChanged += OnPlayModeStateChanged;
        }

        public static void RunOnNextPlay(string scenarioPath)
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
                throw new InvalidOperationException(
                    "A fresh gameplay probe must be armed from Edit Mode.");
            if (string.IsNullOrWhiteSpace(scenarioPath) || !File.Exists(scenarioPath))
                throw new FileNotFoundException(
                    "The fresh gameplay probe scenario is unavailable.", scenarioPath);
            int characterIndex = ResolveRequestedRosterIndex(scenarioPath);
            if (characterIndex >= 0)
            {
                MatchSetup.CharacterIndex = characterIndex;
                MatchSetup.FromMenu = true;
                SessionState.SetInt(
                    PendingPlayCharacterIndexKey, characterIndex);
            }
            else
            {
                SessionState.EraseInt(PendingPlayCharacterIndexKey);
            }
            SessionState.SetString(PendingPlayScenarioKey, scenarioPath);
            EditorApplication.EnterPlaymode();
        }

        static void OnPlayModeStateChanged(PlayModeStateChange state)
        {
            if (state != PlayModeStateChange.EnteredPlayMode) return;
            string scenarioPath = SessionState.GetString(
                PendingPlayScenarioKey, string.Empty);
            if (string.IsNullOrEmpty(scenarioPath)) return;
            SessionState.EraseString(PendingPlayScenarioKey);
            SessionState.EraseInt(PendingPlayCharacterIndexKey);
            Run(scenarioPath);
        }

        static int ResolveRequestedRosterIndex(string scenarioPath)
        {
            var scenario = JsonUtility.FromJson<ScriptedBrawlerDriver.ProbeScenario>(
                File.ReadAllText(scenarioPath));
            if (scenario == null || string.IsNullOrWhiteSpace(scenario.rosterId))
                return -1;

            GameFlow flow = UnityEngine.Object.FindFirstObjectByType<GameFlow>(
                FindObjectsInactive.Include);
            if (flow == null || flow.roster == null)
            {
                throw new InvalidOperationException(
                    "A fresh roster-specific gameplay probe requires the Arena GameFlow in Edit Mode.");
            }
            for (int i = 0; i < flow.roster.Length; i++)
            {
                BrawlerDefinition definition = flow.roster[i];
                if (definition == null || definition.id != scenario.rosterId)
                    continue;
                return i;
            }

            throw new InvalidOperationException(
                "The fresh gameplay probe requested roster id '" +
                scenario.rosterId + "', which is not present in the active GameFlow roster.");
        }

        public static void Run(string scenarioPath)
        {
            if (!EditorApplication.isPlaying)
                throw new InvalidOperationException("GameplayProbe requires play mode.");
            if (running)
                throw new InvalidOperationException("GameplayProbe already running.");

            scenarioJson = File.ReadAllText(scenarioPath);
            var scenario = JsonUtility.FromJson<ScriptedBrawlerDriver.ProbeScenario>(scenarioJson);
            rosterId = scenario != null ? scenario.rosterId : string.Empty;
            relaxedPresentationGate = scenario != null && scenario.relaxedPresentationGate;
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
                BrawlerController bluePrimary = brawlers.FirstOrDefault(b =>
                    b != null && b.team == TeamId.Blue && b.MatchSpawnSlot == 0);
                if (bluePrimary == null) return;
                if (!MatchesRoster(bluePrimary, rosterId))
                {
                    string actualRoster = ResolveRosterId(bluePrimary)
                        ?? "<missing identity>";
                    FailProbe(
                        "The requested roster '" + rosterId +
                        "' was not pinned to Blue primary slot 0; actual roster is '" +
                        actualRoster + "'.");
                    return;
                }

                var player = brawlers.FirstOrDefault(b =>
                        MatchesRoster(b, rosterId) && b != null && b.IsPlayer &&
                        !b.IsDead && !b.IsRespawning)
                    ?? (!bluePrimary.IsDead && !bluePrimary.IsRespawning
                        ? bluePrimary
                        : null);
                if (player == null) return;
                SecureCandidate(player, brawlers);

                // Heavy actors expose no IK proof surface; any installed
                // IBrawlerWeaponPresentation counts as presentation-ready
                // once the brawler can act.
                bool presentationReady = relaxedPresentationGate
                    ? player.CanAct
                    : player.CanAct &&
                      player.GetComponent<IBrawlerWeaponPresentation>() != null;
                if (!presentationReady)
                {
                    readyCandidate = null;
                    readySince = -1.0;
                    return;
                }
                if (readyCandidate != player)
                {
                    readyCandidate = player;
                    readySince = EditorApplication.timeSinceStartup;
                    return;
                }
                if (EditorApplication.timeSinceStartup - readySince < 0.5)
                    return;

                try
                {
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
            for (int i = 0; i < suspendedAIs.Count; i++)
                if (suspendedAIs[i] != null) suspendedAIs[i].enabled = true;
            suspendedAIs.Clear();
            if (protectedHealth != null)
                protectedHealth.Invulnerable = previousInvulnerable;
            if (completed && outputDir != null)
                File.WriteAllText(Path.Combine(outputDir, "done.json"), "{\"completed\":true}");
            Debug.Log("[GameplayProbe] " + (completed
                ? "completed, output=" + outputDir
                : "aborted (play mode ended or error)"));
            driver = null;
            recorder = null;
            protectedHealth = null;
            previousInvulnerable = false;
            readyCandidate = null;
            securedCandidate = null;
            readySince = -1.0;
            running = false;
            rosterId = string.Empty;
            RemoveAutopilotFlag();
        }

        static void FailProbe(string message)
        {
            Debug.LogError("[GameplayProbe] " + message);
            if (!string.IsNullOrEmpty(outputDir))
            {
                Directory.CreateDirectory(outputDir);
                File.WriteAllText(
                    Path.Combine(outputDir, "failure.json"),
                    JsonUtility.ToJson(new FailureMarker
                    {
                        completed = false,
                        error = message,
                    }, true));
            }
            Cleanup(false);
        }

        static void SecureCandidate(
            BrawlerController player,
            IReadOnlyList<BrawlerController> brawlers)
        {
            if (securedCandidate == player) return;
            if (securedCandidate != null)
                throw new InvalidOperationException(
                    "GameplayProbe candidate changed after takeover protection began.");

            protectedHealth = player.GetComponent<Health>();
            if (protectedHealth != null)
            {
                previousInvulnerable = protectedHealth.Invulnerable;
                protectedHealth.Invulnerable = true;
            }
            player.SetMoveInput(Vector3.zero);

            suspendedAIs.Clear();
            for (int i = 0; i < brawlers.Count; i++)
            {
                BrawlerController brawler = brawlers[i];
                if (brawler == null) continue;
                AIBrawler ai = brawler.GetComponent<AIBrawler>();
                if (ai == null || !ai.enabled) continue;
                suspendedAIs.Add(ai);
                ai.enabled = false;
            }
            securedCandidate = player;
        }

        static void RemoveAutopilotFlag()
        {
            string flagPath = Path.Combine(
                Directory.GetParent(Application.dataPath).FullName,
                "Automation", "autopilot.flag");
            if (File.Exists(flagPath)) File.Delete(flagPath);
        }

        static bool MatchesRoster(BrawlerController actor, string requestedRosterId)
        {
            if (actor == null || string.IsNullOrWhiteSpace(requestedRosterId))
                return true;
            return ResolveRosterId(actor) == requestedRosterId;
        }

        /// <summary>
        /// Roster lookup from the generated heavy prefab identity. Null when
        /// no identity exists on the actor root.
        /// </summary>
        static string ResolveRosterId(BrawlerController actor)
        {
            if (actor == null) return null;
            HeavyBrawlerIdentity identity =
                actor.GetComponent<HeavyBrawlerIdentity>();
            if (identity != null && !string.IsNullOrWhiteSpace(identity.heroId))
                return identity.heroId;
            return null;
        }
    }
}
