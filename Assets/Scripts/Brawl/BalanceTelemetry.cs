using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;

namespace BrawlArena
{
    /// <summary>
    /// Provider-neutral destination for a completed, privacy-safe match report.
    /// Implementations must receive at most one call per accumulator/match.
    /// </summary>
    public interface IBalanceTelemetrySink
    {
        bool TryWrite(BalanceMatchReport report);
    }

    [Serializable]
    public sealed class BalanceBrawlerReport
    {
        public string heroName;
        public string team;
        public bool isLocal;
        public float damageDealt;
        public float damageTaken;
        public float healing;
        public int attacks;
        public int supers;
        public int knockouts;
        public int deaths;
    }

    /// <summary>
    /// Deliberately small privacy contract. The random matchId is the only
    /// identifier: there are no player tags or stable hardware identifiers.
    /// </summary>
    [Serializable]
    public sealed class BalanceMatchReport
    {
        public int schemaVersion = 1;
        public string matchId;
        public string mode;
        public string winner;
        public float durationSeconds;
        public int blueScore;
        public int redScore;
        public string effectiveQualityTier;
        public List<BalanceBrawlerReport> brawlers = new List<BalanceBrawlerReport>();
    }

    public static class BalanceTelemetryJson
    {
        public static string Serialize(BalanceMatchReport report)
        {
            return report == null ? string.Empty : JsonUtility.ToJson(report, false);
        }
    }

    /// <summary>
    /// Pure, one-match accumulator. It has no scene or GameObject dependency,
    /// making both the clock values and output sink directly injectable in tests.
    /// Combat methods only mutate memory; the sink is touched once by EndMatch.
    /// </summary>
    public sealed class BalanceTelemetryAccumulator
    {
        sealed class Participant
        {
            public string HeroName = "Unknown";
            public TeamId Team;
            public bool IsLocal;
            public float DamageDealt;
            public float DamageTaken;
            public float Healing;
            public int Attacks;
            public int Supers;
            public int Knockouts;
            public int Deaths;
        }

        readonly IBalanceTelemetrySink sink;
        readonly Func<string> matchIdFactory;
        readonly Dictionary<string, Participant> participants =
            new Dictionary<string, Participant>(StringComparer.Ordinal);
        readonly List<string> participantOrder = new List<string>();

        bool began;
        bool ended;
        double startedAtSeconds;
        GameMode mode;
        MobileQualityTier qualityTier;
        string matchId;
        BalanceMatchReport completedReport;

        public BalanceTelemetryAccumulator(IBalanceTelemetrySink sink = null,
            Func<string> matchIdFactory = null)
        {
            this.sink = sink;
            this.matchIdFactory = matchIdFactory ?? NewRandomMatchId;
        }

        public bool HasBegun => began;
        public bool HasEnded => ended;
        public BalanceMatchReport CompletedReport => completedReport;

        public bool BeginMatch(GameMode matchMode, MobileQualityTier effectiveQualityTier,
            double nowSeconds)
        {
            if (began) return false;

            began = true;
            mode = matchMode;
            qualityTier = effectiveQualityTier;
            startedAtSeconds = IsFinite(nowSeconds) ? nowSeconds : 0d;
            try
            {
                matchId = matchIdFactory();
            }
            catch
            {
                matchId = null;
            }
            if (string.IsNullOrEmpty(matchId)) matchId = NewRandomMatchId();
            return true;
        }

        /// <summary>
        /// Registration may occur at any point after begin. If combat created a
        /// placeholder first, metadata is merged without losing accumulated stats.
        /// </summary>
        public bool RegisterBrawler(string participantKey, string heroName, TeamId team,
            bool isLocal)
        {
            if (!CanRecord || string.IsNullOrEmpty(participantKey)) return false;
            Participant participant = GetOrCreate(participantKey);
            participant.HeroName = SanitizeHeroName(heroName);
            participant.Team = team;
            participant.IsLocal = isLocal;
            return true;
        }

        public void RecordDamage(string victimKey, string attackerKey, float appliedAmount)
        {
            if (!CanRecord || !IsFinite(appliedAmount) || appliedAmount <= 0f ||
                string.IsNullOrEmpty(victimKey))
                return;
            Participant victim = GetOrCreate(victimKey);
            victim.DamageTaken = AddFinite(victim.DamageTaken, appliedAmount);
            if (!string.IsNullOrEmpty(attackerKey) &&
                !string.Equals(attackerKey, victimKey, StringComparison.Ordinal))
            {
                Participant attacker = GetOrCreate(attackerKey);
                attacker.DamageDealt = AddFinite(attacker.DamageDealt, appliedAmount);
            }
        }

