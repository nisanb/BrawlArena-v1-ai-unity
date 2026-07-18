using UnityEngine;

namespace BrawlArena
{
    /// <summary>
    /// Centralizes the named physics-layer contract used by combat. Brawler
    /// candidates are selected from MatchManager's roster; physics is reserved
    /// for the small, explicit Ground/WorldBlocker masks.
    /// </summary>
    public static class CombatPhysics
    {
        public const string GroundLayerName = "Ground";
        public const string WorldBlockerLayerName = "WorldBlocker";
        public const string BrawlerHitboxLayerName = "BrawlerHitbox";
        public const string ProjectileLayerName = "Projectile";
        public const string VfxLayerName = "VFX";

        public static int GroundLayer => LayerMask.NameToLayer(GroundLayerName);
        public static int WorldBlockerLayer => LayerMask.NameToLayer(WorldBlockerLayerName);
        public static int BrawlerHitboxLayer => LayerMask.NameToLayer(BrawlerHitboxLayerName);
        public static int ProjectileLayer => LayerMask.NameToLayer(ProjectileLayerName);
        public static int VfxLayer => LayerMask.NameToLayer(VfxLayerName);

        public static int WorldBlockerMask => MaskForLayer(WorldBlockerLayer);
        public static int ProjectileCollisionMask =>
            MaskForLayer(WorldBlockerLayer) | MaskForLayer(GroundLayer);

        static int MaskForLayer(int layer)
        {
            return layer >= 0 ? 1 << layer : 0;
        }

        /// <summary>True when no WorldBlocker lies between the two combat points.</summary>
        public static bool HasLineOfSight(Vector3 from, Vector3 to)
        {
            Vector3 delta = to - from;
            float distance = delta.magnitude;
            if (distance <= 0.001f) return true;

            int mask = WorldBlockerMask;
            // The fallback keeps test/dev scenes usable before the named layer
            // contract is installed. ArenaRuntimeOptimizer installs that contract
            // for the production Arena, so gameplay uses the fast single cast.
            if (mask == 0) return HasFallbackLineOfSight(from, delta / distance, distance);
            return !Physics.Raycast(from, delta / distance, distance, mask, QueryTriggerInteraction.Ignore);
        }

        static bool HasFallbackLineOfSight(Vector3 from, Vector3 direction, float distance)
        {
            RaycastHit[] hits = Physics.RaycastAll(from, direction, distance,
                Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
            for (int i = 0; i < hits.Length; i++)
            {
                Collider collider = hits[i].collider;
                if (collider == null) continue;
                if (collider.GetComponentInParent<BrawlerController>() != null) continue;
                if (collider.GetComponentInParent<Projectile>() != null) continue;
                return false;
            }
            return true;
        }

        /// <summary>
        /// Sweeps only the explicitly named world layers. Returns the nearest
        /// blocking point, including the case where the sphere starts embedded.
        /// </summary>
        public static bool SweepWorld(Vector3 origin, float radius, Vector3 direction,
            float distance, bool includeGround, out RaycastHit hit)
        {
            hit = default;
            if (distance <= 0f || direction.sqrMagnitude <= 0.0001f) return false;

            int mask = WorldBlockerMask;
            if (includeGround) mask |= MaskForLayer(GroundLayer);
            if (mask == 0) mask = Physics.DefaultRaycastLayers;

            direction.Normalize();
            radius = Mathf.Max(0.01f, radius);
            if (Physics.CheckSphere(origin, radius, mask, QueryTriggerInteraction.Ignore))
            {
                hit.point = origin;
                hit.distance = 0f;
                return true;
            }

            return Physics.SphereCast(origin, radius, direction, out hit, distance, mask,
                QueryTriggerInteraction.Ignore);
        }

        /// <summary>Tests a point against a capsule without querying the physics world.</summary>
        public static bool PointInsideCapsule(Vector3 point, Vector3 start, Vector3 end, float radius)
        {
            Vector3 segment = end - start;
            float lengthSq = segment.sqrMagnitude;
            float t = lengthSq > 0.0001f
                ? Mathf.Clamp01(Vector3.Dot(point - start, segment) / lengthSq)
                : 0f;
            Vector3 closest = start + segment * t;
            return (point - closest).sqrMagnitude <= radius * radius;
        }

        /// <summary>
        /// Finds the first distance at which a ray segment enters a sphere.
        /// Used for deterministic projectile and dash contact against the roster.
        /// </summary>
        public static bool TryIntersectSegmentSphere(Vector3 origin, Vector3 direction,
            float distance, Vector3 center, float radius, out float hitDistance)
        {
            hitDistance = 0f;
            if (distance < 0f || direction.sqrMagnitude <= 0.0001f) return false;
            direction.Normalize();
            radius = Mathf.Max(0.01f, radius);

            Vector3 offset = origin - center;
            float c = Vector3.Dot(offset, offset) - radius * radius;
            if (c <= 0f) return true;

            float b = Vector3.Dot(offset, direction);
            if (b > 0f) return false;
            float discriminant = b * b - c;
            if (discriminant < 0f) return false;

            hitDistance = -b - Mathf.Sqrt(discriminant);
            return hitDistance >= 0f && hitDistance <= distance;
        }

        /// <summary>
        /// True when the target sits within the given full arc (degrees),
        /// centered on the committed planar direction. Melee basic attacks use
        /// this to gate candidates before picking the single best target.
        /// </summary>
        public static bool WithinMeleeArc(Vector3 origin, Vector3 committedDir,
            Vector3 targetPos, float arcDegrees)
        {
            committedDir.y = 0f;
            if (committedDir.sqrMagnitude <= 0.0001f) return true;

            Vector3 toTarget = targetPos - origin;
            toTarget.y = 0f;
            if (toTarget.sqrMagnitude <= 0.0001f) return true;

            float halfArc = Mathf.Max(0f, arcDegrees) * 0.5f;
            float angle = Vector3.Angle(committedDir.normalized, toTarget.normalized);
            return angle <= halfArc;
        }

        public static void SetLayerRecursively(GameObject root, int layer)
        {
            if (root == null || layer < 0) return;
            Transform[] transforms = root.GetComponentsInChildren<Transform>(true);
            for (int i = 0; i < transforms.Length; i++)
                transforms[i].gameObject.layer = layer;
        }
    }
}
