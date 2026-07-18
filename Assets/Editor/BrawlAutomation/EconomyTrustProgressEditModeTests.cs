using System.Collections.Generic;
using NUnit.Framework;

namespace BrawlArena.EditorAutomation
{
    public class EconomyTrustProgressEditModeTests
    {
        [TearDown]
        public void TearDown()
        {
            Progress.ClearEditorTestData();
        }

        [Test]
        public void BattleEnergyRegeneratesOverRealTimeWithoutBankingPastTheCap()
        {
            long interval = Progress.EnergyRegenSecondsPerPoint *
                            System.TimeSpan.TicksPerSecond;
            var save = new ProgressData { energy = 0, energyRegenAnchorTicks = 0 };

            // First observation only plants the anchor; no free energy.
            Assert.IsTrue(Progress.AccrueEnergyRegen(save, interval * 10));
            Assert.AreEqual(0, save.energy);
            Assert.AreEqual(interval * 10, save.energyRegenAnchorTicks);

            // Partial interval: nothing credited, anchor holds partial progress.
            Assert.IsFalse(Progress.AccrueEnergyRegen(save, interval * 10 + interval / 2));
            Assert.AreEqual(0, save.energy);

            // Two and a half intervals later: exactly two points, half banked.
            Assert.IsTrue(Progress.AccrueEnergyRegen(save, interval * 12 + interval / 2));
            Assert.AreEqual(2, save.energy);
            Assert.AreEqual(interval * 12, save.energyRegenAnchorTicks);

            // A month offline fills to capacity and never overshoots.
            Assert.IsTrue(Progress.AccrueEnergyRegen(save, interval * 40000));
            Assert.AreEqual(Progress.EnergyCapacity, save.energy);

            // While full, the anchor tracks now so no regen is banked.
            long later = interval * 40010;
            Progress.AccrueEnergyRegen(save, later);
            Assert.AreEqual(later, save.energyRegenAnchorTicks);
            save.energy = Progress.EnergyCapacity - 1;
            Assert.IsFalse(Progress.AccrueEnergyRegen(save, later + interval / 2),
                "No point may arrive earlier than one full interval after leaving the cap.");

            // A rewound clock (corrupt anchor) self-heals instead of crediting.
            save.energyRegenAnchorTicks = long.MaxValue;
            Assert.IsTrue(Progress.AccrueEnergyRegen(save, later + interval));
            Assert.AreEqual(later + interval, save.energyRegenAnchorTicks);
            Assert.AreEqual(Progress.EnergyCapacity - 1, save.energy);
        }

        [Test]
        public void LegacySaveMigrationReconstructsKnownLifetimeEarnings()
        {
            var legacy = new ProgressData
            {
                saveVersion = 1,
                coins = 70,
                characters = new List<CharacterProgress>
                {
                    new CharacterProgress
                    {
                        id = "aria",
                        level = 3,
                        points = 15,
                        skills = new List<CharacterSkillProgress>
                        {
                            new CharacterSkillProgress { id = "arcane_edge", level = 2 },
                        },
                    },
                },
            };

            Progress.UseEditorTestData(legacy);

            Assert.AreEqual(15, Progress.Get("aria").points, "Migration must preserve spendable SP.");
            Assert.AreEqual(70, Progress.Coins, "Migration must preserve the coin wallet.");
            Assert.AreEqual(285, Progress.Get("aria").lifetimePointsEarned);
            Assert.AreEqual(285, Progress.TotalBrawlerPointsEarned());
            Assert.AreEqual(160, Progress.LifetimeCoinsEarned);
            Assert.AreEqual(2, Progress.Data.saveVersion);
        }

        [Test]
        public void FirstTrustedUtcDayBaselinesHistoricalProgress()
        {
            var existingSave = new ProgressData
            {
                saveVersion = 2,
                coins = 175,
                lifetimeCoinsEarned = 640,
                characters = new List<CharacterProgress>
                {
                    new CharacterProgress
                    {
                        id = "aria",
                        level = 4,
                        points = 90,
                        lifetimePointsEarned = 510,
                    },
                },
            };

            Progress.UseEditorTestData(existingSave, utcDay: 75);

            Assert.AreEqual(0, Progress.DailyBrawlerPointsEarned);
            Assert.AreEqual(0, Progress.DailyCoinsEarned);
            Assert.AreEqual(0, Progress.DailyBrawlerLevelsGained);
        }

        [Test]
        public void SpendingBalancesDoesNotLowerLifetimeOrTrophyProgress()
        {
            SeedCurrentSave(points: 200, coins: 200);
            int lifetimePoints = Progress.TotalBrawlerPointsEarned();
            int lifetimeCoins = Progress.LifetimeCoinsEarned;
            int trophiesBefore = Progress.TrophyEstimate();

            Assert.IsTrue(Progress.TryUpgradeSkill("aria", "arcane_edge"));
            Assert.IsTrue(Progress.TrySpendCoins(50));
            Assert.IsTrue(Progress.TryUpgrade("aria"));

            Assert.AreEqual(110, Progress.Get("aria").points);
            Assert.AreEqual(120, Progress.Coins);
            Assert.AreEqual(lifetimePoints, Progress.TotalBrawlerPointsEarned());
            Assert.AreEqual(lifetimeCoins, Progress.LifetimeCoinsEarned);
            Assert.GreaterOrEqual(Progress.TrophyEstimate(), trophiesBefore);
        }

