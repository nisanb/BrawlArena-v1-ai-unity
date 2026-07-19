using UnityEngine;
using UnityEngine.UI;

namespace Crownfall
{
    /// Floating billboarded HP bar above a combatant (built by the forge).
    public class WorldHealthBar : MonoBehaviour
    {
        public Health health;
        public Image fill;
        public Image ghost;
        public CanvasGroup group;

        float shown = 1f, ghostShown = 1f;

        void LateUpdate()
        {
            if (health == null || fill == null) return;

            var cam = Camera.main;
            if (cam != null)
            {
                transform.rotation = Quaternion.LookRotation(transform.position - cam.transform.position);
            }

            // the active player's own bar is redundant with the HUD
            bool isActivePlayer = MatchManager.I != null && MatchManager.I.PlayerMotor == health.Motor;

            float f = health.Max > 0f ? health.Current / health.Max : 0f;
            shown = Mathf.Lerp(shown, f, 16f * Time.deltaTime);
            ghostShown = Mathf.Lerp(ghostShown, f, 5f * Time.deltaTime);
            fill.fillAmount = shown;
            if (ghost != null) ghost.fillAmount = Mathf.Max(ghostShown, shown);

            if (group != null)
            {
                bool matchOver = MatchManager.I != null && MatchManager.I.State == MatchState.Ended;
                bool concealed = health.Motor != null && MatchManager.I != null &&
                                 health.Motor.IsConcealedFrom(MatchManager.I.PlayerMotor);
                float want = health.IsDead || isActivePlayer || matchOver || concealed ? 0f : 1f;
                group.alpha = Mathf.MoveTowards(group.alpha, want, 4f * Time.unscaledDeltaTime);
            }
        }
    }
}
