using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Unity.AI.Navigation;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.Rendering.Universal;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace BrawlArena.EditorAutomation
{
    /// <summary>
    /// Deterministic construction of the first 3v3 gameplay scene
    /// (Assets/Scenes/Arena.unity) from the imported asset packs, plus the
    /// project-wide URP material conversion and scene-view capture helpers.
    /// </summary>
    public static class ArenaSceneBuilder
    {
        const string ScenePath = "Assets/Scenes/Arena.unity";
        const string Chars = "Assets/ModularRPGHeroesPBR/Prefabs/BasicCharacters/";
        const string Anims = "Assets/ModularRPGHeroesPBR/Animators/";
        const string Arena = "Assets/Battle Arena - Cartoon Assets/Prefabs/";
        const string Magic = "Assets/MagicArsenal/Effects/Prefabs/";

        static readonly StringBuilder Report = new StringBuilder();

        // ---------------- URP material conversion ----------------

        public static string ConvertMaterialsToUrp()
        {
            int legacyFixed = FixLegacyParticleMaterials();
            Converters.RunInBatchMode(
                ConverterContainerId.BuiltInToURP,
                new List<ConverterId> { ConverterId.Material },
                ConverterFilter.Inclusive);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            return $"Remapped {legacyFixed} legacy particle materials; ran URP material converter.";
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
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            var env = new GameObject("Environment");
            BuildGround(env.transform);
            BuildBoundary(env.transform);
            BuildProps(env.transform);
            BuildLighting();
            var systems = BuildSystems(env);
            BuildBrawlers(systems);
            BuildCamera();
            BakeNavMesh(env);

            Directory.CreateDirectory("Assets/Scenes");
            EditorSceneManager.SaveScene(scene, ScenePath);
            EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(ScenePath, true) };
            EnsureAlwaysIncludedShader("Universal Render Pipeline/Unlit");
            TunePipelineAssets();
            AssetDatabase.SaveAssets();

            Report.AppendLine("Scene saved: " + ScenePath);
            return Report.ToString();
        }

        static GameObject Load(string path)
        {
            var go = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (go == null) Report.AppendLine("MISSING ASSET: " + path);
            return go;
        }

        static GameObject Place(string path, Vector3 pos, float rotY, Transform parent, float scale = 1f, bool collider = false)
        {
            var prefab = Load(path);
            if (prefab == null) return null;
            var go = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
            go.transform.SetParent(parent, false);
            if (collider && go.GetComponentInChildren<Collider>() == null) FitBoxCollider(go);
            go.transform.position = pos;
            go.transform.rotation = Quaternion.Euler(0f, rotY, 0f);
            if (!Mathf.Approximately(scale, 1f)) go.transform.localScale = Vector3.one * scale;
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
            var floorPrefab2 = Load(Arena + "Floors/Floor2.prefab");
            if (floorPrefab == null) return;

            // Measure the tile so the grid is exact regardless of mesh size.
            var probe = (GameObject)PrefabUtility.InstantiatePrefab(floorPrefab);
            var bounds = probe.GetComponentInChildren<Renderer>().bounds;
            foreach (var r in probe.GetComponentsInChildren<Renderer>()) bounds.Encapsulate(r.bounds);
            float tile = Mathf.Max(bounds.size.x, bounds.size.z);
            UnityEngine.Object.DestroyImmediate(probe);
            if (tile < 0.5f) tile = 4f;
            Report.AppendLine($"Floor tile size: {tile:0.00}");

            var rng = new System.Random(42);
            int half = Mathf.CeilToInt(26f / tile);
            for (int ix = -half; ix < half; ix++)
            {
                for (int iz = -half; iz < half; iz++)
                {
                    bool alt = ((ix + iz) & 1) == 0 && floorPrefab2 != null;
                    var prefab = alt ? floorPrefab2 : floorPrefab;
                    var t = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
                    t.transform.SetParent(groundRoot, false);
                    t.transform.position = new Vector3((ix + 0.5f) * tile, 0f, (iz + 0.5f) * tile);
                    t.transform.rotation = Quaternion.Euler(0f, 90f * rng.Next(4), 0f);
                }
            }

            // One flat physics floor beats hundreds of mesh colliders.
            var floorCol = new GameObject("GroundCollider");
            floorCol.transform.SetParent(parent, false);
            var box = floorCol.AddComponent<BoxCollider>();
            box.center = new Vector3(0f, -0.25f, 0f);
            box.size = new Vector3(60f, 0.5f, 60f);
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
                wall.transform.position = xAxis ? new Vector3(23f * sign, 2f, 0f) : new Vector3(0f, 2f, 23f * sign);
                box.size = xAxis ? new Vector3(1f, 4f, 52f) : new Vector3(52f, 4f, 1f);
            }

            // Cliff ring for looks, with jitter so it reads organic from above.
            string[] cliffs =
            {
                Arena + "Rocks/cliff01.prefab", Arena + "Rocks/cliff02.prefab",
                Arena + "Rocks/cliff03.prefab", Arena + "Rocks/cliff04.prefab",
                Arena + "Rocks/Cliff5.prefab", Arena + "Rocks/Cliff6.prefab",
            };
            var rng = new System.Random(7);
            int n = 0;
            for (float a = 0f; a < 360f; a += 18f)
            {
                float rad = a * Mathf.Deg2Rad;
                float dist = 25.5f + (float)rng.NextDouble() * 2.5f;
                Vector3 pos = new Vector3(Mathf.Sin(rad) * dist, 0f, Mathf.Cos(rad) * dist);
                Place(cliffs[n % cliffs.Length], pos, a + 180f + rng.Next(-15, 15), boundary, 1f + (float)rng.NextDouble() * 0.4f);
                n++;
            }

            // Team gates behind each spawn line.
            Place(Arena + "Props/Entrance.prefab", new Vector3(0f, 0f, -23.5f), 0f, boundary);
            Place(Arena + "Props/Gate.prefab", new Vector3(0f, 0f, 23.5f), 180f, boundary);
        }

        static void BuildProps(Transform parent)
        {
            var props = new GameObject("Props").transform;
            props.SetParent(parent, false);

            // Symmetric pillars = the core cover of the arena.
            Place(Arena + "Props/Pilar1.prefab", new Vector3(-8f, 0f, -8f), 0f, props, 1f, true);
            Place(Arena + "Props/Pilar2.prefab", new Vector3(8f, 0f, -8f), 90f, props, 1f, true);
            Place(Arena + "Props/Pilar3.prefab", new Vector3(-8f, 0f, 8f), 180f, props, 1f, true);
            Place(Arena + "Props/Pilar4.prefab", new Vector3(8f, 0f, 8f), 270f, props, 1f, true);

            // Side cover clusters.
            Place(Arena + "Props/Barrel01.prefab", new Vector3(-13.5f, 0f, -2f), 20f, props, 1f, true);
            Place(Arena + "Props/Barrel02.prefab", new Vector3(-12.6f, 0f, 0.1f), 65f, props, 1f, true);
            Place(Arena + "Props/Box.prefab", new Vector3(-13.2f, 0f, 2.1f), 10f, props, 1f, true);
            Place(Arena + "Props/Box02.prefab", new Vector3(13.4f, 0f, 1.8f), 75f, props, 1f, true);
            Place(Arena + "Props/Barrel01.prefab", new Vector3(13.1f, 0f, -0.4f), 130f, props, 1f, true);
            Place(Arena + "Props/Plank.prefab", new Vector3(12.4f, 0f, -2.3f), 15f, props);

            // Center accent: broken chariot slightly off-middle.
            Place(Arena + "Props/Chariot.prefab", new Vector3(2.5f, 0f, 0.5f), -35f, props, 1f, true);

            // Crystals glowing near the corners.
            Place(Arena + "Rocks/Crystal1.prefab", new Vector3(-16f, 0f, 14f), 30f, props, 1.2f, true);
            Place(Arena + "Rocks/Crystal2.prefab", new Vector3(16f, 0f, -14f), -50f, props, 1.2f, true);
            Place(Arena + "Rocks/Crystal3.prefab", new Vector3(17f, 0f, 15f), 10f, props, 0.9f, true);
            Place(Arena + "Rocks/Crystal4.prefab", new Vector3(-17f, 0f, -15f), 80f, props, 0.9f, true);

            // Rocks and stones along the rim.
            var rng = new System.Random(1234);
            string[] rocks =
            {
                Arena + "Rocks/rock1.prefab", Arena + "Rocks/rock3.prefab", Arena + "Rocks/rock5.prefab",
                Arena + "Rocks/Stone01.prefab", Arena + "Rocks/Stone03.prefab", Arena + "Rocks/Stone05.prefab",
            };
            for (int i = 0; i < 10; i++)
            {
                float a = (float)(rng.NextDouble() * Math.PI * 2.0);
                float d = 18f + (float)rng.NextDouble() * 4f;
                Vector3 pos = new Vector3(Mathf.Sin(a) * d, 0f, Mathf.Cos(a) * d);
                if (Mathf.Abs(pos.z) > 13f && Mathf.Abs(pos.x) < 6f) continue; // keep spawn lanes clear
                Place(rocks[rng.Next(rocks.Length)], pos, rng.Next(360), props, 0.8f + (float)rng.NextDouble() * 0.6f, true);
            }

            // Decorative details: no colliders, walkable.
            string[] grass = { Arena + "Floors/SolGrass01.prefab", Arena + "Floors/SolGrass02.prefab", Arena + "Floors/SolGrass03.prefab" };
            for (int i = 0; i < 26; i++)
            {
                Vector3 pos = new Vector3(rng.Next(-20, 21), 0f, rng.Next(-20, 21));
                Place(grass[rng.Next(grass.Length)], pos, rng.Next(360), props);
            }
            string[] cracks = { Arena + "Floors/Crack1.prefab", Arena + "Floors/Crack2.prefab", Arena + "Floors/Crack3.prefab" };
            for (int i = 0; i < 6; i++)
            {
                Vector3 pos = new Vector3(rng.Next(-16, 17), 0.01f, rng.Next(-16, 17));
                Place(cracks[rng.Next(cracks.Length)], pos, rng.Next(360), props);
            }
            Place(Arena + "Props/skull1.prefab", new Vector3(-6f, 0f, 15f), 45f, props);
            Place(Arena + "Props/Bone1.prefab", new Vector3(7f, 0f, -16f), 100f, props);
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
            lightGo.transform.rotation = Quaternion.Euler(52f, -32f, 0f);

            var skybox = AssetDatabase.LoadAssetAtPath<Material>("Assets/ModularRPGHeroesPBR/Material/Skybox_Mat.mat");
            if (skybox != null) RenderSettings.skybox = skybox;
            RenderSettings.sun = light;
            RenderSettings.ambientMode = AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = new Color(0.62f, 0.68f, 0.78f);
            RenderSettings.ambientEquatorColor = new Color(0.45f, 0.48f, 0.52f);
            RenderSettings.ambientGroundColor = new Color(0.25f, 0.23f, 0.21f);
            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.Exponential;
            RenderSettings.fogDensity = 0.006f;
            RenderSettings.fogColor = new Color(0.64f, 0.72f, 0.82f);

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

        static GameObject BuildSystems(GameObject env)
        {
            var systems = new GameObject("GameSystems");
            var mm = systems.AddComponent<MatchManager>();
            mm.matchDuration = 150f;
            mm.scoreToWin = 10;
            mm.respawnDelay = 2.5f;
            mm.introDuration = 1.6f;

            var spawnRoot = new GameObject("SpawnPoints").transform;
            spawnRoot.SetParent(systems.transform, false);
            Transform Spawn(string name, Vector3 pos)
            {
                var t = new GameObject(name).transform;
                t.SetParent(spawnRoot, false);
                t.position = pos;
                return t;
            }
            mm.blueSpawns = new[]
            {
                Spawn("BlueSpawn0", new Vector3(-4f, 0f, -16f)),
                Spawn("BlueSpawn1", new Vector3(0f, 0f, -17f)),
                Spawn("BlueSpawn2", new Vector3(4f, 0f, -16f)),
            };
            mm.redSpawns = new[]
            {
                Spawn("RedSpawn0", new Vector3(-4f, 0f, 16f)),
                Spawn("RedSpawn1", new Vector3(0f, 0f, 17f)),
                Spawn("RedSpawn2", new Vector3(4f, 0f, 16f)),
            };

            var hud = new GameObject("HUD");
            hud.AddComponent<BrawlHUD>();
            return systems;
        }

        class BrawlerSpec
        {
            public string prefab;
            public string name;
            public TeamId team;
            public string suffix;
            public string controller;
            public string[] attacks;
            public bool player;
            public float hp, dmg, range, radius, cd, hitDelay, moveLock, speed, aim;
            public string projectile;
            public float projSpeed;
            public string swing, impact, ko, spawn;
            public Vector3 pos;
        }

        static void BuildBrawlers(GameObject systems)
        {
            string MagicPath(string sub) => Magic + sub + ".prefab";
            var specs = new List<BrawlerSpec>
            {
                new BrawlerSpec
                {
                    prefab = Chars + "DoubleSword01.prefab", name = "Aria (You)", team = TeamId.Blue,
                    suffix = "DoubleSword", controller = Anims + "DoubleSwords.controller",
                    attacks = new[] { "NormalAttack01_DoubleSword", "NormalAttack02_DoubleSword" }, player = true,
                    hp = 120, dmg = 20, range = 2.3f, radius = 1.6f, cd = 0.75f, hitDelay = 0.32f, moveLock = 0.4f, speed = 5.4f, aim = 3.8f,
                    swing = MagicPath("Slash/ArcaneSlash"), impact = MagicPath("Slash Hit/ArcaneSlashHit"),
                    ko = MagicPath("Nova/NovaArcane"), spawn = MagicPath("Muzzleflash/Big/ArcaneMuzzleBig"),
                    pos = new Vector3(0f, 0f, -16f),
                },
                new BrawlerSpec
                {
                    prefab = Chars + "SwordAndShield01.prefab", name = "Bastion", team = TeamId.Blue,
                    suffix = "SwordShield", controller = Anims + "SwordShield.controller",
                    attacks = new[] { "NormalAttack01_SwordShield", "NormalAttack02_SwordShield" },
                    hp = 150, dmg = 16, range = 2.2f, radius = 1.6f, cd = 1.1f, hitDelay = 0.38f, moveLock = 0.5f, speed = 4.6f, aim = 3.4f,
                    swing = MagicPath("Slash/FrostSlash"), impact = MagicPath("Slash Hit/FrostSlashHit"),
                    ko = MagicPath("Nova/NovaFrost"), spawn = MagicPath("Muzzleflash/Big/FrostMuzzleBig"),
                    pos = new Vector3(-4f, 0f, -16f),
                },
                new BrawlerSpec
                {
                    prefab = Chars + "MagicWand02.prefab", name = "Nova", team = TeamId.Blue,
                    suffix = "MagicWand", controller = Anims + "MagicWand.controller",
                    attacks = new[] { "Attack01_MagicWand", "Attack02_MagicWand" },
                    hp = 85, dmg = 18, range = 8f, radius = 1.2f, cd = 1.3f, hitDelay = 0.42f, moveLock = 0.35f, speed = 4.9f, aim = 10f,
                    projectile = MagicPath("Missiles & Explosions/Storm/StormMissileNormal"), projSpeed = 15f,
                    swing = MagicPath("Muzzleflash/Normal/StormMuzzleNormal"),
                    impact = MagicPath("Missiles & Explosions/Storm/StormExplosionSmall"),
                    ko = MagicPath("Nova/NovaStorm"), spawn = MagicPath("Muzzleflash/Big/StormMuzzleBig"),
                    pos = new Vector3(4f, 0f, -16f),
                },
                new BrawlerSpec
                {
                    prefab = Chars + "SingleTwoHandSword03.prefab", name = "Grimm", team = TeamId.Red,
                    suffix = "SingleTwohandSword", controller = Anims + "SingleTwoHandSword.controller",
                    attacks = new[] { "NormalAttack01_SingleTwohandSword", "NormalAttack02_SingleTwohandSword" },
                    hp = 135, dmg = 28, range = 2.5f, radius = 1.8f, cd = 1.35f, hitDelay = 0.45f, moveLock = 0.55f, speed = 4.7f, aim = 3.6f,
                    swing = MagicPath("Slash/FireSlash"), impact = MagicPath("Slash Hit/FireSlashHit"),
                    ko = MagicPath("Nova/NovaFire"), spawn = MagicPath("Muzzleflash/Big/FireMuzzleBig"),
                    pos = new Vector3(0f, 0f, 16f),
                },
                new BrawlerSpec
                {
                    prefab = Chars + "DoubleSword05.prefab", name = "Vex", team = TeamId.Red,
                    suffix = "DoubleSword", controller = Anims + "DoubleSwords.controller",
                    attacks = new[] { "NormalAttack01_DoubleSword", "NormalAttack02_DoubleSword" },
                    hp = 110, dmg = 19, range = 2.3f, radius = 1.6f, cd = 0.8f, hitDelay = 0.32f, moveLock = 0.4f, speed = 5.3f, aim = 3.6f,
                    swing = MagicPath("Slash/ShadowSlash"), impact = MagicPath("Slash Hit/ShadowSlashHit"),
                    ko = MagicPath("Nova/NovaShadow"), spawn = MagicPath("Muzzleflash/Big/ShadowMuzzleBig"),
                    pos = new Vector3(-4f, 0f, 16f),
                },
                new BrawlerSpec
                {
                    prefab = Chars + "Bow02.prefab", name = "Thorn", team = TeamId.Red,
                    suffix = "Bow", controller = Anims + "Bow.controller",
                    attacks = new[] { "Attack01_Bow", "Attack02_Bow" },
                    hp = 85, dmg = 22, range = 8.5f, radius = 1.2f, cd = 1.5f, hitDelay = 0.5f, moveLock = 0.4f, speed = 4.8f, aim = 11f,
                    projectile = MagicPath("Missiles & Explosions/Earth/EarthMissileNormal"), projSpeed = 20f,
                    swing = MagicPath("Muzzleflash/Normal/EarthMuzzleNormal"),
                    impact = MagicPath("Missiles & Explosions/Earth/EarthExplosionSmall"),
                    ko = MagicPath("Nova/NovaEarth"), spawn = MagicPath("Muzzleflash/Big/EarthMuzzleBig"),
                    pos = new Vector3(4f, 0f, 16f),
                },
            };

            foreach (var spec in specs) BuildBrawler(spec);
        }

        static void BuildBrawler(BrawlerSpec spec)
        {
            var prefab = Load(spec.prefab);
            if (prefab == null) return;
            var go = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
            go.name = spec.name;
            go.transform.position = spec.pos;
            go.transform.rotation = Quaternion.Euler(0f, spec.team == TeamId.Blue ? 0f : 180f, 0f);

            // Animator: make sure the right controller is assigned.
            var animator = go.GetComponentInChildren<Animator>();
            if (animator == null)
            {
                Report.AppendLine($"WARNING {spec.name}: no Animator on {spec.prefab}");
            }
            else if (animator.runtimeAnimatorController == null)
            {
                animator.runtimeAnimatorController = AssetDatabase.LoadAssetAtPath<RuntimeAnimatorController>(spec.controller);
                Report.AppendLine($"{spec.name}: controller assigned from {spec.controller}");
            }

            // Keep only attack states that really exist in the controller.
            var controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(spec.controller);
            string[] attacks = spec.attacks;
            if (controller != null)
            {
                var stateNames = new HashSet<string>();
                foreach (var layer in controller.layers) CollectStates(layer.stateMachine, stateNames);
                var valid = spec.attacks.Where(stateNames.Contains).ToArray();
                if (valid.Length == 0)
                {
                    Report.AppendLine($"WARNING {spec.name}: none of the attack states exist! Wanted: {string.Join(",", spec.attacks)}");
                    valid = spec.attacks;
                }
                else if (valid.Length != spec.attacks.Length)
                {
                    Report.AppendLine($"{spec.name}: filtered attacks to {string.Join(",", valid)}");
                }
                attacks = valid;
                foreach (var s in new[] { "Idle_", "Run_", "GetHit_", "Die_", "Victory_" })
                    if (!stateNames.Contains(s + spec.suffix))
                        Report.AppendLine($"WARNING {spec.name}: missing state {s}{spec.suffix}");
            }

            var health = go.AddComponent<Health>();
            var so = new SerializedObject(health);
            so.FindProperty("maxHealth").floatValue = spec.hp;
            so.ApplyModifiedPropertiesWithoutUndo();

            if (spec.player)
            {
                var cc = go.AddComponent<CharacterController>();
                cc.center = new Vector3(0f, 1f, 0f);
                cc.radius = 0.4f;
                cc.height = 1.8f;
            }
            else
            {
                var capsule = go.AddComponent<CapsuleCollider>();
                capsule.center = new Vector3(0f, 1f, 0f);
                capsule.radius = 0.4f;
                capsule.height = 1.9f;
                var agent = go.AddComponent<NavMeshAgent>();
                agent.radius = 0.45f;
                agent.height = 1.9f;
                agent.speed = spec.speed;
                agent.acceleration = 40f;
                agent.angularSpeed = 720f;
                agent.obstacleAvoidanceType = ObstacleAvoidanceType.LowQualityObstacleAvoidance;
            }

            var ctrl = go.AddComponent<BrawlerController>();
            ctrl.displayName = spec.name;
            ctrl.team = spec.team;
            ctrl.animSuffix = spec.suffix;
            ctrl.attackStates = attacks;
            ctrl.moveSpeed = spec.speed;
            ctrl.attackDamage = spec.dmg;
            ctrl.attackRange = spec.range;
            ctrl.attackRadius = spec.radius;
            ctrl.attackCooldown = spec.cd;
            ctrl.attackHitDelay = spec.hitDelay;
            ctrl.attackMoveLock = spec.moveLock;
            ctrl.autoAimRange = spec.aim;
            ctrl.projectileSpeed = spec.projSpeed;
            if (!string.IsNullOrEmpty(spec.projectile)) ctrl.projectilePrefab = Load(spec.projectile);
            ctrl.swingVfx = Load(spec.swing);
            ctrl.impactVfx = Load(spec.impact);
            ctrl.koVfx = Load(spec.ko);
            ctrl.spawnVfx = Load(spec.spawn);

            if (spec.player) go.AddComponent<PlayerBrawlerInput>();
            else go.AddComponent<AIBrawler>();

            Report.AppendLine($"Brawler ready: {spec.name} [{spec.team}] {(spec.player ? "PLAYER" : "AI")} at {spec.pos}");
        }

        static void CollectStates(AnimatorStateMachine sm, HashSet<string> names)
        {
            foreach (var s in sm.states) names.Add(s.state.name);
            foreach (var child in sm.stateMachines) CollectStates(child.stateMachine, names);
        }

        static void BuildCamera()
        {
            var camGo = new GameObject("Main Camera");
            camGo.tag = "MainCamera";
            var cam = camGo.AddComponent<Camera>();
            cam.fieldOfView = 50f;
            cam.nearClipPlane = 0.3f;
            cam.farClipPlane = 200f;
            camGo.AddComponent<AudioListener>();
            var extra = camGo.AddComponent<UniversalAdditionalCameraData>();
            extra.renderPostProcessing = true;
            extra.antialiasing = AntialiasingMode.FastApproximateAntialiasing;
            var follow = camGo.AddComponent<BrawlCamera>();
            follow.offset = new Vector3(0f, 11.5f, -7.5f);
            camGo.transform.position = new Vector3(0f, 11.5f, -23.5f);
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
                ("iso", new Vector3(24f, 28f, -24f), Vector3.zero),
                ("top", new Vector3(0f, 45f, -0.1f), Vector3.zero),
                ("gameplay", new Vector3(0f, 11.5f, -23.5f), new Vector3(0f, 0f, -14f)),
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
