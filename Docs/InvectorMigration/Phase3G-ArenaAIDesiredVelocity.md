# Phase 3G - Arena Cinder AI Desired-Velocity Boundary

Date: 2026-07-14
Unity: 6000.3.7f1
Invector: Third Person Controller Shooter/Melee 2.6.6
Status: implemented and validated for one deterministic Cinder bot; default-off

## Outcome

The real generated Arena can opt exactly one non-player Cinder into a dedicated Invector
AI body while every other actor remains on the Legacy backend. Brawl keeps tactics, target
selection, combat, health, KO/respawn, camera, UI, and match state. Invector supplies only
the already proven Rigidbody locomotion/Animator/presentation stack.

Direct `BrawlerBackend.Invector` resolution remains closed. The serialized roster remains
Legacy, and `GameFlow.enableCinderAIInvectorPilot` remains false.

## Authority Chain

```text
AIBrawler tactics
  -> IBrawlerNavigation.SetDestination
  -> child NavMeshAgent path + desiredVelocity (planning only)
  -> AIBrawler.BufferNavigationIntent
  -> BrawlerController.SetMoveInput
  -> InvectorBrawlerMotor.SetPlanarIntent
  -> InvectorShooterMeleeInputAdapter.FixedUpdate
  -> Invector controller locomotion + Animator update
  -> Rigidbody-owned actor pose
```

There is no navigation `Update`, `FixedUpdate`, `LateUpdate`, or `OnAnimatorMove` movement
writer. The child planner never writes an actor Transform.

## Runtime Topology

`CinderInvectorAI.prefab` is an inactive direct variant of
`CinderInvectorPilot.prefab`. Its root contains exactly one Brawl `Health`, facade,
`AIBrawler`, `InvectorBrawlerNavigation`, `InvectorAIRuntimeGate`, motor, scheduler,
controller, Animator, animation driver, weapon presenter, Rigidbody, and capsule. One child
contains only a disabled `NavMeshAgent`.

The AI variant forbids `PlayerBrawlerInput`, `InvectorHumanRuntimeGate`, Legacy motor,
Legacy navigation/animation, `CharacterController`, vendor simple-melee AI, camera/UI,
vendor damage authorities, and active vendor shooter/melee/ammo managers.

`InvectorAIRuntimeGate` enables consumers first, opens the planner, and enables
`AIBrawler` last. Teardown disables `AIBrawler` first, clears facade intent/offense, closes
the planner, then closes presentation, scheduling, physics, facade, and health.

## Planner and Motor Rules

- The planner child always has `updatePosition = false`, `updateRotation = false`,
  `updateUpAxis = false`, and `autoTraverseOffMeshLink = false` while live.
- The navigator samples an actual NavMesh polygon and returns zero desired velocity when
  unavailable, pending, stale, or on an off-mesh link.
- It never calls `NavMeshAgent.Move` and never writes the root or child Transform.
- It re-anchors `agent.nextPosition` to the Rigidbody-owned root before planner queries.
- The motor also re-anchors at the one fixed scheduler completion boundary, clears the path
  before Ward Step/knockback external displacement, and uses planner-only `Warp` after a
  Brawl teleport/respawn.
- `ResetPath` is never called while Unity reports an off-mesh link because that operation
  completes the link immediately and can desynchronize planner/body state.

Unity does not serialize `NavMeshAgent.updatePosition`, `updateRotation`, or `updateUpAxis`
into this prefab YAML. The asset therefore stores a project-owned
`plannerTransformNeutralConfigured` marker and keeps the agent disabled. The navigator
reasserts the native runtime properties before enabling the agent and during every sync.
Static validation checks the durable marker and disabled-agent topology; live validation
checks the actual native properties every frame.

## Assembly and Rollout

`BrawlerAssemblyContext.ProductionAICinder` accepts only a non-human `fire` definition
whose serialized backend is Legacy and whose dedicated AI prefab is assigned. Invalid
contexts fail before an actor is retained.

`GameFlow.SpawnAll` starts with one AI budget when the runtime switch is enabled. Stable
lineup order consumes it only for the first non-player Cinder. The human Cinder context is
evaluated separately. A second Cinder and every other bot remain Legacy.

`ArenaSceneBuilder` assigns the dormant AI prefab only to Cinder and writes both human and
AI rollout switches false. `Assets/Scenes/Arena.unity` contains the same serialized
contract.

## Validation Evidence

Focused category `InvectorArenaAIRollout` passed 1/1. The live testcase took 49.255036
seconds. Result:

- `Temp/Phase3GArenaAIRolloutEditModeResults.xml`
- SHA-256 `864A0F2A8417E96F2619420C5DCECE4556ED4178E3043F81C03C68CAA8D158B1`

The complete EditMode regression passed 221/221. The Phase 3G testcase took 49.458888
seconds; the five new static production-AI prefab contracts also passed. Result:

- `Temp/BrawlArenaFullEditModeResults.xml`
- SHA-256 `4A977C2D0A1965057DAC78A12819FBC3A47BB09A8D02F193F4BC7055799330D0`

Generated asset evidence:

- AI prefab GUID `f4276398668df80439bea5f2e6c90808`, local file ID
  `1217625541980929687`, SHA-256
  `2ED5DA4717B72C60803863C393EF091B14CDA6D03E460EEF786B2D1B346759DA`;
- generated Arena SHA-256
  `D65BED54D112435EA8293D72AD8BE26857DA612E975F3884E30E482BFC388FB5`.

The live proof verifies:

- ten registered Arena actors: one Legacy human Rime, one Invector AI Cinder, eight Legacy
  AI bots;
- baked NavMesh path solving and nonzero desired velocity reaching physical displacement;
- equality of scheduler start/complete, motor prepare/complete, controller motor update,
  and Animator update counts;
- transform-write flags false every observed frame, zero adapter physical reads, and no
  external fixed subscriber;
- Ward Step through the Brawl external-displacement boundary with planner resynchronization;
- Brawl lethal damage, score, respawn, planner Warp, resumed navigation, and unchanged
  vendor health;
- Brawl camera/HUD targeting the Legacy human player; and
- a second Arena load with the switch false and all actors on Legacy movement/navigation.

Unity ended with zero Console warnings/errors and restored the original dirty MainMenu.

## Nonclaims and Rollback

This gate does not enable either rollout switch by default, convert another character,
authorize direct backend selection, adopt vendor AI tactics/combat, or move Brawl damage,
health, projectiles, camera, input, equipment, ammo, or match authority.

Off-mesh traversal remains fail-closed. Forced stale-mesh/off-mesh-link cases, bounded
stuck/repath policy, and an explicit knockback-while-pathing proof remain hardening work
before broad AI rollout.

Subsequent Phase 4 hardening closes the bounded stuck/repath and immediate
knockback-while-pathing items with focused 2/2 plus complete 230/230 evidence. Forced
stale-mesh/off-mesh-link live cases and custom off-mesh traversal remain open; see
`Docs/InvectorMigration/Phase4-RimeRosterGeneralization.md`.

Rollback is one switch: leave or set `GameFlow.enableCinderAIInvectorPilot = false`.
Keep the AI prefab reference only on Cinder and leave every serialized backend Legacy.
