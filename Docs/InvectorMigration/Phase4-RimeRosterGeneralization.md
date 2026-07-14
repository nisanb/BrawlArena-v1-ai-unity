# Phase 4 - Rime Roster Generalization

Date: 2026-07-13
Unity: 6000.3.7f1
Invector: Third Person Controller Shooter/Melee 2.6.6
Status: implemented and validated; all rollout selectors remain default-off

## Outcome

Rime is the second roster character with separate inactive Invector human and AI variants.
The implementation generalizes the production seam without opening direct
`BrawlerBackend.Invector` selection or changing any serialized roster backend from Legacy.

Production assembly now requires all of these facts to agree before instantiation:

- an explicit human or AI production context;
- a Legacy-serialized `BrawlerDefinition` with the requested roster ID;
- the dedicated prefab reference for that role; and
- an `InvectorBrawlerPrefabIdentity` on the prefab root with the same roster ID and role.

This exact identity check prevents a valid Cinder prefab from being assembled for Rime, a
human variant from being used as AI, or an unrelated Invector hierarchy from passing a
broad component/topology check. The old Cinder context names remain numeric aliases for
compatibility, while the production implementation uses the roster-neutral human and AI
contexts. Direct Invector backend resolution still fails before cloning.

## Rime Asset Recipe

Rime starts from `Assets/Generated/Wizards/Prefabs/FrostWizard.prefab` rather than from the
Cinder character. It reuses the audited project controller, scheduler, buffered motor,
animation driver, lifecycle overlay, runtime gates, facade, health/combat firewalls, and
`BrawlWizardStaff` IK category. It deliberately owns its character-specific presentation:

- source body and materials: Frost/Rime;
- weapon visual: `Staff02`, not Cinder's `Staff01`;
- cast origin: the Frost source's own `SpellOrigin` transform;
- muzzle effect color: cyan `#75F0FF`;
- Rime-specific staff presentation prefab and four-record IK assets; and
- a Rime-named AnimatorOverrideController over the shared validated lifecycle graph.

Generated outputs:

- `Assets/Generated/InvectorMigration/Rime/Controllers/RimeInvectorPilot.overrideController`
- `Assets/Generated/InvectorMigration/Rime/IK/RimeStaffIKAdjust.asset`
- `Assets/Generated/InvectorMigration/Rime/IK/RimeStaffIKAdjustList.asset`
- `Assets/Generated/InvectorMigration/Rime/Weapons/RimeStaffPresentation.prefab`
- `Assets/Generated/InvectorMigration/Rime/Prefabs/RimeInvectorPilot.prefab`
- `Assets/Generated/InvectorMigration/Rime/Prefabs/RimeInvectorHuman.prefab`
- `Assets/BrawlArena/Prefabs/Invector/RimeInvectorAI.prefab`

The builder operates in an additive preview scene, validates the Frost source GUID and
source nodes/materials, saves every prefab inactive, and validates exact topology and
identity. Rebuilding twice preserves asset identity. The production human prefab GUID is
`8210e5a32e5408841a88fa4e647fa5c1` with root local file ID
`4507023107282245903`. The AI GUID is `0fc7c55cc0dc49e4680705d232d15334`
with root local file ID `4400196431709436480`.

## Rollout and Rollback

`GameFlow` has roster-neutral `invectorHumanRolloutId` and `invectorAIRolloutId` fields.
Each is empty in the generated Arena, so the production default is fully Legacy. At
runtime an exact ID match grants the human context, while the AI field grants a one-use
stable-lineup-order budget for the first matching non-player actor. The existing Cinder
booleans remain as compatibility inputs and also serialize false.

`ArenaSceneBuilder` assigns Cinder variants only to `fire` and Rime variants only to
`frost`. Every `BrawlerDefinition.backend` stays Legacy. Clearing both generic rollout IDs
and leaving both compatibility booleans false is the complete rollback; the dormant
references can remain assigned.

## Live Arena Evidence

Focused production category `InvectorProductionRime` passes 5/5. It covers generated asset
validation, two-build GUID stability and scene preservation, exact identity rejections,
roster assignments/default-off serialization, compatibility aliases, and the closed direct
backend.

