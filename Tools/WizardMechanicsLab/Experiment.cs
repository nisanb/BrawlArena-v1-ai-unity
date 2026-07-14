namespace WizardMechanicsLab;

public sealed record ExperimentSettings(
    int MatchesPerOrderedPair,
    ulong Seed,
    SimulationSettings Simulation);

public sealed record ConfidenceInterval(double Estimate, double Lower, double Upper, int Samples);

public sealed record CalibrationSummary(
    PolicyId SelectedGreedy,
    double RapidScore,
    ConfidenceInterval RapidScoreInterval,
    int PairedSamples);

public sealed class CohortSummary
{
    public int Matches { get; init; }
    public int DecisiveMatches { get; init; }
    public int Timeouts { get; init; }
    public int DoubleKos { get; init; }
    public double MeanCappedDuration { get; init; }
    public double MeanDecisiveTtk { get; init; }
    public double CloseFinishRate { get; init; }
    public double ComebackConversionRate { get; init; }
    public int ComebackOpportunities { get; init; }
    public double MeanLeadChanges { get; init; }
    public ActionCounts Actions { get; init; } = new();
    public double HitRate { get; init; }
    public double InterruptionsPerChargedCast { get; init; }
    public double InterruptEligibilityRate { get; init; }
    public IReadOnlyDictionary<string, ProfileActionSummary> Profiles { get; init; } =
        new Dictionary<string, ProfileActionSummary>();
}

public sealed record ProfileActionSummary(
    int Appearances,
    double CombatSeconds,
    double CastsPerMinute,
    double FocusStarvedFraction,
    double FocusReserveFraction);

public sealed record StrategySummary(
    PolicyId BestGreedy,
    ConfidenceInterval AdaptiveScore,
    double AdaptiveAdvantage,
    int AdaptiveWins,
    int GreedyWins,
    int Draws);

public sealed record FairnessSummary(
    ConfidenceInterval LeftSideScore,
    int LeftWins,
    int RightWins,
    int Draws);

public sealed record MechanicExperiment(
    MechanicId Mechanic,
    CalibrationSummary Calibration,
    CohortSummary NeutralCohort,
    StrategySummary Strategy,
    FairnessSummary MirrorFairness);

public sealed record ExperimentReport(
    string SchemaVersion,
    DateTimeOffset GeneratedAtUtc,
    ExperimentSettings Settings,
    IReadOnlyList<WizardProfile> Roster,
    IReadOnlyList<MechanicExperiment> Mechanics,
    Recommendation Recommendation);

public sealed record GateResult(bool Pace, bool Strategy, bool Fairness)
{
    public bool Passed => Pace && Strategy && Fairness;
}

public sealed record Recommendation(
    MechanicId Selected,
    string Verdict,
    IReadOnlyDictionary<string, GateResult> Gates);

public sealed class ExperimentRunner
{
    private readonly ExperimentSettings _settings;
    private readonly DuelSimulator _simulator;

    public ExperimentRunner(ExperimentSettings settings)
    {
        _settings = settings;
        _simulator = new DuelSimulator(settings.Simulation);
    }

    public ExperimentReport Run()
    {
        var experiments = Enum.GetValues<MechanicId>()
            .Select(RunMechanic)
            .ToArray();
        Recommendation recommendation = SelectRecommendation(experiments);
        return new ExperimentReport(
            "1.0",
            DateTimeOffset.UtcNow,
            _settings,
            WizardRoster.All,
            experiments,
            recommendation);
    }

    private MechanicExperiment RunMechanic(MechanicId mechanic)
    {
        CalibrationSummary calibration = CalibrateGreedy(mechanic);
        CohortSummary neutral = RunNeutral(mechanic);
        StrategySummary strategy = RunStrategy(mechanic, calibration.SelectedGreedy);
        FairnessSummary fairness = RunFairness(mechanic);
        return new MechanicExperiment(mechanic, calibration, neutral, strategy, fairness);
    }

