using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using Object = UnityEngine.Object;

namespace BrawlArena.EditorAutomation
{
    public class BrawlerAnimationPresentationIsolationEditModeTests
    {
        readonly List<GameObject> created = new List<GameObject>();
        int previousTargetFrameRate;
        MatchManager previousMatchManager;

        [SetUp]
        public void SetUp()
        {
            previousTargetFrameRate = Application.targetFrameRate;
            previousMatchManager = MatchManager.Instance;
        }

        [TearDown]
        public void TearDown()
        {
            for (int i = created.Count - 1; i >= 0; i--)
            {
                if (created[i] == null) continue;
                BrawlerController brawler = created[i].GetComponent<BrawlerController>();
                if (brawler != null) brawler.StopAllCoroutines();
                Object.DestroyImmediate(created[i]);
            }
            created.Clear();
            SetStaticAutoPropertyBackingField(
                typeof(MatchManager), "Instance", previousMatchManager);
            Application.targetFrameRate = previousTargetFrameRate;
        }

        [Test]
        public void ThrowingAttackAndSuperPresentationPreserveAuthoredWaitAndDamage()
        {
            MatchManager manager = CreatePlayingManager();
            BrawlerController attacker = CreateBrawler(
                "ThrowingPresentationAttacker", TeamId.Blue, true,
                out RecordingMotor attackerMotor);
            BrawlerController target = CreateBrawler(
                "PresentationIsolationTarget", TeamId.Red, false,
                out _);
            attacker.transform.position = Vector3.zero;
            attacker.transform.forward = Vector3.forward;
            target.transform.position = Vector3.forward;
            attacker.attackDamage = 20f;
            attacker.superDamageMultiplier = 1.6f;
            attacker.superKnockback = 0f;
            manager.Register(attacker);
            manager.Register(target);

            float beforeBasic = target.Health.Current;
            IEnumerator attack = (IEnumerator)InvokePrivate(
                attacker, "AttackRoutine", target, Vector3.forward);

            Assert.IsTrue(attack.MoveNext(),
                "Basic presentation failure must not consume the authored windup.");
            Assert.IsInstanceOf<WaitForSeconds>(attack.Current);
            Assert.AreEqual(beforeBasic, target.Health.Current,
                "Damage must not move ahead of the authored hit delay.");
            Assert.IsFalse(attack.MoveNext());
            Assert.AreEqual(beforeBasic - attacker.attackDamage, target.Health.Current);
            AssertPresentationFailure(attacker, 1, "PlayBasicAttack");

            target.Health.SetMax(100f, true);
            SetPrivateField(attacker, "superInProgress", true);
            float beforeSuper = target.Health.Current;
            IEnumerator super = (IEnumerator)InvokePrivate(
                attacker, "SuperRoutine", target, Vector3.forward);

            Assert.IsTrue(super.MoveNext(),
                "Super presentation failure must not consume the authored windup.");
            Assert.IsInstanceOf<WaitForSeconds>(super.Current);
            Assert.AreEqual(beforeSuper, target.Health.Current,
                "Super damage must not move ahead of its authored delay.");
            Assert.IsFalse(super.MoveNext());
            Assert.AreEqual(
                beforeSuper - attacker.attackDamage * attacker.superDamageMultiplier,
                target.Health.Current);
            Assert.IsFalse(GetPrivateField<bool>(attacker, "superInProgress"));
            Assert.IsNull(GetPrivateField<Coroutine>(attacker, "superRoutine"));
            Assert.IsNull(GetPrivateField<Coroutine>(attacker, "attackRoutine"));
            AssertPresentationFailure(attacker, 2, "PlaySuper");
            Assert.GreaterOrEqual(attackerMotor.FaceCount, 2,
                "Presentation containment must not suppress committed facing.");
        }

