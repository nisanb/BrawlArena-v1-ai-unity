using System.Collections.Generic;
using UnityEngine;

namespace BrawlArena
{
    /// <summary>
    /// Planar-circle concealment zone authored by the scene builder under a
    /// tall-grass cluster. Deliberately not a physics trigger: character rigs
    /// carry many colliders, and a point-in-circle test over a handful of
    /// patches per brawler per frame is cheaper and deterministic.
    /// </summary>
    public class GrassPatchVolume : MonoBehaviour
    {
        [Min(0.5f)] public float radius = 6f;

        static readonly List<GrassPatchVolume> Registry = new List<GrassPatchVolume>();

        void OnEnable()
        {
            Registry.Add(this);
        }

        void OnDisable()
        {
            Registry.Remove(this);
        }

        public bool Contains(Vector3 worldPosition)
        {
            Vector3 delta = worldPosition - transform.position;
            delta.y = 0f;
            return delta.sqrMagnitude <= radius * radius;
        }

        /// <summary>The patch containing the planar position, or null.</summary>
        public static GrassPatchVolume PatchAt(Vector3 worldPosition)
        {
            for (int i = 0; i < Registry.Count; i++)
                if (Registry[i].Contains(worldPosition))
                    return Registry[i];
            return null;
        }

        public static int ActiveCount => Registry.Count;
    }
}
