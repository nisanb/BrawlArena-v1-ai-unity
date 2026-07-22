using UnityEngine;
using UnityEngine.UI;
using TMPro;
using Crownfall.UI;

namespace Crownfall
{
    /// End-of-match ceremony: spinning rays behind the crown/skull, elastic
    /// title, count-up rewards, confetti on victory.
    public partial class HUDController
    {
        TMP_Text resultTitle, resultSub;
        Image resultIcon;
        RectTransform resultRays, resultTitleRect, resultIconRect;
        GameObject rewardsRow;
        TMP_Text rewardCoinsText, rewardXpText, rewardTrophyText, levelUpNum;
        RectTransform levelUpRoot, levelUpRays, levelUpBadge, levelUpTitle, levelUpGems;

        void BuildResult()
        {
            resultModal = MakePanel("Result");
            var t = resultModal.Go.transform;
            // flat dark under the vignette — the vignette texture alone is too
            // transparent in the middle and the fight HUD bled through
            Img("DimFlat", t, Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f),
                Vector2.zero, Vector2.zero, null, new Color(0.02f, 0.02f, 0.05f, 0.62f), true);
            var dim = Img("Dim", t, Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f),
                Vector2.zero, Vector2.zero, dimBlack, new Color(1f, 1f, 1f, 0.9f), true);
            dim.type = Image.Type.Simple;

            var rays = Icon("Rays", t, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                new Vector2(0, 268), new Vector2(760, 760), fxRays, new Color(1f, 0.85f, 0.4f, 0.22f));
            resultRays = rays.rectTransform;
            resultIcon = Icon("Ico", t, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                new Vector2(0.5f, 0.5f), new Vector2(0, 268), new Vector2(150, 150), iconCrown, Color.white);
            resultIconRect = resultIcon.rectTransform;
            resultTitle = Txt("Title", t, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                new Vector2(0.5f, 0.5f), new Vector2(0, 110), new Vector2(1400, 220), "VICTORY", fontBig, 150, Gold);
            resultTitleRect = resultTitle.rectTransform;
            resultSub = Txt("Sub", t, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                new Vector2(0.5f, 0.5f), new Vector2(0, -22), new Vector2(900, 62), "", fontMid, 36, Color.white);

