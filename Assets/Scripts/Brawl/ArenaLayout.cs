using System;
using UnityEngine;

namespace BrawlArena
{
    /// <summary>
    /// Which competitive layout the current scene uses. Classic is the
    /// original 40m brawl arena; Action is the 2x-linear action-MMORPG
    /// experiment map (ActionArena.unity). Selected by ArenaProfileMarker at
    /// runtime and by the scene builders during generation.
    /// </summary>
    public enum ArenaProfile
    {
        Classic,
        Action,
    }

    /// <summary>
    /// Shared competitive-layout contract. The editor scene builder, runtime
    /// minimap, AI and match setup all use these values so a regenerated arena
    /// cannot silently drift away from the gameplay coordinate system.
    /// </summary>
    public static class ArenaLayout
    {
        /// <summary>
        /// Active layout profile. Defaults to Classic; ActionArena.unity's
        /// ArenaProfileMarker switches it for that scene's lifetime.
        /// </summary>
        public static ArenaProfile Profile = ArenaProfile.Classic;

        static bool Action => Profile == ArenaProfile.Action;

        public const int TeamSize = 5;
        public const int ControlZoneTeamSize = ControlZoneRules.TeamSize;

        /// <summary>Authoritative wall center on both horizontal axes.</summary>
        public static float PlayableHalfExtent => Action ? 80f : 40f;

        /// <summary>Floor coverage beyond the walls, used by generation and the broad ground collider.</summary>
        public static float GroundHalfExtent => Action ? 88f : 44f;
        public static float GroundSize => GroundHalfExtent * 2f;

        /// <summary>
        /// The minimap includes the decorative cliff ring outside the playable
        /// wall, so this is intentionally larger than PlayableHalfExtent.
        /// </summary>
        public static float MinimapHalfExtent => Action ? 100f : 52f;

        public const float FloorTileScale = 2f;
        public static float CliffMinRadius => Action ? 86f : 43f;
        public const float CliffRadiusJitter = 3f;
        public static float GateDepth => Action ? 81f : 40.5f;
        public static float TeamHomeDepth => Action ? 60f : 30f;
        public static float TeamHomeHalfWidth => Action ? 20f : 12f;

        // The first three slots are the primary 3v3 formation: centered and
        // closer to the objective. Slots three/four retain the secondary 5v5
        // modes without making a Control Zone respawn choose a remote flank.
        static readonly float[] ClassicSpawnX = { -7f, 0f, 7f, -12f, 12f };
        static readonly float[] ClassicSpawnDepth = { 26f, 25f, 26f, 32f, 32f };
        // Action spawns sit ~10s travel from the center zone at base move speed.
        static readonly float[] ActionSpawnX = { -9f, 0f, 9f, -16f, 16f };
        static readonly float[] ActionSpawnDepth = { 48f, 46f, 48f, 58f, 58f };

        public static int ActiveTeamSize(GameMode mode)
        {
            return mode == GameMode.ControlZone ? ControlZoneTeamSize : TeamSize;
        }

        public static Vector3 SpawnPosition(TeamId team, int slot)
        {
            if (slot < 0 || slot >= TeamSize)
                throw new ArgumentOutOfRangeException(nameof(slot));

            float side = team == TeamId.Blue ? -1f : 1f;
            float[] spawnX = Action ? ActionSpawnX : ClassicSpawnX;
            float[] spawnDepth = Action ? ActionSpawnDepth : ClassicSpawnDepth;
            return new Vector3(spawnX[slot], 0f, spawnDepth[slot] * side);
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
