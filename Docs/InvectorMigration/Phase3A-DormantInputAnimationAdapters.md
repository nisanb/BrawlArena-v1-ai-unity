# Phase 3A - Dormant Input and Animation Adapter Stack

Status: implemented, hardened, and validated; Phase 3B later completed its separate lab-only gate  
Implemented: 2026-07-13

## Outcome

The generated Cinder pilot now contains a project-owned Invector controller boundary,
Unity Input System adapter, and semantic animation driver. The stock
`vThirdPersonController` and `vShooterMeleeInput` components were replaced by project
subclasses in the builder output, not retained beside them.

This remains a dormant integration artifact. The prefab root is inactive, its Animator,
controller, adapter, managers, collider, and animation driver are disabled, and its
Rigidbody is frozen and kinematic. Every production roster entry remains
`BrawlerBackend.Legacy`, and `BrawlerBackend.Invector` still fails before instantiation.

## Project-Owned Runtime Boundaries

Added under `Assets/Scripts/Brawl/Integration/Invector/`:

- `BrawlInvectorThirdPersonController.cs`;
- `InvectorShooterMeleeInputAdapter.cs`;
- `InvectorBrawlerAnimationDriver.cs`.

`InvectorMigrationPilotBuilder` remains the sole source of truth for the prefab,
AnimatorOverrideController, and additive lab. It now maps the audited vendor controller
and input configuration into the project subclasses, remaps same-root serialized
references, adds/configures the semantic driver, and reapplies the dormant safety policy.
The focused EditMode tests remain in
`Assets/Editor/BrawlAutomation/InvectorMigrationPilotEditModeTests.cs`.

The generated asset GUIDs did not change:

- AnimatorOverrideController: `073c25839c579d54e95b3afd2edd8f13`;
- prefab: `12b31ba25708e04489318559f291b366`;
- lab scene: `91742eec95d6352499529bebc9dedea0`.

Two consecutive hardened builds preserved all three GUIDs and produced byte-identical
outputs:

- AnimatorOverrideController SHA-256: `08A30491109E5126B6C1F55F721A6094D445B005686099A4AFDE49D05A3DD4E6`;
- prefab SHA-256: `4807E9AF30FCF9AB301E794B040918D3D0E9ABC72B4435596CA8B1FBA6B7047F`;
- lab scene SHA-256: `B7232866F2ACE89874F55CFCD4D36565ACC583C0B22D9FD8185806937F97A923`.

## Dormant Topology and Authority

The root contains exactly one of each project boundary:

- `BrawlInvectorThirdPersonController`;
- `InvectorShooterMeleeInputAdapter`;
- `InvectorBrawlerAnimationDriver`.

It also retains one disabled `vShooterManager`, `vMeleeManager`, `vAmmoManager`, and
`vCollectShooterMeleeControl`. There is no exact stock `vThirdPersonController`, exact
stock `vShooterMeleeInput`, `LegacyBrawlerAnimationDriver`, `BrawlerController`,
`PlayerBrawlerInput`, Brawl `Health`, template camera, HUD, inventory, damage receiver,
weapon hierarchy, or ragdoll hierarchy on the prefab.

The dormant authority graph is still empty:

```text
inactive prefab root
  -> no Unity lifecycle or scheduler executes
  -> disabled project input adapter
  -> disabled project controller and managers
  -> disabled Animator and semantic animation driver
  -> no movement, input, Animator, health, damage, camera, or lifecycle authority
```

## Audited Input Lifecycle

The adapter uses two independent gates: the component must be enabled and
`RuntimeSchedulingEnabled` must be true. That gate can only be opened in Play mode on a
complete, active, same-root stack with one active `BrawlCamera` in the same scene, an
enabled Animator/controller/capsule, and a non-kinematic Rigidbody that is not frozen on
every axis. Any incomplete scheduled frame clears movement intent, closes the gate,
disables the adapter, and logs an error.

