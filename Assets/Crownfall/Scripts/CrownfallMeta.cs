using System;
using UnityEngine;

namespace Crownfall
{
    public struct MatchRewards
    {
        public int coins, xp, trophies;
        public bool leveledUp;
        public bool Any => coins != 0 || xp != 0 || trophies != 0;
    }

    /// Persistent player progression + soft economy behind the home hub:
    /// gems, coins, trophies, level/XP, selected champion, gift cooldown and
    /// inbox read-state. All PlayerPrefs, all lazy-loaded.
    public static class CrownfallMeta
    {
        public const float GiftCooldownHours = 6f;

        public static readonly (string title, string body)[] News =
        {
            ("WELCOME, CHAMPION!", "The Sundered Crown arena is open. Win matches to earn coins, XP and trophies."),
            ("SEASON 1 — CROWNFALL", "Four champions enter the fray: Knight, Warbrand, Duelist and Mage. More on the forge."),
            ("FREE GIFTS", "A gift chest refills every few hours. Crack it open for coins — and sometimes gems."),
        };

        public static event Action Changed;

        static bool loaded;
        static int gems, coins, trophies, level, xp, selectedClass, inboxReadMask;
        static long lastGiftTicks;

        public static int Gems { get { Ensure(); return gems; } }
        public static int Coins { get { Ensure(); return coins; } }
        public static int Trophies { get { Ensure(); return trophies; } }
        public static int Level { get { Ensure(); return level; } }
        public static int Xp { get { Ensure(); return xp; } }

        public static int SelectedClass
        {
            get { Ensure(); return selectedClass; }
            set
            {
                Ensure();
                selectedClass = Mathf.Clamp(value, 0, 3);
                Save();
                Changed?.Invoke();
            }
        }

        public static int XpForLevel(int lvl) => 80 + (lvl - 1) * 45;

        /// Online display name. Defaults to a numbered champion tag so first-run
        /// players are distinguishable in a room without any setup.
        public static string PlayerName
        {
            get
            {
                string n = PlayerPrefs.GetString("meta.playerName", "");
                if (string.IsNullOrWhiteSpace(n))
                {
                    n = "Champion" + UnityEngine.Random.Range(100, 999);
                    PlayerPrefs.SetString("meta.playerName", n);
                }
                return n;
            }
            set
            {
                string trimmed = (value ?? "").Trim();
                if (trimmed.Length == 0) return;
                PlayerPrefs.SetString("meta.playerName", trimmed.Substring(0, Mathf.Min(16, trimmed.Length)));
                PlayerPrefs.Save();
                Changed?.Invoke();
            }
        }

        static void Ensure()
        {
            if (loaded) return;
            loaded = true;
            gems = PlayerPrefs.GetInt("meta.gems", 30);
            coins = PlayerPrefs.GetInt("meta.coins", 120);
            trophies = PlayerPrefs.GetInt("meta.trophies", 0);
            level = PlayerPrefs.GetInt("meta.level", 1);
            xp = PlayerPrefs.GetInt("meta.xp", 0);
            selectedClass = PlayerPrefs.GetInt("meta.selectedClass", 0);
            inboxReadMask = PlayerPrefs.GetInt("meta.inboxRead", 0);
            long.TryParse(PlayerPrefs.GetString("meta.lastGift", "0"), out lastGiftTicks);
        }

        public static void Save()
        {
            Ensure();
            PlayerPrefs.SetInt("meta.gems", gems);
            PlayerPrefs.SetInt("meta.coins", coins);
            PlayerPrefs.SetInt("meta.trophies", trophies);
            PlayerPrefs.SetInt("meta.level", level);
            PlayerPrefs.SetInt("meta.xp", xp);
            PlayerPrefs.SetInt("meta.selectedClass", selectedClass);
            PlayerPrefs.SetInt("meta.inboxRead", inboxReadMask);
            PlayerPrefs.SetString("meta.lastGift", lastGiftTicks.ToString());
            PlayerPrefs.Save();
        }

        // ---------------------------------------------------------------- economy

        public static void AddCoins(int amount)
        {
            Ensure();
            coins = Mathf.Max(0, coins + amount);
            Save();
            Changed?.Invoke();
        }

        public static void AddGems(int amount)
        {
            Ensure();
            gems = Mathf.Max(0, gems + amount);
            Save();
            Changed?.Invoke();
        }

        public static bool SpendGems(int cost)
        {
            Ensure();
            if (gems < cost) return false;
            gems -= cost;
            Save();
            Changed?.Invoke();
            return true;
        }

        /// XP grant with level-up handling; level-ups pay out gems.
        static bool AddXp(int amount)
        {
            Ensure();
            xp += amount;
            bool leveled = false;
            while (xp >= XpForLevel(level))
            {
                xp -= XpForLevel(level);
                level++;
                gems += 10;
                leveled = true;
            }
            return leveled;
        }

        public static MatchRewards GrantMatchRewards(bool won)
        {
            Ensure();
            var r = new MatchRewards
            {
                coins = won ? 26 : 10,
                xp = won ? 40 : 18,
                trophies = won ? 8 : -4,
            };
            coins += r.coins;
            if (trophies + r.trophies < 0) r.trophies = -trophies;
            trophies += r.trophies;
            r.leveledUp = AddXp(r.xp);
            Save();
            Changed?.Invoke();
            return r;
        }

        // ---------------------------------------------------------------- gifts

        public static bool GiftReady
        {
            get
            {
                Ensure();
                return (DateTime.UtcNow - new DateTime(lastGiftTicks, DateTimeKind.Utc)).TotalHours
                       >= GiftCooldownHours;
            }
        }

        public static TimeSpan GiftTimeLeft
        {
            get
            {
                Ensure();
                var next = new DateTime(lastGiftTicks, DateTimeKind.Utc).AddHours(GiftCooldownHours);
                var left = next - DateTime.UtcNow;
                return left > TimeSpan.Zero ? left : TimeSpan.Zero;
            }
        }

        public static (int coins, int gems) ClaimGift()
        {
            Ensure();
            if (!GiftReady) return (0, 0);
            int c = UnityEngine.Random.Range(40, 91);
            int g = UnityEngine.Random.value < 0.35f ? UnityEngine.Random.Range(2, 7) : 0;
            coins += c;
            gems += g;
            lastGiftTicks = DateTime.UtcNow.Ticks;
            Save();
            Changed?.Invoke();
            return (c, g);
        }

        // ---------------------------------------------------------------- inbox

        public static bool IsNewsRead(int index)
        {
            Ensure();
            return (inboxReadMask & (1 << index)) != 0;
        }

        public static void MarkNewsRead(int index)
        {
            Ensure();
            if (IsNewsRead(index)) return;
            inboxReadMask |= 1 << index;
            Save();
            Changed?.Invoke();
        }

        public static int UnreadNews
        {
            get
            {
                Ensure();
                int n = 0;
                for (int i = 0; i < News.Length; i++)
                    if (!IsNewsRead(i)) n++;
                return n;
            }
        }
    }
}
