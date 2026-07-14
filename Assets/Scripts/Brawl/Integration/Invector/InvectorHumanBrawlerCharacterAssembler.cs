using System;
using System.Linq;
using UnityEngine;
using Object = UnityEngine.Object;

namespace BrawlArena
{
    /// <summary>
    /// Transactionally activates a builder-owned, dormant production human
    /// topology. Brawl remains the identity, input, health, combat, camera, and
    /// match authority; the runtime gate opens only the approved Invector
    /// motor/animation/weapon-presentation stack.
    /// </summary>
    public sealed class InvectorHumanBrawlerCharacterAssembler : IBrawlerCharacterAssembler
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
                    "The production Invector human assembler does not support bots.");
            }
            if (string.IsNullOrWhiteSpace(definition.id))
                throw new InvalidOperationException(
                    "A production Invector human definition requires a roster id.");
            if (definition.invectorHumanPrefab == null)
                throw new InvalidOperationException(
                    "The production Invector human prefab is not assigned.");

            definition.EnsureSuperConfiguration();
            GameObject prefab = definition.invectorHumanPrefab;
            ValidateDedicatedPrefab(prefab, definition.id);

            GameObject actor = null;
            try
            {
                actor = Object.Instantiate(
                    prefab,
                    position,
                    Quaternion.Euler(0f, team == TeamId.Blue ? 0f : 180f, 0f));
                actor.name = definition.displayName;

                Health health = RequireExactlyOneRoot<Health>(actor);
                BrawlerController controller = RequireExactlyOneRoot<BrawlerController>(actor);
                RequireExactlyOneRoot<PlayerBrawlerInput>(actor);
                InvectorBrawlerMotor motor = RequireExactlyOneRoot<InvectorBrawlerMotor>(actor);
                InvectorBrawlerAnimationDriver animationDriver =
                    RequireExactlyOneRoot<InvectorBrawlerAnimationDriver>(actor);
                InvectorBrawlerWeaponPresentation weaponPresentation =
                    RequireExactlyOneRoot<InvectorBrawlerWeaponPresentation>(actor);
                InvectorHumanRuntimeGate runtimeGate =
                    RequireExactlyOneRoot<InvectorHumanRuntimeGate>(actor);

                health.SetMax(Mathf.Round(definition.maxHealth * statMultiplier));
                controller.SetMotor(motor);
                BrawlerCharacterAssembly.ConfigureFacade(
                    controller, definition, team, statMultiplier);
                controller.SetAnimationDriver(animationDriver);
                controller.SetWeaponPresentation(weaponPresentation);
                RpgCharacterVisuals.Attach(controller);

                if (!runtimeGate.IsDormantConfigured)
                {
                    throw new InvalidOperationException(
                        "The production Invector human runtime gate is not in its builder-owned dormant posture.");
                }

                runtimeGate.Activate(definition.moveSpeed);
                return controller;
            }
            catch
            {
                DestroyFailedAssembly(actor);
                throw;
            }
        }

        static void ValidateDedicatedPrefab(GameObject prefab, string rosterId)
        {
            if (prefab == null)
                throw new InvalidOperationException(
                    "The production Invector human prefab is not assigned.");
            if (prefab.activeSelf)
            {
                throw new InvalidOperationException(
                    "The production Invector human prefab must remain inactive until its runtime gate opens.");
            }

            InvectorBrawlerPrefabIdentity identity =
                RequireExactlyOneRoot<InvectorBrawlerPrefabIdentity>(prefab);
            if (!identity.Matches(rosterId, InvectorBrawlerPrefabRole.Human))
            {
                throw new InvalidOperationException(
                    "The production Invector human prefab identity does not match roster id '" +
                    rosterId + "'.");
            }

            RequireExactlyOneRoot<Health>(prefab);
            RequireExactlyOneRoot<BrawlerController>(prefab);
            RequireExactlyOneRoot<PlayerBrawlerInput>(prefab);
            RequireExactlyOneRoot<InvectorBrawlerMotor>(prefab);
            RequireExactlyOneRoot<InvectorBrawlerAnimationDriver>(prefab);
            RequireExactlyOneRoot<InvectorBrawlerWeaponPresentation>(prefab);
            InvectorHumanRuntimeGate gate =
                RequireExactlyOneRoot<InvectorHumanRuntimeGate>(prefab);
            if (!gate.IsDormantConfigured)
            {
                throw new InvalidOperationException(
                    "The dedicated production human prefab does not contain a dormant runtime gate.");
            }

            MonoBehaviour[] behaviours =
                prefab.GetComponentsInChildren<MonoBehaviour>(true);
            if (behaviours.Any(value =>
                    value is IBrawlerMotor && !(value is InvectorBrawlerMotor)) ||
                behaviours.Any(value =>
                    value is IBrawlerAnimationDriver &&
                    !(value is InvectorBrawlerAnimationDriver)) ||
                prefab.GetComponentsInChildren<AIBrawler>(true).Length != 0 ||
                prefab.GetComponentsInChildren<CharacterController>(true).Length != 0)
            {
                throw new InvalidOperationException(
                    "The production Invector human prefab contains a competing motor, animation, AI, or CharacterController authority.");
            }
        }

        static T RequireExactlyOneRoot<T>(GameObject root) where T : Component
        {
            if (root == null) throw new ArgumentNullException(nameof(root));
            T[] components = root.GetComponentsInChildren<T>(true);
            if (components.Length != 1 || components[0].gameObject != root)
            {
                throw new InvalidOperationException(
                    "The production Invector human prefab requires exactly one root " +
                    typeof(T).Name + ".");
            }
            return components[0];
        }

        static void DestroyFailedAssembly(GameObject actor)
        {
            if (actor == null) return;
            if (actor.activeSelf)
                actor.SetActive(false);
            if (Application.isPlaying) Object.Destroy(actor);
            else Object.DestroyImmediate(actor);
        }
    }
}
