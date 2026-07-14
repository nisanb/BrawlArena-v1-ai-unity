# Phase 0B — Settings and Serialized-Mask Decision

Status: approved through the validated isolated Phase 3B live lab; production activation remains gated  
Baseline: Unity 6000.3.7f1, Invector Shooter Template 2.6.6  
Approved: 2026-07-13

## Decision

Do not import `Assets/Invector-3rdPersonController/Basic Locomotion/Editor/Resources/vProjectSettings.unitypackage`. It contains full replacements for `InputManager.asset`, `TagManager.asset`, and `DynamicsManager.asset`, while BrawlArena already owns conflicting settings.

The approved Phase 2 settings delta is deliberately narrow:

- keep Active Input Handling at Input System Package only (`activeInputHandler: 1`);
- keep Ground 8, WorldBlocker 9, BrawlerHitbox 10, Projectile 11, and VFX 12;
- add only layer 23, `InvectorPlayer`, for the project-owned pilot controller root;
- add no custom tags and no legacy axes;
- keep `DynamicsManager.asset`, both collision matrices, and solver settings unchanged;
- keep ambiguous layers 13 and 15 unnamed;
- do not assign any production roster entry to Invector yet.

This approval remains sufficient because the prefab asset stays inactive and the only enabled Invector instance is created inside the isolated generated lab. Its runtime gate enables one audited controller/input/Animator stack while package damage, inventory, template camera, production assembly, and roster selection remain disabled.

## Rejected Fixed Remap

The earlier candidate `Player = 13` and `CompanionAI = 15` mapping is rejected.

Layer 13 is not a free package semantic. The package uses it across look-target colliders/helpers, a particle helper, six standalone collectable melee weapons, and standalone arrow/ammunition content. Naming the project slot 13 `Player` would make those numeric assignments silently mean Player whenever a vendor asset is instantiated.

Layer 15 is also mixed. The selected `vShooterMelee_NoInventory` template has 105 layer-15 objects:

- 11 physical ragdoll/damage nodes;
- 2 foot-step sensor objects;
- 10 weapon-holder/helper transforms;
- 8 weapon renderer objects;
- 74 plain bones/handler transforms.

There is no safe blanket source-15 remap. Future BodyPart support must select only genuine receiver/collider nodes by component and path. The Phase 2 builder copies only selected root components onto Cinder and therefore serializes none of this mixed hierarchy.

## Pilot Layer and Tag Contract

`InvectorPlayer` uses previously unused project layer 23. The static pilot assigns it only to the controller root/capsule. Cinder skeleton, renderer, staff, and inactive aura children retain their authored Default layer. No recursive layer helper is permitted.

The pilot root may retain Unity's built-in `Player` tag. The approved serialized custom-tag list remains empty. Live `Ignore Ragdoll` presence is neither required nor forbidden: it may be recorded as editor/import state, but it is not part of the deterministic serialized contract. Phase 3A cannot depend on it because the project adapter terminally replaces the vendor shooter `Start` path that creates the tagged aim helper. `AutoCrouch` is absent, so `BrawlInvectorThirdPersonController` overrides `OnTriggerStay`/`OnTriggerExit` and never reaches either stock comparison. Package-only `Weapon` and `PlayerUI` objects are not copied.

Enemy, CompanionAI, Triggers, StopMove, HeadTrack, BodyPart, BlockAIRayCast, Pushable, CoverPoint, and PostProcess layers remain deferred. Enemy/team tags must never replace Brawl's `TeamId` authority.

## Input and Physics

Production does not change `InputManager.asset`. The 24 package-only legacy axes in `legacy-input-axes.csv` remain absent. Phase 3A installs one disabled `InvectorShooterMeleeInputAdapter` derived from `vShooterMeleeInput` while the root remains inactive. It disables every copied GenericInput and terminally replaces legacy-reading input, camera/HUD, root-motion, melee, and shooter helpers. Phase 3B is the separate isolated live-scheduler gate.

