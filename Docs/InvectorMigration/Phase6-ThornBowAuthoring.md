# Phase 6 - Thorn Bow Authoring and Arena Rollout

Date: 2026-07-14
Unity: 6000.3.7f1
Invector: Third Person Controller Shooter/Melee 2.6.6
Status: source authoring, dormant production assets, and real-Arena human/AI opt-in plus
all-Legacy rollback validated; detailed live bow presentation remains pending

## Outcome

Thorn is authored directly from the current-roster source
`Assets/ModularRPGHeroesPBR/Prefabs/BasicCharacters/Bow02.prefab` (GUID
`5c2dcf339ba1a9c4ca7a6feb3d8f8760`). The builder pins its Humanoid Avatar at
`Assets/ModularRPGHeroesPBR/Mesh/DefaultCharacter.fbx` (GUID
`47388b002c80e8b49827d81977725b78`), source controller at
`Assets/ModularRPGHeroesPBR/Animators/Bow.controller` (GUID
`2784b5a781ff58f48bb37a4e5d40f565`), and shared weapon material at
`Assets/ModularRPGHeroesPBR/Material/RegularPBR/Weapons.mat` (GUID
`532c7a2f80133ed41b42e2b7a36ecc44`).

The project-owned weapon category is `BrawlWizardBow`. Its visual presentation remains
left-held at `weaponShield_l`; the authored `Arrow2` remains active on the right-hand
`weaponShield_r`; and a generated `NockPoint` plus child `SpellOrigin` preserve the authored
Arrow2 pose without moving or duplicating the right-hand source visual. Thorn owns its
override controller, bow prefab, four-record left/right IK data, inactive pilot, inactive
human variant, and inactive AI variant under `Assets/Generated/InvectorMigration/Thorn`,
with the AI variant at `Assets/BrawlArena/Prefabs/Invector/ThornInvectorAI.prefab`.
Stable GUIDs are human `7ed49b2535df65944800febb0ea37e36`, AI
`bf2fd05f5d4342f4da6b72ec97f8ebb1`, and bow IK adjust
`b7435f188cefaa542a8b5b19ba6dccc0`.

The generated roster attaches those dormant human and AI assets only to exact roster ID
`thorn`. Every serialized backend remains Legacy, both generic rollout IDs remain empty,
the Cinder compatibility switches remain false, and direct `BrawlerBackend.Invector`
resolution remains closed.

Serialized `Assets/Scenes/Arena.unity` is explicitly synchronized to Thorn human GUID
`7ed49b2535df65944800febb0ea37e36` and AI GUID
`bf2fd05f5d4342f4da6b72ec97f8ebb1`. Those references remain dormant because the scene
still serializes every backend Legacy and both rollout IDs empty.

## Evidence

- The canonical final post-preview-safety `InvectorProductionThorn` rerun passes 5/5 in
  9.3536231 seconds, from 2026-07-14 02:59:33Z through 02:59:43Z, at SHA-256
  `753EFC1DE3D8B59E93003C45E365E72DD55086B5129FB1AB821D87F91307CB47`.
- The category pins Bow02/controller/avatar/material identity, audits the bow/Arrow2/nock
  topology and dormant pilot/human/AI variants, proves two preview-scene builds preserve
  Thorn and prior-roster GUIDs plus caller-scene state, rejects wrong roster/role/backend
  contexts before cloning, verifies exact roster attachment and Brawl arrow data, and keeps
  generic production aliases plus direct backend selection fail-closed.
- `InvectorArenaThornRollout` passes 1/1 in 26.7169527 seconds, from
  2026-07-14 02:55:42Z through 02:56:09Z, at SHA-256
  `8C06A91C3F4FBFF4C9BF5528B58B22433670B4151F12720860FBA0FCDB97C7EC`.
  The focused live opt-in loads the serialized Arena, selects exactly one human and one AI
  Thorn while eight bots remain Legacy, proves human and AI Rigidbody locomotion scheduling,
  preserves Brawl `Arrow01`/Explosive Arrow `Arrow02` authority and the bow/vendor firewall,
  then reloads to ten Legacy actors with empty rollout IDs.
- The Phase 6-inclusive full EditMode aggregate at
  `Temp/BrawlArenaFullEditModeResults.xml`, launched by
  `InvectorMigrationPhase3BTestResultRecorder.RunFullEditModeSafely` with run ID
  `66356929-3de5-418c-bbf2-4e9e93b8cfbc`, passes 250/250 with 0 failed, no error nodes
  (0 errors), 0 skipped, and 0 inconclusive. It ran from 2026-07-14 04:11:57Z through
  04:12:15Z with XML duration 17.1907825 seconds, at SHA-256
  `3B6BC1AD48861E9961D3DF0ED9D2835782EE986AB4757146ADDA8968984817EC`.
- After the run Unity was idle: not playing, not paused, not compiling, and not updating.
  It restored `Assets/Scenes/MainMenu.unity` with `dirty=true`. The Console had zero errors
  and one transient `com.unity.ai.assistant` `RelayService` `TaskCanceled` warning caused by
  bridge reconnection; this is not a zero-warning claim.
- This aggregate is automated regression evidence only. It does not close the pending gates
  below, enable default rollout or direct Invector selection, or authorize Legacy retirement.

## Pending Gates and Rollback

These source/production/Arena gates do not prove bow-string deformation, Arrow2 visibility
transitions, draw/release presentation, live four-pose IK reach/calibration, Thorn-specific
clip mapping through the combined Invector graph, rendered animation quality, menu/portrait
presentation, or reduced-motion review. Those remain explicit Phase 6 gates despite the
bounded Arena locomotion/firewall proof.

The authored right-hand `Arrow2` and generated `NockPoint` are presentation references, not
new projectile authority. Brawl still owns standard `Arrow01` pooling/launch/damage and the
`Arrow02` Explosive Arrow Super payload. Vendor shooting, ammo, collision damage, health,
and match authority remain inert.

Rollback is the proven serialized state: leave every backend Legacy, keep both rollout IDs
empty and compatibility switches false, and retain the inactive Thorn references as dormant
assets. Reloading under that state produces ten Legacy actors.
