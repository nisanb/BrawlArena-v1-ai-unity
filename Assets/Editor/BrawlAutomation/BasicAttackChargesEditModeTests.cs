using System.IO;
using System.Reflection;
using NUnit.Framework;
using TMPro;
using UnityEngine;

namespace BrawlArena.EditorAutomation.Tests
{
    [Category("BasicAttackCharges")]
    public class BasicAttackChargesEditModeTests
    {
        [Test]
        public void PoolStartsAtThreeAndFailedFourthSpendDoesNotMutateIt()
        {
            int charges = MobileCombatRules.BasicAttackChargeCapacity;

            Assert.That(charges, Is.EqualTo(3));
            Assert.That(MobileCombatRules.TrySpendBasicAttackCharge(ref charges), Is.True);
            Assert.That(charges, Is.EqualTo(2));
            Assert.That(MobileCombatRules.TrySpendBasicAttackCharge(ref charges), Is.True);
            Assert.That(MobileCombatRules.TrySpendBasicAttackCharge(ref charges), Is.True);
            Assert.That(charges, Is.Zero);
            Assert.That(MobileCombatRules.TrySpendBasicAttackCharge(ref charges), Is.False);
            Assert.That(charges, Is.Zero);
        }

        [Test]
        public void ReloadAdvancesSequentiallyRetainsPartialProgressAndClampsAtThree()
        {
            int charges = 1;
            float elapsed = 0f;
            const float interval = 2f;

            MobileCombatRules.RegenerateBasicAttackCharges(ref charges, ref elapsed,
                interval, 1.25f);
            Assert.That(charges, Is.EqualTo(1));
            Assert.That(elapsed, Is.EqualTo(1.25f).Within(0.0001f));

            // Spending another shot does not restart the charge already reloading.
            Assert.That(MobileCombatRules.TrySpendBasicAttackCharge(ref charges), Is.True);
            MobileCombatRules.RegenerateBasicAttackCharges(ref charges, ref elapsed,
                interval, 0.75f);
            Assert.That(charges, Is.EqualTo(1));
            Assert.That(elapsed, Is.Zero.Within(0.0001f));

            MobileCombatRules.RegenerateBasicAttackCharges(ref charges, ref elapsed,
                interval, 2f);
            Assert.That(charges, Is.EqualTo(2));
            MobileCombatRules.RegenerateBasicAttackCharges(ref charges, ref elapsed,
                interval, 20f);
            Assert.That(charges, Is.EqualTo(3));
            Assert.That(elapsed, Is.Zero.Within(0.0001f));
        }

        [Test]
        public void LegacyZeroReloadIntervalUsesProductionDefault()
        {
            int charges = 2;
            float elapsed = 0f;

            MobileCombatRules.RegenerateBasicAttackCharges(ref charges, ref elapsed,
                0f, MobileCombatRules.BasicAttackReloadInterval - 0.01f);
            Assert.That(charges, Is.EqualTo(2));
            MobileCombatRules.RegenerateBasicAttackCharges(ref charges, ref elapsed,
                0f, 0.01f);
            Assert.That(charges, Is.EqualTo(3));
        }

        [Test]
        public void HudBuildsThreeChargePipsAndAVisibleReloadState()
        {
            var hudObject = new GameObject("BasicAttackChargeHudTest");
            hudObject.SetActive(false);
            var hud = hudObject.AddComponent<BrawlHUD>();
            var gameplay = new GameObject("GameplayRoot", typeof(RectTransform));
            try
            {
                MethodInfo builder = typeof(BrawlHUD).GetMethod("BuildAttackButton",
                    BindingFlags.Instance | BindingFlags.NonPublic);
                Assert.That(builder, Is.Not.Null);
                builder.Invoke(hud, new object[] { gameplay.transform });

                Transform cast = gameplay.transform.Find("CastButton");
                Assert.That(cast, Is.Not.Null);
                Transform row = cast.Find("BasicAttackCharges");
                Assert.That(row, Is.Not.Null);
                Assert.That(row.childCount,
                    Is.EqualTo(MobileCombatRules.BasicAttackChargeCapacity));
                for (int i = 0; i < row.childCount; i++)
                {
                    ImageAssert(row.GetChild(i));
                }

                TextMeshProUGUI reload = cast.Find("ReloadState")
                    ?.GetComponent<TextMeshProUGUI>();
                Assert.That(reload, Is.Not.Null);
                Assert.That(reload.text, Does.Contain("3 / 3").And.Contain("READY"));
            }
            finally
            {
                Object.DestroyImmediate(gameplay);
                Object.DestroyImmediate(hudObject);
            }
        }

