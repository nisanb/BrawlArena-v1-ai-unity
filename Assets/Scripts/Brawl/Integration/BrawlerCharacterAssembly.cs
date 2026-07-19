using System;
using UnityEngine;
using UnityEngine.AI;
using Object = UnityEngine.Object;

namespace BrawlArena
{
    /// <summary>
    /// Selects the required production character role. Default derives the
    /// role from the caller's human/bot flag; explicit values remain useful
    /// to builders and focused validation. Every roster actor is backed by
    /// the generated souls-style heavy prefabs.
    /// </summary>
    public enum BrawlerAssemblyContext
    {
        Default = 0,
        ProductionHuman = 1,
        ProductionAI = 2,
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
    /// actor construction begins. Roster actors carry a HeavyBrawlerIdentity
    /// on their prefab root; anything else is a configuration error.
    /// </summary>
    public static class BrawlerCharacterAssembly
    {
        static readonly IBrawlerCharacterAssembler HeavyHumanAssembler =
            new HeavyHumanBrawlerCharacterAssembler();
        static readonly IBrawlerCharacterAssembler HeavyAIAssembler =
            new HeavyAIBrawlerCharacterAssembler();

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
            BrawlerController controller = assembler.Assemble(
                definition, team, position, asHumanPlayer, statMultiplier);
            AttachMotionFlourish(controller);
            return controller;
        }

        /// <summary>
        /// Presentation-only procedural motion (run bob, lean, roll burst,
        /// Super windup, contact blob shadow) attached at the single choke
        /// point both human and AI production assembly pass through.
        /// Dormant-safe: Configure resolves the model's visual bone anchor
        /// and no-ops the component if one cannot be found, and this only
        /// ever runs on a live (already-activated) Play mode actor.
        /// </summary>
        static void AttachMotionFlourish(BrawlerController controller)
        {
            if (controller == null || !Application.isPlaying) return;
            if (controller.GetComponent<BrawlerMotionFlourish>() != null) return;

            BrawlerMotionFlourish flourish = controller.gameObject.AddComponent<BrawlerMotionFlourish>();
            flourish.Configure(controller);
        }

        static IBrawlerCharacterAssembler Resolve(
            BrawlerDefinition definition,
            bool asHumanPlayer,
            BrawlerAssemblyContext context)
        {
            if (definition == null)
                throw new ArgumentNullException(nameof(definition));
            if (string.IsNullOrWhiteSpace(definition.id))
                throw new InvalidOperationException(
                    "A production definition requires a roster id.");

            switch (context)
            {
                case BrawlerAssemblyContext.Default:
                    break;
                case BrawlerAssemblyContext.ProductionHuman:
                    if (!asHumanPlayer)
                    {
                        throw new NotSupportedException(
                            "The production human context is human-only.");
                    }
                    break;
                case BrawlerAssemblyContext.ProductionAI:
                    if (asHumanPlayer)
                    {
                        throw new NotSupportedException(
                            "The production AI context is bot-only.");
                    }
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(context), context,
                        "Unknown brawler assembly context.");
            }

            if (asHumanPlayer)
            {
                RequireHeavyPrefab(definition.humanBodyPrefab,
                    definition.id, "production-human");
                return HeavyHumanAssembler;
            }

            RequireHeavyPrefab(definition.aiBodyPrefab,
                definition.id, "production-AI");
            return HeavyAIAssembler;
        }

        static void RequireHeavyPrefab(GameObject prefab, string rosterId, string role)
        {
            if (prefab == null)
                throw new InvalidOperationException(
                    "The " + role + " prefab is not assigned for '" + rosterId + "'.");
            if (prefab.GetComponent<HeavyBrawlerIdentity>() == null)
            {
                throw new InvalidOperationException(
                    "The " + role + " prefab for '" + rosterId +
                    "' is not a generated heavy hero prefab.");
            }
        }

