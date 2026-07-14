# Phase 3F - Arena Human Cinder Rollout

Status: complete for the generated Arena/GameFlow opt-in and Legacy rollback proof on
2026-07-13. The serialized rollout switch remains off by default.

## Boundary Completed

Phase 3F moves the Phase 3E production-human assembly proof into the real generated
`Assets/Scenes/Arena.unity` flow. It proves the menu-selection branch, loading sequence,
ten-actor lineup, HUD, camera, match lifecycle, and rollback without opening direct
`BrawlerBackend.Invector` selection or changing any AI actor.

| Arena actor/concern | Authority after Phase 3F opt-in |
|---|---|
| Selected human Cinder physical input and gestures | Brawl `PlayerBrawlerInput` and real `BrawlHUD` controls |
| Selected human Cinder movement/animation/weapon presentation | Phase 3E Invector motor, scheduler, driver, and presenter |
| Selected human Cinder gameplay, health, damage, KO, respawn | Brawl facade, `Health`, combat stack, and `MatchManager` |
| Nine bots, including any Cinder duplicates | Legacy motor/animation, `AIBrawler`, `NavMeshAgent`, and Legacy capsule |
| Camera | One Brawl `BrawlCamera`, `Camera`, and `AudioListener`, targeted to the selected player |
| Serialized rollback | Every roster backend remains `Legacy`; GameFlow switch remains false |

This slice does not migrate Cinder AI, another human hero, previews, inventory/reload
gameplay, or any vendor damage/health authority.

## Generated Arena Artifact

`ArenaSceneBuilder` remains the source of truth. The generated Arena now serializes:

- `backend = Legacy` for all four roster definitions;
- `invectorHumanPrefab = CinderInvectorHuman.prefab` only for the `fire`/Cinder entry;
- null production-human prefabs for Rime, Tempest, and Thorn; and
- `enableCinderHumanInvectorPilot = false`.

The scene had been generated before the new Phase 3E fields existed, so this slice brought
the checked-in scene serialization into exact agreement with the already-updated builder.
The production prefab reference is GUID `6aaadd902b169c74098d8c2bfa77ea0a`, local file ID
`6215850469901778175`. The verified Arena YAML SHA-256 is
`F767C8DC34BEEB6D4FC4A199B6D34E7B30D3FA2EC47AF073F958860A46C1919B`.

Do not enable the serialized switch merely to run a test. The live proof changes only the
loaded GameFlow instance before `Start`; no scene is saved during the test.

## Real GameFlow Proof

`InvectorArenaHumanRolloutPlayModeTests` enters Play mode, snapshots caller scene and user
state through `SessionState`, selects roster index zero through `MatchSetup`, and loads the
real Arena through `EditorSceneManager.LoadSceneInPlayMode`.

The test subscribes a static `SceneManager.sceneLoaded` callback before loading. The
callback runs after Arena `Awake`/`OnEnable` work and before `Start`, so it can opt the loaded
GameFlow instance into Phase 3E without adding a production test seam. It also shortens only
the loaded MatchManager's intro/respawn timing. The callback verifies that the source scene
was serialized with rollback off and the dormant Cinder prefab assigned before changing the
runtime instance.

The first pass proves:

- `GameFlow.Start -> LoadAndSpawn -> SpawnAll -> BeginMatch` reaches `match-begun`;
- ten actors register, with exactly one human `YOU` Cinder and nine Legacy AI bots;
- only the human Cinder owns an active production gate, Invector motor/driver/presenter,
  buffered scheduler, and `PlayerBrawlerInput`;
- the Invector adapter performs zero HUD/Input Action reads, owns no move-action enablement,
  has no movement-camera reference, and has no external fixed subscriber;
- one live Brawl camera targets the Cinder player and the live combat HUD is visible; and
- no active vendor camera, second physical input reader, Legacy authority on the player, or
  Invector authority on a bot appears.

The test then loads the same Arena a second time with the runtime switch false. The selected
human Cinder returns to `LegacyBrawlerMotor`, `LegacyBrawlerAnimationDriver`, and one
`CharacterController`; it has no production gate or Invector motor/driver/presenter. The
camera and HUD still bind normally, proving rollback through the same GameFlow path rather
than through a direct assembler call.

## HUD, Camera, and Lifecycle Evidence

