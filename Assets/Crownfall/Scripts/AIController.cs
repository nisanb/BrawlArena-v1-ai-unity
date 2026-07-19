using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

namespace Crownfall
{
    /// Combat brain. Uses a NavMeshAgent purely as a path planner; all actual
    /// movement flows through the CombatMotor so AI and player share one feel.
    [RequireComponent(typeof(CombatMotor))]
    [DefaultExecutionOrder(-5)] // drivers act before the motor consumes buffers
    public class AIController : MonoBehaviour
    {
        [Range(0f, 1f)] public float aggression = 0.6f;
        public float reactionTime = 0.22f;

        CombatMotor motor;
        NavMeshAgent agent;

        CombatMotor target;
        float retargetAt;
        float nextAttackAt;
        float nextDodgeAllowedAt;
        float strafeSign = 1f;
        float strafeFlipAt;
        float burstEndsAt = -1f;
        float nextBurstShotAt;
        bool lowStamina;
        float blockUntil = -1f;
        float novaReadyAt;
        float repositionUntil = -1f;
        Vector3 repositionPoint;

        static readonly List<CombatMotor> scratch = new List<CombatMotor>();

        void Awake()
        {
            motor = GetComponent<CombatMotor>();
            agent = GetComponent<NavMeshAgent>();
            if (agent != null)
            {
                agent.updatePosition = false;
                agent.updateRotation = false;
                agent.autoBraking = false;
            }
            // small personality jitter so the three AI don't move in sync
            aggression = Mathf.Clamp01(aggression + Random.Range(-0.12f, 0.12f));
            strafeFlipAt = Time.time + Random.Range(1.5f, 3.5f);
        }

        void Update()
        {
            var mm = MatchManager.I;
            if (mm == null || mm.State != MatchState.Fighting || motor.IsDead)
            {
                motor.SetMoveInput(Vector3.zero, false);
                motor.SetBlock(false);
                return;
            }

            if (agent != null && agent.isOnNavMesh) agent.nextPosition = transform.position;

            if (target == null || target.IsDead || Time.time >= retargetAt)
                PickTarget(mm);

            if (target == null)
            {
                motor.SetMoveInput(Vector3.zero, false);
                return;
            }

            motor.LockTarget = target;
            lowStamina = motor.Stamina.Current < (lowStamina ? 45f : 22f);

            if (motor.Kit.isRanged) MageBrain();
            else MeleeBrain();
        }

        void PickTarget(MatchManager mm)
        {
            retargetAt = Time.time + Random.Range(1.2f, 2f);
            scratch.Clear();
            scratch.AddRange(mm.AliveEnemiesOf(motor.Identity.team));
            if (scratch.Count == 0) { target = null; return; }

            CombatMotor best = null;
            float bestScore = float.MinValue;
            foreach (var cand in scratch)
            {
                float dist = (cand.transform.position - transform.position).magnitude;
                float score = -dist;
                score -= mm.CountTargeting(cand, this) * 3.5f;          // spread targets
                if (cand.Health.Current < cand.Health.Max * 0.35f) score += 3f; // finish low hp
                if (cand.Identity.isPlayer) score += aggression * 1.5f; // slight player focus
                if (score > bestScore) { bestScore = score; best = cand; }
            }
            target = best;
        }

        // ------------------------------------------------------------------ melee

        void MeleeBrain()
        {
            Vector3 toTarget = target.transform.position - transform.position;
            toTarget.y = 0f;
            float dist = toTarget.magnitude;
            float engage = motor.Kit.attackRange + 0.75f;

            TryDefensiveReaction(dist);

            if (motor.IsBlockingHeld && Time.time > blockUntil) motor.SetBlock(false);

            // stagger punish: rush and heavy
            if (target.State == MotorState.Staggered && dist < engage + 1.6f)
            {
                motor.SetBlock(false);
                MoveTowards(target.transform.position, dist > engage, 1f);
                if (dist <= engage + 0.5f) motor.RequestHeavy();
                return;
            }

            if (lowStamina)
            {
                // give ground while stamina returns
                Vector3 away = dist > 0.01f ? -toTarget.normalized : -transform.forward;
                motor.SetMoveInput((away + StrafeDir(toTarget) * 0.5f).normalized * 0.85f, false);
                return;
            }

            if (dist > engage + 2.5f)
            {
                MoveTowards(target.transform.position, dist > 6.5f, 1f);
                return;
            }

            if (dist > engage)
            {
                MoveTowards(target.transform.position, false, 0.9f);
                MaybeStartBurst(dist, engage);
                return;
            }

            // in range: orbit, spacing, attack in bursts
            if (Time.time >= strafeFlipAt)
            {
                strafeSign = -strafeSign;
                strafeFlipAt = Time.time + Random.Range(1.6f, 3.4f);
            }

            Vector3 strafe = StrafeDir(toTarget) * 0.62f;
            Vector3 spacing = Vector3.zero;
            if (dist < engage - 0.9f) spacing = -toTarget.normalized * 0.5f;
            motor.SetMoveInput(Vector3.ClampMagnitude(strafe + spacing, 1f) * 0.72f, false);

            MaybeStartBurst(dist, engage);
        }

