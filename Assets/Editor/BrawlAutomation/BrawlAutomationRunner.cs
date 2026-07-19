using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
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
        static int lastPumpFrame = -1;
        static int stallTicks;
        static MenuReviewSession menuReviewSession;

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
            // Keep the game simulating while the editor is unfocused. In a
            // background editor QueuePlayerLoopUpdate is ignored, so when
            // frames stall during an autopilot run, force them with Step().
            if (EditorApplication.isPlaying)
            {
                bool autopilot = File.Exists(Path.Combine(Dir, "autopilot.flag"));
                if (autopilot)
                {
                    if (EditorApplication.isPaused) EditorApplication.isPaused = false;
                    if (Time.frameCount == lastPumpFrame) stallTicks++;
                    else stallTicks = 0;
                    lastPumpFrame = Time.frameCount;
                    if (stallTicks >= 3)
                    {
                        // Update ticks are sparse in a background editor; step
                        // several frames per tick or matches run in slow motion.
                        for (int i = 0; i < 4; i++) EditorApplication.Step();
                    }
                    else
                    {
                        EditorApplication.QueuePlayerLoopUpdate();
                    }
                }
                else if (!EditorApplication.isPaused)
                {
                    EditorApplication.QueuePlayerLoopUpdate();
                }
            }

            if (menuReviewSession != null)
            {
                menuReviewSession.Tick();
                if (menuReviewSession.Done) menuReviewSession = null;
            }

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
                case "build_menu":
                    result.message = MenuSceneBuilder.BuildMenuScene();
                    break;
                case "build_invector_cutover":
                {
                    // Scene builders replace the loaded scene. Preserve any
                    // unsaved caller state as an explicit copy before clearing
                    // the dirty bit and regenerating the two builder-owned
                    // production scenes.
                    var active = UnityEngine.SceneManagement.SceneManager.GetActiveScene();
                    string backup = string.Empty;
                    if (active.IsValid() && active.isLoaded && active.isDirty)
                    {
                        bool untitled = string.IsNullOrEmpty(active.path);
                        backup = untitled
                            ? "Assets/Scenes/Untitled.CodexFailedBuildBackup.unity"
                            : string.IsNullOrWhiteSpace(cmd.arg)
                                ? "Assets/Scenes/MainMenu.CodexUnsavedBackup.unity"
                                : cmd.arg;
                        Directory.CreateDirectory(Path.GetDirectoryName(backup));
                        if (!UnityEditor.SceneManagement.EditorSceneManager.SaveScene(
                                active, backup, !untitled))
                            throw new InvalidOperationException(
                                "Could not snapshot dirty scene before Invector regeneration: " +
                                active.path);
                        if (!untitled &&
                            !UnityEditor.SceneManagement.EditorSceneManager.SaveScene(active))
                            throw new InvalidOperationException(
                                "Could not checkpoint the original dirty scene before replacement: " +
                                active.path);
                    }

                    string arenaReport = ArenaSceneBuilder.BuildArenaScene();
                    string menuReport = MenuSceneBuilder.BuildMenuScene();
                    result.message = "Dirty-scene backup: " +
                                     (string.IsNullOrEmpty(backup) ? "not required" : backup) +
                                     "\n" + arenaReport + "\n" + menuReport;
                    break;
                }
                case "run_invector_test":
                    switch (cmd.arg)
                    {
                        case "phase3b":
                            result.message = Tests.InvectorMigrationPhase3BTestResultRecorder
                                .RunSafelyAgainstCurrentScene();
                            break;
                        case "phase3db-lifecycle":
                            result.message = Tests.InvectorMigrationPhase3BTestResultRecorder
                                .RunPhase3DBLifecycleSafely();
                            break;
                        case "phase3dc-weapon-ik":
                            result.message = Tests.InvectorMigrationPhase3BTestResultRecorder
                                .RunPhase3DCWeaponIKSafely();
                            break;
                        case "cutover":
                            result.message = Tests.InvectorMigrationPhase3BTestResultRecorder
                                .RunInvectorOnlyCutoverSafely();
                            break;
                        case "full-editmode":
                            result.message = Tests.InvectorMigrationPhase3BTestResultRecorder
                                .RunFullEditModeSafely();
                            break;
                        case "basic-attack-charges":
                            result.message = Tests.InvectorMigrationPhase3BTestResultRecorder
                                .RunBasicAttackChargesSafely();
                            break;
                        case "combat-cadence-readability":
                            result.message = Tests.InvectorMigrationPhase3BTestResultRecorder
                                .RunCombatCadenceReadabilitySafely();
                            break;
                        case "task2-combat-regression":
                            result.message = Tests.InvectorMigrationPhase3BTestResultRecorder
                                .RunTask2CombatRegressionSafely();
                            break;
                        case "control-zone-match-loop":
                            result.message = Tests.InvectorMigrationPhase3BTestResultRecorder
                                .RunControlZoneMatchLoopSafely();
                            break;
                        case "task3-match-regression":
                            result.message = Tests.InvectorMigrationPhase3BTestResultRecorder
                                .RunTask3MatchRegressionSafely();
                            break;
                        case "thorn-presentation":
                            result.message = Tests.InvectorMigrationPhase3BTestResultRecorder
                                .RunThornPresentationSafely();
                            break;
                        case "tempest-presentation":
                            result.message = Tests.InvectorMigrationPhase3BTestResultRecorder
                                .RunTempestPresentationSafely();
                            break;
                        case "ai-hardening":
                            result.message = Tests.InvectorMigrationPhase3BTestResultRecorder
                                .RunPhase3GAIHardeningSafely();
                            break;
                        default:
                            result.ok = false;
                            result.message = "unknown Invector test: " + cmd.arg;
                            break;
                    }
                    break;
                case "open_scene":
                {
                    string path = string.IsNullOrEmpty(cmd.arg) ? "Assets/Scenes/Arena.unity" : cmd.arg;
                    UnityEditor.SceneManagement.EditorSceneManager.OpenScene(path);
                    result.message = "opened " + path;
                    break;
                }
                case "screenshot_scene":
                    result.message = ArenaSceneBuilder.CaptureSceneOverview();
                    break;
                case "enter_play":
                    EditorApplication.EnterPlaymode();
                    result.message = "entering play mode";
                    break;
                case "ik_calibration_bootstrap":
                    result.message = BrawlIKCalibrationBootstrap.Begin(cmd.arg);
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
                case "gameplay_probe":
                {
                    if (!EditorApplication.isPlaying)
                    {
                        result.ok = false;
                        result.message = "gameplay_probe requires Play Mode";
                        break;
                    }
                    string scenario = string.IsNullOrEmpty(cmd.arg)
                        ? Path.Combine(Dir, "probe-scenarios/shooting-basics.json")
                        : cmd.arg;
                    GameplayProbe.Run(scenario);
                    result.message = "probe armed with " + scenario;
                    break;
                }
                case "play_gameplay_probe":
                {
                    string scenario = string.IsNullOrEmpty(cmd.arg)
                        ? Path.Combine(Dir, "probe-scenarios/shooting-basics.json")
                        : cmd.arg;
                    File.WriteAllText(Path.Combine(Dir, "autopilot.flag"), "1");
                    GameplayProbe.RunOnNextPlay(scenario);
                    result.message = "entering fresh play mode with probe " + scenario;
                    break;
                }
                case "build_action_scene":
                    result.message = ArenaSceneBuilder.BuildActionArenaScene();
                    break;
                case "mixamo_reimport":
                    result.message = MixamoImportTools.ReimportAndValidateInternal();
                    break;
                case "camera_style":
                {
                    // arg: "action" => ActionThirdPerson, anything else => TopDownBrawl.
                    if (!EditorApplication.isPlaying)
                    {
                        result.ok = false;
                        result.message = "camera_style requires Play Mode";
                        break;
                    }
                    var brawlCam = UnityEngine.Object.FindFirstObjectByType<BrawlCamera>();
                    if (brawlCam == null)
                    {
                        result.ok = false;
                        result.message = "no BrawlCamera in scene";
                        break;
                    }
                    var style = string.Equals(cmd.arg, "action", StringComparison.OrdinalIgnoreCase)
                        ? BrawlCameraStyle.ActionThirdPerson
                        : BrawlCameraStyle.TopDownBrawl;
                    brawlCam.SetStyle(style);
                    result.message = "camera style => " + style;
                    break;
                }
                case "game_screenshot":
                {
                    string file = Path.Combine(Dir, string.IsNullOrEmpty(cmd.arg) ? "game.png" : cmd.arg);
                    ScreenCapture.CaptureScreenshot(file);
                    result.message = "capturing to " + file + " (written a frame later)";
                    break;
                }
                case "menu_review":
                {
                    if (!EditorApplication.isPlaying)
                    {
                        result.ok = false;
                        result.message = "menu_review requires Play Mode";
                        break;
                    }
                    menuReviewSession = new MenuReviewSession(Dir, cmd.arg);
                    result.message = "started menu review capture: " + menuReviewSession.OutputDir;
                    break;
                }
                case "menu_nav":
                {
                    if (!EditorApplication.isPlaying)
                    {
                        result.ok = false;
                        result.message = "menu_nav requires Play Mode";
                        break;
                    }

                    var flow = UnityEngine.Object.FindFirstObjectByType<MainMenuFlow>();
                    if (flow == null)
                    {
                        result.ok = false;
                        result.message = "MainMenuFlow not found";
                        break;
                    }

                    NavigateMenu(flow, cmd.arg);
                    EditorApplication.QueuePlayerLoopUpdate();
                    UnityEditorInternal.InternalEditorUtility.RepaintAllViews();
                    result.message = "navigated to " + cmd.arg;
                    break;
                }
                case "show_celebration":
                {
                    if (!EditorApplication.isPlaying)
                    {
                        result.ok = false;
                        result.message = "show_celebration requires Play Mode";
                        break;
                    }

                    var flow = UnityEngine.Object.FindFirstObjectByType<MainMenuFlow>();
                    if (flow == null)
                    {
                        result.ok = false;
                        result.message = "MainMenuFlow not found";
                        break;
                    }

                    ShowCelebrationForReview(flow, cmd.arg);
                    EditorApplication.QueuePlayerLoopUpdate();
                    UnityEditorInternal.InternalEditorUtility.RepaintAllViews();
                    result.message = "prepared celebration review overlay: " + cmd.arg;
                    break;
                }
                default:
                    result.ok = false;
                    result.message = "unknown action: " + cmd.action;
                    break;
            }
        }

        static void ShowCelebrationForReview(MainMenuFlow flow, string arg)
        {
            bool levelUp = !string.Equals(arg, "reward", StringComparison.OrdinalIgnoreCase);
            var theme = UnityEngine.Object.FindFirstObjectByType<UiTheme>();
            CallMenu(flow, "OnBackToMain");

            Sprite icon = null;
            string title = levelUp ? "LEVEL UP" : "REWARD";
            string body = levelUp ? "NOVA UPGRADED" : "REWARD CLAIMED";
            if (theme != null)
                icon = levelUp ? theme.levelFrameHighlight : theme.giftIcon;
            if (levelUp && flow.roster != null && flow.roster.Length > 0)
            {
                BrawlerDefinition selected = null;
                string selectedId = Progress.SelectedCharacterId;
                foreach (var def in flow.roster)
                {
                    if (def == null) continue;
                    if (selected == null) selected = def;
                    if (!string.IsNullOrEmpty(selectedId) && def.id == selectedId)
                    {
                        selected = def;
                        break;
                    }
                }

                if (selected != null)
                {
                    title = selected.displayName.ToUpperInvariant();
                    body = "LEVEL UP";
                    if (selected.portrait != null) icon = selected.portrait;
                }
            }

            const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;
            var sequence = typeof(MainMenuFlow).GetMethod("CelebrationSequence", flags);
            var enumerator = (System.Collections.IEnumerator)sequence.Invoke(flow, new object[]
            {
                title,
                body,
                icon,
                levelUp
            });
            enumerator.MoveNext();

            foreach (var group in UnityEngine.Object.FindObjectsByType<CanvasGroup>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                bool celebration =
                    group.gameObject.name.Contains("Celebration") ||
                    (group.transform.parent != null && group.transform.parent.name.Contains("Celebration"));
                if (celebration) group.alpha = 1f;
            }

            foreach (var rect in UnityEngine.Object.FindObjectsByType<RectTransform>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                if (rect.name == "CelebrationCard") rect.localScale = Vector3.one;
            }

            Canvas.ForceUpdateCanvases();
        }

        static void NavigateMenu(MainMenuFlow flow, string target)
        {
            switch ((target ?? "").Trim().ToLowerInvariant())
            {
                case "main":
                    CallMenu(flow, "OnBackToMain");
                    break;
                case "mode":
                    CallMenu(flow, "OnPlayPressed");
                    break;
                case "character":
                    CallMenu(flow, "OnModePicked", GameMode.Knockout);
                    break;
                case "shop_top":
                    CallMenu(flow, "OnShopPressed");
                    break;
                case "shop_sp":
                    CallMenu(flow, "JumpShop", 0.46f, "SP offers", 1);
                    break;
                case "shop_coins":
                    CallMenu(flow, "JumpShop", 0.22f, "Coin offers", 2);
                    break;
                case "shop_gems":
                    CallMenu(flow, "JumpShop", 0f, "Gem and item offers", 3);
                    break;
                case "brawlers":
                    CallMenu(flow, "OnBrawlersPressed");
                    break;
                case "cards":
                    CallMenu(flow, "OnCardsPressed");
                    break;
                case "inventory":
                    CallMenu(flow, "OnInventoryPressed");
                    break;
                case "quests":
                    CallMenu(flow, "OnMissionsPressed");
                    break;
                case "rewards":
                    CallMenu(flow, "OnRewardsPressed");
                    break;
                case "ranking":
                    CallMenu(flow, "OnRankingPressed");
                    break;
                case "friends":
                    CallMenu(flow, "OnFriendsPressed");
                    break;
                case "inbox":
                    CallMenu(flow, "OnInboxPressed");
                    break;
                case "news":
                    CallMenu(flow, "OnNoticePressed");
                    break;
                case "settings":
                    CallMenu(flow, "OnSettingsPressed");
                    break;
                default:
                    throw new ArgumentException("unknown menu target: " + target);
            }
        }

        static void CallMenu(MainMenuFlow flow, string method, params object[] args)
        {
            const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;
            var info = typeof(MainMenuFlow).GetMethod(method, flags);
            if (info == null) throw new MissingMethodException(typeof(MainMenuFlow).FullName, method);
            info.Invoke(flow, args);
        }

        static string StatusDump()
        {
            var sb = new System.Text.StringBuilder();
            sb.AppendLine($"playing={EditorApplication.isPlaying} paused={EditorApplication.isPaused} compiling={EditorApplication.isCompiling}");
            sb.AppendLine($"frame={Time.frameCount} time={Time.time:0.0} timeScale={Time.timeScale}");
            sb.AppendLine("autopilotFlag=" + File.Exists(Path.Combine(Dir, "autopilot.flag")));
            var flow = UnityEngine.Object.FindFirstObjectByType<GameFlow>();
            sb.AppendLine("gameFlow=" + (flow != null ? $"present rosterLen={(flow.roster != null ? flow.roster.Length : -1)}" : "MISSING"));
            sb.AppendLine("flowPhase=" + GameFlow.DebugPhase);
            sb.AppendLine("menuPhase=" + MainMenuFlow.DebugPhase);
            var gems = UnityEngine.Object.FindFirstObjectByType<GemGrabManager>();
            if (gems != null && gems.ActiveMode)
                sb.AppendLine($"gems blue={gems.TeamGems(TeamId.Blue)} red={gems.TeamGems(TeamId.Red)} " +
                              $"countdown={(gems.CountdownTeam.HasValue ? gems.CountdownTeam.Value + ":" + gems.CountdownRemaining.ToString("0.0") : "none")}");
            sb.AppendLine("coins=" + Progress.Coins + " chars=[" + string.Join(", ",
                Progress.Data.characters.ConvertAll(c => $"{c.id}:L{c.level}/{c.points}sp/S{Progress.TotalSkillLevels(c.id)}")) + "]");
            var mmObj = UnityEngine.Object.FindFirstObjectByType<MatchManager>();
            sb.AppendLine($"mmByFind={(mmObj != null)} mmStatic={(MatchManager.Instance != null)}");
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
                    // Attack swings and lifecycle states play on overlay
                    // layers; sampling only layer 0 misreports a mid-swing
                    // brawler as Idle/Walk. Report the most-weighted overlay
                    // clip too so recorded evidence reflects what is visible.
                    for (int layer = 1; layer < anim.layerCount; layer++)
                    {
                        if (anim.GetLayerWeight(layer) < 0.5f && layer != 0) continue;
                        var overlay = anim.GetCurrentAnimatorClipInfo(layer);
                        if (overlay.Length == 0) continue;
                        string overlayClip = overlay[0].clip.name;
                        if (!string.IsNullOrEmpty(overlayClip) &&
                            overlayClip != clip &&
                            !overlayClip.StartsWith("Idle", StringComparison.Ordinal))
                        {
                            clip = clip + "+" + overlayClip;
                            break;
                        }
                    }
                }
                Vector3 p = b.transform.position;
                string grass = b.Concealment != null && b.Concealment.InGrass
                    ? (b.Concealment.SelfConcealed ? " grass=HIDDEN" : " grass=revealed")
                    : "";
                sb.AppendLine(
                    $"{b.displayName} [{b.team}]{(b.IsPlayer ? " PLAYER" : "")} hp={b.Health.Current:0}/{b.Health.Max:0} " +
                    $"stam={b.Stamina:0} super={b.SuperCharge:0}/{b.maxSuperCharge:0} uses={b.SupersUsed} " +
                    $"pos=({p.x:0.0},{p.z:0.0}) anim={clip} dead={b.IsDead}{grass}");
            }
            return sb.ToString();
        }

        sealed class MenuReviewSession
        {
            readonly List<Step> steps = new List<Step>();
            readonly MethodCache methods = new MethodCache();
            int stepIndex = -1;
            int phase;
            double nextActionAt;
            double captureStartedAt;
            MainMenuFlow flow;
            string currentCaptureFile;

            public bool Done { get; private set; }
            public string OutputDir { get; }

            public MenuReviewSession(string automationDir, string arg)
            {
                string folder = string.IsNullOrEmpty(arg)
                    ? "menu_review_" + DateTime.Now.ToString("yyyyMMdd-HHmmss")
                    : arg;
                OutputDir = Path.IsPathRooted(folder) ? folder : Path.Combine(automationDir, folder);
                Directory.CreateDirectory(OutputDir);

                steps.Add(new Step("01_main_lobby.png", f => methods.Call(f, "OnBackToMain")));
                steps.Add(new Step("02_mode_select.png", f => methods.Call(f, "OnPlayPressed")));
                steps.Add(new Step("03_character_select.png", f => methods.Call(f, "OnModePicked", GameMode.Knockout)));
                steps.Add(new Step("04_shop_brawlers_top.png", f => methods.Call(f, "OnShopPressed")));
                steps.Add(new Step("05_shop_sp_offers.png", f => methods.Call(f, "JumpShop", 0.46f, "SP offers", 1)));
                steps.Add(new Step("06_shop_coin_offers.png", f => methods.Call(f, "JumpShop", 0.22f, "Coin offers", 2)));
                steps.Add(new Step("07_shop_gems_items.png", f => methods.Call(f, "JumpShop", 0f, "Gem and item offers", 3)));
                steps.Add(new Step("08_brawlers.png", f => methods.Call(f, "OnBrawlersPressed")));
                steps.Add(new Step("09_cards.png", f => methods.Call(f, "OnCardsPressed")));
                steps.Add(new Step("10_inventory.png", f => methods.Call(f, "OnInventoryPressed")));
                steps.Add(new Step("11_quests.png", f => methods.Call(f, "OnMissionsPressed")));
                steps.Add(new Step("12_rewards.png", f => methods.Call(f, "OnRewardsPressed")));
                steps.Add(new Step("13_ranking.png", f => methods.Call(f, "OnRankingPressed")));
                steps.Add(new Step("14_friends_clan.png", f => methods.Call(f, "OnFriendsPressed")));
                steps.Add(new Step("15_inbox.png", f => methods.Call(f, "OnInboxPressed")));
                steps.Add(new Step("16_news.png", f => methods.Call(f, "OnNoticePressed")));
                steps.Add(new Step("17_settings.png", f => methods.Call(f, "OnSettingsPressed")));

                File.WriteAllText(Path.Combine(OutputDir, "README.txt"),
                    "Menu/submenu screenshots captured from Unity Play Mode." + Environment.NewLine);
                nextActionAt = EditorApplication.timeSinceStartup + 0.75;
            }

            public void Tick()
            {
                if (Done || !EditorApplication.isPlaying) return;
                if (EditorApplication.timeSinceStartup < nextActionAt) return;

                if (flow == null)
                {
                    flow = UnityEngine.Object.FindFirstObjectByType<MainMenuFlow>();
                    if (flow == null)
                    {
                        nextActionAt = EditorApplication.timeSinceStartup + 0.25;
                        return;
                    }
                    stepIndex = 0;
                    phase = 0;
                }

                if (stepIndex >= steps.Count)
                {
                    File.WriteAllText(Path.Combine(OutputDir, "complete.txt"),
                        "Captured " + steps.Count + " menu screenshots." + Environment.NewLine);
                    Done = true;
                    return;
                }

                Step step = steps[stepIndex];
                if (phase == 0)
                {
                    step.Apply(flow);
                    nextActionAt = EditorApplication.timeSinceStartup + 0.45;
                    phase = 1;
                    return;
                }

                if (phase == 1)
                {
                    currentCaptureFile = Path.Combine(OutputDir, step.FileName);
                    captureStartedAt = EditorApplication.timeSinceStartup;
                    ScreenCapture.CaptureScreenshot(currentCaptureFile);
                    nextActionAt = EditorApplication.timeSinceStartup + 0.25;
                    phase = 2;
                    return;
                }

                if (!string.IsNullOrEmpty(currentCaptureFile) &&
                    File.Exists(currentCaptureFile) &&
                    new FileInfo(currentCaptureFile).Length > 0)
                {
                    currentCaptureFile = null;
                    stepIndex++;
                    phase = 0;
                    nextActionAt = EditorApplication.timeSinceStartup + 0.2;
                    return;
                }

                if (EditorApplication.timeSinceStartup - captureStartedAt < 6.0)
                {
                    nextActionAt = EditorApplication.timeSinceStartup + 0.25;
                    return;
                }

                Debug.LogWarning("[Automation] menu review capture timed out: " + step.FileName);
                currentCaptureFile = null;
                stepIndex++;
                phase = 0;
                nextActionAt = EditorApplication.timeSinceStartup + 0.2;
            }

            sealed class Step
            {
                readonly Action<MainMenuFlow> apply;
                public string FileName { get; }

                public Step(string fileName, Action<MainMenuFlow> apply)
                {
                    FileName = fileName;
                    this.apply = apply;
                }

                public void Apply(MainMenuFlow flow) => apply(flow);
            }

            sealed class MethodCache
            {
                readonly Dictionary<string, MethodInfo> cache = new Dictionary<string, MethodInfo>();
                const BindingFlags Flags = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;

                public void Call(MainMenuFlow flow, string method, params object[] args)
                {
                    if (flow == null) return;
                    if (!cache.TryGetValue(method, out MethodInfo info))
                    {
                        info = typeof(MainMenuFlow).GetMethod(method, Flags);
                        cache[method] = info;
                    }
                    if (info == null)
                        throw new MissingMethodException(typeof(MainMenuFlow).FullName, method);
                    info.Invoke(flow, args);
                }
            }
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
