using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace Crownfall
{
    /// Souls-style character motor shared by the player and AI.
    /// Drivers (PlayerController / AIController) only feed intent; the motor owns
    /// state, commitment, i-frames, stamina gating and animator flow.
    [RequireComponent(typeof(CharacterController))]
    public class CombatMotor : MonoBehaviour
    {
        [Header("Wired by forge")]
        public TrailRenderer weaponTrail;
        public Transform weaponTip;
        public GameObject enchantFx;

        public ClassKit Kit { get; private set; }
        public CombatantIdentity Identity { get; private set; }
        public Health Health { get; private set; }
        public Stamina Stamina { get; private set; }
        public Animator Anim { get; private set; }

        public MotorState State { get; private set; } = MotorState.Locomotion;
        public CombatMotor LockTarget { get; set; }
        public bool IsDead => State == MotorState.Dead;
        public bool IsInvulnerable { get; private set; }
        public bool IsSprinting { get; private set; }
        public bool IsBlockingHeld => blockHeld && Kit != null && Kit.canBlock && State == MotorState.Locomotion && Stamina.Current > 0f;
        public Vector3 AimPoint => transform.position + Vector3.up * 1.15f;
        public Vector3 PlanarVelocity => new Vector3(velocity.x, 0f, velocity.z);

        static readonly int HashMoveX = Animator.StringToHash("MoveX");
        static readonly int HashMoveZ = Animator.StringToHash("MoveZ");
        static readonly int HashLocoRate = Animator.StringToHash("LocoRate");
        static readonly int HashRollX = Animator.StringToHash("RollX");
        static readonly int HashRollZ = Animator.StringToHash("RollZ");
        static readonly int HashLocked = Animator.StringToHash("Locked");
        static readonly int HashBlocking = Animator.StringToHash("Blocking");
        static readonly int HashAttackL = Animator.StringToHash("AttackL");
        static readonly int HashAttackH = Animator.StringToHash("AttackH");
        static readonly int HashRoll = Animator.StringToHash("Roll");
        static readonly int HashHit = Animator.StringToHash("Hit");
        static readonly int HashStagger = Animator.StringToHash("Stagger");
        static readonly int HashRecover = Animator.StringToHash("Recover");
        static readonly int HashDie = Animator.StringToHash("Die");
        static readonly int HashRespawn = Animator.StringToHash("Respawn");
        static readonly int HashVictory = Animator.StringToHash("Victory");
        static readonly int HashBlockImpact = Animator.StringToHash("BlockImpact");

        CharacterController cc;
        Vector3 velocity;
        float yVel;
        Vector3 moveInput;
        bool sprintHeld;
        bool blockHeld;

        float bufferedLightUntil, bufferedHeavyUntil, bufferedRollUntil;
        Vector3 bufferedRollDir;
        float flinchImmuneUntil;   // brief flinch immunity so focus-fire can't chain-stun you

        int comboIndex;
        bool comboWindowOpen;
        Coroutine actionRoutine;
        float animMoveX, animMoveZ;
        CombatMotor attackAim;

        // Ranged cast timing, in normalized clip time. Casters do not use the melee
        // strike/lunge/trail pipeline: the bolt leaves the wand at the forward-point
        // apex, the nova erupts at the overhead slam's contact. Kept as fields so the
        // motion-review loop can retune them without touching the routine.
        const float BoltReleaseNt = 0.32f;   // let the forward wind-up read before the bolt leaves
        const float BoltCutNt     = 0.54f;   // blend back to locomotion (still a snappy bolt)
        const float NovaReleaseNt = 0.46f;   // staff slams down -> AoE
        const float NovaCutNt     = 0.82f;   // longer follow-through for the big cast
        Vector3 enchantBaseScale;             // rest scale of the wand aura (forge-set)

        public CombatMotor LastEngagedEnemy { get; private set; }
        public float LastEngagedAt { get; private set; } = -99f;

        public bool IsConcealed { get; private set; }
        float revealedUntil;
        float concealTimer;
        bool renderersHidden;
        Renderer[] visualRenderers;
        Material ringMat;
        Color ringBaseColor;
        static readonly Color RingConcealColor = new Color(0.35f, 0.9f, 0.4f, 0.8f);

        void Awake()
        {
            cc = GetComponent<CharacterController>();
            Anim = GetComponentInChildren<Animator>();
            Identity = GetComponent<CombatantIdentity>();
            Kit = ClassKits.Get(Identity != null ? Identity.classId : ClassId.Knight);

            Health = GetComponent<Health>();
            if (Health == null) Health = gameObject.AddComponent<Health>();
            Health.Configure(this, Kit.maxHealth, Kit.maxPoise);

            Stamina = GetComponent<Stamina>();
            if (Stamina == null) Stamina = gameObject.AddComponent<Stamina>();
            Stamina.Configure(this, Kit.maxStamina);

            if (Anim != null)
            {
                Anim.applyRootMotion = false;
                Anim.cullingMode = AnimatorCullingMode.AlwaysAnimate;
            }
            SetTrail(false);

            visualRenderers = GetComponentsInChildren<Renderer>(true);
            foreach (var r in visualRenderers)
            {
                if (r.name == "TeamRing")
                {
                    ringMat = r.sharedMaterial;
                    if (ringMat != null) ringBaseColor = ringMat.color;
                }
            }

            if (enchantFx != null) enchantBaseScale = enchantFx.transform.localScale;
        }

        // ------------------------------------------------------------------ concealment

        public bool IsConcealedFrom(CombatMotor viewer)
        {
            return IsConcealed && viewer != null &&
                   (viewer.transform.position - transform.position).sqrMagnitude > 9f;
        }

        public void MarkRevealed(float duration)
        {
            revealedUntil = Mathf.Max(revealedUntil, Time.time + duration);
        }

        void UpdateConcealment()
        {
            // Concealment is an ambush tool, not a mid-fight state: it needs a
            // moment of quiet inside the bush, and ANY enemy within 5m breaks it
            // (wider than the 3m targeting radius, so there is no flicker window).
            bool eligible = !IsDead && State != MotorState.Attacking &&
                            Time.time > revealedUntil && BushField.IsInBush(transform.position);
            if (eligible && MatchManager.I != null && Identity != null)
            {
                foreach (var e in MatchManager.I.AliveEnemiesOf(Identity.team))
                {
                    if ((e.transform.position - transform.position).sqrMagnitude < 25f)
                    {
                        eligible = false;
                        break;
                    }
                }
            }
            concealTimer = eligible ? concealTimer + Time.deltaTime : 0f;
            IsConcealed = eligible && concealTimer > 0.8f;

            var pm = MatchManager.I != null ? MatchManager.I.PlayerMotor : null;
            bool hideFromView = pm != null && pm != this && Identity != null && pm.Identity != null &&
                                Identity.team != pm.Identity.team && IsConcealedFrom(pm);
            if (hideFromView != renderersHidden)
            {
                renderersHidden = hideFromView;
                foreach (var r in visualRenderers)
                    if (r != null) r.enabled = !hideFromView;
            }

            if (ringMat != null)
                ringMat.color = Color.Lerp(ringMat.color,
                    IsConcealed ? RingConcealColor : ringBaseColor, 10f * Time.deltaTime);
        }

        // ------------------------------------------------------------------ driver API

        public void SetMoveInput(Vector3 worldDir, bool sprint)
        {
            moveInput = Vector3.ClampMagnitude(new Vector3(worldDir.x, 0f, worldDir.z), 1f);
            sprintHeld = sprint;
        }

        public void RequestLight() { bufferedLightUntil = Time.time + Tuning.InputBufferSeconds; }
        public void RequestHeavy() { bufferedHeavyUntil = Time.time + Tuning.InputBufferSeconds; }

        public void RequestRoll(Vector3 worldDir)
        {
            bufferedRollUntil = Time.time + Tuning.InputBufferSeconds;
            bufferedRollDir = worldDir.sqrMagnitude > 0.01f ? worldDir.normalized : -transform.forward;
        }

        public void SetBlock(bool held) { blockHeld = held; }

        // ------------------------------------------------------------------ frame loop

        void Update()
        {
            UpdateConcealment();

            // the ranged cast drives animator speed and the wand-aura charge pulse;
            // if a cast is interrupted (hit/stagger/death) make sure neither is left
            // stuck away from its rest value
            if (State != MotorState.Attacking)
            {
                if (Anim != null && Anim.speed != 1f) Anim.speed = 1f;
                if (enchantFx != null && enchantBaseScale != Vector3.zero &&
                    enchantFx.transform.localScale != enchantBaseScale)
                    enchantFx.transform.localScale = enchantBaseScale;
            }

            if (State == MotorState.Dead || State == MotorState.Victory)
            {
                ApplyGravity();
                cc.Move(Vector3.up * yVel * Time.deltaTime);
                UpdateAnimatorParams(Vector3.zero);
                return;
            }

            FlushBuffers();

            if (State == MotorState.Locomotion)
                LocomotionMove();
            else
            {
                // committed states: bleed residual drift, keep grounded
                velocity = Vector3.MoveTowards(velocity, Vector3.zero, 18f * Time.deltaTime);
                ApplyGravity();
                cc.Move((velocity * 0.25f + Vector3.up * yVel) * Time.deltaTime);
            }

            UpdateAnimatorParams(PlanarVelocity);
        }

        void FlushBuffers()
        {
            float now = Time.time;
            bool wantLight = bufferedLightUntil > now;
            bool wantHeavy = bufferedHeavyUntil > now;
            bool wantRoll = bufferedRollUntil > now;

            if (State == MotorState.Locomotion)
            {
                if (wantRoll && Stamina.TrySpend(Kit.staminaRoll))
                {
                    bufferedRollUntil = 0f;
                    StartRoll(bufferedRollDir);
                    return;
                }
                if (wantHeavy && Stamina.TrySpend(Kit.staminaHeavy))
                {
                    bufferedHeavyUntil = 0f;
                    StartAttack(true);
                    return;
                }
                if (wantLight && Stamina.TrySpend(Kit.staminaLight))
                {
                    bufferedLightUntil = 0f;
                    StartAttack(false);
                }
            }
        }

        void LocomotionMove()
        {
            bool canSprint = sprintHeld && moveInput.sqrMagnitude > 0.1f && Stamina.Current > 1f && !IsBlockingHeld;
            IsSprinting = canSprint;
            if (canSprint) Stamina.Drain(Tuning.SprintStaminaPerSec * Time.deltaTime, 0.4f);

            float speed = Kit.runSpeed;
            if (canSprint) speed *= Kit.sprintMultiplier;
            if (IsBlockingHeld) speed *= 0.42f;

            Vector3 desired = moveInput * speed;
            float rate = desired.sqrMagnitude > velocity.sqrMagnitude ? 40f : 46f;
            velocity = Vector3.MoveTowards(velocity, desired, rate * Time.deltaTime);

            ApplyGravity();
            cc.Move((velocity + Vector3.up * yVel) * Time.deltaTime);

            // rotation: face lock target while not sprinting, else face travel direction
            if (LockTarget != null && !LockTarget.IsDead && !canSprint)
            {
                Vector3 to = LockTarget.transform.position - transform.position;
                to.y = 0f;
                if (to.sqrMagnitude > 0.05f)
                    transform.rotation = Quaternion.RotateTowards(transform.rotation,
                        Quaternion.LookRotation(to), 720f * Time.deltaTime);
            }
            else if (moveInput.sqrMagnitude > 0.04f)
            {
                transform.rotation = Quaternion.RotateTowards(transform.rotation,
                    Quaternion.LookRotation(moveInput), 950f * Time.deltaTime);
            }
        }

        void ApplyGravity()
        {
            if (cc.isGrounded) yVel = -3f;
            else yVel = Mathf.Max(yVel - 28f * Time.deltaTime, -35f);
        }

        void UpdateAnimatorParams(Vector3 planarVel)
        {
            if (Anim == null) return;
            bool locked = LockTarget != null && !LockTarget.IsDead && !IsSprinting;

            float targetX, targetZ;
            if (locked)
            {
                Vector3 local = transform.InverseTransformDirection(planarVel) / Mathf.Max(0.01f, Kit.runSpeed);
                targetX = Mathf.Clamp(local.x, -1f, 1f);
                targetZ = Mathf.Clamp(local.z, -1f, 1f);
            }
            else
            {
                float s = planarVel.magnitude / Mathf.Max(0.01f, Kit.runSpeed);
                targetX = 0f;
                targetZ = Mathf.Clamp(IsSprinting ? s * 1.38f : s, 0f, 2f);
            }

            animMoveX = Mathf.Lerp(animMoveX, targetX, 16f * Time.deltaTime);
            animMoveZ = Mathf.Lerp(animMoveZ, targetZ, 16f * Time.deltaTime);
            Anim.SetFloat(HashMoveX, animMoveX);
            Anim.SetFloat(HashMoveZ, animMoveZ);
            Anim.SetBool(HashLocked, locked);
            Anim.SetBool(HashBlocking, IsBlockingHeld);

            // sync locomotion cycle rate to actual ground speed so feet do not slide
            float speed01 = planarVel.magnitude / Mathf.Max(0.01f, Kit.runSpeed);
            float locoRate = IsSprinting ? 1.15f : Mathf.Lerp(0.95f, 1.4f, Mathf.Clamp01(speed01));
            Anim.SetFloat(HashLocoRate, locoRate);
        }

        // ------------------------------------------------------------------ attacks

        void StartAttack(bool heavy)
        {
            State = MotorState.Attacking;
            comboIndex = 1;
            comboWindowOpen = false;
            MarkRevealed(1.3f); // attacking breaks concealment

            // auto-aim: face the stick, acquire the best target, then face IT
            if (moveInput.sqrMagnitude > 0.04f && (LockTarget == null || LockTarget.IsDead))
                transform.rotation = Quaternion.LookRotation(moveInput);
            attackAim = AcquireAttackAim();
            if (attackAim != null)
            {
                Vector3 toAim = attackAim.transform.position - transform.position;
                toAim.y = 0f;
                if (toAim.sqrMagnitude > 0.04f)
                    transform.rotation = Quaternion.LookRotation(toAim);
            }

            if (actionRoutine != null) StopCoroutine(actionRoutine);
            Anim.ResetTrigger(HashRoll);
            Anim.speed = 1f;
            Anim.SetTrigger(heavy ? HashAttackH : HashAttackL);
            // ranged casters run a dedicated spell routine (charge -> release-timed
            // bolt/nova -> snappy recovery); melee keeps the strike/lunge/combo flow
            actionRoutine = StartCoroutine(Kit.isRanged ? CastRoutine(heavy) : AttackRoutine(heavy));
        }

        CombatMotor AcquireAttackAim()
        {
            if (LockTarget != null && !LockTarget.IsDead) return LockTarget;
            if (MatchManager.I == null || Identity == null) return null;
            float maxRange = Kit.isRanged ? 16f : 9f;
            Vector3 refFwd = moveInput.sqrMagnitude > 0.04f ? moveInput.normalized : transform.forward;
            CombatMotor best = null;
            float bestScore = float.MinValue;
            foreach (var e in MatchManager.I.AliveEnemiesOf(Identity.team))
            {
                if (e.IsConcealedFrom(this)) continue;
                Vector3 to = e.transform.position - transform.position;
                to.y = 0f;
                float dist = to.magnitude;
                if (dist > maxRange) continue;
                // full-circle acquisition; the angle term just prefers what you face
                float ang = Vector3.Angle(refFwd, to);
                float score = -dist * 1.2f - ang * 0.045f;
                if (score > bestScore) { bestScore = score; best = e; }
            }
            return best;
        }

        IEnumerator AttackRoutine(bool heavy)
        {
            int prevHash = 0;
            while (State == MotorState.Attacking)
            {
                // --- wait for the next attack state to begin
                float waited = 0f;
                AnimatorStateInfo st = default;
                bool entered = false;
                while (waited < 0.6f)
                {
                    st = CurrentOrNextState();
                    if (st.IsTag("Attack") && st.shortNameHash != prevHash) { entered = true; break; }
                    waited += Time.deltaTime;
                    yield return null;
                }
                if (!entered || State != MotorState.Attacking) break;
                prevHash = st.shortNameHash;

                float len = Mathf.Max(0.35f, st.length);
                comboWindowOpen = false;
                bool struck = false, swung = false, chained = false;
                float trailOffAt = 0.78f;
                float lungeDist = -1f;
                SetTrail(false);

                float t = 0f;
                while (State == MotorState.Attacking)
                {
                    var cur = Anim.GetCurrentAnimatorStateInfo(0);
                    if (cur.shortNameHash == prevHash) t = cur.normalizedTime;
                    else t += Time.deltaTime / len; // transitioning frames

                    // magnetize toward the attack target before the strike lands
                    if (t < Tuning.StrikeMoment && attackAim != null && !attackAim.IsDead)
                    {
                        Vector3 to = attackAim.transform.position - transform.position;
                        to.y = 0f;
                        if (to.sqrMagnitude > 0.04f)
                            transform.rotation = Quaternion.RotateTowards(transform.rotation,
                                Quaternion.LookRotation(to), 560f * Time.deltaTime);
                    }

                    // lunge through the swing; reach further when the target needs it
                    if (!Kit.isRanged && t >= Tuning.LungeStart && t <= Tuning.LungeEnd)
                    {
                        if (lungeDist < 0f)
                        {
                            lungeDist = heavy ? Kit.heavyLunge : Kit.lightLunge;
                            if (attackAim != null && !attackAim.IsDead)
                            {
                                Vector3 toAim = attackAim.transform.position - transform.position;
                                toAim.y = 0f;
                                float need = toAim.magnitude - Kit.attackRange * 0.85f;
                                lungeDist = Mathf.Clamp(need, 0.15f, heavy ? 2.6f : 2.0f);
                            }
                        }
                        float window = (Tuning.LungeEnd - Tuning.LungeStart) * len;
                        cc.Move(transform.forward * (lungeDist / Mathf.Max(0.05f, window)) * Time.deltaTime);
                    }

                    if (!swung && t >= Tuning.StrikeMoment - 0.1f)
                    {
                        swung = true;
                        SetTrail(true);
                        GameEffects.I?.PlaySwing(transform.position, heavy);
                        // elemental arc thrown along the swing (cleave for heavies)
                        GameEffects.I?.SlashArc(Identity.element,
                            transform.position + transform.forward * 0.9f + Vector3.up * 1.1f,
                            transform.rotation, heavy);
                    }

                    if (!struck && t >= Tuning.StrikeMoment)
                    {
                        struck = true;
                        DoStrike(heavy);
                    }

                    if (t >= trailOffAt) SetTrail(false);

                    if (t >= Tuning.ComboWindowOpen) comboWindowOpen = true;

                    // combo chaining: melee light chains up to 3; a ranged caster
                    // fires one clean cast per press (no 3x wand flail), heavy never chains
                    if (comboWindowOpen && !heavy && !Kit.isRanged && comboIndex < 3 &&
                        bufferedLightUntil > Time.time && Stamina.TrySpend(Kit.staminaLight))
                    {
                        bufferedLightUntil = 0f;
                        comboIndex++;
                        attackAim = AcquireAttackAim();
                        Anim.SetTrigger(HashAttackL);
                        chained = true;
                        break;
                    }

                    // roll-cancel out of recovery
                    if (t >= Tuning.RollCancelPoint && bufferedRollUntil > Time.time &&
                        Stamina.TrySpend(Kit.staminaRoll))
                    {
                        bufferedRollUntil = 0f;
                        StartRoll(bufferedRollDir);
                        yield break;
                    }

                    if (t >= 0.92f) break;
                    yield return null;
                }

                SetTrail(false);
                if (!chained) break;
            }

            SetTrail(false);
            if (State == MotorState.Attacking) State = MotorState.Locomotion;
        }

        // Ranged cast: a caster plants and channels rather than swinging. No weapon
        // trail, no melee whoosh, no lunge. The wand aura swells through the windup as
        // a charge tell, the projectile/nova leaves at the gesture's release apex, then
        // a short snappy recovery blends straight back to locomotion (roll-cancelable).
        IEnumerator CastRoutine(bool heavy)
        {
            SetTrail(false);

            // wait for the cast state to actually begin
            float waited = 0f;
            AnimatorStateInfo st = default;
            while (waited < 0.5f)
            {
                st = CurrentOrNextState();
                if (st.IsTag("Attack")) break;
                waited += Time.deltaTime;
                yield return null;
            }
            if (State != MotorState.Attacking) yield break;

            float len = Mathf.Max(0.3f, st.length);
            float release = heavy ? NovaReleaseNt : BoltReleaseNt;
            float cut = heavy ? NovaCutNt : BoltCutNt;
            float chargePeak = heavy ? 3.2f : 2.6f;   // the tell must read at chase-cam distance

            GameEffects.I?.PlayCast(Identity.element, weaponTip != null ? weaponTip.position : AimPoint);
            Anim.speed = 0.9f;   // deliberate wind-up so the charge is legible; snaps on release
            GameObject castChargeFx = weaponTip != null
                ? GameEffects.I?.SpawnCharge(Identity.element, weaponTip, heavy ? 1.5f : 1.1f) : null;

            bool released = false;
            float t = 0f;
            while (State == MotorState.Attacking)
            {
                var cur = Anim.GetCurrentAnimatorStateInfo(0);
                t = cur.IsTag("Attack") ? cur.normalizedTime : t + Time.deltaTime / len;

                // charge tell: the wand aura grows into the release, then eases back
                if (enchantFx != null && enchantBaseScale != Vector3.zero)
                {
                    float mul = !released
                        ? Mathf.Lerp(1f, chargePeak, Mathf.Clamp01(t / Mathf.Max(0.01f, release)))
                        : Mathf.Lerp(chargePeak, 1f, Mathf.Clamp01((t - release) / 0.28f));
                    enchantFx.transform.localScale = enchantBaseScale * mul;
                }

                // hold the aim on the target through the windup (no melee lunge/magnet)
                if (!released && attackAim != null && !attackAim.IsDead)
                {
                    Vector3 to = attackAim.transform.position - transform.position;
                    to.y = 0f;
                    if (to.sqrMagnitude > 0.04f)
                        transform.rotation = Quaternion.RotateTowards(transform.rotation,
                            Quaternion.LookRotation(to), 620f * Time.deltaTime);
                }

                if (!released && t >= release)
                {
                    released = true;
                    Anim.speed = 1.3f;        // snap the release/recoil
                    if (castChargeFx != null) { Destroy(castChargeFx); castChargeFx = null; }
                    DoStrike(heavy);          // fires bolt or nova (+ muzzle/nova vfx, cast sfx)
                }

                // roll-cancel out of the recovery
                if (released && bufferedRollUntil > Time.time && Stamina.TrySpend(Kit.staminaRoll))
                {
                    bufferedRollUntil = 0f;
                    Anim.speed = 1f;
                    if (castChargeFx != null) Destroy(castChargeFx);
                    if (enchantFx != null && enchantBaseScale != Vector3.zero)
                        enchantFx.transform.localScale = enchantBaseScale;
                    StartRoll(bufferedRollDir);
                    yield break;
                }

                if (t >= cut) break;
                yield return null;
            }

            if (castChargeFx != null) Destroy(castChargeFx);
            if (!released) DoStrike(heavy);   // safety: a cast never fizzles
            Anim.speed = 1f;
            if (enchantFx != null && enchantBaseScale != Vector3.zero)
                enchantFx.transform.localScale = enchantBaseScale;
            if (State == MotorState.Attacking)
            {
                State = MotorState.Locomotion;
                if (Anim != null) Anim.CrossFadeInFixedTime("Locomotion", 0.14f);
            }
        }

        AnimatorStateInfo CurrentOrNextState()
        {
            if (Anim.IsInTransition(0)) return Anim.GetNextAnimatorStateInfo(0);
            return Anim.GetCurrentAnimatorStateInfo(0);
        }

        void DoStrike(bool heavy)
        {
            if (Kit.isRanged)
            {
                if (heavy) DoNova();
                else DoBolt();
                return;
            }

            float dmg = heavy ? Kit.heavyDamage : Kit.lightDamage;
            float poise = heavy ? Kit.heavyPoiseDamage : Kit.lightPoiseDamage;
            if (!heavy && comboIndex == 3) { dmg *= Tuning.ComboFinisherMult; poise *= 1.3f; }
            else if (!heavy && comboIndex == 2) dmg *= 1.05f;

            Vector3 origin = transform.position + transform.forward * Kit.attackRange + Vector3.up * 1.05f;
            var cols = Physics.OverlapSphere(origin, Kit.attackRadius);
            var seen = new HashSet<Health>();
            bool anyHit = false, anyKill = false;

            foreach (var col in cols)
            {
                var victim = col.GetComponentInParent<Health>();
                if (victim == null || victim.Motor == this || seen.Contains(victim)) continue;
                if (victim.IsDead || victim.Identity == null || Identity == null) continue;
                if (victim.Identity.team == Identity.team) continue;
                seen.Add(victim);

                Vector3 to = victim.transform.position - transform.position;
                to.y = 0f;
                if (Vector3.Angle(transform.forward, to) > Tuning.MeleeHitAngle) continue;

                var hit = new HitInfo
                {
                    attacker = this,
                    damage = dmg,
                    poiseDamage = poise,
                    direction = to.normalized,
                    point = victim.Motor != null ? victim.Motor.AimPoint - to.normalized * 0.3f
                                                 : victim.transform.position + Vector3.up,
                    element = Identity.element,
                    heavy = heavy,
                };
                var res = victim.TakeHit(hit);
                if (!res.landed) continue;
                anyHit = true;
                anyKill |= res.killed;
                if (victim.Motor != null) { LastEngagedEnemy = victim.Motor; LastEngagedAt = Time.time; }

                GameEffects.I?.MeleeImpact(Identity.element, hit.point, res.blocked, heavy);
                GameEffects.I?.ShowDamage(hit.point, res.damageDealt, res.blocked);
            }

            if (anyHit)
            {
                bool playerInvolved = Identity.isPlayer;
                foreach (var v in seen)
                    if (v.Identity != null && v.Identity.isPlayer) playerInvolved = true;
                GameEffects.I?.Hitstop(anyKill || heavy ? Tuning.HitstopHeavy : Tuning.HitstopLight);
                if (playerInvolved) OrbitCamera.I?.Shake(heavy ? 0.5f : 0.25f);
            }
        }

        void DoBolt()
        {
            Vector3 origin = weaponTip != null ? weaponTip.position : AimPoint + transform.forward * 0.5f;
            CombatMotor homing = attackAim != null && !attackAim.IsDead ? attackAim
                : (LockTarget != null && !LockTarget.IsDead ? LockTarget : null);
            Vector3 aim = homing != null ? homing.AimPoint : AimPoint + transform.forward * 14f;
            GameEffects.I?.Muzzle(Identity.element, origin, transform.rotation);
            GameEffects.I?.PlayCast(Identity.element, origin);
            if (homing != null) { LastEngagedEnemy = homing; LastEngagedAt = Time.time; }
            Projectile.Fire(this, Identity.element, origin, aim, homing,
                Kit.lightDamage, Kit.lightPoiseDamage, Kit.projectileSpeed);
        }

        void DoNova()
        {
            GameEffects.I?.Nova(Identity.element, transform.position);
            GameEffects.I?.PlayCast(Identity.element, transform.position);
            foreach (var victim in MatchManager.I ? MatchManager.I.AliveEnemiesOf(Identity.team) : new List<CombatMotor>())
            {
                Vector3 to = victim.transform.position - transform.position;
                if (to.magnitude > Kit.novaRadius) continue;
                var hit = new HitInfo
                {
                    attacker = this,
                    damage = Kit.heavyDamage,
                    poiseDamage = Kit.heavyPoiseDamage,
                    direction = to.normalized,
                    point = victim.AimPoint,
                    element = Identity.element,
                    heavy = true,
                    unblockable = true,
                };
                var res = victim.Health.TakeHit(hit);
                if (res.landed)
                {
                    GameEffects.I?.ShowDamage(hit.point, res.damageDealt, false);
                    LastEngagedEnemy = victim;
                    LastEngagedAt = Time.time;
                }
            }
            OrbitCamera.I?.ShakeIfNear(transform.position, 8f, 0.6f);
            if (Identity != null && Identity.isPlayer) GameEffects.I?.Hitstop(Tuning.HitstopHeavy);
        }

        // ------------------------------------------------------------------ roll

        void StartRoll(Vector3 dir)
        {
            State = MotorState.Rolling;
            if (actionRoutine != null) StopCoroutine(actionRoutine);
            actionRoutine = StartCoroutine(RollRoutine(dir));
        }

        IEnumerator RollRoutine(Vector3 dir)
        {
            bool locked = LockTarget != null && !LockTarget.IsDead;
            if (!locked)
            {
                transform.rotation = Quaternion.LookRotation(dir);
                Anim.SetFloat(HashRollX, 0f);
                Anim.SetFloat(HashRollZ, 1f);
            }
            else
            {
                Vector3 local = transform.InverseTransformDirection(dir);
                Anim.SetFloat(HashRollX, Mathf.Abs(local.x) > Mathf.Abs(local.z) ? Mathf.Sign(local.x) : 0f);
                Anim.SetFloat(HashRollZ, Mathf.Abs(local.z) >= Mathf.Abs(local.x) ? Mathf.Sign(local.z) : 0f);
            }
            Anim.SetTrigger(HashRoll);
            GameEffects.I?.PlayRoll(transform.position);

            float waited = 0f;
            AnimatorStateInfo st = default;
            while (waited < 0.5f)
            {
                st = CurrentOrNextState();
                if (st.IsTag("Roll")) break;
                waited += Time.deltaTime;
                yield return null;
            }
            float len = st.IsTag("Roll") ? Mathf.Max(0.4f, st.length) : 0.8f;

            float t = 0f;
            while (State == MotorState.Rolling && t < 0.9f)
            {
                var cur = Anim.GetCurrentAnimatorStateInfo(0);
                t = cur.IsTag("Roll") ? cur.normalizedTime : t + Time.deltaTime / len;

                IsInvulnerable = t >= Tuning.RollIFrameStart && t <= Tuning.RollIFrameEnd;
                float speed = Mathf.Lerp(Tuning.RollSpeedStart, Tuning.RollSpeedEnd, t);
                ApplyGravity();
                cc.Move((dir * speed + Vector3.up * yVel) * Time.deltaTime);
                yield return null;
            }

            IsInvulnerable = false;
            if (State == MotorState.Rolling) State = MotorState.Locomotion;
        }

        // ------------------------------------------------------------------ reactions

        public bool IsBlockEffectiveAgainst(HitInfo hit)
        {
            if (!IsBlockingHeld) return false;
            return Vector3.Angle(transform.forward, -hit.direction) <= Tuning.BlockAngle;
        }

        /// Stamina chip while blocking. Returns true when the guard breaks.
        public bool OnBlockedHit(HitInfo hit)
        {
            Anim.SetTrigger(HashBlockImpact);
            cc.Move(hit.direction * 0.3f);
            return Stamina.Drain(hit.damage * 0.85f + 6f, 0.8f);
        }

        public void NotifyHitReact(HitInfo hit)
        {
            if (IsDead || State == MotorState.Staggered || State == MotorState.Rolling) return;

            // Hyperarmor: a light hit no longer yanks you out of your own committed
            // attack/cast — poise still accrues (a heavy hit or a poise-break can
            // still interrupt), but chip damage stops cancelling every swing/bolt.
            if (State == MotorState.Attacking && !hit.heavy) { flinchImmuneUntil = Time.time + 0.25f; return; }

            // Flinch budget: after a flinch you get brief flinch immunity, so a
            // focus-firing squad can't chain-stun you locked-in-place (the "every
            // hit knocks me and I can't move" feel). The hit still deals damage and
            // spawns its impact vfx/number — you just keep control.
            if (Time.time < flinchImmuneUntil) return;
            if (State != MotorState.Locomotion && State != MotorState.Hit) return;

            if (actionRoutine != null) StopCoroutine(actionRoutine);
            State = MotorState.Hit;
            Anim.SetTrigger(HashHit);
            cc.Move(hit.direction * (hit.heavy ? 0.28f : 0.16f));
            flinchImmuneUntil = Time.time + (hit.heavy ? 0.5f : 0.65f);
            actionRoutine = StartCoroutine(HitRecover(hit.heavy ? 0.4f : 0.24f));
        }

        IEnumerator HitRecover(float dur)
        {
            yield return new WaitForSeconds(dur);
            if (State == MotorState.Hit) State = MotorState.Locomotion;
        }

        GameObject stunFxInstance;

        public void EnterStagger()
        {
            if (IsDead) return;
            if (actionRoutine != null) StopCoroutine(actionRoutine);
            SetTrail(false);
            IsInvulnerable = false;
            State = MotorState.Staggered;
            Anim.ResetTrigger(HashHit);
            Anim.SetTrigger(HashStagger);
            ClearStunFx();
            stunFxInstance = GameEffects.I?.SpawnStun(transform);
            actionRoutine = StartCoroutine(StaggerRoutine());
        }

        IEnumerator StaggerRoutine()
        {
            yield return new WaitForSeconds(Tuning.StaggerDuration);
            ClearStunFx();
            if (State == MotorState.Staggered)
            {
                Anim.SetTrigger(HashRecover);
                yield return new WaitForSeconds(0.15f);
                if (State == MotorState.Staggered) State = MotorState.Locomotion;
                // a breather after being stunned so you aren't instantly re-chained
                flinchImmuneUntil = Time.time + 0.85f;
            }
        }

        void ClearStunFx()
        {
            if (stunFxInstance != null)
            {
                Destroy(stunFxInstance);
                stunFxInstance = null;
            }
        }

        public void EnterDeath()
        {
            if (actionRoutine != null) StopCoroutine(actionRoutine);
            SetTrail(false);
            ClearStunFx();
            MarkRevealed(10f); // corpses are visible
            State = MotorState.Dead;
            IsInvulnerable = true;
            blockHeld = false;
            moveInput = Vector3.zero;
            velocity = Vector3.zero;
            Anim.SetTrigger(HashDie);
            GameEffects.I?.PlayDeath(transform.position);
        }

        public void ResetForRespawn(Vector3 pos, Quaternion rot)
        {
            if (actionRoutine != null) StopCoroutine(actionRoutine);
            ClearStunFx();
            cc.enabled = false;
            transform.SetPositionAndRotation(pos, rot);
            cc.enabled = true;
            velocity = Vector3.zero;
            yVel = 0f;
            moveInput = Vector3.zero;
            comboIndex = 0;
            LockTarget = null;
            Health.ReviveFull();
            Stamina.RefillFull();
            State = MotorState.Locomotion;
            Anim.ResetTrigger(HashDie);
            Anim.SetTrigger(HashRespawn);
            Anim.Play("Locomotion", 0, 0f);
            StartCoroutine(SpawnProtection());
        }

        IEnumerator SpawnProtection()
        {
            IsInvulnerable = true;
            yield return new WaitForSeconds(Tuning.SpawnProtection);
            if (State != MotorState.Rolling) IsInvulnerable = false;
        }

        public void PlayVictory()
        {
            if (IsDead) return;
            if (actionRoutine != null) StopCoroutine(actionRoutine);
            SetTrail(false);
            State = MotorState.Victory;
            Anim.SetTrigger(HashVictory);
        }

        void SetTrail(bool on)
        {
            if (weaponTrail != null) weaponTrail.emitting = on;
        }
    }
}
