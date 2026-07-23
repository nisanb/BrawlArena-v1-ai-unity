using UnityEngine;

namespace Crownfall
{
    public enum Team { Azure = 0, Crimson = 1 }

    public enum ClassId { Knight = 0, Greatsword = 1, Duelist = 2, Mage = 3, Warhammer = 4, Healer = 5 }

    public enum ElementId { Light = 0, Earth = 1, Frost = 2, Storm = 3, Shadow = 4, Fire = 5, Arcane = 6, Life = 7 }

    public enum MotorState { Locomotion, Attacking, Rolling, Hit, Staggered, Dead, Victory }

    [System.Serializable]
    public class ClassKit
    {
        public ClassId id;
        public string displayName;
        public string blurb;
        public bool isRanged;
        public bool canBlock;
        public float maxHealth;
        public float maxStamina;
        public float runSpeed;
        public float sprintMultiplier = 1.45f;
        public float lightDamage;
        public float heavyDamage;
        public float lightPoiseDamage;
        public float heavyPoiseDamage;
        public float maxPoise;
        public float attackRange;
        public float attackRadius;
        public float lightLunge;
        public float heavyLunge;
        public float novaRadius;
        public float blockDamageFactor = 0.25f;
        public float projectileSpeed = 19f;
        public float staminaLight = 16f;
        public float staminaHeavy = 30f;
        public float staminaRoll = 22f;

        // class skill (E / touch SKILL button)
        public string skillName;
        public float skillCooldown = 9f;
        public float staminaSkill = 34f;
        public float skillDamage;       // per hit / pulse / bolt
        public float skillPoiseDamage;  // per hit / pulse / bolt
        public float skillRadius;       // AoE reach for slam / whirl / sanctuary

        // healer support kit: a Healer's basic strikes mend allies caught in the
        // swing arc (and still chip enemies), and the skill is an AoE heal burst.
        public bool isHealer;
        public float healLight;         // HP restored to allies by a light strike
        public float healHeavy;         // HP restored to allies by a heavy strike
        public float skillHeal;         // HP restored to every ally by Sanctuary
    }

    public static class ClassKits
    {
        static readonly ClassKit[] kits =
        {
            new ClassKit { id = ClassId.Knight, displayName = "Knight", blurb = "Sword & shield. The only class that can block. Skill: Aegis Slam.",
                canBlock = true, maxHealth = 175, maxStamina = 110, runSpeed = 4.3f,
                lightDamage = 14, heavyDamage = 27, lightPoiseDamage = 12, heavyPoiseDamage = 27, maxPoise = 46,
                attackRange = 1.35f, attackRadius = 0.95f, lightLunge = 1.0f, heavyLunge = 1.7f,
                blockDamageFactor = 0.22f,
                skillName = "Aegis Slam", skillCooldown = 10f, staminaSkill = 36f,
                skillDamage = 26, skillPoiseDamage = 42, skillRadius = 3.2f },

            new ClassKit { id = ClassId.Greatsword, displayName = "Warbrand", blurb = "Colossal sword. Slow, staggering blows. Skill: Sundering Whirl.",
                maxHealth = 165, maxStamina = 105, runSpeed = 4.0f,
                lightDamage = 20, heavyDamage = 37, lightPoiseDamage = 17, heavyPoiseDamage = 34, maxPoise = 52,
                attackRange = 1.6f, attackRadius = 1.15f, lightLunge = 1.2f, heavyLunge = 2.1f,
                staminaLight = 19f, staminaHeavy = 34f,
                skillName = "Sundering Whirl", skillCooldown = 10f, staminaSkill = 38f,
                skillDamage = 16, skillPoiseDamage = 20, skillRadius = 3.0f },

            new ClassKit { id = ClassId.Duelist, displayName = "Duelist", blurb = "Twin blades. Fast combos, fast feet. Skill: Blade Dance.",
                maxHealth = 135, maxStamina = 118, runSpeed = 4.7f,
                lightDamage = 11, heavyDamage = 23, lightPoiseDamage = 9, heavyPoiseDamage = 20, maxPoise = 30,
                attackRange = 1.25f, attackRadius = 0.9f, lightLunge = 1.1f, heavyLunge = 1.9f,
                staminaLight = 12f, staminaRoll = 19f,
                skillName = "Blade Dance", skillCooldown = 8f, staminaSkill = 32f,
                skillDamage = 9, skillPoiseDamage = 8, skillRadius = 0f },

            new ClassKit { id = ClassId.Mage, displayName = "Mage", blurb = "Bolts at range, a nova when cornered. Skill: Arcane Barrage.",
                isRanged = true, maxHealth = 125, maxStamina = 100, runSpeed = 4.45f,
                lightDamage = 16, heavyDamage = 30, lightPoiseDamage = 10, heavyPoiseDamage = 26, maxPoise = 42,
                attackRange = 1.2f, attackRadius = 0.9f, lightLunge = 0f, heavyLunge = 0f,
                novaRadius = 4.0f, staminaLight = 14f, staminaHeavy = 32f,
                skillName = "Arcane Barrage", skillCooldown = 9f, staminaSkill = 34f,
                skillDamage = 8, skillPoiseDamage = 6, skillRadius = 0f },

            // the Meshy "Neon Hammer Hero" (CC0): slowest, tankiest, hardest
            // single hits; skill reuses the leap-slam routine with a wide arena-
            // shaking shockwave
            new ClassKit { id = ClassId.Warhammer, displayName = "Juggernaut", blurb = "Neon warhammer. Crushing arcs. Skill: Seismic Verdict.",
                maxHealth = 185, maxStamina = 100, runSpeed = 3.85f,
                lightDamage = 22, heavyDamage = 40, lightPoiseDamage = 19, heavyPoiseDamage = 38, maxPoise = 60,
                attackRange = 1.7f, attackRadius = 1.25f, lightLunge = 1.2f, heavyLunge = 2.2f,
                staminaLight = 20f, staminaHeavy = 36f,
                skillName = "Seismic Verdict", skillCooldown = 11f, staminaSkill = 40f,
                skillDamage = 30, skillPoiseDamage = 50, skillRadius = 4.0f },

            // Seraph: a staff-wielding battle medic. Every swing mends allies in the
            // arc while chipping enemies; the skill, Sanctuary, is a 360 AoE heal.
            new ClassKit { id = ClassId.Healer, displayName = "Seraph", blurb = "Staff healer. Swing to mend allies. Skill: Sanctuary (AoE heal).",
                isHealer = true, maxHealth = 165, maxStamina = 125, runSpeed = 4.5f,
                lightDamage = 9, heavyDamage = 17, lightPoiseDamage = 8, heavyPoiseDamage = 16, maxPoise = 72,
                attackRange = 1.5f, attackRadius = 1.15f, lightLunge = 0.9f, heavyLunge = 1.5f,
                staminaLight = 13f, staminaHeavy = 28f,
                healLight = 20, healHeavy = 34,
                skillName = "Sanctuary", skillCooldown = 13f, staminaSkill = 40f,
                skillHeal = 46, skillRadius = 5.5f },
        };

