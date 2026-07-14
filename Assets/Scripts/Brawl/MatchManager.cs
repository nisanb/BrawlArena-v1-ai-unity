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
        Ended
    }

    /// <summary>
    /// Match rules for the 5v5 KO brawl: timer, team scores, respawns and the
    /// end-of-match flow. Also performs a runtime NavMesh bake if the attached
    /// NavMeshSurface has no baked data yet.
    /// </summary>
    public class MatchManager : MonoBehaviour
    {
        public static MatchManager Instance { get; private set; }

        [Header("Rules")]
        public GameMode mode = GameMode.Knockout;
        public float matchDuration = 150f;
        [Tooltip("KO score that ends a Knockout match. Ignored in Gem Grab.")]
        public int scoreToWin = 8;
        public float respawnDelay = 2.5f;
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
            TimeRemaining = matchDuration;

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
            VictoryPresentationFaultCount = 0;
            LastVictoryPresentationFaultActor = null;
            LastVictoryPresentationFault = null;
            EnsureExperienceSystem().BeginMatch();
            BalanceTelemetryRuntime.BeginMatch(this);
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
                State = MatchState.Playing;
            if (State != MatchState.Playing) return;

            TimeRemaining -= Time.deltaTime;
            if (TimeRemaining <= 0f)
            {
                TimeRemaining = 0f;
                EndMatch(TimeoutWinner());
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

        /// <summary>
        /// Pick a safe team spawn while spreading same-frame respawns across
        /// distinct slots and avoiding points already occupied by living allies.
        /// </summary>
        public Vector3 GetSpawnPoint(TeamId team)
        {
            Transform[] set = team == TeamId.Blue ? blueSpawns : redSpawns;
            if (set == null || set.Length == 0) return Vector3.zero;

            RefreshSpawnReservations();
            HashSet<Transform> reservations = team == TeamId.Blue
                ? blueSpawnReservations
                : redSpawnReservations;
            int cursor = team == TeamId.Blue ? blueSpawnCursor : redSpawnCursor;
            Transform best = null;
            int bestIndex = -1;

            bool FindBest(bool skipReserved)
            {
                float bestScore = float.MinValue;
                for (int offset = 0; offset < set.Length; offset++)
                {
                    int index = (cursor + offset) % set.Length;
                    Transform candidate = set[index];
                    if (candidate == null || (skipReserved && reservations.Contains(candidate)))
                        continue;

                    float nearestEnemy = float.MaxValue;
                    float nearestAlly = 12f;
                    bool hasEnemy = false;
                    bool hasAlly = false;
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

                    if (!hasEnemy) nearestEnemy = ArenaLayout.PlayableHalfExtent;
                    float score = nearestEnemy + nearestAlly * 0.35f;
                    if (hasAlly && nearestAlly < 1.75f) score -= 1000f;
                    // Preserve the cursor order for exact ties.
                    if (best != null && score <= bestScore + 0.0001f) continue;
                    bestScore = score;
                    best = candidate;
                    bestIndex = index;
                }
                return best != null;
            }

            // There can only be more same-frame requests than slots if custom
            // content exceeds the authored team size; in that case wrap safely.
            if (!FindBest(true)) FindBest(false);
            if (best == null) return Vector3.zero;

            reservations.Add(best);
            int nextCursor = (bestIndex + 1) % set.Length;
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
