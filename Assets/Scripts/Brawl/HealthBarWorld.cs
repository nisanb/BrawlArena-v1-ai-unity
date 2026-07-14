using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace BrawlArena
{
    /// <summary>
    /// Team-colored billboard health bar floating above a brawler's head.
    /// Built entirely in code; fades out while the brawler is down.
    /// </summary>
    public class HealthBarWorld : MonoBehaviour
    {
        BrawlerController owner;
        HeroMatchProgression progression;
        Image fill;
        TextMeshProUGUI identity;
        CanvasGroup group;
        Camera cam;
        float displayed = 1f;
        int shownLevel = -1;
        string shownName;

        public static HealthBarWorld Create(BrawlerController owner)
        {
            var root = new GameObject("HealthBar", typeof(RectTransform));
            root.transform.SetParent(owner.transform, false);
            root.transform.localPosition = new Vector3(0f, 2.55f, 0f);
            var bar = root.AddComponent<HealthBarWorld>();
            bar.owner = owner;
            bar.Build();
            return bar;
        }

        void Build()
        {
            var canvasGo = new GameObject("Canvas", typeof(RectTransform), typeof(Canvas), typeof(CanvasGroup));
            canvasGo.transform.SetParent(transform, false);
            var canvas = canvasGo.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.WorldSpace;
            var rt = (RectTransform)canvasGo.transform;
            rt.sizeDelta = new Vector2(170f, 36f);
            canvasGo.transform.localScale = Vector3.one * 0.012f;
            group = canvasGo.GetComponent<CanvasGroup>();

            var nameBackground = NewImage("NameBG", canvasGo.transform,
                new Color(0.025f, 0.04f, 0.075f, 0.82f));
            var nameBgRt = nameBackground.rectTransform;
            nameBgRt.anchorMin = new Vector2(0.04f, 0.43f);
            nameBgRt.anchorMax = new Vector2(0.96f, 1f);
            nameBgRt.offsetMin = Vector2.zero;
            nameBgRt.offsetMax = Vector2.zero;

            identity = NewText("Identity", canvasGo.transform);
            var identityRt = identity.rectTransform;
            identityRt.anchorMin = new Vector2(0.04f, 0.43f);
            identityRt.anchorMax = new Vector2(0.96f, 1f);
            identityRt.offsetMin = Vector2.zero;
            identityRt.offsetMax = Vector2.zero;

            var bg = NewImage("BG", canvasGo.transform, new Color(0.06f, 0.06f, 0.09f, 0.8f));
            var bgRt = bg.rectTransform;
            bgRt.anchorMin = new Vector2(0.08f, 0.04f);
            bgRt.anchorMax = new Vector2(0.92f, 0.37f);
            bgRt.offsetMin = Vector2.zero;
            bgRt.offsetMax = Vector2.zero;

            fill = NewImage("Fill", canvasGo.transform, TeamUtil.Color(owner.team));
            var frt = fill.rectTransform;
            frt.anchorMin = new Vector2(0.08f, 0.04f);
            frt.anchorMax = new Vector2(0.92f, 0.37f);
            frt.offsetMin = new Vector2(2f, 2f);
            frt.offsetMax = new Vector2(-2f, -2f);
            // Image.Type.Filled is ignored when the Image has no sprite.
            fill.sprite = GetWhiteSprite();
            fill.type = Image.Type.Filled;
            fill.fillMethod = Image.FillMethod.Horizontal;
        }

        void LateUpdate()
        {
            if (owner == null)
            {
                Destroy(gameObject);
                return;
            }
            if (cam == null) cam = Camera.main;
            if (cam != null) transform.rotation = cam.transform.rotation;

            group.alpha = Mathf.MoveTowards(group.alpha, owner.IsDead ? 0f : 1f, Time.deltaTime * 6f);

            float target = owner.Health.Max > 0f ? owner.Health.Current / owner.Health.Max : 0f;
            displayed = Mathf.MoveTowards(displayed, target, Time.deltaTime * 2.5f);
            fill.fillAmount = displayed;

            if (progression == null) progression = owner.GetComponent<HeroMatchProgression>();
            int level = progression != null ? progression.Level : 1;
            string heroName = string.IsNullOrWhiteSpace(owner.displayName)
                ? "HERO"
                : owner.displayName.Trim().ToUpperInvariant();
            if (shownLevel != level || shownName != heroName)
            {
                shownLevel = level;
                shownName = heroName;
                identity.text = heroName + "  •  LV " + level;
            }
        }

        static Sprite whiteSprite;

        static Sprite GetWhiteSprite()
        {
            if (whiteSprite != null) return whiteSprite;
            var tex = Texture2D.whiteTexture;
            whiteSprite = Sprite.Create(tex, new Rect(0f, 0f, tex.width, tex.height), new Vector2(0.5f, 0.5f), 100f);
            return whiteSprite;
        }

        static Image NewImage(string name, Transform parent, Color color)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Image));
            go.transform.SetParent(parent, false);
            var img = go.GetComponent<Image>();
            img.color = color;
            img.raycastTarget = false;
            return img;
        }

        static TextMeshProUGUI NewText(string name, Transform parent)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(TextMeshProUGUI));
            go.transform.SetParent(parent, false);
            var text = go.GetComponent<TextMeshProUGUI>();
            TMP_FontAsset font = UiTheme.Instance != null ? UiTheme.Instance.buttonFont : null;
            if (font == null) font = TMP_Settings.defaultFontAsset;
            if (font != null) text.font = font;
            text.fontSize = 12f;
            text.color = Color.white;
            text.alignment = TextAlignmentOptions.Center;
            text.textWrappingMode = TextWrappingModes.NoWrap;
            text.overflowMode = TextOverflowModes.Ellipsis;
            text.raycastTarget = false;
            return text;
        }

        static void Stretch(RectTransform rt)
        {
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;
        }
    }
}
