using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

namespace BrawlArena
{
    /// <summary>
    /// Drives one brawler body: movement, animation, combat, sprint, death and
    /// respawn. Intent comes from PlayerBrawlerInput (CharacterController) or
    /// AIBrawler (NavMeshAgent); this class is shared by both.
    ///
    /// The ModularRPGHeroesPBR showcase controllers expose no locomotion
    /// parameters (their only parameter is a trigger), so locomotion is driven
    /// by state: CrossFade between Idle_&lt;suffix&gt; and Run_&lt;suffix&gt; based on
    /// measured speed, with a watchdog that returns to locomotion when any
    /// one-shot state (attack, hit reaction) finishes.
    /// </summary>
    [RequireComponent(typeof(Health))]
    public class BrawlerController : MonoBehaviour
    {
        [Header("Identity")]
        public string displayName = "Brawler";
        public TeamId team = TeamId.Blue;

        [Header("Animation")]
        public string animSuffix = "DoubleSword";
        public string[] attackStates = { "NormalAttack01_DoubleSword" };

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

        [Header("Sprint")]
        public float sprintMultiplier = 1.5f;
        public float maxStamina = 100f;
        public float staminaDrainPerSec = 26f;
        public float staminaRegenPerSec = 20f;
        public float staminaRegenDelay = 0.8f;

        [Header("Ranged (leave prefab empty for melee)")]
        public GameObject projectilePrefab;
        public float projectileSpeed = 16f;

        [Header("Effects (all optional)")]
        public GameObject swingVfx;
        public GameObject impactVfx;
        public GameObject koVfx;
        public GameObject spawnVfx;
        public AudioClip attackSfx;
        public AudioClip hitSfx;

        public Health Health { get; private set; }
        public bool IsDead => Health.IsDead;
        public bool IsPlayer { get; private set; }
        public bool MovementLocked => Time.time < attackLockUntil;
        public float Stamina { get; private set; }
        public bool Sprinting { get; private set; }
        public float CurrentSpeed => moveSpeed * (Sprinting ? sprintMultiplier : 1f);
        public float CooldownFraction =>
            Mathf.Clamp01((nextAttackTime - Time.time) / Mathf.Max(0.01f, attackCooldown));
        public bool CanAct =>
            !IsDead && !respawning &&
            (MatchManager.Instance == null || MatchManager.Instance.State == MatchState.Playing);

        static readonly Collider[] MeleeBuffer = new Collider[24];
        static readonly HashSet<BrawlerController> MeleeHitSet = new HashSet<BrawlerController>();

        Animator anim;
        CharacterController cc;
        NavMeshAgent agent;
        AudioSource audioSource;
        SkinnedMeshRenderer[] skins;
        Vector3 moveInput;
        bool sprintInput;
        float nextAttackTime;
        float attackLockUntil;
        float nextFlinchTime;
        float staminaRegenAt;
        bool respawning;
        bool initialized;

        int idleHash;
        int runHash;
        int getHitHash;
        int dieHash;
        int victoryHash;
        int[] attackHashes;

        void Awake()
        {
            Health = GetComponent<Health>();
            cc = GetComponent<CharacterController>();
            agent = GetComponent<NavMeshAgent>();
            anim = GetComponentInChildren<Animator>();
            skins = GetComponentsInChildren<SkinnedMeshRenderer>();
            Stamina = maxStamina;

            audioSource = gameObject.AddComponent<AudioSource>();
            audioSource.playOnAwake = false;
            audioSource.spatialBlend = 1f;
            audioSource.maxDistance = 30f;

            Health.Damaged += OnDamaged;
            Health.Died += OnDied;
        }

