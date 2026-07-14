using UnityEngine;

namespace BrawlArena
{
    /// <summary>
    /// Straight-flying projectile. Combatant contact is resolved against the
    /// deterministic MatchManager roster; physics is used only for the named
    /// Ground/WorldBlocker layers, so dense scenery cannot truncate hit lists.
    /// </summary>
    public class Projectile : MonoBehaviour
    {
        public const float DefaultHitRadius = 0.3f;
        public const float DefaultHomingTurnRate = 150f;
        public const float DefaultHomingAcquireHalfAngle = 52f;
        const float HomingReacquireInterval = 0.1f;

        public float lifeTime = 3f;
        public float hitRadius = DefaultHitRadius;
        [Tooltip("Maximum steering speed after a target is locked. Zero disables homing.")]
        public float homingTurnRate = DefaultHomingTurnRate;
        [Tooltip("Manual shots may acquire enemies inside this forward half-angle.")]
        public float homingAcquireHalfAngle = DefaultHomingAcquireHalfAngle;

        public BrawlerController HomingTarget => homingTarget;
        public Vector3 TravelDirection => dir;

        BrawlerController owner;
        MatchManager matchManager;
        Vector3 dir;
        float damage;
        float speed;
        GameObject impactVfx;
        float blastRadius;
        float knockback;
        SpellSpecialty specialty;
        GameObject secondaryImpactVfx;
        float activeHitRadius;
        float remainingTravelDistance;
        float dieAt;
        BrawlerController homingTarget;
        float homingAcquireRange;
        float nextHomingAcquireAt;
        bool launched;
        bool destroyRequested;
        bool specialtyChainTriggered;

        public void Launch(BrawlerController owner, Vector3 direction, float damage, float speed,
            GameObject impactVfx)
        {
            Launch(owner, direction, damage, speed, impactVfx, 0f, 0f);
        }

        /// <summary>
        /// Launches a projectile with an optional impact blast. Standard
        /// attacks use the short overload; Super projectiles use the blast.
        /// </summary>
        public void Launch(BrawlerController owner, Vector3 direction, float damage, float speed,
            GameObject impactVfx, float blastRadius, float knockback)
        {
            Launch(owner, direction, damage, speed, impactVfx, blastRadius, knockback, 0f);
        }

        /// <summary>
        /// The final argument changes only this lease's collision radius. It
        /// keeps a Super projectile from widening a later normal shot when the
        /// same clone is reused.
        /// </summary>
        public void Launch(BrawlerController owner, Vector3 direction, float damage, float speed,
            GameObject impactVfx, float blastRadius, float knockback, float minimumHitRadius)
        {
            Launch(owner, direction, damage, speed, impactVfx, blastRadius, knockback,
                minimumHitRadius, default, null);
        }

        /// <summary>
        /// Launches a spell projectile with a bounded school payload and an
        /// optional second impact effect. Legacy overloads intentionally flow
        /// through here so pooled leases always clear school-specific state.
        /// </summary>
        public void Launch(BrawlerController owner, Vector3 direction, float damage, float speed,
            GameObject impactVfx, float blastRadius, float knockback, float minimumHitRadius,
            SpellSpecialty specialty, GameObject secondaryImpactVfx)
        {
            Launch(owner, direction, damage, speed, impactVfx, blastRadius, knockback,
                minimumHitRadius, specialty, secondaryImpactVfx, 0f);
        }

        /// <summary>
        /// Launches a spell projectile with an optional authoritative travel
        /// limit. A non-positive limit preserves the legacy lifetime-only
        /// behavior used by Super projectiles and existing callers.
        /// </summary>
        public void Launch(BrawlerController owner, Vector3 direction, float damage, float speed,
            GameObject impactVfx, float blastRadius, float knockback, float minimumHitRadius,
            SpellSpecialty specialty, GameObject secondaryImpactVfx, float maxTravelDistance)
        {
            Launch(owner, direction, damage, speed, impactVfx, blastRadius, knockback,
                minimumHitRadius, specialty, secondaryImpactVfx, maxTravelDistance, null);
        }

