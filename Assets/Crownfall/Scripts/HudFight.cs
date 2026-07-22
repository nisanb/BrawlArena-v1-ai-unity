using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using Crownfall.UI;

namespace Crownfall
{
    /// Fight HUD: score banner, timer, player vitals, ally rows, target frame,
    /// kill feed, announcements, lock-on marker, damage flash.
    public partial class HUDController
    {
        TMP_Text scoreAzureText, scoreCrimsonText, timerText;
        TMP_Text announceText;
        TMP_Text playerName;
        Image portraitIcon;
        GameObject targetFrame;
        TMP_Text targetName;
        Image targetIcon;
        Image targetFill, targetGhost;
        float targetShown = 1f, targetGhostShown = 1f;
        CombatMotor shownTarget;
        Image hpFill, hpGhost, stFill;
        Image skillPipIcon, skillPipCover;
        RectTransform skillPipRect;
        bool skillWasReady;
        Image damageFlash;
        RectTransform lockOnMarker;
        RectTransform feedContainer;
        GameObject autopilotTag;
        RectTransform scoreBannerRect, playerPanelRect;

        readonly List<(RectTransform rt, float dieAt)> feedEntries = new List<(RectTransform, float)>();

        class AllyRow { public CombatMotor motor; public Image fill; public TMP_Text label; public Image icon; }
        readonly List<AllyRow> allyRows = new List<AllyRow>();

        Coroutine announceRoutine;
        float hpShown = 1f, ghostShown = 1f, stShown = 1f;

