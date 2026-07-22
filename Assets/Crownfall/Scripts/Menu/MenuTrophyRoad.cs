using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using Crownfall.UI;

namespace Crownfall
{
    /// Trophy road: milestone rewards for lifetime trophies, following the
    /// pack's Pass/Missions pattern — designed navy rows in a scrolling list
    /// under a progress header, with claim / done / locked states.
    public partial class MenuHud
    {
        static readonly int[] RoadReqs = { 50, 100, 200, 350, 500, 750, 1000 };
        static readonly (int coins, int gems)[] RoadRewards =
            { (100, 0), (150, 0), (0, 8), (250, 0), (0, 12), (400, 0), (0, 20) };

        const float RoadRowPitch = 122f;
        const float RoadViewH = 500f;

        /// PlayerPrefs claim ledger, keyed by milestone requirement.
        static class RoadClaims
        {
            public static bool Claimed(int req) =>
                PlayerPrefs.GetInt("trophyroad.claimed." + req, 0) != 0;

            public static void MarkClaimed(int req)
            {
                PlayerPrefs.SetInt("trophyroad.claimed." + req, 1);
                PlayerPrefs.Save();
            }
        }

        class RoadRowUi
        {
            public CanvasGroup group;
            public Button claimBtn;
            public TMP_Text claimLabel;
            public GameObject glow;
        }
        readonly List<RoadRowUi> roadRows = new List<RoadRowUi>();

        TMP_Text roadTrophyCount, roadNextLabel;
        Image roadProgressFill;
        ScrollRect roadScroll;
        RectTransform roadContent;

