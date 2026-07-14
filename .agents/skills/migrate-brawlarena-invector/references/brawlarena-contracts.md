# BrawlArena Contracts Under Invector

Treat these as acceptance criteria unless the user explicitly requests a gameplay redesign.

## Actor Identity and Assembly

- Keep one `BrawlerController` instance as the actor identity for the lifetime of a spawned actor.
- Route every spawn through `BrawlerCharacterAssembly` and an exact Human or AI Invector assembler.
- Let the default assembly context derive the role from `asHumanPlayer`; use explicit Invector contexts only for validation or controlled construction.
- Require a non-empty roster ID and an exact inactive prefab whose root `InvectorBrawlerPrefabIdentity` matches both roster ID and role.
- Reject missing, duplicate, off-root, or competing motor, navigation, animation, input, AI, physics, or runtime-gate authorities before activating the clone.
- Configure `Health`, facade stats, motor, animation driver, weapon presenter, and navigation while dormant. Open the role-specific runtime gate transactionally.
- Destroy or deactivate a partially assembled clone on failure. Never return a half-open actor.

Do not reintroduce a backend enum, a Legacy assembler, fallback component creation, rollout selectors, or alternative character prefab fields.

## Brawler Facade and Interfaces

Keep `BrawlerController` as the stable gameplay-facing facade. Route controller infrastructure through:

- `IBrawlerMotor`: desired movement, facing, grounded/collision measurements, velocity, displacement/knockback, Ward Step, stop/suspend, constraints, and teleport;
- `IBrawlerNavigation`: AI sampling, destination requests, path state, desired velocity, clearing, and external-displacement handoff;
- `IBrawlerAnimationDriver`: locomotion handoff plus semantic basic attack, Super, hit reaction, death, respawn, and victory;
- `IBrawlerWeaponPresentation`: visual aim/release, muzzle, visibility, reset, and arm IK;
- `IBrawlerCharacterAssembler`: exact role-specific actor construction.

Require one component-backed owner per interface on the root, except the AI planner `NavMeshAgent`, which must be one disabled transform-neutral child. Do not install a runtime fallback when a required owner is missing.

## Health and Match Semantics

Keep `Health` authoritative:

- preserve `Max`, `Current`, `IsDead`, and `Invulnerable`;
- preserve `Damaged`, `Healed`, `Died`, and `Changed` event ordering;
- preserve death idempotence, sender/killer identity, revive, spawn protection, and invulnerability;
- use the actual delta returned by `TakeDamage` for telemetry, Super charge, feedback, chaining, and match guards.

Never mirror Brawl health into Invector health. Vendor health events, death, ragdoll, collider mutation, and Rigidbody mutation must remain unreachable.

Contain presentation failures. Animation, IK, muzzle, menu, and victory presentation must not abort attack timing, projectile launch, damage, KO, revive, respawn, or `MatchEnded`.

## Player Input and Aim

Keep `PlayerBrawlerInput` as the sole production physical-input and player-identity owner. Preserve:

- one cast on release;
- tap auto-aim;
- drag manual aim;
- camera basis latched at gesture start;
- the last valid direction when the pointer returns to the deadzone;
- attack preview origin and distance semantics;
- movement converted through `BrawlCamera` yaw.

Feed its world-space movement intent through `InvectorBrawlerMotor`. In production buffered mode, `InvectorShooterMeleeInputAdapter` must not read BrawlHUD, Input Actions, keyboard, gamepad, `UnityEngine.Input`, or camera state.

## Camera

Keep `BrawlCamera` as the only gameplay camera and aim frame. Preserve stable yaw, obstruction handling, pull-in/hiding, and reduced-motion-aware shake. Do not let a template camera or `Camera.main` discovery path become active.

## AI

Keep `AIBrawler` as the tactical producer for targeting, range management, retreating, kiting/flanking, Ward Step, Gem Grab, XP boxes, and destination choice.

Use `InvectorBrawlerNavigation` only as a planner:

- keep one child `NavMeshAgent` with transform writes and automatic off-mesh traversal disabled;
- let the root Rigidbody/Invector motor own physical movement and facing;
- re-anchor planner simulation to the root at the synchronization boundary;
- cancel durable destinations immediately on external displacement or teleport;
- expose zero desired velocity when no valid request/path exists;
- permit one bounded replan for a stuck request, then fail closed with a same-destination cooldown;
- allow a materially different destination to recover immediately;
- fail closed on stale mesh and occupied off-mesh cases.

Do not add Invector sample melee AI or a second root motion authority.

## Movement and Physics

Keep one approved fixed scheduler. The adapter may call the inherited Invector fixed scheduler once; no other component may run a second controller/locomotion/rotation/Animator update loop.