        void BuildFightHud()
        {
            fightScreen = MakePanel("FightHud");
            var fight = fightScreen.Go.transform;

            // -- score banner: navy banner, team trapezoid plates, gold crown between scores
            var banner = Img("ScoreBanner", fight, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0.5f, 1f),
                new Vector2(0, -8), new Vector2(500, 96), bannerNavy, new Color(1f, 1f, 1f, 0.96f));
            scoreBannerRect = banner.rectTransform;
            var azPlate = Img("AzPlate", banner.transform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                new Vector2(0.5f, 0.5f), new Vector2(-163, 16), new Vector2(130, 36), trapBlue, Color.white);
            Txt("AzLabel", azPlate.transform, Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f),
                new Vector2(0, 1), new Vector2(-8, -6), "AZURE", fontSmall, 19, Color.white);
            var crPlate = Img("CrPlate", banner.transform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                new Vector2(0.5f, 0.5f), new Vector2(163, 16), new Vector2(150, 36), trapOrange, Color.white);
            Txt("CrLabel", crPlate.transform, Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f),
                new Vector2(0, 1), new Vector2(-8, -6), "CRIMSON", fontSmall, 19, Color.white);
            scoreAzureText = Txt("AzScore", banner.transform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                new Vector2(0.5f, 0.5f), new Vector2(-78, -14), new Vector2(90, 66), "0", fontMid, 46, Color.white);
            scoreCrimsonText = Txt("CrScore", banner.transform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                new Vector2(0.5f, 0.5f), new Vector2(78, -14), new Vector2(90, 66), "0", fontMid, 46, Color.white);
            Icon("Crown", banner.transform, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0.5f, 0.5f),
                new Vector2(0, -2), new Vector2(54, 54), iconCrown, Color.white);

            // -- timer plate under the banner
            var timerPlate = Img("TimerPlate", fight, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f),
                new Vector2(0.5f, 1f), new Vector2(0, -108), new Vector2(190, 42), plateRound, PlateDark);
            Icon("TimerIco", timerPlate.transform, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f),
                new Vector2(0f, 0.5f), new Vector2(14, 0), new Vector2(24, 24), icoTimer, Color.white);
            timerText = Txt("Timer", timerPlate.transform, Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f),
                new Vector2(12, 1), new Vector2(-40, -6), "5:00", fontSmall, 23, new Color(1f, 1f, 1f, 0.95f));

            // -- player panel: profile ring portrait with class icon, segmented HP
            var panel = Rect("PlayerPanel", fight, Vector2.zero, Vector2.zero, Vector2.zero,
                new Vector2(28, 26), new Vector2(560, 152));
            playerPanelRect = panel;
            Icon("PortraitRing", panel, Vector2.zero, Vector2.zero, Vector2.zero,
                new Vector2(0, 2), new Vector2(104, 108), profileRing, Color.white);
            Icon("PortraitInner", panel, Vector2.zero, Vector2.zero, Vector2.zero,
                new Vector2(9, 15), new Vector2(86, 86), profileInner, Color.white);
            portraitIcon = Icon("PortraitIcon", panel, Vector2.zero, Vector2.zero, Vector2.zero,
                new Vector2(26, 32), new Vector2(52, 52), icoShield, new Color(0.16f, 0.22f, 0.42f));
            playerName = Txt("Name", panel, Vector2.zero, Vector2.zero, Vector2.zero,
                new Vector2(116, 106), new Vector2(340, 40), "KNIGHT", fontSmall, 26, Color.white,
                TextAlignmentOptions.Left);
            hpFill = ProBar("HP", panel, new Vector2(116, 76), new Vector2(430, 36), bar4FillWhite,
                new Color(0.42f, 0.88f, 0.34f), out hpGhost);
            stFill = Bar("Stamina", panel, new Vector2(116, 36), new Vector2(360, 24),
                barBgBasic, barFillBasic, new Color(1f, 0.8f, 0.25f), out _);

            // -- class skill pip: gold when ready, radial shade sweeps off as it recharges
            var skillPlate = Img("SkillPip", panel, Vector2.zero, Vector2.zero, Vector2.zero,
                new Vector2(492, 8), new Vector2(60, 60), frameCircle, new Color(0.08f, 0.09f, 0.16f, 0.92f));
            skillPlate.type = Image.Type.Simple;
            skillPipRect = skillPlate.rectTransform;
            skillPipIcon = Icon("SkillIco", skillPlate.transform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                new Vector2(0.5f, 0.5f), new Vector2(0, 1), new Vector2(32, 32), icoSkill, Gold);
            skillPipCover = Img("SkillCd", skillPlate.transform, Vector2.zero, Vector2.one,
                new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero, frameCircle,
                new Color(0.03f, 0.03f, 0.07f, 0.72f));
            skillPipCover.type = Image.Type.Filled;
            skillPipCover.fillMethod = Image.FillMethod.Radial360;
            skillPipCover.fillOrigin = (int)Image.Origin360.Top;
            skillPipCover.fillClockwise = false;
            skillPipCover.fillAmount = 0f;
            Txt("SkillKey", skillPlate.transform, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f),
                new Vector2(0.5f, 1f), new Vector2(0, 2), new Vector2(40, 20), "E", fontSmall, 15,
                new Color(1f, 1f, 1f, 0.75f));

            // -- ally rows with mini class icons
            for (int i = 0; i < 2; i++)
            {
                var row = Rect("Ally" + i, fight, Vector2.zero, Vector2.zero, Vector2.zero,
                    new Vector2(28, 198 + i * 58), new Vector2(300, 52));
                var icon = Icon("AllyIcon", row, Vector2.zero, Vector2.zero, Vector2.zero,
                    new Vector2(0, 22), new Vector2(24, 24), icoShield, new Color(0.8f, 0.9f, 1f));
                var label = Txt("AllyName", row, Vector2.zero, Vector2.zero, Vector2.zero,
                    new Vector2(30, 24), new Vector2(270, 28), "Ally", fontSmall, 18, new Color(0.8f, 0.9f, 1f),
                    TextAlignmentOptions.Left);
                var fill = Bar("AllyHp", row, new Vector2(0, 10), new Vector2(240, 18),
                    barBgBasic, barFillBasic, AzureCol, out _);
                allyRows.Add(new AllyRow { fill = fill, label = label, icon = icon });
            }

            // -- target frame: the enemy you are locked onto or last damaged
            var tf = Img("TargetFrame", fight, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0.5f, 1f),
                new Vector2(0, -162), new Vector2(520, 88), frameRound, new Color(0.08f, 0.09f, 0.16f, 0.92f));
            targetFrame = tf.gameObject;
            var tCircle = Img("TCircle", tf.transform, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f),
                new Vector2(0.5f, 0.5f), new Vector2(46, 0), new Vector2(62, 62), frameCircle,
                new Color(0.32f, 0.12f, 0.12f, 0.98f));
            tCircle.type = Image.Type.Simple;
            targetIcon = Icon("TIcon", tCircle.transform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                new Vector2(0.5f, 0.5f), new Vector2(0, 1), new Vector2(34, 34), icoSword,
                new Color(1f, 0.92f, 0.85f));
            targetName = Txt("TName", tf.transform, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0.5f, 1f),
                new Vector2(24, -8), new Vector2(400, 32), "Vex  ·  Knight", fontSmall, 23, Color.white);
            var tBarBg = Img("TBarBg", tf.transform, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0.5f, 0f),
                new Vector2(24, 10), new Vector2(410, 26), bar4Bg, new Color(0.07f, 0.06f, 0.1f, 0.95f));
            targetGhost = MakeFill(tBarBg.rectTransform, bar4FillWhite, new Color(1f, 0.88f, 0.75f, 0.95f),
                new Vector2(410, 26));
            targetFill = MakeFill(tBarBg.rectTransform, bar4FillRed, Color.white, new Vector2(410, 26));
            targetFrame.SetActive(false);

            // -- kill feed
            feedContainer = Rect("KillFeed", fight, Vector2.one, Vector2.one, Vector2.one,
                new Vector2(-20, -110), new Vector2(430, 400));

            // -- announcement
            announceText = Txt("Announce", fight, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                new Vector2(0.5f, 0.5f), new Vector2(0, 130), new Vector2(1400, 220), "", fontBig, 130, Gold);
            announceText.gameObject.SetActive(false);

            // -- lock-on marker
            var marker = Icon("LockOn", fight, Vector2.zero, Vector2.zero, new Vector2(0.5f, 0.5f),
                Vector2.zero, new Vector2(64, 64), icoTarget, Gold);
            lockOnMarker = marker.rectTransform;
            lockOnMarker.gameObject.SetActive(false);

            // -- damage flash
            damageFlash = Img("DamageFlash", fight, Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f),
                Vector2.zero, Vector2.zero, null, new Color(0.8f, 0.05f, 0.05f, 0f));

            // -- autopilot tag
            var auto = Img("Autopilot", fight, new Vector2(1f, 0f), new Vector2(1f, 0f), new Vector2(1f, 0f),
                new Vector2(-24, 20), new Vector2(356, 42), plateRound, PlateDark);
            Icon("AutoIco", auto.transform, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(0f, 0.5f),
                new Vector2(14, 0), new Vector2(22, 22), icoPlay, Gold);
            Txt("AutoTxt", auto.transform, Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f),
                new Vector2(0, 1), new Vector2(-52, -8), "AUTOPILOT ON  [F1]", fontSmall, 20, Gold,
                TextAlignmentOptions.Right);
            autopilotTag = auto.gameObject;
            autopilotTag.SetActive(false);

            // entrance: banner drops in, player panel slides up from the edge
            fightScreen.OnShow = () =>
            {
                UiTween.SlideIn(scoreBannerRect, new Vector2(0, 130f), 0.42f, UiTween.Ease.BackOut);
                UiTween.SlideIn(playerPanelRect, new Vector2(0, -220f), 0.42f, UiTween.Ease.CubicOut, 0.08f);
            };
        }

        void BindPlayer()
        {
            var pm = MatchManager.I?.PlayerMotor;
            if (pm == null) return;
            playerName.text = pm.Kit.displayName.ToUpper();
            portraitIcon.sprite = IconFor(pm.Kit.id);
            hpShown = ghostShown = stShown = 1f;
            pm.Health.Damaged -= OnPlayerDamaged;
            pm.Health.Damaged += OnPlayerDamaged;

            allyRows.ForEach(r => r.motor = null);
            int i = 0;
            // allies are the player's actual team (online they may be Crimson)
            Team myTeam = pm.Identity != null ? pm.Identity.team : Team.Azure;
            foreach (var m in FindObjectsByType<CombatMotor>(FindObjectsSortMode.InstanceID))
            {
                if (m == pm || m.Identity == null || m.Identity.team != myTeam || m.Identity.isPlayer) continue;
                if (i >= allyRows.Count) break;
                allyRows[i].motor = m;
                allyRows[i].label.text = $"{m.Identity.displayName}  ·  {m.Kit.displayName}";
                allyRows[i].icon.sprite = IconFor(m.Kit.id);
                i++;
            }
        }

        void OnPlayerDamaged(HitInfo hit, HitResult res)
        {
            if (res.damageDealt > 0.5f && !res.blocked)
                damageFlash.color = new Color(0.8f, 0.05f, 0.05f, 0.28f);
        }

        void OnKill(CombatantIdentity killer, CombatantIdentity victim)
        {
            string k = killer != null ? killer.displayName : "The Arena";
            Color kc = killer != null ? killer.TeamColor : Color.gray;
            var plate = Img("Feed", feedContainer, new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(1f, 1f),
                Vector2.zero, new Vector2(430, 38), plateRound, PlateDark);
            Icon("Skull", plate.transform, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(0f, 0.5f),
                new Vector2(12, 0), new Vector2(22, 22), icoSkull, new Color(1f, 1f, 1f, 0.85f));
            var entryText = Txt("T", plate.transform, Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f),
                new Vector2(-6, 1), new Vector2(-58, -6),
                $"<color=#{ColorUtility.ToHtmlStringRGB(kc)}>{k}</color>  >  <color=#{ColorUtility.ToHtmlStringRGB(victim.TeamColor)}>{victim.displayName}</color>",
                fontSmall, 21, Color.white, TextAlignmentOptions.Right);
            entryText.ForceMeshUpdate();
            plate.rectTransform.sizeDelta = new Vector2(entryText.preferredWidth + 70f, 38);
            feedEntries.Add((plate.rectTransform, Time.unscaledTime + 5f));
            if (feedEntries.Count > 6)
            {
                Destroy(feedEntries[0].rt.gameObject);
                feedEntries.RemoveAt(0);
            }
            Relayout();
            UiTween.SlideIn(plate.rectTransform, new Vector2(140f, 0f), 0.28f, UiTween.Ease.CubicOut);
        }

        void Relayout()
        {
            for (int i = 0; i < feedEntries.Count; i++)
            {
                int fromTop = feedEntries.Count - 1 - i;
                feedEntries[i].rt.anchoredPosition = new Vector2(0, -fromTop * 42);
            }
        }

        void Pop(string msg, Color color, float scale)
        {
            if (announceRoutine != null) StopCoroutine(announceRoutine);
            announceRoutine = StartCoroutine(PopRoutine(msg, color, scale));
        }

        IEnumerator PopRoutine(string msg, Color color, float scale)
        {
            announceText.gameObject.SetActive(true);
            announceText.text = msg;

            // countdown digits live just under a second so each fades out before
            // the next fades in; announcements linger longer
            float life = msg.Length <= 2 ? 0.92f : 1.6f;
            float fadeIn = 0.16f;
            float fadeOut = 0.32f;

            float t = 0f;
            while (t < life)
            {
                t += Time.unscaledDeltaTime;
                float s = Mathf.Lerp(0.6f, scale, 1f - Mathf.Pow(1f - Mathf.Clamp01(t / 0.24f), 3f));
                s += 0.05f * Mathf.Clamp01(t / life); // slow drift upward
                announceText.rectTransform.localScale = Vector3.one * s;

                float alpha = Mathf.Clamp01(t / fadeIn);
                if (t > life - fadeOut) alpha = Mathf.Clamp01((life - t) / fadeOut);
                announceText.color = new Color(color.r, color.g, color.b, alpha);
                yield return null;
            }
            announceText.gameObject.SetActive(false);
        }

        /// Per-frame fight HUD polling, called from the core Update.
        void TickFight(MatchManager mm)
        {
            // timer
            float tl = Mathf.Max(0f, mm.TimeLeft);
            timerText.text = mm.SuddenDeath ? "SUDDEN DEATH" : $"{(int)tl / 60}:{(int)tl % 60:00}";

            // bars
            var pm = mm.PlayerMotor;
            if (pm != null)
            {
                float hp = pm.Health.Max > 0 ? pm.Health.Current / pm.Health.Max : 0f;
                float st = pm.Stamina.Fraction;
                hpShown = Mathf.Lerp(hpShown, hp, 14f * Time.unscaledDeltaTime);
                ghostShown = Mathf.Lerp(ghostShown, hp, 3.2f * Time.unscaledDeltaTime);
                stShown = Mathf.Lerp(stShown, st, 14f * Time.unscaledDeltaTime);
                hpFill.fillAmount = hpShown;
                hpGhost.fillAmount = Mathf.Max(ghostShown, hpShown);
                stFill.fillAmount = stShown;

                bool skillUp = pm.SkillReady;
                skillPipCover.fillAmount = 1f - pm.SkillReadiness;
                skillPipIcon.color = skillUp ? Gold : new Color(0.6f, 0.6f, 0.68f);
                if (skillUp != skillWasReady)
                {
                    skillWasReady = skillUp;
                    if (skillUp)
                    {
                        UiTween.PulseForever(skillPipRect, 0.98f, 1.1f, 0.9f);
                        Burst(fxSparklePrefab, skillPipRect, Vector2.zero, 0.6f);
                    }
                    else UiTween.StopLoop(skillPipRect);
                }
            }

            foreach (var row in allyRows)
            {
                bool has = row.motor != null;
                row.fill.transform.parent.parent.gameObject.SetActive(has);
                if (has)
                {
                    float f = row.motor.Health.Max > 0 ? row.motor.Health.Current / row.motor.Health.Max : 0f;
                    row.fill.fillAmount = Mathf.Lerp(row.fill.fillAmount, f, 12f * Time.unscaledDeltaTime);
                    row.fill.color = row.motor.IsDead ? new Color(0.4f, 0.4f, 0.45f) : AzureCol;
                }
            }

            // damage flash decay
            if (damageFlash.color.a > 0.001f)
            {
                var c = damageFlash.color;
                c.a = Mathf.MoveTowards(c.a, 0f, 0.6f * Time.unscaledDeltaTime);
                damageFlash.color = c;
            }

            // feed expiry
            for (int i = feedEntries.Count - 1; i >= 0; i--)
            {
                if (Time.unscaledTime > feedEntries[i].dieAt)
                {
                    Destroy(feedEntries[i].rt.gameObject);
                    feedEntries.RemoveAt(i);
                    Relayout();
                }
            }

            autopilotTag.SetActive(mm.Autopilot);

            // target frame: locked enemy, else last-damaged enemy for 6s
            CombatMotor frameTarget = null;
            if (pm != null && mm.State == MatchState.Fighting)
            {
                if (pm.LockTarget != null && !pm.LockTarget.IsDead) frameTarget = pm.LockTarget;
                else if (pm.LastEngagedEnemy != null && !pm.LastEngagedEnemy.IsDead &&
                         Time.time - pm.LastEngagedAt < 6f) frameTarget = pm.LastEngagedEnemy;
            }
            if (frameTarget != shownTarget)
            {
                shownTarget = frameTarget;
                if (shownTarget != null)
                {
                    targetName.text = $"{shownTarget.Identity.displayName}  ·  {shownTarget.Kit.displayName}";
                    targetIcon.sprite = IconFor(shownTarget.Kit.id);
                    float f0 = shownTarget.Health.Max > 0 ? shownTarget.Health.Current / shownTarget.Health.Max : 0f;
                    targetShown = targetGhostShown = f0;
                    UiTween.PopIn((RectTransform)targetFrame.transform, 0.26f);
                }
            }
            targetFrame.SetActive(shownTarget != null);
            if (shownTarget != null)
            {
                float tfrac = shownTarget.Health.Max > 0 ? shownTarget.Health.Current / shownTarget.Health.Max : 0f;
                targetShown = Mathf.Lerp(targetShown, tfrac, 16f * Time.unscaledDeltaTime);
                targetGhostShown = Mathf.Lerp(targetGhostShown, tfrac, 4f * Time.unscaledDeltaTime);
                targetFill.fillAmount = targetShown;
                targetGhost.fillAmount = Mathf.Max(targetGhostShown, targetShown);
            }

            // lock-on marker
            var target = pm != null ? pm.LockTarget : null;
            var cam = Camera.main;
            if (target != null && !target.IsDead && cam != null)
            {
                Vector3 sp = cam.WorldToScreenPoint(target.AimPoint + Vector3.up * 0.4f);
                if (sp.z > 0f)
                {
                    lockOnMarker.gameObject.SetActive(true);
                    lockOnMarker.position = sp;
                    float pulse = 1f + Mathf.Sin(Time.unscaledTime * 7f) * 0.08f;
                    lockOnMarker.localScale = Vector3.one * pulse;
                }
                else lockOnMarker.gameObject.SetActive(false);
            }
            else lockOnMarker.gameObject.SetActive(false);
        }
    }
}
