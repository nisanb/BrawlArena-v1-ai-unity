using UnityEditor;
using UnityEngine;

namespace BrawlArena.EditorAutomation
{
    /// <summary>
    /// Applies the roster weapon-presentation overrides (MagicWand attack and
    /// carry-pose clips for the staff wizards, Bow clips for Thorn) to the
    /// existing generated override controllers without running the full
    /// builders. The builders apply the same configuration on regeneration.
    /// </summary>
    public static class InvectorWeaponPoseOverrideUtility
    {
        [MenuItem("Brawl Arena/Invector Migration/Apply Weapon Pose Overrides")]
        public static void ApplyAll()
        {
            ApplyWizard(InvectorRimeMigrationBuilder.OverrideControllerPath);

            var thorn = RequireController(
                InvectorThornMigrationBuilder.OverrideControllerPath);
            InvectorThornMigrationBuilder.ConfigureAttackOverrides(thorn);

            AssetDatabase.SaveAssets();
            Debug.Log("[InvectorWeaponPoseOverrideUtility] applied wizard and Thorn overrides.");
        }

        static void ApplyWizard(string controllerPath)
        {
            InvectorMigrationPilotBuilder.ConfigureWizardPresentationOverrides(
                RequireController(controllerPath));
        }

        static AnimatorOverrideController RequireController(string path)
        {
            var controller =
                AssetDatabase.LoadAssetAtPath<AnimatorOverrideController>(path);
            if (controller == null)
            {
                throw new System.InvalidOperationException(
                    "Missing override controller at '" + path + "'. Run the roster builders first.");
            }
            return controller;
        }
    }
}