| Surface | Phase 3A disposition | Phase 3B-approved effect |
|---|---|---|
| Controller `Awake` / fixed timestep | The inherited motor `Awake` dynamically calls the project override. | `SetCustomFixedTimeStep` forces `Default` and never writes global `Time.fixedDeltaTime`. |
| Adapter `OnEnable` / `OnDisable` | Disabled on the prefab; both clear or close runtime state. | A disabled/re-enabled adapter cannot retain movement or an open scheduler gate. |
| `Start` | Overrides the complete shooter/melee/base chain and never calls vendor `Start`. | Once gated, calls only `BrawlInvectorThirdPersonController.Init`; it creates no camera, HUD, cursor state, AnimatorMove sender, or shooter aim helper. |
| `CharacterInit`, `FindCamera`, `FindHUD`, `UpdateHUD` | Terminal no-ops. | No template camera/HUD discovery or destruction path can execute. |
| `Update` | Gated project override. | Calls only the project `InputHandle`; no vendor HUD or input loop executes. |
| `FixedUpdate` | Gated project override with exactly one audited `base.FixedUpdate()` call. | Schedules `Physics.SyncTransforms`, `cc.UpdateMotor`, locomotion control, project rotation, and `cc.UpdateAnimator` once. |
| `LateUpdate` | Terminal override that clears `updateIK`. | No melee polling, template camera control, shooter aim, or IK executes. |
| `InputHandle` / `MoveInput` | Project-owned implementations only. | Samples the existing `BrawlHUD` joystick, then reads the project `Player/Move` Input Action once; action input overrides HUD input before writing `cc.input`. |
| `ControlRotation` | Project-owned implementation. | Uses only the explicitly supplied, active, same-scene `BrawlCamera`; there is no tag-discovered or `Camera.main` fallback. |
| Root motion | `UseAnimatorMove`, enable/disable helpers, and `OnAnimatorMoveEvent` are terminal no-ops. | Root motion cannot become a second movement authority. |
| Locomotion action inputs | Sprint, crouch, strafe, jump, and roll readers are terminal no-ops. | Phase 3B is movement-only until each behavior receives an explicit design. |
| Melee/shooter inputs | Attack polling, block, aim, shot, reload, camera-side, scope, and shot helpers are terminal no-ops. | Brawl combat remains the only gameplay input/action path. |

All copied `GenericInput.useInput` flags are also false. Source guards prove that the
adapter contains no `UnityEngine.Input` polling and calls vendor bases only for the one
fixed scheduler and the three guarded semantic presentation APIs.

Inherited camera/HUD discovery, cursor mutation, camera-state changes, camera/shooter
lock events, aim-canvas resolution, scope, and aiming-cancellation surfaces are terminal
project overrides. This matters even with legacy inputs disabled because several vendor
lock methods invoke UnityEvents or resolve global UI objects without reading an input
axis.

Three vendor surfaces cannot be cleanly overridden in 2.6.6. Controller `Init` is
monolithic and initializes health/stamina fields with required motor state; those later
resource paths are neutralized instead. `SetLockMeleeInput` is non-virtual, so the
adapter permanently pins `lockMeleeInput`. Retained base `FixedUpdate` invokes the C#
`onFixedUpdate` event; base `Start` is suppressed. Phase 3B subsequently proved at
activation and continuously that no external subscriber is attached.

### Current Input System behavior

`ResolveMoveIntent` samples the current `BrawlHUD` joystick and reads the project
`Player/Move` Input Action exactly once per `Update`; nonzero action input overrides the
HUD value and the result is clamped to magnitude one. Keyboard and gamepad bindings are
owned by the project action asset, so the adapter performs no direct device polling.
The generated lab has no BrawlHUD, making the project action its one effective source.
Unity 6 may already enable this project-wide action, so teardown disables it only when
the adapter itself enabled it. This is a deliberately small lab locomotion seam, not a
claim of complete production input migration. Phase 3C-C later added an explicit
`BufferedMotor` feed whose scheduled path returns before the HUD/action lookup, but only
for the same isolated human lab; no production actor or assembler selects it.

The configured `Assets/InputSystem_Actions.inputactions` asset and its `Player/Move`
action remain valid baseline evidence, including keyboard and gamepad bindings, but the
adapter does not adopt the whole action map in Phase 3A. Its attack binding does not
encode Brawl's tap/drag/release contract, and the map has no Super or Ward Step actions.
Phase 3B live-proved movement through a virtual gamepad. Attack gestures, Super, and Ward
Step input remain owned by production `PlayerBrawlerInput` and behind the Phase 3C
facade/motor intent-integration gate.

## Tag, Layer, and Timestep Resolution

`AutoCrouch` is not defined in the project. Merely disabling auto-crouch would not be
safe because vendor `OnTriggerStay` and `OnTriggerExit` still call `CompareTag`. The
project controller overrides both trigger callbacks, invokes the retained
`onActionStay`/`onActionExit` events directly, and never enters either vendor comparison.
Its auto-crouch layer is also serialized to zero.

