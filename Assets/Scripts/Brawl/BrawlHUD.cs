using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.UI;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace BrawlArena
{
    /// <summary>
    /// In-match HUD for the mobile brawler. It is constructed at runtime so
    /// the Arena scene stays clean, while its visual language is sourced from
    /// the shared UiTheme used by the main menu.
    /// </summary>
    public class BrawlHUD : MonoBehaviour
    {
        const int MaxSquadRows = 5;

        enum HudTextStyle
        {
            Body,
            Button,
            Display,
        }

        /// <summary>A row in the squad panel: portrait, name, HP, and Super readiness.</summary>
        sealed class SquadRow
        {
            public GameObject root;
            public CanvasGroup group;
            public Image background;
            public Image portraitFrame;
            public Image portrait;
            public TextMeshProUGUI identity;
            public Image healthFill;
            public Image superDiamond;
            public Image superGlow;
            public GameObject respawnOverlay;
            public TextMeshProUGUI respawnText;
            public BrawlerController owner;
            public Sprite shownPortrait;
            public int shownHealth = -1;
            public int shownMaxHealth = -1;
            public bool shownDead;
            public string shownIdentity;
        }

        /// <summary>Structured result data used by the post-match reveal.</summary>
        public struct MatchRewardSummary
        {
            public int eliminations;
            public int brawlerPoints;
            public int coins;
            public int pointsBefore;
            public int pointsAfter;
            public int level;
            public int pointsNeeded;
        }

        public static BrawlHUD Instance { get; private set; }

        public VirtualJoystick Joystick { get; private set; }
        public bool AttackHeld => attackButton != null && attackButton.Held;
        public float AttackHeldDuration => attackButton != null ? attackButton.HeldDuration : 0f;
        public RectTransform RightCastSurface { get; private set; }

        AttackButtonWidget attackButton;
        AttackButtonWidget wardStepButton;
        AttackButtonWidget superButton;
        Image staminaFill;
        TextMeshProUGUI staminaText;
        Image wardStepOuter;
        TextMeshProUGUI wardFeedbackText;
        GameObject gameplayRoot;
        Image cooldownOverlay;
        readonly Image[] basicAttackChargePips =
            new Image[MobileCombatRules.BasicAttackChargeCapacity];
        TextMeshProUGUI basicAttackReloadText;
        Image superChargeOverlay;
        Image superButtonOuter;
        TextMeshProUGUI superAbilityText;
        TextMeshProUGUI superReadinessText;
        TextMeshProUGUI superFeedbackText;
        TextMeshProUGUI playerHealthText;
        Image playerSuperStrip;
        TextMeshProUGUI playerMatchLevelText;
        TextMeshProUGUI playerMatchExperienceText;
        Image playerMatchExperienceFill;
        GameObject personalGemRoot;
        TextMeshProUGUI personalGemText;
        TextMeshProUGUI timerText;
        TextMeshProUGUI blueScoreText;
        TextMeshProUGUI redScoreText;
        GameObject blueGemIcon;
        GameObject redGemIcon;
        GameObject announcementRoot;
        TextMeshProUGUI centerText;
        UiTheme theme;
        TextMeshProUGUI zoneModeLabel;
        TextMeshProUGUI zoneObjectiveLabel;
        Image zoneBarBlueFill;
        Image zoneBarRedFill;
        Image zoneCenterChip;
        readonly Image[] zoneBluePips = new Image[3];
        readonly Image[] zoneRedPips = new Image[3];
        TextMeshProUGUI respawnText;
        TextMeshProUGUI protectionText;
        TextMeshProUGUI bannerTitle;
        TextMeshProUGUI bannerSub;
        Image bannerIcon;
        GameObject bannerRoot;
        GameObject resultCard;
        Transform resultFxRoot;
        CanvasGroup resultCardGroup;
        CanvasGroup eliminationsRow;
        CanvasGroup brawlerPointsRow;
        CanvasGroup coinsRow;
        CanvasGroup progressionRow;
        CanvasGroup resultButtonsGroup;
        TextMeshProUGUI eliminationsValue;
        TextMeshProUGUI brawlerPointsValue;
        TextMeshProUGUI coinsValue;
        TextMeshProUGUI progressionLabel;
        TextMeshProUGUI progressionValue;
        RectTransform progressionFill;
        Button replayButton;
        Button menuButton;
        GameObject respawnRoot;
        GameObject protectionRoot;
        GameObject hiddenChipRoot;
        GameObject squadRoot;
        TextMeshProUGUI squadHeader;
        BrawlerController player;
        HeroMatchProgression playerMatchProgression;
        TeamId? shownSquadTeam;

        readonly SquadRow[] squadRows = new SquadRow[MaxSquadRows];
        readonly BrawlerController[] orderedSquadMembers = new BrawlerController[MaxSquadRows];

        int lastBlue = -1;
        int lastRed = -1;
        int lastSeconds = -1;
        int lastRespawnTenths = -1;
        int lastProtectionTenths = -1;
        int lastHealth = -1;
        int lastMaxHealth = -1;
        int lastPersonalGems = -1;
        int lastSuperPercent = -1;
        int lastStamina = -1;
        int lastMaxStamina = -1;
        int lastBasicAttackCharges = -1;
        int lastBasicAttackReloadTenths = -1;
        int lastMatchLevel = -1;
        int lastMatchExperience = -1;
        int lastMatchExperienceToNext = -1;
        string lastSuperName;
        float respawnEndsAt;
        float fightFlashUntil;
        float superFeedbackUntil;
        float wardFeedbackUntil;
        MatchState prevState = MatchState.Waiting;
        bool hasModePresentation;
        GameMode lastPresentedMode = (GameMode)(-1);
        ControlZoneState lastZoneState = (ControlZoneState)(-1);
        bool lastZoneOvertime;
        bool matchEnded;
        bool replayLoading;
        bool hasMatchRewardSummary;
        string rewardLine;
        MatchRewardSummary matchRewardSummary;
        Coroutine resultRoutine;

        static Sprite circleSprite;
        static Sprite whiteSprite;

        void Awake()
        {
            Instance = this;
            // Direct lookup instead of UiTheme.Instance: Awake order is not
            // guaranteed when both objects are loaded from the scene.
            theme = FindFirstObjectByType<UiTheme>();
            EnsureEventSystem();
            Build();
        }

        void OnDestroy()
        {
            if (Instance == this) Instance = null;
            if (MatchManager.Instance != null) MatchManager.Instance.MatchEnded -= OnMatchEnded;
        }

        void Start()
        {
            player = ResolveLocalPlayer();
            if (MatchManager.Instance != null) MatchManager.Instance.MatchEnded += OnMatchEnded;
        }

        /// <summary>
        /// Binds the HUD to whichever brawler the human plays. The normal
        /// path finds the live PlayerBrawlerInput component; a bot-driven or
        /// autopilot-controlled session never spawns one, which used to leave
        /// every widget (HP, XP, squad, respawn, super strip) stuck at "--/--"
        /// for the whole match. GameFlow always pins the human lineup slot at
        /// Blue team, match spawn slot 0, regardless of who is driving it, so
        /// that is the fallback anchor.
        /// </summary>
        BrawlerController ResolveLocalPlayer()
        {
            var pi = FindFirstObjectByType<PlayerBrawlerInput>();
            if (pi != null) return pi.GetComponent<BrawlerController>();

            MatchManager mm = MatchManager.Instance;
            if (mm == null) return null;
            var brawlers = mm.GetBrawlers();
            for (int i = 0; i < brawlers.Count; i++)
            {
                BrawlerController candidate = brawlers[i];
                if (candidate != null && candidate.team == TeamId.Blue &&
                    candidate.MatchSpawnSlot == 0)
                    return candidate;
            }
            return null;
        }

        public bool ConsumeAttackPressed()
        {
            return attackButton != null && attackButton.ConsumePressed();
        }

        /// <summary>Consumes an attack gesture cancelled by a camera-orbit takeover.</summary>
        public bool ConsumeAttackCancelled()
        {
            return attackButton != null && attackButton.ConsumeCancelled();
        }

        public bool ConsumeSuperPressed()
        {
            return superButton != null && superButton.ConsumePressed();
        }

        public bool ConsumeWardStepPressed()
        {
            if (wardStepButton == null) return false;
            // Ward Step fires on pointer-down, so its pointer-up is only
            // lifecycle bookkeeping. Drain it every frame to admit the next tap.
            wardStepButton.ConsumeReleased(out _);
            return wardStepButton.ConsumePressed();
        }

        /// <summary>Current attack drag in screen pixels, from press origin to pointer.</summary>
        public bool TryGetAttackAimDrag(out Vector2 screenDrag)
        {
            if (attackButton != null) return attackButton.TryGetDrag(out screenDrag);
            screenDrag = Vector2.zero;
            return false;
        }

        /// <summary>Consumes the completed attack gesture and returns its screen-space drag.</summary>
        public bool ConsumeAttackReleased(out Vector2 screenDrag)
        {
            if (attackButton != null) return attackButton.ConsumeReleased(out screenDrag);
            screenDrag = Vector2.zero;
            return false;
        }

        /// <summary>Consumes the completed Super gesture and returns its screen-space drag.</summary>
        public bool ConsumeSuperReleased(out Vector2 screenDrag)
        {
            if (superButton != null) return superButton.ConsumeReleased(out screenDrag);
            screenDrag = Vector2.zero;
            return false;
        }

        /// <summary>Brief, visible acknowledgement when a requested Super cannot execute.</summary>
        public void ShowSuperFailure()
        {
            superFeedbackUntil = Time.unscaledTime + 0.9f;
            if (superFeedbackText == null) return;

            bool unavailable = player == null || !player.CanAct;
            bool charging = !unavailable && !player.SuperReady;
            superFeedbackText.text = unavailable
                ? "SUPER UNAVAILABLE"
                : charging
                    ? "SUPER CHARGING"
                    : "NO VALID TARGET";
            superFeedbackText.color = charging
                ? new Color(1f, 0.72f, 0.22f)
                : new Color(1f, 0.34f, 0.28f);
            superFeedbackText.gameObject.SetActive(true);
        }

        /// <summary>Explains a rejected Ward Step without consuming Flow.</summary>
        public void ShowWardStepFailure()
        {
            wardFeedbackUntil = Time.unscaledTime + 0.75f;
            if (wardFeedbackText == null) return;
            wardFeedbackText.text = player != null &&
                                    player.WardFlow + 0.0001f < player.WardStepCost
                ? "RECHARGING"
                : "PATH BLOCKED";
            wardFeedbackText.gameObject.SetActive(true);
        }

        /// <summary>Show or hide the active combat controls and status UI.</summary>
        public void SetGameplayVisible(bool visible)
        {
            if (gameplayRoot != null) gameplayRoot.SetActive(visible);
        }

        public void ShowRespawn(float duration)
        {
            respawnEndsAt = Time.time + duration;
            if (respawnRoot != null) respawnRoot.SetActive(true);
        }

        public void HideRespawn()
        {
            if (respawnRoot != null) respawnRoot.SetActive(false);
        }

        void Update()
        {
            var mm = MatchManager.Instance;
            if (mm == null) return;

            int seconds = Mathf.CeilToInt(mm.TimeRemaining);
            if (mm.State == MatchState.Overtime)
            {
                if (lastSeconds != -2)
                {
                    lastSeconds = -2;
                    timerText.text = "OT";
                }
            }
            else if (seconds != lastSeconds)
            {
                lastSeconds = seconds;
                timerText.SetText("{0}:{1:00}", seconds / 60, seconds % 60);
            }

            bool gemMode = mm.mode == GameMode.GemGrab && GemGrabManager.Instance != null;
            var gems = GemGrabManager.Instance;
            bool controlMode = mm.mode == GameMode.ControlZone;
            ControlZoneManager zone = ControlZoneManager.Instance;
            ControlZoneState zoneState = zone != null
                ? zone.State
                : ControlZoneState.Inactive;
            bool zoneOvertime = mm.State == MatchState.Overtime;
            if (!hasModePresentation || lastPresentedMode != mm.mode ||
                (controlMode && (lastZoneState != zoneState ||
                                 lastZoneOvertime != zoneOvertime)))
            {
                hasModePresentation = true;
                lastPresentedMode = mm.mode;
                lastZoneState = zoneState;
                lastZoneOvertime = zoneOvertime;
                if (controlMode)
                {
                    // Two separate, properly kerned lines read as words at a
                    // glance; personas misread the old single-line "CONTROL
                    // ZONE - FIRST TO 90" mash as a debug counter, so the mode
                    // name is its own badge and the objective is a distinct
                    // sub-line beneath it.
                    zoneModeLabel.text = zoneOvertime ? "OVERTIME" : "CONTROL ZONE";
                    zoneObjectiveLabel.text = zoneOvertime
                        ? "NEXT POINT WINS"
                        : "FIRST TO " + mm.scoreToWin;
                }
                else
                {
                    zoneModeLabel.text = gemMode ? "GEM GRAB" : "KNOCKOUT";
                    zoneObjectiveLabel.text = gemMode
                        ? "FIRST TO " + gems.gemsToWin + " GEMS"
                        : "FIRST TO " + mm.scoreToWin + " KOS";
                }
            }

            int blueValue = gemMode ? gems.TeamGems(TeamId.Blue) : mm.BlueScore;
            int redValue = gemMode ? gems.TeamGems(TeamId.Red) : mm.RedScore;
            if (blueValue != lastBlue)
            {
                lastBlue = blueValue;
                blueScoreText.text = blueValue.ToString();
            }
            if (redValue != lastRed)
            {
                lastRed = redValue;
                redScoreText.text = redValue.ToString();
            }

            int zoneTarget = gemMode ? Mathf.Max(1, gems.gemsToWin) : Mathf.Max(1, mm.scoreToWin);
            zoneBarBlueFill.fillAmount = Mathf.Clamp01(blueValue / (float)zoneTarget);
            zoneBarRedFill.fillAmount = Mathf.Clamp01(redValue / (float)zoneTarget);
            UpdateZoneCenterChip(controlMode, zoneState, zone);
            if (blueGemIcon != null && blueGemIcon.activeSelf != gemMode)
            {
                blueGemIcon.SetActive(gemMode);
                redGemIcon.SetActive(gemMode);
            }
            if (personalGemRoot != null && personalGemRoot.activeSelf != gemMode)
                personalGemRoot.SetActive(gemMode);

            if (prevState == MatchState.Intro && mm.State == MatchState.Playing)
                fightFlashUntil = Time.time + 0.8f;
            prevState = mm.State;

            if (mm.State == MatchState.Intro)
            {
                ShowAnnouncement("READY", Color.white);
            }
            else if (Time.time < fightFlashUntil)
            {
                ShowAnnouncement("FIGHT!", new Color(1f, 0.88f, 0.28f));
            }
            else if (gemMode && gems.CountdownTeam.HasValue && mm.State == MatchState.Playing)
            {
                int countdown = Mathf.CeilToInt(gems.CountdownRemaining);
                string team = gems.CountdownTeam.Value == TeamId.Blue ? "BLUE" : "RED";
                ShowAnnouncement(team + " WINS IN " + countdown, TeamUtil.Color(gems.CountdownTeam.Value));
            }
            else if (controlMode && mm.State == MatchState.Overtime)
            {
                ShowAnnouncement("OVERTIME - ZONE EXPANDING",
                    new Color(1f, 0.42f, 0.82f));
            }
            else
            {
                HideAnnouncement();
            }

            if (player == null) player = ResolveLocalPlayer();

            // Polled directly from the bound player rather than only reacting
            // to BrawlerController's ShowRespawn/HideRespawn push, which is
            // gated on its own IsPlayer flag and never fires for a spectated
            // or autopilot-controlled human slot (no PlayerBrawlerInput to
            // set that flag).
            if (respawnRoot != null)
            {
                bool respawningNow = !matchEnded && player != null && player.IsRespawning;
                if (respawnRoot.activeSelf != respawningNow)
                    respawnRoot.SetActive(respawningNow);
                if (respawningNow)
                {
                    int tenths = Mathf.Max(0, Mathf.CeilToInt(player.RespawnRemaining * 10f));
                    if (tenths != lastRespawnTenths)
                    {
                        lastRespawnTenths = tenths;
                        respawnText.text = $"RESPAWNING IN {tenths / 10f:0.0}";
                    }
                }
                else
                {
                    lastRespawnTenths = -1;
                }
            }
            if (hiddenChipRoot != null)
            {
                bool concealedNow = player != null && !player.IsDead && !matchEnded &&
                                    player.Concealment != null && player.Concealment.SelfConcealed;
                if (hiddenChipRoot.activeSelf != concealedNow)
                    hiddenChipRoot.SetActive(concealedNow);
            }

            if (protectionRoot != null && player != null)
            {
                bool protectedNow = player.IsSpawnProtected;
                if (protectionRoot.activeSelf != protectedNow)
                    protectionRoot.SetActive(protectedNow);
                if (protectedNow)
                {
                    int tenths = Mathf.Max(0,
                        Mathf.CeilToInt(player.SpawnProtectionRemaining * 10f));
                    if (tenths != lastProtectionTenths)
                    {
                        lastProtectionTenths = tenths;
                        protectionText.SetText("SPAWN SHIELD {0:0.0}", tenths / 10f);
                    }
                }
                else
                {
                    lastProtectionTenths = -1;
                }
            }
            UpdatePlayerMatchProgression();
            UpdateSquadPanel(mm);
            if (player != null && cooldownOverlay != null)
                cooldownOverlay.fillAmount = player.CooldownFraction;
            UpdateBasicAttackChargeFeedback();

            if (player != null && player.Health != null && playerHealthText != null)
            {
                int health = Mathf.CeilToInt(player.Health.Current);
                int maxHealth = Mathf.CeilToInt(player.Health.Max);
                if (health != lastHealth || maxHealth != lastMaxHealth)
                {
                    lastHealth = health;
                    lastMaxHealth = maxHealth;
                    playerHealthText.text = health.ToString("N0") + " / " +
                                            maxHealth.ToString("N0") + " HP";
                    playerHealthText.color = health <= maxHealth * 0.3f
                        ? new Color(1f, 0.38f, 0.3f)
                        : Color.white;
                }
            }

            if (gemMode && player != null && personalGemText != null)
            {
                int carriedGems = gems.CarriedBy(player);
                if (carriedGems != lastPersonalGems)
                {
                    lastPersonalGems = carriedGems;
                    personalGemText.text = "CARRY " + carriedGems;
                    personalGemText.color = carriedGems >= 4
                        ? new Color(1f, 0.82f, 0.26f)
                        : Color.white;
                }
            }

            if (player != null && staminaFill != null)
            {
                float fraction = player.maxStamina > 0f ? player.Stamina / player.maxStamina : 0f;
                staminaFill.fillAmount = fraction;
                staminaFill.color = fraction < 0.3f
                    ? new Color(1f, 0.47f, 0.22f)
                    : new Color(1f, 0.88f, 0.24f);

                int stamina = Mathf.CeilToInt(player.Stamina);
                int maxStamina = Mathf.CeilToInt(player.maxStamina);
                if (staminaText != null &&
                    (stamina != lastStamina || maxStamina != lastMaxStamina))
                {
                    lastStamina = stamina;
                    lastMaxStamina = maxStamina;
                    staminaText.SetText("{0} / {1}", stamina, maxStamina);
                }

                if (wardStepOuter != null)
                {
                    bool ready = player.WardFlow + 0.0001f >= player.WardStepCost;
                    wardStepOuter.color = Time.unscaledTime < wardFeedbackUntil
                        ? new Color(1f, 0.32f, 0.24f)
                        : ready
                            ? new Color(0.25f, 0.9f, 0.82f)
                            : new Color(0.18f, 0.38f, 0.4f);
                }
            }

            if (player != null)
            {
                int superPercent = Mathf.RoundToInt(player.SuperCharge01 * 100f);
                if (superChargeOverlay != null)
                    superChargeOverlay.fillAmount = 1f - player.SuperCharge01;
                if (playerSuperStrip != null)
                {
                    playerSuperStrip.fillAmount = player.SuperCharge01;
                    playerSuperStrip.color = player.SuperReady
                        ? new Color(1f, 0.86f, 0.28f)
                        : new Color(0.62f, 0.24f, 0.92f);
                }
                if (superAbilityText != null && lastSuperName != player.SuperName)
                {
                    lastSuperName = player.SuperName;
                    superAbilityText.text = player.SuperName;
                }
                if (superReadinessText != null && superPercent != lastSuperPercent)
                {
                    lastSuperPercent = superPercent;
                    superReadinessText.text = player.SuperReady ? "READY" : superPercent + "%";
                    superReadinessText.color = player.SuperReady
                        ? new Color(1f, 0.86f, 0.28f)
                        : new Color(0.82f, 0.76f, 0.94f);
                }
                if (superButtonOuter != null)
                {
                    superButtonOuter.color = Time.unscaledTime < superFeedbackUntil
                        ? new Color(1f, 0.28f, 0.24f)
                        : player.SuperReady
                            ? new Color(1f, 0.74f, 0.2f)
                            : new Color(0.35f, 0.2f, 0.58f);
                }
            }

            if (superFeedbackText != null && superFeedbackText.gameObject.activeSelf &&
                Time.unscaledTime >= superFeedbackUntil)
                superFeedbackText.gameObject.SetActive(false);
            if (wardFeedbackText != null && wardFeedbackText.gameObject.activeSelf &&
                Time.unscaledTime >= wardFeedbackUntil)
                wardFeedbackText.gameObject.SetActive(false);

            if (mm.State == MatchState.Ended && Keyboard.current != null &&
                Keyboard.current.rKey.wasPressedThisFrame)
                Restart();
        }

        /// <summary>
        /// Colors the zone-meter center chip and lights the per-team occupant
        /// pips. Pips and zone-state coloring only mean something in Control
        /// Zone; other modes show a neutral chip with the pips dark.
        /// </summary>
        void UpdateZoneCenterChip(bool controlMode, ControlZoneState zoneState, ControlZoneManager zone)
        {
            if (zoneCenterChip == null) return;

            Color chipColor;
            int blueOccupants = 0;
            int redOccupants = 0;
            if (controlMode)
            {
                chipColor = zoneState == ControlZoneState.BlueControlled
                    ? TeamUtil.Color(TeamId.Blue)
                    : zoneState == ControlZoneState.RedControlled
                        ? TeamUtil.Color(TeamId.Red)
                        : zoneState == ControlZoneState.Contested
                            ? new Color(1f, 0.68f, 0.12f)
                            : new Color(0.32f, 0.4f, 0.5f);
                if (zoneState == ControlZoneState.Contested)
                {
                    float pulse = AccessibilitySettings.ReducedMotionEnabled
                        ? 1f
                        : 0.82f + 0.18f * (0.5f + 0.5f * Mathf.Sin(Time.unscaledTime * 5f));
                    chipColor *= pulse;
                }
                if (zone != null)
                {
                    blueOccupants = zone.BlueOccupants;
                    redOccupants = zone.RedOccupants;
                }
            }
            else
            {
                chipColor = new Color(0.16f, 0.2f, 0.26f, 0.96f);
            }
            zoneCenterChip.color = chipColor;

            for (int i = 0; i < zoneBluePips.Length; i++)
            {
                if (zoneBluePips[i] == null) continue;
                zoneBluePips[i].enabled = controlMode;
                zoneBluePips[i].color = i < blueOccupants
                    ? new Color(0.4f, 0.82f, 1f)
                    : new Color(0.4f, 0.82f, 1f, 0.22f);
            }
            for (int i = 0; i < zoneRedPips.Length; i++)
            {
                if (zoneRedPips[i] == null) continue;
                zoneRedPips[i].enabled = controlMode;
                zoneRedPips[i].color = i < redOccupants
                    ? new Color(1f, 0.48f, 0.42f)
                    : new Color(1f, 0.48f, 0.42f, 0.22f);
            }
        }

        void UpdatePlayerMatchProgression()
        {
            HeroMatchProgression progression = player != null
                ? player.GetComponent<HeroMatchProgression>()
                : null;
            if (progression != playerMatchProgression)
            {
                playerMatchProgression = progression;
                lastMatchLevel = -1;
                lastMatchExperience = -1;
                lastMatchExperienceToNext = -1;
            }

            if (playerMatchLevelText == null || playerMatchExperienceText == null ||
                playerMatchExperienceFill == null)
                return;

            if (progression == null)
            {
                if (lastMatchLevel != 0)
                {
                    playerMatchLevelText.text = "LV 1";
                    playerMatchExperienceText.text = "MATCH XP  --";
                    playerMatchExperienceFill.fillAmount = 0f;
                    lastMatchLevel = 0;
                }
                return;
            }

            int level = progression.Level;
            int experience = progression.Experience;
            int experienceToNext = progression.ExperienceToNext;
            if (level == lastMatchLevel && experience == lastMatchExperience &&
                experienceToNext == lastMatchExperienceToNext)
                return;

            lastMatchLevel = level;
            lastMatchExperience = experience;
            lastMatchExperienceToNext = experienceToNext;
            playerMatchLevelText.text = "LV " + level;
            playerMatchExperienceText.text = experienceToNext > 0
                ? "MATCH XP  " + experience + " / " + experienceToNext
                : "MATCH XP  MAX";
            playerMatchExperienceFill.fillAmount = progression.Experience01;
        }

        void UpdateSquadPanel(MatchManager manager)
        {
            if (squadRoot == null) return;
            bool hasLocalPlayer = player != null;
            if (squadRoot.activeSelf != hasLocalPlayer)
                squadRoot.SetActive(hasLocalPlayer);
            if (!hasLocalPlayer)
            {
                shownSquadTeam = null;
                for (int i = 0; i < squadRows.Length; i++)
                    SetSquadRowOwner(squadRows[i], null);
                return;
            }

            if (!shownSquadTeam.HasValue || shownSquadTeam.Value != player.team)
            {
                shownSquadTeam = player.team;
                squadHeader.text = TeamUtil.ClanName(player.team) + "  •  " +
                                    TeamUtil.CueLabel(player.team, player.team);
                squadHeader.color = Color.Lerp(TeamUtil.Color(player.team), Color.white, 0.34f);
            }

            int memberCount = 0;
            orderedSquadMembers[memberCount++] = player;
            var brawlers = manager.GetBrawlers();
            for (int i = 0; i < brawlers.Count && memberCount < MaxSquadRows; i++)
            {
                BrawlerController candidate = brawlers[i];
                if (candidate == null || candidate == player || candidate.team != player.team)
                    continue;
                orderedSquadMembers[memberCount++] = candidate;
            }

            for (int i = 0; i < squadRows.Length; i++)
            {
                BrawlerController member = i < memberCount ? orderedSquadMembers[i] : null;
                SetSquadRowOwner(squadRows[i], member);
                if (member != null) RefreshSquadRow(squadRows[i], i);
                orderedSquadMembers[i] = null;
            }
        }

        void SetSquadRowOwner(SquadRow row, BrawlerController owner)
        {
            if (row == null || row.owner == owner) return;
            row.owner = owner;
            row.shownPortrait = null;
            row.shownIdentity = null;
            row.shownHealth = -1;
            row.shownMaxHealth = -1;
            row.shownDead = false;
            if (row.root != null) row.root.SetActive(owner != null);
        }

        void RefreshSquadRow(SquadRow row, int rowIndex)
        {
            BrawlerController member = row.owner;
            if (member == null) return;

            bool isLocal = member == player;
            bool isDead = member.Health == null || member.IsDead;
            bool isRespawning = member.IsRespawning;
            Color teamColor = TeamUtil.Color(member.team);
            row.background.color = isLocal
                ? new Color(teamColor.r * 0.42f, teamColor.g * 0.42f,
                    teamColor.b * 0.42f, 0.96f)
                : new Color(0.025f, 0.075f, 0.14f, 0.9f);
            row.portraitFrame.color = teamColor;
            row.group.alpha = isRespawning ? 0.55f : isDead ? 0.58f : 1f;

            string tag = string.IsNullOrWhiteSpace(member.playerTag)
                ? isLocal ? "YOU" : "ALLY"
                : member.playerTag.Trim();
            string heroName = string.IsNullOrWhiteSpace(member.displayName)
                ? "HERO"
                : member.displayName.Trim();
            string identity = tag.ToUpperInvariant() + "  •  " + heroName.ToUpperInvariant();
            if (isLocal && !tag.Equals("YOU", System.StringComparison.OrdinalIgnoreCase))
                identity = "YOU  •  " + identity;
            if (identity != row.shownIdentity)
            {
                row.shownIdentity = identity;
                row.identity.text = identity;
                row.identity.color = isLocal ? Color.white : new Color(0.9f, 0.96f, 1f);
            }

            Sprite portrait = member.portrait;
            if (portrait == null && theme != null)
                portrait = theme.SchoolIcon(member.specialty.school.ToString(),
                    (int)member.specialty.school + rowIndex);
            if (portrait != row.shownPortrait)
            {
                row.shownPortrait = portrait;
                row.portrait.sprite = portrait;
                row.portrait.enabled = portrait != null;
            }

            int health = member.Health != null ? Mathf.CeilToInt(member.Health.Current) : 0;
            int maxHealth = member.Health != null ? Mathf.CeilToInt(member.Health.Max) : 0;
            row.shownHealth = health;
            row.shownMaxHealth = maxHealth;
            row.shownDead = isDead;
            row.healthFill.fillAmount = maxHealth > 0
                ? Mathf.Clamp01(health / (float)maxHealth)
                : 0f;
            row.healthFill.color = isDead
                ? new Color(0.5f, 0.5f, 0.5f, 0.6f)
                : new Color(0.25f, 0.94f, 0.48f);

            bool superReady = member.SuperReady;
            row.superDiamond.fillAmount = member.SuperCharge01;
            row.superDiamond.color = superReady
                ? new Color(1f, 0.86f, 0.28f)
                : new Color(0.62f, 0.24f, 0.92f);
            if (row.superGlow != null)
            {
                if (superReady)
                {
                    float pulse = AccessibilitySettings.ReducedMotionEnabled
                        ? 0.55f
                        : 0.42f + 0.24f * (0.5f + 0.5f * Mathf.Sin(Time.unscaledTime * 4.2f + rowIndex));
                    row.superGlow.color = new Color(1f, 0.82f, 0.24f, pulse);
                }
                else
                {
                    row.superGlow.color = new Color(1f, 0.82f, 0.24f, 0f);
                }
            }

            if (row.respawnOverlay != null)
            {
                row.respawnOverlay.SetActive(isRespawning);
                if (isRespawning && row.respawnText != null)
                    row.respawnText.SetText("{0:0.0}s", Mathf.Max(0f, member.RespawnRemaining));
            }
        }

        /// <summary>
        /// Supplies real, precomputed reward values for the staged end-game
        /// sequence. It is safe to call before or after MatchEnded.
        /// </summary>
        public void ShowMatchRewards(MatchRewardSummary summary)
        {
            matchRewardSummary = summary;
            hasMatchRewardSummary = true;
            rewardLine = null;
            if (matchEnded)
            {
                ApplyBannerSub();
                StartResultSequence();
            }
        }

        /// <summary>Legacy text fallback for external callers.</summary>
        public void ShowRewards(string line)
        {
            rewardLine = line;
            if (!hasMatchRewardSummary && bannerRoot != null && bannerRoot.activeSelf) ApplyBannerSub();
        }

        void ApplyBannerSub()
        {
            bannerSub.text = hasMatchRewardSummary
                ? "ARCANE REWARDS"
                : string.IsNullOrEmpty(rewardLine)
                    ? "READY FOR ANOTHER ROUND?"
                    : rewardLine;
        }

        void OnMatchEnded(TeamId? winner)
        {
            PresentMatchResult(winner);
        }

        /// <summary>
        /// Presents a fully specified post-match result. This is used by the
        /// standalone preview scene as well as future non-match result flows.
        /// </summary>
        public void PresentMatchResult(TeamId? winner, MatchRewardSummary summary)
        {
            matchRewardSummary = summary;
            hasMatchRewardSummary = true;
            rewardLine = null;
            PresentMatchResult(winner);
        }

        /// <summary>Shows the result shell; structured rewards can arrive later.</summary>
        public void PresentMatchResult(TeamId? winner)
        {
            matchEnded = true;
            bannerRoot.SetActive(true);
            ApplyBannerSub();
            HideRespawn();
            if (protectionRoot != null) protectionRoot.SetActive(false);

            TeamId playerTeam = player != null ? player.team : TeamId.Blue;
            if (!winner.HasValue)
            {
                bannerTitle.text = "DRAW";
                bannerTitle.color = Color.white;
                SetBannerIcon(theme != null ? theme.timerIcon : null, Color.white);
            }
            else if (winner.Value == playerTeam)
            {
                bannerTitle.text = "VICTORY!";
                bannerTitle.color = new Color(1f, 0.86f, 0.26f);
                SetBannerIcon(theme != null ? theme.trophyIcon : null, Color.white);
            }
            else
            {
                bannerTitle.text = "DEFEAT";
                bannerTitle.color = new Color(1f, 0.34f, 0.3f);
                SetBannerIcon(theme != null ? theme.swordIcon : null, Color.white);
            }
            ResetResultPresentation();
            if (hasMatchRewardSummary) StartResultSequence();
        }

        void SetBannerIcon(Sprite icon, Color color)
        {
            if (bannerIcon == null) return;
            bannerIcon.sprite = icon;
            bannerIcon.color = color;
            bannerIcon.enabled = icon != null;
        }

        void ResetResultPresentation()
        {
            if (resultRoutine != null)
            {
                StopCoroutine(resultRoutine);
                resultRoutine = null;
            }

            if (resultCardGroup != null) resultCardGroup.alpha = 1f;
            if (resultCard != null) resultCard.transform.localScale = Vector3.one * 0.94f;
            SetRowState(eliminationsRow, eliminationsValue, "0");
            SetRowState(brawlerPointsRow, brawlerPointsValue, "+0");
            SetRowState(coinsRow, coinsValue, "+0");
            if (progressionRow != null)
            {
                progressionRow.alpha = 0f;
                progressionRow.transform.localScale = Vector3.one * 0.94f;
            }
            if (progressionLabel != null) progressionLabel.text = "HERO MASTERY";
            if (progressionValue != null) progressionValue.text = "0 / 0 AP";
            if (progressionFill != null) progressionFill.anchorMax = new Vector2(0f, 1f);
            if (resultButtonsGroup != null) resultButtonsGroup.alpha = 0f;
            ClearResultFx();
            SetResultButtonsInteractable(false);
        }

        void SetRowState(CanvasGroup row, TextMeshProUGUI value, string text)
        {
            if (row != null)
            {
                row.alpha = 0f;
                row.transform.localScale = Vector3.one * 0.94f;
            }
            if (value != null) value.text = text;
        }

        void StartResultSequence()
        {
            if (!hasMatchRewardSummary || !matchEnded || bannerRoot == null || !bannerRoot.activeSelf) return;
            if (resultRoutine != null) StopCoroutine(resultRoutine);
            ResetResultPresentation();
            resultRoutine = StartCoroutine(ResultSequence());
        }

        IEnumerator ResultSequence()
        {
            PlayResultFx();
            yield return ScaleResultCard(0.94f, 1f, 0.22f);
            yield return new WaitForSecondsRealtime(0.16f);
            yield return RevealResultRow(eliminationsRow, eliminationsValue,
                matchRewardSummary.eliminations, string.Empty, 0.32f);
            yield return new WaitForSecondsRealtime(0.08f);
            yield return RevealResultRow(brawlerPointsRow, brawlerPointsValue,
                matchRewardSummary.brawlerPoints, "+", 0.44f);
            yield return new WaitForSecondsRealtime(0.08f);
            yield return RevealResultRow(coinsRow, coinsValue,
                matchRewardSummary.coins, "+", 0.42f);
            yield return new WaitForSecondsRealtime(0.1f);
            yield return RevealProgression();
            yield return FadeInResultButtons();
            resultRoutine = null;
        }

        IEnumerator ScaleResultCard(float from, float to, float duration)
        {
            if (resultCard == null) yield break;
            for (float elapsed = 0f; elapsed < duration; elapsed += Time.unscaledDeltaTime)
            {
                float t = Mathf.SmoothStep(0f, 1f, elapsed / duration);
                resultCard.transform.localScale = Vector3.one * Mathf.Lerp(from, to, t);
                yield return null;
            }
            resultCard.transform.localScale = Vector3.one * to;
        }

        IEnumerator RevealResultRow(CanvasGroup row, TextMeshProUGUI value, int target,
            string prefix, float duration)
        {
            if (row == null || value == null) yield break;
            for (float elapsed = 0f; elapsed < duration; elapsed += Time.unscaledDeltaTime)
            {
                float t = Mathf.SmoothStep(0f, 1f, elapsed / duration);
                row.alpha = t;
                row.transform.localScale = Vector3.one * Mathf.Lerp(0.94f, 1f, t);
                value.text = prefix + Mathf.RoundToInt(Mathf.Lerp(0f, target, t)).ToString("N0");
                yield return null;
            }
            row.alpha = 1f;
            row.transform.localScale = Vector3.one;
            value.text = prefix + target.ToString("N0");
        }

        IEnumerator RevealProgression()
        {
            if (progressionRow == null || progressionFill == null || progressionValue == null) yield break;
            int before = Mathf.Max(0, matchRewardSummary.pointsBefore);
            int after = Mathf.Max(before, matchRewardSummary.pointsAfter);
            int needed = Mathf.Max(1, matchRewardSummary.pointsNeeded);
            if (progressionLabel != null) progressionLabel.text = "HERO MASTERY " + matchRewardSummary.level;

            const float duration = 0.58f;
            for (float elapsed = 0f; elapsed < duration; elapsed += Time.unscaledDeltaTime)
            {
                float t = Mathf.SmoothStep(0f, 1f, elapsed / duration);
                int value = Mathf.RoundToInt(Mathf.Lerp(before, after, t));
                progressionRow.alpha = t;
                progressionRow.transform.localScale = Vector3.one * Mathf.Lerp(0.94f, 1f, t);
                progressionFill.anchorMax = new Vector2(Mathf.Clamp01(value / (float)needed), 1f);
                progressionValue.text = value.ToString("N0") + " / " + needed.ToString("N0") + " AP";
                yield return null;
            }
            progressionRow.alpha = 1f;
            progressionRow.transform.localScale = Vector3.one;
            progressionFill.anchorMax = new Vector2(Mathf.Clamp01(after / (float)needed), 1f);
            progressionValue.text = after.ToString("N0") + " / " + needed.ToString("N0") + " AP";
        }

        IEnumerator FadeInResultButtons()
        {
            if (resultButtonsGroup == null) yield break;
            for (float elapsed = 0f; elapsed < 0.18f; elapsed += Time.unscaledDeltaTime)
            {
                resultButtonsGroup.alpha = Mathf.SmoothStep(0f, 1f, elapsed / 0.18f);
                yield return null;
            }
            resultButtonsGroup.alpha = 1f;
            SetResultButtonsInteractable(true);
        }

        void SetResultButtonsInteractable(bool interactable)
        {
            if (resultButtonsGroup != null)
            {
                resultButtonsGroup.interactable = interactable;
                resultButtonsGroup.blocksRaycasts = interactable;
            }
            if (replayButton != null) replayButton.interactable = interactable;
            if (menuButton != null) menuButton.interactable = interactable;
        }

        void Restart()
        {
            if (replayLoading) return;
            if (!Progress.TrySpendBattleEnergy())
            {
                if (bannerSub != null)
                {
                    bannerSub.text = "NEED " + Progress.BattleEnergyCost + " BATTLE ENERGY / " +
                                     Progress.Energy + " AVAILABLE";
                    bannerSub.color = new Color(1f, 0.4f, 0.3f);
                }
                return;
            }

            replayLoading = true;
            SetResultButtonsInteractable(false);
            var scene = SceneManager.GetActiveScene();
            if (scene.buildIndex >= 0) SceneManager.LoadScene(scene.buildIndex);
            else SceneManager.LoadScene(scene.name);
        }

        void EnsureEventSystem()
        {
            if (FindFirstObjectByType<EventSystem>() != null) return;
            new GameObject("EventSystem", typeof(EventSystem), typeof(InputSystemUIInputModule));
        }

        void Build()
        {
            var canvasGo = new GameObject("HUDCanvas", typeof(RectTransform), typeof(Canvas),
                typeof(CanvasScaler), typeof(GraphicRaycaster));
            canvasGo.transform.SetParent(transform, false);
            var canvas = canvasGo.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 10;

            var scaler = canvasGo.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            // GUI Pro's authored gameplay composition is 2560x1440.
            scaler.referenceResolution = new Vector2(2560f, 1440f);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            scaler.matchWidthOrHeight = 0.5f;

            var safeArea = NewRect("SafeArea", canvasGo.transform);
            StretchRect((RectTransform)safeArea.transform);
            safeArea.AddComponent<BrawlSafeArea>();

            gameplayRoot = NewRect("GameplayRoot", safeArea.transform);
            StretchRect((RectTransform)gameplayRoot.transform);
            Transform gameplay = gameplayRoot.transform;

            // The wizard HUD foundation is a complete demo HUD (map, skill buttons,
            // cooldowns, and controls), not a background skin. Spawning it here
            // duplicates the live mobile controls built below.
            BuildJoystick(gameplay);
            BuildAttackButton(gameplay);
            BuildWardStepControls(gameplay);
            BuildSuperButton(gameplay);
            BuildTopBar(gameplay);
            BuildPlayerStatus(gameplay);
            BuildSquadPanel(gameplay);
            BuildKillFeed(gameplay);
            var minimap = MinimapView.Create(gameplay, theme, 330f);
            if (minimap != null && theme != null && theme.minimapFrame != null)
            {
                var frame = minimap.GetComponent<Image>();
                if (frame != null)
                {
                    frame.sprite = theme.minimapFrame;
                    frame.type = Image.Type.Sliced;
                    frame.color = Color.white;
                }
            }
            BuildAnnouncement(safeArea.transform);
            BuildRespawnOverlay(safeArea.transform);
            BuildProtectionIndicator(safeArea.transform);
            BuildHiddenIndicator(safeArea.transform);
            BuildEndBanner(canvasGo.transform);
        }

        void BuildJoystick(Transform root)
        {
            var zone = NewRect("JoystickZone", root);
            var zoneRt = (RectTransform)zone.transform;
            zoneRt.anchorMin = Vector2.zero;
            zoneRt.anchorMax = new Vector2(0.48f, 0.82f);
            zoneRt.offsetMin = Vector2.zero;
            zoneRt.offsetMax = Vector2.zero;
            var zoneImage = zone.AddComponent<Image>();
            zoneImage.color = new Color(1f, 1f, 1f, 0f);

            var joyBase = NewRect("JoyBase", zone.transform);
            var baseRt = (RectTransform)joyBase.transform;
            baseRt.sizeDelta = new Vector2(304f, 304f);
            var baseImage = joyBase.AddComponent<Image>();
            baseImage.sprite = theme != null && theme.joystickBackground != null
                ? theme.joystickBackground
                : theme != null && theme.buttonRoundDark != null ? theme.buttonRoundDark : GetCircleSprite();
            baseImage.color = theme != null && theme.joystickBackground != null
                ? Color.white
                : new Color(0.025f, 0.08f, 0.15f, 0.78f);
            baseImage.raycastTarget = false;

            var ring = NewRect("Ring", joyBase.transform);
            var ringRt = (RectTransform)ring.transform;
            ringRt.anchorMin = ringRt.anchorMax = new Vector2(0.5f, 0.5f);
            ringRt.sizeDelta = new Vector2(280f, 280f);
            var ringImage = ring.AddComponent<Image>();
            ringImage.sprite = theme != null && theme.buttonRound != null ? theme.buttonRound : GetCircleSprite();
            ringImage.color = new Color(0.18f, 0.72f, 1f, 0.32f);
            ringImage.raycastTarget = false;

            var knob = NewRect("JoyKnob", joyBase.transform);
            var knobRt = (RectTransform)knob.transform;
            knobRt.sizeDelta = new Vector2(124f, 124f);
            var knobImage = knob.AddComponent<Image>();
            knobImage.sprite = theme != null && theme.joystickHandle != null
                ? theme.joystickHandle
                : theme != null && theme.buttonRound != null ? theme.buttonRound : GetCircleSprite();
            knobImage.color = theme != null && theme.joystickHandle != null
                ? Color.white
                : new Color(0.32f, 0.82f, 1f, 0.95f);
            knobImage.raycastTarget = false;

            joyBase.AddComponent<Canvas>();
            Joystick = zone.AddComponent<VirtualJoystick>();
            Joystick.baseRect = baseRt;
            Joystick.knobRect = knobRt;
            Joystick.radius = 122f;
            // VirtualJoystick.Awake ran before its references were wired.
            joyBase.SetActive(false);
        }

        void BuildSquadPanel(Transform root)
        {
            squadRoot = NewRect("SquadPanel", root);
            var rosterRt = (RectTransform)squadRoot.transform;
            rosterRt.anchorMin = rosterRt.anchorMax = new Vector2(0f, 1f);
            rosterRt.pivot = new Vector2(0f, 1f);
            rosterRt.anchoredPosition = new Vector2(34f, -126f);
            rosterRt.sizeDelta = new Vector2(560f, 380f);

            squadHeader = MakeText("Header", squadRoot.transform,
                "SQUAD", 22f,
                new Color(0.84f, 0.94f, 1f), TextAlignmentOptions.MidlineLeft,
                HudTextStyle.Button);
            var headerRt = squadHeader.rectTransform;
            headerRt.anchorMin = headerRt.anchorMax = new Vector2(0f, 1f);
            headerRt.pivot = new Vector2(0f, 1f);
            headerRt.anchoredPosition = Vector2.zero;
            headerRt.sizeDelta = new Vector2(400f, 34f);

            for (int i = 0; i < MaxSquadRows; i++)
                squadRows[i] = BuildSquadRow(squadRoot.transform, i);

            squadRoot.SetActive(false);
        }

        SquadRow BuildSquadRow(Transform root, int index)
        {
            var row = new SquadRow();
            row.root = NewRect("SquadMember" + (index + 1), root);
            var rowRt = (RectTransform)row.root.transform;
            rowRt.anchorMin = rowRt.anchorMax = new Vector2(0f, 1f);
            rowRt.pivot = new Vector2(0f, 1f);
            rowRt.anchoredPosition = new Vector2(0f, -38f - index * 68f);
            rowRt.sizeDelta = new Vector2(560f, 62f);

            row.background = row.root.AddComponent<Image>();
            row.background.sprite = theme != null && theme.resourceCapsule != null
                ? theme.resourceCapsule
                : theme != null && theme.panel != null ? theme.panel : GetWhiteSprite();
            row.background.type = row.background.sprite != null && theme != null
                ? Image.Type.Sliced
                : Image.Type.Simple;
            row.background.color = new Color(0.025f, 0.075f, 0.14f, 0.9f);
            row.background.raycastTarget = false;
            row.group = row.root.AddComponent<CanvasGroup>();
            row.group.interactable = false;
            row.group.blocksRaycasts = false;

            var portraitFrame = NewRect("PortraitFrame", row.root.transform);
            var portraitFrameRt = (RectTransform)portraitFrame.transform;
            portraitFrameRt.anchorMin = portraitFrameRt.anchorMax = new Vector2(0f, 0.5f);
            portraitFrameRt.pivot = new Vector2(0f, 0.5f);
            portraitFrameRt.anchoredPosition = new Vector2(7f, 0f);
            portraitFrameRt.sizeDelta = new Vector2(50f, 50f);
            row.portraitFrame = portraitFrame.AddComponent<Image>();
            row.portraitFrame.sprite = theme != null && theme.profileFrame != null
                ? theme.profileFrame
                : GetCircleSprite();
            row.portraitFrame.color = Color.white;
            row.portraitFrame.raycastTarget = false;

            var portrait = NewRect("Portrait", portraitFrame.transform);
            var portraitRt = (RectTransform)portrait.transform;
            StretchRect(portraitRt);
            portraitRt.offsetMin = new Vector2(4f, 4f);
            portraitRt.offsetMax = new Vector2(-4f, -4f);
            row.portrait = portrait.AddComponent<Image>();
            row.portrait.preserveAspect = true;
            row.portrait.raycastTarget = false;
            row.portrait.enabled = false;

            row.identity = MakeText("Identity", row.root.transform, "ALLY  •  HERO", 19f,
                Color.white, TextAlignmentOptions.MidlineLeft, HudTextStyle.Button);
            var identityRt = row.identity.rectTransform;
            identityRt.anchorMin = identityRt.anchorMax = new Vector2(0f, 1f);
            identityRt.pivot = new Vector2(0f, 1f);
            identityRt.anchoredPosition = new Vector2(64f, -4f);
            identityRt.sizeDelta = new Vector2(420f, 26f);
            row.identity.overflowMode = TextOverflowModes.Ellipsis;

            row.healthFill = BuildSquadMeter(row.root.transform, "HealthMeter",
                new Vector2(64f, -34f), new Vector2(410f, 10f),
                theme != null ? theme.barFillGreen : null, new Color(0.25f, 0.94f, 0.48f));

            // Super diamond: a rotated square that fills bottom-up with
            // SuperCharge01 and glows gold once SuperReady, mirroring the
            // local player's Super button without duplicating its full UI.
            var glowGo = NewRect("SuperGlow", row.root.transform);
            var glowRt = (RectTransform)glowGo.transform;
            glowRt.anchorMin = glowRt.anchorMax = new Vector2(1f, 0.5f);
            glowRt.pivot = new Vector2(0.5f, 0.5f);
            glowRt.anchoredPosition = new Vector2(-15f, 0f);
            glowRt.sizeDelta = new Vector2(46f, 46f);
            glowRt.localRotation = Quaternion.Euler(0f, 0f, 45f);
            row.superGlow = glowGo.AddComponent<Image>();
            row.superGlow.sprite = GetCircleSprite();
            row.superGlow.color = new Color(1f, 0.82f, 0.24f, 0f);
            row.superGlow.raycastTarget = false;

            var diamondGo = NewRect("SuperDiamond", row.root.transform);
            var diamondRt = (RectTransform)diamondGo.transform;
            diamondRt.anchorMin = diamondRt.anchorMax = new Vector2(1f, 0.5f);
            diamondRt.pivot = new Vector2(0.5f, 0.5f);
            diamondRt.anchoredPosition = new Vector2(-15f, 0f);
            diamondRt.sizeDelta = new Vector2(30f, 30f);
            diamondRt.localRotation = Quaternion.Euler(0f, 0f, 45f);
            var diamondBg = diamondGo.AddComponent<Image>();
            diamondBg.sprite = GetWhiteSprite();
            diamondBg.color = new Color(0.02f, 0.05f, 0.1f, 0.92f);
            diamondBg.raycastTarget = false;

            var diamondFillGo = NewRect("Fill", diamondGo.transform);
            StretchRect((RectTransform)diamondFillGo.transform);
            diamondFillGo.transform.localScale = Vector3.one * 0.78f;
            row.superDiamond = diamondFillGo.AddComponent<Image>();
            row.superDiamond.sprite = GetWhiteSprite();
            row.superDiamond.type = Image.Type.Filled;
            row.superDiamond.fillMethod = Image.FillMethod.Vertical;
            row.superDiamond.fillOrigin = (int)Image.OriginVertical.Bottom;
            row.superDiamond.fillAmount = 0f;
            row.superDiamond.color = new Color(0.62f, 0.24f, 0.92f);
            row.superDiamond.raycastTarget = false;

            row.respawnOverlay = NewRect("RespawnOverlay", row.root.transform);
            StretchRect((RectTransform)row.respawnOverlay.transform);
            var respawnBg = row.respawnOverlay.AddComponent<Image>();
            respawnBg.color = new Color(0f, 0f, 0f, 0.5f);
            respawnBg.raycastTarget = false;
            row.respawnText = MakeText("Text", row.respawnOverlay.transform, "4.2s", 20f,
                Color.white, TextAlignmentOptions.Center, HudTextStyle.Button);
            StretchRect(row.respawnText.rectTransform);
            row.respawnOverlay.SetActive(false);

            row.root.SetActive(false);
            return row;
        }

        Image BuildSquadMeter(Transform root, string name, Vector2 position,
            Vector2 size, Sprite fillSprite, Color fillColor)
        {
            var meter = NewRect(name, root);
            var meterRt = (RectTransform)meter.transform;
            meterRt.anchorMin = meterRt.anchorMax = new Vector2(0f, 1f);
            meterRt.pivot = new Vector2(0f, 0.5f);
            meterRt.anchoredPosition = position;
            meterRt.sizeDelta = size;
            var background = meter.AddComponent<Image>();
            background.sprite = theme != null && theme.barBg != null
                ? theme.barBg
                : GetWhiteSprite();
            background.type = background.sprite != null && theme != null
                ? Image.Type.Sliced
                : Image.Type.Simple;
            background.color = new Color(0.015f, 0.035f, 0.06f, 0.96f);
            background.raycastTarget = false;

            var fill = NewRect("Fill", meter.transform);
            var fillRt = (RectTransform)fill.transform;
            StretchRect(fillRt);
            fillRt.offsetMin = new Vector2(2f, 2f);
            fillRt.offsetMax = new Vector2(-2f, -2f);
            var image = fill.AddComponent<Image>();
            image.sprite = fillSprite != null ? fillSprite : GetWhiteSprite();
            image.type = Image.Type.Filled;
            image.fillMethod = Image.FillMethod.Horizontal;
            image.fillOrigin = 0;
            image.fillAmount = 0f;
            image.color = fillColor;
            image.raycastTarget = false;
            return image;
        }

        void BuildKillFeed(Transform root)
        {
            var label = MakeText("KillFeedLabel", root, "KO FEED", 25f,
                new Color(1f, 1f, 1f, 0.95f), TextAlignmentOptions.MidlineLeft, HudTextStyle.Button);
            var labelRt = label.rectTransform;
            labelRt.anchorMin = labelRt.anchorMax = new Vector2(0f, 1f);
            labelRt.pivot = new Vector2(0f, 1f);
            labelRt.anchoredPosition = new Vector2(34f, -520f);
            labelRt.sizeDelta = new Vector2(360f, 34f);

            var feed = NewRect("KillFeed", root);
            var feedRt = (RectTransform)feed.transform;
            feedRt.anchorMin = feedRt.anchorMax = new Vector2(0f, 1f);
            feedRt.pivot = new Vector2(0f, 1f);
            feedRt.anchoredPosition = new Vector2(34f, -554f);
            feedRt.sizeDelta = new Vector2(420f, 220f);
            feed.AddComponent<KillFeed>();
        }

        void BuildAttackButton(Transform root)
        {
            // The cast surface is scoped to the bottom-right quadrant only, so
            // it neither overlaps the left-side joystick nor the minimap/top
            // bar above it. The ATTACK orb below is visual-only; Dash and
            // Super are created later and therefore remain the topmost
            // interactive raycast targets over any part of this surface they cover.
            var zone = NewRect("RightCastSurface", root);
            RightCastSurface = (RectTransform)zone.transform;
            RightCastSurface.anchorMin = new Vector2(0.55f, 0f);
            RightCastSurface.anchorMax = new Vector2(1f, 0.7f);
            RightCastSurface.offsetMin = Vector2.zero;
            RightCastSurface.offsetMax = Vector2.zero;
            var zoneImage = zone.AddComponent<Image>();
            zoneImage.color = new Color(1f, 1f, 1f, 0f);
            attackButton = zone.AddComponent<AttackButtonWidget>();
            attackButton.cameraOrbitEnabled = true;

            BuildActionButton(root, "CastButton", new Vector2(1f, 0f),
                new Vector2(-210f, 210f), new Vector2(250f, 250f),
                theme != null && theme.spellCastIcon != null ? theme.spellCastIcon : theme != null ? theme.swordIcon : null,
                "ATTACK", new Color(0.26f, 0.72f, 1f), true, false, attackButton);
            BuildBasicAttackChargeFeedback(root.Find("CastButton"));
        }

        void BuildBasicAttackChargeFeedback(Transform castButton)
        {
            if (castButton == null) return;

            var row = NewRect("BasicAttackCharges", castButton);
            var rowRt = (RectTransform)row.transform;
            rowRt.anchorMin = rowRt.anchorMax = new Vector2(0.5f, 1f);
            rowRt.pivot = new Vector2(0.5f, 0f);
            rowRt.anchoredPosition = new Vector2(0f, 8f);
            rowRt.sizeDelta = new Vector2(210f, 42f);

            for (int i = 0; i < basicAttackChargePips.Length; i++)
            {
                var pip = NewRect("Charge" + (i + 1), row.transform);
                var pipRt = (RectTransform)pip.transform;
                pipRt.anchorMin = pipRt.anchorMax = new Vector2(0.5f, 0.5f);
                pipRt.anchoredPosition = new Vector2((i - 1) * 58f, 0f);
                pipRt.sizeDelta = new Vector2(46f, 22f);
                var background = pip.AddComponent<Image>();
                background.sprite = theme != null && theme.resourceCapsule != null
                    ? theme.resourceCapsule
                    : GetWhiteSprite();
                background.type = background.sprite != null && theme != null
                    ? Image.Type.Sliced
                    : Image.Type.Simple;
                background.color = new Color(0.02f, 0.06f, 0.11f, 0.94f);
                background.raycastTarget = false;

                var fill = NewRect("Fill", pip.transform);
                var fillRt = (RectTransform)fill.transform;
                StretchRect(fillRt);
                fillRt.offsetMin = new Vector2(3f, 3f);
                fillRt.offsetMax = new Vector2(-3f, -3f);
                Image image = fill.AddComponent<Image>();
                image.sprite = theme != null && theme.barFillBlue != null
                    ? theme.barFillBlue
                    : GetWhiteSprite();
                image.type = Image.Type.Filled;
                image.fillMethod = Image.FillMethod.Horizontal;
                image.fillOrigin = 0;
                image.fillAmount = 1f;
                image.color = new Color(0.24f, 0.82f, 1f);
                image.raycastTarget = false;
                basicAttackChargePips[i] = image;
            }

            basicAttackReloadText = MakeText("ReloadState", castButton,
                "3 / 3  READY", 18f, Color.white, TextAlignmentOptions.Center,
                HudTextStyle.Button);
            var textRt = basicAttackReloadText.rectTransform;
            textRt.anchorMin = textRt.anchorMax = new Vector2(0.5f, 1f);
            textRt.anchoredPosition = new Vector2(0f, 66f);
            textRt.sizeDelta = new Vector2(260f, 30f);
            basicAttackReloadText.raycastTarget = false;
        }

        void UpdateBasicAttackChargeFeedback()
        {
            if (player == null) return;

            int charges = Mathf.Clamp(player.BasicAttackCharges, 0,
                MobileCombatRules.BasicAttackChargeCapacity);
            float progress = player.BasicAttackReloadProgress01;
            for (int i = 0; i < basicAttackChargePips.Length; i++)
            {
                Image pip = basicAttackChargePips[i];
                if (pip == null) continue;
                bool loaded = i < charges;
                bool reloading = player.BasicAttackReloading && i == charges;
                pip.fillAmount = loaded ? 1f : reloading ? progress : 0f;
                pip.color = loaded
                    ? new Color(0.24f, 0.82f, 1f)
                    : new Color(0.42f, 0.58f, 0.68f);
            }

            int reloadTenths = player.BasicAttackReloading
                ? Mathf.CeilToInt(player.BasicAttackReloadSecondsRemaining * 10f)
                : 0;
            if (basicAttackReloadText == null ||
                (charges == lastBasicAttackCharges &&
                 reloadTenths == lastBasicAttackReloadTenths))
                return;

            lastBasicAttackCharges = charges;
            lastBasicAttackReloadTenths = reloadTenths;
            basicAttackReloadText.text = player.BasicAttackReloading
                ? charges + " / " + MobileCombatRules.BasicAttackChargeCapacity +
                  "  RELOAD " + (reloadTenths / 10f).ToString("0.0") + "s"
                : charges + " / " + MobileCombatRules.BasicAttackChargeCapacity + "  READY";
            basicAttackReloadText.color = charges > 0
                ? Color.white
                : new Color(1f, 0.48f, 0.28f);
        }

        void BuildWardStepControls(Transform root)
        {
            wardStepButton = BuildActionButton(root, "WardStepButton", new Vector2(1f, 0f),
                new Vector2(-470f, 150f), new Vector2(170f, 170f),
                theme != null && theme.spellHasteIcon != null ? theme.spellHasteIcon : theme != null ? theme.speedIcon : null,
                "DASH", new Color(0.25f, 0.9f, 0.82f), false);
            wardStepOuter = wardStepButton.GetComponent<Image>();

            var meter = NewRect("StaminaMeter", root);
            var meterRt = (RectTransform)meter.transform;
            meterRt.anchorMin = meterRt.anchorMax = new Vector2(1f, 0f);
            meterRt.pivot = new Vector2(1f, 0.5f);
            meterRt.anchoredPosition = new Vector2(-420f, 58f);
            meterRt.sizeDelta = new Vector2(640f, 58f);

            var label = MakeText("Label", meter.transform, "ENERGY", 22f,
                new Color(1f, 1f, 1f, 0.84f), TextAlignmentOptions.MidlineLeft, HudTextStyle.Button);
            var labelRt = label.rectTransform;
            labelRt.anchorMin = labelRt.anchorMax = new Vector2(0.12f, 0.8f);
            labelRt.sizeDelta = new Vector2(260f, 30f);

            staminaText = MakeText("Value", meter.transform, "60 / 60", 22f,
                new Color(1f, 1f, 1f, 0.9f), TextAlignmentOptions.MidlineRight,
                HudTextStyle.Button);
            var valueRt = staminaText.rectTransform;
            valueRt.anchorMin = valueRt.anchorMax = new Vector2(0.88f, 0.8f);
            valueRt.sizeDelta = new Vector2(320f, 30f);

            if (theme != null && theme.energyIcon != null)
            {
                var icon = NewRect("Icon", meter.transform);
                var iconRt = (RectTransform)icon.transform;
                iconRt.anchorMin = iconRt.anchorMax = new Vector2(0.025f, 0.76f);
                iconRt.sizeDelta = new Vector2(38f, 38f);
                var iconImage = icon.AddComponent<Image>();
                iconImage.sprite = theme.energyIcon;
                iconImage.preserveAspect = true;
                iconImage.raycastTarget = false;
            }

            var barBg = NewRect("BarBg", meter.transform);
            var barRt = (RectTransform)barBg.transform;
            barRt.anchorMin = barRt.anchorMax = new Vector2(0.5f, 0.28f);
            barRt.sizeDelta = new Vector2(640f, 28f);
            var barImage = barBg.AddComponent<Image>();
            barImage.sprite = theme != null ? theme.barBg : GetWhiteSprite();
            barImage.type = barImage.sprite != null && theme != null ? Image.Type.Sliced : Image.Type.Simple;
            barImage.color = new Color(0.025f, 0.06f, 0.12f, 0.9f);
            barImage.raycastTarget = false;

            var fill = NewRect("Fill", barBg.transform);
            var fillRt = (RectTransform)fill.transform;
            StretchRect(fillRt);
            fillRt.offsetMin = new Vector2(4f, 4f);
            fillRt.offsetMax = new Vector2(-4f, -4f);
            fill.AddComponent<Canvas>();
            staminaFill = fill.AddComponent<Image>();
            staminaFill.sprite = theme != null && theme.barFillYellow != null
                ? theme.barFillYellow
                : GetWhiteSprite();
            staminaFill.type = Image.Type.Filled;
            staminaFill.fillMethod = Image.FillMethod.Horizontal;
            staminaFill.fillOrigin = 0;
            staminaFill.color = new Color(1f, 0.88f, 0.24f);
            staminaFill.raycastTarget = false;

            for (int i = 1; i < 3; i++)
            {
                var divider = NewRect("ChargeDivider" + i, barBg.transform);
                var dividerRt = (RectTransform)divider.transform;
                float x = i / 3f;
                dividerRt.anchorMin = dividerRt.anchorMax = new Vector2(x, 0.5f);
                dividerRt.sizeDelta = new Vector2(4f, 24f);
                var dividerImage = divider.AddComponent<Image>();
                dividerImage.color = new Color(0.03f, 0.08f, 0.14f, 0.9f);
                dividerImage.raycastTarget = false;
            }

            wardFeedbackText = MakeText("WardFeedback", root, "RECHARGING", 20f,
                new Color(1f, 0.42f, 0.3f), TextAlignmentOptions.Center, HudTextStyle.Button);
            var feedbackRt = wardFeedbackText.rectTransform;
            feedbackRt.anchorMin = feedbackRt.anchorMax = new Vector2(1f, 0f);
            feedbackRt.anchoredPosition = new Vector2(-470f, 265f);
            feedbackRt.sizeDelta = new Vector2(300f, 36f);
            wardFeedbackText.gameObject.SetActive(false);
        }

        void BuildSuperButton(Transform root)
        {
            superButton = BuildActionButton(root, "RitualButton", new Vector2(1f, 0f),
                new Vector2(-210f, 470f), new Vector2(200f, 200f),
                theme != null && theme.spellUltimateIcon != null ? theme.spellUltimateIcon : theme != null ? theme.energyIcon : null,
                "SUPER", new Color(0.62f, 0.24f, 0.92f), false);
            superButtonOuter = superButton.GetComponent<Image>();
            superChargeOverlay = CreateRadialOverlay(superButton.transform, "ChargeMask");

            superAbilityText = MakeText("AbilityName", superButton.transform, "SUPER", 18f,
                Color.white, TextAlignmentOptions.Center, HudTextStyle.Button);
            var abilityRt = superAbilityText.rectTransform;
            abilityRt.anchorMin = abilityRt.anchorMax = new Vector2(0.5f, 1.16f);
            abilityRt.sizeDelta = new Vector2(290f, 34f);
            superAbilityText.enableAutoSizing = true;
            superAbilityText.fontSizeMin = 13f;
            superAbilityText.fontSizeMax = 18f;
            superAbilityText.enableWordWrapping = false;

            superReadinessText = MakeText("Readiness", superButton.transform, "0%", 17f,
                new Color(0.82f, 0.76f, 0.94f), TextAlignmentOptions.Center, HudTextStyle.Button);
            var readinessRt = superReadinessText.rectTransform;
            readinessRt.anchorMin = readinessRt.anchorMax = new Vector2(0.5f, 0.86f);
            readinessRt.sizeDelta = new Vector2(110f, 28f);

            superFeedbackText = MakeText("FailureFeedback", root, "NO TARGET IN RANGE", 22f,
                new Color(1f, 0.34f, 0.28f), TextAlignmentOptions.Center, HudTextStyle.Button);
            var feedbackRt = superFeedbackText.rectTransform;
            feedbackRt.anchorMin = feedbackRt.anchorMax = new Vector2(1f, 0f);
            feedbackRt.anchoredPosition = new Vector2(-220f, 603f);
            feedbackRt.sizeDelta = new Vector2(420f, 42f);
            superFeedbackText.gameObject.SetActive(false);
        }

        void BuildPlayerStatus(Transform root)
        {
            var status = NewRect("LocalPlayerStatus", root);
            var statusRt = (RectTransform)status.transform;
            statusRt.anchorMin = statusRt.anchorMax = new Vector2(0f, 1f);
            statusRt.pivot = new Vector2(0f, 1f);
            statusRt.anchoredPosition = new Vector2(34f, -28f);
            statusRt.sizeDelta = new Vector2(680f, 90f);

            var background = status.AddComponent<Image>();
            background.sprite = theme != null && theme.resourceCapsule != null
                ? theme.resourceCapsule
                : theme != null && theme.panel != null
                    ? theme.panel
                    : GetWhiteSprite();
            background.type = theme != null ? Image.Type.Sliced : Image.Type.Simple;
            background.color = new Color(0.025f, 0.075f, 0.14f, 0.92f);
            background.raycastTarget = false;

            if (theme != null && theme.hpIcon != null)
            {
                var hpIcon = NewRect("HealthIcon", status.transform);
                var hpIconRt = (RectTransform)hpIcon.transform;
                hpIconRt.anchorMin = hpIconRt.anchorMax = new Vector2(0.045f, 0.5f);
                hpIconRt.sizeDelta = new Vector2(38f, 38f);
                var hpImage = hpIcon.AddComponent<Image>();
                hpImage.sprite = theme.hpIcon;
                hpImage.preserveAspect = true;
                hpImage.raycastTarget = false;
            }

            playerHealthText = MakeText("NumericHealth", status.transform, "-- / -- HP", 25f,
                Color.white, TextAlignmentOptions.Center, HudTextStyle.Display);
            var healthRt = playerHealthText.rectTransform;
            healthRt.anchorMin = new Vector2(0.08f, 0.3f);
            healthRt.anchorMax = new Vector2(0.37f, 1f);
            healthRt.offsetMin = Vector2.zero;
            healthRt.offsetMax = Vector2.zero;

            // A slim Super readout under HP so charge/readiness is visible
            // without looking away toward the bottom-right Super button.
            var superStripBg = NewRect("SuperStripBg", status.transform);
            var superStripBgRt = (RectTransform)superStripBg.transform;
            superStripBgRt.anchorMin = new Vector2(0.08f, 0.08f);
            superStripBgRt.anchorMax = new Vector2(0.37f, 0.2f);
            superStripBgRt.offsetMin = Vector2.zero;
            superStripBgRt.offsetMax = Vector2.zero;
            var superStripBgImage = superStripBg.AddComponent<Image>();
            superStripBgImage.sprite = theme != null && theme.barBg != null ? theme.barBg : GetWhiteSprite();
            superStripBgImage.type = superStripBgImage.sprite != null && theme != null
                ? Image.Type.Sliced
                : Image.Type.Simple;
            superStripBgImage.color = new Color(0.015f, 0.04f, 0.08f, 0.95f);
            superStripBgImage.raycastTarget = false;

            var superStripFill = NewRect("Fill", superStripBg.transform);
            StretchRect((RectTransform)superStripFill.transform);
            playerSuperStrip = superStripFill.AddComponent<Image>();
            playerSuperStrip.sprite = GetWhiteSprite();
            playerSuperStrip.type = Image.Type.Filled;
            playerSuperStrip.fillMethod = Image.FillMethod.Horizontal;
            playerSuperStrip.fillOrigin = 0;
            playerSuperStrip.fillAmount = 0f;
            playerSuperStrip.color = new Color(0.62f, 0.24f, 0.92f);
            playerSuperStrip.raycastTarget = false;

            playerMatchLevelText = MakeText("MatchLevel", status.transform, "LV 1", 24f,
                new Color(1f, 0.88f, 0.28f), TextAlignmentOptions.Center,
                HudTextStyle.Display);
            var matchLevelRt = playerMatchLevelText.rectTransform;
            matchLevelRt.anchorMin = new Vector2(0.37f, 0f);
            matchLevelRt.anchorMax = new Vector2(0.49f, 1f);
            matchLevelRt.offsetMin = Vector2.zero;
            matchLevelRt.offsetMax = Vector2.zero;

            playerMatchExperienceText = MakeText("MatchExperience", status.transform,
                "MATCH XP  --", 16f, new Color(0.76f, 0.94f, 1f),
                TextAlignmentOptions.Center, HudTextStyle.Button);
            var matchExperienceRt = playerMatchExperienceText.rectTransform;
            matchExperienceRt.anchorMin = new Vector2(0.49f, 0.48f);
            matchExperienceRt.anchorMax = new Vector2(0.8f, 1f);
            matchExperienceRt.offsetMin = Vector2.zero;
            matchExperienceRt.offsetMax = Vector2.zero;

            var matchExperienceBar = NewRect("MatchExperienceBar", status.transform);
            var matchExperienceBarRt = (RectTransform)matchExperienceBar.transform;
            matchExperienceBarRt.anchorMin = new Vector2(0.51f, 0.16f);
            matchExperienceBarRt.anchorMax = new Vector2(0.78f, 0.43f);
            matchExperienceBarRt.offsetMin = Vector2.zero;
            matchExperienceBarRt.offsetMax = Vector2.zero;
            var experienceBackground = matchExperienceBar.AddComponent<Image>();
            experienceBackground.sprite = theme != null && theme.barBg != null
                ? theme.barBg
                : GetWhiteSprite();
            experienceBackground.type = experienceBackground.sprite != null && theme != null
                ? Image.Type.Sliced
                : Image.Type.Simple;
            experienceBackground.color = new Color(0.015f, 0.04f, 0.08f, 0.95f);
            experienceBackground.raycastTarget = false;

            var matchExperienceFill = NewRect("Fill", matchExperienceBar.transform);
            var matchExperienceFillRt = (RectTransform)matchExperienceFill.transform;
            StretchRect(matchExperienceFillRt);
            matchExperienceFillRt.offsetMin = new Vector2(3f, 3f);
            matchExperienceFillRt.offsetMax = new Vector2(-3f, -3f);
            playerMatchExperienceFill = matchExperienceFill.AddComponent<Image>();
            playerMatchExperienceFill.sprite = theme != null && theme.barFillBlue != null
                ? theme.barFillBlue
                : GetWhiteSprite();
            playerMatchExperienceFill.type = Image.Type.Filled;
            playerMatchExperienceFill.fillMethod = Image.FillMethod.Horizontal;
            playerMatchExperienceFill.fillOrigin = 0;
            playerMatchExperienceFill.fillAmount = 0f;
            playerMatchExperienceFill.color = new Color(0.28f, 0.84f, 1f);
            playerMatchExperienceFill.raycastTarget = false;

            personalGemRoot = NewRect("PersonalGems", status.transform);
            var gemRt = (RectTransform)personalGemRoot.transform;
            gemRt.anchorMin = new Vector2(0.8f, 0f);
            gemRt.anchorMax = Vector2.one;
            gemRt.offsetMin = Vector2.zero;
            gemRt.offsetMax = Vector2.zero;

            if (theme != null && theme.gemIcon != null)
            {
                var gemIcon = NewRect("GemIcon", personalGemRoot.transform);
                var gemIconRt = (RectTransform)gemIcon.transform;
                gemIconRt.anchorMin = gemIconRt.anchorMax = new Vector2(0.18f, 0.5f);
                gemIconRt.sizeDelta = new Vector2(40f, 40f);
                var gemImage = gemIcon.AddComponent<Image>();
                gemImage.sprite = theme.gemIcon;
                gemImage.preserveAspect = true;
                gemImage.raycastTarget = false;
            }

            personalGemText = MakeText("Count", personalGemRoot.transform, "CARRY 0", 23f,
                Color.white, TextAlignmentOptions.Center, HudTextStyle.Button);
            var gemTextRt = personalGemText.rectTransform;
            gemTextRt.anchorMin = new Vector2(0.28f, 0f);
            gemTextRt.anchorMax = Vector2.one;
            gemTextRt.offsetMin = Vector2.zero;
            gemTextRt.offsetMax = Vector2.zero;
            personalGemRoot.SetActive(false);
        }

        AttackButtonWidget BuildActionButton(Transform root, string name, Vector2 anchor, Vector2 position,
            Vector2 size, Sprite icon, string label, Color accent, bool isAttack,
            bool capturesPointer = true, AttackButtonWidget gestureOwner = null)
        {
            var buttonGo = NewRect(name, root);
            var rt = (RectTransform)buttonGo.transform;
            rt.anchorMin = rt.anchorMax = anchor;
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = position;
            rt.sizeDelta = size;

            var outer = buttonGo.AddComponent<Image>();
            outer.sprite = theme != null && theme.spellOrbFrame != null
                ? theme.spellOrbFrame
                : theme != null && theme.buttonRound != null ? theme.buttonRound : GetCircleSprite();
            outer.color = accent;
            outer.raycastTarget = capturesPointer;

            var aura = NewRect("ArcaneAura", buttonGo.transform);
            var auraRt = (RectTransform)aura.transform;
            auraRt.anchorMin = auraRt.anchorMax = new Vector2(0.5f, 0.5f);
            auraRt.sizeDelta = size * 1.22f;
            var auraImage = aura.AddComponent<Image>();
            auraImage.sprite = theme != null && theme.spellOrbFocus != null
                ? theme.spellOrbFocus
                : theme != null && theme.glow != null ? theme.glow : GetCircleSprite();
            auraImage.color = new Color(accent.r, accent.g, accent.b, 0.28f);
            auraImage.preserveAspect = true;
            auraImage.raycastTarget = false;

            if (capturesPointer)
            {
                var selectable = buttonGo.AddComponent<Button>();
                selectable.transition = Selectable.Transition.ColorTint;
                selectable.navigation = new Navigation { mode = Navigation.Mode.None };
                var colors = selectable.colors;
                colors.normalColor = Color.white;
                colors.highlightedColor = new Color(1f, 1f, 1f, 0.96f);
                colors.pressedColor = new Color(0.72f, 0.72f, 0.72f, 1f);
                colors.disabledColor = new Color(0.58f, 0.58f, 0.58f, 0.7f);
                colors.colorMultiplier = 1f;
                selectable.colors = colors;
            }

            if (isAttack)
            {
                cooldownOverlay = CreateRadialOverlay(buttonGo.transform, "Cooldown");
            }

            var inner = NewRect("Inner", buttonGo.transform);
            var innerRt = (RectTransform)inner.transform;
            innerRt.anchorMin = innerRt.anchorMax = new Vector2(0.5f, 0.52f);
            innerRt.sizeDelta = size * 0.7f;
            var innerImage = inner.AddComponent<Image>();
            innerImage.sprite = theme != null && theme.spellOrbFocus != null
                ? theme.spellOrbFocus
                : theme != null && theme.buttonRoundDark != null ? theme.buttonRoundDark : GetCircleSprite();
            innerImage.color = new Color(0.025f, 0.07f, 0.14f, 0.78f);
            innerImage.raycastTarget = false;

            if (icon != null)
            {
                var iconGo = NewRect("Icon", buttonGo.transform);
                var iconRt = (RectTransform)iconGo.transform;
                iconRt.anchorMin = iconRt.anchorMax = new Vector2(0.5f, 0.58f);
                iconRt.sizeDelta = size * 0.34f;
                var iconImage = iconGo.AddComponent<Image>();
                iconImage.sprite = icon;
                iconImage.preserveAspect = true;
                iconImage.color = Color.white;
                iconImage.raycastTarget = false;
            }

            var buttonLabel = MakeText("Label", buttonGo.transform, label, isAttack ? 28f : 22f,
                Color.white, TextAlignmentOptions.Center, HudTextStyle.Button);
            var labelRt = buttonLabel.rectTransform;
            labelRt.anchorMin = labelRt.anchorMax = new Vector2(0.5f, 0.18f);
            labelRt.sizeDelta = new Vector2(size.x * 0.8f, 38f);

            if (cooldownOverlay != null && isAttack)
                cooldownOverlay.transform.SetAsLastSibling();

            if (!capturesPointer)
            {
                if (gestureOwner != null) gestureOwner.SetPressVisual(buttonGo.transform);
                return null;
            }

            return buttonGo.AddComponent<AttackButtonWidget>();
        }

        Image CreateRadialOverlay(Transform parent, string name)
        {
            var cooldown = NewRect(name, parent);
            StretchRect((RectTransform)cooldown.transform);
            cooldown.transform.SetSiblingIndex(0);
            var overlay = cooldown.AddComponent<Image>();
            overlay.sprite = theme != null && theme.spellCooldown != null
                ? theme.spellCooldown
                : theme != null && theme.buttonRound != null ? theme.buttonRound
                : GetCircleSprite();
            overlay.color = new Color(0.01f, 0.02f, 0.06f, 0.62f);
            overlay.raycastTarget = false;
            overlay.type = Image.Type.Filled;
            overlay.fillMethod = Image.FillMethod.Radial360;
            overlay.fillOrigin = (int)Image.Origin360.Top;
            overlay.fillClockwise = false;
            overlay.fillAmount = 0f;
            return overlay;
        }

        void BuildTopBar(Transform root)
        {
            var timerCard = NewRect("TimerCard", root);
            var timerRt = (RectTransform)timerCard.transform;
            timerRt.anchorMin = timerRt.anchorMax = new Vector2(0.5f, 1f);
            timerRt.anchoredPosition = new Vector2(0f, -58f);
            timerRt.sizeDelta = new Vector2(254f, 84f);
            var timerImage = timerCard.AddComponent<Image>();
            timerImage.sprite = theme != null && theme.matchTimeFrame != null
                ? theme.matchTimeFrame
                : theme != null && theme.labelChip != null ? theme.labelChip : GetWhiteSprite();
            timerImage.type = timerImage.sprite != null && theme != null ? Image.Type.Sliced : Image.Type.Simple;
            timerImage.color = new Color(0.04f, 0.1f, 0.19f, 0.96f);
            timerImage.raycastTarget = false;

            if (theme != null && theme.timerIcon != null)
            {
                var icon = NewRect("Icon", timerCard.transform);
                var iconRt = (RectTransform)icon.transform;
                iconRt.anchorMin = iconRt.anchorMax = new Vector2(0.2f, 0.5f);
                iconRt.sizeDelta = new Vector2(40f, 40f);
                var iconImage = icon.AddComponent<Image>();
                iconImage.sprite = theme.timerIcon;
                iconImage.preserveAspect = true;
                iconImage.raycastTarget = false;
            }

            timerText = MakeText("Timer", timerCard.transform, "2:30", 46f, Color.white,
                TextAlignmentOptions.Center, HudTextStyle.Display);
            var timerTextRt = timerText.rectTransform;
            timerTextRt.anchorMin = timerTextRt.anchorMax = new Vector2(0.59f, 0.5f);
            timerTextRt.sizeDelta = new Vector2(164f, 62f);

            blueScoreText = MakeScoreChip(root, TeamId.Blue, new Vector2(-242f, -58f), out blueGemIcon);
            redScoreText = MakeScoreChip(root, TeamId.Red, new Vector2(242f, -58f), out redGemIcon);

            BuildZoneMeter(root);
        }

        /// <summary>
        /// Dual-fill objective bar centered under the timer: blue fills from
        /// the left edge toward the middle, red from the right edge toward
        /// the middle, both capped at the mode's score/gem/KO target. The
        /// center chip carries Control Zone's state color and per-team
        /// occupant pips; other modes show a neutral chip and the same bar
        /// doubling as their score-progress readout.
        /// </summary>
        void BuildZoneMeter(Transform root)
        {
            // Badge (mode name) and sub-text (objective) are separate, letter-
            // spaced labels rather than one concatenated string, so both read
            // as words instead of mashing into an unreadable debug-counter
            // look (e.g. "CONTROLZONE FIRSTTO90").
            zoneModeLabel = MakeText("ZoneModeLabel", root, "CONTROL ZONE", 20f,
                new Color(1f, 1f, 1f, 0.9f), TextAlignmentOptions.Center, HudTextStyle.Button);
            var modeLabelRt = zoneModeLabel.rectTransform;
            modeLabelRt.anchorMin = modeLabelRt.anchorMax = new Vector2(0.5f, 1f);
            modeLabelRt.anchoredPosition = new Vector2(0f, -126f);
            modeLabelRt.sizeDelta = new Vector2(900f, 26f);
            zoneModeLabel.characterSpacing = 4f;

            zoneObjectiveLabel = MakeText("ZoneObjectiveLabel", root, "FIRST TO 90", 16f,
                new Color(1f, 1f, 1f, 0.6f), TextAlignmentOptions.Center, HudTextStyle.Button);
            var objectiveLabelRt = zoneObjectiveLabel.rectTransform;
            objectiveLabelRt.anchorMin = objectiveLabelRt.anchorMax = new Vector2(0.5f, 1f);
            objectiveLabelRt.anchoredPosition = new Vector2(0f, -150f);
            objectiveLabelRt.sizeDelta = new Vector2(900f, 20f);
            zoneObjectiveLabel.characterSpacing = 2f;

            var meterRoot = NewRect("ZoneMeter", root);
            var meterRt = (RectTransform)meterRoot.transform;
            meterRt.anchorMin = meterRt.anchorMax = new Vector2(0.5f, 1f);
            meterRt.anchoredPosition = new Vector2(0f, -180f);
            meterRt.sizeDelta = new Vector2(900f, 54f);

            var barBg = meterRoot.AddComponent<Image>();
            barBg.sprite = theme != null && theme.barBg != null ? theme.barBg : GetWhiteSprite();
            barBg.type = barBg.sprite != null && theme != null ? Image.Type.Sliced : Image.Type.Simple;
            barBg.color = new Color(0.02f, 0.05f, 0.1f, 0.92f);
            barBg.raycastTarget = false;

            var blueHalf = NewRect("BlueHalf", meterRoot.transform);
            var blueHalfRt = (RectTransform)blueHalf.transform;
            blueHalfRt.anchorMin = Vector2.zero;
            blueHalfRt.anchorMax = new Vector2(0.5f, 1f);
            blueHalfRt.offsetMin = new Vector2(4f, 4f);
            blueHalfRt.offsetMax = new Vector2(-2f, -4f);
            var blueFillGo = NewRect("Fill", blueHalf.transform);
            StretchRect((RectTransform)blueFillGo.transform);
            zoneBarBlueFill = blueFillGo.AddComponent<Image>();
            zoneBarBlueFill.sprite = theme != null && theme.barFillBlue != null ? theme.barFillBlue : GetWhiteSprite();
            zoneBarBlueFill.type = Image.Type.Filled;
            zoneBarBlueFill.fillMethod = Image.FillMethod.Horizontal;
            zoneBarBlueFill.fillOrigin = (int)Image.OriginHorizontal.Left;
            zoneBarBlueFill.fillAmount = 0f;
            zoneBarBlueFill.color = TeamUtil.Color(TeamId.Blue);
            zoneBarBlueFill.raycastTarget = false;

            var redHalf = NewRect("RedHalf", meterRoot.transform);
            var redHalfRt = (RectTransform)redHalf.transform;
            redHalfRt.anchorMin = new Vector2(0.5f, 0f);
            redHalfRt.anchorMax = Vector2.one;
            redHalfRt.offsetMin = new Vector2(2f, 4f);
            redHalfRt.offsetMax = new Vector2(-4f, -4f);
            var redFillGo = NewRect("Fill", redHalf.transform);
            StretchRect((RectTransform)redFillGo.transform);
            zoneBarRedFill = redFillGo.AddComponent<Image>();
            zoneBarRedFill.sprite = theme != null && theme.barFillRed != null ? theme.barFillRed : GetWhiteSprite();
            zoneBarRedFill.type = Image.Type.Filled;
            zoneBarRedFill.fillMethod = Image.FillMethod.Horizontal;
            zoneBarRedFill.fillOrigin = (int)Image.OriginHorizontal.Right;
            zoneBarRedFill.fillAmount = 0f;
            zoneBarRedFill.color = TeamUtil.Color(TeamId.Red);
            zoneBarRedFill.raycastTarget = false;

            var chipGo = NewRect("CenterChip", meterRoot.transform);
            var chipRt = (RectTransform)chipGo.transform;
            chipRt.anchorMin = chipRt.anchorMax = new Vector2(0.5f, 0.5f);
            chipRt.sizeDelta = new Vector2(96f, 54f);
            zoneCenterChip = chipGo.AddComponent<Image>();
            zoneCenterChip.sprite = theme != null && theme.labelChip != null ? theme.labelChip : GetWhiteSprite();
            zoneCenterChip.type = zoneCenterChip.sprite != null && theme != null ? Image.Type.Sliced : Image.Type.Simple;
            zoneCenterChip.color = new Color(0.16f, 0.2f, 0.26f, 0.96f);
            zoneCenterChip.raycastTarget = false;

            for (int i = 0; i < zoneBluePips.Length; i++)
                zoneBluePips[i] = BuildZonePip(chipGo.transform, new Vector2(-34f + i * 13f, 0f));
            for (int i = 0; i < zoneRedPips.Length; i++)
                zoneRedPips[i] = BuildZonePip(chipGo.transform, new Vector2(9f + i * 13f, 0f));
        }

        Image BuildZonePip(Transform root, Vector2 position)
        {
            var pip = NewRect("Pip", root);
            var pipRt = (RectTransform)pip.transform;
            pipRt.anchorMin = pipRt.anchorMax = new Vector2(0.5f, 0.5f);
            pipRt.anchoredPosition = position;
            pipRt.sizeDelta = new Vector2(9f, 9f);
            var image = pip.AddComponent<Image>();
            image.sprite = GetCircleSprite();
            image.raycastTarget = false;
            return image;
        }

        TextMeshProUGUI MakeScoreChip(Transform root, TeamId team, Vector2 position, out GameObject gemIcon)
        {
            var chip = NewRect(team + "Score", root);
            var rt = (RectTransform)chip.transform;
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 1f);
            rt.anchoredPosition = position;
            rt.sizeDelta = new Vector2(176f, 84f);

            var image = chip.AddComponent<Image>();
            image.sprite = theme != null && theme.labelChip != null ? theme.labelChip : GetWhiteSprite();
            image.type = image.sprite != null && theme != null ? Image.Type.Sliced : Image.Type.Simple;
            Color teamColor = TeamUtil.Color(team);
            image.color = new Color(teamColor.r * 0.82f, teamColor.g * 0.82f, teamColor.b * 0.82f, 0.96f);
            image.raycastTarget = false;

            var name = MakeText("Team", chip.transform, TeamUtil.CueLabel(team, TeamId.Blue), 17f,
                new Color(1f, 1f, 1f, 0.82f), TextAlignmentOptions.Center, HudTextStyle.Button);
            var nameRt = name.rectTransform;
            nameRt.anchorMin = nameRt.anchorMax = new Vector2(0.5f, 0.76f);
            nameRt.sizeDelta = new Vector2(126f, 28f);

            var score = MakeText("Score", chip.transform, "0", 42f, Color.white,
                TextAlignmentOptions.Center, HudTextStyle.Display);
            var scoreRt = score.rectTransform;
            scoreRt.anchorMin = scoreRt.anchorMax = new Vector2(0.5f, 0.38f);
            scoreRt.sizeDelta = new Vector2(120f, 56f);

            gemIcon = null;
            if (theme != null && theme.gemIcon != null)
            {
                gemIcon = NewRect("GemIcon", chip.transform);
                var gemRt = (RectTransform)gemIcon.transform;
                gemRt.anchorMin = gemRt.anchorMax = new Vector2(0.18f, 0.45f);
                gemRt.sizeDelta = new Vector2(42f, 42f);
                var gemImage = gemIcon.AddComponent<Image>();
                gemImage.sprite = theme.gemIcon;
                gemImage.preserveAspect = true;
                gemImage.raycastTarget = false;
                gemIcon.SetActive(false);
            }

            return score;
        }

        void BuildAnnouncement(Transform root)
        {
            announcementRoot = NewRect("Announcement", root);
            var rt = (RectTransform)announcementRoot.transform;
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.6f);
            rt.sizeDelta = new Vector2(1120f, 130f);
            var image = announcementRoot.AddComponent<Image>();
            image.sprite = theme != null && theme.ribbon != null ? theme.ribbon : GetWhiteSprite();
            image.type = image.sprite != null && theme != null ? Image.Type.Sliced : Image.Type.Simple;
            image.color = new Color(0.03f, 0.08f, 0.16f, 0.88f);
            image.raycastTarget = false;

            centerText = MakeText("Text", announcementRoot.transform, "", 68f, Color.white,
                TextAlignmentOptions.Center, HudTextStyle.Display);
            StretchRect(centerText.rectTransform);
            centerText.rectTransform.offsetMin = new Vector2(100f, 0f);
            centerText.rectTransform.offsetMax = new Vector2(-100f, 0f);
            announcementRoot.SetActive(false);
        }

        void ShowAnnouncement(string message, Color color)
        {
            if (announcementRoot == null) return;
            announcementRoot.SetActive(true);
            centerText.text = message;
            centerText.color = color;
        }

        void HideAnnouncement()
        {
            if (announcementRoot != null) announcementRoot.SetActive(false);
        }

        void BuildRespawnOverlay(Transform root)
        {
            respawnRoot = NewRect("RespawnOverlay", root);
            var rt = (RectTransform)respawnRoot.transform;
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.35f);
            rt.sizeDelta = new Vector2(670f, 100f);
            var image = respawnRoot.AddComponent<Image>();
            image.sprite = theme != null && theme.panel != null ? theme.panel : GetWhiteSprite();
            image.type = image.sprite != null && theme != null ? Image.Type.Sliced : Image.Type.Simple;
            image.color = new Color(0.03f, 0.08f, 0.15f, 0.95f);
            image.raycastTarget = false;

            if (theme != null && theme.timerIcon != null)
            {
                var icon = NewRect("Icon", respawnRoot.transform);
                var iconRt = (RectTransform)icon.transform;
                iconRt.anchorMin = iconRt.anchorMax = new Vector2(0.11f, 0.5f);
                iconRt.sizeDelta = new Vector2(48f, 48f);
                var iconImage = icon.AddComponent<Image>();
                iconImage.sprite = theme.timerIcon;
                iconImage.preserveAspect = true;
                iconImage.raycastTarget = false;
            }

            respawnText = MakeText("Text", respawnRoot.transform, "", 36f,
                Color.white, TextAlignmentOptions.MidlineLeft, HudTextStyle.Button);
            var textRt = respawnText.rectTransform;
            textRt.anchorMin = new Vector2(0.19f, 0f);
            textRt.anchorMax = new Vector2(0.94f, 1f);
            textRt.offsetMin = Vector2.zero;
            textRt.offsetMax = Vector2.zero;
            respawnRoot.SetActive(false);
        }

        void BuildProtectionIndicator(Transform root)
        {
            protectionRoot = NewRect("SpawnProtection", root);
            var rt = (RectTransform)protectionRoot.transform;
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.26f);
            rt.sizeDelta = new Vector2(430f, 64f);
            var image = protectionRoot.AddComponent<Image>();
            image.sprite = theme != null && theme.labelChip != null
                ? theme.labelChip
                : GetWhiteSprite();
            image.type = image.sprite != null && theme != null
                ? Image.Type.Sliced
                : Image.Type.Simple;
            image.color = new Color(0.18f, 0.68f, 0.94f, 0.9f);
            image.raycastTarget = false;

            protectionText = MakeText("Text", protectionRoot.transform,
                "SPAWN SHIELD 1.8", 29f, Color.white,
                TextAlignmentOptions.Center, HudTextStyle.Button);
            StretchRect(protectionText.rectTransform);
            protectionRoot.SetActive(false);
        }

        void BuildHiddenIndicator(Transform root)
        {
            hiddenChipRoot = NewRect("HiddenChip", root);
            var rt = (RectTransform)hiddenChipRoot.transform;
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.19f);
            rt.sizeDelta = new Vector2(260f, 56f);
            var image = hiddenChipRoot.AddComponent<Image>();
            image.sprite = theme != null && theme.labelChip != null
                ? theme.labelChip
                : GetWhiteSprite();
            image.type = image.sprite != null && theme != null
                ? Image.Type.Sliced
                : Image.Type.Simple;
            image.color = new Color(0.2f, 0.55f, 0.24f, 0.88f);
            image.raycastTarget = false;

            var text = MakeText("Text", hiddenChipRoot.transform,
                "HIDDEN", 27f, Color.white,
                TextAlignmentOptions.Center, HudTextStyle.Button);
            StretchRect(text.rectTransform);
            hiddenChipRoot.SetActive(false);
        }

        void BuildEndBanner(Transform root)
        {
            bannerRoot = NewRect("EndBanner", root);
            StretchRect((RectTransform)bannerRoot.transform);

            var dim = bannerRoot.AddComponent<Image>();
            dim.color = new Color(0.01f, 0.025f, 0.07f, 0.76f);
            dim.raycastTarget = true;

            var fxLayer = NewRect("ResultFx", bannerRoot.transform);
            StretchRect((RectTransform)fxLayer.transform);
            resultFxRoot = fxLayer.transform;

            var card = NewRect("ResultCard", bannerRoot.transform);
            resultCard = card;
            var cardRt = (RectTransform)card.transform;
            cardRt.anchorMin = cardRt.anchorMax = new Vector2(0.5f, 0.51f);
            cardRt.sizeDelta = new Vector2(970f, 850f);
            var cardImage = card.AddComponent<Image>();
            cardImage.sprite = theme != null && theme.card != null ? theme.card :
                theme != null ? theme.panel : GetWhiteSprite();
            cardImage.type = cardImage.sprite != null && theme != null ? Image.Type.Sliced : Image.Type.Simple;
            cardImage.color = new Color(0.035f, 0.12f, 0.24f, 0.98f);
            resultCardGroup = card.AddComponent<CanvasGroup>();

            if (theme != null && theme.cardGlow != null)
            {
                var glow = NewRect("Glow", card.transform);
                StretchRect((RectTransform)glow.transform);
                var glowImage = glow.AddComponent<Image>();
                glowImage.sprite = theme.cardGlow;
                glowImage.type = Image.Type.Sliced;
                glowImage.color = new Color(0.28f, 0.82f, 1f, 0.24f);
                glowImage.raycastTarget = false;
            }

            var resultLabel = MakeText("ResultLabel", card.transform, "MATCH RESULT", 22f,
                new Color(1f, 1f, 1f, 0.72f), TextAlignmentOptions.Center, HudTextStyle.Button);
            var resultLabelRt = resultLabel.rectTransform;
            resultLabelRt.anchorMin = resultLabelRt.anchorMax = new Vector2(0.5f, 0.96f);
            resultLabelRt.sizeDelta = new Vector2(340f, 36f);

            var icon = NewRect("Icon", card.transform);
            var iconRt = (RectTransform)icon.transform;
            iconRt.anchorMin = iconRt.anchorMax = new Vector2(0.5f, 0.85f);
            iconRt.sizeDelta = new Vector2(96f, 96f);
            bannerIcon = icon.AddComponent<Image>();
            bannerIcon.preserveAspect = true;
            bannerIcon.raycastTarget = false;

            bannerTitle = MakeText("Title", card.transform, "VICTORY!", 102f,
                new Color(1f, 0.85f, 0.25f), TextAlignmentOptions.Center, HudTextStyle.Display);
            var titleRt = bannerTitle.rectTransform;
            titleRt.anchorMin = titleRt.anchorMax = new Vector2(0.5f, 0.72f);
            titleRt.sizeDelta = new Vector2(760f, 110f);

            bannerSub = MakeText("Sub", card.transform, "ARCANE REWARDS", 26f,
                new Color(1f, 1f, 1f, 0.86f), TextAlignmentOptions.Center, HudTextStyle.Body);
            var subRt = bannerSub.rectTransform;
            subRt.anchorMin = subRt.anchorMax = new Vector2(0.5f, 0.63f);
            subRt.sizeDelta = new Vector2(660f, 52f);

            eliminationsRow = BuildResultStatRow(card.transform, "KOS",
                theme != null ? theme.swordIcon : null, new Color(1f, 0.42f, 0.3f),
                new Vector2(0.5f, 0.48f), out eliminationsValue);
            brawlerPointsRow = BuildResultStatRow(card.transform, "ARCANE POINTS",
                theme != null ? theme.passiveSkillIcon : null, new Color(0.42f, 0.92f, 1f),
                new Vector2(0.5f, 0.36f), out brawlerPointsValue);
            coinsRow = BuildResultStatRow(card.transform, "COINS EARNED",
                theme != null ? theme.coinIcon : null, new Color(1f, 0.84f, 0.24f),
                new Vector2(0.5f, 0.24f), out coinsValue);
            progressionRow = BuildResultProgressionRow(card.transform);

            var buttons = NewRect("ResultButtons", card.transform);
            var buttonsRt = (RectTransform)buttons.transform;
            buttonsRt.anchorMin = buttonsRt.anchorMax = new Vector2(0.5f, 0.036f);
            buttonsRt.sizeDelta = new Vector2(800f, 68f);
            resultButtonsGroup = buttons.AddComponent<CanvasGroup>();

            replayButton = MakeCommandButton(buttons.transform, "PlayAgain", "PLAY AGAIN",
                Application.CanStreamedLevelBeLoaded("MainMenu") ? new Vector2(0.68f, 0.5f) : new Vector2(0.5f, 0.5f),
                new Vector2(300f, 68f),
                theme != null ? theme.buttonYellow : null);
            replayButton.onClick.AddListener(Restart);

            if (Application.CanStreamedLevelBeLoaded("MainMenu"))
            {
                menuButton = MakeCommandButton(buttons.transform, "MenuButton", "MAIN MENU",
                    new Vector2(0.31f, 0.5f), new Vector2(270f, 64f),
                    theme != null ? theme.buttonBlue : null);
                menuButton.onClick.AddListener(() => SceneManager.LoadScene("MainMenu"));
            }

            ResetResultPresentation();
            bannerRoot.SetActive(false);
        }

        CanvasGroup BuildResultStatRow(Transform root, string label, Sprite icon, Color accent,
            Vector2 anchor, out TextMeshProUGUI value)
        {
            var row = NewRect(label + "Row", root);
            var rt = (RectTransform)row.transform;
            rt.anchorMin = rt.anchorMax = anchor;
            rt.sizeDelta = new Vector2(760f, 72f);
            var background = row.AddComponent<Image>();
            background.sprite = theme != null && theme.labelChip != null ? theme.labelChip : GetWhiteSprite();
            background.type = background.sprite != null && theme != null ? Image.Type.Sliced : Image.Type.Simple;
            background.color = new Color(0.025f, 0.07f, 0.14f, 0.88f);
            background.raycastTarget = false;

            var accentStrip = NewRect("Accent", row.transform);
            var stripRt = (RectTransform)accentStrip.transform;
            stripRt.anchorMin = new Vector2(0f, 0f);
            stripRt.anchorMax = new Vector2(0f, 1f);
            stripRt.pivot = new Vector2(0f, 0.5f);
            stripRt.sizeDelta = new Vector2(12f, 0f);
            var stripImage = accentStrip.AddComponent<Image>();
            stripImage.color = accent;
            stripImage.raycastTarget = false;

            if (icon != null)
            {
                var iconRoot = NewRect("Icon", row.transform);
                var iconRt = (RectTransform)iconRoot.transform;
                iconRt.anchorMin = iconRt.anchorMax = new Vector2(0.08f, 0.5f);
                iconRt.sizeDelta = new Vector2(44f, 44f);
                var image = iconRoot.AddComponent<Image>();
                image.sprite = icon;
                image.preserveAspect = true;
                image.color = Color.white;
                image.raycastTarget = false;
            }

            var name = MakeText("Name", row.transform, label, 25f,
                new Color(1f, 1f, 1f, 0.84f), TextAlignmentOptions.MidlineLeft, HudTextStyle.Button);
            var nameRt = name.rectTransform;
            nameRt.anchorMin = new Vector2(0.15f, 0f);
            nameRt.anchorMax = new Vector2(0.7f, 1f);
            nameRt.offsetMin = Vector2.zero;
            nameRt.offsetMax = Vector2.zero;

            value = MakeText("Value", row.transform, "0", 34f, accent,
                TextAlignmentOptions.MidlineRight, HudTextStyle.Display);
            var valueRt = value.rectTransform;
            valueRt.anchorMin = new Vector2(0.68f, 0f);
            valueRt.anchorMax = new Vector2(0.94f, 1f);
            valueRt.offsetMin = Vector2.zero;
            valueRt.offsetMax = Vector2.zero;

            var group = row.AddComponent<CanvasGroup>();
            group.interactable = false;
            group.blocksRaycasts = false;
            return group;
        }

        CanvasGroup BuildResultProgressionRow(Transform root)
        {
            var row = NewRect("ProgressionRow", root);
            var rt = (RectTransform)row.transform;
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.135f);
            rt.sizeDelta = new Vector2(760f, 88f);
            var background = row.AddComponent<Image>();
            background.sprite = theme != null && theme.panel != null ? theme.panel : GetWhiteSprite();
            background.type = background.sprite != null && theme != null ? Image.Type.Sliced : Image.Type.Simple;
            background.color = new Color(0.025f, 0.07f, 0.14f, 0.94f);
            background.raycastTarget = false;

            progressionLabel = MakeText("Label", row.transform, "HERO MASTERY", 22f,
                new Color(1f, 1f, 1f, 0.85f), TextAlignmentOptions.MidlineLeft, HudTextStyle.Button);
            var labelRt = progressionLabel.rectTransform;
            labelRt.anchorMin = new Vector2(0.05f, 0.55f);
            labelRt.anchorMax = new Vector2(0.55f, 1f);
            labelRt.offsetMin = Vector2.zero;
            labelRt.offsetMax = Vector2.zero;

            progressionValue = MakeText("Value", row.transform, "0 / 0 AP", 20f,
                new Color(1f, 0.9f, 0.3f), TextAlignmentOptions.MidlineRight, HudTextStyle.Button);
            var valueRt = progressionValue.rectTransform;
            valueRt.anchorMin = new Vector2(0.55f, 0.55f);
            valueRt.anchorMax = new Vector2(0.95f, 1f);
            valueRt.offsetMin = Vector2.zero;
            valueRt.offsetMax = Vector2.zero;

            var bar = NewRect("Bar", row.transform);
            var barRt = (RectTransform)bar.transform;
            barRt.anchorMin = new Vector2(0.05f, 0.15f);
            barRt.anchorMax = new Vector2(0.95f, 0.45f);
            barRt.offsetMin = Vector2.zero;
            barRt.offsetMax = Vector2.zero;
            var barImage = bar.AddComponent<Image>();
            barImage.sprite = theme != null && theme.barBg != null ? theme.barBg : GetWhiteSprite();
            barImage.type = barImage.sprite != null && theme != null ? Image.Type.Sliced : Image.Type.Simple;
            barImage.color = new Color(0.01f, 0.03f, 0.08f, 0.95f);
            barImage.raycastTarget = false;

            var fill = NewRect("Fill", bar.transform);
            progressionFill = (RectTransform)fill.transform;
            progressionFill.anchorMin = Vector2.zero;
            progressionFill.anchorMax = new Vector2(0f, 1f);
            progressionFill.offsetMin = new Vector2(3f, 3f);
            progressionFill.offsetMax = new Vector2(-3f, -3f);
            var fillImage = fill.AddComponent<Image>();
            fillImage.sprite = theme != null && theme.barFillBlue != null ? theme.barFillBlue : GetWhiteSprite();
            fillImage.type = fillImage.sprite != null && theme != null ? Image.Type.Sliced : Image.Type.Simple;
            fillImage.color = new Color(0.28f, 0.88f, 1f, 1f);
            fillImage.raycastTarget = false;

            var group = row.AddComponent<CanvasGroup>();
            group.interactable = false;
            group.blocksRaycasts = false;
            return group;
        }

        void PlayResultFx()
        {
            if (resultFxRoot == null || theme == null) return;
            ClearResultFx();
            SpawnResultFx(theme.fxRotateLight, new Vector2(0.5f, 0.53f), new Vector2(610f, 610f), 1f);
            SpawnResultFx(theme.fxSpreadCircle, new Vector2(0.5f, 0.53f), new Vector2(700f, 700f), 1f);
            SpawnResultFx(theme.fxSpreadStar, new Vector2(0.5f, 0.53f), new Vector2(800f, 800f), 1f);
            SpawnResultFx(theme.fxSparkleYellow, new Vector2(0.5f, 0.72f), new Vector2(700f, 420f), 1f);
            SpawnResultFx(theme.fxSparkleBlue, new Vector2(0.5f, 0.32f), new Vector2(700f, 420f), 1f);
        }

        void ClearResultFx()
        {
            if (resultFxRoot == null) return;
            for (int i = resultFxRoot.childCount - 1; i >= 0; i--)
                Destroy(resultFxRoot.GetChild(i).gameObject);
        }

        void SpawnResultFx(GameObject prefab, Vector2 anchor, Vector2 size, float scale)
        {
            if (prefab == null || resultFxRoot == null) return;
            var fx = Instantiate(prefab, resultFxRoot, false);
            fx.name = prefab.name + "_ResultFx";
            var rt = fx.transform as RectTransform;
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
                fx.transform.localPosition = Vector3.zero;
                fx.transform.localScale = Vector3.one * scale;
            }

            foreach (var canvas in fx.GetComponentsInChildren<Canvas>(true))
            {
                canvas.overrideSorting = false;
                canvas.sortingOrder = 0;
            }
            foreach (var graphic in fx.GetComponentsInChildren<Graphic>(true))
                graphic.raycastTarget = false;
            foreach (var text in fx.GetComponentsInChildren<TMP_Text>(true))
                text.enabled = false;
            foreach (var button in fx.GetComponentsInChildren<Button>(true))
                button.interactable = false;
            foreach (var particles in fx.GetComponentsInChildren<ParticleSystem>(true))
                particles.Play(true);
        }

        Button MakeCommandButton(Transform root, string name, string label, Vector2 anchor,
            Vector2 size, Sprite sprite)
        {
            var go = NewRect(name, root);
            var rt = (RectTransform)go.transform;
            rt.anchorMin = rt.anchorMax = anchor;
            rt.sizeDelta = size;
            var image = go.AddComponent<Image>();
            image.sprite = sprite != null ? sprite : GetWhiteSprite();
            image.type = sprite != null ? Image.Type.Sliced : Image.Type.Simple;
            image.color = sprite != null ? Color.white : new Color(0.18f, 0.52f, 0.93f, 1f);
            var button = go.AddComponent<Button>();
            button.navigation = new Navigation { mode = Navigation.Mode.None };

            var text = MakeText("Label", go.transform, label, 31f, Color.white,
                TextAlignmentOptions.Center, HudTextStyle.Button);
            StretchRect(text.rectTransform);
            return button;
        }

        TextMeshProUGUI MakeText(string name, Transform parent, string content, float size,
            Color color, TextAlignmentOptions alignment, HudTextStyle style)
        {
            var go = NewRect(name, parent);
            var text = go.AddComponent<TextMeshProUGUI>();
            TMP_FontAsset font = style == HudTextStyle.Display
                ? theme != null ? theme.headingFont : null
                : style == HudTextStyle.Button
                    ? theme != null ? theme.buttonFont : null
                    : theme != null ? theme.bodyFont : null;
            if (font == null) font = TMP_Settings.defaultFontAsset;
            if (font != null) text.font = font;
            text.text = content;
            text.fontSize = size;
            text.fontStyle = FontStyles.Normal;
            text.color = color;
            text.alignment = alignment;
            text.enableWordWrapping = false;
            text.overflowMode = TextOverflowModes.Overflow;
            text.margin = new Vector4(8f, 3f, 8f, 3f);
            text.raycastTarget = false;

            if (style != HudTextStyle.Body)
            {
                var shadow = go.AddComponent<Shadow>();
                shadow.effectColor = new Color(0f, 0f, 0f, 0.72f);
                shadow.effectDistance = style == HudTextStyle.Display
                    ? new Vector2(3f, -3f)
                    : new Vector2(1.5f, -1.5f);
            }
            return text;
        }

        static GameObject NewRect(string name, Transform parent)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            return go;
        }

        static void StretchRect(RectTransform rect)
        {
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
        }

        static Sprite GetWhiteSprite()
        {
            if (whiteSprite != null) return whiteSprite;
            var texture = Texture2D.whiteTexture;
            whiteSprite = Sprite.Create(texture, new Rect(0f, 0f, texture.width, texture.height),
                new Vector2(0.5f, 0.5f), 100f);
            return whiteSprite;
        }

        static Sprite GetCircleSprite()
        {
            if (circleSprite != null) return circleSprite;
            const int size = 128;
            var texture = new Texture2D(size, size, TextureFormat.RGBA32, false);
            texture.name = "GeneratedCircle";
            float radius = size * 0.5f - 1f;
            Vector2 center = new Vector2(size * 0.5f, size * 0.5f);
            var pixels = new Color[size * size];
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float distance = Vector2.Distance(new Vector2(x + 0.5f, y + 0.5f), center);
                    pixels[y * size + x] = new Color(1f, 1f, 1f, Mathf.Clamp01(radius - distance + 0.5f));
                }
            }
            texture.SetPixels(pixels);
            texture.Apply();
            circleSprite = Sprite.Create(texture, new Rect(0f, 0f, size, size),
                new Vector2(0.5f, 0.5f), 100f);
            return circleSprite;
        }
    }

    /// <summary>Applies the current device safe area to the HUD's content root.</summary>
    [RequireComponent(typeof(RectTransform))]
    public sealed class BrawlSafeArea : MonoBehaviour
    {
        RectTransform rectTransform;
        Rect lastSafeArea;
        Vector2Int lastScreenSize;

        void OnEnable()
        {
            rectTransform = GetComponent<RectTransform>();
            Apply();
        }

        void Update()
        {
            Rect safeArea = Screen.safeArea;
            var screenSize = new Vector2Int(Screen.width, Screen.height);
            if (safeArea != lastSafeArea || screenSize != lastScreenSize)
                Apply();
        }

        void Apply()
        {
            if (rectTransform == null || Screen.width <= 0 || Screen.height <= 0) return;
            Rect safeArea = Screen.safeArea;
            lastSafeArea = safeArea;
            lastScreenSize = new Vector2Int(Screen.width, Screen.height);
            rectTransform.anchorMin = new Vector2(
                safeArea.xMin / Screen.width,
                safeArea.yMin / Screen.height);
            rectTransform.anchorMax = new Vector2(
                safeArea.xMax / Screen.width,
                safeArea.yMax / Screen.height);
            rectTransform.offsetMin = Vector2.zero;
            rectTransform.offsetMax = Vector2.zero;
        }
    }

    /// <summary>
    /// Pointer-state widget for tap, drag-to-aim, and an explicitly separate
    /// camera orbit. Primary pointer gestures cast; right/middle mouse drags
    /// orbit, while a second primary touch cancels the pending cast and takes
    /// over as the orbit pointer.
    /// </summary>
    public class AttackButtonWidget : MonoBehaviour, IPointerDownHandler, IDragHandler, IPointerUpHandler
    {
        const int NoPointer = int.MinValue;

        int activePointerId = NoPointer;
        PointerEventData.InputButton activePointerButton = PointerEventData.InputButton.Left;
        int orbitPointerId = NoPointer;
        PointerEventData.InputButton orbitPointerButton = PointerEventData.InputButton.Left;
        bool pressed;
        bool released;
        bool cancelled;
        Vector2 pressPosition;
        Vector2 pointerPosition;
        Vector2 releasedDrag;
        float pressedAt;
        Transform pressVisual;
        Vector3 restScale;
        BrawlCamera cameraController;

        [Tooltip("Allow explicit mouse orbit and second-touch orbit takeover on this surface.")]
        public bool cameraOrbitEnabled;
        public bool Held => activePointerId != NoPointer;
        public bool OrbitHeld => orbitPointerId != NoPointer;
        public float HeldDuration => Held ? Time.unscaledTime - pressedAt : 0f;

        BrawlCamera CameraController
        {
            get
            {
                if (cameraController == null)
                {
                    Camera main = Camera.main;
                    if (main != null) cameraController = main.GetComponent<BrawlCamera>();
                }
                return cameraController;
            }
        }

        void Awake()
        {
            SetPressVisual(transform);
        }

        void OnDisable()
        {
            CancelPointerState();
        }

        void OnApplicationFocus(bool hasFocus)
        {
            if (!hasFocus) CancelPointerState();
        }

        void OnApplicationPause(bool paused)
        {
            if (paused) CancelPointerState();
        }

        void CancelPointerState()
        {
            activePointerId = NoPointer;
            activePointerButton = PointerEventData.InputButton.Left;
            orbitPointerId = NoPointer;
            orbitPointerButton = PointerEventData.InputButton.Left;
            pressed = false;
            released = false;
            cancelled = false;
            pressPosition = Vector2.zero;
            pointerPosition = Vector2.zero;
            releasedDrag = Vector2.zero;
            SetVisualPressed(false);
        }

        /// <summary>Uses a visual child/sibling for press feedback without enlarging its raycast area.</summary>
        public void SetPressVisual(Transform visual)
        {
            if (pressVisual != null) pressVisual.localScale = restScale;
            pressVisual = visual != null ? visual : transform;
            restScale = pressVisual.localScale;
            SetVisualPressed(Held);
        }

        void SetVisualPressed(bool value)
        {
            if (pressVisual != null)
                pressVisual.localScale = restScale * (value ? 0.94f : 1f);
        }

        void CancelActiveAttack(bool notify)
        {
            bool hadAttack = activePointerId != NoPointer || pressed;
            activePointerId = NoPointer;
            activePointerButton = PointerEventData.InputButton.Left;
            pressed = false;
            pressPosition = Vector2.zero;
            pointerPosition = Vector2.zero;
            SetVisualPressed(false);
            if (notify && hadAttack) cancelled = true;
        }

        void BeginOrbit(PointerEventData eventData)
        {
            orbitPointerId = eventData.pointerId;
            orbitPointerButton = eventData.button;
        }

        static bool IsExplicitCameraButton(PointerEventData.InputButton button)
        {
            return button == PointerEventData.InputButton.Right ||
                   button == PointerEventData.InputButton.Middle;
        }

        bool IsOrbitPointer(PointerEventData eventData)
        {
            return eventData.pointerId == orbitPointerId &&
                   eventData.button == orbitPointerButton;
        }

        public bool ConsumePressed()
        {
            bool value = pressed;
            pressed = false;
            return value;
        }

        public bool ConsumeCancelled()
        {
            bool value = cancelled;
            cancelled = false;
            return value;
        }

        /// <summary>Returns the live drag delta in screen pixels while this control is held.</summary>
        public bool TryGetDrag(out Vector2 screenDrag)
        {
            screenDrag = pointerPosition - pressPosition;
            return Held;
        }

        /// <summary>Consumes a pointer-up gesture and its final screen-space drag delta.</summary>
        public bool ConsumeReleased(out Vector2 screenDrag)
        {
            screenDrag = releasedDrag;
            bool value = released;
            released = false;
            releasedDrag = Vector2.zero;
            return value;
        }

        public void OnPointerDown(PointerEventData eventData)
        {
            if (cameraOrbitEnabled && IsExplicitCameraButton(eventData.button))
            {
                if (OrbitHeld) return;
                if (Held || pressed) CancelActiveAttack(true);
                BeginOrbit(eventData);
                return;
            }

            // Non-cast widgets must not turn a right/middle click over their
            // artwork into a Dash or Super activation.
            if (eventData.button != PointerEventData.InputButton.Left) return;

            if (cameraOrbitEnabled)
            {
                if (OrbitHeld) return;

                // A second touch changes the gesture mode instead of allowing one
                // finger to aim while the same motion also turns the camera.
                if (Held)
                {
                    if (eventData.pointerId != activePointerId)
                    {
                        CancelActiveAttack(true);
                        BeginOrbit(eventData);
                    }
                    return;
                }
            }
            else if (Held) return;

            // Preserve an unconsumed completion rather than allowing a second
            // very-fast tap to erase the first spell gesture.
            if (released || cancelled) return;
            activePointerId = eventData.pointerId;
            activePointerButton = eventData.button;
            pressPosition = eventData.position;
            pointerPosition = eventData.position;
            pressedAt = Time.unscaledTime;
            pressed = true;
            SetVisualPressed(true);
        }

        public void OnDrag(PointerEventData eventData)
        {
            if (IsOrbitPointer(eventData))
            {
                BrawlCamera orbitCamera = CameraController;
                if (orbitCamera != null) orbitCamera.AddOrbit(eventData.delta.x, eventData.delta.y);
                return;
            }

            if (eventData.pointerId == activePointerId &&
                eventData.button == activePointerButton)
                pointerPosition = eventData.position;
        }

        public void OnPointerUp(PointerEventData eventData)
        {
            if (IsOrbitPointer(eventData))
            {
                orbitPointerId = NoPointer;
                orbitPointerButton = PointerEventData.InputButton.Left;
                return;
            }

            if (eventData.pointerId != activePointerId ||
                eventData.button != activePointerButton) return;
            pointerPosition = eventData.position;
            releasedDrag = pointerPosition - pressPosition;
            released = true;
            activePointerId = NoPointer;
            activePointerButton = PointerEventData.InputButton.Left;
            SetVisualPressed(false);
        }
    }
}