Focused generated-Arena category `InvectorArenaRimeRollout` passes 1/1. The proof loads the
real Arena with selected player index 1 and runtime rollout IDs set to `frost`, then verifies:

- one Invector human Rime and one stable-order Invector AI Rime;
- eight remaining Legacy bots and no competing movement/animation/vendor authorities;
- Brawl HUD and camera ownership;
- real Input System `W` locomotion for the human;
- AI planner desired velocity reaching the buffered Rigidbody scheduler;
- immediate destination cancellation when knockback begins; and
- a second load with empty IDs producing ten Legacy actors.

Focused evidence:

- production 5/5 in 6.043188 seconds,
  `Temp/Phase4RimeProductionEditModeResults.xml`, SHA-256
  `CA29A8F93CDF38D48BFCC9EBED31251BE9293CA1B25400965365DB996AB7D790`;
- generated Arena 1/1 in 17.9186204 seconds,
  `Temp/Phase4RimeArenaRolloutEditModeResults.xml`, SHA-256
  `4787A687AEFC9BABC997839C8D3B209FF850055FE760310D8E4F4EB75430BF5A`;
  and
- complete combined EditMode regression 230/230,
  `Temp/BrawlArenaFullEditModeResults.xml`, SHA-256
  `29F02CAEC0B07A23324EAAEC4223C68A429C2546B04F9D40798775B09F61F6EC`.

Unity restored `Assets/Scenes/MainMenu.unity` active and dirty and ended with zero Console
warnings/errors. Final generated hashes are Rime human
`095C6B89C80D204C4E1FDBF4FAD2559509DB759F13A97CC5E7FD59E36539AF17`,
Rime AI `520F9EBAD262557391454928E932B3FA511A9C60F74F36CA8171F75961A6CBC2`,
and Arena `2BB953EB334A4DF82B73122BF2039009ABA06AA05A27A2553930DBE7402BE698`.

## Planner Timing Hardening

The live Rime proof exposed two Unity `NavMeshAgent` timing details that are now part of the
integration contract:

1. `SetDestination` can be accepted while both `hasPath` and `pathPending` still read false.
   External displacement must therefore cancel the project-owned durable destination
   request, not rely only on Unity's transient path flags.
2. `desiredVelocity` can remain nonzero for a frame after `ResetPath`. The navigator must
   report movement intent only while it is ready, still owns an accepted destination,
   has a resolved path, and is not pending.

Successful path resets increment `PathResetCount`, including the zero-yield
`SetDestination -> ApplyKnockback` case. The navigator continues to avoid `ResetPath` while
on an off-mesh link and fails closed for unavailable, pending, stale, or off-mesh planning.

The bounded stuck watchdog runs only from the existing motor synchronization boundary; it
adds no Unity message scheduler or transform writer. With an accepted resolved path,
nontrivial desired speed, and less than 0.08 m progress over 0.75 seconds, it attempts one
automatic repath. A second observation window without progress clears intent and enters a
one-second same-destination cooldown. A materially different tactical destination can
recover immediately. Repeated same-destination calls are suppressed by the durable project
request, not Unity's transient flags, so they cannot reset the one-repath budget.

Focused category `InvectorAIHardening` now passes 4/4 on in-memory NavMeshes, including one
repath, one fail-close, cooldown rejection, different-destination recovery, runtime
NavMeshData removal, occupied off-mesh-link fail-close, and unchanged transform-neutral
flags. Result `Temp/Phase3GAIHardeningEditModeResults.xml`, SHA-256
`5D3C8C5D8F097FBDA3A34A72DCE3FC1882A924C7F9A4E0EDB8613720FFC91DD9`.

## Nonclaims

This slice does not enable rollout by default, convert Tempest or Thorn, adopt Invector AI
tactics/combat, authorize custom off-mesh traversal, or move Brawl input gestures, camera,
health, damage, projectiles, equipment, ammo, status, death/respawn, or match authority.
Forced stale-mesh and off-mesh-link fail-close are now proven. Custom off-mesh traversal
remains explicitly unsupported.
