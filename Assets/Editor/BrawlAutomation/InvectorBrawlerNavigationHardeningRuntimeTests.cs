using System.Collections;
using System.Collections.Generic;
using System.IO;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace BrawlArena.EditorAutomation.Tests
{
    /// <summary>
    /// In-memory Phase 3G planner hardening. The fixture never loads, saves,
    /// or modifies a scene or NavMesh asset; its only navigation data exists
    /// for the duration of Play mode.
    /// </summary>
    public sealed class InvectorBrawlerNavigationHardeningRuntimeTests
    {
        const float MoveSpeed = 5f;
        const float StoppingDistance = 0.75f;
        const string RuntimeActorName = "Phase3G_StuckWatchdogActor";
        const string RuntimeNavMeshDataName = "Phase3G_StuckWatchdogNavMesh";
        const string NavigatorSourcePath =
            "Assets/Scripts/Brawl/Integration/Invector/InvectorBrawlerNavigation.cs";

        static readonly Vector3 NavMeshCenter =
            new Vector3(1400f, 0f, 1400f);
        static readonly Vector3 OffMeshCenter =
            new Vector3(1450f, 0f, 1450f);

        [Test]
        [Category("InvectorAIHardening")]
        public void WatchdogUsesTheExistingSyncBoundaryWithoutAddingTraversalOrSchedulers()
        {
            string source = File.ReadAllText(NavigatorSourcePath);

            StringAssert.Contains("EvaluateStuckWatchdog(worldPosition);", source);
            StringAssert.Contains("public int RepathCount => repathCount;", source);
            StringAssert.Contains(
                "public int StuckFailClosedCount => stuckFailClosedCount;", source);
            StringAssert.DoesNotContain("CompleteOffMeshLink(", source);
            StringAssert.DoesNotContain("void Update(", source);
            StringAssert.DoesNotContain("void FixedUpdate(", source);
            StringAssert.DoesNotContain("void LateUpdate(", source);
            StringAssert.DoesNotContain("void OnAnimatorMove(", source);
        }

        [UnityTest]
        [Category("InvectorAIHardening")]
        public IEnumerator RuntimePlannerRepathsOnceThenFailsClosedWithBoundedRetry()
        {
            Scene original = SceneManager.GetActiveScene();
            string originalPath = original.path;
            bool originalDirty = original.isDirty;

            yield return new EnterPlayMode();

            NavMeshData navMeshData = BuildRuntimeNavMesh();
            RuntimeArtifacts.TrackNavMeshData(navMeshData);
            GameObject actor = null;
            InvectorBrawlerNavigation navigation = null;
            NavMeshAgent planner = null;

            bool navMeshBuilt = navMeshData != null;
            bool opened = false;
            bool initialPathSolved = false;
            bool boundedRecoveryObserved = false;
            bool cooldownRejectedDuplicate = false;
            bool differentDestinationRecovered = false;
            bool transformsStayedNeutral = false;
            int requestCountAtFailClosed = -1;
            int pathResetCountAtFailClosed = -1;
            string watchdogTelemetry = "navigator unavailable";
            Vector3 start = NavMeshCenter;
            Vector3 destination = NavMeshCenter + Vector3.right * 6f;
            Vector3 recoveryDestination = NavMeshCenter + Vector3.forward * 3f;

            if (navMeshBuilt)
            {
                NavMeshDataInstance navMeshInstance =
                    NavMesh.AddNavMeshData(navMeshData);
                RuntimeArtifacts.TrackNavMeshInstance(navMeshInstance);
                opened = TryCreateOpenNavigator(
                    NavMeshCenter, out start, out actor,
                    out navigation, out planner);
            }

            float readyDeadline = Time.realtimeSinceStartup + 2f;
            for (int frame = 0; frame < 120 && navigation != null &&
                 !navigation.IsReady && Time.realtimeSinceStartup < readyDeadline;
                 frame++)
            {
                yield return new WaitForFixedUpdate();
            }

            if (navigation != null && navigation.IsReady &&
                navigation.TrySamplePosition(
                    start + Vector3.right * 6f, 2f, out destination) &&
                navigation.TrySamplePosition(
                    start + Vector3.forward * 3f, 2f,
                    out recoveryDestination))
            {
                navigation.SetDestination(destination);
            }

            float pathDeadline = Time.realtimeSinceStartup + 3f;
            for (int frame = 0; frame < 180 && navigation != null &&
                 (!navigation.HasPath ||
                  navigation.DesiredVelocity.sqrMagnitude <= 0.01f) &&
                 Time.realtimeSinceStartup < pathDeadline; frame++)
            {
                yield return new WaitForFixedUpdate();
            }

            initialPathSolved = navigation != null && navigation.HasPath &&
                                navigation.DesiredVelocity.sqrMagnitude > 0.01f;
            int initialRequestCount = navigation != null
                ? navigation.DestinationRequestCount
                : -1;

            float stuckDeadline = Time.realtimeSinceStartup + 6f;
            for (int frame = 0; frame < 360 && initialPathSolved &&
                 navigation.StuckFailClosedCount == 0 &&
                 Time.realtimeSinceStartup < stuckDeadline; frame++)
            {
                // Match AIBrawler's repeated tactical request while keeping
                // the Rigidbody-owned root intentionally motionless.
                navigation.SetDestination(destination);
                navigation.SynchronizePlannerPosition(start, false);
                yield return new WaitForFixedUpdate();
            }

            if (initialPathSolved)
            {
                boundedRecoveryObserved = navigation.RepathCount == 1 &&
                    navigation.StuckFailClosedCount == 1 &&
                    !navigation.HasPath &&
                    navigation.DesiredVelocity == Vector3.zero;
                requestCountAtFailClosed = navigation.DestinationRequestCount;
                pathResetCountAtFailClosed = navigation.PathResetCount;

                // Stay well inside the one-second cooldown. Repeated copies
                // of the failed destination must not create a request loop.
                for (int frame = 0; frame < 10; frame++)
                {
                    navigation.SetDestination(destination);
                    navigation.SynchronizePlannerPosition(start, false);
                    yield return new WaitForFixedUpdate();
                }

                cooldownRejectedDuplicate =
                    navigation.DestinationRequestCount ==
                        requestCountAtFailClosed &&
                    navigation.RepathCount == 1 &&
                    navigation.StuckFailClosedCount == 1 &&
                    navigation.PathResetCount == pathResetCountAtFailClosed;

                navigation.SetDestination(recoveryDestination);
                float recoveryDeadline = Time.realtimeSinceStartup + 3f;
                for (int frame = 0; frame < 180 &&
                     (!navigation.HasPath ||
                      navigation.DesiredVelocity.sqrMagnitude <= 0.01f) &&
                     Time.realtimeSinceStartup < recoveryDeadline; frame++)
                {
                    yield return new WaitForFixedUpdate();
                }

                differentDestinationRecovered = navigation.HasPath &&
                    navigation.DesiredVelocity.sqrMagnitude > 0.01f &&
                    navigation.DestinationRequestCount == initialRequestCount + 1 &&
                    navigation.RepathCount == 1 &&
                    navigation.StuckFailClosedCount == 1;

                transformsStayedNeutral =
                    PlanarDistance(actor.transform.position, start) <= 0.001f &&
                    planner.transform.localPosition == Vector3.zero &&
                    !planner.updatePosition && !planner.updateRotation &&
                    !planner.updateUpAxis && !planner.autoTraverseOffMeshLink;

                watchdogTelemetry = string.Format(
                    "repath={0}, failClosed={1}, requests={2}, resets={3}, " +
                    "hasPath={4}, desired={5}, pending={6}, status={7}, " +
                    "stopped={8}, syncFailures={9}",
                    navigation.RepathCount,
                    navigation.StuckFailClosedCount,
                    navigation.DestinationRequestCount,
                    navigation.PathResetCount,
                    navigation.HasPath,
                    navigation.DesiredVelocity,
                    planner.pathPending,
                    planner.pathStatus,
                    planner.isStopped,
                    navigation.PlannerSyncFailureCount);
            }

            RuntimeArtifacts.Cleanup();
            yield return null;
            yield return new ExitPlayMode();

            Scene restored = SceneManager.GetSceneByPath(originalPath);
            Assert.That(navMeshBuilt, Is.True,
                "The in-memory NavMesh failed to build.");
            Assert.That(opened, Is.True,
                "The transform-neutral planner did not open on the runtime NavMesh.");
            Assert.That(initialPathSolved, Is.True,
                "The runtime planner did not solve the initial watchdog path.");
            Assert.That(boundedRecoveryObserved, Is.True,
                "The watchdog did not perform exactly one repath before failing intent closed. " +
                watchdogTelemetry);
            Assert.That(cooldownRejectedDuplicate, Is.True,
                "The fail-closed cooldown allowed a same-destination request/reset loop.");
            Assert.That(differentDestinationRecovered, Is.True,
                "A materially different tactical destination did not reopen planning.");
            Assert.That(transformsStayedNeutral, Is.True,
                "The watchdog changed Transform ownership or native planner write flags.");
            Assert.That(restored.IsValid() && restored.isLoaded, Is.True,
                "The caller's original scene was not retained.");
            Assert.That(restored.isDirty, Is.EqualTo(originalDirty),
                "The caller's original scene dirty state changed.");
        }

        [UnityTest]
        [Category("InvectorAIHardening")]
        public IEnumerator RemovedRuntimeNavMeshFailsEveryPlannerQueryClosed()
        {
            Scene original = SceneManager.GetActiveScene();
            string originalPath = original.path;
            bool originalDirty = original.isDirty;

            yield return new EnterPlayMode();

            NavMeshData navMeshData = BuildRuntimeNavMesh();
            RuntimeArtifacts.TrackNavMeshData(navMeshData);
            GameObject actor = null;
            InvectorBrawlerNavigation navigation = null;
            NavMeshAgent planner = null;
            Vector3 start = NavMeshCenter;
            Vector3 destination = NavMeshCenter + Vector3.right * 6f;

            bool navMeshBuilt = navMeshData != null;
            bool opened = false;
            bool initialPathSolved = false;
            bool navMeshInstanceRemoved = false;
            bool failedClosed = false;
            bool rejectedNewRequest = false;
            bool transformsStayedExternal = false;
            string telemetry = "navigator unavailable";

            if (navMeshBuilt)
            {
                NavMeshDataInstance navMeshInstance =
                    NavMesh.AddNavMeshData(navMeshData);
                RuntimeArtifacts.TrackNavMeshInstance(navMeshInstance);
                opened = TryCreateOpenNavigator(
                    NavMeshCenter, out start, out actor,
                    out navigation, out planner);
            }

            float readyDeadline = Time.realtimeSinceStartup + 2f;
            for (int frame = 0; frame < 120 && navigation != null &&
                 !navigation.IsReady && Time.realtimeSinceStartup < readyDeadline;
                 frame++)
            {
                yield return new WaitForFixedUpdate();
            }

            if (navigation != null && navigation.IsReady &&
                navigation.TrySamplePosition(
                    start + Vector3.right * 6f, 2f, out destination))
            {
                navigation.SetDestination(destination);
            }

            float pathDeadline = Time.realtimeSinceStartup + 3f;
            for (int frame = 0; frame < 180 && navigation != null &&
                 (!navigation.HasPath ||
                  navigation.DesiredVelocity.sqrMagnitude <= 0.01f) &&
                 Time.realtimeSinceStartup < pathDeadline; frame++)
            {
                yield return new WaitForFixedUpdate();
            }

            initialPathSolved = navigation != null && navigation.HasPath &&
                                navigation.DesiredVelocity.sqrMagnitude > 0.01f;
            int requestCountBeforeRemoval = navigation != null
                ? navigation.DestinationRequestCount
                : -1;
            int pathResetCountBeforeRemoval = navigation != null
                ? navigation.PathResetCount
                : -1;
            Vector3 rootPositionBeforeRemoval = actor != null
                ? actor.transform.position
                : Vector3.zero;
            Quaternion rootRotationBeforeRemoval = actor != null
                ? actor.transform.rotation
                : Quaternion.identity;

            if (initialPathSolved)
                navMeshInstanceRemoved = RuntimeArtifacts.RemoveNavMeshInstance();

            float removalDeadline = Time.realtimeSinceStartup + 2f;
            for (int frame = 0; frame < 120 && initialPathSolved &&
                 navigation.IsReady &&
                 Time.realtimeSinceStartup < removalDeadline; frame++)
            {
                yield return new WaitForFixedUpdate();
            }

            if (initialPathSolved && navMeshInstanceRemoved)
            {
                Vector3 rejectedCandidate = start + Vector3.left * 2f;
                bool sampled = navigation.TrySamplePosition(
                    rejectedCandidate, 2f, out Vector3 sampledPosition);
                int syncFailuresBefore = navigation.PlannerSyncFailureCount;
                navigation.SetDestination(rejectedCandidate);
                navigation.ClearPath();
                bool synchronized = navigation.SynchronizePlannerPosition(
                    actor.transform.position, false);

                failedClosed = planner.enabled &&
                    navigation.RuntimePlanningOpen && !navigation.IsReady &&
                    !navigation.HasPath &&
                    navigation.DesiredVelocity == Vector3.zero &&
                    !sampled && sampledPosition == rejectedCandidate &&
                    !synchronized &&
                    navigation.PlannerSyncFailureCount > syncFailuresBefore;
                rejectedNewRequest =
                    navigation.DestinationRequestCount ==
                        requestCountBeforeRemoval &&
                    navigation.PathResetCount == pathResetCountBeforeRemoval;
                transformsStayedExternal =
                    actor.transform.position == rootPositionBeforeRemoval &&
                    actor.transform.rotation == rootRotationBeforeRemoval &&
                    planner.transform.localPosition == Vector3.zero &&
                    planner.transform.localRotation == Quaternion.identity &&
                    !planner.updatePosition && !planner.updateRotation &&
                    !planner.updateUpAxis && !planner.autoTraverseOffMeshLink;

                telemetry = string.Format(
                    "enabled={0}, onMesh={1}, ready={2}, hasPath={3}, " +
                    "desired={4}, requests={5}/{6}, resets={7}/{8}, " +
                    "syncFailures={9}, sampled={10}, synchronized={11}",
                    planner.enabled,
                    planner.isOnNavMesh,
                    navigation.IsReady,
                    navigation.HasPath,
                    navigation.DesiredVelocity,
                    navigation.DestinationRequestCount,
                    requestCountBeforeRemoval,
                    navigation.PathResetCount,
                    pathResetCountBeforeRemoval,
                    navigation.PlannerSyncFailureCount,
                    sampled,
                    synchronized);
            }

            RuntimeArtifacts.Cleanup();
            yield return null;
            yield return new ExitPlayMode();

            Scene restored = SceneManager.GetSceneByPath(originalPath);
            Assert.That(navMeshBuilt, Is.True,
                "The stale-mesh fixture could not build runtime navigation data.");
            Assert.That(opened, Is.True,
                "The stale-mesh fixture could not open its planner.");
            Assert.That(initialPathSolved, Is.True,
                "The stale-mesh fixture did not resolve its control destination.");
            Assert.That(navMeshInstanceRemoved, Is.True,
                "The live runtime NavMeshData instance was not removed.");
            Assert.That(failedClosed, Is.True,
                "Planner queries did not fail closed after NavMeshData removal. " +
                telemetry);
            Assert.That(rejectedNewRequest, Is.True,
                "The stale planner accepted a new request or reset its old native path. " +
                telemetry);
            Assert.That(transformsStayedExternal, Is.True,
                "Removed navigation data changed Transform ownership.");
            Assert.That(restored.IsValid() && restored.isLoaded, Is.True,
                "The caller's original scene was not retained.");
            Assert.That(restored.isDirty, Is.EqualTo(originalDirty),
                "The caller's original scene dirty state changed.");
        }

        [UnityTest]
        [Category("InvectorAIHardening")]
        public IEnumerator RuntimeOffMeshLinkRemainsOccupiedAndFailClosedWithoutTraversal()
        {
            Scene original = SceneManager.GetActiveScene();
            string originalPath = original.path;
            bool originalDirty = original.isDirty;

            yield return new EnterPlayMode();

            NavMeshData navMeshData = BuildRuntimeOffMeshNavMesh(
                out NavMeshBuildSettings buildSettings);
            RuntimeArtifacts.TrackNavMeshData(navMeshData);
            GameObject actor = null;
            InvectorBrawlerNavigation navigation = null;
            NavMeshAgent planner = null;

            bool navMeshBuilt = navMeshData != null;
            bool linkAdded = false;
            bool opened = false;
            bool destinationAccepted = false;
            bool enteredLink = false;
            bool failedClosedOnLink = false;
            bool linkStayedUncompleted = false;
            bool transformsStayedExternal = false;
            string telemetry = "off-mesh setup unavailable";
            Vector3 linkStart = OffMeshCenter + Vector3.left * 1.5f;
            Vector3 linkEnd = OffMeshCenter + Vector3.right * 1.5f;
            Vector3 destination = OffMeshCenter + Vector3.right * 4f;

            if (navMeshBuilt)
            {
                NavMeshDataInstance navMeshInstance =
                    NavMesh.AddNavMeshData(navMeshData);
                RuntimeArtifacts.TrackNavMeshInstance(navMeshInstance);

                bool sampledEndpoints = NavMesh.SamplePosition(
                    linkStart, out NavMeshHit startHit, 2f,
                    NavMesh.AllAreas);
                sampledEndpoints &= NavMesh.SamplePosition(
                    linkEnd, out NavMeshHit endHit, 2f,
                    NavMesh.AllAreas);
                sampledEndpoints &= NavMesh.SamplePosition(
                    destination, out NavMeshHit destinationHit, 2f,
                    NavMesh.AllAreas);
                if (sampledEndpoints)
                {
                    linkStart = startHit.position;
                    linkEnd = endHit.position;
                    destination = destinationHit.position;
                    var linkData = new NavMeshLinkData
                    {
                        startPosition = linkStart,
                        endPosition = linkEnd,
                        width = 0f,
                        costModifier = -1f,
                        bidirectional = true,
                        area = 0,
                        agentTypeID = buildSettings.agentTypeID,
                    };
                    NavMeshLinkInstance linkInstance = NavMesh.AddLink(
                        linkData, Vector3.zero, Quaternion.identity);
                    RuntimeArtifacts.TrackLinkInstance(linkInstance);
                    linkAdded = NavMesh.IsLinkValid(linkInstance);

                    if (linkAdded)
                    {
                        opened = TryCreateOpenNavigator(
                            linkStart, out Vector3 plannerStart, out actor,
                            out navigation, out planner);
                        if (opened) linkStart = plannerStart;
                    }
                }
            }

            float readyDeadline = Time.realtimeSinceStartup + 2f;
            for (int frame = 0; frame < 120 && navigation != null &&
                 !navigation.IsReady && Time.realtimeSinceStartup < readyDeadline;
                 frame++)
            {
                yield return new WaitForFixedUpdate();
            }

            if (navigation != null && navigation.IsReady)
            {
                int requestsBefore = navigation.DestinationRequestCount;
                navigation.SetDestination(destination);
                destinationAccepted =
                    navigation.DestinationRequestCount == requestsBefore + 1;
            }

            // Do not call navigator queries while approaching the link: they
            // intentionally re-anchor nextPosition to the external body.
            float linkDeadline = Time.realtimeSinceStartup + 3f;
            for (int frame = 0; frame < 180 && destinationAccepted &&
                 !planner.isOnOffMeshLink &&
                 Time.realtimeSinceStartup < linkDeadline; frame++)
            {
                yield return null;
            }
            enteredLink = destinationAccepted && planner.isOnOffMeshLink &&
                          planner.currentOffMeshLinkData.valid;

            if (enteredLink)
            {
                int pathResetCountBefore = navigation.PathResetCount;
                int requestCountBefore = navigation.DestinationRequestCount;
                int offMeshBlockedBefore = navigation.OffMeshBlockedCount;
                int syncFailuresBefore = navigation.PlannerSyncFailureCount;
                Vector3 rootPositionBefore = actor.transform.position;
                Quaternion rootRotationBefore = actor.transform.rotation;
                Vector3 childLocalPositionBefore = planner.transform.localPosition;
                Quaternion childLocalRotationBefore = planner.transform.localRotation;

                bool readyWhileOnLink = navigation.IsReady;
                Vector3 desiredWhileOnLink = navigation.DesiredVelocity;
                navigation.ClearPath();
                bool synchronized = navigation.SynchronizePlannerPosition(
                    actor.transform.position, false);
                navigation.SetDestination(destination);

                bool stayedOnLinkEveryFrame = true;
                bool transformNeutralEveryFrame = true;
                for (int frame = 0; frame < 8; frame++)
                {
                    stayedOnLinkEveryFrame &= planner.isOnOffMeshLink &&
                        planner.currentOffMeshLinkData.valid;
                    transformNeutralEveryFrame &=
                        !planner.updatePosition && !planner.updateRotation &&
                        !planner.updateUpAxis &&
                        !planner.autoTraverseOffMeshLink &&
                        actor.transform.position == rootPositionBefore &&
                        actor.transform.rotation == rootRotationBefore &&
                        planner.transform.localPosition ==
                            childLocalPositionBefore &&
                        planner.transform.localRotation ==
                            childLocalRotationBefore;
                    yield return null;
                }

                failedClosedOnLink = !readyWhileOnLink &&
                    desiredWhileOnLink == Vector3.zero && !synchronized &&
                    navigation.PathResetCount == pathResetCountBefore &&
                    navigation.DestinationRequestCount == requestCountBefore &&
                    navigation.OffMeshBlockedCount >= offMeshBlockedBefore + 2 &&
                    navigation.PlannerSyncFailureCount > syncFailuresBefore;
                linkStayedUncompleted = stayedOnLinkEveryFrame &&
                    planner.isOnOffMeshLink &&
                    planner.currentOffMeshLinkData.valid;
                transformsStayedExternal = transformNeutralEveryFrame;

                telemetry = string.Format(
                    "onLink={0}, currentValid={1}, ready={2}, desired={3}, " +
                    "autoTraverse={4}, resets={5}/{6}, requests={7}/{8}, " +
                    "blocked={9}/{10}, syncFailures={11}/{12}, sync={13}",
                    planner.isOnOffMeshLink,
                    planner.currentOffMeshLinkData.valid,
                    readyWhileOnLink,
                    desiredWhileOnLink,
                    planner.autoTraverseOffMeshLink,
                    navigation.PathResetCount,
                    pathResetCountBefore,
                    navigation.DestinationRequestCount,
                    requestCountBefore,
                    navigation.OffMeshBlockedCount,
                    offMeshBlockedBefore,
                    navigation.PlannerSyncFailureCount,
                    syncFailuresBefore,
                    synchronized);
            }

            RuntimeArtifacts.Cleanup();
            yield return null;
            yield return new ExitPlayMode();

            Scene restored = SceneManager.GetSceneByPath(originalPath);
            Assert.That(navMeshBuilt, Is.True,
                "The two-island runtime NavMesh failed to build.");
            Assert.That(linkAdded, Is.True,
                "The sampled runtime NavMeshLinkData was not valid.");
            Assert.That(opened, Is.True,
                "The off-mesh fixture could not open its planner at the link start.");
            Assert.That(destinationAccepted, Is.True,
                "The off-mesh destination request was not accepted.");
            Assert.That(enteredLink, Is.True,
                "The planner did not deterministically enter the runtime off-mesh link.");
            Assert.That(failedClosedOnLink, Is.True,
                "The planner did not fail its public API closed on the link. " +
                telemetry);
            Assert.That(linkStayedUncompleted, Is.True,
                "ClearPath or synchronization reset/completed the occupied link. " +
                telemetry);
            Assert.That(transformsStayedExternal, Is.True,
                "The link path acquired Transform ownership. " + telemetry);
            Assert.That(restored.IsValid() && restored.isLoaded, Is.True,
                "The caller's original scene was not retained.");
            Assert.That(restored.isDirty, Is.EqualTo(originalDirty),
                "The caller's original scene dirty state changed.");
        }

        [UnityTearDown]
        public IEnumerator CleanupRuntimeArtifactsAndExitPlayMode()
        {
            if (Application.isPlaying)
            {
                RuntimeArtifacts.Cleanup();
                yield return null;
                yield return new ExitPlayMode();
            }
            RuntimeArtifacts.ClearReferences();
        }

        static NavMeshData BuildRuntimeNavMesh()
        {
            if (NavMesh.GetSettingsCount() <= 0) return null;

            NavMeshBuildSettings settings = NavMesh.GetSettingsByIndex(0);
            var sources = new List<NavMeshBuildSource>
            {
                new NavMeshBuildSource
                {
                    shape = NavMeshBuildSourceShape.Box,
                    transform = Matrix4x4.TRS(
                        NavMeshCenter + Vector3.down * 0.1f,
                        Quaternion.identity,
                        Vector3.one),
                    size = new Vector3(18f, 0.2f, 12f),
                    area = 0,
                },
            };
            var bounds = new Bounds(
                NavMeshCenter, new Vector3(22f, 4f, 16f));
            NavMeshData data = NavMeshBuilder.BuildNavMeshData(
                settings, sources, bounds, Vector3.zero, Quaternion.identity);
            if (data != null) data.name = RuntimeNavMeshDataName;
            return data;
        }

        static NavMeshData BuildRuntimeOffMeshNavMesh(
            out NavMeshBuildSettings settings)
        {
            settings = default;
            if (NavMesh.GetSettingsCount() <= 0) return null;

            settings = NavMesh.GetSettingsByIndex(0);
            var sources = new List<NavMeshBuildSource>
            {
                new NavMeshBuildSource
                {
                    shape = NavMeshBuildSourceShape.Box,
                    transform = Matrix4x4.TRS(
                        OffMeshCenter + Vector3.left * 4f +
                        Vector3.down * 0.1f,
                        Quaternion.identity,
                        Vector3.one),
                    size = new Vector3(6f, 0.2f, 6f),
                    area = 0,
                },
                new NavMeshBuildSource
                {
                    shape = NavMeshBuildSourceShape.Box,
                    transform = Matrix4x4.TRS(
                        OffMeshCenter + Vector3.right * 4f +
                        Vector3.down * 0.1f,
                        Quaternion.identity,
                        Vector3.one),
                    size = new Vector3(6f, 0.2f, 6f),
                    area = 0,
                },
            };
            var bounds = new Bounds(
                OffMeshCenter, new Vector3(16f, 4f, 10f));
            NavMeshData data = NavMeshBuilder.BuildNavMeshData(
                settings, sources, bounds, Vector3.zero, Quaternion.identity);
            if (data != null) data.name = RuntimeNavMeshDataName;
            return data;
        }

        static bool TryCreateOpenNavigator(
            Vector3 desiredStart, out Vector3 start, out GameObject actor,
            out InvectorBrawlerNavigation navigation,
            out NavMeshAgent planner)
        {
            start = desiredStart;
            actor = null;
            navigation = null;
            planner = null;
            if (!NavMesh.SamplePosition(
                    desiredStart, out NavMeshHit startHit, 2f,
                    NavMesh.AllAreas))
                return false;

            start = startHit.position;
            actor = new GameObject(RuntimeActorName);
            RuntimeArtifacts.TrackActor(actor);
            actor.transform.position = start;

            GameObject plannerObject = new GameObject("Planner");
            plannerObject.transform.SetParent(actor.transform, false);
            planner = plannerObject.AddComponent<NavMeshAgent>();
            planner.enabled = false;
            planner.radius = 0.4f;
            planner.height = 1.8f;
            planner.agentTypeID = NavMesh.GetSettingsByIndex(0).agentTypeID;

            navigation = actor.AddComponent<InvectorBrawlerNavigation>();
            navigation.ConfigureDormant(planner);
            actor.SetActive(false);
            actor.SetActive(true);
            navigation.enabled = true;
            navigation.Initialize(MoveSpeed, StoppingDistance);
            navigation.OpenPlanner(start);
            return true;
        }

        static float PlanarDistance(Vector3 a, Vector3 b)
        {
            a.y = 0f;
            b.y = 0f;
            return Vector3.Distance(a, b);
        }

        static class RuntimeArtifacts
        {
            static GameObject actor;
            static NavMeshData navMeshData;
            static NavMeshDataInstance navMeshInstance;
            static NavMeshLinkInstance linkInstance;

            public static void TrackActor(GameObject value) => actor = value;
            public static void TrackNavMeshData(NavMeshData value) =>
                navMeshData = value;
            public static void TrackNavMeshInstance(NavMeshDataInstance value) =>
                navMeshInstance = value;
            public static void TrackLinkInstance(NavMeshLinkInstance value) =>
                linkInstance = value;

            public static bool RemoveNavMeshInstance()
            {
                bool wasValid = navMeshInstance.valid;
                if (wasValid) navMeshInstance.Remove();
                navMeshInstance = default;
                return wasValid;
            }

            public static void Cleanup()
            {
                GameObject trackedActor = actor;
                if (trackedActor == null && Application.isPlaying)
                    trackedActor = GameObject.Find(RuntimeActorName);
                if (trackedActor != null)
                {
                    InvectorBrawlerNavigation navigation =
                        trackedActor.GetComponent<InvectorBrawlerNavigation>();
                    if (navigation != null && navigation.RuntimePlanningOpen)
                        navigation.ClosePlanner();
                    if (Application.isPlaying) Object.Destroy(trackedActor);
                    else Object.DestroyImmediate(trackedActor);
                }
                actor = null;

                if (NavMesh.IsLinkValid(linkInstance))
                    NavMesh.RemoveLink(linkInstance);
                linkInstance = default;

                RemoveNavMeshInstance();

                if (navMeshData != null)
                {
                    if (Application.isPlaying) Object.Destroy(navMeshData);
                    else Object.DestroyImmediate(navMeshData);
                }
                navMeshData = null;
            }

            public static void ClearReferences()
            {
                actor = null;
                navMeshData = null;
                navMeshInstance = default;
                linkInstance = default;
            }
        }
    }
}
