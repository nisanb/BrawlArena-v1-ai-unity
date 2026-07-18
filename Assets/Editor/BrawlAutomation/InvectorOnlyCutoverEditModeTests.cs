using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Invector.vCharacterController;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.AI;

namespace BrawlArena.EditorAutomation.Tests
{
    public sealed class InvectorOnlyCutoverEditModeTests
    {
        const string CategoryName = "InvectorOnlyCutover";
        const string Phase3BLabControllerPath =
            "Assets/Scripts/Brawl/Integration/Invector/InvectorPhase3BLabController.cs";

        static readonly BindingFlags AllInstanceFields =
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

        static readonly string[] RemovedDefinitionFields =
        {
            "prefab",
            "backend",
            "animSuffix",
            "idleState",
            "runState",
            "hitState",
            "deathState",
            "victoryState",
            "attackStates",
        };

        static readonly string[] RemovedGameFlowFields =
        {
            "enableCinderHumanInvectorPilot",
            "enableCinderAIInvectorPilot",
            "invectorHumanRolloutId",
            "invectorAIRolloutId",
        };

        static readonly string[] RemovedRuntimeImplementationPaths =
        {
            "Assets/Scripts/Brawl/Integration/Legacy/LegacyBrawlerAnimationDriver.cs",
            "Assets/Scripts/Brawl/Integration/Legacy/LegacyBrawlerCharacterAssembler.cs",
            "Assets/Scripts/Brawl/Integration/Legacy/LegacyBrawlerMotor.cs",
            "Assets/Scripts/Brawl/Integration/Legacy/LegacyBrawlerNavigation.cs",
        };

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
        public void CharacterAssemblyApiContainsNoLegacyBackendRolloutOrStateNameContract()
        {
            AssertFieldsAreAbsent(typeof(BrawlerDefinition), RemovedDefinitionFields);
            AssertFieldsAreAbsent(typeof(GameFlow), RemovedGameFlowFields);

            FieldInfo[] definitionFields = typeof(BrawlerDefinition).GetFields(AllInstanceFields);
            FieldInfo[] flowFields = typeof(GameFlow).GetFields(AllInstanceFields);
            Assert.That(definitionFields.Where(IsBackendOrRolloutField), Is.Empty);
            Assert.That(flowFields.Where(IsBackendOrRolloutField), Is.Empty);

            Type removedBackend = typeof(BrawlerDefinition).Assembly.GetType(
                "BrawlArena.BrawlerBackend", false, false);
            Assert.That(removedBackend, Is.Null,
                "The retired character-controller backend selector must not remain in the runtime assembly.");
            Assert.That(Enum.GetNames(typeof(BrawlerAssemblyContext)), Is.EqualTo(new[]
            {
                nameof(BrawlerAssemblyContext.Default),
                nameof(BrawlerAssemblyContext.ProductionHumanInvector),
                nameof(BrawlerAssemblyContext.ProductionAIInvector),
            }), "The assembly context may select only an Invector role, with no compatibility aliases.");
        }

        [Test]
        [Category(CategoryName)]
        public void RuntimeLegacyCharacterControllerImplementationsArePhysicallyAbsent()
        {
            foreach (string path in RemovedRuntimeImplementationPaths)
            {
                Assert.That(File.Exists(path), Is.False,
                    "Retired runtime implementation still exists: " + path);
                Assert.That(AssetDatabase.LoadAssetAtPath<MonoScript>(path), Is.Null,
                    "Unity still resolves a retired runtime implementation: " + path);
            }

            const string legacyDirectory = "Assets/Scripts/Brawl/Integration/Legacy";
            string[] remainingScripts = Directory.Exists(legacyDirectory)
                ? Directory.GetFiles(legacyDirectory, "*.cs", SearchOption.AllDirectories)
                : Array.Empty<string>();
            Assert.That(remainingScripts, Is.Empty,
                "The runtime Legacy integration directory must contain no C# implementation files.");
        }

