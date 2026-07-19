using UnityEngine;

namespace Crownfall
{
    /// Brief white flash on the model whenever this fighter takes damage, so hits
    /// read instantly even without watching the HP bar.
    public class HitFlash : MonoBehaviour
    {
        static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");

        Renderer[] rends;
        MaterialPropertyBlock mpb;
        float flashUntil;
        bool applied;

        void Start()
        {
            var list = new System.Collections.Generic.List<Renderer>();
            foreach (var r in GetComponentsInChildren<Renderer>(true))
            {
                if (r is TrailRenderer || r is ParticleSystemRenderer) continue;
                if (r.name == "TeamRing") continue;
                list.Add(r);
            }
            rends = list.ToArray();
            mpb = new MaterialPropertyBlock();

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
            foreach (var r in rends)
            {
                if (r == null) continue;
                if (on)
                {
                    mpb.SetColor(BaseColorId, new Color(2.4f, 1.7f, 1.6f));
                    r.SetPropertyBlock(mpb);
                }
                else
                {
                    r.SetPropertyBlock(null);
                }
            }
        }
    }
}
