# Invector-Only Validation Guide

Use this guide after any character-controller, generated asset, scene, menu preview, combat presentation, or vendor-package change. Treat historical phase evidence as context, not proof of the current tree.

## Before Editing

1. Inspect `git status --short` and the scoped diff.
2. Record the active Unity scene, whether it is dirty, Play mode, compile state, and console errors.
3. Read Unity MCP user guidelines when the editor connector is available.
4. Run `scripts/audit-invector.ps1 -FailOnBlockers`.
5. For settings/layer/mask/package work, run `scripts/audit-invector-settings.ps1` to `Temp/` and inspect raw values before interpreting them.
6. Identify the source-of-truth builder and affected ownership contracts.

Do not save, discard, close, replace, or clear a user's dirty scene merely to run validation.

## Static Cutover Gate

Require the current runtime/editor implementation, generated production assets, Arena, and MainMenu to contain no active character-controller Legacy path.

Search at minimum for:

```powershell
rg -n "LegacyBrawler|BrawlerBackend|ProductionHumanCinder|ProductionAICinder|enableCinder.*Pilot|invectorHumanRolloutId|invectorAIRolloutId" Assets --glob "*.cs" --glob "*.prefab" --glob "*.unity" --glob "*.asset"
rg -n "definition\.prefab|definition\.backend|attackStates|animSuffix|idleState|runState|hitState|deathState|victoryState" Assets/Scripts Assets/Editor --glob "*.cs"
rg --files Assets/Scripts/Brawl/Integration | rg "Legacy"
```

Allow historical names only when they are test category/result labels or migration-history documentation and cannot select runtime behavior. Prefer renaming stale labels when the owner is already being edited.

Require:

- no Legacy runtime folder or components;
- no alternative prefab/backend fields in `BrawlerDefinition`;
- no rollout switches or roster selectors in `GameFlow`;
- default assembly always choosing Invector Human or AI;
- no fallback motor, navigator, or animation-driver creation;
- no competing interface implementations on generated production prefabs;
- no missing script GUIDs or serialized references;
- no `CombatPhysics.SetLayerRecursively` call on an Invector hierarchy.

## Package and Settings Gate

