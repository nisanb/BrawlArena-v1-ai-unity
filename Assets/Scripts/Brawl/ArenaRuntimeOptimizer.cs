using System;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

namespace BrawlArena
{
    /// <summary>
    /// Runtime-only cleanup for the legacy Arena scene. It keeps the authored
    /// ground box used by movement/NavMesh, removes per-tile collision work,
    /// stops floor tiles from casting shadows onto themselves (they still
    /// receive shadows from characters/props — see ORDER R), and places solid
    /// arena geometry on the explicit combat layers. No scene or prefab asset
    /// is modified.
    /// </summary>
    public static class ArenaRuntimeOptimizer
    {
        public readonly struct Result
        {
            public readonly int FloorRenderersOptimized;
            public readonly int FloorMeshCollidersDisabled;
            public readonly int WorldBlockersAssigned;

            public Result(int floorRenderersOptimized, int floorMeshCollidersDisabled,
                int worldBlockersAssigned)
            {
                FloorRenderersOptimized = floorRenderersOptimized;
                FloorMeshCollidersDisabled = floorMeshCollidersDisabled;
                WorldBlockersAssigned = worldBlockersAssigned;
            }
        }

        public static Result LastResult { get; private set; }
        static int optimizedSceneHandle = -1;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetRuntimeState()
        {
            SceneManager.sceneLoaded -= OnSceneLoaded;
            LastResult = default;
            optimizedSceneHandle = -1;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void InstallSceneHook()
        {
            // Subtract first so Enter Play Mode configurations without a domain
            // reload cannot accumulate duplicate callbacks.
            SceneManager.sceneLoaded -= OnSceneLoaded;
            SceneManager.sceneLoaded += OnSceneLoaded;
        }

        static bool IsArenaScene(string sceneName)
        {
            return string.Equals(sceneName, "Arena", StringComparison.Ordinal) ||
                   string.Equals(sceneName, "ActionArena", StringComparison.Ordinal);
        }

        static void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            if (IsArenaScene(scene.name))
                TryOptimizeActiveArena(out _);
        }

        /// <summary>Returns false and performs no work when Arena is not active.</summary>
        public static bool TryOptimizeActiveArena(out Result result)
        {
            Scene scene = SceneManager.GetActiveScene();
            if (!scene.IsValid() || !scene.isLoaded || !IsArenaScene(scene.name))
            {
                result = default;
                return false;
            }
            if (optimizedSceneHandle == scene.handle)
            {
                result = LastResult;
                return true;
            }

            int groundLayer = CombatPhysics.GroundLayer;
            int blockerLayer = CombatPhysics.WorldBlockerLayer;
            int optimizedRenderers = 0;
            int disabledFloorColliders = 0;
            int assignedBlockers = 0;

            Renderer[] renderers = UnityEngine.Object.FindObjectsByType<Renderer>(
                FindObjectsInactive.Include, FindObjectsSortMode.None);
            for (int i = 0; i < renderers.Length; i++)
            {
                Renderer renderer = renderers[i];
                if (renderer == null || renderer.gameObject.scene != scene ||
                    !IsFloorTileName(renderer.gameObject.name))
                    continue;

                renderer.shadowCastingMode = ShadowCastingMode.Off;
                // Ground must receive shadows for depth cues (ORDER R / shadow
                // bug fix) — only casting is disabled here, an unrelated,
                // still-legitimate optimization (the floor never needs to
                // shadow itself or other tiles).
                if (groundLayer >= 0) renderer.gameObject.layer = groundLayer;
                optimizedRenderers++;
            }

            Collider[] colliders = UnityEngine.Object.FindObjectsByType<Collider>(
                FindObjectsInactive.Include, FindObjectsSortMode.None);
            for (int i = 0; i < colliders.Length; i++)
            {
                Collider collider = colliders[i];
                if (collider == null || collider.gameObject.scene != scene) continue;

                GameObject go = collider.gameObject;
                if (IsFloorTileName(go.name))
                {
                    if (groundLayer >= 0) go.layer = groundLayer;
                    if (collider is MeshCollider && collider.enabled)
                    {
                        collider.enabled = false;
                        disabledFloorColliders++;
                    }
                    continue;
                }

                // This is the single broad collider that replaces the hundreds
                // of tile MeshColliders. Preserve it and keep it out of LOS.
                if (string.Equals(go.name, "GroundCollider", StringComparison.Ordinal))
                {
                    if (groundLayer >= 0) go.layer = groundLayer;
                    continue;
                }

                if (!collider.enabled || collider.isTrigger) continue;
                if (collider.GetComponentInParent<BrawlerController>() != null) continue;
                if (collider.GetComponentInParent<Projectile>() != null) continue;

                // Every remaining solid collider in Arena is navigation/world
                // geometry. Combat casts can now use one explicit layer rather
                // than an all-layer buffer whose contents depend on tile count.
                if (blockerLayer >= 0 && go.layer != blockerLayer)
                {
                    go.layer = blockerLayer;
                    assignedBlockers++;
                }
            }

            result = new Result(optimizedRenderers, disabledFloorColliders, assignedBlockers);
            LastResult = result;
            optimizedSceneHandle = scene.handle;
            Debug.Log($"ArenaRuntimeOptimizer: floor renderers {optimizedRenderers}, " +
                      $"tile colliders disabled {disabledFloorColliders}, " +
                      $"world blockers assigned {assignedBlockers}.");
            return true;
        }

        public static bool IsFloorTileName(string objectName)
        {
            return string.Equals(objectName, "Floor1", StringComparison.Ordinal) ||
                   string.Equals(objectName, "Floor2", StringComparison.Ordinal) ||
                   (objectName != null &&
                    (objectName.StartsWith("Floor1 (", StringComparison.Ordinal) ||
                     objectName.StartsWith("Floor2 (", StringComparison.Ordinal)));
        }
    }
}
