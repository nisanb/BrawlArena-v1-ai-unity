using System;
using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.SceneManagement;

namespace BrawlArena.EditorAutomation
{
    /// <summary>
    /// Builds the souls-style heavy hero stack from scratch: one per-hero
    /// two-layer animator controller driven by the Mixamo motion kit
    /// (8-way locomotion, dodge roll, full-body committed attacks, hit
    /// reactions, death), plus the six active hero prefabs (human + AI per
    /// hero) carrying the HeavyAction runtime components. Prefabs are plain
    /// active actors — no dormancy gates, no vendor components.
    /// </summary>
    public static class HeavyHeroBuilder
    {
        public const string Root = "Assets/Generated/HeavyAction/";
        public const string ControllersRoot = Root + "Controllers/";
        public const string WeaponsRoot = Root + "Weapons/";
        public const string UpperBodyMaskPath = Root + "UpperBody.mask";
        public const string SourceUpperBodyMaskPath =
            "Assets/ModularRPGHeroesPBR/Animations/SwordShield/ForAnimationLayers/New Avatar Mask.mask";
        public const string NavigationPlannerName = "HeavyNavigationPlanner";
        const string MixamoRoot = "Assets/ThirdParty/Mixamo/";

        // Contract animator names; HeavyAnimationDriver and the automation
        // status sampler consume these literally.
        public const string BaseLayerName = "Base";
        public const string UpperBodyLayerName = "UpperBody";
        public const string SpeedParameter = "Speed01";
        public const string MoveXParameter = "MoveX";
        public const string MoveZParameter = "MoveZ";
        public const string AttackSpeedParameter = "AttackSpeed";
        public const string LocomotionStateName = "Locomotion";
        public const string DieStateName = "Die";
        public const string VictoryStateName = "Victory";
        public const string VictoryMaintainStateName = "VictoryMaintain";
        public const string DashStateName = "Dash";
        public const string AttackPrimaryStateName = "AttackPrimary";
        public const string AttackSuperStateName = "AttackSuper";
        public const string EmptyStateName = "Empty";
        public const string GetHitStateName = "GetHit";

        // Root layer kept from the earlier generated actors so existing
        // physics/combat layer masks stay valid for the new ones.
        public const int BrawlerLayer = 23;

        const float BodyMass = 50f;
        const float BodyAngularDamping = 0.05f;
        const float CapsuleRadius = 0.375f;
        const float CapsuleHeight = 1.8f;
        static readonly Vector3 CapsuleCenter = new Vector3(0f, 0.9f, 0f);

        const float AutoExitTime = 0.97f;
        const float AutoExitDurationSeconds = 0.12f;

        sealed class HeroConfig
        {
            public string HeroId;             // roster id
            public string HeroName;           // Rime / Thorn / Bastion
            public string SourcePrefabPath;
            public string WeaponPrefabName;   // under WeaponsRoot
            public string[] AuthoredWeaponNames;
            public string[] DisabledChildren;
            public bool WeaponHeldInLeftHand;
            public Color TrailColor;          // per-swing weapon trail tint
            public float TrailWidthScale;
            public string IdleClip;           // Mixamo clip names
            public string AttackPrimaryClip;
            public string AttackSuperClip;
            public float AttackStateSpeed;    // default; assembler overrides
        }

