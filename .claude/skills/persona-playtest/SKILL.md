---
name: persona-playtest
description: Run the customer-persona playtest loop - capture live-match evidence, have 3 player personas review and score it, turn findings into fix orders, then re-review for a measured before/after. Use when the owner asks how the game feels, wants player feedback, says gameplay/UX is lacking, or after any significant gameplay change.
---

# Persona playtest loop

A repeatable evidence -> review -> fix -> re-review cycle. It converted "gameplay feels lacking"
into scored, evidence-cited findings twice (round 1: 3/10 average "would uninstall";
round 2: 6-7/10 "keep playing").

## 1. Capture evidence

Run `Automation/capture-review-evidence.ps1 -RunName <name>` in the background (~5 min).
It drives the harness through `play_test`, writes 8 timed player-camera screenshots +
`status` dumps into `Automation/review-evidence/<name>/`, then exits play. If the script
is missing, recreate it: loop { status -> save message; game_screenshot with arg
`review-evidence/<name>/shot-NN.png`; sleep ~18s } x8 between `play_test` and `exit_play`.

Sanity-read one screenshot yourself before spending persona tokens.

## 2. Persona reviews (3 parallel background agents, weaker model)

Write one shared brief file (evidence paths, game rules so reviews are informed, output
format demanding: verdict, 0-10 scores per axis, findings ranked by severity with exact
file citations). Personas that worked:
- **Riley** — competitive mobile-arena veteran; judges skill expression, TTK fairness, counter-picking, camera information.
- **Sam** — casual commuter; judges first-minute clarity, trust signals ("asset flip?" tells), readability.
- **Dana** — game-feel/animation designer (Vlambeer/Sakurai school); judges anticipation/contact/follow-through, VFX hierarchy, camera composition, art coherence; may read source to verify suspicions.

Instruct: tie every complaint to a named evidence file; describe experience gaps, not code fixes.

## 3. Act, then re-review

Convert findings + owner complaints into work orders with exclusive file ownership
(see parallel-work-orders skill). After fixes: compile, full EditMode suite, rebuild scenes,
capture a fresh run, then re-run the SAME personas.

## Pitfalls

- Persona agents usually cannot be resumed in a later pass — launch fresh agents and quote each persona's prior scores/findings inline so they report `round1 -> round2` deltas.
- Under autopilot there is no PlayerBrawlerInput; before the spectate-binding fix the HUD showed "--/--" — personas treat artifacts like that as game bugs. Keep evidence-capture artifacts fixed or disclosed in the brief.
- Tell re-reviewers "don't grade on a curve; say what improved, what didn't, what regressed."
- Status `anim=` shows overlay layers as `Base+Overlay` (e.g. `Idle+Die`); older captures without this misled reviewers into "no animations exist".