        [Test]
        [Category(CategoryName)]
        public void ExistingRosterAssignsAllFourExactInactiveInvectorRolePrefabs()
        {
            BrawlerDefinition[] roster = ArenaSceneBuilder.BuildRosterFromExistingAssets();

            Assert.That(roster, Has.Length.EqualTo(ExpectedRoster.Length));
            Assert.That(roster.Select(definition => definition.id),
                Is.EquivalentTo(ExpectedRoster.Select(expected => expected.RosterId)));

            foreach (ExpectedRosterEntry expected in ExpectedRoster)
            {
                BrawlerDefinition definition = roster.Single(value =>
                    string.Equals(value.id, expected.RosterId, StringComparison.Ordinal));
                GameObject human = RequireExactRosterReference(
                    definition.invectorHumanPrefab, expected.HumanPrefabPath,
                    expected.RosterId, InvectorBrawlerPrefabRole.Human);
                GameObject ai = RequireExactRosterReference(
                    definition.invectorAIPrefab, expected.AIPrefabPath,
                    expected.RosterId, InvectorBrawlerPrefabRole.AI);

                AssertHumanAuthority(human, expected.RosterId);
                AssertAIAuthority(ai, expected.RosterId);
            }
        }

        [Test]
        [Category(CategoryName)]
        public void ProductionOwnedScriptsScenesAndPrefabsContainNoRetiredControllerTokens()
        {
            var offendingFiles = new List<string>();
            foreach (string file in EnumerateProductionOwnedTextAssets())
            {
                string source = File.ReadAllText(file);
                if (source.IndexOf("LegacyBrawler", StringComparison.Ordinal) >= 0 ||
                    source.IndexOf("BrawlerBackend", StringComparison.Ordinal) >= 0)
                {
                    offendingFiles.Add(ToAssetPath(file));
                }
            }

            Assert.That(offendingFiles, Is.Empty,
                "Retired character-controller tokens remain in project-owned production assets: " +
                string.Join(", ", offendingFiles));
        }

        static void AssertHumanAuthority(GameObject prefab, string rosterId)
        {
            BrawlerController facade = RequireSingleRootComponent<BrawlerController>(prefab);
            InvectorBrawlerMotor motor = RequireExactAuthority<IBrawlerMotor, InvectorBrawlerMotor>(prefab);
            InvectorBrawlerAnimationDriver animation =
                RequireExactAuthority<IBrawlerAnimationDriver, InvectorBrawlerAnimationDriver>(prefab);
            InvectorBrawlerWeaponPresentation presentation =
                RequireExactAuthority<IBrawlerWeaponPresentation, InvectorBrawlerWeaponPresentation>(prefab);
            PlayerBrawlerInput playerInput = RequireSingleRootComponent<PlayerBrawlerInput>(prefab);
            InvectorHumanRuntimeGate gate = RequireSingleRootComponent<InvectorHumanRuntimeGate>(prefab);
            InvectorShooterMeleeInputAdapter scheduler = RequireSingleScheduler(prefab);

            Assert.That(facade.Motor, Is.SameAs(motor), rosterId + " human motor selection drifted.");
            Assert.That(facade.AnimationDriver, Is.SameAs(animation),
                rosterId + " human animation selection drifted.");
            Assert.That(facade.WeaponPresentation, Is.SameAs(presentation),
                rosterId + " human weapon presentation selection drifted.");
            AssertWeaponHierarchy(presentation, rosterId + " human");
            Assert.That(playerInput, Is.Not.Null);
            Assert.That(gate, Is.Not.Null);
            Assert.That(scheduler.MovementFeedMode, Is.EqualTo(InvectorMovementFeedMode.BufferedMotor));
            Assert.That(scheduler.ProjectMoveActionOwnedByAdapter, Is.False);
            Assert.That(FindAuthorities<IBrawlerNavigation>(prefab), Is.Empty,
                rosterId + " human prefab must not contain an AI navigation authority.");
            Assert.That(prefab.GetComponentsInChildren<AIBrawler>(true), Is.Empty);
            Assert.That(prefab.GetComponentsInChildren<InvectorAIRuntimeGate>(true), Is.Empty);
        }

