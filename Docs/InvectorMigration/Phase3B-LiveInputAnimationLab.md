# Phase 3B — Isolated Live Input and Animation Lab

Status: complete and validated on 2026-07-13  
Package: Invector Third Person Controller Shooter Template 2.6.6  
Production backend: closed; every roster entry remains Legacy

## Outcome

Phase 3B activates one generated Cinder/Invector instance only inside
`Assets/Scenes/InvectorMigrationLab.unity`. The prefab asset and serialized scene
instance remain inactive. A scene-owned gate configures, activates, continuously audits,
and deterministically deactivates the instance during Play mode.

The live slice proves that BrawlArena can drive Invector locomotion and the complete
combined Animator graph without enabling Invector health, damage, inventory, equipment,
projectiles, template camera, HUD, AI, or production assembly.

## Implemented Runtime Boundary

- `InvectorShooterMeleeInputAdapter` is the only `vThirdPersonInput`. It reads
  `Player/Move` from the project Input Action asset once per `Update` and owns the only
  `FixedUpdate` motor/locomotion/rotation/Animator schedule.
- The current lab `MoveInput` checks the project `BrawlHUD` singleton before reading the
  action, but the exact generated lab contains no BrawlHUD, so this does not create a
  second live source there. Phase 3C-C later added a separate `BufferedMotor` lab mode
  that returns before both reads. It is not selected on a production actor;
  `PlayerBrawlerInput` remains the sole production reader.
- Unity 6 project-wide actions may already be enabled by the project lifecycle. The
  adapter treats that as an approved external lifecycle, not as a second reader. It
  disables the action on shutdown only when it enabled the action itself.
- `BrawlInvectorThirdPersonController` wraps and traces `UpdateMotor`, locomotion,
  rotation, and `UpdateAnimator`, pins its internal motor stamina, and initializes the
  eight-layer Animator state-listener registry without starting vendor health recovery.
- `InvectorBrawlerAnimationDriver` exposes guarded semantic basic, Super, and hit-reaction
  requests. Brawl damage, target selection, cooldowns, resources, and telemetry remain
  authoritative.
- `InvectorPhase3BLabController` enforces exact topology, same-scene `BrawlCamera`, root
  motion off, one dynamic Rigidbody/capsule, disabled melee/shooter managers, fixed-step
  parity, inert health/stamina, exact recoil-state observation, and fail-closed teardown.

The approved recoil states are:

- `FullBody.Hit Recoil.recoil_hard`
- `FullBody.Hit Recoil.recoil_low`
- `FullBody.Hit Recoil.recoil_unarmed`

The lab gate observes the exact current or next full-path hash; an arbitrary Animator
state change is not accepted as recoil evidence.

## Generated Lab

`InvectorMigrationPilotBuilder.BuildPilotAssets()` owns the complete output:

- `Assets/Generated/InvectorMigration/Cinder/Controllers/CinderInvectorPilot.overrideController`
- `Assets/Generated/InvectorMigration/Cinder/Prefabs/CinderInvectorPilot.prefab`
- `Assets/Scenes/InvectorMigrationLab.unity`

The scene contains exactly one BrawlCamera/Camera/AudioListener, Ground, a Step, a Slope,
a WorldBlocker collision wall, one inactive pilot instance, and one live gate. The
builder now preserves the caller's active-scene dirty state on success or failure.

Two final rebuilds preserved these output GUIDs:

- override controller: `073c25839c579d54e95b3afd2edd8f13`
- prefab: `12b31ba25708e04489318559f291b366`
- scene: `91742eec95d6352499529bebc9dedea0`

The controller and prefab were byte-stable at SHA-256
`08A30491109E5126B6C1F55F721A6094D445B005686099A4AFDE49D05A3DD4E6` and
`C028F6D75183A11A059BE44270E7908A295B2369C32A456B5F07100991BC8C3D`.
Unity regenerates local scene object IDs and YAML document order, so the scene's raw byte
hash is not a determinism gate. Stable GUIDs plus the focused topology/reference suite
are the scene gate.

## Automated Evidence

The reusable safe test recorder temporarily clears only the dirty marker—not scene
content—before a Test Runner request, then restores the original dirty state after the
original scene is loaded and Edit mode resumes. It supports focused, live, and complete
EditMode runs.

