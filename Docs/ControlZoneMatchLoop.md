# Control Zone match loop

Control Zone is BrawlArena's primary match mode. Knockout and Gem Grab keep
their existing enum values and remain selectable secondary modes.

## Authoritative rules

- Teams: deterministic 3v3 roster rotation. The selected human is Blue slot 0.
- Regulation: 180 seconds in a 7-metre central zone; first to 90 points wins.
- Scoring: one point for each full uninterrupted second held by exactly one
  team. Empty, contested, controller-switch, match reset, and overtime entry
  discard partial progress.
- Expiry: a regulation lead wins. A tie enters sudden-death overtime.
- Overtime: the zone expands from 7 to 12 metres at 1 metre/second and clamps
  at 12. The first full uninterrupted uncontested second wins.
- Knockouts: never add Control Zone score. They create pressure through death
  and respawn only.

`MatchManager` owns match phase, time, scores, respawn selection, and victory.
`ControlZoneManager` samples living Brawl-owned actor positions and reports
whole scoring seconds. Generated rings are VFX-layer presentation with no
collider, damage, status, or other authority.

## Respawn and protection

- Control Zone respawn delay is exactly 6 seconds and ignores per-brawler
  secondary-mode multipliers.
- A brawler prefers its original primary slot. An occupied or reserved slot
  falls back deterministically within slots 0-2; slots 3-4 are secondary-mode
  authoring only.
- Respawn protection lasts 1.75 seconds. It blocks Brawl health damage,
  specialty statuses, knockback, and the related damage/Super telemetry.
- A protected living actor still counts for zone occupancy.
- Only an accepted basic attack or accepted Super cancels protection. Rejected
  auto aim, invalid direction, cooldown, uncharged Super, and Ward Step do not.
- Death, match reset, match end, and natural expiry clear protection and its
  code-generated ring. Respawn clears all spell statuses before revival.

## AI and HUD

Control Zone is the AI's primary non-retreat objective. Bots return to the
zone, spread across team tactical points, contest nearby enemies, and clamp
chases inside the authoritative radius. Retreat remains higher priority.

The HUD shows mode, regulation/OT state, score-to-90, zone controller,
overtime sudden death, respawn countdown, and spawn-protection countdown.

## Regeneration and validation

Use `ArenaSceneBuilder.RefreshControlZoneMatchLoopData()` for Task 3 scene
updates. It changes only MatchManager rule data, the ControlZoneManager
component, and the five authored spawn transforms per team; it does not run the
full Arena or menu builders.

Focused evidence is written to
`Temp/ControlZoneMatchLoopEditModeResults.xml`. The production proof instantiates
the exact Cinder Human and AI Invector prefabs in Play mode with physical input
and AI tactics held inert while it validates assembly, protection, occupancy,
respawn timing, spawn fallback, and KO non-scoring.