        static readonly HeroConfig[] Heroes =
        {
            new HeroConfig
            {
                HeroId = "frost",
                HeroName = "Rime",
                SourcePrefabPath = "Assets/Generated/Wizards/Prefabs/FrostWizard.prefab",
                WeaponPrefabName = "RimeStaffPresentation",
                AuthoredWeaponNames = new[] { "Staff02" },
                DisabledChildren = new[] { "SchoolAura" },
                WeaponHeldInLeftHand = false,
                TrailColor = new Color(0.45f, 0.8f, 1f),   // ice-blue
                TrailWidthScale = 1f,
                IdleClip = "Mixamo_Idle",
                AttackPrimaryClip = "Mixamo_Frost_Cast1",
                AttackSuperClip = "Mixamo_Frost_Cast2",
                AttackStateSpeed = 1f,
            },
            new HeroConfig
            {
                HeroId = "thorn",
                HeroName = "Thorn",
                SourcePrefabPath =
                    "Assets/ModularRPGHeroesPBR/Prefabs/BasicCharacters/Bow02.prefab",
                WeaponPrefabName = "ThornBowPresentation",
                // Bow2 rides the left wrist socket and Arrow2 the right one;
                // both are replaced by the generated bow presentation visual.
                AuthoredWeaponNames = new[] { "Bow2", "Arrow2" },
                DisabledChildren = Array.Empty<string>(),
                WeaponHeldInLeftHand = true,
                TrailColor = new Color(0.55f, 1f, 0.35f),  // lime
                TrailWidthScale = 0.8f,
                IdleClip = "Mixamo_Idle",
                AttackPrimaryClip = "Mixamo_Thorn_Shoot",
                AttackSuperClip = "Mixamo_Thorn_PowerShot",
                AttackStateSpeed = 1.1f,
            },
            new HeroConfig
            {
                HeroId = "bastion",
                HeroName = "Bastion",
                SourcePrefabPath = "Assets/Generated/Warriors/Prefabs/BastionWarrior.prefab",
                WeaponPrefabName = "BastionSwordPresentation",
                AuthoredWeaponNames = new[] { "Sword1_R" },
                DisabledChildren = Array.Empty<string>(),
                WeaponHeldInLeftHand = false,
                TrailColor = new Color(1f, 0.95f, 0.75f),  // steel white-gold
                TrailWidthScale = 1.2f,
                IdleClip = "Mixamo_Bastion_Idle",
                AttackPrimaryClip = "Mixamo_Bastion_Slash",
                AttackSuperClip = "Mixamo_Bastion_Power",
                AttackStateSpeed = 0.95f,
            },
        };

        [MenuItem("Brawl Arena/Heavy Action/Build Hero Assets")]
        static void BuildFromMenu()
        {
            EnsureAssets();
            Debug.Log("Built the heavy hero controllers, mask, and six prefabs.");
        }

        /// <summary>
        /// Idempotent full build: upper-body mask clone, one controller per
        /// hero, then the six hero prefabs. Safe to run on every roster
        /// rebuild; prefab GUIDs are preserved.
        /// </summary>
        public static void EnsureAssets()
        {
            EnsureFolder(Root.TrimEnd('/'));
            EnsureFolder(ControllersRoot.TrimEnd('/'));

            AvatarMask upperBodyMask = EnsureUpperBodyMask();

            foreach (HeroConfig hero in Heroes)
            {
                EnsureFolder(Root + hero.HeroName);
                AnimatorController controller = BuildHeroController(hero, upperBodyMask);
                BuildHeroPrefab(hero, controller, true);
                BuildHeroPrefab(hero, controller, false);
            }

            EnsureAlignedProjectile("Arrow01");
            EnsureAlignedProjectile("Arrow02");

            AssetDatabase.SaveAssets();
        }

