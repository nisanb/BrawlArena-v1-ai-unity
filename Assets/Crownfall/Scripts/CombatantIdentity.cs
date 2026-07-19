using UnityEngine;

namespace Crownfall
{
    public class CombatantIdentity : MonoBehaviour
    {
        public string displayName = "Champion";
        public Team team;
        public ClassId classId;
        public ElementId element;
        public bool isPlayer;

        public Color TeamColor => team == Team.Azure
            ? new Color(0.3f, 0.6f, 1f)
            : new Color(1f, 0.32f, 0.26f);

        public Color ElementColor => ElementColors.Get(element);
    }
}
