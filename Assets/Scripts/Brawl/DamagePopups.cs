using System.Collections.Generic;
using DamageNumbersPro;
using UnityEngine;

namespace BrawlArena
{
    /// <summary>
    /// Spawns DamageNumbersPro world-space popups for every hit. Numbers over
    /// enemies use the bright "hit" style; numbers over the player's own team
    /// use the red "hurt" style, so at a glance outgoing and incoming damage
    /// read differently. Prefab references are wired by ArenaSceneBuilder to
    /// project-owned copies of the DNP presets (pooling enabled on the copies).
    /// </summary>
    public class DamagePopups : MonoBehaviour
    {
        [Tooltip("Style for damage dealt to the enemy team.")]
        public DamageNumberMesh enemyHitPrefab;
        [Tooltip("Style for damage taken by the player's team.")]
        public DamageNumberMesh allyHurtPrefab;
        [Tooltip("Style for health restored (tinted green per spawn).")]
        public DamageNumberMesh healPrefab;

        static readonly Color HealGreen = new Color(0.35f, 1f, 0.5f);

        // A routine tick and a lucky big hit must not read the same size: this
        // caps how large the prefab's own number-based scale curve is allowed
        // to grow, so no single popup dominates the screen.
        const float MaxPopupScale = 1.5f;
        // Hits on the same victim inside this window accumulate into the one
        // already-visible popup (Brawl Stars style) instead of spawning a
        // sibling, so "23"+"23" can never render as one garbled "2323".
        const float MergeWindow = 0.6f;
        // How long after its last hit a popup still counts as "occupying" its
        // spot for stagger purposes. Must be >= MergeWindow, and short enough
        // that a popup which has visibly risen away no longer pushes others.
        const float OccupiedWindow = 1f;
        // Popups for DIFFERENT victims closer together than this stack upward
        // and fan sideways instead of landing on top of each other (or on a
        // nameplate).
        const float StaggerRadius = 1.2f;
        const float StaggerRise = 0.55f;
        const float StaggerFan = 0.45f;

        // One live merge target per victim. Entries are recycled through
        // entryPool so a busy teamfight never allocates per hit.
        sealed class ActivePopup
        {
            public BrawlerController victim;
            public DamageNumber popup;
            public float total;
            public float lastHitAt;
        }

        readonly List<(Health health, System.Action<float, GameObject> handler)> hooks =
            new List<(Health, System.Action<float, GameObject>)>();
        readonly List<(Health health, System.Action<float> handler)> healHooks =
            new List<(Health, System.Action<float>)>();
        readonly List<ActivePopup> activePopups = new List<ActivePopup>();
        readonly Stack<ActivePopup> entryPool = new Stack<ActivePopup>();
        bool managerHooked;

        void Start()
        {
            TryHookManager();
            CapPopupScale(enemyHitPrefab);
            CapPopupScale(allyHurtPrefab);
            if (enemyHitPrefab != null) enemyHitPrefab.PrewarmPool();
            if (allyHurtPrefab != null) allyHurtPrefab.PrewarmPool();
            if (healPrefab != null) healPrefab.PrewarmPool();
        }

        static void CapPopupScale(DamageNumberMesh prefab)
        {
            if (prefab == null || !prefab.enableScaleByNumber) return;
            var settings = prefab.scaleByNumberSettings;
            bool changed = false;
            if (settings.toScale > MaxPopupScale)
            {
                settings.toScale = MaxPopupScale;
                changed = true;
            }
            if (settings.fromScale > MaxPopupScale)
            {
                settings.fromScale = MaxPopupScale;
                changed = true;
            }
            if (changed) prefab.scaleByNumberSettings = settings;
        }

        void Update()
        {
            TryHookManager();
        }

        void TryHookManager()
        {
            if (managerHooked || MatchManager.Instance == null) return;
            managerHooked = true;
            MatchManager.Instance.BrawlerRegistered += HookBrawler;
            foreach (var b in MatchManager.Instance.GetBrawlers()) HookBrawler(b);
        }

        void HookBrawler(BrawlerController b)
        {
            if (b == null) return;
            System.Action<float, GameObject> handler = (amount, attacker) => OnDamaged(b, amount);
            b.Health.Damaged += handler;
            hooks.Add((b.Health, handler));

            System.Action<float> healHandler = amount => OnHealed(b, amount);
            b.Health.Healed += healHandler;
            healHooks.Add((b.Health, healHandler));
        }

