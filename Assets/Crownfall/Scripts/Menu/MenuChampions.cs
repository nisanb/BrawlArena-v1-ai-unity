using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using Crownfall.UI;

namespace Crownfall
{
    /// Champion roster: five designed rarity-colored CardFrame03 cards with
    /// big portrait renders over trapezoid name plates, gold focus glow on
    /// the current pick, designed pre-colored stat fills, sparkle burst on
    /// selection and a gentle idle bob on the selected hero.
    public partial class MenuHud
    {
        RectTransform champFocus, champFocusGlow;
        readonly List<Vector2> champCardPos = new List<Vector2>();
        readonly List<RectTransform> champCards = new List<RectTransform>();
        readonly List<RectTransform> champPortraitRects = new List<RectTransform>();
        readonly List<Vector2> champPortraitRest = new List<Vector2>();
        int champBobIdx = -1;

        void BuildChampions()
        {
            champScreen = MakePanel("Champions");
            champScreen.OnBack = () => { ShowMenuLayer(); return true; };
            var t = champScreen.Go.transform;

            // translucent navy dim: the 3D champion still reads behind the cards
            var dim = Img("Dim", t, Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f),
                Vector2.zero, Vector2.zero, dimNavy, new Color(1f, 1f, 1f, 0.86f), true);
            dim.type = Image.Type.Simple;

