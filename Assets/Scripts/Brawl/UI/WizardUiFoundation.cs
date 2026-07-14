using UnityEngine;
using UnityEngine.UI;

namespace BrawlArena
{
    /// <summary>
    /// Marks a project-owned GUI Pro composition as a decorative foundation.
    /// The authored hierarchy, masks, glows, and UI particles remain intact,
    /// while gameplay input is handled exclusively by the live views above it.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class WizardUiFoundation : MonoBehaviour
    {
        static readonly Vector2 ReferenceSize = new Vector2(2560f, 1440f);

        void Awake()
        {
            MakeDecorative();
        }

        public void MakeDecorative()
        {
            var group = GetComponent<CanvasGroup>();
            if (group == null) group = gameObject.AddComponent<CanvasGroup>();
            group.interactable = false;
            group.blocksRaycasts = false;

            foreach (var graphic in GetComponentsInChildren<Graphic>(true))
                graphic.raycastTarget = false;
            foreach (var selectable in GetComponentsInChildren<Selectable>(true))
                selectable.interactable = false;
            foreach (var childGroup in GetComponentsInChildren<CanvasGroup>(true))
            {
                childGroup.interactable = false;
                childGroup.blocksRaycasts = false;
            }
        }

        public static GameObject Spawn(GameObject prefab, Transform parent, string instanceName)
        {
            if (prefab == null || parent == null) return null;

            var instance = Instantiate(prefab, parent, false);
            instance.name = instanceName;
            var rect = instance.transform as RectTransform;
            if (rect != null)
            {
                rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0.5f);
                rect.pivot = new Vector2(0.5f, 0.5f);
                rect.anchoredPosition = Vector2.zero;
                rect.sizeDelta = ReferenceSize;
                rect.localScale = Vector3.one;
            }

            var foundation = instance.GetComponent<WizardUiFoundation>();
            if (foundation == null) foundation = instance.AddComponent<WizardUiFoundation>();
            foundation.MakeDecorative();
            instance.transform.SetAsFirstSibling();
            return instance;
        }
    }
}
