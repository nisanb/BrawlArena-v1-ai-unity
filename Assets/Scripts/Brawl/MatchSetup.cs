namespace BrawlArena
{
    public enum GameMode
    {
        Knockout = 0,
        GemGrab = 1
    }

    /// <summary>
    /// Cross-scene match configuration chosen in the main menu. Plain statics:
    /// they survive scene loads within a session, and reset to defaults when
    /// the Arena scene is played directly (editor / autopilot), in which case
    /// GameFlow falls back to its in-scene character select.
    /// </summary>
    public static class MatchSetup
    {
        public static GameMode Mode = GameMode.Knockout;

        /// <summary>Roster index picked in the menu, or -1 for "not chosen".</summary>
        public static int CharacterIndex = -1;

        /// <summary>True when the Arena was launched from the main menu scene.</summary>
        public static bool FromMenu;
    }
}
