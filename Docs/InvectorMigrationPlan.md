# BrawlArena Invector-Only Character Architecture

## Status

BrawlArena's character-controller architecture is Invector-only. All four roster entries use exact project-owned Invector Human and AI prefabs. The former Legacy controller, motor, navigation, animation, prefab-selection, rollout, and rollback paths are not part of the final design and must not return.

This document describes the current architecture and its acceptance criteria. Files under `Docs/InvectorMigration/Phase*/` are historical implementation evidence only. Their default-off rollout switches, rollback instructions, phase-local test counts, and open-work lists are not current requirements or current proof.

## Outcome

The cutover changes character execution without replacing BrawlArena's game rules:

- Invector owns locomotion execution and locomotion animation through project adapters.
- Invector supplies visual combat/lifecycle animation and guarded weapon IK.
- BrawlArena owns input meaning, AI decisions, camera, attacks, three-charge basic-attack reloads, projectiles, damage, health, status effects, resources, death/respawn rules, match flow, roster data, equipment data, and telemetry.
- Production, menu previews, portraits, and generated scenes all consume the same exact Invector Human identities.
- Vendor health, damage, projectile, ammo, reload, inventory UI, camera, ragdoll, and sample AI remain inert.

## Runtime Data Flow

### Human

```text
BrawlHUD / Input System
        |
        v
PlayerBrawlerInput ---- attack gesture / aim / Super ----> BrawlerController
        |                                                   |
        | world movement intent                             | Brawl combat, Health,
        v                                                   | projectiles, match rules
InvectorBrawlerMotor <--------------------------------------+
        |
        v
InvectorShooterMeleeInputAdapter (one fixed scheduler)
        |
        +--> BrawlInvectorThirdPersonController --> Rigidbody/Capsule
        +--> Animator locomotion
        +--> InvectorBrawlerAnimationDriver (semantic presentation)
        +--> InvectorBrawlerWeaponPresentation (visual-only IK/muzzle/bow)
```

`PlayerBrawlerInput` is the only production physical-input reader. The adapter uses buffered world intent and does not read HUD, Input Actions, keyboard, gamepad, `UnityEngine.Input`, or camera state.

### AI

```text
AIBrawler tactics
        |
        v
InvectorBrawlerNavigation --> child NavMeshAgent (planning only)
        |
        | desired velocity
        v
InvectorBrawlerMotor --> one Invector fixed scheduler --> Rigidbody/Animator
        |
        +--> BrawlerController combat decisions and Brawl gameplay
        +--> semantic animation and visual weapon presentation
```

The child planner never writes the actor transform. The root Invector motor/Rigidbody remains the only physical authority.

## Authority Matrix

| Concern | Final authority | Invector involvement |
|---|---|---|
| Roster identity and stats | `BrawlerDefinition`, `GameFlow` | Exact prefab identity validation only |
| Player input semantics | `PlayerBrawlerInput` | Buffered execution only |
| Camera and aim frame | `BrawlCamera` | None |
| AI tactics | `AIBrawler` | Navigation/motor execution only |
| AI path planning | `InvectorBrawlerNavigation` | One transform-neutral child `NavMeshAgent` |
| Physical locomotion | `InvectorBrawlerMotor` | Invector controller/Rigidbody stack |
| Locomotion Animator writes | One guarded Invector scheduler | Complete project-owned controller graph |
| Attack acceptance/timing | `BrawlerController` | Semantic animation request only |
| Basic-attack charges/reload | `BrawlerController` / `MobileCombatRules` | None; vendor ammo/reload stays inert |
| Projectiles/melee selection | Brawl combat code | Presentation only |
| Health/damage | Brawl `Health` | Vendor health/damage disabled |
| Status/knockback/Super charge | Brawl combat code | Motor displacement/presentation only |
| Death/respawn/victory rules | Brawl match/lifecycle code | Triggered visual presentation only |
| Weapon equipment data | Brawl roster/builders | Visual prefab and IK only |
| HUD/menu/portraits | Brawl UI and preview adapter | Neutralized Animator-only preview |

## Production Roster

Every `BrawlerDefinition` contains the exact Invector Human and AI prefab references. There is no generic character prefab field and no backend/rollout selector.

