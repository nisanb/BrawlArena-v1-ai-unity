using System.Reflection;
using NUnit.Framework;
using UnityEngine;

namespace BrawlArena.EditorAutomation
{
    public class OnboardingAccessibilityEditModeTests
    {
        PreferenceSnapshot coachPreference;
        PreferenceSnapshot reducedMotionPreference;
        PreferenceSnapshot highContrastPreference;

        [SetUp]
        public void SetUp()
        {
            coachPreference = Capture(GameplayCoachState.CompletionPreferenceKey);
            reducedMotionPreference = Capture(AccessibilitySettings.ReducedMotionPreferenceKey);
            highContrastPreference = Capture(AccessibilitySettings.HighContrastPreferenceKey);
            AccessibilitySettings.ResetCacheForTests();
        }

        [TearDown]
        public void TearDown()
        {
            Restore(GameplayCoachState.CompletionPreferenceKey, coachPreference);
            Restore(AccessibilitySettings.ReducedMotionPreferenceKey, reducedMotionPreference);
            Restore(AccessibilitySettings.HighContrastPreferenceKey, highContrastPreference);
            PlayerPrefs.Save();
            AccessibilitySettings.ResetCacheForTests();
        }

        [TestCase(0, 1, false, true)]
        [TestCase(1, 1, false, false)]
        [TestCase(1, 2, false, true)]
        [TestCase(99, 1, false, false)]
        [TestCase(0, 1, true, false)]
        public void CoachVersionPolicyOnlyShowsIncompleteContentOutsideAutomation(
            int completedVersion, int contentVersion, bool automation, bool expected)
        {
            Assert.AreEqual(expected, GameplayCoachState.ShouldShowVersion(
                completedVersion, contentVersion, automation));
        }

        [Test]
        public void CoachCompletionAndReplayArePersisted()
        {
            PlayerPrefs.DeleteKey(GameplayCoachState.CompletionPreferenceKey);
            Assert.IsTrue(GameplayCoachState.ShouldShow(false));

            Assert.IsTrue(GameplayCoachState.MarkCompleted());
            Assert.AreEqual(GameplayCoachState.CurrentVersion,
                PlayerPrefs.GetInt(GameplayCoachState.CompletionPreferenceKey));
            Assert.IsFalse(GameplayCoachState.ShouldShow(false));

            Assert.IsTrue(GameplayCoachState.RequestReplay());
            Assert.IsFalse(PlayerPrefs.HasKey(GameplayCoachState.CompletionPreferenceKey));
            Assert.IsTrue(GameplayCoachState.ShouldShow(false));
        }

        [Test]
        public void CoachContainsFourReadableControlPages()
        {
            string[] expected = { "MOVE", "AIM & ATTACK", "DASH", "SUPER" };
            Assert.AreEqual(expected.Length, GameplayCoachState.PageCount);

            for (int i = 0; i < expected.Length; i++)
            {
                GameplayCoachPage page = GameplayCoachState.GetPage(i);
                Assert.AreEqual(expected[i], page.Title);
                Assert.IsNotEmpty(page.Control);
                Assert.Greater(page.Body.Length, 35);
            }

            Assert.Less(GameplayCoach.CardSize.x, GameplayCoach.ReferenceResolution.x);
            Assert.Less(GameplayCoach.CardSize.y, GameplayCoach.ReferenceResolution.y);
        }

        [Test]
        public void AccessibilityPreferencesPersistAndReload()
        {
            Assert.IsTrue(AccessibilitySettings.SetReducedMotionEnabled(true));
            Assert.IsTrue(AccessibilitySettings.SetHighContrastEnabled(true));
            Assert.AreEqual(1, PlayerPrefs.GetInt(
                AccessibilitySettings.ReducedMotionPreferenceKey));
            Assert.AreEqual(1, PlayerPrefs.GetInt(
                AccessibilitySettings.HighContrastPreferenceKey));

            AccessibilitySettings.ReloadFromPreferences();
            Assert.IsTrue(AccessibilitySettings.ReducedMotionEnabled);
            Assert.IsTrue(AccessibilitySettings.HighContrastEnabled);

            Assert.IsTrue(AccessibilitySettings.SetReducedMotionEnabled(false));
            Assert.IsTrue(AccessibilitySettings.SetHighContrastEnabled(false));
            AccessibilitySettings.ReloadFromPreferences();
            Assert.IsFalse(AccessibilitySettings.ReducedMotionEnabled);
            Assert.IsFalse(AccessibilitySettings.HighContrastEnabled);
        }

