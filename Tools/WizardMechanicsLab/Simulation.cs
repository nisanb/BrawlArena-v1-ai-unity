namespace WizardMechanicsLab;

public sealed record SimulationSettings(
    double StepSeconds = 0.025,
    double TimeLimitSeconds = 45.0,
    double ArenaRadius = 10.5,
    double StartingSeparation = 8.0,
    double ThinkInterval = 0.10,
    double HitRadius = 1.05,
    double AimErrorDegrees = 1.75);

public sealed class ActionCounts
{
    public int Casts { get; set; }
    public int QuickCasts { get; set; }
    public int MediumCasts { get; set; }
    public int FullCasts { get; set; }
    public int Dodges { get; set; }
    public int RangeChanges { get; set; }
    public int FocusWaits { get; set; }
    public int FocusReserveHolds { get; set; }
    public int Interruptions { get; set; }
    public int ChargeHitsReceived { get; set; }
    public int Hits { get; set; }
    public int Misses { get; set; }
    public double FocusStarvedSeconds { get; set; }
    public double FocusReserveSeconds { get; set; }

    public void Add(ActionCounts other)
    {
        Casts += other.Casts;
        QuickCasts += other.QuickCasts;
        MediumCasts += other.MediumCasts;
        FullCasts += other.FullCasts;
        Dodges += other.Dodges;
        RangeChanges += other.RangeChanges;
        FocusWaits += other.FocusWaits;
        FocusReserveHolds += other.FocusReserveHolds;
        Interruptions += other.Interruptions;
        ChargeHitsReceived += other.ChargeHitsReceived;
        Hits += other.Hits;
        Misses += other.Misses;
        FocusStarvedSeconds += other.FocusStarvedSeconds;
        FocusReserveSeconds += other.FocusReserveSeconds;
    }
}

public sealed record DuelResult(
    int Winner,
    double Duration,
    double LeftHealth,
    double RightHealth,
    bool LeftHadComebackOpportunity,
    bool RightHadComebackOpportunity,
    int LeadChanges,
    ActionCounts LeftActions,
    ActionCounts RightActions)
{
    public required bool TimeLimitReached { get; init; }
    public bool TimedOut => Winner < 0 && TimeLimitReached;
    public bool DoubleKo => Winner < 0 && !TimeLimitReached;
}

public sealed class DuelSimulator
{
    private readonly SimulationSettings _settings;

    public DuelSimulator(SimulationSettings settings) => _settings = settings;

    private sealed class Fighter
    {
        public required WizardProfile Wizard { get; init; }
        public required PolicyId Policy { get; init; }
        public required DeterministicRandom Random { get; init; }
        public required Vec2 Position { get; set; }
        public Vec2 Velocity { get; set; }
        public double Health { get; set; }
        public double NextThinkAt { get; set; }
        public double NextCastAt { get; set; }
        public double DodgeUntil { get; set; }
        public double NextThreatDecisionAt { get; set; }
        public double NextRangeActionAt { get; set; }
        public int StrafeSign { get; set; }
        public FocusEconomyRules.State Focus { get; set; } = FocusEconomyRules.State.Full;
        public Cast? PendingCast { get; set; }
        public bool WaitingForFocus { get; set; }
        public bool WaitingForReserve { get; set; }
        public ActionCounts Actions { get; } = new();
        public bool Alive => Health > 1e-9;
    }

    private sealed record Cast(double LaunchAt, Vec2 LockedDirection, double Damage, double Speed);

    private sealed class Projectile
    {
        public required int Owner { get; init; }
        public required Vec2 Position { get; set; }
        public required Vec2 Velocity { get; init; }
        public required double Damage { get; init; }
        public required double ExpiresAt { get; init; }
    }

