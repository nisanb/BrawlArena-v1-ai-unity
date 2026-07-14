using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

namespace BrawlArena.EditorAutomation
{
    public class CombatFeedbackEditModeTests
    {
        bool hadSfxPreference;
        bool hadHapticsPreference;
        int previousSfxPreference;
        int previousHapticsPreference;

        [SetUp]
        public void SetUp()
        {
            hadSfxPreference = PlayerPrefs.HasKey(FeedbackSettings.SfxPreferenceKey);
            hadHapticsPreference = PlayerPrefs.HasKey(FeedbackSettings.HapticsPreferenceKey);
            previousSfxPreference = PlayerPrefs.GetInt(FeedbackSettings.SfxPreferenceKey, 1);
            previousHapticsPreference = PlayerPrefs.GetInt(FeedbackSettings.HapticsPreferenceKey, 1);
            CombatFeedback.ResetForTests();
        }

        [TearDown]
        public void TearDown()
        {
            RestorePreference(FeedbackSettings.SfxPreferenceKey, hadSfxPreference, previousSfxPreference);
            RestorePreference(FeedbackSettings.HapticsPreferenceKey, hadHapticsPreference, previousHapticsPreference);
            PlayerPrefs.Save();
            CombatFeedback.ResetForTests();
        }

        [Test]
        public void SettingsDefaultOnAndSavedValuesOverrideDefaults()
        {
            PlayerPrefs.DeleteKey(FeedbackSettings.SfxPreferenceKey);
            PlayerPrefs.DeleteKey(FeedbackSettings.HapticsPreferenceKey);

            Assert.IsTrue(FeedbackSettings.SfxEnabled);
            Assert.IsTrue(FeedbackSettings.HapticsEnabled);

            PlayerPrefs.SetInt(FeedbackSettings.SfxPreferenceKey, 0);
            PlayerPrefs.SetInt(FeedbackSettings.HapticsPreferenceKey, 0);
            Assert.IsFalse(FeedbackSettings.SfxEnabled);
            Assert.IsFalse(FeedbackSettings.HapticsEnabled);

            PlayerPrefs.SetInt(FeedbackSettings.SfxPreferenceKey, 1);
            PlayerPrefs.SetInt(FeedbackSettings.HapticsPreferenceKey, 1);
            Assert.IsTrue(FeedbackSettings.SfxEnabled);
            Assert.IsTrue(FeedbackSettings.HapticsEnabled);
        }

        [Test]
        public void TestOverridesDoNotMutateSavedPreferences()
        {
            PlayerPrefs.SetInt(FeedbackSettings.SfxPreferenceKey, 1);
            PlayerPrefs.SetInt(FeedbackSettings.HapticsPreferenceKey, 0);

            FeedbackSettings.SetTestOverrides(false, true);
            Assert.IsFalse(FeedbackSettings.SfxEnabled);
            Assert.IsTrue(FeedbackSettings.HapticsEnabled);
            Assert.AreEqual(1, PlayerPrefs.GetInt(FeedbackSettings.SfxPreferenceKey));
            Assert.AreEqual(0, PlayerPrefs.GetInt(FeedbackSettings.HapticsPreferenceKey));

            FeedbackSettings.SetTestOverrides(null, null);
            Assert.IsTrue(FeedbackSettings.SfxEnabled);
            Assert.IsFalse(FeedbackSettings.HapticsEnabled);
        }

        [Test]
        public void ReportsEverySemanticEventAndThrottlesOnlyImpactHardwareCalls()
        {
            double now = 10d;
            var backend = new RecordingHapticBackend();
            var reported = new List<CombatFeedbackEvent>();
            CombatFeedback.ConfigureForTests(backend, () => now);
            FeedbackSettings.SetTestOverrides(null, true);
            CombatFeedback.Reported += reported.Add;

            CombatFeedback.ReportLocalDealtHit();
            now += CombatFeedback.ImpactHapticThrottleSeconds * 0.5d;
            CombatFeedback.ReportLocalReceivedHit();
            now += CombatFeedback.ImpactHapticThrottleSeconds;
            CombatFeedback.ReportLocalReceivedHit();
            CombatFeedback.ReportLocalKnockout();
            CombatFeedback.ReportLocalSuper();

            CollectionAssert.AreEqual(new[]
            {
                CombatFeedbackEvent.LocalDealtHit,
                CombatFeedbackEvent.LocalReceivedHit,
                CombatFeedbackEvent.LocalReceivedHit,
                CombatFeedbackEvent.LocalKnockout,
                CombatFeedbackEvent.LocalSuper,
            }, reported);
            CollectionAssert.AreEqual(new[]
            {
                CombatFeedbackEvent.LocalDealtHit,
                CombatFeedbackEvent.LocalReceivedHit,
                CombatFeedbackEvent.LocalKnockout,
                CombatFeedbackEvent.LocalSuper,
            }, backend.Calls);
        }