        /// <summary>
        /// Wraps a weapon-pack projectile mesh whose long axis is authored
        /// vertical (+Y, e.g. the ModularRPG arrows) in a root whose +Z is the
        /// flight axis Projectile.Launch aligns to velocity, with the mesh
        /// centered on the root so the shaft straddles the sweep origin.
        /// </summary>
        public static GameObject EnsureAlignedProjectile(string weaponPrefabName)
        {
            string outputPath = Root + "Projectiles/" + weaponPrefabName + "Projectile.prefab";
            EnsureFolder(Root + "Projectiles");

            string sourcePath =
                "Assets/ModularRPGHeroesPBR/Prefabs/Weapons/" + weaponPrefabName + ".prefab";
            GameObject source = AssetDatabase.LoadAssetAtPath<GameObject>(sourcePath);
            if (source == null)
            {
                throw new InvalidOperationException(
                    "Missing projectile source prefab at " + sourcePath + ".");
            }

            var root = new GameObject(weaponPrefabName + "Projectile");
            try
            {
                GameObject visual = (GameObject)UnityEngine.Object.Instantiate(source);
                visual.name = weaponPrefabName;
                visual.transform.SetParent(root.transform, false);
                // +90 about X maps the authored +Y long axis onto root +Z.
                visual.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);

                Vector3 meshCenter = Vector3.zero;
                MeshFilter filter = visual.GetComponentInChildren<MeshFilter>(true);
                if (filter != null && filter.sharedMesh != null)
                    meshCenter = filter.sharedMesh.bounds.center;
                visual.transform.localPosition =
                    -(visual.transform.localRotation * meshCenter);

                // Impact authority is the roster sweep in Projectile; a stray
                // authored collider would fight the brawler capsules.
                foreach (Collider collider in root.GetComponentsInChildren<Collider>(true))
                    UnityEngine.Object.DestroyImmediate(collider);

                return PrefabUtility.SaveAsPrefabAsset(root, outputPath);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(root);
            }
        }

        public static GameObject LoadAlignedProjectile(string weaponPrefabName)
        {
            string path = Root + "Projectiles/" + weaponPrefabName + "Projectile.prefab";
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (prefab == null)
            {
                throw new InvalidOperationException(
                    "Missing aligned projectile prefab at " + path +
                    ". Run HeavyHeroBuilder.EnsureAssets first.");
            }
            return prefab;
        }

        public static GameObject LoadHumanPrefab(string heroId)
        {
            return LoadPrefab(heroId, true);
        }

        public static GameObject LoadAIPrefab(string heroId)
        {
            return LoadPrefab(heroId, false);
        }

        public static string PrefabPath(string heroId, bool humanVariant)
        {
            HeroConfig hero = RequireHero(heroId);
            return Root + hero.HeroName + "/" + hero.HeroName + "Heavy" +
                (humanVariant ? "Human" : "AI") + ".prefab";
        }

        static GameObject LoadPrefab(string heroId, bool humanVariant)
        {
            string path = PrefabPath(heroId, humanVariant);
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (prefab == null)
            {
                throw new InvalidOperationException(
                    "Missing heavy hero prefab at " + path +
                    ". Run HeavyHeroBuilder.EnsureAssets first.");
            }
            return prefab;
        }

        static HeroConfig RequireHero(string heroId)
        {
            HeroConfig hero = Heroes.FirstOrDefault(candidate =>
                string.Equals(candidate.HeroId, heroId, StringComparison.Ordinal));
            if (hero == null)
            {
                throw new ArgumentException(
                    "Unknown heavy hero id '" + heroId + "'.", nameof(heroId));
            }
            return hero;
        }

        static AvatarMask EnsureUpperBodyMask()
        {
            var mask = AssetDatabase.LoadAssetAtPath<AvatarMask>(UpperBodyMaskPath);
            if (mask == null)
            {
                if (AssetDatabase.LoadAssetAtPath<AvatarMask>(SourceUpperBodyMaskPath) == null)
                {
                    throw new InvalidOperationException(
                        "The upper-body avatar mask source is missing at " +
                        SourceUpperBodyMaskPath + ".");
                }
                if (!AssetDatabase.CopyAsset(SourceUpperBodyMaskPath, UpperBodyMaskPath))
                {
                    throw new InvalidOperationException(
                        "Unity failed to clone the upper-body avatar mask to " +
                        UpperBodyMaskPath + ".");
                }
                mask = AssetDatabase.LoadAssetAtPath<AvatarMask>(UpperBodyMaskPath);
                mask.name = "UpperBody";
                EditorUtility.SetDirty(mask);
            }
            return mask;
        }

