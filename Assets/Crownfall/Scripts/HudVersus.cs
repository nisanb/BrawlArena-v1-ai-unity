using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using Crownfall.UI;

namespace Crownfall
{
    /// Match loading screen (the pack's Play_BattleVS made 3v3): azure team
    /// cards on the left, crimson on the right, the designed VS wordmark in the
    /// middle, portraits and class stat bars per fighter. Shown between the
    /// menu handoff and the countdown.
    public partial class HUDController
    {
        UiPanel versusScreen;
        RectTransform versusVsRect, versusRibbonRect;
        readonly List<RectTransform> versusLeftRows = new List<RectTransform>();
        readonly List<RectTransform> versusRightRows = new List<RectTransform>();
        Transform versusRoot;
        TMP_Text versusModeText;

        void BuildVersus()
        {
            versusScreen = MakePanel("Versus");
            var t = versusScreen.Go.transform;
            versusRoot = t;

            var dim = Img("Dim", t, Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f),
                Vector2.zero, Vector2.zero, dimNavy, Color.white, true);
            dim.type = Image.Type.Simple;
            Glow("MidGlow", t, Vector2.zero, 900, new Color(1f, 0.85f, 0.45f, 0.20f));

            // mode ribbon up top
            var ribbon = Img("Ribbon", t, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f),
                new Vector2(0.5f, 1f), new Vector2(0, -26), new Vector2(640, 132), ribbonYellow, Color.white);
            versusRibbonRect = ribbon.rectTransform;
            versusModeText = Txt("Mode", ribbon.transform, Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f),
                new Vector2(0, 12), new Vector2(-200, -60), "10-KILL BRAWL", fontMid, 40, Color.white);

            // the designed VS wordmark, dead center
            var vs = Icon("VS", t, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                new Vector2(0, 10), new Vector2(280, 280), vsText, Color.white);
            versusVsRect = vs.rectTransform;

            Txt("Hint", t, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0.5f, 0f),
                new Vector2(0, 36), new Vector2(700, 30), "raising the arena...", fontSmall, 20,
                new Color(1f, 1f, 1f, 0.6f));
        }

        /// One team card: designed navy row, rarity item-frame portrait socket,
        /// name + class, mini stat bars from the class kit.
        RectTransform VersusRow(bool azure, int slot, string playerName, ClassId cls)
        {
            var kit = ClassKits.Get(cls);
            float x = azure ? -480f : 480f;
            var row = Img((azure ? "Az" : "Cr") + slot, versusRoot,
                new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                new Vector2(x, 170 - slot * 190), new Vector2(470, 172), rowNavy, Color.white);

            var frame = Img("PFrame", row.transform, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f),
                new Vector2(0f, 0.5f), new Vector2(14, 0), new Vector2(140, 148),
                azure ? itemBlue : itemRed, Color.white);
            var portrait = PortraitFor((int)cls);
            if (portrait != null)
                Icon("P", frame.transform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                    new Vector2(0.5f, 0.5f), new Vector2(0, 2), new Vector2(120, 128), portrait, Color.white);
            else
                Icon("P", frame.transform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                    new Vector2(0.5f, 0.5f), new Vector2(0, 2), new Vector2(72, 72), IconFor(cls), Color.white);

            // team-colored name plate (designed trapezoid)
            var plate = Img("Name", row.transform, new Vector2(0f, 1f), new Vector2(0f, 1f),
                new Vector2(0f, 1f), new Vector2(162, -14), new Vector2(290, 52),
                azure ? trapBlue : trapOrange, Color.white);
            Txt("N", plate.transform, Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f),
                new Vector2(0, 1), new Vector2(-24, -8), playerName.ToUpper(), fontSmall, 24, Color.white);
            Txt("C", row.transform, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(0f, 1f),
                new Vector2(172, -66), new Vector2(280, 26), kit.displayName.ToUpper(), fontSmall, 16,
                new Color(1f, 1f, 1f, 0.7f), TextAlignmentOptions.Left);

            VersusStat(row.transform, new Vector2(258, -102), icoStatHp,
                Mathf.Clamp01(kit.maxHealth / 170f), bar4FillGreen, Color.white);
            VersusStat(row.transform, new Vector2(258, -128), icoStatDmg,
                Mathf.Clamp01(kit.lightDamage / 30f), bar4FillRed, Color.white);
            VersusStat(row.transform, new Vector2(258, -154), icoStatSpd,
                Mathf.Clamp01((kit.runSpeed - 3f) / 2.5f), bar4FillWhite, AzureCol);
            return row.rectTransform;
        }

        void VersusStat(Transform parent, Vector2 pos, Sprite icon, float frac, Sprite fill, Color tint)
        {
            Icon("SI", parent, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(0.5f, 0.5f),
                pos + new Vector2(-86, 0), new Vector2(22, 22), icon, Color.white);
            var bg = Img("SB", parent, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(0.5f, 0.5f),
                pos + new Vector2(30, 0), new Vector2(196, 16), bar4Bg, Color.white);
            var f = MakeFill(bg.rectTransform, fill, tint, new Vector2(196, 16));
            f.fillAmount = Mathf.Max(0.08f, frac);
        }

        /// Populate from the scene roster, play the entrance, then hand off to
        /// the match start after a beat.
        void ShowVersus(System.Action onDone)
        {
            foreach (var r in versusLeftRows) if (r != null) Destroy(r.gameObject);
            foreach (var r in versusRightRows) if (r != null) Destroy(r.gameObject);
            versusLeftRows.Clear();
            versusRightRows.Clear();

            int selected = CrownfallMeta.SelectedClass;
            int az = 0, cr = 0;
            foreach (var id in FindObjectsByType<CombatantIdentity>(
                FindObjectsInactive.Include, FindObjectsSortMode.InstanceID))
            {
                // only the selected variant of the four player rigs fights
                if (id.isPlayer && (int)id.classId != selected) continue;
                string label = id.isPlayer ? CrownfallMeta.PlayerName : id.displayName;
                if (id.team == Team.Azure && az < 3)
                    versusLeftRows.Add(VersusRow(true, az++, label, id.classId));
                else if (id.team == Team.Crimson && cr < 3)
                    versusRightRows.Add(VersusRow(false, cr++, label, id.classId));
            }

            versusModeText.text = GameModes.Selected.title;
            router.Show(versusScreen);
            for (int i = 0; i < versusLeftRows.Count; i++)
                UiTween.SlideIn(versusLeftRows[i], new Vector2(-620f, 0f), 0.42f, UiTween.Ease.CubicOut, 0.07f * i);
            for (int i = 0; i < versusRightRows.Count; i++)
                UiTween.SlideIn(versusRightRows[i], new Vector2(620f, 0f), 0.42f, UiTween.Ease.CubicOut, 0.07f * i);
            UiTween.SlideIn(versusRibbonRect, new Vector2(0, 170f), 0.4f, UiTween.Ease.BackOut);
            UiTween.Scale(versusVsRect, Vector3.one * 3f, Vector3.one, 0.45f, UiTween.Ease.BounceOut, 0.25f);
            GameEffects.I?.PlayUi(GameEffects.I.uiFight, 0.7f);

            StartCoroutine(VersusHold(onDone));
        }

        IEnumerator VersusHold(System.Action onDone)
        {
            yield return new WaitForSecondsRealtime(2.9f);
            onDone?.Invoke();
        }
    }
}
