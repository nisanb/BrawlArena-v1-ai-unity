using UnityEngine;

namespace BrawlArena
{
    /// <summary>
    /// Straight-flying projectile. Visuals come from whatever prefab this is
    /// attached to (particle trails etc.); this component only handles motion
    /// and hit detection. Uses non-allocating sphere casts, plus an overlap
    /// check each frame because SphereCast skips colliders that already
    /// overlap the cast origin (point-blank shots).
    /// </summary>
    public class Projectile : MonoBehaviour
    {
        public float lifeTime = 3f;
        public float hitRadius = 0.3f;

        static readonly Collider[] OverlapBuffer = new Collider[16];
        static readonly RaycastHit[] CastBuffer = new RaycastHit[16];

        BrawlerController owner;
        Vector3 dir;
        float damage;
        float speed;
        GameObject impactVfx;
        float dieAt;
        bool launched;

        public void Launch(BrawlerController owner, Vector3 direction, float damage, float speed, GameObject impactVfx)
        {
            this.owner = owner;
            this.damage = damage;
            this.speed = speed;
            this.impactVfx = impactVfx;
            dir = direction.sqrMagnitude > 0.001f ? direction.normalized : Vector3.forward;
            dieAt = Time.time + lifeTime;
            launched = true;
            transform.rotation = Quaternion.LookRotation(dir);
        }

        void Update()
        {
            if (!launched) return;

            float step = speed * Time.deltaTime;
            Vector3 pos = transform.position;

            // 1) Colliders already overlapping the origin are invisible to SphereCast.
            int overlapCount = Physics.OverlapSphereNonAlloc(pos, hitRadius, OverlapBuffer, ~0, QueryTriggerInteraction.Ignore);
            for (int i = 0; i < overlapCount; i++)
            {
                if (HandleCollider(OverlapBuffer[i], pos)) return;
            }

            // 2) Sweep forward and take the nearest collider that actually blocks.
            int hitCount = Physics.SphereCastNonAlloc(pos, hitRadius, dir, CastBuffer, step + 0.05f, ~0, QueryTriggerInteraction.Ignore);
            int best = -1;
            float bestDist = float.MaxValue;
            for (int i = 0; i < hitCount; i++)
            {
                if (!Blocks(CastBuffer[i].collider)) continue;
                if (CastBuffer[i].distance < bestDist)
                {
                    bestDist = CastBuffer[i].distance;
                    best = i;
                }
            }
            if (best >= 0)
            {
                HandleCollider(CastBuffer[best].collider, CastBuffer[best].distance > 0f ? CastBuffer[best].point : pos);
                return;
            }

            transform.position = pos + dir * step;
            if (Time.time >= dieAt) Explode(transform.position);
        }

        bool Blocks(Collider col)
        {
            var brawler = col.GetComponentInParent<BrawlerController>();
            if (brawler != null)
            {
                if (owner != null && (brawler == owner || brawler.team == owner.team)) return false;
                return !brawler.IsDead;
            }
            return col.GetComponentInParent<Projectile>() == null;
        }

        /// <summary>Damages/explodes on a blocking collider. True if the projectile ended.</summary>
        bool HandleCollider(Collider col, Vector3 at)
        {
            if (!Blocks(col)) return false;
            var brawler = col.GetComponentInParent<BrawlerController>();
            if (brawler != null)
                brawler.Health.TakeDamage(damage, owner != null ? owner.gameObject : gameObject);
            Explode(at);
            return true;
        }

        void Explode(Vector3 at)
        {
            if (impactVfx != null) BrawlerController.SpawnVfx(impactVfx, at, Quaternion.identity, 2.5f);
            Destroy(gameObject);
        }
    }
}
