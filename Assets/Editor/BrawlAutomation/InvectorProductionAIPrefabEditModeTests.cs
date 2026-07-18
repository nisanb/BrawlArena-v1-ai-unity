using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.AI;

namespace BrawlArena.EditorAutomation.Tests
{
    public sealed class InvectorProductionAIPrefabEditModeTests
    {
        const string BuilderSourcePath =
            "Assets/Editor/BrawlAutomation/InvectorMigrationPilotBuilder.cs";
        const string RimeBuilderSourcePath =
            "Assets/Editor/BrawlAutomation/InvectorRimeMigrationBuilder.cs";
        const string NavigationSourcePath =
            "Assets/Scripts/Brawl/Integration/Invector/InvectorBrawlerNavigation.cs";

        [Test]
        [Category("InvectorProductionAICinder")]
        public void GeneratedAIVariantIsDirectDormantPilotVariant()
        {
            GameObject prefab = RequireProductionAIPrefab();

            Assert.DoesNotThrow(() =>
                InvectorRimeMigrationBuilder.ValidateAIPrefab(prefab));
            Assert.That(prefab.activeSelf, Is.False);
            Assert.That(prefab.layer,
                Is.EqualTo(InvectorMigrationPilotBuilder.InvectorPlayerLayer));
            Assert.That(PrefabUtility.GetPrefabAssetType(prefab),
                Is.EqualTo(PrefabAssetType.Variant));

            GameObject source = PrefabUtility.GetCorrespondingObjectFromSource(prefab);
            Assert.That(source, Is.Not.Null);
            Assert.That(AssetDatabase.GetAssetPath(source),
                Is.EqualTo(InvectorRimeMigrationBuilder.PilotPrefabPath));
            Assert.That(AssetDatabase.GetAssetPath(prefab),
                Is.EqualTo(InvectorRimeMigrationBuilder.ProductionAIPrefabPath));
        }

        [Test]
        [Category("InvectorProductionAICinder")]
        public void AIVariantSelectsOneBrawlBrainPlannerAndInvectorMotor()
        {
            GameObject prefab = RequireProductionAIPrefab();
            BrawlerController facade = prefab.GetComponent<BrawlerController>();
            AIBrawler ai = prefab.GetComponent<AIBrawler>();
            InvectorBrawlerNavigation navigation =
                prefab.GetComponent<InvectorBrawlerNavigation>();
            InvectorBrawlerMotor motor = prefab.GetComponent<InvectorBrawlerMotor>();
            InvectorBrawlerAnimationDriver animation =
                prefab.GetComponent<InvectorBrawlerAnimationDriver>();
            InvectorBrawlerWeaponPresentation weapon =
                prefab.GetComponent<InvectorBrawlerWeaponPresentation>();
            InvectorAIRuntimeGate gate = prefab.GetComponent<InvectorAIRuntimeGate>();
            NavMeshAgent planner = prefab.GetComponentInChildren<NavMeshAgent>(true);

            Assert.That(prefab.GetComponents<Health>(), Has.Length.EqualTo(1));
            Assert.That(prefab.GetComponents<BrawlerController>(), Has.Length.EqualTo(1));
            Assert.That(prefab.GetComponents<AIBrawler>(), Has.Length.EqualTo(1));
            Assert.That(prefab.GetComponents<InvectorBrawlerNavigation>(),
                Has.Length.EqualTo(1));
            Assert.That(prefab.GetComponents<InvectorAIRuntimeGate>(),
                Has.Length.EqualTo(1));
            Assert.That(prefab.GetComponentsInChildren<NavMeshAgent>(true),
                Has.Length.EqualTo(1));

            Assert.That(facade.Motor, Is.SameAs(motor));
            Assert.That(motor.HasConfiguredNavigationPlanner, Is.True);
            Assert.That(facade.AnimationDriver, Is.SameAs(animation));
            Assert.That(facade.WeaponPresentation, Is.SameAs(weapon));
            Assert.That(ai.Navigation, Is.SameAs(navigation));
            Assert.That(navigation.PlannerAgent, Is.SameAs(planner));
            Assert.That(navigation.IsDormantConfigured, Is.True);
            Assert.That(gate.IsDormantConfigured, Is.True);
            Assert.That(ai.enabled, Is.False);
            Assert.That(navigation.enabled, Is.False);
            Assert.That(gate.enabled, Is.False);
        }

