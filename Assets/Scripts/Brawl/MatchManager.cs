using System;
using System.Collections.Generic;
using Unity.AI.Navigation;
using UnityEngine;

namespace BrawlArena
{
    public enum MatchState
    {
        Intro,
        Playing,
        Ended
    }

    /// <summary>
    /// Match rules for the 3v3 KO brawl: timer, team scores, respawns and the
    /// end-of-match flow. Also performs a runtime NavMesh bake if the attached
    /// NavMeshSurface has no baked data yet.
    /// </summary>
    public class MatchManager : MonoBehaviour
    {
        public static MatchManager Instance { get; private set; }

        [Header("Rules")]
        public float matchDuration = 150f;
        public int scoreToWin = 10;
        public float respawnDelay = 2.5f;
        public float introDuration = 1.6f;

        [Header("Spawns")]
        public Transform[] blueSpawns;
        public Transform[] redSpawns;

        public MatchState State { get; private set; } = MatchState.Intro;
        public float TimeRemaining { get; private set; }
        public int BlueScore { get; private set; }
        public int RedScore { get; private set; }

        public event Action ScoreChanged;
        /// <summary>Winner, or null on a draw.</summary>
        public event Action<TeamId?> MatchEnded;

        readonly List<BrawlerController> brawlers = new List<BrawlerController>();
        float introEndsAt;

        void Awake()
        {
            Instance = this;
            Application.targetFrameRate = 60;
            TimeRemaining = matchDuration;

            var surface = GetComponent<NavMeshSurface>();
            if (surface != null && surface.navMeshData == null) surface.BuildNavMesh();
        }

        void Start()
        {
            introEndsAt = Time.time + introDuration;
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
                if (BlueScore == RedScore) EndMatch(null);
                else EndMatch(BlueScore > RedScore ? TeamId.Blue : TeamId.Red);
            }
        }

        public void Register(BrawlerController b)
        {
            if (!brawlers.Contains(b)) brawlers.Add(b);
        }

        public List<BrawlerController> GetBrawlers()
        {
            return brawlers;
        }

        public void ReportKO(BrawlerController victim, GameObject attackerGo)
        {
            if (State == MatchState.Ended) return;

            TeamId scoringTeam = TeamUtil.Other(victim.team);
            var attacker = attackerGo != null ? attackerGo.GetComponentInParent<BrawlerController>() : null;
            if (attacker != null) scoringTeam = attacker.team;
            if (scoringTeam == victim.team) return;

            if (scoringTeam == TeamId.Blue) BlueScore++;
            else RedScore++;
            ScoreChanged?.Invoke();

            if (BlueScore >= scoreToWin || RedScore >= scoreToWin)
                EndMatch(BlueScore >= scoreToWin ? TeamId.Blue : TeamId.Red);
        }

        /// <summary>Pick the team spawn farthest from living enemies.</summary>
        public Vector3 GetSpawnPoint(TeamId team)
        {
            Transform[] set = team == TeamId.Blue ? blueSpawns : redSpawns;
            if (set == null || set.Length == 0) return Vector3.zero;

            Transform best = set[0];
            float bestScore = float.MinValue;
            foreach (var s in set)
            {
                if (s == null) continue;
                float minDist = float.MaxValue;
                foreach (var b in brawlers)
                {
                    if (b == null || b.team == team || b.IsDead) continue;
                    minDist = Mathf.Min(minDist, Vector3.Distance(s.position, b.transform.position));
                }
                if (minDist > bestScore)
                {
                    bestScore = minDist;
                    best = s;
                }
            }
            return best.position;
        }

        void EndMatch(TeamId? winner)
        {
            if (State == MatchState.Ended) return;
            State = MatchState.Ended;
            foreach (var b in brawlers)
            {
                if (b == null || b.IsDead) continue;
                if (winner.HasValue && b.team == winner.Value) b.PlayVictory();
            }
            MatchEnded?.Invoke(winner);
        }
    }
}
