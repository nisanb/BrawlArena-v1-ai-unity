using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace BrawlArena
{
    /// <summary>Runtime-built, safe-area-aware first-match coach overlay.</summary>
    public sealed class GameplayCoach : MonoBehaviour
    {
        CanvasGroup overlayGroup;
        RectTransform cardRect;
        TextMeshProUGUI titleText;
        TextMeshProUGUI controlText;
        TextMeshProUGUI bodyText;
        TextMeshProUGUI progressText;
        TextMeshProUGUI nextText;
        Image pageIcon;
        UiTheme theme;
        int pageIndex;
        bool closing;

        public static Vector2 ReferenceResolution => new Vector2(2560f, 1440f);
        public static Vector2 CardSize => new Vector2(1320f, 760f);
        public bool Finished { get; private set; }

        public static bool ReplayNextMatch()
        {
            return GameplayCoachState.RequestReplay();
        }

        public static bool ResetProgress()
        {
            return GameplayCoachState.ResetCompletion();
        }

        public static IEnumerator ShowIfNeeded(bool automation)
        {
            if (!GameplayCoachState.ShouldShow(automation)) yield break;

            var host = new GameObject("GameplayCoachRuntime");
            var coach = host.AddComponent<GameplayCoach>();
            coach.Build();
            while (coach != null && !coach.Finished) yield return null;
            if (host != null) Destroy(host);
        }

        void Build()
        {
            theme = UiTheme.Instance != null ? UiTheme.Instance : FindFirstObjectByType<UiTheme>();

            var canvasGo = NewRect("CoachCanvas", transform);
            var canvas = canvasGo.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 100;
            var scaler = canvasGo.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = ReferenceResolution;
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            scaler.matchWidthOrHeight = 0.5f;
            canvasGo.AddComponent<GraphicRaycaster>();

            var safeArea = NewRect("SafeArea", canvasGo.transform);
            Stretch((RectTransform)safeArea.transform);
            safeArea.AddComponent<BrawlSafeArea>();

            var dim = NewRect("InputBlocker", safeArea.transform);
            Stretch((RectTransform)dim.transform);
            var dimImage = dim.AddComponent<Image>();
            dimImage.color = new Color(0.005f, 0.018f, 0.05f, 0.9f);
            dimImage.raycastTarget = true;

            overlayGroup = safeArea.AddComponent<CanvasGroup>();
            overlayGroup.interactable = true;
            overlayGroup.blocksRaycasts = true;

            var card = NewRect("CoachCard", safeArea.transform);
            cardRect = (RectTransform)card.transform;
            cardRect.anchorMin = cardRect.anchorMax = new Vector2(0.5f, 0.5f);
            cardRect.sizeDelta = CardSize;
            var cardImage = card.AddComponent<Image>();
            cardImage.sprite = theme != null && theme.panel != null ? theme.panel : null;
            cardImage.type = cardImage.sprite != null ? Image.Type.Sliced : Image.Type.Simple;
            cardImage.color = AccessibilitySettings.HighContrastEnabled
                ? new Color(0.015f, 0.08f, 0.16f, 1f)
                : new Color(0.025f, 0.13f, 0.25f, 0.99f);
            cardImage.raycastTarget = true;

            var kicker = MakeText(card.transform, "FIRST MATCH COACH", 28f,
                new Color(0.45f, 0.9f, 1f), FontStyle.Button);
            Place(kicker.rectTransform, new Vector2(0.5f, 0.91f), new Vector2(680f, 50f));

            pageIcon = NewRect("ControlIcon", card.transform).AddComponent<Image>();
            var iconRect = pageIcon.rectTransform;
            iconRect.anchorMin = iconRect.anchorMax = new Vector2(0.18f, 0.62f);
            iconRect.sizeDelta = new Vector2(220f, 220f);
            pageIcon.preserveAspect = true;
            pageIcon.raycastTarget = false;

            titleText = MakeText(card.transform, "MOVE", 78f, Color.white, FontStyle.Display);
            Place(titleText.rectTransform, new Vector2(0.61f, 0.7f), new Vector2(760f, 110f));
            titleText.alignment = TextAlignmentOptions.Left;

            var controlChip = NewRect("ControlChip", card.transform);
            var chipRect = (RectTransform)controlChip.transform;
            chipRect.anchorMin = chipRect.anchorMax = new Vector2(0.61f, 0.57f);
            chipRect.sizeDelta = new Vector2(760f, 70f);
            var chipImage = controlChip.AddComponent<Image>();
            chipImage.sprite = theme != null ? theme.labelChip : null;
            chipImage.type = chipImage.sprite != null ? Image.Type.Sliced : Image.Type.Simple;
            chipImage.color = new Color(0.06f, 0.42f, 0.68f, 0.98f);
            chipImage.raycastTarget = false;
            controlText = MakeText(controlChip.transform, "", 30f, Color.white, FontStyle.Button);
            Stretch(controlText.rectTransform);

            bodyText = MakeText(card.transform, "", 40f,
                new Color(0.94f, 0.98f, 1f), FontStyle.Body);
            var bodyRect = bodyText.rectTransform;
            bodyRect.anchorMin = bodyRect.anchorMax = new Vector2(0.5f, 0.36f);
            bodyRect.sizeDelta = new Vector2(1080f, 170f);
            bodyText.alignment = TextAlignmentOptions.TopLeft;
            bodyText.textWrappingMode = TextWrappingModes.Normal;
            bodyText.overflowMode = TextOverflowModes.Ellipsis;

            progressText = MakeText(card.transform, "1 / 4", 26f,
                new Color(0.7f, 0.88f, 1f), FontStyle.Button);
            Place(progressText.rectTransform, new Vector2(0.5f, 0.15f), new Vector2(240f, 44f));

            BuildButton(card.transform, "SKIP", new Vector2(0.28f, 0.08f),
                theme != null ? theme.buttonNavy : null, OnSkip, out _);
            BuildButton(card.transform, "NEXT", new Vector2(0.72f, 0.08f),
                theme != null ? theme.buttonGreen : null, OnNext, out nextText);

            UpdatePage(false);
            if (AccessibilitySettings.ReducedMotionEnabled)
            {
                overlayGroup.alpha = 1f;
                cardRect.localScale = Vector3.one;
            }
            else
            {
                overlayGroup.alpha = 0f;
                cardRect.localScale = Vector3.one * 0.92f;
                StartCoroutine(AnimateIn());
            }
        }

        void OnNext()
        {
            if (closing) return;
            if (pageIndex >= GameplayCoachState.PageCount - 1)
            {
                CompleteAndClose();
                return;
            }

            pageIndex++;
            UpdatePage(true);
        }

        void OnSkip()
        {
            if (!closing) CompleteAndClose();
        }

        void CompleteAndClose()
        {
            closing = true;
            GameplayCoachState.MarkCompleted();
            if (AccessibilitySettings.ReducedMotionEnabled)
            {
                Finished = true;
                return;
            }
            StartCoroutine(AnimateOut());
        }

        void UpdatePage(bool animate)
        {
            GameplayCoachPage page = GameplayCoachState.GetPage(pageIndex);
            titleText.text = page.Title;
            controlText.text = page.Control;
            bodyText.text = page.Body;
            progressText.text = (pageIndex + 1) + " / " + GameplayCoachState.PageCount;
            nextText.text = pageIndex == GameplayCoachState.PageCount - 1 ? "START" : "NEXT";

            if (pageIcon != null)
            {
                pageIcon.sprite = PageIcon(pageIndex);
                pageIcon.enabled = pageIcon.sprite != null;
            }

            if (animate && !AccessibilitySettings.ReducedMotionEnabled)
            {
                StopCoroutine(nameof(PulsePage));
                StartCoroutine(nameof(PulsePage));
            }
        }

        Sprite PageIcon(int index)
        {
            if (theme == null) return null;
            switch (index)
            {
                case 0: return theme.speedIcon;
                case 1: return theme.swordIcon;
                case 2: return theme.energyIcon;
                default: return theme.starOnIcon;
            }
        }

        IEnumerator AnimateIn()
        {
            const float duration = 0.18f;
            for (float t = 0f; t < duration; t += Time.unscaledDeltaTime)
            {
                float amount = Mathf.SmoothStep(0f, 1f, t / duration);
                overlayGroup.alpha = amount;
                cardRect.localScale = Vector3.one * Mathf.Lerp(0.92f, 1f, amount);
                yield return null;
            }
            overlayGroup.alpha = 1f;
            cardRect.localScale = Vector3.one;
        }

        IEnumerator PulsePage()
        {
            cardRect.localScale = Vector3.one * 0.975f;
            const float duration = 0.12f;
            for (float t = 0f; t < duration; t += Time.unscaledDeltaTime)
            {
                cardRect.localScale = Vector3.one * Mathf.Lerp(0.975f, 1f, t / duration);
                yield return null;
            }
            cardRect.localScale = Vector3.one;
        }

        IEnumerator AnimateOut()
        {
            const float duration = 0.14f;
            float startAlpha = overlayGroup.alpha;
            for (float t = 0f; t < duration; t += Time.unscaledDeltaTime)
            {
                float amount = Mathf.SmoothStep(0f, 1f, t / duration);
                overlayGroup.alpha = Mathf.Lerp(startAlpha, 0f, amount);
                cardRect.localScale = Vector3.one * Mathf.Lerp(1f, 0.96f, amount);
                yield return null;
            }
            Finished = true;
        }

        enum FontStyle
        {
            Body,
            Button,
            Display,
        }

        TextMeshProUGUI MakeText(Transform parent, string content, float size, Color color,
            FontStyle style)
        {
            var go = NewRect("Text", parent);
            var text = go.AddComponent<TextMeshProUGUI>();
            if (theme != null)
            {
                text.font = style == FontStyle.Display
                    ? theme.headingFont
                    : style == FontStyle.Button
                        ? theme.buttonFont
                        : theme.bodyFont;
            }
            if (text.font == null) text.font = TMP_Settings.defaultFontAsset;
            text.text = content;
            text.fontSize = size;
            text.color = color;
            text.alignment = TextAlignmentOptions.Center;
            text.raycastTarget = false;
            return text;
        }

        void BuildButton(Transform parent, string label, Vector2 anchor, Sprite sprite,
            UnityEngine.Events.UnityAction action, out TextMeshProUGUI text)
        {
            var go = NewRect(label + "Button", parent);
            var rect = (RectTransform)go.transform;
            rect.anchorMin = rect.anchorMax = anchor;
            rect.sizeDelta = new Vector2(360f, 104f);
            var image = go.AddComponent<Image>();
            image.sprite = sprite;
            image.type = sprite != null ? Image.Type.Sliced : Image.Type.Simple;
            image.color = sprite != null ? Color.white : new Color(0.1f, 0.55f, 0.82f, 1f);
            var button = go.AddComponent<Button>();
            button.onClick.AddListener(action);
            text = MakeText(go.transform, label, 36f, Color.white, FontStyle.Button);
            Stretch(text.rectTransform);
            text.rectTransform.offsetMin = new Vector2(0f, 8f);
        }

        static GameObject NewRect(string name, Transform parent)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            return go;
        }

        static void Place(RectTransform rect, Vector2 anchor, Vector2 size)
        {
            rect.anchorMin = rect.anchorMax = anchor;
            rect.sizeDelta = size;
        }

        static void Stretch(RectTransform rect)
        {
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
        }
    }
}
