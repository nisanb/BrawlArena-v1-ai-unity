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
        Image fill;
        CanvasGroup group;
        Camera cam;
        float displayed = 1f;

        public static HealthBarWorld Create(BrawlerController owner)
        {
            var root = new GameObject("HealthBar", typeof(RectTransform));
            root.transform.SetParent(owner.transform, false);
            root.transform.localPosition = new Vector3(0f, 2.45f, 0f);
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
            rt.sizeDelta = new Vector2(120f, 14f);
            canvasGo.transform.localScale = Vector3.one * 0.012f;
            group = canvasGo.GetComponent<CanvasGroup>();

            var bg = NewImage("BG", canvasGo.transform, new Color(0.06f, 0.06f, 0.09f, 0.8f));
            Stretch(bg.rectTransform);

            fill = NewImage("Fill", canvasGo.transform, TeamUtil.Color(owner.team));
            var frt = fill.rectTransform;
            frt.anchorMin = Vector2.zero;
            frt.anchorMax = Vector2.one;
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

        static void Stretch(RectTransform rt)
        {
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;
        }
    }
}