        /// <summary>
        /// Applies gameplay-facing data to the production compatibility facade.
        /// Concrete role assemblers own topology and activation.
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
            controller.wardStepDistance = definition.wardStepDistance;
            controller.wardStepCost = definition.wardStepCost;
            controller.meleeArcDegrees = definition.meleeArcDegrees;
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
            controller.ConfigureProjectileReadability(definition.projectileReadability);
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

        /// <summary>
        /// Authored presentation timing for the generated souls-style animator
        /// controllers, derived from the same roster data combat already uses
        /// so animation contact frames and damage frames share one source.
        /// </summary>
        internal static HeavyAnimationProfile BuildHeavyAnimationProfile(
            BrawlerDefinition definition)
        {
            if (definition == null) throw new ArgumentNullException(nameof(definition));
            return new HeavyAnimationProfile
            {
                primaryImpactDelay = definition.hitDelay,
                superImpactDelay = definition.hitDelay,
                // The body must finish its fall and rest on the ground for a
                // beat before despawning, otherwise the corpse vanishes
                // mid-fall and the KO reads as a glitch.
                dieVisibleSeconds = 1.35f,
                attackStateSpeed = ResolveHeavyAttackStateSpeed(definition.id),
                lungeImpulse = ResolveHeavyLungeImpulse(definition.id),
            };
        }

        /// <summary>
        /// Attack state speed per hero: below 1 reads heavier. Bastion's
        /// greatsword-cadence swing plays slowest; thorn's draw is the
        /// snappiest but still committed.
        /// </summary>
        public static float ResolveHeavyAttackStateSpeed(string heroId)
        {
            if (string.Equals(heroId, "bastion", StringComparison.Ordinal)) return 0.95f;
            if (string.Equals(heroId, "thorn", StringComparison.Ordinal)) return 1.1f;
            return 1f;
        }

        /// <summary>
        /// Melee step-in impulse per hero; ranged/caster kits author zero so
        /// their committed shots stay planted.
        /// </summary>
        public static float ResolveHeavyLungeImpulse(string heroId)
        {
            return string.Equals(heroId, "bastion", StringComparison.Ordinal)
                ? 5.5f
                : 0f;
        }

