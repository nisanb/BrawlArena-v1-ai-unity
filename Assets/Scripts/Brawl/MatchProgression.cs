using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

namespace BrawlArena
{
    /// <summary>
    /// Match-local RPG progression for one hero. This state is deliberately
    /// separate from the persistent Progress save and is reset for every match.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(BrawlerController))]
    public sealed class HeroMatchProgression : MonoBehaviour
    {
        public const int MaxLevel = 6;

        const float HealthPerLevel = 0.06f;
        const float DamagePerLevel = 0.05f;
        const float MoveSpeedPerLevel = 0.02f;
        const float StaminaRegenPerLevel = 0.05f;

        public int Level { get; private set; } = 1;
        public int Experience { get; private set; }
        public int ExperienceToNext => Level >= MaxLevel ? 0 : ExperienceRequiredForLevel(Level);
        public float Experience01 => Level >= MaxLevel
            ? 1f
            : Mathf.Clamp01(Experience / (float)Mathf.Max(1, ExperienceToNext));

        public event Action Changed;
        public event Action<int> LeveledUp;

        BrawlerController brawler;
        Health health;
        bool baselineCaptured;
        float baseMaxHealth;
        float baseAttackDamage;
        float baseMoveSpeed;
        float baseStaminaRegen;

        internal bool IsInitialized => baselineCaptured && brawler != null;

        /// <summary>XP needed to advance from <paramref name="level"/>.</summary>
        public static int ExperienceRequiredForLevel(int level)
        {
            level = Mathf.Clamp(level, 1, MaxLevel - 1);
            return 60 + 30 * (level - 1);
        }

        void Awake()
        {
            brawler = GetComponent<BrawlerController>();
            health = GetComponent<Health>();
        }

        /// <summary>
        /// Captures the fully configured, post-loadout level-one baseline.
        /// MatchManager calls this when a brawler joins the roster.
        /// </summary>
        public void Initialize(BrawlerController owner)
        {
            if (owner == null) return;

            if (brawler != owner)
            {
                brawler = owner;
                health = owner.GetComponent<Health>();
                baselineCaptured = false;
            }
            else if (health == null)
            {
                health = owner.GetComponent<Health>();
            }

            CaptureBaselineIfNeeded();
            ResetForMatch();
        }

        /// <summary>Restores level one and the captured baseline for a fresh match.</summary>
        public void ResetForMatch()
        {
            if (!CaptureBaselineIfNeeded()) return;

            Level = 1;
            Experience = 0;
            ApplyStats(refillHealth: true);
            Changed?.Invoke();
        }

        /// <summary>
        /// Adds match-only XP, carrying overflow across levels and discarding it
        /// after level six. Returns true when any XP was accepted.
        /// </summary>
        public bool AddExperience(int amount)
        {
            if (amount <= 0 || Level >= MaxLevel || !CaptureBaselineIfNeeded()) return false;

            Experience += amount;
            while (Level < MaxLevel && Experience >= ExperienceRequiredForLevel(Level))
            {
                Experience -= ExperienceRequiredForLevel(Level);
                Level++;
                ApplyStats(refillHealth: false);
                LeveledUp?.Invoke(Level);
            }

            if (Level >= MaxLevel) Experience = 0;
            Changed?.Invoke();
            return true;
        }

        bool CaptureBaselineIfNeeded()
        {
            if (baselineCaptured) return true;
            if (brawler == null) brawler = GetComponent<BrawlerController>();
            if (health == null) health = GetComponent<Health>();
            if (brawler == null || health == null) return false;

            baseMaxHealth = Mathf.Max(1f, health.Max);
            baseAttackDamage = Mathf.Max(0f, brawler.attackDamage);
            baseMoveSpeed = Mathf.Max(0f, brawler.moveSpeed);
            baseStaminaRegen = Mathf.Max(0f, brawler.staminaRegenPerSec);
            baselineCaptured = true;
            return true;
        }