        /// <summary>
        /// Deferred init: runtime spawning (GameFlow) adds this component before
        /// configuring fields and sibling components, so identity, hashes and
        /// cosmetics must resolve in Start, not Awake.
        /// </summary>
        void Start()
        {
            IsPlayer = GetComponent<PlayerBrawlerInput>() != null;
            cc = GetComponent<CharacterController>();
            agent = GetComponent<NavMeshAgent>();
            Stamina = maxStamina;

            idleHash = Animator.StringToHash("Idle_" + animSuffix);
            runHash = Animator.StringToHash("Run_" + animSuffix);
            getHitHash = Animator.StringToHash("GetHit_" + animSuffix);
            dieHash = Animator.StringToHash("Die_" + animSuffix);
            victoryHash = Animator.StringToHash("Victory_" + animSuffix);
            attackHashes = new int[attackStates != null ? attackStates.Length : 0];
            for (int i = 0; i < attackHashes.Length; i++)
                attackHashes[i] = Animator.StringToHash(attackStates[i]);

            if (anim != null) anim.applyRootMotion = false;
            if (agent != null)
            {
                agent.speed = moveSpeed;
                agent.acceleration = 40f;
                agent.angularSpeed = 720f;
                // If this agent enabled before the NavMesh bake finished, it
                // never attached to the mesh; re-enable and warp to recover.
                if (!agent.isOnNavMesh)
                {
                    agent.enabled = false;
                    agent.enabled = true;
                    if (!agent.isOnNavMesh) agent.Warp(transform.position);
                }
            }
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

        public void SetSprintInput(bool on)
        {
            sprintInput = on;
        }

        void Update()
        {
            if (!initialized) return;
            UpdateSprint();

            if (cc != null && cc.enabled)
            {
                // Attacking does not lock movement: swings re-aim at the
                // target when damage lands, so kiting mid-swing stays fair.
                Vector3 planar = CanAct ? moveInput * CurrentSpeed : Vector3.zero;
                cc.Move((planar + Vector3.down * 15f) * Time.deltaTime);
                if (planar.sqrMagnitude > 0.04f)
                {
                    Quaternion look = Quaternion.LookRotation(planar.normalized, Vector3.up);
                    transform.rotation = Quaternion.Slerp(transform.rotation, look, 14f * Time.deltaTime);
                }
            }
            else if (agent != null && agent.enabled)
            {
                agent.speed = CurrentSpeed;
            }

            UpdateAnimator();
        }

        void UpdateSprint()
        {
            bool moving = cc != null
                ? moveInput.sqrMagnitude > 0.04f
                : agent != null && agent.enabled && agent.velocity.sqrMagnitude > 0.2f;
            Sprinting = sprintInput && moving && Stamina > 0f && CanAct;
            if (Sprinting)
            {
                Stamina = Mathf.Max(0f, Stamina - staminaDrainPerSec * Time.deltaTime);
                staminaRegenAt = Time.time + staminaRegenDelay;
            }
            else if (Time.time >= staminaRegenAt)
            {
                Stamina = Mathf.Min(maxStamina, Stamina + staminaRegenPerSec * Time.deltaTime);
            }
        }

        // ---------------- animation ----------------

        float ComputeSpeed01()
        {
            Vector3 v;
            if (agent != null && agent.enabled) v = agent.velocity;
            else if (cc != null) v = cc.velocity;
            else return 0f;
            v.y = 0f;
            return Mathf.Clamp01(v.magnitude / Mathf.Max(0.1f, moveSpeed));
        }

        /// <summary>
        /// State-driven locomotion: the pack controllers have no locomotion
        /// parameters, so switch Idle/Run by CrossFade and return from finished
        /// one-shots ourselves.
        /// </summary>
        void UpdateAnimator()
        {
            if (anim == null || IsDead) return;
            if (MatchManager.Instance != null && MatchManager.Instance.State == MatchState.Ended) return;
            if (anim.IsInTransition(0)) return;

            float speed01 = ComputeSpeed01();
            var st = anim.GetCurrentAnimatorStateInfo(0);
            int cur = st.shortNameHash;
            bool wantRun = speed01 > 0.25f;
            bool wantIdle = speed01 <= 0.2f;

            if (cur == runHash)
            {
                if (wantIdle) anim.CrossFadeInFixedTime(idleHash, 0.15f);
                return;
            }
            if (cur == idleHash)
            {
                if (wantRun) anim.CrossFadeInFixedTime(runHash, 0.12f);
                return;
            }
            if (st.loop)
            {
                // A looping variation (idle fidget etc.) — pull back into main locomotion.
                if (wantRun) anim.CrossFadeInFixedTime(runHash, 0.15f);
                else if (wantIdle) anim.CrossFadeInFixedTime(idleHash, 0.25f);
                return;
            }
            // One-shot (attack, hit reaction, spawn pose): let it finish, then return.
            if (st.normalizedTime >= 0.92f)
                anim.CrossFadeInFixedTime(wantRun ? runHash : idleHash, 0.12f);
        }

        // ---------------- combat ----------------

        public BrawlerController FindNearestEnemy(float maxRange)
        {
            if (MatchManager.Instance == null) return null;
            BrawlerController best = null;
            float bestDist = maxRange;
            var brawlers = MatchManager.Instance.GetBrawlers();
            for (int i = 0; i < brawlers.Count; i++)
            {
                var b = brawlers[i];
                if (b == null || b == this || b.team == team || b.IsDead) continue;
                float d = Vector3.Distance(transform.position, b.transform.position);
                if (d < bestDist)
                {
                    bestDist = d;
                    best = b;
                }
            }
            return best;
        }

        public bool TryAttackAuto()
        {
            // Early-out before the enemy scan: this runs every frame while the
            // attack button is held.
            if (!CanAct || Time.time < nextAttackTime) return false;
            return TryAttack(FindNearestEnemy(autoAimRange));
        }

        public bool TryAttack(BrawlerController target)
        {
            if (!CanAct || Time.time < nextAttackTime) return false;
            nextAttackTime = Time.time + attackCooldown;
            attackLockUntil = Time.time + attackMoveLock;
            if (target != null) FaceInstant(target.transform.position);
            StartCoroutine(AttackRoutine(target));
            return true;
        }

        void FaceInstant(Vector3 worldPoint)
        {
            Vector3 dir = worldPoint - transform.position;
            dir.y = 0f;
            if (dir.sqrMagnitude > 0.001f)
                transform.rotation = Quaternion.LookRotation(dir.normalized, Vector3.up);
        }

        IEnumerator AttackRoutine(BrawlerController target)
        {
            if (anim != null && attackHashes != null && attackHashes.Length > 0)
                anim.CrossFadeInFixedTime(attackHashes[Random.Range(0, attackHashes.Length)], 0.08f);
            if (attackSfx != null) audioSource.PlayOneShot(attackSfx);

            yield return new WaitForSeconds(attackHitDelay);
            if (IsDead) yield break;

            // Re-face the target at the moment damage lands, so enemies that
            // circled around during the windup are still hit.
            if (target != null && !target.IsDead) FaceInstant(target.transform.position);

            if (projectilePrefab != null) FireProjectile(target);
            else MeleeStrike();
        }

        void FireProjectile(BrawlerController target)
        {
            Vector3 muzzle = transform.position + transform.forward * 0.6f + Vector3.up * 1.25f;
            Vector3 dir = transform.forward;
            if (target != null && !target.IsDead)
            {
                Vector3 aim = target.transform.position + Vector3.up * 1.1f - muzzle;
                aim.y *= 0.35f;
                if (aim.sqrMagnitude > 0.01f) dir = aim.normalized;
            }
            if (swingVfx != null) SpawnVfx(swingVfx, muzzle, Quaternion.LookRotation(dir), 2f);

            GameObject go = Instantiate(projectilePrefab, muzzle, Quaternion.LookRotation(dir));
            var proj = go.GetComponent<Projectile>();
            if (proj == null) proj = go.AddComponent<Projectile>();
            proj.Launch(this, dir, attackDamage, projectileSpeed, impactVfx);
        }

        void MeleeStrike()
        {
            // Capsule from the body out to attack range: point-blank enemies are
            // inside the volume too, not just those standing at max range.
            Vector3 origin = transform.position + Vector3.up;
            Vector3 tip = origin + transform.forward * attackRange;
            if (swingVfx != null)
                SpawnVfx(swingVfx, origin + transform.forward * (attackRange * 0.5f), transform.rotation, 2f);

            MeleeHitSet.Clear();
            int count = Physics.OverlapCapsuleNonAlloc(origin, tip, attackRadius, MeleeBuffer, ~0, QueryTriggerInteraction.Ignore);
            for (int i = 0; i < count; i++)
            {
                var other = MeleeBuffer[i].GetComponentInParent<BrawlerController>();
                if (other == null || other == this || other.team == team || other.IsDead) continue;
                if (!MeleeHitSet.Add(other)) continue;
                other.Health.TakeDamage(attackDamage, gameObject);
                if (impactVfx != null)
                    SpawnVfx(impactVfx, other.transform.position + Vector3.up * 1.1f, Quaternion.identity, 2.5f);
            }
        }

        public static void SpawnVfx(GameObject prefab, Vector3 pos, Quaternion rot, float life)
        {
            if (prefab == null) return;
            var go = Instantiate(prefab, pos, rot);
            Destroy(go, life);
        }

        // ---------------- damage / death / respawn ----------------

        void OnDamaged(float amount, GameObject attacker)
        {
            if (hitSfx != null) audioSource.PlayOneShot(hitSfx);
            if (IsPlayer) BrawlCamera.Shake(0.28f, 0.18f);
            if (IsDead) return;
            if (Time.time > attackLockUntil && Time.time > nextFlinchTime && Random.value < 0.6f)
            {
                nextFlinchTime = Time.time + 1.1f;
                if (anim != null) anim.CrossFadeInFixedTime(getHitHash, 0.08f);
            }
        }

        void OnDied(GameObject attacker)
        {
            StopAllCoroutines();
            moveInput = Vector3.zero;
            if (anim != null) anim.CrossFadeInFixedTime(dieHash, 0.1f);
            if (koVfx != null) SpawnVfx(koVfx, transform.position + Vector3.up * 0.5f, Quaternion.identity, 3f);
            if (IsPlayer) BrawlCamera.Shake(0.5f, 0.3f);
            if (agent != null)
            {
                if (agent.enabled && agent.isOnNavMesh) agent.ResetPath();
                agent.enabled = false;
            }
            if (MatchManager.Instance != null) MatchManager.Instance.ReportKO(this, attacker);
            StartCoroutine(RespawnRoutine());
        }

        IEnumerator RespawnRoutine()
        {
            respawning = true;
            float delay = MatchManager.Instance != null ? MatchManager.Instance.respawnDelay : 2.5f;
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
            Health.Revive();
            Stamina = maxStamina;
            if (anim != null) anim.CrossFadeInFixedTime(idleHash, 0.05f);
            if (spawnVfx != null) SpawnVfx(spawnVfx, spawn, Quaternion.identity, 3f);
            if (IsPlayer && BrawlHUD.Instance != null) BrawlHUD.Instance.HideRespawn();
            respawning = false;
            StartCoroutine(InvulnerabilityRoutine(1.5f));
        }

        public void Teleport(Vector3 pos)
        {
            if (agent != null)
            {
                agent.enabled = true;
                agent.Warp(pos);
            }
            else if (cc != null)
            {
                cc.enabled = false;
                transform.position = pos;
                cc.enabled = true;
            }
            else
            {
                transform.position = pos;
            }
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
        }

        void SetSkinsVisible(bool visible)
        {
            foreach (var s in skins)
                if (s != null) s.enabled = visible;
        }

        public void PlayVictory()
        {
            moveInput = Vector3.zero;
            if (agent != null && agent.enabled && agent.isOnNavMesh) agent.ResetPath();
            if (anim != null) anim.CrossFadeInFixedTime(victoryHash, 0.2f);
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