        public void RecordHealing(string participantKey, float restoredAmount)
        {
            if (!CanRecord || !IsFinite(restoredAmount) || restoredAmount <= 0f ||
                string.IsNullOrEmpty(participantKey))
                return;
            Participant participant = GetOrCreate(participantKey);
            participant.Healing = AddFinite(participant.Healing, restoredAmount);
        }

        public void RecordAttack(string participantKey)
        {
            if (!CanRecord || string.IsNullOrEmpty(participantKey)) return;
            GetOrCreate(participantKey).Attacks++;
        }

        public void RecordSuper(string participantKey)
        {
            if (!CanRecord || string.IsNullOrEmpty(participantKey)) return;
            GetOrCreate(participantKey).Supers++;
        }

        /// <summary>Environment knockouts use a null attacker and only add a death.</summary>
        public void RecordKnockout(string victimKey, string attackerKey)
        {
            if (!CanRecord || string.IsNullOrEmpty(victimKey)) return;
            GetOrCreate(victimKey).Deaths++;
            if (!string.IsNullOrEmpty(attackerKey) &&
                !string.Equals(attackerKey, victimKey, StringComparison.Ordinal))
                GetOrCreate(attackerKey).Knockouts++;
        }

        /// <summary>
        /// Finalizes and submits once. Repeated calls return the same finalized
        /// report and never call the sink again, even when that sink failed.
        /// </summary>
        public BalanceMatchReport EndMatch(TeamId? winner, int blueScore, int redScore,
            double nowSeconds)
        {
            if (!began) return null;
            if (ended) return completedReport;

            ended = true;
            completedReport = BuildReport(winner, blueScore, redScore, nowSeconds);
            try
            {
                sink?.TryWrite(completedReport);
            }
            catch
            {
                // Telemetry can never affect match completion. Provider adapters
                // are isolated even if they violate the TryWrite contract.
            }
            return completedReport;
        }

        bool CanRecord => began && !ended;

        Participant GetOrCreate(string participantKey)
        {
            if (participants.TryGetValue(participantKey, out Participant participant))
                return participant;

            participant = new Participant();
            participants.Add(participantKey, participant);
            participantOrder.Add(participantKey);
            return participant;
        }

        BalanceMatchReport BuildReport(TeamId? winner, int blueScore, int redScore,
            double nowSeconds)
        {
            double safeNow = IsFinite(nowSeconds) ? nowSeconds : startedAtSeconds;
            double duration = Math.Max(0d, safeNow - startedAtSeconds);
            var report = new BalanceMatchReport
            {
                matchId = matchId,
                mode = ModeName(mode),
                winner = winner.HasValue ? TeamName(winner.Value) : "Draw",
                durationSeconds = (float)Math.Min(float.MaxValue, duration),
                blueScore = Math.Max(0, blueScore),
                redScore = Math.Max(0, redScore),
                effectiveQualityTier = QualityName(qualityTier),
            };

            for (int i = 0; i < participantOrder.Count; i++)
            {
                Participant participant = participants[participantOrder[i]];
                report.brawlers.Add(new BalanceBrawlerReport
                {
                    heroName = participant.HeroName,
                    team = TeamName(participant.Team),
                    isLocal = participant.IsLocal,
                    damageDealt = participant.DamageDealt,
                    damageTaken = participant.DamageTaken,
                    healing = participant.Healing,
                    attacks = participant.Attacks,
                    supers = participant.Supers,
                    knockouts = participant.Knockouts,
                    deaths = participant.Deaths,
                });
            }

            return report;
        }

        static string NewRandomMatchId()
        {
            return Guid.NewGuid().ToString("N");
        }

        static float AddFinite(float current, float amount)
        {
            double sum = (double)current + amount;
            return sum >= float.MaxValue ? float.MaxValue : (float)sum;
        }

        static bool IsFinite(float value)
        {
            return !float.IsNaN(value) && !float.IsInfinity(value);
        }

        static bool IsFinite(double value)
        {
            return !double.IsNaN(value) && !double.IsInfinity(value);
        }

        static string SanitizeHeroName(string value)
        {
            if (string.IsNullOrWhiteSpace(value)) return "Unknown";
            value = value.Trim();
            var result = new StringBuilder(Math.Min(value.Length, 64));
            for (int i = 0; i < value.Length && result.Length < 64; i++)
            {
                char character = value[i];
                result.Append(char.IsControl(character) ? ' ' : character);
            }
            return result.ToString();
        }

