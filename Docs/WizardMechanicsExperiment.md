# Wizard mechanics experiment

Date: 2026-07-11

## Decision to make

Choose one mechanic package that makes wizard combat more competitive and deliberate, rewards tactical decisions, and has more believable spell preparation. The live combat code is intentionally unchanged by this experiment; the repository already contains extensive uncommitted wizard work, so the candidates are isolated in a headless lab first.

## Current baseline

The six wizard attacks are cooldown-only. Their cooldowns range from 0.82 to 1.28 seconds, casts do not reduce movement speed, targeted attacks re-aim when their wind-up completes, and elemental effects happen automatically on a hit. Ideal all-hit TTK is roughly 5.5–7.4 seconds against the roster's mean health, while the controlled Unity run also included misses, cover, regeneration, and respawns.

A controlled Unity baseline used seed `20260711`, Knockout, a 150-second clock, and an 8-KO target. Blue (Aether, Cinder, Rime) led Red (Tempest, Terra, Nyx) 6–5 when time expired:

| Metric | Baseline |
|---|---:|
| Duration | 150.27 s (timeout) |
| Attacks | 357 |
| Damage | 2,810.6 |
| Healing | 1,511.7 |
| Supers | 11 |
| KOs | 11 |
| KOs/min | 4.39 |
| Attacks/KO | 32.5 |
| Applied damage/attack | 7.87 |

This is one descriptive run, not a balance sample. It also shows why total match duration cannot be the success measure: the current mode already times out. "Slower" therefore means longer, more readable engagements and fewer low-commitment casts, with match completion retained as a guardrail. Healing was 53.8% of applied damage, so a headless duel model without the full regeneration/specialty loop is valid only for relative A/B comparisons.

## Candidate A: Focus Economy

- 100 Focus, 30 per accepted basic cast.
- Regenerate 18 Focus/second after 0.75 seconds without casting.
- A rejected cast spends nothing and starts no cooldown.
- Respawn refills Focus; Supers remain charge-gated and do not cost Focus.
- A mechanic-aware bot saves Focus for plausible hit windows and repositions while depleted; the greedy policy casts whenever legal.

Hypothesis: opening burst remains responsive, while sustained spam becomes costly. The main risks are passive waiting, resource-starved input, and disproportionate impact on Tempest's fast-caster identity.

## Candidate B: Committed Charged Casting

- Hold 0.25–0.90 seconds before release.
- Damage scales from 0.70x to 1.35x and projectile speed from 0.80x to 1.20x.
- Movement is 55% while charging, and aim locks when charging begins.
- A direct enemy hit dealing at least 10% max HP interrupts the charge and causes 0.45 seconds of recovery.
- A mechanic-aware bot quick-casts under pressure and charges when distance and threat allow; fixed policies always use one charge level.

Hypothesis: quick-safe versus slow-powerful decisions create active counterplay without a passive refill phase. The risks are over-bundling several changes and making full charge a trap because normal direct hits usually exceed the interrupt threshold.

## Pre-registered evaluation

The headless lab uses all six authored wizard stat lines, fixed seeds, side-swapped mirrors, and at least 1,000 trials per matchup. The adaptive policy is compared with the strongest fixed/greedy policy chosen on a separate calibration seed.

Evaluation priorities:

- Strategic depth — adaptive policy advantage, with action mix as supporting evidence.
- Pace and competition — engagement TTK +15–40%, mirrored side win rate 45–55%, and no excessive duel timeout rate.
- Mechanical plausibility — bounded resource or visible preparation, trajectory commitment, travel time, and counterplay.

Pace, strategy, and mirror fairness are hard gates, not an arithmetic weighted score; timeout rate breaks ties between candidates that pass the same gates. The strategy gate is an adaptive win-rate uplift of at least 10 percentage points with mirrored evaluation. A simulation can test mechanical plausibility, not whether combat *feels* realistic; that requires a blinded in-engine human playtest.

## Results

The frozen primary model ran 1,000 seeds for every ordered wizard pairing: 36,000 neutral duels and 72,000 side-swapped policy duels per mechanic, plus separate calibration and mirror-fairness cohorts.

| Mechanic | Mean capped duel | Change | Timeout | Adaptive vs best fixed | Mirror fairness |
|---|---:|---:|---:|---:|---:|
| Baseline | 8.84 s | — | 0.0% | 50.0% | 49.3% |
| Focus Economy | 13.86 s | +56.8% | 0.0% | 49.8% (49.6–50.0%) | 50.7% |
| Committed Casting | 26.57 s | +200.6% | 36.7% | 37.7% (37.3–38.0%) | 48.2% |

Neither variant passed the pace or strategy gate. Both passed the mirror-fairness gate.

- Focus slowed exchanges with a near-zero headless timeout rate (5 of 36,000; 0.014%), but overshot the +15–40% target and gave the adaptive policy no advantage. Actors were Focus-starved 37–46% of the time, and the flat cost reduced Tempest's sustainable cadence by 50.8% versus Terra's 23.2%.
- Committed Casting was far too slow in the spatial model. Rapid fixed casting beat full-power casting with an 82.7% paired score, only 1.3% of adaptive casts were full charges, and 36.7% of duels timed out. The intended quick-versus-powerful choice collapsed into a rapid-cast dominant strategy.
- Competitive proxies did not improve: Focus close finishes fell from 39.9% to 34.1%; Committed Casting cut comeback conversion from 20.7% to 8.8% in the primary model.

An independent 60,000-duel-per-mechanic cross-check used a simpler stochastic exposure model. It also found no strategic uplift: Focus adaptive play scored 49.73%, and Committed adaptive play scored 45.17% against their best fixed policies. It found less severe charged-cast pacing, which confirms that absolute timeout estimates are model-sensitive, but the failed strategy conclusion is robust. Its recorded assumptions and per-wizard results are in `Docs/WizardMechanicsIndependentCrossCheck.md`; it was a read-only cross-check, not a second checked-in executable.

## Decision

**Focus Economy is the best of the two packages exactly as tested, but neither should ship.** It wins on balance safety: fair mirrors, near-zero simulated timeouts, and a much smaller failure than Committed Casting. It still fails the requested strategy goal and risks worsening the live game's existing regeneration-driven stalemates.

Committed Casting is the better direction for a second design pass because telegraphing, aim commitment, movement commitment, and interruption can create active counterplay and more believable spell preparation. Before retesting, make full charge situationally viable without making it dominant, lengthen or otherwise expose the full-charge window, and test each lever separately. Do not deploy the tested 0.25–0.90 s / 0.70–1.35x package.

The complete reproducible output for the primary spatial model is in `Tools/WizardMechanicsLab/results`.

## Follow-up playtest needed

Before shipping any revised candidate, run a counterbalanced in-engine test with 10–12 players, two matches per candidate, randomized order, and identical loadouts. Ask for 1–5 ratings on spell weight, cause/effect clarity, readable counterplay, meaningful decisions, and sluggishness (reverse-scored). Require at least 60% preference or a mean realism/weight score of 4/5, while responsiveness stays at least 3.5/5.