    public DuelResult Run(
        MechanicId mechanic,
        WizardProfile leftWizard,
        PolicyId leftPolicy,
        WizardProfile rightWizard,
        PolicyId rightPolicy,
        ulong seed,
        bool swapRandomStreams = false)
    {
        var fighters = new[]
        {
            CreateFighter(leftWizard, leftPolicy, new Vec2(-_settings.StartingSeparation / 2, 0),
                DeterministicRandom.Derive(seed, swapRandomStreams ? 1 : 0)),
            CreateFighter(rightWizard, rightPolicy, new Vec2(_settings.StartingSeparation / 2, 0),
                DeterministicRandom.Derive(seed, swapRandomStreams ? 0 : 1)),
        };
        var projectiles = new List<Projectile>(16);
        double time = 0;
        int lead = 0;
        int leadChanges = 0;
        bool[] comeback = { false, false };

        while (time < _settings.TimeLimitSeconds && fighters[0].Alive && fighters[1].Alive)
        {
            double step = Math.Min(_settings.StepSeconds, _settings.TimeLimitSeconds - time);
            for (int i = 0; i < 2; i++)
            {
                Fighter self = fighters[i];
                Fighter enemy = fighters[1 - i];
                if (mechanic == MechanicId.FocusEconomy)
                {
                    self.Focus = FocusEconomyRules.Advance(self.Focus, step);
                    if (self.Focus.Focus + 1e-9 < FocusEconomyRules.CastCost)
                        self.Actions.FocusStarvedSeconds += step;
                    if (self.WaitingForReserve) self.Actions.FocusReserveSeconds += step;
                }
                if (time + 1e-9 >= self.NextThinkAt)
                {
                    Think(self, enemy, projectiles, i, mechanic, time);
                    self.NextThinkAt = time + _settings.ThinkInterval;
                }
                Move(self, step, mechanic);
            }

            for (int i = 0; i < 2; i++)
            {
                Fighter self = fighters[i];
                if (self.PendingCast is { } cast && time + step + 1e-9 >= cast.LaunchAt)
                {
                    if (cast.LockedDirection.LengthSquared < 1e-9)
                        cast = cast with { LockedDirection = Aim(self, fighters[1 - i], cast.Speed, false) };
                    Launch(projectiles, self, i, cast, time + step);
                    self.PendingCast = null;
                }
            }

            AdvanceProjectiles(projectiles, fighters, time, step, mechanic);
            time += step;

            double gap = fighters[0].Health / fighters[0].Wizard.MaxHealth -
                         fighters[1].Health / fighters[1].Wizard.MaxHealth;
            if (gap <= -0.20) comeback[0] = true;
            if (gap >= 0.20) comeback[1] = true;
            int nextLead = gap > 0.05 ? 1 : gap < -0.05 ? -1 : 0;
            if (nextLead != 0 && lead != 0 && nextLead != lead) leadChanges++;
            if (nextLead != 0) lead = nextLead;
        }

        int winner = fighters[0].Alive == fighters[1].Alive ? -1 : fighters[0].Alive ? 0 : 1;
        bool timeLimitReached = time >= _settings.TimeLimitSeconds - 1e-8 &&
                                fighters[0].Alive && fighters[1].Alive;
        return new DuelResult(
            winner,
            Math.Min(time, _settings.TimeLimitSeconds),
            Math.Max(0, fighters[0].Health),
            Math.Max(0, fighters[1].Health),
            comeback[0],
            comeback[1],
            leadChanges,
            fighters[0].Actions,
            fighters[1].Actions)
        {
            TimeLimitReached = timeLimitReached,
        };
    }

    private Fighter CreateFighter(
        WizardProfile wizard,
        PolicyId policy,
        Vec2 position,
        ulong randomSeed)
    {
        var random = new DeterministicRandom(randomSeed);
        return new Fighter
        {
            Wizard = wizard,
            Policy = policy,
            Random = random,
            Position = position,
            Health = wizard.MaxHealth,
            NextThinkAt = random.Range(0, _settings.ThinkInterval),
            StrafeSign = random.NextDouble() < 0.5 ? -1 : 1,
        };
    }