        static void AssertAIAuthority(GameObject prefab, string rosterId)
        {
            BrawlerController facade = RequireSingleRootComponent<BrawlerController>(prefab);
            InvectorBrawlerMotor motor = RequireExactAuthority<IBrawlerMotor, InvectorBrawlerMotor>(prefab);
            InvectorBrawlerAnimationDriver animation =
                RequireExactAuthority<IBrawlerAnimationDriver, InvectorBrawlerAnimationDriver>(prefab);
            InvectorBrawlerWeaponPresentation presentation =
                RequireExactAuthority<IBrawlerWeaponPresentation, InvectorBrawlerWeaponPresentation>(prefab);
            InvectorBrawlerNavigation navigation =
                RequireExactAuthority<IBrawlerNavigation, InvectorBrawlerNavigation>(prefab);
            AIBrawler brain = RequireSingleRootComponent<AIBrawler>(prefab);
            InvectorAIRuntimeGate gate = RequireSingleRootComponent<InvectorAIRuntimeGate>(prefab);
            InvectorShooterMeleeInputAdapter scheduler = RequireSingleScheduler(prefab);

            Assert.That(facade.Motor, Is.SameAs(motor), rosterId + " AI motor selection drifted.");
            Assert.That(facade.AnimationDriver, Is.SameAs(animation),
                rosterId + " AI animation selection drifted.");
            Assert.That(facade.WeaponPresentation, Is.SameAs(presentation),
                rosterId + " AI weapon presentation selection drifted.");
            AssertWeaponHierarchy(presentation, rosterId + " AI");
            Assert.That(brain.Navigation, Is.SameAs(navigation),
                rosterId + " AI navigation selection drifted.");
            Assert.That(gate, Is.Not.Null);
            Assert.That(scheduler.MovementFeedMode, Is.EqualTo(InvectorMovementFeedMode.BufferedMotor));
            Assert.That(scheduler.ProjectMoveActionOwnedByAdapter, Is.False);
            Assert.That(prefab.GetComponentsInChildren<PlayerBrawlerInput>(true), Is.Empty,
                rosterId + " AI prefab must not contain the human physical input authority.");
            Assert.That(prefab.GetComponentsInChildren<InvectorHumanRuntimeGate>(true), Is.Empty);
            Assert.That(prefab.GetComponentsInChildren<NavMeshAgent>(true), Has.Length.EqualTo(1),
                rosterId + " AI prefab must contain exactly one planning agent.");
        }

        static void AssertWeaponHierarchy(
            InvectorBrawlerWeaponPresentation presentation,
            string context)
        {
            Animator animator = presentation.ConfiguredAnimator;
            Transform visualRoot = presentation.WeaponVisualRoot;
            Transform supportTarget = presentation.SupportHandTarget;
            Transform supportHint = presentation.SupportHintTarget;
            HumanBodyBones heldHand = presentation.WeaponHeldInLeftHand
                ? HumanBodyBones.LeftHand
                : HumanBodyBones.RightHand;
            Transform weaponHand = animator != null && animator.isHuman
                ? animator.GetBoneTransform(heldHand)
                : null;

            Assert.That(weaponHand, Is.Not.Null,
                context + " has no configured Humanoid weapon hand.");
            Assert.That(visualRoot, Is.Not.Null,
                context + " has no configured weapon visual root.");
            Assert.That(visualRoot.IsChildOf(weaponHand), Is.True,
                context + " weapon visual must be owned by its configured weapon hand.");
            Assert.That(supportTarget, Is.Not.Null);
            Assert.That(supportTarget.IsChildOf(visualRoot), Is.True,
                context + " support-hand target must stay inside the weapon visual.");
            Assert.That(supportHint, Is.Not.Null);
            Assert.That(supportHint.IsChildOf(visualRoot), Is.True,
                context + " support hint must stay inside the weapon visual.");
        }

        static GameObject RequireExactRosterReference(
            GameObject assignedPrefab,
            string expectedPath,
            string rosterId,
            InvectorBrawlerPrefabRole role)
        {
            GameObject expectedPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(expectedPath);
            Assert.That(expectedPrefab, Is.Not.Null, "Missing generated Invector prefab: " + expectedPath);
            Assert.That(assignedPrefab, Is.SameAs(expectedPrefab),
                rosterId + " " + role + " roster reference must be the exact builder-owned asset.");
            Assert.That(AssetDatabase.GetAssetPath(assignedPrefab), Is.EqualTo(expectedPath));
            Assert.That(assignedPrefab.activeSelf, Is.False,
                rosterId + " " + role + " prefab asset must remain inactive.");

            InvectorBrawlerPrefabIdentity[] identities =
                assignedPrefab.GetComponents<InvectorBrawlerPrefabIdentity>();
            Assert.That(identities, Has.Length.EqualTo(1));
            Assert.That(identities[0].Matches(rosterId, role), Is.True,
                rosterId + " " + role + " identity does not match its roster assignment.");

            int missingScripts = assignedPrefab.GetComponentsInChildren<Transform>(true)
                .Sum(value => GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(value.gameObject));
            Assert.That(missingScripts, Is.Zero,
                rosterId + " " + role + " prefab contains missing MonoBehaviour references.");
            return assignedPrefab;
        }