        public static ClassKit Get(ClassId id) => kits[(int)id];
    }

    public static class ElementColors
    {
        public static Color Get(ElementId el)
        {
            switch (el)
            {
                case ElementId.Light: return new Color(1f, 0.93f, 0.55f);
                case ElementId.Earth: return new Color(0.55f, 0.85f, 0.35f);
                case ElementId.Frost: return new Color(0.5f, 0.85f, 1f);
                case ElementId.Storm: return new Color(0.55f, 0.7f, 1f);
                case ElementId.Shadow: return new Color(0.7f, 0.4f, 1f);
                case ElementId.Fire: return new Color(1f, 0.55f, 0.25f);
                case ElementId.Life: return new Color(0.4f, 1f, 0.55f);
                default: return new Color(0.85f, 0.5f, 1f);
            }
        }
    }

    public static class Tuning
    {
        public const float InputBufferSeconds = 0.35f;
        public const float RollIFrameStart = 0.05f;
        public const float RollIFrameEnd = 0.62f;
        public const float RollSpeedStart = 7.2f;
        public const float RollSpeedEnd = 2.6f;
        public const float ComboWindowOpen = 0.5f;
        public const float StrikeMoment = 0.38f;
        public const float RollCancelPoint = 0.55f;
        public const float LungeStart = 0.12f;
        public const float LungeEnd = 0.42f;
        public const float StaggerDuration = 1.05f;   // was 1.55 — shorter stuns, less helpless time
        public const float RespawnSeconds = 5f;
        public const float SpawnProtection = 1.5f;
        public const float HitstopLight = 0.05f;
        public const float HitstopHeavy = 0.09f;
        public const float ComboFinisherMult = 1.5f;
        public const int MeleeComboLength = 4;
        public const float MageCastComboWindow = 2.0f;  // consecutive casts inside this chain
        public const float MageSurgeMult = 1.45f;       // 3rd chained bolt hits harder
        public const float StaminaRegenPerSec = 24f;
        public const float StaminaRegenDelay = 0.75f;
        public const float SprintStaminaPerSec = 9f;
        public const float PoiseRegenPerSec = 20f;    // was 12 — poise recovers faster, fewer staggers
        public const float PoiseRegenDelay = 1f;       // was 2
        public const float BlockAngle = 68f;
        public const float MeleeHitAngle = 85f;
    }

    public struct HitInfo
    {
        public CombatMotor attacker;
        public float damage;
        public float poiseDamage;
        public Vector3 direction;
        public Vector3 point;
        public ElementId element;
        public bool heavy;
        public bool unblockable;
    }

    public struct HitResult
    {
        public bool landed;
        public bool blocked;
        public bool killed;
        public bool staggered;
        public float damageDealt;
        /// True when the hit was relayed to the victim owner's client instead of
        /// applied here — the other fields are then an optimistic prediction.
        public bool forwarded;
    }
}
