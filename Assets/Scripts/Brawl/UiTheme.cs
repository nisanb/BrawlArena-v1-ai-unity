using TMPro;
using UnityEngine;

namespace BrawlArena
{
    /// <summary>
    /// Scene-wired visual kit sourced from the Layer Lab GUI Pro pack by the
    /// editor scene builders. Runtime UI code reads it through
    /// <see cref="Instance"/> and falls back to the old generated look when a
    /// slot (or the whole theme) is missing, so scenes without the pack keep
    /// working.
    /// </summary>
    public class UiTheme : MonoBehaviour
    {
        public static UiTheme Instance { get; private set; }

        [Header("Fonts")]
        public TMP_FontAsset headingFont;
        public TMP_FontAsset buttonFont;
        public TMP_FontAsset bodyFont;

        [Header("Panels & frames")]
        public Sprite panel;
        public Sprite frame;
        public Sprite topBar;
        public Sprite bottomBar;
        public Sprite ribbon;
        public Sprite labelChip;
        public Sprite card;
        public Sprite cardFocus;
        public Sprite cardGlow;
        public Sprite cardGreen;
        public Sprite cardYellow;
        public Sprite stageCard;
        public Sprite stageFocus;
        public Sprite profileFrame;
        public Sprite levelFrame;
        public Sprite glow;
        public Sprite lobbyBackgroundLeft;
        public Sprite lobbyBackgroundMiddle;
        public Sprite lobbyBackgroundRight;
        public Sprite lobbyBottomGlowGreen;
        public Sprite lobbyBottomGlowBlue;
        public Sprite lobbyScreenGlow;
        public Material additiveMaterial;

        [Header("Wizard UI foundations")]
        public GameObject wizardLobbyFoundation;
        public GameObject wizardSelectFoundation;
        public GameObject wizardHudFoundation;

        [Header("Buttons")]
        public Sprite buttonYellow;
        public Sprite buttonGreen;
        public Sprite buttonBlue;
        public Sprite buttonRed;
        public Sprite buttonPurple;
        public Sprite buttonNavy;
        public Sprite buttonRound;
        public Sprite buttonRoundDark;
        public Sprite buttonSquareNavy;
        public Sprite buttonSquareBlue;
        public Sprite buttonSquareSky;
        public Sprite menuTopButton;
        public Sprite menuTopButtonFocus;
        public Sprite arrowLeft;
        public Sprite arrowRight;
        public Sprite homeIcon;
        public Sprite pauseIcon;
        public Sprite playIcon;
        public Sprite settingsIcon;
        public Sprite inboxIcon;
        public Sprite newsIcon;
        public Sprite clanIcon;
        public Sprite chatIcon;
        public Sprite lockIcon;
        public Sprite addIcon;
        public Sprite alertDot;

        [Header("Bars")]
        public Sprite barBg;
        public Sprite barFillYellow;
        public Sprite barFillGreen;
        public Sprite barFillBlue;
        public Sprite barFillRed;

        [Header("Icons")]
        public Sprite gemIcon;
        public Sprite trophyIcon;
        public Sprite swordIcon;
        public Sprite timerIcon;
        public Sprite coinIcon;
        public Sprite energyIcon;
        public Sprite mapIcon;
        public Sprite shopIcon;
        public Sprite inventoryIcon;
        public Sprite cardsIcon;
        public Sprite rewardsIcon;
        public Sprite rankingIcon;
        public Sprite missionIcon;
        public Sprite friendsIcon;
        public Sprite hpIcon;
        public Sprite damageIcon;
        public Sprite speedIcon;
        public Sprite starOnIcon;
        public Sprite starOffIcon;
        public Sprite resourceCapsule;
        public Sprite resourcePlusButton;
        public Sprite resourcePlusIcon;

        [Header("Wizard spell UI")]
        public Sprite schoolArcaneIcon;
        public Sprite schoolFireIcon;
        public Sprite schoolFrostIcon;
        public Sprite schoolStormIcon;
        public Sprite schoolEarthIcon;
        public Sprite schoolNatureIcon;
        public Sprite schoolVoidIcon;
        public Sprite archerIcon;
        public Sprite spellCastIcon;
        public Sprite spellHasteIcon;
        public Sprite spellUltimateIcon;
        public Sprite spellOrbFrame;
        public Sprite spellOrbFocus;
        public Sprite spellCooldown;
        public Sprite joystickBackground;
        public Sprite joystickHandle;
        public Sprite minimapFrame;
        public Sprite matchTimeFrame;

        [Header("Celebration FX")]
        public GameObject levelUpPanelPrefab;
        public GameObject rewardPopupPrefab;
        public GameObject fxSpreadStar;
        public GameObject fxSpreadCircle;
        public GameObject fxRotateLight;
        public GameObject fxSparkleYellow;
        public GameObject fxSparkleBlue;
        public Sprite giftIcon;
        public Sprite passiveSkillIcon;
        public Sprite levelFrameHighlight;

        [Header("Minimap")]
        public Sprite minimapBackground;

        public Sprite SchoolIcon(string schoolId, int fallbackIndex)
        {
            string id = (schoolId ?? string.Empty).ToLowerInvariant();
            if (id.Contains("archer") || id.Contains("thorn") || id.Contains("ranger") ||
                id.Contains("bow") || id.Contains("marksman"))
                return archerIcon != null ? archerIcon : swordIcon;
            if (id.Contains("arcane") || id.Contains("aether") || id.Contains("astral")) return schoolArcaneIcon;
            if (id.Contains("fire") || id.Contains("pyro") || id.Contains("ember")) return schoolFireIcon;
            if (id.Contains("frost") || id.Contains("cryo") || id.Contains("ice")) return schoolFrostIcon;
            if (id.Contains("storm") || id.Contains("lightning") || id.Contains("thunder")) return schoolStormIcon;
            if (id.Contains("earth") || id.Contains("geo") || id.Contains("stone")) return schoolEarthIcon;
            if (id.Contains("nature") || id.Contains("life") || id.Contains("verdant")) return schoolNatureIcon;
            if (id.Contains("poison") || id.Contains("toxic") || id.Contains("plague"))
                return schoolNatureIcon != null ? schoolNatureIcon : schoolVoidIcon;
            if (id.Contains("void") || id.Contains("shadow")) return schoolVoidIcon;

            switch (Mathf.Abs(fallbackIndex) % 6)
            {
                case 0: return schoolArcaneIcon;
                case 1: return schoolFireIcon;
                case 2: return schoolFrostIcon;
                case 3: return schoolStormIcon;
                case 4: return schoolEarthIcon;
                default: return schoolVoidIcon;
            }
        }

        void Awake()
        {
            Instance = this;
        }

        void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }
    }
}