Final results:

- focused generated-asset suite: 8/8 passed, 0 skipped, 0 failed;
- isolated live lab: 1/1 passed, 0 skipped, 0 failed;
- complete EditMode regression: 157/157 passed, 0 skipped, 0 failed;
- final Unity Console: 0 warnings and 0 errors;
- original `MainMenu` dirty state: preserved.

Result files:

- `Temp/InvectorMigrationPilotEditModeResults.xml`
- `Temp/InvectorMigrationPhase3BEditModeResults.xml`
- `Temp/BrawlArenaFullEditModeResults.xml`

The live test enters Play mode, synchronously loads only the migration scene, drives a
virtual gamepad through `Player/Move`, proves movement and neutral input, dispatches and
releases basic/Super/recoil semantics, checks all scheduler counters for equality,
checks the eight-layer listener registry, proves health and stamina remain inert, and
verifies dormant teardown including Input Action lifecycle restoration.

Manual Unity evidence also exercised the generated course: forward motion travelled
about 4.68 m, climbed the Step, and stopped against the collision wall near `z = 5.15`.
Pausing held position and all fixed-scheduler counters stable; releasing input while
paused prevented a resume burst. These observations supplement, but do not replace, the
automated authority and teardown assertions.

## Settings Rebaseline

Two final settings scans were byte-identical:

- 435 candidate files; 432 Unity YAML files; 3 listed non-YAML files;
- 9,383 GameObject layer assignments; 14,953 mask/bitfield findings;
- 8,084,377 bytes;
- SHA-256 `D9EF22D949E6DDCB2A6468F289B0DA54F921CF47AFC8B4EAA6D9C0C6477553E9`.

All approved ProjectSettings hashes match the Phase 0B manifest. Custom tags remain
empty, layers 13 and 15 remain unnamed, layer 23 remains `InvectorPlayer`, Active Input
Handling remains Input System Package only, and no Invector settings package was
imported. The count increase is exactly the three course objects plus their primitive
renderer/collider fields and the BrawlCamera obstruction mask.

## Teardown Contract

Shutdown is idempotent and fail-closed:

1. close semantic presentation requests;
2. close the scheduler and release only adapter-owned Input Action state;
3. clear movement and velocities;
4. disable controller, Animator, and capsule;
5. restore a kinematic, gravity-off, discrete, FreezeAll Rigidbody;
6. deactivate the pilot root.

`OnDisable`, `OnDestroy`, explicit deactivation, activation failure, and continuous-audit
failure all converge on this dormant state.

## Boundary for the Next Slices

Phase 3B did not authorize production selection. At that cutoff, the dependency audit found direct
`CharacterController`/`NavMeshAgent` coupling throughout `BrawlerController` and
`AIBrawler`, recursive child-layer flattening, and unsupported death/respawn/victory
methods that can abort KO, respawn, or match-end flow.

Phase 3C-A subsequently extracted and proved a Legacy-parity `IBrawlerMotor`; see
`Phase3C-A-LegacyMotor.md`. Phase 3C-B subsequently extracted Legacy navigation as a
separate planner seam; see `Phase3C-B-LegacyNavigation.md`. `PlayerBrawlerInput` remains
the sole production gesture/identity reader. Phase 3C-C subsequently completed the
isolated human buffered motor/adapter/builder/live-lab bridge; see
`Phase3C-C-InvectorMotorCore.md`. That result adds no production selector/assembler,
live `BrawlerController`/`PlayerBrawlerInput`/`Health` lifecycle, or Invector AI
navigation. Production selection stays closed.

Phase 3D-A subsequently removed the strong-attack/reload coupling and installed the
terminal melee-window firewall; see `Phase3D-A-MeleePresentationFirewall.md`. Phase 3D-B
then added nonthrowing project Death/Respawn/Victory states and presentation-failure
isolation; see `Phase3D-B-LifecyclePresentation.md`. Phase 3D-C subsequently completed
visual-only weapon/IK presentation and selective collision handling in the same isolated
lab; see `Phase3D-C-WeaponIKSelectiveCollision.md`. Production assembly and AI remain
open. Brawl `Health`, deterministic hit resolution, projectiles, resources, team rules,
statuses, and telemetry remain authoritative.
