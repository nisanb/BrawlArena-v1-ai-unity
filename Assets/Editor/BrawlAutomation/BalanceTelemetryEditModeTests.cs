using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using NUnit.Framework;
using UnityEngine;

namespace BrawlArena.EditorAutomation
{
    public class BalanceTelemetryEditModeTests
    {
        sealed class RecordingSink : IBalanceTelemetrySink
        {
            public readonly List<BalanceMatchReport> Reports =
                new List<BalanceMatchReport>();

            public bool TryWrite(BalanceMatchReport report)
            {
                Reports.Add(report);
                return true;
            }
        }

        sealed class ThrowingSink : IBalanceTelemetrySink
        {
            public int Calls;

            public bool TryWrite(BalanceMatchReport report)
            {
                Calls++;
                throw new IOException("Injected provider failure");
            }
        }

        [Test]
        public void AggregatesOneReportPerMatchWithCorrectAttribution()
        {
            var sink = new RecordingSink();
            var accumulator = new BalanceTelemetryAccumulator(sink, () => "match_0123456789");

            Assert.IsTrue(accumulator.BeginMatch(GameMode.Knockout, MobileQualityTier.Medium, 10d));
            accumulator.RegisterBrawler("blue", "Aria", TeamId.Blue, true);
            accumulator.RegisterBrawler("red", "Nova", TeamId.Red, false);

            accumulator.RecordAttack("blue");
            accumulator.RecordAttack("blue");
            accumulator.RecordSuper("blue");
            accumulator.RecordDamage("red", "blue", 27.5f);
            accumulator.RecordDamage("blue", null, 6f);
            accumulator.RecordHealing("blue", 4f);
            accumulator.RecordKnockout("red", "blue");
            accumulator.RecordKnockout("blue", null);

            Assert.AreEqual(0, sink.Reports.Count, "Combat must only mutate memory.");
            BalanceMatchReport report = accumulator.EndMatch(TeamId.Blue, 8, 3, 42.5d);

            Assert.AreEqual(1, sink.Reports.Count);
            Assert.AreSame(report, sink.Reports[0]);
            Assert.AreEqual("match_0123456789", report.matchId);
            Assert.AreEqual("Knockout", report.mode);
            Assert.AreEqual("Blue", report.winner);
            Assert.AreEqual(32.5f, report.durationSeconds, 0.001f);
            Assert.AreEqual(8, report.blueScore);
            Assert.AreEqual(3, report.redScore);
            Assert.AreEqual("Medium", report.effectiveQualityTier);

            BalanceBrawlerReport blue = report.brawlers.Single(b => b.heroName == "Aria");
            Assert.AreEqual("Blue", blue.team);
            Assert.IsTrue(blue.isLocal);
            Assert.AreEqual(27.5f, blue.damageDealt, 0.001f);
            Assert.AreEqual(6f, blue.damageTaken, 0.001f);
            Assert.AreEqual(4f, blue.healing, 0.001f);
            Assert.AreEqual(2, blue.attacks);
            Assert.AreEqual(1, blue.supers);
            Assert.AreEqual(1, blue.knockouts);
            Assert.AreEqual(1, blue.deaths);

            BalanceBrawlerReport red = report.brawlers.Single(b => b.heroName == "Nova");
            Assert.AreEqual(0f, red.damageDealt, 0.001f);
            Assert.AreEqual(27.5f, red.damageTaken, 0.001f);
            Assert.AreEqual(0, red.knockouts);
            Assert.AreEqual(1, red.deaths);
        }

