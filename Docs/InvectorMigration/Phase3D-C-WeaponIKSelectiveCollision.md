# Phase 3D-C — Weapon/IK Presentation and Selective Collision

Status: complete for the generated, isolated human Cinder lab on 2026-07-13.
Production selection remains closed.

## Boundary Completed

Phase 3D-C adds a visual-only weapon seam, a project-owned staff presentation, guarded
support-hand/weapon-hand IK, and an inert Brawl hit-selection proxy. It moves no
targeting, attack timing, projectile, damage, health, ammo, reload, equipment-data,
input, camera, lifecycle, match, or AI authority to Invector.

| Concern | Authoritative owner after this phase |
|---|---|
| Attack gesture, direction, timing, cooldown, Super | Brawl `PlayerBrawlerInput` and `BrawlerController` |
| Projectile pooling, launch, hit selection, damage | Brawl `CombatObjectPool`, `Projectile`, `Health`, and combat rules |
| Weapon socket, aim/IK, muzzle effect, visibility | optional `IBrawlerWeaponPresentation` |
| IK pose data | project-owned `BrawlWizardStaff` assets |
| Broad Brawl target-selection trigger | inert root-child `BrawlerHitProxy` |
| Vendor shooter/melee/projectile/damage systems | disabled, empty, null, or absent |

The generated pilot remains inactive. Every production roster entry remains Legacy, and
`BrawlerCharacterAssembly.Resolve(BrawlerBackend.Invector)` still fails before
instantiation.

## Visual-Only Facade

`IBrawlerWeaponPresentation` exposes exactly five semantic operations:

- `PresentAim(Vector3)`, where `Vector3.zero` releases aim and IK;
- `TryGetMuzzlePosition(out Vector3)`;
- `PresentMuzzle(Vector3, Vector3)`;
- `SetVisible(bool)`; and
- `ResetForRespawn()`.

The contract contains no Invector type, Animator state, damage, ammo, stamina, cooldown,
shoot, reload, inventory, or equipment-data API. `BrawlerController` discovers or
accepts exactly one component-backed implementation on its own root, serializes the
component source for domain-reload restoration, and locks selection at `Start`. Null,
plain managed, cross-root, and duplicate owners fail before runtime selection.

Every presentation call crosses one nonthrowing `TryWeaponPresent` boundary. Failures
increment `WeaponPresentationFailureCount` and retain the last exception, operation,
type, and message without escaping into targeting, attack/Super cleanup, projectile
timing, death visibility, or respawn. A missing, unavailable, throwing, or non-finite
presenter muzzle falls back to the authored `SpellOrigin` (and then the existing root
fallback) instead of poisoning Brawl combat.

Basic attacks and Supers acquire presentation aim after the Brawl action is accepted and
release it with `PresentAim(Vector3.zero)` in their existing `finally` blocks. Projectile
creation remains authoritative: `FireProjectile` and `FireSuperProjectile` first ask
`CombatObjectPool` for the Brawl projectile, return when the pool returns null, and only
then emit the visual muzzle effect before launching that Brawl projectile. Death hides
the presentation before lifecycle animation; respawn revives/teleports first, then
resets and shows the weapon before the semantic Respawn request.

Legacy actors deliberately install no weapon presenter. Their `SpellOrigin` and visual
behavior remain unchanged.

## Project Weapon and IK Presenter

`InvectorBrawlerWeaponPresentation` is a same-root, lab-gated implementation. The
builder configures it while disabled; only `InvectorPhase3BLabController` may open its
runtime gate on an active, valid Humanoid lab stack. It owns one guarded `LateUpdate`
presentation pass and never calls the inherited Invector shooter `LateUpdate`, aim,
shoot, reload, recoil, weapon-handler, ammo, projectile, or damage lifecycle.

The presenter uses `Invector.IK.vIKSolver` only as the package's two-bone math solver.
It constructs weapon-hand and support-hand solvers from explicit Humanoid arm-bone
references, not the Animator constructor, so no hidden hint/helper GameObjects are
created. `RuntimeHelperCount` therefore remains zero, and gate close releases the solver
objects and clears aim/effects without changing hierarchy topology.

The project category is `BrawlWizardStaff`. Its project-owned
`vWeaponIKAdjust` contains exactly the four package state records for both hands:

- `Standing`;
- `StandingAiming`;
- `Crouching`; and
- `CrouchingAiming`.

Current crouch and presented-aim state select one record. The pass validates finite and
reachable target/hint poses, snapshots both arm chains before solving, and restores them
on an invalid pose or fault. It suppresses IK while the visual is hidden, while a project
lifecycle trigger/state is active, or when the graph exposes `IgnoreIK`; it independently
honors `IgnoreSupportHandIK`. Death and Victory release aim, Respawn reset restores
visibility and clears effects, and immediate lab close/reopen cannot replay deferred
presentation.

This is presentation IK only. It does not activate `vShooterManager.weaponIKAdjustList`,
install a vendor weapon, or write the Animator's aim/shoot/reload parameters.

## Generated Assets and Selective Layers

`InvectorMigrationPilotBuilder` is the source of truth for:

- `Assets/Generated/InvectorMigration/Cinder/Weapons/CinderStaffPresentation.prefab`;
- `Assets/Generated/InvectorMigration/Cinder/IK/CinderStaffIKAdjust.asset`;
- `Assets/Generated/InvectorMigration/Cinder/IK/CinderStaffIKAdjustList.asset`; and
- the presenter/proxy references in `CinderInvectorPilot.prefab`.

The staff prefab is a project-owned pure visual derived from Cinder's authored
`Staff01`. It contains `StaffVisual`, the retained `SpellOrigin`, explicit
`SupportHandTarget` and `SupportHintTarget` anchors, and one non-looping,
non-play-on-awake `BrawlMuzzleVfx` particle system. It contains no vendor shooter weapon,
projectile, hitbox, damage sender/receiver, or melee attack component.

