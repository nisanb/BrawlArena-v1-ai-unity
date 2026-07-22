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
    }

    /// The rotating event slots behind the hub's mode carousel. Offline matches
    /// honor the selected mode's kill target and clock; online quick-match always
    /// runs the standard brawl (fields reset with the scene reload after every
    /// match, so a local mode pick never leaks into a networked room).
    public static class GameModes
    {
        public static readonly GameMode[] All =
        {
            new GameMode { id = "brawl", title = "10-KILL BRAWL",
                subtitle = "Sundered Crown  ·  3v3  ·  5:00", killTarget = 10, duration = 300f },
            new GameMode { id = "blitz", title = "BLITZ",
                subtitle = "First to 3  ·  3v3  ·  2:00", killTarget = 3, duration = 120f },
            new GameMode { id = "marathon", title = "CROWN MARATHON",
                subtitle = "First to 20  ·  3v3  ·  8:00", killTarget = 20, duration = 480f },
        };

        public static GameMode Selected =>
            All[Mathf.Clamp(CrownfallMeta.SelectedMode, 0, All.Length - 1)];
    }
}
