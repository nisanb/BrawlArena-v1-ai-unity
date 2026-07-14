using System;
using System.Linq;
using Invector.vCharacterController.AI;
using UnityEngine;
using UnityEngine.AI;
using Object = UnityEngine.Object;

namespace BrawlArena
{
    /// <summary>
    /// Transactionally instantiates a builder-owned production AI variant.
    /// AIBrawler remains the tactical producer, the child NavMeshAgent remains
    /// planning-only, and Brawl keeps identity, health, combat, and lifecycle.
    /// </summary>
    public sealed class InvectorAIBrawlerCharacterAssembler : IBrawlerCharacterAssembler
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
                    "The production Invector AI assembler does not support human players.");
            }
            if (string.IsNullOrWhiteSpace(definition.id))
                throw new InvalidOperationException(
                    "A production Invector AI definition requires a roster id.");
            if (definition.invectorAIPrefab == null)
            {
                throw new InvalidOperationException(
                    "The production Invector AI prefab is not assigned.");
            }

            definition.EnsureSuperConfiguration();
            GameObject prefab = definition.invectorAIPrefab;
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
                AIBrawler ai = RequireExactlyOneRoot<AIBrawler>(actor);
                InvectorBrawlerNavigation navigation =
                    RequireExactlyOneRoot<InvectorBrawlerNavigation>(actor);
                InvectorBrawlerMotor motor =
                    RequireExactlyOneRoot<InvectorBrawlerMotor>(actor);
                InvectorBrawlerAnimationDriver animationDriver =
                    RequireExactlyOneRoot<InvectorBrawlerAnimationDriver>(actor);
                InvectorBrawlerWeaponPresentation weaponPresentation =
                    RequireExactlyOneRoot<InvectorBrawlerWeaponPresentation>(actor);
                InvectorAIRuntimeGate runtimeGate =
                    RequireExactlyOneRoot<InvectorAIRuntimeGate>(actor);

                health.SetMax(Mathf.Round(definition.maxHealth * statMultiplier));
                controller.SetMotor(motor);
                BrawlerCharacterAssembly.ConfigureFacade(
                    controller, definition, team, statMultiplier);
                controller.SetAnimationDriver(animationDriver);
                controller.SetWeaponPresentation(weaponPresentation);
                ai.SetNavigation(navigation);
                RpgCharacterVisuals.Attach(controller);

                if (!runtimeGate.IsDormantConfigured)
                {
                    throw new InvalidOperationException(
                        "The production Invector AI runtime gate is not in its builder-owned dormant posture.");
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
            {
                throw new InvalidOperationException(
                    "The production Invector AI prefab is not assigned.");
            }
            if (prefab.activeSelf)
            {
                throw new InvalidOperationException(
                    "The production Invector AI prefab must remain inactive until its runtime gate opens.");
            }

            InvectorBrawlerPrefabIdentity identity =
                RequireExactlyOneRoot<InvectorBrawlerPrefabIdentity>(prefab);
            if (!identity.Matches(rosterId, InvectorBrawlerPrefabRole.AI))
            {
                throw new InvalidOperationException(
                    "The production Invector AI prefab identity does not match roster id '" +
                    rosterId + "'.");
            }

            RequireExactlyOneRoot<Health>(prefab);
            RequireExactlyOneRoot<BrawlerController>(prefab);
            RequireExactlyOneRoot<AIBrawler>(prefab);
            RequireExactlyOneRoot<InvectorBrawlerMotor>(prefab);
            RequireExactlyOneRoot<InvectorBrawlerAnimationDriver>(prefab);
            RequireExactlyOneRoot<InvectorBrawlerWeaponPresentation>(prefab);
            InvectorBrawlerNavigation navigation =
                RequireExactlyOneRoot<InvectorBrawlerNavigation>(prefab);
            InvectorAIRuntimeGate gate =
                RequireExactlyOneRoot<InvectorAIRuntimeGate>(prefab);

            NavMeshAgent[] agents = prefab.GetComponentsInChildren<NavMeshAgent>(true);
            if (agents.Length != 1 || agents[0].gameObject == prefab ||
                !agents[0].transform.IsChildOf(prefab.transform))
            {
                throw new InvalidOperationException(
                    "The production Invector AI prefab requires exactly one dedicated child NavMeshAgent.");
            }
            if (agents[0] != navigation.PlannerAgent || agents[0].enabled ||
                agents[0].autoTraverseOffMeshLink)
            {
                throw new InvalidOperationException(
                    "The Cinder production AI child planner is not in its transform-neutral dormant posture.");
            }
            if (!navigation.IsDormantConfigured || !gate.IsDormantConfigured)
            {
                throw new InvalidOperationException(
                    "The dedicated production AI prefab does not contain dormant navigation and runtime gates.");
            }

            MonoBehaviour[] behaviours =
                prefab.GetComponentsInChildren<MonoBehaviour>(true);
            if (prefab.GetComponentsInChildren<PlayerBrawlerInput>(true).Length != 0 ||
                prefab.GetComponentsInChildren<InvectorHumanRuntimeGate>(true).Length != 0 ||
                behaviours.Any(value =>
                    value is IBrawlerMotor && !(value is InvectorBrawlerMotor)) ||
                behaviours.Any(value =>
                    value is IBrawlerAnimationDriver &&
                    !(value is InvectorBrawlerAnimationDriver)) ||
                behaviours.Any(value =>
                    value is IBrawlerNavigation &&
                    !(value is InvectorBrawlerNavigation)) ||
                prefab.GetComponentsInChildren<CharacterController>(true).Length != 0 ||
                prefab.GetComponentsInChildren<vSimpleMeleeAI_Motor>(true).Length != 0 ||
                prefab.GetComponentsInChildren<vSimpleMeleeAI_SphereSensor>(true).Length != 0 ||
                prefab.GetComponentsInChildren<vSimpleMeleeAI_WeaponsControl>(true).Length != 0)
            {
                throw new InvalidOperationException(
                    "The production Invector AI prefab contains a competing human, controller, CharacterController, or vendor AI authority.");
            }
        }

        static T RequireExactlyOneRoot<T>(GameObject root) where T : Component
        {
            if (root == null) throw new ArgumentNullException(nameof(root));
            T[] components = root.GetComponentsInChildren<T>(true);
            if (components.Length != 1 || components[0].gameObject != root)
            {
                throw new InvalidOperationException(
                    "The production Invector AI prefab requires exactly one root " +
                    typeof(T).Name + ".");
            }
            return components[0];
        }

        static void DestroyFailedAssembly(GameObject actor)
        {
            if (actor == null) return;
            if (actor.activeSelf) actor.SetActive(false);
            if (Application.isPlaying) Object.Destroy(actor);
            else Object.DestroyImmediate(actor);
        }
    }
}
