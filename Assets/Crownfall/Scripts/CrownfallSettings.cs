using UnityEngine;

namespace Crownfall
{
    /// Player-facing options, persisted via PlayerPrefs and applied globally.
    public static class CrownfallSettings
    {
        public static float Volume = 0.8f;
        public static float Sensitivity = 1f;
        public static bool ShakeEnabled = true;

        /// How the on-screen (mobile) controls behave.
        ///   Auto   — hidden on desktop until a real touch happens, always on mobile
        ///   Always — force the mobile layout on every platform (owner's PC testing)
        ///   Never  — keyboard/mouse only
        public enum VirtualControlMode { Auto = 0, Always = 1, Never = 2 }
        public static VirtualControlMode VirtualControls = VirtualControlMode.Auto;

        const string KeyVol = "cf_volume";
        const string KeySens = "cf_sensitivity";
        const string KeyShake = "cf_shake";
        const string KeyVirtual = "cf_virtualcontrols";

        public static void Load()
        {
            Volume = PlayerPrefs.GetFloat(KeyVol, 0.8f);
            Sensitivity = PlayerPrefs.GetFloat(KeySens, 1f);
            ShakeEnabled = PlayerPrefs.GetInt(KeyShake, 1) == 1;
            VirtualControls = (VirtualControlMode)Mathf.Clamp(PlayerPrefs.GetInt(KeyVirtual, 0), 0, 2);
            Apply();
        }

        public static void Save()
        {
            PlayerPrefs.SetFloat(KeyVol, Volume);
            PlayerPrefs.SetFloat(KeySens, Sensitivity);
            PlayerPrefs.SetInt(KeyShake, ShakeEnabled ? 1 : 0);
            PlayerPrefs.SetInt(KeyVirtual, (int)VirtualControls);
            PlayerPrefs.Save();
        }

        public static void Apply()
        {
            AudioListener.volume = Volume;
        }
    }
}
