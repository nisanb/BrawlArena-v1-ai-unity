# Invector 2.6.6 Reference for BrawlArena

Use this reference for the installed Asset Store package only. Re-audit and revise it when the package or Unity version changes.

## Installed Baseline

- Unity: `6000.3.7f1`
- Invector package: `2.6.6`
- Vendor root: `Assets/Invector-3rdPersonController`
- Active input handling: Input System Package (New)
- Approved project root layer: 23, `InvectorPlayer`
- Combined controller: `Assets/Invector-3rdPersonController/Shooter/Animator/Invector@ShooterMelee.controller`
- No-inventory template: `Assets/Invector-3rdPersonController/Shooter/Prefabs/Player/vShooterMelee_NoInventory.prefab`

Run `scripts/audit-invector.ps1` for current counts and compatibility patterns. Do not copy inventory counts into durable instructions because package reimports may change them.

## Bundled Documentation

Read the relevant manual before changing a vendor-derived subsystem:

- `Assets/Invector-3rdPersonController/Basic Locomotion/Documentation_BasicLocomotion.pdf`
- `Assets/Invector-3rdPersonController/Melee Combat/Documentation_MeleeCombat.pdf`
- `Assets/Invector-3rdPersonController/Shooter/Documentation_Shooter.pdf`

The PDFs contain diagrams and Inspector screenshots that text extraction may omit. Inspect rendered pages as images when determining setup order, component references, Animator layers, masks, or weapon configuration. Verify every conclusion against installed source and serialized assets; the package code is authoritative for this exact version.

## Unity 6000.3 Compatibility Patches

Preserve and re-check these package-source compatibility corrections:

- P001: `vThirdPersonInput.cs` and `vShooterManager.cs` originally used an incompatible one-argument `FindObjectsByType` call under `UNITY_6000_2_OR_NEWER`.
- P002: `vInvectorIcon.cs` originally subscribed to an unavailable `EditorApplication.hierarchyWindowItemByEntityIdOnGUI` callback under `UNITY_6000_3_OR_NEWER`.

The audit script detects the original bad patterns. It does not prove Unity compilation. After a reimport or upgrade, inspect the vendor diff, rerun the audit, allow a domain reload, and inspect the live console.

Keep unavoidable vendor changes minimal and record them in `Docs/VendorPatches/Invector-2.6.6.md`.

## Package Architecture Used by BrawlArena

The project deliberately uses a narrow subset of Invector:

| Invector area | Project use |
|---|---|
| `vThirdPersonController` / motor | Project subclass supplies one Rigidbody locomotion and Animator path |
| `vShooterMeleeInput` | Project subclass supplies one guarded scheduler and suppresses physical/vendor action readers |
| Shooter+Melee Animator graph | Project-owned controller copy/AOCs preserve layers, parameters, tags, behaviours, and transitions |
| Melee manager | Project terminal subtype accepts presentation timing but never activates vendor damage |
| IK solver/data | Project-owned staff/bow adjustment assets drive visual-only two-arm IK |
| Shooter/melee/ammo/collect managers | Serialized only where the inherited stack requires references; kept disabled and inert |

Do not activate vendor camera, health, damage receivers, ragdoll, inventory UI, projectile control, sample AI, ammo, reload, collect, or weapon damage merely because a template includes them.

## Input and Scheduler

Stock Invector 2.6.6 reads legacy `UnityEngine.Input` through `GenericInput` and performs camera/HUD discovery. BrawlArena is Input System only and already owns higher-level gesture semantics.

Use `InvectorShooterMeleeInputAdapter`:

- replace the exact `vShooterMeleeInput` template component;
- keep copied `GenericInput.useInput` disabled;
- suppress vendor Start/camera/HUD/root-motion/action/shooter input paths individually;
- use buffered production movement supplied by `InvectorBrawlerMotor`;
- call the guarded inherited fixed scheduler exactly once;
- require the complete controller, Animator, Rigidbody, CapsuleCollider, motor bridge, and presentation firewall before scheduling;
- fail closed when a prerequisite disappears;
- avoid camera discovery and physical input reads in production.

Do not infer safety from `enabled = false` alone. Ordinary public calls and Animator StateMachineBehaviours can reach disabled components.

## Controller and Animator

Use `BrawlInvectorThirdPersonController`, not the stock controller directly. Keep:

- root motion disabled;
- locomotion type and speed configured by the project motor;
- vendor stamina mutation neutralized;
- fall damage, health recovery, health mutation, and auto-crouch paths suppressed;
- approved Animator state listeners registered exactly once;
- weak, strong, recoil, and lifecycle writes routed through project semantic methods;
- one `UpdateAnimator` call per approved fixed scheduler tick.

Preserve the complete combined shooter+melee Animator graph. Parameter names alone are insufficient: state-machine behaviours, tags, transitions, layers, Avatar masks, and state timing are part of the runtime contract.

The shared project lifecycle graph has these layers in order:

1. `Base Layer`
2. `RightArm`
3. `LeftArm`
4. `OnlyArms`
5. `UpperBody`
6. `UnderBody`
7. `Shot`
8. `FullBody`

Keep project lifecycle triggers/states for death, respawn, and victory. Keep lifecycle state markers trace-only and root motion off.

Use AnimatorOverrideControllers for roster-specific clips. Do not rebuild a simplified graph from wizard state names and do not hand-edit generated controllers.

## Melee Presentation Firewall