The approved serialized custom-tag list remains empty. Live `Ignore Ragdoll` presence is
neither required nor forbidden; it is editor/import state rather than a deterministic
project contract. The project adapter avoids the dependency by not calling
`vShooterMeleeInput.Start`, so it does not create or tag the vendor `aimAngleReference`
helper. Shooter aim and IK initialization remain deferred.

Layer 23 `InvectorPlayer` remains root-only, layers 13 and 15 remain unnamed, Ground is
layer 8, WorldBlocker is layer 9, and every damage/stop-move mask remains disabled as in
Phase 2. No project setting changed in Phase 3A.

The project controller's terminal `SetCustomFixedTimeStep` override provides a second
line of defense beyond serialized `Default`: copied or future Inspector data cannot make
this pilot change the global physics timestep when its inherited `Awake` runs.

## Semantic Animation Dispositions

At the Phase 3A cutoff, `InvectorBrawlerAnimationDriver` was a disabled component
implementing the existing seven-method `IBrawlerAnimationDriver` contract. Presentation
requests required a same-root active stack and an open Play-mode presentation gate.
Shooter/melee managers remained disabled and data-only. Dormant mapped action/reaction
requests threw rather than silently no-op, while lifecycle requests were explicitly
unsupported. The table below records that historical Phase 3A exit state.

| Semantic request | Phase 3A mapping |
|---|---|
| `TickLocomotion` | Permanent intentional no-op. The single fixed scheduler and `vThirdPersonAnimator` own locomotion parameters. |
| `PlayBasicAttack` | Guarded public `InvectorShooterMeleeInputAdapter.TriggerWeakAttack()`. |
| `PlaySuper` | Guarded public `InvectorShooterMeleeInputAdapter.TriggerStrongAttack()`. |
| `PlayHitReaction` | Guarded public `InvectorShooterMeleeInputAdapter.OnRecoil(hitReactionId)`. |
| `PlayDeath` | Phase 3A threw `NotSupportedException`; no approved presentation-only Invector 2.6.6 API existed. |
| `PlayRespawn` | Phase 3A threw `NotSupportedException`; Invector health/reset was not accepted as Brawl respawn presentation. |
| `PlayVictory` | Phase 3A threw `NotSupportedException`; generic action-state/CrossFade APIs were not an approved semantic mapping. |

The driver contains no raw Animator parameter, state, `Play`, `CrossFade`, health,
damage, or shot call. Phase 3B live-proved basic, Super, and exact recoil mappings;
Phase 3D-A removed their vendor combat-base/reload coupling; and Phase 3D-B subsequently
added nonthrowing project Death/Respawn/Victory Trigger semantics plus dormant-drop and
failure-isolation behavior. See `Phase3D-B-LifecyclePresentation.md`. Production selection
remains closed; Phase 3D-C subsequently completed the isolated weapon/IK and selective-
collision firewall, but no production assembler exists.

## Ward Flow, Invector Stamina, Health, and Damage

`BrawlerController.Stamina`/`WardFlow` remains the only gameplay resource. Ward Step
spending, regeneration, skills/progression, HUD values, death, and respawn continue to
use it. Invector's `currentStamina` is only an internal motor field and is not exposed to
Brawl gameplay or UI.

The combined Animator contains `vMeleeAttackControl`, whose state entry normally calls
`vMeleeCombatInput.OnEnableAttack` and subtracts an Invector melee stamina cost. The
project adapter overrides that callback without calling the vendor base: it marks only
presentation attack state and cancels sprint. The builder also serializes the dormant
melee manager's default stamina cost and recovery delay to zero. Sprint, jump, roll,
blocking, and inherited attack-input stamina paths are suppressed.

`OnReceiveAttack` throws `NotSupportedException` and never calls `cc.TakeDamage`.
The retained scheduler can otherwise reach fall-damage, health-recovery, and
stamina-recovery/consumption callbacks, so `BrawlInvectorThirdPersonController`
terminally disables those paths, pins motor-local stamina full, and throws from public
Invector health/stamina mutation methods. Invector controller health remains
immortal/dormant, external package damage components are absent, and Brawl `Health`
remains the sole future gameplay damage/death authority.

The builder also clears shooter damage/block-aim masks, lock/aim/camera-recoil flags,
weapon and IK references, ammo data/item-manager/list references, melee members/weapons,
damage tags/recoil, and default melee stamina costs. These are serialized defense in
depth; they do not authorize any of those subsystems in Phase 3B.

## Validation Evidence