The generated pilot's selective layer contract is:

| Object | Layer | Runtime posture in the prefab |
|---|---:|---|
| actor root | 23 `InvectorPlayer` | inactive |
| `BrawlHitProxy` | 10 `BrawlerHitbox` | component and trigger collider disabled |
| skeleton, mesh, staff, IK anchors | 0 `Default` | visual hierarchy retained |
| `SpellOrigin`, `BrawlMuzzleVfx` | 12 `VFX` | effect stopped/cleared |

`BrawlerHitProxy` is an inert marker around exactly one trigger `SphereCollider`; it has
no trigger/collision callbacks and no damage behavior. No child may inherit layer 23.
The generated hierarchy never calls `CombatPhysics.SetLayerRecursively`.

To preserve unchanged Legacy behavior, the one recursive BrawlerHitbox assignment moved
out of `BrawlerController.Start` and into `LegacyBrawlerCharacterAssembler` immediately
after it instantiates the Legacy prefab. This keeps every Legacy child on layer 10 while
preventing a future Invector actor from flattening its semantic hierarchy.

The builder creates or normalizes all three weapon/IK assets, replaces the embedded
authored staff with the project prefab, configures the one root presenter and one proxy,
then reapplies dormant safety. Repeated builds reuse the assets and preserve their GUIDs
and intended topology; generated YAML must not be hand-edited.

Current project-owned asset identities are:

| Asset | GUID |
|---|---|
| staff presentation prefab | `9d5e236e804e54a48b9baff7aed98f50` |
| staff IK adjust | `26c856b78ccecae4d8ac4e607d4b8c7b` |
| staff IK adjust list | `475fa2e0f16a64e4fa11d2fe859a7022` |
| Cinder pilot prefab | `12b31ba25708e04489318559f291b366` |

## Vendor Systems Kept Inert

The pilot still retains disabled template managers only where the earlier scheduler and
animation graph require their data shape. Phase 3D-C does not enable them:

- `vShooterManager` is disabled, has null left/right weapons, a null vendor IK list,
  zero damage/block-aim masks, and disabled aim/recoil/lock/reload-display flags;
- `vAmmoManager` and `vCollectShooterMeleeControl` remain disabled and empty;
- `BrawlInvectorMeleePresentationManager` remains disabled with empty members, null
  weapons, and terminal damage-window callbacks; and
- vendor shooter weapons, projectile controls/instantiators, object damage,
  damage senders/receivers, hitboxes, and melee attack objects are absent from the full
  hierarchy.

The live proof pins vendor health/death, internal stamina, ammo signature, reload state,
weapon references, manager enabled states, melee counters, and suppressed-path counters
before weapon/IK exercise and requires every value to remain unchanged.

## Test Evidence

All live results were launched through the dirty-scene-safe recorder and restored the
caller's active dirty `Assets/Scenes/MainMenu.unity`:

| Gate | Result | Duration | XML SHA-256 |
|---|---:|---:|---|
| Focused pilot/static/source | 9/9 | 1.4222955 s | `D5CC1A989EB03618558A02CA2A749C8FDFD8ADADD407E02E776823239E632439` |
| Phase 3D-C weapon/IK live | 1/1 | 8.4471329 s | `EEA77D8D621608E057E1A632922DB0C44E0DC12295BF42CDF31E3110F174AA5B` |
| Phase 3B action-feed compatibility | 1/1 | 8.5676496 s | `CEC2393F263A8D299F89E1A732DA342777720808EAA2B3D74F8C2B0FC88528CD` |
| Phase 3C-C buffered compatibility | 1/1 | 5.7778935 s | `A77333BC4C6B67103746BBAC95DA125029AA5370A9C3FAA42217A7D86834A6E8` |
| Phase 3D-B lifecycle compatibility | 1/1 | 8.8816134 s | `BE5D7295400FB2F9684F7B1C792FCB17921F29161E54698D75448C60144FF488` |
| Complete EditMode regression | 208/208 | 4.8979558 s | `FB42A4923406B8A949D90A3E6E5186F6D433E6C20570725C3D030A7AC0C408C8` |

The dedicated live test resolves all four configured IK records and observes at least
one clean support-hand solver pass; strict reach/hint validation may suppress other
sampled poses. It verifies finite aim plus zero-vector release, requires exactly one
particle emission per muzzle presentation call, checks the complete layer topology and
forbidden-component census, exercises Death/Respawn/Victory suppression, closes and
reopens the lab without deferred state or helper accumulation, returns the pilot to its
dormant action-feed posture, and captures zero scoped warnings/errors.

## Nonclaims, Rollback, and Next Slice

This phase does not prove a production `BrawlerController`/`Health` actor using the
Invector motor/animation/presenter stack, a production assembler/selector, live
`PlayerBrawlerInput` plus buffered-motor coexistence, MainMenu-to-Arena spawn/respawn
flow, previews, AI navigation/motion, equipment/reload gameplay, vendor collision or
projectiles, or any roster entry beyond isolated Cinder.

Rollback is unchanged: keep every spawn context on `BrawlerBackend.Legacy`, leave the
generated Cinder prefab inactive, and disable the lab presenter. Legacy actors retain
their recursive layer assignment and authored `SpellOrigin` without a presenter.

The next bounded phase is the production human assembler: construct one context-gated
human Cinder with the already proven motor, input bridge, Animator/lifecycle, and
weapon/IK presentation boundaries while preserving Brawl `PlayerBrawlerInput`,
`BrawlCamera`, `Health`, combat, and match authority. AI adaptation follows separately,
then roster conversion; Cinder AI and every other roster entry remain Legacy meanwhile.