| Character | ID | Human prefab | AI prefab | Builder |
|---|---|---|---|---|
| Cinder | `fire` | `Assets/Generated/InvectorMigration/Cinder/Prefabs/CinderInvectorHuman.prefab` | `Assets/BrawlArena/Prefabs/Invector/CinderInvectorAI.prefab` | `InvectorMigrationPilotBuilder.BuildPilotAssets()` |
| Rime | `frost` | `Assets/Generated/InvectorMigration/Rime/Prefabs/RimeInvectorHuman.prefab` | `Assets/BrawlArena/Prefabs/Invector/RimeInvectorAI.prefab` | `InvectorRimeMigrationBuilder.BuildRimePilotAssetsSafely()` |
| Tempest | `storm` | `Assets/Generated/InvectorMigration/Tempest/Prefabs/TempestInvectorHuman.prefab` | `Assets/BrawlArena/Prefabs/Invector/TempestInvectorAI.prefab` | `InvectorTempestMigrationBuilder.BuildTempestPilotAssetsSafely()` |
| Thorn | `thorn` | `Assets/Generated/InvectorMigration/Thorn/Prefabs/ThornInvectorHuman.prefab` | `Assets/BrawlArena/Prefabs/Invector/ThornInvectorAI.prefab` | `InvectorThornMigrationBuilder.BuildThornPilotAssetsSafely()` |

Every prefab asset remains inactive. Its exact root `InvectorBrawlerPrefabIdentity` must match the requested roster ID and `Human` or `AI` role before instantiation.

## Assembly

`BrawlerCharacterAssembly.Default` derives the role from `asHumanPlayer` and resolves only:

- `InvectorHumanBrawlerCharacterAssembler`; or
- `InvectorAIBrawlerCharacterAssembler`.

`ProductionHumanInvector` and `ProductionAIInvector` remain explicit construction contexts for builders/tests. They do not provide an alternate backend.

Both assemblers:

1. validate exact inactive prefab identity and topology;
2. instantiate the dormant prefab;
3. configure Brawl `Health`, facade stats, motor, animation driver, weapon presenter, and role-specific navigation/input;
4. attach project RPG visuals;
5. require the builder-owned dormant gate posture;
6. open the complete role-specific stack transactionally;
7. destroy/deactivate the clone if any step fails.

No component is synthesized as a fallback. A missing owner is a configuration error.

## Production Topology

### Common root components

Each production role has exactly one root:

- `InvectorBrawlerPrefabIdentity`;
- `Health`;
- `BrawlerController`;
- `InvectorBrawlerMotor`;
- `InvectorBrawlerAnimationDriver`;
- `InvectorBrawlerWeaponPresentation`;
- `BrawlInvectorThirdPersonController`;
- `InvectorShooterMeleeInputAdapter`;
- Animator;
- Rigidbody;
- CapsuleCollider;
- configured child `BrawlerHitProxy`.

It contains the exact project melee-presentation firewall and required inherited shooter/melee manager references, all dormant/inert until the gate opens. It contains no `CharacterController`, vendor camera, vendor damage authority, or vendor sample AI.

### Human role

Add exactly one root:

- `PlayerBrawlerInput`;
- `InvectorHumanRuntimeGate`.

Exclude `AIBrawler`, Invector navigation, AI gate, and planner agent.

### AI role

Add exactly one root:

- `AIBrawler`;
- `InvectorBrawlerNavigation`;
- `InvectorAIRuntimeGate`.

Add one disabled child planner `NavMeshAgent`. Keep `updatePosition`, `updateRotation`, `updateUpAxis`, and automatic off-mesh traversal disabled when live. Exclude `PlayerBrawlerInput` and the Human gate.

## Runtime Gates

Human and AI runtime gates open the approved stack synchronously and close every partially opened authority on failure.

Activation order establishes consumers before producers:

1. select buffered movement and clear camera references;
2. enable Animator/controller and configure Rigidbody/Capsule;
3. enable Brawl facade/Health and Invector motor/presentation consumers;
4. initialize the motor and one fixed scheduler;
5. open visual weapon presentation;
6. open AI planner when applicable;
7. enable `PlayerBrawlerInput` or `AIBrawler` last.

Teardown closes physical/tactical producers first, clears offensive actions and destinations, disables presentation/scheduling, returns the motor and physics to dormant posture, then deactivates the root.

## Movement, Navigation, and Camera

`InvectorBrawlerMotor` implements all `IBrawlerMotor` capabilities: movement intent, facing, velocity, grounded/collision measurement, external displacement, Ward Step/knockback, stop, constraints, and teleport.

The adapter owns no independent gameplay input. Its one guarded inherited fixed scheduler is the only controller, locomotion, rotation, and Animator scheduler.