        [TestCase(0, true, false)]
        [TestCase(1, false, true)]
        [TestCase(-1, true, true)]
        [TestCase(7, false, false)]
        public void AccessibilityDecoderSanitizesStoredValues(int storedValue,
            bool defaultValue, bool expected)
        {
            Assert.AreEqual(expected,
                AccessibilitySettings.DecodeStoredBool(storedValue, defaultValue));
        }

        [Test]
        public void HighContrastPaletteAndTeamCuesRemainDistinctWithoutColor()
        {
            Color normalAlly = TeamUtil.Color(TeamId.Blue, false);
            Color contrastAlly = TeamUtil.Color(TeamId.Blue, true);
            Color contrastEnemy = TeamUtil.Color(TeamId.Red, true);

            Assert.AreNotEqual(normalAlly, contrastAlly);
            Assert.AreNotEqual(contrastAlly, contrastEnemy);
            Assert.AreEqual("ALLY", TeamUtil.CueLabel(TeamId.Blue, TeamId.Blue, false));
            Assert.AreEqual("ENEMY", TeamUtil.CueLabel(TeamId.Red, TeamId.Blue, false));
            Assert.AreEqual("ALLY +", TeamUtil.CueLabel(TeamId.Blue, TeamId.Blue, true));
            Assert.AreEqual("ENEMY !", TeamUtil.CueLabel(TeamId.Red, TeamId.Blue, true));

            AccessibilitySettings.SetHighContrastEnabled(true);
            Assert.AreEqual(contrastAlly, TeamUtil.Color(TeamId.Blue));
            Assert.AreEqual("ENEMY !", TeamUtil.CueLabel(TeamId.Red, TeamId.Blue));
        }

        [Test]
        public void ReducedMotionGatesCameraShake()
        {
            var root = new GameObject("ReducedMotionCameraTest");
            var camera = root.AddComponent<BrawlCamera>();
            MethodInfo awake = typeof(BrawlCamera).GetMethod("Awake",
                BindingFlags.Instance | BindingFlags.NonPublic);
            FieldInfo amplitude = typeof(BrawlCamera).GetField("shakeAmplitude",
                BindingFlags.Instance | BindingFlags.NonPublic);

            try
            {
                Assert.NotNull(awake);
                Assert.NotNull(amplitude);
                awake.Invoke(camera, null);

                AccessibilitySettings.SetReducedMotionEnabled(true);
                BrawlCamera.Shake(0.8f, 0.5f);
                Assert.AreEqual(0f, (float)amplitude.GetValue(camera));

                AccessibilitySettings.SetReducedMotionEnabled(false);
                BrawlCamera.Shake(0.8f, 0.5f);
                Assert.AreEqual(0.8f, (float)amplitude.GetValue(camera));
            }
            finally
            {
                Object.DestroyImmediate(root);
            }
        }

        [Test]
        public void SettingsLabelsAndQualityCycleAreDeterministic()
        {
            MobileQualityMode mode = MobileQualityMode.Automatic;
            string[] labels = { "AUTO", "LOW", "MEDIUM", "HIGH" };
            for (int i = 0; i < labels.Length; i++)
            {
                Assert.AreEqual(labels[i], MobileQualitySettings.GetModeLabel(mode));
                mode = MobileQualitySettings.NextMode(mode);
            }

            Assert.AreEqual(MobileQualityMode.Automatic, mode);
            Assert.AreEqual("ON", AccessibilitySettings.ToggleLabel(true));
            Assert.AreEqual("OFF", AccessibilitySettings.ToggleLabel(false));
        }

        readonly struct PreferenceSnapshot
        {
            public readonly bool HadValue;
            public readonly int Value;

            public PreferenceSnapshot(bool hadValue, int value)
            {
                HadValue = hadValue;
                Value = value;
            }
        }

        static PreferenceSnapshot Capture(string key)
        {
            return new PreferenceSnapshot(PlayerPrefs.HasKey(key), PlayerPrefs.GetInt(key, 0));
        }

        static void Restore(string key, PreferenceSnapshot snapshot)
        {
            if (snapshot.HadValue) PlayerPrefs.SetInt(key, snapshot.Value);
            else PlayerPrefs.DeleteKey(key);
        }
    }
}