        [Test]
        public void LateRegistrationMergesMetadataAndPreservesEarlierStats()
        {
            var accumulator = new BalanceTelemetryAccumulator(null, () => "late_match");
            accumulator.BeginMatch(GameMode.GemGrab, MobileQualityTier.Low, 1d);

            accumulator.RecordDamage("victim", "attacker", 12f);
            accumulator.RecordAttack("attacker");
            accumulator.RegisterBrawler("attacker", "Thorn", TeamId.Red, false);
            accumulator.RegisterBrawler("victim", "Bastion", TeamId.Blue, true);

            BalanceMatchReport report = accumulator.EndMatch(null, 5, 5, 6d);
            BalanceBrawlerReport thorn = report.brawlers.Single(b => b.heroName == "Thorn");
            BalanceBrawlerReport bastion = report.brawlers.Single(b => b.heroName == "Bastion");
            Assert.AreEqual("Draw", report.winner);
            Assert.AreEqual("GemGrab", report.mode);
            Assert.AreEqual("Low", report.effectiveQualityTier);
            Assert.AreEqual(12f, thorn.damageDealt, 0.001f);
            Assert.AreEqual(1, thorn.attacks);
            Assert.AreEqual(12f, bastion.damageTaken, 0.001f);
            Assert.IsTrue(bastion.isLocal);
        }

        [Test]
        public void DuplicateBeginAndEndAreIdempotent()
        {
            var sink = new RecordingSink();
            var accumulator = new BalanceTelemetryAccumulator(sink, () => "one_match");
            Assert.IsTrue(accumulator.BeginMatch(GameMode.Knockout, MobileQualityTier.High, 2d));
            Assert.IsFalse(accumulator.BeginMatch(GameMode.GemGrab, MobileQualityTier.Low, 50d));
            accumulator.RegisterBrawler("p", "Vex", TeamId.Blue, true);
            accumulator.RecordAttack("p");

            BalanceMatchReport first = accumulator.EndMatch(null, 0, 0, 12d);
            accumulator.RecordAttack("p");
            BalanceMatchReport second = accumulator.EndMatch(TeamId.Red, 0, 9, 99d);

            Assert.AreSame(first, second);
            Assert.AreEqual(1, sink.Reports.Count);
            Assert.AreEqual("Knockout", second.mode);
            Assert.AreEqual("Draw", second.winner);
            Assert.AreEqual(10f, second.durationSeconds, 0.001f);
            Assert.AreEqual(1, second.brawlers[0].attacks);
        }

        [Test]
        public void SerializationContainsNoPlayerOrDeviceIdentifiers()
        {
            var accumulator = new BalanceTelemetryAccumulator(null,
                () => "9f55db0b408c46bfac5942e58430ba89");
            accumulator.BeginMatch(GameMode.Knockout, MobileQualityTier.High, 0d);
            accumulator.RegisterBrawler("internal-object-key", "Grimm", TeamId.Red, false);
            BalanceMatchReport report = accumulator.EndMatch(TeamId.Red, 1, 8, 20d);

            string json = BalanceTelemetryJson.Serialize(report);
            string lower = json.ToLowerInvariant();
            StringAssert.Contains("9f55db0b408c46bfac5942e58430ba89", json);
            StringAssert.Contains("\"heroName\":\"Grimm\"", json);
            StringAssert.DoesNotContain("internal-object-key", json);
            StringAssert.DoesNotContain("gamertag", lower);
            StringAssert.DoesNotContain("playername", lower);
            StringAssert.DoesNotContain("device", lower);
            StringAssert.DoesNotContain("identifier", lower);
            StringAssert.DoesNotContain("model", lower);
            StringAssert.DoesNotContain("vendor", lower);
            StringAssert.DoesNotContain("processor", lower);
        }

        [Test]
        public void ThrowingSinkCannotBreakCompletionOrSubmitTwice()
        {
            var sink = new ThrowingSink();
            var accumulator = new BalanceTelemetryAccumulator(sink, () => "safe_match");
            accumulator.BeginMatch(GameMode.Knockout, MobileQualityTier.Medium, 0d);
            accumulator.RegisterBrawler("p", "Aria", TeamId.Blue, true);

            BalanceMatchReport report = null;
            Assert.DoesNotThrow(() => report = accumulator.EndMatch(TeamId.Blue, 1, 0, 3d));
            Assert.NotNull(report);
            Assert.DoesNotThrow(() => accumulator.EndMatch(TeamId.Red, 0, 1, 4d));
            Assert.AreEqual(1, sink.Calls);
        }