`InvectorBrawlerNavigation` accepts durable Brawl destination requests and exposes desired velocity only for a valid resolved request. External displacement and teleport cancel planning immediately. Stuck recovery performs one bounded replan, then fails the repeated destination closed while allowing a materially different destination to recover.

`BrawlCamera` remains the sole gameplay camera and aim frame. No Invector camera or tag-discovered camera path may activate.

## Animator and Lifecycle

The shared project controller preserves the complete Invector Shooter+Melee graph and its eight layers:

1. Base Layer
2. RightArm
3. LeftArm
4. OnlyArms
5. UpperBody
6. UnderBody
7. Shot
8. FullBody

Roster AnimatorOverrideControllers replace character-specific clips without simplifying the graph.

`InvectorBrawlerAnimationDriver` exposes semantic requests only. The Invector scheduler owns locomotion parameters. Project controller methods own guarded weak, strong/Super, recoil, death, respawn, and victory graph writes.

Death and victory hold visually. Respawn exits motionless. Root motion remains disabled. Lifecycle state markers record trace information only and cannot mutate gameplay, resources, physics, or match state.

## Combat Firewall

Brawl combat remains deterministic and authoritative:

- tap/drag aim and action acceptance;
- three sequentially reloading basic-attack charges, attack cooldowns, movement locks, and Super charge;
- projectile pooling, travel, sweeps, impact, explosions, and target selection;
- melee selection;
- damage, team filtering, obstruction, status effects, knockback, healing, hazards, and telemetry;
- actual-applied-damage semantics.

`BrawlInvectorMeleePresentationManager` terminates both Animator attack-window callbacks without activating vendor damage. Weak, strong, and recoil presentation do not enter vendor combat base methods. Vendor shooting, ammo, reload, collection, inventory, and projectile paths remain inert.

Do not mirror Brawl `Health` into Invector health. Invector health/death is coupled to Rigidbody, collider, ragdoll, and lifecycle mutation and would create a second authority.

## Character Presentation

`InvectorBrawlerWeaponPresentation` is visual-only. It owns aim/release presentation, muzzle lookup/effect, equipment visibility, respawn reset, and guarded two-arm IK for:

- standing;
- standing aiming;
- crouching;
- crouching aiming.

Character-specific outputs remain isolated:

- Cinder: Staff01, fire presentation;
- Rime: Staff02, Frost `SpellOrigin`, cyan muzzle;
- Tempest: Staff03, Storm `SpellOrigin`, `#B58CFF` muzzle, builder-owned inward 1 cm hand reach calibration;
- Thorn: Bow02, `BrawlWizardBow`, authored right-hand Arrow2, bow string, and bow-specific IK.

The presenter cannot accept attacks, choose targets, create projectiles, spend ammo, reload, apply damage, mutate health, or alter match state.

## Thorn Bow Boundary

Thorn uses these pinned sources:

- `Assets/ModularRPGHeroesPBR/Prefabs/BasicCharacters/Bow02.prefab`;
- `Assets/ModularRPGHeroesPBR/Animators/Bow.controller`;
- `Assets/ModularRPGHeroesPBR/Mesh/DefaultCharacter.fbx`;
- `Assets/ModularRPGHeroesPBR/Material/RegularPBR/Weapons.mat`;
- `Attack01_Bow.fbx` for weak/basic presentation;
- `Attack02_Bow.fbx` for strong/Super presentation.

The bow remains left-held. The authored `Arrow2` remains a right-hand/nock presentation object, not a gameplay projectile.

`InvectorBowPresentationRig` owns:

- `StringTop`, `StringRest`, and `StringBottom` anchors;
- a three-point local-space `LineRenderer`;
- Arrow2 nock/tip alignment;
- aim/draw staging;
- visibility and reset;
- a bounded release hold.

Brawl retains `Arrow01` basic and `Arrow02` explosive-Super pooling, launch, target selection, damage, and telemetry.

## Layers and Physics

Use the project layer namespace:

- 8 `Ground`;
- 9 `WorldBlocker`;
- 10 `BrawlerHitbox`;
- 11 `Projectile`;
- 12 `VFX`;
- 23 `InvectorPlayer`.

Keep layer 23 on the production root, the inert hit proxy on 10, ordinary visuals on 0, and VFX on 12. Preserve authored semantic child layers.

Never call `CombatPhysics.SetLayerRecursively` on an Invector hierarchy. Never import Invector ProjectSettings. Leave mixed upstream layers 13 and 15 unnamed unless a new audited design explicitly remaps them.

## MainMenu and Portraits

