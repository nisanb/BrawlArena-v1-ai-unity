using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace BrawlArena.EditorAutomation
{
    /// <summary>
    /// File-based automation channel for driving the editor without an MCP
    /// connection: polls the project-root Automation/command.json (outside
    /// Assets, so no importing is involved) and executes known actions,
    /// writing result files when done. The last processed command id persists
    /// on disk so commands never re-run across domain reloads or play mode.
    /// </summary>
    [InitializeOnLoad]
    public static class AutomationRunner
    {
        static readonly string Dir = Path.Combine(Directory.GetParent(Application.dataPath).FullName, "Automation");
        static string CommandPath => Path.Combine(Dir, "command.json");
        static string LastIdPath => Path.Combine(Dir, ".last-id");

        static readonly List<string> LogBuffer = new List<string>();
        static double nextPoll;

        static AutomationRunner()
        {
            Directory.CreateDirectory(Dir);
            Application.logMessageReceivedThreaded += OnLog;
            EditorApplication.update += Poll;
        }

        static void OnLog(string condition, string stackTrace, LogType type)
        {
            if (type == LogType.Log) return;
            lock (LogBuffer)
            {
                if (LogBuffer.Count < 400) LogBuffer.Add($"[{type}] {condition}");
            }
        }

        static void Poll()
        {
            if (EditorApplication.timeSinceStartup < nextPoll) return;
            nextPoll = EditorApplication.timeSinceStartup + 0.5;
            if (EditorApplication.isCompiling) return;
            try
            {
                ProcessCommandFile();
            }
            catch (Exception e)
            {
                Debug.LogError("[Automation] runner failure: " + e);
            }
        }

        static void ProcessCommandFile()
        {
            if (!File.Exists(CommandPath)) return;
            Command cmd;
            try
            {
                cmd = JsonUtility.FromJson<Command>(File.ReadAllText(CommandPath));
            }
            catch
            {
                return; // partially written file; retry next poll
            }
            if (cmd == null || string.IsNullOrEmpty(cmd.id)) return;

            string lastId = File.Exists(LastIdPath) ? File.ReadAllText(LastIdPath).Trim() : "";
            if (cmd.id == lastId) return;
            File.WriteAllText(LastIdPath, cmd.id);

            var result = new Result { id = cmd.id, action = cmd.action, ok = true, message = "" };
            try
            {
                Execute(cmd, result);
            }
            catch (Exception e)
            {
                result.ok = false;
                result.message = e.ToString();
            }
            lock (LogBuffer)
            {
                result.consoleWarningsAndErrors = LogBuffer.ToArray();
            }
            result.isPlaying = EditorApplication.isPlaying;
            File.WriteAllText(Path.Combine(Dir, $"result-{cmd.id}.json"), JsonUtility.ToJson(result, true));
        }

        static void Execute(Command cmd, Result result)
        {
            switch (cmd.action)
            {
                case "ping":
                    result.message = $"Unity {Application.unityVersion}, playing={EditorApplication.isPlaying}, scene={UnityEngine.SceneManagement.SceneManager.GetActiveScene().name}";
                    break;
                case "clear_console_buffer":
                    lock (LogBuffer) LogBuffer.Clear();
                    result.message = "cleared";
                    break;
                case "urp_convert":
                    result.message = ArenaSceneBuilder.ConvertMaterialsToUrp();
                    break;
                case "build_scene":
                    result.message = ArenaSceneBuilder.BuildArenaScene();
                    break;
                case "screenshot_scene":
                    result.message = ArenaSceneBuilder.CaptureSceneOverview();
                    break;
                case "enter_play":
                    EditorApplication.EnterPlaymode();
                    result.message = "entering play mode";
                    break;
                case "play_test":
                    // Autopilot flag: GameFlow auto-picks a character and the
                    // player brawler is bot-driven, so the match runs unattended.
                    File.WriteAllText(Path.Combine(Dir, "autopilot.flag"), "1");
                    EditorApplication.EnterPlaymode();
                    result.message = "entering play mode with autopilot";
                    break;
                case "exit_play":
                {
                    string flag = Path.Combine(Dir, "autopilot.flag");
                    if (File.Exists(flag)) File.Delete(flag);
                    EditorApplication.ExitPlaymode();
                    result.message = "exiting play mode";
                    break;
                }
                case "status":
                    result.message = StatusDump();
                    break;
                case "game_screenshot":
                {
                    string file = Path.Combine(Dir, string.IsNullOrEmpty(cmd.arg) ? "game.png" : cmd.arg);
                    ScreenCapture.CaptureScreenshot(file);
                    result.message = "capturing to " + file + " (written a frame later)";
                    break;
                }
                default:
                    result.ok = false;
                    result.message = "unknown action: " + cmd.action;
                    break;
            }
        }

        static string StatusDump()
        {
            var sb = new System.Text.StringBuilder();
            sb.AppendLine($"playing={EditorApplication.isPlaying} compiling={EditorApplication.isCompiling}");
            var mm = MatchManager.Instance;
            if (mm != null)
                sb.AppendLine($"match={mm.State} time={mm.TimeRemaining:0} blue={mm.BlueScore} red={mm.RedScore}");
            foreach (var b in UnityEngine.Object.FindObjectsByType<BrawlerController>(FindObjectsSortMode.None))
            {
                var anim = b.GetComponentInChildren<Animator>();
                string clip = "?";
                if (anim != null && anim.isActiveAndEnabled && anim.runtimeAnimatorController != null)
                {
                    var clips = anim.GetCurrentAnimatorClipInfo(0);
                    clip = clips.Length > 0 ? clips[0].clip.name : "(none)";
                }
                Vector3 p = b.transform.position;
                sb.AppendLine(
                    $"{b.displayName} [{b.team}]{(b.IsPlayer ? " PLAYER" : "")} hp={b.Health.Current:0}/{b.Health.Max:0} " +
                    $"stam={b.Stamina:0} pos=({p.x:0.0},{p.z:0.0}) anim={clip} dead={b.IsDead}");
            }
            return sb.ToString();
        }

        [Serializable]
        class Command
        {
            public string id;
            public string action;
            public string arg;
        }

        [Serializable]
        class Result
        {
            public string id;
            public string action;
            public bool ok;
            public bool isPlaying;
            public string message;
            public string[] consoleWarningsAndErrors;
        }
    }
}
