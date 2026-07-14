# Phase 3C-B - Legacy Navigation Extraction

Status: complete and validated on 2026-07-13  
Production backend: Legacy; `BrawlerBackend.Invector` still fails before instantiation  
Gameplay change: none intended

## Outcome

`AIBrawler` retains every tactical decision, but no longer imports `UnityEngine.AI`,
discovers a `NavMeshAgent`, writes the actor transform, or issues a concrete NavMesh
operation. One same-root component-backed `IBrawlerNavigation` owns path planning and
feeds desired world velocity into the already-selected `IBrawlerMotor` through the
existing `BrawlerController.SetMoveInput` buffer.

`Awake` discovers existing navigation components without installing a fallback. This
keeps the documented selection window open for an active actor to install and select a
custom same-root planner before `Start`. At `Start`, an absent selection receives exactly
one `LegacyBrawlerNavigation`; the selected source is initialized, serialized through a
`MonoBehaviour` reference for reload restoration, and then locked. Null, non-component,
cross-root, and second-owner selections fail closed.

## Contract and Routed Authority

`IBrawlerNavigation` contains only the audited planning surface:

- readiness, path state, and desired world velocity queries;
- speed/stopping-distance initialization;
- sampled destinations;
- destination requests and path clearing;
- handoff of rotation ownership while combat-facing a target.

`AIBrawler` still owns target selection, melee/ranged spacing, retreat, kite/flank,
tactical Ward Step, Gem Grab, XP-box decisions, attack timing, and Super use. It routes
all four destination branches, both NavMesh sample branches, path clearing, and external
facing through the selected navigator. It routes target facing through `IBrawlerMotor`.

The navigation desired velocity is flattened to the arena plane and divided by the
facade's current speed before clamping to unit magnitude. This preserves the agent's
arrival-speed magnitude instead of converting every nonzero path request into full-speed
intent. Physical movement, Ward Step, knockback, stop/suspend, and teleport remain owned
by `IBrawlerMotor`.

## Legacy Backend Semantics

`LegacyBrawlerNavigation` wraps only NavMesh planning:

- it configures speed, acceleration, angular speed, stopping distance, and leaves
  `NavMeshAgent.updatePosition = true`, preserving the Legacy agent as the AI transform
  authority;
- readiness requires an enabled component, enabled agent, `isOnNavMesh`, and an actual
  polygon sampled at `agent.nextPosition`; the final polygon check closes a Unity window
  where `isOnNavMesh` can remain stale after its runtime `NavMeshData` is removed;
- sample, destination, path-state, desired-velocity, and clear operations fail closed
  while unavailable or off-mesh;
- `SetExternalFacing` toggles `updateRotation` for an enabled agent even while off-mesh,
  preserving the prior facing handoff rather than waiting for a valid path;
- it never calls `Move`, `Warp`, changes agent enabled state, or writes a transform.

`LegacyBrawlerMotor` remains the physical/lifecycle backend. Its agent-aware facing path
uses the prior AI turn rate of 12 per second; human facing remains 14 per second. The
Legacy assembler installs the navigator before `AIBrawler`, explicitly selects it, and
therefore has one deterministic owner without relying on fallback timing.

## Files

- `Assets/Scripts/Brawl/Integration/IBrawlerNavigation.cs`
- `Assets/Scripts/Brawl/Integration/Legacy/LegacyBrawlerNavigation.cs`
- `Assets/Scripts/Brawl/Integration/Legacy/LegacyBrawlerMotor.cs`
- `Assets/Scripts/Brawl/Integration/Legacy/LegacyBrawlerCharacterAssembler.cs`
- `Assets/Scripts/Brawl/AIBrawler.cs`
- `Assets/Editor/BrawlAutomation/BrawlerLegacyNavigationEditModeTests.cs`
- `Assets/Editor/BrawlAutomation/LegacyBrawlerNavigationRuntimeTests.cs`
- `Assets/Editor/BrawlAutomation/BrawlerLegacyBackendEditModeTests.cs`
- `Assets/Editor/BrawlAutomation/InvectorMigrationPhase3BPlayModeTests.cs`