        void ApplyStats(bool refillHealth)
        {
            int levelsGained = Mathf.Max(0, Level - 1);
            float oldMaxHealth = health.Max;
            float newMaxHealth = Mathf.Round(baseMaxHealth * (1f + HealthPerLevel * levelsGained));

            brawler.attackDamage = Mathf.Round(baseAttackDamage * (1f + DamagePerLevel * levelsGained));
            brawler.moveSpeed = baseMoveSpeed * (1f + MoveSpeedPerLevel * levelsGained);
            brawler.staminaRegenPerSec = baseStaminaRegen * (1f + StaminaRegenPerLevel * levelsGained);

            if (refillHealth)
            {
                health.SetMax(newMaxHealth);
                return;
            }

            health.SetMax(newMaxHealth, false);
            float gainedMaxHealth = Mathf.Max(0f, newMaxHealth - oldMaxHealth);
            if (gainedMaxHealth > 0f) health.Heal(gainedMaxHealth);
        }
    }

    /// <summary>
    /// A non-physics XP pickup. MatchExperienceSystem distance-polls collection,
    /// while this component only owns presentation and single-loot state.
    /// </summary>
    public sealed class ExperienceBox : MonoBehaviour
    {
        MatchExperienceSystem owner;
        Transform visual;
        Vector3 visualRestPosition;
        Material runtimeMaterial;
        int slotIndex;
        float phase;

        public bool CanBeLooted { get; private set; }
        public int SlotIndex => slotIndex;

        internal static ExperienceBox Create(MatchExperienceSystem owner, int slotIndex,
            Vector3 position, GameObject prefab)
        {
            var root = new GameObject("ExperienceBox_" + slotIndex);
            root.transform.SetParent(owner.transform, false);
            root.transform.position = position;

            var box = root.AddComponent<ExperienceBox>();
            box.owner = owner;
            box.slotIndex = slotIndex;
            box.phase = slotIndex * 0.83f;
            box.CanBeLooted = true;

            GameObject visualObject;
            if (prefab != null)
            {
                visualObject = Instantiate(prefab, root.transform, false);
            }
            else
            {
                visualObject = GameObject.CreatePrimitive(PrimitiveType.Cube);
                visualObject.transform.SetParent(root.transform, false);
                visualObject.transform.localScale = new Vector3(0.85f, 0.72f, 0.85f);
                visualObject.transform.localRotation = Quaternion.Euler(0f, 45f, 0f);
                box.runtimeMaterial = ConfigureFallbackMaterial(visualObject.GetComponent<Renderer>());
                CreateExperienceLabel(root.transform);
            }

            visualObject.name = "Visual";
            box.visual = visualObject.transform;
            box.visualRestPosition = new Vector3(0f, 0.62f, 0f);
            box.visual.localPosition = box.visualRestPosition;
            DisablePhysics(root);
            return box;
        }

        public bool TryLoot(BrawlerController collector)
        {
            if (!CanBeLooted || collector == null || owner == null) return false;
            return owner.TryLootBox(this, collector);
        }

        internal void MarkLooted()
        {
            if (!CanBeLooted) return;
            CanBeLooted = false;
            gameObject.SetActive(false);
            if (Application.isPlaying) Destroy(gameObject);
        }

        internal void RemoveImmediately()
        {
            CanBeLooted = false;
            if (gameObject == null) return;
            if (Application.isPlaying) Destroy(gameObject);
            else DestroyImmediate(gameObject);
        }

        void Update()
        {
            if (!CanBeLooted || visual == null) return;
            visual.localPosition = visualRestPosition +
                                   Vector3.up * (Mathf.Sin(Time.time * 2.4f + phase) * 0.11f);
            visual.Rotate(0f, 70f * Time.deltaTime, 0f, Space.Self);
        }

        void OnDestroy()
        {
            if (runtimeMaterial == null) return;
            if (Application.isPlaying) Destroy(runtimeMaterial);
            else DestroyImmediate(runtimeMaterial);
            runtimeMaterial = null;
        }