            var ribbon = Img("Ribbon", t, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f),
                new Vector2(0.5f, 1f), new Vector2(0, -30), new Vector2(760, 148), ribbonYellow, Color.white);
            Txt("Title", ribbon.transform, Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f),
                new Vector2(0, 12), new Vector2(-220, -66), "CHAMPIONS", fontMid, 48, Color.white);

            MenuButton(t, Vector2.zero, new Vector2(180, 68), "BACK", 24,
                btnGray, icoBack, () => ShowMenuLayer())
                .GetComponent<RectTransform>().SetAnchor(new Vector2(0f, 1f), new Vector2(126, -60));

            // focus glow pair sits behind the cards; hops onto the current pick
            var glowImg = Icon("FocusGlow", t, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(520, 560), cardChampGlow, Gold);
            champFocusGlow = glowImg.rectTransform;

            champCardPos.Clear();
            champCards.Clear();
            champPortraitRects.Clear();
            champPortraitRest.Clear();
            champBobIdx = -1;
            int classCount = System.Enum.GetValues(typeof(ClassId)).Length;
            for (int i = 0; i < classCount; i++)
            {
                int idx = i;
                var kit = ClassKits.Get((ClassId)i);
                // centre the row: 6 cards fit at a 300px pitch inside 1920 wide
                var pos = new Vector2((i - (classCount - 1) * 0.5f) * 300f, -66);
                champCardPos.Add(pos);
                var card = Img("Class" + i, t, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                    new Vector2(0.5f, 0.5f), pos, new Vector2(262, 420), CardFor(i), Color.white, true);
                champCards.Add(card.rectTransform);
                MakeClickable(card, () =>
                {
                    CrownfallMeta.SelectedClass = idx;
                    Burst(fxSparklePrefab, champCards[idx], Vector2.zero, 0.9f);
                });

                var portrait = PortraitFor(i);
                RectTransform hero;
                if (portrait != null)
                {
                    hero = Icon("Portrait", card.transform, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f),
                        new Vector2(0.5f, 1f), new Vector2(0, -6), new Vector2(236, 250),
                        portrait, Color.white).rectTransform;
                }
                else
                {
                    Glow("G", card.transform, new Vector2(0, 84), 190, new Color(1f, 1f, 1f, 0.22f));
                    hero = Icon("Ico", card.transform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                        new Vector2(0.5f, 0.5f), new Vector2(0, 84), new Vector2(120, 120),
                        IconFor(kit.id), Color.white).rectTransform;
                }
                // BobForever treats the current anchoredPosition as its rest,
                // so the build-time pose must be remembered to restore it
                champPortraitRects.Add(hero);
                champPortraitRest.Add(hero.anchoredPosition);

                // designed trapezoid name plate, drawn after the portrait so
                // the hero's feet tuck behind it
                var plate = Img("NamePlate", card.transform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                    new Vector2(0.5f, 0.5f), new Vector2(0, -52), new Vector2(236, 44),
                    (i & 1) == 0 ? trapBlue : trapPurple, Color.white);
                Txt("N", plate.transform, Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f),
                    new Vector2(0, 1), new Vector2(-36, -8), kit.displayName.ToUpper(), fontMid, 26, Color.white);

                var blurb = Txt("B", card.transform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                    new Vector2(0.5f, 0.5f), new Vector2(0, -96), new Vector2(238, 42), kit.blurb, fontSmall, 16,
                    new Color(0.93f, 0.93f, 0.99f));
                blurb.enableWordWrapping = true;
                blurb.maxVisibleLines = 2;

                StatBar(card.transform, new Vector2(0, -128), icoStatHp, Mathf.Clamp01(kit.maxHealth / 190f),
                    bar4FillGreen, Color.white, ((int)kit.maxHealth).ToString());
                StatBar(card.transform, new Vector2(0, -152), icoStatDmg, Mathf.Clamp01(kit.lightDamage / 30f),
                    bar4FillRed, Color.white, ((int)kit.lightDamage).ToString());
                StatBar(card.transform, new Vector2(0, -176), icoStatSpd, Mathf.Clamp01((kit.runSpeed - 3f) / 2.5f),
                    bar4FillWhite, AzureCol, kit.runSpeed.ToString("0.0"));

                Txt("SK", card.transform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                    new Vector2(0, -196), new Vector2(240, 18), "SKILL: " + kit.skillName, fontSmall, 14, Gold);
            }

            var focus = Icon("Focus", t, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                new Vector2(0.5f, 0.5f), champCardPos[0], new Vector2(300, 462), cardChampFocus, Gold);
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

        /// One icon + mini fill bar row on a champion card. Basic04 family
        /// throughout: designed dark tube + matching pre-colored fills (the
        /// speed row tints the family's white fill — team-color coding).
        void StatBar(Transform parent, Vector2 pos, Sprite icon, float frac, Sprite fillSprite, Color fillTint,
            string valueText)
        {
            Icon("SI", parent, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                pos + new Vector2(-108, 0), new Vector2(26, 26), icon, Color.white);
            var bg = Img("SB", parent, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                pos + new Vector2(2, 0), new Vector2(164, 18), bar4Bg, Color.white);
            var fill = MakeFill(bg.rectTransform, fillSprite != null ? fillSprite : bar4FillWhite, fillTint,
                new Vector2(164, 18));
            fill.fillAmount = Mathf.Max(0.08f, frac);
            // numeric value so picks are informed reads, not guesses (Riley)
            Txt("V", parent, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                pos + new Vector2(106, 0), new Vector2(56, 22), valueText, fontSmall, 16,
                Color.white, TextAlignmentOptions.Right);
        }

        void RefreshChampFocus()
        {
            if (champFocus == null || CrownfallMeta.SelectedClass >= champCardPos.Count) return;
            int sel = CrownfallMeta.SelectedClass;
            var pos = champCardPos[sel];
            champFocus.anchoredPosition = pos;
            champFocusGlow.anchoredPosition = pos;
            UiTween.Punch(champFocus, 0.1f, 0.3f);

            // idle bob rides on the selected portrait only; RefreshHub calls
            // here on every meta change, so restart only on an actual switch
            if (sel == champBobIdx) return;
            if (champBobIdx >= 0 && champBobIdx < champPortraitRects.Count && champPortraitRects[champBobIdx] != null)
            {
                UiTween.StopLoop(champPortraitRects[champBobIdx]);
                champPortraitRects[champBobIdx].anchoredPosition = champPortraitRest[champBobIdx];
            }
            champBobIdx = sel;
            if (sel < champPortraitRects.Count && champPortraitRects[sel] != null)
            {
                champPortraitRects[sel].anchoredPosition = champPortraitRest[sel];
                UiTween.BobForever(champPortraitRects[sel], 4f, 2.2f);
            }
        }
    }
}