        [Test]
        public void NonFiniteInputsCannotPoisonSerializedMetrics()
        {
            var accumulator = new BalanceTelemetryAccumulator(null, () => "finite_match");
            accumulator.BeginMatch(GameMode.Knockout, MobileQualityTier.Medium, double.NaN);
            accumulator.RegisterBrawler("p", "Aria", TeamId.Blue, true);
            accumulator.RecordDamage("p", null, float.NaN);
            accumulator.RecordDamage("p", null, float.PositiveInfinity);
            accumulator.RecordHealing("p", float.NegativeInfinity);

            BalanceMatchReport report = accumulator.EndMatch(null, 0, 0,
                double.PositiveInfinity);
            Assert.AreEqual(0f, report.durationSeconds);
            Assert.AreEqual(0f, report.brawlers[0].damageTaken);
            Assert.AreEqual(0f, report.brawlers[0].healing);
            string json = BalanceTelemetryJson.Serialize(report);
            StringAssert.DoesNotContain("NaN", json);
            StringAssert.DoesNotContain("Infinity", json);
        }

        [Test]
        public void JsonLinesSinkRotatesWithinConfiguredFileBound()
        {
            string directory = Path.Combine(Path.GetTempPath(),
                "BrawlArenaBalanceTelemetry_" + Guid.NewGuid().ToString("N"));
            string path = Path.Combine(directory, "balance.jsonl");
            Directory.CreateDirectory(directory);

            try
            {
                var sink = new JsonLinesBalanceTelemetrySink(path, 420, 3);
                for (int i = 0; i < 10; i++)
                {
                    BalanceMatchReport report = CreateReportForRotation(i);
                    Assert.IsTrue(sink.TryWrite(report));
                }

                string[] files = Directory.GetFiles(directory, "balance.jsonl*");
                Assert.LessOrEqual(files.Length, 3);
                Assert.GreaterOrEqual(files.Length, 2, "Small bound should force rotation.");
                Assert.IsTrue(File.Exists(path), "The active JSONL file must remain present.");
                foreach (string file in files)
                {
                    Assert.LessOrEqual(new FileInfo(file).Length, 420,
                        "Test reports fit below the cap, so every rotated file must too.");
                    string[] lines = File.ReadAllLines(file);
                    Assert.Greater(lines.Length, 0);
                    foreach (string line in lines)
                        Assert.NotNull(JsonUtility.FromJson<BalanceMatchReport>(line));
                }
            }
            finally
            {
                if (Directory.Exists(directory)) Directory.Delete(directory, true);
            }
        }

        [Test]
        public void HealthWithoutMatchManagerSafelySkipsRuntimeTelemetry()
        {
            var gameObject = new GameObject("StandaloneHealthTelemetryTest");
            try
            {
                Health health = gameObject.AddComponent<Health>();
                health.SetMax(100f);
                Assert.DoesNotThrow(() => health.TakeDamage(10f, null));
                Assert.DoesNotThrow(() => health.Heal(5f));
                Assert.AreEqual(95f, health.Current, 0.001f);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(gameObject);
            }
        }

        static BalanceMatchReport CreateReportForRotation(int index)
        {
            var report = new BalanceMatchReport
            {
                matchId = index.ToString("D32"),
                mode = "Knockout",
                winner = index % 2 == 0 ? "Blue" : "Red",
                durationSeconds = 100f + index,
                blueScore = 8,
                redScore = 7,
                effectiveQualityTier = "Medium",
            };
            report.brawlers.Add(new BalanceBrawlerReport
            {
                heroName = "Bastion",
                team = "Blue",
                isLocal = true,
                damageDealt = 123.5f,
                damageTaken = 100f,
                healing = 40f,
                attacks = 30,
                supers = 3,
                knockouts = 5,
                deaths = 2,
            });
            return report;
        }
    }
}
