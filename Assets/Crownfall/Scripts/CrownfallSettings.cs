using UnityEngine;

namespace Crownfall
{
    /// Player-facing options, persisted via PlayerPrefs and applied globally.
    public static class CrownfallSettings
    {
        public static float Volume = 0.8f;
        public static float Sensitivity = 1f;
        public static bool ShakeEnabled = true;

        const string KeyVol = "cf_volume";
        const string KeySens = "cf_sensitivity";
        const string KeyShake = "cf_shake";

        public static void Load()
        {
            Volume = PlayerPrefs.GetFloat(KeyVol, 0.8f);
            Sensitivity = PlayerPrefs.GetFloat(KeySens, 1f);
            ShakeEnabled = PlayerPrefs.GetInt(KeyShake, 1) == 1;
            Apply();
        }

        public static void Save()
        {
            PlayerPrefs.SetFloat(KeyVol, Volume);
            PlayerPrefs.SetFloat(KeySens, Sensitivity);
            PlayerPrefs.SetInt(KeyShake, ShakeEnabled ? 1 : 0);
            PlayerPrefs.Save();
        }

        public static void Apply()
        {
            AudioListener.volume = Volume;
        }
    }
}
