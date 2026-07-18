using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace BrawlArena
{
    /// <summary>
    /// Compact, team-colored KO log mounted by BrawlHUD.
    /// </summary>
    public class KillFeed : MonoBehaviour
    {
        public int maxEntries = 4;
        public float entryLifetime = 4.5f;
        public float fadeDuration = 0.6f;

        readonly List<Entry> entries = new List<Entry>();
        bool hooked;

        class Entry
        {
            public GameObject root;
            public CanvasGroup group;
            public float bornAt;
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
            var root = new GameObject("KillEntry", typeof(RectTransform), typeof(CanvasGroup));
            root.transform.SetParent(transform, false);
            var rt = (RectTransform)root.transform;
            rt.anchorMin = rt.anchorMax = new Vector2(0f, 1f);
            rt.pivot = new Vector2(0f, 1f);
            rt.sizeDelta = new Vector2(420f, 44f);

            var theme = UiTheme.Instance;
            var background = root.AddComponent<Image>();
            if (theme != null && theme.labelChip != null)
            {
                background.sprite = theme.labelChip;
                background.type = Image.Type.Sliced;
                background.color = new Color(0.04f, 0.1f, 0.18f, 0.92f);
            }
            else
            {
                background.color = new Color(0f, 0f, 0f, 0.55f);
            }
            background.raycastTarget = false;

            MakeText(root.transform, attackerName, attackerColor, TextAlignmentOptions.MidlineLeft, 0.05f, 0.38f);
            MakeText(root.transform, "KO", new Color(1f, 0.88f, 0.3f), TextAlignmentOptions.Center, 0.38f, 0.62f);
            MakeText(root.transform, victimName, victimColor, TextAlignmentOptions.MidlineRight, 0.62f, 0.95f);

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

        void MakeText(Transform parent, string content, Color color, TextAlignmentOptions alignment,
            float xMin, float xMax)
        {
            var go = new GameObject("Text", typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var rt = (RectTransform)go.transform;
            rt.anchorMin = new Vector2(xMin, 0f);
            rt.anchorMax = new Vector2(xMax, 1f);
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;

            var theme = UiTheme.Instance;
            var text = go.AddComponent<TextMeshProUGUI>();
            text.font = theme != null && theme.bodyFont != null ? theme.bodyFont : TMP_Settings.defaultFontAsset;
            text.text = content;
            text.fontSize = 21f;
            text.color = color;
            text.alignment = alignment;
            text.enableWordWrapping = false;
            text.overflowMode = TextOverflowModes.Ellipsis;
            text.margin = new Vector4(6f, 2f, 6f, 2f);
            text.raycastTarget = false;

            var shadow = go.AddComponent<Shadow>();
            shadow.effectColor = new Color(0f, 0f, 0f, 0.7f);
            shadow.effectDistance = new Vector2(1.5f, -1.5f);
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