        [Test]
        public void MatchAwardsIncreaseBalancesAndLifetimeTogether()
        {
            SeedCurrentSave(points: 10, coins: 20);

            var payout = Progress.AwardMatch("aria", won: true, kills: 2);

            Assert.AreEqual(46, payout.points);
            Assert.AreEqual(35, payout.coins);
            Assert.AreEqual(56, Progress.Get("aria").points);
            Assert.AreEqual(55, Progress.Coins);
            Assert.AreEqual(56, Progress.TotalBrawlerPointsEarned());
            Assert.AreEqual(55, Progress.LifetimeCoinsEarned);
        }

        [Test]
        public void BattleEnergyApiUsesCanonicalCostAndDoesNotOverdraw()
        {
            SeedCurrentSave(points: 0, coins: 0, energy: Progress.BattleEnergyCost);

            Assert.IsTrue(Progress.TrySpendBattleEnergy());
            Assert.AreEqual(0, Progress.Energy);
            Assert.IsFalse(Progress.TrySpendBattleEnergy());
            Assert.AreEqual(0, Progress.Energy);
        }

        [Test]
        public void DailyQuestClaimsResetOnlyOnTheNextTrustedUtcDay()
        {
            SeedCurrentSave(points: 0, coins: 0, utcDay: 100);
            Progress.MarkQuestClaimed(1);
            Assert.IsTrue(Progress.IsQuestClaimed(1));

            Progress.SetEditorTestUtcDay(100);
            Assert.IsTrue(Progress.IsQuestClaimed(1));

            Progress.SetEditorTestUtcDay(101);
            Assert.IsFalse(Progress.IsQuestClaimed(1));
            Assert.AreEqual(0, Progress.DailyBrawlerPointsEarned);
            Assert.AreEqual(0, Progress.DailyCoinsEarned);

            Progress.AddCharacterPoints("aria", 12);
            Progress.AddCoins(9);
            Assert.AreEqual(12, Progress.DailyBrawlerPointsEarned);
            Assert.AreEqual(9, Progress.DailyCoinsEarned);
        }

        [Test]
        public void LoginRewardsAreSequentialAndLimitedToOnePerUtcDay()
        {
            SeedCurrentSave(points: 0, coins: 0, utcDay: 200);

            Assert.IsTrue(Progress.CanClaimLoginReward(0));
            Assert.IsTrue(Progress.TryClaimLoginReward(0));
            Assert.IsFalse(Progress.TryClaimLoginReward(1));
            Assert.AreEqual(1, Progress.LoginRewardIndex);

            Progress.SetEditorTestUtcDay(201);
            Assert.IsTrue(Progress.TryClaimLoginReward(1));
            Assert.AreEqual(2, Progress.LoginRewardIndex);
            Assert.AreEqual(2, Progress.LoginRewardStreak);
        }

        [Test]
        public void MissingADayResetsAnUnfinishedLoginTrack()
        {
            SeedCurrentSave(points: 0, coins: 0, utcDay: 300);
            Assert.IsTrue(Progress.TryClaimLoginReward(0));

            Progress.SetEditorTestUtcDay(302);

            Assert.AreEqual(0, Progress.LoginRewardIndex);
            Assert.AreEqual(0, Progress.LoginRewardStreak);
            Assert.IsFalse(Progress.IsRewardClaimed(0));
            Assert.IsTrue(Progress.CanClaimLoginReward(0));
        }

        [Test]
        public void ClockRollbackBlocksClaimsWithoutResettingTrustedState()
        {
            SeedCurrentSave(points: 0, coins: 0, utcDay: 400);
            Assert.IsTrue(Progress.TryClaimLoginReward(0));

            Progress.SetEditorTestUtcDay(399);

            Assert.IsTrue(Progress.LoginClockRollbackDetected);
            Assert.IsFalse(Progress.CanClaimLoginReward(1));
            Assert.AreEqual(1, Progress.LoginRewardIndex);
            Assert.IsTrue(Progress.IsRewardClaimed(0));
        }

        static void SeedCurrentSave(int points, int coins, int energy = 60, int? utcDay = null)
        {
            var data = new ProgressData
            {
                saveVersion = 2,
                coins = coins,
                lifetimeCoinsEarned = coins,
                energy = energy,
                equippedCardMask = 7,
                characters = new List<CharacterProgress>
                {
                    new CharacterProgress
                    {
                        id = "aria",
                        level = 1,
                        points = points,
                        lifetimePointsEarned = points,
                    },
                },
            };
            if (utcDay.HasValue) Progress.UseEditorTestData(data, utcDay.Value);
            else Progress.UseEditorTestData(data);
        }
    }
}
