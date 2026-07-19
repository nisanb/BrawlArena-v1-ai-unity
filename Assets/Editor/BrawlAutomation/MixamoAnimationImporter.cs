using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace BrawlArena.EditorAutomation
{
    /// <summary>
    /// Deterministic import rules for third-party Mixamo animation FBX files
    /// under Assets/ThirdParty/Mixamo/. Every Mixamo export ships its take
    /// named "mixamo.com"; the override-controller builders resolve clips by
    /// name, so each take is renamed to its file's base name here. All clips
    /// import as Humanoid against the shared T-pose reference avatar with the
    /// root motion baked into pose (locomotion is parameter-driven and
    /// applyRootMotion is off on the pilots).
    /// </summary>
    public class MixamoAnimationImporter : AssetPostprocessor
    {
        public const string Root = "Assets/ThirdParty/Mixamo/";
        public const string ReferenceAvatarBaseName = "Mixamo_TPose_Ref";

        static readonly HashSet<string> LoopingClips = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "Mixamo_Idle",
            "Mixamo_Walk",
            "Mixamo_Run",
            "Mixamo_Sprint",
            "Mixamo_StrafeLeft",
            "Mixamo_StrafeRight",
            "Mixamo_WalkBack",
            "Mixamo_RunBack",
            "Mixamo_Bastion_Idle",
            "Mixamo_Frost_Idle",
        };

        static bool IsMixamoAsset(string path) =>
            path.Replace('\\', '/').StartsWith(Root, StringComparison.OrdinalIgnoreCase) &&
            path.EndsWith(".fbx", StringComparison.OrdinalIgnoreCase);

        void OnPreprocessModel()
        {
            if (!IsMixamoAsset(assetPath)) return;
            var importer = (ModelImporter)assetImporter;
            importer.animationType = ModelImporterAnimationType.Human;
            importer.materialImportMode = ModelImporterMaterialImportMode.None;
            importer.importCameras = false;
            importer.importLights = false;
            importer.resampleCurves = true;

            if (IsReferenceAvatar(assetPath))
            {
                importer.avatarSetup = ModelImporterAvatarSetup.CreateFromThisModel;
                return;
            }

            Avatar reference = FindReferenceAvatar();
            if (reference != null)
            {
                importer.avatarSetup = ModelImporterAvatarSetup.CopyFromOther;
                importer.sourceAvatar = reference;
            }
            else
            {
                // Reference not imported yet (fresh checkout ordering). Each
                // Mixamo skeleton is identical, so a per-file avatar still
                // retargets; the one-shot reimport pass converges everything
                // onto the shared avatar afterwards.
                importer.avatarSetup = ModelImporterAvatarSetup.CreateFromThisModel;
            }
        }

        void OnPreprocessAnimation()
        {
            if (!IsMixamoAsset(assetPath) || IsReferenceAvatar(assetPath)) return;
            var importer = (ModelImporter)assetImporter;
            var clips = importer.clipAnimations;
            if (clips == null || clips.Length == 0) clips = importer.defaultClipAnimations;
            if (clips == null || clips.Length == 0) return;

            string baseName = Path.GetFileNameWithoutExtension(assetPath);
            bool loop = LoopingClips.Contains(baseName);
            for (int i = 0; i < clips.Length; i++)
            {
                var clip = clips[i];
                clip.name = clips.Length == 1 ? baseName : baseName + "_" + i;
                clip.loopTime = loop;
                clip.lockRootRotation = true;
                clip.lockRootHeightY = true;
                clip.lockRootPositionXZ = true;
                clip.keepOriginalOrientation = true;
                clip.keepOriginalPositionY = true;
                clip.keepOriginalPositionXZ = true;
                clip.events = Array.Empty<AnimationEvent>();
            }
            importer.clipAnimations = clips;
        }

        static bool IsReferenceAvatar(string path) =>
            string.Equals(Path.GetFileNameWithoutExtension(path), ReferenceAvatarBaseName,
                StringComparison.OrdinalIgnoreCase);

        internal static string ReferenceAvatarPath =>
            AssetDatabase.FindAssets(ReferenceAvatarBaseName + " t:Model", new[] { Root.TrimEnd('/') })
                .Select(AssetDatabase.GUIDToAssetPath)
                .FirstOrDefault(p => IsReferenceAvatar(p));

        internal static Avatar FindReferenceAvatar()
        {
            string path = ReferenceAvatarPath;
            if (string.IsNullOrEmpty(path)) return null;
            return AssetDatabase.LoadAllAssetsAtPath(path).OfType<Avatar>().FirstOrDefault();
        }
    }

    /// <summary>
    /// One-shot deterministic reimport + validation for the Mixamo staging
    /// folder: reference avatar first, then every clip, then a hard check
    /// that no take kept its "mixamo.com" name and loop flags match the table.
    /// </summary>
    public static class MixamoImportTools
    {
        [MenuItem("Brawl Arena/Mixamo/Reimport And Validate Mixamo Clips")]
        public static void ReimportAndValidate()
        {
            Debug.Log(ReimportAndValidateInternal());
        }

        public static string ReimportAndValidateInternal()
        {
            if (!AssetDatabase.IsValidFolder(MixamoAnimationImporter.Root.TrimEnd('/')))
                return "no Mixamo folder at " + MixamoAnimationImporter.Root + " (nothing to import)";

            string referencePath = MixamoAnimationImporter.ReferenceAvatarPath;
            if (!string.IsNullOrEmpty(referencePath))
                AssetDatabase.ImportAsset(referencePath,
                    ImportAssetOptions.ForceUpdate | ImportAssetOptions.ForceSynchronousImport);

            var fbxPaths = AssetDatabase.FindAssets("t:Model", new[] { MixamoAnimationImporter.Root.TrimEnd('/') })
                .Select(AssetDatabase.GUIDToAssetPath)
                .Where(p => p.EndsWith(".fbx", StringComparison.OrdinalIgnoreCase))
                .OrderBy(p => p)
                .ToList();
            foreach (string path in fbxPaths)
                if (!string.Equals(path, referencePath, StringComparison.OrdinalIgnoreCase))
                    AssetDatabase.ImportAsset(path,
                        ImportAssetOptions.ForceUpdate | ImportAssetOptions.ForceSynchronousImport);
            AssetDatabase.SaveAssets();

            var problems = new StringBuilder();
            int clipCount = 0;
            foreach (string path in fbxPaths)
            {
                if (string.Equals(path, referencePath, StringComparison.OrdinalIgnoreCase)) continue;
                foreach (var clip in AssetDatabase.LoadAllAssetsAtPath(path).OfType<AnimationClip>())
                {
                    if (clip.name.StartsWith("__preview__")) continue;
                    clipCount++;
                    if (clip.name.Contains("mixamo.com"))
                        problems.AppendLine(path + ": take still named '" + clip.name + "'");
                    if (!clip.isHumanMotion)
                        problems.AppendLine(path + ": clip '" + clip.name + "' is not humanoid motion");
                }
            }

            if (problems.Length > 0)
                throw new InvalidOperationException("Mixamo import validation failed:\n" + problems);
            return "Mixamo import ok: " + fbxPaths.Count + " fbx, " + clipCount + " clips validated" +
                   (string.IsNullOrEmpty(referencePath) ? " (no shared T-pose reference yet)" : "");
        }
    }
}