        /// <summary>
        /// One controller per hero. Base layer: Mixamo 8-way locomotion blend
        /// plus FULL-BODY AttackPrimary/AttackSuper one-shots (the committed
        /// swing IS the souls feel), the Mixamo_Roll dodge, and lifecycle
        /// states. Upper layer (masked): Empty + GetHit stagger overlay.
        /// </summary>
        static AnimatorController BuildHeroController(
            HeroConfig hero, AvatarMask upperBodyMask)
        {
            string path = ControllersRoot + hero.HeroName + "Heavy.controller";
            if (AssetDatabase.LoadAssetAtPath<AnimatorController>(path) != null)
                AssetDatabase.DeleteAsset(path);

            AnimatorController controller =
                AnimatorController.CreateAnimatorControllerAtPath(path);
            controller.AddParameter(SpeedParameter, AnimatorControllerParameterType.Float);
            controller.AddParameter(MoveXParameter, AnimatorControllerParameterType.Float);
            controller.AddParameter(MoveZParameter, AnimatorControllerParameterType.Float);
            // HeavyAnimationDriver.Configure writes the per-hero profile value
            // here; the default keeps un-assembled actors (previews) at the
            // authored hero speed.
            controller.AddParameter(new AnimatorControllerParameter
            {
                name = AttackSpeedParameter,
                type = AnimatorControllerParameterType.Float,
                defaultFloat = hero.AttackStateSpeed,
            });

            AnimatorControllerLayer[] layers = controller.layers;
            layers[0].name = BaseLayerName;
            controller.layers = layers;

            AnimatorStateMachine baseMachine = controller.layers[0].stateMachine;
            AnimatorState locomotion = AddState(baseMachine, LocomotionStateName);
            locomotion.motion = BuildLocomotionTree(controller, hero);
            baseMachine.defaultState = locomotion;

            AnimatorState die = AddState(baseMachine, DieStateName);
            die.motion = LoadMixamoClip("Lifecycle/Mixamo_Death");

            AnimatorState victory = AddState(baseMachine, VictoryStateName);
            victory.motion = LoadMixamoClip("Lifecycle/Mixamo_Victory");
            AnimatorState victoryMaintain = AddState(baseMachine, VictoryMaintainStateName);
            victoryMaintain.motion = LoadHeroIdleClip(hero);
            AddAutoExit(victory, victoryMaintain);

            AnimatorState dash = AddState(baseMachine, DashStateName);
            dash.motion = LoadMixamoClip("Locomotion/Mixamo_Roll");
            AddAutoExit(dash, locomotion);

            AnimatorState attackPrimary = AddState(baseMachine, AttackPrimaryStateName);
            attackPrimary.motion = LoadHeroClip(hero, hero.AttackPrimaryClip);
            attackPrimary.speed = 1f;
            attackPrimary.speedParameter = AttackSpeedParameter;
            attackPrimary.speedParameterActive = true;
            AddAutoExit(attackPrimary, locomotion);

            AnimatorState attackSuper = AddState(baseMachine, AttackSuperStateName);
            attackSuper.motion = LoadHeroClip(hero, hero.AttackSuperClip);
            attackSuper.speed = 1f;
            attackSuper.speedParameter = AttackSpeedParameter;
            attackSuper.speedParameterActive = true;
            AddAutoExit(attackSuper, locomotion);

            var upperMachine = new AnimatorStateMachine
            {
                name = UpperBodyLayerName,
                hideFlags = HideFlags.HideInHierarchy,
            };
            AssetDatabase.AddObjectToAsset(upperMachine, controller);
            controller.AddLayer(new AnimatorControllerLayer
            {
                name = UpperBodyLayerName,
                defaultWeight = 1f,
                avatarMask = upperBodyMask,
                blendingMode = AnimatorLayerBlendingMode.Override,
                stateMachine = upperMachine,
            });

            AnimatorState empty = AddState(upperMachine, EmptyStateName);
            upperMachine.defaultState = empty;

            AnimatorState getHit = AddState(upperMachine, GetHitStateName);
            getHit.motion = LoadMixamoClip("Reactions/Mixamo_HitSmall");
            AddAutoExit(getHit, empty);

            EditorUtility.SetDirty(controller);
            return controller;
        }

