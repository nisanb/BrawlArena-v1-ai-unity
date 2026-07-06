using System.Collections;
using System.Collections.Generic;
using System.IO;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace BrawlArena
{
    /// <summary>
    /// Main menu scene driver: title screen -> game mode select -> character
    /// select with a live 3D character on the podium, then loads the Arena
    /// with the picks stored in MatchSetup. All UI is built in code from the
    /// GUI Pro theme wired by MenuSceneBuilder. With Automation/autopilot.flag
    /// present, the flow advances by itself so unattended tests can reach the
    /// Arena (flag content "gemgrab" selects that mode).
    /// </summary>
    public class MainMenuFlow : MonoBehaviour
    {
        public BrawlerDefinition[] roster;
        [Tooltip("Preview characters spawn under this pivot; it rotates.")]
        public Transform podium;

        public static string DebugPhase = "none";

        UiTheme theme;
        Canvas canvas;
        GameObject mainPanel;
        GameObject modePanel;
        GameObject charPanel;
        GameObject shopPanel;
        TextMeshProUGUI menuCoinsText;
        TextMeshProUGUI shopCoinsText;
        readonly List<System.Action> shopRefreshers = new List<System.Action>();

        // Character select widgets
        TextMeshProUGUI charName;
        TextMeshProUGUI charRole;
        TextMeshProUGUI charDescription;
        TextMeshProUGUI charKind;
        TextMeshProUGUI charLevel;
        Image[] statFills = new Image[3];
        GameObject previewInstance;
        int charIndex;
        float lastSpinInputAt;
        bool launching;

        static bool AutopilotRequested =>
            Application.isEditor &&
            File.Exists(Path.Combine(Directory.GetParent(Application.dataPath).FullName, "Automation", "autopilot.flag"));

        void Awake()
        {
            theme = FindFirstObjectByType<UiTheme>();
        }

        void Start()
        {
            MatchSetup.CharacterIndex = -1;
            MatchSetup.FromMenu = false;
            EnsureEventSystem();
            BuildUi();
            ShowPanel(mainPanel);
            SetPreviewVisible(false);
            DebugPhase = "main";
            if (AutopilotRequested) StartCoroutine(Autopilot());
        }

        void EnsureEventSystem()
        {
            if (FindFirstObjectByType<EventSystem>() != null) return;
            new GameObject("EventSystem", typeof(EventSystem), typeof(InputSystemUIInputModule));
        }

        void Update()
        {
            // Idle spin for the podium character after manual input settles.
            if (podium != null && previewInstance != null && Time.time - lastSpinInputAt > 2.5f)
                podium.Rotate(0f, 14f * Time.deltaTime, 0f);
        }

        public void RotatePodium(float degrees)
        {
            if (podium == null) return;
            podium.Rotate(0f, degrees, 0f);
            lastSpinInputAt = Time.time;
        }

        IEnumerator Autopilot()
        {
            string flag = Path.Combine(Directory.GetParent(Application.dataPath).FullName, "Automation", "autopilot.flag");
            bool gemGrab = false;
            try { gemGrab = File.ReadAllText(flag).Contains("gemgrab"); } catch { }

            // Detour through the shop so unattended runs exercise/screenshot it.
            yield return new WaitForSeconds(1.4f);
            OnShopPressed();
            yield return new WaitForSeconds(2f);
            OnBackToMain();
            yield return new WaitForSeconds(0.6f);
            OnPlayPressed();
            yield return new WaitForSeconds(1.6f);
            OnModePicked(gemGrab ? GameMode.GemGrab : GameMode.Knockout);
            yield return new WaitForSeconds(1.2f);
            int steps = Random.Range(0, roster.Length);
            for (int i = 0; i < steps; i++)
            {
                StepCharacter(1);
                yield return new WaitForSeconds(0.7f);
            }
            yield return new WaitForSeconds(1f);
            OnBattlePressed();
        }

        // ---------------- flow ----------------

        void ShowPanel(GameObject panel)
        {
            mainPanel.SetActive(panel == mainPanel);
            modePanel.SetActive(panel == modePanel);
            charPanel.SetActive(panel == charPanel);
            if (shopPanel != null) shopPanel.SetActive(panel == shopPanel);
            if (panel == mainPanel || panel == shopPanel) RefreshShop();
        }

        void OnShopPressed()
        {
            ShowPanel(shopPanel);
            SetPreviewVisible(false);
            DebugPhase = "shop";
        }

        void RefreshShop()
        {
            string coins = Progress.Coins.ToString("N0");
            if (menuCoinsText != null) menuCoinsText.text = coins;
            if (shopCoinsText != null) shopCoinsText.text = coins;
            foreach (var refresh in shopRefreshers) refresh();
        }

        void OnPlayPressed()
        {
            ShowPanel(modePanel);
            DebugPhase = "mode";
        }

        void OnModePicked(GameMode mode)
        {
            MatchSetup.Mode = mode;
            ShowPanel(charPanel);
            SetPreviewVisible(true);
            SetCharacter(charIndex);
            DebugPhase = "character mode=" + mode;
        }

        void OnBackToMain()
        {
            ShowPanel(mainPanel);
            SetPreviewVisible(false);
            DebugPhase = "main";
        }

        void OnBackToMode()
        {
            ShowPanel(modePanel);
            SetPreviewVisible(false);
            DebugPhase = "mode";
        }

        void StepCharacter(int direction)
        {
            SetCharacter((charIndex + direction + roster.Length) % roster.Length);
        }

        void OnBattlePressed()
        {
            if (launching) return;
            launching = true;
            MatchSetup.CharacterIndex = charIndex;
            MatchSetup.FromMenu = true;
            DebugPhase = "launching char=" + charIndex + " mode=" + MatchSetup.Mode;

            // Victory flourish on the podium, then off to the arena.
            if (previewInstance != null)
            {
                var anim = previewInstance.GetComponentInChildren<Animator>();
                if (anim != null)
                    anim.CrossFadeInFixedTime("Victory_" + roster[charIndex].animSuffix, 0.15f);
            }
            StartCoroutine(LaunchAfter(1.1f));
        }

        IEnumerator LaunchAfter(float delay)
        {
            yield return new WaitForSeconds(delay);
            SceneManager.LoadScene("Arena");
        }

        // ---------------- 3D preview ----------------

        void SetPreviewVisible(bool visible)
        {
            if (podium != null) podium.gameObject.SetActive(visible);
            if (!visible && previewInstance != null)
            {
                Destroy(previewInstance);
                previewInstance = null;
            }
        }

        void SetCharacter(int index)
        {
            charIndex = index;
            var def = roster[index];

            if (previewInstance != null) Destroy(previewInstance);
            if (podium != null && def.prefab != null)
            {
                podium.rotation = Quaternion.identity;
                previewInstance = Instantiate(def.prefab, podium, false);
                previewInstance.transform.localPosition = Vector3.zero;
                previewInstance.transform.localRotation = Quaternion.identity;
                var anim = previewInstance.GetComponentInChildren<Animator>();
                if (anim != null)
                {
                    anim.applyRootMotion = false;
                    anim.CrossFadeInFixedTime("Idle_" + def.animSuffix, 0.05f);
                }
            }

            if (charName != null) charName.text = def.displayName.ToUpperInvariant();
            if (charRole != null) charRole.text = def.role.ToUpperInvariant();
            if (charDescription != null) charDescription.text = def.description;
            if (charKind != null) charKind.text = def.projectilePrefab != null ? "RANGED" : "MELEE";
            if (charLevel != null) charLevel.text = "LEVEL " + Progress.Get(def.id).level;
            SetStat(0, def.maxHealth / 160f);
            SetStat(1, def.damage / 30f);
            SetStat(2, (def.moveSpeed - 4f) / 1.6f);
        }

        void SetStat(int i, float value01)
        {
            if (statFills[i] == null) return;
            var rt = statFills[i].rectTransform;
            rt.anchorMax = new Vector2(Mathf.Clamp01(value01), 1f);
        }

        // ---------------- UI construction ----------------

        void BuildUi()
        {
            var canvasGo = new GameObject("MenuCanvas", typeof(RectTransform), typeof(Canvas),
                typeof(CanvasScaler), typeof(GraphicRaycaster));
            canvasGo.transform.SetParent(transform, false);
            canvas = canvasGo.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 10;
            var scaler = canvasGo.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            // GUI Pro components are authored for a 2560x1440 canvas.
            scaler.referenceResolution = new Vector2(2560f, 1440f);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.Expand;

            mainPanel = BuildMainPanel(canvasGo.transform);
            modePanel = BuildModePanel(canvasGo.transform);
            charPanel = BuildCharacterPanel(canvasGo.transform);
            shopPanel = BuildShopPanel(canvasGo.transform);
        }

        GameObject BuildMainPanel(Transform root)
        {
            var panel = NewRect("Main", root);
            Stretch((RectTransform)panel.transform);

            var title = MakeHeading(panel.transform, "BRAWL ARENA", 190f, new Color(1f, 0.85f, 0.25f));
            var trt = title.rectTransform;
            trt.anchorMin = trt.anchorMax = new Vector2(0.5f, 0.78f);
            trt.sizeDelta = new Vector2(2000f, 260f);

            var subtitle = MakeBody(panel.transform, "3v3  MAYHEM", 56f, new Color(1f, 1f, 1f, 0.9f));
            var srt = subtitle.rectTransform;
            srt.anchorMin = srt.anchorMax = new Vector2(0.5f, 0.68f);
            srt.sizeDelta = new Vector2(1200f, 90f);

            MakeButton(panel.transform, "PLAY", theme != null ? theme.buttonYellow : null,
                new Vector2(0.5f, 0.24f), new Vector2(620f, 225f), 96f, OnPlayPressed);
            MakeButton(panel.transform, "SHOP", theme != null ? theme.buttonBlue : null,
                new Vector2(0.5f, 0.09f), new Vector2(420f, 150f), 60f, OnShopPressed);

            menuCoinsText = AddCoinsCapsule(panel.transform);

            var version = MakeBody(panel.transform, "v0.3 dev", 34f, new Color(1f, 1f, 1f, 0.45f));
            var vrt = version.rectTransform;
            vrt.anchorMin = vrt.anchorMax = new Vector2(0.97f, 0.03f);
            vrt.pivot = new Vector2(1f, 0f);
            vrt.sizeDelta = new Vector2(300f, 50f);
            version.alignment = TextAlignmentOptions.BottomRight;

            return panel;
        }

        GameObject BuildModePanel(Transform root)
        {
            var panel = NewRect("ModeSelect", root);
            Stretch((RectTransform)panel.transform);

            AddDim(panel.transform, 0.45f);
            AddRibbonTitle(panel.transform, "CHOOSE MODE");
            AddBackButton(panel.transform, OnBackToMain);

            BuildModeCard(panel.transform, -420f, "KNOCKOUT",
                "Classic 3v3 team brawl.\nFirst team to 2 KOs takes the match.",
                theme != null ? theme.swordIcon : null, new Color(1f, 0.45f, 0.3f),
                () => OnModePicked(GameMode.Knockout));
            BuildModeCard(panel.transform, 420f, "GEM GRAB",
                "Gems erupt from the center mine.\nHold 10 for 15 seconds to win — but\ndeath spills everything you carry!",
                theme != null ? theme.gemIcon : null, new Color(0.35f, 0.95f, 0.6f),
                () => OnModePicked(GameMode.GemGrab));

            return panel;
        }

        void BuildModeCard(Transform root, float x, string name, string blurb,
            Sprite icon, Color accent, UnityEngine.Events.UnityAction onPick)
        {
            var card = NewRect("Card_" + name, root);
            var rt = (RectTransform)card.transform;
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.47f);
            rt.anchoredPosition = new Vector2(x, 0f);
            rt.sizeDelta = new Vector2(700f, 820f);

            var bg = card.AddComponent<Image>();
            if (theme != null && theme.card != null)
            {
                bg.sprite = theme.card;
                bg.type = Image.Type.Sliced;
            }
            else
            {
                bg.color = new Color(0.12f, 0.15f, 0.24f, 0.97f);
            }

            if (icon != null)
            {
                var iconGo = NewRect("Icon", card.transform);
                var irt = (RectTransform)iconGo.transform;
                irt.anchorMin = irt.anchorMax = new Vector2(0.5f, 0.72f);
                irt.sizeDelta = new Vector2(260f, 260f);
                var img = iconGo.AddComponent<Image>();
                img.sprite = icon;
                img.preserveAspect = true;
                img.raycastTarget = false;
            }

            var title = MakeHeading(card.transform, name, 76f, accent);
            var trt = title.rectTransform;
            trt.anchorMin = trt.anchorMax = new Vector2(0.5f, 0.5f);
            trt.sizeDelta = new Vector2(650f, 110f);

            var body = MakeBody(card.transform, blurb, 38f, new Color(1f, 1f, 1f, 0.92f));
            var brt = body.rectTransform;
            brt.anchorMin = brt.anchorMax = new Vector2(0.5f, 0.33f);
            brt.sizeDelta = new Vector2(600f, 260f);

            MakeButton(card.transform, "SELECT", theme != null ? theme.buttonGreen : null,
                new Vector2(0.5f, 0.09f), new Vector2(420f, 145f), 60f, onPick);
        }

        GameObject BuildCharacterPanel(Transform root)
        {
            var panel = NewRect("CharacterSelect", root);
            Stretch((RectTransform)panel.transform);

            AddRibbonTitle(panel.transform, "CHOOSE YOUR BRAWLER");
            AddBackButton(panel.transform, OnBackToMode);

            // Drag anywhere in the center rotates the 3D preview.
            var spin = NewRect("SpinZone", panel.transform);
            var sprt = (RectTransform)spin.transform;
            sprt.anchorMin = new Vector2(0.28f, 0.1f);
            sprt.anchorMax = new Vector2(0.66f, 0.82f);
            sprt.offsetMin = Vector2.zero;
            sprt.offsetMax = Vector2.zero;
            var spinImg = spin.AddComponent<Image>();
            spinImg.color = new Color(1f, 1f, 1f, 0f);
            spin.AddComponent<PodiumSpinZone>().flow = this;

            // Name plate on the left.
            charName = MakeHeading(panel.transform, "", 110f, Color.white);
            var nrt = charName.rectTransform;
            nrt.anchorMin = nrt.anchorMax = new Vector2(0.16f, 0.62f);
            nrt.sizeDelta = new Vector2(620f, 140f);

            charRole = MakeBody(panel.transform, "", 48f, new Color(0.65f, 0.85f, 1f));
            var rrt = charRole.rectTransform;
            rrt.anchorMin = rrt.anchorMax = new Vector2(0.16f, 0.54f);
            rrt.sizeDelta = new Vector2(620f, 70f);

            charKind = MakeBody(panel.transform, "", 42f, new Color(1f, 0.9f, 0.5f));
            var krt = charKind.rectTransform;
            krt.anchorMin = krt.anchorMax = new Vector2(0.16f, 0.47f);
            krt.sizeDelta = new Vector2(620f, 60f);

            charLevel = MakeHeading(panel.transform, "", 52f, new Color(1f, 0.85f, 0.3f));
            var lrt = charLevel.rectTransform;
            lrt.anchorMin = lrt.anchorMax = new Vector2(0.16f, 0.39f);
            lrt.sizeDelta = new Vector2(620f, 70f);

            // Description + stats card on the right.
            var info = NewRect("InfoCard", panel.transform);
            var irt = (RectTransform)info.transform;
            irt.anchorMin = irt.anchorMax = new Vector2(0.84f, 0.47f);
            irt.sizeDelta = new Vector2(640f, 700f);
            var infoBg = info.AddComponent<Image>();
            if (theme != null && theme.panel != null)
            {
                infoBg.sprite = theme.panel;
                infoBg.type = Image.Type.Sliced;
                // The pack panel is white; tint navy so the white text reads.
                infoBg.color = new Color(0.14f, 0.18f, 0.3f, 0.97f);
            }
            else
            {
                infoBg.color = new Color(0.1f, 0.12f, 0.2f, 0.95f);
            }

            charDescription = MakeBody(info.transform, "", 40f, new Color(1f, 1f, 1f, 0.95f));
            var drt = charDescription.rectTransform;
            drt.anchorMin = new Vector2(0.08f, 0.45f);
            drt.anchorMax = new Vector2(0.92f, 0.9f);
            drt.offsetMin = Vector2.zero;
            drt.offsetMax = Vector2.zero;
            charDescription.alignment = TextAlignmentOptions.TopLeft;

            string[] statNames = { "HEALTH", "DAMAGE", "SPEED" };
            Color[] statColors =
            {
                new Color(0.4f, 0.9f, 0.45f),
                new Color(1f, 0.55f, 0.35f),
                new Color(0.45f, 0.75f, 1f),
            };
            for (int i = 0; i < 3; i++)
                statFills[i] = AddStatBar(info.transform, statNames[i], statColors[i], 0.32f - i * 0.1f);

            // Prev / next arrows flanking the podium.
            MakeArrowButton(panel.transform, new Vector2(0.28f, 0.42f), false, () => StepCharacter(-1));
            MakeArrowButton(panel.transform, new Vector2(0.66f, 0.42f), true, () => StepCharacter(1));

            MakeButton(panel.transform, "BATTLE!", theme != null ? theme.buttonYellow : null,
                new Vector2(0.5f, 0.09f), new Vector2(560f, 195f), 84f, OnBattlePressed);

            return panel;
        }

        // ---------------- shop ----------------

        TextMeshProUGUI AddCoinsCapsule(Transform root)
        {
            var capsule = NewRect("Coins", root);
            var rt = (RectTransform)capsule.transform;
            rt.anchorMin = rt.anchorMax = new Vector2(0.93f, 0.94f);
            rt.sizeDelta = new Vector2(300f, 82f);
            var bg = capsule.AddComponent<Image>();
            if (theme != null && theme.resourceCapsule != null)
            {
                bg.sprite = theme.resourceCapsule;
                bg.type = Image.Type.Sliced;
                bg.color = new Color(0.1f, 0.12f, 0.2f, 0.9f);
            }
            else
            {
                bg.color = new Color(0f, 0f, 0f, 0.5f);
            }
            bg.raycastTarget = false;

            if (theme != null && theme.coinIcon != null)
            {
                var icon = NewRect("Icon", capsule.transform);
                var irt = (RectTransform)icon.transform;
                irt.anchorMin = irt.anchorMax = new Vector2(0.14f, 0.5f);
                irt.sizeDelta = new Vector2(72f, 78f);
                var img = icon.AddComponent<Image>();
                img.sprite = theme.coinIcon;
                img.preserveAspect = true;
                img.raycastTarget = false;
            }

            var text = MakeButtonLabel(capsule.transform, Progress.Coins.ToString("N0"), 44f, Color.white);
            var trt = text.rectTransform;
            trt.anchorMin = new Vector2(0.28f, 0f);
            trt.anchorMax = new Vector2(0.96f, 1f);
            trt.offsetMin = Vector2.zero;
            trt.offsetMax = Vector2.zero;
            text.alignment = TextAlignmentOptions.Center;
            return text;
        }

        GameObject BuildShopPanel(Transform root)
        {
            var panel = NewRect("Shop", root);
            Stretch((RectTransform)panel.transform);

            AddDim(panel.transform, 0.55f);
            AddRibbonTitle(panel.transform, "SHOP");
            AddBackButton(panel.transform, OnBackToMain);
            shopCoinsText = AddCoinsCapsule(panel.transform);

            var hint = MakeBody(panel.transform,
                "EARN POINTS AND COINS IN BATTLE  —  SPEND THEM HERE TO LEVEL UP YOUR BRAWLERS (+5% HP & DAMAGE PER LEVEL)",
                34f, new Color(1f, 1f, 1f, 0.8f));
            var hrt = hint.rectTransform;
            hrt.anchorMin = hrt.anchorMax = new Vector2(0.5f, 0.82f);
            hrt.sizeDelta = new Vector2(2100f, 50f);

            for (int i = 0; i < roster.Length; i++)
            {
                int col = i % 3;
                int row = i / 3;
                BuildShopCard(panel.transform, roster[i],
                    new Vector2((col - 1) * 720f, 90f - row * 560f));
            }

            panel.SetActive(false);
            return panel;
        }

        void BuildShopCard(Transform root, BrawlerDefinition def, Vector2 pos)
        {
            var card = NewRect("Shop_" + def.id, root);
            var rt = (RectTransform)card.transform;
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.45f);
            rt.anchoredPosition = pos;
            rt.sizeDelta = new Vector2(660f, 520f);

            var bg = card.AddComponent<Image>();
            if (theme != null && theme.card != null)
            {
                bg.sprite = theme.card;
                bg.type = Image.Type.Sliced;
            }
            else
            {
                bg.color = new Color(0.12f, 0.15f, 0.24f, 0.97f);
            }

            if (def.portrait != null)
            {
                var pGo = NewRect("Portrait", card.transform);
                var prt = (RectTransform)pGo.transform;
                prt.anchorMin = prt.anchorMax = new Vector2(0.22f, 0.62f);
                prt.sizeDelta = new Vector2(240f, 300f);
                var img = pGo.AddComponent<Image>();
                img.sprite = def.portrait;
                img.preserveAspect = true;
                img.raycastTarget = false;
            }

            var name = MakeHeading(card.transform, def.displayName.ToUpperInvariant(), 56f, Color.white);
            var nrt = name.rectTransform;
            nrt.anchorMin = nrt.anchorMax = new Vector2(0.66f, 0.86f);
            nrt.sizeDelta = new Vector2(320f, 70f);

            var levelText = MakeButtonLabel(card.transform, "", 44f, new Color(1f, 0.85f, 0.3f));
            var lrt = levelText.rectTransform;
            lrt.anchorMin = lrt.anchorMax = new Vector2(0.66f, 0.7f);
            lrt.sizeDelta = new Vector2(320f, 60f);

            // Points progress toward the next level.
            var barBg = NewRect("PointsBg", card.transform);
            var brt = (RectTransform)barBg.transform;
            brt.anchorMin = brt.anchorMax = new Vector2(0.66f, 0.55f);
            brt.sizeDelta = new Vector2(280f, 32f);
            var bgImg = barBg.AddComponent<Image>();
            if (theme != null && theme.barBg != null)
            {
                bgImg.sprite = theme.barBg;
                bgImg.type = Image.Type.Sliced;
            }
            bgImg.color = new Color(0.1f, 0.1f, 0.16f, 0.9f);
            bgImg.raycastTarget = false;

            var fillGo = NewRect("PointsFill", barBg.transform);
            var frt = (RectTransform)fillGo.transform;
            frt.anchorMin = Vector2.zero;
            frt.anchorMax = new Vector2(0.5f, 1f);
            frt.offsetMin = new Vector2(3f, 3f);
            frt.offsetMax = new Vector2(-3f, -3f);
            var fillImg = fillGo.AddComponent<Image>();
            if (theme != null && theme.barFillYellow != null)
            {
                fillImg.sprite = theme.barFillYellow;
                fillImg.type = Image.Type.Sliced;
            }
            fillImg.color = new Color(0.55f, 0.4f, 1f);
            fillImg.raycastTarget = false;

            var pointsText = MakeBody(card.transform, "", 30f, new Color(1f, 1f, 1f, 0.85f));
            var ptr = pointsText.rectTransform;
            ptr.anchorMin = ptr.anchorMax = new Vector2(0.66f, 0.46f);
            ptr.sizeDelta = new Vector2(320f, 40f);

            // Upgrade button along the card bottom.
            var btnGo = NewRect("Upgrade", card.transform);
            var urt = (RectTransform)btnGo.transform;
            urt.anchorMin = urt.anchorMax = new Vector2(0.5f, 0.14f);
            urt.sizeDelta = new Vector2(480f, 130f);
            var btnImg = btnGo.AddComponent<Image>();
            if (theme != null && theme.buttonGreen != null)
            {
                btnImg.sprite = theme.buttonGreen;
                btnImg.type = Image.Type.Sliced;
            }
            else
            {
                btnImg.color = new Color(0.3f, 0.8f, 0.35f, 0.95f);
            }
            var button = btnGo.AddComponent<Button>();
            var btnLabel = MakeButtonLabel(btnGo.transform, "", 44f, Color.white);
            Stretch(btnLabel.rectTransform);
            btnLabel.rectTransform.offsetMin = new Vector2(0f, 16f);
            btnLabel.raycastTarget = false;

            button.onClick.AddListener(() =>
            {
                if (Progress.TryUpgrade(def.id)) RefreshShop();
            });

            void Refresh()
            {
                var c = Progress.Get(def.id);
                levelText.text = "LEVEL " + c.level;
                if (c.level >= Progress.MaxLevel)
                {
                    pointsText.text = "MAX LEVEL";
                    frt.anchorMax = new Vector2(1f, 1f);
                    btnLabel.text = "MAXED";
                    button.interactable = false;
                    btnImg.color = new Color(0.6f, 0.6f, 0.6f, 0.8f);
                    return;
                }
                int needed = Progress.PointsNeeded(c.level);
                pointsText.text = $"{c.points} / {needed} POINTS";
                frt.anchorMax = new Vector2(Mathf.Clamp01(c.points / (float)needed), 1f);
                btnLabel.text = $"UPGRADE   {Progress.CoinCost(c.level)} COINS";
                bool can = Progress.CanUpgrade(def.id);
                button.interactable = can;
                btnImg.color = can ? Color.white : new Color(0.55f, 0.55f, 0.55f, 0.85f);
            }

            shopRefreshers.Add(Refresh);
            Refresh();
        }

        Image AddStatBar(Transform card, string label, Color color, float anchorY)
        {
            var text = MakeBody(card, label, 34f, new Color(1f, 1f, 1f, 0.85f));
            var trt = text.rectTransform;
            trt.anchorMin = trt.anchorMax = new Vector2(0.22f, anchorY);
            trt.sizeDelta = new Vector2(180f, 46f);

            var barBg = NewRect(label + "Bg", card);
            var brt = (RectTransform)barBg.transform;
            brt.anchorMin = brt.anchorMax = new Vector2(0.63f, anchorY);
            brt.sizeDelta = new Vector2(330f, 34f);
            var bgImg = barBg.AddComponent<Image>();
            if (theme != null && theme.barBg != null)
            {
                bgImg.sprite = theme.barBg;
                bgImg.type = Image.Type.Sliced;
                bgImg.color = new Color(0.15f, 0.15f, 0.2f, 0.9f);
            }
            else
            {
                bgImg.color = new Color(0f, 0f, 0f, 0.55f);
            }
            bgImg.raycastTarget = false;

            var fill = NewRect(label + "Fill", barBg.transform);
            var frt = (RectTransform)fill.transform;
            frt.anchorMin = Vector2.zero;
            frt.anchorMax = new Vector2(0.5f, 1f);
            frt.offsetMin = new Vector2(4f, 4f);
            frt.offsetMax = new Vector2(-4f, -4f);
            var fillImg = fill.AddComponent<Image>();
            if (theme != null && theme.barFillYellow != null)
            {
                fillImg.sprite = theme.barFillYellow;
                fillImg.type = Image.Type.Sliced;
            }
            fillImg.color = color;
            fillImg.raycastTarget = false;
            return fillImg;
        }

        // ---------------- shared widget helpers ----------------

        void AddDim(Transform root, float alpha)
        {
            var dim = NewRect("Dim", root);
            Stretch((RectTransform)dim.transform);
            var img = dim.AddComponent<Image>();
            img.color = new Color(0.02f, 0.03f, 0.06f, alpha);
        }

        void AddRibbonTitle(Transform root, string text)
        {
            var ribbon = NewRect("Ribbon", root);
            var rt = (RectTransform)ribbon.transform;
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.92f);
            rt.sizeDelta = new Vector2(1100f, 150f);
            if (theme != null && theme.ribbon != null)
            {
                var img = ribbon.AddComponent<Image>();
                img.sprite = theme.ribbon;
                img.type = Image.Type.Sliced;
                img.raycastTarget = false;
            }
            var title = MakeHeading(ribbon.transform, text, 64f, Color.white);
            Stretch(title.rectTransform);
            // Ribbon art hangs below its band; nudge text up into the band.
            title.rectTransform.offsetMin = new Vector2(0f, 40f);
        }

        void AddBackButton(Transform root, UnityEngine.Events.UnityAction onBack)
        {
            var btn = NewRect("Back", root);
            var rt = (RectTransform)btn.transform;
            rt.anchorMin = rt.anchorMax = new Vector2(0.05f, 0.92f);
            rt.sizeDelta = new Vector2(120f, 132f);
            var img = btn.AddComponent<Image>();
            if (theme != null && theme.buttonRound != null)
            {
                img.sprite = theme.buttonRound;
                img.preserveAspect = true;
            }
            else
            {
                img.color = new Color(0.25f, 0.28f, 0.4f, 0.9f);
            }
            btn.AddComponent<Button>().onClick.AddListener(onBack);

            if (theme != null && theme.arrowLeft != null)
            {
                var glyph = NewRect("Glyph", btn.transform);
                var grt = (RectTransform)glyph.transform;
                grt.anchorMin = grt.anchorMax = new Vector2(0.5f, 0.53f);
                grt.sizeDelta = new Vector2(52f, 62f);
                var gi = glyph.AddComponent<Image>();
                gi.sprite = theme.arrowLeft;
                gi.preserveAspect = true;
                gi.color = new Color(0.2f, 0.24f, 0.35f);
                gi.raycastTarget = false;
            }
            else
            {
                var label = MakeHeading(btn.transform, "<", 60f, Color.white);
                Stretch(label.rectTransform);
            }
        }

        void MakeArrowButton(Transform root, Vector2 anchor, bool right, UnityEngine.Events.UnityAction onClick)
        {
            var btn = NewRect(right ? "Next" : "Prev", root);
            var rt = (RectTransform)btn.transform;
            rt.anchorMin = rt.anchorMax = anchor;
            rt.sizeDelta = new Vector2(150f, 165f);
            var img = btn.AddComponent<Image>();
            if (theme != null && theme.buttonRound != null)
            {
                img.sprite = theme.buttonRound;
                img.preserveAspect = true;
            }
            else
            {
                img.color = new Color(0.25f, 0.28f, 0.4f, 0.9f);
            }
            btn.AddComponent<Button>().onClick.AddListener(onClick);

            Sprite glyphSprite = theme != null ? (right ? theme.arrowRight : theme.arrowLeft) : null;
            if (glyphSprite != null)
            {
                var glyph = NewRect("Glyph", btn.transform);
                var grt = (RectTransform)glyph.transform;
                grt.anchorMin = grt.anchorMax = new Vector2(0.5f, 0.53f);
                grt.sizeDelta = new Vector2(60f, 72f);
                var gi = glyph.AddComponent<Image>();
                gi.sprite = glyphSprite;
                gi.preserveAspect = true;
                gi.color = new Color(0.2f, 0.24f, 0.35f);
                gi.raycastTarget = false;
            }
            else
            {
                var label = MakeHeading(btn.transform, right ? ">" : "<", 64f, Color.white);
                Stretch(label.rectTransform);
            }
        }

        void MakeButton(Transform root, string label, Sprite sprite, Vector2 anchor,
            Vector2 size, float fontSize, UnityEngine.Events.UnityAction onClick)
        {
            var btn = NewRect("Button_" + label, root);
            var rt = (RectTransform)btn.transform;
            rt.anchorMin = rt.anchorMax = anchor;
            rt.sizeDelta = size;
            var img = btn.AddComponent<Image>();
            if (sprite != null)
            {
                // GUI Pro CTA pills are zero-center vertical strips: they must
                // be sliced and never native-sized.
                img.sprite = sprite;
                img.type = Image.Type.Sliced;
            }
            else
            {
                img.color = new Color(0.95f, 0.75f, 0.2f, 0.95f);
            }
            btn.AddComponent<Button>().onClick.AddListener(onClick);

            var txt = MakeButtonLabel(btn.transform, label, fontSize, Color.white);
            Stretch(txt.rectTransform);
            // The pill art has a baked bottom shadow; optically center the label.
            txt.rectTransform.offsetMin = new Vector2(0f, size.y * 0.12f);
            txt.raycastTarget = false;
        }

        TextMeshProUGUI MakeHeading(Transform parent, string text, float size, Color color)
        {
            return MakeTmp(parent, text, size, color, theme != null ? theme.headingFont : null);
        }

        TextMeshProUGUI MakeButtonLabel(Transform parent, string text, float size, Color color)
        {
            return MakeTmp(parent, text, size, color, theme != null ? theme.buttonFont : null);
        }

        TextMeshProUGUI MakeBody(Transform parent, string text, float size, Color color)
        {
            return MakeTmp(parent, text, size, color, theme != null ? theme.bodyFont : null);
        }

        TextMeshProUGUI MakeTmp(Transform parent, string text, float size, Color color, TMP_FontAsset font)
        {
            var go = NewRect("Text", parent);
            var tmp = go.AddComponent<TextMeshProUGUI>();
            if (font != null) tmp.font = font;
            tmp.text = text;
            tmp.fontSize = size;
            tmp.color = color;
            tmp.alignment = TextAlignmentOptions.Center;
            tmp.raycastTarget = false;
            return tmp;
        }

        static GameObject NewRect(string name, Transform parent)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            return go;
        }

        static void Stretch(RectTransform rt)
        {
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;
        }
    }

    /// <summary>Drag surface that spins the character-select podium.</summary>
    public class PodiumSpinZone : MonoBehaviour, IDragHandler
    {
        public MainMenuFlow flow;

        public void OnDrag(PointerEventData e)
        {
            if (flow != null) flow.RotatePodium(-e.delta.x * 0.35f);
        }
    }
}