        static string ModeName(GameMode value)
        {
            switch (value)
            {
                case GameMode.Knockout: return "Knockout";
                case GameMode.GemGrab: return "GemGrab";
                case GameMode.ControlZone: return "ControlZone";
                default: return "Unknown";
            }
        }

        static string QualityName(MobileQualityTier value)
        {
            switch (value)
            {
                case MobileQualityTier.Low: return "Low";
                case MobileQualityTier.Medium: return "Medium";
                case MobileQualityTier.High: return "High";
                default: return "Unknown";
            }
        }

        static string TeamName(TeamId value)
        {
            return value == TeamId.Red ? "Red" : "Blue";
        }
    }

    /// <summary>
    /// Local-only JSONL output with bounded file rotation. It performs no network
    /// operations and catches all path/permission/serialization failures. Reports
    /// remain atomic: one report larger than the byte cap is retained as one line,
    /// then rotated before the next report instead of being truncated.
    /// </summary>
    public sealed class JsonLinesBalanceTelemetrySink : IBalanceTelemetrySink
    {
        readonly object gate = new object();
        readonly string filePath;
        readonly long maxFileBytes;
        readonly int maxFileCount;

        public JsonLinesBalanceTelemetrySink(string filePath, long maxFileBytes = 512 * 1024,
            int maxFileCount = 4)
        {
            this.filePath = filePath;
            this.maxFileBytes = Math.Max(128, maxFileBytes);
            this.maxFileCount = Math.Max(1, maxFileCount);
        }

        public bool TryWrite(BalanceMatchReport report)
        {
            if (report == null || string.IsNullOrEmpty(filePath)) return false;
            try
            {
                string line = BalanceTelemetryJson.Serialize(report);
                if (string.IsNullOrEmpty(line)) return false;
                byte[] bytes = Encoding.UTF8.GetBytes(line + Environment.NewLine);

                lock (gate)
                {
                    string directory = Path.GetDirectoryName(filePath);
                    if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);
                    if (File.Exists(filePath) && new FileInfo(filePath).Length > 0 &&
                        new FileInfo(filePath).Length + bytes.Length > maxFileBytes)
                        RotateFiles();

                    using (var stream = new FileStream(filePath, FileMode.Append, FileAccess.Write,
                               FileShare.Read))
                        stream.Write(bytes, 0, bytes.Length);
                }
                return true;
            }
            catch
            {
                return false;
            }
        }

        void RotateFiles()
        {
            if (maxFileCount <= 1)
            {
                File.Delete(filePath);
                return;
            }

            string oldest = RotatedPath(maxFileCount - 1);
            if (File.Exists(oldest)) File.Delete(oldest);
            for (int index = maxFileCount - 2; index >= 1; index--)
            {
                string source = RotatedPath(index);
                if (!File.Exists(source)) continue;
                string destination = RotatedPath(index + 1);
                if (File.Exists(destination)) File.Delete(destination);
                File.Move(source, destination);
            }

            if (File.Exists(filePath)) File.Move(filePath, RotatedPath(1));
        }

