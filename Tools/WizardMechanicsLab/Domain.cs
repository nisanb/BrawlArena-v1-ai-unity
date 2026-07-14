namespace WizardMechanicsLab;

public enum MechanicId
{
    Baseline,
    FocusEconomy,
    CommittedCharge
}

public enum PolicyId
{
    Adaptive,
    GreedyRapid,
    GreedyPower
}

public sealed record WizardProfile(
    string Id,
    string Name,
    double MaxHealth,
    double Damage,
    double AttackRange,
    double Cooldown,
    double HitDelay,
    double MoveSpeed,
    double AutoAimRange,
    double ProjectileSpeed);

/// <summary>
/// Exact combat fields from ArenaSceneBuilder.BuildRoster. Supers and school
/// specialties are intentionally outside this isolated basic-attack experiment.
/// </summary>
public static class WizardRoster
{
    public static readonly IReadOnlyList<WizardProfile> All = new[]
    {
        new WizardProfile("arcane", "Aether", 108, 19, 9.0, 1.02, 0.40, 5.00, 11.0, 18),
        new WizardProfile("fire",   "Cinder",  92, 22, 9.2, 1.16, 0.43, 4.90, 11.5, 17),
        new WizardProfile("frost",  "Rime",   112, 16, 8.7, 1.08, 0.42, 4.75, 10.5, 16),
        new WizardProfile("storm",  "Tempest", 88, 17, 9.5, 0.82, 0.32, 5.55, 12.0, 21),
        new WizardProfile("earth",  "Terra",  138, 20, 8.3, 1.28, 0.47, 4.45, 10.0, 15),
        new WizardProfile("void",   "Nyx",     98, 20, 9.0, 1.08, 0.39, 5.20, 11.0, 18),
    };
}

public readonly record struct Vec2(double X, double Y)
{
    public static readonly Vec2 Zero = new(0, 0);

    public double LengthSquared => X * X + Y * Y;
    public double Length => Math.Sqrt(LengthSquared);
    public Vec2 Normalized => Length > 1e-9 ? this / Length : Zero;
    public Vec2 Perpendicular => new(-Y, X);

    public static Vec2 operator +(Vec2 a, Vec2 b) => new(a.X + b.X, a.Y + b.Y);
    public static Vec2 operator -(Vec2 a, Vec2 b) => new(a.X - b.X, a.Y - b.Y);
    public static Vec2 operator -(Vec2 a) => new(-a.X, -a.Y);
    public static Vec2 operator *(Vec2 a, double b) => new(a.X * b, a.Y * b);
    public static Vec2 operator /(Vec2 a, double b) => new(a.X / b, a.Y / b);
    public static double Dot(Vec2 a, Vec2 b) => a.X * b.X + a.Y * b.Y;
    public static double Distance(Vec2 a, Vec2 b) => (a - b).Length;
}

/// <summary>Runtime-independent seeded generator (SplitMix64).</summary>
public sealed class DeterministicRandom
{
    private ulong _state;

    public DeterministicRandom(ulong seed) => _state = seed;

    public ulong NextUInt64()
    {
        ulong z = (_state += 0x9E3779B97F4A7C15UL);
        z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL;
        z = (z ^ (z >> 27)) * 0x94D049BB133111EBUL;
        return z ^ (z >> 31);
    }

    public double NextDouble() => (NextUInt64() >> 11) * (1.0 / (1UL << 53));
    public double Range(double min, double max) => min + (max - min) * NextDouble();

    public static ulong Derive(ulong seed, params int[] values)
    {
        ulong result = seed ^ 0xD1B54A32D192ED03UL;
        foreach (int value in values)
        {
            result ^= unchecked((uint)value) + 0x9E3779B97F4A7C15UL + (result << 6) + (result >> 2);
            result ^= result >> 29;
            result *= 0x94D049BB133111EBUL;
        }
        return result;
    }
}
