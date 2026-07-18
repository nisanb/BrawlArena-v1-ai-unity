using UnityEngine;

namespace BrawlArena
{
    public enum CharacterSkillEffect
    {
        Damage,
        MaxHealth,
        MoveSpeed,
        AttackSpeed,
        Stamina,
        Regen,
        AutoAim,
        Respawn
    }

    public sealed class CharacterSkillDefinition
    {
        public readonly string id;
        public readonly string displayName;
        public readonly string description;
        public readonly CharacterSkillEffect effect;
        public readonly float valuePerLevel;

        public CharacterSkillDefinition(string id, string displayName, string description,
            CharacterSkillEffect effect, float valuePerLevel)
        {
            this.id = id;
            this.displayName = displayName;
            this.description = description;
            this.effect = effect;
            this.valuePerLevel = valuePerLevel;
        }

        public string BonusText(int level)
        {
            level = Mathf.Clamp(level, 0, Progress.MaxSkillLevel);
            if (level <= 0) return "LOCKED";
            float value = valuePerLevel * level;
            switch (effect)
            {
                case CharacterSkillEffect.Damage:
                    return "+" + Percent(value) + " damage";
                case CharacterSkillEffect.MaxHealth:
                    return "+" + Percent(value) + " health";
                case CharacterSkillEffect.MoveSpeed:
                    return "+" + Percent(value) + " speed";
                case CharacterSkillEffect.AttackSpeed:
                    return "-" + Percent(value) + " cooldown";
                case CharacterSkillEffect.Stamina:
                    return "+" + Percent(value) + " Ward Flow recharge";
                case CharacterSkillEffect.Regen:
                    return "+" + Percent(value) + " regen";
                case CharacterSkillEffect.AutoAim:
                    return "+" + value.ToString("0.0") + " aim";
                case CharacterSkillEffect.Respawn:
                    return "-" + Percent(value) + " respawn";
                default:
                    return "";
            }
        }

        static string Percent(float value)
        {
            return Mathf.RoundToInt(value * 100f) + "%";
        }
    }

    public static class CharacterSkillBook
    {
        static readonly CharacterSkillDefinition[] Bastion =
        {
            new CharacterSkillDefinition("iron_guard", "Iron Guard", "Maximum health is increased — hold the line longer.",
                CharacterSkillEffect.MaxHealth, 0.08f),
            new CharacterSkillDefinition("shield_drive", "Shield Drive", "Dash recharges faster, letting the vanguard lunge into the zone more often.",
                CharacterSkillEffect.Stamina, 0.07f),
            new CharacterSkillDefinition("bulwark_recovery", "Bulwark Recovery", "Out-of-combat regen restores more between engages.",
                CharacterSkillEffect.Regen, 0.03f),
        };

        static readonly CharacterSkillDefinition[] Thorn =
        {
            new CharacterSkillDefinition("longshot", "Longshot", "Auto-aim reaches farther.",
                CharacterSkillEffect.AutoAim, 0.8f),
            new CharacterSkillDefinition("earthen_draw", "Heavy Draw", "Arrow damage is increased.",
                CharacterSkillEffect.Damage, 0.06f),
            new CharacterSkillDefinition("ranger_pace", "Ranger Pace", "Movement speed is increased.",
                CharacterSkillEffect.MoveSpeed, 0.04f),
        };

        static readonly CharacterSkillDefinition[] FrostWizard =
        {
            new CharacterSkillDefinition("ice_armor", "Ice Armor", "Maximum health is increased.",
                CharacterSkillEffect.MaxHealth, 0.065f),
            new CharacterSkillDefinition("winter_respite", "Winter Respite", "Out-of-combat recovery is improved.",
                CharacterSkillEffect.Regen, 0.025f),
            new CharacterSkillDefinition("cold_focus", "Cold Focus", "Auto-aim reaches farther.",
                CharacterSkillEffect.AutoAim, 0.7f),
        };

        static readonly CharacterSkillDefinition[] DefaultMelee =
        {
            new CharacterSkillDefinition("martial_training", "Martial Training", "Damage is increased.",
                CharacterSkillEffect.Damage, 0.05f),
            new CharacterSkillDefinition("battle_vigor", "Battle Vigor", "Maximum health is increased.",
                CharacterSkillEffect.MaxHealth, 0.05f),
            new CharacterSkillDefinition("quick_feet", "Quick Feet", "Movement speed is increased.",
                CharacterSkillEffect.MoveSpeed, 0.04f),
        };

        static readonly CharacterSkillDefinition[] DefaultRanged =
        {
            new CharacterSkillDefinition("sharpshooter", "Sharpshooter", "Projectile damage is increased.",
                CharacterSkillEffect.Damage, 0.05f),
            new CharacterSkillDefinition("steady_aim", "Steady Aim", "Auto-aim reaches farther.",
                CharacterSkillEffect.AutoAim, 0.7f),
            new CharacterSkillDefinition("quick_cast", "Quick Cast", "Attacks recover faster.",
                CharacterSkillEffect.AttackSpeed, 0.05f),
        };

        public static CharacterSkillDefinition[] For(BrawlerDefinition def)
        {
            if (def == null) return DefaultMelee;
            switch (def.id)
            {
                case "bastion": return Bastion;
                case "thorn": return Thorn;
                case "frost": return FrostWizard;
                default: return def.projectilePrefab != null ? DefaultRanged : DefaultMelee;
            }
        }

        public static void ApplyProgression(BrawlerController ctrl, BrawlerDefinition def)
        {
            if (ctrl == null || def == null) return;
            var skills = For(def);
            for (int i = 0; i < skills.Length; i++)
            {
                var skill = skills[i];
                int level = Progress.GetSkillLevel(def.id, skill.id);
                if (level <= 0) continue;
                Apply(ctrl, skill, level);
            }
        }

        static void Apply(BrawlerController ctrl, CharacterSkillDefinition skill, int level)
        {
            float value = skill.valuePerLevel * Mathf.Clamp(level, 0, Progress.MaxSkillLevel);
            switch (skill.effect)
            {
                case CharacterSkillEffect.Damage:
                    ctrl.attackDamage = Mathf.Round(ctrl.attackDamage * (1f + value));
                    break;
                case CharacterSkillEffect.MaxHealth:
                {
                    var health = ctrl.GetComponent<Health>();
                    if (health != null) health.SetMax(Mathf.Round(health.Max * (1f + value)));
                    break;
                }
                case CharacterSkillEffect.MoveSpeed:
                    ctrl.moveSpeed *= 1f + value;
                    break;
                case CharacterSkillEffect.AttackSpeed:
                    ctrl.attackCooldown *= Mathf.Max(0.55f, 1f - value);
                    ctrl.attackHitDelay *= Mathf.Max(0.65f, 1f - value * 0.45f);
                    break;
                case CharacterSkillEffect.Stamina:
                    ctrl.staminaRegenPerSec *= 1f + value;
                    break;
                case CharacterSkillEffect.Regen:
                    ctrl.healthRegenTickFraction += value;
                    ctrl.healthRegenDelay = Mathf.Max(1.2f, ctrl.healthRegenDelay - 0.2f * level);
                    break;
                case CharacterSkillEffect.AutoAim:
                    ctrl.autoAimRange += value;
                    break;
                case CharacterSkillEffect.Respawn:
                    ctrl.respawnDelayMultiplier *= Mathf.Max(0.65f, 1f - value);
                    break;
            }
        }
    }
}