        string RotatedPath(int index)
        {
            return filePath + "." + index;
        }
    }

    /// <summary>
    /// Thin scene adapter. All public hooks are failure-isolated so optional
    /// telemetry cannot interrupt gameplay, including when a custom sink throws.
    /// </summary>
    public static class BalanceTelemetryRuntime
    {
        static MatchManager currentManager;
        static BalanceTelemetryAccumulator accumulator;
        static IBalanceTelemetrySink sink;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetRuntimeState()
        {
            currentManager = null;
            accumulator = null;
            sink = null;
        }

        public static void SetSink(IBalanceTelemetrySink replacement)
        {
            sink = replacement;
        }

        public static void BeginMatch(MatchManager manager)
        {
            try
            {
                if (manager == null) return;
                if (ReferenceEquals(currentManager, manager) && accumulator != null &&
                    accumulator.HasBegun)
                    return;

                currentManager = manager;
                accumulator = new BalanceTelemetryAccumulator(sink ?? CreateDefaultSink());
                accumulator.BeginMatch(manager.mode, MobileQualitySettings.EffectiveTier,
                    Time.timeAsDouble);

                List<BrawlerController> existing = manager.GetBrawlers();
                for (int i = 0; i < existing.Count; i++) RegisterBrawler(manager, existing[i]);
            }
            catch
            {
                currentManager = null;
                accumulator = null;
            }
        }

        public static void RegisterBrawler(MatchManager manager, BrawlerController brawler)
        {
            try
            {
                if (!IsCurrent(manager) || brawler == null) return;
                accumulator.RegisterBrawler(Key(brawler), brawler.displayName, brawler.team,
                    brawler.IsPlayer);
            }
            catch
            {
                // Optional instrumentation never blocks fighter registration.
            }
        }

        public static void RecordDamage(Health victimHealth, GameObject attackerObject,
            float appliedAmount)
        {
            try
            {
                if (!CanRecordCombat() || victimHealth == null || appliedAmount <= 0f) return;
                BrawlerController victim = victimHealth.GetComponentInParent<BrawlerController>();
                if (victim == null) return;
                BrawlerController attacker = attackerObject != null
                    ? attackerObject.GetComponentInParent<BrawlerController>()
                    : null;
                EnsureRegistered(victim);
                if (attacker != null) EnsureRegistered(attacker);
                accumulator.RecordDamage(Key(victim), attacker != null ? Key(attacker) : null,
                    appliedAmount);
            }
            catch
            {
                // Damage remains authoritative if telemetry is unavailable.
            }
        }

        public static void RecordHealing(Health health, float restoredAmount)
        {
            try
            {
                if (!CanRecordCombat() || health == null || restoredAmount <= 0f) return;
                BrawlerController brawler = health.GetComponentInParent<BrawlerController>();
                if (brawler == null) return;
                EnsureRegistered(brawler);
                accumulator.RecordHealing(Key(brawler), restoredAmount);
            }
            catch
            {
                // Healing remains authoritative if telemetry is unavailable.
            }
        }

        public static void RecordAttack(BrawlerController brawler)
        {
            try
            {
                if (!CanRecordCombat() || brawler == null) return;
                EnsureRegistered(brawler);
                accumulator.RecordAttack(Key(brawler));
            }
            catch
            {
                // Attack activation remains authoritative.
            }
        }

        public static void RecordSuper(BrawlerController brawler)
        {
            try
            {
                if (!CanRecordCombat() || brawler == null) return;
                EnsureRegistered(brawler);
                accumulator.RecordSuper(Key(brawler));
            }
            catch
            {
                // Super activation remains authoritative.
            }
        }

        public static void RecordKnockout(MatchManager manager, BrawlerController victim,
            BrawlerController attacker)
        {
            try
            {
                if (!IsCurrent(manager) || victim == null || accumulator.HasEnded) return;
                EnsureRegistered(victim);
                bool credited = attacker != null && attacker != victim &&
                                attacker.team != victim.team;
                if (credited) EnsureRegistered(attacker);
                accumulator.RecordKnockout(Key(victim), credited ? Key(attacker) : null);
            }
            catch
            {
                // KO scoring remains authoritative.
            }
        }

        public static BalanceMatchReport EndMatch(MatchManager manager, TeamId? winner,
            int blueScore, int redScore)
        {
            try
            {
                if (!IsCurrent(manager)) return null;
                return accumulator.EndMatch(winner, blueScore, redScore,
                    Time.timeAsDouble);
            }
            catch
            {
                return null;
            }
        }

        static bool IsCurrent(MatchManager manager)
        {
            return manager != null && ReferenceEquals(currentManager, manager) &&
                   accumulator != null && accumulator.HasBegun;
        }

        static bool CanRecordCombat()
        {
            return currentManager != null && MatchManager.Instance == currentManager &&
                   currentManager.IsCombatActive && accumulator != null &&
                   accumulator.HasBegun && !accumulator.HasEnded;
        }

        static void EnsureRegistered(BrawlerController brawler)
        {
            accumulator.RegisterBrawler(Key(brawler), brawler.displayName, brawler.team,
                brawler.IsPlayer);
        }

        static string Key(BrawlerController brawler)
        {
            // Internal for one in-memory match only; never copied to the report.
            return brawler.GetInstanceID().ToString();
        }

        static IBalanceTelemetrySink CreateDefaultSink()
        {
#if UNITY_EDITOR
            string root = Directory.GetParent(Application.dataPath)?.FullName ??
                          Application.persistentDataPath;
            string path = Path.Combine(root, "Logs", "balance-telemetry.jsonl");
#else
            string path = Path.Combine(Application.persistentDataPath,
                "balance-telemetry.jsonl");
#endif
            return new JsonLinesBalanceTelemetrySink(path);
        }
    }
}