        Vector3 StrafeDir(Vector3 toTarget)
        {
            return Vector3.Cross(Vector3.up, toTarget.normalized) * strafeSign;
        }

        void MaybeStartBurst(float dist, float engage)
        {
            if (Time.time >= burstEndsAt && Time.time >= nextAttackAt && dist <= engage + 0.6f)
            {
                int swings = Random.value < 0.35f + aggression * 0.3f ? Random.Range(2, 4) : 1;
                bool heavy = Random.value < 0.22f + (target.State == MotorState.Attacking ? 0.15f : 0f);
                if (heavy) swings = 1;
                burstEndsAt = Time.time + swings * 0.75f;
                nextBurstShotAt = Time.time;
                nextAttackAt = burstEndsAt + Mathf.Lerp(2.6f, 1.1f, aggression) + Random.Range(0f, 0.7f);
                if (heavy) motor.RequestHeavy();
            }

            if (Time.time < burstEndsAt && Time.time >= nextBurstShotAt)
            {
                motor.RequestLight();
                nextBurstShotAt = Time.time + 0.45f;
            }
        }

        void TryDefensiveReaction(float dist)
        {
            if (Time.time < nextDodgeAllowedAt) return;
            bool threat = target.State == MotorState.Attacking && dist < 3.4f &&
                          Vector3.Angle(target.transform.forward, transform.position - target.transform.position) < 55f;
            if (!threat) return;

            nextDodgeAllowedAt = Time.time + Random.Range(2.2f, 3.6f);
            float roll = Random.value;
            float dodgeChance = 0.38f + aggression * 0.25f;

            if (motor.Kit.canBlock && roll > dodgeChance && roll < dodgeChance + 0.4f)
            {
                motor.SetBlock(true);
                blockUntil = Time.time + Random.Range(0.8f, 1.6f);
            }
            else if (roll < dodgeChance)
            {
                Invoke(nameof(DoDodge), reactionTime + Random.Range(0f, 0.1f));
            }
        }

        void DoDodge()
        {
            if (motor.IsDead || target == null) return;
            Vector3 toTarget = target.transform.position - transform.position;
            toTarget.y = 0f;
            Vector3 dir = (StrafeDir(toTarget) + -toTarget.normalized * 0.4f).normalized;
            motor.RequestRoll(dir);
        }

        // ------------------------------------------------------------------ mage

        void MageBrain()
        {
            Vector3 toTarget = target.transform.position - transform.position;
            toTarget.y = 0f;
            float dist = toTarget.magnitude;

            TryDefensiveReaction(dist);

            // panic nova when swarmed
            if (dist < 3.1f && Time.time >= novaReadyAt && motor.Stamina.CanAfford(motor.Kit.staminaHeavy))
            {
                novaReadyAt = Time.time + 9f;
                motor.RequestHeavy();
                return;
            }

            if (dist < 6.5f)
            {
                // kite away, slight arc
                Vector3 away = (-toTarget.normalized + StrafeDir(toTarget) * 0.45f).normalized;
                motor.SetMoveInput(away, dist < 4f);
                return;
            }

            if (dist > 14f)
            {
                MoveTowards(target.transform.position, true, 1f);
                return;
            }

            // hold range band and cast
            if (Time.time >= repositionUntil)
            {
                repositionUntil = Time.time + Random.Range(1.4f, 2.4f);
                repositionPoint = transform.position + StrafeDir(toTarget) * Random.Range(1.5f, 3f);
            }
            Vector3 drift = repositionPoint - transform.position;
            drift.y = 0f;
            motor.SetMoveInput(drift.sqrMagnitude > 0.4f ? drift.normalized * 0.55f : Vector3.zero, false);

            bool los = !Physics.Linecast(motor.AimPoint, target.AimPoint,
                Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
            if (los && Time.time >= nextAttackAt)
            {
                nextAttackAt = Time.time + Mathf.Lerp(3f, 1.9f, aggression) + Random.Range(0f, 0.5f);
                motor.RequestLight();
            }
            else if (!los)
            {
                MoveTowards(target.transform.position, false, 0.8f);
            }
        }

        // ------------------------------------------------------------------ pathing

        void MoveTowards(Vector3 dest, bool sprint, float speedScale)
        {
            Vector3 dir;
            if (agent != null && agent.isOnNavMesh)
            {
                agent.SetDestination(dest);
                Vector3 steer = agent.steeringTarget - transform.position;
                steer.y = 0f;
                dir = steer.sqrMagnitude > 0.05f ? steer.normalized
                    : (dest - transform.position).normalized;
            }
            else
            {
                dir = dest - transform.position;
                dir.y = 0f;
                dir = dir.sqrMagnitude > 0.05f ? dir.normalized : Vector3.zero;
            }
            motor.SetMoveInput(dir * speedScale, sprint);
        }

        public CombatMotor CurrentTarget => target;
    }
}