        void OnDestroy()
        {
            if (managerHooked && MatchManager.Instance != null)
                MatchManager.Instance.BrawlerRegistered -= HookBrawler;
            foreach (var (health, handler) in hooks)
                if (health != null) health.Damaged -= handler;
            foreach (var (health, handler) in healHooks)
                if (health != null) health.Healed -= handler;
        }

        void OnDamaged(BrawlerController victim, float amount)
        {
            var prefab = victim.team == TeamId.Blue ? allyHurtPrefab : enemyHitPrefab;
            if (prefab == null) return;

            float now = Time.time;
            PruneActivePopups(now);

            float rounded = Mathf.Max(1f, Mathf.Round(amount));
            var entry = FindEntryFor(victim);

            // MERGE: a follow-up hit inside the window feeds the popup that is
            // already on screen. FadeIn() both refreshes the lifetime and
            // replays the preset's scale-in, which reads as the number
            // "popping" each time it grows.
            if (entry != null && now - entry.lastHitAt <= MergeWindow &&
                entry.popup != null && entry.popup.gameObject.activeInHierarchy)
            {
                entry.total += rounded;
                entry.lastHitAt = now;
                entry.popup.number = entry.total;
                entry.popup.UpdateText();
                entry.popup.FadeIn();
                return;
            }

            // STAGGER: a fresh popup landing next to other victims' live
            // numbers takes a deterministic slot — each occupied neighbour
            // pushes it one step up and fans it to alternating sides — so
            // simultaneous multi-target hits never overlap each other or a
            // nameplate. An uncrowded popup keeps the old light jitter.
            int slot = CountNearbyOccupied(victim);
            Vector3 offset = slot == 0
                ? new Vector3(Random.Range(-0.25f, 0.25f), 0f, 0f)
                : new Vector3(
                    (slot % 2 == 1 ? 1f : -1f) * (StaggerFan * ((slot + 1) / 2)),
                    StaggerRise * slot, 0f);
            Vector3 pos = victim.transform.position + Vector3.up * 2.1f + offset;
            // Following the victim's transform keeps the number glued to a
            // moving target for its whole lifetime.
            var spawned = prefab.Spawn(pos, rounded, victim.transform);

            if (entry == null)
            {
                entry = entryPool.Count > 0 ? entryPool.Pop() : new ActivePopup();
                activePopups.Add(entry);
            }
            entry.victim = victim;
            entry.popup = spawned;
            entry.total = rounded;
            entry.lastHitAt = now;
        }

        ActivePopup FindEntryFor(BrawlerController victim)
        {
            for (int i = 0; i < activePopups.Count; i++)
                if (activePopups[i].victim == victim) return activePopups[i];
            return null;
        }

        int CountNearbyOccupied(BrawlerController victim)
        {
            int count = 0;
            Vector3 at = victim.transform.position;
            for (int i = 0; i < activePopups.Count; i++)
            {
                var other = activePopups[i];
                if (other.victim == victim || other.victim == null) continue;
                if (other.popup == null || !other.popup.gameObject.activeInHierarchy) continue;
                if (Vector3.Distance(other.victim.transform.position, at) < StaggerRadius)
                    count++;
            }
            return count;
        }

        void PruneActivePopups(float now)
        {
            // Popups are pooled by DNP: past the occupied window the instance
            // may be recycled for an unrelated spawn, so the entry must be
            // dropped before anything could merge into the wrong number.
            for (int i = activePopups.Count - 1; i >= 0; i--)
            {
                var entry = activePopups[i];
                bool dead = entry.victim == null || entry.popup == null ||
                            !entry.popup.gameObject.activeInHierarchy ||
                            now - entry.lastHitAt > OccupiedWindow;
                if (!dead) continue;
                entry.victim = null;
                entry.popup = null;
                activePopups.RemoveAt(i);
                entryPool.Push(entry);
            }
        }

        void OnHealed(BrawlerController target, float amount)
        {
            if (healPrefab == null || amount < 1f) return;
            Vector3 pos = target.transform.position + Vector3.up * 2.1f;
            var dn = healPrefab.Spawn(pos, Mathf.Round(amount), target.transform);
            // Pooled instances keep their last tint, so recolor every spawn.
            dn.SetColor(HealGreen);
        }
    }
}