Run:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .agents/skills/migrate-brawlarena-invector/scripts/audit-invector.ps1 -FailOnBlockers
powershell -NoProfile -ExecutionPolicy Bypass -File .agents/skills/migrate-brawlarena-invector/scripts/audit-invector-settings.ps1 -OutputPath Temp/InvectorSettingsAudit.json
```

Require:

- the expected Unity and Invector versions, or an explicitly reviewed upgrade;
- Input System Package (New) as active input handling;
- no known P001/P002 source pattern;
- layer 23 named `InvectorPlayer`;
- no blanket names/remaps for mixed upstream layers 13 or 15;
- no unexplained ProjectSettings, input, tag, layer, mask, or collision-matrix drift;
- no package ProjectSettings import.

The scripts are source/serialization audits. They do not prove Unity compilation or live behavior.

## Builder and Asset Gate

Regenerate through owners in dependency order:

1. `WizardAssetBuilder.EnsureAssets()`;
2. `InvectorMigrationPilotBuilder.BuildPilotAssets()`;
3. `InvectorRimeMigrationBuilder.BuildRimePilotAssetsSafely()`;
4. `InvectorTempestMigrationBuilder.BuildTempestPilotAssetsSafely()`;
5. `InvectorThornMigrationBuilder.BuildThornPilotAssetsSafely()`;
6. `ArenaSceneBuilder.BuildArenaScene()` when Arena-owned data changed;
7. `MenuSceneBuilder.BuildMenuScene()` when menu-owned data changed.

Run the affected builders twice. Require:

- stable asset GUIDs;
- identical intended topology and serialized references;
- all Human/AI prefab assets inactive;
- exact roster/role identity markers;
- correct AOC, weapon presentation, IK assets, and source pins;
- no missing scripts/references;
- no unexpected scene switch or dirty-state loss;
- no hand edits required after regeneration.

Unity may regenerate local file IDs and YAML document order. Validate semantic topology, not raw byte identity.

## Production Prefab Gate

For each of `fire`, `frost`, `storm`, and `thorn`, validate both roles.

### Common root

Require exactly one root:

- `InvectorBrawlerPrefabIdentity` with exact ID and role;
- `Health`;
- `BrawlerController`;
- `InvectorBrawlerMotor`;
- `InvectorBrawlerAnimationDriver`;
- `InvectorBrawlerWeaponPresentation`;
- `BrawlInvectorThirdPersonController`;
- `InvectorShooterMeleeInputAdapter`;
- Animator, Rigidbody, and CapsuleCollider;
- configured child `BrawlerHitProxy`.

Require the dormant posture: inactive root, disabled gate/controller/adapter/motor/driver/presenter/Animator/capsule/hit proxy, frozen kinematic Rigidbody, and inert vendor managers.

Reject `CharacterController`, vendor camera, vendor health/damage receivers, vendor projectile authority, and vendor sample AI.

### Human

Require exactly one root `PlayerBrawlerInput` and `InvectorHumanRuntimeGate`. Reject `AIBrawler`, `InvectorBrawlerNavigation`, AI gate, and planner agent.

### AI

Require exactly one root `AIBrawler`, `InvectorBrawlerNavigation`, and `InvectorAIRuntimeGate`. Require exactly one disabled child planner `NavMeshAgent` with transform writes and automatic off-mesh traversal disabled. Reject `PlayerBrawlerInput` and Human gate.

## Compile and Console Gate

After every C# or serialized type change:

1. let Unity refresh and complete domain reload;
2. require no compilation in progress;
3. require zero compiler errors and zero runtime errors attributable to the change;
4. inspect warnings and classify every non-project/transient warning explicitly;
5. query active target scripting defines instead of inferring them from serialized files.

Do not enter Play mode while compilation or asset import is active.

## Automated Test Gate

Use the dirty-scene-safe recorder in `InvectorMigrationPhase3BTestResultRecorder` when it exposes the required run. At minimum run:

- `RunInvectorOnlyCutoverSafely()`;
- the affected roster production category;
- affected motor, navigation, lifecycle, weapon/IK, Tempest combat, or Thorn presentation categories;
- `RunFullEditModeSafely()`.

Store and inspect result XML under `Temp/`. Confirm pass, fail, skip, inconclusive, duration, run ID, and result-path evidence. Do not quote an old aggregate count as a current result.

When an Editor `[UnityTest]` enters Play mode:

- do not capture post-entry local Unity objects in lambdas across domain reload;
- use `SessionState` for evidence that must survive reload;
- wait for `sceneLoaded`, live `GameFlow`, and match readiness explicitly;
- poll HUD/input release propagation with bounded frame loops;
- restore the original scene and dirty state through a reload-safe editor update hook.

## Live Arena Gate

Load the generated Arena in a disposable validation flow and prove:

1. all ten actors assemble through Invector-only paths;
2. the selected player uses the exact Human prefab/identity for the chosen roster entry;
3. every bot uses the exact AI prefab/identity for its roster entry;
4. each actor has exactly one motor and animation authority, and each AI exactly one navigation authority;
5. no vendor camera, health, damage, inventory, ammo, reload, projectile, or sample-AI authority becomes active;
6. movement uses one fixed scheduler and one Animator update path;
7. aim, tap, drag, deadzone retention, and camera-basis latching retain Brawl semantics;
8. weak attack and Super create Brawl projectiles or deterministic Brawl melee results exactly once;
9. actual applied damage, status effects, knockback, KO, respawn, invulnerability, and victory remain Brawl-owned;
10. AI reaches destinations, cancels on displacement, recovers from a new destination, and fails closed on stale/invalid paths;
11. teardown leaves no pooled/live actor with an open runtime gate.

Exercise all four roster entries as Human and observe their AI equivalents. One successful character does not prove roster-wide cutover.

## Animation, IK, and Presentation Gate

For all roster entries require:

- shared eight-layer controller topology and expected lifecycle triggers/states;
- root motion disabled and root transform invariant during presentation-only actions;
- semantic weak, strong/Super, recoil, death, respawn, and victory;
- standing, standing-aiming, crouching, and crouching-aiming IK records resolving and applying;
- equipment visibility, muzzle, death hide, and respawn reset;
- no vendor combat or resource mutation.

Additionally require:

- Cinder Staff01/fire presentation;
- Rime Staff02/Frost origin/cyan muzzle;
- Tempest Staff03/Storm origin/purple muzzle and builder-owned reach calibration;
- Thorn weak/Super bow clip overrides, left-held Bow02, right-hand Arrow2 staging, nock/tip alignment, visible three-point bow string, bounded draw/release behavior, and four-pose bow IK.

Inspect rendered pose quality, clipping, hand placement, string alignment, arrow visibility, camera framing, and reduced-motion behavior. Static serialized assertions do not replace visual review.

## MainMenu and Portrait Gate

For every roster definition:

1. resolve the exact inactive Human prefab;
2. instantiate while inactive;
3. neutralize every Behaviour, Collider, Rigidbody, and Rigidbody2D;
4. activate and neutralize again;
5. enable only the root Animator with root motion off;
6. show idle and victory through `BrawlerPreviewAdapter`;
7. prove no gameplay runtime gate, input, AI, health, combat, physics, or camera authority is enabled;
8. regenerate and inspect the portrait for visible pixels, framing, equipment, transparency, and correct character identity.

Load MainMenu and cycle all four selections. Verify preview replacement/destruction does not leak components, objects, cameras, listeners, or physics bodies.

## Scene, Build, and Reference Gate

Require:

- Arena roster contains exactly `fire`, `frost`, `storm`, and `thorn` with both exact Invector prefabs;
- MainMenu uses the same roster and preview boundary;
- build settings place MainMenu first when present and Arena second;
- no scene/prefab references removed Legacy scripts or obsolete serialized fields;
- no missing script, object, material, Avatar, controller, AOC, clip, IK, weapon, projectile, or portrait reference;
- owned builders can recreate all generated outputs from source assets.

## Diff and Vendor Review

Inspect:

- runtime/editor source changes;
- generated prefab/controller/AOC/IK/scene/portrait churn;
- `.meta` additions/deletions and GUID stability;
- ProjectSettings and package diffs;
- unexpected vendor changes;
- residual historical names in active code or assets.

Keep project integration outside the vendor tree. Record any unavoidable vendor patch in `Docs/VendorPatches/Invector-2.6.6.md` with reason, exact file, and upgrade disposition.

## Completion Report

Report:

- authority/capability changed;
- exact files and generated assets;
- builders run and idempotence evidence;
- audit outputs;
- Unity version, package version, compile/console result;
- focused, full, Arena, menu, and visual evidence;
- residual Legacy census;
- disabled vendor subsystems;
- evidence not rerun and remaining risks.

Do not call the cutover complete while any required role, capability, preview, generated asset, scene, test, or residual-Legacy check remains unproven.
