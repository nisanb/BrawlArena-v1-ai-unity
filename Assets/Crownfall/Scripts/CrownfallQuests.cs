using System;
using UnityEngine;

namespace Crownfall
{
    public struct QuestDef
    {
        public string id, title, desc;
        public int target, coins, gems;
    }

    /// Daily quests (the pack's Missions screen made real): progress feeds from
    /// finished matches, rewards pay into CrownfallMeta, everything resets at
    /// UTC midnight. PlayerPrefs-backed like the rest of the meta-game.
    public static class CrownfallQuests
    {
        public static readonly QuestDef[] Defs =
        {
            new QuestDef { id = "play", title = "ENTER THE FRAY", desc = "Play 3 matches",
                target = 3, coins = 60, gems = 0 },
            new QuestDef { id = "win", title = "CLAIM VICTORY", desc = "Win 2 matches",
                target = 2, coins = 40, gems = 4 },
            new QuestDef { id = "kills", title = "TAKEDOWN ARTIST", desc = "Score 8 takedowns",
                target = 8, coins = 90, gems = 0 },
        };

        public static event Action Changed;

        /// Repaints quest UI after an external writer (cloud sync / admin console)
        /// has rewritten the quests.* keys. The store itself reads PlayerPrefs
        /// live, so there is no cache to drop — just fan the change out.
        public static void Reload() => Changed?.Invoke();

        static string Today => DateTime.UtcNow.ToString("yyyyMMdd");

        static void EnsureDay()
        {
            if (PlayerPrefs.GetString("quests.day", "") == Today) return;
            PlayerPrefs.SetString("quests.day", Today);
            foreach (var q in Defs)
            {
                PlayerPrefs.SetInt("quests.p." + q.id, 0);
                PlayerPrefs.SetInt("quests.c." + q.id, 0);
            }
            PlayerPrefs.Save();
        }

        public static int Progress(string id)
        {
            EnsureDay();
            return PlayerPrefs.GetInt("quests.p." + id, 0);
        }

        public static bool IsClaimed(string id)
        {
            EnsureDay();
            return PlayerPrefs.GetInt("quests.c." + id, 0) != 0;
        }

        public static bool CanClaim(QuestDef q) => !IsClaimed(q.id) && Progress(q.id) >= q.target;

        public static int ClaimableCount
        {
            get
            {
                int n = 0;
                foreach (var q in Defs)
                    if (CanClaim(q)) n++;
                return n;
            }
        }

        /// Pays the reward once; returns false when not yet earned or already taken.
        public static bool Claim(QuestDef q)
        {
            if (!CanClaim(q)) return false;
            PlayerPrefs.SetInt("quests.c." + q.id, 1);
            PlayerPrefs.Save();
            if (q.coins > 0) CrownfallMeta.AddCoins(q.coins);
            if (q.gems > 0) CrownfallMeta.AddGems(q.gems);
            Changed?.Invoke();
            return true;
        }

        /// Called by MatchManager when a real (non-demo) match ends.
        public static void OnMatchFinished(bool won, int playerKills)
        {
            EnsureDay();
            Bump("play", 1);
            if (won) Bump("win", 1);
            if (playerKills > 0) Bump("kills", playerKills);
            PlayerPrefs.Save();
            Changed?.Invoke();
        }

        static void Bump(string id, int by)
        {
            PlayerPrefs.SetInt("quests.p." + id, PlayerPrefs.GetInt("quests.p." + id, 0) + by);
        }
    }
}
