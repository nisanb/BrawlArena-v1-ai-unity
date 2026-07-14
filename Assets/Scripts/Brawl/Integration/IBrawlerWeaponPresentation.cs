using UnityEngine;

namespace BrawlArena
{
    /// <summary>
    /// Visual-only weapon boundary for one brawler. Implementations may drive
    /// weapon art, sockets, muzzle effects, or aiming IK, but never gameplay
    /// resource, resolution, attack-timing, or action-execution authority.
    /// </summary>
    public interface IBrawlerWeaponPresentation
    {
        /// <summary>
        /// Presents a world-space aim direction. Vector3.zero releases aiming
        /// and any support-hand/aiming IK owned by the implementation.
        /// </summary>
        void PresentAim(Vector3 worldDirection);
        bool TryGetMuzzlePosition(out Vector3 worldPosition);
        void PresentMuzzle(Vector3 worldPosition, Vector3 worldDirection);
        void SetVisible(bool visible);
        void ResetForRespawn();
    }
}
