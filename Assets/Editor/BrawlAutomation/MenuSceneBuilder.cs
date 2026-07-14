using System.IO;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace BrawlArena.EditorAutomation
{
    /// <summary>
    /// Deterministic construction of the main-menu scene: an arcane sanctum
    /// display diorama, beauty camera and lighting, plus the existing runtime
    /// theme/menu-flow objects that own all interactive UI.
    /// </summary>
    public static class MenuSceneBuilder
    {
        const string ScenePath = "Assets/Scenes/MainMenu.unity";
        const string Arena = "Assets/Battle Arena - Cartoon Assets/Prefabs/";
        const string Magic = "Assets/MagicArsenal/Effects/Prefabs/";

        static readonly StringBuilder Report = new StringBuilder();

        public static string BuildMenuScene()
        {
            Report.Clear();
            // Migration builders use additive proof scenes, so complete them
            // before the new untitled menu scene becomes active.
            BrawlerDefinition[] roster = ArenaSceneBuilder.BuildRoster();
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            var env = new GameObject("Environment");
            BuildIsland(env.transform);
            BuildLighting();
            Transform podium = BuildPodium(env.transform);
            BuildCamera();
            BuildSystems(podium, roster);

            Directory.CreateDirectory("Assets/Scenes");
            EditorSceneManager.SaveScene(scene, ScenePath);
            ArenaSceneBuilder.RegisterBuildScenes();
            AssetDatabase.SaveAssets();

            Report.AppendLine("Scene saved: " + ScenePath);
            return Report.ToString();
        }

        static GameObject Place(string path, Vector3 pos, float rotY, Transform parent, float scale = 1f)
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (prefab == null)
            {
                Report.AppendLine("MISSING ASSET: " + path);
                return null;
            }
            var go = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
            go.transform.SetParent(parent, false);
            go.transform.position = pos;
            go.transform.rotation = Quaternion.Euler(0f, rotY, 0f);
            if (!Mathf.Approximately(scale, 1f)) go.transform.localScale = Vector3.one * scale;
            return go;
        }

        static void BuildIsland(Transform parent)
        {
            parent.gameObject.name = "ArcaneSanctum";

            // A compact, symmetrical stone dais keeps the selected hero as
            // the focal point while leaving enough depth for the runtime UI.
            var floorPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(Arena + "Floors/Floor1.prefab");
            if (floorPrefab != null)
            {
                var rng = new System.Random(11);
                for (int ix = -2; ix <= 2; ix++)
                {
                    for (int iz = -2; iz <= 2; iz++)
                    {
                        if (ix * ix + iz * iz > 6) continue;
                        var t = (GameObject)PrefabUtility.InstantiatePrefab(floorPrefab);
                        t.transform.SetParent(parent, false);
                        t.transform.position = new Vector3(ix * 4f, 0f, iz * 4f);
                        t.transform.rotation = Quaternion.Euler(0f, 90f * rng.Next(4), 0f);
                    }
                }
            }

            // A distant stone frame preserves a clean silhouette around the
            // hero.  Keeping the cliffs off-centre avoids a flat wall behind
            // the character and lets the sky/fog add depth.
            Place(Arena + "Rocks/cliff01.prefab", new Vector3(-8.2f, -0.35f, -12.5f), 155f, parent, 0.76f);
            Place(Arena + "Rocks/cliff02.prefab", new Vector3(8.2f, -0.35f, -12.5f), 205f, parent, 0.76f);
            Place(Arena + "Props/Entrance.prefab", new Vector3(0f, 0f, -9.2f), 180f, parent, 1.08f);
            Place(Arena + "Props/Pilar1.prefab", new Vector3(-6.6f, 0f, -6.8f), 0f, parent, 1.02f);
            Place(Arena + "Props/Pilar2.prefab", new Vector3(6.6f, 0f, -6.8f), 180f, parent, 1.02f);
            Place(Arena + "Props/Pilar3.prefab", new Vector3(-10f, 0f, 0.8f), 20f, parent, 0.85f);
            Place(Arena + "Props/Pilar4.prefab", new Vector3(10f, 0f, 0.8f), -20f, parent, 0.85f);

            // Crystal choir: cool left, violet right, with smaller foreground
            // shards producing a layered silhouette around the podium.
            Place(Arena + "Rocks/Crystal1.prefab", new Vector3(-5.3f, 0f, -5.1f), 25f, parent, 1.35f);
            Place(Arena + "Rocks/Crystal3.prefab", new Vector3(5.3f, 0f, -5.1f), -35f, parent, 1.2f);
            Place(Arena + "Rocks/Crystal2.prefab", new Vector3(-7.7f, 0f, -1.2f), 65f, parent, 0.72f);
            Place(Arena + "Rocks/Crystal4.prefab", new Vector3(7.7f, 0f, -1.2f), -65f, parent, 0.72f);
            Place(Arena + "Rocks/Crystal2.prefab", new Vector3(-4.8f, 0f, 2.6f), 100f, parent, 0.42f);
            Place(Arena + "Rocks/Crystal4.prefab", new Vector3(4.8f, 0f, 2.6f), -100f, parent, 0.42f);

            // Looping magical ambience only; no gameplay scripts or colliders.
            Place(Magic + "Aura/AuraCircling/AuraCirclingArcane.prefab",
                new Vector3(0f, 0.17f, 0f), 0f, parent, 1.45f);
            Place(Magic + "Orbital/ArcaneOrbitSphere.prefab",
                new Vector3(0f, 0.62f, -4.3f), 0f, parent, 1.15f);
            Place(Magic + "Flames/ArcaneFlame.prefab",
                new Vector3(-5.25f, 1.05f, -4.75f), 0f, parent, 0.72f);
            Place(Magic + "Flames/ShadowFlame.prefab",
                new Vector3(5.25f, 1.05f, -4.75f), 0f, parent, 0.72f);

            CreateSanctumLight("Cyan Crystal Glow", new Vector3(-5.2f, 2.1f, -4.7f),
                new Color(0.16f, 0.62f, 1f), 1.75f, 7.2f, parent);
            CreateSanctumLight("Violet Crystal Glow", new Vector3(5.2f, 2.1f, -4.7f),
                new Color(0.58f, 0.25f, 1f), 1.7f, 7.2f, parent);
        }

        static void CreateSanctumLight(string name, Vector3 position, Color color,
            float intensity, float range, Transform parent)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.position = position;
            var light = go.AddComponent<Light>();
            light.type = LightType.Point;
            light.color = color;
            light.intensity = intensity;
            light.range = range;
            light.shadows = LightShadows.None;
        }

        static Transform BuildPodium(Transform parent)
        {
            var root = new GameObject("PodiumRoot");
            root.transform.SetParent(parent, false);
            root.transform.position = Vector3.zero;

            var disc = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            disc.name = "PodiumDisc";
            Object.DestroyImmediate(disc.GetComponent<Collider>());
            disc.transform.SetParent(root.transform, false);
            disc.transform.localPosition = new Vector3(0f, 0.09f, 0f);
            disc.transform.localScale = new Vector3(2.6f, 0.09f, 2.6f);
            var mr = disc.GetComponent<MeshRenderer>();
            var lit = Shader.Find("Universal Render Pipeline/Lit");
            if (lit != null)
            {
                var m = new Material(lit);
                m.SetColor("_BaseColor", new Color(0.105f, 0.095f, 0.18f));
                m.SetFloat("_Metallic", 0.35f);
                m.SetFloat("_Smoothness", 0.62f);
                mr.sharedMaterial = m;
            }

            var rim = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            rim.name = "PodiumRim";
            Object.DestroyImmediate(rim.GetComponent<Collider>());
            rim.transform.SetParent(root.transform, false);
            rim.transform.localPosition = new Vector3(0f, 0.185f, 0f);
            rim.transform.localScale = new Vector3(2.75f, 0.012f, 2.75f);
            var rimMr = rim.GetComponent<MeshRenderer>();
            var unlit = Shader.Find("Universal Render Pipeline/Unlit");
            if (unlit != null)
            {
                var m = new Material(unlit);
                m.color = new Color(0.08f, 0.48f, 0.68f, 1f);
                rimMr.sharedMaterial = m;
            }

            // Two raised rune bands make the podium read as a magical device
            // instead of a plain cylinder, especially behind translucent UI.
            CreateRuneBand(root.transform, "InnerRuneBand", 1.85f, 0.198f,
                new Color(0.42f, 0.12f, 0.68f), true);
            CreateRuneBand(root.transform, "OuterRuneBand", 3.05f, 0.176f,
                new Color(0.05f, 0.46f, 0.62f), false);

            // Characters get parented here, standing on the disc's top face.
            // Pack prefabs face +Z at identity and the camera sits at +Z, so
            // no extra rotation is needed to face it.
            var pivot = new GameObject("CharacterPivot");
            pivot.transform.SetParent(root.transform, false);
            pivot.transform.localPosition = new Vector3(0f, 0.19f, 0f);
            return pivot.transform;
        }

        static void CreateRuneBand(Transform parent, string name, float radius,
            float height, Color color, bool maskCenter)
        {
            var ring = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            ring.name = name;
            Object.DestroyImmediate(ring.GetComponent<Collider>());
            ring.transform.SetParent(parent, false);
            ring.transform.localPosition = new Vector3(0f, height, 0f);
            ring.transform.localScale = new Vector3(radius, 0.006f, radius);
            var shader = Shader.Find("Universal Render Pipeline/Unlit");
            if (shader == null) return;
            var material = new Material(shader);
            material.SetColor("_BaseColor", color * 1.35f);
            ring.GetComponent<MeshRenderer>().sharedMaterial = material;

            // The primitive is a disc; a slightly raised dark inset turns it
            // into a narrow luminous band without requiring a custom mesh.
            if (!maskCenter) return;
            var inset = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            inset.name = name + "Inset";
            Object.DestroyImmediate(inset.GetComponent<Collider>());
            inset.transform.SetParent(parent, false);
            inset.transform.localPosition = new Vector3(0f, height + 0.008f, 0f);
            float insetRadius = Mathf.Max(0.05f, radius - 0.18f);
            inset.transform.localScale = new Vector3(insetRadius, 0.003f, insetRadius);
            var insetMaterial = new Material(shader);
            insetMaterial.SetColor("_BaseColor", new Color(0.045f, 0.035f, 0.09f));
            inset.GetComponent<MeshRenderer>().sharedMaterial = insetMaterial;
        }

        static void BuildLighting()
        {
            var lightGo = new GameObject("Key Light");
            var light = lightGo.AddComponent<Light>();
            light.type = LightType.Directional;
            light.color = new Color(0.82f, 0.88f, 1f);
            light.intensity = 1.08f;
            light.shadows = LightShadows.Soft;
            light.shadowStrength = 0.7f;
            lightGo.transform.rotation = Quaternion.Euler(46f, 205f, 0f);

            // Cool fill from camera-left so the character's dark side stays readable.
            var fillGo = new GameObject("Fill Light");
            var fill = fillGo.AddComponent<Light>();
            fill.type = LightType.Directional;
            fill.color = new Color(0.48f, 0.3f, 0.9f);
            fill.intensity = 0.32f;
            fill.shadows = LightShadows.None;
            fillGo.transform.rotation = Quaternion.Euler(18f, 60f, 0f);

            var skybox = AssetDatabase.LoadAssetAtPath<Material>("Assets/ModularRPGHeroesPBR/Material/Skybox_Mat.mat");
            if (skybox != null) RenderSettings.skybox = skybox;
            RenderSettings.sun = light;
            RenderSettings.ambientMode = AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = new Color(0.2f, 0.25f, 0.44f);
            RenderSettings.ambientEquatorColor = new Color(0.14f, 0.13f, 0.28f);
            RenderSettings.ambientGroundColor = new Color(0.055f, 0.04f, 0.1f);
            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.Exponential;
            RenderSettings.fogDensity = 0.012f;
            RenderSettings.fogColor = new Color(0.085f, 0.08f, 0.2f);

            const string profilePath = "Assets/Settings/MenuPostProfile.asset";
            Directory.CreateDirectory("Assets/Settings");
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
            bloom.intensity.Override(0.68f);
            bloom.threshold.Override(0.95f);
            var vignette = AddFx<Vignette>();
            vignette.intensity.Override(0.32f);
            vignette.smoothness.Override(0.5f);
            var color = AddFx<ColorAdjustments>();
            color.postExposure.Override(-0.04f);
            color.saturation.Override(10f);
            color.contrast.Override(8f);
            var dof = AddFx<DepthOfField>();
            dof.mode.Override(DepthOfFieldMode.Bokeh);
            dof.focusDistance.Override(6.2f);
            dof.aperture.Override(6.5f);
            var tone = AddFx<Tonemapping>();
            tone.mode.Override(TonemappingMode.ACES);

            var volumeGo = new GameObject("Global Volume");
            var volume = volumeGo.AddComponent<Volume>();
            volume.isGlobal = true;
            volume.sharedProfile = profile;
        }

        static void BuildCamera()
        {
            var camGo = new GameObject("Main Camera");
            camGo.tag = "MainCamera";
            var cam = camGo.AddComponent<Camera>();
            cam.fieldOfView = 33f;
            cam.nearClipPlane = 0.2f;
            cam.farClipPlane = 120f;
            camGo.AddComponent<AudioListener>();
            var extra = camGo.AddComponent<UniversalAdditionalCameraData>();
            extra.renderPostProcessing = true;
            extra.antialiasing = AntialiasingMode.FastApproximateAntialiasing;
            // A modest high angle exposes the rune dais while the longer lens
            // leaves negative space for the GUI Pro menu composition.
            camGo.transform.position = new Vector3(0f, 1.95f, 6.2f);
            camGo.transform.LookAt(new Vector3(0f, 1.08f, 0f));
        }

        static void BuildSystems(Transform podium, BrawlerDefinition[] roster)
        {
            ThemeKit.CreateThemeObject();
            var systems = new GameObject("MenuSystems");
            var flow = systems.AddComponent<MainMenuFlow>();
            flow.roster = roster;
            PortraitStudio.EnsurePortraits(flow.roster);
            flow.podium = podium;
        }
    }
}
