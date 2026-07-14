# Phase 3C-C - Isolated Invector Buffered Motor Lab (Complete)

Status: complete and validated only for the generated isolated human Cinder lab  
Evidence point: 2026-07-13  
Production backend: Legacy; `BrawlerBackend.Invector` still fails before instantiation  
Gameplay change: none intended

## Proven Boundary

`InvectorBrawlerMotor` is the same-root component-backed `IBrawlerMotor` for the
project-owned Invector stack. It buffers Brawl-produced world movement, facing, Ward
Step/knockback displacement, stop, and teleport requests without declaring `Update`,
`FixedUpdate`, `LateUpdate`, or `OnAnimatorMove`. The one audited
`InvectorShooterMeleeInputAdapter` remains the only fixed scheduler.

The adapter now has two explicit, dormant-only feed selections:

- `LabProjectAction` remains the serialized default and preserves the completed Phase 3B
  action-driven lab path.
- `BufferedMotor` consumes only world intent already stored by `InvectorBrawlerMotor`.
  Its `Update`, `InputHandle`, and `MoveInput` paths return before HUD or Input Action
  access, and its rotation path consumes world direction without a camera reference.

For each accepted buffered fixed frame, the adapter calls motor prepare, executes its
single inherited `base.FixedUpdate()` scheduler, and completes the motor step from
`finally`. Preparation rolls back an incomplete open step, and scheduler exceptions stop
the dynamic body and fail the runtime gate closed. Because locked physical speed causes
the vendor locomotion method to skip its speed helpers, motor preparation explicitly
populates the controller locomotion scalars while the inherited scheduler remains the
only Animator writer.

`InvectorMigrationPilotBuilder` installs exactly one disabled motor on the inactive
generated root, configures reciprocal same-root motor/adapter references, and leaves
`LabProjectAction` selected. `InvectorPhase3BLabController` can reopen that same generated
instance on `BufferedMotor`, initialize the motor before opening the scheduler, validate
zero adapter-owned physical reads, and tear down in this order: close scheduler, return
the motor dormant, restore `LabProjectAction`, then freeze and deactivate the pilot.

## Validated Semantics

The combined EditMode and isolated live evidence covers:

- exact same-root controller/scheduler/Rigidbody/Capsule configuration and rejection of
  competing physical authorities;
- magnitude-preserving planar world-intent buffering and requested-speed scaling;
- locomotion scalar population while `lockSetMoveSpeed` preserves exact Brawl speed;
- deferred/immediate facing with restoration of pre-existing rotation locks;
- nested external displacement, sweep constraints, final-delta end-pending ordering,
  stop/suspension, teleport cleanup, and restoration of prior controller configuration;
- equality among adapter scheduler start/complete, motor prepare/complete, vendor motor,
  locomotion, rotation, and Animator update counts;
- zero buffered-path HUD/Input Action reads, no adapter ownership of a project-wide
  action, no external fixed subscriber, and no suppressed vendor path;
- Phase 3B action-feed compatibility after the motor and buffered mode were added;
- inert vendor health/stamina plus disabled weapon/damage managers; and
- deterministic return to one inactive, disabled, kinematic, frozen, action-feed pilot.

## Evidence

Focused buffered live gate:

- result: `Temp/Phase3CCBufferedMotorEditModeResults.xml`;
- 1 passed, 0 failed, 0 skipped/inconclusive;
- duration: 4.932449 seconds;
- SHA-256: `CB536CF6B2FC6B50A29BB904690A21D58571BA16EAD3881D8C75344544FB9E90`.

Phase 3B compatibility gate:

- result: `Temp/InvectorMigrationPhase3BEditModeResults.xml`;
- 1 passed, 0 failed, 0 skipped/inconclusive;
- duration: 7.2930701 seconds;
- SHA-256: `ABEA42DFE8B64B3D9FE21F7FAA71555675F62E2F72832A97BE266507B4C7D379`.

Final complete regression:

- result: `Temp/BrawlArenaFullEditModeResults.xml`;
- 187 passed, 0 failed, 0 skipped/inconclusive;
- duration: 3.4877173 seconds;
- SHA-256: `72CBA5984CA03AEE2A03FFBB144E5044A47E507AFF774596CCE8E7D0A7B9B748`;
- Unity Console: zero warnings and zero errors;
- caller state: MainMenu remained loaded and dirty.