        /// <summary>
        /// Target-aware launch used by auto-aim and AI. A missing or invalid
        /// target keeps the committed launch heading, then looks for a visible
        /// enemy in a moderate forward cone so manual casts can still track.
        /// </summary>
        public void Launch(BrawlerController owner, Vector3 direction, float damage, float speed,
            GameObject impactVfx, float blastRadius, float knockback, float minimumHitRadius,
            SpellSpecialty specialty, GameObject secondaryImpactVfx, float maxTravelDistance,
            BrawlerController lockedTarget)
        {
            ClearRuntimeState();
            destroyRequested = false;
            this.owner = owner;
            this.damage = Mathf.Max(0f, damage);
            this.speed = Mathf.Max(0f, speed);
            this.impactVfx = impactVfx;
            this.blastRadius = Mathf.Max(0f, blastRadius);
            this.knockback = Mathf.Max(0f, knockback);
            this.specialty = specialty.Sanitized();
            this.secondaryImpactVfx = secondaryImpactVfx;
            activeHitRadius = Mathf.Max(0f, Mathf.Max(hitRadius, minimumHitRadius));
            remainingTravelDistance = maxTravelDistance > 0f
                ? maxTravelDistance
                : float.PositiveInfinity;
            float ownerAimRange = owner != null
                ? Mathf.Max(owner.autoAimRange, owner.attackRange)
                : 0f;
            float travelRange = maxTravelDistance > 0f ? maxTravelDistance : ownerAimRange;
            homingAcquireRange = Mathf.Clamp(Mathf.Max(4f, travelRange + 2f), 4f, 24f);
            dir = direction.sqrMagnitude > 0.001f ? direction.normalized : Vector3.forward;
            dieAt = Time.time + Mathf.Max(0f, lifeTime);
            transform.rotation = Quaternion.LookRotation(dir);
            CombatPhysics.SetLayerRecursively(gameObject, CombatPhysics.ProjectileLayer);

            matchManager = MatchManager.Instance;
            if (matchManager != null)
                matchManager.MatchEnded += OnMatchEnded;

            launched = MatchAllowsDamage();
            if (!launched)
            {
                Despawn();
                return;
            }

            homingTarget = IsValidHomingTarget(lockedTarget) ? lockedTarget : null;
            nextHomingAcquireAt = Time.time;
            if (homingTarget == null) TryAcquireHomingTarget();
        }

        internal void PrepareForReuse()
        {
            ClearRuntimeState();
            destroyRequested = false;
        }

        internal void ResetForPool()
        {
            ClearRuntimeState();
            destroyRequested = false;
        }

        /// <summary>Idempotently returns a pooled projectile or destroys a direct instance.</summary>
        public void Despawn()
        {
            ClearRuntimeState();
            if (CombatObjectPool.Release(gameObject)) return;
            if (destroyRequested) return;
            destroyRequested = true;
            Destroy(gameObject);
        }

        void OnDisable()
        {
            if (launched) ClearRuntimeState();
        }

        void OnDestroy()
        {
            ClearRuntimeState();
        }

        void OnMatchEnded(TeamId? winner)
        {
            Despawn();
        }

        void ClearRuntimeState()
        {
            if (matchManager != null) matchManager.MatchEnded -= OnMatchEnded;
            matchManager = null;
            owner = null;
            dir = Vector3.forward;
            damage = 0f;
            speed = 0f;
            impactVfx = null;
            blastRadius = 0f;
            knockback = 0f;
            specialty = default;
            secondaryImpactVfx = null;
            activeHitRadius = 0f;
            remainingTravelDistance = 0f;
            dieAt = 0f;
            homingTarget = null;
            homingAcquireRange = 0f;
            nextHomingAcquireAt = 0f;
            launched = false;
            specialtyChainTriggered = false;
        }

        bool MatchAllowsDamage()
        {
            MatchManager current = MatchManager.Instance;
            return current == null || current.State != MatchState.Ended;
        }

