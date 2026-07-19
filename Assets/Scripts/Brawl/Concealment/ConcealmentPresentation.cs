using System.Collections.Generic;
using UnityEngine;

namespace BrawlArena
{
    /// <summary>
    /// Hides a concealed enemy brawler from the local player's screen: all
    /// child renderers plus the world-space health bar are toggled off while
    /// ConcealmentTracker.IsHiddenFrom(local player) holds. Hard hide rather
    /// than a shader fade — the character material zoo (toon, URP lit,
    /// particles) makes a universal fade unreliable. When no local player
    /// exists (headless playtests) everything stays visible.
    /// </summary>
    public class ConcealmentPresentation : MonoBehaviour
    {
        const float HiddenRescanInterval = 0.5f;
        const float ViewerScanInterval = 1f;

        BrawlerController self;
        ConcealmentTracker tracker;
        HealthBarWorld healthBar;
        readonly Dictionary<Renderer, bool> hiddenRenderers = new Dictionary<Renderer, bool>();
        bool hidden;
        float nextRescan;

        static BrawlerController localViewer;
        static float nextViewerScan;

        void Awake()
        {
            self = GetComponent<BrawlerController>();
            tracker = GetComponent<ConcealmentTracker>();
        }

        void OnDisable()
        {
            SetHidden(false);
        }

        static BrawlerController ResolveLocalViewer()
        {
            if (Time.unscaledTime < nextViewerScan && localViewer != null) return localViewer;
            nextViewerScan = Time.unscaledTime + ViewerScanInterval;
            localViewer = null;
            if (MatchManager.Instance == null) return null;
            var brawlers = MatchManager.Instance.GetBrawlers();
            for (int i = 0; i < brawlers.Count; i++)
            {
                if (brawlers[i] != null && brawlers[i].IsPlayer)
                {
                    localViewer = brawlers[i];
                    break;
                }
            }
            return localViewer;
        }

        void LateUpdate()
        {
            var viewer = ResolveLocalViewer();
            bool shouldHide = viewer != null && tracker != null && tracker.IsHiddenFrom(viewer);
            if (shouldHide != hidden)
            {
                SetHidden(shouldHide);
            }
            else if (hidden && Time.time >= nextRescan)
            {
                // Catch renderers spawned under us while hidden (trail VFX,
                // weapon swaps) without re-walking the hierarchy every frame.
                DisableChildRenderers();
                nextRescan = Time.time + HiddenRescanInterval;
            }
        }

        void SetHidden(bool value)
        {
            hidden = value;
            if (value)
            {
                DisableChildRenderers();
                nextRescan = Time.time + HiddenRescanInterval;
            }
            else
            {
                foreach (var pair in hiddenRenderers)
                    if (pair.Key != null) pair.Key.enabled = pair.Value;
                hiddenRenderers.Clear();
            }

            if (healthBar == null) healthBar = GetComponentInChildren<HealthBarWorld>(true);
            if (healthBar != null) healthBar.gameObject.SetActive(!value);
        }

        void DisableChildRenderers()
        {
            var renderers = GetComponentsInChildren<Renderer>();
            for (int i = 0; i < renderers.Length; i++)
            {
                var renderer = renderers[i];
                if (!renderer.enabled || hiddenRenderers.ContainsKey(renderer)) continue;
                hiddenRenderers.Add(renderer, true);
                renderer.enabled = false;
            }
        }
    }
}
