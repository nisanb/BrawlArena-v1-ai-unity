# Phase 1B — Legacy Animation Driver Seam

Status: implemented and validated  
Completed: 2026-07-13

## Outcome

`BrawlerController` no longer reads or writes an `Animator` directly. It keeps
gameplay policy—measured speed, action timing, flinch eligibility, death order,
respawn sequencing, and match-end gates—while sending seven semantic requests
through `IBrawlerAnimationDriver`.

Every current roster entry still uses `BrawlerBackend.Legacy`. The Legacy
character assembler installs exactly one `LegacyBrawlerAnimationDriver`, which
preserves the original base-layer state hashes, explicit-state fallback,
locomotion hysteresis/watchdog, random attack variants, blend durations, and
`applyRootMotion = false` policy.

## Runtime Boundary

The backend-neutral contract contains only:

- normalized locomotion ticks;
- basic attack and Super presentation;
- hit reaction;
- death, respawn, and victory presentation.

It deliberately contains no `Animator`, state names, parameters, triggers,
movement components, action timing, or configuration lifecycle. A future
Invector implementation can translate the same requests to its combined graph
without exposing Invector details to combat or lifecycle code.

`BrawlerAnimationSetup` and `LegacyBrawlerAnimationDriver.Configure` are
Legacy-only construction details. Driver selection is locked when `Start`
initializes the facade. A second owner, null owner, foreign-root component, or
post-initialization replacement is rejected.

The production Legacy driver is a component on the brawler root. The facade
stores a hidden serialized `MonoBehaviour` reference and restores its interface
cache in `OnEnable`, so editor domain reload cannot silently replace or lose the
selected animation backend.

## Preserved Legacy Semantics

- run starts above normalized speed `0.25`;
- idle starts at or below `0.20`;
- looping variations return to locomotion;
- non-looping one-shots return at normalized time `0.92`;
- basic attack fade `0.08s`, Super fade `0.06s`;
- hit `0.08s`, death `0.10s`, respawn idle `0.05s`, victory `0.20s`;
- empty attack lists are safe no-ops;
- authored states are preferred only when present on layer 0, otherwise the
  original suffix-based state names are used;
- Legacy never sets the Modular RPG `Combo01` trigger; Thorn still crossfades
  directly to `Attack01_Bow`/`Attack02_Bow`.

Attack impact and Super timing remain Brawl coroutine delays. No Animator event
or Invector hit window became combat authority in this slice.

## Files

Added:

- `Assets/Scripts/Brawl/Integration/IBrawlerAnimationDriver.cs`
- `Assets/Scripts/Brawl/Integration/Legacy/LegacyBrawlerAnimationDriver.cs`
- `Assets/Editor/BrawlAutomation/LegacyBrawlerAnimationDriverEditModeTests.cs`

Changed:

- `Assets/Scripts/Brawl/BrawlerController.cs`
- `Assets/Scripts/Brawl/Integration/Legacy/LegacyBrawlerCharacterAssembler.cs`
- `Assets/Editor/BrawlAutomation/BrawlerLegacyBackendEditModeTests.cs`

No scene, prefab, Animator controller, generated wizard asset, project setting,
input, AI, camera, health, damage, or vendor source was changed by Phase 1B.

## Writer Census

The sole live Arena-brawler Animator writer is now
`LegacyBrawlerAnimationDriver`. `PlayerBrawlerInput`, `AIBrawler`, `GameFlow`,
and `MatchManager` remain semantic callers only.

These separate animation domains are intentionally not folded into the runtime
contract:

- `MainMenuFlow` owns the Animator on its isolated roster preview;
- `PortraitStudio` owns deterministic editor sampling (`Play` + `Update`);
- `CombatObjectPool` resets pooled projectile/VFX Animators;
- `WizardAssetBuilder` owns generated controller/prefab assignment at edit time.

Menu and portrait previews must gain a backend-aware preview adapter when the
concrete Invector animation driver is completed in Phase 4. Portrait sampling also needs an explicit
root-motion-off policy for Thorn.

## Validation

- controlled Unity asset refresh/domain reload: passed;
- post-reload compilation: passed;
- focused Phase 1A + Phase 1B tests: 13 passed, 0 failed;
- full EditMode suite: 148 passed, 0 failed, 0 skipped/inconclusive;
- generated Wizard controller behavior: authored state selection, hysteresis,
  one-shot watchdog, root-motion policy, and empty attack no-op passed;
- Thorn Bow controller behavior: suffix fallback and direct attack-state target
  passed;
- source guard: no direct Animator mutation remains in `BrawlerController`;
- editor remained outside Play mode and `MainMenu` remained clean.

## Rollback

Keep all roster definitions on `BrawlerBackend.Legacy`. A source rollback can
move the exact Legacy driver code back into `BrawlerController`, remove the
driver component installation from `LegacyBrawlerCharacterAssembler`, and
delete the interface/driver files. No asset reconstruction or settings rollback
is required.

## Historical Boundary (Superseded)

The original Phase 1B handoff is complete: Phase 0B settings approval, the
dormant Phase 2 Cinder pilot, and the dormant Phase 3A project adapter stack have
all been implemented. Phase 3A's post-hardening compilation, two-build, focused 8/8,
full 156/156, and final zero-error Console gates passed. See
`Docs/InvectorMigration/Phase3A-DormantInputAnimationAdapters.md`.

Phase 3B owns the separate isolated
runtime lab activation. Keep the prefab root inactive and the Invector resolver
guard closed.

Do not add a guessed motor abstraction before the movement and lifecycle
ownership audit. Brawl input gestures, camera, health, damage, and match
lifecycle remain the sole authorities throughout.
