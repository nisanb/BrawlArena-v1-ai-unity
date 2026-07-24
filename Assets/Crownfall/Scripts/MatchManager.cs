using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Crownfall
{
    public enum MatchState { Menu, ClassSelect, Countdown, Fighting, Ended }

    /// Owns the 3v3 flow: class select, countdown, kills/score, respawns,
    /// sudden death and the end ceremony. Online-mode members live in
    /// MatchManagerOnline.cs; offline flow is unchanged.
    public partial class MatchManager : MonoBehaviour
    {
        [Header("Wired by forge")]
        public GameObject[] playerVariants = new GameObject[6];
        public Transform[] azureSpawns;
        public Transform[] crimsonSpawns;

        public int killTarget = 10;
        public float matchDuration = 300f;

        public static MatchManager I { get; private set; }

        public MatchState State { get; private set; } = MatchState.Menu;
        public bool Paused { get; private set; }
        public float TimeLeft { get; private set; }
        public bool SuddenDeath { get; private set; }
        public CombatMotor PlayerMotor { get; private set; }
        public int ScoreAzure { get; private set; }
        public int ScoreCrimson { get; private set; }
        /// True for exhibition matches (no progression rewards).
        public bool IsDemo { get; private set; }
        public MatchRewards LastRewards { get; private set; }

        public event Action<MatchState> StateChanged;
        public event Action<bool> PausedChanged;
        public event Action<int, int> ScoreChanged;
        public event Action<CombatantIdentity, CombatantIdentity> KillFeed;
        public event Action<int> CountdownTick;
        public event Action<Team> MatchEndedEvent;
        public event Action<string> Announce;

        bool autopilot;
        public bool Autopilot
        {
            get => autopilot;
            set { autopilot = value; ApplyAutopilot(); }
        }

        readonly List<CombatMotor> all = new List<CombatMotor>();
        readonly Dictionary<CombatMotor, int> spawnSlot = new Dictionary<CombatMotor, int>();
        readonly List<CombatMotor> aliveScratch = new List<CombatMotor>();
        readonly List<CombatMotor> allyScratch = new List<CombatMotor>();
        readonly List<GameObject> aiRigs = new List<GameObject>();
        Light podiumLight;

        int playerKills;

        void Awake()
        {
            I = this;
            Time.timeScale = 1f;
            TimeLeft = matchDuration;
            // quest bookkeeping: count the local player's takedowns; works for
            // both the offline path and the master-echoed online kill feed
            KillFeed += (killer, victim) =>
            {
                if (killer != null && PlayerMotor != null && killer == PlayerMotor.Identity)
                    playerKills++;
            };
        }

        void Start()
        {
            int azureIdx = 0, crimsonIdx = 0;
            foreach (var h in FindObjectsByType<Health>(FindObjectsInactive.Include, FindObjectsSortMode.InstanceID))
            {
                var motor = h.GetComponent<CombatMotor>();
                if (motor == null) continue;
                all.Add(motor);
                var id = motor.GetComponent<CombatantIdentity>();
                if (id != null)
                {
                    if (id.isPlayer) spawnSlot[motor] = 0;
                    else if (id.team == Team.Azure) spawnSlot[motor] = ++azureIdx;
                    else spawnSlot[motor] = crimsonIdx++;
                }
                h.Died += killer => OnCombatantDied(motor, killer);
                if (id != null && !id.isPlayer) aiRigs.Add(motor.gameObject);
            }

            CrownfallSettings.Load();
            SetCursorFree(true);
            SetState(MatchState.Menu);
            ShowcaseChampion(CrownfallMeta.SelectedClass);
        }

        public void OpenClassSelect()
        {
            if (State == MatchState.Menu) SetState(MatchState.ClassSelect);
        }

        public void BackToMenu()
        {
            if (State == MatchState.ClassSelect) SetState(MatchState.Menu);
        }

        /// Offline matches honor the hub's mode carousel pick. Online rooms are
        /// untouched: every match ends in a scene reload, which restores the
        /// serialized standard values before a networked start can happen.
        void ApplySelectedMode()
        {
            var mode = GameModes.Selected;
            killTarget = mode.killTarget;
            matchDuration = mode.duration;
            TimeLeft = mode.duration;
            CrownEnabled = mode.crown;
            crownSecondsPerPoint = mode.crownSecondsPerPoint > 0f ? mode.crownSecondsPerPoint : 2.6f;
            matchPointCalled = false;
        }

        // ------------------------------------------------------------------ crown

        /// True when this match runs the Crown objective. Networked rooms always
        /// run the standard brawl for now — the crown is authoritative state and
        /// would need its own master-side sync to be fair online.
        public bool CrownEnabled { get; private set; }
        float crownSecondsPerPoint = 2.6f;

        /// Every fighter in the match, alive or not — the crown polls this for
        /// pickups and the AI reads it to find the carrier.
        public IReadOnlyList<CombatMotor> AllFighters => all;

        void SetupCrown()
        {
            if (!CrownEnabled || OnlineMode) return;
            var crown = CrownObjective.Ensure();
            crown.secondsPerPoint = crownSecondsPerPoint;
            crown.BeginMatch(ArenaCentre());
        }

        /// Centre of the fight: the midpoint of the two teams' spawn clusters, so
        /// the crown lands in genuinely contested ground on any arena layout.
        Vector3 ArenaCentre()
        {
            Vector3 sum = Vector3.zero;
            int n = 0;
            foreach (var arr in new[] { azureSpawns, crimsonSpawns })
            {
                if (arr == null) continue;
                foreach (var t in arr)
                    if (t != null) { sum += t.position; n++; }
            }
            return n > 0 ? sum / n : Vector3.zero;
        }

        /// One crown tick for `team`. Feeds the same score as a kill so the HUD,
        /// win condition and sudden-death logic all work unchanged.
        public void AddCrownPoint(Team team)
        {
            if (State != MatchState.Fighting) return;
            if (team == Team.Azure) ScoreAzure++; else ScoreCrimson++;
            ScoreChanged?.Invoke(ScoreAzure, ScoreCrimson);
            GameEffects.I?.PlayUi(GameEffects.I.uiTick, 0.3f);
            CheckMatchPoint();

            if (ScoreAzure >= killTarget || ScoreCrimson >= killTarget)
                EndMatch(ScoreAzure > ScoreCrimson ? Team.Azure : Team.Crimson);
        }

        public void AnnounceCrown(string text)
        {
            if (!string.IsNullOrEmpty(text)) Announce?.Invoke(text);
        }

        // ---- takedown feedback
        // Landing a kill used to produce nothing but a body falling over and a line
        // in the feed. A kill is the loudest thing a player can do in a match, so it
        // gets its own beat: a moment of slowed time, a camera punch, and a callout
        // when they are chained.
        int playerStreak;
        float streakExpiresAt;
        const float StreakWindow = 4.5f;
        static readonly string[] StreakCallouts =
            { null, "DOUBLE KILL", "TRIPLE KILL", "QUAD KILL", "RAMPAGE" };

        void ReportTakedown(CombatMotor killer, CombatMotor victim)
        {
            if (killer == null || PlayerMotor == null) return;
            bool byPlayer = killer == PlayerMotor;
            bool onPlayer = victim == PlayerMotor;
            if (!byPlayer && !onPlayer) return;

            if (byPlayer)
            {
                GameEffects.I?.Takedown(victim != null ? victim.transform.position : killer.transform.position);

                playerStreak = Time.time < streakExpiresAt ? playerStreak + 1 : 1;
                streakExpiresAt = Time.time + StreakWindow;
                int idx = Mathf.Clamp(playerStreak - 1, 0, StreakCallouts.Length - 1);
                string callout = StreakCallouts[idx];
                if (callout != null) Announce?.Invoke(callout);
            }
            else
            {
                // dying breaks the chain
                playerStreak = 0;
                streakExpiresAt = 0f;
            }
        }

        bool matchPointCalled;

        /// One-shot "this is the last stretch" beat. Without it a 40-point target is
        /// just a number climbing; with it the endgame has a moment where everyone
        /// knows the next crown tick could finish it.
        void CheckMatchPoint()
        {
            if (matchPointCalled || State != MatchState.Fighting) return;
            int lead = Mathf.Max(ScoreAzure, ScoreCrimson);
            int threshold = Mathf.Max(1, Mathf.RoundToInt(killTarget * 0.12f));
            if (killTarget - lead > threshold) return;
            matchPointCalled = true;
            Announce?.Invoke("MATCH POINT");
            GameEffects.I?.PlayUi(GameEffects.I.uiFight, 0.8f);
        }

        /// Home-hub PLAY: fight with the persisted champion pick.
        public void StartMatch()
        {
            IsDemo = false;
            ApplySelectedMode();
            SelectClass(CrownfallMeta.SelectedClass);
        }

        /// Champions screen pick: persist + pose the new champion on the
        /// menu podium, but stay in the menus.
        public void SelectChampion(int classIndex)
        {
            if (State != MatchState.Menu && State != MatchState.ClassSelect) return;
            CrownfallMeta.SelectedClass = classIndex;
            ShowcaseChampion(classIndex);
        }

        /// Activate exactly one player variant so the menu camera has a
        /// champion to orbit; park the AI cast offstage and light the podium.
        void ShowcaseChampion(int classIndex)
        {
            classIndex = Mathf.Clamp(classIndex, 0, playerVariants.Length - 1);
            for (int i = 0; i < playerVariants.Length; i++)
                if (playerVariants[i] != null) playerVariants[i].SetActive(i == classIndex);
            foreach (var rig in aiRigs)
                if (rig != null) rig.SetActive(false);

            var chosen = playerVariants[classIndex];
            if (OrbitCamera.I != null) OrbitCamera.I.menuFocus = chosen != null ? chosen.transform : null;

            if (chosen != null)
            {
                if (podiumLight == null)
                {
                    var go = new GameObject("PodiumLight");
                    podiumLight = go.AddComponent<Light>();
                    podiumLight.type = LightType.Directional;
                    podiumLight.intensity = 1.05f;
                    podiumLight.color = new Color(1f, 0.95f, 0.85f);
                    podiumLight.shadows = LightShadows.None;
                }
                // aim down the champion's facing so their front is lit
                podiumLight.transform.rotation =
                    Quaternion.LookRotation(-chosen.transform.forward + Vector3.down * 0.7f);
                podiumLight.gameObject.SetActive(true);
            }
        }

        /// Autopilot exhibition match with a random champion.
        public void StartDemo()
        {
            if (State != MatchState.Menu && State != MatchState.ClassSelect) return;
            IsDemo = true;
            Autopilot = true;
            ApplySelectedMode();
            SelectClass(UnityEngine.Random.Range(0, playerVariants.Length));
        }

        public void TogglePause()
        {
            if (State != MatchState.Fighting) return;
            Paused = !Paused;
            // a networked match cannot stop the world — pause is menu-only there
            GameEffects.I?.SetBaseTimeScale(Paused && !OnlineMode ? 0f : 1f);
            SetCursorFree(Paused);
            PausedChanged?.Invoke(Paused);
        }

        void Update()
        {
            if (State != MatchState.Fighting) return;

            if (!SuddenDeath)
            {
                TimeLeft -= Time.deltaTime;
                if (TimeLeft <= 0f)
                {
                    TimeLeft = 0f;
                    if (OnlineMode)
                    {
                        // only the master may call time; everyone else waits for
                        // the RPC (their clock is corrected by the stream anyway)
                        if (Photon.Pun.PhotonNetwork.IsMasterClient)
                            NetMatchLink.I?.MasterTimeUp();
                    }
                    else if (ScoreAzure != ScoreCrimson)
                    {
                        EndMatch(ScoreAzure > ScoreCrimson ? Team.Azure : Team.Crimson);
                    }
                    else
                    {
                        SuddenDeath = true;
                        Announce?.Invoke("SUDDEN DEATH");
                        GameEffects.I?.PlayUi(GameEffects.I.uiFight, 0.9f);
                    }
                }
            }

            var kb = UnityEngine.InputSystem.Keyboard.current;
            if (kb != null && kb.f1Key.wasPressedThisFrame && !Paused)
                Autopilot = !Autopilot;
            // ESC is owned by HUDController.Update — handling it here too made
            // the same press toggle pause twice (review 2026-07-22)
        }

        // ------------------------------------------------------------------ flow

        public void SelectClass(int classIndex)
        {
            if (State != MatchState.ClassSelect && State != MatchState.Menu) return;
            classIndex = Mathf.Clamp(classIndex, 0, playerVariants.Length - 1);

            // the menu showcase benched the AI cast — bring everyone back
            foreach (var rig in aiRigs)
                if (rig != null) rig.SetActive(true);
            if (podiumLight != null) podiumLight.gameObject.SetActive(false);
            if (OrbitCamera.I != null) OrbitCamera.I.menuFocus = null;

            for (int i = 0; i < playerVariants.Length; i++)
                if (playerVariants[i] != null) playerVariants[i].SetActive(i == classIndex);

            var chosen = playerVariants[classIndex];
            PlayerMotor = chosen != null ? chosen.GetComponent<CombatMotor>() : null;
            OrbitCamera.I?.SetTarget(PlayerMotor, true);
            ApplyAutopilot();

            StartCoroutine(CountdownRoutine());
        }

        IEnumerator CountdownRoutine()
        {
            SetState(MatchState.Countdown);
            SetCursorFree(false);
            for (int n = 3; n >= 1; n--)
            {
                CountdownTick?.Invoke(n);
                GameEffects.I?.PlayUi(GameEffects.I.uiTick, 0.6f);
                yield return new WaitForSeconds(1f);
            }
            CountdownTick?.Invoke(0);
            GameEffects.I?.PlayUi(GameEffects.I.uiFight, 0.9f);
            SetState(MatchState.Fighting);
            SetupCrown();
        }

        void OnCombatantDied(CombatMotor victim, CombatMotor killer)
        {
            if (State != MatchState.Fighting) return;

            // online: the victim's owner reports the kill; score, feed, respawn
            // and match-end all come back from the master via NetMatchLink RPCs
            if (OnlineMode)
            {
                var net = victim.Net;
                if (net != null && net.IsMine && net.photonView != null && NetMatchLink.I != null)
                {
                    int killerView = killer != null && killer.Net != null && killer.Net.photonView != null
                        ? killer.Net.photonView.ViewID : -1;
                    NetMatchLink.I.ReportKill(net.photonView.ViewID, killerView);
                }
                return;
            }

            // a crown carrier who goes down drops it on the spot — the single most
            // important swing moment in the mode, so it must be immediate
            CrownObjective.I?.NotifyCarrierDown(victim);

            var victimId = victim.Identity;
            var killerId = killer != null ? killer.Identity : null;

            if (victimId.team == Team.Azure) ScoreCrimson++;
            else ScoreAzure++;
            ScoreChanged?.Invoke(ScoreAzure, ScoreCrimson);
            KillFeed?.Invoke(killerId, victimId);
            GameEffects.I?.PlayUi(GameEffects.I.killDing, 0.45f);
            ReportTakedown(killer, victim);
            CheckMatchPoint();

            if (SuddenDeath || ScoreAzure >= killTarget || ScoreCrimson >= killTarget)
            {
                EndMatch(victimId.team == Team.Azure ? Team.Crimson : Team.Azure);
                return;
            }

            StartCoroutine(RespawnRoutine(victim));
        }

        /// Respawn delay for `team`, shortened while they are being run away from.
        /// A 5s walk back for the losing side is how a deficit turns into a rout —
        /// the trailing team gets back to the crown sooner so the game stays live.
        /// Deliberately mild: it speeds up re-entry, it does not hand out damage.
        float RespawnDelayFor(Team team)
        {
            int mine = team == Team.Azure ? ScoreAzure : ScoreCrimson;
            int theirs = team == Team.Azure ? ScoreCrimson : ScoreAzure;
            int deficit = theirs - mine;
            float bigDeficit = Mathf.Max(4f, killTarget * 0.2f);
            float t = Mathf.Clamp01(deficit / bigDeficit);
            return Mathf.Lerp(Tuning.RespawnSeconds, Tuning.RespawnSeconds * 0.6f, t);
        }

        IEnumerator RespawnRoutine(CombatMotor victim)
        {
            var vid = victim.Identity;
            yield return new WaitForSeconds(
                vid != null ? RespawnDelayFor(vid.team) : Tuning.RespawnSeconds);
            if (State != MatchState.Fighting) yield break;

            var id = victim.Identity;
            var spawns = id.team == Team.Azure ? azureSpawns : crimsonSpawns;
            int slot = spawnSlot.TryGetValue(victim, out var s) ? s : 0;
            var point = spawns != null && spawns.Length > 0 ? spawns[slot % spawns.Length] : victim.transform;

            victim.ResetForRespawn(point.position, point.rotation);
            GameEffects.I?.RespawnFlash(point.position);
        }

        void EndMatch(Team winner)
        {
            if (State == MatchState.Ended) return;
            SetState(MatchState.Ended);
            CrownObjective.I?.EndMatch();
            // offline the player is always Azure; online they may be on either side
            bool playerWon = PlayerMotor != null && PlayerMotor.Identity != null
                ? PlayerMotor.Identity.team == winner
                : winner == Team.Azure;
            LastRewards = IsDemo ? default : CrownfallMeta.GrantMatchRewards(playerWon);
            if (!IsDemo) CrownfallQuests.OnMatchFinished(playerWon, playerKills);
            MatchEndedEvent?.Invoke(winner);
            Announce?.Invoke(playerWon ? "VICTORY" : "DEFEAT");
            GameEffects.I?.PlayUi(playerWon ? GameEffects.I.uiVictory : GameEffects.I.uiDefeat, 0.9f);

            foreach (var m in all)
                if (m != null && m.gameObject.activeInHierarchy && !m.IsDead && m.Identity.team == winner)
                    m.PlayVictory();

            StartCoroutine(EndCeremony());
        }

        IEnumerator EndCeremony()
        {
            GameEffects.I?.SetBaseTimeScale(0.35f);
            yield return new WaitForSecondsRealtime(2.6f);
            GameEffects.I?.SetBaseTimeScale(1f);
            SetCursorFree(true);
        }

        public void Restart()
        {
            if (OnlineMode) CrownfallNet.I?.LeaveMatch();
            Time.timeScale = 1f;
            // reload into the same kind of match the menu launched us into —
            // without this the reloaded arena would idle on the empty showcase
            CrownfallLaunch.Pending = CrownfallLaunch.LastKind;
            SceneManager.LoadScene(SceneManager.GetActiveScene().buildIndex);
        }

        /// Leave the match and return to the standalone menu scene.
        public void QuitToMenu()
        {
            if (OnlineMode) CrownfallNet.I?.LeaveMatch();
            Time.timeScale = 1f;
            GameEffects.I?.SetBaseTimeScale(1f);
            CrownfallLaunch.ToMenu();
        }

        void LateUpdate()
        {
            if (State == MatchState.Ended &&
                UnityEngine.InputSystem.Keyboard.current != null &&
                UnityEngine.InputSystem.Keyboard.current.rKey.wasPressedThisFrame)
                Restart();
        }

        // ------------------------------------------------------------------ queries

        public List<CombatMotor> AliveEnemiesOf(Team team)
        {
            aliveScratch.Clear();
            foreach (var m in all)
            {
                if (m == null || !m.gameObject.activeInHierarchy || m.IsDead) continue;
                if (m.Identity == null || m.Identity.team == team) continue;
                aliveScratch.Add(m);
            }
            return aliveScratch;
        }

        /// Living, active teammates on `team`, excluding `self`. Used by the
        /// healer to pick who to face and mend.
        public List<CombatMotor> AlliesOf(Team team, CombatMotor self)
        {
            allyScratch.Clear();
            foreach (var m in all)
            {
                if (m == null || m == self || !m.gameObject.activeInHierarchy || m.IsDead) continue;
                if (m.Identity == null || m.Identity.team != team) continue;
                allyScratch.Add(m);
            }
            return allyScratch;
        }

        public int CountTargeting(CombatMotor candidate, AIController asker)
        {
            int n = 0;
            foreach (var m in all)
            {
                if (m == null || !m.gameObject.activeInHierarchy || m.IsDead) continue;
                var ai = m.GetComponent<AIController>();
                if (ai == null || ai == asker || !ai.enabled) continue;
                if (ai.CurrentTarget == candidate) n++;
            }
            return n;
        }

        // ------------------------------------------------------------------ helpers

        void ApplyAutopilot()
        {
            if (PlayerMotor == null) return;
            var ai = PlayerMotor.GetComponent<AIController>();
            if (ai != null) ai.enabled = autopilot;
        }

        void SetState(MatchState s)
        {
            State = s;
            StateChanged?.Invoke(s);
        }

        void SetCursorFree(bool free)
        {
            // the on-screen controls need a free cursor to be clickable during a
            // fight (mouse-as-finger on PC), so never lock it while they are active
            bool wantFree = free || TouchController.Active;
            Cursor.lockState = wantFree ? CursorLockMode.None : CursorLockMode.Locked;
            Cursor.visible = wantFree;
        }

        /// Headless testing entry: pick a class and optionally enable autopilot.
        /// Autopilot runs count as demos so playtests don't inflate progression.
        public void AutoStart(int classIndex, bool enableAutopilot)
        {
            IsDemo = enableAutopilot;
            Autopilot = enableAutopilot;
            // headless playtests must run the same rules a real match would, or the
            // capture evidence describes a mode nobody actually plays
            ApplySelectedMode();
            SelectClass(classIndex);
        }
    }
}
