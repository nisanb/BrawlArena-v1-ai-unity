namespace WizardMechanicsLab;

public static class SelfTests
{
    public static void RunAll()
    {
        Check(WizardRoster.All.Count == 6, "exactly six wizard profiles");
        Check(WizardRoster.All.Select(x => x.Id).Distinct().Count() == 6, "wizard ids unique");
        Check(WizardRoster.All.Single(x => x.Id == "storm").Cooldown == 0.82,
            "Tempest roster value copied exactly");
        Check(WizardRoster.All.Single(x => x.Id == "earth").MaxHealth == 138,
            "Terra roster value copied exactly");

        FocusEconomyRules.State focus = FocusEconomyRules.State.Full;
        Check(FocusEconomyRules.TrySpend(ref focus), "first focus spend succeeds");
        CheckNear(focus.Focus, 70, "focus cost");
        focus = FocusEconomyRules.Advance(focus, 0.74);
        CheckNear(focus.Focus, 70, "no early focus regeneration");
        focus = FocusEconomyRules.Advance(focus, 0.02);
        CheckNear(focus.Focus, 70.18, "partial step after focus delay");
        FocusEconomyRules.State insufficient = new(29.999, 0);
        Check(!FocusEconomyRules.TrySpend(ref insufficient), "insufficient focus rejected");
        CheckNear(insufficient.Focus, 29.999, "rejected focus spend has no side effect");

        CommittedChargeRules.Evaluation quick =
            CommittedChargeRules.Evaluate(CommittedChargeRules.MinimumSeconds);
        CommittedChargeRules.Evaluation full =
            CommittedChargeRules.Evaluate(CommittedChargeRules.MaximumSeconds);
        CheckNear(quick.DamageMultiplier, 0.70, "quick damage endpoint");
        CheckNear(full.DamageMultiplier, 1.35, "full damage endpoint");
        CheckNear(quick.ProjectileSpeedMultiplier, 0.80, "quick speed endpoint");
        CheckNear(full.ProjectileSpeedMultiplier, 1.20, "full speed endpoint");
        Check(!CommittedChargeRules.Interrupts(9.999, 100), "interrupt below threshold rejected");
        Check(CommittedChargeRules.Interrupts(10, 100), "interrupt threshold inclusive");

        var simulator = new DuelSimulator(new SimulationSettings(TimeLimitSeconds: 8));
        WizardProfile aether = WizardRoster.All[0];
        WizardProfile cinder = WizardRoster.All[1];
        DuelResult first = simulator.Run(MechanicId.CommittedCharge, aether, PolicyId.Adaptive,
            cinder, PolicyId.GreedyRapid, 123456789UL);
        DuelResult second = simulator.Run(MechanicId.CommittedCharge, aether, PolicyId.Adaptive,
            cinder, PolicyId.GreedyRapid, 123456789UL);
        Check(first.Winner == second.Winner && Math.Abs(first.Duration - second.Duration) < 1e-12 &&
              Math.Abs(first.LeftHealth - second.LeftHealth) < 1e-12 &&
              first.LeftActions.Casts == second.LeftActions.Casts,
            "same seed produces same duel");
        Check(double.IsFinite(first.LeftHealth) && double.IsFinite(first.RightHealth),
            "duel health remains finite");
    }

    private static void Check(bool condition, string name)
    {
        if (!condition) throw new InvalidOperationException("Self-test failed: " + name);
    }

    private static void CheckNear(double actual, double expected, string name)
    {
        if (Math.Abs(actual - expected) > 1e-9)
            throw new InvalidOperationException(
                $"Self-test failed: {name}; expected {expected}, got {actual}");
    }
}