- Unity 6000.3.7f1 post-hardening compilation/domain reload: passed;
- focused Phase 3A direct fixture: 8 passed, 0 failed, 0 skipped/inconclusive;
- full plain-NUnit work-item regression forced onto Unity main thread 1: 156 discovered and passed, 0 failed, 0 skipped/inconclusive;
- two consecutive hardened builder runs: all output GUIDs and all three output byte hashes stable;
- serialized-reference audit: no missing references and no retained reference into the vendor template hierarchy;
- source/call-path guard: one allowed `base.FixedUpdate`, no legacy input poll, raw Animator write, health mutation, or shot execution;
- Phase 0B deterministic scan, repeated identically: 435 candidates, 432 YAML files, 3 skipped non-YAML files, 9,380 layer assignments, 14,940 mask findings, 8,079,954 bytes, SHA-256 `FA07B2A6EE5C6255749D23D411E010A4FFBE9BD3580A939B0024D5DCD99A9724`;
- scan rebaseline from `07D7...`: normalized layer assignments unchanged; generated `autoCrouchLayer` hardened from `512` to `0`, generated `blockAimLayer` from `1` to `0`, with remaining raw-report changes limited to generated YAML line/local-ID/document-order evidence;
- settings hashes unchanged and matched the approved manifest:
  - `ProjectSettings.asset`: `66E4F4F7AAC7051E3D6B40E77DE56FBAC62330BA8ECD405325DE301087FD7B49`;
  - `TagManager.asset`: `48128C81F5BE9AFA7577EDD05E95581076B93D7BA67A056A3F3DE5575D7273DD`;
  - `InputManager.asset`: `7A1BB836A1008D26D0860D143E9D9DF7CA41731D30A72901B518063819C62DF2`;
  - `DynamicsManager.asset`: `B7C9A9DC08B98990109D4346F68745E15E238DE400F96C583E53E1C83C743A78`;
- resolver/roster guard: `BrawlerBackend.Invector` remains closed and every generated/current roster entry remains Legacy;
- active scene and dirty flag: preserved across the validation sequence;
- final Unity Console confirmation: 0 errors after the test run.

Phase 3A remains complete as the dormant/static gate. Phase 3B later activated only the
generated lab instance and passed its separate live gate; the prefab remains inactive,
production remains Legacy, and the resolver remains closed.

## Rollback

Production rollback still requires no selector or roster change. Source rollback removes
the three project integration components, restores the builder's Phase 2 exact controller
and stock input mappings, and regenerates the same three isolated outputs. No scene,
roster, camera, health, combat, or project-settings rollback is required.

## Later Phase 3B Result and Next Boundary

Phase 3B activated only the isolated lab instance, kept the prefab asset inactive, and
replaced the plain framing camera with exactly one configured `BrawlCamera`.

The live gate must prove:

- one Input System reader and one `FixedUpdate` motor/Animator scheduler;
- one coordinated Invector Animator writer stack and no `TickLocomotion` duplicate;
- no executed `UnityEngine.Input`, GenericInput, template camera/HUD, root-motion, aim/IK, or global-timestep path;
- Brawl Ward Flow remains authoritative and no Invector stamina-consumption path executes;
- basic attack, Super, and hit-reaction mappings enter and leave the expected graph states without enabling hitbox/projectile damage;
- Invector health, damage, inventory, equipment, production assembly, and lifecycle remain disabled;
- every production roster remains Legacy and the resolver remains closed.

That gate is now recorded in `Phase3B-LiveInputAnimationLab.md`. Phase 3C-A subsequently
extracted and proved the behavior-preserving Legacy `IBrawlerMotor`; see
`Phase3C-A-LegacyMotor.md`. Phase 3C-B subsequently extracted and proved Legacy
navigation; see `Phase3C-B-LegacyNavigation.md`. Phase 3C-C subsequently completed the
isolated human buffered motor/adapter/builder/live-lab bridge; see
`Phase3C-C-InvectorMotorCore.md`. It did not add a production selector/assembler, live
`BrawlerController`/`PlayerBrawlerInput`/`Health` lifecycle, or Invector AI navigation.
Phase 3D-A subsequently completed the isolated attack/reload and melee-window firewall.
Phase 3D-B then completed isolated lifecycle presentation and facade/match presentation-
failure isolation; see `Phase3D-B-LifecyclePresentation.md`. The production selector
remains unchanged. Phase 3D-C subsequently completed the isolated weapon/IK and
selective-collision boundary; see `Phase3D-C-WeaponIKSelectiveCollision.md`. The next
blocker is the context-gated production human assembler, followed by AI and roster work.