## Validation Evidence

Focused Legacy navigation fixture after independent review and the active-object
selection regression:

- 9 passed, 0 failed, 0 skipped/inconclusive;
- duration: 0.6305131 seconds;
- result: `Temp/Phase3CBLegacyNavigationEditModeResults.xml`;
- SHA-256: `AC0D36E58FE3A66D52624E8BBBB2BE306550512E9765E42A1090D24168E10ADA`.

The dirty-scene-safe complete run also executed one Play-mode UnityTest against a
NavMesh built only in memory. It passed in 14.915495 seconds and proved sampled
destination requests, non-unit desired-velocity passthrough, path clearing, slowed
arrival magnitude, actual actor-transform movement to the configured stopping distance,
`updatePosition = true`, disabled-agent fail-close, teleport/re-enable recovery, and
fail-close after removing the runtime NavMesh.

The Phase 3C-A motor fixture also re-passed 9/9 after the navigation extraction. The
final complete EditMode regression, including the live navigation proof and the new
Phase 3C-C motor-core fixture, passed:

- 181 passed, 0 failed, 0 skipped/inconclusive;
- recorded test-run duration: 4.1039228 seconds;
- result: `Temp/BrawlArenaFullEditModeResults.xml`;
- SHA-256: `DC274A4DCA91D8EC4682355F87F331F31ECA445D7569A92F196404DD2DD582BA`.

Unity compiled with zero errors. The final Console contained zero warnings or errors,
the editor was outside Play mode, and the original dirty
`Assets/Scenes/MainMenu.unity` remained loaded, dirty, and unsaved. The repository audit
still reports Unity 6000.3.7f1, Invector Shooter Template 2.6.6, Input System only, the
approved layer state, and absence of the known P001/P002 source patterns.

## Deliberate Residual Risks

The focused fixture proves the contract, ownership, initialization, fallback, source
routing, configuration, and intent magnitude with recording components. The in-memory
runtime fixture adds real NavMesh planning, movement, arrival, availability, and
fail-closed evidence without loading or changing a scene asset. It deliberately does not
claim:

- the baked Arena NavMesh or its authored areas/topology;
- `AIBrawler` -> facade -> `Health` death/respawn lifecycle integration;
- end-to-end tactical desired velocity entering the facade's buffered motor intent with
  production script ordering;
- OffMeshLink traversal or stuck recovery;
- knockback or Ward Step interleavings while a path is active;
- a real editor domain reload restoring the serialized navigation owner.

These gaps do not reopen the completed Legacy extraction, but they block an Invector AI
production claim. Cinder AI and every production roster entry remain Legacy.

## Rollback and Next Boundary

Rollback remains `BrawlerBackend.Legacy`; no roster, generated Invector artifact,
production selector, scene, or project setting changed in this slice. The Legacy
navigator remains the tested planner for all production AI.

Phase 3C-C subsequently completed the isolated human `InvectorBrawlerMotor` buffered
adapter/builder/live-lab bridge; see
`Docs/InvectorMigration/Phase3C-C-InvectorMotorCore.md`. It did not add a production
selector/assembler or an Invector AI navigator. A later AI bridge must not read
HUD/InputAction state, add a CharacterController, or allow a NavMeshAgent to update an
Invector Rigidbody transform. Phase 3D-A subsequently completed the isolated attack/
reload and melee-window firewall, and Phase 3D-B completed isolated lifecycle
presentation/failure containment. Phase 3D-C subsequently completed isolated weapon/IK
and selective collision handling. Production human assembly is the next gate; AI remains
Phase 8. See `Docs/InvectorMigration/Phase3D-C-WeaponIKSelectiveCollision.md`.