Post-closure second-output structural gate:

- result: `Temp/InvectorMigrationPilotEditModeResults.xml`;
- 8 passed, 0 failed, 0 skipped/inconclusive;
- duration: 0.9514211 seconds;
- SHA-256: `1E0BAA224D55659976A7A30DF145BC2AB302DC852E93D0D5AFF136A2B35B94A9`;
- Unity Console: zero warnings and zero errors;
- caller state: MainMenu remained active and dirty.

The full-run fence also hardened the existing Legacy NavMesh clear-path test. Unity may
clear `hasPath` before its sampled `desiredVelocity` converges on a later rendered frame,
so the UnityTest now uses a bounded frame wait instead of treating one fixed boundary as
a NavMesh update fence. Production `ResetPath` and exact desired-velocity passthrough were
not changed.

## Generated Outputs

Two consecutive post-closure builder runs were idempotent. Both runs produced equal
controller and prefab bytes plus an equal semantic scene-topology signature:

- AnimatorOverrideController SHA-256:
  `08A30491109E5126B6C1F55F721A6094D445B005686099A4AFDE49D05A3DD4E6`;
- dormant pilot prefab SHA-256:
  `828FF5849C21C22589FEBEFFDE556D35379464A7BEBF45AAE74BC8E544AA099C`;
- isolated lab semantic topology SHA-256:
  `832FE93A955F5D12E14BB14559142841C16A1B6D8BB66DABD7DEC066CC2F0644`.

Both runs also retained these GUIDs:

- AnimatorOverrideController: `073c25839c579d54e95b3afd2edd8f13`;
- dormant pilot prefab: `12b31ba25708e04489318559f291b366`;
- isolated lab scene: `91742eec95d6352499529bebc9dedea0`.

The verified generated topology contains exactly one dormant
`InvectorShooterMeleeInputAdapter`, exactly one dormant `InvectorBrawlerMotor`, reciprocal
same-root references, and no `LegacyBrawlerMotor`. The prefab asset itself remains
inactive and every production roster definition remains Legacy.

## Files in the Completed Slice

- `Assets/Scripts/Brawl/Integration/Invector/InvectorBrawlerMotor.cs`
- `Assets/Scripts/Brawl/Integration/Invector/InvectorShooterMeleeInputAdapter.cs`
- `Assets/Scripts/Brawl/Integration/Invector/InvectorPhase3BLabController.cs`
- `Assets/Editor/BrawlAutomation/InvectorMigrationPilotBuilder.cs`
- `Assets/Editor/BrawlAutomation/InvectorBrawlerMotorEditModeTests.cs`
- `Assets/Editor/BrawlAutomation/InvectorMigrationPilotEditModeTests.cs`
- `Assets/Editor/BrawlAutomation/InvectorMigrationPhase3BPlayModeTests.cs`
- `Assets/Generated/InvectorMigration/Cinder/Controllers/CinderInvectorPilot.overrideController`
- `Assets/Generated/InvectorMigration/Cinder/Prefabs/CinderInvectorPilot.prefab`
- `Assets/Scenes/InvectorMigrationLab.unity`

## Explicit Nonclaims and Rollback

This completion does not provide an Invector production selector or assembler. It does
not run a live `BrawlerController`, `PlayerBrawlerInput`, or Brawl `Health` on the pilot;
does not prove death, respawn, victory, production pause/lifecycle ordering, or
MainMenu-to-Arena spawning; and does not implement Invector AI navigation. Production
human and AI actors, including Cinder, remain on the Legacy assembler.

Phase 3D-A subsequently completed the isolated attack/reload and melee-window firewall;
see `Phase3D-A-MeleePresentationFirewall.md`. Phase 3D-B then completed isolated
lifecycle-safe semantics and presentation-failure isolation; see
`Phase3D-B-LifecyclePresentation.md`. Phase 3D-C subsequently completed weapon/IK
presentation and selective collision in the isolated lab; see
`Phase3D-C-WeaponIKSelectiveCollision.md`. The context-gated production human assembler
is now the next blocker. AI desired-velocity translation into an Invector Rigidbody
remains Phase 8.

Rollback is to leave every roster Legacy and keep the generated prefab inactive. The
isolated motor/adapter feed can be removed or disabled and regenerated without any
production selector, Arena scene, Health, input-gesture, or project-settings rollback.
