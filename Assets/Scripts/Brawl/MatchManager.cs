using System;
using System.Collections.Generic;
using Unity.AI.Navigation;
using UnityEngine;

namespace BrawlArena
{
    public enum MatchState
    {
        Waiting,
        Intro,
        Playing,
        Overtime,
        Ended
    }

    /// <summary>
    /// Brawl-owned match state, timer, score, spawn, respawn, and victory flow.
    /// Control Zone is the primary 3v3 mode; KO and Gem Grab remain secondary.
    /// Also performs a runtime NavMesh bake if required.
    /// </summary>
    public class MatchManager : MonoBehaviour
    {
        public static MatchManager Instance { get; private set; }

        [Header("Rules")]
        public GameMode mode = GameMode.ControlZone;
        public float matchDuration = ControlZoneRules.RegulationDuration;
        [Tooltip("Objective score limit. Gem Grab uses its own configured limit.")]
        public int scoreToWin = ControlZoneRules.ScoreLimit;
        public float respawnDelay = ControlZoneRules.RespawnDelay;
        public float spawnProtectionDuration = ControlZoneRules.SpawnProtectionDuration;
        public float introDuration = 1.6f;
        [Tooltip("Start the match immediately on Start. Off when GameFlow drives character select first.")]
        public bool autoStart = true;

        [Header("Spawns")]
        public Transform[] blueSpawns;
        public Transform[] redSpawns;

        public MatchState State { get; private set; } = MatchState.Waiting;
        public float TimeRemaining { get; private set; }
        public int BlueScore { get; private set; }
        public int RedScore { get; private set; }
        public bool IsCombatActive => State == MatchState.Playing ||
                                      State == MatchState.Overtime;
        public int ActiveTeamSize => ArenaLayout.ActiveTeamSize(mode);
        public ControlZoneManager ControlZone { get; private set; }

        /// <summary>
        /// Best-effort victory animation failures observed during this match.
        /// Presentation faults never change the authoritative match result.
        /// </summary>
        public int VictoryPresentationFaultCount { get; private set; }
        public BrawlerController LastVictoryPresentationFaultActor { get; private set; }
        public Exception LastVictoryPresentationFault { get; private set; }

        public event Action ScoreChanged;
        /// <summary>(victim, attacker) — attacker may be null (environment).</summary>
        public event Action<BrawlerController, BrawlerController> Kill;
        /// <summary>Winner, or null on a draw.</summary>
        public event Action<TeamId?> MatchEnded;

        readonly List<BrawlerController> brawlers = new List<BrawlerController>();
        readonly HashSet<Transform> blueSpawnReservations = new HashSet<Transform>();
        readonly HashSet<Transform> redSpawnReservations = new HashSet<Transform>();
        float introEndsAt;
        int spawnReservationFrame = -1;
        int blueSpawnCursor;
        int redSpawnCursor;

        void Awake()
        {
            Instance = this;
            Application.targetFrameRate = 60;
            ConfigureMode(mode);
            TimeRemaining = matchDuration;
            ControlZone = GetComponent<ControlZoneManager>();

            // RuntimeInitializeOnLoadMethod runs after the initial startup scene,
            // not after a later MainMenu -> Arena transition. Reinforce the
            // topology pass here so every real match gets the same layer and
            // collider contract before fighters spawn.
            ArenaRuntimeOptimizer.TryOptimizeActiveArena(out _);

            var surface = GetComponent<NavMeshSurface>();
            if (surface != null && surface.navMeshData == null) surface.BuildNavMesh();
        }

        void Start()
        {
            // SceneManager marks Arena active after Awake; this is a safe
            // fallback for editor play configurations and direct scene starts.
            ArenaRuntimeOptimizer.TryOptimizeActiveArena(out _);
            if (autoStart) BeginMatch();
        }

