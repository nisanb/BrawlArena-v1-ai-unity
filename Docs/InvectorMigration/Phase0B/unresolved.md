# Phase 0B Activation Items

The inactive hardened Phase 3A pilot passed compilation, two-build determinism, focused
8/8, full 156/156, and the final zero-error Console gate. Nothing below authorizes
enabling an Invector lifecycle or damage path; Phase 3B is still unimplemented.

## Resolved by Dormant Phase 3A

- The exact stock input/controller components are replaced by
  `InvectorShooterMeleeInputAdapter` and `BrawlInvectorThirdPersonController`.
- Static source guards enumerate one gated `base.FixedUpdate()` scheduler, three guarded
  public presentation calls, and no adapter `UnityEngine.Input`/GenericInput poll.
- `AutoCrouch` is absent from both the live and serialized tag tables; the project
  controller bypasses both stock comparisons while preserving action trigger events.
- The serialized custom-tag list remains empty. Live `Ignore Ragdoll` presence is
  neither required nor forbidden; Phase 3A does not depend on it because vendor shooter
  `Start` is terminally replaced and never creates the tagged aim helper.
- Every retained object reference and LayerMask passed the Phase 3A audit.
- The controller is serialized with `customFixedTimeStep = Default` and overrides
  `SetCustomFixedTimeStep`, preventing any global `Time.fixedDeltaTime` write.
- Brawl Ward Flow remains the gameplay stamina authority; the adapter intercepts the
  melee state callback without inherited stamina subtraction.
- `OnReceiveAttack` fails closed and never reaches Invector health/damage.

## Required Before Phase 3B Activation

- Replace the lab's plain framing Camera with exactly one configured `BrawlCamera`.
- Live-prove one input reader, one scheduler, one Animator writer stack, no legacy input
  execution, and no Invector stamina-consumption path.
- Keep Invector health, damage, lifecycle, inventory, shooter aim/IK, and production
  assembler selection disabled.

## Deferred Subsystems

- Add the selective-layer alternative to `CombatPhysics.SetLayerRecursively` before the Brawl facade is installed on an Invector hierarchy.
- Keep Invector health immortal/dormant until a one-way Brawl vitals bridge proves one damage/death lifecycle.
- BodyPart: select only genuine future collider/receiver nodes; source layer 15 is mixed and has no blanket target.
- HeadTrack and shooter IK: configure Cinder Humanoid bones and weapon-specific targets in the animation/IK phase.
- StopMove, triggers, generic actions, ladders, climbing, water, cover, pushables, and AI visibility: no layers/tags are reserved yet.
- Weapon and inventory tags/UI/data: no package inventory authority is retained in Phase 2.
- Enemy/CompanionAI: never replace Brawl `TeamId` or `AIBrawler` decisions with package tags.
- PostProcess and collision exclusions: default remains deferred/current matrix.
- Three binary/non-YAML assets: inspect only if their owning subsystem enters scope.

The scanner remains the source of inventory evidence. Re-run it after any package update, TagManager change, or expansion of the retained pilot component set.