        [Test]
        public void AiUsesTheSharedFacadeGateWithoutItsOwnChargeMutation()
        {
            string source = ReadProjectSource("Assets/Scripts/Brawl/AIBrawler.cs");

            StringAssert.Contains("self.BasicAttackReady", source);
            StringAssert.Contains("self.TryAttack(target)", source);
            StringAssert.DoesNotContain("TrySpendBasicAttackCharge", source);
            StringAssert.DoesNotContain("basicAttackCharges", source);
        }

        [Test]
        public void RespawnAndRoundStartCallTheSameDeterministicReset()
        {
            string controller = ReadProjectSource("Assets/Scripts/Brawl/BrawlerController.cs");
            string progression = ReadProjectSource("Assets/Scripts/Brawl/MatchProgression.cs");

            StringAssert.Contains("Health.Revive();", controller);
            StringAssert.Contains("ResetBasicAttackCharges();", controller);
            StringAssert.Contains("brawler.ResetBasicAttackCharges();", progression);
        }

        [Test]
        public void AcceptanceGuardsPrecedeSpendAndSuperAndWardPathsNeverSpend()
        {
            string source = ReadProjectSource("Assets/Scripts/Brawl/BrawlerController.cs");
            string begin = Extract(source,
                "bool BeginAttack(BrawlerController target, Vector3 worldDirection)",
                "public bool TrySuperAuto()");
            Assert.That(begin.IndexOf("if (!BasicAttackReady) return false;"),
                Is.LessThan(begin.IndexOf("if (!TryConsumeBasicAttackCharge()) return false;")));
            Assert.That(begin.IndexOf("if (worldDirection.sqrMagnitude <= 0.0001f) return false;"),
                Is.LessThan(begin.IndexOf("if (!TryConsumeBasicAttackCharge()) return false;")));

            string superAndWard = Extract(source,
                "public bool TrySuperAuto()", "void FaceInstant(Vector3 worldPoint)");
            StringAssert.DoesNotContain("TryConsumeBasicAttackCharge", superAndWard);
            string ward = Extract(source,
                "public bool TryWardStep(Vector3 worldDirection)",
                "float ResolveWardStepDistance(Vector3 direction)");
            StringAssert.DoesNotContain("TryConsumeBasicAttackCharge", ward);
        }

        static void ImageAssert(Transform pip)
        {
            Assert.That(pip, Is.Not.Null);
            Assert.That(pip.GetComponent<UnityEngine.UI.Image>(), Is.Not.Null);
            Assert.That(pip.Find("Fill")?.GetComponent<UnityEngine.UI.Image>(), Is.Not.Null);
        }

        static string ReadProjectSource(string relativePath)
        {
            string projectRoot = Directory.GetParent(Application.dataPath)?.FullName;
            Assert.That(projectRoot, Is.Not.Null);
            return File.ReadAllText(Path.Combine(projectRoot, relativePath));
        }

        static string Extract(string source, string start, string end)
        {
            int startIndex = source.IndexOf(start, System.StringComparison.Ordinal);
            int endIndex = source.IndexOf(end, startIndex + start.Length,
                System.StringComparison.Ordinal);
            Assert.That(startIndex, Is.GreaterThanOrEqualTo(0), start);
            Assert.That(endIndex, Is.GreaterThan(startIndex), end);
            return source.Substring(startIndex, endIndex - startIndex);
        }
    }
}
