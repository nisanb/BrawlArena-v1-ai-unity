using UnityEngine;
using UnityEngine.EventSystems;

namespace BrawlArena
{
    /// <summary>
    /// Invisible drag surface covering the right side of the screen: dragging
    /// orbits the BrawlCamera (yaw + clamped pitch). The attack/sprint buttons
    /// are later siblings on the canvas, so they keep receiving their own
    /// pointer events; this zone only sees touches that start on empty space.
    /// </summary>
    public class CameraDragZone : MonoBehaviour, IPointerDownHandler, IDragHandler, IPointerUpHandler
    {
        BrawlCamera cam;
        int pointerId = int.MinValue;

        BrawlCamera Cam
        {
            get
            {
                if (cam == null)
                {
                    var main = Camera.main;
                    if (main != null) cam = main.GetComponent<BrawlCamera>();
                }
                return cam;
            }
        }

        void OnDisable()
        {
            pointerId = int.MinValue;
        }

        public void OnPointerDown(PointerEventData e)
        {
            if (pointerId != int.MinValue) return;
            pointerId = e.pointerId;
        }

        public void OnDrag(PointerEventData e)
        {
            if (e.pointerId != pointerId) return;
            if (Cam != null) Cam.AddOrbit(e.delta.x, e.delta.y);
        }

        public void OnPointerUp(PointerEventData e)
        {
            if (e.pointerId == pointerId) pointerId = int.MinValue;
        }
    }
}
