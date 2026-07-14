using System;
using UnityEngine;

namespace BrawlArena
{
    public enum MobileQualityTier
    {
        Low = 0,
        Medium = 1,
        High = 2
    }

    /// <summary>
    /// Stable, non-identifying hardware signals used for automatic quality selection.
    /// Unknown numeric values are represented as zero and treated as neutral evidence.
    /// </summary>
    public readonly struct MobileDeviceProfile : IEquatable<MobileDeviceProfile>
    {
        public bool IsMobilePlatform { get; }
        public int SystemMemoryMB { get; }
        public int GraphicsMemoryMB { get; }
        public int ProcessorCount { get; }
        public int GraphicsShaderLevel { get; }
        public bool SupportsComputeShaders { get; }

        public MobileDeviceProfile(bool isMobilePlatform, int systemMemoryMB,
            int graphicsMemoryMB, int processorCount, int graphicsShaderLevel,
            bool supportsComputeShaders)
        {
            IsMobilePlatform = isMobilePlatform;
            SystemMemoryMB = Math.Max(0, systemMemoryMB);
            GraphicsMemoryMB = Math.Max(0, graphicsMemoryMB);
            ProcessorCount = Math.Max(0, processorCount);
            GraphicsShaderLevel = Math.Max(0, graphicsShaderLevel);
            SupportsComputeShaders = supportsComputeShaders;
        }

        public bool Equals(MobileDeviceProfile other)
        {
            return IsMobilePlatform == other.IsMobilePlatform &&
                   SystemMemoryMB == other.SystemMemoryMB &&
                   GraphicsMemoryMB == other.GraphicsMemoryMB &&
                   ProcessorCount == other.ProcessorCount &&
                   GraphicsShaderLevel == other.GraphicsShaderLevel &&
                   SupportsComputeShaders == other.SupportsComputeShaders;
        }

        public override bool Equals(object obj)
        {
            return obj is MobileDeviceProfile other && Equals(other);
        }

        public override int GetHashCode()
        {
            unchecked
            {
                int hash = IsMobilePlatform ? 1 : 0;
                hash = (hash * 397) ^ SystemMemoryMB;
                hash = (hash * 397) ^ GraphicsMemoryMB;
                hash = (hash * 397) ^ ProcessorCount;
                hash = (hash * 397) ^ GraphicsShaderLevel;
                hash = (hash * 397) ^ (SupportsComputeShaders ? 1 : 0);
                return hash;
            }
        }

        public override string ToString()
        {
            return $"Mobile={IsMobilePlatform}, RAM={SystemMemoryMB}MB, " +
                   $"VRAM={GraphicsMemoryMB}MB, CPU={ProcessorCount}, " +
                   $"Shader={GraphicsShaderLevel}, Compute={SupportsComputeShaders}";
        }
    }

    /// <summary>
    /// Captures a privacy-safe device profile and deterministically maps it to a tier.
    /// The policy deliberately avoids model/vendor strings, frame timing, resolution,
    /// battery state, and thermal state so the same input always yields the same tier.
    /// </summary>
    public static class MobileDeviceProfiler
    {
        public static MobileDeviceProfile CaptureCurrent()
        {
            return new MobileDeviceProfile(
                Application.isMobilePlatform,
                SystemInfo.systemMemorySize,
                SystemInfo.graphicsMemorySize,
                SystemInfo.processorCount,
                SystemInfo.graphicsShaderLevel,
                SystemInfo.supportsComputeShaders);
        }

        public static MobileQualityTier Classify(MobileDeviceProfile profile)
        {
            // Mobile tuning is intentionally opt-in by platform. PC/editor builds
            // retain the project's authored quality level in automatic mode.
            if (!profile.IsMobilePlatform) return MobileQualityTier.High;

            // These floors indicate devices where memory or shader capability is
            // independently restrictive enough to warrant the low preset.
            if (IsKnownAtMost(profile.SystemMemoryMB, 2048) ||
                IsKnownAtMost(profile.GraphicsMemoryMB, 512) ||
                IsKnownAtMost(profile.GraphicsShaderLevel, 30))
                return MobileQualityTier.Low;

            int score = 0;
            score += ScoreSignal(profile.SystemMemoryMB, 3072, 6144, 2);
            score += ScoreSignal(profile.GraphicsMemoryMB, 768, 2048, 2);
            score += ScoreSignal(profile.ProcessorCount, 4, 8, 1);
            score += ScoreSignal(profile.GraphicsShaderLevel, 35, 45, 1);
            score += profile.SupportsComputeShaders ? 1 : -1;

            if (score <= -4) return MobileQualityTier.Low;
            if (score >= 5) return MobileQualityTier.High;
            return MobileQualityTier.Medium;
        }

        static bool IsKnownAtMost(int value, int maximum)
        {
            return value > 0 && value <= maximum;
        }

        static int ScoreSignal(int value, int lowMaximum, int highMinimum, int weight)
        {
            if (value <= 0) return 0;
            if (value <= lowMaximum) return -weight;
            if (value >= highMinimum) return weight;
            return 0;
        }
    }
}
