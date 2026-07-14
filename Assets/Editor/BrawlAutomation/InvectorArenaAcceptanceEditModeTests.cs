using System;
using System.Linq;
using Invector.vCharacterController;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.SceneManagement;

namespace BrawlArena.EditorAutomation.Tests
{
    public sealed class InvectorArenaAcceptanceEditModeTests
    {
        const string CategoryName = "InvectorArenaAcceptance";
        const string ArenaScenePath = "Assets/Scenes/Arena.unity";

        static readonly ExpectedRosterEntry[] ExpectedRoster =
        {
            new ExpectedRosterEntry(
                "fire",
                InvectorMigrationPilotBuilder.ProductionHumanPrefabPath,
                InvectorMigrationPilotBuilder.ProductionAIPrefabPath),
            new ExpectedRosterEntry(
                InvectorRimeMigrationBuilder.RosterId,
                InvectorRimeMigrationBuilder.ProductionHumanPrefabPath,
                InvectorRimeMigrationBuilder.ProductionAIPrefabPath),
            new ExpectedRosterEntry(
                InvectorTempestMigrationBuilder.RosterId,
                InvectorTempestMigrationBuilder.ProductionHumanPrefabPath,
                InvectorTempestMigrationBuilder.ProductionAIPrefabPath),
            new ExpectedRosterEntry(
                InvectorThornMigrationBuilder.RosterId,
                InvectorThornMigrationBuilder.ProductionHumanPrefabPath,
                InvectorThornMigrationBuilder.ProductionAIPrefabPath),
        };

        [Test]
        [Category(CategoryName)]
        [Category("InvectorOnlyCutover")]
        public void GeneratedArenaRosterClonesAllEightExactInvectorRolesWithSingleAuthorities()
        {
            Scene preview = EditorSceneManager.OpenPreviewScene(ArenaScenePath);
            try
            {
                GameFlow[] flows = preview.GetRootGameObjects()
                    .SelectMany(root => root.GetComponentsInChildren<GameFlow>(true))
                    .ToArray();
                Assert.That(flows, Has.Length.EqualTo(1),
                    "The generated Arena must contain exactly one GameFlow roster owner.");
                Assert.That(flows[0].roster, Has.Length.EqualTo(ExpectedRoster.Length));
                Assert.That(flows[0].roster.Select(definition => definition.id),
                    Is.EquivalentTo(ExpectedRoster.Select(expected => expected.RosterId)));
                Assert.That(preview.GetRootGameObjects()
                        .SelectMany(root => root.GetComponentsInChildren<BrawlerController>(true)),
                    Is.Empty,
                    "Arena must spawn its roster through GameFlow, not serialize actor authorities.");

                foreach (ExpectedRosterEntry expected in ExpectedRoster)
                {
                    BrawlerDefinition definition = flows[0].roster.Single(value =>
                        string.Equals(value.id, expected.RosterId, StringComparison.Ordinal));
                    ValidateRole(
                        preview,
                        definition.invectorHumanPrefab,
                        expected.HumanPrefabPath,
                        expected.RosterId,
                        InvectorBrawlerPrefabRole.Human);
                    ValidateRole(
                        preview,
                        definition.invectorAIPrefab,
                        expected.AIPrefabPath,
                        expected.RosterId,
                        InvectorBrawlerPrefabRole.AI);
                }
            }
            finally
            {
                if (preview.IsValid()) EditorSceneManager.ClosePreviewScene(preview);
            }
        }

        static void ValidateRole(
            Scene preview,
            GameObject assignedPrefab,
            string expectedPath,
            string rosterId,
            InvectorBrawlerPrefabRole role)
        {
            GameObject expectedPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(expectedPath);
            Assert.That(expectedPrefab, Is.Not.Null,
                "Missing builder-owned production prefab: " + expectedPath);
            Assert.That(assignedPrefab, Is.SameAs(expectedPrefab),
                rosterId + " " + role + " must reference the exact builder-owned asset.");
            Assert.That(AssetDatabase.GetAssetPath(assignedPrefab), Is.EqualTo(expectedPath));
            Assert.That(assignedPrefab.activeSelf, Is.False,
                rosterId + " " + role + " production asset must remain inactive.");

            var actor = (GameObject)PrefabUtility.InstantiatePrefab(assignedPrefab, preview);
            Assert.That(actor, Is.Not.Null);
            Assert.That(actor.activeSelf, Is.False,
                rosterId + " " + role + " clone opened before its runtime gate.");
            Assert.That(PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(actor),
                Is.EqualTo(expectedPath));
            Assert.That(actor.GetComponentsInChildren<Transform>(true)
                    .Sum(value => GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(
                        value.gameObject)),
                Is.Zero,
                rosterId + " " + role + " clone contains a missing script.");

            InvectorBrawlerPrefabIdentity identity =
                RequireSingleRoot<InvectorBrawlerPrefabIdentity>(actor);
            Assert.That(identity.Matches(rosterId, role), Is.True);
            BrawlerController facade = RequireSingleRoot<BrawlerController>(actor);
            InvectorBrawlerMotor motor =
                RequireExactAuthority<IBrawlerMotor, InvectorBrawlerMotor>(actor);
            InvectorBrawlerAnimationDriver animation =
                RequireExactAuthority<IBrawlerAnimationDriver,
                    InvectorBrawlerAnimationDriver>(actor);
            InvectorBrawlerWeaponPresentation presentation =
                RequireExactAuthority<IBrawlerWeaponPresentation,
                    InvectorBrawlerWeaponPresentation>(actor);
            Assert.That(facade.Motor, Is.SameAs(motor));
            Assert.That(facade.AnimationDriver, Is.SameAs(animation));
            Assert.That(facade.WeaponPresentation, Is.SameAs(presentation));
            Assert.That(actor.GetComponentsInChildren<CharacterController>(true), Is.Empty);

            vThirdPersonInput[] schedulers =
                actor.GetComponentsInChildren<vThirdPersonInput>(true);
            Assert.That(schedulers, Has.Length.EqualTo(1));
            Assert.That(schedulers[0].GetType(),
                Is.EqualTo(typeof(InvectorShooterMeleeInputAdapter)));
            Assert.That(schedulers[0].gameObject, Is.SameAs(actor));

            if (role == InvectorBrawlerPrefabRole.Human)
                ValidateHuman(actor);
            else
                ValidateAI(actor);
        }

