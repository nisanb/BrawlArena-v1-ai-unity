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
        TMP_FontAsset readableBodyFont;
        Canvas canvas;
        RectTransform safeAreaRoot;
        Rect lastSafeArea;
        int lastScreenWidth;
        int lastScreenHeight;
        GameObject mainPanel;
        GameObject modePanel;
        GameObject charPanel;
        GameObject shopPanel;
        GameObject brawlersPanel;
        GameObject cardsPanel;
        GameObject inventoryPanel;
        GameObject missionsPanel;
        GameObject rewardsPanel;
        GameObject rankingPanel;
        GameObject friendsPanel;
        GameObject inboxPanel;
        GameObject noticePanel;
        GameObject settingsPanel;
        ScrollRect shopScroll;
        TextMeshProUGUI menuCoinsText;
        TextMeshProUGUI menuGemsText;
        TextMeshProUGUI menuEnergyText;
        TextMeshProUGUI stageModeText;
        TextMeshProUGUI mainHeroName;
        TextMeshProUGUI mainHeroMeta;
        TextMeshProUGUI mainHeroProgress;
        TextMeshProUGUI mainLoadoutText;
        TextMeshProUGUI mainQuestText;
        RectTransform mainHeroProgressFill;
        RectTransform mainQuestFill;
        Image mainHeroPortrait;
        TextMeshProUGUI shopCoinsText;
        TextMeshProUGUI shopGemsText;
        TextMeshProUGUI statusToast;
        GameObject statusToastRoot;
        RectTransform fxLayer;
        Coroutine celebrationRoutine;
        Coroutine toastRoutine;
        readonly List<System.Action> shopRefreshers = new List<System.Action>();
        readonly List<System.Action> menuRefreshers = new List<System.Action>();
        readonly Image[] shopTabBackgrounds = new Image[4];
        readonly TextMeshProUGUI[] shopTabLabels = new TextMeshProUGUI[4];
        int activeShopTab;

        // Character select widgets
        TextMeshProUGUI charName;
        TextMeshProUGUI charRole;
        TextMeshProUGUI charDescription;
        TextMeshProUGUI charSchoolTag;
        TextMeshProUGUI charKind;
        TextMeshProUGUI charLevel;
        TextMeshProUGUI charPoints;
        Image charPortrait;
        Image charSchoolIcon;
        readonly Image[] charStars = new Image[5];
        Image[] statFills = new Image[3];
        readonly TextMeshProUGUI[] charSkillNames = new TextMeshProUGUI[3];
        readonly TextMeshProUGUI[] charSkillDescriptions = new TextMeshProUGUI[3];
        readonly TextMeshProUGUI[] charSkillButtonLabels = new TextMeshProUGUI[3];
        readonly Button[] charSkillButtons = new Button[3];
        readonly Image[] charSkillButtonImages = new Image[3];
        readonly List<Image> schoolTabBackgrounds = new List<Image>();
        readonly List<Image> schoolTabIcons = new List<Image>();
        readonly List<TextMeshProUGUI> schoolTabLabels = new List<TextMeshProUGUI>();
        GameObject previewInstance;
        int charIndex;
        float lastSpinInputAt;
        bool launching;

        const float ToastVisibleSeconds = 2.5f;

        static string ModeTitle(GameMode mode)
        {
            switch (mode)
            {
                case GameMode.ControlZone: return "CONTROL ZONE";
                case GameMode.GemGrab: return "GEM GRAB";
                default: return "KNOCKOUT";
            }
        }

        static string ModeTrialTitle(GameMode mode)
        {
            return mode == GameMode.ControlZone
                ? "CONTROL ZONE TRIAL"
                : mode == GameMode.GemGrab ? "GEM RUSH TRIAL" : "KNOCKOUT TRIAL";
        }

        static bool AutopilotRequested =>
            Application.isEditor &&
            File.Exists(Path.Combine(Directory.GetParent(Application.dataPath).FullName, "Automation", "autopilot.flag"));

        void Awake()
        {
            theme = FindFirstObjectByType<UiTheme>();
            readableBodyFont = Resources.Load<TMP_FontAsset>("Fonts & Materials/LiberationSans SDF");
        }

        void Start()
        {
            if (roster != null)
            {
                foreach (BrawlerDefinition definition in roster)
                    if (definition != null) definition.EnsureSuperConfiguration();
            }
            MatchSetup.Mode = Progress.SelectedMode;
            MatchSetup.CharacterIndex = -1;
            MatchSetup.FromMenu = false;
            charIndex = ResolveSelectedCharacterIndex();
            EnsureEventSystem();
            BuildUi();
            ShowPanel(mainPanel);
            SetPreviewVisible(true);
            SetCharacter(charIndex);
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
            RefreshSafeArea();

            // Idle spin for the podium character after manual input settles.
            if (!AccessibilitySettings.ReducedMotionEnabled && podium != null && previewInstance != null &&
                Time.time - lastSpinInputAt > 2.5f)
                podium.Rotate(0f, 14f * Time.deltaTime, 0f);
        }

        void RefreshSafeArea()
        {
            if (safeAreaRoot == null || Screen.width <= 0 || Screen.height <= 0) return;
            Rect area = Screen.safeArea;
            if (lastScreenWidth == Screen.width && lastScreenHeight == Screen.height && lastSafeArea == area) return;

            Vector2 anchorMin = area.position;
            Vector2 anchorMax = area.position + area.size;
            anchorMin.x /= Screen.width;
            anchorMin.y /= Screen.height;
            anchorMax.x /= Screen.width;
            anchorMax.y /= Screen.height;
            safeAreaRoot.anchorMin = anchorMin;
            safeAreaRoot.anchorMax = anchorMax;
            safeAreaRoot.offsetMin = Vector2.zero;
            safeAreaRoot.offsetMax = Vector2.zero;

            lastSafeArea = area;
            lastScreenWidth = Screen.width;
            lastScreenHeight = Screen.height;
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
            GameMode mode = GameMode.ControlZone;
            try
            {
                string content = File.ReadAllText(flag).ToLowerInvariant();
                mode = content.Contains("gemgrab")
                    ? GameMode.GemGrab
                    : content.Contains("knockout")
                        ? GameMode.Knockout
                        : GameMode.ControlZone;
            }
            catch { }

            // Detour through the shop so unattended runs exercise/screenshot it.
            yield return new WaitForSeconds(1.4f);
            OnShopPressed();
            yield return new WaitForSeconds(1f);
            // Scroll to the bottom row so tests prove the grid scrolls; dwell
            // long enough for the slow background-stepped screenshot to land.
            if (shopScroll != null) shopScroll.verticalNormalizedPosition = 0f;
            yield return new WaitForSeconds(5f);
            OnBackToMain();
            yield return new WaitForSeconds(0.6f);
            OnPlayPressed();
            yield return new WaitForSeconds(1.6f);
            OnModePicked(mode);
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
            foreach (var p in AllPanels())
                if (p != null) p.SetActive(p == panel);
            if (panel == mainPanel && roster != null && roster.Length > 0)
            {
                SetPreviewVisible(true);
                SetCharacter(charIndex);
            }
            if (panel == mainPanel || panel == shopPanel) RefreshShop();
            RefreshMenu();
        }

        IEnumerable<GameObject> AllPanels()
        {
            yield return mainPanel;
            yield return modePanel;
            yield return charPanel;
            yield return shopPanel;
            yield return brawlersPanel;
            yield return cardsPanel;
            yield return inventoryPanel;
            yield return missionsPanel;
            yield return rewardsPanel;
            yield return rankingPanel;
            yield return friendsPanel;
            yield return inboxPanel;
            yield return noticePanel;
            yield return settingsPanel;
        }

        void OnShopPressed()
        {
            ShowPanel(shopPanel);
            SetPreviewVisible(false);
            DebugPhase = "shop";
        }

        void OnBrawlersPressed() => ShowUtilityPanel(brawlersPanel, "brawlers");
        void OnCardsPressed() => ShowUtilityPanel(cardsPanel, "cards");
        void OnInventoryPressed() => ShowUtilityPanel(inventoryPanel, "inventory");
        void OnMissionsPressed() => ShowUtilityPanel(missionsPanel, "missions");
        void OnRewardsPressed() => ShowUtilityPanel(rewardsPanel, "rewards");
        void OnRankingPressed() => ShowUtilityPanel(rankingPanel, "ranking");
        void OnFriendsPressed() => ShowUtilityPanel(friendsPanel, "friends");
        void OnInboxPressed() => ShowUtilityPanel(inboxPanel, "inbox");
        void OnNoticePressed() => ShowUtilityPanel(noticePanel, "notice");
        void OnSettingsPressed() => ShowUtilityPanel(settingsPanel, "settings");

        void ShowUtilityPanel(GameObject panel, string phase)
        {
            ShowPanel(panel);
            SetPreviewVisible(false);
            DebugPhase = phase;
        }

        void RefreshShop()
        {
            string coins = Progress.Coins.ToString("N0");
            if (menuCoinsText != null) menuCoinsText.text = coins;
            if (shopCoinsText != null) shopCoinsText.text = coins;
            if (menuGemsText != null) menuGemsText.text = Progress.Gems.ToString("N0");
            if (shopGemsText != null) shopGemsText.text = Progress.Gems.ToString("N0");
            if (menuEnergyText != null) menuEnergyText.text = Progress.Energy + " / 60";
            foreach (var refresh in shopRefreshers) refresh();
        }

        void RefreshMenu()
        {
            if (menuCoinsText != null) menuCoinsText.text = Progress.Coins.ToString("N0");
            if (menuGemsText != null) menuGemsText.text = Progress.Gems.ToString("N0");
            if (menuEnergyText != null) menuEnergyText.text = Progress.Energy + " / 60";
            RefreshMainHero();
            foreach (var refresh in menuRefreshers) refresh();
        }

        void ShowToast(string message)
        {
            if (statusToast == null) return;
            if (toastRoutine != null) StopCoroutine(toastRoutine);
            statusToast.text = message;
            statusToast.gameObject.SetActive(true);
            if (statusToastRoot == null && statusToast.transform.parent != null)
                statusToastRoot = statusToast.transform.parent.gameObject;
            if (statusToastRoot != null) statusToastRoot.SetActive(true);
            toastRoutine = StartCoroutine(HideToastAfterDelay());
        }

        IEnumerator HideToastAfterDelay()
        {
            yield return new WaitForSecondsRealtime(ToastVisibleSeconds);
            if (statusToastRoot != null) statusToastRoot.SetActive(false);
            toastRoutine = null;
        }

        void OnPlayPressed()
        {
            ShowPanel(modePanel);
            SetPreviewVisible(false);
            DebugPhase = "mode";
        }

        void OnModePicked(GameMode mode)
        {
            MatchSetup.Mode = mode;
            Progress.SetSelectedMode(mode);
            ShowPanel(charPanel);
            SetPreviewVisible(true);
            SetCharacter(charIndex);
            DebugPhase = "character mode=" + mode;
        }

        void OnQuickBattlePressed()
        {
            MatchSetup.Mode = Progress.SelectedMode;
            ShowPanel(charPanel);
            SetPreviewVisible(true);
            SetCharacter(charIndex);
            DebugPhase = "quick battle mode=" + MatchSetup.Mode;
        }

        void OnBackToMain()
        {
            ShowPanel(mainPanel);
            SetPreviewVisible(true);
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
            if (roster == null || roster.Length == 0) return;
            SetCharacter((charIndex + direction + roster.Length) % roster.Length);
        }

        void OnBattlePressed()
        {
            if (launching) return;
            if (!AutopilotRequested && !Progress.TrySpendBattleEnergy())
            {
                ShowToast("Need " + Progress.BattleEnergyCost + " Battle Energy - use an Energy Cell or claim rewards");
                RefreshMenu();
                return;
            }
            RefreshMenu();
            launching = true;
            MatchSetup.CharacterIndex = charIndex;
            MatchSetup.FromMenu = true;
            DebugPhase = "launching char=" + charIndex + " mode=" + MatchSetup.Mode;

            // Victory flourish on the podium, then off to the arena.
            if (previewInstance != null)
            {
                try
                {
                    BrawlerPreviewAdapter.ShowVictory(previewInstance, roster[charIndex]);
                }
                catch (System.Exception exception)
                {
                    Debug.LogError("[MainMenuFlow] Invector victory preview failed closed.\n" + exception);
                }
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
            if (roster == null || roster.Length == 0) return;
            charIndex = index;
            var def = roster[index];
            Progress.SetSelectedCharacter(def.id);

            if (previewInstance != null) Destroy(previewInstance);
            previewInstance = null;
            if (podium != null)
            {
                try
                {
                    GameObject previewPrefab = BrawlerPreviewAdapter.ResolvePrefab(def);
                    podium.rotation = Quaternion.identity;
                    previewInstance = Instantiate(previewPrefab, podium, false);
                    previewInstance.transform.localPosition = Vector3.zero;
                    previewInstance.transform.localRotation = Quaternion.identity;
                    BrawlerPreviewAdapter.Prepare(previewInstance, def);
                    BrawlerPreviewAdapter.ShowIdle(previewInstance, def, 0.3f);
                }
                catch (System.Exception exception)
                {
                    if (previewInstance != null) Destroy(previewInstance);
                    previewInstance = null;
                    Debug.LogError("[MainMenuFlow] Invector character preview failed closed for '" +
                        def.id + "'.\n" + exception);
                }
            }

            if (charName != null) charName.text = def.displayName.ToUpperInvariant();
            if (charRole != null) charRole.text = def.role.ToUpperInvariant();
            if (charDescription != null) charDescription.text = def.description;
            if (charSchoolTag != null) charSchoolTag.text = CharacterClassLabel(def);
            if (charKind != null) charKind.text = def.specialty.school == SpellSchool.None
                ? "RANGED HERO"
                : "ELEMENTAL MAGE";
            var progress = Progress.Get(def.id);
            if (charLevel != null) charLevel.text = "LEVEL " + progress.level;
            if (charPoints != null)
            {
                int needed = progress.level >= Progress.MaxLevel ? Progress.PointsNeeded(Progress.MaxLevel) : Progress.PointsNeeded(progress.level);
                charPoints.text = progress.level >= Progress.MaxLevel ? $"{progress.points} AP" : $"{progress.points}/{needed} AP";
            }
            if (charPortrait != null)
            {
                charPortrait.sprite = def.portrait;
                charPortrait.enabled = def.portrait != null;
            }
            if (charSchoolIcon != null)
            {
                charSchoolIcon.sprite = CharacterIcon(def, index);
                charSchoolIcon.enabled = charSchoolIcon.sprite != null;
            }
            int filledStars = Mathf.Clamp(Mathf.CeilToInt(progress.level * 0.5f), 1, charStars.Length);
            for (int i = 0; i < charStars.Length; i++)
            {
                if (charStars[i] == null) continue;
                charStars[i].sprite = theme != null && i < filledStars ? theme.starOnIcon : theme != null ? theme.starOffIcon : null;
                charStars[i].color = charStars[i].sprite != null ? Color.white : new Color(1f, 0.85f, 0.25f, i < filledStars ? 1f : 0.28f);
            }
            SetStat(0, def.maxHealth / 160f);
            SetStat(1, def.damage / 30f);
            SetStat(2, (def.moveSpeed - 4f) / 1.6f);
            RefreshCharacterSkills(def);
            RefreshMainHero();
            RefreshSchoolRail();
        }

        int ResolveSelectedCharacterIndex()
        {
            if (roster == null || roster.Length == 0) return 0;
            string selected = Progress.SelectedCharacterId;
            if (!string.IsNullOrEmpty(selected))
            {
                for (int i = 0; i < roster.Length; i++)
                    if (roster[i] != null && roster[i].id == selected) return i;
            }
            return Mathf.Clamp(charIndex, 0, roster.Length - 1);
        }

        BrawlerDefinition CurrentBrawler()
        {
            if (roster == null || roster.Length == 0) return null;
            return roster[Mathf.Clamp(charIndex, 0, roster.Length - 1)];
        }

        void RefreshMainHero()
        {
            var def = CurrentBrawler();
            if (def == null) return;
            var p = Progress.Get(def.id);
            int needed = p.level >= Progress.MaxLevel ? Progress.PointsNeeded(Progress.MaxLevel) : Progress.PointsNeeded(p.level);

            if (mainHeroName != null) mainHeroName.text = def.displayName.ToUpperInvariant();
            if (mainHeroMeta != null)
                mainHeroMeta.text = def.role.ToUpperInvariant() + "  /  LEVEL " + p.level;
            if (mainHeroProgress != null)
                mainHeroProgress.text = p.level >= Progress.MaxLevel ? p.points + " AP" : p.points + " / " + needed + " AP";
            if (mainHeroProgressFill != null)
                mainHeroProgressFill.anchorMax = new Vector2(p.level >= Progress.MaxLevel ? 1f : Mathf.Clamp01(p.points / (float)needed), 1f);
            if (mainLoadoutText != null) mainLoadoutText.text = Progress.EquippedCardCount() + "/3";
            if (mainHeroPortrait != null)
            {
                mainHeroPortrait.sprite = CharacterIcon(def, charIndex);
                mainHeroPortrait.enabled = mainHeroPortrait.sprite != null;
            }
            if (mainQuestFill != null)
                mainQuestFill.anchorMax = new Vector2(Mathf.Clamp01(Progress.TotalBrawlerPointsEarned() / 50f), 1f);
            if (mainQuestText != null)
                mainQuestText.text = Mathf.Min(Progress.TotalBrawlerPointsEarned(), 50) + " / 50";
        }

        void SetStat(int i, float value01)
        {
            if (statFills[i] == null) return;
            var rt = statFills[i].rectTransform;
            rt.anchorMax = new Vector2(Mathf.Clamp01(value01), 1f);
        }

        void UpgradeCurrentSkill(int slot)
        {
            var def = CurrentBrawler();
            if (def == null) return;
            var skills = CharacterSkillBook.For(def);
            if (slot < 0 || slot >= skills.Length) return;

            var skill = skills[slot];
            bool ok = Progress.TryUpgradeSkill(def.id, skill.id);
            ShowToast(ok
                ? skill.displayName + " " + (Progress.GetSkillLevel(def.id, skill.id) == 1 ? "learned" : "mastered")
                : "Need more arcane points");
            if (ok)
                ShowCelebration(skill.displayName.ToUpperInvariant(),
                    Progress.GetSkillLevel(def.id, skill.id) == 1 ? "SPELL LEARNED" : "SPELL MASTERED",
                    theme != null ? theme.passiveSkillIcon : null, true);
            RefreshCharacterSkills(def);
            RefreshMenu();
            RefreshShop();
        }

        void RefreshCharacterSkills(BrawlerDefinition def)
        {
            var skills = CharacterSkillBook.For(def);
            for (int i = 0; i < charSkillNames.Length; i++)
            {
                bool hasSkill = def != null && i < skills.Length && skills[i] != null;
                if (charSkillNames[i] != null) charSkillNames[i].gameObject.SetActive(hasSkill);
                if (charSkillDescriptions[i] != null) charSkillDescriptions[i].gameObject.SetActive(hasSkill);
                if (charSkillButtons[i] != null) charSkillButtons[i].gameObject.SetActive(hasSkill);
                if (!hasSkill) continue;

                var skill = skills[i];
                int level = Progress.GetSkillLevel(def.id, skill.id);
                int cost = Progress.SkillPointCost(level);
                bool maxed = level >= Progress.MaxSkillLevel;
                bool canUpgrade = Progress.CanUpgradeSkill(def.id, skill.id);

                if (charSkillNames[i] != null)
                    charSkillNames[i].text = $"{skill.displayName.ToUpperInvariant()}  LV {level}/{Progress.MaxSkillLevel}";
                if (charSkillDescriptions[i] != null)
                    charSkillDescriptions[i].text = level == 0 ? skill.description : skill.BonusText(level);
                if (charSkillButtonLabels[i] != null)
                    charSkillButtonLabels[i].text = maxed
                        ? "MASTERED"
                        : level == 0 && cost == 0 ? "LEARN" : (level == 0 ? "LEARN " : "FOCUS ") + cost;
                if (charSkillButtons[i] != null)
                    charSkillButtons[i].interactable = canUpgrade;
                if (charSkillButtonImages[i] != null)
                    charSkillButtonImages[i].color = canUpgrade ? Color.white : new Color(0.55f, 0.55f, 0.55f, 0.86f);
            }
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
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            scaler.matchWidthOrHeight = 0.5f;

            var safeArea = NewRect("SafeArea", canvasGo.transform);
            safeAreaRoot = (RectTransform)safeArea.transform;
            Stretch(safeAreaRoot);
            RefreshSafeArea();

            mainPanel = BuildMainPanel(safeAreaRoot);
            modePanel = BuildModePanel(safeAreaRoot);
            charPanel = BuildCharacterPanel(safeAreaRoot);
            shopPanel = BuildShopPanel(safeAreaRoot);
            brawlersPanel = BuildBrawlersPanel(safeAreaRoot);
            cardsPanel = BuildCardsPanel(safeAreaRoot);
            inventoryPanel = BuildInventoryPanel(safeAreaRoot);
            missionsPanel = BuildMissionsPanel(safeAreaRoot);
            rewardsPanel = BuildRewardsPanel(safeAreaRoot);
            rankingPanel = BuildRankingPanel(safeAreaRoot);
            friendsPanel = BuildFriendsPanel(safeAreaRoot);
            inboxPanel = BuildInboxPanel(safeAreaRoot);
            noticePanel = BuildNoticePanel(safeAreaRoot);
            settingsPanel = BuildSettingsPanel(safeAreaRoot);
            BuildToast(safeAreaRoot);
            BuildCelebrationLayer(safeAreaRoot);
        }

        GameObject BuildMainPanel(Transform root)
        {
            var panel = NewRect("Main", root);
            Stretch((RectTransform)panel.transform);

            // Keep the 3D hero unobstructed. The old full-screen GUI Pro
            // foundations and backdrop slices rendered in ScreenSpaceOverlay,
            // tinting the character behind them. Edge vignettes give text a
            // stable surface without putting a translucent sheet over the hero.
            AddSolidBand(panel.transform, "TopChrome", new Vector2(0f, 0.82f), Vector2.one,
                new Color(0.015f, 0.055f, 0.12f, 0.76f));
            AddSolidBand(panel.transform, "BottomChrome", Vector2.zero, new Vector2(1f, 0.19f),
                new Color(0.01f, 0.035f, 0.08f, 0.86f));
            AddSolidBand(panel.transform, "LeftVignette", new Vector2(0f, 0.19f), new Vector2(0.32f, 0.82f),
                new Color(0.01f, 0.04f, 0.09f, 0.28f));
            AddSolidBand(panel.transform, "RightVignette", new Vector2(0.78f, 0.19f), new Vector2(1f, 0.82f),
                new Color(0.01f, 0.04f, 0.09f, 0.34f));

            AddPlayerProgress(panel.transform);
            menuCoinsText = AddResourceCapsule(panel.transform, theme != null ? theme.coinIcon : null,
                Progress.Coins.ToString("N0"), new Vector2(0.82f, 0.94f), new Vector2(330f, 82f),
                new Color(0.08f, 0.11f, 0.18f, 0.96f));
            menuGemsText = AddResourceCapsule(panel.transform, theme != null ? theme.gemIcon : null,
                Progress.Gems.ToString("N0"), new Vector2(0.69f, 0.94f), new Vector2(270f, 82f),
                new Color(0.08f, 0.11f, 0.18f, 0.96f));
            menuEnergyText = AddResourceCapsule(panel.transform, theme != null ? theme.energyIcon : null,
                Progress.Energy + " / 60", new Vector2(0.56f, 0.94f), new Vector2(310f, 82f),
                new Color(0.05f, 0.18f, 0.16f, 0.96f));
            AddCircleIconButton(panel.transform, theme != null ? theme.settingsIcon : null,
                new Vector2(0.955f, 0.94f), new Vector2(112f, 112f), OnSettingsPressed);

            AddLobbyBrand(panel.transform);
            AddLobbyLeftStack(panel.transform);

            AddQuickDock(panel.transform);
            AddStageBattleCta(panel.transform);
            AddBottomNav(panel.transform);

            var version = MakeBody(panel.transform, "PREVIEW BUILD", 24f, new Color(1f, 1f, 1f, 0.42f));
            var vrt = version.rectTransform;
            vrt.anchorMin = vrt.anchorMax = new Vector2(0.97f, 0.03f);
            vrt.pivot = new Vector2(1f, 0f);
            vrt.sizeDelta = new Vector2(300f, 50f);
            version.alignment = TextAlignmentOptions.BottomRight;

            return panel;
        }

        void AddLobbyBackdrop(Transform root)
        {
            if (theme == null || theme.lobbyBackgroundLeft == null)
            {
                AddSolidBand(root, "Backdrop", Vector2.zero, Vector2.one, new Color(0.05f, 0.19f, 0.38f, 1f));
                return;
            }

            AddBackdropImage(root, "BgLeft", theme.lobbyBackgroundLeft, new Vector2(0f, 0f), new Vector2(0.36f, 1f),
                new Color(1f, 1f, 1f, 0.34f));
            AddBackdropImage(root, "BgMiddle", theme.lobbyBackgroundMiddle, new Vector2(0.32f, 0f), new Vector2(0.69f, 1f),
                new Color(1f, 1f, 1f, 0.06f));
            AddBackdropImage(root, "BgRight", theme.lobbyBackgroundRight, new Vector2(0.64f, 0f), Vector2.one,
                new Color(1f, 1f, 1f, 0.34f));
            AddBackdropImage(root, "BottomGlowGreen", theme.lobbyBottomGlowGreen, new Vector2(0f, 0f), new Vector2(0.62f, 0.34f),
                new Color(1f, 1f, 1f, 0.3f));
            AddBackdropImage(root, "BottomGlowBlue", theme.lobbyBottomGlowBlue, new Vector2(0.38f, 0f), Vector2.one,
                new Color(1f, 1f, 1f, 0.22f));
            AddBackdropImage(root, "ScreenGlow", theme.lobbyScreenGlow, Vector2.zero, Vector2.one,
                new Color(1f, 1f, 1f, 0.08f));
        }

        void AddBackdropImage(Transform root, string name, Sprite sprite, Vector2 anchorMin, Vector2 anchorMax, Color color)
        {
            if (sprite == null) return;
            var go = NewRect(name, root);
            var rt = (RectTransform)go.transform;
            rt.anchorMin = anchorMin;
            rt.anchorMax = anchorMax;
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;
            var img = go.AddComponent<Image>();
            img.sprite = sprite;
            img.color = color;
            img.raycastTarget = false;
        }

        void AddLobbyBrand(Transform root)
        {
            var title = MakeHeading(root, "ARCANE ARENA", 76f, new Color(1f, 0.86f, 0.24f));
            var trt = title.rectTransform;
            trt.anchorMin = trt.anchorMax = new Vector2(0.49f, 0.84f);
            trt.sizeDelta = new Vector2(820f, 92f);

            var subtitle = MakeButtonLabel(root, ModeTrialTitle(Progress.SelectedMode),
                30f, new Color(0.72f, 0.95f, 1f));
            var srt = subtitle.rectTransform;
            srt.anchorMin = srt.anchorMax = new Vector2(0.49f, 0.79f);
            srt.sizeDelta = new Vector2(620f, 44f);
            menuRefreshers.Add(() => subtitle.text = ModeTrialTitle(Progress.SelectedMode));
        }

        void AddLobbyLeftStack(Transform root)
        {
            AddBrawlerProgressStrip(root);
        }

        void AddBrawlerProgressStrip(Transform root)
        {
            var card = AddMenuCard(root, "BrawlerProgressStrip", new Vector2(0.16f, 0.5f),
                new Vector2(620f, 260f), new Color(0.035f, 0.12f, 0.25f, 0.96f));
            card.GetComponent<Image>().raycastTarget = true;
            card.AddComponent<Button>().onClick.AddListener(OnBrawlersPressed);

            var tag = MakeButtonLabel(card.transform, "CURRENT HERO", 32f, new Color(0.56f, 0.88f, 1f));
            var tagRt = tag.rectTransform;
            tagRt.anchorMin = tagRt.anchorMax = new Vector2(0.53f, 0.8f);
            tagRt.sizeDelta = new Vector2(350f, 40f);
            tag.alignment = TextAlignmentOptions.Left;

            var frame = NewRect("PortraitFrame", card.transform);
            var frt = (RectTransform)frame.transform;
            frt.anchorMin = frt.anchorMax = new Vector2(0.19f, 0.52f);
            frt.sizeDelta = new Vector2(152f, 168f);
            var fi = frame.AddComponent<Image>();
            fi.sprite = theme != null ? theme.profileFrame : null;
            if (fi.sprite != null) fi.type = Image.Type.Sliced;
            fi.color = new Color(0.03f, 0.14f, 0.3f, 0.8f);
            fi.raycastTarget = false;

            mainHeroPortrait = NewRect("Portrait", frame.transform).AddComponent<Image>();
            var prt = mainHeroPortrait.rectTransform;
            prt.anchorMin = prt.anchorMax = new Vector2(0.5f, 0.5f);
            prt.sizeDelta = new Vector2(126f, 148f);
            mainHeroPortrait.preserveAspect = true;
            mainHeroPortrait.raycastTarget = false;

            mainHeroName = MakeHeading(card.transform, "", 50f, Color.white);
            var nrt = mainHeroName.rectTransform;
            nrt.anchorMin = nrt.anchorMax = new Vector2(0.63f, 0.62f);
            nrt.sizeDelta = new Vector2(380f, 58f);
            mainHeroName.alignment = TextAlignmentOptions.Left;

            mainHeroMeta = MakeButtonLabel(card.transform, "", 29f, new Color(1f, 0.86f, 0.32f));
            var mrt = mainHeroMeta.rectTransform;
            mrt.anchorMin = mrt.anchorMax = new Vector2(0.63f, 0.43f);
            mrt.sizeDelta = new Vector2(380f, 38f);
            mainHeroMeta.alignment = TextAlignmentOptions.Left;

            mainHeroProgressFill = AddProgressBar(card.transform, new Vector2(0.63f, 0.23f),
                new Vector2(360f, 30f), new Color(0.55f, 0.9f, 1f));
            mainHeroProgress = MakeButtonLabel(card.transform, "", 27f, Color.white);
            var hprt = mainHeroProgress.rectTransform;
            hprt.anchorMin = hprt.anchorMax = new Vector2(0.63f, 0.23f);
            hprt.sizeDelta = new Vector2(360f, 36f);

            mainLoadoutText = MakeButtonLabel(card.transform, "", 23f, new Color(1f, 1f, 1f, 0.92f));
            var lrt = mainLoadoutText.rectTransform;
            lrt.anchorMin = lrt.anchorMax = new Vector2(0.87f, 0.83f);
            lrt.sizeDelta = new Vector2(120f, 40f);
            AddBadge(card.transform, "EDIT", new Vector2(0.87f, 0.13f), new Vector2(112f, 44f), new Color(0.16f, 0.9f, 0.35f, 1f));
        }

        void AddPassProgressStrip(Transform root)
        {
            var pass = AddMenuCard(root, "PassProgressStrip", new Vector2(0.16f, 0.36f),
                new Vector2(620f, 190f), new Color(0.28f, 0.12f, 0.62f, 0.92f));
            pass.GetComponent<Image>().raycastTarget = true;
            pass.AddComponent<Button>().onClick.AddListener(OnRewardsPressed);

            AddLargeIcon(pass.transform, theme != null ? theme.rewardsIcon : null,
                new Vector2(0.17f, 0.52f), new Vector2(104f, 104f));

            var title = MakeHeading(pass.transform, "ARCANE PASS", 40f, Color.white);
            var trt = title.rectTransform;
            trt.anchorMin = trt.anchorMax = new Vector2(0.55f, 0.69f);
            trt.sizeDelta = new Vector2(380f, 52f);
            title.alignment = TextAlignmentOptions.Left;

            var season = MakeButtonLabel(pass.transform, "SEASON 1", 25f, new Color(1f, 0.86f, 0.26f));
            var srt = season.rectTransform;
            srt.anchorMin = srt.anchorMax = new Vector2(0.55f, 0.48f);
            srt.sizeDelta = new Vector2(380f, 34f);
            season.alignment = TextAlignmentOptions.Left;

            mainQuestFill = AddProgressBar(pass.transform, new Vector2(0.55f, 0.25f),
                new Vector2(360f, 28f), new Color(0.3f, 0.95f, 1f));
            mainQuestText = MakeButtonLabel(pass.transform, "", 23f, Color.white);
            var qtr = mainQuestText.rectTransform;
            qtr.anchorMin = qtr.anchorMax = new Vector2(0.55f, 0.25f);
            qtr.sizeDelta = new Vector2(360f, 34f);
            AddBadge(pass.transform, "FREE", new Vector2(0.84f, 0.76f), new Vector2(112f, 48f), new Color(0.1f, 0.86f, 0.28f, 1f));
        }

        void AddSolidBand(Transform root, string name, Vector2 anchorMin, Vector2 anchorMax, Color color)
        {
            var go = NewRect(name, root);
            var rt = (RectTransform)go.transform;
            rt.anchorMin = anchorMin;
            rt.anchorMax = anchorMax;
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;
            var img = go.AddComponent<Image>();
            img.color = color;
            img.raycastTarget = false;
        }

        void AddPlayerProgress(Transform root)
        {
            var chip = NewRect("PlayerProgress", root);
            var rt = (RectTransform)chip.transform;
            rt.anchorMin = rt.anchorMax = new Vector2(0.19f, 0.94f);
            rt.sizeDelta = new Vector2(680f, 124f);
            var bg = chip.AddComponent<Image>();
            if (theme != null && theme.labelChip != null)
            {
                bg.sprite = theme.labelChip;
                bg.type = Image.Type.Sliced;
            }
            bg.color = new Color(0.08f, 0.15f, 0.29f, 0.96f);
            bg.raycastTarget = false;

            int bestLevel = 1;
            int bestPoints = 0;
            if (roster != null)
            {
                foreach (var def in roster)
                {
                    if (def == null) continue;
                    var p = Progress.Get(def.id);
                    if (p.level > bestLevel || (p.level == bestLevel && p.points > bestPoints))
                    {
                        bestLevel = p.level;
                        bestPoints = p.points;
                    }
                }
            }

            var level = NewRect("Level", chip.transform);
            var lrt = (RectTransform)level.transform;
            lrt.anchorMin = lrt.anchorMax = new Vector2(0.08f, 0.5f);
            lrt.sizeDelta = new Vector2(110f, 110f);
            var li = level.AddComponent<Image>();
            li.sprite = theme != null ? theme.levelFrame : null;
            li.preserveAspect = true;
            li.color = li.sprite != null ? Color.white : new Color(0.05f, 0.75f, 1f);
            li.raycastTarget = false;
            var lv = MakeButtonLabel(level.transform, bestLevel.ToString(), 50f, Color.white);
            Stretch(lv.rectTransform);
            lv.rectTransform.offsetMin = new Vector2(0f, 8f);

            var name = MakeHeading(chip.transform, "APPRENTICE", 46f, Color.white);
            var nrt = name.rectTransform;
            nrt.anchorMin = nrt.anchorMax = new Vector2(0.35f, 0.72f);
            nrt.sizeDelta = new Vector2(340f, 58f);
            name.alignment = TextAlignmentOptions.Left;

            var barBg = NewRect("ProgressBg", chip.transform);
            var brt = (RectTransform)barBg.transform;
            brt.anchorMin = brt.anchorMax = new Vector2(0.56f, 0.35f);
            brt.sizeDelta = new Vector2(440f, 34f);
            var barImg = barBg.AddComponent<Image>();
            if (theme != null && theme.barBg != null)
            {
                barImg.sprite = theme.barBg;
                barImg.type = Image.Type.Sliced;
            }
            barImg.color = new Color(0.02f, 0.04f, 0.09f, 0.9f);
            barImg.raycastTarget = false;

            var fill = NewRect("ProgressFill", barBg.transform);
            var frt = (RectTransform)fill.transform;
            frt.anchorMin = Vector2.zero;
            frt.anchorMax = new Vector2(Mathf.Clamp01(bestPoints / (float)Progress.PointsNeeded(bestLevel)), 1f);
            frt.offsetMin = new Vector2(4f, 4f);
            frt.offsetMax = new Vector2(-4f, -4f);
            var fillImg = fill.AddComponent<Image>();
            if (theme != null && theme.barFillBlue != null)
            {
                fillImg.sprite = theme.barFillBlue;
                fillImg.type = Image.Type.Sliced;
            }
            fillImg.color = new Color(0.1f, 0.82f, 1f);
            fillImg.raycastTarget = false;

            var points = MakeButtonLabel(chip.transform, $"{bestPoints}/{Progress.PointsNeeded(bestLevel)} AP", 34f, Color.white);
            var prt = points.rectTransform;
            prt.anchorMin = prt.anchorMax = new Vector2(0.56f, 0.35f);
            prt.sizeDelta = new Vector2(420f, 42f);
        }

        TextMeshProUGUI AddResourceCapsule(Transform root, Sprite icon, string value, Vector2 anchor,
            Vector2 size, Color tint)
        {
            var capsule = NewRect("Resource", root);
            var rt = (RectTransform)capsule.transform;
            rt.anchorMin = rt.anchorMax = anchor;
            rt.sizeDelta = size;
            var bg = capsule.AddComponent<Image>();
            if (theme != null && theme.resourceCapsule != null)
            {
                bg.sprite = theme.resourceCapsule;
                bg.type = Image.Type.Sliced;
            }
            bg.color = tint;
            bg.raycastTarget = false;

            if (icon != null)
            {
                var iconGo = NewRect("Icon", capsule.transform);
                var irt = (RectTransform)iconGo.transform;
                irt.anchorMin = irt.anchorMax = new Vector2(0.16f, 0.5f);
                irt.sizeDelta = new Vector2(size.y * 0.86f, size.y * 0.9f);
                var img = iconGo.AddComponent<Image>();
                img.sprite = icon;
                img.preserveAspect = true;
                img.raycastTarget = false;
            }

            var text = MakeButtonLabel(capsule.transform, value, 42f, Color.white);
            var trt = text.rectTransform;
            trt.anchorMin = new Vector2(0.28f, 0f);
            trt.anchorMax = new Vector2(theme != null && theme.resourcePlusButton != null ? 0.82f : 0.95f, 1f);
            trt.offsetMin = Vector2.zero;
            trt.offsetMax = Vector2.zero;

            if (theme != null && theme.resourcePlusButton != null)
            {
                var plus = NewRect("Plus", capsule.transform);
                var prt = (RectTransform)plus.transform;
                prt.anchorMin = prt.anchorMax = new Vector2(0.91f, 0.5f);
                prt.sizeDelta = new Vector2(size.y * 0.74f, size.y * 0.74f);
                var pi = plus.AddComponent<Image>();
                pi.sprite = theme.resourcePlusButton;
                pi.preserveAspect = true;
                pi.raycastTarget = false;

                if (theme.resourcePlusIcon != null)
                {
                    var glyph = NewRect("Glyph", plus.transform);
                    var grt = (RectTransform)glyph.transform;
                    grt.anchorMin = grt.anchorMax = new Vector2(0.5f, 0.5f);
                    grt.sizeDelta = new Vector2(size.y * 0.28f, size.y * 0.28f);
                    var gi = glyph.AddComponent<Image>();
                    gi.sprite = theme.resourcePlusIcon;
                    gi.preserveAspect = true;
                    gi.raycastTarget = false;
                }
            }
            return text;
        }

        void AddQuickDock(Transform root)
        {
            var dock = NewRect("QuickDock", root);
            var rt = (RectTransform)dock.transform;
            rt.anchorMin = rt.anchorMax = new Vector2(0.885f, 0.51f);
            rt.sizeDelta = new Vector2(500f, 660f);
            var bg = dock.AddComponent<Image>();
            if (theme != null && theme.panel != null)
            {
                bg.sprite = theme.panel;
                bg.type = Image.Type.Sliced;
            }
            bg.color = new Color(0.025f, 0.09f, 0.18f, 0.9f);
            bg.raycastTarget = false;

            var heading = MakeButtonLabel(dock.transform, "QUICK ACCESS", 30f,
                new Color(0.62f, 0.88f, 1f));
            var hrt = heading.rectTransform;
            hrt.anchorMin = hrt.anchorMax = new Vector2(0.5f, 0.93f);
            hrt.sizeDelta = new Vector2(430f, 48f);

            AddIconTile(dock.transform, "RANK", theme != null ? theme.rankingIcon : null,
                new Vector2(0.29f, 0.72f), OnRankingPressed, null);
            AddIconTile(dock.transform, "PASS", theme != null ? theme.rewardsIcon : null,
                new Vector2(0.71f, 0.72f), OnRewardsPressed, "FREE");
            AddIconTile(dock.transform, "TRIALS", theme != null ? theme.missionIcon : null,
                new Vector2(0.29f, 0.43f), OnMissionsPressed, "2");
            AddIconTile(dock.transform, "COVEN", theme != null ? theme.friendsIcon : null,
                new Vector2(0.71f, 0.43f), OnFriendsPressed, "1");
            AddIconTile(dock.transform, "INBOX", theme != null ? (theme.inboxIcon != null ? theme.inboxIcon : theme.inventoryIcon) : null,
                new Vector2(0.29f, 0.14f), OnInboxPressed, "3");
            AddIconTile(dock.transform, "NEWS", theme != null ? (theme.newsIcon != null ? theme.newsIcon : theme.missionIcon) : null,
                new Vector2(0.71f, 0.14f), OnNoticePressed, "NEW");
        }

        void AddStageBattleCta(Transform root)
        {
            var stage = NewRect("StageCta", root);
            var rt = (RectTransform)stage.transform;
            rt.anchorMin = rt.anchorMax = new Vector2(0.77f, 0.095f);
            rt.sizeDelta = new Vector2(970f, 170f);
            var img = stage.AddComponent<Image>();
            if (theme != null && theme.panel != null)
            {
                img.sprite = theme.panel;
                img.type = Image.Type.Sliced;
            }
            img.color = new Color(0.025f, 0.13f, 0.27f, 0.98f);
            stage.AddComponent<Button>().onClick.AddListener(OnPlayPressed);

            if (theme != null && theme.mapIcon != null)
            {
                var icon = NewRect("Icon", stage.transform);
                var irt = (RectTransform)icon.transform;
                irt.anchorMin = irt.anchorMax = new Vector2(0.09f, 0.5f);
                irt.sizeDelta = new Vector2(108f, 108f);
                var ii = icon.AddComponent<Image>();
                ii.sprite = theme.mapIcon;
                ii.preserveAspect = true;
                ii.raycastTarget = false;
            }

            var label = MakeButtonLabel(stage.transform, "SELECTED TRIAL", 25f, new Color(0.62f, 0.9f, 1f));
            var labelRt = label.rectTransform;
            labelRt.anchorMin = labelRt.anchorMax = new Vector2(0.33f, 0.73f);
            labelRt.sizeDelta = new Vector2(350f, 38f);
            label.alignment = TextAlignmentOptions.Left;

            stageModeText = MakeHeading(stage.transform, ModeTitle(MatchSetup.Mode), 44f,
                Color.white);
            var lrt = stageModeText.rectTransform;
            lrt.anchorMin = lrt.anchorMax = new Vector2(0.33f, 0.5f);
            lrt.sizeDelta = new Vector2(360f, 60f);
            stageModeText.alignment = TextAlignmentOptions.Left;
            menuRefreshers.Add(() => stageModeText.text = ModeTitle(Progress.SelectedMode));

            var cost = MakeButtonLabel(stage.transform, "5 ENERGY  /  CHANGE MODE", 24f, new Color(1f, 0.86f, 0.3f));
            var crt = cost.rectTransform;
            crt.anchorMin = crt.anchorMax = new Vector2(0.33f, 0.24f);
            crt.sizeDelta = new Vector2(390f, 36f);
            cost.alignment = TextAlignmentOptions.Left;

            MakeButton(stage.transform, "PLAY", theme != null ? theme.buttonYellow : null,
                new Vector2(0.82f, 0.5f), new Vector2(300f, 122f), 54f, OnQuickBattlePressed);
        }

        void AddBottomNav(Transform root)
        {
            var bar = NewRect("BottomNav", root);
            var rt = (RectTransform)bar.transform;
            rt.anchorMin = new Vector2(0f, 0f);
            rt.anchorMax = new Vector2(0.58f, 0.17f);
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;
            var bg = bar.AddComponent<Image>();
            if (theme != null && theme.bottomBar != null)
            {
                bg.sprite = theme.bottomBar;
                bg.type = Image.Type.Sliced;
            }
            bg.color = new Color(0.03f, 0.13f, 0.27f, 0.92f);
            bg.raycastTarget = false;

            AddNavButton(root, "SPELLBOOK", theme != null ? theme.shopIcon : null,
                new Vector2(0.07f, 0.09f), false, OnShopPressed, "FREE");
            AddNavButton(root, "RELICS", theme != null ? theme.inventoryIcon : null,
                new Vector2(0.195f, 0.09f), false, OnInventoryPressed, null);
            AddNavButton(root, "RUNES", theme != null ? theme.cardsIcon : null,
                new Vector2(0.32f, 0.09f), false, OnCardsPressed, "NEW");
            AddNavButton(root, "HEROES", theme != null ? (theme.clanIcon != null ? theme.clanIcon : theme.friendsIcon) : null,
                new Vector2(0.445f, 0.09f), false, OnBrawlersPressed, null);
        }

        void AddNavButton(Transform root, string label, Sprite icon, Vector2 anchor, bool selected,
            UnityEngine.Events.UnityAction onClick, string badge = null)
        {
            var btn = NewRect("Nav_" + label, root);
            var rt = (RectTransform)btn.transform;
            rt.anchorMin = rt.anchorMax = anchor;
            rt.sizeDelta = new Vector2(220f, 162f);
            var hit = btn.AddComponent<Image>();
            hit.sprite = theme != null ? (selected ? theme.buttonSquareSky : theme.buttonSquareNavy) : null;
            if (hit.sprite != null) hit.type = Image.Type.Sliced;
            hit.color = selected ? Color.white : new Color(0.9f, 0.98f, 1f, 0.96f);
            btn.AddComponent<Button>().onClick.AddListener(onClick);

            if (icon != null)
            {
                var iconGo = NewRect("Icon", btn.transform);
                var irt = (RectTransform)iconGo.transform;
                irt.anchorMin = irt.anchorMax = new Vector2(0.5f, 0.62f);
                irt.sizeDelta = new Vector2(82f, 82f);
                var ii = iconGo.AddComponent<Image>();
                ii.sprite = icon;
                ii.preserveAspect = true;
                ii.raycastTarget = false;
            }
            var text = MakeButtonLabel(btn.transform, label, label.Length > 7 ? 22f : 27f, Color.white);
            var trt = text.rectTransform;
            trt.anchorMin = new Vector2(0f, 0.02f);
            trt.anchorMax = new Vector2(1f, 0.35f);
            trt.offsetMin = Vector2.zero;
            trt.offsetMax = Vector2.zero;

            if (!string.IsNullOrEmpty(badge))
                AddBadge(btn.transform, badge, new Vector2(0.8f, 0.83f), new Vector2(badge.Length > 2 ? 92f : 54f, 42f),
                    badge == "FREE" ? new Color(0.12f, 0.86f, 0.26f, 1f) : new Color(0.95f, 0.12f, 0.14f, 1f));
        }

        void AddIconTile(Transform root, string label, Sprite icon, Vector2 anchor,
            UnityEngine.Events.UnityAction onClick, string badge = null)
        {
            var tile = NewRect("Tile_" + label, root);
            var rt = (RectTransform)tile.transform;
            rt.anchorMin = rt.anchorMax = anchor;
            rt.sizeDelta = new Vector2(168f, 150f);
            var hit = tile.AddComponent<Image>();
            hit.sprite = theme != null ? theme.buttonSquareBlue : null;
            if (hit.sprite != null) hit.type = Image.Type.Sliced;
            hit.color = new Color(0.86f, 0.98f, 1f, 0.96f);
            tile.AddComponent<Button>().onClick.AddListener(onClick);

            if (icon != null)
            {
                var iconGo = NewRect("Icon", tile.transform);
                var irt = (RectTransform)iconGo.transform;
                irt.anchorMin = irt.anchorMax = new Vector2(0.5f, 0.63f);
                irt.sizeDelta = new Vector2(78f, 78f);
                var ii = iconGo.AddComponent<Image>();
                ii.sprite = icon;
                ii.preserveAspect = true;
                ii.raycastTarget = false;
            }
            var text = MakeButtonLabel(tile.transform, label, label.Length > 6 ? 21f : 24f, Color.white);
            var trt = text.rectTransform;
            trt.anchorMin = new Vector2(0f, 0.04f);
            trt.anchorMax = new Vector2(1f, 0.35f);
            trt.offsetMin = Vector2.zero;
            trt.offsetMax = Vector2.zero;

            if (!string.IsNullOrEmpty(badge))
                AddBadge(tile.transform, badge, new Vector2(0.82f, 0.85f), new Vector2(badge.Length > 2 ? 82f : 48f, 40f),
                    badge == "FREE" ? new Color(0.12f, 0.86f, 0.26f, 1f) : new Color(0.95f, 0.12f, 0.14f, 1f));
        }

        string NavLabel(string label)
        {
            switch (label)
            {
                case "BRAWLERS": return "Heroes";
                case "RANKING": return "Ranking";
                case "QUESTS": return "Quests";
                case "REWARDS": return "Rewards";
                case "FRIENDS": return "Friends";
                case "INBOX": return "Inbox";
                case "NOTICE": return "News";
                case "SETTINGS": return "Settings";
                case "SHOP": return "Shop";
                case "CARDS": return "Cards";
                case "BAG": return "Bag";
                default: return label;
            }
        }

        void AddCircleIconButton(Transform root, Sprite icon, Vector2 anchor, Vector2 size,
            UnityEngine.Events.UnityAction onClick)
        {
            var btn = NewRect("IconButton", root);
            var rt = (RectTransform)btn.transform;
            rt.anchorMin = rt.anchorMax = anchor;
            rt.sizeDelta = size;
            var bg = btn.AddComponent<Image>();
            bg.sprite = theme != null ? theme.buttonRound : null;
            bg.preserveAspect = true;
            bg.color = Color.white;
            bg.raycastTarget = onClick != null;
            if (onClick != null) btn.AddComponent<Button>().onClick.AddListener(onClick);
            if (icon == null) return;
            var iconGo = NewRect("Icon", btn.transform);
            var irt = (RectTransform)iconGo.transform;
            irt.anchorMin = irt.anchorMax = new Vector2(0.5f, 0.53f);
            irt.sizeDelta = size * 0.42f;
            var ii = iconGo.AddComponent<Image>();
            ii.sprite = icon;
            ii.preserveAspect = true;
            ii.color = new Color(0.18f, 0.22f, 0.32f);
            ii.raycastTarget = false;
        }

        void AddShopTab(Transform root, int index, string label, Vector2 anchor,
            UnityEngine.Events.UnityAction onClick)
        {
            var tab = NewRect("Tab_" + label, root);
            var rt = (RectTransform)tab.transform;
            rt.anchorMin = rt.anchorMax = anchor;
            rt.sizeDelta = new Vector2(360f, 92f);
            var bg = tab.AddComponent<Image>();
            shopTabBackgrounds[index] = bg;
            if (onClick != null) tab.AddComponent<Button>().onClick.AddListener(onClick);

            var text = MakeButtonLabel(tab.transform, label, 42f, Color.white);
            Stretch(text.rectTransform);
            text.rectTransform.offsetMin = new Vector2(0f, 12f);
            shopTabLabels[index] = text;
            SetShopTab(index, index == activeShopTab);
        }

        void SetShopTab(int index)
        {
            activeShopTab = Mathf.Clamp(index, 0, shopTabBackgrounds.Length - 1);
            for (int i = 0; i < shopTabBackgrounds.Length; i++)
                SetShopTab(i, i == activeShopTab);
        }

        void SetShopTab(int index, bool selected)
        {
            if (index < 0 || index >= shopTabBackgrounds.Length) return;
            var bg = shopTabBackgrounds[index];
            if (bg != null)
            {
                if (selected && theme != null && theme.menuTopButtonFocus != null)
                    bg.sprite = theme.menuTopButtonFocus;
                else if (theme != null && theme.menuTopButton != null)
                    bg.sprite = theme.menuTopButton;
                if (bg.sprite != null) bg.type = Image.Type.Sliced;
                bg.color = selected ? new Color(0.96f, 0.98f, 1f, 1f) : new Color(0.05f, 0.25f, 0.45f, 0.94f);
            }
            if (shopTabLabels[index] != null)
                shopTabLabels[index].color = selected ? Color.white : new Color(0.35f, 0.75f, 1f);
        }

        void UpdateShopTabFromScroll(float normalizedPosition)
        {
            int index = normalizedPosition > 0.62f ? 0 :
                normalizedPosition > 0.34f ? 1 :
                normalizedPosition > 0.12f ? 2 : 3;
            if (index != activeShopTab) SetShopTab(index);
        }

        GameObject BuildModePanel(Transform root)
        {
            var panel = NewRect("ModeSelect", root);
            Stretch((RectTransform)panel.transform);

            AddSolidBand(panel.transform, "HeaderChrome", new Vector2(0f, 0.84f), Vector2.one,
                new Color(0.015f, 0.055f, 0.12f, 0.84f));
            AddSolidBand(panel.transform, "FooterChrome", Vector2.zero, new Vector2(1f, 0.11f),
                new Color(0.01f, 0.035f, 0.08f, 0.72f));
            AddSolidBand(panel.transform, "CenterVignette", new Vector2(0.14f, 0.11f), new Vector2(0.86f, 0.84f),
                new Color(0.01f, 0.04f, 0.09f, 0.12f));
            AddScreenTitle(panel.transform, "CHOOSE YOUR TRIAL");
            AddBackButton(panel.transform, OnBackToMain);

            BuildModeCard(panel.transform, -570f, "CONTROL ZONE",
                "The primary 3v3 trial.\nHold the center and reach 90.\nTied regulation expands overtime.",
                theme != null ? theme.mapIcon : null, new Color(0.35f, 0.88f, 1f),
                () => OnModePicked(GameMode.ControlZone));
            BuildModeCard(panel.transform, 0f, "KNOCKOUT",
                "A ruthless 5v5 hero clash.\nFirst clan to 8 banishments wins.",
                theme != null ? theme.swordIcon : null, new Color(1f, 0.45f, 0.3f),
                () => OnModePicked(GameMode.Knockout));
            BuildModeCard(panel.transform, 570f, "GEM GRAB",
                "Arcane crystals surge from the nexus.\nHold 10 for 15 seconds to win - but\nbanishment scatters every crystal!",
                theme != null ? theme.gemIcon : null, new Color(0.35f, 0.95f, 0.6f),
                () => OnModePicked(GameMode.GemGrab));

            return panel;
        }

        void BuildModeCard(Transform root, float x, string name, string blurb,
            Sprite icon, Color accent, UnityEngine.Events.UnityAction onPick)
        {
            var card = NewRect("Card_" + name, root);
            var rt = (RectTransform)card.transform;
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.46f);
            rt.anchoredPosition = new Vector2(x, 0f);
            rt.sizeDelta = new Vector2(520f, 780f);

            var bg = card.AddComponent<Image>();
            if (theme != null && theme.stageCard != null)
            {
                bg.sprite = theme.stageCard;
                bg.type = Image.Type.Sliced;
            }
            else
            {
                bg.color = new Color(0.07f, 0.37f, 0.66f, 0.98f);
            }
            bg.color = new Color(0.07f, 0.37f, 0.66f, 0.98f);

            var focus = NewRect("Focus", card.transform);
            Stretch((RectTransform)focus.transform);
            var focusImg = focus.AddComponent<Image>();
            if (theme != null && theme.stageFocus != null)
            {
                focusImg.sprite = theme.stageFocus;
                focusImg.type = Image.Type.Sliced;
            }
            focusImg.color = new Color(accent.r, accent.g, accent.b, 0.34f);
            focusImg.raycastTarget = false;

            if (icon != null)
            {
                var iconGo = NewRect("Icon", card.transform);
                var irt = (RectTransform)iconGo.transform;
                irt.anchorMin = irt.anchorMax = new Vector2(0.5f, 0.72f);
                irt.sizeDelta = new Vector2(250f, 250f);
                var img = iconGo.AddComponent<Image>();
                img.sprite = icon;
                img.preserveAspect = true;
                img.raycastTarget = false;
            }

            var title = MakeHeading(card.transform, name, 76f, accent);
            var trt = title.rectTransform;
            trt.anchorMin = trt.anchorMax = new Vector2(0.5f, 0.49f);
            trt.sizeDelta = new Vector2(650f, 110f);

            var body = MakeBody(card.transform, blurb, 38f, new Color(1f, 1f, 1f, 0.92f));
            var brt = body.rectTransform;
            brt.anchorMin = brt.anchorMax = new Vector2(0.5f, 0.31f);
            brt.sizeDelta = new Vector2(600f, 260f);

            MakeButton(card.transform, "SELECT", theme != null ? theme.buttonGreen : null,
                new Vector2(0.5f, 0.09f), new Vector2(420f, 145f), 60f, onPick);
        }

        GameObject BuildCharacterPanel(Transform root)
        {
            var panel = NewRect("CharacterSelect", root);
            Stretch((RectTransform)panel.transform);

            // The 3D hero is the primary visual. Restrict contrast surfaces to
            // the edges instead of dimming the full world behind the overlay.
            AddSolidBand(panel.transform, "HeaderChrome", new Vector2(0f, 0.84f), Vector2.one,
                new Color(0.015f, 0.055f, 0.12f, 0.84f));
            AddSolidBand(panel.transform, "FooterChrome", Vector2.zero, new Vector2(1f, 0.14f),
                new Color(0.01f, 0.035f, 0.08f, 0.86f));
            AddSolidBand(panel.transform, "LeftScrim", new Vector2(0f, 0.14f), new Vector2(0.285f, 0.84f),
                new Color(0.01f, 0.04f, 0.09f, 0.42f));
            AddSolidBand(panel.transform, "RightScrim", new Vector2(0.72f, 0.14f), new Vector2(1f, 0.84f),
                new Color(0.01f, 0.04f, 0.09f, 0.48f));
            AddScreenTitle(panel.transform, "CHOOSE YOUR HERO");
            AddBackButton(panel.transform, OnBackToMode);
            AddSchoolRail(panel.transform);

            // Drag anywhere in the center rotates the 3D preview.
            var spin = NewRect("SpinZone", panel.transform);
            var sprt = (RectTransform)spin.transform;
            sprt.anchorMin = new Vector2(0.29f, 0.14f);
            sprt.anchorMax = new Vector2(0.72f, 0.84f);
            sprt.offsetMin = Vector2.zero;
            sprt.offsetMax = Vector2.zero;
            var spinImg = spin.AddComponent<Image>();
            spinImg.color = new Color(1f, 1f, 1f, 0f);
            spin.AddComponent<PodiumSpinZone>().flow = this;

            var heroCard = NewRect("HeroCard", panel.transform);
            var hrt = (RectTransform)heroCard.transform;
            hrt.anchorMin = hrt.anchorMax = new Vector2(0.15f, 0.22f);
            hrt.sizeDelta = new Vector2(620f, 250f);
            var heroBg = heroCard.AddComponent<Image>();
            if (theme != null && theme.card != null)
            {
                heroBg.sprite = theme.card;
                heroBg.type = Image.Type.Sliced;
            }
            heroBg.color = new Color(0.03f, 0.11f, 0.22f, 0.97f);

            var focus = NewRect("Focus", heroCard.transform);
            Stretch((RectTransform)focus.transform);
            var focusImg = focus.AddComponent<Image>();
            if (theme != null && theme.cardFocus != null)
            {
                focusImg.sprite = theme.cardFocus;
                focusImg.type = Image.Type.Sliced;
            }
            focusImg.color = new Color(0.35f, 0.85f, 1f, 0.18f);
            focusImg.raycastTarget = false;

            var masteryTitle = MakeButtonLabel(heroCard.transform, "HERO MASTERY", 29f,
                new Color(0.62f, 0.88f, 1f));
            var masteryTitleRt = masteryTitle.rectTransform;
            masteryTitleRt.anchorMin = masteryTitleRt.anchorMax = new Vector2(0.5f, 0.83f);
            masteryTitleRt.sizeDelta = new Vector2(520f, 42f);

            var identity = AddMenuCard(panel.transform, "SelectedHeroIdentity", new Vector2(0.5f, 0.205f),
                new Vector2(650f, 185f), new Color(0.025f, 0.09f, 0.18f, 0.94f));

            charSchoolTag = MakeButtonLabel(identity.transform, "FIRE MAGE", 28f,
                new Color(0.62f, 0.9f, 1f));
            var tagRt = charSchoolTag.rectTransform;
            tagRt.anchorMin = tagRt.anchorMax = new Vector2(0.56f, 0.79f);
            tagRt.sizeDelta = new Vector2(500f, 42f);
            charSchoolTag.alignment = TextAlignmentOptions.Left;
            charSchoolTag.enableAutoSizing = true;
            charSchoolTag.fontSizeMin = 22f;
            charSchoolTag.fontSizeMax = 28f;

            var schoolIconGo = NewRect("SchoolIcon", identity.transform);
            var schoolIconRt = (RectTransform)schoolIconGo.transform;
            schoolIconRt.anchorMin = schoolIconRt.anchorMax = new Vector2(0.13f, 0.52f);
            schoolIconRt.sizeDelta = new Vector2(104f, 104f);
            charSchoolIcon = schoolIconGo.AddComponent<Image>();
            charSchoolIcon.preserveAspect = true;
            charSchoolIcon.raycastTarget = false;

            charName = MakeHeading(identity.transform, "", 62f, Color.white);
            var nrt = charName.rectTransform;
            nrt.anchorMin = nrt.anchorMax = new Vector2(0.56f, 0.51f);
            nrt.sizeDelta = new Vector2(500f, 76f);
            charName.alignment = TextAlignmentOptions.Left;

            // Active hero portraits are generated assets and may be absent;
            // the live 3D preview and class crest are the reliable identity.
            charPortrait = null;

            for (int i = 0; i < charStars.Length; i++)
            {
                var star = NewRect("Star" + i, heroCard.transform);
                var srt = (RectTransform)star.transform;
                srt.anchorMin = srt.anchorMax = new Vector2(0.3f + i * 0.1f, 0.56f);
                srt.sizeDelta = new Vector2(42f, 42f);
                charStars[i] = star.AddComponent<Image>();
                charStars[i].sprite = theme != null ? theme.starOffIcon : null;
                charStars[i].preserveAspect = true;
                charStars[i].raycastTarget = false;
            }

            var progressBar = NewRect("HeroProgressBg", heroCard.transform);
            var pbrt = (RectTransform)progressBar.transform;
            pbrt.anchorMin = pbrt.anchorMax = new Vector2(0.66f, 0.25f);
            pbrt.sizeDelta = new Vector2(330f, 56f);
            var pbImg = progressBar.AddComponent<Image>();
            if (theme != null && theme.barBg != null)
            {
                pbImg.sprite = theme.barBg;
                pbImg.type = Image.Type.Sliced;
            }
            pbImg.color = new Color(0.03f, 0.06f, 0.14f, 0.92f);
            pbImg.raycastTarget = false;

            charPoints = MakeButtonLabel(progressBar.transform, "", 31f, Color.white);
            Stretch(charPoints.rectTransform);

            charLevel = MakeButtonLabel(heroCard.transform, "", 36f, new Color(1f, 0.86f, 0.28f));
            var lrt = charLevel.rectTransform;
            lrt.anchorMin = lrt.anchorMax = new Vector2(0.22f, 0.25f);
            lrt.sizeDelta = new Vector2(220f, 58f);

            charRole = MakeBody(identity.transform, "", 29f, new Color(0.74f, 0.88f, 0.98f));
            var rrt = charRole.rectTransform;
            rrt.anchorMin = rrt.anchorMax = new Vector2(0.56f, 0.21f);
            rrt.sizeDelta = new Vector2(500f, 42f);
            charRole.alignment = TextAlignmentOptions.Left;

            charKind = null;

            // Description + stats card on the right.
            var info = NewRect("InfoCard", panel.transform);
            var irt = (RectTransform)info.transform;
            irt.anchorMin = irt.anchorMax = new Vector2(0.855f, 0.49f);
            irt.sizeDelta = new Vector2(650f, 880f);
            var infoBg = info.AddComponent<Image>();
            if (theme != null && theme.panel != null)
            {
                infoBg.sprite = theme.panel;
                infoBg.type = Image.Type.Sliced;
                infoBg.color = new Color(0.035f, 0.105f, 0.2f, 0.97f);
            }
            else
            {
                infoBg.color = new Color(0.035f, 0.105f, 0.2f, 0.97f);
            }

            var profileTitle = MakeButtonLabel(info.transform, "COMBAT PROFILE", 34f,
                new Color(0.62f, 0.9f, 1f));
            var profileTitleRt = profileTitle.rectTransform;
            profileTitleRt.anchorMin = profileTitleRt.anchorMax = new Vector2(0.5f, 0.93f);
            profileTitleRt.sizeDelta = new Vector2(550f, 46f);

            charDescription = MakeBody(info.transform, "", 31f, new Color(0.93f, 0.97f, 1f, 0.96f));
            var drt = charDescription.rectTransform;
            drt.anchorMin = new Vector2(0.08f, 0.7f);
            drt.anchorMax = new Vector2(0.92f, 0.87f);
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
                statFills[i] = AddStatBar(info.transform, statNames[i], statColors[i], 0.62f - i * 0.075f);

            var skillsTitle = MakeButtonLabel(info.transform, "SKILL MASTERY", 32f, new Color(1f, 0.86f, 0.3f));
            var skt = skillsTitle.rectTransform;
            skt.anchorMin = skt.anchorMax = new Vector2(0.5f, 0.37f);
            skt.sizeDelta = new Vector2(560f, 44f);
            for (int i = 0; i < 3; i++)
                AddCharacterSkillRow(info.transform, i, 0.29f - i * 0.105f);

            // Prev / next arrows flanking the podium.
            MakeArrowButton(panel.transform, new Vector2(0.31f, 0.46f), false, () => StepCharacter(-1));
            MakeArrowButton(panel.transform, new Vector2(0.69f, 0.46f), true, () => StepCharacter(1));

            MakeButton(panel.transform, "ENTER ARENA", theme != null ? theme.buttonYellow : null,
                new Vector2(0.5f, 0.065f), new Vector2(520f, 118f), 52f, OnBattlePressed);

            return panel;
        }

        void AddSchoolRail(Transform root)
        {
            schoolTabBackgrounds.Clear();
            schoolTabIcons.Clear();
            schoolTabLabels.Clear();
            if (roster == null || roster.Length == 0) return;

            int count = Mathf.Min(6, roster.Length);
            bool compact = count <= 4;
            var rail = NewRect("HeroRoster", root);
            var railRt = (RectTransform)rail.transform;
            railRt.anchorMin = railRt.anchorMax = new Vector2(0.15f, 0.575f);
            railRt.sizeDelta = new Vector2(620f, compact ? 470f : 590f);
            var railBg = rail.AddComponent<Image>();
            if (theme != null && theme.panel != null)
            {
                railBg.sprite = theme.panel;
                railBg.type = Image.Type.Sliced;
            }
            railBg.color = new Color(0.03f, 0.105f, 0.2f, 0.96f);
            railBg.raycastTarget = false;

            var title = MakeButtonLabel(rail.transform, "HERO ROSTER", 31f,
                new Color(0.62f, 0.9f, 1f));
            var titleRt = title.rectTransform;
            titleRt.anchorMin = titleRt.anchorMax = new Vector2(0.5f, compact ? 0.88f : 0.91f);
            titleRt.sizeDelta = new Vector2(520f, 44f);

            for (int i = 0; i < count; i++)
            {
                int schoolIndex = i;
                int column = i % 2;
                int row = i / 2;
                var tab = NewRect("HeroTab_" + roster[i].id, rail.transform);
                var rt = (RectTransform)tab.transform;
                rt.anchorMin = rt.anchorMax = new Vector2(
                    column == 0 ? 0.28f : 0.72f,
                    (compact ? 0.64f : 0.72f) - row * (compact ? 0.31f : 0.255f));
                rt.sizeDelta = new Vector2(245f, 128f);

                var bg = tab.AddComponent<Image>();
                bg.sprite = theme != null && theme.buttonSquareNavy != null
                    ? theme.buttonSquareNavy
                    : theme != null ? theme.card : null;
                if (bg.sprite != null) bg.type = Image.Type.Sliced;
                schoolTabBackgrounds.Add(bg);
                var button = tab.AddComponent<Button>();
                button.navigation = new Navigation { mode = Navigation.Mode.Automatic };
                button.onClick.AddListener(() => SetCharacter(schoolIndex));

                var iconGo = NewRect("ClassGlyph", tab.transform);
                var iconRt = (RectTransform)iconGo.transform;
                iconRt.anchorMin = iconRt.anchorMax = new Vector2(0.25f, 0.56f);
                iconRt.sizeDelta = new Vector2(68f, 68f);
                var icon = iconGo.AddComponent<Image>();
                icon.sprite = CharacterIcon(roster[i], i);
                icon.preserveAspect = true;
                icon.raycastTarget = false;
                schoolTabIcons.Add(icon);

                var label = MakeButtonLabel(tab.transform, roster[i].displayName.ToUpperInvariant(), 23f, Color.white);
                var labelRt = label.rectTransform;
                labelRt.anchorMin = labelRt.anchorMax = new Vector2(0.64f, 0.5f);
                labelRt.sizeDelta = new Vector2(145f, 58f);
                label.alignment = TextAlignmentOptions.Left;
                label.enableAutoSizing = true;
                label.fontSizeMin = 18f;
                label.fontSizeMax = 23f;
                schoolTabLabels.Add(label);
            }
            RefreshSchoolRail();
        }

        void RefreshSchoolRail()
        {
            for (int i = 0; i < schoolTabBackgrounds.Count; i++)
            {
                bool selected = i == charIndex;
                Color accent = SchoolAccent(i);
                schoolTabBackgrounds[i].color = selected
                    ? accent
                    : new Color(accent.r * 0.34f, accent.g * 0.34f, accent.b * 0.34f, 0.9f);
                schoolTabBackgrounds[i].transform.localScale = Vector3.one * (selected ? 1.04f : 0.97f);
                if (i < schoolTabIcons.Count)
                    schoolTabIcons[i].color = selected ? Color.white : new Color(0.72f, 0.84f, 0.96f, 0.82f);
                if (i < schoolTabLabels.Count)
                    schoolTabLabels[i].color = selected ? new Color(1f, 0.9f, 0.34f) : new Color(0.76f, 0.9f, 1f);
            }
        }

        Color SchoolAccent(int index)
        {
            BrawlerDefinition definition = roster != null && index >= 0 && index < roster.Length
                ? roster[index]
                : null;
            SpellSchool school = definition != null
                ? definition.specialty.school
                : SpellSchool.None;
            if (school == SpellSchool.None && definition != null && definition.id == "thorn")
                return new Color(0.62f, 0.9f, 0.32f, 1f);
            switch (school)
            {
                case SpellSchool.Arcane: return new Color(0.91f, 1f, 0.69f, 1f);
                case SpellSchool.Fire: return new Color(1f, 0.35f, 0.12f, 1f);
                case SpellSchool.Frost: return new Color(0.52f, 0.94f, 1f, 1f);
                case SpellSchool.Storm: return new Color(0.73f, 0.58f, 1f, 1f);
                case SpellSchool.Earth: return new Color(0.72f, 0.86f, 0.41f, 1f);
                case SpellSchool.Poison: return new Color(0.49f, 1f, 0.22f, 1f);
                case SpellSchool.Void: return new Color(0.83f, 0.3f, 1f, 1f);
                default: return new Color(0.38f, 0.82f, 1f, 1f);
            }
        }

        static string CharacterClassLabel(BrawlerDefinition definition)
        {
            if (definition == null) return "HERO";
            switch (definition.specialty.school)
            {
                case SpellSchool.Fire: return "FIRE MAGE";
                case SpellSchool.Frost: return "ICE MAGE";
                case SpellSchool.Storm: return "THUNDER MAGE";
                case SpellSchool.None:
                    return string.IsNullOrWhiteSpace(definition.role)
                        ? "HERO"
                        : definition.role.ToUpperInvariant();
                default:
                    return definition.specialty.school.ToString().ToUpperInvariant() + " MAGE";
            }
        }

        Sprite CharacterIcon(BrawlerDefinition definition, int fallbackIndex)
        {
            if (definition == null) return null;
            string schoolKey = definition.specialty.school != SpellSchool.None
                ? definition.specialty.school.ToString()
                : definition.id;
            Sprite icon = theme != null ? theme.SchoolIcon(schoolKey, fallbackIndex) : null;
            return icon != null ? icon : definition.portrait;
        }

        // ---------------- live menu sections ----------------

        GameObject BuildUtilityPanel(Transform root, string title, string subtitle = "")
        {
            var panel = NewRect(title.Replace(" ", "") + "Panel", root);
            Stretch((RectTransform)panel.transform);
            AddDim(panel.transform, 0.64f);
            AddSolidBand(panel.transform, "HeaderWash", new Vector2(0f, 0.84f), Vector2.one,
                new Color(0.02f, 0.11f, 0.22f, 0.92f));
            AddScreenTitle(panel.transform, title);
            if (!string.IsNullOrEmpty(subtitle))
            {
                var sub = MakeBody(panel.transform, subtitle, 30f, new Color(0.78f, 0.94f, 1f, 0.82f));
                var srt = sub.rectTransform;
                srt.anchorMin = srt.anchorMax = new Vector2(0.18f, 0.86f);
                srt.pivot = new Vector2(0f, 0.5f);
                srt.sizeDelta = new Vector2(1000f, 46f);
                sub.alignment = TextAlignmentOptions.Left;
            }
            AddPanelResourceStrip(panel.transform);
            AddBackButton(panel.transform, OnBackToMain);
            panel.SetActive(false);
            return panel;
        }

        void AddPanelResourceStrip(Transform root)
        {
            var coins = AddCompactResource(root, theme != null ? theme.coinIcon : null,
                Progress.Coins.ToString("N0"), new Vector2(0.72f, 0.92f), new Vector2(250f, 66f));
            var gems = AddCompactResource(root, theme != null ? theme.gemIcon : null,
                Progress.Gems.ToString("N0"), new Vector2(0.84f, 0.92f), new Vector2(220f, 66f));
            var energy = AddCompactResource(root, theme != null ? theme.energyIcon : null,
                Progress.Energy + "/60", new Vector2(0.95f, 0.92f), new Vector2(230f, 66f));
            menuRefreshers.Add(() =>
            {
                coins.text = Progress.Coins.ToString("N0");
                gems.text = Progress.Gems.ToString("N0");
                energy.text = Progress.Energy + "/60";
            });
        }

        TextMeshProUGUI AddCompactResource(Transform root, Sprite icon, string value, Vector2 anchor, Vector2 size)
        {
            var capsule = NewRect("HeaderResource", root);
            var rt = (RectTransform)capsule.transform;
            rt.anchorMin = rt.anchorMax = anchor;
            rt.sizeDelta = size;
            var bg = capsule.AddComponent<Image>();
            if (theme != null && theme.resourceCapsule != null)
            {
                bg.sprite = theme.resourceCapsule;
                bg.type = Image.Type.Sliced;
            }
            bg.color = new Color(0.06f, 0.1f, 0.18f, 0.96f);
            bg.raycastTarget = false;
            if (icon != null)
            {
                var iconGo = NewRect("Icon", capsule.transform);
                var irt = (RectTransform)iconGo.transform;
                irt.anchorMin = irt.anchorMax = new Vector2(0.18f, 0.5f);
                irt.sizeDelta = new Vector2(size.y * 0.82f, size.y * 0.82f);
                var img = iconGo.AddComponent<Image>();
                img.sprite = icon;
                img.preserveAspect = true;
                img.raycastTarget = false;
            }
            var text = MakeButtonLabel(capsule.transform, value, 32f, Color.white);
            var trt = text.rectTransform;
            trt.anchorMin = new Vector2(0.34f, 0f);
            trt.anchorMax = new Vector2(0.94f, 1f);
            trt.offsetMin = Vector2.zero;
            trt.offsetMax = Vector2.zero;
            return text;
        }

        GameObject BuildBrawlersPanel(Transform root)
        {
            var panel = BuildUtilityPanel(root, "HEROES", "Three elemental mages and one archer, each with unique skills.");
            var stats = MakeBody(panel.transform,
                $"TOTAL LEVELS {Progress.TotalBrawlerLevels()}     SKILLS {Progress.TotalSkillLevels()}     TROPHIES {Progress.TrophyEstimate()}",
                38f, new Color(0.8f, 0.94f, 1f));
            var srt = stats.rectTransform;
            srt.anchorMin = srt.anchorMax = new Vector2(0.5f, 0.81f);
            srt.sizeDelta = new Vector2(1300f, 60f);
            menuRefreshers.Add(() => stats.text =
                $"TOTAL LEVELS {Progress.TotalBrawlerLevels()}     SKILLS {Progress.TotalSkillLevels()}     TROPHIES {Progress.TrophyEstimate()}");

            for (int i = 0; i < roster.Length; i++)
            {
                int index = i;
                int columns = roster.Length <= 4 ? 2 : 3;
                int col = i % columns;
                int row = i / columns;
                float startX = columns == 2 ? 0.35f : 0.23f;
                float gap = columns == 2 ? 0.3f : 0.27f;
                var card = AddMenuCard(panel.transform, "Brawler_" + roster[i].id,
                    new Vector2(startX + col * gap, 0.58f - row * 0.32f), new Vector2(560f, 380f),
                    new Color(0.04f, 0.42f, 0.74f, 0.98f));
                FillBrawlerSummaryCard(card.transform, roster[i], index);
            }

            return panel;
        }

        void FillBrawlerSummaryCard(Transform card, BrawlerDefinition def, int index)
        {
            Sprite classIcon = CharacterIcon(def, index);
            if (classIcon != null)
            {
                var crestFrame = NewRect("SchoolCrestFrame", card);
                var frameRt = (RectTransform)crestFrame.transform;
                frameRt.anchorMin = frameRt.anchorMax = new Vector2(0.23f, 0.55f);
                frameRt.sizeDelta = new Vector2(205f, 220f);
                var frame = crestFrame.AddComponent<Image>();
                frame.sprite = theme != null ? theme.profileFrame : null;
                if (frame.sprite != null) frame.type = Image.Type.Sliced;
                frame.color = new Color(0.025f, 0.11f, 0.22f, 0.9f);
                frame.raycastTarget = false;

                var crest = NewRect("SchoolCrest", crestFrame.transform);
                var crestRt = (RectTransform)crest.transform;
                crestRt.anchorMin = crestRt.anchorMax = new Vector2(0.5f, 0.5f);
                crestRt.sizeDelta = new Vector2(128f, 128f);
                var image = crest.AddComponent<Image>();
                image.sprite = classIcon;
                image.preserveAspect = true;
                image.raycastTarget = false;
            }

            var name = MakeHeading(card, def.displayName.ToUpperInvariant(), 48f, Color.white);
            var nrt = name.rectTransform;
            nrt.anchorMin = nrt.anchorMax = new Vector2(0.66f, 0.78f);
            nrt.sizeDelta = new Vector2(300f, 58f);

            var role = MakeBody(card, def.role.ToUpperInvariant(), 25f, new Color(0.75f, 0.92f, 1f));
            var rrt = role.rectTransform;
            rrt.anchorMin = rrt.anchorMax = new Vector2(0.66f, 0.62f);
            rrt.sizeDelta = new Vector2(310f, 48f);

            var level = MakeButtonLabel(card, "", 36f, new Color(1f, 0.86f, 0.25f));
            var lrt = level.rectTransform;
            lrt.anchorMin = lrt.anchorMax = new Vector2(0.66f, 0.52f);
            lrt.sizeDelta = new Vector2(280f, 46f);
            menuRefreshers.Add(() => level.text = "LEVEL " + Progress.Get(def.id).level);

            var skills = MakeButtonLabel(card, "", 25f, new Color(0.78f, 0.94f, 1f));
            var skrt = skills.rectTransform;
            skrt.anchorMin = skrt.anchorMax = new Vector2(0.66f, 0.435f);
            skrt.sizeDelta = new Vector2(280f, 36f);
            menuRefreshers.Add(() => skills.text = "SKILLS " + Progress.TotalSkillLevels(def.id) + "/9");

            AddMiniStat(card, "HP", def.maxHealth / 160f, 0.34f, new Color(0.35f, 0.95f, 0.45f));
            AddMiniStat(card, "DMG", def.damage / 30f, 0.265f, new Color(1f, 0.55f, 0.35f));

            MakeButton(card, "DETAILS", theme != null ? theme.buttonBlue : null,
                new Vector2(0.35f, 0.11f), new Vector2(190f, 64f), 24f, () =>
                {
                    charIndex = index;
                    MatchSetup.Mode = Progress.SelectedMode;
                    ShowPanel(charPanel);
                    SetPreviewVisible(true);
                    SetCharacter(charIndex);
                    DebugPhase = "brawler details " + def.id;
                });
            MakeButton(card, "UPGRADE", theme != null ? theme.buttonGreen : null,
                new Vector2(0.71f, 0.11f), new Vector2(210f, 64f), 24f, () =>
                {
                    bool ok = Progress.TryUpgrade(def.id);
                    ShowToast(ok ? def.displayName + " upgraded" : "Need more AP or coins");
                    if (ok)
                        ShowCelebration(def.displayName.ToUpperInvariant(), "LEVEL UP", CharacterIcon(def, index), true);
                    RefreshShop();
                    RefreshMenu();
                });
        }

        GameObject BuildCardsPanel(Transform root)
        {
            var panel = BuildUtilityPanel(root, "RUNES", "Build a three-rune spell loadout.");
            string[] names = { "STRIKE", "BULWARK", "RUSH", "ARCANE", "GEM SENSE", "REVIVE" };
            string[] desc =
            {
                "+8% damage in matches",
                "+12% max health in matches",
                "+18% Ward Flow recharge",
                "Ranged attacks charge faster",
                "+0.6 auto-aim range",
                "Respawn 15% faster"
            };
            Sprite[] icons =
            {
                theme != null ? theme.damageIcon : null,
                theme != null ? theme.hpIcon : null,
                theme != null ? theme.speedIcon : null,
                theme != null ? theme.cardsIcon : null,
                theme != null ? theme.gemIcon : null,
                theme != null ? theme.rewardsIcon : null,
            };

            for (int i = 0; i < names.Length; i++)
            {
                int index = i;
                int col = i % 3;
                int row = i / 3;
                var card = AddMenuCard(panel.transform, "Card_" + names[i],
                    new Vector2(0.23f + col * 0.27f, 0.62f - row * 0.34f), new Vector2(540f, 390f),
                    Progress.IsCardEquipped(i) ? new Color(0.05f, 0.45f, 0.75f, 0.98f) : new Color(0.36f, 0.12f, 0.68f, 0.98f));
                var cardBg = card.GetComponent<Image>();
                AddLargeIcon(card.transform, icons[i], new Vector2(0.5f, 0.68f), new Vector2(140f, 140f));
                var title = MakeHeading(card.transform, names[i], 48f, Color.white);
                var trt = title.rectTransform;
                trt.anchorMin = trt.anchorMax = new Vector2(0.5f, 0.46f);
                trt.sizeDelta = new Vector2(470f, 64f);
                var body = MakeBody(card.transform, desc[i], 30f, new Color(1f, 1f, 1f, 0.88f));
                var brt = body.rectTransform;
                brt.anchorMin = brt.anchorMax = new Vector2(0.5f, 0.28f);
                brt.sizeDelta = new Vector2(430f, 88f);
                TextMeshProUGUI buttonLabel;
                Button button;
                MakeRewardButton(card.transform, "EQUIP", new Vector2(0.5f, 0.08f), out button, out buttonLabel);
                button.onClick.AddListener(() =>
                {
                    bool ok = Progress.TryToggleCard(index, 3);
                    ShowToast(ok ? "Loadout updated" : "Keep 1-3 cards equipped");
                    if (ok)
                        ShowCelebration(names[index], Progress.IsCardEquipped(index) ? "CARD EQUIPPED" : "CARD REMOVED",
                            icons[index], false);
                    RefreshMenu();
                });
                menuRefreshers.Add(() =>
                {
                    bool equipped = Progress.IsCardEquipped(index);
                    int count = Progress.EquippedCardCount();
                    SetRewardButtonState(button, buttonLabel, equipped ? "EQUIPPED" : "EQUIP",
                        equipped ? count > 1 : count < 3);
                    if (cardBg != null)
                        cardBg.color = equipped ? new Color(0.05f, 0.45f, 0.75f, 0.98f) : new Color(0.36f, 0.12f, 0.68f, 0.98f);
                });
            }
            return panel;
        }

        GameObject BuildInventoryPanel(Transform root)
        {
            var panel = BuildUtilityPanel(root, "RELICS", "Arcane relics, catalysts, and consumables.");
            AddInventoryItem(panel.transform, 0, "ENERGY CELL", "Restores 10 Battle Energy for more matches.",
                theme != null ? theme.energyIcon : null, () =>
                {
                    Progress.AddEnergy(10);
                    ShowToast("Battle Energy restored");
                    ShowCelebration("ENERGY CELL", "+10 BATTLE ENERGY", theme != null ? theme.energyIcon : null, false);
                    RefreshMenu();
                });
            AddInventoryItem(panel.transform, 1, "COIN CRATE", "Open for 35 coins.",
                theme != null ? theme.coinIcon : null, () =>
                {
                    Progress.AddCoins(35);
                    ShowToast("+35 coins");
                    ShowCelebration("COIN CRATE", "+35 COINS", theme != null ? theme.coinIcon : null, false);
                    RefreshShop();
                });
            AddInventoryItem(panel.transform, 2, "ARCANE ESSENCE", "Grant 25 AP to the selected hero.",
                theme != null ? theme.cardsIcon : null, () =>
                {
                    if (roster.Length > 0) Progress.AddCharacterPoints(roster[charIndex].id, 25);
                    ShowToast("+25 AP");
                    ShowCelebration("ARCANE ESSENCE", "+25 AP", theme != null ? theme.cardsIcon : null, false);
                    RefreshShop();
                });
            AddInventoryItem(panel.transform, 3, "GEM POUCH", "Premium currency for future offers.",
                theme != null ? theme.gemIcon : null, () =>
                {
                    Progress.AddGems(3);
                    ShowToast("+3 gems");
                    ShowCelebration("GEM POUCH", "+3 GEMS", theme != null ? theme.gemIcon : null, false);
                    RefreshMenu();
                });
            return panel;
        }

        void AddInventoryItem(Transform root, int index, string title, string desc, Sprite icon, UnityEngine.Events.UnityAction action)
        {
            var card = AddMenuCard(root, "Inventory_" + title,
                new Vector2(0.29f + (index % 2) * 0.42f, 0.65f - (index / 2) * 0.32f),
                new Vector2(780f, 300f), new Color(0.05f, 0.35f, 0.62f, 0.98f));
            AddLargeIcon(card.transform, icon, new Vector2(0.18f, 0.52f), new Vector2(150f, 150f));
            var name = MakeHeading(card.transform, title, 44f, Color.white);
            var nrt = name.rectTransform;
            nrt.anchorMin = nrt.anchorMax = new Vector2(0.58f, 0.68f);
            nrt.sizeDelta = new Vector2(470f, 58f);
            var body = MakeBody(card.transform, desc, 30f, new Color(1f, 1f, 1f, 0.86f));
            var brt = body.rectTransform;
            brt.anchorMin = brt.anchorMax = new Vector2(0.58f, 0.46f);
            brt.sizeDelta = new Vector2(470f, 76f);
            var owned = MakeButtonLabel(card.transform, "", 28f, new Color(1f, 0.9f, 0.45f));
            var ort = owned.rectTransform;
            ort.anchorMin = ort.anchorMax = new Vector2(0.43f, 0.17f);
            ort.sizeDelta = new Vector2(210f, 54f);
            TextMeshProUGUI buttonLabel;
            Button button;
            MakeRewardButton(card.transform, "USE", new Vector2(0.75f, 0.17f), out button, out buttonLabel);
            button.onClick.AddListener(() =>
            {
                if (!Progress.TryUseInventoryItem(index))
                {
                    ShowToast(title + " is empty");
                    RefreshMenu();
                    return;
                }
                action();
                RefreshMenu();
            });
            menuRefreshers.Add(() =>
            {
                int count = Progress.InventoryItemCount(index);
                owned.text = "OWNED " + count;
                SetRewardButtonState(button, buttonLabel, count > 0 ? "USE" : "EMPTY", count > 0);
            });
        }

        GameObject BuildMissionsPanel(Transform root)
        {
            var panel = BuildUtilityPanel(root, "TRIALS", "Daily hero trials with claimable rewards.");
            AddQuestCard(panel.transform, 0, "ARCANE PRACTICE", "Earn 50 AP today", () => Progress.DailyBrawlerPointsEarned, 50,
                "30 COINS", () => Progress.AddCoins(30));
            AddQuestCard(panel.transform, 1, "HERO ASCENSION", "Gain 2 hero mastery levels today", () => Progress.DailyBrawlerLevelsGained, 2,
                "5 GEMS", () => Progress.AddGems(5));
            AddQuestCard(panel.transform, 2, "WAR CHEST", "Collect 120 coins today", () => Progress.DailyCoinsEarned, 120,
                "25 AP", () =>
                {
                    if (roster.Length > 0) Progress.AddCharacterPoints(roster[charIndex].id, 25);
                });
            return panel;
        }

        void AddQuestCard(Transform root, int index, string title, string desc, System.Func<int> current,
            int target, string reward, System.Action grant)
        {
            var card = AddMenuCard(root, "Quest_" + title, new Vector2(0.5f, 0.68f - index * 0.22f),
                new Vector2(1500f, 230f), new Color(0.05f, 0.38f, 0.66f, 0.98f));
            AddLargeIcon(card.transform, theme != null ? theme.missionIcon : null, new Vector2(0.08f, 0.5f), new Vector2(120f, 120f));
            var name = MakeHeading(card.transform, title, 46f, Color.white);
            var nrt = name.rectTransform;
            nrt.anchorMin = nrt.anchorMax = new Vector2(0.28f, 0.64f);
            nrt.sizeDelta = new Vector2(560f, 60f);
            name.alignment = TextAlignmentOptions.Left;
            var body = MakeBody(card.transform, desc, 30f, new Color(1f, 1f, 1f, 0.84f));
            var brt = body.rectTransform;
            brt.anchorMin = brt.anchorMax = new Vector2(0.28f, 0.38f);
            brt.sizeDelta = new Vector2(560f, 50f);
            body.alignment = TextAlignmentOptions.Left;
            var progress = AddProgressBar(card.transform, new Vector2(0.61f, 0.52f), new Vector2(420f, 34f), new Color(0.36f, 0.9f, 1f));
            var progressText = MakeButtonLabel(card.transform, "", 28f, Color.white);
            var ptr = progressText.rectTransform;
            ptr.anchorMin = ptr.anchorMax = new Vector2(0.61f, 0.52f);
            ptr.sizeDelta = new Vector2(420f, 42f);
            TextMeshProUGUI buttonLabel = null;
            Button button = null;
            MakeRewardButton(card.transform, "CLAIM", new Vector2(0.87f, 0.5f), out button, out buttonLabel);
            button.onClick.AddListener(() =>
            {
                if (Progress.IsQuestClaimed(index) || current() < target) return;
                grant();
                Progress.MarkQuestClaimed(index);
                ShowToast("Quest reward claimed: " + reward);
                ShowCelebration("QUEST COMPLETE", reward, theme != null ? theme.missionIcon : null, false);
                RefreshMenu();
            });
            menuRefreshers.Add(() =>
            {
                int value = Mathf.Min(current(), target);
                progress.anchorMax = new Vector2(target > 0 ? value / (float)target : 1f, 1f);
                progressText.text = value + " / " + target;
                bool claimed = Progress.IsQuestClaimed(index);
                bool ready = value >= target;
                SetRewardButtonState(button, buttonLabel, claimed ? "CLAIMED" : ready ? "CLAIM" : reward,
                    ready && !claimed);
            });
        }

        GameObject BuildRewardsPanel(Transform root)
        {
            var panel = BuildUtilityPanel(root, "REWARDS", "Login track and streak bonuses.");
            string[] labels = { "DAY 1", "DAY 2", "DAY 3", "DAY 4", "DAY 5", "DAY 6", "DAY 7" };
            string[] rewards = { "25 COINS", "5 GEMS", "20 BATTLE ENERGY", "30 AP", "60 COINS", "10 GEMS", "100 COINS" };
            Sprite[] icons =
            {
                theme != null ? theme.coinIcon : null,
                theme != null ? theme.gemIcon : null,
                theme != null ? theme.energyIcon : null,
                theme != null ? theme.cardsIcon : null,
                theme != null ? theme.coinIcon : null,
                theme != null ? theme.gemIcon : null,
                theme != null ? theme.rewardsIcon : null,
            };
            for (int i = 0; i < labels.Length; i++)
            {
                int index = i;
                int col = i % 4;
                int row = i / 4;
                var card = AddMenuCard(panel.transform, "Reward_" + labels[i],
                    new Vector2(0.18f + col * 0.21f, 0.61f - row * 0.34f), new Vector2(430f, 390f),
                    i == 6 ? new Color(0.95f, 0.6f, 0.04f, 0.98f) : new Color(0.42f, 0.16f, 0.8f, 0.98f));
                AddLargeIcon(card.transform, icons[i], new Vector2(0.5f, 0.62f), new Vector2(150f, 150f));
                var title = MakeHeading(card.transform, labels[i], 42f, Color.white);
                var trt = title.rectTransform;
                trt.anchorMin = trt.anchorMax = new Vector2(0.5f, 0.84f);
                trt.sizeDelta = new Vector2(330f, 56f);
                var body = MakeButtonLabel(card.transform, rewards[i], 38f, Color.white);
                var brt = body.rectTransform;
                brt.anchorMin = brt.anchorMax = new Vector2(0.5f, 0.34f);
                brt.sizeDelta = new Vector2(330f, 60f);
                TextMeshProUGUI buttonLabel = null;
                Button button = null;
                MakeRewardButton(card.transform, "CLAIM", new Vector2(0.5f, 0.1f), out button, out buttonLabel);
                button.onClick.AddListener(() =>
                {
                    if (!Progress.TryClaimLoginReward(index))
                    {
                        string message = Progress.LoginClockRollbackDetected
                            ? "Check your device clock to continue login rewards"
                            : Progress.LoginRewardTrackComplete
                                ? "Login track complete - a new track starts tomorrow"
                                : index < Progress.LoginRewardIndex
                                    ? "Reward already claimed"
                                    : index > Progress.LoginRewardIndex
                                        ? "Claim earlier login days first"
                                        : "Next login reward is available tomorrow";
                        ShowToast(message);
                        return;
                    }
                    GrantReward(index);
                    ShowToast(rewards[index] + " claimed");
                    ShowCelebration(labels[index], rewards[index], icons[index], false);
                    RefreshMenu();
                });
                menuRefreshers.Add(() =>
                {
                    bool claimed = Progress.IsRewardClaimed(index);
                    bool current = index == Progress.LoginRewardIndex;
                    bool available = current && Progress.LoginRewardAvailableToday;
                    string state = claimed
                        ? "CLAIMED"
                        : available
                            ? "CLAIM"
                            : current
                                ? "TOMORROW"
                                : "LOCKED";
                    SetRewardButtonState(button, buttonLabel, state, available);
                });
            }
            return panel;
        }

        void GrantReward(int index)
        {
            switch (index)
            {
                case 0: Progress.AddCoins(25); break;
                case 1: Progress.AddGems(5); break;
                case 2: Progress.AddEnergy(20); break;
                case 3:
                    if (roster.Length > 0) Progress.AddCharacterPoints(roster[charIndex].id, 30);
                    break;
                case 4: Progress.AddCoins(60); break;
                case 5: Progress.AddGems(10); break;
                case 6: Progress.AddCoins(100); break;
            }
        }

        GameObject BuildRankingPanel(Transform root)
        {
            var panel = BuildUtilityPanel(root, "RANKING", "Current arena standings.");
            string[] names = { "RiftQueen", "BoltSmith", "YOU", "GemPilot", "IronNova", "StageBoss", "ArcBlade" };
            for (int i = 0; i < names.Length; i++)
            {
                int trophies = names[i] == "YOU" ? Progress.TrophyEstimate() : 980 - i * 87;
                var row = AddMenuCard(panel.transform, "Rank_" + names[i], new Vector2(0.5f, 0.75f - i * 0.095f),
                    new Vector2(1500f, 105f), names[i] == "YOU" ? new Color(0.1f, 0.56f, 0.82f, 0.98f) : new Color(0.05f, 0.27f, 0.48f, 0.96f));
                var rank = MakeButtonLabel(row.transform, "#" + (i + 1), 36f, new Color(1f, 0.88f, 0.25f));
                var rrt = rank.rectTransform;
                rrt.anchorMin = rrt.anchorMax = new Vector2(0.08f, 0.5f);
                rrt.sizeDelta = new Vector2(120f, 56f);
                var name = MakeHeading(row.transform, names[i], 40f, Color.white);
                var nrt = name.rectTransform;
                nrt.anchorMin = nrt.anchorMax = new Vector2(0.32f, 0.5f);
                nrt.sizeDelta = new Vector2(520f, 60f);
                name.alignment = TextAlignmentOptions.Left;
                AddLargeIcon(row.transform, theme != null ? theme.trophyIcon : null, new Vector2(0.72f, 0.5f), new Vector2(64f, 64f));
                var score = MakeButtonLabel(row.transform, trophies.ToString(), 38f, Color.white);
                var srt = score.rectTransform;
                srt.anchorMin = srt.anchorMax = new Vector2(0.82f, 0.5f);
                srt.sizeDelta = new Vector2(240f, 56f);
                if (names[i] == "YOU")
                    menuRefreshers.Add(() => score.text = Progress.TrophyEstimate().ToString());
            }
            return panel;
        }

        GameObject BuildFriendsPanel(Transform root)
        {
            var panel = BuildUtilityPanel(root, "FRIENDS", "Invite squadmates and inspect profiles.");
            string[] friends = { "Mira", "Talon", "Byte", "Kora", "Axel" };
            string[] states = { "ONLINE", "IN MATCH", "ONLINE", "AWAY", "OFFLINE" };
            for (int i = 0; i < friends.Length; i++)
            {
                int index = i;
                string friendName = friends[i];
                string stateText = states[i];
                var row = AddMenuCard(panel.transform, "Friend_" + friends[i], new Vector2(0.5f, 0.74f - i * 0.13f),
                    new Vector2(1450f, 135f), new Color(0.05f, 0.31f, 0.55f, 0.96f));
                AddLargeIcon(row.transform, theme != null ? theme.friendsIcon : null, new Vector2(0.08f, 0.5f), new Vector2(82f, 82f));
                var name = MakeHeading(row.transform, friends[i], 42f, Color.white);
                var nrt = name.rectTransform;
                nrt.anchorMin = nrt.anchorMax = new Vector2(0.28f, 0.62f);
                nrt.sizeDelta = new Vector2(520f, 58f);
                name.alignment = TextAlignmentOptions.Left;
                var state = MakeButtonLabel(row.transform, states[i], 30f,
                    states[i] == "ONLINE" ? new Color(0.45f, 1f, 0.45f) : new Color(1f, 0.85f, 0.35f));
                var srt = state.rectTransform;
                srt.anchorMin = srt.anchorMax = new Vector2(0.28f, 0.28f);
                srt.sizeDelta = new Vector2(520f, 44f);
                state.alignment = TextAlignmentOptions.Left;
                TextMeshProUGUI inviteLabel;
                Button inviteButton;
                MakeRewardButton(row.transform, "INVITE", new Vector2(0.76f, 0.5f), out inviteButton, out inviteLabel);
                inviteButton.onClick.AddListener(() =>
                {
                    if (Progress.IsFriendInvited(index)) return;
                    Progress.MarkFriendInvited(index);
                    ShowToast("Invite sent to " + friendName);
                    ShowCelebration("SQUAD INVITE", friendName.ToUpperInvariant(), theme != null ? theme.friendsIcon : null, false);
                    RefreshMenu();
                });
                menuRefreshers.Add(() =>
                {
                    bool invited = Progress.IsFriendInvited(index);
                    inviteLabel.text = invited ? "INVITED" : "INVITE";
                    inviteButton.interactable = !invited && stateText != "OFFLINE";
                });
                MakeButton(row.transform, "PROFILE", theme != null ? theme.buttonBlue : null,
                    new Vector2(0.91f, 0.5f), new Vector2(240f, 82f), 30f,
                    () => ShowToast(friendName + " - " + stateText + " - " + (Progress.TrophyEstimate() + 120 + index * 45) + " trophies"));
            }
            return panel;
        }

        GameObject BuildInboxPanel(Transform root)
        {
            var panel = BuildUtilityPanel(root, "INBOX", "Account messages and grants.");
            AddInboxMessage(panel.transform, 0, "WELCOME PACK", "Thanks for joining Brawl Arena.", "50 COINS",
                () => Progress.AddCoins(50));
            AddInboxMessage(panel.transform, 1, "PATCH BONUS", "Menu systems are online.", "5 GEMS",
                () => Progress.AddGems(5));
            AddInboxMessage(panel.transform, 2, "ARCANE GRANT", "Use this to prepare your current hero.", "25 AP",
                () =>
                {
                    if (roster.Length > 0) Progress.AddCharacterPoints(roster[charIndex].id, 25);
                });
            return panel;
        }

        void AddInboxMessage(Transform root, int index, string title, string body, string reward, System.Action grant)
        {
            var card = AddMenuCard(root, "Inbox_" + title, new Vector2(0.5f, 0.72f - index * 0.22f),
                new Vector2(1500f, 220f), new Color(0.05f, 0.33f, 0.58f, 0.98f));
            AddLargeIcon(card.transform, theme != null ? theme.inventoryIcon : null, new Vector2(0.08f, 0.5f), new Vector2(110f, 110f));
            var name = MakeHeading(card.transform, title, 44f, Color.white);
            var nrt = name.rectTransform;
            nrt.anchorMin = nrt.anchorMax = new Vector2(0.3f, 0.66f);
            nrt.sizeDelta = new Vector2(650f, 58f);
            name.alignment = TextAlignmentOptions.Left;
            var text = MakeBody(card.transform, body + "  Reward: " + reward, 30f, new Color(1f, 1f, 1f, 0.86f));
            var trt = text.rectTransform;
            trt.anchorMin = trt.anchorMax = new Vector2(0.34f, 0.34f);
            trt.sizeDelta = new Vector2(760f, 58f);
            text.alignment = TextAlignmentOptions.Left;
            TextMeshProUGUI label;
            Button button;
            MakeRewardButton(card.transform, "CLAIM", new Vector2(0.86f, 0.5f), out button, out label);
            button.onClick.AddListener(() =>
            {
                if (Progress.IsInboxClaimed(index)) return;
                grant();
                Progress.MarkInboxClaimed(index);
                ShowToast(reward + " claimed");
                ShowCelebration(title, reward, theme != null ? theme.giftIcon : null, false);
                RefreshMenu();
            });
            menuRefreshers.Add(() =>
            {
                bool claimed = Progress.IsInboxClaimed(index);
                SetRewardButtonState(button, label, claimed ? "READ" : "CLAIM", !claimed);
            });
        }

        GameObject BuildNoticePanel(Transform root)
        {
            var panel = BuildUtilityPanel(root, "NOTICE", "Mode rotation and update notes.");
            AddNotice(panel.transform, 0, "5V5 TRIAL ROTATION", "Knockout and Gem Grab now share the same hero roster and rewards.");
            AddNotice(panel.transform, 1, "SPELLBOOK UPGRADES", "Collect AP in trials, then spend coins here to master your favorites.");
            AddNotice(panel.transform, 2, "SOCIAL HUB", "Friends, ranking, inbox, quests, and rewards are now available from the lobby.");
            return panel;
        }

        void AddNotice(Transform root, int index, string title, string body)
        {
            var card = AddMenuCard(root, "Notice_" + title, new Vector2(0.5f, 0.72f - index * 0.22f),
                new Vector2(1500f, 220f), new Color(0.05f, 0.31f, 0.55f, 0.98f));
            AddLargeIcon(card.transform, theme != null ? theme.rewardsIcon : null, new Vector2(0.08f, 0.5f), new Vector2(110f, 110f));
            var name = MakeHeading(card.transform, title, 44f, Color.white);
            var nrt = name.rectTransform;
            nrt.anchorMin = nrt.anchorMax = new Vector2(0.33f, 0.64f);
            nrt.sizeDelta = new Vector2(760f, 58f);
            name.alignment = TextAlignmentOptions.Left;
            var text = MakeBody(card.transform, body, 31f, new Color(1f, 1f, 1f, 0.86f));
            var trt = text.rectTransform;
            trt.anchorMin = trt.anchorMax = new Vector2(0.46f, 0.34f);
            trt.sizeDelta = new Vector2(1040f, 72f);
            text.alignment = TextAlignmentOptions.Left;
        }

        GameObject BuildSettingsPanel(Transform root)
        {
            var panel = BuildUtilityPanel(root, "SETTINGS",
                "SFX, haptics, visual comfort, quality, and gameplay guidance.");

            AddLiveSetting(panel.transform, 0, "SFX", theme != null ? theme.playIcon : null,
                () => AccessibilitySettings.ToggleLabel(FeedbackSettings.SfxEnabled),
                () =>
                {
                    bool next = !FeedbackSettings.SfxEnabled;
                    FeedbackSettings.SetSfxEnabled(next);
                    ShowToast("SFX " + AccessibilitySettings.ToggleLabel(next));
                });
            AddLiveSetting(panel.transform, 1, "HAPTICS", theme != null ? theme.energyIcon : null,
                () => AccessibilitySettings.ToggleLabel(FeedbackSettings.HapticsEnabled),
                () =>
                {
                    bool next = !FeedbackSettings.HapticsEnabled;
                    FeedbackSettings.SetHapticsEnabled(next);
                    ShowToast("Haptics " + AccessibilitySettings.ToggleLabel(next));
                });
            AddLiveSetting(panel.transform, 2, "QUALITY", theme != null ? theme.starOnIcon : null,
                () => MobileQualitySettings.GetModeLabel(MobileQualitySettings.Mode),
                () =>
                {
                    MobileQualityMode next = MobileQualitySettings.NextMode(MobileQualitySettings.Mode);
                    MobileQualitySettings.SetMode(next);
                    ShowToast("Quality " + MobileQualitySettings.GetModeLabel(next));
                });
            AddLiveSetting(panel.transform, 3, "REDUCED MOTION", theme != null ? theme.speedIcon : null,
                () => AccessibilitySettings.ToggleLabel(AccessibilitySettings.ReducedMotionEnabled),
                () =>
                {
                    bool next = AccessibilitySettings.ToggleReducedMotion();
                    ShowToast("Reduced motion " + AccessibilitySettings.ToggleLabel(next));
                });
            AddLiveSetting(panel.transform, 4, "HIGH CONTRAST", theme != null ? theme.settingsIcon : null,
                () => AccessibilitySettings.ToggleLabel(AccessibilitySettings.HighContrastEnabled),
                () =>
                {
                    bool next = AccessibilitySettings.ToggleHighContrast();
                    ShowToast("High contrast and team cues " + AccessibilitySettings.ToggleLabel(next));
                });
            AddLiveSetting(panel.transform, 5, "GAMEPLAY COACH", theme != null ? theme.cardsIcon : null,
                () => GameplayCoachState.IsCompleted ? "REPLAY" : "READY",
                () =>
                {
                    GameplayCoach.ReplayNextMatch();
                    ShowToast("Gameplay coach will open before your next match");
                });
            return panel;
        }

        void AddLiveSetting(Transform root, int index, string title, Sprite icon,
            System.Func<string> valueProvider, System.Action activate)
        {
            var card = AddMenuCard(root, "Setting_" + title,
                new Vector2(0.34f + (index % 2) * 0.34f, 0.68f - (index / 2) * 0.22f),
                new Vector2(610f, 190f), new Color(0.05f, 0.33f, 0.58f, 0.98f));
            AddLargeIcon(card.transform, icon, new Vector2(0.17f, 0.53f), new Vector2(88f, 88f));
            var name = MakeHeading(card.transform, title, 32f, Color.white);
            var nrt = name.rectTransform;
            nrt.anchorMin = nrt.anchorMax = new Vector2(0.57f, 0.67f);
            nrt.sizeDelta = new Vector2(390f, 50f);
            TextMeshProUGUI label;
            Button button;
            MakeRewardButton(card.transform, "", new Vector2(0.66f, 0.22f), out button, out label);
            void Refresh()
            {
                label.text = valueProvider != null ? valueProvider() : "";
            }
            button.onClick.AddListener(() =>
            {
                activate?.Invoke();
                Refresh();
            });
            Refresh();
            menuRefreshers.Add(Refresh);
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

            AddDim(panel.transform, 0.64f);
            AddSolidBand(panel.transform, "HeaderWash", new Vector2(0f, 0.84f), Vector2.one,
                new Color(0.02f, 0.11f, 0.22f, 0.92f));
            AddScreenTitle(panel.transform, "SPELLBOOK");
            AddBackButton(panel.transform, OnBackToMain);
            shopCoinsText = AddResourceCapsule(panel.transform, theme != null ? theme.coinIcon : null,
                Progress.Coins.ToString("N0"), new Vector2(0.77f, 0.94f), new Vector2(360f, 82f),
                new Color(0.08f, 0.11f, 0.18f, 0.96f));
            shopGemsText = AddResourceCapsule(panel.transform, theme != null ? theme.gemIcon : null,
                Progress.Gems.ToString("N0"), new Vector2(0.91f, 0.94f), new Vector2(260f, 82f),
                new Color(0.08f, 0.11f, 0.18f, 0.96f));

            activeShopTab = 0;
            AddShopTab(panel.transform, 0, "HEROES", new Vector2(0.22f, 0.82f),
                () => JumpShop(1f, "Hero mastery", 0));
            AddShopTab(panel.transform, 1, "AP", new Vector2(0.43f, 0.82f),
                () => JumpShop(0.46f, "Arcane point offers", 1));
            AddShopTab(panel.transform, 2, "COINS", new Vector2(0.63f, 0.82f),
                () => JumpShop(0.22f, "Coin offers", 2));
            AddShopTab(panel.transform, 3, "GEMS", new Vector2(0.81f, 0.82f),
                () => JumpShop(0f, "Gem and item offers", 3));

            var hint = MakeBody(panel.transform,
                "EARN AP AND COINS IN TRIALS - SPEND THEM HERE TO DEVELOP HEROES AND MASTER SKILLS",
                34f, new Color(1f, 1f, 1f, 0.8f));
            var hrt = hint.rectTransform;
            hrt.anchorMin = hrt.anchorMax = new Vector2(0.5f, 0.75f);
            hrt.sizeDelta = new Vector2(2100f, 50f);

            // Scrollable card grid: two rows don't fit above the safe area on
            // shorter screens.
            var viewport = NewRect("Viewport", panel.transform);
            var vrt = (RectTransform)viewport.transform;
            vrt.anchorMin = new Vector2(0.05f, 0.03f);
            vrt.anchorMax = new Vector2(0.95f, 0.71f);
            vrt.offsetMin = Vector2.zero;
            vrt.offsetMax = Vector2.zero;
            var vpImg = viewport.AddComponent<Image>();
            vpImg.color = new Color(1f, 1f, 1f, 0f); // invisible drag surface
            viewport.AddComponent<RectMask2D>();

            var content = NewRect("Content", viewport.transform);
            var crt = (RectTransform)content.transform;
            crt.anchorMin = new Vector2(0f, 1f);
            crt.anchorMax = new Vector2(1f, 1f);
            crt.pivot = new Vector2(0.5f, 1f);
            int columns = roster.Length <= 4 ? 2 : 3;
            int rows = (roster.Length + columns - 1) / columns;
            crt.sizeDelta = new Vector2(0f, rows * 540f + 1360f);
            crt.anchoredPosition = Vector2.zero;

            shopScroll = viewport.AddComponent<ScrollRect>();
            shopScroll.viewport = vrt;
            shopScroll.content = crt;
            shopScroll.horizontal = false;
            shopScroll.vertical = true;
            shopScroll.movementType = ScrollRect.MovementType.Elastic;
            shopScroll.scrollSensitivity = 40f;
            shopScroll.onValueChanged.AddListener(pos => UpdateShopTabFromScroll(pos.y));

            for (int i = 0; i < roster.Length; i++)
            {
                int col = i % columns;
                int row = i / columns;
                float spacing = columns == 2 ? 760f : 720f;
                BuildShopCard(content.transform, roster[i], i,
                    new Vector2((col - (columns - 1) * 0.5f) * spacing,
                        -300f - row * 540f));
            }

            float offerY = -300f - rows * 540f;
            BuildShopOfferSection(content.transform, "AP", offerY,
                ("25 CURRENT AP", "20 COINS", theme != null ? theme.cardsIcon : null,
                    new UnityEngine.Events.UnityAction(() => RunShopPurchase("+25 AP", () =>
                    {
                        if (!Progress.TrySpendCoins(20)) return false;
                        GrantCurrentBrawlerPoints(25);
                        return true;
                    }))),
                ("80 CURRENT AP", "8 GEMS", theme != null ? theme.gemIcon : null,
                    new UnityEngine.Events.UnityAction(() => RunShopPurchase("+80 AP", () =>
                    {
                        if (!Progress.TrySpendGems(8)) return false;
                        GrantCurrentBrawlerPoints(80);
                        return true;
                    }))),
                ("TOKEN PACK", "12 GEMS", theme != null ? theme.inventoryIcon : null,
                    new UnityEngine.Events.UnityAction(() => RunShopPurchase("+1 token pack", () =>
                    {
                        if (!Progress.TrySpendGems(12)) return false;
                        Progress.AddInventoryItem(2, 1);
                        return true;
                    }))));
            offerY -= 400f;
            BuildShopOfferSection(content.transform, "COINS", offerY,
                ("75 COINS", "10 GEMS", theme != null ? theme.coinIcon : null,
                    new UnityEngine.Events.UnityAction(() => RunShopPurchase("+75 coins", () =>
                    {
                        if (!Progress.TrySpendGems(10)) return false;
                        Progress.AddCoins(75);
                        return true;
                    }))),
                ("COIN CRATE", "7 GEMS", theme != null ? theme.rewardsIcon : null,
                    new UnityEngine.Events.UnityAction(() => RunShopPurchase("+1 coin crate", () =>
                    {
                        if (!Progress.TrySpendGems(7)) return false;
                        Progress.AddInventoryItem(1, 1);
                        return true;
                    }))),
                ("TRAINING BONUS", "15 BATTLE ENERGY", theme != null ? theme.energyIcon : null,
                    new UnityEngine.Events.UnityAction(() => RunShopPurchase("+40 coins", () =>
                    {
                        if (!Progress.TrySpendEnergy(15)) return false;
                        Progress.AddCoins(40);
                        return true;
                    }))));
            offerY -= 400f;
            BuildShopOfferSection(content.transform, "GEMS & ITEMS", offerY,
                ("GEM POUCH", "150 COINS", theme != null ? theme.gemIcon : null,
                    new UnityEngine.Events.UnityAction(() => RunShopPurchase("+1 gem pouch", () =>
                    {
                        if (!Progress.TrySpendCoins(150)) return false;
                        Progress.AddInventoryItem(3, 1);
                        return true;
                    }))),
                ("ENERGY CELL", "3 GEMS", theme != null ? theme.energyIcon : null,
                    new UnityEngine.Events.UnityAction(() => RunShopPurchase("+1 energy cell", () =>
                    {
                        if (!Progress.TrySpendGems(3)) return false;
                        Progress.AddInventoryItem(0, 1);
                        return true;
                    }))),
                ("OPEN REWARDS", "CLAIM", theme != null ? theme.rewardsIcon : null,
                    new UnityEngine.Events.UnityAction(OnRewardsPressed)));

            panel.SetActive(false);
            return panel;
        }

        void JumpShop(float normalizedPosition, string message, int tabIndex)
        {
            SetShopTab(tabIndex);
            if (shopScroll != null) shopScroll.verticalNormalizedPosition = Mathf.Clamp01(normalizedPosition);
            ShowToast(message);
        }

        void GrantCurrentBrawlerPoints(int amount)
        {
            if (roster == null || roster.Length == 0) return;
            Progress.AddCharacterPoints(roster[charIndex].id, amount);
        }

        void RunShopPurchase(string successMessage, System.Func<bool> purchase)
        {
            bool ok = purchase != null && purchase();
            ShowToast(ok ? successMessage : "Not enough currency");
            if (ok)
                ShowCelebration("SHOP PURCHASE", successMessage, theme != null ? theme.shopIcon : null, false);
            RefreshShop();
            RefreshMenu();
        }

        void BuildShopOfferSection(Transform root, string title, float y,
            (string title, string price, Sprite icon, UnityEngine.Events.UnityAction action) left,
            (string title, string price, Sprite icon, UnityEngine.Events.UnityAction action) middle,
            (string title, string price, Sprite icon, UnityEngine.Events.UnityAction action) right)
        {
            var heading = MakeHeading(root, title, 54f, new Color(1f, 0.88f, 0.3f));
            var hrt = heading.rectTransform;
            hrt.anchorMin = hrt.anchorMax = new Vector2(0.5f, 1f);
            hrt.anchoredPosition = new Vector2(0f, y + 200f);
            hrt.sizeDelta = new Vector2(1200f, 70f);

            BuildShopOfferCard(root, left.title, left.price, left.icon, new Vector2(-560f, y), left.action);
            BuildShopOfferCard(root, middle.title, middle.price, middle.icon, new Vector2(0f, y), middle.action);
            BuildShopOfferCard(root, right.title, right.price, right.icon, new Vector2(560f, y), right.action);
        }

        void BuildShopOfferCard(Transform root, string title, string price, Sprite icon, Vector2 pos,
            UnityEngine.Events.UnityAction action)
        {
            var card = NewRect("Offer_" + title, root);
            var rt = (RectTransform)card.transform;
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 1f);
            rt.anchoredPosition = pos;
            rt.sizeDelta = new Vector2(520f, 300f);
            var bg = card.AddComponent<Image>();
            if (theme != null && theme.cardGreen != null)
            {
                bg.sprite = theme.cardGreen;
                bg.type = Image.Type.Sliced;
            }
            bg.color = new Color(0.05f, 0.4f, 0.62f, 0.98f);

            AddLargeIcon(card.transform, icon, new Vector2(0.21f, 0.56f), new Vector2(130f, 130f));
            var name = MakeHeading(card.transform, title, 38f, Color.white);
            var nrt = name.rectTransform;
            nrt.anchorMin = nrt.anchorMax = new Vector2(0.63f, 0.72f);
            nrt.sizeDelta = new Vector2(310f, 58f);
            var cost = MakeButtonLabel(card.transform, price, 32f, new Color(1f, 0.92f, 0.35f));
            var crt = cost.rectTransform;
            crt.anchorMin = crt.anchorMax = new Vector2(0.63f, 0.48f);
            crt.sizeDelta = new Vector2(300f, 50f);
            MakeButton(card.transform, "BUY", theme != null ? theme.buttonYellow : null,
                new Vector2(0.63f, 0.18f), new Vector2(260f, 86f), 32f, action);
        }

        void BuildShopCard(Transform root, BrawlerDefinition def, int rosterIndex, Vector2 pos)
        {
            var card = NewRect("Shop_" + def.id, root);
            var rt = (RectTransform)card.transform;
            // Anchored to the scroll content's top edge.
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 1f);
            rt.anchoredPosition = pos;
            rt.sizeDelta = new Vector2(660f, 500f);

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
            bg.color = new Color(0.04f, 0.42f, 0.74f, 0.98f);

            if (theme != null && theme.cardGlow != null)
            {
                var glow = NewRect("Glow", card.transform);
                Stretch((RectTransform)glow.transform);
                var gi = glow.AddComponent<Image>();
                gi.sprite = theme.cardGlow;
                gi.type = Image.Type.Sliced;
                gi.color = new Color(0.28f, 0.92f, 1f, 0.28f);
                gi.raycastTarget = false;
            }

            Sprite classIcon = CharacterIcon(def, rosterIndex);
            if (classIcon != null)
            {
                var frame = NewRect("SchoolCrestFrame", card.transform);
                var fr = (RectTransform)frame.transform;
                fr.anchorMin = fr.anchorMax = new Vector2(0.22f, 0.6f);
                fr.sizeDelta = new Vector2(260f, 300f);
                var fi = frame.AddComponent<Image>();
                fi.sprite = theme != null ? theme.profileFrame : null;
                if (fi.sprite != null) fi.type = Image.Type.Sliced;
                fi.color = new Color(0.06f, 0.2f, 0.42f, 0.9f);
                fi.raycastTarget = false;

                var crest = NewRect("SchoolCrest", frame.transform);
                var crestRt = (RectTransform)crest.transform;
                crestRt.anchorMin = crestRt.anchorMax = new Vector2(0.5f, 0.5f);
                crestRt.sizeDelta = new Vector2(155f, 155f);
                var img = crest.AddComponent<Image>();
                img.sprite = classIcon;
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
            urt.sizeDelta = new Vector2(500f, 118f);
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
            btnLabel.rectTransform.offsetMin = new Vector2(0f, 12f);
            btnLabel.raycastTarget = false;

            button.onClick.AddListener(() =>
            {
                if (Progress.TryUpgrade(def.id))
                {
                    ShowToast(def.displayName + " upgraded");
                    ShowCelebration(def.displayName.ToUpperInvariant(), "LEVEL UP", classIcon, true);
                    RefreshShop();
                    RefreshMenu();
                }
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
                pointsText.text = $"{c.points} / {needed} AP";
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
            var text = MakeButtonLabel(card, label, 29f, new Color(0.88f, 0.95f, 1f, 0.9f));
            var trt = text.rectTransform;
            trt.anchorMin = trt.anchorMax = new Vector2(0.22f, anchorY);
            trt.sizeDelta = new Vector2(180f, 46f);
            text.alignment = TextAlignmentOptions.Left;

            var barBg = NewRect(label + "Bg", card);
            var brt = (RectTransform)barBg.transform;
            brt.anchorMin = brt.anchorMax = new Vector2(0.63f, anchorY);
            brt.sizeDelta = new Vector2(330f, 30f);
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

        void AddCharacterSkillRow(Transform root, int index, float anchorY)
        {
            var iconFrame = NewRect("SkillIconFrame_" + index, root);
            var ifrt = (RectTransform)iconFrame.transform;
            ifrt.anchorMin = ifrt.anchorMax = new Vector2(0.1f, anchorY);
            ifrt.sizeDelta = new Vector2(64f, 64f);
            var frameImg = iconFrame.AddComponent<Image>();
            frameImg.sprite = theme != null && theme.levelFrameHighlight != null ? theme.levelFrameHighlight : theme != null ? theme.levelFrame : null;
            frameImg.preserveAspect = true;
            frameImg.color = frameImg.sprite != null ? Color.white : new Color(0.1f, 0.55f, 0.85f, 0.85f);
            frameImg.raycastTarget = false;

            var iconGo = NewRect("SkillIcon_" + index, iconFrame.transform);
            var irt = (RectTransform)iconGo.transform;
            irt.anchorMin = irt.anchorMax = new Vector2(0.5f, 0.5f);
            irt.sizeDelta = new Vector2(40f, 40f);
            var iconImg = iconGo.AddComponent<Image>();
            iconImg.sprite = theme != null && theme.passiveSkillIcon != null ? theme.passiveSkillIcon : theme != null ? theme.starOnIcon : null;
            iconImg.preserveAspect = true;
            iconImg.color = iconImg.sprite != null ? Color.white : new Color(1f, 0.9f, 0.28f, 0.95f);
            iconImg.raycastTarget = false;

            var name = MakeButtonLabel(root, "", 27f, Color.white);
            var nrt = name.rectTransform;
            nrt.anchorMin = nrt.anchorMax = new Vector2(0.39f, anchorY);
            nrt.sizeDelta = new Vector2(350f, 44f);
            name.alignment = TextAlignmentOptions.Left;
            name.enableWordWrapping = false;
            charSkillNames[index] = name;

            // Long spell copy became illegible at the target landscape size.
            // Names and current levels keep these comparison rows scannable.
            charSkillDescriptions[index] = null;

            var btnGo = NewRect("SkillButton_" + index, root);
            var brt = (RectTransform)btnGo.transform;
            brt.anchorMin = brt.anchorMax = new Vector2(0.84f, anchorY);
            brt.sizeDelta = new Vector2(170f, 64f);
            var img = btnGo.AddComponent<Image>();
            if (theme != null && theme.buttonGreen != null)
            {
                img.sprite = theme.buttonGreen;
                img.type = Image.Type.Sliced;
            }
            else
            {
                img.color = new Color(0.3f, 0.85f, 0.25f, 0.96f);
            }
            charSkillButtonImages[index] = img;

            var button = btnGo.AddComponent<Button>();
            int slot = index;
            button.onClick.AddListener(() => UpgradeCurrentSkill(slot));
            charSkillButtons[index] = button;

            var label = MakeButtonLabel(btnGo.transform, "", 22f, Color.white);
            Stretch(label.rectTransform);
            label.rectTransform.offsetMin = new Vector2(0f, 8f);
            label.raycastTarget = false;
            charSkillButtonLabels[index] = label;
        }

        GameObject AddMenuCard(Transform root, string name, Vector2 anchor, Vector2 size, Color color)
        {
            var card = NewRect(name, root);
            var rt = (RectTransform)card.transform;
            rt.anchorMin = rt.anchorMax = anchor;
            rt.sizeDelta = size;
            var bg = card.AddComponent<Image>();
            if (theme != null && theme.card != null)
            {
                bg.sprite = theme.card;
                bg.type = Image.Type.Sliced;
            }
            bg.color = color;
            bg.raycastTarget = false;
            if (theme != null && theme.cardGlow != null)
            {
                var glow = NewRect("Glow", card.transform);
                Stretch((RectTransform)glow.transform);
                var gi = glow.AddComponent<Image>();
                gi.sprite = theme.cardGlow;
                gi.type = Image.Type.Sliced;
                gi.color = new Color(0.24f, 0.9f, 1f, 0.16f);
                gi.raycastTarget = false;
            }
            return card;
        }

        void AddLargeIcon(Transform root, Sprite icon, Vector2 anchor, Vector2 size)
        {
            var frame = NewRect("IconFrame", root);
            var frt = (RectTransform)frame.transform;
            frt.anchorMin = frt.anchorMax = anchor;
            frt.sizeDelta = size * 1.02f;
            var frameImg = frame.AddComponent<Image>();
            frameImg.sprite = theme != null && theme.buttonRoundDark != null ? theme.buttonRoundDark : theme != null ? theme.buttonRound : null;
            if (frameImg.sprite != null) frameImg.preserveAspect = true;
            frameImg.color = new Color(0.02f, 0.12f, 0.24f, 0.58f);
            frameImg.raycastTarget = false;

            if (icon == null) return;
            var iconGo = NewRect("Icon", frame.transform);
            var rt = (RectTransform)iconGo.transform;
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.53f);
            rt.sizeDelta = size * 0.62f;
            var img = iconGo.AddComponent<Image>();
            img.sprite = icon;
            img.preserveAspect = true;
            img.raycastTarget = false;
        }

        void AddBadge(Transform root, string text, Vector2 anchor, Vector2 size, Color color)
        {
            var badge = NewRect("Badge_" + text, root);
            var rt = (RectTransform)badge.transform;
            rt.anchorMin = rt.anchorMax = anchor;
            rt.sizeDelta = size;
            var bg = badge.AddComponent<Image>();
            bool useDot = size.x <= size.y * 1.5f && theme != null && theme.alertDot != null;
            bg.sprite = useDot ? theme.alertDot : theme != null ? theme.labelChip : null;
            if (bg.sprite != null && !useDot) bg.type = Image.Type.Sliced;
            if (bg.sprite != null && useDot) bg.preserveAspect = true;
            bg.color = color;
            bg.raycastTarget = false;

            var label = MakeButtonLabel(badge.transform, text, size.x > 80f ? 23f : 24f, Color.white);
            Stretch(label.rectTransform);
            label.rectTransform.offsetMin = new Vector2(0f, size.y * 0.08f);
            label.raycastTarget = false;
        }

        void AddMiniStat(Transform root, string label, float value01, float y, Color color)
        {
            var text = MakeButtonLabel(root, label, 22f, new Color(1f, 1f, 1f, 0.82f));
            var trt = text.rectTransform;
            trt.anchorMin = trt.anchorMax = new Vector2(0.47f, y);
            trt.sizeDelta = new Vector2(70f, 30f);

            var fill = AddProgressBar(root, new Vector2(0.68f, y), new Vector2(210f, 20f), color);
            fill.anchorMax = new Vector2(Mathf.Clamp01(value01), 1f);
        }

        RectTransform AddProgressBar(Transform root, Vector2 anchor, Vector2 size, Color color)
        {
            var barBg = NewRect("ProgressBg", root);
            var brt = (RectTransform)barBg.transform;
            brt.anchorMin = brt.anchorMax = anchor;
            brt.sizeDelta = size;
            var bgImg = barBg.AddComponent<Image>();
            if (theme != null && theme.barBg != null)
            {
                bgImg.sprite = theme.barBg;
                bgImg.type = Image.Type.Sliced;
            }
            bgImg.color = new Color(0.03f, 0.05f, 0.11f, 0.9f);
            bgImg.raycastTarget = false;

            var fill = NewRect("ProgressFill", barBg.transform);
            var frt = (RectTransform)fill.transform;
            frt.anchorMin = Vector2.zero;
            frt.anchorMax = Vector2.one;
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
            return frt;
        }

        void MakeRewardButton(Transform root, string text, Vector2 anchor, out Button button, out TextMeshProUGUI label)
        {
            var btnGo = NewRect("RewardButton_" + text, root);
            var rt = (RectTransform)btnGo.transform;
            rt.anchorMin = rt.anchorMax = anchor;
            rt.sizeDelta = new Vector2(260f, 88f);
            var img = btnGo.AddComponent<Image>();
            if (theme != null && theme.buttonGreen != null)
            {
                img.sprite = theme.buttonGreen;
                img.type = Image.Type.Sliced;
            }
            else
            {
                img.color = new Color(0.3f, 0.85f, 0.25f, 0.96f);
            }
            button = btnGo.AddComponent<Button>();
            label = MakeButtonLabel(btnGo.transform, text, 32f, Color.white);
            Stretch(label.rectTransform);
            label.rectTransform.offsetMin = new Vector2(0f, 8f);
            label.raycastTarget = false;
        }

        void SetRewardButtonState(Button button, TextMeshProUGUI label, string text, bool enabled)
        {
            if (button != null)
            {
                button.interactable = enabled;
                var img = button.GetComponent<Image>();
                if (img != null)
                    img.color = enabled
                        ? (theme != null && theme.buttonGreen != null ? Color.white : new Color(0.3f, 0.85f, 0.25f, 0.96f))
                        : new Color(0.34f, 0.38f, 0.45f, 0.9f);
            }
            if (label != null)
            {
                label.text = text;
                label.color = enabled ? Color.white : new Color(1f, 1f, 1f, 0.58f);
            }
        }

        void BuildToast(Transform root)
        {
            var toast = NewRect("StatusToast", root);
            statusToastRoot = toast;
            var rt = (RectTransform)toast.transform;
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.025f);
            rt.pivot = new Vector2(0.5f, 0f);
            rt.sizeDelta = new Vector2(920f, 74f);
            var bg = toast.AddComponent<Image>();
            if (theme != null && theme.labelChip != null)
            {
                bg.sprite = theme.labelChip;
                bg.type = Image.Type.Sliced;
            }
            bg.color = new Color(0.03f, 0.08f, 0.16f, 0.92f);
            bg.raycastTarget = false;
            statusToast = MakeButtonLabel(toast.transform, "", 30f, Color.white);
            Stretch(statusToast.rectTransform);
            statusToastRoot.SetActive(false);
        }

        void BuildCelebrationLayer(Transform root)
        {
            var layer = NewRect("CelebrationLayer", root);
            fxLayer = (RectTransform)layer.transform;
            Stretch(fxLayer);
            layer.SetActive(true);
        }

        void ShowCelebration(string title, string body, Sprite icon, bool levelUp)
        {
            if (fxLayer == null) return;
            if (celebrationRoutine != null) StopCoroutine(celebrationRoutine);
            celebrationRoutine = StartCoroutine(CelebrationSequence(title, body, icon, levelUp));
        }

        IEnumerator CelebrationSequence(string title, string body, Sprite icon, bool levelUp)
        {
            for (int i = fxLayer.childCount - 1; i >= 0; i--)
                Destroy(fxLayer.GetChild(i).gameObject);

            var root = NewRect(levelUp ? "LevelUpCelebration" : "RewardCelebration", fxLayer);
            Stretch((RectTransform)root.transform);
            root.transform.SetAsLastSibling();

            var group = root.AddComponent<CanvasGroup>();
            group.alpha = 0f;
            group.interactable = false;
            group.blocksRaycasts = false;

            AddSolidBand(root.transform, "CelebrationDim", Vector2.zero, Vector2.one,
                new Color(0.01f, 0.04f, 0.1f, 0.3f));

            GameObject panelPrefab = levelUp
                ? (theme != null ? theme.levelUpPanelPrefab : null)
                : (theme != null ? theme.rewardPopupPrefab : null);
            PrepareCelebrationPanelBackdrop(SpawnThemePrefab(root.transform, panelPrefab,
                new Vector2(0.5f, 0.54f), new Vector2(900f, 620f), 0.58f));
            SpawnThemePrefab(root.transform, theme != null ? theme.fxRotateLight : null,
                new Vector2(0.5f, 0.54f), new Vector2(520f, 520f), 1f);
            SpawnThemePrefab(root.transform, theme != null ? theme.fxSpreadCircle : null,
                new Vector2(0.5f, 0.54f), new Vector2(620f, 620f), 1f);
            SpawnThemePrefab(root.transform, theme != null ? theme.fxSpreadStar : null,
                new Vector2(0.5f, 0.54f), new Vector2(700f, 700f), 1f);
            SpawnThemePrefab(root.transform, theme != null ? (levelUp ? theme.fxSparkleYellow : theme.fxSparkleBlue) : null,
                new Vector2(0.5f, 0.68f), new Vector2(560f, 360f), 1f);
            AddCelebrationSparkles(root.transform, levelUp);

            var card = AddMenuCard(root.transform, "CelebrationCard", new Vector2(0.5f, 0.53f),
                new Vector2(790f, 360f), levelUp
                    ? new Color(0.05f, 0.35f, 0.72f, 0.96f)
                    : new Color(0.05f, 0.42f, 0.2f, 0.96f));
            if (theme != null && theme.glow != null)
            {
                var halo = NewRect("Halo", card.transform);
                var hrt = (RectTransform)halo.transform;
                hrt.anchorMin = hrt.anchorMax = new Vector2(0.5f, 0.55f);
                hrt.sizeDelta = new Vector2(520f, 300f);
                var hi = halo.AddComponent<Image>();
                hi.sprite = theme.glow;
                hi.material = theme.additiveMaterial;
                hi.color = levelUp ? new Color(1f, 0.82f, 0.18f, 0.34f) : new Color(0.34f, 0.95f, 1f, 0.3f);
                hi.raycastTarget = false;
                halo.transform.SetAsFirstSibling();
            }

            AddLargeIcon(card.transform, icon != null ? icon : (theme != null ? theme.starOnIcon : null),
                new Vector2(0.5f, 0.6f), new Vector2(150f, 150f));

            var heading = MakeHeading(card.transform, title, 62f, Color.white);
            var h = heading.rectTransform;
            h.anchorMin = h.anchorMax = new Vector2(0.5f, 0.86f);
            h.sizeDelta = new Vector2(660f, 78f);

            var message = MakeButtonLabel(card.transform, body, 36f, new Color(1f, 0.93f, 0.35f));
            var m = message.rectTransform;
            m.anchorMin = m.anchorMax = new Vector2(0.5f, 0.22f);
            m.sizeDelta = new Vector2(660f, 70f);

            for (float t = 0f; t < 0.18f; t += Time.unscaledDeltaTime)
            {
                group.alpha = Mathf.SmoothStep(0f, 1f, t / 0.18f);
                card.transform.localScale = Vector3.one * Mathf.Lerp(0.86f, 1.04f, t / 0.18f);
                yield return null;
            }
            group.alpha = 1f;
            card.transform.localScale = Vector3.one;

            yield return new WaitForSecondsRealtime(1.25f);

            for (float t = 0f; t < 0.24f; t += Time.unscaledDeltaTime)
            {
                group.alpha = 1f - Mathf.SmoothStep(0f, 1f, t / 0.24f);
                card.transform.localScale = Vector3.one * Mathf.Lerp(1f, 0.94f, t / 0.24f);
                yield return null;
            }
            Destroy(root);
            celebrationRoutine = null;
        }

        void AddCelebrationSparkles(Transform root, bool levelUp)
        {
            if (theme == null || theme.starOnIcon == null) return;

            Color primary = levelUp
                ? new Color(1f, 0.86f, 0.22f, 0.95f)
                : new Color(0.4f, 0.95f, 1f, 0.95f);
            Color secondary = levelUp
                ? new Color(0.72f, 0.95f, 1f, 0.8f)
                : new Color(1f, 0.95f, 0.45f, 0.8f);

            Vector2[] positions =
            {
                new Vector2(-440f, 190f), new Vector2(-330f, 285f), new Vector2(-170f, 255f),
                new Vector2(170f, 265f), new Vector2(330f, 285f), new Vector2(440f, 190f),
                new Vector2(-450f, -80f), new Vector2(-300f, -210f), new Vector2(300f, -210f),
                new Vector2(450f, -80f), new Vector2(-530f, 45f), new Vector2(530f, 45f)
            };
            float[] sizes = { 64f, 42f, 34f, 36f, 42f, 64f, 38f, 54f, 54f, 38f, 30f, 30f };

            for (int i = 0; i < positions.Length; i++)
            {
                var sparkle = NewRect("CelebrationSparkle", root);
                var rt = (RectTransform)sparkle.transform;
                rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.54f);
                rt.pivot = new Vector2(0.5f, 0.5f);
                rt.anchoredPosition = positions[i];
                rt.sizeDelta = new Vector2(sizes[i], sizes[i]);
                rt.localRotation = Quaternion.Euler(0f, 0f, i * 23f);

                var image = sparkle.AddComponent<Image>();
                image.sprite = theme.starOnIcon;
                image.material = theme.additiveMaterial;
                image.color = i % 2 == 0 ? primary : secondary;
                image.raycastTarget = false;
            }
        }

        GameObject SpawnThemePrefab(Transform root, GameObject prefab, Vector2 anchor, Vector2 size, float scale)
        {
            if (prefab == null) return null;
            var go = Instantiate(prefab, root, false);
            go.name = prefab.name + "_Runtime";
            var rt = go.transform as RectTransform;
            if (rt != null)
            {
                rt.anchorMin = rt.anchorMax = anchor;
                rt.pivot = new Vector2(0.5f, 0.5f);
                rt.anchoredPosition = Vector2.zero;
                rt.sizeDelta = size;
                rt.localScale = Vector3.one * scale;
            }
            else
            {
                go.transform.localPosition = Vector3.zero;
                go.transform.localScale = Vector3.one * scale;
            }

            foreach (var graphic in go.GetComponentsInChildren<Graphic>(true))
                graphic.raycastTarget = false;
            foreach (var button in go.GetComponentsInChildren<Button>(true))
                button.interactable = false;
            foreach (var ps in go.GetComponentsInChildren<ParticleSystem>(true))
                ps.Play(true);

            return go;
        }

        void PrepareCelebrationPanelBackdrop(GameObject panel)
        {
            if (panel == null) return;

            foreach (var nestedCanvas in panel.GetComponentsInChildren<Canvas>(true))
            {
                nestedCanvas.overrideSorting = false;
                nestedCanvas.sortingOrder = 0;
            }

            foreach (var text in panel.GetComponentsInChildren<TMP_Text>(true))
                text.enabled = false;

            var group = panel.GetComponent<CanvasGroup>();
            if (group == null) group = panel.AddComponent<CanvasGroup>();
            group.alpha = 0.48f;
            group.interactable = false;
            group.blocksRaycasts = false;
            panel.transform.SetSiblingIndex(Mathf.Min(1, panel.transform.parent.childCount - 1));
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

        void AddScreenTitle(Transform root, string text)
        {
            var title = MakeHeading(root, text, 78f, Color.white);
            var trt = title.rectTransform;
            trt.anchorMin = trt.anchorMax = new Vector2(0.18f, 0.92f);
            trt.pivot = new Vector2(0f, 0.5f);
            trt.sizeDelta = new Vector2(1000f, 110f);
            title.alignment = TextAlignmentOptions.Left;
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
            rt.sizeDelta = new Vector2(112f, 124f);
            var img = btn.AddComponent<Image>();
            if (theme != null && theme.buttonRoundDark != null)
            {
                img.sprite = theme.buttonRoundDark;
                img.preserveAspect = true;
                img.color = new Color(0.72f, 0.9f, 1f, 0.94f);
            }
            else
            {
                img.color = new Color(0.04f, 0.15f, 0.28f, 0.94f);
            }
            btn.AddComponent<Button>().onClick.AddListener(onClick);

            Sprite glyphSprite = theme != null ? (right ? theme.arrowRight : theme.arrowLeft) : null;
            if (glyphSprite != null)
            {
                var glyph = NewRect("Glyph", btn.transform);
                var grt = (RectTransform)glyph.transform;
                grt.anchorMin = grt.anchorMax = new Vector2(0.5f, 0.53f);
                grt.sizeDelta = new Vector2(44f, 54f);
                var gi = glyph.AddComponent<Image>();
                gi.sprite = glyphSprite;
                gi.preserveAspect = true;
                gi.color = Color.white;
                gi.raycastTarget = false;
            }
            else
            {
                var label = MakeHeading(btn.transform, right ? ">" : "<", 48f, Color.white);
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
            TMP_FontAsset font = readableBodyFont != null
                ? readableBodyFont
                : theme != null ? theme.bodyFont : null;
            return MakeTmp(parent, text, size, color, font);
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
            tmp.enableWordWrapping = true;
            tmp.overflowMode = TextOverflowModes.Ellipsis;
            tmp.characterSpacing = 0f;
            tmp.wordSpacing = 0f;
            tmp.lineSpacing = 0f;
            tmp.margin = new Vector4(4f, 0f, 4f, 0f);
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