        void Update()
        {
            if (!launched) return;
            if (!MatchAllowsDamage())
            {
                Despawn();
                return;
            }

            UpdateHoming(Time.deltaTime);
            float step = Mathf.Min(speed * Time.deltaTime, remainingTravelDistance);
            float sweepDistance = Mathf.Min(step + 0.05f, remainingTravelDistance);
            Vector3 position = transform.position;

            bool hitWorld = CombatPhysics.SweepWorld(position, activeHitRadius, dir, sweepDistance,
                true, out RaycastHit worldHit);
            float worldDistance = hitWorld ? worldHit.distance : float.MaxValue;

            bool hitBrawler = TryFindFirstBrawlerHit(position, sweepDistance,
                out BrawlerController target, out float targetDistance);
            if (hitBrawler && targetDistance <= worldDistance + 0.0001f)
            {
                Vector3 impactPoint = position + dir * targetDistance;
                if (blastRadius <= 0f) DamageTarget(target, impactPoint);
                Explode(impactPoint);
                return;
            }

            if (hitWorld)
            {
                Vector3 impactPoint = worldHit.distance > 0f
                    ? worldHit.point
                    : position;
                Explode(impactPoint);
                return;
            }

            transform.position = position + dir * step;
            remainingTravelDistance -= step;
            if (remainingTravelDistance <= 0.0001f || Time.time >= dieAt)
                Explode(transform.position);
        }

        void UpdateHoming(float deltaTime)
        {
            if (homingTurnRate <= 0f || owner == null) return;

            if (!IsValidHomingTarget(homingTarget)) homingTarget = null;
            if (homingTarget == null && Time.time >= nextHomingAcquireAt)
            {
                nextHomingAcquireAt = Time.time + HomingReacquireInterval;
                TryAcquireHomingTarget();
            }
            if (homingTarget == null) return;

            Vector3 desired = homingTarget.CombatAimPoint - transform.position;
            if (desired.sqrMagnitude <= 0.0001f) return;
            float radians = Mathf.Deg2Rad * Mathf.Max(0f, homingTurnRate) *
                            Mathf.Max(0f, deltaTime);
            dir = Vector3.RotateTowards(dir, desired.normalized, radians, 0f).normalized;
            if (dir.sqrMagnitude > 0.0001f)
                transform.rotation = Quaternion.LookRotation(dir, Vector3.up);
        }

        void TryAcquireHomingTarget()
        {
            MatchManager manager = MatchManager.Instance;
            if (manager == null || owner == null || homingAcquireRange <= 0f) return;

            Vector3 forward = dir.sqrMagnitude > 0.0001f ? dir.normalized : transform.forward;
            float minimumDot = Mathf.Cos(Mathf.Deg2Rad *
                Mathf.Clamp(homingAcquireHalfAngle, 5f, 85f));
            float bestScore = float.MaxValue;
            BrawlerController best = null;
            var brawlers = manager.GetBrawlers();
            for (int i = 0; i < brawlers.Count; i++)
            {
                BrawlerController candidate = brawlers[i];
                if (!IsValidHomingTarget(candidate)) continue;

                Vector3 toTarget = candidate.CombatAimPoint - transform.position;
                float distanceSq = toTarget.sqrMagnitude;
                if (distanceSq <= 0.0001f ||
                    distanceSq > homingAcquireRange * homingAcquireRange)
                    continue;
                float alignment = Vector3.Dot(forward, toTarget.normalized);
                if (alignment < minimumDot) continue;
                if (!CombatPhysics.HasLineOfSight(transform.position, candidate.CombatAimPoint))
                    continue;

                // Primarily prefer nearby targets, with a small bias toward the
                // center of the cast cone so a side target cannot steal a shot.
                float score = distanceSq * Mathf.Lerp(1.25f, 0.85f, alignment);
                if (score >= bestScore) continue;
                bestScore = score;
                best = candidate;
            }
            homingTarget = best;
        }

