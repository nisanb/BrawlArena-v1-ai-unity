using System.Collections.Generic;
using UnityEngine;

namespace Crownfall
{
    /// Makes friend-or-foe readable at a glance.
    ///
    /// The arena is a bright, saturated, prop-dense space and the pack rigs are all
    /// dark armoured figures — at combat camera distance they read as interchangeable
    /// silhouettes, and the only team cue was a thin ground ring that props and
    /// foliage constantly hid. This adds a low-intensity team-coloured self-glow to
    /// each fighter's material instances, so allies carry a blue cast and enemies a
    /// red one no matter what they are standing behind.
    ///
    /// Deliberately uses per-renderer material INSTANCES rather than a
    /// MaterialPropertyBlock: URP's SRP batcher ignores property blocks entirely
    /// (learned the hard way on the hit-flash path, see HitFlash).
    ///
    /// It only ever writes emission, so it composes cleanly with HitFlash, which
    /// owns _BaseColor.
    [DefaultExecutionOrder(10)]
    public class TeamTint : MonoBehaviour
    {
        static readonly int EmissionId = Shader.PropertyToID("_EmissionColor");

        /// Strong enough to separate teams, weak enough that the pack's own
        /// materials still read as armour rather than neon.
        const float RestIntensity = 0.34f;
        /// The crown carrier glows harder — you should be able to pick them out of a
        /// scrum from across the arena, because chasing them is the whole mode.
        const float CarrierIntensity = 1.15f;
        const float CarrierPulseSpeed = 4.2f;

        readonly List<Material> mats = new List<Material>();
        CombatMotor motor;
        CombatantIdentity identity;
        Color teamColor = Color.white;
        float shownIntensity = RestIntensity;
        Team? tintedFor;

        void Start()
        {
            motor = GetComponent<CombatMotor>();
            identity = GetComponent<CombatantIdentity>();

            foreach (var r in GetComponentsInChildren<Renderer>(true))
            {
                if (r is TrailRenderer || r is ParticleSystemRenderer) continue;
                if (r.name == "TeamRing") continue;
                foreach (var m in r.materials)   // instances, intentional
                {
                    if (m == null || !m.HasProperty(EmissionId)) continue;
                    m.EnableKeyword("_EMISSION");
                    // shader still renders the glow, but it is excluded from GI —
                    // six fighters' worth of realtime emissive would be pure cost
                    // for an effect that is purely a readability cue
                    m.globalIlluminationFlags = MaterialGlobalIlluminationFlags.EmissiveIsBlack;
                    mats.Add(m);
                }
            }
            Rebind();
        }

        /// Networked fighters learn their team after Start — call this when identity
        /// changes so the tint follows.
        public void Rebind()
        {
            if (identity == null) identity = GetComponent<CombatantIdentity>();
            if (identity == null) return;
            teamColor = identity.TeamColor;
            tintedFor = identity.team;
        }

        void LateUpdate()
        {
            if (mats.Count == 0 || identity == null) return;
            if (tintedFor != identity.team) Rebind();

            float want = RestIntensity;
            var crown = CrownObjective.I;
            if (crown != null && motor != null && crown.IsCarriedBy(motor))
                want = CarrierIntensity * (0.82f + 0.18f * Mathf.Sin(Time.time * CarrierPulseSpeed));
            else if (motor != null && motor.IsDead)
                want = 0f;

            shownIntensity = Mathf.MoveTowards(shownIntensity, want, 3.5f * Time.deltaTime);

            Color e = teamColor * shownIntensity;
            for (int i = 0; i < mats.Count; i++)
                if (mats[i] != null) mats[i].SetColor(EmissionId, e);
        }
    }
}
