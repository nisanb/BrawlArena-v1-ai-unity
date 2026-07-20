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

            // Read the bar's allegiance from the PLAYER's point of view, not raw
            // team colour: enemies = red, allies = green. Team-blue collided with
            // the blue "Azure" scoreboard and made enemy bars look friendly.
            var pm = MatchManager.I != null ? MatchManager.I.PlayerMotor : null;
            if (pm != null && pm.Identity != null && health.Motor != null && health.Motor.Identity != null)
            {
                bool ally = health.Motor.Identity.team == pm.Identity.team;
                fill.color = ally ? new Color(0.35f, 0.85f, 0.42f) : new Color(0.92f, 0.26f, 0.24f);
            }

            if (group != null)
            {
                // bars belong to combat only — the menu podium champion must stay clean
                bool inCombat = MatchManager.I != null &&
                                (MatchManager.I.State == MatchState.Countdown ||
                                 MatchManager.I.State == MatchState.Fighting);
                bool concealed = health.Motor != null && MatchManager.I != null &&
                                 health.Motor.IsConcealedFrom(MatchManager.I.PlayerMotor);
                // don't double up: whoever is in the top HUD nameplate already shows
                // a big HP bar there, so hide their redundant floating world bar
                var pmm = MatchManager.I != null ? MatchManager.I.PlayerMotor : null;
                CombatMotor framed = null;
                if (pmm != null)
                {
                    if (pmm.LockTarget != null && !pmm.LockTarget.IsDead) framed = pmm.LockTarget;
                    else if (pmm.LastEngagedEnemy != null && !pmm.LastEngagedEnemy.IsDead) framed = pmm.LastEngagedEnemy;
                }
                bool nameplated = framed != null && framed == health.Motor;
                float want = !inCombat || health.IsDead || isActivePlayer || concealed || nameplated ? 0f : 1f;
                group.alpha = Mathf.MoveTowards(group.alpha, want, 4f * Time.unscaledDeltaTime);
                if (!inCombat) group.alpha = 0f;
            }
        }
    }
}