The current schema-13 `DynamicsManager.asset`, solver iterations, callback reuse, cloth fields, and collision matrix remain authoritative. All candidate semantic collision exclusions in `collision-matrix.csv` remain deferred until an enabled pilot has focused tests.

The dormant controller is serialized with:

- `groundLayer = Ground (8)`;
- `autoCrouchLayer = 0`, with the project controller removing both stock tag comparisons;
- `stopMoveLayer = 0`;
- custom fixed timestep `Default`;
- immortal health state;
- shooter damage and melee recoil/damage masks disabled.

The hardened builder also clears the shooter block-aim mask and aim/lock/recoil flags,
all weapon/IK references, the ammo database/item-manager/list, and all melee members,
weapons, damage tags, recoil, and default stamina costs. The project controller disables
fall damage and health recovery, pins its motor-local stamina full, and fails closed on
public Invector health/stamina mutation APIs.

These values are preparation, not runtime authority. In addition, the project controller
overrides `SetCustomFixedTimeStep` so inherited `Awake` cannot change global
`Time.fixedDeltaTime` even if copied serialized data later drifts.

## Reproducible Evidence

The repository scanner covers 435 candidate files: 432 Unity YAML files plus 3 listed binary/non-YAML files. That scope includes the complete vendor YAML tree, the project-owned generated migration assets, and the migration lab scene. It records 9,383 GameObject layer assignments and 14,953 mask/bitfield findings.

After the final Phase 3B regeneration, two consecutive scanner runs produced identical reports:

- paths: `%TEMP%/BrawlArena-InvectorSettings-Phase3B-Final-1.json` and `-2.json`;
- size: 8,084,377 bytes each;
- SHA-256: `D9EF22D949E6DDCB2A6468F289B0DA54F921CF47AFC8B4EAA6D9C0C6477553E9` for both.

Relative to the hardened Phase 3A report, the candidate/YAML/skipped counts and all
approved ProjectSettings hashes remain unchanged. The live lab deliberately adds Step
and Slope on Ground plus Collision Wall on WorldBlocker, raising normalized GameObject
layer assignments by three. Those primitive renderers/colliders add twelve serialized
bitfields and the BrawlCamera adds `obstructionMask = 512`, raising mask findings by
thirteen. No project layer, tag, physics matrix, input setting, or production asset changed.
Builder regeneration changes raw scene local IDs and document order, so scene YAML bytes
are not a determinism gate; stable output GUIDs plus identical intended topology and
serialized-reference tests are the gate.

The raw report remains temporary and reproducible rather than committed. Static decoding is inventory evidence; it does not authorize future runtime subsystems.

## Exit and Activation Gates

Phase 0B remains complete for the isolated Phase 3B lab because the source/mask census is reproducible, ambiguous numeric layers are explicitly rejected, the exact one-layer delta is approved, and no package settings file was imported.

Phase 3A implemented and hardened the source/builder portions of its dormant gate:

1. the stock legacy-input reader and exact stock controller were replaced by project subclasses;
2. the one proposed Animator/motor scheduler and all guarded presentation calls were enumerated;
3. the absent `AutoCrouch` path was eliminated, while live `Ignore Ragdoll` presence or absence is explicitly non-gating and unused;
4. retained LayerMasks and object references passed re-audit;
5. Brawl camera, Ward Flow, health, damage, team, and match lifecycle remain authoritative;
6. the prefab stayed inactive, every roster stayed Legacy, the resolver stayed fail-closed, the hardened source compiled, and two builds preserved all output GUIDs and bytes.

The final focused fixture passed 8/8, the isolated live test passed 1/1, and the complete
EditMode regression passed 157/157 with no skips or inconclusive cases. The final Unity
Console reported zero warnings or errors, and the pre-existing dirty MainMenu state was
restored after every run. Phase 3B proved one Rigidbody/capsule motor, one project action
reader, one `FixedUpdate` scheduler, one approved Animator writer stack, and inert vendor
health/stamina/damage paths. This approval does not open production selection.

The remaining CSVs are retained as audit history and future-scope inventory; only the dispositions explicitly marked Phase 2 approved are active decisions.