        static void ValidateHuman(GameObject actor)
        {
            RequireSingleRoot<PlayerBrawlerInput>(actor);
            InvectorHumanRuntimeGate gate =
                RequireSingleRoot<InvectorHumanRuntimeGate>(actor);
            Assert.That(gate.IsDormantConfigured, Is.True);
            Assert.That(FindAuthorities<IBrawlerNavigation>(actor), Is.Empty);
            Assert.That(actor.GetComponentsInChildren<AIBrawler>(true), Is.Empty);
            Assert.That(actor.GetComponentsInChildren<InvectorAIRuntimeGate>(true), Is.Empty);
            Assert.That(actor.GetComponentsInChildren<NavMeshAgent>(true), Is.Empty);
        }

        static void ValidateAI(GameObject actor)
        {
            AIBrawler brain = RequireSingleRoot<AIBrawler>(actor);
            InvectorBrawlerNavigation navigation =
                RequireExactAuthority<IBrawlerNavigation,
                    InvectorBrawlerNavigation>(actor);
            InvectorAIRuntimeGate gate = RequireSingleRoot<InvectorAIRuntimeGate>(actor);
            Assert.That(gate.IsDormantConfigured, Is.True);
            Assert.That(brain.Navigation, Is.SameAs(navigation));
            Assert.That(actor.GetComponentsInChildren<PlayerBrawlerInput>(true), Is.Empty);
            Assert.That(actor.GetComponentsInChildren<InvectorHumanRuntimeGate>(true), Is.Empty);

            NavMeshAgent[] planners = actor.GetComponentsInChildren<NavMeshAgent>(true);
            Assert.That(planners, Has.Length.EqualTo(1));
            Assert.That(planners[0].gameObject, Is.Not.SameAs(actor));
            Assert.That(planners[0].transform.IsChildOf(actor.transform), Is.True);
            Assert.That(navigation.PlannerAgent, Is.SameAs(planners[0]));
            Assert.That(planners[0].enabled, Is.False);
            Assert.That(planners[0].autoTraverseOffMeshLink, Is.False);
        }

        static TConcrete RequireExactAuthority<TAuthority, TConcrete>(GameObject actor)
            where TConcrete : MonoBehaviour
        {
            TAuthority[] authorities = FindAuthorities<TAuthority>(actor);
            Assert.That(authorities, Has.Length.EqualTo(1),
                actor.name + " must contain exactly one " + typeof(TAuthority).Name + ".");
            Assert.That(authorities[0].GetType(), Is.EqualTo(typeof(TConcrete)));
            TConcrete concrete = RequireSingleRoot<TConcrete>(actor);
            Assert.That(authorities[0], Is.SameAs(concrete));
            return concrete;
        }

        static TAuthority[] FindAuthorities<TAuthority>(GameObject actor)
        {
            return actor.GetComponentsInChildren<MonoBehaviour>(true)
                .Where(component => component is TAuthority)
                .Cast<TAuthority>()
                .ToArray();
        }

        static T RequireSingleRoot<T>(GameObject actor) where T : Component
        {
            T[] components = actor.GetComponentsInChildren<T>(true);
            Assert.That(components, Has.Length.EqualTo(1),
                actor.name + " must contain exactly one " + typeof(T).Name + ".");
            Assert.That(components[0].gameObject, Is.SameAs(actor),
                typeof(T).Name + " must remain a root authority.");
            return components[0];
        }

        sealed class ExpectedRosterEntry
        {
            public readonly string RosterId;
            public readonly string HumanPrefabPath;
            public readonly string AIPrefabPath;

            public ExpectedRosterEntry(
                string rosterId,
                string humanPrefabPath,
                string aiPrefabPath)
            {
                RosterId = rosterId;
                HumanPrefabPath = humanPrefabPath;
                AIPrefabPath = aiPrefabPath;
            }
        }
    }
}
