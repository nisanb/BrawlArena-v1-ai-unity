---
name: motion-review
description: Capture and review animation/game-feel/3D-ness evidence in motion (not stills) using the GameplayProbe flipbook recorder plus a live render-rig dump. Use when the owner complains about animation quality, "feels 2D/flat", camera feel, or when verifying any presentation change actually reads in motion.
---

# Motion review (flipbook probe)

Stills cannot judge motion; the GameplayProbe kit records the SAME scripted gameplay
every run, giving true before/after comparisons.

## Capture

1. Harness: `open_scene` `Assets/Scenes/Arena.unity` — the probe REQUIRES Arena's GameFlow
   in Edit Mode to resolve the scenario's rosterId; it fails otherwise.
2. Harness: `play_gameplay_probe` with arg = absolute path to a scenario JSON
   (canonical: `Automation/probe-scenarios/motion-review.json` — 13.4s Bastion script:
   runs all 4 directions incl. toward camera, strafes, dash, directional attacks, super, stop).
3. Wait for `Automation/probe_<scenarioName>/done.json` (~2-3 min). Output: `sheet-main-NNN.png`
   (4x4 chronological strips from the real game camera, ~0.1s/cell), `sheet-front/side-*.png`
   (locked character cameras — best for animation judging), ordered frames dirs, `frames.json`
   (per-tick animClips/positions/yaw/weapon grips).
4. While the match is live, dump the render rig via MCP RunCommand (cameras, lights, ambient,
   fog, URP asset, post volume components) — menu scene has a different rig than Arena;
   only the in-match dump counts.
5. To preserve a "before": rename `probe_<name>` before re-running the identical scenario.

## Review

Fan out two specialist agents: (a) animation-in-motion (cell-level citations: gliding, turn
anticipation, swing arc phases, dash reading, idle life, transition pops; cross-check
frames.json — e.g. bit-identical subjectPos[1] across all ticks proved zero root bob);
(b) 3D-ness/tech-art auditor (shadow rendering, light azimuth vs camera azimuth, pitch/FOV/
horizon-in-frame, depth cues), producing a ranked mobile-safe plan.

## Pitfalls (all hit in practice)

- Melee heroes never pass the probe's strict wand/bow IK pose gate — scenarios must set
  `"relaxedPresentationGate": true` (field on ScriptedBrawlerDriver.ProbeScenario).
- `super_auto` silently no-ops in a fresh match (zero super charge; TrySuper returns false).
  Don't conclude "super has no animation" from a probe unless charge was granted first.
- `frames.json` subjectPos samples the GAMEPLAY root; BrawlerMotionFlourish animates the
  visual child, so procedural bob/lean never appears in telemetry — judge it from sheets.
- Scene-serialized values beat code defaults: BuildCamera once hard-coded the camera offset,
  silently discarding a BrawlCamera default change. After any rig change, verify the LIVE
  values via the in-match dump, not the source code.
- The `screenshot_scene` overview tool renders the arena as floating tiles in a void —
  known capture-tool quirk; trust in-match frames for map judgments.
- Blob/quad helpers under a brawler must never come from GameObject.CreatePrimitive: its
  MeshCollider's deferred Destroy still hits one physics sync under the dynamic Rigidbody
  and PhysX errors every spawn. Build meshes by hand.