        public void BeginMatch()
        {
            if (State != MatchState.Waiting) return;
            State = MatchState.Intro;
            introEndsAt = Time.time + introDuration;
            TimeRemaining = matchDuration;
            BlueScore = 0;
            RedScore = 0;
            blueSpawnCursor = 0;
            redSpawnCursor = 0;
            spawnReservationFrame = -1;
            blueSpawnReservations.Clear();
            redSpawnReservations.Clear();
            VictoryPresentationFaultCount = 0;
            LastVictoryPresentationFaultActor = null;
            LastVictoryPresentationFault = null;
            if (ControlZone == null) ControlZone = GetComponent<ControlZoneManager>();
            ControlZone?.ResetForMatch();
            EnsureExperienceSystem().BeginMatch();
            for (int i = 0; i < brawlers.Count; i++)
                brawlers[i]?.ResetForMatchLifecycle();
            BalanceTelemetryRuntime.BeginMatch(this);
            ScoreChanged?.Invoke();
        }

        public void ConfigureMode(GameMode selectedMode)
        {
            mode = selectedMode;
            if (mode == GameMode.ControlZone)
            {
                matchDuration = ControlZoneRules.RegulationDuration;
                scoreToWin = ControlZoneRules.ScoreLimit;
                respawnDelay = ControlZoneRules.RespawnDelay;
                spawnProtectionDuration = ControlZoneRules.SpawnProtectionDuration;
            }
            else
            {
                matchDuration = 150f;
                scoreToWin = 8;
                respawnDelay = 2.5f;
                spawnProtectionDuration = 1.5f;
            }
        }

        MatchExperienceSystem EnsureExperienceSystem()
        {
            MatchExperienceSystem system = GetComponent<MatchExperienceSystem>();
            if (system == null) system = gameObject.AddComponent<MatchExperienceSystem>();
            system.Attach(this);
            return system;
        }

        void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        void Update()
        {
            if (State == MatchState.Intro && Time.time >= introEndsAt)
            {
                State = MatchState.Playing;
                if (mode == GameMode.ControlZone) ControlZone?.BeginRegulation();
            }
            AdvanceActiveMatch(Time.deltaTime);
        }

        /// <summary>
        /// Advances only authoritative active-match time. Keeping this step
        /// explicit makes scoring/expiry deterministic and independently testable.
        /// </summary>
        public void AdvanceActiveMatch(float deltaTime)
        {
            if (!float.IsFinite(deltaTime) || deltaTime <= 0f) return;
            if (State == MatchState.Overtime)
            {
                ControlZone?.Tick(deltaTime, true);
                return;
            }
            if (State != MatchState.Playing) return;

            float activeStep = Mathf.Min(Mathf.Max(0f, TimeRemaining), deltaTime);
            if (mode == GameMode.ControlZone) ControlZone?.Tick(activeStep, false);
            if (State == MatchState.Ended) return;
            TimeRemaining -= deltaTime;
            if (TimeRemaining <= 0f)
            {
                TimeRemaining = 0f;
                ControlZoneRegulationResult result =
                    ControlZoneRules.ResolveRegulationResult(BlueScore, RedScore);
                if (mode == GameMode.ControlZone &&
                    result == ControlZoneRegulationResult.Overtime)
                {
                    State = MatchState.Overtime;
                    ControlZone?.BeginOvertime();
                }
                else
                {
                    EndMatch(TimeoutWinner());
                }
            }
        }

        TeamId? TimeoutWinner()
        {
            if (mode == GameMode.GemGrab && GemGrabManager.Instance != null)
                return GemGrabManager.Instance.LeadingTeam();
            if (BlueScore == RedScore) return null;
            return BlueScore > RedScore ? TeamId.Blue : TeamId.Red;
        }

        /// <summary>Fires for every brawler that joins (spawned any time).</summary>
        public event Action<BrawlerController> BrawlerRegistered;

        public void Register(BrawlerController b)
        {
            if (b == null) return;
            if (brawlers.Contains(b)) return;

            HeroMatchProgression progression = b.GetComponent<HeroMatchProgression>();
            if (progression == null) progression = b.gameObject.AddComponent<HeroMatchProgression>();
            progression.Initialize(b);

            brawlers.Add(b);
            BalanceTelemetryRuntime.RegisterBrawler(this, b);
            BrawlerRegistered?.Invoke(b);
        }

        public List<BrawlerController> GetBrawlers()
        {
            return brawlers;
        }

