using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace BrawlArena.EditorAutomation
{
    /// <summary>
    /// Edit-time generation of the 2D art the runtime UI needs: character
    /// portraits (idle-posed prefab renders on transparent background) and the
    /// top-down arena minimap. Both render with the currently open scene's
    /// lighting, so callers run this during scene builds.
    /// </summary>
    public static class PortraitStudio
    {
        const string PortraitDir = "Assets/Textures/Portraits/";
        const string MinimapPath = "Assets/Textures/ArenaMinimap.png";

        /// <summary>Generate any missing portraits and wire them into the roster.</summary>
        public static void EnsurePortraits(BrawlerDefinition[] roster)
        {
            Directory.CreateDirectory(PortraitDir);
            foreach (var def in roster)
            {
                string path = PortraitDir + def.id + ".png";
                GameObject previewPrefab = BrawlerPreviewAdapter.ResolvePrefab(def);
                if (PortraitNeedsRefresh(previewPrefab, path))
                    RenderPortrait(def, previewPrefab, path);
                def.portrait = AssetDatabase.LoadAssetAtPath<Sprite>(path);
            }
        }

        static bool PortraitNeedsRefresh(GameObject previewPrefab, string path)
        {
            if (AssetDatabase.LoadAssetAtPath<Sprite>(path) == null) return true;
            if (!PortraitHasVisiblePixels(path)) return true;
            string prefabPath = AssetDatabase.GetAssetPath(previewPrefab);
            if (string.IsNullOrEmpty(prefabPath) || !File.Exists(prefabPath) || !File.Exists(path))
                return false;
            System.DateTime portraitWriteTime = File.GetLastWriteTimeUtc(path);
            foreach (string dependency in AssetDatabase.GetDependencies(prefabPath, true))
                if (File.Exists(dependency) && File.GetLastWriteTimeUtc(dependency) > portraitWriteTime)
                    return true;
            return false;
        }

        static void RenderPortrait(BrawlerDefinition def, GameObject previewPrefab, string path)
        {
            // Far from the arena so no props leak into the frame.
            Vector3 basePos = new Vector3(500f, 500f, 500f);
            var model = (GameObject)Object.Instantiate(previewPrefab, basePos, Quaternion.identity);
            var camGo = new GameObject("__PortraitCam");
            try
            {
                BrawlerPreviewAdapter.Prepare(model, def);
                BrawlerPreviewAdapter.ShowIdle(model, def, 0.3f);
                PrepareModelForPortrait(model);

                Bounds bounds = CalculateBounds(model);
                float height = Mathf.Max(1f, bounds.size.y);

                var cam = camGo.AddComponent<Camera>();
                var extra = camGo.AddComponent<UniversalAdditionalCameraData>();
                extra.renderPostProcessing = false;
                cam.clearFlags = CameraClearFlags.SolidColor;
                cam.backgroundColor = new Color(0f, 0f, 0f, 0f);
                cam.fieldOfView = 40f;
                cam.nearClipPlane = 0.1f;
                // Frame from evaluated renderer bounds so every imported hero
                // silhouette shares a consistent crop.
                float verticalHalf = bounds.extents.y * 1.14f;
                float horizontalHalf = bounds.extents.x * 1.14f;
                float verticalHalfFov = cam.fieldOfView * 0.5f * Mathf.Deg2Rad;
                float horizontalHalfFov = Mathf.Atan(Mathf.Tan(verticalHalfFov) * (512f / 640f));
                float distance = Mathf.Max(
                    verticalHalf / Mathf.Tan(verticalHalfFov),
                    horizontalHalf / Mathf.Tan(horizontalHalfFov));
                distance = Mathf.Max(2.4f, distance);
                Vector3 focus = bounds.center + Vector3.up * height * 0.03f;
                cam.nearClipPlane = Mathf.Max(0.05f, distance - bounds.extents.magnitude * 1.5f);
                cam.farClipPlane = distance + Mathf.Max(10f, bounds.extents.magnitude * 2f);
                camGo.transform.position = focus + Vector3.forward * distance;
                camGo.transform.LookAt(focus);

                Capture(cam, 512, 640, path);
            }
            finally
            {
                Object.DestroyImmediate(model);
                Object.DestroyImmediate(camGo);
            }
            ImportAsSprite(path);
            if (!PortraitHasVisiblePixels(path))
                Debug.LogError("[PortraitStudio] portrait contains no visible pixels: " + path);
        }

        static void PrepareModelForPortrait(GameObject model)
        {
            foreach (var renderer in model.GetComponentsInChildren<Renderer>(true))
            {
                // Spell auras, trails, and particles can report enormous bounds
                // before their first simulation tick. They were pushing the
                // camera beyond its clip plane and producing transparent PNGs.
                if (!(renderer is MeshRenderer) && !(renderer is SkinnedMeshRenderer))
                    renderer.enabled = false;
                if (renderer is SkinnedMeshRenderer skinned)
                    skinned.updateWhenOffscreen = true;
            }
        }

        static Bounds CalculateBounds(GameObject model)
        {
            Renderer[] renderers = model.GetComponentsInChildren<Renderer>(false);
            Bounds bounds = default;
            bool found = false;
            foreach (var renderer in renderers)
            {
                if (!renderer.enabled ||
                    (!(renderer is MeshRenderer) && !(renderer is SkinnedMeshRenderer)))
                    continue;
                if (!found)
                {
                    bounds = renderer.bounds;
                    found = true;
                }
                else
                {
                    bounds.Encapsulate(renderer.bounds);
                }
            }
            return found
                ? bounds
                : new Bounds(model.transform.position + Vector3.up, Vector3.one * 2f);
        }

        static bool PortraitHasVisiblePixels(string path)
        {
            if (!File.Exists(path)) return false;
            var texture = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            try
            {
                if (!texture.LoadImage(File.ReadAllBytes(path), false)) return false;
                Color32[] pixels = texture.GetPixels32();
                int minimumVisible = Mathf.Max(32, pixels.Length / 500);
                int visible = 0;
                foreach (Color32 pixel in pixels)
                {
                    if (pixel.a <= 8) continue;
                    visible++;
                    if (visible >= minimumVisible) return true;
                }
                return false;
            }
            finally
            {
                Object.DestroyImmediate(texture);
            }
        }

        /// <summary>
        /// Top-down render of the built arena. Uses a high perspective camera
        /// (near-orthographic at this distance) because ortho render requests
        /// came back empty, and disables fog which would wash out a camera
        /// 120 units up.
        /// </summary>
        public static void CaptureMinimap(float worldHalfExtent)
        {
            CaptureMinimap(worldHalfExtent, MinimapPath);
        }

        /// <summary>Same capture, explicit output — ActionArena keeps its own sprite so rebuilding one arena never clobbers the other's minimap.</summary>
        public static void CaptureMinimap(float worldHalfExtent, string outputPath)
        {
            var camGo = new GameObject("__MinimapCam");
            bool fog = RenderSettings.fog;
            RenderSettings.fog = false;
            try
            {
                var cam = camGo.AddComponent<Camera>();
                var extra = camGo.AddComponent<UniversalAdditionalCameraData>();
                extra.renderPostProcessing = false;
                const float height = 120f;
                cam.fieldOfView = 2f * Mathf.Atan(worldHalfExtent / height) * Mathf.Rad2Deg;
                cam.nearClipPlane = 10f;
                cam.farClipPlane = 200f;
                cam.clearFlags = CameraClearFlags.SolidColor;
                cam.backgroundColor = new Color(0.1f, 0.12f, 0.16f, 1f);
                camGo.transform.position = new Vector3(0f, height, 0f);
                // North (+Z) up on the image.
                camGo.transform.rotation = Quaternion.Euler(90f, 0f, 0f);

                Directory.CreateDirectory(Path.GetDirectoryName(outputPath));
                Capture(cam, 1024, 1024, outputPath);
            }
            finally
            {
                RenderSettings.fog = fog;
                Object.DestroyImmediate(camGo);
            }
            ImportAsSprite(outputPath);
        }

        static void Capture(Camera cam, int width, int height, string path)
        {
            var rt = new RenderTexture(width, height, 24, RenderTextureFormat.ARGB32);
            try
            {
                var request = new RenderPipeline.StandardRequest { destination = rt };
                if (!RenderPipeline.SupportsRenderRequest(cam, request))
                {
                    Debug.LogWarning("[PortraitStudio] render request unsupported for " + path);
                    return;
                }
                RenderPipeline.SubmitRenderRequest(cam, request);
                var prev = RenderTexture.active;
                RenderTexture.active = rt;
                var tex = new Texture2D(width, height, TextureFormat.RGBA32, false);
                tex.ReadPixels(new Rect(0, 0, width, height), 0, 0);
                tex.Apply();
                RenderTexture.active = prev;
                File.WriteAllBytes(path, tex.EncodeToPNG());
                Object.DestroyImmediate(tex);
            }
            finally
            {
                rt.Release();
                Object.DestroyImmediate(rt);
            }
        }

        static void ImportAsSprite(string path)
        {
            AssetDatabase.ImportAsset(path);
            var importer = AssetImporter.GetAtPath(path) as TextureImporter;
            if (importer == null) return;
            importer.textureType = TextureImporterType.Sprite;
            // Default sprite mode is Multiple with no rects = zero Sprite
            // sub-assets, making LoadAssetAtPath<Sprite> return null.
            importer.spriteImportMode = SpriteImportMode.Single;
            importer.alphaIsTransparency = true;
            importer.mipmapEnabled = false;
            importer.SaveAndReimport();
        }
    }
}
