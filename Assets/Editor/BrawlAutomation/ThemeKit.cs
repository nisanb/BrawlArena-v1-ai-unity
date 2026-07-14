using DamageNumbersPro;
using TMPro;
using UnityEditor;
using UnityEngine;

namespace BrawlArena.EditorAutomation
{
    /// <summary>
    /// Shared wiring of the Layer Lab GUI Pro sprites/fonts into a scene
    /// UiTheme object, plus creation of the project-owned DamageNumbersPro
    /// prefab variants. Used by both Arena and MainMenu scene builders.
    /// </summary>
    public static class ThemeKit
    {
        const string Gui = "Assets/Layer Lab/GUI Pro-CasualGame/ResourcesData/";
        const string Dnp = "Assets/DamageNumbersPro/Demo/Prefabs/3D/";
        const string DnpOut = "Assets/Prefabs/DNP/";
        const string WizardUi = "Assets/Prefabs/UI/Wizard/";

        /// <summary>Load a sprite tolerating the pack's mixed .png/.Png casing.</summary>
        static Sprite LoadSprite(string pathNoExt)
        {
            var s = AssetDatabase.LoadAssetAtPath<Sprite>(pathNoExt + ".png");
            if (s == null) s = AssetDatabase.LoadAssetAtPath<Sprite>(pathNoExt + ".Png");
            if (s == null) Debug.LogWarning("[ThemeKit] missing sprite: " + pathNoExt);
            return s;
        }

        static TMP_FontAsset LoadFont(string file)
        {
            var f = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(Gui + "Fonts/" + file);
            if (f == null) Debug.LogWarning("[ThemeKit] missing font: " + file);
            return f;
        }

        static GameObject LoadPrefab(string path)
        {
            var p = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (p == null) Debug.LogWarning("[ThemeKit] missing prefab: " + path);
            return p;
        }

