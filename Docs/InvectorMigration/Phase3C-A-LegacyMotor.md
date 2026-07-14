# Phase 3C-A — Legacy Motor Extraction

Status: complete and validated on 2026-07-13  
Production backend: Legacy; `BrawlerBackend.Invector` still fails before instantiation  
Gameplay change: none intended

## Outcome

`BrawlerController` remains the stable game-facing identity and gameplay facade, but no
longer owns a concrete `CharacterController` or `NavMeshAgent`. One component-backed
`IBrawlerMotor` is selected on the brawler root before `Start`, serialized through a
`MonoBehaviour` source reference for domain reload, initialized once, and then locked.

The production assembler installs `LegacyBrawlerMotor` before the facade. Manual legacy
fixtures that do not use the assembler receive the same fallback component, while a
second motor or a cross-root/non-component implementation fails closed.

## Contract and Routed Authority

`IBrawlerMotor` exposes only audited physical operations:

- velocity, collision radius, and grounded queries;
- initialization and planar movement intent;
- facing;
- NavMesh-safe external-displacement and teleport constraints;
- nested begin/displace/end ownership for Ward Step and knockback;
- stop/suspend and teleport.

`BrawlerController` routes all of these through the selected motor:

- ordinary player movement and facing;
- speed measurement for semantic locomotion animation;
- hit radius selection;
- Ward Step collision clamping and displacement;
- knockback displacement;
- Super-dash destination clamping and teleport;
- death suspension, respawn teleport, and victory stop.

The facade contains no `UnityEngine.AI` import, concrete controller/agent discovery,
`Move`, `Warp`, `ResetPath`, `isStopped`, or `NavMesh.SamplePosition` call.

## Legacy Backend Semantics

`LegacyBrawlerMotor` preserves the prior split implementation:

- a human `CharacterController` receives clamped planar intent plus the existing
  downward grounding motion and 14-per-second facing Slerp;
- an AI `NavMeshAgent` remains the physical/path-following authority, retains its speed,
  acceleration, angular speed, path reset, raycast constraint, move, and warp behavior;
- Ward Step and knockback temporarily stop a ready agent and restore its previous stopped
  state with nested displacement ownership;
- teleport preserves any active displacement ownership so a dash during knockback still
  restores the agent's pre-displacement stopped state when the caller ends the sequence;
- death clears the path and suspends the agent; respawn teleport re-enables and warps it;
- transform fallback remains available for isolated fixtures with no concrete controller.

At the close of Phase 3C-A, `AIBrawler` still owned concrete NavMesh planning. Phase 3C-B
subsequently removed that coupling behind `IBrawlerNavigation`; neither extraction is
evidence of an Invector-ready AI.

## Files

- `Assets/Scripts/Brawl/Integration/IBrawlerMotor.cs`
- `Assets/Scripts/Brawl/Integration/Legacy/LegacyBrawlerMotor.cs`
- `Assets/Scripts/Brawl/Integration/Legacy/LegacyBrawlerCharacterAssembler.cs`
- `Assets/Scripts/Brawl/BrawlerController.cs`
- `Assets/Editor/BrawlAutomation/BrawlerLegacyMotorEditModeTests.cs`
- `Assets/Editor/BrawlAutomation/BrawlerLegacyBackendEditModeTests.cs`

## Validation Evidence

Focused Legacy motor/backend fixtures:

- 9 passed, 0 failed, 0 skipped/inconclusive;
- duration: 0.818 seconds;
- result: `Temp/Phase3CALegacyMotorEditModeResults.xml`;
- SHA-256: `F3D7554F8FAA1F86787A780BA7FC90F3725AE69DEDE5E49E47442E39DB2F566B`.

Final complete EditMode regression after recorder hardening:

- 162 passed, 0 failed, 0 skipped/inconclusive;
- duration: 5.173 seconds;
- result: `Temp/BrawlArenaFullEditModeResults.xml`;
- SHA-256: `7E08298FC6E04DBECCBE882D84C4DE5838BBF783032EB16BF4D83B272BCA126D`.

The isolated Phase 3B live test also re-passed 1/1 after this extraction and after the
safe recorder fix. Unity compiled with zero errors, the final Console had zero warnings
or errors, the editor was outside Play mode, and the original dirty
`Assets/Scenes/MainMenu.unity` remained loaded, dirty, and unsaved.

The safe test recorder now records a post-result SessionState flag and uses a reload-safe
`EditorApplication.update` restoration hook. This prevents Unity's exit-Play transition
from dropping the pending dirty-scene restoration callback. The final run cleared all
recorder SessionState and restored the pre-run dirty marker without saving scene content.

After Phase 3C-B changed AI-facing and assembler ownership, the impacted Phase 3C-A
fixture re-passed 9/9 in 0.633 seconds. Its result is
`Temp/Phase3CALegacyMotorEditModeResults.xml`, SHA-256
`3B34643CBF6675521BE8D06FEDB2D2B214A751E6DBA8559463AB96E1AB4325A9`.

## Rollback and Next Boundary

Rollback remains `BrawlerBackend.Legacy`; no roster, prefab, generated Invector artifact,
or project setting changed. Removing the motor seam would require restoring the prior
direct facade motion code, but production selection itself needs no migration rollback.

Phase 3C-B is complete; see
`Docs/InvectorMigration/Phase3C-B-LegacyNavigation.md`. Phase 3C-C subsequently
completed the isolated human Invector buffered motor/adapter/builder/live-lab bridge;
see `Docs/InvectorMigration/Phase3C-C-InvectorMotorCore.md`. It did not add a production
selector/assembler or live Brawl actor lifecycle. Disabling Legacy agent
position/rotation authority and translating live AI paths into an Invector motor belongs
to the later Phase 8 AI adaptation, after the Phase 3D lifecycle/presentation firewall.
