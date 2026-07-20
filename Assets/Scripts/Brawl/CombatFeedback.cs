using System;
using UnityEngine;

namespace BrawlArena
{
    /// <summary>Player-facing combat moments that can produce haptic feedback.</summary>
    public enum CombatFeedbackEvent
    {
        LocalDealtHit,
        LocalReceivedHit,
        LocalKnockout,
        LocalSuper,
    }

    /// <summary>
    /// Injectable boundary around platform haptics. Runtime uses the mobile-only
    /// Handheld fallback; EditMode tests provide a recording implementation.
    /// </summary>
    public interface IHapticFeedbackBackend
    {
        bool IsAvailable { get; }
        void Vibrate(CombatFeedbackEvent feedbackEvent);
    }

    /// <summary>Canonical access to the feedback toggles shown in the menu.</summary>
    public static class FeedbackSettings
    {
        public const string SfxPreferenceKey = "menu_sfx";
        public const string HapticsPreferenceKey = "menu_haptics";

        static bool? sfxTestOverride;
        static bool? hapticsTestOverride;

        public static bool SfxEnabled =>
            sfxTestOverride ?? PlayerPrefs.GetInt(SfxPreferenceKey, 1) == 1;

        public static bool HapticsEnabled =>
            hapticsTestOverride ?? PlayerPrefs.GetInt(HapticsPreferenceKey, 1) == 1;

        public static void SetSfxEnabled(bool enabled)
        {
            PlayerPrefs.SetInt(SfxPreferenceKey, enabled ? 1 : 0);
            PlayerPrefs.Save();
        }

        public static void SetHapticsEnabled(bool enabled)
        {
            PlayerPrefs.SetInt(HapticsPreferenceKey, enabled ? 1 : 0);
            PlayerPrefs.Save();
        }

#if UNITY_EDITOR
        public static void SetTestOverrides(bool? sfxEnabled, bool? hapticsEnabled)
        {
            sfxTestOverride = sfxEnabled;
            hapticsTestOverride = hapticsEnabled;
        }
#endif

        internal static void ResetRuntimeState()
        {
            sfxTestOverride = null;
            hapticsTestOverride = null;
        }
    }

    /// <summary>
    /// Central audio/haptic gate for combat. Impact haptics share a short
    /// throttle window so a burst or area hit does not vibrate once per victim;
    /// the more meaningful KO and Super signals are never swallowed by it.
    /// </summary>
    public static class CombatFeedback
    {
        public const double ImpactHapticThrottleSeconds = 0.08d;

        /// <summary>
        /// Camera shake for a hit/KO only matters while it happens near what
        /// the camera is actually following; beyond this world-unit radius
        /// from the followed target it fades out entirely rather than
        /// shaking the whole screen for an offscreen skirmish.
        /// </summary>
        public const float ProximityShakeRange = 9f;

        static readonly IHapticFeedbackBackend RuntimeHapticBackend =
            new MobileHandheldHapticBackend();

        static IHapticFeedbackBackend hapticBackend = RuntimeHapticBackend;
        static Func<double> realtimeProvider = DefaultRealtime;
        static double lastImpactHapticAt = double.NegativeInfinity;

        /// <summary>
        /// Fires for every valid local feedback request, even when user settings,
        /// platform support, or throttling prevent a hardware vibration.
        /// </summary>
        public static event Action<CombatFeedbackEvent> Reported;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetOnSubsystemRegistration()
        {
            ResetState(clearSubscribers: true);
        }

        /// <summary>Plays a one-shot only when SFX are enabled and inputs are valid.</summary>
        public static bool TryPlaySfx(AudioSource source, AudioClip clip, float volumeScale = 1f)
        {
            if (!FeedbackSettings.SfxEnabled || source == null || clip == null ||
                !source.isActiveAndEnabled || volumeScale <= 0f)
                return false;

            source.PlayOneShot(clip, Mathf.Clamp01(volumeScale));
            return true;
        }

