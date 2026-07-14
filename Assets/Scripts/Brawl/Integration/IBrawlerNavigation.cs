using UnityEngine;

namespace BrawlArena
{
    /// <summary>
    /// Owns path planning for one AI brawler. Tactical decisions stay in
    /// AIBrawler, while the selected navigation backend translates those
    /// decisions into path state and a desired world velocity.
    /// </summary>
    public interface IBrawlerNavigation
    {
        bool IsReady { get; }
        bool HasPath { get; }
        Vector3 DesiredVelocity { get; }

        void Initialize(float moveSpeed, float stoppingDistance);
        bool TrySamplePosition(
            Vector3 candidate, float maxDistance, out Vector3 sampledPosition);
        void SetDestination(Vector3 destination);
        void ClearPath();
        void SetExternalFacing(bool externalFacing);
    }
}