        void BuildTrophyRoad()
        {
            var frame = ModalShell("Trophy Road", new Vector2(900, 680), out trophyRoadModal);

            // -- progress header: current trophies + bar toward the next milestone
            Icon("HdrTrophy", frame, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0.5f, 0.5f),
                new Vector2(-330, -104), new Vector2(54, 54), icoTrophyBig, Color.white);
            roadTrophyCount = Txt("HdrCount", frame, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f),
                new Vector2(0f, 0.5f), new Vector2(-296, -104), new Vector2(150, 40), "0", fontMid, 30, Gold,
                TextAlignmentOptions.Left);
            var pbBg = Img("HdrPB", frame, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f),
                new Vector2(0.5f, 0.5f), new Vector2(50, -104), new Vector2(330, 24), bar4Bg, Color.white);
            roadProgressFill = MakeFill(pbBg.rectTransform, bar4FillGreen, Color.white, new Vector2(330, 24));
            roadNextLabel = Txt("HdrNext", frame, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f),
                new Vector2(0f, 0.5f), new Vector2(228, -104), new Vector2(190, 30), "", fontSmall, 16,
                new Color(1f, 1f, 1f, 0.75f), TextAlignmentOptions.Left);

            // -- scrolling milestone list (seven rows outgrow the frame height)
            var view = Img("RoadView", frame, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f),
                new Vector2(0.5f, 1f), new Vector2(0, -150), new Vector2(740, RoadViewH),
                null, new Color(0f, 0f, 0f, 0f), true);
            view.gameObject.AddComponent<RectMask2D>();
            roadScroll = view.gameObject.AddComponent<ScrollRect>();
            roadContent = Rect("Content", view.transform, new Vector2(0f, 1f), new Vector2(1f, 1f),
                new Vector2(0.5f, 1f), Vector2.zero, new Vector2(0, 20f + RoadReqs.Length * RoadRowPitch));
            roadScroll.viewport = view.rectTransform;
            roadScroll.content = roadContent;
            roadScroll.horizontal = false;
            roadScroll.movementType = ScrollRect.MovementType.Clamped;
            roadScroll.scrollSensitivity = 26f;

            roadRows.Clear();
            for (int i = 0; i < RoadReqs.Length; i++)
            {
                int req = RoadReqs[i];
                var (coins, gems) = RoadRewards[i];
                var ui = new RoadRowUi();

                var row = Img("Road" + req, roadContent, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f),
                    new Vector2(0.5f, 1f), new Vector2(0, -10 - i * RoadRowPitch), new Vector2(700, 110),
                    rowNavy, Color.white);
                ui.group = row.gameObject.AddComponent<CanvasGroup>();

                Icon("T", row.transform, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(0.5f, 0.5f),
                    new Vector2(52, 2), new Vector2(54, 54), icoTrophyBig, Color.white);
                Txt("R", row.transform, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(0f, 0.5f),
                    new Vector2(92, 12), new Vector2(150, 34), req.ToString(), fontMid, 28, Gold,
                    TextAlignmentOptions.Left);
                Txt("RL", row.transform, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(0f, 0.5f),
                    new Vector2(92, -16), new Vector2(150, 24), "TROPHIES", fontSmall, 13,
                    new Color(1f, 1f, 1f, 0.55f), TextAlignmentOptions.Left);

                Icon("RI", row.transform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                    new Vector2(0.5f, 0.5f), new Vector2(-44, 2), new Vector2(44, 44),
                    coins > 0 ? iconCoinBig : icoGemGold, Color.white);
                Txt("RA", row.transform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                    new Vector2(0f, 0.5f), new Vector2(-14, 2), new Vector2(150, 34),
                    coins > 0 ? "+" + coins : "+" + gems, fontMid, 24,
                    coins > 0 ? Gold : new Color(0.8f, 0.9f, 1f), TextAlignmentOptions.Left);

                ui.glow = Glow("G", row.transform, new Vector2(255, 0), 170,
                    new Color(1f, 0.9f, 0.5f, 0.4f)).gameObject;
                var claim = Img("Claim", row.transform, new Vector2(1f, 0.5f), new Vector2(1f, 0.5f),
                    new Vector2(1f, 0.5f), new Vector2(-20, 0), new Vector2(150, 58), btnGreen, Color.white);
                var rowRect = row.rectTransform;
                ui.claimBtn = MakeClickable(claim, () =>
                {
                    if (RoadClaims.Claimed(req) || CrownfallMeta.Trophies < req) return;
                    RoadClaims.MarkClaimed(req);
                    if (coins > 0) CrownfallMeta.AddCoins(coins);
                    else CrownfallMeta.AddGems(gems);
                    Burst(fxSparklePrefab, rowRect, Vector2.zero, 0.8f);
                    GameEffects.I?.PlayUi(GameEffects.I.killDing, 0.5f);
                    ShowToast(coins > 0 ? $"+{coins} COINS" : $"+{gems} GEMS");
                    RefreshTrophyRoad();
                    RefreshHub();
                });
                ui.claimLabel = Txt("L", claim.transform, Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f),
                    new Vector2(0, 2), new Vector2(-14, -10), "CLAIM", fontSmall, 20, Color.white);
                roadRows.Add(ui);

                int stagger = i;
                trophyRoadModal.OnShow += () => UiTween.SlideIn(rowRect, new Vector2(90f, 0f), 0.3f,
                    UiTween.Ease.CubicOut, 0.06f * stagger);
            }

            // static event — attached only while the modal is open, because
            // MenuHud's OnDestroy doesn't know this screen and can't detach it
            trophyRoadModal.OnShow += () =>
            {
                RefreshTrophyRoad();
                ScrollRoadToFirstOpen();
                CrownfallMeta.Changed -= RefreshTrophyRoad;
                CrownfallMeta.Changed += RefreshTrophyRoad;
            };
            trophyRoadModal.OnHide += () => CrownfallMeta.Changed -= RefreshTrophyRoad;
        }

        void RefreshTrophyRoad()
        {
            if (trophyRoadModal == null || !trophyRoadModal.Active)
            {
                CrownfallMeta.Changed -= RefreshTrophyRoad;  // scene torn down while open
                return;
            }

            int tr = CrownfallMeta.Trophies;
            roadTrophyCount.text = tr.ToString();
            int prev = 0, next = -1;
            for (int i = 0; i < RoadReqs.Length; i++)
            {
                if (tr < RoadReqs[i]) { next = RoadReqs[i]; break; }
                prev = RoadReqs[i];
            }
            roadNextLabel.text = next > 0 ? "NEXT  " + next : "ROAD COMPLETE";
            UiTween.FillTo(roadProgressFill,
                next > 0 ? Mathf.Clamp01((tr - prev) / (float)(next - prev)) : 1f);

            for (int i = 0; i < roadRows.Count; i++)
            {
                var ui = roadRows[i];
                bool claimed = RoadClaims.Claimed(RoadReqs[i]);
                bool can = !claimed && tr >= RoadReqs[i];
                ui.group.alpha = claimed || tr >= RoadReqs[i] ? 1f : 0.55f;
                ui.claimBtn.interactable = can;
                ui.claimLabel.text = claimed ? "DONE" : "CLAIM";
                ui.claimLabel.color = claimed ? new Color(1f, 1f, 1f, 0.55f) : Color.white;
                ui.glow.SetActive(can);
            }
        }

        /// Land the list on the first unclaimed milestone so the actionable
        /// row is on screen without hunting.
        void ScrollRoadToFirstOpen()
        {
            int first = roadRows.Count - 1;
            for (int i = 0; i < RoadReqs.Length; i++)
                if (!RoadClaims.Claimed(RoadReqs[i])) { first = i; break; }
            float maxScroll = Mathf.Max(0f, roadContent.sizeDelta.y - RoadViewH);
            roadScroll.StopMovement();
            roadContent.anchoredPosition = new Vector2(0, Mathf.Clamp(first * RoadRowPitch, 0f, maxScroll));
        }
    }
}
