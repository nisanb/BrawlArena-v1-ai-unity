using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine;
using Unity.Services.CloudSave;
using Unity.Services.CloudSave.Models;

namespace Crownfall.Backend
{
    /// Mirrors the durable slice of PlayerPrefs (the player's account: economy,
    /// progression, chosen champion, cosmetics, quests, trophy-road claims) into
    /// one Cloud Save record so progress follows the player across devices and
    /// reinstalls. Device-only preferences (audio/sensitivity) are deliberately
    /// left out — they should stay per-device.
    ///
    /// The layer is intentionally non-invasive: it subscribes to the existing
    /// CrownfallMeta.Changed / CrownfallQuests.Changed events and pushes a
    /// debounced snapshot. No gameplay/mutation code had to change. Conflict
    /// resolution is a monotonic revision counter: whichever side has the higher
    /// rev wins, so an offline session that advanced local state is not clobbered
    /// by a stale cloud copy on the next sign-in.
    public static class CrownfallCloud
    {
        const string Key = "player_state";
        const string RevPref = "cloud.rev";
        const float PushDebounceSeconds = 2.5f;

        // Durable account keys mirrored to the cloud. Order-independent.
        static readonly string[] IntKeys =
        {
            "meta.gems", "meta.coins", "meta.trophies", "meta.level", "meta.xp",
            "meta.selectedClass", "meta.mode", "meta.sigil", "meta.sigilsOwned",
            "meta.hasProfile", "meta.inboxRead",
            "quests.p.play", "quests.p.win", "quests.p.kills",
            "quests.c.play", "quests.c.win", "quests.c.kills",
            "trophyroad.claimed.50", "trophyroad.claimed.100", "trophyroad.claimed.200",
            "trophyroad.claimed.350", "trophyroad.claimed.500", "trophyroad.claimed.750",
            "trophyroad.claimed.1000",
        };

        static readonly string[] StringKeys =
        {
            "meta.playerName", "meta.lastGift", "quests.day",
        };

        [Serializable] class IntKV { public string k; public int v; }
        [Serializable] class StrKV { public string k; public string v; }

        [Serializable]
        class Snapshot
        {
            public int rev;
            public long utc;
            public string playerId;
            public List<IntKV> ints = new List<IntKV>();
            public List<StrKV> strs = new List<StrKV>();
        }

        static bool watching;
        static bool applying;   // guards pull->push echo
        static bool dirty;
        static float sinceDirty;
        static bool pushing;

        // ------------------------------------------------------------ watching

        /// Begin mirroring local changes upward. Called by CrownfallServices only
        /// after the initial pull, so seeding writes don't bounce back as pushes.
        public static void BeginWatching()
        {
            if (watching) return;
            watching = true;
            CrownfallMeta.Changed += OnLocalChanged;
            CrownfallQuests.Changed += OnLocalChanged;
        }

        static void OnLocalChanged()
        {
            if (applying) return;      // this change came from a cloud pull
            dirty = true;
            sinceDirty = 0f;
        }

        /// Driven by the pump each frame. Flushes a debounced push.
        public static void Tick(float unscaledDt)
        {
            if (!dirty || !CrownfallServices.Online || pushing) return;
            sinceDirty += unscaledDt;
            if (sinceDirty >= PushDebounceSeconds)
                _ = PushAsync();
        }

        /// Immediate flush (app pause / quit / admin button).
        public static void FlushNow()
        {
            if (dirty && CrownfallServices.Online && !pushing)
                _ = PushAsync();
        }

        // ------------------------------------------------------------ snapshot

        static int LocalRev => PlayerPrefs.GetInt(RevPref, 0);
        static void SetLocalRev(int r) { PlayerPrefs.SetInt(RevPref, r); PlayerPrefs.Save(); }

        static Snapshot Capture(int rev)
        {
            var s = new Snapshot { rev = rev, utc = DateTime.UtcNow.Ticks, playerId = CrownfallServices.PlayerId };
            foreach (var k in IntKeys)
                if (PlayerPrefs.HasKey(k)) s.ints.Add(new IntKV { k = k, v = PlayerPrefs.GetInt(k) });
            foreach (var k in StringKeys)
                if (PlayerPrefs.HasKey(k)) s.strs.Add(new StrKV { k = k, v = PlayerPrefs.GetString(k) });
            return s;
        }

