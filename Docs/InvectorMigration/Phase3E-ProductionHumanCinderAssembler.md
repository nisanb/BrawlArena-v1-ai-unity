# Phase 3E - Production Human Cinder Assembler

Status: complete for the explicit, production-shaped human Cinder assembly context on
2026-07-13. The generated Arena rollout switch remains off by default.

## Boundary Completed

Phase 3E proves that the already isolated Invector motor, scheduler, animation, lifecycle,
weapon, IK, and selective-layer stack can run on a real `BrawlerController` actor without
moving BrawlArena's gameplay authority. It adds a production-shaped assembly path, but it
does not globally select `BrawlerBackend.Invector` and it does not migrate AI or another
roster entry.

| Concern | Runtime owner after Phase 3E |
|---|---|
| Human physical input and attack gestures | Brawl `PlayerBrawlerInput` |
| Movement physics and locomotion scheduling | `InvectorBrawlerMotor` plus one buffered `InvectorShooterMeleeInputAdapter` scheduler |
| Locomotion/action/lifecycle animation | project-owned Invector controller and `InvectorBrawlerAnimationDriver` |
| Staff aim, muzzle VFX, visibility, and arm IK | `InvectorBrawlerWeaponPresentation` |
| Identity, stats, abilities, resources, status, telemetry | Brawl `BrawlerController` |
| Health, applied damage, death, KO, respawn, invulnerability | Brawl `Health` and `MatchManager` |
| Targeting, projectile pooling/launch/hits, team filtering | Brawl combat stack |
| Camera and aim frame | Brawl `BrawlCamera` |
| Vendor health, damage, ammo, reload, equipment, melee hitboxes | inert, disabled, empty, null, or absent |

The live proof covers one human Cinder. Cinder AI, autopilot, every other hero, menu and
portrait previews, equipment/reload gameplay, and global Arena rollout remain outside this
slice.

## Explicit Assembly Context and Rollback

`BrawlerAssemblyContext` is separate from the serialized backend enum:

- `Default = 0` preserves every existing caller and resolves the roster's serialized
  `BrawlerBackend`, which remains `Legacy` for every entry;
- `ProductionHumanCinder` is accepted only for a human actor whose definition ID is exactly
  `fire`, whose serialized backend is still `Legacy`, and whose dedicated production prefab
  is assigned; and
- `BrawlerCharacterAssembly.Resolve(BrawlerBackend.Invector)` remains closed and throws
  before instantiation.

This deliberately keeps rollback data and the experiment selector independent. Invalid AI,
wrong-character, direct-Invector-backend, or missing-prefab requests fail before a clone is
created. The old five-argument `GameFlow.Spawn` overload still uses `Default`; the new
six-argument overload is the only direct route to the approved context.

`GameFlow.SpawnAll` selects the production context only when all of these are true:

1. `enableCinderHumanInvectorPilot` is enabled;
2. the lineup entry is the actual player;
3. automation/autopilot is off; and
4. the selected definition ID is `fire`.

`ArenaSceneBuilder` assigns `invectorHumanPrefab` only to Cinder and explicitly authors
`enableCinderHumanInvectorPilot = false`. Bots, autopilot, non-Cinder players, old scenes,
and all ordinary spawn calls therefore retain Legacy assembly.

Rollback is one change: keep or return the GameFlow switch to false. Do not change the
serialized Cinder backend away from Legacy and do not open the direct backend resolver.

## Builder-Owned Production Variant

`InvectorMigrationPilotBuilder` owns:

`Assets/Generated/InvectorMigration/Cinder/Prefabs/CinderInvectorHuman.prefab`

The asset is a prefab variant of the dormant
`Assets/Generated/InvectorMigration/Cinder/Prefabs/CinderInvectorPilot.prefab`. It stays
inactive as an asset and adds exactly one root `Health`, `BrawlerController`,
`PlayerBrawlerInput`, and `InvectorHumanRuntimeGate`. It reuses the one root Invector motor,
input adapter, animation driver, and weapon presenter already proven by the lab.

The variant keeps:

- root layer 23 `InvectorPlayer` only;
- the disabled child `BrawlerHitProxy` on layer 10;
- visual/skeleton/staff children on layer 0;
- muzzle/VFX objects on layer 12;
- one dynamic-runtime `Rigidbody`/`CapsuleCollider` pair in a frozen dormant posture; and
- all vendor shooter, melee, ammo, and collect managers disabled and non-authoritative.

It contains no Legacy motor/animation driver, `AIBrawler`, `CharacterController`,
`NavMeshAgent`, vendor camera, active vendor weapon/projectile/damage path, or recursive
layer assignment. The builder's production validator checks the independent variant source,
exact topology, reciprocal buffered references, dormant managers/gates/physics, selective
layers, and forbidden-component census.

Current asset identity and two-pass stability evidence:

| Asset | GUID | Stable SHA-256 |
|---|---|---|
| Cinder production human variant | `6aaadd902b169c74098d8c2bfa77ea0a` | `0847D8F4A0FE2ECE7C9ED6E72FF0F1AF277185E406AB035B5BB1872710C03430` |

Generated YAML must not be hand-edited. Change the builder, rebuild twice, and revalidate
the variant and its source pilot.

