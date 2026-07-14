---
name: migrate-brawlarena-invector
description: Maintain, extend, regenerate, review, or validate BrawlArena's Invector Third Person Controller 2.6.6 character architecture across all four human and AI roster roles, Input System adapters, locomotion, navigation, camera boundaries, Animator controllers, IK, melee and shooter presentation, health and damage firewalls, equipment visuals, production prefabs, menu previews, scene builders, tests, and vendor upgrades. Use whenever work touches Assets/Invector-3rdPersonController, Invector-backed BrawlerController assembly, PlayerBrawlerInput, AIBrawler, BrawlCamera, Health, GameFlow spawning, generated Invector variants, or Invector project settings. Do not use for unrelated Unity gameplay or art tasks.
---

# Maintain BrawlArena's Invector Character Architecture

Treat the Invector-only cutover as the current architecture. Do not recreate a Legacy backend, selector, motor, navigator, animation driver, prefab path, rollout flag, or rollback branch.

## Start Safely

1. Inspect `git status --short` and the relevant diff. Preserve unrelated and user-owned changes.
2. Run:

   ```powershell
   powershell -NoProfile -ExecutionPolicy Bypass -File .agents/skills/migrate-brawlarena-invector/scripts/audit-invector.ps1 -FailOnBlockers
   ```

3. For settings, tags, layers, masks, collision, or package-upgrade work, also run:

   ```powershell
   powershell -NoProfile -ExecutionPolicy Bypass -File .agents/skills/migrate-brawlarena-invector/scripts/audit-invector-settings.ps1 -OutputPath Temp/InvectorSettingsAudit.json
   ```

4. Read `Docs/InvectorMigrationPlan.md` for the current architecture and ownership map.
5. Read only the references needed:
   - `references/brawlarena-contracts.md` for game-authoritative behavior.
   - `references/invector-2.6.6.md` for package topology, PDFs, compatibility patches, and subsystem hazards.
   - `references/validation.md` for regeneration and proof gates.
   - `Docs/InvectorMigration/Phase*/` only as historical evidence when tracing why a constraint exists. Never treat a historical phase's rollout, rollback, test count, or open item as current truth.
6. If Unity MCP is available, call its user-guidelines method before using the editor. Inspect the editor, compile state, console, active scene, dirty state, and Play mode before mutating Unity state.

## Preserve the Final Ownership Split

Keep BrawlArena authoritative for:

- roster identity, stats, teams, specialties, Super charge, match lifecycle, spawn, death, respawn, and victory;
- `Health`, target filtering, damage calculation, applied-damage semantics, projectiles, explosions, knockback, status effects, VFX, SFX, and telemetry;
- `PlayerBrawlerInput` gesture semantics and `BrawlCamera` aim basis;
- `AIBrawler` tactics and destination requests;
- HUD, inventory data, equipment data, and menu selection.

Use Invector through project-owned adapters for:

- Rigidbody locomotion, grounded/collision measurements, facing, displacement, stop, and teleport through `InvectorBrawlerMotor`;
- planning-only AI navigation through `InvectorBrawlerNavigation` and one transform-neutral child `NavMeshAgent`;
- the approved one-scheduler controller/Animator stack;
- semantic combat, hit, death, respawn, and victory presentation through `InvectorBrawlerAnimationDriver`;
- staff/bow visibility, muzzle, four-pose arm IK, and bow-string/Arrow2 staging through `InvectorBrawlerWeaponPresentation`.

Keep vendor health, damage, projectile, ammo, reload, inventory UI, camera, ragdoll, and sample AI inert. Never mirror Brawl health into Invector health.

## Require Exact Production Assembly

Let `BrawlerCharacterAssembly.Default` derive Human versus AI from `asHumanPlayer`. Keep only `ProductionHumanInvector` and `ProductionAIInvector` as explicit validation contexts.

Require every production prefab asset to remain inactive. Before cloning, require exactly one root `InvectorBrawlerPrefabIdentity` matching the requested roster ID and role.

| Roster | ID | Human prefab | AI prefab | Builder |
|---|---|---|---|---|
| Cinder | `fire` | `Assets/Generated/InvectorMigration/Cinder/Prefabs/CinderInvectorHuman.prefab` | `Assets/BrawlArena/Prefabs/Invector/CinderInvectorAI.prefab` | `InvectorMigrationPilotBuilder.BuildPilotAssets()` |
| Rime | `frost` | `Assets/Generated/InvectorMigration/Rime/Prefabs/RimeInvectorHuman.prefab` | `Assets/BrawlArena/Prefabs/Invector/RimeInvectorAI.prefab` | `InvectorRimeMigrationBuilder.BuildRimePilotAssetsSafely()` |
| Tempest | `storm` | `Assets/Generated/InvectorMigration/Tempest/Prefabs/TempestInvectorHuman.prefab` | `Assets/BrawlArena/Prefabs/Invector/TempestInvectorAI.prefab` | `InvectorTempestMigrationBuilder.BuildTempestPilotAssetsSafely()` |
| Thorn | `thorn` | `Assets/Generated/InvectorMigration/Thorn/Prefabs/ThornInvectorHuman.prefab` | `Assets/BrawlArena/Prefabs/Invector/ThornInvectorAI.prefab` | `InvectorThornMigrationBuilder.BuildThornPilotAssetsSafely()` |

Require one root `Health`, `BrawlerController`, `InvectorBrawlerMotor`, `InvectorBrawlerAnimationDriver`, `InvectorBrawlerWeaponPresentation`, Animator, Rigidbody, CapsuleCollider, hit proxy, and role-specific runtime gate. Require `PlayerBrawlerInput` only for Human; require `AIBrawler`, `InvectorBrawlerNavigation`, and one disabled child planner only for AI. Reject competing interface implementations, `CharacterController`, and vendor sample AI.

