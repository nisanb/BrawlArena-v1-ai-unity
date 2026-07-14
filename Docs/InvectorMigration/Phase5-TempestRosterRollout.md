# Phase 5 - Tempest Roster Rollout

Date: 2026-07-14
Unity: 6000.3.7f1
Invector: Third Person Controller Shooter/Melee 2.6.6
Status: implemented and validated within the bounded production, Arena rollout, AI hardening,
combat-contract, combined live presentation/Staff03 IK, and historical aggregate gates below

## Outcome

Tempest is the third exact roster/role conversion. It uses the existing roster-neutral
production contexts, prefab identity, rollout IDs, runtime gates, and Legacy rollback.
Every serialized backend remains Legacy and both generic rollout IDs remain empty.

The builder pins `Assets/Generated/Wizards/Prefabs/StormWizard.prefab` GUID
`855345f398366284ca65b631d3d06fa3`, shared Humanoid Avatar GUID
`172414bf2ce653048b23105e793fff98`, `Staff03`, StormStaff/StormBody materials, the source
SpellOrigin pose, and purple muzzle `#B58CFF`. Tempest owns its AOC, Staff03 presentation,
four-record IK pair, inactive pilot, inactive human variant, and inactive AI variant under
`Assets/Generated/InvectorMigration/Tempest`, with the AI prefab under
`Assets/BrawlArena/Prefabs/Invector`.

The builder also owns the Staff03 reach calibration. It pulls the weapon-hand IK target
1 cm inward on local X (`-0.01`) and the support-hand target 1 cm inward on local X
(`+0.01`) so the guarded two-bone solver stays inside the source bind pose's reach skin.
These offsets are regenerated data, not manual prefab or IK-asset edits.

Human GUID `c1cee4eff40942a439165c232872cb91` and AI GUID
`faec4d591ae92a74184207ebd513d3f4` are assigned only to roster ID `storm`. Cinder remains
`fire` and Rime remains `frost`. At this Phase 5 cutoff Thorn had no Invector reference;
the separate Phase 6 authoring record now covers its dormant default-off assets.

## Evidence

- `InvectorProductionTempest` passes 5/5, including two-build GUID stability, pinned source
  and Staff03 assets, exact identity rejection, fire/frost/storm-only roster assignment, and
  default-off rollout. Result `Temp/Phase5TempestProductionEditModeResults.xml`, SHA-256
  `4EADECB60AB82EDCF629FA75E7F85B4D1C8225DB5AA54F49F9FCA8C1DFC74077`.
- `InvectorArenaTempestRollout` passes 1/1 through the real generated Arena: selected index
  2, one human Tempest, one stable-order AI Tempest, eight Legacy bots, HUD/camera, Input
  System human motion, AI desired velocity, immediate knockback cancellation, dormant
  vendor combat, then ten-Legacy rollback. Result
  `Temp/Phase5TempestArenaRolloutEditModeResults.xml`, SHA-256
  `6E6F2846B494E55FC6AF5BE7ED1719DCD73037447D3AA6D0F25C92AAF63A4E01`.
- Expanded `InvectorAIHardening` passes 4/4, including bounded stuck recovery, runtime
  NavMeshData removal, and actual occupied off-mesh-link fail-close. SHA-256
  `5D3C8C5D8F097FBDA3A34A72DCE3FC1882A924C7F9A4E0EDB8613720FFC91DD9`.
- `InvectorTempestCombat` passes 4/4. It pins the rapid-cast and Eye of the Storm data,
  Brawl-owned cadence/call graph, nearest-visible strict-range chain selection,
  invulnerability termination, actual-applied chain decay, and pooled ProjectileBlast
  payload. The applied fix decays the first hop from the initial `Health` receipt and every
  later hop from the prior hop's actual applied damage, each at `0.55`, so overkill cannot
  amplify discarded damage. SHA-256
  `6607B972B7E0BC84DD3F6315682AB6F82EE26CE644656A1CBE4800F63F768A45`.
- Combined live `InvectorTempestPresentation` passes 2/2. One production-human proof
  accepts/rejects/accepts basic attacks against the exact `0.82`-second cadence, charges
  Super through Brawl `Health`, presents one semantic strong attack, keeps vendor combat
  inert, and returns dormant. The Staff03 proof resolves Standing, StandingAiming,
  Crouching, and CrouchingAiming with clean guarded LateUpdate IK passes. SHA-256
  `99EF7595E63092AE79EC1A571B68FF79F2979D8B0B9AEFB47491313CD7D3538B`.
- The historical Phase-5-cutoff complete aggregate passed 238/238, SHA-256
  `15FDA4AFE6DA65C863C69467C35CFF5B00226C231C3E96CA2539E39678B2EA2B`.
  It predates the added Tempest presentation proof and Phase 6; retain it as phase-local
  evidence rather than treating it as the current aggregate.

## Rollback and Nonclaims

Rollback is unchanged: empty generic rollout IDs, false Cinder compatibility booleans, and
Legacy backends produce ten Legacy actors. The deterministic combat category proves data,
the project call graph, chain semantics, and pooled payload configuration. The combined live
gate additionally proves disposable production-human cadence/Super semantic presentation
and all four Staff03 poses. It does not prove real-Arena projectile flight/impact/explosion,
chain-plus-blast overlap, rendered animation/effect quality, equivalent AI presentation,
menu/portrait presentation, or reduced-motion review. Brawl remains authoritative for input
gestures, projectiles, damage, status, health, lifecycle, resources, camera, and match state;
vendor combat, ammo, health, damage, and match authority remain inert.
