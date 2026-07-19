using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace BrawlArena
{
    public enum ControlZoneState
    {
        Inactive,
        Empty,
        Contested,
        BlueControlled,
        RedControlled,
    }

    public enum ControlZoneRegulationResult
    {
        Overtime,
        BlueWin,
        RedWin,
    }

    public static class ControlZoneRules
    {
        public const int TeamSize = 3;
        public const float RegulationDuration = 180f;
        public const int ScoreLimit = 90;
        public const float ScoreInterval = 1f;
        public const float RegulationRadius = 7f;
        public const float OvertimeRadius = 12f;
        public const float OvertimeExpansionPerSecond = 1f;
        public const float RespawnDelay = 6f;
        public const float SpawnProtectionDuration = 1.75f;

        /// <summary>Score gap (points) that flags a team as trailing/leading for comeback levers.</summary>
        public const int ComebackScoreDeficit = 15;
        public const float TrailingRespawnDelay = 4f;
        public const float TrailingKnockoutXpMultiplier = 1.25f;
        public const float LeadingExperienceBoxMultiplier = 0.75f;
        /// <summary>Points a valid KO awards to the scoring team during Control Zone regulation.</summary>
        public const int RegulationKnockoutPoints = 2;
        /// <summary>Anti-snowball: reduced KO award while the scoring team already leads by the comeback gap.</summary>
        public const int LeadingRegulationKnockoutPoints = 1;

        public static int ApplyScore(int current, int points, int limit = ScoreLimit)
        {
            return Mathf.Clamp(current + Mathf.Max(0, points), 0, Mathf.Max(1, limit));
        }

        public static ControlZoneRegulationResult ResolveRegulationResult(
            int blueScore, int redScore)
        {
            if (blueScore > redScore) return ControlZoneRegulationResult.BlueWin;
            if (redScore > blueScore) return ControlZoneRegulationResult.RedWin;
            return ControlZoneRegulationResult.Overtime;
        }

        public static float OvertimeRadiusAt(float elapsed, float regulationRadius,
            float overtimeRadius, float expansionPerSecond)
        {
            regulationRadius = Mathf.Max(1f, regulationRadius);
            overtimeRadius = Mathf.Max(regulationRadius, overtimeRadius);
            expansionPerSecond = Mathf.Max(0.1f, expansionPerSecond);
            elapsed = float.IsFinite(elapsed) ? Mathf.Max(0f, elapsed) : 0f;
            return Mathf.Min(overtimeRadius,
                regulationRadius + elapsed * expansionPerSecond);
        }

        /// <summary>
        /// Majority-based control: whichever team has more occupants holds the
        /// zone outright (even against a lone defender), a tied non-zero count
        /// contests it, and an empty zone is neutral.
        /// </summary>
        public static ControlZoneState ResolveState(int blueOccupants, int redOccupants)
        {
            blueOccupants = Mathf.Max(0, blueOccupants);
            redOccupants = Mathf.Max(0, redOccupants);
            if (blueOccupants == 0 && redOccupants == 0) return ControlZoneState.Empty;
            if (blueOccupants > redOccupants) return ControlZoneState.BlueControlled;
            if (redOccupants > blueOccupants) return ControlZoneState.RedControlled;
            return ControlZoneState.Contested;
        }

        /// <summary>Score gap comeback lever: true once the trailing team falls behind by the threshold.</summary>
        public static bool IsTrailing(int teamScore, int opponentScore)
        {
            return opponentScore - teamScore >= ComebackScoreDeficit;
        }

        /// <summary>Score gap comeback lever: true once the leading team is ahead by the threshold.</summary>
        public static bool IsLeading(int teamScore, int opponentScore)
        {
            return teamScore - opponentScore >= ComebackScoreDeficit;
        }

        /// <summary>Trailing victims respawn faster so a losing team can keep contesting.</summary>
        public static float RespawnDelaySeconds(int victimScore, int enemyScore)
        {
            return IsTrailing(victimScore, enemyScore) ? TrailingRespawnDelay : RespawnDelay;
        }

        /// <summary>
        /// Anti-snowball KO award: a team leading by the comeback gap earns the
        /// reduced bonus while that lead holds; a trailing or tied team keeps
        /// the full regulation bonus.
        /// </summary>
        public static int RegulationKnockoutPointsFor(int scoringTeamScore, int opponentScore)
        {
            return IsLeading(scoringTeamScore, opponentScore)
                ? LeadingRegulationKnockoutPoints
                : RegulationKnockoutPoints;
        }

        public static int ApplyTrailingKnockoutXpMultiplier(int baseXp, bool trailing)
        {
            return trailing ? Mathf.RoundToInt(baseXp * TrailingKnockoutXpMultiplier) : baseXp;
        }

        public static int ApplyLeadingExperienceBoxMultiplier(int baseXp, bool leading)
        {
            return leading ? Mathf.RoundToInt(baseXp * LeadingExperienceBoxMultiplier) : baseXp;
        }

        public static bool TryGetController(ControlZoneState state, out TeamId team)
        {
            if (state == ControlZoneState.BlueControlled)
            {
                team = TeamId.Blue;
                return true;
            }
            if (state == ControlZoneState.RedControlled)
            {
                team = TeamId.Red;
                return true;
            }
            team = default;
            return false;
        }
    }

    /// <summary>Allocation-free input record for deterministic spawn selection.</summary>
    public readonly struct MatchSpawnCandidate
    {
        public readonly bool valid;
        public readonly bool reserved;
        public readonly bool occupied;
        public readonly float safety;

        public MatchSpawnCandidate(bool valid, bool reserved, bool occupied, float safety)
        {
            this.valid = valid;
            this.reserved = reserved;
            this.occupied = occupied;
            this.safety = float.IsFinite(safety) ? safety : float.MinValue;
        }

        public bool IsOpen => valid && !reserved && !occupied;
    }

    public static class MatchSpawnPlanner
    {
        /// <summary>
        /// Selects the preferred open slot, otherwise the safest open slot in
        /// cursor order. Only when every authored slot is blocked may it reuse
        /// a valid slot. candidateCount is the authoritative mode team size.
        /// </summary>
        public static int SelectSlot(MatchSpawnCandidate[] candidates,
            int candidateCount, int preferredSlot, int cursor)
        {
            if (candidates == null) return -1;
            candidateCount = Mathf.Clamp(candidateCount, 0, candidates.Length);
            if (candidateCount == 0) return -1;
            if (preferredSlot >= 0 && preferredSlot < candidateCount &&
                candidates[preferredSlot].IsOpen)
                return preferredSlot;

            cursor = ((cursor % candidateCount) + candidateCount) % candidateCount;
            int best = SelectBest(candidates, candidateCount, cursor, true);
            return best >= 0
                ? best
                : SelectBest(candidates, candidateCount, cursor, false);
        }

        static int SelectBest(MatchSpawnCandidate[] candidates, int count,
            int cursor, bool requireOpen)
        {
            int best = -1;
            float bestSafety = float.MinValue;
            for (int offset = 0; offset < count; offset++)
            {
                int index = (cursor + offset) % count;
                MatchSpawnCandidate candidate = candidates[index];
                if (!candidate.valid || (requireOpen && !candidate.IsOpen)) continue;
                if (best >= 0 && candidate.safety <= bestSafety + 0.0001f) continue;
                best = index;
                bestSafety = candidate.safety;
            }
            return best;
        }
    }

    /// <summary>
    /// Deterministic whole-second score cadence. Partial control never survives
    /// empty, contested, controller-switch, regulation reset, or overtime entry.
    /// </summary>
    [Serializable]
    public sealed class ControlZoneScoreClock
    {
        public float Progress { get; private set; }
        public TeamId? Controller { get; private set; }

        public void Reset()
        {
            Progress = 0f;
            Controller = null;
        }

        public int Advance(ControlZoneState state, float deltaTime, out TeamId scoringTeam)
        {
            scoringTeam = default;
            if (!ControlZoneRules.TryGetController(state, out TeamId current) ||
                !float.IsFinite(deltaTime) || deltaTime <= 0f)
            {
                Reset();
                return 0;
            }

            if (!Controller.HasValue || Controller.Value != current)
            {
                Controller = current;
                Progress = 0f;
            }

            Progress += deltaTime;
            int points = Mathf.FloorToInt(Progress / ControlZoneRules.ScoreInterval);
            if (points <= 0) return 0;
            Progress -= points * ControlZoneRules.ScoreInterval;
            scoringTeam = current;
            return points;
        }
    }

    /// <summary>
    /// Brawl-owned central objective. Occupancy is sampled from authoritative
    /// BrawlerController positions; presentation has no collider or rule path.
    /// MatchManager owns time, score, and victory transitions.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class ControlZoneManager : MonoBehaviour
    {
        const int RingSegments = 64;

        public static ControlZoneManager Instance { get; private set; }

        [Min(1f)] public float regulationRadius = ControlZoneRules.RegulationRadius;
        [Min(1f)] public float overtimeRadius = ControlZoneRules.OvertimeRadius;
        [Min(0.1f)] public float overtimeExpansionPerSecond =
            ControlZoneRules.OvertimeExpansionPerSecond;

        public ControlZoneState State { get; private set; } = ControlZoneState.Inactive;
        public int BlueOccupants { get; private set; }
        public int RedOccupants { get; private set; }
        public float CurrentRadius { get; private set; } = ControlZoneRules.RegulationRadius;
        public float RenderedRadius => boundary != null ? boundary.transform.lossyScale.x : 0f;
        public float ScoreProgress => scoreClock.Progress;
        public bool IsOvertime { get; private set; }
        public bool ActiveMode => MatchManager.Instance != null &&
                                  MatchManager.Instance.mode == GameMode.ControlZone;

        /// <summary>
        /// HUD-facing controller contract. TeamId has no neutral member, so a
        /// contested/empty zone is signalled by <see cref="HasControllingTeam"/>
        /// being false rather than by a sentinel team value.
        /// </summary>
        public bool HasControllingTeam { get; private set; }
        public TeamId ControllingTeam { get; private set; }
        public bool IsContested => State == ControlZoneState.Contested;
        public Vector3 ZoneCenter => transform.position;
        /// <summary>Alias of <see cref="CurrentRadius"/> for HUD consumers.</summary>
        public float ZoneRadius => CurrentRadius;

        public event Action<ControlZoneState> StateChanged;

        readonly ControlZoneScoreClock scoreClock = new ControlZoneScoreClock();
        LineRenderer boundary;
        LineRenderer overtimeLimit;
        MaterialPropertyBlock boundaryBlock;
        MaterialPropertyBlock limitBlock;
        float overtimeElapsed;
        float lastRenderedRadius = -1f;
        ControlZoneState lastRenderedState = (ControlZoneState)(-1);
        bool lastRenderedOvertime;

        void Awake()
        {
            Instance = this;
            SanitizeConfiguration();
            EnsurePresentation();
            ResetForMatch();
        }

        void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        public void ResetForMatch()
        {
            SanitizeConfiguration();
            IsOvertime = false;
            overtimeElapsed = 0f;
            CurrentRadius = regulationRadius;
            BlueOccupants = 0;
            RedOccupants = 0;
            scoreClock.Reset();
            SetState(ActiveMode ? ControlZoneState.Empty : ControlZoneState.Inactive);
            RefreshPresentation(true);
        }

        public void BeginRegulation()
        {
            IsOvertime = false;
            overtimeElapsed = 0f;
            CurrentRadius = regulationRadius;
            scoreClock.Reset();
            EvaluateOccupancy();
            RefreshPresentation(true);
        }

        public void BeginOvertime()
        {
            IsOvertime = true;
            overtimeElapsed = 0f;
            CurrentRadius = regulationRadius;
            scoreClock.Reset();
            EvaluateOccupancy();
            RefreshPresentation(true);
        }

        public void EndMode()
        {
            scoreClock.Reset();
            SetState(ControlZoneState.Inactive);
            RefreshPresentation(true);
        }

        /// <summary>Called only by MatchManager while the authoritative match is active.</summary>
        public void Tick(float deltaTime, bool overtime)
        {
            if (!ActiveMode || !float.IsFinite(deltaTime) || deltaTime <= 0f) return;
            if (overtime)
            {
                IsOvertime = true;
                overtimeElapsed = Mathf.Min(
                    overtimeElapsed + deltaTime,
                    (overtimeRadius - regulationRadius) /
                    Mathf.Max(0.1f, overtimeExpansionPerSecond));
                CurrentRadius = ControlZoneRules.OvertimeRadiusAt(overtimeElapsed,
                    regulationRadius, overtimeRadius, overtimeExpansionPerSecond);
            }
            else
            {
                IsOvertime = false;
                CurrentRadius = regulationRadius;
            }

            EvaluateOccupancy();
            int points = scoreClock.Advance(State, deltaTime, out TeamId controller);
            MatchManager manager = MatchManager.Instance;
            if (points > 0 && manager != null)
            {
                if (overtime) manager.AddControlZoneOvertimePoint(controller);
                else manager.AddControlZoneScore(controller, points);
            }
            RefreshPresentation(false);
        }

        public bool Contains(Vector3 worldPosition)
        {
            Vector3 offset = worldPosition - transform.position;
            offset.y = 0f;
            return offset.sqrMagnitude <= CurrentRadius * CurrentRadius + 0.0001f;
        }

        public Vector3 ClampInside(Vector3 worldPosition, float margin = 0.75f)
        {
            Vector3 center = transform.position;
            Vector3 offset = worldPosition - center;
            offset.y = 0f;
            float radius = Mathf.Max(0f, CurrentRadius - Mathf.Max(0f, margin));
            if (offset.sqrMagnitude > radius * radius && offset.sqrMagnitude > 0.0001f)
                offset = offset.normalized * radius;
            return new Vector3(center.x + offset.x, worldPosition.y, center.z + offset.z);
        }

        public Vector3 TacticalPoint(TeamId team, float sideSign)
        {
            float teamDepth = team == TeamId.Blue ? -1f : 1f;
            Vector3 offset = new Vector3(Mathf.Clamp(sideSign, -1f, 1f) * 1.75f,
                0f, teamDepth * 1.4f);
            return transform.position + Vector3.ClampMagnitude(offset,
                Mathf.Max(0f, CurrentRadius - 1f));
        }

        void EvaluateOccupancy()
        {
            BlueOccupants = 0;
            RedOccupants = 0;
            MatchManager manager = MatchManager.Instance;
            if (manager == null)
            {
                SetState(ControlZoneState.Empty);
                return;
            }

            List<BrawlerController> brawlers = manager.GetBrawlers();
            for (int i = 0; i < brawlers.Count; i++)
            {
                BrawlerController brawler = brawlers[i];
                if (brawler == null || !brawler.CanContestObjective ||
                    !Contains(brawler.transform.position))
                    continue;
                if (brawler.team == TeamId.Blue) BlueOccupants++;
                else RedOccupants++;
            }
            SetState(ControlZoneRules.ResolveState(BlueOccupants, RedOccupants));
        }

        void SetState(ControlZoneState value)
        {
            HasControllingTeam = ControlZoneRules.TryGetController(value, out TeamId controller);
            if (HasControllingTeam) ControllingTeam = controller;
            if (State == value) return;
            State = value;
            StateChanged?.Invoke(value);
        }

        void SanitizeConfiguration()
        {
            regulationRadius = Mathf.Max(1f, regulationRadius);
            overtimeRadius = Mathf.Max(regulationRadius, overtimeRadius);
            overtimeExpansionPerSecond = Mathf.Max(0.1f, overtimeExpansionPerSecond);
        }

        void EnsurePresentation()
        {
            Transform boundaryChild = transform.Find("Brawl Control Boundary");
            boundary = boundaryChild != null
                ? boundaryChild.GetComponent<LineRenderer>()
                : CreateRing("Brawl Control Boundary", 0.18f);
            Transform limitChild = transform.Find("Brawl Overtime Limit");
            overtimeLimit = limitChild != null
                ? limitChild.GetComponent<LineRenderer>()
                : CreateRing("Brawl Overtime Limit", 0.08f);
        }

        LineRenderer CreateRing(string name, float width)
        {
            GameObject ring = new GameObject(name);
            ring.layer = CombatPhysics.VfxLayer;
            ring.transform.SetParent(transform, false);
            ring.transform.localPosition = new Vector3(0f, 0.08f, 0f);
            LineRenderer line = ring.AddComponent<LineRenderer>();
            line.sharedMaterial = ProjectileReadabilityRuntime.SharedCueMaterial;
            line.useWorldSpace = false;
            line.loop = true;
            line.positionCount = RingSegments;
            line.widthMultiplier = width;
            line.numCornerVertices = 2;
            line.numCapVertices = 2;
            line.shadowCastingMode = ShadowCastingMode.Off;
            line.receiveShadows = false;
            line.lightProbeUsage = LightProbeUsage.Off;
            line.reflectionProbeUsage = ReflectionProbeUsage.Off;
            for (int i = 0; i < RingSegments; i++)
            {
                float angle = i * Mathf.PI * 2f / RingSegments;
                line.SetPosition(i, new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle)));
            }
            return line;
        }

        void RefreshPresentation(bool force)
        {
            EnsurePresentation();
            bool active = ActiveMode && State != ControlZoneState.Inactive;
            if (boundary != null) boundary.enabled = active;
            if (overtimeLimit != null) overtimeLimit.enabled = active && IsOvertime;
            if (!active) return;

            if (force || Mathf.Abs(lastRenderedRadius - CurrentRadius) > 0.0001f)
            {
                boundary.transform.localScale = new Vector3(CurrentRadius, 1f, CurrentRadius);
                overtimeLimit.transform.localScale = new Vector3(overtimeRadius, 1f, overtimeRadius);
                lastRenderedRadius = CurrentRadius;
            }

            if (force || lastRenderedState != State || lastRenderedOvertime != IsOvertime)
            {
                Color color = State == ControlZoneState.BlueControlled
                    ? TeamUtil.Color(TeamId.Blue)
                    : State == ControlZoneState.RedControlled
                        ? TeamUtil.Color(TeamId.Red)
                        : State == ControlZoneState.Contested
                            ? new Color(1f, 0.68f, 0.12f)
                            : new Color(0.58f, 0.86f, 1f);
                if (IsOvertime) color = Color.Lerp(color, Color.white, 0.3f);
                SetColor(boundary, color, ref boundaryBlock);
                SetColor(overtimeLimit, new Color(1f, 0.35f, 0.82f, 0.72f),
                    ref limitBlock);
                lastRenderedState = State;
                lastRenderedOvertime = IsOvertime;
            }
        }

        static void SetColor(Renderer renderer, Color color,
            ref MaterialPropertyBlock block)
        {
            if (renderer == null) return;
            if (block == null) block = new MaterialPropertyBlock();
            block.Clear();
            block.SetColor("_Color", color);
            block.SetColor("_BaseColor", color);
            renderer.SetPropertyBlock(block);
        }
    }
}