        public void ReportKO(BrawlerController victim, GameObject attackerGo)
        {
            if (State == MatchState.Ended) return;

            var attacker = attackerGo != null ? attackerGo.GetComponentInParent<BrawlerController>() : null;
            BalanceTelemetryRuntime.RecordKnockout(this, victim, attacker);
            Kill?.Invoke(victim, attacker);

            TeamId scoringTeam = TeamUtil.Other(victim.team);
            if (attacker != null) scoringTeam = attacker.team;
            if (scoringTeam == victim.team) return;

            // KOs matter through death/respawn pressure and, during Control
            // Zone regulation only, also award a fixed bonus toward the
            // objective score. AddControlZoneScore already no-ops outside
            // MatchState.Playing, so Overtime KOs correctly score nothing —
            // the zone tick decides overtime instead.
            if (mode == GameMode.ControlZone)
            {
                AddControlZoneScore(scoringTeam, ControlZoneRules.RegulationKnockoutPoints);
                return;
            }

            if (scoringTeam == TeamId.Blue) BlueScore++;
            else RedScore++;
            ScoreChanged?.Invoke();

            if (mode == GameMode.Knockout && (BlueScore >= scoreToWin || RedScore >= scoreToWin))
                EndMatch(BlueScore >= scoreToWin ? TeamId.Blue : TeamId.Red);
        }

        /// <summary>Mode managers (Gem Grab countdown) end the match through this.</summary>
        public void DeclareWinner(TeamId? winner)
        {
            EndMatch(winner);
        }

        public void AddControlZoneScore(TeamId team, int points)
        {
            if (mode != GameMode.ControlZone || State != MatchState.Playing || points <= 0)
                return;
            if (team == TeamId.Blue)
                BlueScore = ControlZoneRules.ApplyScore(BlueScore, points, scoreToWin);
            else
                RedScore = ControlZoneRules.ApplyScore(RedScore, points, scoreToWin);
            ScoreChanged?.Invoke();
            if (BlueScore >= scoreToWin || RedScore >= scoreToWin)
                EndMatch(BlueScore >= scoreToWin ? TeamId.Blue : TeamId.Red);
        }

        public void AddControlZoneOvertimePoint(TeamId team)
        {
            if (mode != GameMode.ControlZone || State != MatchState.Overtime) return;
            if (team == TeamId.Blue) BlueScore++;
            else RedScore++;
            ScoreChanged?.Invoke();
            EndMatch(team);
        }

        public float RespawnDelayFor(BrawlerController brawler)
        {
            if (mode == GameMode.ControlZone)
            {
                if (brawler == null) return ControlZoneRules.RespawnDelay;
                int victimScore = brawler.team == TeamId.Blue ? BlueScore : RedScore;
                int enemyScore = brawler.team == TeamId.Blue ? RedScore : BlueScore;
                return ControlZoneRules.RespawnDelaySeconds(victimScore, enemyScore);
            }
            float multiplier = brawler != null
                ? Mathf.Max(0.2f, brawler.respawnDelayMultiplier)
                : 1f;
            return Mathf.Max(0f, respawnDelay) * multiplier;
        }

        /// <summary>
        /// Pick a safe team spawn while spreading same-frame respawns across
        /// distinct slots and avoiding points already occupied by living allies.
        /// </summary>
        public Vector3 GetSpawnPoint(TeamId team)
        {
            return GetSpawnPoint(team, -1);
        }

