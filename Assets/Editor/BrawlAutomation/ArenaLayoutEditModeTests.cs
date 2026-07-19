using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEngine;

namespace BrawlArena.EditorAutomation
{
    public class ArenaLayoutEditModeTests
    {
        [Test]
        public void CompetitiveLayoutDefinesFiveMirroredSpawnsPerTeam()
        {
            Assert.AreEqual(5, ArenaLayout.TeamSize);
            Assert.AreEqual(40f, ArenaLayout.PlayableHalfExtent);
            Assert.Greater(ArenaLayout.GroundHalfExtent, ArenaLayout.PlayableHalfExtent);
            Assert.Greater(ArenaLayout.MinimapHalfExtent, ArenaLayout.GroundHalfExtent);

            var bluePositions = new HashSet<Vector3>();
            var redPositions = new HashSet<Vector3>();
            for (int i = 0; i < ArenaLayout.TeamSize; i++)
            {
                Vector3 blue = ArenaLayout.SpawnPosition(TeamId.Blue, i);
                Vector3 red = ArenaLayout.SpawnPosition(TeamId.Red, i);

                Assert.AreEqual(blue.x, red.x);
                Assert.AreEqual(-blue.z, red.z);
                Assert.Less(Mathf.Abs(blue.x), ArenaLayout.PlayableHalfExtent);
                Assert.Less(Mathf.Abs(blue.z), ArenaLayout.PlayableHalfExtent);
                Assert.IsTrue(bluePositions.Add(blue), "Blue spawn slots must be unique.");
                Assert.IsTrue(redPositions.Add(red), "Red spawn slots must be unique.");
            }
        }

        [Test]
        public void FourHeroRosterUsesEveryHeroBeforeFillingTheFifthSlot()
        {
            const int selectedHero = 2;
            int[] blue = MatchLineupPlanner.BuildTeamDefinitionIndices(
                4, ArenaLayout.TeamSize, selectedHero, 1234);
            int[] red = MatchLineupPlanner.BuildTeamDefinitionIndices(
                4, ArenaLayout.TeamSize, -1, 5678);

            Assert.AreEqual(ArenaLayout.TeamSize, blue.Length);
            Assert.AreEqual(ArenaLayout.TeamSize, red.Length);
            Assert.AreEqual(selectedHero, blue[0]);
            Assert.AreEqual(4, new HashSet<int>(blue.Take(4)).Count);
            Assert.AreEqual(4, new HashSet<int>(red.Take(4)).Count);
            Assert.AreEqual(4, new HashSet<int>(blue).Count);
            Assert.AreEqual(4, new HashSet<int>(red).Count);
            AssertDefinitionIndicesAreValid(blue, 4);
            AssertDefinitionIndicesAreValid(red, 4);
        }

        [Test]
        public void PlayableRosterContainsOneMageOneArcherAndOneWarrior()
        {
            // Roster-content assertions only need the generated assets, not a
            // full rebuild: BuildRoster() replaces scenes (lab scene included),
            // which is illegal from the test runner's untitled scene. The
            // builder pipeline itself is covered by the per-roster production
            // tests.
            BrawlerDefinition[] roster =
                ArenaSceneBuilder.BuildRosterFromExistingAssets();

            CollectionAssert.AreEqual(new[] { "frost", "thorn", "bastion" },
                roster.Select(definition => definition.id).ToArray());
            CollectionAssert.AreEqual(new[]
                {
                    SpellSchool.Frost,
                    SpellSchool.None,
                    SpellSchool.None,
                },
                roster.Select(definition => definition.specialty.school).ToArray());
            Assert.AreEqual("Archer", roster[1].role);
            Assert.IsNotNull(roster[1].humanBodyPrefab);
            Assert.IsNotNull(roster[1].aiBodyPrefab);
            Assert.AreEqual("ThornHeavyHuman", roster[1].humanBodyPrefab.name);
            Assert.AreEqual("ThornHeavyAI", roster[1].aiBodyPrefab.name);
            Assert.AreEqual("Arrow01Projectile", roster[1].projectilePrefab.name);
            Assert.AreEqual("EXPLOSIVE ARROW", roster[1].superName);
            Assert.AreEqual(3, CharacterSkillBook.For(roster[1]).Length);

            Assert.AreEqual("Vanguard", roster[2].role);
            Assert.IsNotNull(roster[2].humanBodyPrefab);
            Assert.IsNotNull(roster[2].aiBodyPrefab);
            Assert.AreEqual("BastionHeavyHuman", roster[2].humanBodyPrefab.name);
            Assert.AreEqual("BastionHeavyAI", roster[2].aiBodyPrefab.name);
            Assert.IsNull(roster[2].projectilePrefab);
            Assert.AreEqual("AEGIS SHOCKWAVE", roster[2].superName);
            Assert.AreEqual(3, CharacterSkillBook.For(roster[2]).Length);
        }

        [Test]
        public void SmallerRosterIsReusedOnlyAfterItsFirstPass()
        {
            int[] lineup = MatchLineupPlanner.BuildTeamDefinitionIndices(
                2, ArenaLayout.TeamSize, 1, 42);

            Assert.AreEqual(1, lineup[0]);
            Assert.AreEqual(0, lineup[1], "The other unique hero should be used before reuse begins.");
            AssertDefinitionIndicesAreValid(lineup, 2);
        }

        [Test]
        public void SameFrameRespawnsReserveDistinctTeamSlots()
        {
            var root = new GameObject("RespawnReservationTest");
            int previousTargetFrameRate = Application.targetFrameRate;
            try
            {
                var manager = root.AddComponent<MatchManager>();
                manager.ConfigureMode(GameMode.Knockout);
                manager.blueSpawns = BuildSpawns(root.transform, TeamId.Blue);

                var selected = new HashSet<Vector3>();
                for (int i = 0; i < ArenaLayout.TeamSize; i++)
                    Assert.IsTrue(selected.Add(manager.GetSpawnPoint(TeamId.Blue)),
                        "Same-frame respawns should not stack on an already reserved slot.");
            }
            finally
            {
                Object.DestroyImmediate(root);
                Application.targetFrameRate = previousTargetFrameRate;
            }
        }

        static Transform[] BuildSpawns(Transform parent, TeamId team)
        {
            var result = new Transform[ArenaLayout.TeamSize];
            for (int i = 0; i < result.Length; i++)
            {
                var spawn = new GameObject(team + "Spawn" + i).transform;
                spawn.SetParent(parent, false);
                spawn.position = ArenaLayout.SpawnPosition(team, i);
                result[i] = spawn;
            }
            return result;
        }

        static void AssertDefinitionIndicesAreValid(int[] indices, int rosterCount)
        {
            for (int i = 0; i < indices.Length; i++)
                Assert.That(indices[i], Is.InRange(0, rosterCount - 1));
        }
    }
}