        [Test]
        public void ThrowingLifecyclePresentationPreservesKoRespawnProtectionAndVictoryStop()
        {
            MatchManager manager = CreatePlayingManager();
            manager.scoreToWin = 1;
            BrawlerController victim = CreateBrawler(
                "ThrowingLifecycleVictim", TeamId.Blue, true,
                out RecordingMotor motor);
            manager.Register(victim);
            SetAutoPropertyBackingField(victim.Health, "Current", 0f);

            Assert.DoesNotThrow(() => InvokePrivate(victim, "OnDied", (object)null));

            Assert.AreEqual(1, manager.RedScore,
                "KO reporting must still run after death presentation fails.");
            Assert.AreEqual(1, motor.StopCount);
            Assert.IsTrue(motor.LastStopSuspended,
                "Death must still suspend the physical motor.");
            Assert.AreEqual(MatchState.Ended, manager.State,
                "KO processing must still reach the authoritative match-end threshold.");
            Assert.IsFalse(GetPrivateField<bool>(victim, "respawning"),
                "An ended match must not start a respawn.");
            AssertPresentationFailure(victim, 1, "PlayDeath");

            victim.StopAllCoroutines();
            SetAutoPropertyBackingField(manager, "State", MatchState.Playing);
            SetPrivateField(victim, "respawning", false);
            IEnumerator respawn = (IEnumerator)InvokePrivate(victim, "RespawnRoutine");
            Assert.IsTrue(respawn.MoveNext());
            Assert.IsInstanceOf<WaitForSeconds>(respawn.Current);
            Assert.IsFalse(respawn.MoveNext());

            Assert.AreEqual(1, motor.TeleportCount,
                "Respawn teleport must remain authoritative when presentation fails.");
            Assert.IsFalse(victim.Health.IsDead,
                "Health.Revive must complete before presentation is attempted.");
            Assert.IsFalse(GetPrivateField<bool>(victim, "respawning"),
                "Respawn cleanup must complete after presentation failure.");
            if (!victim.Health.Invulnerable)
            {
                IEnumerator protection = (IEnumerator)InvokePrivate(
                    victim, "InvulnerabilityRoutine", 1.5f);
                Assert.IsTrue(protection.MoveNext(),
                    "The spawn-protection iterator must remain executable in EditMode.");
            }
            Assert.IsTrue(victim.Health.Invulnerable,
                "Spawn protection must still begin after presentation failure.");
            AssertPresentationFailure(victim, 2, "PlayRespawn");

            Assert.DoesNotThrow(victim.PlayVictory);
            Assert.AreEqual(2, motor.StopCount);
            Assert.IsFalse(motor.LastStopSuspended,
                "Victory must retain the Legacy non-suspending stop semantic.");
            AssertPresentationFailure(victim, 3, "PlayVictory");
        }

        [Test]
        public void LocomotionAndHitReactionFailuresAreContainedAndRecordedWithoutLogs()
        {
            BrawlerController brawler = CreateBrawler(
                "ThrowingMomentToMomentPresentation", TeamId.Blue, true,
                out _);

            Assert.DoesNotThrow(() => InvokePrivate(brawler, "UpdateAnimator"));
            AssertPresentationFailure(brawler, 1, "TickLocomotion");

            SetPrivateField(brawler, "attackLockUntil", -100f);
            SetPrivateField(brawler, "nextFlinchTime", -100f);
            UnityEngine.Random.State priorRandomState = UnityEngine.Random.state;
            int seed = FindFlinchSeed();
            try
            {
                UnityEngine.Random.InitState(seed);
                Assert.DoesNotThrow(() => InvokePrivate(brawler, "OnDamaged", 1f, null));
            }
            finally
            {
                UnityEngine.Random.state = priorRandomState;
            }

            AssertPresentationFailure(brawler, 2, "PlayHitReaction");
            Assert.AreEqual(typeof(NotSupportedException).FullName,
                brawler.LastAnimationPresentationFailureType);
            Assert.AreEqual("PlayHitReaction",
                brawler.LastAnimationPresentationFailureMessage);
        }

