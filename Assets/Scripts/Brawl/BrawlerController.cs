using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace BrawlArena
{
    public enum BrawlerSuperStyle
    {
        Burst,
        Dash,
        ProjectileBlast,
    }

    /// <summary>
    /// Gameplay facade for one Invector-backed brawler body. Human input or AI
    /// tactics provide intent; one required Invector motor, animation driver,
    /// and optional weapon presenter own physical/visual execution.
    /// </summary>
    [RequireComponent(typeof(Health))]
    public class BrawlerController : MonoBehaviour
    {
        [Header("Identity")]
        public string displayName = "Brawler";
        public string playerTag;
        public string role;
        public Sprite portrait;
        public TeamId team = TeamId.Blue;

        [Header("Stats")]
        public float moveSpeed = 5f;
        public float attackDamage = 20f;
        public float attackRange = 2.2f;
        public float attackRadius = 1.5f;
        public float attackCooldown = 0.9f;
        [Tooltip("Seconds into the swing when damage lands / projectile spawns.")]
        public float attackHitDelay = 0.35f;
        public float attackMoveLock = 0.45f;
        public float autoAimRange = 3.5f;

        [Header("Ward Step")]
        [Tooltip("Combat-only Ward Flow. Capacity is fixed at 60 for every brawler.")]
        public float maxStamina = MobileCombatRules.ArcaneFlowCapacity;
        public float wardStepCost = MobileCombatRules.WardStepCost;
        public float wardStepDistance = MobileCombatRules.WardStepDistance;
        public float wardStepDuration = MobileCombatRules.WardStepDuration;
        public float staminaRegenPerSec = MobileCombatRules.WardRegenPerSecond;
        public float staminaRegenDelay = MobileCombatRules.WardRegenDelay;

        [Header("Health regen")]
        [Tooltip("Seconds without taking damage before health regen starts.")]
        public float healthRegenDelay = 3f;
        [Tooltip("Fraction of max health restored per regen tick.")]
        public float healthRegenTickFraction = 0.08f;
        public float healthRegenTickInterval = 0.8f;
        public float respawnDelayMultiplier = 1f;

        [Header("Ranged (leave prefab empty for melee)")]
        public GameObject projectilePrefab;
        public float projectileSpeed = 16f;
        public SpellSpecialty specialty;

        [Header("Super")]
        public string superName = "POWER BURST";
        public BrawlerSuperStyle superStyle = BrawlerSuperStyle.Burst;
        public float maxSuperCharge = 150f;
        public float superDamageMultiplier = 1.6f;
        public float superRange = 3.2f;
        public float superKnockback = 6f;
        public float superDashDistance = 4.8f;
        public float superProjectileSpeed = 22f;
        public float superProjectileBlastRadius = 2f;
        public GameObject superProjectilePrefab;
        public GameObject superImpactVfx;
        public GameObject superVfx;
        public GameObject secondarySuperVfx;
        [Tooltip("Charge gained for every point of damage dealt.")]
        public float superChargeFromDamageDealt = 0.55f;
        [Tooltip("Charge gained for every point of damage received.")]
        public float superChargeFromDamageTaken = 0.24f;
        public float superChargeOnKO = 12f;

        [Header("Effects (all optional)")]
        public GameObject castVfx;
        public GameObject secondaryCastVfx;
        public GameObject swingVfx;
        public GameObject impactVfx;
        public GameObject secondaryImpactVfx;
        public GameObject koVfx;
        public GameObject spawnVfx;
        public AudioClip attackSfx;
        public AudioClip hitSfx;

        public Health Health { get; private set; }
        public IBrawlerAnimationDriver AnimationDriver
        {
            get
            {
                RestoreAnimationDriverReference();
                return animationDriver;
            }
        }
        public IBrawlerMotor Motor
        {
            get
            {
                RestoreMotorReference();
                return motor;
            }
        }
        public IBrawlerWeaponPresentation WeaponPresentation
        {
            get
            {
                RestoreWeaponPresentationReference();
                return weaponPresentation;
            }
        }
        public int AnimationPresentationFailureCount { get; private set; }
        public System.Exception LastAnimationPresentationFailure { get; private set; }
        public string LastAnimationPresentationFailureOperation { get; private set; }
        public string LastAnimationPresentationFailureType { get; private set; }
        public string LastAnimationPresentationFailureMessage { get; private set; }
        public int WeaponPresentationFailureCount { get; private set; }
        public System.Exception LastWeaponPresentationFailure { get; private set; }
        public string LastWeaponPresentationFailureOperation { get; private set; }
        public string LastWeaponPresentationFailureType { get; private set; }
        public string LastWeaponPresentationFailureMessage { get; private set; }
        public bool IsDead => Health.IsDead;
        public bool IsPlayer { get; private set; }
        public bool MovementLocked => Time.time < attackLockUntil;
        public float Stamina { get; private set; }
        public float WardFlow => Stamina;
        public bool WardStepping { get; private set; }
        public bool CanWardStep =>
            CanAct && !WardStepping && !superInProgress && knockbackRoutine == null &&
            Stamina + 0.0001f >= wardStepCost;
        public float SpecialtyMoveMultiplier => spellSlowMultiplier;
        public bool IsBurning => burnTicksRemaining > 0 && burnExpiresAt > Time.time;
        public bool IsPoisoned => poisonTicksRemaining > 0 && poisonExpiresAt > Time.time;
        public bool IsSlowed => spellSlowUntil > Time.time && spellSlowMultiplier < 0.999f;
        public string ActiveStatusLabel
        {
            get
            {
                if (IsBurning && IsPoisoned) return IsSlowed ? "BURN / POISON / SLOW" : "BURN / POISON";
                if (IsBurning) return IsSlowed ? "BURN / SLOW" : "BURN";
                if (IsPoisoned) return IsSlowed ? "POISON / SLOW" : "POISON";
                return IsSlowed ? "SLOWED" : string.Empty;
            }
        }
        public float CurrentSpeed => moveSpeed * spellSlowMultiplier *
            (attackRoutine != null ? MobileCombatRules.CastMovementMultiplier : 1f);
        public float CooldownFraction =>
            Mathf.Clamp01((nextAttackTime - Time.time) / Mathf.Max(0.01f, attackCooldown));
        public string SuperName => string.IsNullOrEmpty(superName) ? "POWER BURST" : superName;
        public float SuperCharge { get; private set; }
        public float SuperCharge01 => Mathf.Clamp01(SuperCharge / Mathf.Max(1f, maxSuperCharge));
        public bool SuperReady => SuperCharge >= Mathf.Max(1f, maxSuperCharge);
        public int AttacksUsed { get; private set; }
        public int SupersUsed { get; private set; }
        public float SuperAimRange
        {
            get
            {
                switch (superStyle)
                {
                    case BrawlerSuperStyle.Dash:
                        return Mathf.Max(superRange + 0.8f, superDashDistance + superRange);
                    case BrawlerSuperStyle.ProjectileBlast:
                        return Mathf.Max(autoAimRange, superRange);
                    default:
                        return superRange + 0.8f;
                }
            }
        }
        public bool CanAct =>
            !IsDead && !respawning &&
            (MatchManager.Instance == null || MatchManager.Instance.State == MatchState.Playing);
        public Vector3 CombatAimPoint => transform.position + Vector3.up;
        public float CombatHitRadius
        {
            get
            {
                return Motor != null ? Motor.CollisionRadius : 0.65f;
            }
        }

        [SerializeField, HideInInspector] MonoBehaviour motorSource;
        [SerializeField, HideInInspector] bool motorLocked;
        IBrawlerMotor motor;
        [SerializeField, HideInInspector] MonoBehaviour animationDriverSource;
        [SerializeField, HideInInspector] bool animationDriverLocked;
        IBrawlerAnimationDriver animationDriver;
        [SerializeField, HideInInspector] MonoBehaviour weaponPresentationSource;
        [SerializeField, HideInInspector] bool weaponPresentationLocked;
        IBrawlerWeaponPresentation weaponPresentation;
        Transform spellOrigin;
        AudioSource audioSource;
        SkinnedMeshRenderer[] skins;
        Vector3 moveInput;
        float nextAttackTime;
        float attackLockUntil;
        float nextFlinchTime;
        float staminaRegenAt;
        float lastDamagedAt;
        float nextHealthRegenAt;
        bool respawning;
        bool initialized;
        bool superInProgress;
        Coroutine attackRoutine;
        Coroutine superRoutine;
        Coroutine knockbackRoutine;
        Coroutine invulnerabilityRoutine;

        enum AnimationPresentationOperation
        {
            TickLocomotion,
            PlayBasicAttack,
            PlaySuper,
            PlayHitReaction,
            PlayDeath,
            PlayRespawn,
            PlayVictory,
        }

        enum WeaponPresentationOperation
        {
            PresentAim,
            ResolveMuzzle,
            PresentMuzzle,
            SetVisible,
            ResetForRespawn,
        }

        Vector3 wardStepDirection;
        float wardStepStartedAt;
        float wardStepTravelDistance;
        float wardStepMovedDistance;
        bool wardOwnsExternalDisplacement;
        bool knockbackOwnsExternalDisplacement;

        float spellSlowMultiplier = 1f;
        float spellSlowUntil;
        BrawlerController burnSource;
        float burnDamagePerTick;
        float burnTickInterval;
        float burnNextTickAt;
        float burnExpiresAt;
        int burnTicksRemaining;
        BrawlerController poisonSource;
        float poisonDamagePerTick;
        float poisonTickInterval;
        float poisonNextTickAt;
        float poisonExpiresAt;
        int poisonTicksRemaining;

        void Awake()
        {
            Health = GetComponent<Health>();
            EnsureMotorSelected();
            skins = GetComponentsInChildren<SkinnedMeshRenderer>();
            maxStamina = MobileCombatRules.ArcaneFlowCapacity;
            Stamina = maxStamina;

            audioSource = gameObject.AddComponent<AudioSource>();
            audioSource.playOnAwake = false;
            audioSource.spatialBlend = 1f;
            audioSource.maxDistance = 30f;

            Health.Damaged += OnDamaged;
            Health.Died += OnDied;
        }

        void OnEnable()
        {
            RestoreMotorReference();
            RestoreAnimationDriverReference();
            RestoreWeaponPresentationReference();
        }

        /// <summary>
        /// Deferred init: runtime spawning (GameFlow) adds this component before
        /// configuring fields and sibling components, so identity, hashes and
        /// cosmetics must resolve in Start, not Awake.
        /// </summary>
        void Start()
        {
            IsPlayer = GetComponent<PlayerBrawlerInput>() != null;
            spellOrigin = FindSpellOrigin();
            maxStamina = MobileCombatRules.ArcaneFlowCapacity;
            Stamina = maxStamina;

            specialty = specialty.Sanitized();
            InitializeMotor();
            InitializeAnimationDriver();
            InitializeWeaponPresentation();
            if (MatchManager.Instance != null) MatchManager.Instance.Register(this);
            CreateTeamRing();
            HealthBarWorld.Create(this);
            initialized = true;
        }

        void OnDestroy()
        {
            if (Health != null)
            {
                Health.Damaged -= OnDamaged;
                Health.Died -= OnDied;
            }
        }

        public void SetMoveInput(Vector3 worldDir)
        {
            moveInput = Vector3.ClampMagnitude(new Vector3(worldDir.x, 0f, worldDir.z), 1f);
        }

        /// <summary>
        /// Installs the sole physical motor before Start locks actor ownership.
        /// Production assembly supplies the one same-root Invector motor before
        /// Start; no runtime fallback is created.
        /// </summary>
        public void SetMotor(IBrawlerMotor selectedMotor)
        {
            if (selectedMotor == null)
                throw new System.ArgumentNullException(nameof(selectedMotor));
            if (motorLocked)
                throw new System.InvalidOperationException(
                    "The brawler motor must be selected before Start.");
            if (motor != null && !object.ReferenceEquals(motor, selectedMotor))
                throw new System.InvalidOperationException(
                    "A brawler can have only one physical motor.");
            if (!(selectedMotor is MonoBehaviour source))
                throw new System.ArgumentException(
                    "A production brawler motor must be a MonoBehaviour on the brawler root.",
                    nameof(selectedMotor));
            if (source.gameObject != gameObject)
                throw new System.ArgumentException(
                    "A component-backed brawler motor must live on the brawler root.",
                    nameof(selectedMotor));
            motor = selectedMotor;
            motorSource = source;
        }

        void InitializeMotor()
        {
            EnsureMotorSelected();
            motor.Initialize(moveSpeed);
            motorLocked = true;
        }

        void EnsureMotorSelected()
        {
            RestoreMotorReference();
            MonoBehaviour[] components = GetComponents<MonoBehaviour>();
            for (int i = 0; i < components.Length; i++)
            {
                if (!(components[i] is IBrawlerMotor candidate)) continue;
                if (motor != null && !object.ReferenceEquals(motor, candidate))
                    throw new System.InvalidOperationException(
                        "A brawler can have only one physical motor component.");
                motor = candidate;
                motorSource = components[i];
            }
            if (motor == null)
                throw new System.InvalidOperationException(
                    "An Invector-backed brawler requires one configured IBrawlerMotor before Start.");
        }

        void RestoreMotorReference()
        {
            if (motor == null && motorSource is IBrawlerMotor restored)
                motor = restored;
        }

        /// <summary>
        /// Installs the sole Animator writer before Start configures the actor.
        /// Invector assembly uses this hook; no fallback Animator writer exists.
        /// </summary>
        public void SetAnimationDriver(IBrawlerAnimationDriver driver)
        {
            if (driver == null) throw new System.ArgumentNullException(nameof(driver));
            if (animationDriverLocked)
                throw new System.InvalidOperationException(
                    "The brawler animation driver must be selected before Start.");
            if (animationDriver != null && !object.ReferenceEquals(animationDriver, driver))
                throw new System.InvalidOperationException(
                    "A brawler can have only one animation driver.");
            if (!(driver is MonoBehaviour source))
                throw new System.ArgumentException(
                    "A production animation driver must be a MonoBehaviour on the brawler root.",
                    nameof(driver));
            if (source.gameObject != gameObject)
                throw new System.ArgumentException(
                    "A component-backed animation driver must live on the brawler root.",
                    nameof(driver));
            animationDriver = driver;
            animationDriverSource = source;
        }

        void InitializeAnimationDriver()
        {
            RestoreAnimationDriverReference();
            MonoBehaviour[] components = GetComponents<MonoBehaviour>();
            for (int i = 0; i < components.Length; i++)
            {
                if (!(components[i] is IBrawlerAnimationDriver candidate)) continue;
                if (animationDriver != null && !object.ReferenceEquals(animationDriver, candidate))
                    throw new System.InvalidOperationException(
                        "A brawler can have only one animation driver component.");
                animationDriver = candidate;
                animationDriverSource = components[i];
            }
            if (animationDriver == null)
                throw new System.InvalidOperationException(
                    "An Invector-backed brawler requires one configured animation driver before Start.");
            animationDriverLocked = true;
        }

        void RestoreAnimationDriverReference()
        {
            if (animationDriver == null && animationDriverSource is IBrawlerAnimationDriver restored)
                animationDriver = restored;
        }

        /// <summary>
        /// Installs the one visual weapon owner before Start.
        /// </summary>
        public void SetWeaponPresentation(IBrawlerWeaponPresentation presentation)
        {
            if (presentation == null)
                throw new System.ArgumentNullException(nameof(presentation));
            if (weaponPresentationLocked)
                throw new System.InvalidOperationException(
                    "The brawler weapon presentation must be selected before Start.");
            if (weaponPresentation != null &&
                !object.ReferenceEquals(weaponPresentation, presentation))
                throw new System.InvalidOperationException(
                    "A brawler can have only one weapon presentation owner.");
            if (!(presentation is MonoBehaviour source))
                throw new System.ArgumentException(
                    "A production weapon presentation must be a MonoBehaviour on the brawler root.",
                    nameof(presentation));
            if (source.gameObject != gameObject)
                throw new System.ArgumentException(
                    "A component-backed weapon presentation must live on the brawler root.",
                    nameof(presentation));

            weaponPresentation = presentation;
            weaponPresentationSource = source;
        }

        void InitializeWeaponPresentation()
        {
            RestoreWeaponPresentationReference();
            if (weaponPresentation is MonoBehaviour restoredSource &&
                restoredSource.gameObject != gameObject)
                throw new System.InvalidOperationException(
                    "The selected weapon presentation must live on the brawler root.");

            MonoBehaviour[] components = GetComponents<MonoBehaviour>();
            for (int i = 0; i < components.Length; i++)
            {
                if (!(components[i] is IBrawlerWeaponPresentation candidate)) continue;
                if (weaponPresentation != null &&
                    !object.ReferenceEquals(weaponPresentation, candidate))
                    throw new System.InvalidOperationException(
                        "A brawler can have only one weapon presentation component.");
                weaponPresentation = candidate;
                weaponPresentationSource = components[i];
            }
            weaponPresentationLocked = true;
        }

        void RestoreWeaponPresentationReference()
        {
            if (weaponPresentation == null &&
                weaponPresentationSource is IBrawlerWeaponPresentation restored)
                weaponPresentation = restored;
        }

        /// <summary>
        /// Animation is presentation-only at the facade boundary. A backend fault is
        /// retained for diagnostics, but it must never escape into combat, movement,
        /// damage, or match-lifecycle authority.
        /// </summary>
        void TryPresent(AnimationPresentationOperation operation, float normalizedSpeed = 0f)
        {
            IBrawlerAnimationDriver driver = AnimationDriver;
            if (driver == null) return;

            try
            {
                switch (operation)
                {
                    case AnimationPresentationOperation.TickLocomotion:
                        driver.TickLocomotion(normalizedSpeed);
                        break;
                    case AnimationPresentationOperation.PlayBasicAttack:
                        driver.PlayBasicAttack();
                        break;
                    case AnimationPresentationOperation.PlaySuper:
                        driver.PlaySuper();
                        break;
                    case AnimationPresentationOperation.PlayHitReaction:
                        driver.PlayHitReaction();
                        break;
                    case AnimationPresentationOperation.PlayDeath:
                        driver.PlayDeath();
                        break;
                    case AnimationPresentationOperation.PlayRespawn:
                        driver.PlayRespawn();
                        break;
                    case AnimationPresentationOperation.PlayVictory:
                        driver.PlayVictory();
                        break;
                }
            }
            catch (System.Exception exception)
            {
                AnimationPresentationFailureCount++;
                LastAnimationPresentationFailure = exception;
                LastAnimationPresentationFailureOperation = operation.ToString();
                LastAnimationPresentationFailureType = exception.GetType().FullName;
                LastAnimationPresentationFailureMessage = exception.Message;
            }
        }

        /// <summary>
        /// Weapon art and IK are best-effort presentation. Failures are retained
        /// for inspection and never escape into targeting, projectile timing,
        /// damage, visibility, or respawn authority.
        /// </summary>
        bool TryWeaponPresent(WeaponPresentationOperation operation, Vector3 position,
            Vector3 direction, bool visible, out Vector3 muzzlePosition)
        {
            muzzlePosition = default;
            IBrawlerWeaponPresentation presentation = WeaponPresentation;
            if (presentation == null) return false;

            try
            {
                switch (operation)
                {
                    case WeaponPresentationOperation.PresentAim:
                        presentation.PresentAim(direction);
                        break;
                    case WeaponPresentationOperation.ResolveMuzzle:
                        if (!presentation.TryGetMuzzlePosition(out muzzlePosition))
                            return false;
                        if (float.IsNaN(muzzlePosition.x) ||
                            float.IsNaN(muzzlePosition.y) ||
                            float.IsNaN(muzzlePosition.z) ||
                            float.IsInfinity(muzzlePosition.x) ||
                            float.IsInfinity(muzzlePosition.y) ||
                            float.IsInfinity(muzzlePosition.z))
                            throw new System.InvalidOperationException(
                                "Weapon presentation returned an invalid muzzle position.");
                        return true;
                    case WeaponPresentationOperation.PresentMuzzle:
                        presentation.PresentMuzzle(position, direction);
                        break;
                    case WeaponPresentationOperation.SetVisible:
                        presentation.SetVisible(visible);
                        break;
                    case WeaponPresentationOperation.ResetForRespawn:
                        presentation.ResetForRespawn();
                        break;
                }
                return true;
            }
            catch (System.Exception exception)
            {
                WeaponPresentationFailureCount++;
                LastWeaponPresentationFailure = exception;
                LastWeaponPresentationFailureOperation = operation.ToString();
                LastWeaponPresentationFailureType = exception.GetType().FullName;
                LastWeaponPresentationFailureMessage = exception.Message;
                return false;
            }
        }

        void PresentWeaponAim(Vector3 worldDirection)
        {
            TryWeaponPresent(WeaponPresentationOperation.PresentAim, default,
                worldDirection, false, out _);
        }

        void PresentWeaponMuzzle(Vector3 worldPosition, Vector3 worldDirection)
        {
            TryWeaponPresent(WeaponPresentationOperation.PresentMuzzle, worldPosition,
                worldDirection, false, out _);
        }

        void PresentWeaponVisibility(bool visible)
        {
            TryWeaponPresent(WeaponPresentationOperation.SetVisible, default,
                default, visible, out _);
        }

        void ResetWeaponForRespawn()
        {
            TryWeaponPresent(WeaponPresentationOperation.ResetForRespawn, default,
                default, true, out _);
        }

        /// <summary>
        /// Commits a short, collision-aware step in one direction. Validation
        /// happens before Flow is spent, so tapping into an immediate wall is
        /// friendly rather than punitive.
        /// </summary>
        public bool TryWardStep(Vector3 worldDirection)
        {
            if (!CanWardStep) return false;

            worldDirection.y = 0f;
            if (worldDirection.sqrMagnitude <= 0.0001f) worldDirection = moveInput;
            if (worldDirection.sqrMagnitude <= 0.0001f) worldDirection = transform.forward;
            worldDirection.y = 0f;
            if (worldDirection.sqrMagnitude <= 0.0001f) return false;
            worldDirection.Normalize();

            float travelDistance = ResolveWardStepDistance(worldDirection);
            if (travelDistance <= 0.15f) return false;

            float flow = Stamina;
            if (!MobileCombatRules.TrySpendWardFlow(ref flow, wardStepCost)) return false;
            Stamina = flow;
            staminaRegenAt = Time.time + staminaRegenDelay;

            wardStepDirection = worldDirection;
            wardStepTravelDistance = travelDistance;
            wardStepMovedDistance = 0f;
            wardStepStartedAt = Time.time;
            WardStepping = true;
            FaceInstant(transform.position + worldDirection);
            Motor.BeginExternalDisplacement();
            wardOwnsExternalDisplacement = true;
            return true;
        }

        float ResolveWardStepDistance(Vector3 direction)
        {
            float distance = Mathf.Max(0f, wardStepDistance);
            if (distance <= 0f) return 0f;

            Vector3 sweepOrigin = CombatAimPoint;
            if (CombatPhysics.SweepWorld(sweepOrigin, CombatHitRadius, direction, distance,
                    false, out RaycastHit worldHit))
                distance = Mathf.Min(distance, Mathf.Max(0f, worldHit.distance - 0.06f));

            MatchManager manager = MatchManager.Instance;
            if (manager != null)
            {
                List<BrawlerController> brawlers = manager.GetBrawlers();
                for (int i = 0; i < brawlers.Count; i++)
                {
                    BrawlerController other = brawlers[i];
                    if (other == null || other == this || other.IsDead) continue;
                    if (CombatPhysics.TryIntersectSegmentSphere(sweepOrigin, direction, distance,
                            other.CombatAimPoint, CombatHitRadius + other.CombatHitRadius,
                            out float contactDistance))
                        distance = Mathf.Min(distance, Mathf.Max(0f, contactDistance - 0.04f));
                }
            }

            return Motor.ConstrainExternalDisplacement(direction, distance);
        }

        void UpdateWardStep()
        {
            float duration = Mathf.Max(0.01f, wardStepDuration);
            float t = Mathf.Clamp01((Time.time - wardStepStartedAt) / duration);
            float nextDistance = Mathf.SmoothStep(0f, wardStepTravelDistance, t);
            Vector3 delta = wardStepDirection * (nextDistance - wardStepMovedDistance);
            wardStepMovedDistance = nextDistance;

            Motor.Displace(delta, true);

            if (t >= 1f) CancelWardStep();
        }

        void CancelWardStep()
        {
            if (wardOwnsExternalDisplacement) Motor.EndExternalDisplacement();
            WardStepping = false;
            wardStepDirection = Vector3.zero;
            wardStepStartedAt = 0f;
            wardStepTravelDistance = 0f;
            wardStepMovedDistance = 0f;
            wardOwnsExternalDisplacement = false;
        }

        void Update()
        {
            if (!initialized) return;
            UpdateSpellStatuses();
            UpdateWardFlow();
            UpdateHealthRegen();

            if (WardStepping)
            {
                UpdateWardStep();
            }
            else
            {
                // A committed cast slows movement without rooting the player.
                // Knockback suppresses only ordinary planar input.
                Motor.SetPlanarIntent(moveInput, CurrentSpeed,
                    CanAct && knockbackRoutine == null);
            }

            UpdateAnimator();
        }

        void UpdateWardFlow()
        {
            if (!CanAct) return;
            if (Time.time < staminaRegenAt || Stamina >= maxStamina) return;
            Stamina = MobileCombatRules.RegenerateWardFlow(Stamina, maxStamina,
                staminaRegenPerSec, Time.deltaTime);
        }

        /// <summary>
        /// Out-of-combat recovery: after a few seconds without taking damage,
        /// health returns in chunky ticks (only taking damage resets the
        /// timer — dealing damage doesn't pause your own regen).
        /// </summary>
        void UpdateHealthRegen()
        {
            if (!CanAct || Health.Current >= Health.Max) return;
            if (Time.time < lastDamagedAt + healthRegenDelay) return;
            if (Time.time < nextHealthRegenAt) return;
            nextHealthRegenAt = Time.time + healthRegenTickInterval;
            Health.Heal(Mathf.Round(Health.Max * healthRegenTickFraction));
        }

        void UpdateSpellStatuses()
        {
            if (spellSlowUntil > 0f && Time.time >= spellSlowUntil)
            {
                spellSlowMultiplier = 1f;
                spellSlowUntil = 0f;
            }

            UpdateBurn();
            UpdatePoison();
        }

        void UpdateBurn()
        {
            if (burnTicksRemaining <= 0 || burnExpiresAt <= 0f) return;
            if (IsDead || Time.time >= burnExpiresAt || !SpecialtyDamageAllowed() ||
                burnSource == null || burnSource == this || burnSource.team == team)
            {
                ClearBurn();
                return;
            }
            if (Time.time < burnNextTickAt) return;

            burnNextTickAt = Time.time + burnTickInterval;
            Health.TakeDamage(burnDamagePerTick, burnSource.gameObject);
            burnTicksRemaining--;
            if (IsDead || burnTicksRemaining <= 0) ClearBurn();
        }

        void UpdatePoison()
        {
            if (poisonTicksRemaining <= 0 || poisonExpiresAt <= 0f) return;
            if (IsDead || Time.time >= poisonExpiresAt || !SpecialtyDamageAllowed() ||
                poisonSource == null || poisonSource == this || poisonSource.team == team)
            {
                ClearPoison();
                return;
            }
            if (Time.time < poisonNextTickAt) return;

            poisonNextTickAt = Time.time + poisonTickInterval;
            Health.TakeDamage(poisonDamagePerTick, poisonSource.gameObject);
            poisonTicksRemaining--;
            if (IsDead || poisonTicksRemaining <= 0) ClearPoison();
        }

        bool SpecialtyDamageAllowed()
        {
            return MatchManager.Instance == null || MatchManager.Instance.State != MatchState.Ended;
        }

        Transform FindSpellOrigin()
        {
            Transform[] children = GetComponentsInChildren<Transform>(true);
            for (int i = 0; i < children.Length; i++)
                if (children[i] != null && children[i].name == "SpellOrigin")
                    return children[i];
            return null;
        }

        Vector3 SpellMuzzlePosition
        {
            get
            {
                Vector3 fallback = spellOrigin != null
                    ? spellOrigin.position
                    : transform.position + transform.forward * 0.6f + Vector3.up * 1.25f;
                return TryWeaponPresent(WeaponPresentationOperation.ResolveMuzzle, default,
                    default, true, out Vector3 presentedMuzzle)
                    ? presentedMuzzle
                    : fallback;
            }
        }

        public Vector3 AttackPreviewOrigin => SpellMuzzlePosition;

        /// <summary>
        /// Returns the same authoritative basic-cast range used by the
        /// projectile, clipped at the first wall or valid enemy contact.
        /// </summary>
        public float GetAttackPreviewDistance(Vector3 worldDirection)
        {
            worldDirection.y = 0f;
            if (worldDirection.sqrMagnitude <= 0.0001f) return 0f;
            worldDirection.Normalize();

            float distance = Mathf.Max(0f, attackRange);
            float hitRadius = Projectile.DefaultHitRadius;
            if (projectilePrefab != null)
            {
                Projectile projectile = projectilePrefab.GetComponent<Projectile>();
                if (projectile != null) hitRadius = Mathf.Max(hitRadius, projectile.hitRadius);
            }

            Vector3 origin = SpellMuzzlePosition;
            if (CombatPhysics.SweepWorld(origin, hitRadius, worldDirection, distance,
                    true, out RaycastHit worldHit))
                distance = Mathf.Min(distance, worldHit.distance);

            MatchManager manager = MatchManager.Instance;
            if (manager == null) return distance;
            List<BrawlerController> brawlers = manager.GetBrawlers();
            for (int i = 0; i < brawlers.Count; i++)
            {
                BrawlerController other = brawlers[i];
                if (other == null || other == this || other.IsDead || other.team == team) continue;
                if (CombatPhysics.TryIntersectSegmentSphere(origin, worldDirection, distance,
                        other.CombatAimPoint, hitRadius + other.CombatHitRadius,
                        out float contactDistance))
                    distance = Mathf.Min(distance, contactDistance);
            }
            return distance;
        }

        // ---------------- animation ----------------

        float ComputeSpeed01()
        {
            Vector3 v = Motor != null ? Motor.Velocity : Vector3.zero;
            v.y = 0f;
            return Mathf.Clamp01(v.magnitude / Mathf.Max(0.1f, moveSpeed));
        }

        /// <summary>
        /// Routes measured locomotion speed to the selected animation backend.
        /// The Invector driver translates normalized facade motion semantically.
        /// </summary>
        void UpdateAnimator()
        {
            if (IsDead) return;
            if (MatchManager.Instance != null && MatchManager.Instance.State == MatchState.Ended) return;
            TryPresent(AnimationPresentationOperation.TickLocomotion, ComputeSpeed01());
        }

        // ---------------- combat ----------------

        public BrawlerController FindNearestEnemy(float maxRange)
        {
            if (MatchManager.Instance == null) return null;
            BrawlerController best = null;
            float bestDistSq = maxRange * maxRange;
            var brawlers = MatchManager.Instance.GetBrawlers();
            for (int i = 0; i < brawlers.Count; i++)
            {
                var b = brawlers[i];
                if (b == null || b == this || b.team == team || b.IsDead) continue;
                float distanceSq = (transform.position - b.transform.position).sqrMagnitude;
                if (distanceSq < bestDistSq &&
                    CombatPhysics.HasLineOfSight(CombatAimPoint, b.CombatAimPoint))
                {
                    bestDistSq = distanceSq;
                    best = b;
                }
            }
            return best;
        }

        BrawlerController FindNearestReachableBasicTarget()
        {
            MatchManager manager = MatchManager.Instance;
            if (manager == null) return null;

            Vector3 origin = projectilePrefab != null
                ? SpellMuzzlePosition
                : transform.position + Vector3.up;
            float contactRadius = attackRadius;
            if (projectilePrefab != null)
            {
                Projectile projectile = projectilePrefab.GetComponent<Projectile>();
                contactRadius = projectile != null
                    ? Mathf.Max(0f, projectile.hitRadius)
                    : Projectile.DefaultHitRadius;
            }

            BrawlerController best = null;
            float bestDistanceSq = autoAimRange * autoAimRange;
            List<BrawlerController> brawlers = manager.GetBrawlers();
            for (int i = 0; i < brawlers.Count; i++)
            {
                BrawlerController candidate = brawlers[i];
                if (candidate == null || candidate == this || candidate.team == team ||
                    candidate.IsDead)
                    continue;

                float distanceSq = (transform.position - candidate.transform.position).sqrMagnitude;
                if (distanceSq >= bestDistanceSq) continue;
                float reachableCenterDistance = attackRange + contactRadius +
                                                candidate.CombatHitRadius;
                if ((candidate.CombatAimPoint - origin).sqrMagnitude >
                    reachableCenterDistance * reachableCenterDistance)
                    continue;
                if (!CombatPhysics.HasLineOfSight(CombatAimPoint, candidate.CombatAimPoint))
                    continue;

                bestDistanceSq = distanceSq;
                best = candidate;
            }
            return best;
        }

        /// <summary>
        /// Applies the bounded school effect after authoritative damage has been
        /// accepted by Health. Projectile and melee paths share this method so
        /// attribution, team filtering, and match-end behavior stay identical.
        /// </summary>
        public void ApplySpellSpecialtyHit(BrawlerController target, float appliedDamage,
            Vector3 impactPoint, GameObject chainedImpactVfx = null, bool allowChain = true)
        {
            if (!IsValidSpecialtyTarget(target) || appliedDamage <= 0f) return;
            SpellSpecialty payload = specialty.Sanitized();
            switch (payload.school)
            {
                case SpellSchool.Arcane:
                    if (payload.sustainFraction > 0f && !IsDead && SpecialtyDamageAllowed())
                        Health.Heal(appliedDamage * payload.sustainFraction);
                    SmartHealLowestWoundedAlly(appliedDamage * payload.allyHealFraction,
                        payload.allyHealRadius);
                    break;
                case SpellSchool.Fire:
                    target.ApplySpellBurn(this, appliedDamage * payload.burnDamageFraction,
                        payload.burnDuration, payload.burnTickInterval);
                    break;
                case SpellSchool.Poison:
                    target.ApplySpellPoison(this,
                        appliedDamage * payload.poisonDamageFraction,
                        payload.poisonDuration, payload.poisonTickInterval);
                    break;
                case SpellSchool.Frost:
                    target.ApplySpellSlow(this, payload.slowMultiplier, payload.slowDuration);
                    break;
                case SpellSchool.Storm:
                    if (allowChain) ChainSpellFrom(target, appliedDamage, payload, chainedImpactVfx);
                    break;
                case SpellSchool.Void:
                    if (payload.voidPullDistance > 0f)
                        target.ApplyKnockback(transform.position - target.transform.position,
                            payload.voidPullDistance);
                    break;
            }
        }

        public void ApplySpellSlow(BrawlerController source, float multiplier, float duration)
        {
            if (!IsValidIncomingSpell(source) || duration <= 0f) return;
            multiplier = Mathf.Clamp(multiplier, 0.25f, 1f);
            duration = Mathf.Clamp(duration, 0f, 4f);
            spellSlowMultiplier = Mathf.Min(spellSlowMultiplier, multiplier);
            spellSlowUntil = Mathf.Max(spellSlowUntil, Time.time + duration);
        }

        public void ApplySpellBurn(BrawlerController source, float totalDamage, float duration,
            float tickInterval)
        {
            if (!IsValidIncomingSpell(source) || totalDamage <= 0f || duration <= 0f) return;
            duration = Mathf.Clamp(duration, 0.2f, 6f);
            tickInterval = Mathf.Clamp(tickInterval, 0.2f, 2f);
            int ticks = Mathf.Clamp(Mathf.CeilToInt(duration / tickInterval), 1, 12);
            float tickDamage = Mathf.Min(totalDamage / ticks, Health.Max * 0.12f);
            bool active = burnExpiresAt > Time.time && burnSource != null &&
                          burnTicksRemaining > 0;
            if (active && burnSource != source && tickDamage < burnDamagePerTick) return;
            float scheduledTick = active ? burnNextTickAt : 0f;

            burnSource = source;
            burnDamagePerTick = Mathf.Max(active ? burnDamagePerTick : 0f, tickDamage);
            burnTickInterval = Mathf.Max(tickInterval, duration / ticks);
            burnTicksRemaining = ticks;
            burnExpiresAt = Time.time + duration + 0.05f;
            burnNextTickAt = active && scheduledTick > 0f
                ? Mathf.Min(scheduledTick, Time.time + burnTickInterval)
                : Time.time + burnTickInterval;
        }

        public void ApplySpellPoison(BrawlerController source, float totalDamage, float duration,
            float tickInterval)
        {
            if (!IsValidIncomingSpell(source) || totalDamage <= 0f || duration <= 0f) return;
            duration = Mathf.Clamp(duration, 0.2f, 8f);
            tickInterval = Mathf.Clamp(tickInterval, 0.2f, 2f);
            int ticks = Mathf.Clamp(Mathf.CeilToInt(duration / tickInterval), 1, 20);
            float tickDamage = Mathf.Min(totalDamage / ticks, Health.Max * 0.1f);
            bool active = poisonExpiresAt > Time.time && poisonSource != null &&
                          poisonTicksRemaining > 0;
            if (active && poisonSource != source && tickDamage < poisonDamagePerTick) return;
            float scheduledTick = active ? poisonNextTickAt : 0f;

            poisonSource = source;
            poisonDamagePerTick = Mathf.Max(active ? poisonDamagePerTick : 0f, tickDamage);
            poisonTickInterval = Mathf.Max(tickInterval, duration / ticks);
            poisonTicksRemaining = ticks;
            poisonExpiresAt = Time.time + duration + 0.05f;
            poisonNextTickAt = active && scheduledTick > 0f
                ? Mathf.Min(scheduledTick, Time.time + poisonTickInterval)
                : Time.time + poisonTickInterval;
        }

        float SmartHealLowestWoundedAlly(float amount, float radius)
        {
            if (amount <= 0f || radius <= 0f || !SpecialtyDamageAllowed()) return 0f;
            MatchManager manager = MatchManager.Instance;
            if (manager == null) return 0f;

            BrawlerController best = null;
            float bestHealthFraction = float.MaxValue;
            float bestDistanceSq = float.MaxValue;
            float radiusSq = radius * radius;
            List<BrawlerController> brawlers = manager.GetBrawlers();
            for (int i = 0; i < brawlers.Count; i++)
            {
                BrawlerController candidate = brawlers[i];
                if (candidate == null || candidate == this || candidate.team != team ||
                    candidate.IsDead || candidate.Health == null ||
                    candidate.Health.Current >= candidate.Health.Max - 0.001f)
                    continue;

                float distanceSq = (candidate.CombatAimPoint - CombatAimPoint).sqrMagnitude;
                if (distanceSq > radiusSq ||
                    !CombatPhysics.HasLineOfSight(CombatAimPoint, candidate.CombatAimPoint))
                    continue;
                float healthFraction = candidate.Health.Current /
                                       Mathf.Max(1f, candidate.Health.Max);
                if (healthFraction > bestHealthFraction + 0.0001f ||
                    (Mathf.Abs(healthFraction - bestHealthFraction) <= 0.0001f &&
                     distanceSq >= bestDistanceSq))
                    continue;

                best = candidate;
                bestHealthFraction = healthFraction;
                bestDistanceSq = distanceSq;
            }
            if (best == null) return 0f;

            float before = best.Health.Current;
            best.Health.Heal(amount);
            float restored = best.Health.Current - before;
            if (restored > 0f && secondaryImpactVfx != null)
                SpawnVfx(secondaryImpactVfx, best.CombatAimPoint, Quaternion.identity, 2f);
            return restored;
        }

        /// <summary>
        /// Arcane Supers heal every living ally in the ritual area, including
        /// the caster. Projectile blasts call this at impact; burst Supers call
        /// it around the caster, so the support effect never depends on an
        /// enemy being caught in the damage volume.
        /// </summary>
        public float ApplyArcaneRitualHeal(Vector3 center, float radiusOverride = 0f)
        {
            SpellSpecialty payload = specialty.Sanitized();
            if (payload.school != SpellSchool.Arcane || payload.ritualHealFraction <= 0f ||
                !SpecialtyDamageAllowed())
                return 0f;
            MatchManager manager = MatchManager.Instance;
            if (manager == null) return 0f;

            float radius = radiusOverride > 0f ? radiusOverride : payload.allyHealRadius;
            radius = Mathf.Clamp(radius, 0f, 14f);
            if (radius <= 0f) return 0f;
            float radiusSq = radius * radius;
            float totalRestored = 0f;
            List<BrawlerController> brawlers = manager.GetBrawlers();
            for (int i = 0; i < brawlers.Count; i++)
            {
                BrawlerController ally = brawlers[i];
                if (ally == null || ally.team != team || ally.IsDead || ally.Health == null)
                    continue;
                if ((ally.CombatAimPoint - center).sqrMagnitude > radiusSq ||
                    !CombatPhysics.HasLineOfSight(center, ally.CombatAimPoint))
                    continue;

                float before = ally.Health.Current;
                ally.Health.Heal(ally.Health.Max * payload.ritualHealFraction);
                float restored = ally.Health.Current - before;
                totalRestored += restored;
                if (restored > 0f && secondaryImpactVfx != null)
                    SpawnVfx(secondaryImpactVfx, ally.CombatAimPoint,
                        Quaternion.identity, 2.2f);
            }
            return totalRestored;
        }

        bool IsValidSpecialtyTarget(BrawlerController target)
        {
            return target != null && target != this && !target.IsDead && target.team != team &&
                   SpecialtyDamageAllowed();
        }

        bool IsValidIncomingSpell(BrawlerController source)
        {
            return source != null && source != this && !IsDead && source.team != team &&
                   SpecialtyDamageAllowed();
        }

        void ChainSpellFrom(BrawlerController first, float firstAppliedDamage,
            SpellSpecialty payload, GameObject chainedImpactVfx)
        {
            MatchManager manager = MatchManager.Instance;
            if (manager == null || payload.chainTargets <= 0 || payload.chainRange <= 0f ||
                payload.chainDamageMultiplier <= 0f)
                return;

            var visited = new List<BrawlerController>(payload.chainTargets + 1) { first };
            BrawlerController current = first;
            float nextDamage = firstAppliedDamage * payload.chainDamageMultiplier;
            for (int hop = 0; hop < payload.chainTargets && nextDamage > 0.01f; hop++)
            {
                if (!SpecialtyDamageAllowed()) break;
                BrawlerController next = null;
                float bestDistanceSq = payload.chainRange * payload.chainRange;
                List<BrawlerController> brawlers = manager.GetBrawlers();
                for (int i = 0; i < brawlers.Count; i++)
                {
                    BrawlerController candidate = brawlers[i];
                    if (!IsValidSpecialtyTarget(candidate) || visited.Contains(candidate)) continue;
                    float distanceSq = (candidate.CombatAimPoint - current.CombatAimPoint).sqrMagnitude;
                    if (distanceSq >= bestDistanceSq ||
                        !CombatPhysics.HasLineOfSight(current.CombatAimPoint, candidate.CombatAimPoint))
                        continue;
                    bestDistanceSq = distanceSq;
                    next = candidate;
                }
                if (next == null) break;

                visited.Add(next);
                float applied = next.Health.TakeDamage(nextDamage, gameObject);
                if (applied <= 0f) break;
                if (chainedImpactVfx != null)
                    SpawnVfx(chainedImpactVfx, next.transform.position + Vector3.up * 1.1f,
                        Quaternion.identity, 2.25f);
                current = next;
                // Chain decay follows the damage that actually entered the prior
                // target. Invulnerability already terminates above; overkill must
                // likewise not amplify the next hop from damage that was discarded.
                nextDamage = applied * payload.chainDamageMultiplier;
            }
        }

        void ClearBurn()
        {
            burnSource = null;
            burnDamagePerTick = 0f;
            burnTickInterval = 0f;
            burnNextTickAt = 0f;
            burnExpiresAt = 0f;
            burnTicksRemaining = 0;
        }

        void ClearPoison()
        {
            poisonSource = null;
            poisonDamagePerTick = 0f;
            poisonTickInterval = 0f;
            poisonNextTickAt = 0f;
            poisonExpiresAt = 0f;
            poisonTicksRemaining = 0;
        }

        void ClearSpellStatuses()
        {
            spellSlowMultiplier = 1f;
            spellSlowUntil = 0f;
            ClearBurn();
            ClearPoison();
        }

        public bool TryAttackAuto()
        {
            // Early-out before the enemy scan. Mobile input invokes this once
            // per completed tap rather than repeatedly while held.
            if (!CanAct || superInProgress || Time.time < nextAttackTime) return false;
            return TryAttack(FindNearestReachableBasicTarget());
        }

        public bool TryAttack(BrawlerController target)
        {
            if (target != null &&
                (target == this || target.team == team || target.IsDead ||
                 !CombatPhysics.HasLineOfSight(CombatAimPoint, target.CombatAimPoint)))
                return false;

            Vector3 direction = target != null
                ? target.transform.position - transform.position
                : transform.forward;
            return BeginAttack(target, direction);
        }

        /// <summary>
        /// Starts an attack in an authored world-space direction without
        /// requiring an auto-aim target. The planar direction is captured for
        /// the windup, so movement cannot silently rotate a manual shot.
        /// </summary>
        public bool TryAttackDirection(Vector3 worldDirection)
        {
            worldDirection.y = 0f;
            if (worldDirection.sqrMagnitude <= 0.0001f) return false;
            return BeginAttack(null, worldDirection.normalized);
        }

        bool BeginAttack(BrawlerController target, Vector3 worldDirection)
        {
            if (!CanAct || superInProgress || Time.time < nextAttackTime) return false;
            CancelSpawnProtectionOnOffense();
            worldDirection.y = 0f;
            if (worldDirection.sqrMagnitude <= 0.0001f) worldDirection = transform.forward;
            worldDirection.Normalize();

            nextAttackTime = Time.time + attackCooldown;
            attackLockUntil = Time.time + attackMoveLock;
            AttacksUsed++;
            FaceInstant(transform.position + worldDirection);
            PresentWeaponAim(worldDirection);
            attackRoutine = StartCoroutine(AttackRoutine(target, worldDirection));
            BalanceTelemetryRuntime.RecordAttack(this);
            return true;
        }

        public bool TrySuperAuto()
        {
            if (!CanAct || !SuperReady || superInProgress || WardStepping) return false;
            return TrySuper(FindNearestEnemy(SuperAimRange));
        }

        public bool TrySuper(BrawlerController target)
        {
            if (!CanAct || !SuperReady || superInProgress || WardStepping ||
                target == null || target.IsDead)
                return false;
            if (Vector3.Distance(transform.position, target.transform.position) > SuperAimRange)
                return false;
            if (!CombatPhysics.HasLineOfSight(CombatAimPoint, target.CombatAimPoint))
                return false;

            return BeginSuper(target, target.transform.position - transform.position);
        }

        /// <summary>
        /// Activates the charged Super in a world-space direction. Burst Supers
        /// use the direction for presentation; dashes and blast projectiles use
        /// it as their actual trajectory and do not require an auto target.
        /// </summary>
        public bool TrySuperDirection(Vector3 worldDirection)
        {
            worldDirection.y = 0f;
            if (worldDirection.sqrMagnitude <= 0.0001f) return false;
            return BeginSuper(null, worldDirection.normalized);
        }

        bool BeginSuper(BrawlerController target, Vector3 worldDirection)
        {
            if (!CanAct || !SuperReady || superInProgress || WardStepping ||
                attackRoutine != null)
                return false;
            CancelSpawnProtectionOnOffense();
            worldDirection.y = 0f;
            if (worldDirection.sqrMagnitude <= 0.0001f) worldDirection = transform.forward;
            worldDirection.Normalize();

            SuperCharge = 0f;
            SupersUsed++;
            superInProgress = true;
            nextAttackTime = Mathf.Max(nextAttackTime, Time.time + 0.3f);
            attackLockUntil = Mathf.Max(attackLockUntil, Time.time + 0.28f);
            FaceInstant(transform.position + worldDirection);
            PresentWeaponAim(worldDirection);
            superRoutine = StartCoroutine(SuperRoutine(target, worldDirection));
            BalanceTelemetryRuntime.RecordSuper(this);
            if (IsPlayer) CombatFeedback.ReportLocalSuper();
            return true;
        }

        void FaceInstant(Vector3 worldPoint)
        {
            Vector3 dir = worldPoint - transform.position;
            dir.y = 0f;
            Motor?.Face(dir, true);
        }

        IEnumerator AttackRoutine(BrawlerController target, Vector3 worldDirection)
        {
            try
            {
                TryPresent(AnimationPresentationOperation.PlayBasicAttack);
                CombatFeedback.TryPlaySfx(audioSource, attackSfx);

                yield return new WaitForSeconds(attackHitDelay);
                if (!CanAct) yield break;

                // Auto-aim may make a small correction during the windup, but it
                // never hard-snaps around the player. Manual casts have no target
                // and therefore remain fully committed.
                if (target != null && !target.IsDead)
                {
                    Vector3 trackedDirection = target.transform.position - transform.position;
                    worldDirection = MobileCombatRules.LimitAimCorrection(
                        worldDirection, trackedDirection);
                }
                FaceInstant(transform.position + worldDirection);

                if (projectilePrefab != null) FireProjectile(target, worldDirection);
                else MeleeStrike(worldDirection);
            }
            finally
            {
                PresentWeaponAim(Vector3.zero);
                attackRoutine = null;
            }
        }

        IEnumerator SuperRoutine(BrawlerController target, Vector3 worldDirection)
        {
            try
            {
                TryPresent(AnimationPresentationOperation.PlaySuper);
                if (superVfx != null)
                    SpawnVfx(superVfx, transform.position + Vector3.up * 0.8f, transform.rotation, 2.8f);
                if (secondarySuperVfx != null && secondarySuperVfx != superVfx)
                    SpawnVfx(secondarySuperVfx, transform.position + Vector3.up * 0.8f,
                        transform.rotation, 2.8f);

                yield return new WaitForSeconds(
                    superStyle == BrawlerSuperStyle.Dash ? 0.04f : 0.14f);
                if (!CanAct) yield break;

                if (target != null && !target.IsDead)
                {
                    Vector3 trackedDirection = target.transform.position - transform.position;
                    trackedDirection.y = 0f;
                    if (trackedDirection.sqrMagnitude > 0.0001f)
                        worldDirection = trackedDirection.normalized;
                }
                FaceInstant(transform.position + worldDirection);

                switch (superStyle)
                {
                    case BrawlerSuperStyle.Dash:
                        ExecuteSuperDash(worldDirection);
                        PerformSuperBurst();
                        break;
                    case BrawlerSuperStyle.ProjectileBlast:
                        if (projectilePrefab != null) FireSuperProjectile(target, worldDirection);
                        else PerformSuperBurst();
                        break;
                    default:
                        PerformSuperBurst();
                        break;
                }
            }
            finally
            {
                PresentWeaponAim(Vector3.zero);
                superInProgress = false;
                superRoutine = null;
            }
        }

        void FireProjectile(BrawlerController target, Vector3 worldDirection)
        {
            Vector3 muzzle = SpellMuzzlePosition;
            Vector3 dir = worldDirection.sqrMagnitude > 0.0001f
                ? worldDirection.normalized
                : transform.forward;
            if (target != null && !target.IsDead)
            {
                // Preserve the already-limited planar heading while allowing a
                // small vertical correction toward the target's center mass.
                Vector3 targetDelta = target.transform.position + Vector3.up * 1.1f - muzzle;
                float planarDistance = Vector3.ProjectOnPlane(targetDelta, Vector3.up).magnitude;
                dir = (Vector3.ProjectOnPlane(dir, Vector3.up).normalized *
                       Mathf.Max(0.01f, planarDistance) +
                       Vector3.up * (targetDelta.y * 0.35f)).normalized;
            }
            Projectile proj = CombatObjectPool.SpawnProjectile(
                projectilePrefab, muzzle, Quaternion.LookRotation(dir));
            if (proj == null) return;
            PresentWeaponMuzzle(muzzle, dir);
            // Basic shots use only the authored projectile and compact primary
            // hit. The secondary collision layer is reserved for Supers.
            proj.Launch(this, dir, attackDamage, projectileSpeed, impactVfx,
                0f, specialty.knockback, 0f, specialty, null, attackRange, target);
        }

        void FireSuperProjectile(BrawlerController target, Vector3 worldDirection)
        {
            Vector3 muzzle = SpellMuzzlePosition;
            Vector3 dir = worldDirection.sqrMagnitude > 0.0001f
                ? worldDirection.normalized
                : transform.forward;
            if (target != null && !target.IsDead)
            {
                Vector3 aim = target.transform.position + Vector3.up * 1.1f - muzzle;
                aim.y *= 0.35f;
                if (aim.sqrMagnitude > 0.01f) dir = aim.normalized;
            }

            SpawnCastEffects(muzzle, Quaternion.LookRotation(dir), 2.4f);
            GameObject prefab = superProjectilePrefab != null ? superProjectilePrefab : projectilePrefab;
            Projectile proj = CombatObjectPool.SpawnProjectile(
                prefab, muzzle, Quaternion.LookRotation(dir));
            if (proj == null) return;
            PresentWeaponMuzzle(muzzle, dir);
            GameObject superImpact = superImpactVfx != null ? superImpactVfx : impactVfx;
            proj.Launch(this, dir, attackDamage * superDamageMultiplier,
                superProjectileSpeed > 0f ? superProjectileSpeed : projectileSpeed * 1.4f,
                superImpact, superProjectileBlastRadius,
                Mathf.Max(superKnockback, specialty.knockback), 0.48f,
                specialty, secondaryImpactVfx, 0f, target);
        }

        void MeleeStrike(Vector3 worldDirection)
        {
            // Capsule from the body out to attack range: point-blank enemies are
            // inside the volume too, not just those standing at max range.
            Vector3 origin = transform.position + Vector3.up;
            worldDirection.y = 0f;
            Vector3 direction = worldDirection.sqrMagnitude > 0.0001f
                ? worldDirection.normalized
                : transform.forward;
            Vector3 tip = origin + direction * attackRange;
            SpawnCastEffects(origin + direction * (attackRange * 0.5f), transform.rotation, 2f);

            var manager = MatchManager.Instance;
            if (manager == null || manager.State == MatchState.Ended) return;
            var brawlers = manager.GetBrawlers();
            bool specialtyChainUsed = false;
            for (int i = 0; i < brawlers.Count; i++)
            {
                var other = brawlers[i];
                if (other == null || other == this || other.team == team || other.IsDead) continue;
                if (!CombatPhysics.PointInsideCapsule(other.CombatAimPoint, origin, tip,
                        attackRadius + other.CombatHitRadius))
                    continue;
                if (!CombatPhysics.HasLineOfSight(origin, other.CombatAimPoint)) continue;

                float applied = other.Health.TakeDamage(attackDamage, gameObject);
                if (impactVfx != null)
                    SpawnVfx(impactVfx, other.transform.position + Vector3.up * 1.1f, Quaternion.identity, 2.5f);
                if (secondaryImpactVfx != null && secondaryImpactVfx != impactVfx)
                    SpawnVfx(secondaryImpactVfx, other.transform.position + Vector3.up * 1.1f,
                        Quaternion.identity, 2.5f);
                if (applied > 0f)
                {
                    if (specialty.knockback > 0f)
                        other.ApplyKnockback(other.transform.position - transform.position,
                            specialty.knockback);
                    ApplySpellSpecialtyHit(other, applied, other.CombatAimPoint,
                        secondaryImpactVfx, !specialtyChainUsed);
                    if (specialty.school == SpellSchool.Storm)
                        specialtyChainUsed = true;
                }
                if (manager.State == MatchState.Ended) break;
            }
        }

        void PerformSuperBurst()
        {
            Vector3 origin = transform.position + Vector3.up;
            SpawnCastEffects(origin, transform.rotation, 2.2f);
            if (specialty.school == SpellSchool.Arcane)
                ApplyArcaneRitualHeal(origin,
                    Mathf.Max(superRange, specialty.allyHealRadius));

            var manager = MatchManager.Instance;
            if (manager == null || manager.State == MatchState.Ended) return;
            var brawlers = manager.GetBrawlers();
            float damage = attackDamage * superDamageMultiplier;
            bool specialtyChainUsed = false;
            for (int i = 0; i < brawlers.Count; i++)
            {
                var other = brawlers[i];
                if (other == null || other == this || other.team == team || other.IsDead) continue;
                float reach = superRange + other.CombatHitRadius;
                if ((other.CombatAimPoint - origin).sqrMagnitude > reach * reach) continue;
                if (!CombatPhysics.HasLineOfSight(origin, other.CombatAimPoint)) continue;

                float applied = other.Health.TakeDamage(damage, gameObject);
                if (applied > 0f && manager.State == MatchState.Playing)
                {
                    other.ApplyKnockback(other.transform.position - transform.position,
                        Mathf.Max(superKnockback, specialty.knockback));
                    ApplySpellSpecialtyHit(other, applied, other.CombatAimPoint,
                        secondaryImpactVfx, !specialtyChainUsed);
                    if (specialty.school == SpellSchool.Storm)
                        specialtyChainUsed = true;
                }
                if (impactVfx != null)
                    SpawnVfx(impactVfx, other.transform.position + Vector3.up * 1.1f, Quaternion.identity, 2.5f);
                if (secondaryImpactVfx != null && secondaryImpactVfx != impactVfx)
                    SpawnVfx(secondaryImpactVfx, other.transform.position + Vector3.up * 1.1f,
                        Quaternion.identity, 2.5f);
                if (manager.State == MatchState.Ended) break;
            }
        }

        void ExecuteSuperDash(Vector3 worldDirection)
        {
            Vector3 direction = worldDirection;
            direction.y = 0f;
            if (direction.sqrMagnitude < 0.01f) direction = transform.forward;
            direction.Normalize();

            float distance = superDashDistance;
            Vector3 sweepOrigin = CombatAimPoint;
            if (CombatPhysics.SweepWorld(sweepOrigin, CombatHitRadius, direction, distance,
                    false, out RaycastHit hit))
                distance = Mathf.Max(0f, hit.distance - 0.05f);

            var manager = MatchManager.Instance;
            if (manager != null)
            {
                var brawlers = manager.GetBrawlers();
                for (int i = 0; i < brawlers.Count; i++)
                {
                    var other = brawlers[i];
                    if (other == null || other == this || other.team == team || other.IsDead) continue;
                    if (CombatPhysics.TryIntersectSegmentSphere(sweepOrigin, direction, distance,
                            other.CombatAimPoint, CombatHitRadius + other.CombatHitRadius,
                            out float contactDistance))
                        distance = Mathf.Min(distance, contactDistance);
                }
            }

            if (distance <= 0.05f) return;
            Vector3 destination = transform.position + direction * distance;
            destination = Motor.ConstrainTeleportDestination(destination, 2f);
            Teleport(destination);
            FaceInstant(transform.position + direction);
        }

        public void ApplyKnockback(Vector3 direction, float distance)
        {
            if (IsDead || Health.Invulnerable || distance <= 0.01f) return;
            if (MatchManager.Instance != null && MatchManager.Instance.State != MatchState.Playing)
                return;
            direction.y = 0f;
            if (direction.sqrMagnitude < 0.001f) return;
            CancelWardStep();
            StopKnockback();
            Motor.BeginExternalDisplacement();
            knockbackOwnsExternalDisplacement = true;
            knockbackRoutine = StartCoroutine(KnockbackRoutine(direction.normalized, distance));
        }

        IEnumerator KnockbackRoutine(Vector3 direction, float distance)
        {
            float duration = Mathf.Clamp(0.1f + distance * 0.018f, 0.12f, 0.28f);
            float moved = 0f;
            for (float elapsed = 0f; elapsed < duration; elapsed += Time.deltaTime)
            {
                float next = Mathf.SmoothStep(0f, distance, Mathf.Clamp01((elapsed + Time.deltaTime) / duration));
                Vector3 delta = direction * (next - moved);
                moved = next;
                Motor.Displace(delta, false);
                yield return null;
            }
            knockbackRoutine = null;
            EndKnockbackDisplacement();
        }

        void StopKnockback()
        {
            if (knockbackRoutine != null)
            {
                StopCoroutine(knockbackRoutine);
                knockbackRoutine = null;
            }
            EndKnockbackDisplacement();
        }

        void EndKnockbackDisplacement()
        {
            if (knockbackOwnsExternalDisplacement) Motor.EndExternalDisplacement();
            knockbackOwnsExternalDisplacement = false;
        }

        public static void SpawnVfx(GameObject prefab, Vector3 pos, Quaternion rot, float life)
        {
            if (prefab == null) return;
            CombatObjectPool.SpawnVfx(prefab, pos, rot, life);
        }

        void SpawnCastEffects(Vector3 pos, Quaternion rot, float life)
        {
            GameObject primary = castVfx != null ? castVfx : swingVfx;
            if (primary != null) SpawnVfx(primary, pos, rot, life);
            if (secondaryCastVfx != null && secondaryCastVfx != primary)
                SpawnVfx(secondaryCastVfx, pos, rot, life);
        }

        /// <summary>
        /// Stops windups and offensive movement without disturbing unrelated
        /// lifecycle coroutines such as respawn UI or invulnerability flashing.
        /// MatchManager calls this synchronously before announcing match end.
        /// </summary>
        public void CancelOffensiveActions()
        {
            if (attackRoutine != null)
            {
                StopCoroutine(attackRoutine);
                attackRoutine = null;
            }
            if (superRoutine != null)
            {
                StopCoroutine(superRoutine);
                superRoutine = null;
            }
            StopKnockback();
            CancelWardStep();
            superInProgress = false;
        }

        // ---------------- damage / death / respawn ----------------

        void OnDamaged(float amount, GameObject attacker)
        {
            lastDamagedAt = Time.time;
            var source = attacker != null ? attacker.GetComponentInParent<BrawlerController>() : null;
            if (source != null && source != this)
            {
                source.AddSuperCharge(amount * source.superChargeFromDamageDealt);
                // Lethal damage is reported once by OnDied as the higher-value
                // KO signal, avoiding two hardware pulses in the same frame.
                if (source.IsPlayer && !IsDead) CombatFeedback.ReportLocalDealtHit();
            }
            AddSuperCharge(amount * superChargeFromDamageTaken);
            CombatFeedback.TryPlaySfx(audioSource, hitSfx);
            if (IsPlayer)
            {
                if (!IsDead) CombatFeedback.ReportLocalReceivedHit();
                BrawlCamera.Shake(0.28f, 0.18f);
            }
            if (IsDead) return;
            if (Time.time > attackLockUntil && Time.time > nextFlinchTime && Random.value < 0.6f)
            {
                nextFlinchTime = Time.time + 1.1f;
                TryPresent(AnimationPresentationOperation.PlayHitReaction);
            }
        }

        void OnDied(GameObject attacker)
        {
            StopAllCoroutines();
            EndKnockbackDisplacement();
            CancelWardStep();
            ClearSpellStatuses();
            attackRoutine = null;
            superRoutine = null;
            superInProgress = false;
            knockbackRoutine = null;
            invulnerabilityRoutine = null;
            var killer = attacker != null ? attacker.GetComponentInParent<BrawlerController>() : null;
            if (killer != null && killer != this) killer.AddSuperCharge(killer.superChargeOnKO);
            if (IsPlayer || (killer != null && killer.IsPlayer))
                CombatFeedback.ReportLocalKnockout();
            moveInput = Vector3.zero;
            PresentWeaponVisibility(false);
            TryPresent(AnimationPresentationOperation.PlayDeath);
            if (koVfx != null) SpawnVfx(koVfx, transform.position + Vector3.up * 0.5f, Quaternion.identity, 3f);
            if (IsPlayer) BrawlCamera.Shake(0.5f, 0.3f);
            Motor?.Stop(true);
            if (MatchManager.Instance != null)
            {
                MatchManager.Instance.ReportKO(this, attacker);
                if (MatchManager.Instance.State == MatchState.Ended) return;
            }
            StartCoroutine(RespawnRoutine());
        }

        IEnumerator RespawnRoutine()
        {
            respawning = true;
            if (MatchManager.Instance != null && MatchManager.Instance.State == MatchState.Ended)
            {
                respawning = false;
                yield break;
            }
            float delay = MatchManager.Instance != null ? MatchManager.Instance.respawnDelay : 2.5f;
            delay *= Mathf.Max(0.2f, respawnDelayMultiplier);
            if (IsPlayer && BrawlHUD.Instance != null) BrawlHUD.Instance.ShowRespawn(delay);

            yield return new WaitForSeconds(delay);

            if (MatchManager.Instance != null && MatchManager.Instance.State == MatchState.Ended)
            {
                if (IsPlayer && BrawlHUD.Instance != null) BrawlHUD.Instance.HideRespawn();
                respawning = false;
                yield break;
            }

            Vector3 spawn = MatchManager.Instance != null
                ? MatchManager.Instance.GetSpawnPoint(team)
                : transform.position;
            Teleport(spawn);
            ClearSpellStatuses();
            Health.Revive();
            maxStamina = MobileCombatRules.ArcaneFlowCapacity;
            Stamina = maxStamina;
            staminaRegenAt = 0f;
            CancelWardStep();
            EndKnockbackDisplacement();
            superInProgress = false;
            ResetWeaponForRespawn();
            PresentWeaponVisibility(true);
            TryPresent(AnimationPresentationOperation.PlayRespawn);
            if (spawnVfx != null) SpawnVfx(spawnVfx, spawn, Quaternion.identity, 3f);
            if (IsPlayer && BrawlHUD.Instance != null) BrawlHUD.Instance.HideRespawn();
            respawning = false;
            if (invulnerabilityRoutine != null) StopCoroutine(invulnerabilityRoutine);
            invulnerabilityRoutine = StartCoroutine(InvulnerabilityRoutine(1.5f));
        }

        void CancelSpawnProtectionOnOffense()
        {
            if (Health == null || !Health.Invulnerable) return;
            if (invulnerabilityRoutine != null)
            {
                StopCoroutine(invulnerabilityRoutine);
                invulnerabilityRoutine = null;
            }
            Health.Invulnerable = false;
            SetSkinsVisible(true);
        }

        void AddSuperCharge(float amount)
        {
            if (amount <= 0f || maxSuperCharge <= 0f) return;
            SuperCharge = Mathf.Clamp(SuperCharge + amount, 0f, maxSuperCharge);
        }

        public void Teleport(Vector3 pos)
        {
            Motor?.Teleport(pos);
        }

        IEnumerator InvulnerabilityRoutine(float duration)
        {
            Health.Invulnerable = true;
            float end = Time.time + duration;
            bool visible = true;
            while (Time.time < end)
            {
                visible = !visible;
                SetSkinsVisible(visible);
                yield return new WaitForSeconds(0.12f);
            }
            SetSkinsVisible(true);
            Health.Invulnerable = false;
            invulnerabilityRoutine = null;
        }

        void SetSkinsVisible(bool visible)
        {
            foreach (var s in skins)
                if (s != null) s.enabled = visible;
            PresentWeaponVisibility(visible);
        }

        public void PlayVictory()
        {
            CancelOffensiveActions();
            moveInput = Vector3.zero;
            Motor?.Stop(false);
            TryPresent(AnimationPresentationOperation.PlayVictory);
        }

        // ---------------- cosmetics ----------------

        void CreateTeamRing()
        {
            var ring = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            ring.name = "TeamRing";
            Destroy(ring.GetComponent<Collider>());
            ring.transform.SetParent(transform, false);
            ring.transform.localPosition = new Vector3(0f, 0.04f, 0f);
            ring.transform.localScale = new Vector3(1.7f, 0.012f, 1.7f);

            var mr = ring.GetComponent<MeshRenderer>();
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            mr.receiveShadows = false;

            var shader = Shader.Find("Universal Render Pipeline/Unlit");
            if (shader == null) shader = Shader.Find("Universal Render Pipeline/Lit");
            if (shader == null) shader = Shader.Find("Sprites/Default");
            if (shader == null) return;
            var m = new Material(shader);
            Color c = TeamUtil.Color(team);
            m.color = new Color(c.r, c.g, c.b, IsPlayer ? 0.8f : 0.5f);
            m.SetFloat("_Surface", 1f);
            m.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
            m.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            m.SetFloat("_ZWrite", 0f);
            m.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            m.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
            mr.sharedMaterial = m;
        }
    }
}
