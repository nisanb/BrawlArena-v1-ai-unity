using UnityEngine;

namespace Crownfall
{
    public struct GameMode
    {
        public string id;
        public string title;
        public string subtitle;
        public int killTarget;
        public float duration;
        /// Whether the Crown objective spawns. Crown points and kills feed the same
        /// score, so a crown match is a race the trailing team can always re-enter.
        public bool crown;
        public float crownSecondsPerPoint;
    }

    /// The rotating event slots behind the hub's mode carousel. Offline matches
    /// honor the selected mode's kill target and clock; online quick-match always
    /// runs the standard brawl (fields reset with the scene reload after every
    /// match, so a local mode pick never leaks into a networked room).
    public static class GameModes
    {
        public static readonly GameMode[] All =
        {
            // The headline mode: hold the crown to bank points, kills still count.
            // Targets are calibrated against a measured autopilot match — the leading
            // team banks ~16 points/minute with crown+kills combined, so 40 lands a
            // typical match at 2.5-3 minutes, which is the sweet spot for this genre.
            new GameMode { id = "crownrush", title = "CROWN RUSH",
                subtitle = "Hold the crown  ·  3v3  ·  4:00", killTarget = 40, duration = 240f,
                crown = true, crownSecondsPerPoint = 3.0f },
            new GameMode { id = "brawl", title = "10-KILL BRAWL",
                subtitle = "Pure deathmatch  ·  3v3  ·  5:00", killTarget = 10, duration = 300f },
            new GameMode { id = "blitz", title = "BLITZ",
                subtitle = "First to 20  ·  3v3  ·  2:00", killTarget = 20, duration = 120f,
                crown = true, crownSecondsPerPoint = 2.2f },
            new GameMode { id = "marathon", title = "CROWN MARATHON",
                subtitle = "First to 80  ·  3v3  ·  8:00", killTarget = 80, duration = 480f,
                crown = true, crownSecondsPerPoint = 2.8f },
        };

        public static GameMode Selected =>
            All[Mathf.Clamp(CrownfallMeta.SelectedMode, 0, All.Length - 1)];
    }
}
