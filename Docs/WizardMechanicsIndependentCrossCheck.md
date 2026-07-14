# Wizard mechanics independent cross-check

Date: 2026-07-11

This was a read-only, independently constructed stochastic lane model used to challenge the primary spatial simulator's conclusion. It is recorded for auditability but is not a checked-in executable.

## Protocol

- Seed `20260711`.
- The six authored wizard health, damage, range, cooldown, hit-delay, movement, and projectile-speed profiles.
- 60,000 adaptive mirror duels per mechanic and 60,000 held-out adaptive-versus-selected-naive duels per mechanic.
- A 45-second cap, 180 ms reaction latency, projectile travel, observable 0.65–1.3-second stochastic exposure windows, and ±10% damage variance.
- Supers, school effects, health regeneration, and 3v3 coordination excluded to isolate the tested mechanics.

## Results

| Wizard | Baseline TTK | Focus TTK | Charged TTK |
|---|---:|---:|---:|
| Aether | 7.25 s | 11.30 s | 11.30 s |
| Cinder | 6.95 s | 8.90 s | 9.00 s |
| Rime | 9.80 s | 16.05 s | 14.95 s |
| Tempest | 5.85 s | 11.10 s | 9.00 s |
| Terra | 11.40 s | 16.05 s | 16.30 s |
| Nyx | 6.40 s | 8.85 s | 9.90 s |

Pooled median TTK was 7.60 seconds at baseline and 11.35 seconds for each candidate. There were no 45-second timeouts. First-seat wins were 50.59% for Focus and 50.12% for Charged Casting.

Focus adaptive play scored 49.73% against its best naive threshold policy (95% CI 49.33–50.13%). Charged adaptive play scored 45.17% against its best fixed-charge policy (95% CI 44.77–45.57%). Observed charged-cast interruption was 3.2–5.0%, too rare to offset the fixed policy's payoff.

## Interpretation

This model disagreed with the primary model about the severity of charged-cast slowdown, so absolute pacing and timeout estimates are model-sensitive. It independently agreed on the decision-critical result: neither tested mechanic rewarded adaptive strategy. Focus is safer exactly as tested; Committed Casting has stronger active-counterplay hooks but needs a new tuning pass.