        static BlendTree BuildLocomotionTree(
            AnimatorController controller, HeroConfig hero)
        {
            var tree = new BlendTree
            {
                name = LocomotionStateName,
                blendType = BlendTreeType.FreeformDirectional2D,
                blendParameter = MoveXParameter,
                blendParameterY = MoveZParameter,
                hideFlags = HideFlags.HideInHierarchy,
            };
            AssetDatabase.AddObjectToAsset(tree, controller);

            tree.AddChild(LoadHeroIdleClip(hero), new Vector2(0f, 0f));
            tree.AddChild(LoadMixamoClip("Locomotion/Mixamo_Run"), new Vector2(0f, 1f));
            tree.AddChild(LoadMixamoClip("Locomotion/Mixamo_RunBack"), new Vector2(0f, -1f));
            tree.AddChild(LoadMixamoClip("Locomotion/Mixamo_StrafeLeft"), new Vector2(-1f, 0f));
            tree.AddChild(LoadMixamoClip("Locomotion/Mixamo_StrafeRight"), new Vector2(1f, 0f));
            return tree;
        }

        static AnimationClip LoadHeroIdleClip(HeroConfig hero)
        {
            return LoadHeroClip(hero, hero.IdleClip);
        }

        /// <summary>
        /// Hero clips live either in the hero's own Mixamo folder or the
        /// shared Locomotion set; resolve by clip name across both.
        /// </summary>
        static AnimationClip LoadHeroClip(HeroConfig hero, string clipName)
        {
            string[] candidates =
            {
                MixamoRoot + hero.HeroName + "/" + clipName + ".fbx",
                MixamoRoot + "Bastion/" + clipName + ".fbx",
                MixamoRoot + "Frost/" + clipName + ".fbx",
                MixamoRoot + "Thorn/" + clipName + ".fbx",
                MixamoRoot + "Locomotion/" + clipName + ".fbx",
            };
            foreach (string fbxPath in candidates)
            {
                if (AssetDatabase.LoadAssetAtPath<GameObject>(fbxPath) == null) continue;
                foreach (UnityEngine.Object asset in
                         AssetDatabase.LoadAllAssetsAtPath(fbxPath))
                {
                    if (asset is AnimationClip clip &&
                        string.Equals(clip.name, clipName, StringComparison.Ordinal))
                        return clip;
                }
            }
            throw new InvalidOperationException(
                "Missing Mixamo clip '" + clipName + "' for hero " + hero.HeroName + ".");
        }

        static AnimationClip LoadMixamoClip(string relativePath)
        {
            string fbxPath = MixamoRoot + relativePath + ".fbx";
            string clipName = relativePath.Substring(relativePath.LastIndexOf('/') + 1);
            foreach (UnityEngine.Object asset in AssetDatabase.LoadAllAssetsAtPath(fbxPath))
            {
                if (asset is AnimationClip clip &&
                    string.Equals(clip.name, clipName, StringComparison.Ordinal))
                    return clip;
            }
            throw new InvalidOperationException(
                "Missing Mixamo animation clip '" + clipName + "' in " + fbxPath + ".");
        }

        static AnimatorState AddState(AnimatorStateMachine machine, string name)
        {
            AnimatorState state = machine.AddState(name);
            state.writeDefaultValues = true;
            return state;
        }

        static void AddAutoExit(AnimatorState from, AnimatorState to)
        {
            AnimatorStateTransition transition = from.AddTransition(to);
            transition.hasExitTime = true;
            transition.exitTime = AutoExitTime;
            transition.hasFixedDuration = true;
            transition.duration = AutoExitDurationSeconds;
        }