        /// <summary>
        /// Pool-friendly reset for AudioSources embedded in a prefab. It never
        /// changes authored mute/volume values: all sources are stopped, then
        /// eligible play-on-awake sources are restarted only when SFX are on.
        /// Call after activating a pooled object when restartPlayOnAwake is true.
        /// Returns the number of sources restarted.
        /// </summary>
        public static int ResetEmbeddedSfx(GameObject root, bool restartPlayOnAwake)
        {
            if (root == null) return 0;

            AudioSource[] sources = root.GetComponentsInChildren<AudioSource>(true);
            for (int i = 0; i < sources.Length; i++)
            {
                AudioSource source = sources[i];
                if (source != null) source.Stop();
            }

            if (!restartPlayOnAwake || !FeedbackSettings.SfxEnabled) return 0;

            int restarted = 0;
            for (int i = 0; i < sources.Length; i++)
            {
                AudioSource source = sources[i];
                if (source == null || !source.playOnAwake || source.clip == null ||
                    !source.isActiveAndEnabled)
                    continue;

                source.Play();
                restarted++;
            }
            return restarted;
        }

        public static void ReportLocalDealtHit() => Report(CombatFeedbackEvent.LocalDealtHit);
        public static void ReportLocalReceivedHit() => Report(CombatFeedbackEvent.LocalReceivedHit);
        public static void ReportLocalKnockout() => Report(CombatFeedbackEvent.LocalKnockout);
        public static void ReportLocalSuper() => Report(CombatFeedbackEvent.LocalSuper);

        public static void Report(CombatFeedbackEvent feedbackEvent)
        {
            Reported?.Invoke(feedbackEvent);

            if (!FeedbackSettings.HapticsEnabled || hapticBackend == null ||
                !hapticBackend.IsAvailable)
                return;

            if (IsImpact(feedbackEvent))
            {
                double now = realtimeProvider != null ? realtimeProvider() : DefaultRealtime();
                if (now - lastImpactHapticAt < ImpactHapticThrottleSeconds) return;
                lastImpactHapticAt = now;
            }

            hapticBackend.Vibrate(feedbackEvent);
        }

        static bool IsImpact(CombatFeedbackEvent feedbackEvent)
        {
            return feedbackEvent == CombatFeedbackEvent.LocalDealtHit ||
                   feedbackEvent == CombatFeedbackEvent.LocalReceivedHit;
        }

        /// <summary>
        /// Shakes the camera for any hit/KO in the world, not just ones the
        /// local player caused or received: proximity to what the camera
        /// follows is what makes an impact feel present on screen, not who
        /// is involved. Amplitude/duration fall off linearly to zero at
        /// ProximityShakeRange. BrawlCamera.Shake already gates ReducedMotion.
        /// </summary>
        public static void ReportProximityShake(Vector3 worldPosition, float amplitude, float duration)
        {
            Transform followed = ResolveCameraFollowTarget();
            if (followed == null) return;

            float distance = Vector3.Distance(followed.position, worldPosition);
            float falloff = Mathf.Clamp01(1f - distance / ProximityShakeRange);
            if (falloff <= 0f) return;

            BrawlCamera.Shake(amplitude * falloff, duration * falloff);
        }

        static Transform ResolveCameraFollowTarget()
        {
            BrawlCamera cam = UnityEngine.Object.FindFirstObjectByType<BrawlCamera>();
            if (cam != null && cam.target != null) return cam.target;
            Camera main = Camera.main;
            return main != null ? main.transform : null;
        }

        static double DefaultRealtime()
        {
            return Time.realtimeSinceStartupAsDouble;
        }

        static void ResetState(bool clearSubscribers)
        {
            hapticBackend = RuntimeHapticBackend;
            realtimeProvider = DefaultRealtime;
            lastImpactHapticAt = double.NegativeInfinity;
            FeedbackSettings.ResetRuntimeState();
            if (clearSubscribers) Reported = null;
        }

#if UNITY_EDITOR
        /// <summary>Installs deterministic, hardware-free test dependencies.</summary>
        public static void ConfigureForTests(IHapticFeedbackBackend backend, Func<double> clock)
        {
            hapticBackend = backend;
            realtimeProvider = clock ?? DefaultRealtime;
            lastImpactHapticAt = double.NegativeInfinity;
        }

        public static void ResetForTests()
        {
            ResetState(clearSubscribers: true);
        }
#endif

        sealed class MobileHandheldHapticBackend : IHapticFeedbackBackend
        {
            public bool IsAvailable => Application.isMobilePlatform;

            public void Vibrate(CombatFeedbackEvent feedbackEvent)
            {
                // Handheld only exists on mobile player builds; guard so desktop
                // builds of this (dead) code still compile
#if UNITY_IOS || UNITY_ANDROID
                if (Application.isMobilePlatform) Handheld.Vibrate();
#endif
            }
        }
    }
}