        static void DisablePhysics(GameObject root)
        {
            foreach (Collider collider in root.GetComponentsInChildren<Collider>(true))
            {
                collider.enabled = false;
                if (Application.isPlaying) Destroy(collider);
                else DestroyImmediate(collider);
            }

            foreach (Rigidbody body in root.GetComponentsInChildren<Rigidbody>(true))
            {
                body.detectCollisions = false;
                body.isKinematic = true;
                if (Application.isPlaying) Destroy(body);
                else DestroyImmediate(body);
            }
        }

        static void CreateExperienceLabel(Transform root)
        {
            var labelObject = new GameObject("XP_Label");
            labelObject.transform.SetParent(root, false);
            labelObject.transform.localPosition = new Vector3(0f, 1.08f, 0f);
            labelObject.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
            var label = labelObject.AddComponent<TextMesh>();
            label.text = "XP";
            label.anchor = TextAnchor.MiddleCenter;
            label.alignment = TextAlignment.Center;
            label.fontSize = 64;
            label.characterSize = 0.075f;
            label.color = new Color(0.75f, 1f, 0.86f);
        }

        static Material ConfigureFallbackMaterial(Renderer renderer)
        {
            if (renderer == null) return null;
            Shader shader = Shader.Find("Universal Render Pipeline/Lit");
            if (shader == null) shader = Shader.Find("Standard");
            if (shader == null) return null;

            var material = new Material(shader) { name = "ExperienceBox_Runtime" };
            Color color = new Color(0.2f, 0.92f, 0.62f);
            if (material.HasProperty("_BaseColor")) material.SetColor("_BaseColor", color);
            else if (material.HasProperty("_Color")) material.SetColor("_Color", color);
            if (material.HasProperty("_EmissionColor"))
            {
                material.EnableKeyword("_EMISSION");
                material.SetColor("_EmissionColor", color * 1.6f);
            }
            renderer.sharedMaterial = material;
            return material;
        }
    }

    /// <summary>
    /// Owns all match XP sources: attributed KOs, anti-farming forward progress,
    /// and symmetric distance-polled XP boxes.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(MatchManager))]
    public sealed class MatchExperienceSystem : MonoBehaviour
    {
        public const int KnockoutExperience = 40;
        public const int ForwardCheckpointExperience = 15;
        public const int ExperienceBoxValue = 25;

        const float ExperienceBoxRespawnDelay = 18f;

        static readonly float[] ForwardCheckpoints = { -16f, 0f, 16f };
        static readonly Vector3[] ExperienceBoxPositions =
        {
            new Vector3(-18f, 0f, -12f),
            new Vector3(18f, 0f, 12f),
            new Vector3(18f, 0f, -12f),
            new Vector3(-18f, 0f, 12f),
            new Vector3(-8f, 0f, 0f),
            new Vector3(8f, 0f, 0f),
        };

        sealed class AdvanceRecord
        {
            public float furthestDepth;
            public int claimedMask;
        }

        public static MatchExperienceSystem Instance { get; private set; }

        [Min(0.5f)] public float experienceBoxPickupRadius = 1.8f;
        [Tooltip("Optional XP-box visual. A glowing cube is generated when empty.")]
        public GameObject experienceBoxPrefab;

        public bool Active => matchActive && manager != null && manager.State == MatchState.Playing;

        MatchManager manager;
        bool hooked;
        bool matchActive;
        readonly Dictionary<BrawlerController, AdvanceRecord> advanceRecords =
            new Dictionary<BrawlerController, AdvanceRecord>();
        readonly ExperienceBox[] experienceBoxes = new ExperienceBox[ExperienceBoxPositions.Length];
        readonly float[] boxRespawnAt = new float[ExperienceBoxPositions.Length];

        void Awake()
        {
            Instance = this;
            MatchManager attachedManager = GetComponent<MatchManager>();
            if (attachedManager != null) Attach(attachedManager);
        }

        void OnDestroy()
        {
            Unhook();
            if (Instance == this) Instance = null;
        }

