using UnityEngine;

namespace BrawlArena
{
    /// <summary>
    /// Owns runtime quality initialization before the first scene starts.
    /// MobileQualitySettings also reapplies its preset after every scene load.
    /// </summary>
    public static class ApplicationRuntimeBootstrap
    {
        public const int DefaultTargetFrameRate = 60;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetRuntimeState()
        {
            MobileQualitySettings.ResetRuntimeState();
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void ApplyRuntimeSettings()
        {
            MobileQualitySettings.Initialize();
        }
    }
}
