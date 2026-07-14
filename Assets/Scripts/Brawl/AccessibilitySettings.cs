using System;
using UnityEngine;

namespace BrawlArena
{
    /// <summary>
    /// Persisted visual-comfort preferences shared by menu, gameplay, and onboarding.
    /// Values are cached after first read so camera feedback can query them every frame.
    /// </summary>
    public static class AccessibilitySettings
    {
        public const string ReducedMotionPreferenceKey =
            "BrawlArena.Accessibility.ReducedMotion.v1";
        public const string HighContrastPreferenceKey =
            "BrawlArena.Accessibility.HighContrast.v1";

        static bool loaded;
        static bool reducedMotion;
        static bool highContrast;

        public static event Action Changed;

        public static bool ReducedMotionEnabled
        {
            get
            {
                EnsureLoaded();
                return reducedMotion;
            }
        }

        public static bool HighContrastEnabled
        {
            get
            {
                EnsureLoaded();
                return highContrast;
            }
        }

        public static bool SetReducedMotionEnabled(bool enabled)
        {
            EnsureLoaded();
            if (reducedMotion == enabled) return true;
            reducedMotion = enabled;
            bool saved = SaveBool(ReducedMotionPreferenceKey, enabled);
            Changed?.Invoke();
            return saved;
        }

        public static bool SetHighContrastEnabled(bool enabled)
        {
            EnsureLoaded();
            if (highContrast == enabled) return true;
            highContrast = enabled;
            bool saved = SaveBool(HighContrastPreferenceKey, enabled);
            Changed?.Invoke();
            return saved;
        }

        public static bool ToggleReducedMotion()
        {
            bool next = !ReducedMotionEnabled;
            SetReducedMotionEnabled(next);
            return next;
        }

        public static bool ToggleHighContrast()
        {
            bool next = !HighContrastEnabled;
            SetHighContrastEnabled(next);
            return next;
        }

        /// <summary>Refreshes cached values after an external preference migration.</summary>
        public static void ReloadFromPreferences()
        {
            bool previousMotion = reducedMotion;
            bool previousContrast = highContrast;
            loaded = false;
            EnsureLoaded();
            if (previousMotion != reducedMotion || previousContrast != highContrast)
                Changed?.Invoke();
        }

        public static bool DecodeStoredBool(int storedValue, bool defaultValue)
        {
            if (storedValue == 0) return false;
            if (storedValue == 1) return true;
            return defaultValue;
        }

        public static string ToggleLabel(bool enabled)
        {
            return enabled ? "ON" : "OFF";
        }

#if UNITY_EDITOR
        /// <summary>Clears cached values without reading or changing PlayerPrefs.</summary>
        public static void ResetCacheForTests()
        {
            loaded = false;
            reducedMotion = false;
            highContrast = false;
        }
#endif

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetOnSubsystemRegistration()
        {
            loaded = false;
            reducedMotion = false;
            highContrast = false;
            Changed = null;
        }

        static void EnsureLoaded()
        {
            if (loaded) return;
            reducedMotion = LoadBool(ReducedMotionPreferenceKey, false);
            highContrast = LoadBool(HighContrastPreferenceKey, false);
            loaded = true;
        }

        static bool LoadBool(string key, bool defaultValue)
        {
            try
            {
                if (!PlayerPrefs.HasKey(key)) return defaultValue;
                int storedValue = PlayerPrefs.GetInt(key);
                if (storedValue == 0 || storedValue == 1)
                    return storedValue == 1;

                Debug.LogWarning($"Ignoring invalid accessibility preference '{key}': " +
                                 storedValue);
                PlayerPrefs.DeleteKey(key);
                PlayerPrefs.Save();
                return defaultValue;
            }
            catch (Exception exception)
            {
                Debug.LogWarning($"Could not load accessibility preference '{key}': " +
                                 exception.Message);
                return defaultValue;
            }
        }

        static bool SaveBool(string key, bool value)
        {
            try
            {
                PlayerPrefs.SetInt(key, value ? 1 : 0);
                PlayerPrefs.Save();
                return true;
            }
            catch (Exception exception)
            {
                Debug.LogWarning($"Could not save accessibility preference '{key}': " +
                                 exception.Message);
                return false;
            }
        }
    }
}