    private void Think(
        Fighter self,
        Fighter enemy,
        IReadOnlyList<Projectile> projectiles,
        int selfIndex,
        MechanicId mechanic,
        double time)
    {
        Vec2 toEnemy = enemy.Position - self.Position;
        double distance = toEnemy.Length;
        Vec2 forward = toEnemy.Normalized;
        Projectile? threat = FindThreat(self, projectiles, selfIndex);

        if (threat is not null && time >= self.NextThreatDecisionAt)
        {
            // Common human-scale execution model: every policy has the same
            // chance to react to currently visible danger. Policy comparisons
            // therefore isolate resource/charge choice, not motor skill.
            if (self.Random.NextDouble() < 0.55)
            {
                self.DodgeUntil = time + 0.32;
                self.Actions.Dodges++;
            }
            self.NextThreatDecisionAt = time + 0.35;
        }

        if (threat is not null && time < self.DodgeUntil)
        {
            Vec2 side = threat.Velocity.Normalized.Perpendicular;
            if (Vec2.Dot(side, self.Position - threat.Position) < 0) side = -side;
            self.Velocity = side * self.Wizard.MoveSpeed;
        }
        else
        {
            double desiredRange = self.Wizard.AttackRange * 0.76;
            Vec2 direction;
            if (distance > desiredRange + 0.65) direction = forward;
            else if (distance < desiredRange - 0.65) direction = -forward;
            else direction = forward.Perpendicular * self.StrafeSign;
            self.Velocity = direction * self.Wizard.MoveSpeed;
            if (time >= self.NextRangeActionAt)
            {
                self.Actions.RangeChanges++;
                self.NextRangeActionAt = time + 0.75;
            }
        }

        if (self.PendingCast is not null || time + 1e-9 < self.NextCastAt || !enemy.Alive)
            return;

        double castRange = self.Policy == PolicyId.Adaptive
            ? self.Wizard.AutoAimRange * 0.90
            : self.Wizard.AutoAimRange;
        if (distance > castRange) return;

        if (mechanic == MechanicId.FocusEconomy)
        {
            bool favorableWindow = enemy.PendingCast is not null ||
                                   enemy.Health <= self.Wizard.Damage * 1.05 ||
                                   (distance <= self.Wizard.AttackRange * 0.70 && threat is null);
            if (self.Policy == PolicyId.Adaptive &&
                self.Focus.Focus < FocusEconomyRules.CastCost * 2 &&
                !favorableWindow)
            {
                if (!self.WaitingForReserve)
                {
                    self.Actions.FocusReserveHolds++;
                    self.WaitingForReserve = true;
                }
                return;
            }
            FocusEconomyRules.State focus = self.Focus;
            if (!FocusEconomyRules.TrySpend(ref focus))
            {
                if (!self.WaitingForFocus)
                {
                    self.Actions.FocusWaits++;
                    self.WaitingForFocus = true;
                }
                return;
            }
            self.Focus = focus;
            self.WaitingForFocus = false;
            self.WaitingForReserve = false;
        }

        BeginCast(self, enemy, mechanic, time, threat is not null);
    }

    private void BeginCast(Fighter self, Fighter enemy, MechanicId mechanic, double time, bool threatened)
    {
        double windup;
        double damage = self.Wizard.Damage;
        double speed = self.Wizard.ProjectileSpeed;
        Vec2 direction = Vec2.Zero;

        if (mechanic == MechanicId.CommittedCharge)
        {
            double requested = ChooseCharge(self, enemy, threatened);
            CommittedChargeRules.Evaluation charge = CommittedChargeRules.Evaluate(requested);
            windup = charge.Seconds;
            damage *= charge.DamageMultiplier;
            speed *= charge.ProjectileSpeedMultiplier;
            direction = Aim(self, enemy, speed, lockNow: true);
            if (charge.NormalizedCharge <= 0.20) self.Actions.QuickCasts++;
            else if (charge.NormalizedCharge >= 0.80) self.Actions.FullCasts++;
            else self.Actions.MediumCasts++;
        }
        else
        {
            windup = self.Wizard.HitDelay;
        }

        self.Actions.Casts++;
        self.NextCastAt = mechanic == MechanicId.CommittedCharge
            ? time + windup + self.Wizard.Cooldown
            : time + self.Wizard.Cooldown;
        self.PendingCast = new Cast(time + windup, direction, damage, speed);
    }

    private static double ChooseCharge(Fighter self, Fighter enemy, bool threatened)
    {
        if (self.Policy == PolicyId.GreedyRapid) return CommittedChargeRules.MinimumSeconds;
        if (self.Policy == PolicyId.GreedyPower) return CommittedChargeRules.MaximumSeconds;

        double multiplierToFinish = enemy.Health / self.Wizard.Damage;
        if (multiplierToFinish <= CommittedChargeRules.MaximumDamageMultiplier)
            return CommittedChargeRules.SecondsForDamageMultiplier(multiplierToFinish);
        if (enemy.PendingCast is not null || threatened)
            return CommittedChargeRules.MinimumSeconds;
        double distance = Vec2.Distance(self.Position, enemy.Position);
        if (distance < self.Wizard.AttackRange * 0.58) return 0.32;
        if (enemy.Velocity.Length > enemy.Wizard.MoveSpeed * 0.8) return 0.44;
        if (self.Health / self.Wizard.MaxHealth < 0.42) return 0.38;
        return distance > self.Wizard.AttackRange * 0.82 ? 0.76 : 0.58;
    }

    private Vec2 Aim(Fighter self, Fighter enemy, double projectileSpeed, bool lockNow)
    {
        Vec2 delta = enemy.Position - self.Position;
        double travel = Math.Min(0.70, delta.Length / Math.Max(1, projectileSpeed));
        const double leadFraction = 1.0;
        Vec2 aimPoint = enemy.Position + enemy.Velocity * (travel * leadFraction);
        Vec2 direction = (aimPoint - self.Position).Normalized;
        double error = self.Random.Range(-_settings.AimErrorDegrees, _settings.AimErrorDegrees) *
                       Math.PI / 180.0;
        double c = Math.Cos(error);
        double s = Math.Sin(error);
        _ = lockNow; // documents that the returned vector is captured by the caller.
        return new Vec2(direction.X * c - direction.Y * s, direction.X * s + direction.Y * c);
    }

