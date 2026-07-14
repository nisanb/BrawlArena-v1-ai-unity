namespace WizardMechanicsLab;

/// <summary>
/// Pure focus rules, kept free of simulator types so they can be copied into
/// Unity edit-mode tests without taking the experiment harness with them.
/// </summary>
public static class FocusEconomyRules
{
    public const double Capacity = 100.0;
    public const double CastCost = 30.0;
    public const double RegenPerSecond = 18.0;
    public const double RegenDelaySeconds = 0.75;

    public readonly record struct State(double Focus, double RegenBlockedFor)
    {
        public static State Full => new(Capacity, 0);
    }

    public static State Advance(State state, double deltaSeconds)
    {
        if (deltaSeconds < 0) throw new ArgumentOutOfRangeException(nameof(deltaSeconds));
        double blocked = Math.Max(0, state.RegenBlockedFor - deltaSeconds);
        double eligible = Math.Max(0, deltaSeconds - state.RegenBlockedFor);
        double focus = Math.Min(Capacity, state.Focus + eligible * RegenPerSecond);
        return new State(focus, blocked);
    }

    public static bool TrySpend(ref State state)
    {
        if (state.Focus + 1e-9 < CastCost) return false;
        state = new State(state.Focus - CastCost, RegenDelaySeconds);
        return true;
    }
}

/// <summary>Pure charged-cast curve and interrupt threshold.</summary>
public static class CommittedChargeRules
{
    public const double MinimumSeconds = 0.25;
    public const double MaximumSeconds = 0.90;
    public const double MinimumDamageMultiplier = 0.70;
    public const double MaximumDamageMultiplier = 1.35;
    public const double MinimumProjectileSpeedMultiplier = 0.80;
    public const double MaximumProjectileSpeedMultiplier = 1.20;
    public const double MovementMultiplier = 0.55;
    public const double InterruptFractionOfMaxHealth = 0.10;

    public readonly record struct Evaluation(
        double Seconds,
        double NormalizedCharge,
        double DamageMultiplier,
        double ProjectileSpeedMultiplier);

    public static Evaluation Evaluate(double requestedSeconds)
    {
        double seconds = Math.Clamp(requestedSeconds, MinimumSeconds, MaximumSeconds);
        double t = (seconds - MinimumSeconds) / (MaximumSeconds - MinimumSeconds);
        return new Evaluation(
            seconds,
            t,
            Lerp(MinimumDamageMultiplier, MaximumDamageMultiplier, t),
            Lerp(MinimumProjectileSpeedMultiplier, MaximumProjectileSpeedMultiplier, t));
    }

    public static bool Interrupts(double incomingDamage, double targetMaxHealth) =>
        incomingDamage + 1e-9 >= targetMaxHealth * InterruptFractionOfMaxHealth;

    public static double SecondsForDamageMultiplier(double multiplier)
    {
        double t = (multiplier - MinimumDamageMultiplier) /
                   (MaximumDamageMultiplier - MinimumDamageMultiplier);
        return MinimumSeconds + Math.Clamp(t, 0, 1) * (MaximumSeconds - MinimumSeconds);
    }

    private static double Lerp(double a, double b, double t) => a + (b - a) * t;
}
