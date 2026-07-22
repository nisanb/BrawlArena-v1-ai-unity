using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using TMPro;

namespace Crownfall.EditorTools
{
    /// Builds the standalone menu scene (CrownfallMenu.unity): a fixed camera
    /// over the GUI Pro painted podium backdrop, four visual-only champion rigs
    /// (real models + idle animators, no combat components), the MenuHud UI
    /// host wired with the designed sprite theme, and generated idle-pose
    /// portrait renders for the champion cards.
    public static class CrownfallMenuForge
    {
        const string ScenePath = "Assets/Crownfall/CrownfallMenu.unity";
        const string PortraitDir = CrownfallForge.GenDir + "/Portraits";

        static readonly (ClassId cls, string skin)[] Champions =
        {
            (ClassId.Knight, null),
            (ClassId.Greatsword, null),
            (ClassId.Duelist, null),
            (ClassId.Mage, null),
            (ClassId.Warhammer, null),
        };

        static string SkinFor(ClassId cls) => CrownfallForge.HeroSkins[cls];

        static readonly string[] AnimFolders =
        {
            CrownfallForge.AnimRoot + "/SwordShield",
            CrownfallForge.AnimRoot + "/SingleTwoHandSword",
            CrownfallForge.AnimRoot + "/DoubleSwords",
            CrownfallForge.AnimRoot + "/MagicWand",
            CrownfallForge.AnimRoot + "/SingleTwoHandSword",
        };

        [MenuItem("Crownfall/Build Menu Scene")]
        public static void BuildMenuScene()
        {
            var overrides = new AnimatorOverrideController[5];
            for (int i = 0; i < 5; i++)
            {
                overrides[i] = AssetDatabase.LoadAssetAtPath<AnimatorOverrideController>(
                    $"{CrownfallForge.GenDir}/Fighter_{(ClassId)i}.overrideController");
                if (overrides[i] == null)
                {
                    Debug.LogError("[CrownfallMenuForge] Missing animator overrides — run Crownfall/Build All first.");
                    return;
                }
            }

            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            // ---- ambient: bright, cool sky bounce so the champion pops off the
            // painted stage without looking flat
            RenderSettings.ambientMode = AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = new Color(0.62f, 0.68f, 0.82f);
            RenderSettings.ambientEquatorColor = new Color(0.45f, 0.46f, 0.52f);
            RenderSettings.ambientGroundColor = new Color(0.2f, 0.22f, 0.28f);
            RenderSettings.fog = false;

            // ---- camera: fixed hero framing; the backdrop canvas fills the frame
            var camGo = new GameObject("Main Camera");
            camGo.tag = "MainCamera";
            var cam = camGo.AddComponent<Camera>();
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0.07f, 0.12f, 0.24f);
            cam.fieldOfView = 34f;
            cam.nearClipPlane = 0.1f;
            cam.farClipPlane = 120f;
            camGo.AddComponent<AudioListener>();
            var camData = cam.GetUniversalAdditionalCameraData();
            camData.renderPostProcessing = true;
            camData.antialiasing = AntialiasingMode.SubpixelMorphologicalAntiAliasing;
            camGo.transform.position = new Vector3(0f, 1.45f, -4.9f);
            camGo.transform.LookAt(new Vector3(0f, 0.95f, 0f));

            // ---- key light aimed at the champion's face (champion faces -Z)
            var keyGo = new GameObject("KeyLight");
            var key = keyGo.AddComponent<Light>();
            key.type = LightType.Directional;
            key.color = new Color(1f, 0.96f, 0.88f);
            key.intensity = 1.25f;
            key.shadows = LightShadows.None;
            keyGo.transform.rotation = Quaternion.Euler(35f, 22f, 0f);

            var rimGo = new GameObject("RimLight");
            var rim = rimGo.AddComponent<Light>();
            rim.type = LightType.Directional;
            rim.color = new Color(0.55f, 0.7f, 1f);
            rim.intensity = 0.55f;
            rim.shadows = LightShadows.None;
            rimGo.transform.rotation = Quaternion.Euler(20f, 205f, 0f);

            // ---- post: reuse the arena's bloom/vignette/ACES profile
            var profile = AssetDatabase.LoadAssetAtPath<VolumeProfile>($"{CrownfallForge.GenDir}/CrownfallPost.asset");
            if (profile != null)
            {
                var volGo = new GameObject("PostFX");
                var vol = volGo.AddComponent<Volume>();
                vol.isGlobal = true;
                vol.priority = 10f;
                vol.sharedProfile = profile;
            }

