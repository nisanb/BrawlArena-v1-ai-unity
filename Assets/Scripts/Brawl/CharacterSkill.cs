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
        static readonly CharacterSkillDefinition[] Aria =
        {
            new CharacterSkillDefinition("arcane_edge", "Arcane Edge", "Twin blades hit harder.",
                CharacterSkillEffect.Damage, 0.05f),
            new CharacterSkillDefinition("blade_tempo", "Blade Tempo", "Attacks recover faster.",
                CharacterSkillEffect.AttackSpeed, 0.06f),
            new CharacterSkillDefinition("duelist_footwork", "Duelist Footwork", "Move faster while dueling.",
                CharacterSkillEffect.MoveSpeed, 0.04f),
        };

        static readonly CharacterSkillDefinition[] Bastion =
        {
            new CharacterSkillDefinition("iron_guard", "Iron Guard", "Maximum health is increased.",
                CharacterSkillEffect.MaxHealth, 0.07f),
            new CharacterSkillDefinition("second_wind", "Second Wind", "Out-of-combat regen restores more.",
                CharacterSkillEffect.Regen, 0.025f),
            new CharacterSkillDefinition("shield_drive", "Shield Drive", "Ward Flow recharges faster.",
                CharacterSkillEffect.Stamina, 0.06f),
        };

        static readonly CharacterSkillDefinition[] Nova =
        {
            new CharacterSkillDefinition("storm_focus", "Storm Focus", "Spells recharge faster.",
                CharacterSkillEffect.AttackSpeed, 0.06f),
            new CharacterSkillDefinition("charged_bolts", "Charged Bolts", "Projectile damage is increased.",
                CharacterSkillEffect.Damage, 0.06f),
            new CharacterSkillDefinition("far_sight", "Far Sight", "Auto-aim reaches farther.",
                CharacterSkillEffect.AutoAim, 0.75f),
        };

        static readonly CharacterSkillDefinition[] Grimm =
        {
            new CharacterSkillDefinition("heavy_cleaver", "Heavy Cleaver", "Greatsword damage is increased.",
                CharacterSkillEffect.Damage, 0.07f),
            new CharacterSkillDefinition("battle_hardened", "Battle Hardened", "Maximum health is increased.",
                CharacterSkillEffect.MaxHealth, 0.06f),
            new CharacterSkillDefinition("fast_windup", "Fast Windup", "Heavy swings recover faster.",
                CharacterSkillEffect.AttackSpeed, 0.05f),
        };

        static readonly CharacterSkillDefinition[] Vex =
        {
            new CharacterSkillDefinition("shadow_step", "Shadow Step", "Movement speed is increased.",
                CharacterSkillEffect.MoveSpeed, 0.05f),
            new CharacterSkillDefinition("ambush_blades", "Ambush Blades", "Blade damage is increased.",
                CharacterSkillEffect.Damage, 0.05f),
            new CharacterSkillDefinition("quick_return", "Quick Return", "Respawn time is reduced.",
                CharacterSkillEffect.Respawn, 0.06f),
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

        static readonly CharacterSkillDefinition[] ArcaneWizard =
        {
            new CharacterSkillDefinition("arcane_mastery", "Restoration Mastery", "Life spells hit harder and trigger stronger smart-heals.",
                CharacterSkillEffect.Damage, 0.06f),
            new CharacterSkillDefinition("mana_weave", "Sanctuary Weave", "Out-of-combat recovery is improved.",
                CharacterSkillEffect.Regen, 0.025f),
            new CharacterSkillDefinition("astral_sight", "Merciful Sight", "Auto-aim reaches farther.",
                CharacterSkillEffect.AutoAim, 0.75f),
        };

        static readonly CharacterSkillDefinition[] FireWizard =
        {
            new CharacterSkillDefinition("wildfire", "Wildfire", "Fire spell damage is increased.",
                CharacterSkillEffect.Damage, 0.07f),
            new CharacterSkillDefinition("quick_kindling", "Quick Kindling", "Fire spells recover faster.",
                CharacterSkillEffect.AttackSpeed, 0.055f),
            new CharacterSkillDefinition("cinder_ward", "Cinder Ward", "Maximum health is increased.",
                CharacterSkillEffect.MaxHealth, 0.05f),
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

        static readonly CharacterSkillDefinition[] StormWizard =
        {
            new CharacterSkillDefinition("overcharge", "Overcharge", "Storm spells recover faster.",
                CharacterSkillEffect.AttackSpeed, 0.065f),
            new CharacterSkillDefinition("tailwind", "Tailwind", "Movement speed is increased.",
                CharacterSkillEffect.MoveSpeed, 0.05f),
            new CharacterSkillDefinition("lightning_rod", "Lightning Rod", "Auto-aim reaches farther.",
                CharacterSkillEffect.AutoAim, 0.8f),
        };

        static readonly CharacterSkillDefinition[] EarthWizard =
        {
            new CharacterSkillDefinition("stone_skin", "Stone Skin", "Maximum health is increased.",
                CharacterSkillEffect.MaxHealth, 0.075f),
            new CharacterSkillDefinition("fault_line", "Fault Line", "Earth spell damage is increased.",
                CharacterSkillEffect.Damage, 0.06f),
            new CharacterSkillDefinition("deep_roots", "Deep Roots", "Ward Flow recharges faster.",
                CharacterSkillEffect.Stamina, 0.07f),
        };

        static readonly CharacterSkillDefinition[] VoidWizard =
        {
            new CharacterSkillDefinition("rift_walker", "Plaguebearer", "Movement speed is increased.",
                CharacterSkillEffect.MoveSpeed, 0.055f),
            new CharacterSkillDefinition("entropy", "Virulent Mixture", "Poison spells recover faster.",
                CharacterSkillEffect.AttackSpeed, 0.055f),
            new CharacterSkillDefinition("return_from_beyond", "Toxic Renewal", "Respawn time is reduced.",
                CharacterSkillEffect.Respawn, 0.07f),
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
                case "aria": return Aria;
                case "bastion": return Bastion;
                case "nova": return Nova;
                case "grimm": return Grimm;
                case "vex": return Vex;
                case "thorn": return Thorn;
                case "arcane": return ArcaneWizard;
                case "fire": return FireWizard;
                case "frost": return FrostWizard;
                case "storm": return StormWizard;
                case "earth": return EarthWizard;
                case "void": return VoidWizard;
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
