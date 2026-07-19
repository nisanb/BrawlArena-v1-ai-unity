using System;
using UnityEngine;

namespace BrawlArena
{
    /// <summary>
    /// Per-brawler concealment state: tracks which grass patch (if any) the
    /// brawler stands in, reveal timers from attacking/taking damage, and
    /// answers visibility queries for AI targeting, the minimap, and the
    /// hidden-renderer presentation. Added automatically by BrawlerController.
    /// </summary>
    public class ConcealmentTracker : MonoBehaviour
    {
        BrawlerController self;
        float revealedUntil;

        public GrassPatchVolume CurrentPatch { get; private set; }
        public bool InGrass => CurrentPatch != null;
        /// <summary>True while this brawler is actually concealed (in grass and not revealed).</summary>
        public bool SelfConcealed => InGrass && Time.time >= revealedUntil;

        /// <summary>Raised when this brawler's own concealed state flips (for the local-player HUD chip).</summary>
        public event Action<bool> ConcealedChanged;
        bool lastConcealed;

        public static ConcealmentTracker Ensure(BrawlerController owner)
        {
            var tracker = owner.GetComponent<ConcealmentTracker>();
            if (tracker == null) tracker = owner.gameObject.AddComponent<ConcealmentTracker>();
            if (owner.GetComponent<ConcealmentPresentation>() == null)
                owner.gameObject.AddComponent<ConcealmentPresentation>();
            return tracker;
        }

        void Awake()
        {
            self = GetComponent<BrawlerController>();
        }

        void OnEnable()
        {
            var health = GetComponent<Health>();
            if (health != null) health.Damaged += OnDamaged;
        }

        void OnDisable()
        {
            var health = GetComponent<Health>();
            if (health != null) health.Damaged -= OnDamaged;
        }

        void OnDamaged(float amount, GameObject attacker)
        {
            RevealFor(ConcealmentRules.DamageRevealSeconds);
        }

        public void RevealFor(float seconds)
        {
            revealedUntil = Mathf.Max(revealedUntil, Time.time + seconds);
        }

        void Update()
        {
            CurrentPatch = GrassPatchVolume.PatchAt(transform.position);
            bool concealed = InGrass && Time.time >= revealedUntil;
            if (concealed != lastConcealed)
            {
                lastConcealed = concealed;
                ConcealedChanged?.Invoke(concealed);
            }
        }

        /// <summary>
        /// True when the viewer cannot see this brawler. Teammates and the
        /// brawler itself always see it; dead subjects are never "hidden"
        /// (death presentation should play out in the open).
        /// </summary>
        public bool IsHiddenFrom(BrawlerController viewer)
        {
            if (self == null || viewer == null || viewer == self) return false;
            if (self.IsDead) return false;
            if (viewer.team == self.team) return false;

            Vector3 delta = viewer.transform.position - transform.position;
            delta.y = 0f;
            return ConcealmentRules.IsHidden(
                InGrass,
                CurrentPatch != null && CurrentPatch.Contains(viewer.transform.position),
                delta.magnitude,
                revealedUntil,
                Time.time);
        }
    }
}
