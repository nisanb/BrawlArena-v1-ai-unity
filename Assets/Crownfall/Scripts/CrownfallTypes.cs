using UnityEngine;

namespace Crownfall
{
    public enum Team { Azure = 0, Crimson = 1 }

    public enum ClassId { Knight = 0, Greatsword = 1, Duelist = 2, Mage = 3 }

    public enum ElementId { Light = 0, Earth = 1, Frost = 2, Storm = 3, Shadow = 4, Fire = 5, Arcane = 6 }

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
    }

    public static class ClassKits
    {
        static readonly ClassKit[] kits =
        {
            new ClassKit { id = ClassId.Knight, displayName = "Knight", blurb = "Sword & shield. The only class that can block.",
                canBlock = true, maxHealth = 175, maxStamina = 110, runSpeed = 5.0f,
                lightDamage = 14, heavyDamage = 27, lightPoiseDamage = 12, heavyPoiseDamage = 27, maxPoise = 46,
                attackRange = 1.35f, attackRadius = 0.95f, lightLunge = 1.0f, heavyLunge = 1.7f,
                blockDamageFactor = 0.22f },

            new ClassKit { id = ClassId.Greatsword, displayName = "Warbrand", blurb = "Colossal sword. Slow, staggering blows.",
                maxHealth = 165, maxStamina = 105, runSpeed = 4.7f,
                lightDamage = 20, heavyDamage = 37, lightPoiseDamage = 17, heavyPoiseDamage = 34, maxPoise = 52,
                attackRange = 1.6f, attackRadius = 1.15f, lightLunge = 1.2f, heavyLunge = 2.1f,
                staminaLight = 19f, staminaHeavy = 34f },

            new ClassKit { id = ClassId.Duelist, displayName = "Duelist", blurb = "Twin blades. Fast combos, fast feet.",
                maxHealth = 135, maxStamina = 118, runSpeed = 5.5f,
                lightDamage = 11, heavyDamage = 23, lightPoiseDamage = 9, heavyPoiseDamage = 20, maxPoise = 30,
                attackRange = 1.25f, attackRadius = 0.9f, lightLunge = 1.1f, heavyLunge = 1.9f,
                staminaLight = 12f, staminaRoll = 19f },

            new ClassKit { id = ClassId.Mage, displayName = "Mage", blurb = "Bolts at range, a nova when cornered.",
                isRanged = true, maxHealth = 115, maxStamina = 100, runSpeed = 5.0f,
                lightDamage = 16, heavyDamage = 30, lightPoiseDamage = 10, heavyPoiseDamage = 26, maxPoise = 24,
                attackRange = 1.2f, attackRadius = 0.9f, lightLunge = 0f, heavyLunge = 0f,
                novaRadius = 4.0f, staminaLight = 14f, staminaHeavy = 32f },
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
                default: return new Color(0.85f, 0.5f, 1f);
            }
        }
    }

    public static class Tuning
    {
        public const float InputBufferSeconds = 0.35f;
        public const float RollIFrameStart = 0.05f;
        public const float RollIFrameEnd = 0.58f;
        public const float RollSpeedStart = 8.2f;
        public const float RollSpeedEnd = 2.2f;
        public const float ComboWindowOpen = 0.55f;
        public const float StrikeMoment = 0.40f;
        public const float RollCancelPoint = 0.62f;
        public const float LungeStart = 0.20f;
        public const float LungeEnd = 0.48f;
        public const float StaggerDuration = 1.55f;
        public const float RespawnSeconds = 5f;
        public const float SpawnProtection = 1.5f;
        public const float HitstopLight = 0.05f;
        public const float HitstopHeavy = 0.09f;
        public const float ComboFinisherMult = 1.5f;
        public const float StaminaRegenPerSec = 24f;
        public const float StaminaRegenDelay = 0.75f;
        public const float SprintStaminaPerSec = 9f;
        public const float PoiseRegenPerSec = 12f;
        public const float PoiseRegenDelay = 2f;
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
    }
}
