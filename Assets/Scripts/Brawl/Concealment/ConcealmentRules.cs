using UnityEngine;

namespace BrawlArena
{
    /// <summary>
    /// Pure rules for tall-grass concealment (Brawl-Stars bush semantics in
    /// 3D): a brawler standing in a grass patch is hidden from enemies unless
    /// the viewer shares the same patch, is very close, or the subject
    /// recently revealed itself by attacking or taking damage. Teammate
    /// visibility is the caller's responsibility.
    /// </summary>
    public static class ConcealmentRules
    {
        /// <summary>Enemies inside this planar distance always see the subject.</summary>
        public const float ProximityRevealRadius = 2.5f;
        public const float AttackRevealSeconds = 1.6f;
        public const float DamageRevealSeconds = 1.2f;

        public static bool IsHidden(
            bool subjectInPatch,
            bool viewerInSamePatch,
            float planarDistance,
            float revealedUntil,
            float now)
        {
            if (!subjectInPatch) return false;
            if (viewerInSamePatch) return false;
            if (planarDistance <= ProximityRevealRadius) return false;
            if (now < revealedUntil) return false;
            return true;
        }
    }
}
