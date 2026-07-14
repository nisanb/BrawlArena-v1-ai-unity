using System;
using UnityEngine;

namespace BrawlArena
{
    /// <summary>
    /// Declares which roster entry and production role a builder-owned dormant
    /// Invector prefab is allowed to assemble. Runtime assemblers validate this
    /// marker before instantiation so a visually or topologically wrong prefab
    /// cannot be selected merely because it was assigned to a definition.
    /// </summary>
    public enum InvectorBrawlerPrefabRole
    {
        None = 0,
        Human = 1,
        AI = 2,
    }

    [DisallowMultipleComponent]
    [AddComponentMenu("")]
    public sealed class InvectorBrawlerPrefabIdentity : MonoBehaviour
    {
        [SerializeField] string rosterId;
        [SerializeField] InvectorBrawlerPrefabRole role;

        public string RosterId => rosterId;
        public InvectorBrawlerPrefabRole Role => role;
        public bool IsConfigured =>
            !string.IsNullOrWhiteSpace(rosterId) && role != InvectorBrawlerPrefabRole.None;

        public void ConfigureDormant(string configuredRosterId, InvectorBrawlerPrefabRole configuredRole)
        {
            if (string.IsNullOrWhiteSpace(configuredRosterId))
                throw new ArgumentException("A production Invector prefab requires a roster id.", nameof(configuredRosterId));
            if (configuredRole == InvectorBrawlerPrefabRole.None)
                throw new ArgumentOutOfRangeException(nameof(configuredRole), configuredRole,
                    "A production Invector prefab requires a human or AI role.");

            rosterId = configuredRosterId;
            role = configuredRole;
        }

        public bool Matches(string requestedRosterId, InvectorBrawlerPrefabRole requestedRole)
        {
            return IsConfigured &&
                   string.Equals(rosterId, requestedRosterId, StringComparison.Ordinal) &&
                   role == requestedRole;
        }
    }
}
