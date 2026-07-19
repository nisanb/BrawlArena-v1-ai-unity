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
        /// <summary>
        /// Stable roster rotation for production matches. No hidden random seed
        /// means the same selection always creates the same team order.
        /// </summary>
        public static int[] BuildRotatedTeamDefinitionIndices(int rosterCount,
            int teamSize, int pinnedFirstIndex, int rotationStart)
        {
            if (rosterCount <= 0)
                throw new ArgumentOutOfRangeException(nameof(rosterCount));
            if (teamSize <= 0)
                throw new ArgumentOutOfRangeException(nameof(teamSize));
            if (pinnedFirstIndex < -1 || pinnedFirstIndex >= rosterCount)
                throw new ArgumentOutOfRangeException(nameof(pinnedFirstIndex));

            int[] result = new int[teamSize];
            int count = 0;
            if (pinnedFirstIndex >= 0) result[count++] = pinnedFirstIndex;
            int cursor = ((rotationStart % rosterCount) + rosterCount) % rosterCount;
            while (count < teamSize)
            {
                int candidate = cursor++ % rosterCount;
                if (candidate == pinnedFirstIndex && count < rosterCount) continue;
                result[count++] = candidate;
            }
            return result;
        }

        /// <summary>
        /// Deterministic role-balanced lineup for the strategy-triangle roster.
        /// Slot zero is always the pinned entry; the rest cycle forward through
        /// the roster (pinned+1, pinned+2, ...) so a 3-hero roster fields every
        /// role once per 3 slots, wrapping around for larger team sizes. No
        /// hidden RNG: identical inputs always produce identical lineups.
        /// </summary>
        public static int[] BuildRoleBalancedLineup(BrawlerDefinition[] roster, int pinnedIndex,
            int teamSize)
        {
            if (roster == null || roster.Length == 0)
                throw new ArgumentNullException(nameof(roster));
            if (teamSize <= 0)
                throw new ArgumentOutOfRangeException(nameof(teamSize));
            if (pinnedIndex < 0 || pinnedIndex >= roster.Length)
                throw new ArgumentOutOfRangeException(nameof(pinnedIndex));

            int[] result = new int[teamSize];
            result[0] = pinnedIndex;
            for (int slot = 1; slot < teamSize; slot++)
                result[slot] = (pinnedIndex + slot) % roster.Length;
            return result;
        }

        /// <summary>
        /// Deterministic opponent lineup that breaks the perfect ally mirror a
        /// small roster would otherwise produce every match. Starts from a
        /// copy of <paramref name="allyLineup"/> (so team size and validity
        /// match the ally comp). When the roster fields exactly one hero per
        /// role, the opponent keeps the ally's full role spread — composition
        /// is fixed and match-to-match variety lives in the seeded slot ORDER
        /// only, never in a hero double that would drop a role. Rosters with
        /// duplicated roles keep the legacy single-slot swap that reads the
        /// comp differently (e.g. a 3-1-1 mirror becomes a 2-1 comp) without
        /// pushing any hero above two copies when an alternative allows it.
        /// Seeded PRNG only — this runs once per match setup, not inside any
        /// per-frame Workflow script.
        /// </summary>
        public static int[] BuildOpponentLineup(BrawlerDefinition[] roster, int[] allyLineup,
            int teamSize, int seed)
        {
            if (roster == null || roster.Length == 0)
                throw new ArgumentNullException(nameof(roster));
            if (allyLineup == null || allyLineup.Length == 0)
                throw new ArgumentNullException(nameof(allyLineup));
            if (teamSize <= 0)
                throw new ArgumentOutOfRangeException(nameof(teamSize));

            int[] result = new int[teamSize];
            for (int i = 0; i < teamSize; i++)
                result[i] = allyLineup[i % allyLineup.Length];

            if (roster.Length < 2) return result;

            var random = new System.Random(seed);
            if (HasOneHeroPerRole(roster) &&
                TryReorderKeepingComposition(result, allyLineup, random))
                return result;

            var slotOrder = new List<int>(teamSize);
            for (int i = 0; i < teamSize; i++) slotOrder.Add(i);
            Shuffle(slotOrder, random);

            // First pass: find a slot whose hero can be swapped for a
            // different roster entry without exceeding the two-copy cap.
            if (TrySwapDifferentRole(result, roster.Length, slotOrder, random, respectCap: true))
                return result;

            // Fallback (only reachable with a roster so small the cap can't
            // be honored for any slot): still guarantee at least one slot
            // differs from the mirror, ignoring the cap.
            TrySwapDifferentRole(result, roster.Length, slotOrder, random, respectCap: false);
            return result;
        }

        /// <summary>
        /// One hero per role means the ally comp already carries the full role
        /// spread, so the opponent must preserve that composition. Role falls
        /// back to id (then index) so id-only rosters, as in EditMode tests,
        /// still count each unique hero as its own role.
        /// </summary>
        static bool HasOneHeroPerRole(BrawlerDefinition[] roster)
        {
            var seen = new HashSet<string>();
            for (int i = 0; i < roster.Length; i++)
            {
                BrawlerDefinition definition = roster[i];
                string key = definition != null && !string.IsNullOrEmpty(definition.role)
                    ? definition.role
                    : definition != null && !string.IsNullOrEmpty(definition.id)
                        ? definition.id
                        : i.ToString();
                if (!seen.Add(key)) return false;
            }
            return true;
        }

        /// <summary>
        /// Shuffles the slots of <paramref name="result"/> without changing
        /// which heroes appear, so the comp keeps every role the ally comp
        /// carries. Returns false only for degenerate comps (every slot the
        /// same hero) where reordering can never break the ally mirror.
        /// </summary>
        static bool TryReorderKeepingComposition(int[] result, int[] allyLineup,
            System.Random random)
        {
            var order = new List<int>(result.Length);
            for (int i = 0; i < result.Length; i++) order.Add(result[i]);
            Shuffle(order, random);
            for (int i = 0; i < result.Length; i++) result[i] = order[i];
            if (MirrorsAlly(result, allyLineup)) RotateLeft(result);
            return !MirrorsAlly(result, allyLineup);
        }

        static bool MirrorsAlly(int[] result, int[] allyLineup)
        {
            for (int i = 0; i < result.Length; i++)
                if (result[i] != allyLineup[i % allyLineup.Length]) return false;
            return true;
        }

        static void RotateLeft(int[] values)
        {
            if (values.Length < 2) return;
            int first = values[0];
            for (int i = 0; i < values.Length - 1; i++)
                values[i] = values[i + 1];
            values[values.Length - 1] = first;
        }

        static bool TrySwapDifferentRole(int[] result, int rosterCount, List<int> slotOrder,
            System.Random random, bool respectCap)
        {
            foreach (int slot in slotOrder)
            {
                int current = result[slot];
                var candidates = new List<int>(rosterCount - 1);
                for (int r = 0; r < rosterCount; r++)
                {
                    if (r == current) continue;
                    if (respectCap && CountOf(result, r) >= 2) continue;
                    candidates.Add(r);
                }
                if (candidates.Count == 0) continue;
                result[slot] = candidates[random.Next(candidates.Count)];
                return true;
            }
            return false;
        }

        static int CountOf(int[] values, int value)
        {
            int count = 0;
            for (int i = 0; i < values.Length; i++)
                if (values[i] == value) count++;
            return count;
        }

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
