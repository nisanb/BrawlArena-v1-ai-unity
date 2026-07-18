using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace BrawlArena.EditorAutomation
{
    /// <summary>
    /// Produces project-owned wizard animation, material and prefab variants.
    /// Imported package assets remain untouched so they can be upgraded safely.
    /// </summary>
    public static class WizardAssetBuilder
    {
        public const string Root = "Assets/Generated/Wizards/";

        const string ClipDir = Root + "Clips/";
        const string MaterialDir = Root + "Materials/";
        const string PrefabDir = Root + "Prefabs/";
        const string SourcePrefab = "Assets/WizardPBR/Prefabs/WizardPBRMaskTintMaterial.prefab";
        const string SourceBodyMaterial = "Assets/WizardPBR/Materials/PBRMaskTint.mat";
        const string Magic = "Assets/MagicArsenal/Effects/Prefabs/";

        // Shader Graph reference names are stable but intentionally opaque;
        // the friendly inspector labels are Color01/02/03.  Addressing made-up
        // names such as _Hair silently leaves every generated material red.
        const string BodyTint01 = "Color_c18aea2e3ad54319abb53f299507b005";
        const string BodyTint02 = "Color_2085cedf5aa5442ebdde2251f5cd0293";
        const string BodyTint03 = "Color_8d375582c627450ba1fbf264b23a9d20";

        readonly struct SchoolLook
        {
            public readonly string Id;
            public readonly Color Hair;
            public readonly Color Inner;
            public readonly Color Outer;
            public readonly Color Accent;
            public readonly int Staff;
            public readonly string AuraElement;

            public SchoolLook(string id, Color hair, Color inner, Color outer,
                Color accent, int staff, string auraElement)
            {
                Id = id;
                Hair = hair;
                Inner = inner;
                Outer = outer;
                Accent = accent;
                Staff = staff;
                AuraElement = auraElement;
            }
        }

        static readonly SchoolLook[] Looks =
        {
            // Only the surviving mage school is generated. The internal frost
            // ID remains stable for saves and combat.
            new SchoolLook("frost", C("F4FDFF"), C("55C9ED"), C("102A64"), C("75F0FF"), 2, "Frost"),
        };

        static bool ensuredThisDomain;

        static readonly string[] RetiredLegacyOutputs =
        {
            Root + "WizardBrawl.controller",
            ClipDir + "Idle01.anim",
            ClipDir + "BattleRunForward.anim",
            ClipDir + "Attack01.anim",
            ClipDir + "Attack02.anim",
            ClipDir + "Attack02Start.anim",
            ClipDir + "Attack02Maintain.anim",
            ClipDir + "GetHit.anim",
            PrefabDir + "ArcaneWizard.prefab",
            PrefabDir + "EarthWizard.prefab",
            PrefabDir + "VoidWizard.prefab",
            PrefabDir + "FireWizard.prefab",
            PrefabDir + "StormWizard.prefab",
            MaterialDir + "ArcaneBody.mat",
            MaterialDir + "ArcaneStaff.mat",
            MaterialDir + "EarthBody.mat",
            MaterialDir + "EarthStaff.mat",
            MaterialDir + "VoidBody.mat",
            MaterialDir + "VoidStaff.mat",
            MaterialDir + "FireBody.mat",
            MaterialDir + "FireStaff.mat",
            MaterialDir + "StormBody.mat",
            MaterialDir + "StormStaff.mat",
        };

        public static string PrefabPath(string id)
        {
            return PrefabDir + char.ToUpperInvariant(id[0]) + id.Substring(1) + "Wizard.prefab";
        }

        public static string EnsureAssets()
        {
            EnsureFolder(Root);
            EnsureFolder(ClipDir);
            EnsureFolder(MaterialDir);
            EnsureFolder(PrefabDir);
            DeleteRetiredLegacyOutputs();

            if (ensuredThisDomain &&
                AssetDatabase.LoadAssetAtPath<AnimationClip>(
                    ClipDir + "Die.anim") != null &&
                AssetDatabase.LoadAssetAtPath<AnimationClip>(
                    ClipDir + "VictoryStart.anim") != null &&
                AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath("frost")) != null)
                return "Invector wizard source art already refreshed in this editor domain.";

            CopyClip("Die", false);
            CopyClip("VictoryStart", false);

            int built = 0;
            foreach (SchoolLook look in Looks)
            {
                Material body = BuildBodyMaterial(look);
                if (BuildVariant(look, body)) built++;
            }

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            ensuredThisDomain = true;
            return $"Invector wizard source art ready: 2 lifecycle clips + {built} school prefabs in {Root}";
        }

        static AnimationClip CopyClip(string sourceName, bool loop)
        {
            string sourcePath = "Assets/WizardPBR/Animations/" + sourceName + ".fbx";
            AnimationClip source = AssetDatabase.LoadAllAssetsAtPath(sourcePath)
                .OfType<AnimationClip>()
                .FirstOrDefault(c => !c.name.StartsWith("__preview__", StringComparison.Ordinal));
            if (source == null) throw new InvalidOperationException("Missing wizard clip: " + sourcePath);

            string outputName = sourceName == "Attack04" ? "Attack02" : sourceName;
            string outputPath = ClipDir + outputName + ".anim";
            AnimationClip clip = AssetDatabase.LoadAssetAtPath<AnimationClip>(outputPath);
            if (clip == null)
            {
                clip = UnityEngine.Object.Instantiate(source);
                clip.name = outputName;
                AssetDatabase.CreateAsset(clip, outputPath);
            }
            else
            {
                EditorUtility.CopySerialized(source, clip);
                clip.name = outputName;
            }

            clip.wrapMode = loop ? WrapMode.Loop : WrapMode.Once;
            var serialized = new SerializedObject(clip);
            SerializedProperty settings = serialized.FindProperty("m_AnimationClipSettings");
            SerializedProperty loopTime = settings?.FindPropertyRelative("m_LoopTime");
            if (loopTime != null) loopTime.boolValue = loop;
            serialized.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(clip);
            return clip;
        }

        static void DeleteRetiredLegacyOutputs()
        {
            for (int i = 0; i < RetiredLegacyOutputs.Length; i++)
                if (AssetDatabase.LoadMainAssetAtPath(RetiredLegacyOutputs[i]) != null)
                    AssetDatabase.DeleteAsset(RetiredLegacyOutputs[i]);
        }

        static Material BuildBodyMaterial(SchoolLook look)
        {
            Material source = AssetDatabase.LoadAssetAtPath<Material>(SourceBodyMaterial);
            if (source == null) throw new InvalidOperationException("Missing wizard material: " + SourceBodyMaterial);
            string path = MaterialDir + Capitalize(look.Id) + "Body.mat";
            Material material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material == null)
            {
                material = new Material(source);
                AssetDatabase.CreateAsset(material, path);
            }
            else
            {
                material.shader = source.shader;
                material.CopyPropertiesFromMaterial(source);
            }
            SetColor(material, BodyTint01, look.Hair);
            SetColor(material, BodyTint02, look.Inner);
            SetColor(material, BodyTint03, look.Outer);
            EditorUtility.SetDirty(material);
            return material;
        }

        static bool BuildVariant(SchoolLook look, Material bodyMaterial)
        {
            GameObject source = AssetDatabase.LoadAssetAtPath<GameObject>(SourcePrefab);
            if (source == null) throw new InvalidOperationException("Missing wizard prefab: " + SourcePrefab);
            GameObject instance = (GameObject)PrefabUtility.InstantiatePrefab(source);
            try
            {
                instance.name = Capitalize(look.Id) + "Wizard";
                Animator animator = instance.GetComponentInChildren<Animator>(true);
                if (animator != null) animator.runtimeAnimatorController = null;

                SkinnedMeshRenderer body = instance.GetComponentsInChildren<SkinnedMeshRenderer>(true)
                    .FirstOrDefault(r => r.gameObject.name == "WizardBody");
                if (body != null) body.sharedMaterial = bodyMaterial;

                Transform activeStaff = null;
                for (int i = 1; i <= 3; i++)
                {
                    Transform staff = FindDeep(instance.transform, "Staff0" + i);
                    if (staff == null) continue;
                    bool active = i == look.Staff;
                    staff.gameObject.SetActive(active);
                    if (active) activeStaff = staff;
                }
                if (activeStaff != null)
                {
                    TintStaff(activeStaff, look);
                    CreateSpellOrigin(activeStaff);
                }

                string auraPath = Magic + "Aura/AuraSimple/AuraSimple" + look.AuraElement + ".prefab";
                GameObject auraPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(auraPath);
                if (auraPrefab != null)
                {
                    var aura = (GameObject)PrefabUtility.InstantiatePrefab(auraPrefab);
                    aura.name = "SchoolAura";
                    aura.transform.SetParent(instance.transform, false);
                    aura.transform.localPosition = new Vector3(0f, 0.04f, 0f);
                    aura.transform.localRotation = Quaternion.identity;
                    aura.transform.localScale = Vector3.one * 0.82f;
                }

                PrefabUtility.SaveAsPrefabAsset(instance, PrefabPath(look.Id));
                return true;
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(instance);
            }
        }

        static void TintStaff(Transform staff, SchoolLook look)
        {
            Renderer renderer = staff.GetComponentInChildren<Renderer>(true);
            if (renderer == null || renderer.sharedMaterial == null) return;
            string path = MaterialDir + Capitalize(look.Id) + "Staff.mat";
            Material material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material == null)
            {
                material = new Material(renderer.sharedMaterial);
                AssetDatabase.CreateAsset(material, path);
            }
            else
            {
                material.shader = renderer.sharedMaterial.shader;
                material.CopyPropertiesFromMaterial(renderer.sharedMaterial);
            }
            SetColor(material, "_BaseColor", Color.Lerp(Color.white, look.Accent, 0.35f));
            SetColor(material, "_Color", Color.Lerp(Color.white, look.Accent, 0.35f));
            SetColor(material, "_EmissionColor", look.Accent * 2.4f);
            renderer.sharedMaterial = material;
            EditorUtility.SetDirty(material);
        }

        static void CreateSpellOrigin(Transform staff)
        {
            var go = new GameObject("SpellOrigin");
            go.transform.SetParent(staff, false);
            Renderer[] renderers = staff.GetComponentsInChildren<Renderer>(true);
            if (renderers.Length == 0)
            {
                go.transform.localPosition = Vector3.up;
                return;
            }
            Bounds bounds = renderers[0].bounds;
            for (int i = 1; i < renderers.Length; i++) bounds.Encapsulate(renderers[i].bounds);
            go.transform.position = new Vector3(bounds.center.x, bounds.max.y + 0.04f, bounds.center.z);
            go.transform.rotation = staff.rotation;
        }

        static Transform FindDeep(Transform root, string name)
        {
            foreach (Transform child in root.GetComponentsInChildren<Transform>(true))
                if (child.name == name) return child;
            return null;
        }

        static void EnsureFolder(string path)
        {
            Directory.CreateDirectory(path);
        }

        static void SetColor(Material material, string property, Color value)
        {
            if (material.HasProperty(property)) material.SetColor(property, value);
        }

        static string Capitalize(string value)
        {
            return char.ToUpperInvariant(value[0]) + value.Substring(1);
        }

        static Color C(string hex)
        {
            return ColorUtility.TryParseHtmlString("#" + hex, out Color value) ? value : Color.white;
        }
    }
}