        [Test]
        [Category("InvectorProductionAICinder")]
        public void ChildPlannerCannotWriteTheActorTransform()
        {
            GameObject prefab = RequireProductionAIPrefab();
            NavMeshAgent planner = prefab.GetComponentInChildren<NavMeshAgent>(true);

            Assert.That(planner.transform.parent, Is.SameAs(prefab.transform));
            Assert.That(planner.name,
                Is.EqualTo(InvectorMigrationPilotBuilder.ProductionAIPlannerName));
            Assert.That(planner.transform.localPosition, Is.EqualTo(Vector3.zero));
            Assert.That(planner.transform.localRotation, Is.EqualTo(Quaternion.identity));
            Assert.That(planner.transform.localScale, Is.EqualTo(Vector3.one));
            Assert.That(planner.gameObject.activeSelf, Is.True);
            Assert.That(planner.GetComponents<Component>(), Has.Length.EqualTo(2),
                "The planner child may contain only its Transform and NavMeshAgent.");
            Assert.That(planner.enabled, Is.False);
            Assert.That(planner.autoTraverseOffMeshLink, Is.False);
            Assert.That(planner.autoRepath, Is.True);
            Assert.That(planner.autoBraking, Is.True);
            Assert.That(planner.angularSpeed, Is.Zero);
            Assert.That(planner.acceleration, Is.GreaterThanOrEqualTo(40f));

            var serializedNavigation = new SerializedObject(
                prefab.GetComponent<InvectorBrawlerNavigation>());
            Assert.That(serializedNavigation.FindProperty(
                    "plannerTransformNeutralConfigured").boolValue, Is.True,
                "Unity does not serialize NavMeshAgent update flags; the navigator must retain its builder-owned runtime firewall marker.");
        }

        [Test]
        [Category("InvectorProductionAICinder")]
        public void AIVariantContainsNoHumanLegacyVendorAIOrDamageCompetitor()
        {
            GameObject prefab = RequireProductionAIPrefab();

            Assert.That(prefab.GetComponentsInChildren<PlayerBrawlerInput>(true), Is.Empty);
            Assert.That(prefab.GetComponentsInChildren<InvectorHumanRuntimeGate>(true), Is.Empty);
            Assert.That(prefab.GetComponentsInChildren<CharacterController>(true), Is.Empty);
            Assert.That(prefab.GetComponentsInChildren<Camera>(true), Is.Empty);
            Assert.That(prefab.GetComponentsInChildren<AudioListener>(true), Is.Empty);

            string[] forbidden =
            {
                "vSimpleMeleeAI_Motor", "vSimpleMeleeAI_Animator",
                "vSimpleMeleeAI_Controller", "vSimpleMeleeAI_Companion",
                "vSimpleMeleeAI_SphereSensor", "vSimpleMeleeAI_WeaponsControl",
                "vRagdoll", "vDamageReceiver", "vHitBox", "vMeleeAttackObject",
                "vShooterWeapon", "vProjectileControl", "vObjectDamage", "vDamageSender",
                "vThirdPersonCamera", "vLockOnShooter",
            };
            string[] present = prefab.GetComponentsInChildren<Component>(true)
                .Where(component => component != null)
                .Select(component => component.GetType().Name)
                .ToArray();
            Assert.That(forbidden.Intersect(present), Is.Empty);
        }

