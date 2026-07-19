using UnityEngine;

namespace BrawlArena
{
    /// <summary>
    /// Scene-embedded profile selector: lives at the root of ActionArena.unity
    /// (or any non-classic scene) so every runtime consumer of ArenaLayout
    /// sees the right coordinate system without per-call-site changes.
    /// Resets to Classic when the scene unloads.
    /// </summary>
    public class ArenaProfileMarker : MonoBehaviour
    {
        public ArenaProfile profile = ArenaProfile.Action;

        void Awake()
        {
            ArenaLayout.Profile = profile;
        }

        void OnDestroy()
        {
            ArenaLayout.Profile = ArenaProfile.Classic;
        }
    }
}
