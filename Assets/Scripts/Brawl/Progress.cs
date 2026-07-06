using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace BrawlArena
{
    [Serializable]
    public class CharacterProgress
    {
        public string id;
        public int level = 1;
        public int points;
    }

    [Serializable]
    public class ProgressData
    {
        public int coins;
        public List<CharacterProgress> characters = new List<CharacterProgress>();
    }

    /// <summary>
    /// Player progression: per-character levels and points (earned by playing
    /// that character), and a coin wallet spent in the shop to level up.
    /// Levels raise HP and damage at spawn. Persisted as JSON in
    /// persistentDataPath so it survives play sessions.
    /// </summary>
    public static class Progress
    {
        public const int MaxLevel = 10;

        static ProgressData data;

        static string FilePath => Path.Combine(Application.persistentDataPath, "progress.json");

        public static ProgressData Data
        {
            get
            {
                if (data == null) Load();
                return data;
            }
        }

        public static int Coins => Data.coins;

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
        }

        public static void Save()
        {
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
            Get(characterId).points += points;
            Data.coins += coins;
            Save();
            return (points, coins);
        }
    }
}