        internal void Attach(MatchManager target)
        {
            if (target == null) return;
            Instance = this;
            if (hooked && manager == target) return;

            Unhook();
            manager = target;
            manager.BrawlerRegistered += OnBrawlerRegistered;
            manager.Kill += OnKill;
            manager.MatchEnded += OnMatchEnded;
            hooked = true;

            foreach (BrawlerController brawler in manager.GetBrawlers())
                TrackBrawler(brawler);
        }

        internal void BeginMatch()
        {
            if (manager == null) return;
            matchActive = true;
            advanceRecords.Clear();

            foreach (BrawlerController brawler in manager.GetBrawlers())
            {
                if (brawler == null) continue;
                HeroMatchProgression progression = EnsureProgression(brawler);
                progression?.ResetForMatch();
                brawler.ResetBasicAttackCharges();
                TrackBrawler(brawler);
            }

            ResetExperienceBoxes();
        }

        void Update()
        {
            if (!Active) return;
            EvaluateAdvancement();
            EvaluateExperienceBoxes();
            RespawnExperienceBoxes();
        }

        void OnBrawlerRegistered(BrawlerController brawler)
        {
            TrackBrawler(brawler);
        }

        void OnKill(BrawlerController victim, BrawlerController attacker)
        {
            if (!Active || victim == null || attacker == null || attacker == victim) return;
            if (attacker.team == victim.team) return;
            EnsureProgression(attacker)?.AddExperience(KnockoutExperience);
        }

        void OnMatchEnded(TeamId? winner)
        {
            matchActive = false;
        }

        void TrackBrawler(BrawlerController brawler)
        {
            if (brawler == null) return;
            EnsureProgression(brawler);
            float depth = TeamForwardDepth(brawler.team, brawler.transform.position);
            int claimedMask = 0;
            for (int i = 0; i < ForwardCheckpoints.Length; i++)
                if (depth >= ForwardCheckpoints[i]) claimedMask |= 1 << i;
            advanceRecords[brawler] = new AdvanceRecord
            {
                furthestDepth = depth,
                claimedMask = claimedMask,
            };
        }

        static HeroMatchProgression EnsureProgression(BrawlerController brawler)
        {
            if (brawler == null) return null;
            HeroMatchProgression progression = brawler.GetComponent<HeroMatchProgression>();
            if (progression == null)
            {
                progression = brawler.gameObject.AddComponent<HeroMatchProgression>();
                progression.Initialize(brawler);
            }
            else if (!progression.IsInitialized)
            {
                progression.Initialize(brawler);
            }
            return progression;
        }

        /// <summary>Mirrors both teams onto one home-to-enemy forward axis.</summary>
        public static float TeamForwardDepth(TeamId team, Vector3 position)
        {
            return team == TeamId.Blue ? position.z : -position.z;
        }

        /// <summary>
        /// Awards each forward checkpoint once. Only a new furthest position can
        /// cross a checkpoint, so retreating and walking forward again cannot farm XP.
        /// </summary>
        public void EvaluateAdvancement()
        {
            if (!Active) return;
            List<BrawlerController> brawlers = manager.GetBrawlers();
            for (int i = 0; i < brawlers.Count; i++)
            {
                BrawlerController brawler = brawlers[i];
                if (brawler == null || !brawler.CanAct) continue;
                if (!advanceRecords.TryGetValue(brawler, out AdvanceRecord record))
                {
                    TrackBrawler(brawler);
                    continue;
                }

                float depth = TeamForwardDepth(brawler.team, brawler.transform.position);
                if (depth <= record.furthestDepth) continue;

                HeroMatchProgression progression = EnsureProgression(brawler);
                for (int checkpoint = 0; checkpoint < ForwardCheckpoints.Length; checkpoint++)
                {
                    int bit = 1 << checkpoint;
                    if ((record.claimedMask & bit) != 0) continue;
                    if (record.furthestDepth < ForwardCheckpoints[checkpoint] &&
                        depth >= ForwardCheckpoints[checkpoint])
                    {
                        record.claimedMask |= bit;
                        progression?.AddExperience(ForwardCheckpointExperience);
                    }
                }
                record.furthestDepth = depth;
            }
        }

