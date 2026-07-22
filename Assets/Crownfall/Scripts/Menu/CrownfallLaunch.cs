using UnityEngine.SceneManagement;

namespace Crownfall
{
    public enum LaunchKind { None, Offline, Demo, Online }

    /// Scene handoff between the standalone menu (CrownfallMenu.unity) and the
    /// arena (CrownfallArena.unity). The menu writes a pending launch request
    /// and loads the arena; the arena consumes it on load and starts the match.
    /// LaunchKind.None (arena opened directly in the editor / by automation
    /// probes) leaves the match in the legacy Menu state for AutoStart drivers.
    public static class CrownfallLaunch
    {
        public const string MenuScene = "CrownfallMenu";
        public const string ArenaScene = "CrownfallArena";

        public static LaunchKind Pending { get; set; } = LaunchKind.None;

        /// The launch that started the current arena session — lets REMATCH
        /// reload the arena into the same kind of match.
        public static LaunchKind LastKind { get; private set; } = LaunchKind.None;

        public static LaunchKind Consume()
        {
            var k = Pending;
            Pending = LaunchKind.None;
            if (k != LaunchKind.None) LastKind = k;
            return k;
        }

        public static void ToArena(LaunchKind kind)
        {
            Pending = kind;
            SceneManager.LoadScene(ArenaScene);
        }

        public static void ToMenu()
        {
            Pending = LaunchKind.None;
            SceneManager.LoadScene(MenuScene);
        }
    }
}
