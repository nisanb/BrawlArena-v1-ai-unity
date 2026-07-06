using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace BrawlArena
{
    /// <summary>
    /// Top-left kill log: "X killed Y" entries with team-colored names that
    /// slide in, hold for a few seconds and fade out. Mounted and positioned
    /// by BrawlHUD; listens to MatchManager.Kill.
    /// </summary>
    public class KillFeed : MonoBehaviour
    {
        public int maxEntries = 4;
        public float entryLifetime = 4.5f;
        public float fadeDuration = 0.6f;

        readonly List<Entry> entries = new List<Entry>();
        Font font;
        bool hooked;

        class Entry
        {
            public GameObject root;
            public CanvasGroup group;
            public float bornAt;
        }

        void Awake()
        {
            font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        }

        void Start()
        {
            TryHook();
        }

        void TryHook()
        {
            if (hooked || MatchManager.Instance == null) return;
            MatchManager.Instance.Kill += OnKill;
            hooked = true;
        }

        void OnDestroy()
        {
            if (hooked && MatchManager.Instance != null)
                MatchManager.Instance.Kill -= OnKill;
        }

        void OnKill(BrawlerController victim, BrawlerController attacker)
        {
            string attackerName = attacker != null ? attacker.displayName : "THE ARENA";
            Color attackerColor = attacker != null ? TeamUtil.Color(attacker.team) : Color.gray;
            AddEntry(attackerName, attackerColor, victim.displayName, TeamUtil.Color(victim.team));
        }

        void AddEntry(string attackerName, Color attackerColor, string victimName, Color victimColor)
        {
            var theme = UiTheme.Instance;
            var root = new GameObject("KillEntry", typeof(RectTransform), typeof(CanvasGroup));
            root.transform.SetParent(transform, false);
            var rt = (RectTransform)root.transform;
            rt.anchorMin = rt.anchorMax = new Vector2(0f, 1f);
            rt.pivot = new Vector2(0f, 1f);
            rt.sizeDelta = new Vector2(430f, 44f);

            var bg = root.AddComponent<Image>();
            if (theme != null && theme.labelChip != null)
            {
                bg.sprite = theme.labelChip;
                bg.type = Image.Type.Sliced;
                bg.color = new Color(1f, 1f, 1f, 0.9f);
            }
            else
            {
                bg.color = new Color(0f, 0f, 0f, 0.45f);
            }
            bg.raycastTarget = false;

            MakeText(root.transform, attackerName, attackerColor, TextAnchor.MiddleLeft, 0.04f, 0.42f);
            MakeText(root.transform, "killed", new Color(0.15f, 0.15f, 0.2f, 0.95f), TextAnchor.MiddleCenter, 0.42f, 0.58f);
            MakeText(root.transform, victimName, victimColor, TextAnchor.MiddleRight, 0.58f, 0.96f);

            entries.Add(new Entry
            {
                root = root,
                group = root.GetComponent<CanvasGroup>(),
                bornAt = Time.time,
            });
            while (entries.Count > maxEntries)
            {
                Destroy(entries[0].root);
                entries.RemoveAt(0);
            }
            Relayout();
        }

        void MakeText(Transform parent, string content, Color color, TextAnchor anchor, float xMin, float xMax)
        {
            var go = new GameObject("Text", typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var rt = (RectTransform)go.transform;
            rt.anchorMin = new Vector2(xMin, 0f);
            rt.anchorMax = new Vector2(xMax, 1f);
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;
            var txt = go.AddComponent<Text>();
            txt.font = font;
            txt.text = content;
            txt.fontSize = 26;
            txt.fontStyle = FontStyle.Bold;
            txt.color = color;
            txt.alignment = anchor;
            txt.horizontalOverflow = HorizontalWrapMode.Overflow;
            txt.raycastTarget = false;
            var outline = go.AddComponent<Outline>();
            outline.effectColor = new Color(0f, 0f, 0f, 0.6f);
            outline.effectDistance = new Vector2(1.5f, -1.5f);
        }

        void Relayout()
        {
            for (int i = 0; i < entries.Count; i++)
            {
                var rt = (RectTransform)entries[i].root.transform;
                rt.anchoredPosition = new Vector2(0f, -(entries.Count - 1 - i) * 50f);
            }
        }

        void Update()
        {
            TryHook();
            bool removed = false;
            for (int i = entries.Count - 1; i >= 0; i--)
            {
                float age = Time.time - entries[i].bornAt;
                if (age >= entryLifetime)
                {
                    Destroy(entries[i].root);
                    entries.RemoveAt(i);
                    removed = true;
                }
                else if (age >= entryLifetime - fadeDuration)
                {
                    entries[i].group.alpha = Mathf.Clamp01((entryLifetime - age) / fadeDuration);
                }
            }
            if (removed) Relayout();
        }
    }
}
