using System;
using UnityEngine;

namespace BrawlArena
{
    /// <summary>
    /// Selects the required production Invector role. Default derives the role
    /// from the caller's human/bot flag; explicit values remain useful to
    /// builders and focused validation.
    /// </summary>
    public enum BrawlerAssemblyContext
    {
        Default = 0,
        ProductionHumanInvector = 1,
        ProductionAIInvector = 2,
    }

    /// <summary>
    /// Builds one persistent brawler root while preserving BrawlerController as
    /// the identity and game-facing compatibility facade.
    /// </summary>
    public interface IBrawlerCharacterAssembler
    {
        BrawlerController Assemble(
            BrawlerDefinition definition,
            TeamId team,
            Vector3 position,
            bool asHumanPlayer,
            float statMultiplier);
    }

    /// <summary>
    /// Resolves the one production character-controller implementation before
    /// actor construction begins. Every roster actor is now Invector-backed.
    /// </summary>
    public static class BrawlerCharacterAssembly
    {
        static readonly IBrawlerCharacterAssembler InvectorHumanAssembler =
            new InvectorHumanBrawlerCharacterAssembler();
        static readonly IBrawlerCharacterAssembler InvectorAIAssembler =
            new InvectorAIBrawlerCharacterAssembler();

        public static BrawlerController Assemble(
            BrawlerDefinition definition,
            TeamId team,
            Vector3 position,
            bool asHumanPlayer,
            float statMultiplier)
        {
            return Assemble(definition, team, position, asHumanPlayer, statMultiplier,
                BrawlerAssemblyContext.Default);
        }

        public static BrawlerController Assemble(
            BrawlerDefinition definition,
            TeamId team,
            Vector3 position,
            bool asHumanPlayer,
            float statMultiplier,
            BrawlerAssemblyContext context)
        {
            IBrawlerCharacterAssembler assembler = Resolve(
                definition, asHumanPlayer, context);
            return assembler.Assemble(definition, team, position, asHumanPlayer, statMultiplier);
        }

        static IBrawlerCharacterAssembler Resolve(
            BrawlerDefinition definition,
            bool asHumanPlayer,
            BrawlerAssemblyContext context)
        {
            if (definition == null)
                throw new ArgumentNullException(nameof(definition));

            switch (context)
            {
                case BrawlerAssemblyContext.Default:
                    return ResolveInvectorRole(definition, asHumanPlayer);
                case BrawlerAssemblyContext.ProductionHumanInvector:
                    if (!asHumanPlayer)
                    {
                        throw new NotSupportedException(
                            "The production Invector human context is human-only.");
                    }
                    if (string.IsNullOrWhiteSpace(definition.id))
                    {
                        throw new InvalidOperationException(
                            "A production Invector human definition requires a roster id.");
                    }
                    if (definition.invectorHumanPrefab == null)
                    {
                        throw new InvalidOperationException(
                            "The production Invector human prefab is not assigned.");
                    }
                    return InvectorHumanAssembler;
                case BrawlerAssemblyContext.ProductionAIInvector:
                    if (asHumanPlayer)
                    {
                        throw new NotSupportedException(
                            "The production Invector AI context is bot-only.");
                    }
                    if (string.IsNullOrWhiteSpace(definition.id))
                    {
                        throw new InvalidOperationException(
                            "A production Invector AI definition requires a roster id.");
                    }
                    if (definition.invectorAIPrefab == null)
                    {
                        throw new InvalidOperationException(
                            "The production Invector AI prefab is not assigned.");
                    }
                    return InvectorAIAssembler;
                default:
                    throw new ArgumentOutOfRangeException(nameof(context), context,
                        "Unknown brawler assembly context.");
            }
        }

        static IBrawlerCharacterAssembler ResolveInvectorRole(
            BrawlerDefinition definition,
            bool asHumanPlayer)
        {
            if (string.IsNullOrWhiteSpace(definition.id))
                throw new InvalidOperationException(
                    "An Invector production definition requires a roster id.");
            if (asHumanPlayer)
            {
                if (definition.invectorHumanPrefab == null)
                    throw new InvalidOperationException(
                        "The Invector production-human prefab is not assigned for '" +
                        definition.id + "'.");
                return InvectorHumanAssembler;
            }
            if (definition.invectorAIPrefab == null)
                throw new InvalidOperationException(
                    "The Invector production-AI prefab is not assigned for '" +
                    definition.id + "'.");
            return InvectorAIAssembler;
        }

        /// <summary>
        /// Applies gameplay-facing data to the Invector-backed compatibility
        /// facade. Concrete role assemblers own topology and activation.
        /// </summary>
        internal static void ConfigureFacade(
            BrawlerController controller,
            BrawlerDefinition definition,
            TeamId team,
            float statMultiplier)
        {
            if (controller == null) throw new ArgumentNullException(nameof(controller));
            if (definition == null) throw new ArgumentNullException(nameof(definition));

            controller.displayName = definition.displayName;
            controller.team = team;
            controller.moveSpeed = definition.moveSpeed;
            controller.attackDamage = Mathf.Round(definition.damage * statMultiplier);
            controller.attackRange = definition.attackRange;
            controller.attackRadius = definition.attackRadius;
            controller.attackCooldown = definition.cooldown;
            controller.basicAttackReloadInterval = definition.basicAttackReloadInterval > 0f
                ? definition.basicAttackReloadInterval
                : MobileCombatRules.BasicAttackReloadInterval;
            controller.ResetBasicAttackCharges();
            controller.attackHitDelay = definition.hitDelay;
            controller.attackMoveLock = definition.moveLock;
            controller.autoAimRange = definition.autoAimRange;
            controller.projectilePrefab = definition.projectilePrefab;
            controller.projectileSpeed = definition.projectileSpeed;
            controller.castVfx = definition.castVfx;
            controller.secondaryCastVfx = definition.secondaryCastVfx;
            controller.swingVfx = definition.swingVfx;
            controller.impactVfx = definition.impactVfx;
            controller.secondaryImpactVfx = definition.secondaryImpactVfx;
            controller.koVfx = definition.koVfx;
            controller.spawnVfx = definition.spawnVfx;
            controller.specialty = definition.specialty.Sanitized();
            controller.attackSfx = CombatAudioDefaults.ResolveAttack(definition.attackSfx);
            controller.hitSfx = CombatAudioDefaults.ResolveHit(definition.hitSfx);
            controller.superName = definition.superName;
            controller.superStyle = definition.superStyle;
            controller.superDamageMultiplier = definition.superDamageMultiplier;
            controller.superRange = definition.superRange;
            controller.superKnockback = definition.superKnockback;
            controller.superDashDistance = definition.superDashDistance;
            controller.superProjectileSpeed = definition.superProjectileSpeed;
            controller.superProjectileBlastRadius = definition.superProjectileBlastRadius;
            controller.superProjectilePrefab = definition.superProjectilePrefab;
            controller.superImpactVfx = definition.superImpactVfx;
            controller.superVfx = definition.superVfx != null
                ? definition.superVfx
                : definition.koVfx;
            controller.secondarySuperVfx = definition.secondarySuperVfx;
        }
    }
}