        static GameObject BuildHeroPrefab(
            HeroConfig hero, AnimatorController controller, bool humanVariant)
        {
            GameObject source =
                AssetDatabase.LoadAssetAtPath<GameObject>(hero.SourcePrefabPath);
            if (source == null)
            {
                throw new InvalidOperationException(
                    "Missing heavy hero source prefab at " + hero.SourcePrefabPath + ".");
            }
            string weaponPrefabPath = WeaponsRoot + hero.WeaponPrefabName + ".prefab";
            GameObject weaponPrefab =
                AssetDatabase.LoadAssetAtPath<GameObject>(weaponPrefabPath);
            if (weaponPrefab == null)
            {
                throw new InvalidOperationException(
                    "Missing weapon presentation prefab at " + weaponPrefabPath + ".");
            }

            string destinationPath = PrefabPath(hero.HeroId, humanVariant);
            Scene previewScene = EditorSceneManager.NewPreviewScene();
            try
            {
                var instance = (GameObject)PrefabUtility.InstantiatePrefab(
                    source, previewScene);
                instance.name = hero.HeroName + "Heavy" +
                    (humanVariant ? "Human" : "AI");
                instance.tag = "Player";
                instance.layer = BrawlerLayer;
                instance.SetActive(true);

                Animator animator = instance.GetComponent<Animator>();
                if (animator == null || !animator.isHuman || animator.avatar == null ||
                    !animator.avatar.isValid)
                {
                    throw new InvalidOperationException(
                        hero.SourcePrefabPath + " must keep a valid Humanoid Avatar.");
                }
                animator.runtimeAnimatorController = controller;
                animator.applyRootMotion = false;
                animator.updateMode = AnimatorUpdateMode.Normal;
                animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
                animator.enabled = true;

                foreach (string authoredName in hero.AuthoredWeaponNames)
                {
                    Transform authored = FindDescendant(instance.transform, authoredName);
                    if (authored == null)
                    {
                        throw new InvalidOperationException(
                            hero.SourcePrefabPath + " lost its authored '" +
                            authoredName + "' weapon child.");
                    }
                    UnityEngine.Object.DestroyImmediate(authored.gameObject);
                }

                foreach (string childName in hero.DisabledChildren)
                {
                    Transform child = FindDescendant(instance.transform, childName);
                    if (child != null) child.gameObject.SetActive(false);
                }

                Transform weaponRoot =
                    AttachWeaponPresentation(hero, instance, animator, weaponPrefab);

                Rigidbody body = instance.GetComponent<Rigidbody>();
                if (body == null) body = instance.AddComponent<Rigidbody>();
                body.mass = BodyMass;
                body.linearDamping = 0f;
                body.angularDamping = BodyAngularDamping;
                body.useGravity = true;
                body.isKinematic = false;
                body.constraints = RigidbodyConstraints.FreezeRotation;
                body.interpolation = RigidbodyInterpolation.Interpolate;
                body.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;

                CapsuleCollider capsule = instance.GetComponent<CapsuleCollider>();
                if (capsule == null) capsule = instance.AddComponent<CapsuleCollider>();
                capsule.center = CapsuleCenter;
                capsule.radius = CapsuleRadius;
                capsule.height = CapsuleHeight;
                capsule.direction = 1;
                capsule.isTrigger = false;
                capsule.enabled = true;

                instance.AddComponent<HeavyBrawlerMotor>();
                instance.AddComponent<HeavyAnimationDriver>();
                HeavyWeaponPresentation weaponPresentation =
                    instance.AddComponent<HeavyWeaponPresentation>();
                weaponPresentation.Configure(weaponRoot);
                // Per-swing trail telegraph on the weapon visual root, tip
                // anchored at the SpellOrigin muzzle the wiring above already
                // guaranteed exists. Prefabs rebuild from scratch each run,
                // so plain AddComponent stays idempotent.
                HeavyWeaponTrail weaponTrail =
                    weaponRoot.gameObject.AddComponent<HeavyWeaponTrail>();
                weaponTrail.Configure(hero.TrailColor, hero.TrailWidthScale,
                    FindDescendant(weaponRoot, "SpellOrigin"));
                HeavyBrawlerIdentity identity =
                    instance.AddComponent<HeavyBrawlerIdentity>();
                identity.heroId = hero.HeroId;
                identity.isHumanVariant = humanVariant;
                instance.AddComponent<Health>();
                instance.AddComponent<BrawlerController>();

                if (humanVariant)
                {
                    PlayerBrawlerInput input =
                        instance.AddComponent<PlayerBrawlerInput>();
                    input.enabled = false;
                }
                else
                {
                    ConfigureAIComponents(instance, capsule);
                }

                GameObject saved = PrefabUtility.SaveAsPrefabAsset(
                    instance, destinationPath, out bool success);
                if (!success || saved == null)
                {
                    throw new InvalidOperationException(
                        "Unity failed to save " + destinationPath + ".");
                }
                return saved;
            }
            finally
            {
                EditorSceneManager.ClosePreviewScene(previewScene);
            }
        }