## Transactional Runtime Gate

`InvectorHumanBrawlerCharacterAssembler` validates the dormant prefab before instantiation,
configures Brawl health/stats/facade data and the three selected component-backed seams, then
opens `InvectorHumanRuntimeGate`. Any exception deactivates and destroys the partial clone.

The runtime gate opens authorities in a deliberate order:

1. reset all runtime traces and select `BufferedMotor` with no movement-camera reference;
2. enable the Humanoid Animator and project Invector controller with root motion off;
3. make the Rigidbody dynamic, enable gravity/capsule, and retain only X/Z rotation freezes;
4. enable Brawl `Health`, facade, motor, gate, and root;
5. initialize the motor and open the single fixed scheduler;
6. enable semantic animation presentation, then visual weapon/IK presentation; and
7. enable `PlayerBrawlerInput` last, after every downstream consumer is ready.

The adapter never reads HUD, keyboard, gamepad, or an Input Action in buffered mode.
`PlayerBrawlerInput` remains the only physical reader and writes camera-relative world intent
to the facade; `BrawlerController` buffers it into the selected motor; and the adapter owns
the only inherited fixed scheduler.

Unexpected scheduler closure fails the entire runtime gate closed. Normal teardown disables
physical input first, clears Brawl movement/offense, closes weapon/animation/scheduler gates,
returns the motor dormant, disables colliders/controller/Animator/facade/Health, freezes the
Rigidbody, and deactivates the root. Death and respawn do not close the gate: Brawl lifecycle
keeps the same actor, suspends/resumes the motor, drives semantic presentation, and preserves
vendor health/death state unchanged.

## Live Authority Proof

The disposable live test uses an actual virtual keyboard to exercise the production
`PlayerBrawlerInput` path. It proves:

- invalid AI context fails without a partial actor;
- one human Cinder activates with the exact motor/driver/presenter authorities and no Legacy
  or `CharacterController` competitor;
- keyboard movement produces facade intent, one buffered scheduler trace, and physical
  displacement while adapter physical-read counters remain zero;
- a Brawl auto-aim attack produces one Invector animation request, guarded aim/release and
  muzzle emission, one Brawl projectile hit, and no vendor combat/resource mutation;
- one lethal Brawl hit reports one KO, increments the correct score, preserves the actor,
  drives Death and Respawn once, teleports to the Brawl spawn, restores health and spawn
  invulnerability, and leaves vendor `currentHealth`/`isDead` unchanged; and
- explicit gate teardown returns the variant to its fully inactive dormant posture without
  solver/helper residue.

The test stores pre-PlayMode scene restoration evidence in `SessionState`. Do not capture a
post-entry local in an `Assert.Throws` lambda: the compiler display class is not preserved by
Unity's EnterPlayMode domain reload. Use an explicit typed `try`/`catch` for invalid-context
checks and use a reload-safe session snapshot across `ExitPlayMode`.

## Test Evidence

All recorder runs restored the caller's dirty `Assets/Scenes/MainMenu.unity`. Unity ended in
Edit mode with zero Console warnings/errors.

| Gate | Result | Recorder duration | XML SHA-256 |
|---|---:|---:|---|
| Phase 3E production category (5 EditMode plus 1 live) | 6/6 | 5.7127538 s | `F2D26D5B79B259F908D64D5ED79B6DCB977A25C83EA8B0650956034BC0C136AE` |
| Phase 3B action compatibility | 1/1 | 6.03127 s | `43C18DAE738FE75F4EF781EEEC8E2857FC33C0205D602F7C93816DD50688C8FE` |
| Phase 3C-C buffered compatibility | 1/1 | 4.65256 s | `F73CEA5C51270DF024EBFA6EA0EC265228AEB37E9DE86133205E7FFAE4398F47` |
| Phase 3D-B lifecycle compatibility | 1/1 | 6.4957172 s | `BCBE48F29289196FF6744D24EAED4D0A535CDA3DF2FFBCD6FA035E0699BC23CC` |
| Phase 3D-C weapon/IK compatibility | 1/1 | 6.3936207 s | `C0344FAFDC0B13BDB938E492D502F99C50BE813D8B2C67CC3909576F88DC2784` |
| Complete EditMode regression | 214/214 | 5.7422884 s | `E977432E4A1719F4F36D8A6025C2B1A111F60092594EC8E79C9A07D15C2AAE14` |

The recorder's aggregate duration excludes time spent across some domain reloads; individual
live testcase duration is recorded separately in the XML.

## Nonclaims and Next Slice

Phase 3E proves the production-shaped assembler and runtime topology, not default Arena
rollout. The generated GameFlow switch intentionally remains false. It also does not prove
automation/autopilot, Cinder AI, Invector navigation for AI, another roster entry, menu or
portrait previews, equipment/reload semantics, vendor collision damage, or retirement of the
Legacy rollback.

The next bounded slice is a real Arena human-Cinder rollout gate: exercise the generated
Arena/GameFlow path with the opt-in switch enabled in a disposable test, validate character
selection, BrawlCamera binding, HUD gestures, match death/respawn, exit, and rollback, and
only then decide whether the generated Arena should author the switch on. AI desired-velocity
adaptation follows separately; roster conversion remains one character at a time.
