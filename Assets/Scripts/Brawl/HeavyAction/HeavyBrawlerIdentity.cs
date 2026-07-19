using UnityEngine;

namespace BrawlArena
{
    /// <summary>
    /// Marks a generated souls-style hero prefab root with its roster
    /// identity. The character assembly resolves the production path from
    /// this component's presence; validation matches heroId and variant.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class HeavyBrawlerIdentity : MonoBehaviour
    {
        public string heroId;
        public bool isHumanVariant;
    }
}
