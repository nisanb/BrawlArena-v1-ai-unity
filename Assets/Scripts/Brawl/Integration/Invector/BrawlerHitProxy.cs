using System;
using UnityEngine;

namespace BrawlArena
{
    /// <summary>
    /// Inert marker for the one project-owned combat-selection trigger on an
    /// Invector actor. It has no physics callbacks and never changes children.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(SphereCollider))]
    public sealed class BrawlerHitProxy : MonoBehaviour
    {
        public SphereCollider TriggerCollider => GetComponent<SphereCollider>();

        public bool IsConfigured
        {
            get
            {
                Collider[] colliders = GetComponents<Collider>();
                return colliders.Length == 1 && colliders[0] is SphereCollider sphere &&
                       sphere.isTrigger &&
                       gameObject.layer == CombatPhysics.BrawlerHitboxLayer;
            }
        }

        public void Configure(Vector3 center, float radius)
        {
            if (!IsFinite(center))
                throw new ArgumentException("Hit-proxy center must be finite.", nameof(center));
            if (!IsFinite(radius) || radius <= 0f)
                throw new ArgumentOutOfRangeException(nameof(radius));

            Collider[] colliders = GetComponents<Collider>();
            if (colliders.Length != 1 || !(colliders[0] is SphereCollider sphere))
            {
                throw new InvalidOperationException(
                    "BrawlerHitProxy requires exactly one SphereCollider.");
            }

            int hitboxLayer = CombatPhysics.BrawlerHitboxLayer;
            if (hitboxLayer < 0)
            {
                throw new InvalidOperationException(
                    "The BrawlerHitbox project layer must exist before configuring a proxy.");
            }

            sphere.center = center;
            sphere.radius = radius;
            sphere.isTrigger = true;
            gameObject.layer = hitboxLayer;
        }

        static bool IsFinite(Vector3 value)
        {
            return IsFinite(value.x) && IsFinite(value.y) && IsFinite(value.z);
        }

        static bool IsFinite(float value)
        {
            return !float.IsNaN(value) && !float.IsInfinity(value);
        }
    }
}
