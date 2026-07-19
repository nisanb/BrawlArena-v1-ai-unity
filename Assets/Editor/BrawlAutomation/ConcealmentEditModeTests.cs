using NUnit.Framework;
using UnityEngine;

namespace BrawlArena.EditorAutomation
{
    public class ConcealmentEditModeTests
    {
        [Test]
        public void RuleMatrixMatchesBushSemantics()
        {
            const float now = 100f;
            const float notRevealed = 0f;
            float far = ConcealmentRules.ProximityRevealRadius + 1f;

            // In a patch, viewer outside, far away, no reveal => hidden.
            Assert.IsTrue(ConcealmentRules.IsHidden(true, false, far, notRevealed, now));
            // Not in any patch => always visible.
            Assert.IsFalse(ConcealmentRules.IsHidden(false, false, far, notRevealed, now));
            // Viewer shares the patch => visible.
            Assert.IsFalse(ConcealmentRules.IsHidden(true, true, far, notRevealed, now));
            // Viewer inside the proximity ring => visible.
            Assert.IsFalse(ConcealmentRules.IsHidden(true, false,
                ConcealmentRules.ProximityRevealRadius - 0.1f, notRevealed, now));
            // Recently attacked/damaged => visible until the reveal expires.
            Assert.IsFalse(ConcealmentRules.IsHidden(true, false, far, now + 0.5f, now));
            Assert.IsTrue(ConcealmentRules.IsHidden(true, false, far, now - 0.5f, now));
        }

        [Test]
        public void ActionProfileScalesLayoutAndResetsCleanly()
        {
            Assert.AreEqual(ArenaProfile.Classic, ArenaLayout.Profile,
                "Tests must start on the Classic profile.");
            float classicExtent = ArenaLayout.PlayableHalfExtent;
            Vector3 classicSpawn = ArenaLayout.SpawnPosition(TeamId.Blue, 0);
            try
            {
                ArenaLayout.Profile = ArenaProfile.Action;
                Assert.AreEqual(80f, ArenaLayout.PlayableHalfExtent);
                Assert.Greater(ArenaLayout.GroundHalfExtent, ArenaLayout.PlayableHalfExtent);
                Assert.Greater(ArenaLayout.MinimapHalfExtent, ArenaLayout.GroundHalfExtent);
                Assert.Greater(ArenaLayout.GateDepth, classicExtent);
                for (int i = 0; i < ArenaLayout.TeamSize; i++)
                {
                    Vector3 blue = ArenaLayout.SpawnPosition(TeamId.Blue, i);
                    Vector3 red = ArenaLayout.SpawnPosition(TeamId.Red, i);
                    Assert.AreEqual(blue.x, red.x);
                    Assert.AreEqual(-blue.z, red.z);
                    Assert.Less(Mathf.Abs(blue.z), ArenaLayout.PlayableHalfExtent);
                }
                Assert.AreNotEqual(classicSpawn, ArenaLayout.SpawnPosition(TeamId.Blue, 0));
            }
            finally
            {
                ArenaLayout.Profile = ArenaProfile.Classic;
            }
            Assert.AreEqual(classicExtent, ArenaLayout.PlayableHalfExtent);
        }

        [Test]
        public void GrassPatchVolumeAnswersPlanarContainment()
        {
            var go = new GameObject("PatchTest");
            try
            {
                var patch = go.AddComponent<GrassPatchVolume>();
                patch.radius = 5f;
                go.transform.position = new Vector3(10f, 0f, 10f);

                Assert.IsTrue(patch.Contains(new Vector3(12f, 0f, 10f)));
                // Height must not matter: a jumping brawler stays concealed.
                Assert.IsTrue(patch.Contains(new Vector3(12f, 3f, 10f)));
                Assert.IsFalse(patch.Contains(new Vector3(16f, 0f, 10f)));
            }
            finally
            {
                Object.DestroyImmediate(go);
            }
        }
    }
}