    private CalibrationSummary CalibrateGreedy(MechanicId mechanic)
    {
        int calibrationMatches = Math.Max(4, _settings.MatchesPerOrderedPair / 5);
        var pairedScores = new List<double>();
        int mechanicKey = (int)mechanic;
        for (int leftProfile = 0; leftProfile < WizardRoster.All.Count; leftProfile++)
        for (int rightProfile = 0; rightProfile < WizardRoster.All.Count; rightProfile++)
        for (int match = 0; match < calibrationMatches; match++)
        {
            ulong seed = DeterministicRandom.Derive(_settings.Seed, 10, mechanicKey,
                leftProfile, rightProfile, match);
            DuelResult rapidLeft = _simulator.Run(mechanic,
                WizardRoster.All[leftProfile], PolicyId.GreedyRapid,
                WizardRoster.All[rightProfile], PolicyId.GreedyPower, seed);
            DuelResult rapidRight = _simulator.Run(mechanic,
                WizardRoster.All[leftProfile], PolicyId.GreedyPower,
                WizardRoster.All[rightProfile], PolicyId.GreedyRapid, seed);
            double score = (ScoreForSide(rapidLeft, 0) + ScoreForSide(rapidRight, 1)) / 2;
            pairedScores.Add(score);
        }

        ConfidenceInterval interval = MeanInterval(pairedScores);
        PolicyId selected = interval.Estimate >= 0.5 ? PolicyId.GreedyRapid : PolicyId.GreedyPower;
        return new CalibrationSummary(selected, interval.Estimate, interval, pairedScores.Count);
    }

    private CohortSummary RunNeutral(MechanicId mechanic)
    {
        var accumulator = new CohortAccumulator();
        int mechanicKey = (int)mechanic;
        for (int leftProfile = 0; leftProfile < WizardRoster.All.Count; leftProfile++)
        for (int rightProfile = 0; rightProfile < WizardRoster.All.Count; rightProfile++)
        for (int match = 0; match < _settings.MatchesPerOrderedPair; match++)
        {
            ulong seed = DeterministicRandom.Derive(_settings.Seed, 20, mechanicKey,
                leftProfile, rightProfile, match);
            WizardProfile left = WizardRoster.All[leftProfile];
            WizardProfile right = WizardRoster.All[rightProfile];
            DuelResult result = _simulator.Run(mechanic, left, PolicyId.Adaptive,
                right, PolicyId.Adaptive, seed);
            accumulator.Add(result, left, right);
        }
        return accumulator.Build();
    }

    private StrategySummary RunStrategy(MechanicId mechanic, PolicyId greedy)
    {
        var samples = new List<double>();
        int adaptiveWins = 0;
        int greedyWins = 0;
        int draws = 0;
        int mechanicKey = (int)mechanic;
        for (int leftProfile = 0; leftProfile < WizardRoster.All.Count; leftProfile++)
        for (int rightProfile = 0; rightProfile < WizardRoster.All.Count; rightProfile++)
        for (int match = 0; match < _settings.MatchesPerOrderedPair; match++)
        {
            ulong seed = DeterministicRandom.Derive(_settings.Seed, 30, mechanicKey,
                leftProfile, rightProfile, match);
            WizardProfile left = WizardRoster.All[leftProfile];
            WizardProfile right = WizardRoster.All[rightProfile];
            DuelResult adaptiveLeft = _simulator.Run(mechanic, left, PolicyId.Adaptive,
                right, greedy, seed);
            DuelResult adaptiveRight = _simulator.Run(mechanic, left, greedy,
                right, PolicyId.Adaptive, seed);
            CountPolicyResult(adaptiveLeft, 0, ref adaptiveWins, ref greedyWins, ref draws);
            CountPolicyResult(adaptiveRight, 1, ref adaptiveWins, ref greedyWins, ref draws);
            samples.Add((ScoreForSide(adaptiveLeft, 0) + ScoreForSide(adaptiveRight, 1)) / 2);
        }

        ConfidenceInterval interval = MeanInterval(samples);
        return new StrategySummary(greedy, interval, interval.Estimate - 0.5,
            adaptiveWins, greedyWins, draws);
    }

