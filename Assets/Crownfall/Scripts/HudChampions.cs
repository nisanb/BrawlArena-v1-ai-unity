using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using Crownfall.UI;

namespace Crownfall
{
    /// Champion select: four CardFrame hero cards over a navy dim, gold focus
    /// glow on the current pick, mini stat bars, sparkle burst on selection.
    public partial class HUDController
    {
        RectTransform champFocus, champFocusGlow;
        readonly List<Vector2> champCardPos = new List<Vector2>();
        readonly List<RectTransform> champCards = new List<RectTransform>();

        void BuildChampions()
        {
            champScreen = MakePanel("Champions");
            champScreen.OnBack = () => { MatchManager.I?.BackToMenu(); return true; };
            var t = champScreen.Go.transform;

            var dim = Img("Dim", t, Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f),
                Vector2.zero, Vector2.zero, dimNavy, new Color(1f, 1f, 1f, 0.9f));
            dim.type = Image.Type.Simple;

            var ribbon = Img("Ribbon", t, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f),
                new Vector2(0.5f, 1f), new Vector2(0, -30), new Vector2(760, 148), ribbonYellow, Color.white);
            Txt("Title", ribbon.transform, Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f),
                new Vector2(0, 12), new Vector2(-220, -66), "CHAMPIONS", fontMid, 48, Color.white);

            MenuButton(t, Vector2.zero, new Vector2(180, 68), "BACK", 24,
                btnGray, icoBack, () => MatchManager.I?.BackToMenu())
                .GetComponent<RectTransform>().SetAnchor(new Vector2(0f, 1f), new Vector2(126, -60));

            // focus glow pair sits behind the cards; hops onto the current pick
            var glowImg = Icon("FocusGlow", t, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(520, 560), cardChampGlow, Gold);
            champFocusGlow = glowImg.rectTransform;

            champCardPos.Clear();
            champCards.Clear();
            for (int i = 0; i < 4; i++)
            {
                int idx = i;
                var kit = ClassKits.Get((ClassId)i);
                var pos = new Vector2(-540 + i * 360, -66);
                champCardPos.Add(pos);
                var card = Img("Class" + i, t, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                    new Vector2(0.5f, 0.5f), pos, new Vector2(310, 400),
                    i % 2 == 0 ? cardChampBlue : cardChampPurple, Color.white, true);
                card.type = Image.Type.Simple;
                champCards.Add(card.rectTransform);
                MakeClickable(card, () =>
                {
                    MatchManager.I?.SelectChampion(idx);
                    Burst(fxSparklePrefab, champCards[idx], Vector2.zero, 0.9f);
                    RefreshHub();
                });

                Glow("G", card.transform, new Vector2(0, 112), 190, new Color(1f, 1f, 1f, 0.22f));
                Icon("Ico", card.transform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                    new Vector2(0, 112), new Vector2(112, 112), IconFor(kit.id), Color.white);
                Txt("N", card.transform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                    new Vector2(0, 30), new Vector2(280, 52), kit.displayName.ToUpper(), fontMid, 33, Color.white);
                var blurb = Txt("B", card.transform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                    new Vector2(0.5f, 0.5f), new Vector2(0, -34), new Vector2(252, 80), kit.blurb, fontSmall, 18,
                    new Color(0.93f, 0.93f, 0.99f));
                blurb.enableWordWrapping = true;

                StatBar(card.transform, new Vector2(0, -96), icoStatHp, Mathf.Clamp01(kit.maxHealth / 170f),
                    new Color(0.42f, 0.88f, 0.34f));
                StatBar(card.transform, new Vector2(0, -128), icoStatDmg, Mathf.Clamp01(kit.lightDamage / 30f),
                    new Color(1f, 0.5f, 0.35f));
                StatBar(card.transform, new Vector2(0, -160), icoStatSpd, Mathf.Clamp01((kit.runSpeed - 3f) / 2.5f),
                    new Color(0.45f, 0.75f, 1f));
            }

            var focus = Icon("Focus", t, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                new Vector2(0.5f, 0.5f), champCardPos[0], new Vector2(354, 448), cardChampFocus, Gold);
            focus.preserveAspect = false;
            champFocus = focus.rectTransform;

            MenuButton(t, Vector2.zero, new Vector2(320, 100), "BATTLE", 40,
                btnYellow, icoPlay, OpenPlayMenu)
                .GetComponent<RectTransform>().SetAnchor(new Vector2(1f, 0f), new Vector2(-200, 88));

            Txt("Hint", t, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0.5f, 0f),
                new Vector2(0, 74), new Vector2(1200, 34), "pick your champion  ·  they take the podium and fight for you",
                fontSmall, 20, new Color(0.85f, 0.88f, 0.95f));
            string controls = Application.isMobilePlatform
                ? "left thumb: move  ·  right drag: camera  ·  ATTACK tap = light, hold = heavy  ·  DODGE tap = roll, hold = sprint"
                : "WASD move  ·  LMB attack (hold = heavy)  ·  RMB block / heavy  ·  SPACE roll  ·  SHIFT sprint  ·  Q lock-on  ·  F1 autopilot";
            Txt("Controls", t, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0.5f, 0f),
                new Vector2(0, 38), new Vector2(1700, 34), controls,
                fontSmall, 18, new Color(0.72f, 0.75f, 0.83f));

            champScreen.OnShow = () =>
            {
                for (int i = 0; i < champCards.Count; i++)
                    UiTween.PopIn(champCards[i], 0.34f, 0.06f * i);
                RefreshChampFocus();
            };
        }

        /// One icon + mini fill bar row on a champion card.
        void StatBar(Transform parent, Vector2 pos, Sprite icon, float frac, Color color)
        {
            Icon("SI", parent, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                pos + new Vector2(-108, 0), new Vector2(26, 26), icon, Color.white);
            var bg = Img("SB", parent, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                pos + new Vector2(14, 0), new Vector2(190, 18), barBgBasic, new Color(0.05f, 0.05f, 0.09f, 0.9f));
            var fill = MakeFill(bg.rectTransform, barFillBasic, color, new Vector2(190, 18));
            fill.fillAmount = Mathf.Max(0.08f, frac);
        }

        void RefreshChampFocus()
        {
            if (champFocus == null || CrownfallMeta.SelectedClass >= champCardPos.Count) return;
            var pos = champCardPos[CrownfallMeta.SelectedClass];
            champFocus.anchoredPosition = pos;
            champFocusGlow.anchoredPosition = pos;
            UiTween.Punch(champFocus, 0.1f, 0.3f);
        }
    }
}