        [Test]
        public void FacadeRoutesEveryDriverCallThroughContainmentAndFinallyClearsOffense()
        {
            string projectRoot = Directory.GetParent(Application.dataPath)?.FullName;
            Assert.IsNotNull(projectRoot);
            string source = File.ReadAllText(Path.Combine(
                projectRoot, "Assets/Scripts/Brawl/BrawlerController.cs"));

            StringAssert.DoesNotContain("AnimationDriver?.", source);
            string containment = Extract(source,
                "void TryPresent(", "public bool TryWardStep(");
            string[] driverCalls =
            {
                "driver.TickLocomotion(",
                "driver.PlayBasicAttack(",
                "driver.PlaySuper(",
                "driver.PlayHitReaction(",
                "driver.PlayDeath(",
                "driver.PlayRespawn(",
                "driver.PlayVictory(",
            };
            foreach (string driverCall in driverCalls)
            {
                Assert.AreEqual(1, CountOccurrences(source, driverCall), driverCall);
                StringAssert.Contains(driverCall, containment);
            }
            StringAssert.Contains("catch (System.Exception exception)", containment);
            StringAssert.Contains("AnimationPresentationFailureCount++", containment);

            string attack = Extract(source,
                "IEnumerator AttackRoutine(", "IEnumerator SuperRoutine(");
            StringAssert.Contains("finally", attack);
            StringAssert.Contains("attackRoutine = null;", attack);
            string super = Extract(source,
                "IEnumerator SuperRoutine(", "void FireProjectile(");
            StringAssert.Contains("finally", super);
            StringAssert.Contains("superInProgress = false;", super);
            StringAssert.Contains("superRoutine = null;", super);

            string death = Extract(source, "void OnDied(", "IEnumerator RespawnRoutine(");
            AssertOrdered(death,
                "TryPresent(AnimationPresentationOperation.PlayDeath)",
                "Motor?.Stop(true)",
                "MatchManager.Instance.ReportKO(this, attacker)",
                "StartCoroutine(RespawnRoutine())");
            string respawn = Extract(source,
                "IEnumerator RespawnRoutine(", "void CancelSpawnProtectionOnOffense(");
            AssertOrdered(respawn,
                "Teleport(spawn)",
                "Health.Revive()",
                "TryPresent(AnimationPresentationOperation.PlayRespawn)",
                "respawning = false",
                "BeginSpawnProtection(protectionDuration)");
        }

        MatchManager CreatePlayingManager()
        {
            var go = new GameObject("PresentationIsolationMatchManager");
            created.Add(go);
            MatchManager manager = go.AddComponent<MatchManager>();
            manager.ConfigureMode(GameMode.Knockout);
            SetStaticAutoPropertyBackingField(typeof(MatchManager), "Instance", manager);
            SetAutoPropertyBackingField(manager, "State", MatchState.Playing);
            return manager;
        }

        BrawlerController CreateBrawler(string name, TeamId team, bool throwingDriver,
            out RecordingMotor motor)
        {
            var go = new GameObject(name);
            created.Add(go);
            Health health = go.AddComponent<Health>();
            health.SetMax(100f, true);
            motor = go.AddComponent<RecordingMotor>();
            ThrowingAnimationDriver driver = throwingDriver
                ? go.AddComponent<ThrowingAnimationDriver>()
                : null;
            BrawlerController brawler = go.AddComponent<BrawlerController>();
            if (brawler.Health == null) InvokePrivate(brawler, "Awake");
            brawler.team = team;
            if (driver != null)
            {
                brawler.SetAnimationDriver(driver);
                InvokePrivate(brawler, "InitializeAnimationDriver");
            }
            return brawler;
        }

        static int FindFlinchSeed()
        {
            for (int seed = 0; seed < 1000; seed++)
            {
                UnityEngine.Random.InitState(seed);
                if (UnityEngine.Random.value < 0.6f) return seed;
            }
            Assert.Fail("Could not find a deterministic hit-reaction seed.");
            return 0;
        }

        static void AssertPresentationFailure(BrawlerController brawler, int count,
            string operation)
        {
            Assert.AreEqual(count, brawler.AnimationPresentationFailureCount);
            Assert.AreEqual(operation, brawler.LastAnimationPresentationFailureOperation);
            Assert.AreEqual(typeof(NotSupportedException).FullName,
                brawler.LastAnimationPresentationFailureType);
        }

        static string Extract(string source, string startMarker, string endMarker)
        {
            int start = source.IndexOf(startMarker, StringComparison.Ordinal);
            Assert.GreaterOrEqual(start, 0, "Missing source marker: " + startMarker);
            int end = source.IndexOf(endMarker, start + startMarker.Length,
                StringComparison.Ordinal);
            Assert.Greater(end, start, "Missing source marker: " + endMarker);
            return source.Substring(start, end - start);
        }