    private void Move(Fighter fighter, double step, MechanicId mechanic)
    {
        double multiplier = mechanic == MechanicId.CommittedCharge && fighter.PendingCast is not null
            ? CommittedChargeRules.MovementMultiplier
            : 1.0;
        Vec2 next = fighter.Position + fighter.Velocity * (step * multiplier);
        if (next.Length > _settings.ArenaRadius)
        {
            Vec2 outward = next.Normalized;
            Vec2 tangent = fighter.Velocity - outward * Vec2.Dot(fighter.Velocity, outward);
            fighter.Velocity = tangent;
            next = outward * _settings.ArenaRadius;
        }
        fighter.Position = next;
    }

    private void Launch(
        ICollection<Projectile> projectiles,
        Fighter self,
        int owner,
        Cast cast,
        double time)
    {
        Vec2 direction = cast.LockedDirection;
        // Baseline and focus reproduce target tracking during authored hitDelay.
        // Charged casts already supplied a nonzero direction captured at commit.
        if (direction.LengthSquared < 1e-9)
            throw new InvalidOperationException("Dynamic casts must be aimed before Launch.");
        projectiles.Add(new Projectile
        {
            Owner = owner,
            Position = self.Position + direction * 0.45,
            Velocity = direction * cast.Speed,
            Damage = cast.Damage,
            ExpiresAt = time + (self.Wizard.AttackRange + 2.0) / cast.Speed,
        });
    }

    private void AdvanceProjectiles(
        List<Projectile> projectiles,
        Fighter[] fighters,
        double time,
        double step,
        MechanicId mechanic)
    {
        var damage = new double[2];
        var hitProjectiles = new bool[projectiles.Count];
        for (int i = 0; i < projectiles.Count; i++)
        {
            Projectile projectile = projectiles[i];
            int targetIndex = 1 - projectile.Owner;
            Vec2 start = projectile.Position;
            Vec2 end = start + projectile.Velocity * step;
            projectile.Position = end;
            if (SegmentDistance(start, end, fighters[targetIndex].Position) <= _settings.HitRadius)
            {
                damage[targetIndex] += projectile.Damage;
                fighters[projectile.Owner].Actions.Hits++;
                hitProjectiles[i] = true;
            }
            else if (time + step + 1e-9 >= projectile.ExpiresAt)
            {
                fighters[projectile.Owner].Actions.Misses++;
                hitProjectiles[i] = true;
            }
        }

        for (int i = projectiles.Count - 1; i >= 0; i--)
            if (hitProjectiles[i]) projectiles.RemoveAt(i);

        for (int i = 0; i < 2; i++)
        {
            if (damage[i] <= 0) continue;
            Fighter target = fighters[i];
            target.Health -= damage[i];
            if (mechanic == MechanicId.CommittedCharge && target.PendingCast is not null)
            {
                target.Actions.ChargeHitsReceived++;
                if (CommittedChargeRules.Interrupts(damage[i], target.Wizard.MaxHealth))
                {
                    target.PendingCast = null;
                    target.Actions.Interruptions++;
                    // No projectile was released, so release-based cooldown never
                    // starts. The cancelled attempt gets only its recovery lock.
                    target.NextCastAt = time + step + 0.45;
                }
            }
        }
    }

    private Projectile? FindThreat(Fighter self, IReadOnlyList<Projectile> projectiles, int selfIndex)
    {
        Projectile? closest = null;
        double closestTime = double.MaxValue;
        foreach (Projectile projectile in projectiles)
        {
            if (projectile.Owner == selfIndex) continue;
            Vec2 relative = self.Position - projectile.Position;
            double speedSq = projectile.Velocity.LengthSquared;
            if (speedSq < 1e-9) continue;
            double t = Vec2.Dot(relative, projectile.Velocity) / speedSq;
            if (t < 0 || t > 0.45) continue;
            Vec2 closestPoint = projectile.Position + projectile.Velocity * t;
            if (Vec2.Distance(closestPoint, self.Position) <= _settings.HitRadius + 0.25 && t < closestTime)
            {
                closest = projectile;
                closestTime = t;
            }
        }
        return closest;
    }

    private static double SegmentDistance(Vec2 a, Vec2 b, Vec2 point)
    {
        Vec2 segment = b - a;
        double lengthSq = segment.LengthSquared;
        if (lengthSq < 1e-12) return Vec2.Distance(a, point);
        double t = Math.Clamp(Vec2.Dot(point - a, segment) / lengthSq, 0, 1);
        return Vec2.Distance(a + segment * t, point);
    }
}
