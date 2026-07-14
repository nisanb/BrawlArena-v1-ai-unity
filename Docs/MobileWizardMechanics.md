# Mobile wizard mechanics implementation

Date: 2026-07-12

## Goal

Keep the first match understandable with one thumb per side, while giving skilled players readable aim, positioning, and defensive timing decisions. Avoid spell-resource starvation and long hold-to-charge interactions; both tested poorly in the earlier headless experiment.

## Recommended loop: Cast + Ward Step

### Basic cast

- One gesture produces one spell; holding does not auto-repeat.
- Tap for forgiving auto-aim.
- Drag and release for manual aim with a short trajectory preview.
- Keep the authored 0.32–0.47-second cast windups and slow movement to about 80% during the windup.
- Lock manual aim on release. Limit an auto-aimed spell's correction during the windup to roughly 12 degrees instead of perfectly re-facing the target.
- Basic casts never spend Ward Flow, so the primary action always works when its cooldown is ready.

This preserves a friendly tap option while manual aim, prediction, and cast commitment create the competitive ceiling.

### Ward Step

- Replace continuous Haste with a one-tap 2.75-metre step in the movement-stick direction.
- Cost: 20 of 60 Ward Flow, giving three steps from full.
- Duration: about 0.16 seconds, with no invulnerability.
- Regeneration: 8 Flow/second after 0.75 seconds without stepping.
- Normal movement and casting remain available at zero Flow.

This loop is implemented for the player and bots. Immediate obstruction rejects the step before spending Flow; accepted steps can be interrupted by knockback without a refund.

### Recovery and match length

- Dealing or receiving damage resets health recovery.
- Start recovery after 4 seconds and restore about 5–6% maximum health per second. The current recovery loop can restore roughly half of applied damage over a match and contributes to resets.
- Use 105-second regulation and first to 6 KOs.
- If tied, use 15-second first-KO overtime.

This slows individual decisions without making total mobile sessions longer.

## Thumb layout

- Floating movement joystick at bottom-left.
- Large Cast button at bottom-right.
- Ward Step and Ritual immediately above/inside the right-thumb arc.
- Fixed three-quarter combat camera. Remove the right-half camera drag zone, which currently competes with attack gestures.
- Keep one live HUD. Do not spawn the full sample `WizardHudFoundation` beneath it.

## Resource naming

The lobby's 60-point match-entry wallet and the combatant's 60-point mobility resource are separate systems. Label them explicitly as `BATTLE ENERGY` and `WARD FLOW`; never show both as an unexplained generic `ENERGY`.

## Implemented and verified

- One pointer hold produces zero casts; its release produces exactly one.
- Drag aim shows one world-space range guide and hides it on release.
- Basic projectile travel is capped at the same authored range shown by the guide.
- Auto-aim correction is capped at 12 degrees; manual aim stays committed.
- A valid step moves 2.75 metres and spends 20 Flow; an immediate wall spends zero.
- Player and bot capacity is fixed at 60, while progression can improve recharge only.
- The pre-match `FlowCanvas`, camera drag zone, and sample HUD foundation are absent/inactive during combat, leaving one interactive HUD layer.
- The first slice passed 11 focused EditMode/onboarding checks plus live match verification. Match duration, score target, and health-recovery retuning remain a later balance pass.

## Acceptance gates

- Ready basic-cast rejection rate: 0%.
- Engagement TTK: 15–30% above the current direct-combat baseline.
- Median match: 80–105 seconds; hard-cap finishes below 15%.
- Close finishes improve over the measured 39.9% headless baseline.
- At least 4/5 mobile playtest score for control clarity and 3.5/5 for responsiveness.
- Tap remains viable for new players; drag-cast and Ward Step timing create a measurable but non-dominant skill advantage.
