using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

namespace Crownfall
{
    /// Builds the whole HUD in code at startup from Layer Lab sprites + LilitaOne
    /// fonts (wired by the forge), then binds to match events.
    public class HUDController : MonoBehaviour
    {
        [Header("Wired by forge")]
        public TMP_FontAsset fontBig;
        public TMP_FontAsset fontMid;
        public TMP_FontAsset fontSmall;
        public Sprite barBgTrapezoid;
        public Sprite barFillTrapezoid;
        public Sprite barBgBasic;
        public Sprite barFillBasic;
        public Sprite frameRound;
        public Sprite frameCircle;
        public Sprite bannerNavy;

        static readonly Color Gold = new Color(1f, 0.85f, 0.35f);
        static readonly Color AzureCol = new Color(0.35f, 0.65f, 1f);
        static readonly Color CrimsonCol = new Color(1f, 0.36f, 0.3f);

        Canvas canvas;
        RectTransform root;

        GameObject classPanel;
        GameObject resultPanel;
        TMP_Text resultTitle, resultSub;
        TMP_Text scoreAzureText, scoreCrimsonText, timerText;
        TMP_Text announceText;
        TMP_Text playerName;
        TMP_Text portraitLetter;
        Image hpFill, hpGhost, stFill;
        Image damageFlash;
        RectTransform lockOnMarker;
        RectTransform feedContainer;
        TMP_Text autopilotText;

        readonly List<(RectTransform rt, float dieAt)> feedEntries = new List<(RectTransform, float)>();

        class AllyRow { public CombatMotor motor; public Image fill; public TMP_Text label; }
        readonly List<AllyRow> allyRows = new List<AllyRow>();

        Coroutine announceRoutine;
        float hpShown = 1f, ghostShown = 1f, stShown = 1f;

        void Start()
        {
            BuildCanvas();
            BuildFightHud();
            BuildClassSelect();
            BuildResultPanel();

            var mm = MatchManager.I;
            if (mm != null)
            {
                mm.StateChanged += OnStateChanged;
                mm.ScoreChanged += (a, c) => { scoreAzureText.text = a.ToString(); scoreCrimsonText.text = c.ToString(); };
                mm.CountdownTick += n => Pop(n > 0 ? n.ToString() : "FIGHT!", n > 0 ? Color.white : Gold, n > 0 ? 0.9f : 1.2f);
                mm.Announce += msg => Pop(msg, Gold, 1.6f);
                mm.KillFeed += OnKill;
                mm.MatchEndedEvent += OnEnded;
                OnStateChanged(mm.State);
            }
        }

        // ================================================================== build

        void BuildCanvas()
        {
            var go = new GameObject("HUD Canvas", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            go.transform.SetParent(transform, false);
            canvas = go.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 10;
            var scaler = go.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920, 1080);
            scaler.matchWidthOrHeight = 0.5f;
            root = go.GetComponent<RectTransform>();
        }

        RectTransform Rect(string name, Transform parent, Vector2 anchorMin, Vector2 anchorMax,
            Vector2 pivot, Vector2 anchoredPos, Vector2 size)
        {
            var go = new GameObject(name, typeof(RectTransform));
            var rt = go.GetComponent<RectTransform>();
            rt.SetParent(parent, false);
            rt.anchorMin = anchorMin;
            rt.anchorMax = anchorMax;
            rt.pivot = pivot;
            rt.anchoredPosition = anchoredPos;
            rt.sizeDelta = size;
            return rt;
        }

        Image Img(string name, Transform parent, Vector2 anchorMin, Vector2 anchorMax, Vector2 pivot,
            Vector2 pos, Vector2 size, Sprite sprite, Color color, bool raycast = false)
        {
            var rt = Rect(name, parent, anchorMin, anchorMax, pivot, pos, size);
            var img = rt.gameObject.AddComponent<Image>();
            img.sprite = sprite;
            img.color = color;
            img.raycastTarget = raycast;
            if (sprite != null) img.type = Image.Type.Sliced;
            return img;
        }

        TMP_Text Txt(string name, Transform parent, Vector2 anchorMin, Vector2 anchorMax, Vector2 pivot,
            Vector2 pos, Vector2 size, string text, TMP_FontAsset font, float fontSize, Color color,
            TextAlignmentOptions align = TextAlignmentOptions.Center)
        {
            var rt = Rect(name, parent, anchorMin, anchorMax, pivot, pos, size);
            var t = rt.gameObject.AddComponent<TextMeshProUGUI>();
            if (font != null) t.font = font;
            t.text = text;
            t.fontSize = fontSize;
            t.color = color;
            t.alignment = align;
            t.raycastTarget = false;
            return t;
        }