Vendor Animator behaviours may call `vMeleeManager.SetActiveAttack` even when the component is disabled. Use `BrawlInvectorMeleePresentationManager` as the exact manager type and keep both attack-window overloads terminal.

Require:

- no vendor base combat call from weak, strong, or recoil presentation;
- no attack member or weapon damage activation;
- no reload cancellation through strong attack;
- empty/null damage-source references as defense in depth;
- Brawl attack timing and damage outside the Animator graph.

## Shooter, Ammo, and Inventory

Stock shooter paths consume ammo, control reload, aim through package camera assumptions, and create vendor projectiles. Keep them unavailable.

`vShooterManager`, `vAmmoManager`, and `vCollectShooterMeleeControl` may remain serialized to satisfy inherited references, but runtime gates must keep them disabled and inert. Do not equip a `vShooterWeapon`, enable `vProjectileControl`, or let inventory/equipment UI become authoritative.

Use the project's weapon presenter only for visual aim, muzzle, visibility, and IK. Launch Brawl projectiles from the existing pool and combat code.

## IK and Weapon Presentation

Use project-owned `vWeaponIKAdjust` and list assets with explicit Humanoid bones. Keep exactly four pose records:

- standing;
- standing aiming;
- crouching;
- crouching aiming.

Use `InvectorBrawlerWeaponPresentation` to select records and run guarded `vIKSolver` instances. Keep helper objects out of production topology and keep the presenter free of Animator parameter writes.

Respect Animator tags that disable all IK or support-hand IK. Fail presentation closed when the Animator, Avatar, bones, controller, weapon data, or solver prerequisites are unavailable.

For staff characters, use their own authored staff, `SpellOrigin`, muzzle color, presentation prefab, and IK assets. Do not share character-specific presentation through Cinder merely because the lifecycle controller is shared.

For Thorn, use `InvectorBowPresentationRig` in addition to the generic presenter. Preserve:

- `Bow2`/Bow02 on the left weapon socket;
- authored right-hand `Arrow2` staging;
- exact nock and tip references;
- `StringTop`, `StringRest`, and `StringBottom` anchors;
- a three-point local-space `LineRenderer`;
- aim draw, visibility, reset, and bounded release hold;
- weak `Attack01_Bow` and strong/Super `Attack02_Bow` overrides;
- Brawl projectile authority.

## AI Navigation

Do not use Invector's bundled simple melee AI for gameplay decisions. It is not BrawlArena's tactical layer and the installed package does not provide the needed shooter AI behavior.

Use one `InvectorBrawlerNavigation` with one child `NavMeshAgent` as planning-only state:

- disable `updatePosition`, `updateRotation`, `updateUpAxis`, and automatic off-mesh traversal;
- keep the agent disabled in prefab assets;
- open it through the AI runtime gate only after the root physics/controller stack is ready;
- feed desired velocity to the root Invector motor;
- re-anchor `nextPosition` to the Rigidbody root;
- cancel requests on external displacement and teleport;
- fail closed on invalid/stale mesh and off-mesh conditions.

Unity does not serialize every native `NavMeshAgent` runtime property into prefab YAML. Keep a durable builder marker/topology check and assert the actual properties live.

## Health and Damage Hazard

Invector health is coupled to death events, collider/Rigidbody state, ragdoll, and component lifecycle. Do not mirror or synchronize Brawl `Health` into vendor health.

Keep attack-receive and public health/stamina mutation paths suppressed or terminal. Route all damage through one Brawl target gateway and use actual applied damage returned by Brawl `Health`.

## Camera Hazard

Template input expects an Invector third-person camera and may discover cameras/HUD by tag or singleton. Keep those paths suppressed. Use `BrawlCamera` for chase, stable yaw, aim basis, obstruction, and shake.

## Template and Copying Rules

Treat `vShooterMelee_NoInventory.prefab` as an upstream configuration reference, not a production prefab.

When a builder copies template configuration:

- copy only audited root component state;
- replace stock controller/input/manager types with project subtypes;
- remap serialized object references in two passes;
- clear references to omitted vendor children;
- omit cameras, UI, inventory, ragdoll, damage receivers, weapons, and projectiles;
- preserve the target character's Humanoid skeleton and Avatar;
- save project-owned inactive prefabs outside the vendor tree;
- validate the completed topology before activation.

## Project Settings and Layers

Never run `Invector > Import ProjectSettings`. The package operation replaces whole settings files, conflicts with BrawlArena layers, and can introduce legacy-input assumptions.

Keep the approved layer model:

- 8 `Ground`
- 9 `WorldBlocker`
- 10 `BrawlerHitbox`
- 11 `Projectile`
- 12 `VFX`
- 23 `InvectorPlayer`

Keep layer 23 root-only on production actors. Preserve selective child layers. Do not recursively set an Invector hierarchy to `BrawlerHitbox` or `InvectorPlayer`.

Leave upstream-mixed layers 13 and 15 unnamed unless a future audited subsystem establishes a project-wide semantic remap. Do not add package tags or axes speculatively.

## Upgrade Checklist

On any Invector or Unity upgrade:

1. preserve a restorable copy/provenance record for the imported package;
2. diff every vendor source and serialized asset change;
3. reread relevant PDFs, including their images;
4. recheck P001/P002 and all project subclass overrides against new signatures;
5. inspect combined controller, templates, state behaviours, tags, parameters, layers, IK data, and managers;
6. rerun settings/layer/mask audits;
7. regenerate every project-owned variant and scene;
8. run the complete static, EditMode, live Arena, menu, and visual validation matrix;
9. update this reference and the vendor patch ledger.
