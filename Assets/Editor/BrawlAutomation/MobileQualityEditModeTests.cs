using NUnit.Framework;

namespace BrawlArena.EditorAutomation
{
    public class MobileQualityEditModeTests
    {
        [Test]
        public void LowCapabilityMobileProfileSelectsLow()
        {
            var profile = new MobileDeviceProfile(true, 2048, 512, 4, 30, false);

            Assert.AreEqual(MobileQualityTier.Low, MobileDeviceProfiler.Classify(profile));
        }

        [Test]
        public void BalancedMobileProfileSelectsMedium()
        {
            var profile = new MobileDeviceProfile(true, 4096, 1024, 6, 40, true);

            Assert.AreEqual(MobileQualityTier.Medium, MobileDeviceProfiler.Classify(profile));
        }

        [Test]
        public void HighCapabilityMobileProfileSelectsHigh()
        {
            var profile = new MobileDeviceProfile(true, 8192, 4096, 8, 50, true);

            Assert.AreEqual(MobileQualityTier.High, MobileDeviceProfiler.Classify(profile));
        }

        [Test]
        public void MissingMobileSignalsConservativelySelectMedium()
        {
            var profile = new MobileDeviceProfile(true, 0, 0, 0, 0, false);

            Assert.AreEqual(MobileQualityTier.Medium, MobileDeviceProfiler.Classify(profile));
        }

        [Test]
        public void NonMobileAutomaticProfileKeepsHighTier()
        {
            var profile = new MobileDeviceProfile(false, 1024, 256, 2, 20, false);

            Assert.AreEqual(MobileQualityTier.High, MobileDeviceProfiler.Classify(profile));
        }

        [TestCase(MobileQualityMode.Low, MobileQualityTier.High, MobileQualityTier.Low)]
        [TestCase(MobileQualityMode.Medium, MobileQualityTier.Low, MobileQualityTier.Medium)]
        [TestCase(MobileQualityMode.High, MobileQualityTier.Low, MobileQualityTier.High)]
        [TestCase(MobileQualityMode.Automatic, MobileQualityTier.Medium, MobileQualityTier.Medium)]
        public void UserModeDeterministicallyResolvesEffectiveTier(MobileQualityMode mode,
            MobileQualityTier automaticTier, MobileQualityTier expected)
        {
            Assert.AreEqual(expected, MobileQualitySettings.ResolveTier(mode, automaticTier));
        }

        [TestCase(0, MobileQualityMode.Automatic)]
        [TestCase(1, MobileQualityMode.Low)]
        [TestCase(2, MobileQualityMode.Medium)]
        [TestCase(3, MobileQualityMode.High)]
        public void PersistedModeDecoderAcceptsKnownValues(int storedValue,
            MobileQualityMode expected)
        {
            Assert.IsTrue(MobileQualitySettings.TryDecodePersistedMode(storedValue,
                out MobileQualityMode decoded));
            Assert.AreEqual(expected, decoded);
        }

        [TestCase(-1)]
        [TestCase(4)]
        [TestCase(999)]
        public void PersistedModeDecoderRejectsCorruptValues(int storedValue)
        {
            Assert.IsFalse(MobileQualitySettings.TryDecodePersistedMode(storedValue,
                out MobileQualityMode decoded));
            Assert.AreEqual(MobileQualityMode.Automatic, decoded);
        }

        [Test]
        public void PresetsScaleCostMonotonically()
        {
            MobileQualityPreset low = MobileQualitySettings.GetPreset(MobileQualityTier.Low);
            MobileQualityPreset medium = MobileQualitySettings.GetPreset(MobileQualityTier.Medium);
            MobileQualityPreset high = MobileQualitySettings.GetPreset(MobileQualityTier.High);

            Assert.Less(low.TargetFrameRate, medium.TargetFrameRate);
            Assert.Less(low.ResolutionScale, medium.ResolutionScale);
            Assert.Less(medium.ResolutionScale, high.ResolutionScale);
            Assert.Less(low.LodBias, medium.LodBias);
            Assert.Less(medium.LodBias, high.LodBias);
            Assert.Less(low.ParticleRaycastBudget, medium.ParticleRaycastBudget);
            Assert.Less(medium.ParticleRaycastBudget, high.ParticleRaycastBudget);
            Assert.Greater(low.GlobalTextureMipmapLimit, high.GlobalTextureMipmapLimit);
        }

        [TestCase(false, MobileQualityMode.Automatic, false)]
        [TestCase(false, MobileQualityMode.Low, true)]
        [TestCase(false, MobileQualityMode.Medium, true)]
        [TestCase(false, MobileQualityMode.High, true)]
        [TestCase(true, MobileQualityMode.Automatic, true)]
        public void RenderingPolicyPreservesOnlyAutomaticDesktopQuality(bool isMobile,
            MobileQualityMode mode, bool expected)
        {
            var profile = new MobileDeviceProfile(isMobile, 4096, 1024, 6, 40, true);

            Assert.AreEqual(expected,
                MobileQualitySettings.ShouldApplyRenderingSettings(profile, mode));
        }
    }
}
