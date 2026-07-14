using System;
using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace BrawlArena.EditorAutomation
{
    public class MatchVictoryPresentationIsolationEditModeTests
    {
        readonly List<GameObject> created = new List<GameObject>();
        MatchManager manager;
        int previousTargetFrameRate;

        [SetUp]
        public void SetUp()
        {
            previousTargetFrameRate = Application.targetFrameRate;
            GameObject managerObject = new GameObject("VictoryIsolationMatchManager");
            created.Add(managerObject);
            manager = managerObject.AddComponent<MatchManager>();
            manager.autoStart = false;
            if (MatchManager.Instance != manager) InvokePrivate(manager, "Awake");
        }

        [TearDown]
        public void TearDown()
        {
            for (int i = created.Count - 1; i >= 0; i--)
            {
                if (created[i] != null) Object.DestroyImmediate(created[i]);
            }
            created.Clear();
            Application.targetFrameRate = previousTargetFrameRate;
        }

        [Test]
        public void ThrowingWinnerDoesNotBlockLaterWinnerOrAuthoritativeMatchEnd()
        {
            var trace = new List<string>();
            BrawlerController throwingWinner = CreateRecordingBrawler(
                "ThrowingWinner", TeamId.Blue, "first", trace, true,
                out RecordingAnimationDriver throwingDriver);
            CreateRecordingBrawler(
                "LosingBrawler", TeamId.Red, "loser", trace, false,
                out RecordingAnimationDriver losingDriver);
            CreateRecordingBrawler(
                "RemainingWinner", TeamId.Blue, "second", trace, false,
                out RecordingAnimationDriver remainingDriver);

            int matchEndedCalls = 0;
            TeamId? observedWinner = null;
            manager.MatchEnded += winner =>
            {
                matchEndedCalls++;
                observedWinner = winner;
                trace.Add("ended");
            };

            Assert.DoesNotThrow(() => manager.DeclareWinner(TeamId.Blue));

            Assert.AreEqual(MatchState.Ended, manager.State);
            Assert.AreEqual(1, matchEndedCalls);
            Assert.AreEqual(TeamId.Blue, observedWinner);
            Assert.AreEqual(1, throwingDriver.VictoryCalls);
            Assert.AreEqual(0, losingDriver.VictoryCalls);
            Assert.AreEqual(1, remainingDriver.VictoryCalls);
            Assert.AreEqual(1, manager.VictoryPresentationFaultCount);
            Assert.AreSame(throwingWinner, manager.LastVictoryPresentationFaultActor);
            Assert.IsInstanceOf<InvalidOperationException>(
                manager.LastVictoryPresentationFault);
            CollectionAssert.AreEqual(
                new[] { "victory:first", "victory:second", "ended" }, trace);
            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void NonThrowingDriversPreserveWinnerSelectionRegistrationAndEventOrder()
        {
            var trace = new List<string>();
            CreateRecordingBrawler(
                "FirstWinner", TeamId.Blue, "first", trace, false,
                out RecordingAnimationDriver firstWinner);
            CreateRecordingBrawler(
                "LosingBrawler", TeamId.Red, "loser", trace, false,
                out RecordingAnimationDriver loser);
            CreateRecordingBrawler(
                "SecondWinner", TeamId.Blue, "second", trace, false,
                out RecordingAnimationDriver secondWinner);
            manager.MatchEnded += _ => trace.Add("ended");

            manager.DeclareWinner(TeamId.Blue);

            Assert.AreEqual(1, firstWinner.VictoryCalls);
            Assert.AreEqual(0, loser.VictoryCalls);
            Assert.AreEqual(1, secondWinner.VictoryCalls);
            Assert.AreEqual(0, manager.VictoryPresentationFaultCount);
            Assert.IsNull(manager.LastVictoryPresentationFaultActor);
            Assert.IsNull(manager.LastVictoryPresentationFault);
            CollectionAssert.AreEqual(
                new[] { "victory:first", "victory:second", "ended" }, trace);
            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void ConfiguredSemanticDriverEndsWithoutPresentationFault()
        {
            GameObject actor = CreateActor("SemanticWinner");
            Tests.InvectorCutoverTestAnimationDriver driver =
                actor.AddComponent<Tests.InvectorCutoverTestAnimationDriver>();
            BrawlerController brawler = CompleteBrawler(
                actor, TeamId.Blue, driver);
            manager.Register(brawler);
            int matchEndedCalls = 0;
            manager.MatchEnded += _ => matchEndedCalls++;

            Assert.DoesNotThrow(() => manager.DeclareWinner(TeamId.Blue));

            Assert.AreEqual(MatchState.Ended, manager.State);
            Assert.AreEqual(1, matchEndedCalls);
            Assert.AreEqual(1, driver.VictoryCalls);
            Assert.AreEqual(0, manager.VictoryPresentationFaultCount);
            Assert.IsNull(manager.LastVictoryPresentationFaultActor);
            Assert.IsNull(manager.LastVictoryPresentationFault);
            LogAssert.NoUnexpectedReceived();
        }

        BrawlerController CreateRecordingBrawler(
            string name,
            TeamId team,
            string traceId,
            List<string> trace,
            bool throwOnVictory,
            out RecordingAnimationDriver driver)
        {
            GameObject actor = CreateActor(name);
            driver = actor.AddComponent<RecordingAnimationDriver>();
            driver.Configure(traceId, trace, throwOnVictory);
            BrawlerController brawler = CompleteBrawler(actor, team, driver);
            manager.Register(brawler);
            return brawler;
        }

        GameObject CreateActor(string name)
        {
            GameObject actor = new GameObject(name);
            created.Add(actor);
            actor.AddComponent<Health>().SetMax(100f);
            actor.AddComponent<Tests.InvectorCutoverTestMotor>();
            return actor;
        }

        static BrawlerController CompleteBrawler(
            GameObject actor,
            TeamId team,
            IBrawlerAnimationDriver driver)
        {
            BrawlerController brawler = actor.AddComponent<BrawlerController>();
            if (brawler.Health == null) InvokePrivate(brawler, "Awake");
            brawler.team = team;
            brawler.SetAnimationDriver(driver);
            InvokePrivate(brawler, "InitializeAnimationDriver");
            return brawler;
        }

        static void InvokePrivate(object target, string methodName)
        {
            MethodInfo method = target.GetType().GetMethod(
                methodName, BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.IsNotNull(method, "Missing private method: " + methodName);
            method.Invoke(target, null);
        }

        sealed class RecordingAnimationDriver : MonoBehaviour, IBrawlerAnimationDriver
        {
            string traceId;
            List<string> trace;
            bool throwOnVictory;

            public int VictoryCalls { get; private set; }

            public void Configure(
                string configuredTraceId,
                List<string> configuredTrace,
                bool configuredThrowOnVictory)
            {
                traceId = configuredTraceId;
                trace = configuredTrace;
                throwOnVictory = configuredThrowOnVictory;
            }

            public void TickLocomotion(float normalizedSpeed) { }
            public void PlayBasicAttack() { }
            public void PlaySuper() { }
            public void PlayHitReaction() { }
            public void PlayDeath() { }
            public void PlayRespawn() { }

            public void PlayVictory()
            {
                VictoryCalls++;
                trace.Add("victory:" + traceId);
                if (throwOnVictory)
                    throw new InvalidOperationException(
                        "Synthetic victory presentation failure: " + traceId);
            }
        }
    }
}