    private FairnessSummary RunFairness(MechanicId mechanic)
    {
        int matchesPerWizard = Math.Max(200, _settings.MatchesPerOrderedPair * 4);
        var pairedScores = new List<double>();
        int leftWins = 0;
        int rightWins = 0;
        int draws = 0;
        int mechanicKey = (int)mechanic;
        for (int profile = 0; profile < WizardRoster.All.Count; profile++)
        for (int match = 0; match < matchesPerWizard; match++)
        {
            ulong seed = DeterministicRandom.Derive(_settings.Seed, 40, mechanicKey, profile, match);
            WizardProfile wizard = WizardRoster.All[profile];
            DuelResult original = _simulator.Run(mechanic, wizard, PolicyId.Adaptive,
                wizard, PolicyId.Adaptive, seed, swapRandomStreams: false);
            DuelResult streamSwap = _simulator.Run(mechanic, wizard, PolicyId.Adaptive,
                wizard, PolicyId.Adaptive, seed, swapRandomStreams: true);
            CountSides(original, ref leftWins, ref rightWins, ref draws);
            CountSides(streamSwap, ref leftWins, ref rightWins, ref draws);
            pairedScores.Add((ScoreForSide(original, 0) + ScoreForSide(streamSwap, 0)) / 2);
        }
        return new FairnessSummary(MeanInterval(pairedScores), leftWins, rightWins, draws);
    }

    private Recommendation SelectRecommendation(IReadOnlyList<MechanicExperiment> experiments)
    {
        MechanicExperiment baseline = experiments.Single(x => x.Mechanic == MechanicId.Baseline);
        var gates = new Dictionary<string, GateResult>();
        foreach (MechanicExperiment item in experiments.Where(x => x.Mechanic != MechanicId.Baseline))
        {
            double paceDelta = item.NeutralCohort.MeanCappedDuration /
                               baseline.NeutralCohort.MeanCappedDuration - 1;
            bool pace = paceDelta >= 0.15 && paceDelta <= 0.40;
            bool strategy = item.Strategy.AdaptiveAdvantage >= 0.10 &&
                            item.Strategy.AdaptiveScore.Lower > 0.50;
            bool fairness = item.MirrorFairness.LeftSideScore.Estimate >= 0.45 &&
                            item.MirrorFairness.LeftSideScore.Estimate <= 0.55;
            gates[item.Mechanic.ToString()] = new GateResult(pace, strategy, fairness);
        }

        MechanicExperiment[] passed = experiments
            .Where(x => x.Mechanic != MechanicId.Baseline && gates[x.Mechanic.ToString()].Passed)
            .OrderBy(x => (double)x.NeutralCohort.Timeouts / x.NeutralCohort.Matches)
            .ThenByDescending(x => x.Strategy.AdaptiveAdvantage)
            .ToArray();
        MechanicExperiment selected;
        string verdict;
        if (passed.Length > 0)
        {
            selected = passed[0];
            verdict = "Selected from variants passing all pre-registered pace, strategy, and fairness gates; lowest timeout rate breaks ties, then adaptive uplift.";
        }
        else
        {
            selected = experiments
                .Where(x => x.Mechanic != MechanicId.Baseline)
                .OrderByDescending(x => gates[x.Mechanic.ToString()].Pace ? 1 : 0)
                .ThenByDescending(x => gates[x.Mechanic.ToString()].Strategy ? 1 : 0)
                .ThenByDescending(x => gates[x.Mechanic.ToString()].Fairness ? 1 : 0)
                .ThenBy(x => (double)x.NeutralCohort.Timeouts / x.NeutralCohort.Matches)
                .First();
            verdict = "No variant passed every pre-registered gate; this is the strongest candidate, not a ship recommendation.";
        }
        return new Recommendation(selected.Mechanic, verdict, gates);
    }

    private static void CountPolicyResult(
        DuelResult result,
        int adaptiveSide,
        ref int adaptiveWins,
        ref int greedyWins,
        ref int draws)
    {
        if (result.Winner < 0) draws++;
        else if (result.Winner == adaptiveSide) adaptiveWins++;
        else greedyWins++;
    }

    private static void CountSides(DuelResult result, ref int leftWins, ref int rightWins, ref int draws)
    {
        if (result.Winner == 0) leftWins++;
        else if (result.Winner == 1) rightWins++;
        else draws++;
    }

    private static double ScoreForSide(DuelResult result, int side) =>
        result.Winner < 0 ? 0.5 : result.Winner == side ? 1.0 : 0.0;

    private static ConfidenceInterval MeanInterval(IReadOnlyList<double> values)
    {
        if (values.Count == 0) return new ConfidenceInterval(0, 0, 0, 0);
        double mean = values.Average();
        double variance = values.Count > 1
            ? values.Sum(x => (x - mean) * (x - mean)) / (values.Count - 1)
            : 0;
        double margin = 1.96 * Math.Sqrt(variance / values.Count);
        return new ConfidenceInterval(mean, Math.Max(0, mean - margin), Math.Min(1, mean + margin), values.Count);
    }

