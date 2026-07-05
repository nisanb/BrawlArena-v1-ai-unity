using UnityEngine;
using UnityEngine.AI;

namespace BrawlArena
{
    /// <summary>
    /// NavMeshAgent-driven brawler brain used for both AI teammates and enemies.
    /// Melee units chase with a small flank offset; ranged units hold a firing
    /// band and kite. Everyone retreats briefly when badly hurt.
    /// </summary>
    [RequireComponent(typeof(BrawlerController))]
    [RequireComponent(typeof(NavMeshAgent))]
    public class AIBrawler : MonoBehaviour
    {
        public float thinkInterval = 0.25f;
        [Tooltip("0 = derive automatically from weapon type.")]
        public float preferredRange = 0f;
        public float retreatBelowPct = 0.28f;
        public float resumeAbovePct = 0.5f;

        BrawlerController self;
        NavMeshAgent agent;
        BrawlerController target;
        float nextThink;
        float attackReadyAt;
        bool retreating;
        float strafeSign;

        bool Ranged => self.projectilePrefab != null;

        void Awake()
        {
            self = GetComponent<BrawlerController>();
            agent = GetComponent<NavMeshAgent>();
            strafeSign = Random.value < 0.5f ? -1f : 1f;
        }

        void Start()
        {
            agent.speed = self.moveSpeed;
            agent.acceleration = 40f;
            agent.angularSpeed = 720f;
            agent.stoppingDistance = Ranged ? 0.5f : Mathf.Max(0.5f, self.attackRange * 0.65f);
            if (preferredRange <= 0f)
                preferredRange = Ranged ? 7.5f : self.attackRange * 0.8f;
            nextThink = Time.time + Random.Range(0f, thinkInterval);
        }

        void Update()
        {
            if (!self.CanAct)
            {
                if (agent.enabled && agent.isOnNavMesh && agent.hasPath) agent.ResetPath();
                return;
            }

            if (self.MovementLocked)
            {
                // Hold position while the attack swing plays, like the player does.
                if (agent.enabled && agent.isOnNavMesh && agent.hasPath) agent.ResetPath();
            }
            else if (Time.time >= nextThink)
            {
                Think();
                nextThink = Time.time + thinkInterval;
            }

            bool engaging = false;
            if (target != null && !target.IsDead)
            {
                float dist = PlanarDistance(transform.position, target.transform.position);
                float engageRange = Ranged ? preferredRange + 1.5f : self.attackRange + 0.4f;
                if (dist <= engageRange)
                {
                    engaging = true;
                    FaceTarget();
                    if (!retreating && Time.time >= attackReadyAt && self.TryAttack(target))
                        attackReadyAt = Time.time + self.attackCooldown + Random.Range(0.05f, 0.35f);
                }
            }
            agent.updateRotation = !engaging;
        }

        void Think()
        {
            target = PickTarget();

            float hpPct = self.Health.Current / Mathf.Max(1f, self.Health.Max);
            if (retreating && hpPct >= resumeAbovePct) retreating = false;
            else if (!retreating && hpPct <= retreatBelowPct) retreating = true;

            if (!agent.enabled || !agent.isOnNavMesh) return;
            if (target == null)
            {
                if (agent.hasPath) agent.ResetPath();
                return;
            }

            Vector3 myPos = transform.position;
            Vector3 tPos = target.transform.position;
            Vector3 away = myPos - tPos;
            away.y = 0f;
            away = away.sqrMagnitude > 0.01f ? away.normalized : -transform.forward;

            Vector3 dest;
            if (retreating)
            {
                dest = myPos + away * 6f;
            }
            else if (Ranged)
            {
                float dist = PlanarDistance(myPos, tPos);
                if (dist < preferredRange * 0.6f) dest = myPos + away * 4f;
                else if (dist > preferredRange * 1.15f) dest = tPos + away * preferredRange;
                else dest = myPos + Vector3.Cross(Vector3.up, away) * (strafeSign * 2.5f);
            }
            else
            {
                // Approach with a small flank offset so allies don't stack up.
                Vector3 flank = Vector3.Cross(Vector3.up, away) * (strafeSign * 1.2f);
                dest = tPos + flank;
            }

            if (NavMesh.SamplePosition(dest, out NavMeshHit hit, 3f, NavMesh.AllAreas))
                dest = hit.position;
            agent.SetDestination(dest);
        }

        BrawlerController PickTarget()
        {
            if (MatchManager.Instance == null) return null;
            BrawlerController best = null;
            float bestScore = float.MinValue;
            foreach (var b in MatchManager.Instance.GetBrawlers())
            {
                if (b == null || b == self || b.team == self.team || b.IsDead) continue;
                float d = PlanarDistance(transform.position, b.transform.position);
                float score = -d;
                score += (1f - b.Health.Current / Mathf.Max(1f, b.Health.Max)) * 3f;
                if (b.IsPlayer) score += 0.5f;
                if (score > bestScore)
                {
                    bestScore = score;
                    best = b;
                }
            }
            return best;
        }

        void FaceTarget()
        {
            Vector3 dir = target.transform.position - transform.position;
            dir.y = 0f;
            if (dir.sqrMagnitude < 0.01f) return;
            Quaternion look = Quaternion.LookRotation(dir.normalized, Vector3.up);
            transform.rotation = Quaternion.Slerp(transform.rotation, look, 12f * Time.deltaTime);
        }

        static float PlanarDistance(Vector3 a, Vector3 b)
        {
            a.y = 0f;
            b.y = 0f;
            return Vector3.Distance(a, b);
        }
    }
}