            // match rewards strip (hidden for demo matches) — designed navy row
            var rr = Img("Rewards", t, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                new Vector2(0.5f, 0.5f), new Vector2(0, -88), new Vector2(560, 62), rowNavy, Color.white);
            rewardsRow = rr.gameObject;
            Icon("CoinI", rr.transform, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(0f, 0.5f),
                new Vector2(24, 0), new Vector2(38, 38), iconCoinBig, Color.white);
            rewardCoinsText = Txt("CoinT", rr.transform, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f),
                new Vector2(0f, 0.5f), new Vector2(68, 1), new Vector2(110, 44), "+0", fontMid, 27, Gold,
                TextAlignmentOptions.Left);
            rewardXpText = Txt("XpT", rr.transform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                new Vector2(0.5f, 0.5f), new Vector2(10, 1), new Vector2(180, 44), "+0 XP", fontMid, 27,
                new Color(0.65f, 0.85f, 1f));
            Icon("TroI", rr.transform, new Vector2(1f, 0.5f), new Vector2(1f, 0.5f), new Vector2(1f, 0.5f),
                new Vector2(-118, 0), new Vector2(38, 38), menuTrophy, Color.white);
            rewardTrophyText = Txt("TroT", rr.transform, new Vector2(1f, 0.5f), new Vector2(1f, 0.5f),
                new Vector2(1f, 0.5f), new Vector2(-24, 1), new Vector2(90, 44), "+0", fontMid, 27, Gold,
                TextAlignmentOptions.Left);
            // level-up celebration cluster — parked left of the ceremony centre so
            // it never covers the rewards strip; shown only when the match levelled us
            levelUpRoot = Rect("LevelUp", t, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                new Vector2(0.5f, 0.5f), new Vector2(-520, 10), new Vector2(380, 420));
            levelUpRays = Icon("Rays", levelUpRoot, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(330, 330), fxRays,
                new Color(1f, 0.85f, 0.4f, 0.28f)).rectTransform;
            Glow("Glow", levelUpRoot, Vector2.zero, 300f, new Color(1f, 0.85f, 0.4f, 0.55f));
            var lvBadge = Icon("Badge", levelUpRoot, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(140, 140), lvl3Badge, Color.white);
            levelUpBadge = lvBadge.rectTransform;
            levelUpNum = Txt("Num", lvBadge.transform, Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f),
                new Vector2(0, 9), new Vector2(-18, -44), "1", fontMid, 52, Color.white);
            levelUpTitle = Txt("LvTitle", levelUpRoot, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                new Vector2(0.5f, 0.5f), new Vector2(0, 128), new Vector2(380, 56), "LEVEL UP!",
                fontMid, 40, Gold).rectTransform;
            levelUpGems = Rect("Gems", levelUpRoot, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                new Vector2(0.5f, 0.5f), new Vector2(0, -112), new Vector2(220, 46));
            Icon("GemI", levelUpGems, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f),
                new Vector2(0.5f, 0.5f), new Vector2(28, 0), new Vector2(40, 40), icoGemGold, Color.white);
            Txt("GemT", levelUpGems, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(0f, 0.5f),
                new Vector2(56, 1), new Vector2(160, 42), "+10 GEMS", fontMid, 28, Gold,
                TextAlignmentOptions.Left);

            MenuButton(t, new Vector2(-200, -196), new Vector2(370, 96), "REMATCH", 34,
                btnGreen, icoRefresh, () => MatchManager.I?.Restart());
            MenuButton(t, new Vector2(200, -196), new Vector2(370, 96), "HOME", 34,
                btnBlue, icoHome, () => MatchManager.I?.QuitToMenu());
            Txt("Hint", t, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                new Vector2(0.5f, 0.5f), new Vector2(0, -262), new Vector2(400, 30), "press  [R]",
                fontSmall, 18, new Color(1f, 1f, 1f, 0.55f));
        }

        void OnEnded(Team winner)
        {
            var pmTeam = MatchManager.I?.PlayerMotor?.Identity != null
                ? MatchManager.I.PlayerMotor.Identity.team : Team.Azure;
            bool won = winner == pmTeam;
            router.OpenModal(resultModal);
            resultTitle.text = won ? "VICTORY" : "DEFEAT";
            resultTitle.color = won ? Gold : new Color(0.85f, 0.3f, 0.3f);
            resultIcon.sprite = won ? iconCrown : icoSkull;
            resultIcon.color = won ? Color.white : new Color(0.9f, 0.4f, 0.38f);
            resultRays.GetComponent<Image>().color = won
                ? new Color(1f, 0.85f, 0.4f, 0.22f) : new Color(0.7f, 0.25f, 0.25f, 0.16f);
            resultSub.text = $"Azure {MatchManager.I.ScoreAzure}  —  {MatchManager.I.ScoreCrimson} Crimson";

            UiTween.SpinForever(resultRays, won ? 16f : 30f);
            UiTween.Scale(resultIconRect, Vector3.one * 2.4f, Vector3.one, 0.5f, UiTween.Ease.BounceOut, 0.1f);
            UiTween.Scale(resultTitleRect, Vector3.one * 0.4f, Vector3.one, 0.7f, UiTween.Ease.ElasticOut, 0.25f);
            if (won)
            {
                Burst(fxConfettiPrefab, root, new Vector2(-380, 160), 1.1f);
                Burst(fxConfettiPrefab, root, new Vector2(380, 160), 1.1f);
                Burst(fxConfettiPrefab, root, new Vector2(0, 320), 1.3f);
            }

            var r = MatchManager.I.LastRewards;
            rewardsRow.SetActive(r.Any);
            levelUpRoot.gameObject.SetActive(r.leveledUp);
            if (r.leveledUp)
            {
                levelUpNum.text = CrownfallMeta.Level.ToString();
                UiTween.SpinForever(levelUpRays, 24f);
                // Scale-from-zero (not PopIn) so the cluster stays invisible
                // through the delay while the title lands first
                UiTween.Scale(levelUpRoot, Vector3.zero, Vector3.one, 0.5f,
                    UiTween.Ease.BackOut, 1.2f, () =>
                    {
                        UiTween.Punch(levelUpBadge, 0.16f, 0.3f);
                        Burst(fxConfettiPrefab, levelUpRoot, Vector2.zero, 1f);
                    });
                UiTween.PopIn(levelUpTitle, 0.35f, 1.5f);
                UiTween.PopIn(levelUpGems, 0.35f, 1.65f);
            }
            if (r.Any)
            {
                UiTween.CountUp(rewardCoinsText, 0, r.coins, 0.9f, v => $"+{v}");
                UiTween.CountUp(rewardXpText, 0, r.xp, 0.9f, v => $"+{v} XP");
                rewardTrophyText.text = r.trophies >= 0 ? $"+{r.trophies}" : r.trophies.ToString();
                rewardTrophyText.color = r.trophies >= 0 ? Gold : new Color(1f, 0.5f, 0.45f);
            }
        }
    }
}