        static int CountOccurrences(string source, string value)
        {
            int count = 0;
            int index = 0;
            while ((index = source.IndexOf(value, index, StringComparison.Ordinal)) >= 0)
            {
                count++;
                index += value.Length;
            }
            return count;
        }

        static void AssertOrdered(string source, params string[] markers)
        {
            int prior = -1;
            foreach (string marker in markers)
            {
                int index = source.IndexOf(marker, prior + 1, StringComparison.Ordinal);
                Assert.Greater(index, prior, "Expected ordered source marker: " + marker);
                prior = index;
            }
        }

        static object InvokePrivate(object target, string methodName, params object[] arguments)
        {
            MethodInfo method = target.GetType().GetMethod(
                methodName, BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.IsNotNull(method, "Missing private method: " + methodName);
            return method.Invoke(target, arguments);
        }

        static T GetPrivateField<T>(object target, string fieldName)
        {
            FieldInfo field = target.GetType().GetField(
                fieldName, BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.IsNotNull(field, "Missing private field: " + fieldName);
            return (T)field.GetValue(target);
        }

        static void SetPrivateField(object target, string fieldName, object value)
        {
            FieldInfo field = target.GetType().GetField(
                fieldName, BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.IsNotNull(field, "Missing private field: " + fieldName);
            field.SetValue(target, value);
        }

        static void SetAutoPropertyBackingField(object target, string propertyName, object value)
        {
            FieldInfo field = target.GetType().GetField(
                "<" + propertyName + ">k__BackingField",
                BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.IsNotNull(field, "Missing auto-property backing field: " + propertyName);
            field.SetValue(target, value);
        }

        static void SetStaticAutoPropertyBackingField(Type type, string propertyName, object value)
        {
            FieldInfo field = type.GetField(
                "<" + propertyName + ">k__BackingField",
                BindingFlags.Static | BindingFlags.NonPublic);
            Assert.IsNotNull(field, "Missing static auto-property backing field: " + propertyName);
            field.SetValue(null, value);
        }

        sealed class ThrowingAnimationDriver : MonoBehaviour, IBrawlerAnimationDriver
        {
            public void TickLocomotion(float normalizedSpeed) =>
                throw new NotSupportedException(nameof(TickLocomotion));
            public void PlayBasicAttack() =>
                throw new NotSupportedException(nameof(PlayBasicAttack));
            public void PlaySuper() => throw new NotSupportedException(nameof(PlaySuper));
            public void PlayHitReaction() =>
                throw new NotSupportedException(nameof(PlayHitReaction));
            public void PlayDeath() => throw new NotSupportedException(nameof(PlayDeath));
            public void PlayRespawn() => throw new NotSupportedException(nameof(PlayRespawn));
            public void PlayVictory() => throw new NotSupportedException(nameof(PlayVictory));
        }

        sealed class RecordingMotor : MonoBehaviour, IBrawlerMotor
        {
            public Vector3 Velocity { get; private set; }
            public float CollisionRadius => 0.5f;
            public bool IsGrounded => true;
            public int FaceCount { get; private set; }
            public int StopCount { get; private set; }
            public bool LastStopSuspended { get; private set; }
            public int TeleportCount { get; private set; }

            public void Initialize(float moveSpeed) { }
            public void SetPlanarIntent(Vector3 worldDirection, float speed,
                bool movementAllowed) { }
            public void Face(Vector3 worldDirection, bool immediate) => FaceCount++;
            public float ConstrainExternalDisplacement(Vector3 direction, float distance) =>
                distance;
            public Vector3 ConstrainTeleportDestination(Vector3 position,
                float sampleRadius) => position;
            public void BeginExternalDisplacement() { }
            public void Displace(Vector3 displacement, bool keepGrounded)
            {
                transform.position += displacement;
                Velocity = displacement;
            }
            public void EndExternalDisplacement() { }
            public void Stop(bool suspend)
            {
                StopCount++;
                LastStopSuspended = suspend;
                Velocity = Vector3.zero;
            }
            public void Teleport(Vector3 position)
            {
                TeleportCount++;
                transform.position = position;
                Velocity = Vector3.zero;
            }
        }
    }
}