        /// <summary>Returns the closest currently lootable XP box.</summary>
        public ExperienceBox NearestExperienceBox(Vector3 from)
        {
            if (!Active) return null;
            ExperienceBox best = null;
            float bestDistance = float.MaxValue;
            for (int i = 0; i < experienceBoxes.Length; i++)
            {
                ExperienceBox box = experienceBoxes[i];
                if (box == null || !box.CanBeLooted) continue;
                Vector3 delta = box.transform.position - from;
                delta.y = 0f;
                float distance = delta.sqrMagnitude;
                if (distance >= bestDistance) continue;
                bestDistance = distance;
                best = box;
            }
            return best;
        }

        public void EvaluateExperienceBoxes()
        {
            if (!Active) return;
            float pickupRadiusSq = experienceBoxPickupRadius * experienceBoxPickupRadius;
            List<BrawlerController> brawlers = manager.GetBrawlers();

            for (int boxIndex = 0; boxIndex < experienceBoxes.Length; boxIndex++)
            {
                ExperienceBox box = experienceBoxes[boxIndex];
                if (box == null || !box.CanBeLooted) continue;

                for (int i = 0; i < brawlers.Count; i++)
                {
                    BrawlerController brawler = brawlers[i];
                    if (brawler == null || brawler.IsDead || !brawler.CanAct) continue;
                    Vector3 delta = brawler.transform.position - box.transform.position;
                    delta.y = 0f;
                    if (delta.sqrMagnitude > pickupRadiusSq) continue;
                    box.TryLoot(brawler);
                    break;
                }
            }
        }

        internal bool TryLootBox(ExperienceBox box, BrawlerController collector)
        {
            if (!Active || box == null || !box.CanBeLooted || collector == null ||
                collector.IsDead || !collector.CanAct)
                return false;

            int slot = box.SlotIndex;
            if (slot < 0 || slot >= experienceBoxes.Length || experienceBoxes[slot] != box)
                return false;

            Vector3 pickupDelta = collector.transform.position - box.transform.position;
            pickupDelta.y = 0f;
            if (pickupDelta.sqrMagnitude > experienceBoxPickupRadius * experienceBoxPickupRadius)
                return false;

            EnsureProgression(collector)?.AddExperience(ExperienceBoxValue);
            experienceBoxes[slot] = null;
            boxRespawnAt[slot] = Time.time + ExperienceBoxRespawnDelay;
            box.MarkLooted();
            return true;
        }

        void ResetExperienceBoxes()
        {
            for (int i = 0; i < experienceBoxes.Length; i++)
            {
                if (experienceBoxes[i] != null)
                {
                    experienceBoxes[i].gameObject.SetActive(false);
                    experienceBoxes[i].RemoveImmediately();
                }
                experienceBoxes[i] = null;
                boxRespawnAt[i] = 0f;
                SpawnExperienceBox(i);
            }
        }

        void RespawnExperienceBoxes()
        {
            for (int i = 0; i < experienceBoxes.Length; i++)
                if (experienceBoxes[i] == null && Time.time >= boxRespawnAt[i])
                    SpawnExperienceBox(i);
        }

        void SpawnExperienceBox(int slot)
        {
            Vector3 position = ExperienceBoxPositions[slot];
            if (NavMesh.SamplePosition(position, out NavMeshHit hit, 5f, NavMesh.AllAreas))
                position = hit.position;
            experienceBoxes[slot] = ExperienceBox.Create(this, slot, position, experienceBoxPrefab);
        }

        void Unhook()
        {
            if (hooked && manager != null)
            {
                manager.BrawlerRegistered -= OnBrawlerRegistered;
                manager.Kill -= OnKill;
                manager.MatchEnded -= OnMatchEnded;
            }
            hooked = false;
            manager = null;
            matchActive = false;
        }
    }
}
