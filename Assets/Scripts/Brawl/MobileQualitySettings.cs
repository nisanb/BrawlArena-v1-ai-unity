using System;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace BrawlArena
{
    public enum MobileQualityMode
    {
        Automatic = 0,
        Low = 1,
        Medium = 2,
        High = 3
    }

    /// <summary>Concrete runtime values associated with one mobile quality tier.</summary>
    public readonly struct MobileQualityPreset
    {
        public MobileQualityTier Tier { get; }
        public int TargetFrameRate { get; }
        public float ResolutionScale { get; }
        public float LodBias { get; }
        public float ShadowDistance { get; }
        public int GlobalTextureMipmapLimit { get; }
        public int ParticleRaycastBudget { get; }
        public ShadowQuality Shadows { get; }
        public AnisotropicFiltering AnisotropicFiltering { get; }

        public MobileQualityPreset(MobileQualityTier tier, int targetFrameRate,
            float resolutionScale, float lodBias, float shadowDistance,
            int globalTextureMipmapLimit, int particleRaycastBudget,
            ShadowQuality shadows, AnisotropicFiltering anisotropicFiltering)
        {
            Tier = tier;
            TargetFrameRate = targetFrameRate;
            ResolutionScale = resolutionScale;
            LodBias = lodBias;
            ShadowDistance = shadowDistance;
            GlobalTextureMipmapLimit = globalTextureMipmapLimit;
            ParticleRaycastBudget = particleRaycastBudget;
            Shadows = shadows;
            AnisotropicFiltering = anisotropicFiltering;
        }
    }

    /// <summary>
    /// Runtime facade for automatic profiling, persisted user selection, and
    /// platform-safe application of mobile rendering settings.
    /// </summary>
    public static class MobileQualitySettings
    {
        // The version suffix allows a future policy migration without interpreting
        // values written under a different enum or persistence contract.
        public const string PreferenceKey = "BrawlArena.MobileQuality.Mode.v1";

        static readonly MobileQualityPreset LowPreset = new MobileQualityPreset(
            MobileQualityTier.Low, 30, 0.72f, 0.65f, 0f, 1, 64,
            ShadowQuality.Disable, AnisotropicFiltering.Disable);

        static readonly MobileQualityPreset MediumPreset = new MobileQualityPreset(
            MobileQualityTier.Medium, 60, 0.85f, 0.9f, 22f, 0, 128,
            ShadowQuality.HardOnly, AnisotropicFiltering.Enable);

        static readonly MobileQualityPreset HighPreset = new MobileQualityPreset(
            MobileQualityTier.High, 60, 1f, 1.25f, 40f, 0, 256,
            ShadowQuality.All, AnisotropicFiltering.ForceEnable);

        static bool initialized;
        static MobileQualityMode mode = MobileQualityMode.Automatic;
        static MobileQualityTier automaticTier = MobileQualityTier.Medium;
        static MobileDeviceProfile currentProfile;
        static DesktopRenderingSnapshot desktopSnapshot;

        struct DesktopRenderingSnapshot
        {
            public bool Captured;
            public float LodBias;
            public float ShadowDistance;
            public int GlobalTextureMipmapLimit;
            public int ParticleRaycastBudget;
            public ShadowQuality Shadows;
            public AnisotropicFiltering AnisotropicFiltering;
        }

        /// <summary>Raised after a mode, profile, or automatic tier change is applied.</summary>
        public static event Action<MobileQualityMode, MobileQualityTier> Changed;

        public static bool IsInitialized => initialized;

        public static MobileQualityMode Mode
        {
            get
            {
                EnsureInitialized();
                return mode;
            }
        }

        public static MobileQualityTier AutomaticTier
        {
            get
            {
                EnsureInitialized();
                return automaticTier;
            }
        }

        public static MobileQualityTier EffectiveTier
        {
            get
            {
                EnsureInitialized();
                return ResolveTier(mode, automaticTier);
            }
        }

        public static MobileDeviceProfile CurrentProfile
        {
            get
            {
                EnsureInitialized();
                return currentProfile;
            }
        }

        public static MobileQualityPreset CurrentPreset => GetPreset(EffectiveTier);

        /// <summary>
        /// Initializes once, loads a validated override, profiles the device, and
        /// applies the effective preset. Safe to call repeatedly.
        /// </summary>
        public static void Initialize()
        {
            if (initialized) return;

            mode = LoadPersistedMode();
            currentProfile = MobileDeviceProfiler.CaptureCurrent();
            automaticTier = MobileDeviceProfiler.Classify(currentProfile);
            initialized = true;

            // Reapply after scene Awake calls. This keeps the bootstrap authoritative
            // if a legacy scene component writes a hard-coded frame cap while loading.
            SceneManager.sceneLoaded -= OnSceneLoaded;
            SceneManager.sceneLoaded += OnSceneLoaded;
            ApplyCurrentInternal();
        }

        /// <summary>
        /// Selects and immediately applies a mode. Returns false only when the
        /// preference could not be durably saved; the in-session selection still applies.
        /// </summary>
        public static bool SetMode(MobileQualityMode newMode)
        {
            if (!IsValidMode(newMode))
                throw new ArgumentOutOfRangeException(nameof(newMode), newMode,
                    "Unknown mobile quality mode.");

            EnsureInitialized();
            bool changed = mode != newMode;
            bool persisted = PersistMode(newMode);
            mode = newMode;
            ApplyCurrentInternal();
            if (changed) Changed?.Invoke(mode, ResolveTier(mode, automaticTier));
            return persisted;
        }

        public static bool UseAutomatic()
        {
            return SetMode(MobileQualityMode.Automatic);
        }

        /// <summary>Recaptures stable signals and reapplies automatic mode if active.</summary>
        public static void RefreshAutomaticProfile()
        {
            EnsureInitialized();
            MobileDeviceProfile nextProfile = MobileDeviceProfiler.CaptureCurrent();
            MobileQualityTier nextTier = MobileDeviceProfiler.Classify(nextProfile);
            bool changed = !currentProfile.Equals(nextProfile) || automaticTier != nextTier;

            currentProfile = nextProfile;
            automaticTier = nextTier;
            if (mode == MobileQualityMode.Automatic) ApplyCurrentInternal();
            if (changed) Changed?.Invoke(mode, ResolveTier(mode, automaticTier));
        }

        /// <summary>Reapplies the effective preset without touching persistence.</summary>
        public static void ApplyCurrent()
        {
            EnsureInitialized();
            ApplyCurrentInternal();
        }

        public static MobileQualityTier ResolveTier(MobileQualityMode selectedMode,
            MobileQualityTier profiledTier)
        {
            switch (selectedMode)
            {
                case MobileQualityMode.Automatic:
                    return profiledTier;
                case MobileQualityMode.Low:
                    return MobileQualityTier.Low;
                case MobileQualityMode.Medium:
                    return MobileQualityTier.Medium;
                case MobileQualityMode.High:
                    return MobileQualityTier.High;
                default:
                    throw new ArgumentOutOfRangeException(nameof(selectedMode), selectedMode,
                        "Unknown mobile quality mode.");
            }
        }

        public static MobileQualityPreset GetPreset(MobileQualityTier tier)
        {
            switch (tier)
            {
                case MobileQualityTier.Low:
                    return LowPreset;
                case MobileQualityTier.Medium:
                    return MediumPreset;
                case MobileQualityTier.High:
                    return HighPreset;
                default:
                    throw new ArgumentOutOfRangeException(nameof(tier), tier,
                        "Unknown mobile quality tier.");
            }
        }

        public static string GetModeLabel(MobileQualityMode selectedMode)
        {
            switch (selectedMode)
            {
                case MobileQualityMode.Automatic: return "AUTO";
                case MobileQualityMode.Low: return "LOW";
                case MobileQualityMode.Medium: return "MEDIUM";
                case MobileQualityMode.High: return "HIGH";
                default:
                    throw new ArgumentOutOfRangeException(nameof(selectedMode), selectedMode,
                        "Unknown mobile quality mode.");
            }
        }

        public static MobileQualityMode NextMode(MobileQualityMode selectedMode)
        {
            switch (selectedMode)
            {
                case MobileQualityMode.Automatic: return MobileQualityMode.Low;
                case MobileQualityMode.Low: return MobileQualityMode.Medium;
                case MobileQualityMode.Medium: return MobileQualityMode.High;
                case MobileQualityMode.High: return MobileQualityMode.Automatic;
                default:
                    throw new ArgumentOutOfRangeException(nameof(selectedMode), selectedMode,
                        "Unknown mobile quality mode.");
            }
        }

        /// <summary>
        /// Automatic desktop/editor mode preserves the authored PC quality settings.
        /// Explicit modes apply portable render knobs so they can be previewed and tested.
        /// </summary>
        public static bool ShouldApplyRenderingSettings(MobileDeviceProfile profile,
            MobileQualityMode selectedMode)
        {
            if (!IsValidMode(selectedMode))
                throw new ArgumentOutOfRangeException(nameof(selectedMode), selectedMode,
                    "Unknown mobile quality mode.");
            return profile.IsMobilePlatform || selectedMode != MobileQualityMode.Automatic;
        }

        /// <summary>Pure decoder exposed for settings UI migration and EditMode tests.</summary>
        public static bool TryDecodePersistedMode(int storedValue, out MobileQualityMode decoded)
        {
            decoded = (MobileQualityMode)storedValue;
            if (IsValidMode(decoded)) return true;
            decoded = MobileQualityMode.Automatic;
            return false;
        }

        internal static void ResetRuntimeState()
        {
            SceneManager.sceneLoaded -= OnSceneLoaded;
            RestoreDesktopRenderingSettings();
            initialized = false;
            mode = MobileQualityMode.Automatic;
            automaticTier = MobileQualityTier.Medium;
            currentProfile = default;
            Changed = null;
        }

        static void EnsureInitialized()
        {
            if (!initialized) Initialize();
        }

        static bool IsValidMode(MobileQualityMode value)
        {
            return value == MobileQualityMode.Automatic || value == MobileQualityMode.Low ||
                   value == MobileQualityMode.Medium || value == MobileQualityMode.High;
        }

        static MobileQualityMode LoadPersistedMode()
        {
            try
            {
                if (!PlayerPrefs.HasKey(PreferenceKey)) return MobileQualityMode.Automatic;

                int storedValue = PlayerPrefs.GetInt(PreferenceKey, 0);
                if (TryDecodePersistedMode(storedValue, out MobileQualityMode storedMode))
                    return storedMode;

                Debug.LogWarning($"Ignoring invalid mobile quality preference {storedValue}.");
                PlayerPrefs.DeleteKey(PreferenceKey);
                PlayerPrefs.Save();
            }
            catch (Exception exception)
            {
                Debug.LogWarning($"Could not load the mobile quality preference: " +
                                 exception.Message);
            }

            return MobileQualityMode.Automatic;
        }

        static bool PersistMode(MobileQualityMode value)
        {
            try
            {
                // Absence of the key is the durable representation of Automatic.
                // Only actual user overrides need persisted data.
                if (value == MobileQualityMode.Automatic)
                    PlayerPrefs.DeleteKey(PreferenceKey);
                else
                    PlayerPrefs.SetInt(PreferenceKey, (int)value);

                PlayerPrefs.Save();
                return true;
            }
            catch (Exception exception)
            {
                Debug.LogWarning($"Could not save the mobile quality preference: " +
                                 exception.Message);
                return false;
            }
        }

        static void OnSceneLoaded(Scene scene, LoadSceneMode loadMode)
        {
            ApplyCurrentInternal();
        }

        static void ApplyCurrentInternal()
        {
            MobileQualityPreset preset = GetPreset(ResolveTier(mode, automaticTier));
            Application.targetFrameRate = preset.TargetFrameRate;

            // Desktop quality is authored separately in ProjectSettings. Preserve
            // it in Automatic, while allowing an explicit tier to be previewed.
            if (!ShouldApplyRenderingSettings(currentProfile, mode))
            {
                RestoreDesktopRenderingSettings();
                return;
            }

            if (currentProfile.IsMobilePlatform)
            {
                // vSync is controlled by the display on mobile. Fixed-DPI scaling
                // is also mobile-specific, so neither value is touched on desktop.
                QualitySettings.vSyncCount = 0;
                QualitySettings.resolutionScalingFixedDPIFactor = preset.ResolutionScale;
            }
            else
            {
                CaptureDesktopRenderingSettings();
            }

            QualitySettings.lodBias = preset.LodBias;
            QualitySettings.shadowDistance = preset.ShadowDistance;
            QualitySettings.globalTextureMipmapLimit = preset.GlobalTextureMipmapLimit;
            QualitySettings.particleRaycastBudget = preset.ParticleRaycastBudget;
            QualitySettings.shadows = preset.Shadows;
            QualitySettings.anisotropicFiltering = preset.AnisotropicFiltering;
        }

        static void CaptureDesktopRenderingSettings()
        {
            if (desktopSnapshot.Captured) return;
            desktopSnapshot = new DesktopRenderingSnapshot
            {
                Captured = true,
                LodBias = QualitySettings.lodBias,
                ShadowDistance = QualitySettings.shadowDistance,
                GlobalTextureMipmapLimit = QualitySettings.globalTextureMipmapLimit,
                ParticleRaycastBudget = QualitySettings.particleRaycastBudget,
                Shadows = QualitySettings.shadows,
                AnisotropicFiltering = QualitySettings.anisotropicFiltering,
            };
        }

        static void RestoreDesktopRenderingSettings()
        {
            if (!desktopSnapshot.Captured) return;
            QualitySettings.lodBias = desktopSnapshot.LodBias;
            QualitySettings.shadowDistance = desktopSnapshot.ShadowDistance;
            QualitySettings.globalTextureMipmapLimit = desktopSnapshot.GlobalTextureMipmapLimit;
            QualitySettings.particleRaycastBudget = desktopSnapshot.ParticleRaycastBudget;
            QualitySettings.shadows = desktopSnapshot.Shadows;
            QualitySettings.anisotropicFiltering = desktopSnapshot.AnisotropicFiltering;
            desktopSnapshot = default;
        }
    }
}