        static Transform AttachWeaponPresentation(
            HeroConfig hero, GameObject instance, Animator animator,
            GameObject weaponPrefab)
        {
            Transform hand = animator.GetBoneTransform(
                hero.WeaponHeldInLeftHand
                    ? HumanBodyBones.LeftHand
                    : HumanBodyBones.RightHand);
            if (hand == null)
            {
                throw new InvalidOperationException(
                    hero.SourcePrefabPath + " has no valid hand bone for its weapon.");
            }

            var weapon = (GameObject)PrefabUtility.InstantiatePrefab(
                weaponPrefab, instance.scene);
            weapon.name = hero.WeaponPrefabName;
            weapon.transform.SetParent(hand, false);
            weapon.transform.localPosition = Vector3.zero;
            weapon.transform.localRotation = Quaternion.identity;
            weapon.transform.localScale = Vector3.one;

            if (FindDescendant(weapon.transform, "SpellOrigin") == null)
            {
                throw new InvalidOperationException(
                    hero.WeaponPrefabName + " lost its SpellOrigin muzzle child.");
            }
            return weapon.transform;
        }

        static void ConfigureAIComponents(GameObject instance, CapsuleCollider capsule)
        {
            AIBrawler ai = instance.AddComponent<AIBrawler>();
            ai.enabled = false;

            HeavyBrawlerNavigation navigation =
                instance.AddComponent<HeavyBrawlerNavigation>();

            var plannerObject = new GameObject(NavigationPlannerName);
            plannerObject.layer = 0;
            plannerObject.transform.SetParent(instance.transform, false);
            plannerObject.transform.localPosition = Vector3.zero;
            plannerObject.transform.localRotation = Quaternion.identity;
            plannerObject.transform.localScale = Vector3.one;

            NavMeshAgent planner = plannerObject.AddComponent<NavMeshAgent>();
            planner.radius = capsule.radius;
            planner.height = capsule.height;
            planner.baseOffset = 0f;
            planner.speed = 3.5f;
            planner.acceleration = 40f;
            planner.angularSpeed = 0f;
            planner.stoppingDistance = 0.5f;
            planner.autoBraking = true;
            planner.autoRepath = true;
            planner.autoTraverseOffMeshLink = false;
            planner.updatePosition = false;
            planner.updateRotation = false;
            planner.updateUpAxis = false;
            planner.areaMask = NavMesh.AllAreas;
            planner.obstacleAvoidanceType =
                ObstacleAvoidanceType.HighQualityObstacleAvoidance;
            planner.enabled = false;

            // Persists the planner binding and transform-neutral posture in
            // the prefab; leaves both navigation and agent disabled for the
            // assembler to enable alongside AIBrawler.
            navigation.ConfigureDormant(planner);
            ai.SetNavigation(navigation);
        }

        static Transform FindDescendant(Transform root, string name)
        {
            if (root == null) return null;
            return root.GetComponentsInChildren<Transform>(true)
                .FirstOrDefault(candidate =>
                    string.Equals(candidate.name, name, StringComparison.Ordinal));
        }

        static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;
            string normalized = path.Replace('\\', '/').TrimEnd('/');
            int separator = normalized.LastIndexOf('/');
            if (separator <= 0)
                throw new InvalidOperationException("Invalid asset folder path: " + path);
            string parent = normalized.Substring(0, separator);
            string name = normalized.Substring(separator + 1);
            EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, name);
        }
    }
}