Open runtime components transactionally through `InvectorHumanRuntimeGate` or `InvectorAIRuntimeGate`. Enable the physical input or tactical producer last. Close the whole stack on any partial failure.

## Maintain One Scheduler and Animator Authority

Keep `PlayerBrawlerInput` as the sole production physical-input reader. Feed its world intent into `InvectorBrawlerMotor`; let `InvectorShooterMeleeInputAdapter` consume buffered intent without polling HUD, Input Actions, keyboard, gamepad, `UnityEngine.Input`, or a camera.

Let the adapter's guarded inherited fixed scheduler update the controller, locomotion, rotation, and Animator exactly once. Do not add another Unity-message scheduler or call `cc.UpdateAnimator` elsewhere.

Let the Invector controller stack own locomotion parameters and state polling. Send only semantic presentation requests through `IBrawlerAnimationDriver`. Keep lifecycle graph details in the concrete driver/controller and builder-owned Animator assets.

## Keep Combat and Presentation Firewalled

Route gameplay attacks, Super, projectiles, hit selection, damage, and status effects through Brawl code. Use Invector attack/recoil/lifecycle triggers only as visual presentation.

Keep `BrawlInvectorMeleePresentationManager` terminal: Animator attack windows must not enable vendor damage. Keep shooter, ammo, reload, collect, and inventory managers disabled and inert.

Use `InvectorBrawlerWeaponPresentation` for visual-only aim, release, muzzle, visibility, reset, and guarded IK. Never call `CombatPhysics.SetLayerRecursively` on an Invector hierarchy; preserve root layer 23, hit proxy layer 10, ordinary visual layer 0, and VFX layer 12.

For Thorn, preserve:

- `BrawlWizardBow` as the weapon category;
- Bow02 left-hand presentation and exact `Arrow2` authored staging;
- `Attack01_Bow` mapped to weak and `Attack02_Bow` mapped to strong/Super;
- `InvectorBowPresentationRig` string anchors, three-point `LineRenderer`, nock/tip alignment, aim staging, and bounded release hold;
- Brawl `Arrow01`/`Arrow02` projectile and damage authority;
- the four standing/crouching, aiming/non-aiming IK records.

## Keep Previews Isolated

Resolve only each definition's exact production Human prefab through `BrawlerPreviewAdapter`. Neutralize every Behaviour, Collider, Rigidbody, and Rigidbody2D while the clone is inactive; activate, neutralize again, then enable only the root Animator with root motion off.

Use `ShowIdle` and `ShowVictory` for `MainMenuFlow` and `PortraitStudio`. Never open a gameplay runtime gate, input path, AI path, physics path, health path, or combat path in a preview.

## Regenerate Through Owners

Do not hand-edit generated prefabs, controllers, IK assets, portraits, Arena, or MainMenu.

Regenerate in dependency order:

1. Run `WizardAssetBuilder.EnsureAssets()` for Cinder/Rime/Tempest source art.
2. Run the four Invector builders in roster order.
3. Use `ArenaSceneBuilder.BuildRoster()` or `BuildArenaScene()` for owned roster/scene data.
4. Use `MenuSceneBuilder.BuildMenuScene()` for MainMenu.
5. Let `PortraitStudio` refresh portraits from exact Human preview prefabs.
6. Register MainMenu first and Arena second in build settings.

Require builders to preserve the caller's active scene and dirty state. Do not save, close, reload, or clear a user's dirty scene as a side effect. Require stable asset GUIDs and intended topology across two consecutive builds; do not require byte-identical Unity YAML.

## Validate Before Hand-Off

Follow `references/validation.md`. At minimum:

1. Re-run both relevant audit scripts.
2. Require a completed Unity domain reload and zero compile errors.
3. Run the focused Invector-only cutover, affected presentation/navigation categories, full EditMode suite, and applicable live Arena/menu tests through the dirty-scene-safe recorder when available.
4. Load Arena and prove all spawned Human and AI actors use only exact Invector authorities.
5. Exercise movement, aim, weak attack, Super, hit, KO, respawn, victory, AI pathing, external displacement, and each roster's weapon/IK presentation.
6. Load MainMenu and inspect all four idle/victory previews and regenerated portraits with no gameplay authority enabled.
7. Audit missing references, build settings, generated GUID/topology stability, vendor diffs, scene/prefab churn, ownership uniqueness, and residual Legacy symbols/assets.

Do not claim completion from compilation, historical phase results, or static tests alone.

## Upgrade Invector Deliberately

Never import `Invector > Import ProjectSettings`. Never copy the package's ProjectSettings over BrawlArena.

Keep project integration outside `Assets/Invector-3rdPersonController`. Record unavoidable vendor patches in `Docs/VendorPatches/Invector-2.6.6.md`. On any package or Unity version change:

1. preserve a restorable package baseline and diff the vendor tree;
2. re-run both audits and inspect the three bundled PDFs;
3. re-check Unity compatibility patches, APIs, Animator graphs, template topology, serialized layers/masks, and Input System assumptions;
4. regenerate all owned variants and scenes;
5. run the complete validation matrix before accepting the upgrade;
6. update this skill and `references/invector-2.6.6.md` when assumptions change.

## Report Precisely

Report the authority or capability changed, files and generated assets changed, builder commands, Unity/test evidence, remaining parity risks, disabled vendor subsystems, residual Legacy census, and any evidence not rerun. Do not present historical phase evidence as proof of the current tree.