        static TConcrete RequireExactAuthority<TAuthority, TConcrete>(GameObject prefab)
            where TConcrete : MonoBehaviour
        {
            TAuthority[] authorities = FindAuthorities<TAuthority>(prefab);
            Assert.That(authorities, Has.Length.EqualTo(1),
                prefab.name + " must contain exactly one " + typeof(TAuthority).Name + " authority. Found: " +
                string.Join(", ", authorities.Select(value => value.GetType().FullName)));
            Assert.That(authorities[0].GetType(), Is.EqualTo(typeof(TConcrete)),
                prefab.name + " selected a competing " + typeof(TAuthority).Name + " implementation.");

            TConcrete concrete = prefab.GetComponent<TConcrete>();
            Assert.That(concrete, Is.Not.Null,
                prefab.name + " must keep its " + typeof(TAuthority).Name + " authority on the root.");
            Assert.That(authorities[0], Is.SameAs(concrete));
            return concrete;
        }

        static TAuthority[] FindAuthorities<TAuthority>(GameObject prefab)
        {
            return prefab.GetComponentsInChildren<MonoBehaviour>(true)
                .Where(component => component is TAuthority)
                .Cast<TAuthority>()
                .ToArray();
        }

        static T RequireSingleRootComponent<T>(GameObject prefab) where T : Component
        {
            T[] components = prefab.GetComponents<T>();
            Assert.That(components, Has.Length.EqualTo(1),
                prefab.name + " must contain exactly one root " + typeof(T).Name + ".");
            return components[0];
        }

        static InvectorShooterMeleeInputAdapter RequireSingleScheduler(GameObject prefab)
        {
            vThirdPersonInput[] invectorInputs =
                prefab.GetComponentsInChildren<vThirdPersonInput>(true);
            Assert.That(invectorInputs, Has.Length.EqualTo(1),
                prefab.name + " must contain exactly one Invector input/scheduler authority.");
            Assert.That(invectorInputs[0].GetType(), Is.EqualTo(typeof(InvectorShooterMeleeInputAdapter)));
            Assert.That(invectorInputs[0].gameObject, Is.SameAs(prefab));
            return (InvectorShooterMeleeInputAdapter)invectorInputs[0];
        }

        static void AssertFieldsAreAbsent(Type type, IEnumerable<string> fieldNames)
        {
            foreach (string fieldName in fieldNames)
            {
                Assert.That(type.GetField(fieldName, AllInstanceFields), Is.Null,
                    type.Name + " still exposes retired serialized field '" + fieldName + "'.");
            }
        }

        static bool IsBackendOrRolloutField(FieldInfo field)
        {
            return field.Name.IndexOf("backend", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   field.Name.IndexOf("rollout", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        static IEnumerable<string> EnumerateProductionOwnedTextAssets()
        {
            foreach (string file in EnumerateFiles("Assets/Scripts", ".cs"))
            {
                string assetPath = ToAssetPath(file);
                if (!string.Equals(assetPath, Phase3BLabControllerPath, StringComparison.Ordinal))
                    yield return file;
            }

            string[] assetRoots =
            {
                "Assets/Scenes",
                "Assets/Prefabs",
                "Assets/Resources",
                "Assets/Generated",
                "Assets/BrawlArena",
            };
            foreach (string root in assetRoots)
            {
                foreach (string file in EnumerateFiles(root, ".unity", ".prefab"))
                    yield return file;
            }
        }

        static IEnumerable<string> EnumerateFiles(string root, params string[] extensions)
        {
            if (!Directory.Exists(root)) yield break;
            foreach (string file in Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories))
            {
                if (extensions.Contains(Path.GetExtension(file), StringComparer.OrdinalIgnoreCase))
                    yield return file;
            }
        }

        static string ToAssetPath(string path)
        {
            string fullPath = Path.GetFullPath(path).Replace('\\', '/');
            string projectRoot = Path.GetFullPath(".").Replace('\\', '/').TrimEnd('/');
            return fullPath.StartsWith(projectRoot + "/", StringComparison.OrdinalIgnoreCase)
                ? fullPath.Substring(projectRoot.Length + 1)
                : path.Replace('\\', '/');
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
