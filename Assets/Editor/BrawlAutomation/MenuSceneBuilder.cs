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
    /// Deterministic construction of the main-menu scene
    /// (Assets/Scenes/MainMenu.unity): a small display diorama — stone podium
    /// on a tile island ringed by cliffs and crystals — a fixed beauty camera,
    /// warm key light, post volume, plus the UiTheme and MainMenuFlow objects
    /// that build all menu UI at runtime.
    /// </summary>
    public static class MenuSceneBuilder
    {
        const string ScenePath = "Assets/Scenes/MainMenu.unity";
        const string Arena = "Assets/Battle Arena - Cartoon Assets/Prefabs/";

        static readonly StringBuilder Report = new StringBuilder();

        public static string BuildMenuScene()
        {
            Report.Clear();
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            var env = new GameObject("Environment");
            BuildIsland(env.transform);
            BuildLighting();
            Transform podium = BuildPodium(env.transform);
            BuildCamera();
            BuildSystems(podium);

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
            // Tile disc under the podium.
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

            // Backdrop arc behind the podium (facing the camera).
            Place(Arena + "Rocks/cliff01.prefab", new Vector3(-7f, 0f, -9f), 160f, parent, 1.1f);
            Place(Arena + "Rocks/cliff03.prefab", new Vector3(0f, 0f, -11f), 180f, parent, 1.25f);
            Place(Arena + "Rocks/cliff02.prefab", new Vector3(7f, 0f, -9f), 200f, parent, 1.1f);
            Place(Arena + "Rocks/Cliff5.prefab", new Vector3(-11f, 0f, -4f), 130f, parent);
            Place(Arena + "Rocks/Cliff6.prefab", new Vector3(11f, 0f, -4f), 230f, parent);

            Place(Arena + "Rocks/Crystal1.prefab", new Vector3(-4.6f, 0f, -5.5f), 25f, parent, 1.15f);
            Place(Arena + "Rocks/Crystal3.prefab", new Vector3(4.8f, 0f, -5.2f), -35f, parent, 0.95f);
            Place(Arena + "Props/Barrel01.prefab", new Vector3(-3.4f, 0f, -3.2f), 40f, parent);
            Place(Arena + "Props/skull1.prefab", new Vector3(3.2f, 0f, -2.6f), 105f, parent);
            Place(Arena + "Props/Bone1.prefab", new Vector3(2.4f, 0f, 2.2f), 80f, parent);
            Place(Arena + "Floors/SolGrass01.prefab", new Vector3(-2.2f, 0f, 1.6f), 15f, parent);
            Place(Arena + "Floors/SolGrass03.prefab", new Vector3(1.6f, 0f, -1.8f), 200f, parent);
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
                m.SetColor("_BaseColor", new Color(0.32f, 0.3f, 0.36f));
                m.SetFloat("_Smoothness", 0.25f);
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
                m.color = new Color(1f, 0.85f, 0.3f, 1f);
                rimMr.sharedMaterial = m;
            }

            // Characters get parented here, standing on the disc's top face.
            // Pack prefabs face +Z at identity and the camera sits at +Z, so
            // no extra rotation is needed to face it.
            var pivot = new GameObject("CharacterPivot");
            pivot.transform.SetParent(root.transform, false);
            pivot.transform.localPosition = new Vector3(0f, 0.19f, 0f);
            return pivot.transform;
        }

        static void BuildLighting()
        {
            var lightGo = new GameObject("Key Light");
            var light = lightGo.AddComponent<Light>();
            light.type = LightType.Directional;
            light.color = new Color(1f, 0.95f, 0.85f);
            light.intensity = 1.25f;
            light.shadows = LightShadows.Soft;
            light.shadowStrength = 0.7f;
            lightGo.transform.rotation = Quaternion.Euler(46f, 205f, 0f);

            // Cool fill from camera-left so the character's dark side stays readable.
            var fillGo = new GameObject("Fill Light");
            var fill = fillGo.AddComponent<Light>();
            fill.type = LightType.Directional;
            fill.color = new Color(0.55f, 0.65f, 0.9f);
            fill.intensity = 0.35f;
            fill.shadows = LightShadows.None;
            fillGo.transform.rotation = Quaternion.Euler(18f, 60f, 0f);

            var skybox = AssetDatabase.LoadAssetAtPath<Material>("Assets/ModularRPGHeroesPBR/Material/Skybox_Mat.mat");
            if (skybox != null) RenderSettings.skybox = skybox;
            RenderSettings.sun = light;
            RenderSettings.ambientMode = AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = new Color(0.6f, 0.66f, 0.78f);
            RenderSettings.ambientEquatorColor = new Color(0.44f, 0.46f, 0.52f);
            RenderSettings.ambientGroundColor = new Color(0.24f, 0.22f, 0.22f);
            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.Exponential;
            RenderSettings.fogDensity = 0.012f;
            RenderSettings.fogColor = new Color(0.6f, 0.68f, 0.8f);

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
            bloom.intensity.Override(0.6f);
            bloom.threshold.Override(1f);
            var vignette = AddFx<Vignette>();
            vignette.intensity.Override(0.32f);
            vignette.smoothness.Override(0.5f);
            var color = AddFx<ColorAdjustments>();
            color.postExposure.Override(0.15f);
            color.saturation.Override(16f);
            color.contrast.Override(10f);
            var dof = AddFx<DepthOfField>();
            dof.mode.Override(DepthOfFieldMode.Bokeh);
            dof.focusDistance.Override(3.6f);
            dof.aperture.Override(5.5f);
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
            cam.fieldOfView = 34f;
            cam.nearClipPlane = 0.2f;
            cam.farClipPlane = 120f;
            camGo.AddComponent<AudioListener>();
            var extra = camGo.AddComponent<UniversalAdditionalCameraData>();
            extra.renderPostProcessing = true;
            extra.antialiasing = AntialiasingMode.FastApproximateAntialiasing;
            // Slightly low, framing the full character with headroom.
            camGo.transform.position = new Vector3(0f, 1.5f, 4.9f);
            camGo.transform.LookAt(new Vector3(0f, 1.05f, 0f));
        }

        static void BuildSystems(Transform podium)
        {
            ThemeKit.CreateThemeObject();
            var systems = new GameObject("MenuSystems");
            var flow = systems.AddComponent<MainMenuFlow>();
            flow.roster = ArenaSceneBuilder.BuildRoster();
            PortraitStudio.EnsurePortraits(flow.roster);
            flow.podium = podium;
        }
    }
}