`BrawlerPreviewAdapter` is the only gameplay-prefab preview boundary.

For a menu or portrait preview it:

1. resolves only the exact inactive production Human prefab assigned to the definition;
2. validates exact Human identity and the shared controller/AOC topology;
3. neutralizes every Behaviour, Collider, Rigidbody, and Rigidbody2D while inactive;
4. activates the clone and neutralizes it again to contain `Awake` side effects;
5. enables only the root Animator with root motion off;
6. exposes semantic idle and victory presentation;
7. fails closed and deactivates the clone if any invariant changes.

`MainMenuFlow` uses this adapter for selected-character idle/victory presentation. `PortraitStudio` renders portraits from the same exact Human prefabs. Neither path opens a gameplay runtime gate, player input, AI, health, combat, physics, or camera authority.

## Generated Asset Ownership

Do not hand-edit generated outputs.

| Owner | Outputs |
|---|---|
| `WizardAssetBuilder` | Cinder/Rime/Tempest source wizard assets |
| `InvectorMigrationPilotBuilder` | shared lifecycle controller, Cinder AOC/pilot/Human/AI/staff/IK, isolated lab |
| `InvectorRimeMigrationBuilder` | Rime AOC/pilot/Human/AI/staff/IK |
| `InvectorTempestMigrationBuilder` | Tempest AOC/pilot/Human/AI/staff/IK |
| `InvectorThornMigrationBuilder` | Thorn AOC/pilot/Human/AI/bow/string/IK |
| `ArenaSceneBuilder` | roster, Arena, NavMesh, minimap, portraits, build registration |
| `MenuSceneBuilder` | MainMenu |
| `PortraitStudio` | roster portrait textures/import settings |

Regenerate in that dependency order. Run affected builders twice and require stable GUIDs and identical intended topology. Unity YAML local IDs/order may change; byte identity is not the acceptance criterion.

Builders must preserve the caller's active scene and dirty state. Scene builders intentionally own their target scenes but must not silently discard unrelated in-memory scene changes.

## Build Scenes

Register MainMenu first when it exists and Arena second. Generated scenes must serialize only exact Invector Human/AI roster references and contain no obsolete backend, rollout, prefab, or animation-state fields.

## Vendor Maintenance

The imported package is Invector 2.6.6. Keep project integration under BrawlArena source/output paths. If a vendor source patch is unavoidable, keep it minimal and record it in `Docs/VendorPatches/Invector-2.6.6.md`.

Never run `Invector > Import ProjectSettings`.

On any Unity or Invector upgrade:

1. preserve a restorable vendor baseline and provenance;
2. diff the vendor tree;
3. rerun both project audit scripts;
4. inspect all three bundled PDF manuals, including diagrams/screenshots;
5. recheck compatibility patches, subclass overrides, controller/template topology, layers/masks, and Input System assumptions;
6. regenerate every owned prefab/controller/IK/scene/portrait;
7. run the complete validation matrix before accepting the upgrade.

## Removal Guardrail

The following are prohibited in active code and generated assets:

- `BrawlerBackend`;
- `LegacyBrawlerCharacterAssembler`;
- `LegacyBrawlerMotor`;
- `LegacyBrawlerNavigation`;
- `LegacyBrawlerAnimationDriver`;
- character `prefab`, backend, animation-suffix/state, or attack-state fields in `BrawlerDefinition`;
- Human/AI rollout flags or roster selectors in `GameFlow`;
- runtime fallback component creation;
- old Legacy production prefabs/controllers/scenes;
- tests whose desired behavior is Legacy selection or rollback.

Historical documentation may retain those names only to explain the migration record.

## Definition of Done

Do not infer completion from static code or historical test counts. Require current evidence for:

1. package/settings audits passing;
2. Unity domain reload with zero compile errors;
3. exact four-roster Human/AI assets and idempotent regeneration;
4. focused Invector-only and affected subsystem tests;
5. full EditMode suite;
6. live Arena proof for all Human selections and AI equivalents;
7. movement, aim, weak attack, Super, hit, status, knockback, KO, respawn, victory, AI pathing, and teardown;
8. all staff/bow IK poses and Thorn string/Arrow2 presentation;
9. MainMenu idle/victory previews and regenerated portraits;
10. build settings and missing-reference audit;
11. one-owner motor/navigation/Animator/input/camera/health/damage invariants;
12. zero residual Legacy runtime symbols, assets, serialized references, or tests.

Record current result XML/run IDs and console evidence in the implementation hand-off. Do not rewrite this architecture document with phase-local pass counts.
