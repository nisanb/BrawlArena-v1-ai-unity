using UnityEngine;

namespace Crownfall
{
    /// The menu scene's 3D champion display: four visual-only rigs standing on
    /// the painted podium (Background_02), exactly one active. Swapping picks
    /// plays a small scale pop so the change lands.
    public class MenuShowcase : MonoBehaviour
    {
        [Header("Wired by forge")]
        public GameObject[] championRigs = new GameObject[6];

        int shown = -1;
        float popT = 1f;
        Vector3 baseScale = Vector3.one;

        void Start()
        {
            Show(CrownfallMeta.SelectedClass, instant: true);
        }

        public void Show(int classIndex, bool instant = false)
        {
            classIndex = Mathf.Clamp(classIndex, 0, championRigs.Length - 1);
            if (shown == classIndex) return;
            shown = classIndex;
            for (int i = 0; i < championRigs.Length; i++)
                if (championRigs[i] != null) championRigs[i].SetActive(i == classIndex);
            var rig = championRigs[classIndex];
            if (rig != null)
            {
                baseScale = Vector3.one;
                popT = instant ? 1f : 0f;
                rig.transform.localScale = instant ? baseScale : baseScale * 0.65f;
            }
        }

        void Update()
        {
            if (popT >= 1f || shown < 0) return;
            var rig = championRigs[shown];
            if (rig == null) { popT = 1f; return; }
            popT = Mathf.Min(1f, popT + Time.unscaledDeltaTime / 0.35f);
            // BackOut-style overshoot
            float t = popT - 1f;
            float k = 1f + (t * t * ((1.70158f + 1f) * t + 1.70158f));
            rig.transform.localScale = baseScale * Mathf.LerpUnclamped(0.65f, 1f, k);
        }
    }
}