        public Vector3 GetSpawnPoint(TeamId team, int preferredSlot)
        {
            Transform[] set = team == TeamId.Blue ? blueSpawns : redSpawns;
            if (set == null || set.Length == 0) return Vector3.zero;

            RefreshSpawnReservations();
            HashSet<Transform> reservations = team == TeamId.Blue
                ? blueSpawnReservations
                : redSpawnReservations;
            int candidateCount = Mathf.Min(set.Length, ActiveTeamSize);
            if (candidateCount <= 0) return Vector3.zero;

            bool Occupied(Transform candidate)
            {
                if (candidate == null) return true;
                for (int i = 0; i < brawlers.Count; i++)
                {
                    BrawlerController brawler = brawlers[i];
                    if (brawler == null || brawler.IsDead || !brawler.gameObject.activeInHierarchy)
                        continue;
                    if ((candidate.position - brawler.transform.position).sqrMagnitude <
                        1.75f * 1.75f)
                        return true;
                }
                return false;
            }

            int cursor = team == TeamId.Blue ? blueSpawnCursor : redSpawnCursor;
            var candidates = new MatchSpawnCandidate[candidateCount];
            for (int index = 0; index < candidateCount; index++)
            {
                Transform candidate = set[index];
                float nearestEnemy = float.MaxValue;
                float nearestAlly = 12f;
                bool hasEnemy = false;
                bool hasAlly = false;
                if (candidate != null)
                {
                    foreach (var b in brawlers)
                    {
                        if (b == null || b.IsDead) continue;
                        float distance = Vector3.Distance(candidate.position, b.transform.position);
                        if (b.team == team)
                        {
                            hasAlly = true;
                            nearestAlly = Mathf.Min(nearestAlly, distance);
                        }
                        else
                        {
                            hasEnemy = true;
                            nearestEnemy = Mathf.Min(nearestEnemy, distance);
                        }
                    }
                }
                if (!hasEnemy) nearestEnemy = ArenaLayout.PlayableHalfExtent;
                float safety = nearestEnemy + nearestAlly * 0.35f;
                if (hasAlly && nearestAlly < 1.75f) safety -= 1000f;
                candidates[index] = new MatchSpawnCandidate(candidate != null,
                    candidate != null && reservations.Contains(candidate),
                    Occupied(candidate), safety);
            }

            int bestIndex = MatchSpawnPlanner.SelectSlot(candidates, candidateCount,
                preferredSlot, cursor);
            if (bestIndex < 0) return Vector3.zero;
            Transform best = set[bestIndex];

            reservations.Add(best);
            int nextCursor = (bestIndex + 1) % candidateCount;
            if (team == TeamId.Blue) blueSpawnCursor = nextCursor;
            else redSpawnCursor = nextCursor;
            return best.position;
        }

        void RefreshSpawnReservations()
        {
            int frame = Time.frameCount;
            if (spawnReservationFrame == frame) return;
            spawnReservationFrame = frame;
            blueSpawnReservations.Clear();
            redSpawnReservations.Clear();
        }

        void EndMatch(TeamId? winner)
        {
            if (State == MatchState.Ended) return;
            State = MatchState.Ended;
            ControlZone?.EndMode();
            int finalBlueScore = BlueScore;
            int finalRedScore = RedScore;
            if (mode == GameMode.GemGrab && GemGrabManager.Instance != null)
            {
                finalBlueScore = GemGrabManager.Instance.TeamGems(TeamId.Blue);
                finalRedScore = GemGrabManager.Instance.TeamGems(TeamId.Red);
            }
            BalanceTelemetryRuntime.EndMatch(this, winner, finalBlueScore, finalRedScore);
            foreach (var b in brawlers)
            {
                if (b == null) continue;
                b.ClearSpawnProtection();
                b.CancelOffensiveActions();
                if (b.IsDead) continue;
                if (!winner.HasValue || b.team != winner.Value) continue;

                try
                {
                    int presentationFailuresBefore = b.AnimationPresentationFailureCount;
                    b.PlayVictory();
                    int presentationFailureDelta =
                        b.AnimationPresentationFailureCount - presentationFailuresBefore;
                    if (presentationFailureDelta > 0)
                    {
                        VictoryPresentationFaultCount += presentationFailureDelta;
                        LastVictoryPresentationFaultActor = b;
                        LastVictoryPresentationFault = b.LastAnimationPresentationFailure;
                    }
                }
                catch (Exception exception)
                {
                    // Presentation is best-effort at the match boundary. Keep
                    // the fault queryable without allowing one backend to block
                    // later winners or the authoritative MatchEnded signal.
                    VictoryPresentationFaultCount++;
                    LastVictoryPresentationFaultActor = b;
                    LastVictoryPresentationFault = exception;
                }
            }
            MatchEnded?.Invoke(winner);
        }
    }
}
