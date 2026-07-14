# Phase 3D-B — Lifecycle Presentation and Failure Isolation

Status: complete for the generated, isolated human Cinder lab on 2026-07-13. Production
selection remains closed.

## Boundary Completed

Phase 3D-B makes every `IBrawlerAnimationDriver` call best-effort presentation and adds
project-owned Death, Respawn, and Victory states to the copied Invector FullBody graph.
It does not move health, death, respawn, match, physics, damage, ammo, equipment, input,
camera, or AI authority out of BrawlArena.

| Concern | Authoritative owner after this phase |
|---|---|
| KO, revive, invulnerability, match result | Brawl `Health`, `BrawlerController`, `MatchManager` |
| Motor stop, teleport, capsule configuration | selected `IBrawlerMotor` and Invector motor scheduler |
| Lifecycle animation request | `IBrawlerAnimationDriver` semantic API |
| Lifecycle graph write | `BrawlInvectorThirdPersonController` only |
| Lifecycle state observation | trace-only `BrawlInvectorLifecycleStateMarker` |
| Vendor health, `isDead`, ragdoll, revive | inert and unused |

Rollback remains every production roster entry selecting Legacy. The generated Cinder
prefab stays inactive, and `BrawlerCharacterAssembly.Resolve(BrawlerBackend.Invector)`
still fails before instantiation.

## Presentation Failure Containment

`BrawlerController` now routes all seven animation-driver calls through one nonthrowing
`TryPresent` boundary. A backend exception increments
`AnimationPresentationFailureCount` for the actor lifetime and retains
`LastAnimationPresentationFailure`, `LastAnimationPresentationFailureOperation`,
`LastAnimationPresentationFailureType`, and
`LastAnimationPresentationFailureMessage` for inspection; it does not escape into
gameplay.

The attack and Super iterators use `try/finally`, so their routine references and
`superInProgress` state clear even when presentation fails. Focused tests prove that a
throwing driver cannot:

- skip the authored basic/Super windup or damage;
- leave attack or Super ownership stuck;
- abort KO reporting, motor suspension, match-end evaluation, revive, teleport, or
  spawn protection;
- suppress the victory motor stop; or
- emit an unexpected warning/error while locomotion, hit reaction, or lifecycle calls
  are contained.

`MatchManager` treats each winner's victory presentation independently. It continues to
later winners and always emits the authoritative `MatchEnded` event. It records both an
exception that escapes a winner dispatch and an exception already contained by that
winner's `BrawlerController` through `VictoryPresentationFaultCount`,
`LastVictoryPresentationFaultActor`, and `LastVictoryPresentationFault`; `BeginMatch`
clears those per-match diagnostics.

## Project Lifecycle Graph

`InvectorMigrationPilotBuilder` copies the vendor combined controller once to:

`Assets/Generated/InvectorMigration/Cinder/Controllers/CinderInvectorPilot.controller`

The Cinder AnimatorOverrideController points at this project copy, never at a mutated
vendor asset. Removing the project overlay from its graph fingerprint leaves the exact
vendor topology. The copy retains all eight vendor layers and 44 parameters and adds
only three Trigger parameters:

- `BrawlDeath`
- `BrawlRespawn`
- `BrawlVictory`

The FullBody layer receives one child state machine named `BrawlLifecycle`:

| State | Motion | Entry | Exit |
|---|---|---|---|
| Death | pinned `Die.anim` | FullBody AnyState, `BrawlDeath`, fixed 0.10 s | none; holds the final pose |
| Respawn | null motion | FullBody AnyState, `BrawlRespawn`, fixed 0.05 s | Exit at normalized time 0.01, fixed 0.05 s |
| Victory | pinned `VictoryStart.anim` | FullBody AnyState, `BrawlVictory`, fixed 0.20 s | none; holds the final pose |

All three project AnyState entry transitions have zero offset, no self-transition, no
exit time, ordered interruption, and `TransitionInterruptionSource.None`. They precede the retained
vendor FullBody AnyState transitions without changing the vendor order or settings.
Each lifecycle state has exactly one trace-only project marker and no vendor lifecycle
tag or behaviour.

Respawn deliberately uses a small exit time. Unity ignores an unconditional state
transition that has neither a condition nor exit time; `exitTime = 0.01` makes the
motionless presentation state enter, emit both marker callbacks, and return to the
stable neutral FullBody state captured before the lifecycle sequence. The live test uses
that captured baseline instead of hardcoding `FullBody.Null`: the vendor root's neutral
default is not exposed as an ordinary child-state hash in every runtime query.

## Root Motion and Clip Safety

The pinned Death and Victory clips are Humanoid, nonlooping, `WrapMode.Once`, and have no
Animation Events. They retain exactly seven Humanoid Animator root-pose curve bindings
(`RootT.*` and `RootQ.*`). Those curves are real and must not be described as absent.

Safety comes from the combined gates:

- both clips keep original orientation, Y, and XZ baked;
- `m_HasGenericRootTransform` and `m_HasMotionFloatCurves` remain false;
- the generated Animator has `applyRootMotion = false`; and
- the live lifecycle test proves the actor root position, rotation, and scale remain
  within their pinned tolerances across Death, Respawn, and Victory.

The live test waits for the Invector motor's normal capsule stabilization before taking
its baseline. It pins the configured capsule height, thickness, offset, and step height,
then requires the actual collider to match the deterministic motor formula. That avoids
misattributing the controller's ordinary step-height settling to lifecycle animation.

## Runtime Request and Teardown Contract