        [Test]
        [Category("InvectorProductionAICinder")]
        public void BuilderOwnsAPreviewSceneOnlyAIRebuildPath()
        {
            string source = File.ReadAllText(BuilderSourcePath);
            string rimeSource = File.ReadAllText(RimeBuilderSourcePath);
            string safeBuild = ExtractMethodSource(
                rimeSource, nameof(InvectorRimeMigrationBuilder.BuildRimePilotAssetsSafely));
            int genericVariantStart = source.IndexOf(
                "internal static GameObject BuildProductionAIPrefab(",
                System.StringComparison.Ordinal);
            Assert.That(genericVariantStart, Is.GreaterThanOrEqualTo(0));
            string variantBuild = ExtractMethodSource(
                source.Substring(genericVariantStart), "BuildProductionAIPrefab");

            StringAssert.Contains("BuildProductionAIPrefab(", safeBuild);
            StringAssert.Contains("ValidatePilot(pilot)", safeBuild);
            StringAssert.Contains("ValidateAIPrefab", safeBuild);
            StringAssert.DoesNotContain("EditorSceneManager.NewScene", safeBuild);
            StringAssert.DoesNotContain("EditorSceneManager.OpenScene", safeBuild);
            StringAssert.DoesNotContain("EditorSceneManager.SaveScene", safeBuild);
            StringAssert.DoesNotContain("SceneManager.SetActiveScene", safeBuild);
            StringAssert.Contains("EditorSceneManager.NewPreviewScene()", variantBuild);
            StringAssert.Contains("EditorSceneManager.ClosePreviewScene", variantBuild);
            StringAssert.Contains("PrefabUtility.InstantiatePrefab", variantBuild);
            StringAssert.Contains("PrefabUtility.SaveAsPrefabAsset", variantBuild);
            StringAssert.Contains("ConfigureNavigationPlanner(navigation)", variantBuild);
            StringAssert.DoesNotContain("EditorSceneManager.NewScene", variantBuild);
            StringAssert.DoesNotContain("EditorSceneManager.OpenScene", variantBuild);
            StringAssert.DoesNotContain("EditorSceneManager.SaveScene", variantBuild);
            StringAssert.DoesNotContain("SceneManager.SetActiveScene", variantBuild);
        }

        [Test]
        [Category("InvectorProductionAICinder")]
        public void ExternalDisplacementPathResetIncludesPendingPlannerRequests()
        {
            string source = File.ReadAllText(NavigationSourcePath);
            string clearPath = ExtractMethodSource(
                source, nameof(InvectorBrawlerNavigation.ClearPath));

            StringAssert.Contains(
                "hasDestinationRequest || plannerAgent.hasPath || plannerAgent.pathPending",
                clearPath);
            StringAssert.Contains("plannerAgent.ResetPath()", clearPath);
            StringAssert.Contains("pathResetCount++", clearPath);
            StringAssert.Contains("plannerAgent.isOnOffMeshLink", clearPath);
        }

        static GameObject RequireProductionAIPrefab()
        {
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(
                InvectorRimeMigrationBuilder.ProductionAIPrefabPath);
            Assert.That(prefab, Is.Not.Null,
                "Run InvectorRimeMigrationBuilder.BuildRimePilotAssetsSafely first.");
            return prefab;
        }

        static string ExtractMethodSource(string source, string methodName)
        {
            Match declaration = Regex.Match(
                source,
                @"\b(?:public\s+|private\s+|internal\s+)?(?:static\s+)?[^;{}\r\n]+\b" +
                Regex.Escape(methodName) + @"\s*\([^;{}]*\)\s*\{");
            Assert.That(declaration.Success, Is.True,
                "Missing builder method " + methodName + ".");
            int openingBrace = source.IndexOf('{', declaration.Index);

            int depth = 0;
            for (int i = openingBrace; i < source.Length; i++)
            {
                if (source[i] == '{') depth++;
                else if (source[i] == '}') depth--;
                if (depth == 0)
                    return source.Substring(declaration.Index, i - declaration.Index + 1);
            }

            Assert.Fail("Builder method " + methodName + " has unbalanced braces.");
            return string.Empty;
        }
    }
}
