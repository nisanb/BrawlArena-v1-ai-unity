# Phase 1A — Legacy Character Assembler Seam

Status: implemented and validated  
Completed: 2026-07-13

## Outcome

`GameFlow.Spawn` keeps its public signature and `BrawlerController` remains the persistent game-facing identity, but actor construction now delegates through `BrawlerCharacterAssembly` and `IBrawlerCharacterAssembler`.

All current definitions use `BrawlerBackend.Legacy = 0`. Existing serialized roster data therefore remains Legacy by default, and `ArenaSceneBuilder` writes that choice explicitly for generated roster definitions.

`BrawlerBackend.Invector` is reserved but deliberately unsupported. Selecting it throws before prefab instantiation, preventing a partially constructed actor or silent fallback.

## Added Runtime Seam

- `Assets/Scripts/Brawl/Integration/BrawlerCharacterAssembly.cs`
  - `BrawlerBackend`
  - `IBrawlerCharacterAssembler`
  - backend resolver and fail-before-construction behavior
- `Assets/Scripts/Brawl/Integration/Legacy/LegacyBrawlerCharacterAssembler.cs`
  - exact former `GameFlow.Spawn` recipe
  - human `CharacterController` topology
  - AI `CapsuleCollider` + `NavMeshAgent` topology
  - unchanged Health/BrawlerController configuration, visuals, input/AI component order

## Existing Sources Changed

- `BrawlerDefinition` gained `backend = BrawlerBackend.Legacy`.
- `GameFlow.Spawn` now delegates to the assembler in one line.
- `ArenaSceneBuilder` explicitly assigns Legacy in both wizard and archer factories.
- `BrawlerLegacyBackendEditModeTests.cs` verifies the seam and component topology.

No `BrawlerController`, `PlayerBrawlerInput`, `AIBrawler`, `Health`, combat, camera, Animator, scene, prefab, or ProjectSettings behavior was changed.

## Validation

- Unity clean compilation/domain reload: passed
- Unity Console errors after compilation: 0
- Focused Phase 1A tests: 4 passed, 0 failed
- Exposed regression selection: 44 passed, 0 failed
- Full EditMode suite: 139 passed, 0 failed, 0 skipped/inconclusive
- MainMenu remained active, loaded, and clean
- Editor remained outside Play mode
- Invector P001/P002 audit findings remained absent

## Rollback

Production rollback is `BrawlerBackend.Legacy`, which is already the serialized default and the only supported resolver result. A source rollback can inline `LegacyBrawlerCharacterAssembler.Assemble` back into `GameFlow.Spawn` and remove the selector/interface without changing actor data or gameplay systems.

## Deliberately Deferred

- Historical Phase 1A note: at that point, `IBrawlerMotor`, `IBrawlerNavigation`,
  `IBrawlerVitals`, `IBrawlerActionDriver`, and `IBrawlerAnimationDriver` were intentionally
  deferred rather than guessed. Phase 1B later implemented the animation driver and Phase
  3C-A implemented the motor and Phase 3C-B implemented navigation from complete
  call-surface audits. Later evidence rejected separate vitals/action interfaces because Brawl
  `Health`/facade lifecycle and the semantic animation driver already own those concerns.
- The current movement contract spans CharacterController/NavMesh movement, Ward Step, knockback, dash, teleport, lifecycle, and Animator velocity.
- Historical Phase 1A note: `AIBrawler` still directly owned broad NavMeshAgent behavior.
  Phase 3C-B subsequently moved concrete planning behind `IBrawlerNavigation` while
  retaining Brawl tactical decisions and Legacy agent position authority.
- A definition-level backend affects human and AI spawns of the same archetype. If the pilot requires human-only Invector while AI stays Legacy, introduce a spawn-context override in a separate tested slice.
- Audit animation writers and movement/navigation call surfaces before selecting the next interface boundary.