The proof drives the real runtime widgets directly through their public pointer handlers:

1. `VirtualJoystick.OnPointerDown/OnDrag` publishes a forward gesture after rotating the
   live `BrawlCamera` basis by 72 degrees.
2. `PlayerBrawlerInput.Update` consumes that HUD value and writes camera-relative world
   intent into the facade.
3. The buffered adapter's one fixed scheduler moves the Rigidbody actor. Displacement
   aligns with the rotated camera forward, all scheduler/motor/controller counts agree, and
   adapter physical-read counts stay zero.
4. `OnPointerUp` clears the joystick and a bounded Update wait confirms the motor buffer
   returns to zero.

The real `RightCastSurface` `AttackButtonWidget` similarly proves press/hold/drag/release.
Holding produces no attack. A drag beyond the reference deadzone followed by release
creates exactly one Brawl attack, one semantic weak-animation request, the expected
camera-right aim, one guarded aim release, and one visual muzzle emission. Brawl remains
the only projectile authority.

Finally, a lethal Brawl `Health.TakeDamage` from a red Legacy actor increments the red score,
drives Death and Respawn once, preserves the same player actor, restores Brawl health and
spawn invulnerability at a blue spawn, keeps the production gate open, and leaves vendor
`currentHealth`/`isDead` unchanged.

## Unity Test Timing and Cleanup Rules

Two live timing details are now part of the migration contract:

- `LoadSceneInPlayMode` does not guarantee that `sceneLoaded` has run when the call returns.
  Wait for the configured GameFlow, registered lineup, `MatchState.Playing`, and
  `GameFlow.DebugPhase` before inspecting the scene.
- Calling a widget's public pointer method publishes state; `PlayerBrawlerInput.Update`
  consumes it later. Use a bounded frame loop for joystick/cast release propagation instead
  of assuming one `yield return null` resumes after the consumer.

Prefer explicit loops when dereferencing live Unity actors after an EnterPlayMode domain
reload. Keep restoration evidence and user-state snapshots in `SessionState`. The test has a
`UnityTearDown` safety net that unsubscribes the scene callback, restores the exact gameplay
coach PlayerPrefs value and `MatchSetup`, exits Play mode after failures, and clears session
keys. It never deletes or creates `Automation/autopilot.flag`; the proof fails clearly if
that flag is present because autopilot intentionally selects an AI Legacy player.

## Test Evidence

All recorder runs restored the caller's dirty `Assets/Scenes/MainMenu.unity`. Unity ended in
Edit mode with zero Console warnings/errors.

| Gate | Result | Recorder duration | Live testcase duration | XML SHA-256 |
|---|---:|---:|---:|---|
| Phase 3F generated-Arena human rollout | 1/1 | 15.0808673 s | 45.300150 s | `92FDDDBA0BF1507252CD7DF25199D65B0BC7B0239CA7015D9E99DE2369AE9F19` |
| Complete EditMode regression | 215/215 | 6.7119573 s | Phase 3F 45.377731 s; Phase 3E 39.509054 s | `8FD75201B5F4018B3054AF11A6C45A3C167AA0093EFA4B1B8BFE23C0F42FD46F` |

Aggregate recorder duration excludes work across PlayMode domain reloads; use the individual
testcase durations when estimating this gate.

The runtime and editor projects also compile with zero errors using
`BuildProjectReferences=false`. Existing vendor deprecation warnings remain unchanged. The
read-only audit still reports Unity 6000.3.7f1, Invector 2.6.6, Input System only, 388 vendor
C# files, 320 prefabs, 14 controllers, three PDF manuals, the approved layer map, and no
obsolete P001/P002 source patterns.

## Nonclaims and Next Slice

Phase 3F proves the generated Arena opt-in and rollback path. It does not by itself justify
opening `BrawlerBackend.Invector`, deleting Legacy, or giving vendor systems gameplay
authority. The serialized switch intentionally remains false while migration work continues.

The next bounded slice is the Invector AI movement boundary for one Cinder bot: retain
`AIBrawler` decisions and `IBrawlerNavigation` path planning, disable NavMeshAgent
position/rotation writes, feed desired velocity into one `InvectorBrawlerMotor`, and prove
navigation, stuck/off-mesh handling, knockback/Ward Step interleavings, death/respawn, and
mixed-roster rollback before converting another character.
