# Wizard Mechanics Lab

This isolated .NET 8 console lab compares two basic-attack packages against the current wizard baseline without loading or changing Unity content.

1. **Focus Economy** — starts at 100 focus, costs 30 per successful cast, and regenerates 18/s after a 0.75 s post-spend delay. Adaptive play may hold the last sub-60 reserve for a close, lethal, or enemy-casting window.
2. **Committed Charged Casting** — choose 0.25–0.90 s; linear damage is 0.70–1.35x and projectile speed is 0.80–1.20x; movement is 55%; direction locks at commitment; a hit worth at least 10% of maximum health interrupts. Cooldown starts on a successful release; an interrupt instead applies 0.45 s recovery.

The six profiles in `Domain.cs` reproduce the health, damage, range, cooldown, hit delay, movement, auto-aim, and projectile-speed arguments in `ArenaSceneBuilder.BuildRoster` exactly.

## Run

```powershell
dotnet run --project Tools/WizardMechanicsLab -c Release -- --matches 1000 --seed 20260711 --out Tools/WizardMechanicsLab/results
```

`--matches` is the seed count for each of 36 ordered roster pairings, not the total match count. The recorded decision run uses 1,000, yielding 36,000 neutral duels and 72,000 policy-evaluation duels per mechanic, plus calibration and stream-swapped mirror-fairness cohorts. Use 40 for a quick smoke run. Use `--format json`, `--format markdown`, or the default `both`. Run only invariants with `--self-test-only`.

## Pre-registered decision rule

A candidate must satisfy all three gates:

- capped mean duel duration is 15–40% above baseline;
- adaptive score is at least 60% against the best fixed greedy policy selected on disjoint calibration seeds, and its paired 95% CI is above 50%;
- identical-profile left-side score is 45–55%.

If both pass, the lower timeout rate wins; adaptive uplift breaks a near tie. If neither passes, the report names the strongest candidate but explicitly withholds a ship recommendation. Action mix, close finishes, comebacks, and interrupts support diagnosis rather than changing the winner after results are seen.

## Model boundary

The simulator has deterministic 2D movement, travel-time projectiles, collision, aim error, leading, reactive dodge execution, kiting, direction lock, and simultaneous hit application. It omits obstacles/LOS, supers, school effects, health regeneration, teams, and the gem objective. It tests relative mechanical plausibility, not feel, and its duel durations must not be compared numerically with full Unity matches.
