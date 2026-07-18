using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace BrawlArena
{
    /// <summary>
    /// Top-right minimap: a top-down capture of the arena (generated at scene
    /// build time) with live dot markers — the player as a ringed white dot,
    /// allies blue, enemies red, and loose gems green. Enemy dots hide while
    /// their brawler is down. Built in code by BrawlHUD.
    /// </summary>
    public class MinimapView : MonoBehaviour
    {
        [Tooltip("World half-extent represented by the captured arena map, including its cliff ring.")]
        public float worldHalfExtent = ArenaLayout.MinimapHalfExtent;

        RectTransform markersRoot;
        float mapHalf;

        Image playerMarker;
        readonly List<Image> brawlerMarkers = new List<Image>();
        readonly List<BrawlerController> markerOwners = new List<BrawlerController>();
        readonly List<Image> gemMarkers = new List<Image>();
        Image zoneCircle;

        static Sprite dotSprite;

        public static MinimapView Create(Transform parent, UiTheme theme, float size)
        {
            var root = new GameObject("Minimap", typeof(RectTransform));
            root.transform.SetParent(parent, false);
            var rt = (RectTransform)root.transform;
            rt.anchorMin = rt.anchorMax = new Vector2(1f, 1f);
            rt.pivot = new Vector2(1f, 1f);
            rt.anchoredPosition = new Vector2(-24f, -24f);
            rt.sizeDelta = new Vector2(size, size);

            var frame = root.AddComponent<Image>();
            if (theme != null && theme.frame != null)
            {
                frame.sprite = theme.frame;
                frame.type = Image.Type.Sliced;
                frame.color = new Color(0.1f, 0.12f, 0.2f, 0.85f);
            }
            else
            {
                frame.color = new Color(0f, 0f, 0f, 0.55f);
            }
            frame.raycastTarget = false;

            var mapGo = new GameObject("Map", typeof(RectTransform));
            mapGo.transform.SetParent(root.transform, false);
            var mrt = (RectTransform)mapGo.transform;
            mrt.anchorMin = Vector2.zero;
            mrt.anchorMax = Vector2.one;
            mrt.offsetMin = new Vector2(8f, 8f);
            mrt.offsetMax = new Vector2(-8f, -8f);
            var mapImg = mapGo.AddComponent<Image>();
            if (theme != null && theme.minimapBackground != null)
            {
                mapImg.sprite = theme.minimapBackground;
                mapImg.color = new Color(0.85f, 0.85f, 0.9f, 0.95f);
            }
            else
            {
                mapImg.color = new Color(0.2f, 0.3f, 0.2f, 0.9f);
            }
            mapImg.raycastTarget = false;

            var view = root.AddComponent<MinimapView>();

            var zoneGo = new GameObject("ZoneCircle", typeof(RectTransform));
            zoneGo.transform.SetParent(mapGo.transform, false);
            var zrt = (RectTransform)zoneGo.transform;
            zrt.anchorMin = zrt.anchorMax = new Vector2(0.5f, 0.5f);
            zrt.sizeDelta = Vector2.zero;
            view.zoneCircle = zoneGo.AddComponent<Image>();
            view.zoneCircle.sprite = GetDotSprite();
            view.zoneCircle.raycastTarget = false;
            view.zoneCircle.color = new Color(1f, 1f, 1f, 0f);
            view.zoneCircle.enabled = false;

            var markers = new GameObject("Markers", typeof(RectTransform));
            markers.transform.SetParent(mapGo.transform, false);
            var krt = (RectTransform)markers.transform;
            krt.anchorMin = Vector2.zero;
            krt.anchorMax = Vector2.one;
            krt.offsetMin = Vector2.zero;
            krt.offsetMax = Vector2.zero;
            // Per-frame marker movement rebatches only this subtree.
            markers.AddComponent<Canvas>();
            view.markersRoot = krt;
            view.mapHalf = (size - 16f) * 0.5f;
            return view;
        }

        Image NewMarker(string name, Color color, float size)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(markersRoot, false);
            var rt = (RectTransform)go.transform;
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.sizeDelta = new Vector2(size, size);
            var img = go.AddComponent<Image>();
            img.sprite = GetDotSprite();
            img.color = color;
            img.raycastTarget = false;
            return img;
        }

        Vector2 ToMap(Vector3 world)
        {
            return new Vector2(
                Mathf.Clamp(world.x / worldHalfExtent, -1f, 1f) * mapHalf,
                Mathf.Clamp(world.z / worldHalfExtent, -1f, 1f) * mapHalf);
        }

        void LateUpdate()
        {
            var mm = MatchManager.Instance;
            if (mm == null) return;
            UpdateBrawlerMarkers(mm);
            UpdateGemMarkers();
            UpdateZoneCircle();
        }

        /// <summary>
        /// Draws the Control Zone as a translucent disc tinted by whichever
        /// team holds it, pulsing while contested (skipped under reduced
        /// motion). Hidden outside Control Zone or before the zone activates.
        /// </summary>
        void UpdateZoneCircle()
        {
            if (zoneCircle == null) return;
            ControlZoneManager zone = ControlZoneManager.Instance;
            if (zone == null || !zone.ActiveMode || zone.State == ControlZoneState.Inactive)
            {
                zoneCircle.enabled = false;
                return;
            }

            zoneCircle.enabled = true;
            zoneCircle.rectTransform.anchoredPosition = ToMap(zone.ZoneCenter);
            float scale = mapHalf / Mathf.Max(0.01f, worldHalfExtent);
            float diameter = zone.ZoneRadius * 2f * scale;
            zoneCircle.rectTransform.sizeDelta = new Vector2(diameter, diameter);

            Color tint = zone.HasControllingTeam
                ? TeamUtil.Color(zone.ControllingTeam)
                : zone.IsContested
                    ? new Color(1f, 0.68f, 0.12f)
                    : new Color(0.7f, 0.8f, 0.9f);
            float alpha = 0.3f;
            if (zone.IsContested)
            {
                alpha = AccessibilitySettings.ReducedMotionEnabled
                    ? 0.32f
                    : 0.22f + 0.14f * (0.5f + 0.5f * Mathf.Sin(Time.unscaledTime * 5f));
            }
            zoneCircle.color = new Color(tint.r, tint.g, tint.b, alpha);
        }

        void UpdateBrawlerMarkers(MatchManager mm)
        {
            var brawlers = mm.GetBrawlers();
            // Grow the pool to match roster size (fixed after spawn).
            while (brawlerMarkers.Count < brawlers.Count)
            {
                brawlerMarkers.Add(NewMarker("Brawler", Color.white, 22f));
                markerOwners.Add(null);
            }

            for (int i = 0; i < brawlerMarkers.Count; i++)
            {
                var b = i < brawlers.Count ? brawlers[i] : null;
                var marker = brawlerMarkers[i];
                if (b == null || b.IsDead)
                {
                    // Hide the whole marker hierarchy: the local-player marker
                    // owns a child team-colored core that would otherwise keep
                    // rendering after only the outer Image was disabled.
                    marker.gameObject.SetActive(false);
                    continue;
                }
                marker.gameObject.SetActive(true);
                if (markerOwners[i] != b)
                {
                    markerOwners[i] = b;
                    Color c = TeamUtil.Color(b.team);
                    if (b.IsPlayer)
                    {
                        marker.color = Color.white;
                        marker.rectTransform.sizeDelta = new Vector2(30f, 30f);
                        // Team-colored core inside the white ring.
                        var core = NewMarker("Core", c, 18f);
                        core.transform.SetParent(marker.transform, false);
                        playerMarker = marker;
                    }
                    else
                    {
                        marker.color = c;
                    }
                }
                marker.rectTransform.anchoredPosition = ToMap(b.transform.position);
            }
            // Keep the player drawn above ally/enemy dots.
            if (playerMarker != null) playerMarker.transform.SetAsLastSibling();
        }

        void UpdateGemMarkers()
        {
            var gems = GemGrabManager.Instance;
            int needed = 0;
            if (gems != null && gems.ActiveMode)
            {
                var loose = gems.LooseGems;
                for (int i = 0; i < loose.Count; i++)
                {
                    var gem = loose[i];
                    if (gem == null || !gem.CanBePicked) continue;
                    if (needed >= gemMarkers.Count)
                        gemMarkers.Add(NewMarker("Gem", new Color(0.25f, 1f, 0.55f), 14f));
                    var marker = gemMarkers[needed];
                    marker.enabled = true;
                    marker.rectTransform.anchoredPosition = ToMap(gem.transform.position);
                    marker.rectTransform.localRotation = Quaternion.Euler(0f, 0f, 45f);
                    needed++;
                }
            }
            for (int i = needed; i < gemMarkers.Count; i++)
                gemMarkers[i].enabled = false;
        }

        static Sprite GetDotSprite()
        {
            if (dotSprite != null) return dotSprite;
            const int size = 32;
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
            tex.name = "MinimapDot";
            float r = size / 2f - 1f;
            Vector2 c = new Vector2(size / 2f, size / 2f);
            var px = new Color[size * size];
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float d = Vector2.Distance(new Vector2(x + 0.5f, y + 0.5f), c);
                    px[y * size + x] = new Color(1f, 1f, 1f, Mathf.Clamp01(r - d + 0.5f));
                }
            }
            tex.SetPixels(px);
            tex.Apply();
            dotSprite = Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), 100f);
            return dotSprite;
        }
    }
}
