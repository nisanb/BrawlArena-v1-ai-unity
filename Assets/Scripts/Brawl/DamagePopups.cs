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

        readonly List<(Health health, System.Action<float, GameObject> handler)> hooks =
            new List<(Health, System.Action<float, GameObject>)>();
        readonly List<(Health health, System.Action<float> handler)> healHooks =
            new List<(Health, System.Action<float>)>();
        bool managerHooked;

        void Start()
        {
            TryHookManager();
            if (enemyHitPrefab != null) enemyHitPrefab.PrewarmPool();
            if (allyHurtPrefab != null) allyHurtPrefab.PrewarmPool();
            if (healPrefab != null) healPrefab.PrewarmPool();
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

            // Slight jitter so rapid hits don't stack into one unreadable pile;
            // following the victim keeps numbers glued to moving targets.
            Vector3 pos = victim.transform.position + Vector3.up * 2.1f +
                          new Vector3(Random.Range(-0.25f, 0.25f), 0f, 0f);
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