            // ---- portraits (needs the lights above; renders far off-frame)
            GeneratePortraits();

            // ---- host: MenuHud + showcase + audio
            var menuGo = new GameObject("Menu");
            var hud = menuGo.AddComponent<MenuHud>();
            var showcase = menuGo.AddComponent<MenuShowcase>();
            var fx = menuGo.AddComponent<GameEffects>();
            CrownfallForge.WireEffects(fx);

            // ---- champion rigs: model + idle animator only, facing the camera
            var rigRoot = new GameObject("Champions").transform;
            for (int i = 0; i < 5; i++)
            {
                var rig = BuildShowcaseRig(i, overrides[i]);
                rig.transform.SetParent(rigRoot, true);
                showcase.championRigs[i] = rig;
                rig.SetActive(false);
            }

            var esGo = new GameObject("EventSystem");
            esGo.AddComponent<EventSystem>();
            esGo.AddComponent<InputSystemUIInputModule>();

            CrownfallForge.WireKit(hud);
            hud.showcase = showcase;
            hud.menuCamera = cam;
            EditorUtility.SetDirty(hud);
            EditorUtility.SetDirty(showcase);

            EditorSceneManager.SaveScene(scene, ScenePath);

            // menu owns build slot 0; keep everything else in order after it
            var list = EditorBuildSettings.scenes.Where(s => s.path != ScenePath).ToList();
            list.Insert(0, new EditorBuildSettingsScene(ScenePath, true));
            EditorBuildSettings.scenes = list.ToArray();

            Debug.Log("[CrownfallMenuForge] Menu scene built: " + ScenePath);
        }