        public static UiTheme CreateThemeObject()
        {
            var go = new GameObject("UiTheme");
            var t = go.AddComponent<UiTheme>();

            t.headingFont = LoadFont("LilitaOne-Regular Outline 120 SDF.asset");
            t.buttonFont = LoadFont("LilitaOne-Regular Outline 54 SDF.asset");
            t.bodyFont = LoadFont("LilitaOne-Regular SDF.asset");

            string c = Gui + "Sprites/Components/";
            t.panel = LoadSprite(c + "Frame/PanelFrame01_Round_Bg");
            t.frame = LoadSprite(c + "Frame/BasicFrame_Round12");
            t.topBar = LoadSprite(c + "Frame/PanelFrame03_Topbar");
            t.bottomBar = LoadSprite(c + "Frame/PanelFrame06_Bottom");
            t.ribbon = LoadSprite(c + "Label/Title_Ribbon_Bg_Blue");
            t.labelChip = LoadSprite(c + "Label/Label_Round01_White");
            t.card = LoadSprite(c + "Frame/CardFrame06_Bg_Blue");
            t.cardFocus = LoadSprite(c + "Frame/CardFrame04_Focus");
            t.cardGlow = LoadSprite(c + "Frame/CardFrame06_Glow");
            t.cardGreen = LoadSprite(c + "Frame/CardFrame05_Bg_Green");
            t.cardYellow = LoadSprite(c + "Frame/CardFrame06_Bg_Yellow");
            t.stageCard = LoadSprite(c + "Frame/StageFrame_Single_Bg_n_Blue");
            t.stageFocus = LoadSprite(c + "Frame/StageFrame_Single_Focus");
            t.profileFrame = LoadSprite(c + "Frame/ProfileFrame01_Inner_Blue");
            t.levelFrame = LoadSprite(c + "IconMisc/Icon_ImageIcon_LevelFrame1");
            t.glow = LoadSprite(c + "Popup/Common_Popup_Glow");
            string bg = Gui + "Sprites/Demo/Demo_Background/";
            t.lobbyBackgroundLeft = LoadSprite(bg + "Background_08_1Left");
            t.lobbyBackgroundMiddle = LoadSprite(bg + "Background_08_2Middle");
            t.lobbyBackgroundRight = LoadSprite(bg + "Background_08_3Right");
            t.lobbyBottomGlowGreen = LoadSprite(bg + "Background_08_4BottomGlow_Green");
            t.lobbyBottomGlowBlue = LoadSprite(bg + "Background_08_5BottomGlow_Blue");
            t.lobbyScreenGlow = LoadSprite(bg + "Background_ScreenGlow");
            t.additiveMaterial = AssetDatabase.LoadAssetAtPath<Material>(
                Gui + "Shader & Materials/UIAdditive.mat");
            t.wizardLobbyFoundation = LoadPrefab(WizardUi + "WizardLobbyFoundation.prefab");
            t.wizardSelectFoundation = LoadPrefab(WizardUi + "WizardSelectFoundation.prefab");
            t.wizardHudFoundation = LoadPrefab(WizardUi + "WizardHudFoundation.prefab");

            t.buttonYellow = LoadSprite(c + "Button/Button01_225_Yellow");
            t.buttonGreen = LoadSprite(c + "Button/Button01_175_Green");
            t.buttonBlue = LoadSprite(c + "Button/Button01_175_Blue");
            t.buttonRed = LoadSprite(c + "Button/Button01_175_Red");
            t.buttonPurple = LoadSprite(c + "Button/Button01_175_Purple");
            t.buttonNavy = LoadSprite(c + "Button/Button_Square03_Navy");
            t.buttonRound = LoadSprite(c + "Button/Button_Circle147_White");
            t.buttonRoundDark = LoadSprite(c + "Button/Button_Circle128_Dark");
            t.buttonSquareNavy = LoadSprite(c + "Button/Button_Square03_Navy");
            t.buttonSquareBlue = LoadSprite(c + "Button/Button_Square05_Blue");
            t.buttonSquareSky = LoadSprite(c + "Button/Button_Square01_Sky");
            t.menuTopButton = LoadSprite(c + "Button/Menu_TopBtn");
            t.menuTopButtonFocus = LoadSprite(c + "Button/Menu_TopBtn_Focus");
            t.arrowLeft = LoadSprite(c + "IconMisc/Icon_PictoIcon_Prev01");
            t.arrowRight = LoadSprite(c + "IconMisc/Icon_PictoIcon_Next01");
            t.homeIcon = LoadSprite(c + "IconMisc/Icon_PictoIcon_Home");
            t.pauseIcon = LoadSprite(c + "IconMisc/Icon_PictoIcon_Pause");
            t.playIcon = LoadSprite(c + "IconMisc/Icon_PictoIcon_Play");
            t.settingsIcon = LoadSprite(c + "IconMisc/Icon_PictoIcon_Setting");
            t.inboxIcon = LoadSprite(c + "IconMisc/Icon_MenuIcon04_Inbox");
            t.newsIcon = LoadSprite(c + "IconMisc/Icon_MenuIcon04_News-");
            t.clanIcon = LoadSprite(c + "IconMisc/Icon_MenuIcon02_Clan_n");
            t.chatIcon = LoadSprite(c + "IconMisc/Icon_ImageIcon_Chat");
            t.lockIcon = LoadSprite(c + "IconMisc/Icon_PictoIcon_Lock_s");
            t.addIcon = LoadSprite(c + "IconMisc/Icon_PictoIcon_Add01");
            t.alertDot = LoadSprite(c + "UI_Etc/Alert_Dot_Bg");

            t.barBg = LoadSprite(c + "Slider/Slider_Basic01_Bg");
            t.barFillYellow = LoadSprite(c + "Slider/Slider_Basic02_Fill_Yellow");
            t.barFillGreen = LoadSprite(c + "Slider/Slider_Basic04_Fill_Green");
            t.barFillBlue = LoadSprite(c + "Slider/Slider_Basic01_Fill_Blue");
            t.barFillRed = LoadSprite(c + "Slider/Slider_Basic04_Fill_Red");

            t.gemIcon = LoadSprite(c + "IconMisc/Icon_ImageIcon_Gem01_l");
            t.trophyIcon = LoadSprite(c + "IconMisc/Icon_ImageIcon_Trophy_l");
            t.swordIcon = LoadSprite(c + "IconMisc/Icon_ImageIcon_Knife_Battle");
            t.timerIcon = LoadSprite(c + "IconMisc/Icon_PictoIcon_Time");
            t.coinIcon = LoadSprite(c + "IconMisc/Icon_ImageIcon_Coin01_l");
            t.energyIcon = LoadSprite(c + "IconMisc/Icon_ImageIcon_Energy");
            t.mapIcon = LoadSprite(c + "IconMisc/Icon_ImageIcon_Map_l");
            t.shopIcon = LoadSprite(c + "IconMisc/Icon_MenuIcon02_Shop");
            t.inventoryIcon = LoadSprite(c + "IconMisc/Icon_MenuIcon02_Inventory");
            t.cardsIcon = LoadSprite(c + "IconMisc/Icon_MenuIcon02_Cards");
            t.rewardsIcon = LoadSprite(c + "IconMisc/Icon_MenuIcon04_Reward");
            t.rankingIcon = LoadSprite(c + "IconMisc/Icon_MenuIcon04_Trophy");
            t.missionIcon = LoadSprite(c + "IconMisc/Icon_MenuIcon04_Mission");
            t.friendsIcon = LoadSprite(c + "IconMisc/Icon_MenuIcon04_Friends");
            t.hpIcon = LoadSprite(c + "IconMisc/Icon_StatsIcon_Hp01");
            t.damageIcon = LoadSprite(c + "IconMisc/Icon_StatsIcon_Damage");
            t.speedIcon = LoadSprite(c + "IconMisc/Icon_StatsIcon_Speed");
            t.starOnIcon = LoadSprite(c + "IconMisc/Icon_ImageIcon_StarGrade_s_On");
            t.starOffIcon = LoadSprite(c + "IconMisc/Icon_ImageIcon_StarGrade_s_Off");
            t.resourceCapsule = LoadSprite(c + "UI_Etc/ResourceBar_Bg");
            t.resourcePlusButton = LoadSprite(c + "UI_Etc/ResourceBar_Btn_Single_Blue");
            t.resourcePlusIcon = LoadSprite(c + "UI_Etc/ResourceBar_Btn_Icon_Add");
            t.giftIcon = LoadSprite(c + "IconMisc/Icon_ImageIcon_Gift_Blue");
            t.passiveSkillIcon = LoadSprite(c + "IconMisc/Icon_SkillIcon_Passive_Get");
            t.levelFrameHighlight = LoadSprite(c + "IconMisc/Icon_ImageIcon_LevelFrame2_Highlight");

            string pictos = c + "Icon_PictoIcons/128/";
            t.schoolArcaneIcon = LoadSprite(pictos + "Pictoicon_Magic_Ball");
            t.schoolFireIcon = LoadSprite(pictos + "Pictoicon_Fire");
            t.schoolFrostIcon = LoadSprite(pictos + "Pictoicon_Water");
            t.schoolStormIcon = LoadSprite(pictos + "Pictoicon_Thunder");
            t.schoolEarthIcon = LoadSprite(pictos + "Pictoicon_Magic_Square");
            t.schoolNatureIcon = LoadSprite(pictos + "Pictoicon_Leaf");
            t.schoolVoidIcon = LoadSprite(pictos + "Pictoicon_Moon");
            t.archerIcon = LoadSprite(pictos + "Pictoicon_Arrow");
            t.spellCastIcon = LoadSprite(pictos + "Pictoicon_Magic_Ball");
            t.spellHasteIcon = LoadSprite(pictos + "Pictoicon_Wand_2");
            t.spellUltimateIcon = LoadSprite(pictos + "Pictoicon_Magic_Bomb");

            string play = Gui + "Sprites/Demo/Demo_Play/";
            t.spellOrbFrame = LoadSprite(play + "Play_Skill_Bg_Frame");
            t.spellOrbFocus = LoadSprite(play + "Play_Skill_Bg_Frame_Orange");
            t.spellCooldown = LoadSprite(play + "Play_Skill_Cooltime");
            t.joystickBackground = LoadSprite(play + "Play_Joystick_bg");
            t.joystickHandle = LoadSprite(play + "Play_Joystick_handle");
            t.minimapFrame = LoadSprite(play + "Play_Minimap_Bg_Frame");
            t.matchTimeFrame = LoadSprite(play + "Play_Time_Bg_pvp");

            string fx = "Assets/Layer Lab/GUI Pro-CasualGame/Prefabs/";
            t.levelUpPanelPrefab = LoadPrefab(fx + "Prefabs_DemoScene_Panels/LevelUp.prefab");
            t.rewardPopupPrefab = LoadPrefab(fx + "Prefabs_DemoScene_Panels/Popup_RewardGet.prefab");
            t.fxSpreadStar = LoadPrefab(fx + "Prefabs_DemoScene_Particle/Fx_Spread_Star03.prefab");
            t.fxSpreadCircle = LoadPrefab(fx + "Prefabs_DemoScene_Particle/Fx_Spread_Circle01.prefab");
            t.fxRotateLight = LoadPrefab(fx + "Prefabs_DemoScene_Particle/Fx_Rotate_Light03.prefab");
            t.fxSparkleYellow = LoadPrefab(fx + "Prefabs_DemoScene_Particle/Fx_Sparkle_XStar01_BlurryYellow.prefab");
            t.fxSparkleBlue = LoadPrefab(fx + "Prefabs_DemoScene_Particle/Fx_Sparkle_LongStar01_ClearBlue.prefab");

            // Generated by PortraitStudio during the arena build; may not exist
            // on a fresh checkout until the first build_scene run completes.
            t.minimapBackground = AssetDatabase.LoadAssetAtPath<Sprite>("Assets/Textures/ArenaMinimap.png");
            return t;
        }

