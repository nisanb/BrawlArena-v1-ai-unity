using System;
using UnityEngine;
using UnityEngine.AI;

namespace BrawlArena
{
    /// <summary>
    /// NavMesh planning boundary for an Invector-backed AI brawler. The bound
    /// child agent may calculate paths and desired velocity, but it never owns
    /// the actor Transform: position, rotation, and off-mesh traversal writes
    /// stay disabled while the Invector Rigidbody motor remains authoritative.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class InvectorBrawlerNavigation : MonoBehaviour, IBrawlerNavigation
    {
        const float Acceleration = 40f;
        const float MinimumProbeRadius = 0.05f;
        const float SameDestinationTolerance = 0.05f;
        const float StuckObservationSeconds = 0.75f;
        const float StuckMinimumProgress = 0.08f;
        const float StuckMinimumDesiredSpeed = 0.1f;
        const float StuckRemainingDistanceTolerance = 0.25f;
        const float StuckRetryCooldownSeconds = 1f;
        const int MaximumAutomaticRepathsPerDestination = 1;

        [SerializeField, HideInInspector] NavMeshAgent plannerAgent;
        [SerializeField, HideInInspector] bool plannerTransformNeutralConfigured;

        bool initialized;
        bool runtimePlanningOpen;
        float initializedMoveSpeed;
        float initializedStoppingDistance;
        bool hasDestinationRequest;
        Vector3 lastDestinationRequest;
        bool stuckProgressAnchorValid;
        Vector3 stuckProgressAnchor;
        float stuckObservationElapsed;
        int automaticRepathsForCurrentDestination;
        bool stuckFailClosed;
        float stuckRetryCooldownRemaining;

        int plannerOpenCount;
        int plannerCloseCount;
        int plannerSyncCount;
        int plannerSyncFailureCount;
        int plannerWarpCount;
        int plannerWarpFailureCount;
        int destinationRequestCount;
        int destinationFailureCount;
        int pathResetCount;
        int repathCount;
        int stuckFailClosedCount;
        int offMeshBlockedCount;
        float maxPlannerBodyDrift;

        public NavMeshAgent PlannerAgent => plannerAgent;
        public bool RuntimePlanningOpen => runtimePlanningOpen;
        public int PlannerOpenCount => plannerOpenCount;
        public int PlannerCloseCount => plannerCloseCount;
        public int PlannerSyncCount => plannerSyncCount;
        public int PlannerSyncFailureCount => plannerSyncFailureCount;
        public int PlannerWarpCount => plannerWarpCount;
        public int PlannerWarpFailureCount => plannerWarpFailureCount;
        public int DestinationRequestCount => destinationRequestCount;
        public int DestinationFailureCount => destinationFailureCount;
        public int PathResetCount => pathResetCount;
        public int RepathCount => repathCount;
        public int StuckFailClosedCount => stuckFailClosedCount;
        public int OffMeshBlockedCount => offMeshBlockedCount;
        public float MaxPlannerBodyDrift => maxPlannerBodyDrift;

        public bool IsDormantConfigured =>
            HasCompleteConfiguration && !gameObject.activeSelf && !enabled &&
            !initialized && !runtimePlanningOpen && !plannerAgent.enabled &&
            plannerTransformNeutralConfigured &&
            !plannerAgent.autoTraverseOffMeshLink;

        public bool IsReady
        {
            get
            {
                if (!initialized || !RuntimeAgentAvailable ||
                    plannerAgent.isOnOffMeshLink)
                    return false;

                // updatePosition=false prevents the agent simulation from
                // following Rigidbody motion automatically. Re-anchor before
                // every planner query so desired velocity is calculated from
                // the body, never from a simulation point that ran ahead.
                if (!TrySynchronizePlannerPositionInternal(
                        transform.position, false, true))
                    return false;

                float probeRadius = Mathf.Max(
                    MinimumProbeRadius, plannerAgent.radius * 0.5f);
                return NavMesh.SamplePosition(
                    plannerAgent.nextPosition, out _, probeRadius,
                    plannerAgent.areaMask);
            }
        }

        public bool HasPath => IsReady && plannerAgent.hasPath;

        public Vector3 DesiredVelocity =>
            IsReady && hasDestinationRequest && plannerAgent.hasPath &&
            !plannerAgent.pathPending
                ? plannerAgent.desiredVelocity
                : Vector3.zero;

        bool HasCompleteConfiguration
        {
            get
            {
                if (plannerAgent == null || plannerAgent.gameObject == gameObject ||
                    !plannerAgent.transform.IsChildOf(transform) ||
                    !plannerAgent.gameObject.activeSelf)
                    return false;

                NavMeshAgent[] agents = GetComponentsInChildren<NavMeshAgent>(true);
                return agents.Length == 1 && agents[0] == plannerAgent;
            }
        }

        bool RuntimeAgentAvailable =>
            runtimePlanningOpen && isActiveAndEnabled && plannerAgent != null &&
            plannerAgent.enabled && plannerAgent.isOnNavMesh;

        /// <summary>
        /// Builder-only binding for the one dedicated child planning agent.
        /// The component and agent remain disabled until the runtime gate opens.
        /// </summary>
        public void ConfigureDormant(NavMeshAgent configuredAgent)
        {
            if (configuredAgent == null)
                throw new ArgumentNullException(nameof(configuredAgent));
            if (configuredAgent.gameObject == gameObject ||
                !configuredAgent.transform.IsChildOf(transform))
            {
                throw new ArgumentException(
                    "The Invector planning agent must live on one dedicated child.",
                    nameof(configuredAgent));
            }
            if (!configuredAgent.gameObject.activeSelf)
            {
                throw new ArgumentException(
                    "The dedicated planning child must remain locally active so its agent can be gated independently.",
                    nameof(configuredAgent));
            }

            NavMeshAgent[] agents = GetComponentsInChildren<NavMeshAgent>(true);
            if (agents.Length != 1 || agents[0] != configuredAgent)
            {
                throw new InvalidOperationException(
                    "The Invector AI topology requires exactly one child NavMeshAgent.");
            }

            plannerAgent = configuredAgent;
            if (plannerAgent.enabled) plannerAgent.enabled = false;
            EnforceTransformNeutralPlanner();
            plannerTransformNeutralConfigured = true;
            initialized = false;
            runtimePlanningOpen = false;
            initializedMoveSpeed = 0f;
            initializedStoppingDistance = 0f;
            hasDestinationRequest = false;
            lastDestinationRequest = Vector3.zero;
            ResetTraceFields();
            enabled = false;
        }

        public void Initialize(float moveSpeed, float stoppingDistance)
        {
            EnsureConfigured();
            if (!IsFinite(moveSpeed) || moveSpeed <= 0f)
                throw new ArgumentOutOfRangeException(nameof(moveSpeed));
            if (!IsFinite(stoppingDistance) || stoppingDistance < 0f)
                throw new ArgumentOutOfRangeException(nameof(stoppingDistance));

            if (initialized)
            {
                if (!Mathf.Approximately(initializedMoveSpeed, moveSpeed) ||
                    !Mathf.Approximately(initializedStoppingDistance, stoppingDistance))
                {
                    throw new InvalidOperationException(
                        "The initialized Invector navigator cannot adopt different movement settings.");
                }
                return;
            }

            initializedMoveSpeed = moveSpeed;
            initializedStoppingDistance = stoppingDistance;
            plannerAgent.speed = moveSpeed;
            plannerAgent.acceleration = Acceleration;
            // Rotation is always motor-owned; keep the planner's rotational
            // speed inert as an additional serialized/runtime firewall.
            plannerAgent.angularSpeed = 0f;
            plannerAgent.stoppingDistance = stoppingDistance;
            plannerAgent.autoBraking = true;
            plannerAgent.autoRepath = true;
            EnforceTransformNeutralPlanner();
            initialized = true;
        }

        public bool TrySamplePosition(
            Vector3 candidate, float maxDistance, out Vector3 sampledPosition)
        {
            sampledPosition = candidate;
            if (!IsFinite(candidate) || !IsFinite(maxDistance) ||
                maxDistance < 0f || !IsReady)
                return false;

            if (!NavMesh.SamplePosition(
                    candidate, out NavMeshHit hit, maxDistance,
                    plannerAgent.areaMask))
                return false;

            sampledPosition = hit.position;
            return true;
        }

        public void SetDestination(Vector3 destination)
        {
            if (!IsFinite(destination) || !IsReady) return;

            bool repeatsFailClosedDestination = stuckFailClosed &&
                PlanarSqrDistance(destination, lastDestinationRequest) <=
                SameDestinationTolerance * SameDestinationTolerance;
            if (repeatsFailClosedDestination && stuckRetryCooldownRemaining > 0f)
                return;

            // Unity can transiently report neither pathPending nor hasPath
            // after accepting a destination or an automatic repath. The
            // project-owned request is the durable duplicate-suppression
            // boundary; otherwise repeated tactical intent can reset the
            // bounded stuck-recovery budget during that native flag gap.
            if (hasDestinationRequest &&
                PlanarSqrDistance(destination, lastDestinationRequest) <=
                SameDestinationTolerance * SameDestinationTolerance)
                return;

            destinationRequestCount++;
            try
            {
                if (!plannerAgent.SetDestination(destination))
                {
                    destinationFailureCount++;
                    hasDestinationRequest = false;
                    ResetStuckWatchdog(true);
                    return;
                }

                hasDestinationRequest = true;
                lastDestinationRequest = destination;
                StartStuckObservation(destination);
            }
            catch (InvalidOperationException)
            {
                destinationFailureCount++;
                hasDestinationRequest = false;
                ResetStuckWatchdog(true);
            }
        }

        public void ClearPath()
        {
            if (!RuntimeAgentAvailable) return;
            if (plannerAgent.isOnOffMeshLink)
            {
                offMeshBlockedCount++;
                return;
            }

            if (!TrySynchronizePlannerPositionInternal(
                    transform.position, false, true))
                return;

            if (hasDestinationRequest || plannerAgent.hasPath || plannerAgent.pathPending)
            {
                try
                {
                    plannerAgent.ResetPath();
                    pathResetCount++;
                }
                catch (InvalidOperationException)
                {
                    destinationFailureCount++;
                }
            }
            hasDestinationRequest = false;
            ResetStuckWatchdog(true);
        }

        public void SetExternalFacing(bool externalFacing)
        {
            if (plannerAgent == null) return;
            // Both tactical and travel facing are owned by the Rigidbody motor.
            // The parameter is intentionally accepted only to preserve the
            // shared planner contract; it can never restore agent rotation.
            EnforceTransformNeutralPlanner();
        }

        /// <summary>
        /// Runtime-gate operation. Enables only agent simulation and seeds it
        /// from the physical body's current world position.
        /// </summary>
        public void OpenPlanner(Vector3 worldPosition)
        {
            EnsureConfigured();
            if (!Application.isPlaying)
                throw new InvalidOperationException(
                    "The Invector AI planner can open only in Play mode.");
            if (!IsFinite(worldPosition))
                throw new ArgumentException(
                    "Planner position must be finite.", nameof(worldPosition));
            if (runtimePlanningOpen)
                throw new InvalidOperationException(
                    "The Invector AI planner is already open.");
            if (!isActiveAndEnabled || !gameObject.activeInHierarchy)
                throw new InvalidOperationException(
                    "Enable the root navigator and actor before opening its child planner.");

            EnforceTransformNeutralPlanner();
            try
            {
                plannerAgent.enabled = true;
                runtimePlanningOpen = true;
                if (!WarpPlanner(worldPosition))
                    throw new InvalidOperationException(
                        "The child planning agent could not bind to the spawn NavMesh.");
                if (plannerAgent.isOnNavMesh) plannerAgent.isStopped = false;
                plannerOpenCount++;
            }
            catch
            {
                runtimePlanningOpen = false;
                if (plannerAgent != null && plannerAgent.enabled)
                    plannerAgent.enabled = false;
                EnforceTransformNeutralPlanner();
                throw;
            }
        }

        /// <summary>
        /// Disables planning without touching the actor Transform or Rigidbody.
        /// Safe path reset is skipped while Unity reports an off-mesh link.
        /// </summary>
        public void ClosePlanner()
        {
            if (plannerAgent == null)
            {
                runtimePlanningOpen = false;
                initialized = false;
                enabled = false;
                return;
            }

            if (plannerAgent.enabled && plannerAgent.isOnNavMesh)
            {
                if (plannerAgent.isOnOffMeshLink)
                {
                    offMeshBlockedCount++;
                }
                else
                {
                    try
                    {
                        if (hasDestinationRequest || plannerAgent.hasPath || plannerAgent.pathPending)
                        {
                            plannerAgent.ResetPath();
                            pathResetCount++;
                        }
                        plannerAgent.isStopped = true;
                    }
                    catch (InvalidOperationException)
                    {
                        destinationFailureCount++;
                    }
                }
            }

            if (plannerAgent.enabled) plannerAgent.enabled = false;
            EnforceTransformNeutralPlanner();
            runtimePlanningOpen = false;
            initialized = false;
            initializedMoveSpeed = 0f;
            initializedStoppingDistance = 0f;
            hasDestinationRequest = false;
            lastDestinationRequest = Vector3.zero;
            ResetStuckWatchdog(true);
            plannerCloseCount++;
            enabled = false;
        }

        /// <summary>
        /// Keeps the planner simulation colocated with the Rigidbody-owned body.
        /// This writes NavMeshAgent.nextPosition only; it never writes a Transform.
        /// </summary>
        public bool SynchronizePlannerPosition(
            Vector3 worldPosition, bool clearPath)
        {
            bool synchronized = TrySynchronizePlannerPositionInternal(
                worldPosition, clearPath, true);
            if (!synchronized)
            {
                ResetStuckProgressObservation();
                return false;
            }

            if (clearPath)
            {
                ResetStuckWatchdog(true);
                return true;
            }

            AdvanceStuckRetryCooldown();
            EvaluateStuckWatchdog(worldPosition);
            return true;
        }

        /// <summary>
        /// Rebinds only the child agent simulation after a Brawl-owned teleport.
        /// updatePosition/updateRotation remain false, so the actor pose is never
        /// sourced from this operation.
        /// </summary>
        public bool WarpPlanner(Vector3 worldPosition)
        {
            if (!IsFinite(worldPosition) || !RuntimeAgentAvailable)
            {
                plannerWarpFailureCount++;
                return false;
            }
            EnforceTransformNeutralPlanner();
            if (plannerAgent.isOnOffMeshLink)
            {
                offMeshBlockedCount++;
                plannerWarpFailureCount++;
                return false;
            }

            float probeRadius = Mathf.Max(
                MinimumProbeRadius, plannerAgent.radius * 0.5f);
            if (!NavMesh.SamplePosition(
                    worldPosition, out NavMeshHit hit, probeRadius,
                    plannerAgent.areaMask))
            {
                plannerWarpFailureCount++;
                return false;
            }

            EnforceTransformNeutralPlanner();
            bool warped;
            try
            {
                warped = plannerAgent.Warp(hit.position);
            }
            catch (InvalidOperationException)
            {
                warped = false;
            }

            EnforceTransformNeutralPlanner();
            if (!warped)
            {
                plannerWarpFailureCount++;
                return false;
            }

            hasDestinationRequest = false;
            ResetStuckWatchdog(true);
            plannerWarpCount++;
            return true;
        }

        public void ResetRuntimeTrace()
        {
            EnsureConfigured();
            if (enabled || runtimePlanningOpen || plannerAgent.enabled || initialized)
            {
                throw new InvalidOperationException(
                    "Close the Invector AI planner before resetting runtime trace.");
            }
            ResetTraceFields();
        }

        void ResetTraceFields()
        {
            plannerOpenCount = 0;
            plannerCloseCount = 0;
            plannerSyncCount = 0;
            plannerSyncFailureCount = 0;
            plannerWarpCount = 0;
            plannerWarpFailureCount = 0;
            destinationRequestCount = 0;
            destinationFailureCount = 0;
            pathResetCount = 0;
            repathCount = 0;
            stuckFailClosedCount = 0;
            offMeshBlockedCount = 0;
            maxPlannerBodyDrift = 0f;
            ResetStuckWatchdog(true);
        }

        void EvaluateStuckWatchdog(Vector3 worldPosition)
        {
            if (stuckFailClosed || !hasDestinationRequest ||
                plannerAgent.pathPending || !plannerAgent.hasPath ||
                plannerAgent.isOnOffMeshLink || plannerAgent.isStopped ||
                plannerAgent.pathStatus != NavMeshPathStatus.PathComplete)
            {
                ResetStuckProgressObservation();
                return;
            }

            Vector3 desiredVelocity = plannerAgent.desiredVelocity;
            desiredVelocity.y = 0f;
            float remainingDistance = plannerAgent.remainingDistance;
            if (!IsFinite(remainingDistance) ||
                remainingDistance <= initializedStoppingDistance +
                                     StuckRemainingDistanceTolerance ||
                desiredVelocity.sqrMagnitude <
                StuckMinimumDesiredSpeed * StuckMinimumDesiredSpeed)
            {
                ResetStuckProgressObservation();
                return;
            }

            if (!stuckProgressAnchorValid)
            {
                stuckProgressAnchorValid = true;
                stuckProgressAnchor = worldPosition;
                stuckObservationElapsed = 0f;
                return;
            }

            if (PlanarSqrDistance(worldPosition, stuckProgressAnchor) >=
                StuckMinimumProgress * StuckMinimumProgress)
            {
                stuckProgressAnchor = worldPosition;
                stuckObservationElapsed = 0f;
                return;
            }

            stuckObservationElapsed += Mathf.Max(0.001f, Time.fixedDeltaTime);
            if (stuckObservationElapsed < StuckObservationSeconds) return;

            if (automaticRepathsForCurrentDestination <
                MaximumAutomaticRepathsPerDestination)
            {
                TryAutomaticRepath();
                return;
            }

            FailStuckClosed();
        }

        void TryAutomaticRepath()
        {
            automaticRepathsForCurrentDestination++;
            Vector3 destination = lastDestinationRequest;
            try
            {
                plannerAgent.ResetPath();
                pathResetCount++;
                hasDestinationRequest = false;
                if (!plannerAgent.SetDestination(destination))
                {
                    destinationFailureCount++;
                    FailStuckClosed();
                    return;
                }

                hasDestinationRequest = true;
                repathCount++;
                ResetStuckProgressObservation();
            }
            catch (InvalidOperationException)
            {
                destinationFailureCount++;
                hasDestinationRequest = false;
                FailStuckClosed();
            }
        }

        void FailStuckClosed()
        {
            if (!plannerAgent.isOnOffMeshLink &&
                (hasDestinationRequest || plannerAgent.hasPath ||
                 plannerAgent.pathPending))
            {
                try
                {
                    plannerAgent.ResetPath();
                    pathResetCount++;
                }
                catch (InvalidOperationException)
                {
                    destinationFailureCount++;
                }
            }

            hasDestinationRequest = false;
            if (!stuckFailClosed) stuckFailClosedCount++;
            stuckFailClosed = true;
            stuckRetryCooldownRemaining = StuckRetryCooldownSeconds;
            ResetStuckProgressObservation();
        }

        void StartStuckObservation(Vector3 destination)
        {
            lastDestinationRequest = destination;
            automaticRepathsForCurrentDestination = 0;
            stuckFailClosed = false;
            stuckRetryCooldownRemaining = 0f;
            ResetStuckProgressObservation();
        }

        void AdvanceStuckRetryCooldown()
        {
            if (!stuckFailClosed || stuckRetryCooldownRemaining <= 0f) return;
            stuckRetryCooldownRemaining = Mathf.Max(
                0f, stuckRetryCooldownRemaining -
                    Mathf.Max(0.001f, Time.fixedDeltaTime));
        }

        void ResetStuckWatchdog(bool clearDestination)
        {
            ResetStuckProgressObservation();
            automaticRepathsForCurrentDestination = 0;
            stuckFailClosed = false;
            stuckRetryCooldownRemaining = 0f;
            if (clearDestination) lastDestinationRequest = Vector3.zero;
        }

        void ResetStuckProgressObservation()
        {
            stuckProgressAnchorValid = false;
            stuckProgressAnchor = Vector3.zero;
            stuckObservationElapsed = 0f;
        }

        bool TrySynchronizePlannerPositionInternal(
            Vector3 worldPosition, bool clearPath, bool recordTrace)
        {
            if (!IsFinite(worldPosition) || !RuntimeAgentAvailable)
            {
                if (recordTrace) plannerSyncFailureCount++;
                return false;
            }
            EnforceTransformNeutralPlanner();
            if (plannerAgent.isOnOffMeshLink)
            {
                if (recordTrace)
                {
                    offMeshBlockedCount++;
                    plannerSyncFailureCount++;
                }
                return false;
            }

            maxPlannerBodyDrift = Mathf.Max(
                maxPlannerBodyDrift,
                Vector3.Distance(plannerAgent.nextPosition, worldPosition));

            float probeRadius = Mathf.Max(
                MinimumProbeRadius, plannerAgent.radius * 0.5f);
            if (!NavMesh.SamplePosition(
                    worldPosition, out NavMeshHit hit, probeRadius,
                    plannerAgent.areaMask))
            {
                if (recordTrace) plannerSyncFailureCount++;
                return false;
            }

            if (clearPath && (hasDestinationRequest || plannerAgent.hasPath ||
                              plannerAgent.pathPending))
            {
                try
                {
                    plannerAgent.ResetPath();
                    pathResetCount++;
                    hasDestinationRequest = false;
                }
                catch (InvalidOperationException)
                {
                    if (recordTrace)
                    {
                        destinationFailureCount++;
                        plannerSyncFailureCount++;
                    }
                    return false;
                }
            }

            plannerAgent.nextPosition = hit.position;
            if (recordTrace) plannerSyncCount++;
            return true;
        }

        void EnsureConfigured()
        {
            if (!HasCompleteConfiguration || !plannerTransformNeutralConfigured)
            {
                throw new InvalidOperationException(
                    "Configure the Invector navigator with exactly one dedicated child agent first.");
            }
        }

        void EnforceTransformNeutralPlanner()
        {
            if (plannerAgent == null) return;
            plannerAgent.updatePosition = false;
            plannerAgent.updateRotation = false;
            plannerAgent.updateUpAxis = false;
            plannerAgent.autoTraverseOffMeshLink = false;
        }

        static float PlanarSqrDistance(Vector3 a, Vector3 b)
        {
            a.y = 0f;
            b.y = 0f;
            return (a - b).sqrMagnitude;
        }

        static bool IsFinite(Vector3 value) =>
            IsFinite(value.x) && IsFinite(value.y) && IsFinite(value.z);

        static bool IsFinite(float value) =>
            !float.IsNaN(value) && !float.IsInfinity(value);
    }
}