        bool IsValidHomingTarget(BrawlerController candidate)
        {
            if (candidate == null || candidate == owner || candidate.IsDead || owner == null ||
                candidate.team == owner.team || !MatchAllowsDamage())
                return false;
            float lockRange = Mathf.Max(6f, homingAcquireRange * 1.75f);
            return (candidate.CombatAimPoint - transform.position).sqrMagnitude <=
                   lockRange * lockRange;
        }

        bool TryFindFirstBrawlerHit(Vector3 origin, float distance,
            out BrawlerController target, out float hitDistance)
        {
            target = null;
            hitDistance = float.MaxValue;
            MatchManager manager = MatchManager.Instance;
            if (manager == null) return false;

            var brawlers = manager.GetBrawlers();
            for (int i = 0; i < brawlers.Count; i++)
            {
                BrawlerController candidate = brawlers[i];
                if (candidate == null || candidate.IsDead || candidate == owner) continue;
                if (owner != null && candidate.team == owner.team) continue;

                float combinedRadius = activeHitRadius + candidate.CombatHitRadius;
                if (!CombatPhysics.TryIntersectSegmentSphere(origin, dir, distance,
                        candidate.CombatAimPoint, combinedRadius, out float candidateDistance))
                    continue;
                if (candidateDistance >= hitDistance) continue;

                target = candidate;
                hitDistance = candidateDistance;
            }
            return target != null;
        }

        void Explode(Vector3 at)
        {
            if (!launched || !MatchAllowsDamage())
            {
                Despawn();
                return;
            }

            launched = false;
            if (blastRadius > 0f)
            {
                MatchManager manager = MatchManager.Instance;
                if (manager != null)
                {
                    Vector3 lineOfSightOrigin = at - dir * Mathf.Max(0.05f, activeHitRadius);
                    var brawlers = manager.GetBrawlers();
                    for (int i = 0; i < brawlers.Count; i++)
                    {
                        BrawlerController target = brawlers[i];
                        if (target == null || target.IsDead || target == owner) continue;
                        if (owner != null && target.team == owner.team) continue;

                        float reach = blastRadius + target.CombatHitRadius;
                        if ((target.CombatAimPoint - at).sqrMagnitude > reach * reach) continue;
                        if (!CombatPhysics.HasLineOfSight(lineOfSightOrigin, target.CombatAimPoint))
                            continue;

                        DamageTarget(target, at);
                        if (!MatchAllowsDamage()) break;
                    }
                }
            }

            if (owner != null && MatchAllowsDamage())
            {
                if (specialty.school == SpellSchool.Fire)
                    GroundSpellHazard.SpawnFire(owner, at, damage, specialty);
                else if (specialty.school == SpellSchool.Arcane && blastRadius > 0f)
                    owner.ApplyArcaneRitualHeal(at,
                        Mathf.Max(blastRadius, specialty.allyHealRadius));
            }

            if (MatchAllowsDamage() && impactVfx != null)
                BrawlerController.SpawnVfx(impactVfx, at, Quaternion.identity, 2.5f);
            if (MatchAllowsDamage() && secondaryImpactVfx != null &&
                secondaryImpactVfx != impactVfx)
                BrawlerController.SpawnVfx(secondaryImpactVfx, at, Quaternion.identity, 2.5f);
            Despawn();
        }

        void DamageTarget(BrawlerController target, Vector3 impactPoint)
        {
            if (target == null || !MatchAllowsDamage()) return;
            float applied = target.Health.TakeDamage(damage,
                owner != null ? owner.gameObject : gameObject);
            if (applied <= 0f || !MatchAllowsDamage()) return;

            if (knockback > 0f)
            {
                Vector3 direction = target.transform.position - impactPoint;
                if (direction.sqrMagnitude < 0.001f) direction = dir;
                target.ApplyKnockback(direction, knockback);
            }

            if (owner == null || !MatchAllowsDamage()) return;
            bool allowChain = !specialtyChainTriggered;
            if (allowChain && specialty.school == SpellSchool.Storm && specialty.chainTargets > 0)
                specialtyChainTriggered = true;
            owner.ApplySpellSpecialtyHit(target, applied, impactPoint, secondaryImpactVfx,
                allowChain);
        }
    }
}