        /// <summary>
        /// Movement mass per hero: the vanguard is ponderous, the archer
        /// nimble, the caster in between. Turn rates stay below the aim-snap
        /// path so committed shots always out-rank travel facing.
        /// </summary>
        public static HeavyMotorProfile BuildHeavyMotorProfile(string heroId)
        {
            if (string.Equals(heroId, "bastion", StringComparison.Ordinal))
            {
                return new HeavyMotorProfile
                {
                    weight = 1.25f,
                    acceleration = 18f,
                    deceleration = 24f,
                    turnRateDegreesPerSecond = 480f,
                    impulseDamping = 5.5f,
                };
            }
            if (string.Equals(heroId, "thorn", StringComparison.Ordinal))
            {
                return new HeavyMotorProfile
                {
                    weight = 0.9f,
                    acceleration = 24f,
                    deceleration = 30f,
                    turnRateDegreesPerSecond = 620f,
                    impulseDamping = 5.5f,
                };
            }
            return new HeavyMotorProfile
            {
                weight = 1f,
                acceleration = 20f,
                deceleration = 26f,
                turnRateDegreesPerSecond = 540f,
                impulseDamping = 5.5f,
            };
        }
    }

    /// <summary>
    /// Shared requirements for the generated heavy prefabs. These prefabs
    /// ship as plain ACTIVE roots whose components are safe on Awake, so
    /// assembly is Instantiate + wire + enable with no transactional runtime
    /// gate.
    /// </summary>
    static class HeavyBrawlerAssemblyRequirements
    {
        public static HeavyBrawlerIdentity RequireIdentity(
            GameObject prefab, string rosterId, bool humanVariant)
        {
            if (prefab == null)
                throw new InvalidOperationException(
                    "The heavy production prefab is not assigned.");
            if (!prefab.activeSelf)
            {
                throw new InvalidOperationException(
                    "The heavy production prefab must be an active root; no dormancy gate exists on this path.");
            }

            HeavyBrawlerIdentity identity =
                RequireExactlyOneRoot<HeavyBrawlerIdentity>(prefab);
            if (!string.Equals(identity.heroId, rosterId, StringComparison.Ordinal) ||
                identity.isHumanVariant != humanVariant)
            {
                throw new InvalidOperationException(
                    "The heavy production prefab identity does not match roster id '" +
                    rosterId + "' as " + (humanVariant ? "Human" : "AI") + ".");
            }
            return identity;
        }

        public static T RequireExactlyOneRoot<T>(GameObject root) where T : Component
        {
            if (root == null) throw new ArgumentNullException(nameof(root));
            T[] components = root.GetComponentsInChildren<T>(true);
            if (components.Length != 1 || components[0].gameObject != root)
            {
                throw new InvalidOperationException(
                    "The heavy production prefab requires exactly one root " +
                    typeof(T).Name + ".");
            }
            return components[0];
        }

        public static void DestroyFailedAssembly(GameObject actor)
        {
            if (actor == null) return;
            if (actor.activeSelf) actor.SetActive(false);
            if (Application.isPlaying) Object.Destroy(actor);
            else Object.DestroyImmediate(actor);
        }
    }

    /// <summary>
    /// Assembles a generated heavy human actor: instantiate the active
    /// prefab, wire the facade's motor/animation/weapon contracts, apply the
    /// per-hero movement mass and animation profiles, then enable player
    /// input last.
    /// </summary>
    public sealed class HeavyHumanBrawlerCharacterAssembler : IBrawlerCharacterAssembler
    {
        public BrawlerController Assemble(
            BrawlerDefinition definition,
            TeamId team,
            Vector3 position,
            bool asHumanPlayer,
            float statMultiplier)
        {
            if (definition == null) throw new ArgumentNullException(nameof(definition));
            if (!asHumanPlayer)
            {
                throw new NotSupportedException(
                    "The heavy human assembler does not support bots.");
            }
            if (string.IsNullOrWhiteSpace(definition.id))
                throw new InvalidOperationException(
                    "A heavy human definition requires a roster id.");
            if (definition.humanBodyPrefab == null)
                throw new InvalidOperationException(
                    "The heavy human prefab is not assigned.");

            definition.EnsureSuperConfiguration();
            GameObject prefab = definition.humanBodyPrefab;
            ValidateDedicatedPrefab(prefab, definition.id);

            GameObject actor = null;
            try
            {
                actor = Object.Instantiate(
                    prefab,
                    position,
                    Quaternion.Euler(0f, team == TeamId.Blue ? 0f : 180f, 0f));
                actor.name = definition.displayName;

                Health health =
                    HeavyBrawlerAssemblyRequirements.RequireExactlyOneRoot<Health>(actor);
                BrawlerController controller =
                    HeavyBrawlerAssemblyRequirements.RequireExactlyOneRoot<BrawlerController>(actor);
                PlayerBrawlerInput input =
                    HeavyBrawlerAssemblyRequirements.RequireExactlyOneRoot<PlayerBrawlerInput>(actor);
                HeavyBrawlerMotor motor =
                    HeavyBrawlerAssemblyRequirements.RequireExactlyOneRoot<HeavyBrawlerMotor>(actor);
                HeavyAnimationDriver animationDriver =
                    HeavyBrawlerAssemblyRequirements.RequireExactlyOneRoot<HeavyAnimationDriver>(actor);
                HeavyWeaponPresentation weaponPresentation =
                    HeavyBrawlerAssemblyRequirements.RequireExactlyOneRoot<HeavyWeaponPresentation>(actor);

                health.SetMax(Mathf.Round(definition.maxHealth * statMultiplier));
                motor.ConfigureProfile(
                    BrawlerCharacterAssembly.BuildHeavyMotorProfile(definition.id));
                controller.SetMotor(motor);
                BrawlerCharacterAssembly.ConfigureFacade(
                    controller, definition, team, statMultiplier);
                controller.SetAnimationDriver(animationDriver);
                controller.SetWeaponPresentation(weaponPresentation);
                animationDriver.Configure(
                    BrawlerCharacterAssembly.BuildHeavyAnimationProfile(definition));
                RpgCharacterVisuals.Attach(controller);

                // Input is deliberately last: no player intent can enter until
                // every facade contract has been wired.
                input.enabled = true;
                return controller;
            }
            catch
            {
                HeavyBrawlerAssemblyRequirements.DestroyFailedAssembly(actor);
                throw;
            }
        }

        static void ValidateDedicatedPrefab(GameObject prefab, string rosterId)
        {
            HeavyBrawlerAssemblyRequirements.RequireIdentity(prefab, rosterId, true);
            HeavyBrawlerAssemblyRequirements.RequireExactlyOneRoot<Health>(prefab);
            HeavyBrawlerAssemblyRequirements.RequireExactlyOneRoot<BrawlerController>(prefab);
            HeavyBrawlerAssemblyRequirements.RequireExactlyOneRoot<PlayerBrawlerInput>(prefab);
            HeavyBrawlerAssemblyRequirements.RequireExactlyOneRoot<HeavyBrawlerMotor>(prefab);
            HeavyBrawlerAssemblyRequirements.RequireExactlyOneRoot<HeavyAnimationDriver>(prefab);
            HeavyBrawlerAssemblyRequirements.RequireExactlyOneRoot<HeavyWeaponPresentation>(prefab);

            if (prefab.GetComponentsInChildren<AIBrawler>(true).Length != 0 ||
                prefab.GetComponentsInChildren<CharacterController>(true).Length != 0)
            {
                throw new InvalidOperationException(
                    "The heavy human prefab contains a competing AI or CharacterController authority.");
            }
        }
    }

    /// <summary>
    /// Assembles a generated heavy AI actor: instantiate the active prefab,
    /// wire the facade contracts and the transform-neutral child planner,
    /// then enable the tactical producer last.
    /// </summary>
    public sealed class HeavyAIBrawlerCharacterAssembler : IBrawlerCharacterAssembler
    {
        public BrawlerController Assemble(
            BrawlerDefinition definition,
            TeamId team,
            Vector3 position,
            bool asHumanPlayer,
            float statMultiplier)
        {
            if (definition == null) throw new ArgumentNullException(nameof(definition));
            if (asHumanPlayer)
            {
                throw new NotSupportedException(
                    "The heavy AI assembler does not support human players.");
            }
            if (string.IsNullOrWhiteSpace(definition.id))
                throw new InvalidOperationException(
                    "A heavy AI definition requires a roster id.");
            if (definition.aiBodyPrefab == null)
                throw new InvalidOperationException(
                    "The heavy AI prefab is not assigned.");

            definition.EnsureSuperConfiguration();
            GameObject prefab = definition.aiBodyPrefab;
            ValidateDedicatedPrefab(prefab, definition.id);

            GameObject actor = null;
            try
            {
                actor = Object.Instantiate(
                    prefab,
                    position,
                    Quaternion.Euler(0f, team == TeamId.Blue ? 0f : 180f, 0f));
                actor.name = definition.displayName;

                Health health =
                    HeavyBrawlerAssemblyRequirements.RequireExactlyOneRoot<Health>(actor);
                BrawlerController controller =
                    HeavyBrawlerAssemblyRequirements.RequireExactlyOneRoot<BrawlerController>(actor);
                AIBrawler ai =
                    HeavyBrawlerAssemblyRequirements.RequireExactlyOneRoot<AIBrawler>(actor);
                HeavyBrawlerNavigation navigation =
                    HeavyBrawlerAssemblyRequirements.RequireExactlyOneRoot<HeavyBrawlerNavigation>(actor);
                HeavyBrawlerMotor motor =
                    HeavyBrawlerAssemblyRequirements.RequireExactlyOneRoot<HeavyBrawlerMotor>(actor);
                HeavyAnimationDriver animationDriver =
                    HeavyBrawlerAssemblyRequirements.RequireExactlyOneRoot<HeavyAnimationDriver>(actor);
                HeavyWeaponPresentation weaponPresentation =
                    HeavyBrawlerAssemblyRequirements.RequireExactlyOneRoot<HeavyWeaponPresentation>(actor);

                health.SetMax(Mathf.Round(definition.maxHealth * statMultiplier));
                motor.ConfigureProfile(
                    BrawlerCharacterAssembly.BuildHeavyMotorProfile(definition.id));
                controller.SetMotor(motor);
                BrawlerCharacterAssembly.ConfigureFacade(
                    controller, definition, team, statMultiplier);
                controller.SetAnimationDriver(animationDriver);
                controller.SetWeaponPresentation(weaponPresentation);
                animationDriver.Configure(
                    BrawlerCharacterAssembly.BuildHeavyAnimationProfile(definition));
                ai.SetNavigation(navigation);
                RpgCharacterVisuals.Attach(controller);

                navigation.enabled = true;
                // Planning simulation exists only in Play mode; edit-mode
                // assembly leaves the planner closed (navigation fails closed
                // as not-ready, and the AI never updates outside Play mode).
                if (Application.isPlaying)
                    navigation.OpenPlanner(actor.transform.position);

                // The tactical producer is deliberately last. No AI intent can
                // enter until planning and every facade contract have opened.
                ai.enabled = true;
                return controller;
            }
            catch
            {
                HeavyBrawlerAssemblyRequirements.DestroyFailedAssembly(actor);
                throw;
            }
        }

        static void ValidateDedicatedPrefab(GameObject prefab, string rosterId)
        {
            HeavyBrawlerAssemblyRequirements.RequireIdentity(prefab, rosterId, false);
            HeavyBrawlerAssemblyRequirements.RequireExactlyOneRoot<Health>(prefab);
            HeavyBrawlerAssemblyRequirements.RequireExactlyOneRoot<BrawlerController>(prefab);
            HeavyBrawlerAssemblyRequirements.RequireExactlyOneRoot<AIBrawler>(prefab);
            HeavyBrawlerAssemblyRequirements.RequireExactlyOneRoot<HeavyBrawlerMotor>(prefab);
            HeavyBrawlerAssemblyRequirements.RequireExactlyOneRoot<HeavyAnimationDriver>(prefab);
            HeavyBrawlerAssemblyRequirements.RequireExactlyOneRoot<HeavyWeaponPresentation>(prefab);
            HeavyBrawlerNavigation navigation =
                HeavyBrawlerAssemblyRequirements.RequireExactlyOneRoot<HeavyBrawlerNavigation>(prefab);

            // The active-root convention makes HeavyBrawlerNavigation's
            // IsDormantConfigured unusable here (it requires an inactive
            // root); assert the transform-neutral planner wiring directly.
            NavMeshAgent[] agents = prefab.GetComponentsInChildren<NavMeshAgent>(true);
            if (agents.Length != 1 || agents[0].gameObject == prefab ||
                !agents[0].transform.IsChildOf(prefab.transform))
            {
                throw new InvalidOperationException(
                    "The heavy AI prefab requires exactly one dedicated child NavMeshAgent.");
            }
            if (agents[0] != navigation.PlannerAgent || agents[0].enabled ||
                agents[0].autoTraverseOffMeshLink)
            {
                throw new InvalidOperationException(
                    "The heavy AI child planner is not in its transform-neutral dormant posture.");
            }

            if (prefab.GetComponentsInChildren<PlayerBrawlerInput>(true).Length != 0 ||
                prefab.GetComponentsInChildren<CharacterController>(true).Length != 0)
            {
                throw new InvalidOperationException(
                    "The heavy AI prefab contains a competing human or CharacterController authority.");
            }
        }
    }
}
