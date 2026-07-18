using System;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace BrawlArena.EditorAutomation
{
    /// <summary>
    /// Produces the project-owned Bastion warrior body variant from the pinned
    /// Modular RPG Heroes SwordAndShield01 source. The vendor source prefab
    /// remains untouched so it can be upgraded safely; this selects one static
    /// sword+shield loadout and activates it, mirroring WizardAssetBuilder's
    /// per-school variant recipe.
    /// </summary>
    public static class WarriorAssetBuilder
    {
        public const string Root = "Assets/Generated/Warriors/";

        const string PrefabDir = Root + "Prefabs/";
        const string SourcePrefab =
            "Assets/ModularRPGHeroesPBR/Prefabs/BasicCharacters/SwordAndShield01.prefab";

        // SwordAndShield01 authors every weapon/shield skin the pack offers as
        // inactive siblings under the weaponShield_l/weaponShield_r sockets, and
        // ships with an Axe2_R + Shield6 loadout enabled by default. Bastion
        // needs the sword loadout instead: activate Sword1_R, keep the already
        // authored Shield6 active, and disable the default axe.
        public const string AuthoredWeaponName = "Sword1_R";
        public const string AuthoredShieldName = "Shield6";
        const string DefaultActiveWeaponName = "Axe2_R";

        static bool ensuredThisDomain;

        public static string PrefabPath(string id)
        {
            return PrefabDir + char.ToUpperInvariant(id[0]) + id.Substring(1) + "Warrior.prefab";
        }

        public static string EnsureAssets()
        {
            EnsureFolder(Root);
            EnsureFolder(PrefabDir);

            if (ensuredThisDomain &&
                AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath("bastion")) != null)
                return "Invector warrior source art already refreshed in this editor domain.";

            BuildVariant("bastion");

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            ensuredThisDomain = true;
            return "Invector warrior source art ready: 1 loadout prefab in " + Root;
        }

        static bool BuildVariant(string id)
        {
            GameObject source = AssetDatabase.LoadAssetAtPath<GameObject>(SourcePrefab);
            if (source == null) throw new InvalidOperationException("Missing warrior body prefab: " + SourcePrefab);
            GameObject instance = (GameObject)PrefabUtility.InstantiatePrefab(source);
            try
            {
                instance.name = Capitalize(id) + "Warrior";

                Transform sword = FindDeep(instance.transform, AuthoredWeaponName);
                Transform shield = FindDeep(instance.transform, AuthoredShieldName);
                Transform defaultWeapon = FindDeep(instance.transform, DefaultActiveWeaponName);
                if (sword == null || shield == null)
                {
                    throw new InvalidOperationException(
                        SourcePrefab + " lost its " + AuthoredWeaponName + "/" +
                        AuthoredShieldName + " loadout children.");
                }

                sword.gameObject.SetActive(true);
                shield.gameObject.SetActive(true);
                if (defaultWeapon != null) defaultWeapon.gameObject.SetActive(false);

                CreateSpellOrigin(sword);

                PrefabUtility.SaveAsPrefabAsset(instance, PrefabPath(id));
                return true;
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(instance);
            }
        }

        static void CreateSpellOrigin(Transform weapon)
        {
            var go = new GameObject("SpellOrigin");
            go.transform.SetParent(weapon, false);
            Renderer[] renderers = weapon.GetComponentsInChildren<Renderer>(true);
            if (renderers.Length == 0)
            {
                go.transform.localPosition = Vector3.up;
                return;
            }
            Bounds bounds = renderers[0].bounds;
            for (int i = 1; i < renderers.Length; i++) bounds.Encapsulate(renderers[i].bounds);
            go.transform.position = new Vector3(bounds.center.x, bounds.max.y + 0.04f, bounds.center.z);
            go.transform.rotation = weapon.rotation;
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

        static string Capitalize(string value)
        {
            return char.ToUpperInvariant(value[0]) + value.Substring(1);
        }
    }
}
