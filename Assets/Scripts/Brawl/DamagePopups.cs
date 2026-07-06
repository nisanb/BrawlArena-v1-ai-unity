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

        readonly List<(Health health, System.Action<float, GameObject> handler)> hooks =
            new List<(Health, System.Action<float, GameObject>)>();
        bool managerHooked;

        void Start()
        {
            TryHookManager();
            if (enemyHitPrefab != null) enemyHitPrefab.PrewarmPool();
            if (allyHurtPrefab != null) allyHurtPrefab.PrewarmPool();
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
        }

        void OnDestroy()
        {
            if (managerHooked && MatchManager.Instance != null)
                MatchManager.Instance.BrawlerRegistered -= HookBrawler;
            foreach (var (health, handler) in hooks)
                if (health != null) health.Damaged -= handler;
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
    }
}
