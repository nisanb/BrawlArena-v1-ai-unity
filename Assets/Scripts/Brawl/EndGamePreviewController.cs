using UnityEngine;

namespace BrawlArena
{
    /// <summary>
    /// Supplies deterministic data to the EndGamePreview scene without
    /// requiring a match, input device, or gameplay systems.
    /// </summary>
    public class EndGamePreviewController : MonoBehaviour
    {
        [Header("Result")]
        public TeamId winningTeam = TeamId.Blue;
        [Min(0)] public int eliminations = 3;
        [Min(0)] public int brawlerPoints = 54;
        [Min(0)] public int coins = 40;

        [Header("Brawler Progress")]
        [Min(1)] public int brawlerLevel = 1;
        [Min(0)] public int pointsBefore = 18;
        [Min(0)] public int pointsAfter = 72;
        [Min(1)] public int pointsNeeded = 50;

        void Start()
        {
            var hud = FindFirstObjectByType<BrawlHUD>();
            if (hud == null)
            {
                Debug.LogError("[EndGamePreview] BrawlHUD is missing.");
                return;
            }

            hud.SetGameplayVisible(false);
            hud.PresentMatchResult(winningTeam, new BrawlHUD.MatchRewardSummary
            {
                eliminations = eliminations,
                brawlerPoints = brawlerPoints,
                coins = coins,
                pointsBefore = pointsBefore,
                pointsAfter = pointsAfter,
                level = brawlerLevel,
                pointsNeeded = pointsNeeded,
            });
        }
    }
}
