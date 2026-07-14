using UnityEngine;

namespace BrawlArena
{
    public enum TeamId
    {
        Blue = 0,
        Red = 1
    }

    public static class TeamUtil
    {
        public static Color Color(TeamId team)
        {
            return Color(team, AccessibilitySettings.HighContrastEnabled);
        }

        public static Color Color(TeamId team, bool highContrast)
        {
            if (highContrast)
            {
                return team == TeamId.Blue
                    ? new Color(0.08f, 0.86f, 1f)
                    : new Color(1f, 0.72f, 0.06f);
            }

            return team == TeamId.Blue
                ? new Color(0.25f, 0.58f, 1f)
                : new Color(1f, 0.32f, 0.25f);
        }

        /// <summary>Word and shape cue that remains meaningful without color.</summary>
        public static string CueLabel(TeamId team, TeamId localTeam)
        {
            return CueLabel(team, localTeam, AccessibilitySettings.HighContrastEnabled);
        }

        public static string CueLabel(TeamId team, TeamId localTeam, bool highContrast)
        {
            bool ally = team == localTeam;
            if (!highContrast) return ally ? "ALLY" : "ENEMY";
            return ally ? "ALLY +" : "ENEMY !";
        }

        public static TeamId Other(TeamId team)
        {
            return team == TeamId.Blue ? TeamId.Red : TeamId.Blue;
        }

        public static string ClanName(TeamId team)
        {
            return team == TeamId.Blue ? "AZURE COVEN" : "CRIMSON COVEN";
        }
    }
}