    private sealed class CohortAccumulator
    {
        private int _matches;
        private int _decisive;
        private int _timeouts;
        private int _doubleKos;
        private double _duration;
        private double _decisiveTtk;
        private int _closeFinishes;
        private int _comebackOpportunities;
        private int _comebacks;
        private int _leadChanges;
        private readonly ActionCounts _actions = new();
        private readonly Dictionary<string, ProfileAccumulator> _profiles = new();

        public void Add(DuelResult result, WizardProfile left, WizardProfile right)
        {
            _matches++;
            _duration += result.Duration;
            _leadChanges += result.LeadChanges;
            _actions.Add(result.LeftActions);
            _actions.Add(result.RightActions);
            AddProfile(left.Id, result.Duration, result.LeftActions);
            AddProfile(right.Id, result.Duration, result.RightActions);
            if (result.LeftHadComebackOpportunity)
            {
                _comebackOpportunities++;
                if (result.Winner == 0) _comebacks++;
            }
            if (result.RightHadComebackOpportunity)
            {
                _comebackOpportunities++;
                if (result.Winner == 1) _comebacks++;
            }
            if (result.TimedOut)
            {
                _timeouts++;
                return;
            }
            _decisive++;
            _decisiveTtk += result.Duration;
            if (result.DoubleKo)
            {
                _doubleKos++;
                _closeFinishes++;
                return;
            }
            double winnerFraction = result.Winner == 0
                ? result.LeftHealth / left.MaxHealth
                : result.RightHealth / right.MaxHealth;
            if (winnerFraction <= 0.25) _closeFinishes++;
        }

        public CohortSummary Build()
        {
            int resolvedProjectiles = _actions.Hits + _actions.Misses;
            return new CohortSummary
            {
                Matches = _matches,
                DecisiveMatches = _decisive,
                Timeouts = _timeouts,
                DoubleKos = _doubleKos,
                MeanCappedDuration = _matches > 0 ? _duration / _matches : 0,
                MeanDecisiveTtk = _decisive > 0 ? _decisiveTtk / _decisive : 0,
                CloseFinishRate = _decisive > 0 ? (double)_closeFinishes / _decisive : 0,
                ComebackConversionRate = _comebackOpportunities > 0
                    ? (double)_comebacks / _comebackOpportunities
                    : 0,
                ComebackOpportunities = _comebackOpportunities,
                MeanLeadChanges = _matches > 0 ? (double)_leadChanges / _matches : 0,
                Actions = _actions,
                HitRate = resolvedProjectiles > 0 ? (double)_actions.Hits / resolvedProjectiles : 0,
                InterruptionsPerChargedCast = _actions.Casts > 0
                    ? (double)_actions.Interruptions / _actions.Casts
                    : 0,
                InterruptEligibilityRate = _actions.ChargeHitsReceived > 0
                    ? (double)_actions.Interruptions / _actions.ChargeHitsReceived
                    : 0,
                Profiles = _profiles.ToDictionary(
                    pair => pair.Key,
                    pair => pair.Value.Build()),
            };
        }

        private void AddProfile(string id, double duration, ActionCounts actions)
        {
            if (!_profiles.TryGetValue(id, out ProfileAccumulator? profile))
            {
                profile = new ProfileAccumulator();
                _profiles.Add(id, profile);
            }
            profile.Appearances++;
            profile.Duration += duration;
            profile.Casts += actions.Casts;
            profile.FocusStarved += actions.FocusStarvedSeconds;
            profile.FocusReserve += actions.FocusReserveSeconds;
        }

        private sealed class ProfileAccumulator
        {
            public int Appearances { get; set; }
            public double Duration { get; set; }
            public int Casts { get; set; }
            public double FocusStarved { get; set; }
            public double FocusReserve { get; set; }

            public ProfileActionSummary Build() => new(
                Appearances,
                Duration,
                Duration > 0 ? Casts * 60 / Duration : 0,
                Duration > 0 ? FocusStarved / Duration : 0,
                Duration > 0 ? FocusReserve / Duration : 0);
        }
    }
}
