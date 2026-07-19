using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace BrawlArena
{
    /// <summary>
    /// Authored per-hero presentation timing for the generated souls-style
    /// animator controllers. Assigned by the character assembler through
    /// <see cref="HeavyAnimationDriver.Configure"/> before Start.
    /// </summary>
    [Serializable]
    public sealed class HeavyAnimationProfile
    {
        public float primaryImpactDelay;
        public float superImpactDelay;
        public float dieVisibleSeconds = 1.35f;
        /// <summary>Attack state playback speed; below 1 reads heavier.</summary>
        public float attackStateSpeed = 1f;
        /// <summary>Melee step-in impulse fed to the motor per swing; 0 disables.</summary>
        public float lungeImpulse;
    }

    /// <summary>
    /// Imperative animation driver for the generated souls-style controllers.
    /// Basic attacks and Supers are FULL-BODY base-layer one-shots — swing
    /// commitment is the genre's weight — while GetHit remains a masked
    /// upper-body overlay so victims stagger without losing their footing.
    /// Melee swings feed a step-in lunge impulse to the heavy motor. Lifecycle
    /// is CrossFade/Rebind driven — no triggers, no dead-end states — so a
    /// respawn is idempotent and a missed request cannot strand a pose.
    /// Failures are loud: log-once per kind plus a running count.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class HeavyAnimationDriver : MonoBehaviour, IBrawlerAnimationDriver
    {
        const int BaseLayer = 0;
        const int UpperBodyLayer = 1;
        const float AttackBlendSeconds = 0.07f;
        const float LifecycleBlendSeconds = 0.08f;
        const float ParameterDampSeconds = 0.12f;
        const string AttackSpeedParameterName = "AttackSpeed";
        const float AttackTrailBaseSeconds = 0.667f;
        const float AttackTrailMinSeconds = 0.3f;
        const float AttackTrailMaxSeconds = 0.9f;

        static readonly int Speed01Hash = Animator.StringToHash("Speed01");
        static readonly int MoveXHash = Animator.StringToHash("MoveX");
        static readonly int MoveZHash = Animator.StringToHash("MoveZ");
        static readonly int LocomotionHash = Animator.StringToHash("Locomotion");
        static readonly int DieHash = Animator.StringToHash("Die");
        static readonly int VictoryHash = Animator.StringToHash("Victory");
        static readonly int DashHash = Animator.StringToHash("Dash");
        static readonly int AttackPrimaryHash = Animator.StringToHash("AttackPrimary");
        static readonly int AttackSuperHash = Animator.StringToHash("AttackSuper");
        static readonly int EmptyHash = Animator.StringToHash("Empty");
        static readonly int GetHitHash = Animator.StringToHash("GetHit");
        static readonly int AttackSpeedHash = Animator.StringToHash(AttackSpeedParameterName);

        static readonly Dictionary<int, string> BaseStateNames = new Dictionary<int, string>
        {
            { LocomotionHash, "Locomotion" },
            { DieHash, "Die" },
            { VictoryHash, "Victory" },
            { Animator.StringToHash("VictoryMaintain"), "VictoryMaintain" },
            { DashHash, "Dash" },
            { AttackPrimaryHash, "AttackPrimary" },
            { AttackSuperHash, "AttackSuper" },
        };

        [SerializeField]
        HeavyAnimationProfile profile = new HeavyAnimationProfile();

        Animator cachedAnimator;
        bool animatorResolved;
        Rigidbody cachedBody;
        bool bodyResolved;
        HeavyBrawlerMotor cachedMotor;
        bool motorResolved;
        HeavyWeaponTrail cachedWeaponTrail;
        bool weaponTrailResolved;
        bool deathPresented;
        bool skinsHidden;
        bool attackSpeedParameterResolved;
        bool hasAttackSpeedParameter;
        int lifecycleFailureCount;
        Coroutine hideRoutine;
        Coroutine hitStopRoutine;
        readonly List<Renderer> hiddenSkins = new List<Renderer>();
        readonly HashSet<string> loggedFailureKinds = new HashSet<string>();

        public HeavyAnimationProfile Profile => profile;

        /// <summary>Loud-diagnostics counter: failed presentation requests.</summary>
        public int LifecycleFailureCount => lifecycleFailureCount;

        public bool DeathPresented => deathPresented;
        public bool SkinsHidden => skinsHidden;

        /// <summary>Current base-layer state name for harness status dumps.</summary>
        public string CurrentBaseStateName
        {
            get
            {
                Animator animator = ResolveAnimator();
                if (animator == null || !animator.isActiveAndEnabled ||
                    animator.layerCount <= BaseLayer)
                    return string.Empty;
                AnimatorStateInfo info = animator.GetCurrentAnimatorStateInfo(BaseLayer);
                return BaseStateNames.TryGetValue(info.shortNameHash, out string stateName)
                    ? stateName
                    : info.shortNameHash.ToString();
            }
        }

        /// <summary>Assembler-facing configuration; called before Start.</summary>
        public void Configure(HeavyAnimationProfile configuredProfile)
        {
            if (configuredProfile == null)
            {
                throw new ArgumentNullException(nameof(configuredProfile));
            }

            profile = configuredProfile;
            TryApplyAttackStateSpeed();
        }

        public void TickLocomotion(float normalizedSpeed)
        {
            if (deathPresented) return;
            Animator animator = ResolveAnimator();
            if (animator == null || !animator.isActiveAndEnabled) return;

            float speed01 = Mathf.Clamp01(normalizedSpeed);
            Vector3 localDirection = Vector3.zero;
            Rigidbody body = ResolveBody();
            if (body != null)
            {
                Vector3 planarVelocity = body.linearVelocity;
                planarVelocity.y = 0f;
                if (planarVelocity.sqrMagnitude > 0.0001f)
                    localDirection = transform.InverseTransformDirection(
                        planarVelocity.normalized);
            }

            float deltaTime = Time.deltaTime;
            if (speed01 <= 0.02f &&
                Mathf.Abs(animator.GetFloat(Speed01Hash)) < 0.05f)
            {
                // Damped SetFloat decays asymptotically and the residual
                // weights keep Run/Walk clips faintly blended while standing
                // still; snap to a true idle once the blend is nearly there.
                animator.SetFloat(Speed01Hash, 0f);
                animator.SetFloat(MoveXHash, 0f);
                animator.SetFloat(MoveZHash, 0f);
                return;
            }
            animator.SetFloat(Speed01Hash, speed01, ParameterDampSeconds, deltaTime);
            animator.SetFloat(MoveXHash, localDirection.x * speed01,
                ParameterDampSeconds, deltaTime);
            animator.SetFloat(MoveZHash, localDirection.z * speed01,
                ParameterDampSeconds, deltaTime);
        }

        /// <summary>
        /// Full-body committed swing plus the melee step-in lunge. The swing
        /// interrupts locomotion on the base layer — that interruption IS the
        /// souls feel — and auto-exits back to Locomotion when it finishes.
        /// </summary>
        public void PlayBasicAttack()
        {
            if (deathPresented) return;
            Animator animator = RequireAnimator(nameof(PlayBasicAttack));
            if (animator == null || !RequireLayer(animator, BaseLayer,
                    nameof(PlayBasicAttack))) return;
            animator.CrossFadeInFixedTime(AttackPrimaryHash, AttackBlendSeconds,
                BaseLayer, 0f);
            FlashWeaponTrail();
            ApplyLunge();
        }

        public void PlaySuper()
        {
            if (deathPresented) return;
            Animator animator = RequireAnimator(nameof(PlaySuper));
            if (animator == null || !RequireLayer(animator, BaseLayer,
                    nameof(PlaySuper))) return;
            animator.CrossFadeInFixedTime(AttackSuperHash, AttackBlendSeconds,
                BaseLayer, 0f);
            FlashWeaponTrail();
        }

        /// <summary>Never interrupts an in-flight death presentation.</summary>
        public void PlayHitReaction()
        {
            if (deathPresented) return;
            Animator animator = ResolveAnimator();
            if (animator == null || !animator.isActiveAndEnabled ||
                animator.layerCount <= UpperBodyLayer) return;
            if (InBaseState(animator, DieHash)) return;
            animator.CrossFadeInFixedTime(GetHitHash, AttackBlendSeconds,
                UpperBodyLayer, 0f);
        }

        public void PlayDeath()
        {
            Animator animator = RequireAnimator(nameof(PlayDeath));
            if (animator == null || !RequireLayer(animator, BaseLayer,
                    nameof(PlayDeath))) return;

            deathPresented = true;
            CancelHitStop(animator);
            animator.CrossFadeInFixedTime(DieHash, LifecycleBlendSeconds, BaseLayer, 0f);
            if (animator.layerCount > UpperBodyLayer)
                animator.CrossFadeInFixedTime(EmptyHash, LifecycleBlendSeconds,
                    UpperBodyLayer, 0f);
            RestartHideTimer();
        }

        /// <summary>
        /// Deterministic, idempotent visual reset: show skins, Rebind, and
        /// force the default states. Safe to call twice; safe without a prior
        /// PlayDeath.
        /// </summary>
        public void PlayRespawn()
        {
            Animator animator = RequireAnimator(nameof(PlayRespawn));
            if (animator == null || !RequireLayer(animator, BaseLayer,
                    nameof(PlayRespawn))) return;

            StopHideTimer();
            SetSkinsHidden(false);
            CancelHitStop(animator);
            deathPresented = false;
            animator.Rebind();
            animator.Play(LocomotionHash, BaseLayer, 0f);
            if (animator.layerCount > UpperBodyLayer)
                animator.Play(EmptyHash, UpperBodyLayer, 0f);
            animator.Update(0f);
            TryApplyAttackStateSpeed();
        }

        public void PlayVictory()
        {
            if (deathPresented) return;
            Animator animator = RequireAnimator(nameof(PlayVictory));
            if (animator == null || !RequireLayer(animator, BaseLayer,
                    nameof(PlayVictory))) return;
            animator.CrossFadeInFixedTime(VictoryHash, LifecycleBlendSeconds,
                BaseLayer, 0f);
        }

        /// <summary>Dodge roll one-shot on the base layer.</summary>
        public void PlayDash(Vector3 worldDir)
        {
            if (deathPresented) return;
            Animator animator = ResolveAnimator();
            if (animator == null || !animator.isActiveAndEnabled ||
                animator.layerCount <= BaseLayer) return;
            animator.CrossFadeInFixedTime(DashHash, AttackBlendSeconds, BaseLayer, 0f);
        }

        /// <summary>
        /// Authored per-hero impact timing; the profile is the single source
        /// of truth, decoupled from clip lengths. Unconfigured values fall
        /// back unmodified.
        /// </summary>
        public float GetAttackImpactDelay(bool strongAttack, float fallbackSeconds)
        {
            if (profile == null) return fallbackSeconds;
            float authored = strongAttack ? profile.superImpactDelay : profile.primaryImpactDelay;
            return authored > 0f ? authored : fallbackSeconds;
        }

        /// <summary>
        /// Hit-stop freezes the Animator without touching Time.timeScale.
        /// Refused outright while the death presentation is active so a
        /// lethal hit can never freeze the corpse mid-transition. The latest
        /// call always wins over one in flight.
        /// </summary>
        public void PauseAnimation(float seconds)
        {
            if (seconds <= 0f || deathPresented || !isActiveAndEnabled ||
                !Application.isPlaying) return;
            Animator animator = ResolveAnimator();
            if (animator == null || !animator.isActiveAndEnabled) return;
            if (InBaseState(animator, DieHash)) return;

            if (hitStopRoutine != null) StopCoroutine(hitStopRoutine);
            hitStopRoutine = StartCoroutine(HitStopRoutine(animator, seconds));
        }

        IEnumerator HitStopRoutine(Animator animator, float seconds)
        {
            animator.speed = 0f;
            yield return new WaitForSeconds(seconds);
            animator.speed = 1f;
            hitStopRoutine = null;
        }

        void CancelHitStop(Animator animator)
        {
            if (hitStopRoutine != null)
            {
                StopCoroutine(hitStopRoutine);
                hitStopRoutine = null;
            }
            if (animator != null) animator.speed = 1f;
        }

        /// <summary>
        /// The melee step-in: a forward impulse through the heavy motor so a
        /// committed swing carries the body into the target instead of
        /// swinging in place. Ranged/caster heroes author zero.
        /// </summary>
        void ApplyLunge()
        {
            if (profile == null || profile.lungeImpulse <= 0f) return;
            HeavyBrawlerMotor motor = ResolveMotor();
            if (motor == null) return;
            Vector3 forward = transform.forward;
            forward.y = 0f;
            if (forward.sqrMagnitude <= 0.0001f) return;
            motor.AddImpulse(forward.normalized * profile.lungeImpulse);
        }

        void RestartHideTimer()
        {
            StopHideTimer();
            if (!isActiveAndEnabled || !Application.isPlaying) return;
            hideRoutine = StartCoroutine(HideSkinsAfterDelay());
        }

        void StopHideTimer()
        {
            if (hideRoutine == null) return;
            StopCoroutine(hideRoutine);
            hideRoutine = null;
        }

        IEnumerator HideSkinsAfterDelay()
        {
            float delay = profile != null ? Mathf.Max(0f, profile.dieVisibleSeconds) : 1.35f;
            yield return new WaitForSeconds(delay);
            hideRoutine = null;
            if (deathPresented) SetSkinsHidden(true);
        }

        void SetSkinsHidden(bool hidden)
        {
            if (hidden == skinsHidden) return;

            if (hidden)
            {
                // Whole-actor sweep: character packs frequently keep body
                // meshes as SIBLINGS of the armature (the hips-derived visual
                // root misses them). Restore re-enables exactly the
                // hiddenSkins list, so the wide sweep cannot leak state.
                Renderer[] renderers = GetComponentsInChildren<Renderer>(true);
                for (int i = 0; i < renderers.Length; i++)
                {
                    Renderer candidate = renderers[i];
                    if (candidate == null || !candidate.enabled) continue;
                    if (!(candidate is SkinnedMeshRenderer) && !(candidate is MeshRenderer))
                        continue;
                    candidate.enabled = false;
                    hiddenSkins.Add(candidate);
                }
                skinsHidden = true;
                return;
            }

            for (int i = 0; i < hiddenSkins.Count; i++)
                if (hiddenSkins[i] != null) hiddenSkins[i].enabled = true;
            hiddenSkins.Clear();
            skinsHidden = false;
        }

        Animator ResolveAnimator()
        {
            if (!animatorResolved || cachedAnimator == null)
            {
                cachedAnimator = GetComponent<Animator>();
                animatorResolved = true;
            }
            return cachedAnimator;
        }

        Rigidbody ResolveBody()
        {
            if (!bodyResolved || cachedBody == null)
            {
                cachedBody = GetComponent<Rigidbody>();
                bodyResolved = true;
            }
            return cachedBody;
        }

        HeavyBrawlerMotor ResolveMotor()
        {
            if (!motorResolved || cachedMotor == null)
            {
                cachedMotor = GetComponent<HeavyBrawlerMotor>();
                motorResolved = true;
            }
            return cachedMotor;
        }

        /// <summary>
        /// Per-swing weapon trail telegraph. The flash window tracks the
        /// authored attack state speed so the trail dies with the swing. A
        /// missing trail component can never fail the attack: null-safe,
        /// no error.
        /// </summary>
        void FlashWeaponTrail()
        {
            HeavyWeaponTrail weaponTrail = ResolveWeaponTrail();
            if (weaponTrail == null) return;
            float attackStateSpeed = profile != null
                ? Mathf.Max(profile.attackStateSpeed, 0.01f)
                : 1f;
            weaponTrail.Flash(Mathf.Clamp(AttackTrailBaseSeconds / attackStateSpeed,
                AttackTrailMinSeconds, AttackTrailMaxSeconds));
        }

        HeavyWeaponTrail ResolveWeaponTrail()
        {
            if (!weaponTrailResolved || cachedWeaponTrail == null)
            {
                cachedWeaponTrail = GetComponentInChildren<HeavyWeaponTrail>(true);
                weaponTrailResolved = true;
            }
            return cachedWeaponTrail;
        }

        Animator RequireAnimator(string request)
        {
            Animator animator = ResolveAnimator();
            if (animator == null || !animator.isActiveAndEnabled)
            {
                FailPresentation(request + ".AnimatorUnavailable",
                    request + " needs an active Animator on the brawler root.");
                return null;
            }
            return animator;
        }

        bool RequireLayer(Animator animator, int layer, string request)
        {
            if (animator.layerCount > layer) return true;
            FailPresentation(request + ".LayerMissing",
                request + " needs animator layer " + layer +
                " but the controller has " + animator.layerCount + ".");
            return false;
        }

        static bool InBaseState(Animator animator, int shortNameHash)
        {
            if (animator.layerCount <= BaseLayer) return false;
            if (animator.GetCurrentAnimatorStateInfo(BaseLayer).shortNameHash ==
                shortNameHash) return true;
            return animator.IsInTransition(BaseLayer) &&
                animator.GetNextAnimatorStateInfo(BaseLayer).shortNameHash == shortNameHash;
        }

        /// <summary>
        /// Optional belt-and-braces: if the generated controller ever exposes
        /// an AttackSpeed float parameter, keep it synced to the profile.
        /// Attack state speed is otherwise baked at build time.
        /// </summary>
        void TryApplyAttackStateSpeed()
        {
            Animator animator = ResolveAnimator();
            if (animator == null || animator.runtimeAnimatorController == null ||
                profile == null || profile.attackStateSpeed <= 0f) return;

            if (!attackSpeedParameterResolved)
            {
                hasAttackSpeedParameter = false;
                AnimatorControllerParameter[] parameters = animator.parameters;
                for (int i = 0; i < parameters.Length; i++)
                {
                    if (parameters[i].type == AnimatorControllerParameterType.Float &&
                        parameters[i].name == AttackSpeedParameterName)
                    {
                        hasAttackSpeedParameter = true;
                        break;
                    }
                }
                attackSpeedParameterResolved = true;
            }

            if (hasAttackSpeedParameter)
                animator.SetFloat(AttackSpeedHash, profile.attackStateSpeed);
        }

        void FailPresentation(string kind, string message)
        {
            lifecycleFailureCount++;
            if (loggedFailureKinds.Add(kind))
                Debug.LogError("HeavyAnimationDriver[" + name + "] " + kind +
                    ": " + message, this);
        }

        void OnDisable()
        {
            StopHideTimer();
            if (hitStopRoutine != null)
            {
                StopCoroutine(hitStopRoutine);
                hitStopRoutine = null;
            }
            Animator animator = ResolveAnimator();
            if (animator != null) animator.speed = 1f;
        }
    }
}
