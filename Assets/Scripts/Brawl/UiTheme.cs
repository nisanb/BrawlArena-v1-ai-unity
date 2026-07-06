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
        public Sprite ribbon;
        public Sprite labelChip;
        public Sprite card;
        public Sprite cardFocus;
        public Sprite glow;
        public Material additiveMaterial;

        [Header("Buttons")]
        public Sprite buttonYellow;
        public Sprite buttonGreen;
        public Sprite buttonBlue;
        public Sprite buttonRed;
        public Sprite buttonRound;
        public Sprite arrowLeft;
        public Sprite arrowRight;

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
        public Sprite resourceCapsule;

        [Header("Minimap")]
        public Sprite minimapBackground;

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