        static void ApplyToLocal(Snapshot s)
        {
            if (s == null) return;
            applying = true;
            try
            {
                if (s.ints != null)
                    foreach (var kv in s.ints) PlayerPrefs.SetInt(kv.k, kv.v);
                if (s.strs != null)
                    foreach (var kv in s.strs) PlayerPrefs.SetString(kv.k, kv.v);
                SetLocalRev(s.rev);
                PlayerPrefs.Save();

                // Re-hydrate the static caches and repaint the UI. These fire
                // Changed, but `applying` keeps that from scheduling a push.
                CrownfallMeta.Reload();
                CrownfallQuests.Reload();
            }
            finally { applying = false; }
        }

        // ------------------------------------------------------------ transport

        public static async Task PullAsync()
        {
            try
            {
                var results = await CloudSaveService.Instance.Data.Player.LoadAsync(
                    new HashSet<string> { Key });

                if (results != null && results.TryGetValue(Key, out var item))
                {
                    Snapshot cloud = null;
                    try { cloud = item.Value.GetAs<Snapshot>(); }
                    catch (Exception ex) { Debug.LogWarning("[Crownfall] Cloud record unreadable: " + ex.Message); }

                    if (cloud == null)
                    {
                        // Unreadable/old-format record — reseed from local so it self-heals.
                        await PushAsync();
                        return;
                    }
                    if (cloud.rev >= LocalRev)
                    {
                        ApplyToLocal(cloud);
                        Debug.Log($"[Crownfall] Cloud pull applied (rev {cloud.rev}).");
                        return;
                    }
                    // Local is ahead of the cloud (offline progress) — push it up.
                    Debug.Log($"[Crownfall] Local ahead of cloud ({LocalRev} > {cloud.rev}); pushing.");
                    await PushAsync();
                    return;
                }

                // No cloud record yet — seed it from whatever local state exists.
                Debug.Log("[Crownfall] No cloud record; seeding from local.");
                await PushAsync();
            }
            catch (Exception e)
            {
                Debug.LogWarning("[Crownfall] Cloud pull failed: " + e.Message);
            }
        }

        public static async Task PushAsync()
        {
            if (pushing) return;
            pushing = true;
            try
            {
                int rev = LocalRev + 1;
                var snap = Capture(rev);

                // Store the snapshot as a structured object (not a stringified
                // blob) so the UGS CLI admin tool reads/writes the same shape.
                await CloudSaveService.Instance.Data.Player.SaveAsync(
                    new Dictionary<string, object> { { Key, snap } });

                SetLocalRev(rev);
                dirty = false;
                sinceDirty = 0f;
                Debug.Log($"[Crownfall] Cloud push ok (rev {rev}).");
            }
            catch (Exception e)
            {
                Debug.LogWarning("[Crownfall] Cloud push failed: " + e.Message);
            }
            finally { pushing = false; }
        }

        // ------------------------------------------------------------ admin aid

        /// Force an immediate push (admin overlay). Bumps rev so it wins the next
        /// pull on any other device.
        public static Task ForcePush() => PushAsync();

        /// Force a fresh pull, ignoring the local rev guard (admin overlay). Used
        /// to yank down a value set remotely via the UGS CLI admin tool.
        public static async Task ForcePull()
        {
            try
            {
                var results = await CloudSaveService.Instance.Data.Player.LoadAsync(
                    new HashSet<string> { Key });
                if (results != null && results.TryGetValue(Key, out var item))
                {
                    var cloud = item.Value.GetAs<Snapshot>();
                    ApplyToLocal(cloud);
                    Debug.Log("[Crownfall] Admin force-pull applied.");
                }
            }
            catch (Exception e) { Debug.LogWarning("[Crownfall] Force-pull failed: " + e.Message); }
        }
    }
}