        static GameObject BuildShowcaseRig(int classIndex, AnimatorOverrideController aoc)
        {
            var cls = Champions[classIndex].cls;
            string skin = SkinFor(cls);
            var root = new GameObject($"Champion_{cls}");
            root.transform.position = Vector3.zero;
            root.transform.rotation = Quaternion.Euler(0f, 180f, 0f); // face the camera

            var charPrefab = CrownfallForge.LoadHeroPrefab(skin);
            GameObject model;
            if (charPrefab != null)
            {
                model = (GameObject)PrefabUtility.InstantiatePrefab(charPrefab);
                PrefabUtility.UnpackPrefabInstance(model, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
            }
            else
            {
                Debug.LogError("[CrownfallMenuForge] Missing character prefab " + skin);
                model = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            }
            model.name = "Model";
            model.transform.SetParent(root.transform, false);
            model.transform.localPosition = Vector3.zero;
            model.transform.localRotation = Quaternion.identity;

            var anim = model.GetComponentInChildren<Animator>();
            if (anim == null) anim = model.AddComponent<Animator>();
            anim.runtimeAnimatorController = aoc;
            anim.applyRootMotion = false;
            anim.cullingMode = AnimatorCullingMode.AlwaysAnimate;

            if (skin.Contains("/"))
            {
                CrownfallForge.PrepareExternalHero(model, cls);
            }
            else
            {
                CrownfallForge.ReparentWeaponsToHands(model.transform);
                if (cls == ClassId.Warhammer) CrownfallForge.ApplyHammerLoadout(model.transform);
            }
            return root;
        }

        // ------------------------------------------------------------------ portraits

        /// Render each champion skin in its idle pose to a transparent PNG for
        /// the roster cards. Runs inside the freshly-lit menu scene, far from
        /// the podium so nothing leaks into the frame.
        static void GeneratePortraits()
        {
            Directory.CreateDirectory(PortraitDir);

            for (int i = 0; i < 5; i++)
            {
                string path = $"{PortraitDir}/Champion_{i}.png";
                var cls = Champions[i].cls;
                string skin = SkinFor(cls);
                var charPrefab = CrownfallForge.LoadHeroPrefab(skin);
                if (charPrefab == null) continue;

                var basePos = new Vector3(500f, 500f, 500f);
                var model = (GameObject)Object.Instantiate(charPrefab, basePos, Quaternion.Euler(0f, 200f, 0f));
                if (skin.Contains("/"))
                {
                    CrownfallForge.PrepareExternalHero(model, cls);
                }
                else
                {
                    CrownfallForge.ReparentWeaponsToHands(model.transform);
                    if (cls == ClassId.Warhammer) CrownfallForge.ApplyHammerLoadout(model.transform);
                }
                var camGo = new GameObject("__PortraitCam");
                RenderTexture rt = null;
                Texture2D tex = null;
                try
                {
                    // idle pose from the hero's OWN pack (SampleAnimation plays
                    // raw curves — humanoid retargeting does not apply here)
                    AnimationClip idle = null;
                    if (skin.Contains("/"))
                    {
                        string packRoot = skin.Substring(0, skin.IndexOf('/', 7)); // "Assets/<pack>"
                        foreach (var guid in AssetDatabase.FindAssets("Idle t:AnimationClip", new[] { packRoot }))
                        {
                            var clip = AssetDatabase.LoadAssetAtPath<AnimationClip>(AssetDatabase.GUIDToAssetPath(guid));
                            if (clip != null && !clip.name.StartsWith("__preview")) { idle = clip; break; }
                        }
                    }
                    else
                    {
                        var clips = CrownfallForge.LoadClips(AnimFolders[i]);
                        var idleKey = clips.Keys.FirstOrDefault(k =>
                            k.StartsWith("Idle", System.StringComparison.OrdinalIgnoreCase));
                        if (idleKey != null) idle = clips[idleKey];
                    }
                    if (idle != null) idle.SampleAnimation(model, idle.length * 0.25f);
                    // clips may animate the root — park it back on station
                    model.transform.position = basePos;
                    model.transform.rotation = Quaternion.Euler(0f, 200f, 0f);

                    var renderers = model.GetComponentsInChildren<Renderer>();
                    if (renderers.Length == 0) continue;
                    Bounds bounds = renderers[0].bounds;
                    foreach (var r in renderers) bounds.Encapsulate(r.bounds);

                    var cam = camGo.AddComponent<Camera>();
                    var extra = camGo.AddComponent<UniversalAdditionalCameraData>();
                    extra.renderPostProcessing = false;
                    cam.clearFlags = CameraClearFlags.SolidColor;
                    cam.backgroundColor = new Color(0f, 0f, 0f, 0f);
                    cam.fieldOfView = 40f;
                    cam.nearClipPlane = 0.1f;

                    const int W = 512, H = 640;
                    float verticalHalf = bounds.extents.y * 1.14f;
                    float horizontalHalf = Mathf.Max(bounds.extents.x, bounds.extents.z) * 1.14f;
                    float vFov = cam.fieldOfView * 0.5f * Mathf.Deg2Rad;
                    float hFov = Mathf.Atan(Mathf.Tan(vFov) * (W / (float)H));
                    float distance = Mathf.Max(2.2f,
                        Mathf.Max(verticalHalf / Mathf.Tan(vFov), horizontalHalf / Mathf.Tan(hFov)));
                    Vector3 focus = bounds.center + Vector3.up * bounds.size.y * 0.03f;
                    // with the root restored after sampling, visual facing =
                    // root forward — camera parks on that side for a 3/4 view
                    Vector3 faceSide = Quaternion.Euler(0f, 200f, 0f) * Vector3.forward;
                    camGo.transform.position = focus + faceSide * distance;
                    camGo.transform.LookAt(focus);

                    rt = new RenderTexture(W, H, 24, RenderTextureFormat.ARGB32);
                    cam.targetTexture = rt;
                    cam.Render();

                    var prev = RenderTexture.active;
                    RenderTexture.active = rt;
                    tex = new Texture2D(W, H, TextureFormat.RGBA32, false);
                    tex.ReadPixels(new UnityEngine.Rect(0, 0, W, H), 0, 0);
                    tex.Apply();
                    RenderTexture.active = prev;
                    cam.targetTexture = null;

                    File.WriteAllBytes(path, tex.EncodeToPNG());
                }
                finally
                {
                    if (rt != null) { rt.Release(); Object.DestroyImmediate(rt); }
                    if (tex != null) Object.DestroyImmediate(tex);
                    Object.DestroyImmediate(camGo);
                    Object.DestroyImmediate(model);
                }
                AssetDatabase.ImportAsset(path);
                // project defaults import these as sprite-sheet 'Multiple' with
                // zero slices — no Sprite sub-asset exists until forced Single
                var imp = (TextureImporter)AssetImporter.GetAtPath(path);
                imp.textureType = TextureImporterType.Sprite;
                imp.spriteImportMode = SpriteImportMode.Single;
                imp.alphaIsTransparency = true;
                imp.mipmapEnabled = false;
                imp.SaveAndReimport();
                CrownfallForge.LoadSprite(path); // force RGBA32 mobile overrides
            }
        }
    }
}
