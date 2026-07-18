using System.Collections;
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
        // Hits on the same victim inside this window are "simultaneous" for
        // fan/stagger purposes; anything slower reads as a fresh, isolated hit.
        const float PopupStaggerWindow = 0.14f;
        const float PopupStaggerDelayStep = 0.045f;

        readonly List<(Health health, System.Action<float, GameObject> handler)> hooks =
            new List<(Health, System.Action<float, GameObject>)>();
        readonly List<(Health health, System.Action<float> handler)> healHooks =
            new List<(Health, System.Action<float>)>();
        readonly Dictionary<BrawlerController, float> lastPopupAt =
            new Dictionary<BrawlerController, float>();
        readonly Dictionary<BrawlerController, int> popupFanSlot =
            new Dictionary<BrawlerController, int>();
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

            int fan = NextPopupFanSlot(victim);
            // The first hit in a burst keeps the old light jitter; every hit
            // that lands on the same victim inside the stagger window fans
            // further out and lands a beat later, so "24"+"24" can never
            // render as one garbled "2424".
            float horizontalOffset = fan == 0
                ? Random.Range(-0.25f, 0.25f)
                : (fan % 2 == 1 ? 1f : -1f) * (0.55f + 0.4f * ((fan - 1) / 2));
            float delay = fan * PopupStaggerDelayStep;
            StartCoroutine(SpawnDamagePopupDelayed(prefab, victim, amount, horizontalOffset, delay));
        }

        int NextPopupFanSlot(BrawlerController victim)
        {
            float now = Time.time;
            bool withinWindow = lastPopupAt.TryGetValue(victim, out float last) &&
                                now - last < PopupStaggerWindow;
            int slot = withinWindow
                ? (popupFanSlot.TryGetValue(victim, out int previous) ? previous + 1 : 1)
                : 0;
            popupFanSlot[victim] = slot;
            lastPopupAt[victim] = now;
            return slot;
        }

        IEnumerator SpawnDamagePopupDelayed(DamageNumberMesh prefab, BrawlerController victim,
            float amount, float horizontalOffset, float delay)
        {
            if (delay > 0f) yield return new WaitForSeconds(delay);
            if (victim == null) yield break;

            // Following the victim's transform keeps the number glued to a
            // moving target even after the staggered delay.
            Vector3 pos = victim.transform.position + Vector3.up * 2.1f +
                          new Vector3(horizontalOffset, 0f, 0f);
            prefab.Spawn(pos, Mathf.Round(amount), victim.transform);
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
