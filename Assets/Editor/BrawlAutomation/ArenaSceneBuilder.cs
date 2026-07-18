using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Unity.AI.Navigation;
using UnityEditor;
using UnityEditor.Rendering.Universal;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace BrawlArena.EditorAutomation
{
    /// <summary>
    /// Deterministic construction of the 5v5 gameplay scene
    /// (Assets/Scenes/Arena.unity) from the imported asset packs, plus the
    /// project-wide URP material conversion and scene-view capture helpers.
    /// </summary>
    public static class ArenaSceneBuilder
    {
        const string ScenePath = "Assets/Scenes/Arena.unity";
        const string Heroes = "Assets/ModularRPGHeroesPBR/Prefabs/BasicCharacters/";
        const string Weapons = "Assets/ModularRPGHeroesPBR/Prefabs/Weapons/";
        const string Arena = "Assets/Battle Arena - Cartoon Assets/Prefabs/";
        const string Magic = "Assets/MagicArsenal/Effects/Prefabs/";
        const string MagicSound = "Assets/MagicArsenal/Effects/Sound/";
        const string KriptoParts = "Assets/KriptoFX/Realistic Effects Pack v4/Effects/Prefabs/EffectParts/";

        static readonly StringBuilder Report = new StringBuilder();

        // ---------------- URP material conversion ----------------

        public static string ConvertMaterialsToUrp()
        {
            // Converters.RunInBatchMode is broken in URP 17.3 (Base2DMaterialUpgrader
            // has no default ctor), so drive the public MaterialUpgrader API per
            // material instead â€” silent, no dialogs.
            int legacyFixed = FixLegacyParticleMaterials();

            var upgraders = new Dictionary<string, UnityEditor.Rendering.MaterialUpgrader>
            {
                { "Standard", new StandardUpgrader("Standard") },
                { "Standard (Specular setup)", new StandardUpgrader("Standard (Specular setup)") },
                { "Particles/Standard Unlit", new ParticleUpgrader("Particles/Standard Unlit") },
                { "Particles/Standard Surface", new ParticleUpgrader("Particles/Standard Surface") },
            };

            int upgraded = 0, legacyLit = 0;
            foreach (var guid in AssetDatabase.FindAssets("t:Material", new[] { "Assets" }))
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
                if (mat == null || mat.shader == null) continue;
                string shaderName = mat.shader.name;

                if (upgraders.TryGetValue(shaderName, out var up))
                {
                    UnityEditor.Rendering.MaterialUpgrader.Upgrade(mat, up, UnityEditor.Rendering.MaterialUpgrader.UpgradeFlags.None);
                    upgraded++;
                }
                else if (shaderName.StartsWith("Legacy Shaders/") && !shaderName.Contains("Particle"))
                {
                    var lit = Shader.Find("Universal Render Pipeline/Lit");
                    if (lit == null) continue;
                    Texture tex = mat.HasProperty("_MainTex") ? mat.GetTexture("_MainTex") : null;
                    Color c = mat.HasProperty("_Color") ? mat.GetColor("_Color") : Color.white;
                    mat.shader = lit;
                    if (tex != null) mat.SetTexture("_BaseMap", tex);
                    mat.SetColor("_BaseColor", c);
                    EditorUtility.SetDirty(mat);
                    legacyLit++;
                }
            }
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            return $"legacyParticles={legacyFixed}, upgraded={upgraded}, legacyLitRemapped={legacyLit}";
        }

        static int FixLegacyParticleMaterials()
        {
            var urpParticle = Shader.Find("Universal Render Pipeline/Particles/Unlit");
            if (urpParticle == null) return 0;
            int count = 0;
            foreach (var guid in AssetDatabase.FindAssets("t:Material", new[] { "Assets" }))
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
                if (mat == null || mat.shader == null) continue;
                string s = mat.shader.name;
                bool additive = s.EndsWith("Particles/Additive") || s.EndsWith("Particles/Additive (Soft)");
                bool alpha = s.EndsWith("Particles/Alpha Blended") || s.EndsWith("Particles/Alpha Blended Premultiply");
                if (!additive && !alpha) continue;

                Texture tex = mat.HasProperty("_MainTex") ? mat.GetTexture("_MainTex") : null;
                Color tint = mat.HasProperty("_TintColor") ? mat.GetColor("_TintColor") : Color.white;
                mat.shader = urpParticle;
                mat.SetTexture("_BaseMap", tex);
                mat.SetColor("_BaseColor", tint);
                mat.SetFloat("_Surface", 1f);
                mat.SetFloat("_Blend", additive ? 2f : 0f);
                mat.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
                mat.SetFloat("_DstBlend", additive ? (float)BlendMode.One : (float)BlendMode.OneMinusSrcAlpha);
                mat.SetFloat("_ZWrite", 0f);
                mat.SetOverrideTag("RenderType", "Transparent");
                mat.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
                mat.renderQueue = (int)RenderQueue.Transparent;
                EditorUtility.SetDirty(mat);
                count++;
            }
            return count;
        }

        // ---------------- scene construction ----------------

        public static string BuildArenaScene()
        {
            Report.Clear();
            // Variant builders create and close additive proof scenes. Run
            // them before replacing the active scene so they never encounter
            // the new, unsaved Arena scene as their preservation target.
            BrawlerDefinition[] roster = BuildRoster();
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            var env = new GameObject("Environment");
            BuildGround(env.transform);
            BuildBoundary(env.transform);
            BuildProps(env.transform);
            BuildLighting();
            BuildSystems(roster);
            BuildCamera();
            BakeNavMesh(env);

            Directory.CreateDirectory("Assets/Scenes");
            EditorSceneManager.SaveScene(scene, ScenePath);
            // Force the newly constructed renderer hierarchy through scene
            // serialization before submitting the render request. Capturing
            // earlier in the same editor tick can return only the camera clear
            // color after a large rebuild. The sprite keeps the same asset GUID,
            // so the UiTheme reference created above remains valid on reimport.
            PortraitStudio.CaptureMinimap(ArenaLayout.MinimapHalfExtent);
            RegisterBuildScenes();
            EnsureAlwaysIncludedShader("Universal Render Pipeline/Unlit");
            TunePipelineAssets();
            AssetDatabase.SaveAssets();

            Report.AppendLine("Scene saved: " + ScenePath);
            return Report.ToString();
        }

        /// <summary>MainMenu first (when built), then Arena.</summary>
        internal static void RegisterBuildScenes()
        {
            var list = new List<EditorBuildSettingsScene>();
            const string menuPath = "Assets/Scenes/MainMenu.unity";
            if (AssetDatabase.LoadAssetAtPath<SceneAsset>(menuPath) != null)
                list.Add(new EditorBuildSettingsScene(menuPath, true));
            list.Add(new EditorBuildSettingsScene(ScenePath, true));
            EditorBuildSettings.scenes = list.ToArray();
        }

        static GameObject Load(string path)
        {
            var go = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (go == null) Report.AppendLine("MISSING ASSET: " + path);
            return go;
        }

        static GameObject Place(string path, Vector3 pos, float rotY, Transform parent, float scale = 1f, bool collider = false, bool groundSnap = false)
        {
            var prefab = Load(path);
            if (prefab == null) return null;
            var go = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
            go.transform.SetParent(parent, false);
            Collider[] authoredColliders = go.GetComponentsInChildren<Collider>(true);
            if (collider && authoredColliders.Length == 0)
            {
                FitBoxCollider(go);
            }
            else if (!collider)
            {
                // Decorative placements must stay out of both the NavMesh bake
                // and runtime WorldBlocker queries, even when their source
                // prefab happens to ship with an authored collider.
                for (int i = 0; i < authoredColliders.Length; i++)
                {
                    authoredColliders[i].enabled = false;
                    PrefabUtility.RecordPrefabInstancePropertyModifications(authoredColliders[i]);
                    EditorUtility.SetDirty(authoredColliders[i]);
                }
            }
            go.transform.position = pos;
            go.transform.rotation = Quaternion.Euler(0f, rotY, 0f);
            if (!Mathf.Approximately(scale, 1f)) go.transform.localScale = Vector3.one * scale;
            if (groundSnap)
            {
                // Several source meshes (skull/bone clutter especially) ship
                // with a pivot that is not at their visual base, which reads
                // as a hovering prop with a detached shadow. Re-measure the
                // instantiated renderer bounds after rotation/scale and lift
                // or drop the object so its lowest point rests exactly at the
                // requested ground height instead of trusting the pivot.
                var renderers = go.GetComponentsInChildren<Renderer>();
                if (renderers.Length > 0)
                {
                    var bounds = renderers[0].bounds;
                    for (int i = 1; i < renderers.Length; i++) bounds.Encapsulate(renderers[i].bounds);
                    float correction = pos.y - bounds.min.y;
                    if (Mathf.Abs(correction) > 0.001f)
                        go.transform.position += new Vector3(0f, correction, 0f);
                }
            }
            return go;
        }

        static void FitBoxCollider(GameObject go)
        {
            var renderers = go.GetComponentsInChildren<Renderer>();
            if (renderers.Length == 0) return;
            var b = renderers[0].bounds;
            foreach (var r in renderers) b.Encapsulate(r.bounds);
            var col = go.AddComponent<BoxCollider>();
            col.center = go.transform.InverseTransformPoint(b.center);
            Vector3 size = go.transform.InverseTransformVector(b.size);
            col.size = new Vector3(Mathf.Abs(size.x), Mathf.Abs(size.y), Mathf.Abs(size.z));
        }

        static void BuildGround(Transform parent)
        {
            var groundRoot = new GameObject("Ground").transform;
            groundRoot.SetParent(parent, false);

            var floorPrefab = Load(Arena + "Floors/Floor1.prefab");
            if (floorPrefab == null) return;
            // Floor2's atlas UV samples a dark navy/purple cliff-face swatch —
            // it reads as a missing-texture hole on the play surface and is
            // never used for ground tiles (see ORDER M art-coherence pass).

            // Measure the tile so the grid is exact regardless of mesh size.
            var probe = (GameObject)PrefabUtility.InstantiatePrefab(floorPrefab);
            var bounds = probe.GetComponentInChildren<Renderer>().bounds;
            foreach (var r in probe.GetComponentsInChildren<Renderer>()) bounds.Encapsulate(r.bounds);
            float sourceTile = Mathf.Max(bounds.size.x, bounds.size.z);
            UnityEngine.Object.DestroyImmediate(probe);
            if (sourceTile < 0.5f) sourceTile = 2f;
            float tileScale = ArenaLayout.FloorTileScale;
            float tile = sourceTile * tileScale;
            Report.AppendLine($"Floor tile size: {sourceTile:0.00} x {tileScale:0.0} scale = {tile:0.00}");

            // Deliberate composition: a single straight lane runs from each
            // spawn gate through the zone center, and a plaza ring sits under
            // the control zone. Floor2 is banned (dark/purple), so the lane
            // reads through Floor1 laid at one fixed orientation, breaking
            // out of the surrounding two-rotation checker field instead of
            // introducing a second, clashing material.
            float laneHalfWidth = tile * 1.5f;
            float plazaRadius = ControlZoneRules.RegulationRadius + tile * 0.5f;

            // Verticality accent (ORDER R): the control-zone plaza and the
            // two spawn-gate aprons sit a step above the open field so the
            // arena reads with some relief instead of one flat slab. A true
            // beveled ramp mesh was judged not worth the risk to author
            // procedurally without an editor to preview it; the order's
            // documented fallback — a clean vertical step, with the ground
            // collider extended to match — is used instead. NavMeshAgent's
            // default step-height tolerance (0.4) comfortably clears both
            // 0.22 and 0.15, so the rebake below still walks it as one
            // connected surface.
            const float PlazaLiftHeight = 0.22f;
            const float ApronLiftHeight = 0.15f;
            float apronDepth = tile * 2f;

            var sampleRenderer = floorPrefab.GetComponentInChildren<Renderer>();
            bool supportsTint = sampleRenderer != null && sampleRenderer.sharedMaterial != null &&
                                 sampleRenderer.sharedMaterial.HasProperty("_BaseColor");
            var tintBlock = supportsTint ? new MaterialPropertyBlock() : null;

            int half = Mathf.CeilToInt(ArenaLayout.GroundHalfExtent / tile);
            for (int ix = -half; ix < half; ix++)
            {
                for (int iz = -half; iz < half; iz++)
                {
                    float x = (ix + 0.5f) * tile;
                    float z = (iz + 0.5f) * tile;
                    bool onLane = Mathf.Abs(x) <= laneHalfWidth && Mathf.Abs(z) <= ArenaLayout.GateDepth;
                    bool onPlaza = new Vector2(x, z).sqrMagnitude <= plazaRadius * plazaRadius;
                    bool onApron = onLane && Mathf.Abs(z) >= ArenaLayout.GateDepth - apronDepth;
                    float y = onPlaza ? PlazaLiftHeight : onApron ? ApronLiftHeight : 0f;
                    Vector3 pos = new Vector3(x, y, z);
                    // A fixed two-rotation checker (0/90, never random) keeps
                    // the open field from reading as a uniform slab while
                    // never producing the diagonal-seam "patchwork" the
                    // random four-way rotation used to create. The lane and
                    // plaza stay at a single orientation so the path reads as
                    // deliberate rather than another checker cell.
                    float rotY = (onLane || onPlaza) ? 0f : (((ix + iz) & 1) == 0 ? 0f : 90f);
                    var t = (GameObject)PrefabUtility.InstantiatePrefab(floorPrefab);
                    t.transform.SetParent(groundRoot, false);
                    t.transform.position = pos;
                    t.transform.rotation = Quaternion.Euler(0f, rotY, 0f);
                    t.transform.localScale = Vector3.one * tileScale;

                    if (supportsTint)
                    {
                        // Deterministic per-tile ±4% value variation (seeded
                        // by tile coordinates) so the floor is not one flat
                        // unbroken color, without adding new textures or
                        // material instances (a MaterialPropertyBlock keeps
                        // GPU instancing intact; it only forgoes SRP-Batcher
                        // grouping for the ground renderers).
                        float v = 1f + TileTintNoise(ix, iz) * 0.04f;
                        var tileRenderers = t.GetComponentsInChildren<Renderer>();
                        for (int i = 0; i < tileRenderers.Length; i++)
                        {
                            tileRenderers[i].GetPropertyBlock(tintBlock);
                            tintBlock.SetColor("_BaseColor", new Color(v, v, v, 1f));
                            tileRenderers[i].SetPropertyBlock(tintBlock);
                        }
                    }
                }
            }

            // One flat physics floor beats hundreds of mesh colliders.
            var floorCol = new GameObject("GroundCollider");
            floorCol.transform.SetParent(parent, false);
            var box = floorCol.AddComponent<BoxCollider>();
            box.center = new Vector3(0f, -0.25f, 0f);
            box.size = new Vector3(ArenaLayout.GroundSize, 0.5f, ArenaLayout.GroundSize);

            // Extend the ground collider to match the raised plaza/apron
            // visuals so characters step up onto them instead of clipping
            // through to the base floor height. Named identically to the
            // base collider above ("GroundCollider") so
            // ArenaRuntimeOptimizer's runtime layer pass — which matches on
            // that exact name — assigns these to the ground layer rather
            // than the WorldBlocker layer.
            void AddGroundStep(Vector3 center, Vector3 size)
            {
                var stepGo = new GameObject("GroundCollider");
                stepGo.transform.SetParent(parent, false);
                var stepBox = stepGo.AddComponent<BoxCollider>();
                stepBox.center = center;
                stepBox.size = size;
            }

            AddGroundStep(new Vector3(0f, PlazaLiftHeight - 0.25f, 0f),
                new Vector3(plazaRadius * 2f, 0.5f, plazaRadius * 2f));

            float apronWidth = laneHalfWidth * 2f;
            float apronCenterZ = ArenaLayout.GateDepth - apronDepth * 0.5f;
            AddGroundStep(new Vector3(0f, ApronLiftHeight - 0.25f, apronCenterZ),
                new Vector3(apronWidth, 0.5f, apronDepth));
            AddGroundStep(new Vector3(0f, ApronLiftHeight - 0.25f, -apronCenterZ),
                new Vector3(apronWidth, 0.5f, apronDepth));
        }

        /// <summary>
        /// Deterministic integer hash (Perlin-style) returning a value in
        /// [-1, 1) for a tile grid coordinate, so re-running the builder
        /// always produces the same ground tint pattern.
        /// </summary>
        static float TileTintNoise(int ix, int iz)
        {
            unchecked
            {
                int h = ix * 92821 + iz * 68917;
                h = (h << 13) ^ h;
                int n = (h * (h * h * 15731 + 789221) + 1376312589) & 0x7fffffff;
                return (n / (float)int.MaxValue) * 2f - 1f;
            }
        }

        static void BuildBoundary(Transform parent)
        {
            var boundary = new GameObject("Boundary").transform;
            boundary.SetParent(parent, false);

            // Invisible walls: the authoritative play-area limit.
            for (int i = 0; i < 4; i++)
            {
                var wall = new GameObject("WallCollider" + i);
                wall.transform.SetParent(boundary, false);
                var box = wall.AddComponent<BoxCollider>();
                bool xAxis = i < 2;
                float sign = (i % 2 == 0) ? 1f : -1f;
                float edge = ArenaLayout.PlayableHalfExtent * sign;
                wall.transform.position = xAxis ? new Vector3(edge, 2f, 0f) : new Vector3(0f, 2f, edge);
                box.size = xAxis
                    ? new Vector3(1f, 4f, ArenaLayout.GroundSize)
                    : new Vector3(ArenaLayout.GroundSize, 4f, 1f);
            }

            // Cliff ring for looks, with jitter so it reads organic from above.
            // cliff01-04 are the warm rust/tan boulder set that matches the
            // green/tan play surface; Cliff5/Cliff6 sample a blue-toned atlas
            // swatch that clashed with the rest of the palette and are
            // dropped (see ORDER M art-coherence pass).
            string[] cliffs =
            {
                Arena + "Rocks/cliff01.prefab", Arena + "Rocks/cliff02.prefab",
                Arena + "Rocks/cliff03.prefab", Arena + "Rocks/cliff04.prefab",
            };
            var rng = new System.Random(7);
            int n = 0;
            for (float a = 0f; a < 360f; a += 12f)
            {
                float rad = a * Mathf.Deg2Rad;
                // Alternate a slight radius offset per placement so the ring
                // reads as two shallow, depth-layered rings instead of one
                // flat band.
                float ringOffset = (n % 2 == 0) ? 0f : ArenaLayout.CliffRadiusJitter * 0.5f;
                float dist = ArenaLayout.CliffMinRadius + ringOffset +
                             (float)rng.NextDouble() * ArenaLayout.CliffRadiusJitter;
                Vector3 pos = new Vector3(Mathf.Sin(rad) * dist, 0f, Mathf.Cos(rad) * dist);
                Place(cliffs[n % cliffs.Length], pos, a + 180f + rng.Next(-15, 15), boundary, 1f + (float)rng.NextDouble() * 0.25f);
                n++;
            }

            // Team gates behind each spawn line.
            Place(Arena + "Props/Entrance.prefab", new Vector3(0f, 0f, -ArenaLayout.GateDepth), 0f, boundary);
            Place(Arena + "Props/Gate.prefab", new Vector3(0f, 0f, ArenaLayout.GateDepth), 180f, boundary);
        }

        static void BuildProps(Transform parent)
        {
            var props = new GameObject("Props").transform;
            props.SetParent(parent, false);

            // Every solid gameplay prop is authored in a 180-degree pair so
            // both teams receive the same lanes and cover on the larger map.
            // groundSnap defaults off so naturally-embedded rocks/crystals
            // keep their authored partial burial; it is switched on below
            // only for the man-made cover pieces (pillars/crates/chariot)
            // and clutter that is known to hover on its source pivot.
            void Pair(string path, Vector3 pos, float rotY, float scale = 1f, bool collider = true, bool groundSnap = false)
            {
                Place(path, pos, rotY, props, scale, collider, groundSnap);
                Vector3 mirror = new Vector3(-pos.x, pos.y, -pos.z);
                Place(path, mirror, rotY + 180f, props, scale, collider, groundSnap);
            }

            // Symmetric pillars = the core cover of the arena.
            Pair(Arena + "Props/Pilar1.prefab", new Vector3(-8f, 0f, -8f), 0f, 1f, true, true);
            Pair(Arena + "Props/Pilar2.prefab", new Vector3(8f, 0f, -8f), 90f, 1f, true, true);

            // A second pillar ring keeps the 80m field from becoming long,
            // uninterrupted ranged sightlines.
            Pair(Arena + "Props/Pilar3.prefab", new Vector3(-24f, 0f, -12f), 0f, 1f, true, true);
            Pair(Arena + "Props/Pilar4.prefab", new Vector3(24f, 0f, -12f), 90f, 1f, true, true);

            // Midfield and approach-lane cover. The central channel remains
            // open enough to contest the Gem Grab mine from either side.
            Pair(Arena + "Props/Barrel01.prefab", new Vector3(-14f, 0f, -2f), 20f, 1f, true, true);
            Pair(Arena + "Props/Barrel02.prefab", new Vector3(-13f, 0f, 0.2f), 65f, 1f, true, true);
            Pair(Arena + "Props/Box.prefab", new Vector3(-14f, 0f, 2.4f), 10f, 1f, true, true);
            Pair(Arena + "Props/Box02.prefab", new Vector3(-12f, 0f, -21f), 12f, 1f, true, true);
            Pair(Arena + "Props/Barrel01.prefab", new Vector3(12f, 0f, -21f), 105f, 1f, true, true);

            // Broken chariots anchor the outer rotations without crowding the
            // objective or either five-person spawn fan.
            Pair(Arena + "Props/Chariot.prefab", new Vector3(19f, 0f, 4f), -35f, 1f, true, true);

            // Gem mine marker: small crystal cluster dead center.
            Place(Arena + "Rocks/Crystal2.prefab", new Vector3(0f, 0f, 0f), 15f, props, 0.55f, true, true);

            // Crystals glow near the expanded corners and double as outer cover.
            Pair(Arena + "Rocks/Crystal1.prefab", new Vector3(-31f, 0f, 29f), 30f, 1.2f);
            Pair(Arena + "Rocks/Crystal3.prefab", new Vector3(31f, 0f, 29f), 10f, 1.05f);

            // Symmetric rocks and stones along the rim. Keep a broad central
            // lane clear in front of both five-person spawn rows.
            var rng = new System.Random(1234);
            string[] rocks =
            {
                Arena + "Rocks/rock1.prefab", Arena + "Rocks/rock3.prefab", Arena + "Rocks/rock5.prefab",
                Arena + "Rocks/Stone01.prefab", Arena + "Rocks/Stone03.prefab", Arena + "Rocks/Stone05.prefab",
            };
            int rockPairs = 0;
            int attempts = 0;
            while (rockPairs < 10 && attempts++ < 100)
            {
                float a = (float)(rng.NextDouble() * Math.PI);
                float d = 33f + (float)rng.NextDouble() * 4f;
                Vector3 pos = new Vector3(Mathf.Sin(a) * d, 0f, Mathf.Cos(a) * d);
                if (Mathf.Abs(pos.z) > 25f && Mathf.Abs(pos.x) < 16f) continue;
                Pair(rocks[rng.Next(rocks.Length)], pos, rng.Next(360),
                    0.8f + (float)rng.NextDouble() * 0.6f);
                rockPairs++;
            }

            // Decorative details: paired for the competitive silhouette, but
            // collider-free and fully walkable.
            string[] grass = { Arena + "Floors/SolGrass01.prefab", Arena + "Floors/SolGrass02.prefab", Arena + "Floors/SolGrass03.prefab" };
            for (int i = 0; i < 36; i++)
            {
                Vector3 pos = new Vector3(rng.Next(-36, 37), 0f, rng.Next(-36, 37));
                Pair(grass[rng.Next(grass.Length)], pos, rng.Next(360), 1f, false);
            }
            // Crack decals removed entirely — personas read them as shader
            // glitches rather than ground detail.
            // Skull/bone clutter kept for flavor but ground-snapped so their
            // off-base pivots no longer hover with a detached shadow.
            Pair(Arena + "Props/skull1.prefab", new Vector3(-7f, 0f, 24f), 45f, 1f, false, true);
            Pair(Arena + "Props/Bone1.prefab", new Vector3(8f, 0f, -25f), 100f, 1f, false, true);
        }

        static void BuildLighting()
        {
            var lightGo = new GameObject("Directional Light");
            var light = lightGo.AddComponent<Light>();
            light.type = LightType.Directional;
            light.color = new Color(1f, 0.96f, 0.88f);
            light.intensity = 1.15f;
            light.shadows = LightShadows.Soft;
            light.shadowStrength = 0.75f;
            // Azimuth rotated away from the camera's viewing azimuth (camera
            // looks from -Z toward +Z, see BuildCamera) so shadows fall
            // visibly to the side instead of straight back under the
            // character silhouette (ORDER R / 3D-ness audit: light was
            // ~aligned with the camera).
            lightGo.transform.rotation = Quaternion.Euler(50f, -68f, 0f);

            // Dim cool rim/back light: no shadows, just enough to separate
            // toon-shaded character silhouettes from the ground plane.
            var rimGo = new GameObject("Rim Light");
            var rim = rimGo.AddComponent<Light>();
            rim.type = LightType.Directional;
            rim.color = new Color(0.72f, 0.8f, 0.95f);
            rim.intensity = 0.25f;
            rim.shadows = LightShadows.None;
            rimGo.transform.rotation = Quaternion.Euler(38f, 155f, 0f);

            var skybox = AssetDatabase.LoadAssetAtPath<Material>("Assets/ModularRPGHeroesPBR/Material/Skybox_Mat.mat");
            if (skybox != null) RenderSettings.skybox = skybox;
            RenderSettings.sun = light;
            RenderSettings.ambientMode = AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = new Color(0.62f, 0.68f, 0.78f);
            RenderSettings.ambientEquatorColor = new Color(0.45f, 0.48f, 0.52f);
            RenderSettings.ambientGroundColor = new Color(0.25f, 0.23f, 0.21f);
            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.Exponential;
            // Slightly heavier, slightly deeper-toned fog for a visible depth
            // falloff: distant cliffs read a step hazier than midfield.
            RenderSettings.fogDensity = 0.011f;
            RenderSettings.fogColor = new Color(0.58f, 0.66f, 0.78f);

            // Global post-processing volume with a saved profile asset.
            Directory.CreateDirectory("Assets/Settings");
            const string profilePath = "Assets/Settings/ArenaPostProfile.asset";
            var existing = AssetDatabase.LoadAssetAtPath<VolumeProfile>(profilePath);
            if (existing != null) AssetDatabase.DeleteAsset(profilePath);
            var profile = ScriptableObject.CreateInstance<VolumeProfile>();
            AssetDatabase.CreateAsset(profile, profilePath);

            T AddFx<T>() where T : VolumeComponent
            {
                var fx = profile.Add<T>(true);
                fx.name = typeof(T).Name;
                AssetDatabase.AddObjectToAsset(fx, profile);
                return fx;
            }

            var bloom = AddFx<Bloom>();
            bloom.intensity.Override(0.55f);
            bloom.threshold.Override(1.05f);
            var vignette = AddFx<Vignette>();
            vignette.intensity.Override(0.26f);
            vignette.smoothness.Override(0.42f);
            var color = AddFx<ColorAdjustments>();
            color.postExposure.Override(0.18f);
            color.saturation.Override(14f);
            color.contrast.Override(8f);
            var tone = AddFx<Tonemapping>();
            tone.mode.Override(TonemappingMode.ACES);

            var volumeGo = new GameObject("Global Volume");
            var volume = volumeGo.AddComponent<Volume>();
            volume.isGlobal = true;
            volume.sharedProfile = profile;
        }

        static void BuildSystems(BrawlerDefinition[] roster)
        {
            var systems = new GameObject("GameSystems");
            var mm = systems.AddComponent<MatchManager>();
            mm.ConfigureMode(GameMode.ControlZone);
            // GameFlow starts the match after character select.
            mm.autoStart = false;

            var controlZone = systems.AddComponent<ControlZoneManager>();
            controlZone.regulationRadius = ControlZoneRules.RegulationRadius;
            controlZone.overtimeRadius = ControlZoneRules.OvertimeRadius;
            controlZone.overtimeExpansionPerSecond =
                ControlZoneRules.OvertimeExpansionPerSecond;

            var spawnRoot = new GameObject("SpawnPoints").transform;
            spawnRoot.SetParent(systems.transform, false);
            Transform Spawn(string name, Vector3 pos)
            {
                var t = new GameObject(name).transform;
                t.SetParent(spawnRoot, false);
                t.position = pos;
                return t;
            }
            mm.blueSpawns = new Transform[ArenaLayout.TeamSize];
            mm.redSpawns = new Transform[ArenaLayout.TeamSize];
            for (int i = 0; i < ArenaLayout.TeamSize; i++)
            {
                mm.blueSpawns[i] = Spawn("BlueSpawn" + i,
                    ArenaLayout.SpawnPosition(TeamId.Blue, i));
                mm.redSpawns[i] = Spawn("RedSpawn" + i,
                    ArenaLayout.SpawnPosition(TeamId.Red, i));
            }

            var gems = systems.AddComponent<GemGrabManager>();
            gems.minePosition = Vector3.zero;

            var popups = systems.AddComponent<DamagePopups>();
            var (enemyHit, allyHurt, heal) = ThemeKit.EnsureDnpPrefabs();
            popups.enemyHitPrefab = enemyHit;
            popups.allyHurtPrefab = allyHurt;
            popups.healPrefab = heal;

            ThemeKit.CreateThemeObject();

            var hud = new GameObject("HUD");
            hud.AddComponent<BrawlHUD>();

            var flow = systems.AddComponent<GameFlow>();
            flow.roster = roster;
            PortraitStudio.EnsurePortraits(flow.roster);
        }

        static BrawlerDefinition WizardDef(
            string id, string name, string role, string description, SpellSchool school,
            string element, float hp, float damage, float range, float cooldown,
            float hitDelay, float speed, float aim, float projectileSpeed,
            string superName, float superDamage, float superBlast, float superKnockback,
            string kriptoHand, string kriptoImpact, string secondarySuper)
        {
            BrawlerSuperStyle superStyle = school == SpellSchool.Arcane ||
                                            school == SpellSchool.Frost ||
                                            school == SpellSchool.Earth
                    ? BrawlerSuperStyle.Burst
                    : BrawlerSuperStyle.ProjectileBlast;
            return new BrawlerDefinition
            {
                id = id,
                displayName = name,
                role = role,
                description = description,
                invectorHumanPrefab = string.Equals(id, "frost", StringComparison.Ordinal)
                    ? Load(InvectorRimeMigrationBuilder.ProductionHumanPrefabPath)
                    : null,
                invectorAIPrefab = string.Equals(id, "frost", StringComparison.Ordinal)
                    ? Load(InvectorRimeMigrationBuilder.ProductionAIPrefabPath)
                    : null,
                maxHealth = hp,
                damage = damage,
                attackRange = range,
                attackRadius = 1.15f,
                cooldown = cooldown,
                basicAttackReloadInterval = MobileCombatRules.BasicAttackReloadInterval,
                hitDelay = hitDelay,
                moveLock = Mathf.Min(0.42f, hitDelay + 0.04f),
                moveSpeed = speed,
                autoAimRange = aim,
                projectilePrefab = LoadMagic($"Missiles & Explosions/{element}/{element}MissileNormal"),
                projectileSpeed = projectileSpeed,
                projectileReadability = ProjectileReadabilityProfile.ForRoster(id, school),
                swingVfx = LoadMagic($"Muzzleflash/Normal/{element}MuzzleNormal"),
                impactVfx = LoadMagic($"Missiles & Explosions/{element}/{element}ExplosionSmall"),
                koVfx = LoadMagic($"Nova/Nova{element}"),
                spawnVfx = LoadMagic($"Aura/AuraCast/AuraCast{element}"),
                castVfx = LoadMagic($"Charge/{element}Charge"),
                secondaryCastVfx = LoadKripto("HandEffects", kriptoHand),
                secondaryImpactVfx = LoadKripto("CollisionEffects", kriptoImpact),
                attackSfx = LoadMagicSound("Cast/magic_cast_" + element.ToLowerInvariant()),
                superName = superName,
                superStyle = superStyle,
                superDamageMultiplier = superDamage,
                superRange = superStyle == BrawlerSuperStyle.ProjectileBlast
                    ? Mathf.Max(range + 1.5f, 10f)
                    : superStyle == BrawlerSuperStyle.Dash ? 3.2f : superBlast * 1.45f,
                superKnockback = superKnockback,
                superDashDistance = 4.8f,
                superProjectileSpeed = projectileSpeed * 1.25f,
                superProjectileBlastRadius = superBlast,
                superVfx = LoadMagic($"Muzzleflash/Big/{element}MuzzleBig"),
                superProjectilePrefab = LoadMagic($"Missiles & Explosions/{element}/{element}MissileMega"),
                superImpactVfx = LoadMagic($"Missiles & Explosions/{element}/{element}ExplosionMega"),
                secondarySuperVfx = LoadMagic(secondarySuper),
                specialty = SpellSpecialty.ForSchool(school),
            };
        }

        static GameObject LoadMagic(string relativePath)
        {
            return Load(Magic + relativePath + ".prefab");
        }

        static BrawlerDefinition ArcherDef()
        {
            return new BrawlerDefinition
            {
                id = "thorn",
                displayName = "Thorn",
                role = "Archer",
                description = "A patient sharpshooter who controls long lanes with fast arrows and punishes grouped enemies with an explosive shot.",
                invectorHumanPrefab = Load(
                    InvectorThornMigrationBuilder.ProductionHumanPrefabPath),
                invectorAIPrefab = Load(
                    InvectorThornMigrationBuilder.ProductionAIPrefabPath),
                maxHealth = 96f,
                damage = 30f,
                attackRange = 10.5f,
                attackRadius = 0.8f,
                cooldown = 1.1f,
                basicAttackReloadInterval = MobileCombatRules.BasicAttackReloadInterval,
                hitDelay = 0.48f,
                moveLock = 0.42f,
                moveSpeed = 5.15f,
                autoAimRange = 12.5f,
                wardStepDistance = 3.2f,
                wardStepCost = 24f,
                projectilePrefab = Load(Weapons + "Arrow01.prefab"),
                projectileSpeed = 24f,
                projectileReadability = ProjectileReadabilityProfile.ForRoster(
                    "thorn", SpellSchool.None),
                impactVfx = LoadMagic("Slash Hit/EarthSlashHit"),
                koVfx = LoadMagic("Nova/NovaEarth"),
                spawnVfx = LoadMagic("Aura/AuraCast/AuraCastEarth"),
                attackSfx = LoadMagicSound("Cast/magic_cast_generic"),
                superName = "EXPLOSIVE ARROW",
                superStyle = BrawlerSuperStyle.ProjectileBlast,
                superDamageMultiplier = 1.85f,
                superRange = 14f,
                superKnockback = 6.5f,
                superProjectileSpeed = 29f,
                superProjectileBlastRadius = 2.6f,
                superProjectilePrefab = Load(Weapons + "Arrow02.prefab"),
                superImpactVfx = LoadMagic("Missiles & Explosions/Earth/EarthExplosionMega"),
                superVfx = LoadMagic("Muzzleflash/Big/EarthMuzzleBig"),
                specialty = SpellSpecialty.ForSchool(SpellSchool.None),
            };
        }

        static BrawlerDefinition WarriorDef()
        {
            return new BrawlerDefinition
            {
                id = "bastion",
                displayName = "Bastion",
                role = "Vanguard",
                description = "A shielded vanguard who wins ground up close — dive the archers, hold the zone, and answer ganks with a wall of steel.",
                invectorHumanPrefab = Load(
                    InvectorBastionMigrationBuilder.ProductionHumanPrefabPath),
                invectorAIPrefab = Load(
                    InvectorBastionMigrationBuilder.ProductionAIPrefabPath),
                maxHealth = 150f,
                damage = 26f,
                attackRange = 2.8f,
                attackRadius = 1.2f,
                cooldown = 0.85f,
                basicAttackReloadInterval = MobileCombatRules.BasicAttackReloadInterval,
                hitDelay = 0.42f,
                moveLock = 0.42f,
                moveSpeed = 5.35f,
                autoAimRange = 4.2f,
                wardStepDistance = 2.3f,
                wardStepCost = 14f,
                meleeArcDegrees = 110f,
                projectilePrefab = null,
                projectileReadability = ProjectileReadabilityProfile.ForRoster(
                    "bastion", SpellSchool.None),
                swingVfx = LoadMagic("Slash/EarthSlash"),
                impactVfx = LoadMagic("Slash Hit/EarthSlashHit"),
                koVfx = LoadMagic("Nova/NovaEarth"),
                spawnVfx = LoadMagic("Aura/AuraCast/AuraCastEarth"),
                attackSfx = LoadAudioClip(
                    "Assets/Invector-3rdPersonController/Melee Combat/Audio/slash_A.wav"),
                hitSfx = LoadAudioClip(
                    "Assets/Invector-3rdPersonController/Melee Combat/Audio/Hit 1.wav"),
                superName = "AEGIS SHOCKWAVE",
                superStyle = BrawlerSuperStyle.Burst,
                superDamageMultiplier = 1.3f,
                superRange = 4.2f,
                superKnockback = 9f,
                superVfx = LoadMagic("Cleave/EarthCleave"),
                superImpactVfx = LoadMagic("Missiles & Explosions/Earth/EarthExplosionMega"),
                specialty = SpellSpecialty.ForSchool(SpellSchool.None),
            };
        }

        static GameObject LoadKripto(string category, string prefabName)
        {
            if (string.IsNullOrEmpty(prefabName)) return null;
            return Load(KriptoParts + category + "/" + prefabName + ".prefab");
        }

        static AudioClip LoadMagicSound(string relativePath)
        {
            string path = MagicSound + relativePath + ".wav";
            var clip = AssetDatabase.LoadAssetAtPath<AudioClip>(path);
            if (clip == null) Report.AppendLine("MISSING ASSET: " + path);
            return clip;
        }

        static AudioClip LoadAudioClip(string path)
        {
            var clip = AssetDatabase.LoadAssetAtPath<AudioClip>(path);
            if (clip == null) Report.AppendLine("MISSING ASSET: " + path);
            return clip;
        }

        public static BrawlerDefinition[] BuildRoster()
        {
            // The generated wizard assets are source art for the Invector
            // variants; production roster entries never reference them.
            Report.AppendLine(WizardAssetBuilder.EnsureAssets());
            Report.AppendLine(WarriorAssetBuilder.EnsureAssets());
            Report.AppendLine(InvectorRimeMigrationBuilder.BuildRimePilotAssetsSafely());
            Report.AppendLine(InvectorThornMigrationBuilder.BuildThornPilotAssetsSafely());
            Report.AppendLine(InvectorBastionMigrationBuilder.BuildBastionPilotAssetsSafely());
            return BuildRosterFromExistingAssets();
        }

        internal static BrawlerDefinition[] BuildRosterFromExistingAssets()
        {
            var roster = new[]
            {
                WizardDef("frost", "Rime", "Cryomancer",
                    "A control specialist who layers chill, slows advances, and locks down crowded lanes.",
                    SpellSchool.Frost, "Frost", 112f, 23f, 8.7f, 1.08f, 0.42f, 4.75f, 10.5f, 16f,
                    "ABSOLUTE ZERO", 1.58f, 2.8f, 4.5f,
                    "Effect16_Hand", "Effect16_Explosion", "Pillar Blast/FrostPillarBlast"),
                ArcherDef(),
                WarriorDef(),
            };
            foreach (var definition in roster) definition.EnsureSuperConfiguration();
            return roster;
        }

        /// <summary>
        /// Refreshes only the Task 2 roster payload in the two generated scenes.
        /// This preserves scene topology, object file ids, post-processing, and
        /// all Invector builder outputs when combat data alone changes.
        /// </summary>
        public static string RefreshCombatCadenceReadabilityData()
        {
            BrawlerDefinition[] sourceRoster = BuildRosterFromExistingAssets();
            string[] paths = { ScenePath, "Assets/Scenes/MainMenu.unity" };
            var report = new StringBuilder();
            for (int sceneIndex = 0; sceneIndex < paths.Length; sceneIndex++)
            {
                string path = paths[sceneIndex];
                var scene = EditorSceneManager.OpenScene(path, OpenSceneMode.Single);
                GameFlow flow = UnityEngine.Object.FindFirstObjectByType<GameFlow>(
                    FindObjectsInactive.Include);
                MainMenuFlow menuFlow =
                    UnityEngine.Object.FindFirstObjectByType<MainMenuFlow>(
                        FindObjectsInactive.Include);
                BrawlerDefinition[] targetRoster = flow != null
                    ? flow.roster
                    : menuFlow != null ? menuFlow.roster : null;
                if (targetRoster == null)
                    throw new InvalidOperationException(path + " has no generated roster owner.");

                int updated = 0;
                for (int i = 0; i < targetRoster.Length; i++)
                {
                    BrawlerDefinition target = targetRoster[i];
                    if (target == null) continue;
                    BrawlerDefinition source = sourceRoster.FirstOrDefault(candidate =>
                        candidate != null && string.Equals(candidate.id, target.id,
                            StringComparison.Ordinal));
                    if (source == null) continue;
                    target.maxHealth = source.maxHealth;
                    target.damage = source.damage;
                    target.projectileReadability = source.projectileReadability;
                    updated++;
                }

                if (updated != sourceRoster.Length)
                    throw new InvalidOperationException(path + " refreshed " + updated +
                        " of " + sourceRoster.Length + " roster entries.");
                EditorUtility.SetDirty(flow != null
                    ? (UnityEngine.Object)flow
                    : menuFlow);
                if (!EditorSceneManager.SaveScene(scene, path))
                    throw new InvalidOperationException("Could not save " + path + ".");
                report.AppendLine(path + ": refreshed " + updated + " roster entries");
            }
            AssetDatabase.SaveAssets();
            return report.ToString();
        }

        /// <summary>
        /// Refreshes only Task 3 match rules, objective component, and authored
        /// spawn transforms. It leaves arena cover, NavMesh, roster references,
        /// post-processing, and unrelated local file ids untouched.
        /// </summary>
        public static string RefreshControlZoneMatchLoopData()
        {
            UnityEngine.SceneManagement.Scene existing =
                UnityEngine.SceneManagement.SceneManager.GetSceneByPath(ScenePath);
            bool openedAdditively = !existing.IsValid() || !existing.isLoaded;
            UnityEngine.SceneManagement.Scene scene = openedAdditively
                ? EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Additive)
                : existing;
            try
            {
                MatchManager manager = scene.GetRootGameObjects()
                    .Select(root => root.GetComponentInChildren<MatchManager>(true))
                    .FirstOrDefault(value => value != null);
                if (manager == null)
                    throw new InvalidOperationException("Arena has no MatchManager.");

                manager.ConfigureMode(GameMode.ControlZone);
                manager.autoStart = false;
                ControlZoneManager zone = manager.GetComponent<ControlZoneManager>();
                const string zoneScriptPath =
                    "Assets/Scripts/Brawl/ControlZoneManager.cs";
                if (zone != null && !string.Equals(
                        AssetDatabase.GetAssetPath(MonoScript.FromMonoBehaviour(zone)),
                        zoneScriptPath, StringComparison.Ordinal))
                {
                    // A class/file-name mismatch in an earlier Task 3 import
                    // could serialize a scene-local MonoScript. Recreate only
                    // that builder-owned component after the normal script has
                    // imported so the scene retains a stable external GUID.
                    UnityEngine.Object.DestroyImmediate(zone);
                    zone = null;
                }
                if (zone == null) zone = manager.gameObject.AddComponent<ControlZoneManager>();
                zone.regulationRadius = ControlZoneRules.RegulationRadius;
                zone.overtimeRadius = ControlZoneRules.OvertimeRadius;
                zone.overtimeExpansionPerSecond =
                    ControlZoneRules.OvertimeExpansionPerSecond;

                if (manager.blueSpawns == null || manager.redSpawns == null ||
                    manager.blueSpawns.Length < ArenaLayout.TeamSize ||
                    manager.redSpawns.Length < ArenaLayout.TeamSize)
                    throw new InvalidOperationException(
                        "Arena spawn arrays do not contain the five authored secondary-mode slots.");
                for (int i = 0; i < ArenaLayout.TeamSize; i++)
                {
                    if (manager.blueSpawns[i] == null || manager.redSpawns[i] == null)
                        throw new InvalidOperationException("Arena spawn slot " + i + " is null.");
                    manager.blueSpawns[i].position =
                        ArenaLayout.SpawnPosition(TeamId.Blue, i);
                    manager.redSpawns[i].position =
                        ArenaLayout.SpawnPosition(TeamId.Red, i);
                    EditorUtility.SetDirty(manager.blueSpawns[i]);
                    EditorUtility.SetDirty(manager.redSpawns[i]);
                }

                EditorUtility.SetDirty(manager);
                EditorUtility.SetDirty(zone);
                if (!EditorSceneManager.SaveScene(scene, ScenePath))
                    throw new InvalidOperationException("Could not save " + ScenePath + ".");
                return "Arena: refreshed Control Zone rules, objective, and 5 spawn transforms";
            }
            finally
            {
                if (openedAdditively && scene.IsValid() && scene.isLoaded)
                    EditorSceneManager.CloseScene(scene, true);
            }
        }

        static void BuildCamera()
        {
            var camGo = new GameObject("Main Camera");
            camGo.tag = "MainCamera";
            var cam = camGo.AddComponent<Camera>();
            // Widened for perspective convergence = depth (ORDER R rig pass).
            cam.fieldOfView = 54f;
            cam.nearClipPlane = 0.3f;
            cam.farClipPlane = 200f;
            camGo.AddComponent<AudioListener>();
            var extra = camGo.AddComponent<UniversalAdditionalCameraData>();
            extra.renderPostProcessing = true;
            extra.antialiasing = AntialiasingMode.FastApproximateAntialiasing;
            camGo.AddComponent<BrawlCamera>();
            // BrawlCamera owns the follow rig (offset -> pitch/distance) via
            // its own field default; do not override it here, or the two
            // sources of truth silently drift apart again (ORDER R).
            // Authored vista framing for the character-select orbit.
            camGo.transform.position = new Vector3(30f, 17f, -38f);
            camGo.transform.LookAt(new Vector3(0f, 1.5f, 0f));
        }

        static void BakeNavMesh(GameObject env)
        {
            var surface = env.AddComponent<NavMeshSurface>();
            surface.collectObjects = CollectObjects.Children;
            surface.useGeometry = NavMeshCollectGeometry.PhysicsColliders;
            surface.BuildNavMesh();
            if (surface.navMeshData != null)
            {
                Directory.CreateDirectory("Assets/Scenes");
                const string navPath = "Assets/Scenes/ArenaNavMesh.asset";
                var old = AssetDatabase.LoadAssetAtPath<NavMeshData>(navPath);
                if (old != null) AssetDatabase.DeleteAsset(navPath);
                AssetDatabase.CreateAsset(surface.navMeshData, navPath);
                Report.AppendLine("NavMesh baked and saved to " + navPath);
            }
            else
            {
                Report.AppendLine("WARNING: NavMesh bake produced no data (runtime bake fallback will run).");
            }
        }

        static void EnsureAlwaysIncludedShader(string shaderName)
        {
            var shader = Shader.Find(shaderName);
            if (shader == null) return;
            var graphics = AssetDatabase.LoadAssetAtPath<UnityEngine.Object>("ProjectSettings/GraphicsSettings.asset");
            if (graphics == null) return;
            var so = new SerializedObject(graphics);
            var arr = so.FindProperty("m_AlwaysIncludedShaders");
            for (int i = 0; i < arr.arraySize; i++)
                if (arr.GetArrayElementAtIndex(i).objectReferenceValue == shader) return;
            arr.arraySize++;
            arr.GetArrayElementAtIndex(arr.arraySize - 1).objectReferenceValue = shader;
            so.ApplyModifiedPropertiesWithoutUndo();
            Report.AppendLine("Added to Always Included Shaders: " + shaderName);
        }

        static void TunePipelineAssets()
        {
            var seen = new HashSet<UniversalRenderPipelineAsset>();
            void Tune(RenderPipelineAsset rp)
            {
                if (rp is UniversalRenderPipelineAsset urp && seen.Add(urp))
                {
                    urp.supportsHDR = true;
                    urp.shadowDistance = 45f;
                    EditorUtility.SetDirty(urp);
                }
            }
            Tune(GraphicsSettings.defaultRenderPipeline);
            for (int i = 0; i < QualitySettings.names.Length; i++)
                Tune(QualitySettings.GetRenderPipelineAssetAt(i));
            Report.AppendLine($"Tuned {seen.Count} URP asset(s): HDR on, shadowDistance 45.");
        }

        // ---------------- capture ----------------

        public static string CaptureSceneOverview()
        {
            string dir = Path.Combine(Directory.GetParent(Application.dataPath).FullName, "Automation");
            Directory.CreateDirectory(dir);
            var shots = new (string name, Vector3 pos, Vector3 look)[]
            {
                ("iso", new Vector3(48f, 52f, -48f), Vector3.zero),
                ("top", new Vector3(0f, 115f, -0.1f), Vector3.zero),
                ("gameplay", new Vector3(0f, 11.5f, -39.5f), new Vector3(0f, 0f, -30f)),
            };
            var sb = new StringBuilder();
            foreach (var (name, pos, look) in shots)
            {
                var camGo = new GameObject("__CaptureCam");
                try
                {
                    var cam = camGo.AddComponent<Camera>();
                    var extra = camGo.AddComponent<UniversalAdditionalCameraData>();
                    extra.renderPostProcessing = true;
                    cam.fieldOfView = 50f;
                    cam.farClipPlane = 300f;
                    camGo.transform.position = pos;
                    camGo.transform.LookAt(look);

                    var rt = new RenderTexture(1600, 900, 24, RenderTextureFormat.ARGB32);
                    var request = new RenderPipeline.StandardRequest { destination = rt };
                    if (RenderPipeline.SupportsRenderRequest(cam, request))
                    {
                        RenderPipeline.SubmitRenderRequest(cam, request);
                        var prev = RenderTexture.active;
                        RenderTexture.active = rt;
                        var tex = new Texture2D(rt.width, rt.height, TextureFormat.RGB24, false);
                        tex.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0);
                        tex.Apply();
                        RenderTexture.active = prev;
                        string file = Path.Combine(dir, $"scene-{name}.png");
                        File.WriteAllBytes(file, tex.EncodeToPNG());
                        UnityEngine.Object.DestroyImmediate(tex);
                        sb.AppendLine("captured " + file);
                    }
                    else
                    {
                        sb.AppendLine("render request unsupported for " + name);
                    }
                    rt.Release();
                    UnityEngine.Object.DestroyImmediate(rt);
                }
                finally
                {
                    UnityEngine.Object.DestroyImmediate(camGo);
                }
            }
            return sb.ToString();
        }
    }
}
