using UnityEngine;

namespace Crownfall
{
    /// Brief white flash on the model whenever this fighter takes damage, so hits
    /// read instantly even without watching the HP bar. Uses per-fighter material
    /// instances: URP's SRP batcher ignores MaterialPropertyBlocks entirely.
    public class HitFlash : MonoBehaviour
    {
        static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");

        Material[] mats;
        Color[] baseColors;
        float flashUntil;
        bool applied;

        void Start()
        {
            var matList = new System.Collections.Generic.List<Material>();
            foreach (var r in GetComponentsInChildren<Renderer>(true))
            {
                if (r is TrailRenderer || r is ParticleSystemRenderer) continue;
                if (r.name == "TeamRing") continue;
                foreach (var m in r.materials)   // instances, intentional
                    if (m != null && m.HasProperty(BaseColorId)) matList.Add(m);
            }
            mats = matList.ToArray();
            baseColors = new Color[mats.Length];
            for (int i = 0; i < mats.Length; i++) baseColors[i] = mats[i].GetColor(BaseColorId);

            var health = GetComponent<Health>();
            if (health != null)
                health.Damaged += (hit, res) =>
                {
                    if (res.damageDealt > 0.1f) flashUntil = Time.time + 0.09f;
                };
        }

        void LateUpdate()
        {
            bool on = Time.time < flashUntil;
            if (on == applied) return;
            applied = on;
            for (int i = 0; i < mats.Length; i++)
            {
                if (mats[i] == null) continue;
                mats[i].SetColor(BaseColorId, on ? new Color(2.2f, 1.5f, 1.4f) : baseColors[i]);
            }
        }
    }
}