        [Test]
        public void DisabledHapticsStillReportSemanticsWithoutCallingBackend()
        {
            var backend = new RecordingHapticBackend();
            var reported = new List<CombatFeedbackEvent>();
            CombatFeedback.ConfigureForTests(backend, () => 1d);
            FeedbackSettings.SetTestOverrides(null, false);
            CombatFeedback.Reported += reported.Add;

            CombatFeedback.ReportLocalDealtHit();
            CombatFeedback.ReportLocalReceivedHit();
            CombatFeedback.ReportLocalKnockout();
            CombatFeedback.ReportLocalSuper();

            Assert.AreEqual(4, reported.Count);
            Assert.IsEmpty(backend.Calls);
        }

        [Test]
        public void DisabledSfxDoesNotStartOneShotOrEmbeddedPlayback()
        {
            FeedbackSettings.SetTestOverrides(false, null);
            var root = new GameObject("FeedbackAudioTest");
            var source = root.AddComponent<AudioSource>();
            source.playOnAwake = true;
            var clip = AudioClip.Create("FeedbackAudioTestClip", 128, 1, 8000, false);
            source.clip = clip;

            try
            {
                Assert.IsFalse(CombatFeedback.TryPlaySfx(source, clip));
                Assert.AreEqual(0, CombatFeedback.ResetEmbeddedSfx(root, true));
                Assert.IsFalse(source.isPlaying);
            }
            finally
            {
                Object.DestroyImmediate(root);
                Object.DestroyImmediate(clip);
            }
        }

        [Test]
        public void EnabledSfxAcceptsAValidOneShot()
        {
            FeedbackSettings.SetTestOverrides(true, null);
            var root = new GameObject("EnabledFeedbackAudioTest");
            var source = root.AddComponent<AudioSource>();
            source.playOnAwake = false;
            var clip = AudioClip.Create("EnabledFeedbackAudioTestClip", 128, 1, 8000, false);

            try
            {
                Assert.IsTrue(CombatFeedback.TryPlaySfx(source, clip));
            }
            finally
            {
                Object.DestroyImmediate(root);
                Object.DestroyImmediate(clip);
            }
        }

        [Test]
        public void CharacterAudioConfigurationResolvesPaletteDefaultsAndDefinitionOverrides()
        {
            CombatAudioPalette palette = CombatAudioDefaults.Palette;
            Assert.NotNull(palette, "The runtime Resources palette must be loadable.");
            Assert.NotNull(palette.DefaultAttack);
            Assert.NotNull(palette.DefaultHit);

            var overrideAttack = AudioClip.Create("OverrideAttack", 64, 1, 8000, false);
            var overrideHit = AudioClip.Create("OverrideHit", 64, 1, 8000, false);

            try
            {
                var defaults = new BrawlerDefinition
                {
                    id = "audio-default-test",
                    displayName = "Audio Default",
                };
                Assert.AreSame(palette.DefaultAttack,
                    CombatAudioDefaults.ResolveAttack(defaults.attackSfx));
                Assert.AreSame(palette.DefaultHit,
                    CombatAudioDefaults.ResolveHit(defaults.hitSfx));

                var overrides = new BrawlerDefinition
                {
                    id = "audio-override-test",
                    displayName = "Audio Override",
                    attackSfx = overrideAttack,
                    hitSfx = overrideHit,
                };
                Assert.AreSame(overrideAttack,
                    CombatAudioDefaults.ResolveAttack(overrides.attackSfx));
                Assert.AreSame(overrideHit,
                    CombatAudioDefaults.ResolveHit(overrides.hitSfx));
            }
            finally
            {
                Object.DestroyImmediate(overrideAttack);
                Object.DestroyImmediate(overrideHit);
            }
        }

        static void RestorePreference(string key, bool existed, int previousValue)
        {
            if (existed) PlayerPrefs.SetInt(key, previousValue);
            else PlayerPrefs.DeleteKey(key);
        }

        sealed class RecordingHapticBackend : IHapticFeedbackBackend
        {
            public readonly List<CombatFeedbackEvent> Calls = new List<CombatFeedbackEvent>();
            public bool IsAvailable => true;
            public void Vibrate(CombatFeedbackEvent feedbackEvent) => Calls.Add(feedbackEvent);
        }
    }
}
