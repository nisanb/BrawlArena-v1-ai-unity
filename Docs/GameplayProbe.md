# Gameplay Probe

Editor-only kit for recording and reviewing live gameplay — attack feel,
weapon-in-hand placement, animation correctness — without touching the
user's mouse or keyboard. Built for AI-driven review loops: identical
scripted inputs make before/after visual diffs meaningful.

## Pieces

- `Assets/Scripts/Brawl/Diagnostics/ScriptedBrawlerDriver.cs` — drives one
  `BrawlerController` through a deterministic timeline using the same public
  intent API as `PlayerBrawlerInput` (`SetMoveInput`, `TryAttackAuto`,
  `TryAttackDirection`, `TrySuperAuto`, `TrySuperDirection`, `TryWardStep`).
  Wrapped in `#if UNITY_EDITOR`; never ships.
- `Assets/Scripts/Brawl/Diagnostics/GameplayProbeRecorder.cs` — renders three
  views per captured tick (hand/weapon closeup, side profile, main camera)
  into 4x4 contact-sheet PNGs, plus `frames.json` telemetry: subject/hand/
  weapon transforms, active animator clip names per layer, `CanAct`.
- `Assets/Editor/BrawlAutomation/GameplayProbe/GameplayProbe.cs` — orchestrator.
  While a match runs (autopilot is fine), takes over the player seat
  (`IsPlayer`, else Blue slot 0), suspends its `AIBrawler`, attaches driver +
  recorder, and restores AI control when the scenario finishes.

## Usage

1. Write a scenario JSON under `Automation/probe-scenarios/`:

```json
{
  "name": "shooting-basics",
  "steps": [
    { "at": 0.5, "action": "move", "dir": [0, 1], "duration": 1.5 },
    { "at": 2.2, "action": "attack_dir", "dir": [0, 1] },
    { "at": 6.2, "action": "ward_step", "dir": [1, 0] },
    { "at": 7.0, "action": "super_auto" },
    { "at": 8.6, "action": "stop" }
  ]
}
```

Actions: `move` (world XZ `dir` + `duration`), `stop`, `attack_auto`,
`attack_dir`, `super_auto`, `super_dir`, `ward_step`. Times are seconds from
takeover.

2. Start an autopilot match (write `Automation/autopilot.flag`, enter play in
   the Arena scene), then call
   `BrawlArena.EditorAutomation.GameplayProbe.Run("<scenario path>")`.
3. Output lands in `Automation/probe_<name>/`: `sheet-{closeup,side,main}-NNN.png`
   contact sheets, `frames.json`, `done.json`, `recording-complete.marker`.

## First findings (2026-07-16, Thorn as Blue slot 0)

- Upper-body animator layer plays `Idle@Pistol` while holding a bow.
- Basic attacks play `WeakAttack_UnarmedA` (unarmed swing) on the bow.
- Side view shows a giant arrow prop clipping horizontally through the
  torso/hood while moving; bow stays slung on the back during locomotion.
- Closeup camera framing is tighter than ideal; widen or pull back slightly.