        /// <summary>
        /// Copy two DNP demo presets into project-owned prefabs (once) and
        /// tune them for brawl hits: pooling on, bigger numbers for bigger
        /// hits, short lifetime so spam stays readable.
        /// </summary>
        public static (DamageNumberMesh enemyHit, DamageNumberMesh allyHurt, DamageNumberMesh heal) EnsureDnpPrefabs()
        {
            System.IO.Directory.CreateDirectory(DnpOut);
            var hit = EnsureVariant("Clear.prefab", "EnemyHit.prefab");
            var hurt = EnsureVariant("Red Glow.prefab", "AllyHurt.prefab");
            var heal = EnsureVariant("Clear.prefab", "Heal.prefab");
            if (heal != null)
            {
                // "+27" instead of a bare number; DamagePopups tints it green.
                heal.enableLeftText = true;
                heal.leftText = "+";
                EditorUtility.SetDirty(heal);
            }
            AssetDatabase.SaveAssets();
            return (hit, hurt, heal);
        }

        static DamageNumberMesh EnsureVariant(string source, string dest)
        {
            string destPath = DnpOut + dest;
            if (AssetDatabase.LoadAssetAtPath<GameObject>(destPath) == null)
            {
                if (!AssetDatabase.CopyAsset(Dnp + source, destPath))
                {
                    Debug.LogError("[ThemeKit] failed to copy DNP prefab " + source);
                    return null;
                }
            }
            var dn = AssetDatabase.LoadAssetAtPath<DamageNumberMesh>(destPath);
            if (dn == null) return null;
            dn.enablePooling = true;
            dn.poolSize = 60;
            dn.lifetime = 0.85f;
            dn.enableScaleByNumber = true;
            dn.scaleByNumberSettings.fromNumber = 10f;
            dn.scaleByNumberSettings.toNumber = 40f;
            dn.scaleByNumberSettings.fromScale = 0.85f;
            dn.scaleByNumberSettings.toScale = 1.6f;
            EditorUtility.SetDirty(dn);
            return dn;
        }
    }
}
