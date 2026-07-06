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
                if (AssetDatabase.LoadAssetAtPath<Sprite>(path) == null && def.prefab != null)
                    RenderPortrait(def, path);
                def.portrait = AssetDatabase.LoadAssetAtPath<Sprite>(path);
            }
        }

        static void RenderPortrait(BrawlerDefinition def, string path)
        {
            // Far from the arena so no props leak into the frame.
            Vector3 basePos = new Vector3(500f, 500f, 500f);
            var model = (GameObject)Object.Instantiate(def.prefab, basePos, Quaternion.identity);
            var camGo = new GameObject("__PortraitCam");
            try
            {
                // Pose: sample partway into the idle loop instead of bind pose.
                // Needs AlwaysAnimate (nothing renders it yet, so it would be
                // culled) and a nonzero delta to actually evaluate.
                var animator = model.GetComponentInChildren<Animator>();
                if (animator != null && animator.runtimeAnimatorController != null)
                {
                    animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
                    animator.Play("Idle_" + def.animSuffix, 0, 0.3f);
                    animator.Update(0.02f);
                }

                var cam = camGo.AddComponent<Camera>();
                var extra = camGo.AddComponent<UniversalAdditionalCameraData>();
                extra.renderPostProcessing = false;
                cam.clearFlags = CameraClearFlags.SolidColor;
                cam.backgroundColor = new Color(0f, 0f, 0f, 0f);
                cam.fieldOfView = 40f;
                cam.nearClipPlane = 0.1f;
                cam.farClipPlane = 20f;
                // Far enough back for the full chibi body plus margin.
                camGo.transform.position = basePos + new Vector3(0f, 1.15f, 4.1f);
                camGo.transform.LookAt(basePos + new Vector3(0f, 1.02f, 0f));

                Capture(cam, 512, 640, path);
            }
            finally
            {
                Object.DestroyImmediate(model);
                Object.DestroyImmediate(camGo);
            }
            ImportAsSprite(path);
        }

        /// <summary>
        /// Top-down render of the built arena. Uses a high perspective camera
        /// (near-orthographic at this distance) because ortho render requests
        /// came back empty, and disables fog which would wash out a camera
        /// 120 units up.
        /// </summary>
        public static void CaptureMinimap(float worldHalfExtent)
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

                Directory.CreateDirectory(Path.GetDirectoryName(MinimapPath));
                Capture(cam, 1024, 1024, MinimapPath);
            }
            finally
            {
                RenderSettings.fog = fog;
                Object.DestroyImmediate(camGo);
            }
            ImportAsSprite(MinimapPath);
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
