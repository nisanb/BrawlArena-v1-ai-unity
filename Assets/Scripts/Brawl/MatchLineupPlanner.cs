using System;
using System.Collections.Generic;

namespace BrawlArena
{
    /// <summary>
    /// Pure lineup composition used by GameFlow and EditMode tests. A team uses
    /// unique roster entries until every available definition has appeared,
    /// then cycles through another shuffled pass when the roster is smaller
    /// than the requested team size.
    /// </summary>
    public static class MatchLineupPlanner
    {
        public static int[] BuildTeamDefinitionIndices(int rosterCount, int teamSize,
            int pinnedFirstIndex, int seed)
        {
            if (rosterCount <= 0)
                throw new ArgumentOutOfRangeException(nameof(rosterCount));
            if (teamSize <= 0)
                throw new ArgumentOutOfRangeException(nameof(teamSize));
            if (pinnedFirstIndex < -1 || pinnedFirstIndex >= rosterCount)
                throw new ArgumentOutOfRangeException(nameof(pinnedFirstIndex));

            var result = new List<int>(teamSize);
            if (pinnedFirstIndex >= 0) result.Add(pinnedFirstIndex);

            var random = new System.Random(seed);
            var firstPass = BuildPass(rosterCount, pinnedFirstIndex);
            Shuffle(firstPass, random);
            AppendUntilFull(firstPass, result, teamSize);

            while (result.Count < teamSize)
            {
                var nextPass = BuildPass(rosterCount, -1);
                Shuffle(nextPass, random);
                AppendUntilFull(nextPass, result, teamSize);
            }

            return result.ToArray();
        }

        static List<int> BuildPass(int rosterCount, int excludedIndex)
        {
            var pass = new List<int>(rosterCount);
            for (int i = 0; i < rosterCount; i++)
                if (i != excludedIndex) pass.Add(i);
            return pass;
        }

        static void AppendUntilFull(List<int> pass, List<int> result, int teamSize)
        {
            for (int i = 0; i < pass.Count && result.Count < teamSize; i++)
                result.Add(pass[i]);
        }

        static void Shuffle(List<int> values, System.Random random)
        {
            for (int i = values.Count - 1; i > 0; i--)
            {
                int j = random.Next(i + 1);
                int value = values[i];
                values[i] = values[j];
                values[j] = value;
            }
        }
    }
}
