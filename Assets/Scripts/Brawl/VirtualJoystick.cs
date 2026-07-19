using UnityEngine;
using UnityEngine.EventSystems;

namespace BrawlArena
{
    /// <summary>
    /// Floating virtual joystick: the base appears wherever the finger lands
    /// inside the zone this component covers, and hides on release.
    /// </summary>
    public class VirtualJoystick : MonoBehaviour, IPointerDownHandler, IDragHandler, IPointerUpHandler
    {
        public RectTransform baseRect;
        public RectTransform knobRect;
        [Tooltip("Knob travel radius in reference-resolution pixels.")]
        public float radius = 110f;
        [Tooltip("Inner dead zone as a fraction of the travel radius; resting-thumb micro-drift below this reads as zero input.")]
        [Range(0f, 0.5f)] public float innerDeadZone = 0.08f;

        public Vector2 Value { get; private set; }

        RectTransform zone;
        int pointerId = int.MinValue;

        void Awake()
        {
            zone = (RectTransform)transform;
            if (baseRect != null) baseRect.gameObject.SetActive(false);
        }

        void OnDisable()
        {
            pointerId = int.MinValue;
            Value = Vector2.zero;
            if (baseRect != null) baseRect.gameObject.SetActive(false);
        }

        public void OnPointerDown(PointerEventData e)
        {
            if (pointerId != int.MinValue) return;
            if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(zone, e.position, e.pressEventCamera, out Vector2 local))
                return;
            pointerId = e.pointerId;
            baseRect.gameObject.SetActive(true);
            baseRect.anchoredPosition = local;
            knobRect.anchoredPosition = Vector2.zero;
            Value = Vector2.zero;
        }

        public void OnDrag(PointerEventData e)
        {
            if (e.pointerId != pointerId) return;
            if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(zone, e.position, e.pressEventCamera, out Vector2 local))
                return;
            Vector2 clamped = Vector2.ClampMagnitude(local - baseRect.anchoredPosition, radius);
            knobRect.anchoredPosition = clamped;
            Vector2 normalized = clamped / radius;
            Value = normalized.magnitude < innerDeadZone ? Vector2.zero : normalized;
        }

        public void OnPointerUp(PointerEventData e)
        {
            if (e.pointerId != pointerId) return;
            pointerId = int.MinValue;
            Value = Vector2.zero;
            baseRect.gameObject.SetActive(false);
        }
    }
}
