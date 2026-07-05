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
            return team == TeamId.Blue
                ? new Color(0.25f, 0.58f, 1f)
                : new Color(1f, 0.32f, 0.25f);
        }

        public static TeamId Other(TeamId team)
        {
            return team == TeamId.Blue ? TeamId.Red : TeamId.Blue;
        }
    }
}