Preserve Brawl-facing movement contracts through `InvectorBrawlerMotor`:

- finite normalized desired intent and configured move speed;
- grounded state, velocity, and collision radius measurements;
- facing and rotation independent of physical input readers;
- nested external displacement ownership for knockback and Ward Step;
- stop/freeze and deterministic teardown;
- teleport that clears motion and keeps planner/root state coherent;
- root motion disabled.

Use Rigidbody plus CapsuleCollider. Reject `CharacterController` on production actors.

## Ward Flow and Action Resources

Keep Brawl `Stamina` as Ward Flow for Ward Step, with project spend validation, regeneration delay/rate, HUD, progression modifiers, and reset behavior. Keep basic attacks on Brawl's three-charge sequential reload plus cooldown/action locks, and keep Supers on `SuperCharge`. Failed or blocked casts never spend a charge; Ward Step and Supers never touch the basic-attack pool.

Do not enable Invector stamina consumption, recovery, ammo, reload, or inventory resource paths.

## Animator and Lifecycle

Let the Invector controller stack own locomotion parameters and state polling. Keep `IBrawlerAnimationDriver.TickLocomotion` from creating a second parameter loop.

Route one-shot presentation semantically:

- weak/basic attack;
- strong/Super attack;
- exact recoil/hit reaction;
- `BrawlDeath`, `BrawlRespawn`, and `BrawlVictory` lifecycle triggers.

Keep death and victory presentation holding, respawn motionless, root motion off, and lifecycle state markers trace-only. Keep raw Animator hashes, states, and transitions inside concrete project-owned Invector components and generated controller assets.

Treat gameplay actors, menu/portrait previews, and pooled VFX as separate animation domains. A preview may enable only its root Animator after all gameplay/physics components have been neutralized.

## Combat

Keep deterministic Brawl combat authoritative:

- melee selection and timing;
- projectiles, sweeps, obstruction, explosions, and pooling;
- team filtering and target choice;
- burn, poison, slow, healing, hazards, knockback, and Super charge;
- SFX, VFX, damage numbers, feedback, and telemetry.

Keep `BrawlInvectorMeleePresentationManager` terminal. Animator attack-window callbacks may record/end presentation state but must not enable vendor hit sources. Keep shooter, ammo, reload, collect, inventory, and vendor projectile paths inert.

For Tempest chains, seed each hop from the previous target's actual applied damage, multiply by the sanitized chain multiplier, and stop on zero applied damage. Never seed a later hop from requested or overkill-discarded damage.

For Thorn, distinguish authored presentation `Arrow2` from gameplay projectiles. Preserve Brawl `Arrow01` basic and `Arrow02` explosive-Super pooling, targeting, launch, damage, and telemetry.

## Weapon Presentation and IK

Keep weapon presentation visual-only. It may own:

- aim/release pose requests;
- muzzle socket/effect lookup;
- staff or bow visibility;
- standing, standing-aiming, crouching, and crouching-aiming arm IK;
- death hide and respawn reset/show;
- Thorn string deformation and authored Arrow2 nock/release staging.

It must not own attack acceptance, timing, target selection, projectile creation, ammo, reload, damage, health, equipment data, or match state.

Preserve character-specific art:

| Roster | Source presentation |
|---|---|
| Cinder | Staff01 / fire presentation |
| Rime | Staff02 / Frost `SpellOrigin` / cyan muzzle |
| Tempest | Staff03 / Storm `SpellOrigin` / `#B58CFF` muzzle / builder-owned inward 1 cm hand calibration |
| Thorn | Bow02 left-held / right-hand Arrow2 / `BrawlWizardBow` / four bow IK records |

Never recursively assign one layer to an Invector hierarchy. Preserve root layer 23, hit proxy layer 10, ordinary visuals layer 0, VFX layer 12, and authored semantic child layers.

## Generated Content

Treat these builders as sources of truth:

- `WizardAssetBuilder` for generated wizard source art;
- `InvectorMigrationPilotBuilder` for shared lifecycle topology and Cinder variants;
- `InvectorRimeMigrationBuilder`, `InvectorTempestMigrationBuilder`, and `InvectorThornMigrationBuilder` for roster-specific variants, AOCs, weapons, and IK assets;
- `ArenaSceneBuilder` for roster data, Arena, NavMesh, portraits/minimap, and build registration;
- `MenuSceneBuilder` for MainMenu;
- `PortraitStudio` for portrait rendering from exact Invector Human previews.

Update builders and regenerate; do not hand-edit their output. Keep every production prefab asset inactive and preserve stable GUIDs across idempotent rebuilds.
