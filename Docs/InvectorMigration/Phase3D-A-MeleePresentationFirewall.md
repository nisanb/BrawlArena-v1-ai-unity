# Phase 3D-A — Attack/Reload and Melee Presentation Firewall

Status: completed for the generated, isolated human Cinder migration lab on 2026-07-13.

This slice removes Invector combat side effects from the three presentation semantics
already exercised by the lab. It does not implement lifecycle, equipment art, shooter
IK, a production assembler, or AI.

## Ownership Result

- `InvectorShooterMeleeInputAdapter` retains one vendor base call: the inherited
  `FixedUpdate` motor/Animator scheduler. Weak attack, strong attack, and recoil call no
  vendor combat base.
- `BrawlInvectorThirdPersonController` is the sole raw graph-write boundary for
  `AttackID`, `WeakAttack`, `StrongAttack`, `RecoilID`, `TriggerRecoil`, and
  `ResetState`. It uses `vAnimatorParameters` hashes, not raw parameter strings.
- Strong attack no longer enters `vShooterMeleeInput.TriggerStrongAttack`, so it cannot
  call `shooterManager.CancelReload`.
- `BrawlInvectorMeleePresentationManager : vMeleeManager` terminally overrides both
  `SetActiveAttack` overloads. Animator-authored damage windows are counted, but no
  member, weapon, hit source, or `ApplyDamage` path is activated.
- Direct `OnDamageHit(ref vHitInfo)` fails closed before vendor damage construction or
  application. Empty `Members` plus null left/right weapons remain enforced as defense
  in depth.
- The adapter can be configured only with the project presentation-manager subtype.
  Activation and every presentation request revalidate that the manager is disabled,
  has an empty member list, and has null weapons.
- `ResetAttackTriggers` routes graph state-exit cleanup through the project controller.
  Full scheduler teardown also clears weak, strong, recoil, and reset triggers and
  normalizes `AttackID` and `RecoilID` to zero. A live immediate-close probe uses
  nonzero recoil ID 7 so this reset evidence is not a default-value comparison.

Brawl targeting, fixed attack delay, damage, Super charge, health, stamina, ammo,
projectiles, KO reporting, and telemetry remain unchanged and authoritative.

## Generated Topology

`InvectorMigrationPilotBuilder` replaces the exact stock `vMeleeManager` copied from the
no-inventory template with exactly one root
`BrawlInvectorMeleePresentationManager`. The generated prefab contains no exact stock
manager and remains inactive. The lab keeps the shooter and presentation managers
disabled even while Animator state-machine behaviours report attack-window timing.

Two consecutive builder runs produced equal outputs and preserved GUIDs:

- controller SHA-256:
  `08A30491109E5126B6C1F55F721A6094D445B005686099A4AFDE49D05A3DD4E6`;
- prefab SHA-256:
  `B4928F206DCB1A7088E929DEC0D459EA9F91F129FFD5F140D27C947D3ABB82B9`;
- semantic scene-topology SHA-256:
  `DC2CDC968D0DB9188B8A6E72AB2AD4E9051503AAFC50CADE2E8523CD43D57951`;
- controller/prefab/lab GUIDs remained
  `073c25839c579d54e95b3afd2edd8f13`,
  `12b31ba25708e04489318559f291b366`, and
  `91742eec95d6352499529bebc9dedea0`.

`Assets/Scenes/MainMenu.unity` remained active and dirty across both builds.

## Validation Evidence

- Package/settings audit: Unity 6000.3.7f1, Invector 2.6.6, Input System-only;
  388 C# files, 320 prefabs, 14 Animator controllers, and 3 PDF manuals; all known Unity
  6 compatibility patterns absent.
- Focused generated/source gate:
  `Temp/InvectorMigrationPilotEditModeResults.xml`, 9/9 passed in 1.4346772 seconds,
  SHA-256 `AB33D81C4DAF47B6451C13B17785B750D33380942B557353D14EEFD145135441`.
- Live action-feed gate:
  `Temp/InvectorMigrationPhase3BEditModeResults.xml`, 1/1 passed in 12.9913687 seconds,
  SHA-256 `B0D8648DE85BEC9AB9B00BB99F1DC82C4878849C5F636E5D42A37000F60C8586`.
  It observed weak and strong states, exact recoil, balanced Super window enable/disable
  callbacks behind the project manager, zero direct damage callbacks, request/write
  census equality, and immediate-close residue cleanup.
- Buffered-motor compatibility gate:
  `Temp/Phase3CCBufferedMotorEditModeResults.xml`, 1/1 passed in 3.2538274 seconds,
  SHA-256 `5ACB612338F379CEEF4421CDE8D2D86774087AE2E4CF8B967E09E6EF7A1912E0`.
  It observed zero attack windows and zero melee/recoil presentation writes.
- Complete regression:
  `Temp/BrawlArenaFullEditModeResults.xml`, 188/188 passed in 4.3965437 seconds,
  SHA-256 `E4110F597AA836197568CCB4DDDA2CED02B75A60901195BB6D5B0D107DC98526`.
- Unity Console: zero warnings/errors after compile, regeneration, focused/live gates,
  and the complete run. MainMenu remained loaded and dirty.

## Nonclaims and Next Gate

At the Phase 3D-A cutoff, this result did not prove `PlayDeath`, `PlayRespawn`, or
`PlayVictory`; those methods still threw. It did not prove presentation-exception isolation in `BrawlerController`,
weapon sockets/visibility/muzzle presentation, shooter IK, selective hit-proxy layers,
production `GameFlow` assembly, touch/camera/HUD coexistence, scene transitions, AI, or
any roster entry beyond isolated Cinder.

Phase 3D-B subsequently closed the lifecycle/failure-isolation portion without using
vendor `currentHealth`, `isDead`, death/ragdoll, component removal, or revive authority;
see `Phase3D-B-LifecyclePresentation.md`. Production `BrawlerBackend.Invector` remains
closed and every roster entry remains Legacy. Phase 3D-C subsequently closed isolated
weapon/IK and selective collision handling; see
`Phase3D-C-WeaponIKSelectiveCollision.md`. Production assembly, scene/input coexistence,
AI, and roster gates are still open.