`InvectorBrawlerAnimationDriver` implements all three lifecycle calls as nonthrowing
semantic requests. A closed presentation gate drops and counts a request without a graph
write. An open gate clears presentation-only combat flags, then asks the project
controller to set exactly one cached project Trigger hash. The driver catches and records
live faults without logging or invoking vendor lifecycle APIs.

The state marker only reports enter/exit traces to the driver; it writes no Animator,
health, Rigidbody, or collider state. On entry, it also acknowledges consumption of the
queued trigger.

Full presentation teardown clears weak, strong, recoil, reset, Death, Respawn, and
Victory triggers; normalizes AttackID and RecoilID; and clears every combat/lifecycle
pending trace. Lab deactivation closes the input scheduler before the driver and performs
exactly one full reset. Reopening in buffered-motor mode for 30 fixed frames produces no
deferred lifecycle state.

## Live Evidence

The dedicated lifecycle UnityTest proves, in the generated lab:

- Death enters and holds; Respawn enters/exits to the captured neutral FullBody baseline;
  Victory enters and holds;
- semantic traces match the expected three requests, three controller writes, three
  state entries, and two exits (Death is interrupted by Respawn; Respawn exits; Victory
  holds);
- Death clears an already queued Super and recoil before entering;
- vendor `currentHealth`, `isDead`, Animator `isDead`, and internal motor stamina are
  unchanged;
- Rigidbody settings, controller capsule configuration, resolved capsule, transform,
  ammo signature, shooter state, and melee-firewall counters remain inert;
- immediate gate close clears all lifecycle/combat residue in one reset;
- buffered reopen receives no deferred lifecycle request; and
- teardown restores the inactive, kinematic, capsule-disabled action-feed posture with
  zero scoped warnings/errors.

Static source guards additionally forbid the lifecycle request, controller-write, and
marker paths from reaching vendor health/death/ragdoll, `ActionState`, raw graph names,
`CrossFade`, Rigidbody, or collider mutation.

## Generated-Asset Evidence

Two consecutive builder runs retained the intended topology and these stable assets:

| Asset | GUID | SHA-256 |
|---|---|---|
| Vendor combined controller (unchanged) | `87885946b43e2d1449e1d5aa2042f8a8` | `A0B3339053D5278532CA15C1EE95630E1B17DF5AA63C311B328B6E9E1CFC2980` |
| Project lifecycle controller | `fc4af6f51fba2cf4b9391c6152e391a9` | `8264C753920F23E905C0612C4036DED29C1F806760AF83C80DA43631E7AE07B6` |
| Cinder AnimatorOverrideController | `073c25839c579d54e95b3afd2edd8f13` | `D408EE539D9B1AC8F9F98C28AF798FABCE6BE0A5AF00B69FF8FFF8C03D48B2E7` |
| Cinder pilot prefab | `12b31ba25708e04489318559f291b366` | `B4928F206DCB1A7088E929DEC0D459EA9F91F129FFD5F140D27C947D3ABB82B9` |
| Migration lab scene | `91742eec95d6352499529bebc9dedea0` | semantic topology validated; raw YAML bytes are not a determinism gate |

The pinned clips are:

- `Die.anim`: GUID `0cf8fc0f929385941b5832a35cb74630`, SHA-256
  `5F7600B301611BF7EB34F90C2286BAC6ED2DC5020FE8E9C8D2837208F037D923`;
- `VictoryStart.anim`: GUID `e4715cf696aba3649b0e9624be8bbc1f`, SHA-256
  `DBF17797729016233C808F5CE647F17FE4366F1485078076B83DACB528FEB5D7`.

## Test Evidence

All results were launched through the dirty-scene-safe recorder and preserved the
caller's active dirty `Assets/Scenes/MainMenu.unity`:

| Gate | Result | Duration | XML SHA-256 |
|---|---:|---:|---|
| Focused pilot/static graph | 9/9 | 1.1144709 s | `06953FB33648B2345EDFCE48ADB93FDF1C6921B26C70862C8F3FCCBE494CA4D2` |
| Phase 3D-B lifecycle live | 1/1 | 4.5584311 s | `1830DC26AE9E4E15C7EFCDB199606FC7BE725708B88767094BEC6D3CAF7B7E69` |
| Phase 3B action-feed compatibility | 1/1 | 4.3266055 s | `0F9F0A923E9A19A91139212EDBF76C31BEA9E2B3F0EAB368FA3B150F9ADCBE89` |
| Phase 3C-C buffered compatibility | 1/1 | 2.6870975 s | `8FCC683FF5A402CF1705FD08B8E2D8772658166A0D368AD02C9DCC6242C13399` |
| Complete EditMode regression | 196/196 | 13.1330212 s | `16196BEB9630C03BC776598348128A25BB602FC865CE265A1E62D2429581920C` |

The final recorder state is empty, MainMenu is still loaded and dirty, and the Unity
Console reports zero warnings/errors. The original MainMenu disk SHA-256 remains
`E42BCAC8EFB4FA02A8A6F7AB03132F5F5A1FE1EEE55A138D4AA06B2022CE74C7`.

## Nonclaims and Next Slice

At this phase cutoff, the result did not prove a production `BrawlerController`/`Health`
actor running the Invector backend, a production assembler/selector, weapon art or
sockets, muzzle events, shooter IK, selective collision proxies, Arena/MainMenu scene
flow, AI motion, menu or portrait previews, or any roster entry beyond the isolated
Cinder artifact.

Phase 3D-C subsequently completed the visual-only weapon/IK and selective hit-proxy/
layer boundary while retaining Brawl gameplay authority; see
`Phase3D-C-WeaponIKSelectiveCollision.md`. The next bounded phase is the context-gated
production human Cinder assembler. AI and every remaining roster entry stay Legacy.
