using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace BrawlArena
{
    [Serializable]
    public class CharacterSkillProgress
    {
        public string id;
        public int level;
    }

    [Serializable]
    public class CharacterProgress
    {
        public string id;
        public int level = 1;
        public int points;
        public int lifetimePointsEarned;
        public List<CharacterSkillProgress> skills = new List<CharacterSkillProgress>();
    }

    [Serializable]
    public class ProgressData
    {
        public int saveVersion;
        public int coins;
        public int lifetimeCoinsEarned;
        public int gems = 45;
        public int energy = 60;
        public int selectedMode = (int)GameMode.ControlZone;
        public string selectedCharacterId = "";
        public int equippedCardMask = 7;
        public int friendInviteMask;
        public int energyCells = 2;
        public int coinCrates = 1;
        public int brawlerTokenPacks = 1;
        public int gemPouches;
        public int claimedRewardMask;
        public int claimedQuestMask;
        public int claimedInboxMask;
        public bool utcDayStateInitialized;
        public int dailyQuestUtcDay;
        public int dailyStartLifetimePoints;
        public int dailyStartLifetimeCoins;
        public int dailyStartBrawlerLevels;
        public int lastObservedUtcDay;
        public int lastLoginRewardUtcDay;
        public int loginRewardIndex;
        public int loginRewardStreak;
        public List<CharacterProgress> characters = new List<CharacterProgress>();
    }

    /// <summary>
    /// Player progression and lobby state: per-character levels and points,
    /// resources, inventory, equipped cards, social flags, and claim history.
    /// Levels raise HP and damage at spawn. Persisted as JSON in
    /// persistentDataPath so it survives play sessions.
    /// </summary>
    public static class Progress
    {
        public const int MaxLevel = 10;
        public const int BattleEnergyCost = 5;
        public const int LoginRewardCount = 7;

        const int CurrentSaveVersion = 2;

        static ProgressData data;

        static string FilePath => Path.Combine(Application.persistentDataPath, "progress.json");

        public static ProgressData Data
        {
            get
            {
                if (data == null) Load();
                if (ApplyUtcDay(CurrentUtcDayNumber())) Save();
                return data;
            }
        }

        public static int Coins => Data.coins;
        public static int LifetimeCoinsEarned => Data.lifetimeCoinsEarned;
        public static int Gems => Data.gems;
        public static int Energy => Data.energy;
        public static GameMode SelectedMode
        {
            get
            {
                switch ((GameMode)Data.selectedMode)
                {
                    case GameMode.Knockout: return GameMode.Knockout;
                    case GameMode.GemGrab: return GameMode.GemGrab;
                    case GameMode.ControlZone: return GameMode.ControlZone;
                    default: return GameMode.ControlZone;
                }
            }
        }
        public static string SelectedCharacterId => Data.selectedCharacterId;
        public static int LoginRewardIndex => Data.loginRewardIndex;
        public static int LoginRewardStreak => Data.loginRewardStreak;
        public static bool LoginRewardTrackComplete => Data.loginRewardIndex >= LoginRewardCount;
        public static bool LoginClockRollbackDetected => CurrentUtcDayNumber() < Data.lastObservedUtcDay;
        public static bool LoginRewardAvailableToday => CanClaimLoginReward(Data.loginRewardIndex);
        public static int DailyBrawlerPointsEarned =>
            Mathf.Max(0, RawTotalBrawlerPointsEarned() - Data.dailyStartLifetimePoints);
        public static int DailyCoinsEarned =>
            Mathf.Max(0, Data.lifetimeCoinsEarned - Data.dailyStartLifetimeCoins);
        public static int DailyBrawlerLevelsGained =>
            Mathf.Max(0, RawTotalBrawlerLevels() - Data.dailyStartBrawlerLevels);

#if UNITY_EDITOR
        static bool disableSavingForTests;
        static int? editorTestUtcDay;

        public static void UseEditorTestData(ProgressData testData)
        {
            data = testData ?? new ProgressData();
            disableSavingForTests = true;
            Normalize();
        }

        public static void UseEditorTestData(ProgressData testData, int utcDay)
        {
            editorTestUtcDay = utcDay;
            UseEditorTestData(testData);
        }

        public static void SetEditorTestUtcDay(int utcDay)
        {
            editorTestUtcDay = utcDay;
            if (data != null && ApplyUtcDay(CurrentUtcDayNumber())) Save();
        }

        public static void ClearEditorTestData()
        {
            data = null;
            disableSavingForTests = false;
            editorTestUtcDay = null;
        }
#endif

        static int CurrentUtcDayNumber()
        {
#if UNITY_EDITOR
            if (editorTestUtcDay.HasValue) return Mathf.Max(1, editorTestUtcDay.Value);
#endif
            var unixEpoch = new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc);
            return Mathf.Max(1, (int)(DateTime.UtcNow.Date - unixEpoch).TotalDays);
        }

        /// <summary>
        /// Advances daily state using a monotonically observed UTC calendar day.
        /// Clock rollback never grants or resets anything; progress resumes once
        /// the device reaches the last trusted day again.
        /// </summary>
        static bool ApplyUtcDay(int utcDay)
        {
            if (data == null) return false;
            utcDay = Mathf.Max(1, utcDay);

            if (!data.utcDayStateInitialized)
            {
                data.utcDayStateInitialized = true;
                data.dailyQuestUtcDay = utcDay;
                data.lastObservedUtcDay = utcDay;

                // Legacy saves allowed arbitrary login cards to be claimed. Treat
                // every day through the highest claimed card as consumed so the
                // migration can never duplicate an already granted reward.
                int inferredIndex = InferredLoginRewardIndex(data.claimedRewardMask);
                data.loginRewardIndex = Mathf.Clamp(
                    Mathf.Max(data.loginRewardIndex, inferredIndex), 0, LoginRewardCount);
                data.claimedRewardMask = MaskThroughIndex(data.loginRewardIndex);
                data.loginRewardStreak = Mathf.Clamp(
                    Mathf.Max(data.loginRewardStreak, data.loginRewardIndex), 0, LoginRewardCount);
                if (data.loginRewardIndex > 0 && data.lastLoginRewardUtcDay <= 0)
                    data.lastLoginRewardUtcDay = utcDay;

                // Establish today's quest baselines from the migrated lifetime
                // totals. Without this, historical earnings from an older save
                // would be presented as progress earned on the first trusted day.
                data.dailyStartLifetimePoints = RawTotalBrawlerPointsEarned();
                data.dailyStartLifetimeCoins = data.lifetimeCoinsEarned;
                data.dailyStartBrawlerLevels = RawTotalBrawlerLevels();
                return true;
            }

            if (utcDay < data.lastObservedUtcDay)
                return false;

            bool changed = false;
            if (data.dailyQuestUtcDay != utcDay)
            {
                data.dailyQuestUtcDay = utcDay;
                data.claimedQuestMask = 0;
                data.dailyStartLifetimePoints = RawTotalBrawlerPointsEarned();
                data.dailyStartLifetimeCoins = data.lifetimeCoinsEarned;
                data.dailyStartBrawlerLevels = RawTotalBrawlerLevels();
                changed = true;
            }

            if (utcDay > data.lastObservedUtcDay)
            {
                int gapSinceClaim = data.lastLoginRewardUtcDay > 0
                    ? utcDay - data.lastLoginRewardUtcDay
                    : 0;

                // A completed seven-day track loops on the next UTC day. Missing
                // a calendar day resets an unfinished streak to day one.
                bool completedTrack = data.loginRewardIndex >= LoginRewardCount;
                bool missedDay = data.loginRewardIndex > 0 && gapSinceClaim > 1;
                if (completedTrack || missedDay)
                {
                    data.loginRewardIndex = 0;
                    data.loginRewardStreak = 0;
                    data.claimedRewardMask = 0;
                    changed = true;
                }

                data.lastObservedUtcDay = utcDay;
                changed = true;
            }

            return changed;
        }

        static int InferredLoginRewardIndex(int mask)
        {
            int index = 0;
            for (int i = 0; i < LoginRewardCount; i++)
                if ((mask & (1 << i)) != 0) index = i + 1;
            return index;
        }

        static int MaskThroughIndex(int index)
        {
            index = Mathf.Clamp(index, 0, LoginRewardCount);
            return index <= 0 ? 0 : (1 << index) - 1;
        }

        static int RawTotalBrawlerPointsEarned()
        {
            long total = 0L;
            if (data != null && data.characters != null)
                foreach (var character in data.characters)
                    if (character != null) total += Mathf.Max(0, character.lifetimePointsEarned);
            return ClampToNonnegativeInt(total);
        }

        static int RawTotalBrawlerLevels()
        {
            int total = 0;
            if (data != null && data.characters != null)
                foreach (var character in data.characters)
                    if (character != null) total += Mathf.Max(1, character.level);
            return total;
        }

        public static bool CanClaimLoginReward(int index)
        {
            ProgressData state = Data;
            int utcDay = CurrentUtcDayNumber();
            if (utcDay < state.lastObservedUtcDay) return false;
            if (index < 0 || index >= LoginRewardCount) return false;
            if (index != state.loginRewardIndex) return false;
            return state.lastLoginRewardUtcDay < utcDay;
        }

        /// <summary>Consumes exactly one sequential login claim for the trusted UTC day.</summary>
        public static bool TryClaimLoginReward(int index)
        {
            if (!CanClaimLoginReward(index)) return false;

            int utcDay = CurrentUtcDayNumber();
            Data.claimedRewardMask |= 1 << index;
            Data.loginRewardIndex = Mathf.Min(LoginRewardCount, index + 1);
            Data.loginRewardStreak = Mathf.Min(LoginRewardCount, Data.loginRewardStreak + 1);
            Data.lastLoginRewardUtcDay = utcDay;
            Data.lastObservedUtcDay = Mathf.Max(Data.lastObservedUtcDay, utcDay);
            Save();
            return true;
        }

        static void Load()
        {
            try
            {
                data = File.Exists(FilePath)
                    ? JsonUtility.FromJson<ProgressData>(File.ReadAllText(FilePath))
                    : new ProgressData();
            }
            catch (Exception e)
            {
                Debug.LogWarning("[Progress] failed to load save, starting fresh: " + e.Message);
                data = new ProgressData();
            }
            if (data == null) data = new ProgressData();
            Normalize();
        }

        static void Normalize()
        {
            bool saveWasMigrated = false;
            if (data.characters == null) data.characters = new List<CharacterProgress>();
            for (int i = data.characters.Count - 1; i >= 0; i--)
            {
                var c = data.characters[i];
                if (c == null || string.IsNullOrEmpty(c.id))
                {
                    data.characters.RemoveAt(i);
                    continue;
                }
                c.level = Mathf.Clamp(c.level, 1, MaxLevel);
                c.points = Mathf.Max(0, c.points);
                c.lifetimePointsEarned = Mathf.Max(0, c.lifetimePointsEarned);
                if (c.skills == null) c.skills = new List<CharacterSkillProgress>();
                for (int j = c.skills.Count - 1; j >= 0; j--)
                {
                    var s = c.skills[j];
                    if (s == null || string.IsNullOrEmpty(s.id))
                    {
                        c.skills.RemoveAt(j);
                        continue;
                    }
                    s.level = Mathf.Clamp(s.level, 0, MaxSkillLevel);
                }
            }
            data.coins = Mathf.Max(0, data.coins);
            data.lifetimeCoinsEarned = Mathf.Max(0, data.lifetimeCoinsEarned);
            data.gems = Mathf.Max(0, data.gems);
            data.energy = Mathf.Clamp(data.energy, 0, 60);
            if (data.equippedCardMask == 0) data.equippedCardMask = 7;

            if (data.saveVersion < 1)
            {
                if (data.gems == 0) data.gems = 45;
                if (data.energy == 0) data.energy = 60;
                if (data.energyCells == 0 && data.coinCrates == 0 && data.brawlerTokenPacks == 0 && data.gemPouches == 0)
                {
                    data.energyCells = 2;
                    data.coinCrates = 1;
                    data.brawlerTokenPacks = 1;
                }
                data.saveVersion = 1;
                saveWasMigrated = true;
            }

            if (data.saveVersion < CurrentSaveVersion)
            {
                // Older saves only stored spendable balances. Reconstruct all
                // deterministic level/skill spending so their known earned
                // history is retained, then track future awards explicitly.
                foreach (var c in data.characters)
                    c.lifetimePointsEarned = Mathf.Max(c.lifetimePointsEarned, InferredLifetimePoints(c));
                data.lifetimeCoinsEarned = Mathf.Max(data.lifetimeCoinsEarned, InferredLifetimeCoins());
                data.saveVersion = CurrentSaveVersion;
                saveWasMigrated = true;
            }

            // Lifetime counters are monotonic and may never trail a balance,
            // including saves authored by tools or tests at the current version.
            foreach (var c in data.characters)
                c.lifetimePointsEarned = Mathf.Max(c.lifetimePointsEarned, c.points);
            data.lifetimeCoinsEarned = Mathf.Max(data.lifetimeCoinsEarned, data.coins);

            if (saveWasMigrated) Save();
        }

        static int InferredLifetimePoints(CharacterProgress character)
        {
            long total = character.points;
            for (int level = 1; level < character.level; level++)
                total += PointsNeeded(level);
            if (character.skills != null)
            {
                foreach (var skill in character.skills)
                {
                    if (skill == null) continue;
                    for (int level = 0; level < skill.level; level++)
                        total += SkillPointCost(level);
                }
            }
            return ClampToNonnegativeInt(total);
        }

        static int InferredLifetimeCoins()
        {
            long total = data.coins;
            foreach (var character in data.characters)
            {
                for (int level = 1; level < character.level; level++)
                    total += CoinCost(level);
            }
            return ClampToNonnegativeInt(total);
        }

        static int ClampToNonnegativeInt(long value)
        {
            if (value <= 0L) return 0;
            return value >= int.MaxValue ? int.MaxValue : (int)value;
        }

        static int AddToNonnegativeBalance(int balance, int amount)
        {
            return ClampToNonnegativeInt((long)balance + amount);
        }

        static int AddLifetimeEarnings(int lifetime, int oldBalance, int newBalance)
        {
            int earned = Mathf.Max(0, newBalance - oldBalance);
            return ClampToNonnegativeInt((long)lifetime + earned);
        }

        public static void Save()
        {
#if UNITY_EDITOR
            if (disableSavingForTests) return;
#endif
            try
            {
                File.WriteAllText(FilePath, JsonUtility.ToJson(Data, true));
            }
            catch (Exception e)
            {
                Debug.LogWarning("[Progress] failed to save: " + e.Message);
            }
        }

        public static CharacterProgress Get(string id)
        {
            var list = Data.characters;
            for (int i = 0; i < list.Count; i++)
                if (list[i].id == id) return list[i];
            var fresh = new CharacterProgress { id = id };
            list.Add(fresh);
            return fresh;
        }

        static CharacterSkillProgress GetSkill(CharacterProgress character, string skillId)
        {
            if (character.skills == null) character.skills = new List<CharacterSkillProgress>();
            for (int i = 0; i < character.skills.Count; i++)
                if (character.skills[i].id == skillId) return character.skills[i];
            var fresh = new CharacterSkillProgress { id = skillId };
            character.skills.Add(fresh);
            return fresh;
        }

        public static int TotalBrawlerLevels()
        {
            int total = 0;
            foreach (var c in Data.characters) total += Mathf.Max(1, c.level);
            return total;
        }

        public static int TotalBrawlerPoints()
        {
            int total = 0;
            foreach (var c in Data.characters) total += Mathf.Max(0, c.points);
            return total;
        }

        public static int TotalBrawlerPointsEarned()
        {
            long total = 0L;
            foreach (var c in Data.characters) total += Mathf.Max(0, c.lifetimePointsEarned);
            return ClampToNonnegativeInt(total);
        }

        public static int TotalSkillLevels()
        {
            int total = 0;
            foreach (var c in Data.characters) total += TotalSkillLevels(c.id);
            return total;
        }

        public static int TotalSkillLevels(string characterId)
        {
            var c = Get(characterId);
            int total = 0;
            if (c.skills == null) return 0;
            foreach (var s in c.skills)
                if (s != null) total += Mathf.Clamp(s.level, 0, MaxSkillLevel);
            return total;
        }

        public static int TrophyEstimate()
        {
            long total = (long)TotalBrawlerLevels() * 35L +
                         (long)TotalSkillLevels() * 20L +
                         TotalBrawlerPointsEarned() / 2L +
                         LifetimeCoinsEarned / 10L;
            return ClampToNonnegativeInt(total);
        }

        public static void AddCoins(int amount)
        {
            int oldBalance = Data.coins;
            Data.coins = AddToNonnegativeBalance(oldBalance, amount);
            Data.lifetimeCoinsEarned = AddLifetimeEarnings(Data.lifetimeCoinsEarned, oldBalance, Data.coins);
            Save();
        }

        public static bool TrySpendCoins(int amount)
        {
            if (amount <= 0) return true;
            if (Data.coins < amount) return false;
            Data.coins -= amount;
            Save();
            return true;
        }

        public static void AddGems(int amount)
        {
            Data.gems = Mathf.Max(0, Data.gems + amount);
            Save();
        }

        public static bool TrySpendGems(int amount)
        {
            if (amount <= 0) return true;
            if (Data.gems < amount) return false;
            Data.gems -= amount;
            Save();
            return true;
        }

        public static void AddEnergy(int amount)
        {
            Data.energy = Mathf.Clamp(Data.energy + amount, 0, 60);
            Save();
        }

        public static bool TrySpendEnergy(int amount)
        {
            if (amount <= 0) return true;
            if (Data.energy < amount) return false;
            Data.energy = Mathf.Clamp(Data.energy - amount, 0, 60);
            Save();
            return true;
        }

        /// <summary>Debits the canonical entry cost for any battle, including replay.</summary>
        public static bool TrySpendBattleEnergy()
        {
            return TrySpendEnergy(BattleEnergyCost);
        }

        public static void AddCharacterPoints(string id, int amount)
        {
            var c = Get(id);
            int oldBalance = c.points;
            c.points = AddToNonnegativeBalance(oldBalance, amount);
            c.lifetimePointsEarned = AddLifetimeEarnings(c.lifetimePointsEarned, oldBalance, c.points);
            Save();
        }

        public static void SetSelectedMode(GameMode mode)
        {
            if (Data.selectedMode == (int)mode) return;
            Data.selectedMode = (int)mode;
            Save();
        }

        public static void SetSelectedCharacter(string id)
        {
            if (id == null) id = "";
            if (Data.selectedCharacterId == id) return;
            Data.selectedCharacterId = id;
            Save();
        }

        public static bool IsCardEquipped(int index) => (Data.equippedCardMask & (1 << index)) != 0;

        public static int EquippedCardCount()
        {
            int count = 0;
            int mask = Data.equippedCardMask;
            for (int i = 0; i < 32; i++)
                if ((mask & (1 << i)) != 0) count++;
            return count;
        }

        public static bool TryToggleCard(int index, int maxEquipped)
        {
            int bit = 1 << index;
            if ((Data.equippedCardMask & bit) != 0)
            {
                if (EquippedCardCount() <= 1) return false;
                Data.equippedCardMask &= ~bit;
                Save();
                return true;
            }
            if (EquippedCardCount() >= maxEquipped) return false;
            Data.equippedCardMask |= bit;
            Save();
            return true;
        }

        public static int InventoryItemCount(int index)
        {
            switch (index)
            {
                case 0: return Data.energyCells;
                case 1: return Data.coinCrates;
                case 2: return Data.brawlerTokenPacks;
                case 3: return Data.gemPouches;
                default: return 0;
            }
        }

        public static void AddInventoryItem(int index, int amount)
        {
            if (amount == 0) return;
            switch (index)
            {
                case 0: Data.energyCells = Mathf.Max(0, Data.energyCells + amount); break;
                case 1: Data.coinCrates = Mathf.Max(0, Data.coinCrates + amount); break;
                case 2: Data.brawlerTokenPacks = Mathf.Max(0, Data.brawlerTokenPacks + amount); break;
                case 3: Data.gemPouches = Mathf.Max(0, Data.gemPouches + amount); break;
            }
            Save();
        }

        public static bool TryUseInventoryItem(int index)
        {
            if (InventoryItemCount(index) <= 0) return false;
            AddInventoryItem(index, -1);
            return true;
        }

        public static bool IsFriendInvited(int index) => (Data.friendInviteMask & (1 << index)) != 0;

        public static void MarkFriendInvited(int index)
        {
            Data.friendInviteMask |= 1 << index;
            Save();
        }

        public static bool IsRewardClaimed(int index) => (Data.claimedRewardMask & (1 << index)) != 0;
        public static bool IsQuestClaimed(int index) => (Data.claimedQuestMask & (1 << index)) != 0;
        public static bool IsInboxClaimed(int index) => (Data.claimedInboxMask & (1 << index)) != 0;

        public static void MarkRewardClaimed(int index)
        {
            Data.claimedRewardMask |= 1 << index;
            Save();
        }

        public static void MarkQuestClaimed(int index)
        {
            Data.claimedQuestMask |= 1 << index;
            Save();
        }

        public static void MarkInboxClaimed(int index)
        {
            Data.claimedInboxMask |= 1 << index;
            Save();
        }

        /// <summary>Points required to go from `level` to the next one.</summary>
        public static int PointsNeeded(int level)
        {
            return 50 * level;
        }

        /// <summary>Coin price of the upgrade from `level` to the next one.</summary>
        public static int CoinCost(int level)
        {
            return 30 * level;
        }

        public const int MaxSkillLevel = 3;

        /// <summary>Skill points required to acquire or develop a skill at its current level.</summary>
        public static int SkillPointCost(int currentSkillLevel)
        {
            return 40 * (Mathf.Clamp(currentSkillLevel, 0, MaxSkillLevel - 1) + 1);
        }

        public static int GetSkillLevel(string characterId, string skillId)
        {
            if (string.IsNullOrEmpty(characterId) || string.IsNullOrEmpty(skillId)) return 0;
            return Mathf.Clamp(GetSkill(Get(characterId), skillId).level, 0, MaxSkillLevel);
        }

        public static bool CanUpgradeSkill(string characterId, string skillId)
        {
            if (string.IsNullOrEmpty(characterId) || string.IsNullOrEmpty(skillId)) return false;
            var c = Get(characterId);
            int level = GetSkill(c, skillId).level;
            return level < MaxSkillLevel && c.points >= SkillPointCost(level);
        }

        public static bool TryUpgradeSkill(string characterId, string skillId)
        {
            if (!CanUpgradeSkill(characterId, skillId)) return false;
            var c = Get(characterId);
            var skill = GetSkill(c, skillId);
            c.points -= SkillPointCost(skill.level);
            skill.level++;
            Save();
            return true;
        }

        /// <summary>HP/damage multiplier a character gets at `level`.</summary>
        public static float StatMultiplier(int level)
        {
            return 1f + 0.05f * (Mathf.Clamp(level, 1, MaxLevel) - 1);
        }

        public static bool CanUpgrade(string id)
        {
            var c = Get(id);
            return c.level < MaxLevel &&
                   c.points >= PointsNeeded(c.level) &&
                   Data.coins >= CoinCost(c.level);
        }

        public static bool TryUpgrade(string id)
        {
            if (!CanUpgrade(id)) return false;
            var c = Get(id);
            c.points -= PointsNeeded(c.level);
            Data.coins -= CoinCost(c.level);
            c.level++;
            Save();
            return true;
        }

        /// <summary>End-of-match payout. Returns (points, coins) for display.</summary>
        public static (int points, int coins) AwardMatch(string characterId, bool won, int kills)
        {
            int points = (won ? 30 : 15) + kills * 8;
            int coins = (won ? 25 : 10) + kills * 5;
            var character = Get(characterId);
            int oldPoints = character.points;
            character.points = AddToNonnegativeBalance(oldPoints, points);
            character.lifetimePointsEarned = AddLifetimeEarnings(character.lifetimePointsEarned, oldPoints, character.points);
            int oldCoins = Data.coins;
            Data.coins = AddToNonnegativeBalance(oldCoins, coins);
            Data.lifetimeCoinsEarned = AddLifetimeEarnings(Data.lifetimeCoinsEarned, oldCoins, Data.coins);
            Save();
            return (points, coins);
        }
    }
}
