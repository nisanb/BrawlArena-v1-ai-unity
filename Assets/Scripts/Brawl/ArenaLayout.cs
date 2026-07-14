using System;
using UnityEngine;

namespace BrawlArena
{
    /// <summary>
    /// Shared competitive-layout contract. The editor scene builder, runtime
    /// minimap, AI and match setup all use these values so a regenerated arena
    /// cannot silently drift away from the gameplay coordinate system.
    /// </summary>
    public static class ArenaLayout
    {
        public const int TeamSize = 5;

        /// <summary>Authoritative wall center on both horizontal axes.</summary>
        public const float PlayableHalfExtent = 40f;

        /// <summary>Floor coverage beyond the walls, used by generation and the broad ground collider.</summary>
        public const float GroundHalfExtent = 44f;
        public const float GroundSize = GroundHalfExtent * 2f;

        /// <summary>
        /// The minimap includes the decorative cliff ring outside the playable
        /// wall, so this is intentionally larger than PlayableHalfExtent.
        /// </summary>
        public const float MinimapHalfExtent = 52f;

        public const float FloorTileScale = 2f;
        public const float CliffMinRadius = 43f;
        public const float CliffRadiusJitter = 3f;
        public const float GateDepth = 40.5f;
        public const float TeamHomeDepth = 30f;
        public const float TeamHomeHalfWidth = 12f;

        static readonly float[] SpawnX = { -12f, -6f, 0f, 6f, 12f };
        static readonly float[] SpawnDepth = { 32f, 33f, 34f, 33f, 32f };

        public static Vector3 SpawnPosition(TeamId team, int slot)
        {
            if (slot < 0 || slot >= TeamSize)
                throw new ArgumentOutOfRangeException(nameof(slot));

            float side = team == TeamId.Blue ? -1f : 1f;
            return new Vector3(SpawnX[slot], 0f, SpawnDepth[slot] * side);
        }

        /// <summary>Safe fallback point used by Gem Grab carriers protecting a lead.</summary>
        public static Vector3 TeamHomePosition(TeamId team, float currentX)
        {
            float side = team == TeamId.Blue ? -1f : 1f;
            float x = Mathf.Clamp(currentX * 0.5f, -TeamHomeHalfWidth, TeamHomeHalfWidth);
            return new Vector3(x, 0f, TeamHomeDepth * side);
        }
    }
}
