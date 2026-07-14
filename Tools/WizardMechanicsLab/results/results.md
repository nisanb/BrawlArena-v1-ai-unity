# Wizard mechanics headless experiment

Seed: `20260711`. Seeds per ordered roster pairing: `1000`. Neutral matches per mechanic: `36000`. Duel cap: `45 s`.

## Result

**Focus Economy** — No variant passed every pre-registered gate; this is the strongest candidate, not a ship recommendation.

Pre-registered gates: capped mean duration must be 15–40% above baseline; adaptive score must beat the selected best fixed greedy policy by at least 10 percentage points with its 95% CI above 50%; identical-profile left-side score must remain 45–55%.

| Mechanic | Capped duration | vs baseline | Timeout | Decisive TTK | Adaptive score (95% CI) | Advantage | Mirror left score | Gates |
|---|---:|---:|---:|---:|---:|---:|---:|---|
| Baseline | 8.84s | — | 0.0% | 8.84s | 50.0% (50.0%–50.0%) | 0.0pp | 49.3% | reference |
| Focus Economy | 13.86s | +56.8% | 0.0% | 13.86s | 49.8% (49.6%–50.0%) | -0.2pp | 50.7% | pace ✗, strategy ✗, fairness ✓ |
| Committed Charged Casting | 26.57s | +200.6% | 36.7% | 15.90s | 37.7% (37.3%–38.0%) | -12.3pp | 48.2% | pace ✗, strategy ✗, fairness ✓ |

Competitive proxies (diagnostic only):

| Mechanic | Close finishes | Comeback conversion | Lead changes/match | Double KOs |
|---|---:|---:|---:|---:|
| Baseline | 39.9% | 20.7% | 1.36 | 267 |
| Focus Economy | 34.1% | 21.0% | 1.33 | 326 |
| Committed Charged Casting | 38.4% | 8.8% | 1.03 | 123 |

## Policy and action evidence

A disjoint calibration cohort selected the stronger fixed policy (rapid or power) before adaptive evaluation. Every strategy sample is a pair with policy sides reversed; draws score 0.5.

Baseline-adjusted strategy is the variant advantage minus baseline's 0.0pp: Focus Economy -0.2pp, Committed Charged Casting -12.3pp. A positive raw result that does not exceed baseline would not be evidence that the new mechanic added strategic value.

| Mechanic | Best fixed | Calibration rapid score | Eval W–L–D (adaptive) | Casts/match | Hit rate | Quick / medium / full | Interrupt/cast (eligibility) | Focus waits / reserve holds | Comeback conversion |
|---|---|---:|---:|---:|---:|---:|---:|---:|---:|
| Baseline | greedy rapid | 50.0% | 35734–35734–532 | 16.64 | 61.3% | — | 0.0% (0.0%) | 0.00 / 0.00 | 20.7% (n=40978) |
| Focus Economy | greedy rapid | 50.0% | 35478–35785–737 | 15.87 | 62.4% | — | 0.0% (0.0%) | 3.49 / 9.48 | 21.0% (n=41887) |
| Committed Charged Casting | greedy rapid | 82.7% | 24690–42464–4846 | 36.45 | 28.6% | 39.1% / 59.6% / 1.3% | 1.7% (90.5%) | 0.00 / 0.00 | 8.8% (n=35533) |

## Focus cadence tax

At empty focus the next cast takes `0.75 + 30/18 = 2.42 s`; this universal cost compresses distinct authored cooldowns. The analytic reductions below compare each live cooldown ceiling with the 0.60 casts/s sustained focus ceiling and do not include the initial 100-focus burst.

| Wizard | Authored cooldown | Cooldown ceiling | Focus ceiling | Sustained cadence reduction |
|---|---:|---:|---:|---:|
| Aether | 1.02s | 0.98/s | 0.60/s | 38.8% |
| Cinder | 1.16s | 0.86/s | 0.60/s | 30.4% |
| Rime | 1.08s | 0.93/s | 0.60/s | 35.2% |
| Tempest | 0.82s | 1.22/s | 0.60/s | 50.8% |
| Terra | 1.28s | 0.78/s | 0.60/s | 23.2% |
| Nyx | 1.08s | 0.93/s | 0.60/s | 35.2% |

Observed neutral-cohort focus pressure (per actor-time):

| Wizard | Casts/min | Starved time | Adaptive reserve time |
|---|---:|---:|---:|
| Aether | 34.1 | 40.0% | 34.1% |
| Cinder | 36.2 | 36.7% | 26.1% |
| Rime | 32.8 | 40.1% | 36.9% |
| Tempest | 35.1 | 46.1% | 44.5% |
| Terra | 33.7 | 39.1% | 24.1% |
| Nyx | 34.9 | 39.5% | 32.6% |

> Trap warning: greedy rapid defeated greedy full-power with a 82.7% paired score. Full charge is a trap under this package/model, not a healthy equal option.

## Assumptions and limits

- This is a seeded 2D duel abstraction with continuous movement, projectile travel, collision, aiming error, leading, kiting, and reactive dodging. It tests **mechanical plausibility**, not subjective feel.
- Baseline and Focus re-aim when the authored hit delay completes, matching the current target-tracking attack routine. Charged aim is captured at commitment; its chosen charge replaces the authored hit delay.
- Baseline/Focus cooldown begins at attempt start. Charged cooldown begins only on release; an interrupted attempt launches nothing and instead gets 0.45 s recovery.
- Focus starts full. Only a successful spend resets the 0.75 s regen delay; rejected attempts do not. Adaptive holds the last sub-60 reserve for a close, lethal, or enemy-casting window; the fixed policies spend whenever legal.
- Adaptive and fixed policies observe the same current positions, velocities, casts, health, focus, and visible projectiles. Neither sees future randomness. Adaptive has the same general aim/dodge implementation in every mechanic.
- The charged proposal is a **five-lever package**. Results cannot identify whether charge duration, curves, movement, direction lock, or interrupts caused an outcome. The 10%-max-HP interrupt threshold is intentionally literal and many 16–22 damage base hits cross it.
- No obstacles/LOS, supers, school specialties, teams, gem objective, status effects, health regeneration, or school-specific Arcane sustain are modeled. Focus gaps could trigger live health regeneration, so its timeout estimate is optimistic. Compare variants only within this model; do not compare these duel seconds with the existing 150 s Unity team match.
- Mirror fairness uses identical profiles/policies, independent actor random streams, a second run with those streams swapped, simultaneous damage application, and explicit double-KO separation from timeouts.
- Confidence intervals are normal 95% intervals over paired side-reversal samples. They quantify simulation sampling error, not model uncertainty.