        Image Bar(string name, Transform parent, Vector2 pos, Vector2 size, Sprite bg, Sprite fill,
            Color fillColor, out Image ghost, Transform anchorParent = null)
        {
            var p = anchorParent != null ? anchorParent : parent;
            var bgImg = Img(name + "Bg", p, Vector2.zero, Vector2.zero, new Vector2(0, 0.5f), pos, size,
                bg, new Color(0.08f, 0.07f, 0.1f, 0.92f));
            ghost = MakeFill(bgImg.rectTransform, fill, new Color(1f, 0.9f, 0.8f, 0.85f), size);
            var f = MakeFill(bgImg.rectTransform, fill, fillColor, size);
            return f;
        }

        Image MakeFill(RectTransform bg, Sprite sprite, Color color, Vector2 size)
        {
            var img = Img("Fill", bg, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                Vector2.zero, size - new Vector2(6, 6), sprite, color);
            img.type = Image.Type.Filled;
            img.fillMethod = Image.FillMethod.Horizontal;
            img.fillOrigin = 0;
            img.fillAmount = 1f;
            return img;
        }

        void BuildFightHud()
        {
            // -- score banner
            var banner = Img("ScoreBanner", root, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0.5f, 1f),
                new Vector2(0, -8), new Vector2(430, 92), bannerNavy, new Color(1f, 1f, 1f, 0.96f));
            Txt("AzLabel", banner.transform, Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f),
                new Vector2(-150, 6), new Vector2(110, 60), "AZURE", fontSmall, 20, AzureCol);
            scoreAzureText = Txt("AzScore", banner.transform, Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f),
                new Vector2(-72, 2), new Vector2(80, 70), "0", fontMid, 44, Color.white);
            Txt("Dash", banner.transform, Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f),
                new Vector2(0, 14), new Vector2(60, 44), "—", fontMid, 26, Gold);
            scoreCrimsonText = Txt("CrScore", banner.transform, Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f),
                new Vector2(72, 2), new Vector2(80, 70), "0", fontMid, 44, Color.white);
            Txt("CrLabel", banner.transform, Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f),
                new Vector2(150, 6), new Vector2(120, 60), "CRIMSON", fontSmall, 20, CrimsonCol);
            timerText = Txt("Timer", banner.transform, Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f),
                new Vector2(0, -26), new Vector2(140, 40), "5:00", fontSmall, 24, new Color(1f, 1f, 1f, 0.9f));

            // -- player panel
            var panel = Rect("PlayerPanel", root, Vector2.zero, Vector2.zero, Vector2.zero,
                new Vector2(28, 26), new Vector2(560, 150));
            var portrait = Img("Portrait", panel, Vector2.zero, Vector2.zero, Vector2.zero,
                new Vector2(0, 8), new Vector2(96, 96), frameCircle, new Color(0.16f, 0.2f, 0.3f, 0.95f));
            portraitLetter = Txt("Letter", portrait.transform, Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f),
                new Vector2(0, 2), new Vector2(90, 90), "K", fontMid, 46, Gold);
            playerName = Txt("Name", panel, Vector2.zero, Vector2.zero, Vector2.zero,
                new Vector2(112, 104), new Vector2(340, 40), "KNIGHT", fontSmall, 26, Color.white,
                TextAlignmentOptions.Left);
            hpFill = Bar("HP", panel, new Vector2(112, 74), new Vector2(430, 36),
                barBgTrapezoid, barFillTrapezoid, new Color(0.92f, 0.18f, 0.2f), out hpGhost);
            stFill = Bar("Stamina", panel, new Vector2(112, 34), new Vector2(360, 24),
                barBgBasic, barFillBasic, new Color(0.35f, 0.8f, 0.35f), out _);
            stFill.color = new Color(1f, 0.8f, 0.25f);

            // -- ally rows
            for (int i = 0; i < 2; i++)
            {
                var row = Rect("Ally" + i, root, Vector2.zero, Vector2.zero, Vector2.zero,
                    new Vector2(28, 196 + i * 58), new Vector2(280, 52));
                var label = Txt("AllyName", row, Vector2.zero, Vector2.zero, Vector2.zero,
                    new Vector2(0, 26), new Vector2(280, 28), "Ally", fontSmall, 18, new Color(0.8f, 0.9f, 1f),
                    TextAlignmentOptions.Left);
                var fill = Bar("AllyHp", row, new Vector2(0, 12), new Vector2(240, 18),
                    barBgBasic, barFillBasic, AzureCol, out _);
                allyRows.Add(new AllyRow { fill = fill, label = label });
            }

            // -- kill feed
            feedContainer = Rect("KillFeed", root, Vector2.one, Vector2.one, Vector2.one,
                new Vector2(-20, -110), new Vector2(430, 400));

            // -- announcement
            announceText = Txt("Announce", root, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                new Vector2(0.5f, 0.5f), new Vector2(0, 130), new Vector2(1400, 220), "", fontBig, 130, Gold);
            announceText.gameObject.SetActive(false);

            // -- lock-on marker
            var marker = Img("LockOn", root, Vector2.zero, Vector2.zero, new Vector2(0.5f, 0.5f),
                Vector2.zero, new Vector2(56, 56), frameCircle, Gold);
            marker.type = Image.Type.Simple;
            lockOnMarker = marker.rectTransform;
            lockOnMarker.gameObject.SetActive(false);

            // -- damage flash
            damageFlash = Img("DamageFlash", root, Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f),
                Vector2.zero, Vector2.zero, null, new Color(0.8f, 0.05f, 0.05f, 0f));

            // -- autopilot tag
            autopilotText = Txt("Autopilot", root, new Vector2(1f, 0f), new Vector2(1f, 0f), new Vector2(1f, 0f),
                new Vector2(-24, 20), new Vector2(360, 34), "AUTOPILOT ON  [F1]", fontSmall, 20, Gold,
                TextAlignmentOptions.Right);
            autopilotText.gameObject.SetActive(false);
        }

        void BuildClassSelect()
        {
            classPanel = Rect("ClassSelect", root, Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f),
                Vector2.zero, Vector2.zero).gameObject;
            Img("Dim", classPanel.transform, Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f),
                Vector2.zero, Vector2.zero, null, new Color(0.03f, 0.03f, 0.06f, 0.82f));

            Txt("Title", classPanel.transform, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0.5f, 1f),
                new Vector2(0, -110), new Vector2(1500, 160), "CROWNFALL ARENA", fontBig, 110, Gold);
            Txt("Sub", classPanel.transform, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0.5f, 1f),
                new Vector2(0, -258), new Vector2(1200, 60), "3 v 3  ·  choose your champion", fontMid, 34,
                new Color(0.85f, 0.85f, 0.95f));

            string[] names = { "KNIGHT", "WARBRAND", "DUELIST", "MAGE" };
            for (int i = 0; i < 4; i++)
            {
                int idx = i;
                var kit = ClassKits.Get((ClassId)i);
                var btnImg = Img("Class" + i, classPanel.transform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                    new Vector2(0.5f, 0.5f), new Vector2(-525 + i * 350, -40), new Vector2(320, 330),
                    frameRound, new Color(0.13f, 0.16f, 0.25f, 0.97f), true);
                var btn = btnImg.gameObject.AddComponent<Button>();
                var colors = btn.colors;
                colors.highlightedColor = new Color(1.25f, 1.25f, 1.4f);
                colors.pressedColor = new Color(0.8f, 0.8f, 0.9f);
                btn.colors = colors;
                btn.onClick.AddListener(() => MatchManager.I?.SelectClass(idx));

                Txt("N", btnImg.transform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                    new Vector2(0, 106), new Vector2(300, 70), names[i], fontMid, 38, Gold);
                var blurb = Txt("B", btnImg.transform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                    new Vector2(0, -6), new Vector2(268, 170), kit.blurb, fontSmall, 22,
                    new Color(0.88f, 0.88f, 0.95f));
                blurb.enableWordWrapping = true;
                Txt("S", btnImg.transform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                    new Vector2(0, -126), new Vector2(280, 50),
                    $"HP {kit.maxHealth:0}  ·  DMG {kit.lightDamage:0}", fontSmall, 19,
                    new Color(0.65f, 0.75f, 0.85f));
            }

            Txt("Controls", classPanel.transform, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0.5f, 0f),
                new Vector2(0, 40), new Vector2(1700, 60),
                "WASD move  ·  LMB attack (hold = heavy)  ·  RMB block / heavy  ·  SPACE roll  ·  SHIFT sprint  ·  Q lock-on  ·  F1 autopilot",
                fontSmall, 21, new Color(0.75f, 0.78f, 0.85f));
        }

        void BuildResultPanel()
        {
            resultPanel = Rect("Result", root, Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f),
                Vector2.zero, Vector2.zero).gameObject;
            Img("Dim", resultPanel.transform, Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f),
                Vector2.zero, Vector2.zero, null, new Color(0.02f, 0.02f, 0.05f, 0.72f));
            resultTitle = Txt("Title", resultPanel.transform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                new Vector2(0.5f, 0.5f), new Vector2(0, 120), new Vector2(1400, 220), "VICTORY", fontBig, 150, Gold);
            resultSub = Txt("Sub", resultPanel.transform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                new Vector2(0.5f, 0.5f), new Vector2(0, -20), new Vector2(900, 70), "", fontMid, 36, Color.white);

            var btnImg = Img("Rematch", resultPanel.transform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                new Vector2(0.5f, 0.5f), new Vector2(0, -140), new Vector2(340, 92), frameRound,
                new Color(0.15f, 0.2f, 0.32f, 0.98f), true);
            btnImg.gameObject.AddComponent<Button>().onClick.AddListener(() => MatchManager.I?.Restart());
            Txt("L", btnImg.transform, Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f),
                Vector2.zero, new Vector2(320, 80), "REMATCH  [R]", fontMid, 34, Gold);
            resultPanel.SetActive(false);
        }

        // ================================================================== events

        void OnStateChanged(MatchState s)
        {
            classPanel.SetActive(s == MatchState.ClassSelect);
            if (s == MatchState.Countdown || s == MatchState.Fighting) BindPlayer();
        }

        void BindPlayer()
        {
            var pm = MatchManager.I?.PlayerMotor;
            if (pm == null) return;
            playerName.text = pm.Kit.displayName.ToUpper();
            portraitLetter.text = playerName.text.Substring(0, 1);
            hpShown = ghostShown = stShown = 1f;
            pm.Health.Damaged -= OnPlayerDamaged;
            pm.Health.Damaged += OnPlayerDamaged;

            allyRows.ForEach(r => r.motor = null);
            int i = 0;
            foreach (var m in FindObjectsByType<CombatMotor>(FindObjectsSortMode.InstanceID))
            {
                if (m == pm || m.Identity == null || m.Identity.team != Team.Azure || m.Identity.isPlayer) continue;
                if (i >= allyRows.Count) break;
                allyRows[i].motor = m;
                allyRows[i].label.text = $"{m.Identity.displayName}  ·  {m.Kit.displayName}";
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
            var entry = Txt("Feed", feedContainer,
                new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(1f, 1f),
                new Vector2(0, 0), new Vector2(430, 34),
                $"<color=#{ColorUtility.ToHtmlStringRGB(kc)}>{k}</color>  ⚔  <color=#{ColorUtility.ToHtmlStringRGB(victim.TeamColor)}>{victim.displayName}</color>",
                fontSmall, 22, Color.white, TextAlignmentOptions.Right);
            feedEntries.Add((entry.rectTransform, Time.unscaledTime + 5f));
            if (feedEntries.Count > 6)
            {
                Destroy(feedEntries[0].rt.gameObject);
                feedEntries.RemoveAt(0);
            }
            Relayout();
        }

        void Relayout()
        {
            for (int i = 0; i < feedEntries.Count; i++)
            {
                int fromTop = feedEntries.Count - 1 - i;
                feedEntries[i].rt.anchoredPosition = new Vector2(0, -fromTop * 38);
            }
        }

        void OnEnded(Team winner)
        {
            bool won = winner == Team.Azure;
            resultPanel.SetActive(true);
            resultTitle.text = won ? "VICTORY" : "DEFEAT";
            resultTitle.color = won ? Gold : new Color(0.85f, 0.3f, 0.3f);
            resultSub.text = $"Azure {MatchManager.I.ScoreAzure}  —  {MatchManager.I.ScoreCrimson} Crimson";
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
            announceText.color = color;
            float t = 0f;
            while (t < 1.4f)
            {
                t += Time.unscaledDeltaTime;
                float s = Mathf.Lerp(0.55f, scale, 1f - Mathf.Pow(1f - Mathf.Clamp01(t / 0.22f), 3f));
                announceText.rectTransform.localScale = Vector3.one * s;
                float alpha = t < 1f ? 1f : 1f - (t - 1f) / 0.4f;
                announceText.color = new Color(color.r, color.g, color.b, alpha);
                yield return null;
            }
            announceText.gameObject.SetActive(false);
        }

        // ================================================================== frame

        void Update()
        {
            var mm = MatchManager.I;
            if (mm == null) return;

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

            autopilotText.gameObject.SetActive(mm.Autopilot);

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
