using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace BrawlArena
{
    /// <summary>The elemental rules layered over the shared projectile combat loop.</summary>
    public enum SpellSchool
    {
        None,
        Arcane,
        Fire,
        Frost,
        Storm,
        Earth,
        Void,
        Poison,
    }

    /// <summary>
    /// Small, serializable spell payload. Values are deliberately bounded when a
    /// projectile launches so authored data cannot create infinite chains,
    /// permanent slows, or unbounded damage-over-time stacks.
    /// </summary>
    [Serializable]
    public struct SpellSpecialty
    {
        public SpellSchool school;
        [Range(0f, 1f)] public float burnDamageFraction;
        [Min(0f)] public float burnDuration;
        [Min(0.1f)] public float burnTickInterval;
        [Range(0.25f, 1f)] public float slowMultiplier;
        [Min(0f)] public float slowDuration;
        [Range(0, 3)] public int chainTargets;
        [Min(0f)] public float chainRange;
        [Range(0f, 1f)] public float chainDamageMultiplier;
        [Min(0f)] public float knockback;
        [Range(0f, 0.5f)] public float sustainFraction;
        [Min(0f)] public float voidPullDistance;
        [Range(0f, 1f)] public float poisonDamageFraction;
        [Min(0f)] public float poisonDuration;
        [Min(0.1f)] public float poisonTickInterval;
        [Min(0f)] public float groundEffectRadius;
        [Min(0f)] public float groundEffectDuration;
        [Range(0f, 1f)] public float groundBurnFraction;
        [Range(0f, 2f)] public float allyHealFraction;
        [Min(0f)] public float allyHealRadius;
        [Range(0f, 1f)] public float ritualHealFraction;

        public static SpellSpecialty ForSchool(SpellSchool school)
        {
            var value = new SpellSpecialty
            {
                school = school,
                slowMultiplier = 1f,
                burnTickInterval = 0.6f,
                poisonTickInterval = 0.7f,
            };
            switch (school)
            {
                case SpellSchool.Arcane:
                    value.sustainFraction = 0.08f;
                    value.allyHealFraction = 0.75f;
                    value.allyHealRadius = 8f;
                    value.ritualHealFraction = 0.24f;
                    break;
                case SpellSchool.Fire:
                    value.burnDamageFraction = 0.35f;
                    value.burnDuration = 2.4f;
                    value.groundEffectRadius = 2.35f;
                    value.groundEffectDuration = 4f;
                    value.groundBurnFraction = 0.24f;
                    break;
                case SpellSchool.Frost:
                    value.slowMultiplier = 0.65f;
                    value.slowDuration = 1.8f;
                    break;
                case SpellSchool.Storm:
                    value.chainTargets = 2;
                    value.chainRange = 4.25f;
                    value.chainDamageMultiplier = 0.55f;
                    break;
                case SpellSchool.Earth:
                    value.knockback = 1.6f;
                    break;
                case SpellSchool.Void:
                    value.voidPullDistance = 1.4f;
                    break;
                case SpellSchool.Poison:
                    value.poisonDamageFraction = 0.58f;
                    value.poisonDuration = 4.2f;
                    value.poisonTickInterval = 0.7f;
                    break;
            }
            return value;
        }

        public SpellSpecialty Sanitized()
        {
            var value = this;
            value.burnDamageFraction = Mathf.Clamp(value.burnDamageFraction, 0f, 1f);
            value.burnDuration = Mathf.Clamp(value.burnDuration, 0f, 6f);
            value.burnTickInterval = Mathf.Clamp(value.burnTickInterval, 0.2f, 2f);
            value.slowMultiplier = value.slowDuration > 0f
                ? Mathf.Clamp(value.slowMultiplier, 0.25f, 1f)
                : 1f;
            value.slowDuration = Mathf.Clamp(value.slowDuration, 0f, 4f);
            value.chainTargets = Mathf.Clamp(value.chainTargets, 0, 3);
            value.chainRange = Mathf.Clamp(value.chainRange, 0f, 7f);
            value.chainDamageMultiplier = Mathf.Clamp01(value.chainDamageMultiplier);
            value.knockback = Mathf.Clamp(value.knockback, 0f, 4f);
            value.sustainFraction = Mathf.Clamp(value.sustainFraction, 0f, 0.5f);
            value.voidPullDistance = Mathf.Clamp(value.voidPullDistance, 0f, 3f);
            value.poisonDamageFraction = Mathf.Clamp01(value.poisonDamageFraction);
            value.poisonDuration = Mathf.Clamp(value.poisonDuration, 0f, 8f);
            value.poisonTickInterval = Mathf.Clamp(value.poisonTickInterval, 0.2f, 2f);
            value.groundEffectRadius = Mathf.Clamp(value.groundEffectRadius, 0f, 5f);
            value.groundEffectDuration = Mathf.Clamp(value.groundEffectDuration, 0f, 8f);
            value.groundBurnFraction = Mathf.Clamp01(value.groundBurnFraction);
            value.allyHealFraction = Mathf.Clamp(value.allyHealFraction, 0f, 2f);
            value.allyHealRadius = Mathf.Clamp(value.allyHealRadius, 0f, 12f);
            value.ritualHealFraction = Mathf.Clamp01(value.ritualHealFraction);
            return value;
        }
    }

    /// <summary>One playable brawler archetype, configured in the scene roster.</summary>
    [Serializable]
    public class BrawlerDefinition
    {
        public string id;
        public string displayName;
        public string role;
        [TextArea] public string description;
        public Sprite portrait;
        [Header("Invector character bodies")]
        [Tooltip("Required inactive production-human Invector prefab for this exact roster id.")]
        public GameObject invectorHumanPrefab;
        [Tooltip("Required inactive production-AI Invector prefab for this exact roster id.")]
        public GameObject invectorAIPrefab;
        public float maxHealth = 100f;
        public float damage = 20f;
        public float attackRange = 2.2f;
        public float attackRadius = 1.5f;
        public float cooldown = 0.9f;
        [Tooltip("Seconds required to restore one basic-attack charge. Values from older scenes that deserialize as zero use the production default.")]
        public float basicAttackReloadInterval = MobileCombatRules.BasicAttackReloadInterval;
        public float hitDelay = 0.35f;
        public float moveLock = 0.45f;
        public float moveSpeed = 5f;
        public float autoAimRange = 3.5f;
        public GameObject projectilePrefab;
        public float projectileSpeed = 16f;
        [Header("Spell presentation")]
        public GameObject castVfx;
        public GameObject secondaryCastVfx;
        public GameObject swingVfx;
        public GameObject impactVfx;
        public GameObject secondaryImpactVfx;
        public GameObject koVfx;
        public GameObject spawnVfx;
        public SpellSpecialty specialty;
        [Tooltip("Optional attack sound. The shared combat palette is used when empty.")]
        public AudioClip attackSfx;
        [Tooltip("Optional received-hit sound. The shared combat palette is used when empty.")]
        public AudioClip hitSfx;

        [Header("Super")]
        public string superName;
        public BrawlerSuperStyle superStyle = BrawlerSuperStyle.Burst;
        public float superDamageMultiplier = 1.6f;
        public float superRange = 3.2f;
        public float superKnockback = 6f;
        public float superDashDistance = 4.8f;
        public float superProjectileSpeed = 22f;
        public float superProjectileBlastRadius = 2f;
        public GameObject superProjectilePrefab;
        public GameObject superImpactVfx;
        public GameObject superVfx;
        public GameObject secondarySuperVfx;

        /// <summary>
        /// Arena scenes created before supers existed keep their serialized
        /// roster. Populate those entries at runtime while preserving any
        /// explicitly authored super settings.
        /// </summary>
        public void EnsureSuperConfiguration()
        {
            EnsureRpgIdentityConfiguration();
            EnsureSpecialtyConfiguration();
            string heroId = (id ?? string.Empty).ToLowerInvariant();
            bool legacyLifeSuper = heroId == "arcane" &&
                (string.IsNullOrEmpty(superName) || superName == "ASTRAL CONVERGENCE" ||
                 superName == "ARCANE OVERLOAD");
            bool legacyPoisonSuper = (heroId == "void" || heroId == "poison") &&
                (string.IsNullOrEmpty(superName) || superName == "RIFT STEP" ||
                 superName == "EVENT HORIZON");
            if (legacyLifeSuper)
            {
                SetSuper("SANCTUARY NOVA", BrawlerSuperStyle.Burst, 1.38f, 4.6f,
                    3.5f, 0f, 0f, 0f);
            }
            else if (legacyPoisonSuper)
            {
                SetSuper("TOXIC BLOOM", BrawlerSuperStyle.ProjectileBlast, 1.62f, 13f,
                    4f, 0f, 22f, 2.8f);
            }
            else if (string.IsNullOrEmpty(superName))
            {
                switch (heroId)
                {
                    case "aria":
                        SetSuper("ARCANE TEMPEST", BrawlerSuperStyle.Burst, 1.55f, 3.15f, 5.5f, 0f, 0f, 0f);
                        break;
                    case "bastion":
                        SetSuper("AEGIS SHOCKWAVE", BrawlerSuperStyle.Burst, 1.2f, 3.9f, 8.5f, 0f, 0f, 0f);
                        break;
                    case "nova":
                        SetSuper("THUNDERBURST", BrawlerSuperStyle.ProjectileBlast, 1.7f, 13f, 4.5f, 0f, 24f, 2.4f);
                        break;
                    case "grimm":
                        SetSuper("INFERNO BREAKER", BrawlerSuperStyle.Burst, 2f, 3.25f, 7f, 0f, 0f, 0f);
                        break;
                    case "vex":
                        SetSuper("SHADOW STEP", BrawlerSuperStyle.Dash, 1.75f, 2.65f, 6.5f, 5.6f, 0f, 0f);
                        break;
                    case "thorn":
                        SetSuper("EXPLOSIVE ARROW", BrawlerSuperStyle.ProjectileBlast, 1.85f, 14f, 6.5f, 0f, 29f, 2.6f);
                        break;
                    case "fire":
                        SetSuper("INFERNO COMET", BrawlerSuperStyle.ProjectileBlast, 1.95f, 13f, 5f, 0f, 23f, 2.7f);
                        break;
                    case "frost":
                        SetSuper("ABSOLUTE ZERO", BrawlerSuperStyle.Burst, 1.45f, 4.2f, 8f, 0f, 0f, 0f);
                        break;
                    case "storm":
                        SetSuper("TEMPEST CHAIN", BrawlerSuperStyle.ProjectileBlast, 1.55f, 14f, 4f, 0f, 29f, 2.1f);
                        break;
                    case "earth":
                        SetSuper("TECTONIC WAVE", BrawlerSuperStyle.Burst, 1.55f, 4.4f, 10f, 0f, 0f, 0f);
                        break;
                    default:
                        SetSuper("POWER BURST", BrawlerSuperStyle.Burst, 1.6f, 3.2f, 6f, 0f, 0f, 0f);
                        break;
                }
            }

            if (superVfx == null) superVfx = koVfx;
        }

        public void EnsureSpecialtyConfiguration()
        {
            string heroId = (id ?? string.Empty).ToLowerInvariant();
            if ((heroId == "void" || heroId == "poison") &&
                (specialty.school == SpellSchool.None || specialty.school == SpellSchool.Void))
            {
                specialty = SpellSpecialty.ForSchool(SpellSchool.Poison);
            }
            else if (specialty.school == SpellSchool.None)
            {
                SpellSchool school;
                switch (heroId)
                {
                    case "arcane": school = SpellSchool.Arcane; break;
                    case "fire": school = SpellSchool.Fire; break;
                    case "frost": school = SpellSchool.Frost; break;
                    case "storm": school = SpellSchool.Storm; break;
                    case "earth": school = SpellSchool.Earth; break;
                    case "void":
                    case "poison": school = SpellSchool.Poison; break;
                    default: school = SpellSchool.None; break;
                }
                specialty = SpellSpecialty.ForSchool(school);
            }

            // Older Arena scenes serialize the original payload and therefore
            // have zeroes for newly introduced class mechanics. Fill only those
            // new fields so explicitly tuned legacy combat values stay intact.
            SpellSpecialty defaults = SpellSpecialty.ForSchool(specialty.school);
            if (specialty.school == SpellSchool.Arcane)
            {
                if (specialty.allyHealFraction <= 0f)
                    specialty.allyHealFraction = defaults.allyHealFraction;
                if (specialty.allyHealRadius <= 0f)
                    specialty.allyHealRadius = defaults.allyHealRadius;
                if (specialty.ritualHealFraction <= 0f)
                    specialty.ritualHealFraction = defaults.ritualHealFraction;
            }
            else if (specialty.school == SpellSchool.Fire)
            {
                if (specialty.groundEffectRadius <= 0f)
                    specialty.groundEffectRadius = defaults.groundEffectRadius;
                if (specialty.groundEffectDuration <= 0f)
                    specialty.groundEffectDuration = defaults.groundEffectDuration;
                if (specialty.groundBurnFraction <= 0f)
                    specialty.groundBurnFraction = defaults.groundBurnFraction;
            }
            else if (specialty.school == SpellSchool.Poison)
            {
                if (specialty.poisonDamageFraction <= 0f)
                    specialty.poisonDamageFraction = defaults.poisonDamageFraction;
                if (specialty.poisonDuration <= 0f)
                    specialty.poisonDuration = defaults.poisonDuration;
                if (specialty.poisonTickInterval <= 0f)
                    specialty.poisonTickInterval = defaults.poisonTickInterval;
            }
            specialty = specialty.Sanitized();
        }

        void EnsureRpgIdentityConfiguration()
        {
            string heroId = (id ?? string.Empty).ToLowerInvariant();
            if (heroId == "arcane")
            {
                if (string.IsNullOrEmpty(displayName)) displayName = "Aether";
                if (string.IsNullOrEmpty(role) || role == "Arcane Savant")
                    role = "Lifeweaver";
                if (string.IsNullOrEmpty(description) ||
                    description.IndexOf("arcane sustain", StringComparison.OrdinalIgnoreCase) >= 0)
                    description = "A radiant clan healer whose attacks mend the weakest nearby ally and whose ritual restores the whole formation.";
            }
            else if (heroId == "void" || heroId == "poison")
            {
                if (string.IsNullOrEmpty(displayName) || displayName == "Nyx")
                    displayName = "Mire";
                if (string.IsNullOrEmpty(role) || role == "Voidweaver")
                    role = "Plagueweaver";
                if (string.IsNullOrEmpty(description) ||
                    description.IndexOf("space-bender", StringComparison.OrdinalIgnoreCase) >= 0)
                    description = "A toxic attrition mage whose plague bolts keep damaging enemies long after the first hit.";
            }
        }

        void SetSuper(string name, BrawlerSuperStyle style, float damageMultiplier, float range,
            float knockback, float dashDistance, float projectileSpeed, float projectileBlastRadius)
        {
            superName = name;
            superStyle = style;
            superDamageMultiplier = damageMultiplier;
            superRange = range;
            superKnockback = knockback;
            superDashDistance = dashDistance;
            superProjectileSpeed = projectileSpeed;
            superProjectileBlastRadius = projectileBlastRadius;
        }
    }

    /// <summary>
    /// Pre-match flow: character select screen -> loading screen -> spawn the
    /// picked brawler as the player plus randomized bots, then start the match.
    /// All UI is built in code, like BrawlHUD. When Automation/autopilot.flag
    /// exists (editor test harness), a random character is picked automatically
    /// and the player is bot-driven so the match can run unattended.
    /// </summary>
    public class GameFlow : MonoBehaviour
    {
        public BrawlerDefinition[] roster;
        Canvas canvas;
        GameObject selectPanel;
        GameObject loadingPanel;
        Transform loadingCardsRoot;
        Image loadingFill;
        TextMeshProUGUI loadingTip;
        TextMeshProUGUI loadingStatus;
        TextMeshProUGUI loadingPercent;
        readonly List<CanvasGroup> loadingCardGroups = new List<CanvasGroup>();
        Font font;
        UiTheme theme;
        int playerKills;
        string playerCharacterId;
        bool rewardHooked;

        struct LineupEntry
        {
            public int defIndex;
            public TeamId team;
            public bool isPlayer;
            public string gamertag;
        }

        static readonly string[] BotNames =
        {
            "ShadowFox", "BlitzKing", "NoScope99", "LunaStar", "IronPaw",
            "TurboSnail", "MangoBoom", "GrimReader", "PixelPirate", "SirLagsalot",
        };

        static readonly string[] Tips =
        {
            "TIP: RELEASE CAST ONCE — TAP TO AUTO-AIM OR DRAG TO COMMIT",
            "TIP: TAP WARD STEP WHILE MOVING TO COMMIT AN ESCAPE",
            "TIP: RANGED BRAWLERS MELT UP CLOSE — DIVE THEM",
            "TIP: KOs NEAR YOUR SPAWN ARE EASIER TO FOLLOW UP",
            "TIP: SAVE 20 WARD FLOW WHEN YOU EXPECT A COUNTERATTACK",
            "TIP: PUSH FORWARD, WIN KOs, AND CLAIM XP CACHES TO LEVEL UP",
            "TIP: ARCHERS CONTROL LONG LANES — USE COVER TO CLOSE THE GAP",
        };

        static bool AutopilotRequested
        {
            get
            {
                if (!Application.isEditor) return false;
                string flag = Path.Combine(Directory.GetParent(Application.dataPath).FullName, "Automation", "autopilot.flag");
                if (!File.Exists(flag)) return false;
                // Flag content selects the mode for unattended tests ("gemgrab").
                try
                {
                    if (File.ReadAllText(flag).Contains("gemgrab")) MatchSetup.Mode = GameMode.GemGrab;
                }
                catch { }
                return true;
            }
        }

        /// <summary>Automation breadcrumb readable from the editor status dump.</summary>
        public static string DebugPhase = "none";

        void Start()
        {
            if (roster != null)
            {
                foreach (var definition in roster)
                    if (definition != null) definition.EnsureSuperConfiguration();
            }
            font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            theme = FindFirstObjectByType<UiTheme>();
            BuildUi();
            if (BrawlHUD.Instance != null) BrawlHUD.Instance.SetGameplayVisible(false);

            // Launched from the main menu: character and mode already chosen.
            // Under the test harness the player must still be bot-driven.
            if (MatchSetup.CharacterIndex >= 0 && MatchSetup.CharacterIndex < roster.Length)
            {
                DebugPhase = "menu-selection " + MatchSetup.CharacterIndex;
                selectPanel.SetActive(false);
                StartCoroutine(LoadAndSpawn(MatchSetup.CharacterIndex, AutopilotRequested));
                return;
            }

            bool auto = AutopilotRequested;
            DebugPhase = "started autopilot=" + auto;
            if (auto) StartCoroutine(AutoPick());
        }

        IEnumerator AutoPick()
        {
            DebugPhase = "autopick-waiting";
            yield return new WaitForSeconds(0.8f);
            DebugPhase = "autopick-firing";
            if (selectPanel != null && selectPanel.activeSelf)
                Pick(UnityEngine.Random.Range(0, roster.Length), true);
        }

        void Pick(int index, bool autopilot)
        {
            if (!selectPanel.activeSelf) return;
            selectPanel.SetActive(false);
            StartCoroutine(LoadAndSpawn(index, autopilot));
        }

        IEnumerator LoadAndSpawn(int index, bool autopilot)
        {
            DebugPhase = "loading pick=" + index;
            var lineup = BuildLineup(index);
            ShowMatchFound(lineup);
            loadingTip.text = Tips[UnityEngine.Random.Range(0, Tips.Length)];
            UpdateLoadingPresentation(0f);
            float t = 0f;
            // Long enough to actually read the two lineups.
            const float duration = 3.4f;
            while (t < duration)
            {
                t += Time.deltaTime;
                UpdateLoadingPresentation(Mathf.Clamp01(t / duration));
                yield return null;
            }

            SpawnAll(lineup, autopilot);
            DebugPhase = "spawned";
            loadingPanel.SetActive(false);
            if (BrawlHUD.Instance != null) BrawlHUD.Instance.SetGameplayVisible(true);
            // The pre-match flow is a complete overlay canvas. Disable the
            // whole layer once combat begins so it cannot sit invisibly above
            // the live HUD or intercept mobile touches.
            if (canvas != null) canvas.gameObject.SetActive(false);
            if (MatchManager.Instance != null)
                MatchManager.Instance.mode = MatchSetup.Mode;

            if (GameplayCoachState.ShouldShow(autopilot))
            {
                DebugPhase = "coach";
                yield return GameplayCoach.ShowIfNeeded(autopilot);
            }

            if (MatchManager.Instance != null)
            {
                MatchManager.Instance.BeginMatch();
                HookRewards();
            }
            DebugPhase = "match-begun mode=" + MatchSetup.Mode;
        }

        List<LineupEntry> BuildLineup(int playerIndex)
        {
            var names = new List<string>(BotNames);
            string TakeName()
            {
                int i = UnityEngine.Random.Range(0, names.Count);
                string n = names[i];
                names.RemoveAt(i);
                return n;
            }

            int[] blueDefinitions = MatchLineupPlanner.BuildTeamDefinitionIndices(
                roster.Length, ArenaLayout.TeamSize, playerIndex,
                UnityEngine.Random.Range(0, int.MaxValue));
            int[] redDefinitions = MatchLineupPlanner.BuildTeamDefinitionIndices(
                roster.Length, ArenaLayout.TeamSize, -1,
                UnityEngine.Random.Range(0, int.MaxValue));

            var lineup = new List<LineupEntry>(ArenaLayout.TeamSize * 2)
            {
                new LineupEntry { defIndex = playerIndex, team = TeamId.Blue, isPlayer = true, gamertag = "YOU" },
            };

            // The selected player is pinned in blue slot zero. The four-hero
            // roster is exhausted before one definition is reused to fill the
            // fifth team slot.
            for (int i = 1; i < blueDefinitions.Length; i++)
            {
                lineup.Add(new LineupEntry
                {
                    defIndex = blueDefinitions[i],
                    team = TeamId.Blue,
                    gamertag = TakeName(),
                });
            }
            for (int i = 0; i < redDefinitions.Length; i++)
            {
                lineup.Add(new LineupEntry
                {
                    defIndex = redDefinitions[i],
                    team = TeamId.Red,
                    gamertag = TakeName(),
                });
            }
            return lineup;
        }

        void SpawnAll(List<LineupEntry> lineup, bool autopilot)
        {
            var mm = MatchManager.Instance;
            Vector3 SpawnPos(Transform[] set, int i) =>
                set != null && i < set.Length && set[i] != null ? set[i].position : Vector3.zero;

            int blueSlot = 0;
            int redSlot = 0;
            BrawlerController player = null;
            foreach (var entry in lineup)
            {
                Vector3 pos = entry.team == TeamId.Blue
                    ? SpawnPos(mm.blueSpawns, blueSlot++)
                    : SpawnPos(mm.redSpawns, redSlot++);
                var def = roster[entry.defIndex];
                // Only the player's own character benefits from shop levels.
                float mult = entry.isPlayer
                    ? Progress.StatMultiplier(Progress.Get(def.id).level)
                    : 1f;
                bool asHumanPlayer = entry.isPlayer && !autopilot;
                var ctrl = Spawn(def, entry.team, pos, asHumanPlayer, mult);
                ctrl.playerTag = entry.gamertag;
                ctrl.role = def.role;
                ctrl.portrait = def.portrait;
                if (entry.isPlayer)
                {
                    ApplyLoadout(ctrl, def);
                    player = ctrl;
                    playerCharacterId = def.id;
                }
            }

            var cam = UnityEngine.Object.FindFirstObjectByType<BrawlCamera>();
            if (cam != null && player != null) cam.SetTarget(player.transform);
        }

        void ApplyLoadout(BrawlerController ctrl, BrawlerDefinition def)
        {
            if (ctrl == null) return;
            if (Progress.IsCardEquipped(0))
                ctrl.attackDamage = Mathf.Round(ctrl.attackDamage * 1.08f);
            if (Progress.IsCardEquipped(1))
            {
                var health = ctrl.GetComponent<Health>();
                if (health != null) health.SetMax(Mathf.Round(health.Max * 1.12f));
            }
            if (Progress.IsCardEquipped(2))
            {
                // Capacity stays at exactly three steps for every brawler;
                // progression improves recovery rather than burst mobility.
                ctrl.staminaRegenPerSec *= 1.18f;
            }
            if (Progress.IsCardEquipped(3))
            {
                if (def != null && def.projectilePrefab != null) ctrl.attackCooldown *= 0.9f;
                else ctrl.attackHitDelay *= 0.95f;
            }
            if (Progress.IsCardEquipped(4))
                ctrl.autoAimRange += 0.6f;
            if (Progress.IsCardEquipped(5))
                ctrl.respawnDelayMultiplier = 0.85f;

            CharacterSkillBook.ApplyProgression(ctrl, def);
        }

        // ---------------- match rewards ----------------

        void HookRewards()
        {
            if (rewardHooked || MatchManager.Instance == null) return;
            rewardHooked = true;
            playerKills = 0;
            MatchManager.Instance.Kill += OnKill;
            MatchManager.Instance.MatchEnded += OnMatchEnded;
        }

        void OnDestroy()
        {
            if (rewardHooked && MatchManager.Instance != null)
            {
                MatchManager.Instance.Kill -= OnKill;
                MatchManager.Instance.MatchEnded -= OnMatchEnded;
            }
        }

        void OnKill(BrawlerController victim, BrawlerController attacker)
        {
            if (attacker != null && attacker.IsPlayer && victim.team != attacker.team)
                playerKills++;
        }

        void OnMatchEnded(TeamId? winner)
        {
            // Unattended editor runs are verification only. They must not
            // change the developer's real progression save.
            if (AutopilotRequested) return;
            if (string.IsNullOrEmpty(playerCharacterId)) return;
            bool won = winner == TeamId.Blue;
            var character = Progress.Get(playerCharacterId);
            int pointsBefore = character.points;
            int level = character.level;
            var (points, coins) = Progress.AwardMatch(playerCharacterId, won, playerKills);
            if (BrawlHUD.Instance != null)
            {
                BrawlHUD.Instance.ShowMatchRewards(new BrawlHUD.MatchRewardSummary
                {
                    eliminations = playerKills,
                    brawlerPoints = points,
                    coins = coins,
                    pointsBefore = pointsBefore,
                    pointsAfter = Progress.Get(playerCharacterId).points,
                    level = level,
                    pointsNeeded = Progress.PointsNeeded(level),
                });
            }
        }

        public static BrawlerController Spawn(BrawlerDefinition def, TeamId team, Vector3 pos, bool asHumanPlayer, float statMult = 1f)
        {
            return BrawlerCharacterAssembly.Assemble(def, team, pos, asHumanPlayer, statMult);
        }

        public static BrawlerController Spawn(
            BrawlerDefinition def,
            TeamId team,
            Vector3 pos,
            bool asHumanPlayer,
            float statMult,
            BrawlerAssemblyContext assemblyContext)
        {
            return BrawlerCharacterAssembly.Assemble(
                def, team, pos, asHumanPlayer, statMult, assemblyContext);
        }

        // ---------------- UI construction ----------------

        void BuildUi()
        {
            var canvasGo = new GameObject("FlowCanvas", typeof(RectTransform), typeof(Canvas),
                typeof(CanvasScaler), typeof(GraphicRaycaster));
            canvasGo.transform.SetParent(transform, false);
            canvas = canvasGo.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 20;
            var scaler = canvasGo.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.matchWidthOrHeight = 0.5f;

            BuildSelectPanel(canvasGo.transform);
            BuildLoadingPanel(canvasGo.transform);
        }

        void BuildSelectPanel(Transform root)
        {
            selectPanel = NewRect("CharacterSelect", root);
            Stretch((RectTransform)selectPanel.transform);
            var dim = selectPanel.AddComponent<Image>();
            dim.color = new Color(0.05f, 0.06f, 0.1f, 0.88f);

            var title = MakeText("Title", selectPanel.transform, "CHOOSE YOUR BRAWLER", 84, new Color(1f, 0.9f, 0.45f));
            var trt = title.rectTransform;
            trt.anchorMin = trt.anchorMax = new Vector2(0.5f, 0.9f);
            trt.sizeDelta = new Vector2(1600f, 120f);

            for (int i = 0; i < roster.Length; i++)
            {
                int index = i;
                var def = roster[i];
                var card = NewRect("Card_" + def.id, selectPanel.transform);
                var rt = (RectTransform)card.transform;
                int columns = roster.Length <= 4 ? 2 : 3;
                int col = i % columns;
                int row = i / columns;
                float spacing = columns == 2 ? 520f : 470f;
                rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
                rt.anchoredPosition = new Vector2(
                    (col - (columns - 1) * 0.5f) * spacing,
                    95f - row * 380f);
                rt.sizeDelta = new Vector2(420f, 340f);

                var bg = card.AddComponent<Image>();
                bg.sprite = null;
                bg.color = new Color(0.13f, 0.16f, 0.24f, 0.98f);
                var button = card.AddComponent<Button>();
                var colors = button.colors;
                colors.highlightedColor = new Color(1.2f, 1.2f, 1.2f, 1f);
                colors.pressedColor = new Color(0.8f, 0.8f, 0.8f, 1f);
                button.colors = colors;
                button.onClick.AddListener(() => Pick(index, false));

                var name = MakeText("Name", card.transform, def.displayName, 52, Color.white);
                var nrt = name.rectTransform;
                nrt.anchorMin = nrt.anchorMax = new Vector2(0.5f, 0.82f);
                nrt.sizeDelta = new Vector2(400f, 70f);

                var role = MakeText("Role", card.transform, def.role, 30, new Color(0.7f, 0.85f, 1f));
                var rrt = role.rectTransform;
                rrt.anchorMin = rrt.anchorMax = new Vector2(0.5f, 0.65f);
                rrt.sizeDelta = new Vector2(400f, 50f);

                AddStatBar(card.transform, "HP", def.maxHealth / 150f, 0.48f, new Color(0.4f, 0.9f, 0.45f));
                AddStatBar(card.transform, "DMG", def.damage / 30f, 0.34f, new Color(1f, 0.55f, 0.35f));
                AddStatBar(card.transform, "SPD", (def.moveSpeed - 4f) / 1.6f, 0.2f, new Color(0.45f, 0.75f, 1f));

                string kind = def.projectilePrefab != null ? "RANGED" : "MELEE";
                var kindText = MakeText("Kind", card.transform, kind, 26, new Color(1f, 0.9f, 0.5f));
                var krt = kindText.rectTransform;
                krt.anchorMin = krt.anchorMax = new Vector2(0.5f, 0.07f);
                krt.sizeDelta = new Vector2(400f, 40f);
            }
        }

        void AddStatBar(Transform card, string label, float value01, float anchorY, Color color)
        {
            var text = MakeText(label, card, label, 24, new Color(1f, 1f, 1f, 0.8f));
            var trt = text.rectTransform;
            trt.anchorMin = trt.anchorMax = new Vector2(0.14f, anchorY);
            trt.sizeDelta = new Vector2(90f, 34f);

            var barBg = NewRect(label + "Bg", card);
            var brt = (RectTransform)barBg.transform;
            brt.anchorMin = brt.anchorMax = new Vector2(0.6f, anchorY);
            brt.sizeDelta = new Vector2(240f, 16f);
            barBg.AddComponent<Image>().color = new Color(0f, 0f, 0f, 0.55f);

            var fill = NewRect(label + "Fill", barBg.transform);
            var frt = (RectTransform)fill.transform;
            frt.anchorMin = new Vector2(0f, 0f);
            frt.anchorMax = new Vector2(Mathf.Clamp01(value01), 1f);
            frt.offsetMin = new Vector2(2f, 2f);
            frt.offsetMax = new Vector2(-2f, -2f);
            fill.AddComponent<Image>().color = color;
        }

        void BuildLoadingPanel(Transform root)
        {
            loadingPanel = NewRect("Loading", root);
            Stretch((RectTransform)loadingPanel.transform);
            var dim = loadingPanel.AddComponent<Image>();
            dim.color = new Color(0.012f, 0.026f, 0.065f, 1f);

            // Reuse the lobby's illustrated backdrop at low contrast so the
            // portraits remain the visual priority and loading never falls
            // back to a featureless black screen.
            if (theme != null && theme.lobbyBackgroundLeft != null)
            {
                AddLoadingLayer("BackdropLeft", loadingPanel.transform, Vector2.zero,
                    new Vector2(0.38f, 1f), theme.lobbyBackgroundLeft, new Color(0.55f, 0.75f, 1f, 0.15f));
                AddLoadingLayer("BackdropMiddle", loadingPanel.transform, new Vector2(0.31f, 0f),
                    new Vector2(0.69f, 1f), theme.lobbyBackgroundMiddle, new Color(0.55f, 0.75f, 1f, 0.08f));
                AddLoadingLayer("BackdropRight", loadingPanel.transform, new Vector2(0.62f, 0f),
                    Vector2.one, theme.lobbyBackgroundRight, new Color(1f, 0.56f, 0.5f, 0.15f));
            }

            Color allyColor = TeamUtil.Color(TeamId.Blue);
            Color enemyColor = TeamUtil.Color(TeamId.Red);
            AddLoadingLayer("AllyWash", loadingPanel.transform, new Vector2(0f, 0.54f),
                new Vector2(1f, 0.79f), null, new Color(allyColor.r, allyColor.g, allyColor.b, 0.075f));
            AddLoadingLayer("EnemyWash", loadingPanel.transform, new Vector2(0f, 0.205f),
                new Vector2(1f, 0.49f), null, new Color(enemyColor.r, enemyColor.g, enemyColor.b, 0.07f));
            AddLoadingLayer("TopChrome", loadingPanel.transform, new Vector2(0f, 0.80f),
                Vector2.one, null, new Color(0.005f, 0.018f, 0.05f, 0.72f));
            AddLoadingLayer("BottomChrome", loadingPanel.transform, Vector2.zero,
                new Vector2(1f, 0.195f), null, new Color(0.005f, 0.016f, 0.045f, 0.82f));

            var safeArea = NewRect("SafeArea", loadingPanel.transform);
            Stretch((RectTransform)safeArea.transform);
            safeArea.AddComponent<BrawlSafeArea>();
            Transform contentRoot = safeArea.transform;

            // Dynamic cards are below the static headers and versus badge in
            // draw order. Each match rebuilds only this lightweight container.
            var cards = NewRect("Lineup", contentRoot);
            Stretch((RectTransform)cards.transform);
            loadingCardsRoot = cards.transform;

            var title = MakeLoadingText("Title", contentRoot, "MATCH FOUND", 64,
                new Color(1f, 0.87f, 0.34f), theme != null ? theme.headingFont : null);
            var trt = title.rectTransform;
            trt.anchorMin = trt.anchorMax = new Vector2(0.5f, 0.5f);
            trt.anchoredPosition = new Vector2(0f, 480f);
            trt.sizeDelta = new Vector2(1400f, 72f);

            var modeChip = AddLoadingGraphic("ModeChip", contentRoot,
                new Vector2(0f, 420f), new Vector2(430f, 52f),
                theme != null ? theme.labelChip : null, new Color(0.055f, 0.2f, 0.38f, 0.98f), Image.Type.Sliced);
            Sprite modeIcon = theme != null
                ? (MatchSetup.Mode == GameMode.GemGrab ? theme.gemIcon : theme.swordIcon)
                : null;
            if (modeIcon != null)
            {
                var icon = AddLoadingGraphic("ModeIcon", modeChip.transform,
                    new Vector2(-158f, 0f), new Vector2(34f, 34f), modeIcon, Color.white);
                icon.preserveAspect = true;
            }

            string modeName = MatchSetup.Mode == GameMode.GemGrab ? "GEM GRAB" : "KNOCKOUT";
            var mode = MakeLoadingText("Mode", modeChip.transform,
                modeName + "  |  " + ArenaLayout.TeamSize + "V" + ArenaLayout.TeamSize,
                26, Color.white, theme != null ? theme.buttonFont : null);
            Stretch(mode.rectTransform);
            mode.rectTransform.offsetMin = new Vector2(modeIcon != null ? 38f : 12f, 0f);
            mode.rectTransform.offsetMax = new Vector2(-12f, 0f);

            string objective = MatchSetup.Mode == GameMode.GemGrab
                ? "CONTROL THE NEXUS  |  HOLD 10 ARCANE SHARDS TO WIN"
                : "FIRST COVEN TO 8 BANISHMENTS WINS";
            var objectiveText = MakeLoadingText("Objective", contentRoot, objective, 20,
                new Color(0.76f, 0.9f, 1f, 0.82f), theme != null ? theme.bodyFont : null);
            var objectiveRt = objectiveText.rectTransform;
            objectiveRt.anchorMin = objectiveRt.anchorMax = new Vector2(0.5f, 0.5f);
            objectiveRt.anchoredPosition = new Vector2(0f, 375f);
            objectiveRt.sizeDelta = new Vector2(1300f, 34f);

            BuildLoadingTeamHeader(contentRoot, TeamId.Blue, "YOUR CLAN", 325f);
            BuildLoadingTeamHeader(contentRoot, TeamId.Red, "RIVAL CLAN", -25f);

            AddLoadingGraphic("AllyVersusLine", contentRoot,
                new Vector2(-440f, 31f), new Vector2(690f, 4f), null,
                new Color(allyColor.r, allyColor.g, allyColor.b, 0.9f));
            AddLoadingGraphic("EnemyVersusLine", contentRoot,
                new Vector2(440f, 31f), new Vector2(690f, 4f), null,
                new Color(enemyColor.r, enemyColor.g, enemyColor.b, 0.9f));
            if (theme != null && theme.glow != null)
            {
                var glow = AddLoadingGraphic("VersusGlow", contentRoot,
                    new Vector2(0f, 31f), new Vector2(116f, 116f), theme.glow,
                    new Color(1f, 0.65f, 0.18f, 0.45f));
                glow.preserveAspect = true;
                if (theme.additiveMaterial != null) glow.material = theme.additiveMaterial;
            }
            var versusBadge = AddLoadingGraphic("VersusBadge", contentRoot,
                new Vector2(0f, 31f), new Vector2(74f, 74f),
                theme != null ? theme.buttonRoundDark : null,
                new Color(0.04f, 0.075f, 0.14f, 0.98f), Image.Type.Sliced);
            var vs = MakeLoadingText("VS", versusBadge.transform, "VS", 34,
                new Color(1f, 0.65f, 0.24f), theme != null ? theme.headingFont : null);
            Stretch(vs.rectTransform);

            loadingStatus = MakeLoadingText("LoadingStatus", contentRoot,
                "ASSEMBLING CLANS", 22, new Color(0.83f, 0.94f, 1f),
                theme != null ? theme.buttonFont : null);
            var statusRt = loadingStatus.rectTransform;
            statusRt.anchorMin = statusRt.anchorMax = new Vector2(0.5f, 0.5f);
            statusRt.anchoredPosition = new Vector2(0f, -325f);
            statusRt.sizeDelta = new Vector2(780f, 34f);
            loadingStatus.alignment = TextAlignmentOptions.Left;

            loadingPercent = MakeLoadingText("LoadingPercent", contentRoot,
                "0%", 22, new Color(1f, 0.87f, 0.34f),
                theme != null ? theme.buttonFont : null);
            var percentRt = loadingPercent.rectTransform;
            percentRt.anchorMin = percentRt.anchorMax = new Vector2(0.5f, 0.5f);
            percentRt.anchoredPosition = new Vector2(0f, -325f);
            percentRt.sizeDelta = new Vector2(780f, 34f);
            loadingPercent.alignment = TextAlignmentOptions.Right;

            var barBg = NewRect("BarBg", contentRoot);
            var brt = (RectTransform)barBg.transform;
            brt.anchorMin = brt.anchorMax = new Vector2(0.5f, 0.5f);
            brt.anchoredPosition = new Vector2(0f, -362f);
            brt.sizeDelta = new Vector2(780f, 24f);
            var barBackground = barBg.AddComponent<Image>();
            if (theme != null && theme.barBg != null)
            {
                barBackground.sprite = theme.barBg;
                barBackground.type = Image.Type.Sliced;
            }
            barBackground.color = new Color(0.14f, 0.22f, 0.34f, 0.98f);
            barBackground.raycastTarget = false;

            var fillGo = NewRect("BarFill", barBg.transform);
            Stretch((RectTransform)fillGo.transform);
            ((RectTransform)fillGo.transform).offsetMin = new Vector2(4f, 4f);
            ((RectTransform)fillGo.transform).offsetMax = new Vector2(-4f, -4f);
            loadingFill = fillGo.AddComponent<Image>();
            loadingFill.sprite = theme != null && theme.barFillYellow != null
                ? theme.barFillYellow
                : WhiteSprite();
            loadingFill.color = Color.white;
            loadingFill.type = Image.Type.Filled;
            loadingFill.fillMethod = Image.FillMethod.Horizontal;
            loadingFill.fillOrigin = 0;
            loadingFill.fillAmount = 0f;
            loadingFill.raycastTarget = false;

            loadingTip = MakeLoadingText("Tip", contentRoot, "", 22,
                new Color(0.82f, 0.9f, 1f, 0.76f), theme != null ? theme.bodyFont : null);
            var tiprt = loadingTip.rectTransform;
            tiprt.anchorMin = tiprt.anchorMax = new Vector2(0.5f, 0.5f);
            tiprt.anchoredPosition = new Vector2(0f, -425f);
            tiprt.sizeDelta = new Vector2(1580f, 44f);

            loadingPanel.SetActive(false);
        }

        void ShowMatchFound(List<LineupEntry> lineup)
        {
            loadingPanel.SetActive(true);
            for (int i = loadingCardsRoot.childCount - 1; i >= 0; i--)
                Destroy(loadingCardsRoot.GetChild(i).gameObject);
            loadingCardGroups.Clear();

            int blueCount = 0;
            int redCount = 0;
            foreach (var entry in lineup)
            {
                if (entry.team == TeamId.Blue) blueCount++;
                else redCount++;
            }

            int blueSlot = 0;
            int redSlot = 0;
            foreach (var entry in lineup)
            {
                bool blue = entry.team == TeamId.Blue;
                int count = blue ? blueCount : redCount;
                int slot = blue ? blueSlot++ : redSlot++;
                float spacing = Mathf.Min(315f, 1500f / Mathf.Max(1, count - 1));
                float x = (slot - (count - 1) * 0.5f) * spacing;
                BuildLineupCard(entry, new Vector2(x, blue ? 182f : -175f));
            }
        }

        void BuildLineupCard(LineupEntry entry, Vector2 pos)
        {
            var def = roster[entry.defIndex];
            Color teamColor = TeamUtil.Color(entry.team);

            var slot = NewRect("Card_" + entry.gamertag, loadingCardsRoot);
            var rt = (RectTransform)slot.transform;
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = pos;
            rt.sizeDelta = new Vector2(292f, 238f);

            var group = slot.AddComponent<CanvasGroup>();
            group.alpha = 0f;
            group.interactable = false;
            group.blocksRaycasts = false;
            loadingCardGroups.Add(group);

            if (entry.isPlayer)
            {
                Sprite glowSprite = theme != null
                    ? (theme.cardGlow != null ? theme.cardGlow : theme.glow)
                    : null;
                var glow = AddLoadingGraphic("LocalGlow", slot.transform,
                    Vector2.zero, new Vector2(330f, 276f), glowSprite,
                    new Color(1f, 0.76f, 0.18f, glowSprite != null ? 0.72f : 0.28f));
                glow.preserveAspect = glowSprite != null;
                if (theme != null && theme.additiveMaterial != null && glowSprite != null)
                    glow.material = theme.additiveMaterial;
            }

            AddLoadingGraphic("Shadow", slot.transform, new Vector2(0f, -9f),
                new Vector2(288f, 230f), null, new Color(0f, 0f, 0f, 0.42f));

            var surfaceGo = NewRect("Surface", slot.transform);
            var surfaceRt = (RectTransform)surfaceGo.transform;
            surfaceRt.anchorMin = surfaceRt.anchorMax = new Vector2(0.5f, 0.5f);
            surfaceRt.sizeDelta = new Vector2(288f, 230f);
            var bg = surfaceGo.AddComponent<Image>();
            if (theme != null)
            {
                bg.sprite = entry.isPlayer
                    ? (theme.cardYellow != null ? theme.cardYellow : theme.card)
                    : entry.team == TeamId.Red && theme.cardYellow != null
                        ? theme.cardYellow
                        : theme.card;
            }
            if (bg.sprite != null)
            {
                bg.type = Image.Type.Sliced;
                bg.color = entry.isPlayer
                    ? Color.white
                    : entry.team == TeamId.Blue
                        ? new Color(0.78f, 0.9f, 1f, 1f)
                        : new Color(1f, 0.5f, 0.44f, 1f);
            }
            else
            {
                bg.color = new Color(teamColor.r * 0.22f, teamColor.g * 0.22f,
                    teamColor.b * 0.22f, 0.98f);
            }
            bg.raycastTarget = false;

            AddLoadingGraphic("TeamAccent", surfaceGo.transform, new Vector2(0f, 107f),
                new Vector2(244f, 7f), null, teamColor);

            Sprite schoolIcon = theme != null ? theme.SchoolIcon(def.id, entry.defIndex) : null;
            Sprite portraitFrameSprite = theme != null
                ? (theme.profileFrame != null ? theme.profileFrame : theme.frame)
                : null;
            var portraitFrame = AddLoadingGraphic("PortraitFrame", surfaceGo.transform,
                new Vector2(0f, 15f), new Vector2(170f, 146f), portraitFrameSprite,
                new Color(0.035f, 0.09f, 0.16f, 0.98f), Image.Type.Sliced);

            // The school sigil is intentionally always present behind the
            // render. It gives every slot a readable identity even while a
            // portrait asset is importing or if a platform drops its alpha.
            if (schoolIcon != null)
            {
                var sigil = AddLoadingGraphic("SchoolSigil", portraitFrame.transform,
                    Vector2.zero, new Vector2(112f, 112f), schoolIcon,
                    new Color(1f, 1f, 1f, 0.22f));
                sigil.preserveAspect = true;
            }

            if (def.portrait != null)
            {
                var img = AddLoadingGraphic("Portrait", portraitFrame.transform,
                    Vector2.zero, new Vector2(158f, 138f), def.portrait, Color.white);
                img.sprite = def.portrait;
                img.preserveAspect = true;
                img.raycastTarget = false;
            }

            var tag = MakeLoadingText("Tag", surfaceGo.transform,
                entry.gamertag.ToUpperInvariant(), 23,
                entry.isPlayer ? new Color(1f, 0.9f, 0.35f) : Color.white,
                theme != null ? theme.buttonFont : null);
            var tagRt = tag.rectTransform;
            tagRt.anchorMin = tagRt.anchorMax = new Vector2(0.5f, 0.5f);
            tagRt.anchoredPosition = new Vector2(0f, 96f);
            tagRt.sizeDelta = new Vector2(248f, 30f);
            tag.enableAutoSizing = true;
            tag.fontSizeMin = 18f;
            tag.fontSizeMax = 23f;
            tag.overflowMode = TextOverflowModes.Ellipsis;

            int level = Progress.Get(def.id).level;
            var name = MakeLoadingText("Name", surfaceGo.transform,
                def.displayName.ToUpperInvariant(), 22,
                new Color(1f, 1f, 1f, 0.95f), theme != null ? theme.headingFont : null);
            var nameRt = name.rectTransform;
            nameRt.anchorMin = nameRt.anchorMax = new Vector2(0.5f, 0.5f);
            nameRt.anchoredPosition = new Vector2(0f, -68f);
            nameRt.sizeDelta = new Vector2(252f, 29f);

            var role = MakeLoadingText("Role", surfaceGo.transform, def.role.ToUpperInvariant(), 18,
                new Color(0.83f, 0.93f, 1f, 0.82f), theme != null ? theme.bodyFont : null);
            var roleRt = role.rectTransform;
            roleRt.anchorMin = roleRt.anchorMax = new Vector2(0.5f, 0.5f);
            roleRt.anchoredPosition = new Vector2(0f, -96f);
            roleRt.sizeDelta = new Vector2(252f, 22f);

            if (schoolIcon != null)
            {
                var schoolBadge = AddLoadingGraphic("SchoolBadge", surfaceGo.transform,
                    new Vector2(-105f, 42f), new Vector2(34f, 34f), schoolIcon, Color.white);
                schoolBadge.preserveAspect = true;
            }

            if (entry.isPlayer)
            {
                var levelChip = AddLoadingGraphic("MasteryChip", surfaceGo.transform,
                    new Vector2(102f, 42f), new Vector2(66f, 31f),
                    theme != null ? theme.labelChip : null,
                    new Color(1f, 0.72f, 0.12f, 0.98f), Image.Type.Sliced);
                var levelText = MakeLoadingText("Mastery", levelChip.transform, "LV " + level, 17,
                    new Color(0.08f, 0.07f, 0.04f), theme != null ? theme.buttonFont : null);
                Stretch(levelText.rectTransform);
            }
        }

        void UpdateLoadingPresentation(float progress)
        {
            float eased = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(progress));
            if (loadingFill != null) loadingFill.fillAmount = eased;
            if (loadingPercent != null)
                loadingPercent.text = Mathf.RoundToInt(eased * 100f) + "%";
            if (loadingStatus != null)
            {
                loadingStatus.text = progress < 0.2f
                    ? "ASSEMBLING CLANS"
                    : progress < 0.56f
                        ? "OPENING THE SPELL CIRCLE"
                        : progress < 0.88f
                            ? "SYNCING ARENA"
                            : "BATTLE STARTING";
            }

            for (int i = 0; i < loadingCardGroups.Count; i++)
            {
                CanvasGroup group = loadingCardGroups[i];
                if (group == null) continue;
                float reveal = AccessibilitySettings.ReducedMotionEnabled
                    ? 1f
                    : Mathf.SmoothStep(0f, 1f,
                        Mathf.InverseLerp(i * 0.012f, i * 0.012f + 0.1f, progress));
                group.alpha = reveal;
                group.transform.localScale = AccessibilitySettings.ReducedMotionEnabled
                    ? Vector3.one
                    : Vector3.one * Mathf.Lerp(0.9f, 1f, reveal);
            }
        }

        void BuildLoadingTeamHeader(Transform root, TeamId team, string relationship, float y)
        {
            Color teamColor = TeamUtil.Color(team);
            AddLoadingGraphic(team + "Rule", root, new Vector2(0f, y),
                new Vector2(1540f, 4f), null,
                new Color(teamColor.r, teamColor.g, teamColor.b, 0.72f));
            var chip = AddLoadingGraphic(team + "Header", root, new Vector2(0f, y),
                new Vector2(650f, 46f), theme != null ? theme.labelChip : null,
                new Color(teamColor.r * 0.72f, teamColor.g * 0.72f, teamColor.b * 0.72f, 0.98f),
                Image.Type.Sliced);
            var label = MakeLoadingText("Label", chip.transform,
                relationship + "  |  " + TeamUtil.CueLabel(team, TeamId.Blue) + "  |  " +
                TeamUtil.ClanName(team) + "  |  " + ArenaLayout.TeamSize + " READY",
                24, Color.white, theme != null ? theme.buttonFont : null);
            Stretch(label.rectTransform);
            label.enableAutoSizing = true;
            label.fontSizeMin = 18f;
            label.fontSizeMax = 24f;
        }

        TextMeshProUGUI MakeLoadingText(string name, Transform parent, string content,
            float size, Color color, TMP_FontAsset fontAsset)
        {
            var go = NewRect(name, parent);
            var text = go.AddComponent<TextMeshProUGUI>();
            if (fontAsset != null) text.font = fontAsset;
            else if (TMP_Settings.defaultFontAsset != null) text.font = TMP_Settings.defaultFontAsset;
            text.text = content;
            text.fontSize = size;
            text.fontStyle = FontStyles.Bold;
            text.color = color;
            text.alignment = TextAlignmentOptions.Center;
            text.overflowMode = TextOverflowModes.Overflow;
            text.raycastTarget = false;
            return text;
        }

        Image AddLoadingLayer(string name, Transform parent, Vector2 anchorMin,
            Vector2 anchorMax, Sprite sprite, Color color)
        {
            var go = NewRect(name, parent);
            var rt = (RectTransform)go.transform;
            rt.anchorMin = anchorMin;
            rt.anchorMax = anchorMax;
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;
            var image = go.AddComponent<Image>();
            image.sprite = sprite;
            image.color = color;
            image.raycastTarget = false;
            return image;
        }

        Image AddLoadingGraphic(string name, Transform parent, Vector2 anchoredPosition,
            Vector2 size, Sprite sprite, Color color, Image.Type type = Image.Type.Simple)
        {
            var go = NewRect(name, parent);
            var rt = (RectTransform)go.transform;
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = anchoredPosition;
            rt.sizeDelta = size;
            var image = go.AddComponent<Image>();
            image.sprite = sprite;
            image.color = color;
            image.type = sprite != null ? type : Image.Type.Simple;
            image.raycastTarget = false;
            return image;
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

        Text MakeText(string name, Transform parent, string content, int size, Color color)
        {
            var go = NewRect(name, parent);
            var txt = go.AddComponent<Text>();
            txt.font = font;
            txt.text = content;
            txt.fontSize = size;
            txt.fontStyle = FontStyle.Bold;
            txt.color = color;
            txt.alignment = TextAnchor.MiddleCenter;
            txt.horizontalOverflow = HorizontalWrapMode.Overflow;
            txt.verticalOverflow = VerticalWrapMode.Overflow;
            txt.raycastTarget = false;
            var outline = go.AddComponent<Outline>();
            outline.effectColor = new Color(0f, 0f, 0f, 0.7f);
            outline.effectDistance = new Vector2(2f, -2f);
            return txt;
        }

        static Sprite whiteSprite;

        static Sprite WhiteSprite()
        {
            if (whiteSprite != null) return whiteSprite;
            var tex = Texture2D.whiteTexture;
            whiteSprite = Sprite.Create(tex, new Rect(0f, 0f, tex.width, tex.height), new Vector2(0.5f, 0.5f), 100f);
            return whiteSprite;
        }
    }
}
