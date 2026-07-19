using UnityEngine;

namespace Crownfall
{
    /// Registry of bush patches (built by the forge). Fighters standing inside a
    /// patch become concealed: hidden from the enemy team's view and untargetable
    /// beyond close range. Attacking or taking a hit reveals them briefly.
    public class BushField : MonoBehaviour
    {
        /// xz = patch center, w = radius
        public Vector4[] patches = new Vector4[0];

        public static BushField I { get; private set; }

        void Awake() { I = this; }

        public static bool IsInBush(Vector3 pos)
        {
            if (I == null || I.patches == null) return false;
            foreach (var p in I.patches)
            {
                float dx = pos.x - p.x;
                float dz = pos.z - p.z;
                if (dx * dx + dz * dz <= p.w * p.w) return true;
            }
            return false;
        }
    }
}
