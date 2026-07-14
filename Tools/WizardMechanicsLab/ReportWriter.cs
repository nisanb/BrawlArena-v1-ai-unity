using System.Text;

namespace WizardMechanicsLab;

public static class ReportWriter
{
    public static string ToMarkdown(ExperimentReport report)
    {
        MechanicExperiment baseline = report.Mechanics.Single(x => x.Mechanic == MechanicId.Baseline);
        var text = new StringBuilder();
        text.AppendLine("# Wizard mechanics headless experiment");
        text.AppendLine();
        text.AppendLine($"Seed: `{report.Settings.Seed}`. Seeds per ordered roster pairing: `{report.Settings.MatchesPerOrderedPair}`. " +
                        $"Neutral matches per mechanic: `{baseline.NeutralCohort.Matches}`. Duel cap: `{report.Settings.Simulation.TimeLimitSeconds:0.##} s`.");
        text.AppendLine();
        text.AppendLine("## Result");
        text.AppendLine();
        text.AppendLine($"**{Display(report.Recommendation.Selected)}** — {report.Recommendation.Verdict}");
        text.AppendLine();
        text.AppendLine("Pre-registered gates: capped mean duration must be 15–40% above baseline; adaptive score must beat the selected best fixed greedy policy by at least 10 percentage points with its 95% CI above 50%; identical-profile left-side score must remain 45–55%.");
        text.AppendLine();
        text.AppendLine("| Mechanic | Capped duration | vs baseline | Timeout | Decisive TTK | Adaptive score (95% CI) | Advantage | Mirror left score | Gates |");
        text.AppendLine("|---|---:|---:|---:|---:|---:|---:|---:|---|");
        foreach (MechanicExperiment item in report.Mechanics)
        {
            double pace = item.NeutralCohort.MeanCappedDuration /
                          baseline.NeutralCohort.MeanCappedDuration - 1;
            string gates = item.Mechanic == MechanicId.Baseline
                ? "reference"
                : GateText(report.Recommendation.Gates[item.Mechanic.ToString()]);
            text.AppendLine($"| {Display(item.Mechanic)} | {item.NeutralCohort.MeanCappedDuration:0.00}s | " +
                            $"{(item.Mechanic == MechanicId.Baseline ? "—" : PercentDelta(pace))} | " +
                            $"{Percent(Rate(item.NeutralCohort.Timeouts, item.NeutralCohort.Matches))} | " +
                            $"{item.NeutralCohort.MeanDecisiveTtk:0.00}s | " +
                            $"{Percent(item.Strategy.AdaptiveScore.Estimate)} " +
                            $"({Percent(item.Strategy.AdaptiveScore.Lower)}–{Percent(item.Strategy.AdaptiveScore.Upper)}) | " +
                            $"{PercentSigned(item.Strategy.AdaptiveAdvantage)} | " +
                            $"{Percent(item.MirrorFairness.LeftSideScore.Estimate)} | {gates} |");
        }

        text.AppendLine();
        text.AppendLine("Competitive proxies (diagnostic only):");
        text.AppendLine();
        text.AppendLine("| Mechanic | Close finishes | Comeback conversion | Lead changes/match | Double KOs |");
        text.AppendLine("|---|---:|---:|---:|---:|");
        foreach (MechanicExperiment item in report.Mechanics)
        {
            text.AppendLine($"| {Display(item.Mechanic)} | {Percent(item.NeutralCohort.CloseFinishRate)} | " +
                            $"{Percent(item.NeutralCohort.ComebackConversionRate)} | " +
                            $"{item.NeutralCohort.MeanLeadChanges:0.00} | {item.NeutralCohort.DoubleKos} |");
        }

        text.AppendLine();
        text.AppendLine("## Policy and action evidence");
        text.AppendLine();
        text.AppendLine("A disjoint calibration cohort selected the stronger fixed policy (rapid or power) before adaptive evaluation. Every strategy sample is a pair with policy sides reversed; draws score 0.5.");
        text.AppendLine();
        text.AppendLine($"Baseline-adjusted strategy is the variant advantage minus baseline's {PercentSigned(baseline.Strategy.AdaptiveAdvantage)}: " +
                        string.Join(", ", report.Mechanics
                            .Where(x => x.Mechanic != MechanicId.Baseline)
                            .Select(x => $"{Display(x.Mechanic)} {PercentSigned(x.Strategy.AdaptiveAdvantage - baseline.Strategy.AdaptiveAdvantage)}")) +
                        ". A positive raw result that does not exceed baseline would not be evidence that the new mechanic added strategic value.");
        text.AppendLine();
        text.AppendLine("| Mechanic | Best fixed | Calibration rapid score | Eval W–L–D (adaptive) | Casts/match | Hit rate | Quick / medium / full | Interrupt/cast (eligibility) | Focus waits / reserve holds | Comeback conversion |");
        text.AppendLine("|---|---|---:|---:|---:|---:|---:|---:|---:|---:|");
        foreach (MechanicExperiment item in report.Mechanics)
        {
            ActionCounts a = item.NeutralCohort.Actions;
            int chargeCasts = a.QuickCasts + a.MediumCasts + a.FullCasts;
            string mix = chargeCasts == 0
                ? "—"
                : $"{Percent(Rate(a.QuickCasts, chargeCasts))} / {Percent(Rate(a.MediumCasts, chargeCasts))} / {Percent(Rate(a.FullCasts, chargeCasts))}";
            text.AppendLine($"| {Display(item.Mechanic)} | {Display(item.Calibration.SelectedGreedy)} | " +
                            $"{Percent(item.Calibration.RapidScore)} | " +
                            $"{item.Strategy.AdaptiveWins}–{item.Strategy.GreedyWins}–{item.Strategy.Draws} | " +
                            $"{Rate(a.Casts, item.NeutralCohort.Matches):0.00} | {Percent(item.NeutralCohort.HitRate)} | " +
                            $"{mix} | {Percent(item.NeutralCohort.InterruptionsPerChargedCast)} " +
                            $"({Percent(item.NeutralCohort.InterruptEligibilityRate)}) | " +
                            $"{Rate(a.FocusWaits, item.NeutralCohort.Matches):0.00} / " +
                            $"{Rate(a.FocusReserveHolds, item.NeutralCohort.Matches):0.00} | " +
                            $"{Percent(item.NeutralCohort.ComebackConversionRate)} " +
                            $"(n={item.NeutralCohort.ComebackOpportunities}) |");
        }

        text.AppendLine();
        text.AppendLine("## Focus cadence tax");
        text.AppendLine();
        text.AppendLine("At empty focus the next cast takes `0.75 + 30/18 = 2.42 s`; this universal cost compresses distinct authored cooldowns. The analytic reductions below compare each live cooldown ceiling with the 0.60 casts/s sustained focus ceiling and do not include the initial 100-focus burst.");
        text.AppendLine();
        text.AppendLine("| Wizard | Authored cooldown | Cooldown ceiling | Focus ceiling | Sustained cadence reduction |");
        text.AppendLine("|---|---:|---:|---:|---:|");
        foreach (WizardProfile wizard in report.Roster)
        {
            double authored = 1 / wizard.Cooldown;
            double focus = FocusEconomyRules.RegenPerSecond / FocusEconomyRules.CastCost;
            text.AppendLine($"| {wizard.Name} | {wizard.Cooldown:0.00}s | {authored:0.00}/s | {focus:0.00}/s | {Percent(1 - focus / authored)} |");
        }

        MechanicExperiment focusExperiment = report.Mechanics.Single(x => x.Mechanic == MechanicId.FocusEconomy);
        text.AppendLine();
        text.AppendLine("Observed neutral-cohort focus pressure (per actor-time):");
        text.AppendLine();
        text.AppendLine("| Wizard | Casts/min | Starved time | Adaptive reserve time |");
        text.AppendLine("|---|---:|---:|---:|");
        foreach (WizardProfile wizard in report.Roster)
        {
            ProfileActionSummary profile = focusExperiment.NeutralCohort.Profiles[wizard.Id];
            text.AppendLine($"| {wizard.Name} | {profile.CastsPerMinute:0.0} | " +
                            $"{Percent(profile.FocusStarvedFraction)} | {Percent(profile.FocusReserveFraction)} |");
        }

        MechanicExperiment chargeExperiment = report.Mechanics.Single(x => x.Mechanic == MechanicId.CommittedCharge);
        if (chargeExperiment.Calibration.RapidScore > 0.60)
        {
            text.AppendLine();
            text.AppendLine($"> Trap warning: greedy rapid defeated greedy full-power with a {Percent(chargeExperiment.Calibration.RapidScore)} paired score. Full charge is a trap under this package/model, not a healthy equal option.");
        }

        text.AppendLine();
        text.AppendLine("## Assumptions and limits");
        text.AppendLine();
        text.AppendLine("- This is a seeded 2D duel abstraction with continuous movement, projectile travel, collision, aiming error, leading, kiting, and reactive dodging. It tests **mechanical plausibility**, not subjective feel.");
        text.AppendLine("- Baseline and Focus re-aim when the authored hit delay completes, matching the current target-tracking attack routine. Charged aim is captured at commitment; its chosen charge replaces the authored hit delay.");
        text.AppendLine("- Baseline/Focus cooldown begins at attempt start. Charged cooldown begins only on release; an interrupted attempt launches nothing and instead gets 0.45 s recovery.");
        text.AppendLine("- Focus starts full. Only a successful spend resets the 0.75 s regen delay; rejected attempts do not. Adaptive holds the last sub-60 reserve for a close, lethal, or enemy-casting window; the fixed policies spend whenever legal.");
        text.AppendLine("- Adaptive and fixed policies observe the same current positions, velocities, casts, health, focus, and visible projectiles. Neither sees future randomness. Adaptive has the same general aim/dodge implementation in every mechanic.");
        text.AppendLine("- The charged proposal is a **five-lever package**. Results cannot identify whether charge duration, curves, movement, direction lock, or interrupts caused an outcome. The 10%-max-HP interrupt threshold is intentionally literal and many 16–22 damage base hits cross it.");
        text.AppendLine("- No obstacles/LOS, supers, school specialties, teams, gem objective, status effects, health regeneration, or school-specific Arcane sustain are modeled. Focus gaps could trigger live health regeneration, so its timeout estimate is optimistic. Compare variants only within this model; do not compare these duel seconds with the existing 150 s Unity team match.");
        text.AppendLine("- Mirror fairness uses identical profiles/policies, independent actor random streams, a second run with those streams swapped, simultaneous damage application, and explicit double-KO separation from timeouts.");
        text.AppendLine("- Confidence intervals are normal 95% intervals over paired side-reversal samples. They quantify simulation sampling error, not model uncertainty.");
        return text.ToString();
    }

    private static string GateText(GateResult gate) =>
        $"pace {(gate.Pace ? "✓" : "✗")}, strategy {(gate.Strategy ? "✓" : "✗")}, fairness {(gate.Fairness ? "✓" : "✗")}";

    private static string Display(MechanicId value) => value switch
    {
        MechanicId.FocusEconomy => "Focus Economy",
        MechanicId.CommittedCharge => "Committed Charged Casting",
        _ => "Baseline",
    };

    private static string Display(PolicyId value) => value switch
    {
        PolicyId.GreedyRapid => "greedy rapid",
        PolicyId.GreedyPower => "greedy power",
        _ => "adaptive",
    };

    private static double Rate(int numerator, int denominator) => denominator > 0 ? (double)numerator / denominator : 0;
    private static string Percent(double value) => $"{value * 100:0.0}%";
    private static string PercentSigned(double value) => $"{value * 100:+0.0;-0.0;0.0}pp";
    private static string PercentDelta(double value) => $"{value * 100:+0.0;-0.0;0.0}%";
}
