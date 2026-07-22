using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.UI;
using Unity.AI.Navigation;
using TMPro;
using DamageNumbersPro;
using Photon.Pun;

namespace Crownfall.EditorTools
{
    /// Builds the whole game: animator graph + per-class overrides from the
    /// ModularRPGHeroes clips, the arena scene, nine fighter rigs, HUD/VFX
    /// wiring, lighting, post-processing and the NavMesh.
    public static class CrownfallForge
    {
        internal const string GenDir = "Assets/Crownfall/Generated";
        const string ScenePath = "Assets/Crownfall/CrownfallArena.unity";
        internal const string AnimRoot = "Assets/ModularRPGHeroesPBR/Animations";
        internal const string CharDir = "Assets/ModularRPGHeroesPBR/Prefabs/BasicCharacters";
        internal const string LayerLabSprites = "Assets/Layer Lab/GUI Pro-CasualGame/ResourcesData/Sprites/Components";
        internal const string LayerLabFonts = "Assets/Layer Lab/GUI Pro-CasualGame/ResourcesData/Fonts";

        static readonly string[] ElementNames = { "Light", "Earth", "Frost", "Storm", "Shadow", "Fire", "Arcane" };

        /// Legacy modular-pack hammer skin (superseded by the Dungeon Mason
        /// roster below; kept for ApplyHammerLoadout fallback experiments).
        internal const string HammerSkin = "SingleTwoHandSword05";

        /// The 2026-07-22 roster redo: Dungeon Mason heroes (all Humanoid).
        /// Player + net-prefab rigs use the PBR variants; AI understudies wear
        /// the Polyart finishes so twins on the field still read apart.
        internal static readonly Dictionary<ClassId, string> HeroSkins = new Dictionary<ClassId, string>
        {
            { ClassId.Knight,     "Assets/Mini Legion Footman PBR HP Polyart/Prefabs/FootmanPBR.prefab" },
            { ClassId.Greatsword, "Assets/Mini Legion Grunt PBR HP Polyart/Prefab/GruntPBR.prefab" },
            { ClassId.Duelist,    "Assets/RPG Tiny Hero Duo/Prefab/MaleCharacterPBR.prefab" },
            { ClassId.Mage,       "Assets/WizardPBR/Prefabs/WizardStandardMaterial.prefab" },
            { ClassId.Warhammer,  "Assets/Mini Legion Rock Golem PBR HP Polyart/Prefabs/PBR_Golem.prefab" },
        };

        internal static readonly Dictionary<ClassId, string> HeroSkinsAlt = new Dictionary<ClassId, string>
        {
            { ClassId.Knight,     "Assets/Mini Legion Footman PBR HP Polyart/Prefabs/FootmanPolyart.prefab" },
            { ClassId.Greatsword, "Assets/Mini Legion Grunt PBR HP Polyart/Prefab/GruntPolyart.prefab" },
            { ClassId.Duelist,    "Assets/RPG Tiny Hero Duo/Prefab/MaleCharacterPolyart.prefab" },
            { ClassId.Mage,       "Assets/WizardPBR/Prefabs/WizardPBRMaskTintMaterial.prefab" },
            { ClassId.Warhammer,  "Assets/Mini Legion Rock Golem PBR HP Polyart/Prefabs/Polyart_Golem.prefab" },
        };

        internal static float HeroHeight(ClassId cls) => cls switch
        {
            ClassId.Duelist => 1.6f,
            ClassId.Greatsword => 1.85f,
            ClassId.Warhammer => 2.0f,
            _ => 1.72f,
        };

        internal static GameObject LoadHeroPrefab(string skin) =>
            AssetDatabase.LoadAssetAtPath<GameObject>(
                skin.Contains("/") ? skin : $"{CharDir}/{skin}.prefab");

        static Transform FindDeepChild(Transform root, string name)
        {
            foreach (var tr in root.GetComponentsInChildren<Transform>(true))
                if (string.Equals(tr.name, name, System.StringComparison.OrdinalIgnoreCase))
                    return tr;
            return null;
        }

        /// Normalize + arm an external (Dungeon Mason) hero model: uniform
        /// gameplay height, the wizard's Staff01 flown, the duelist dual-wielding
        /// the pack's one-handed swords on the humanoid hand bones.
        internal static void PrepareExternalHero(GameObject model, ClassId cls)
        {
            var b = MeasureBoundsInstance(model);
            if (b.size.y > 0.05f)
            {
                float s = HeroHeight(cls) / b.size.y;
                if (Mathf.Abs(1f - s) > 0.05f)
                    model.transform.localScale = model.transform.localScale * s;
            }

            if (cls == ClassId.Mage)
            {
                for (int i = 1; i <= 3; i++)
                {
                    var staff = FindDeepChild(model.transform, "Staff0" + i);
                    if (staff != null) staff.gameObject.SetActive(i == 1);
                }
            }
            else if (cls == ClassId.Duelist)
            {
                // the male hero ships already holding OHS03 + a shield — twin
                // blades means shield off, second sword into the left hand
                var shield = FindDeepChild(model.transform, "Shield08");
                if (shield != null) shield.gameObject.SetActive(false);
                var anim = model.GetComponentInChildren<Animator>();
                if (anim != null && anim.isHuman)
                    AttachProp(anim, HumanBodyBones.LeftHand, "Assets/RPG Tiny Hero Duo/Prefab/OHS06PBR.prefab");
            }
        }

        static void AttachProp(Animator anim, HumanBodyBones bone, string prefabPath)
        {
            var hand = anim.GetBoneTransform(bone);
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
            if (hand == null || prefab == null)
            {
                Debug.LogWarning($"[CrownfallForge] AttachProp failed: hand={hand != null} prefab={prefabPath}");
                return;
            }
            var prop = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
            PrefabUtility.UnpackPrefabInstance(prop, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
            prop.transform.SetParent(hand, false);
            prop.transform.localPosition = Vector3.zero;
            prop.transform.localRotation = Quaternion.identity;
        }

        // ------------------------------------------------------------------ entry points

        [MenuItem("Crownfall/Build All")]
        public static void BuildAll()
        {
            EnsureFolders();
            var overrides = BuildAnimators();
            BuildNetPrefabs(overrides);
            BuildScene(overrides);
            Debug.Log("[CrownfallForge] Build complete: " + ScenePath);
        }

        [MenuItem("Crownfall/Build Scene Only")]
        public static void BuildSceneOnly()
        {
            EnsureFolders();
            var overrides = new Dictionary<ClassId, AnimatorOverrideController>();
            foreach (ClassId c in System.Enum.GetValues(typeof(ClassId)))
            {
                var aoc = AssetDatabase.LoadAssetAtPath<AnimatorOverrideController>($"{GenDir}/Fighter_{c}.overrideController");
                if (aoc == null) { BuildAll(); return; }
                overrides[c] = aoc;
            }
            BuildScene(overrides);
            Debug.Log("[CrownfallForge] Scene rebuilt: " + ScenePath);
        }

        static void EnsureFolders()
        {
            if (!AssetDatabase.IsValidFolder("Assets/Crownfall"))
                AssetDatabase.CreateFolder("Assets", "Crownfall");
            if (!AssetDatabase.IsValidFolder(GenDir))
                AssetDatabase.CreateFolder("Assets/Crownfall", "Generated");
        }

        // ================================================================== animators

        internal static Dictionary<string, AnimationClip> LoadClips(string folder)
        {
            // keyed by FILE name: the packs' internal clip names are unreliable
            // (abbreviated suffixes, even duplicated names across files)
            var dict = new Dictionary<string, AnimationClip>(System.StringComparer.OrdinalIgnoreCase);
            foreach (var file in Directory.GetFiles(folder, "*.fbx"))
            {
                string path = file.Replace('\\', '/');
                foreach (var obj in AssetDatabase.LoadAllAssetsAtPath(path))
                {
                    if (obj is AnimationClip clip && !clip.name.StartsWith("__preview"))
                        dict[Path.GetFileNameWithoutExtension(path)] = clip;
                }
            }
            return dict;
        }

        static Dictionary<ClassId, AnimatorOverrideController> BuildAnimators()
        {
            string ctrlPath = $"{GenDir}/CrownfallFighter.controller";
            AssetDatabase.DeleteAsset(ctrlPath);

            var ss = LoadClips($"{AnimRoot}/SwordShield");
            AnimationClip C(string stem) =>
                ss.TryGetValue(stem + "_SwordShield", out var c) ? c : null;

            var ctrl = AnimatorController.CreateAnimatorControllerAtPath(ctrlPath);
            ctrl.AddParameter("MoveX", AnimatorControllerParameterType.Float);
            ctrl.AddParameter("MoveZ", AnimatorControllerParameterType.Float);
            ctrl.AddParameter("RollX", AnimatorControllerParameterType.Float);
            ctrl.AddParameter("RollZ", AnimatorControllerParameterType.Float);
            var locoRateParam = new AnimatorControllerParameter
            {
                name = "LocoRate",
                type = AnimatorControllerParameterType.Float,
                defaultFloat = 1f,
            };
            ctrl.AddParameter(locoRateParam);
            ctrl.AddParameter("Locked", AnimatorControllerParameterType.Bool);
            ctrl.AddParameter("Blocking", AnimatorControllerParameterType.Bool);
            foreach (var t in new[] { "AttackL", "AttackH", "Skill", "Roll", "Hit", "Stagger", "Recover", "Die", "Respawn", "Victory", "BlockImpact" })
                ctrl.AddParameter(t, AnimatorControllerParameterType.Trigger);

            var sm = ctrl.layers[0].stateMachine;

            // --- locomotion blend tree
            var loco = ctrl.CreateBlendTreeInController("Locomotion", out BlendTree locoTree);
            locoTree.blendType = BlendTreeType.FreeformDirectional2D;
            locoTree.blendParameter = "MoveX";
            locoTree.blendParameterY = "MoveZ";
            locoTree.AddChild(C("Idle"), new Vector2(0f, 0f));
            locoTree.AddChild(C("Walk"), new Vector2(0f, 0.5f));
            locoTree.AddChild(C("WalkBack"), new Vector2(0f, -0.5f));
            locoTree.AddChild(C("WalkLeft"), new Vector2(-0.5f, 0f));
            locoTree.AddChild(C("WalkRight"), new Vector2(0.5f, 0f));
            locoTree.AddChild(C("Run"), new Vector2(0f, 1f));
            locoTree.AddChild(C("RunBack"), new Vector2(0f, -1f));
            locoTree.AddChild(C("WalkLeft"), new Vector2(-1f, 0f));
            locoTree.AddChild(C("WalkRight"), new Vector2(1f, 0f));
            locoTree.AddChild(C("Sprint"), new Vector2(0f, 2f));
            var locoChildren = locoTree.children;
            locoChildren[7].timeScale = 1.7f;
            locoChildren[8].timeScale = 1.7f;
            locoTree.children = locoChildren;
            loco.speedParameter = "LocoRate";
            loco.speedParameterActive = true;
            sm.defaultState = loco;

            // --- roll blend tree
            var roll = ctrl.CreateBlendTreeInController("Roll", out BlendTree rollTree);
            rollTree.blendType = BlendTreeType.SimpleDirectional2D;
            rollTree.blendParameter = "RollX";
            rollTree.blendParameterY = "RollZ";
            rollTree.AddChild(C("Roll"), new Vector2(0f, 1f));
            rollTree.AddChild(C("RollBack"), new Vector2(0f, -1f));
            rollTree.AddChild(C("RollLeft"), new Vector2(-1f, 0f));
            rollTree.AddChild(C("RollRight"), new Vector2(1f, 0f));
            roll.tag = "Roll";
            roll.speed = 1.3f;

            AnimatorState S(string name, string stem, string tag, float speed = 1f)
            {
                var st = sm.AddState(name);
                st.motion = C(stem);
                st.tag = tag;
                st.speed = speed;
                return st;
            }

            // light chain uses the short snappy clips; the heavy gets the big slow swing
            var l1 = S("AttackL1", "NormalAttack01", "Attack", 1.2f);
            var l2 = S("AttackL2", "Combo01", "Attack", 1.15f);
            var l3 = S("AttackL3", "Combo02", "Attack", 1.15f);
            var l4 = S("AttackL4", "Combo03", "Attack", 1.15f);
            var heavy = S("Heavy", "NormalAttack02", "Attack", 1.12f);
            // class skill: the flashiest chain clip per weapon (mage overrides to the
            // channel/maintain pose and drives its exit from code)
            var skill = S("Skill", "Combo05", "Skill", 1.05f);
            var getHit = S("GetHit", "GetHit", "Hit", 1.1f);
            var dizzy = S("Dizzy", "Dizzy", "Stagger");
            var die = S("Die", "Die", "Die");
            var block = S("Block", "Defend", "Block");
            var blockHit = S("BlockHit", "DefendHit", "Block", 1.15f);
            var victory = S("Victory", "Victory", "Victory");
            var victoryLoop = S("VictoryLoop", "VictoryMaintain", "Victory");

            AnimatorStateTransition T(AnimatorState from, AnimatorState to, string trigger,
                bool hasExit = false, float exitTime = 0.9f, float duration = 0.12f)
            {
                var tr = from.AddTransition(to);
                tr.hasExitTime = hasExit;
                tr.exitTime = exitTime;
                tr.hasFixedDuration = true;
                tr.duration = duration;
                if (!string.IsNullOrEmpty(trigger))
                    tr.AddCondition(AnimatorConditionMode.If, 0f, trigger);
                return tr;
            }

            // locomotion & block entries
            T(loco, l1, "AttackL", duration: 0.045f);
            T(loco, heavy, "AttackH", duration: 0.06f);
            T(loco, skill, "Skill", duration: 0.05f);
            T(loco, roll, "Roll", duration: 0.05f);
            var toBlock = loco.AddTransition(block);
            toBlock.hasExitTime = false; toBlock.hasFixedDuration = true; toBlock.duration = 0.14f;
            toBlock.AddCondition(AnimatorConditionMode.If, 0f, "Blocking");
            var fromBlock = block.AddTransition(loco);
            fromBlock.hasExitTime = false; fromBlock.hasFixedDuration = true; fromBlock.duration = 0.16f;
            fromBlock.AddCondition(AnimatorConditionMode.IfNot, 0f, "Blocking");
            T(block, blockHit, "BlockImpact", duration: 0.05f);
            T(blockHit, block, null, hasExit: true, exitTime: 0.8f, duration: 0.12f);
            T(block, l1, "AttackL", duration: 0.08f);
            T(block, heavy, "AttackH", duration: 0.1f);
            T(block, skill, "Skill", duration: 0.08f);
            T(block, roll, "Roll", duration: 0.07f);

            // combo chain + returns
            T(l1, l2, "AttackL", duration: 0.06f);
            T(l2, l3, "AttackL", duration: 0.06f);
            T(l3, l4, "AttackL", duration: 0.06f);
            foreach (var atk in new[] { l1, l2, l3, l4, heavy, skill })
            {
                // late exit so buffered combo presses win the race against the
                // return-to-locomotion transition (motor hard-breaks at 0.92 anyway)
                T(atk, loco, null, hasExit: true, exitTime: 0.92f, duration: 0.14f);
                T(atk, roll, "Roll", duration: 0.06f);
            }

            T(roll, loco, null, hasExit: true, exitTime: 0.8f, duration: 0.12f);
            T(getHit, loco, null, hasExit: true, exitTime: 0.78f, duration: 0.15f);
            T(dizzy, loco, "Recover", duration: 0.2f);
            T(die, loco, "Respawn", duration: 0.05f);
            T(victory, victoryLoop, null, hasExit: true, exitTime: 0.93f, duration: 0.2f);
            T(victory, loco, "Respawn", duration: 0.05f);
            T(victoryLoop, loco, "Respawn", duration: 0.05f);

            AnimatorStateTransition Any(AnimatorState to, string trigger, float duration)
            {
                var tr = sm.AddAnyStateTransition(to);
                tr.hasExitTime = false;
                tr.hasFixedDuration = true;
                tr.duration = duration;
                tr.canTransitionToSelf = false;
                tr.AddCondition(AnimatorConditionMode.If, 0f, trigger);
                return tr;
            }
            Any(getHit, "Hit", 0.06f);
            Any(dizzy, "Stagger", 0.1f);
            Any(die, "Die", 0.08f);
            Any(victory, "Victory", 0.2f);

            EditorUtility.SetDirty(ctrl);

            // --- per-class overrides
            var result = new Dictionary<ClassId, AnimatorOverrideController>();
            var suffixes = new Dictionary<ClassId, string>
            {
                { ClassId.Knight, "SwordShield" },
                { ClassId.Greatsword, "SingleTwohandSword" },
                { ClassId.Duelist, "DoubleSword" },
                { ClassId.Mage, "MagicWand" },
                // the hammer wears the two-hander set — overhead cleaves read as
                // hammer strikes; the clips are Humanoid so any rig can wear them
                { ClassId.Warhammer, "SingleTwohandSword" },
            };
            var folders = new Dictionary<ClassId, string>
            {
                { ClassId.Knight, $"{AnimRoot}/SwordShield" },
                { ClassId.Greatsword, $"{AnimRoot}/SingleTwoHandSword" },
                { ClassId.Duelist, $"{AnimRoot}/DoubleSwords" },
                { ClassId.Mage, $"{AnimRoot}/MagicWand" },
                { ClassId.Warhammer, $"{AnimRoot}/SingleTwoHandSword" },
            };
            // Attack02_MagicWand is a forward point/thrust -> the bolt (light).
            // Attack01_MagicWand is an overhead raise-and-slam -> the nova (heavy),
            // which reads as "raise the staff, slam, AoE erupts". Combo01/02 are unused
            // (ranged skips the melee chain) but map to the bolt clip for safety.
            var mageMap = new Dictionary<string, string>
            {
                { "NormalAttack01", "Attack02" }, { "Combo01", "Attack02" },
                { "Combo02", "Attack02" }, { "Combo03", "Attack02" },
                { "NormalAttack02", "Attack01" },
                { "Combo05", "Attack02Maintain" }, // Arcane Barrage channel pose
                { "Defend", "Idle" }, { "DefendHit", "GetHit" },
            };
            var noShieldMap = new Dictionary<string, string>
            {
                { "Defend", "Idle" }, { "DefendHit", "GetHit" },
            };

            foreach (var kv in suffixes)
            {
                var cls = kv.Key;
                var suffix = kv.Value;
                var clips = LoadClips(folders[cls]);
                var map = cls == ClassId.Mage ? mageMap : (cls == ClassId.Knight ? null : noShieldMap);

                var aoc = new AnimatorOverrideController(ctrl) { name = "Fighter_" + cls };
                var pairs = new List<KeyValuePair<AnimationClip, AnimationClip>>();
                aoc.GetOverrides(pairs);
                for (int i = 0; i < pairs.Count; i++)
                {
                    var baseClip = pairs[i].Key;
                    if (baseClip == null) continue;
                    string stem = baseClip.name.Replace("_SwordShield", "");
                    if (map != null && map.TryGetValue(stem, out var mapped)) stem = mapped;
                    string targetName = stem + "_" + suffix;
                    if (!clips.TryGetValue(targetName, out var replacement))
                    {
                        // fall back to matching the file-name stem
                        replacement = clips.FirstOrDefault(kv =>
                            kv.Key.StartsWith(stem + "_", System.StringComparison.OrdinalIgnoreCase)).Value;
                    }
                    if (replacement != null)
                        pairs[i] = new KeyValuePair<AnimationClip, AnimationClip>(baseClip, replacement);
                    else if (cls != ClassId.Knight)
                        Debug.LogWarning($"[CrownfallForge] Missing clip {targetName} for {cls}; keeping base.");
                }
                aoc.ApplyOverrides(pairs);

                string aocPath = $"{GenDir}/Fighter_{cls}.overrideController";
                AssetDatabase.DeleteAsset(aocPath);
                AssetDatabase.CreateAsset(aoc, aocPath);
                result[cls] = aoc;
            }

            AssetDatabase.SaveAssets();
            return result;
        }

        // ================================================================== scene

        struct FighterSpec
        {
            public ClassId cls;
            public ElementId element;
            public Team team;
            public string name;
            public string skin;
            public int slot;
            public bool isPlayerVariant;
            public float aggression;
        }

        static void BuildScene(Dictionary<ClassId, AnimatorOverrideController> overrides)
        {
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            BuildLighting();
            var env = BuildEnvironment();
            BuildNavMesh();
            BuildPost();

            // ---- camera
            var camGo = new GameObject("Main Camera");
            camGo.tag = "MainCamera";
            var cam = camGo.AddComponent<Camera>();
            cam.fieldOfView = 57f;
            cam.nearClipPlane = 0.08f;
            cam.farClipPlane = 400f;
            camGo.AddComponent<AudioListener>();
            var camData = cam.GetUniversalAdditionalCameraData();
            camData.renderPostProcessing = true;
            camData.antialiasing = AntialiasingMode.SubpixelMorphologicalAntiAliasing;
            camGo.AddComponent<OrbitCamera>();
            camGo.transform.position = new Vector3(15f, 8.5f, -21f);
            camGo.transform.LookAt(new Vector3(0f, 1.2f, 0f));

            // ---- managers
            var managers = new GameObject("Match");
            var mm = managers.AddComponent<MatchManager>();
            var fx = managers.AddComponent<GameEffects>();
            var hud = managers.AddComponent<HUDController>();
            var touch = managers.AddComponent<TouchController>();
            WireEffects(fx);
            WireKit(hud);
            touch.circleSprite = hud.frameCircle;
            // designed dark circle face — color coding lives on the glyphs now
            touch.btnRound = LoadSprite($"{LayerLabSprites}/Button/Button_Circle128_Dark.png");
            touch.joyRing = LoadSprite($"{LayerLabSprites}/Frame/BorderFrame_Circle81.png");
            const string TPicto = LayerLabSprites + "/Icon_PictoIcons/128";
            touch.iconAttack = LoadSprite($"{TPicto}/Pictoicon_Sword.png");
            touch.iconDodge = LoadSprite($"{TPicto}/Pictoicon_Boot_Fly.png");
            touch.iconBlock = LoadSprite($"{TPicto}/Pictoicon_Shield.png");
            touch.iconLock = LoadSprite($"{TPicto}/Pictoicon_Target.png");
            touch.iconAuto = LoadSprite($"{TPicto}/Pictoicon_Control_Play.png");
            touch.iconSkill = LoadSprite($"{TPicto}/Pictoicon_Magic.png");
            touch.font = hud.fontSmall;
            EditorUtility.SetDirty(touch);

            var esGo = new GameObject("EventSystem");
            esGo.AddComponent<EventSystem>();
            esGo.AddComponent<InputSystemUIInputModule>();

            // ---- spawns
            var spawnRoot = new GameObject("Spawns").transform;
            Transform Spawn(string n, Vector3 pos, float yaw)
            {
                var t = new GameObject(n).transform;
                t.SetParent(spawnRoot);
                t.SetPositionAndRotation(pos, Quaternion.Euler(0f, yaw, 0f));
                return t;
            }
            var azure = new[]
            {
                Spawn("Azure0", new Vector3(0f, 0f, -16.5f), 0f),
                Spawn("Azure1", new Vector3(-3.4f, 0f, -17.2f), 0f),
                Spawn("Azure2", new Vector3(3.4f, 0f, -17.2f), 0f),
            };
            var crimson = new[]
            {
                Spawn("Crimson0", new Vector3(0f, 0f, 16.5f), 180f),
                Spawn("Crimson1", new Vector3(-3.4f, 0f, 17.2f), 180f),
                Spawn("Crimson2", new Vector3(3.4f, 0f, 17.2f), 180f),
            };
            mm.azureSpawns = azure.Select(t => t).ToArray();
            mm.crimsonSpawns = crimson.Select(t => t).ToArray();

            // ---- fighters
            var specs = new List<FighterSpec>
            {
                new FighterSpec { cls = ClassId.Knight, element = ElementId.Light, team = Team.Azure, name = "Kael", skin = HeroSkins[ClassId.Knight], slot = 0, isPlayerVariant = true },
                new FighterSpec { cls = ClassId.Greatsword, element = ElementId.Earth, team = Team.Azure, name = "Doran", skin = HeroSkins[ClassId.Greatsword], slot = 0, isPlayerVariant = true },
                new FighterSpec { cls = ClassId.Duelist, element = ElementId.Storm, team = Team.Azure, name = "Vesper", skin = HeroSkins[ClassId.Duelist], slot = 0, isPlayerVariant = true },
                new FighterSpec { cls = ClassId.Mage, element = ElementId.Frost, team = Team.Azure, name = "Elyra", skin = HeroSkins[ClassId.Mage], slot = 0, isPlayerVariant = true },
                new FighterSpec { cls = ClassId.Warhammer, element = ElementId.Storm, team = Team.Azure, name = "Volt", skin = HeroSkins[ClassId.Warhammer], slot = 0, isPlayerVariant = true },

                new FighterSpec { cls = ClassId.Greatsword, element = ElementId.Earth, team = Team.Azure, name = "Bram", skin = HeroSkinsAlt[ClassId.Greatsword], slot = 1, aggression = 0.6f },
                new FighterSpec { cls = ClassId.Mage, element = ElementId.Frost, team = Team.Azure, name = "Lyra", skin = HeroSkinsAlt[ClassId.Mage], slot = 2, aggression = 0.55f },

                new FighterSpec { cls = ClassId.Knight, element = ElementId.Shadow, team = Team.Crimson, name = "Vex", skin = HeroSkinsAlt[ClassId.Knight], slot = 0, aggression = 0.62f },
                new FighterSpec { cls = ClassId.Duelist, element = ElementId.Fire, team = Team.Crimson, name = "Sable", skin = HeroSkinsAlt[ClassId.Duelist], slot = 1, aggression = 0.7f },
                new FighterSpec { cls = ClassId.Mage, element = ElementId.Arcane, team = Team.Crimson, name = "Morgath", skin = HeroSkinsAlt[ClassId.Mage], slot = 2, aggression = 0.58f },
            };

            var playerVariants = new GameObject[5];
            int pv = 0;
            foreach (var spec in specs)
            {
                var spawn = (spec.team == Team.Azure ? azure : crimson)[spec.slot];
                var rig = BuildFighter(spec, overrides[spec.cls], spawn.position, spawn.rotation);
                if (spec.isPlayerVariant)
                {
                    playerVariants[pv++] = rig;
                    rig.SetActive(false);
                }
                else
                {
                    // AI understudies are master-driven networked puppets online
                    AddFighterNetView(rig);
                }
            }
            mm.playerVariants = playerVariants;

            // ---- network services: match authority view + connection service
            var link = mm.gameObject.AddComponent<NetMatchLink>();
            var mmView = mm.gameObject.AddComponent<PhotonView>();
            mmView.OwnershipTransfer = OwnershipOption.Fixed;
            mmView.Synchronization = ViewSynchronization.UnreliableOnChange;
            mmView.observableSearch = PhotonView.ObservableSearch.Manual;
            mmView.ObservedComponents = new List<Component> { link };
            new GameObject("CrownfallNet").AddComponent<CrownfallNet>();

            EditorUtility.SetDirty(mm);
            EditorSceneManager.SaveScene(scene, ScenePath);

            // the arena always sits right after the menu scene, wherever it is;
            // with no menu in the list yet it takes slot 0 (dev bootstrap)
            var list = EditorBuildSettings.scenes.Where(s => s.path != ScenePath).ToList();
            int menuAt = list.FindIndex(s => s.path.EndsWith("CrownfallMenu.unity"));
            if (menuAt < 0)
                Debug.LogWarning("[CrownfallForge] CrownfallMenu.unity missing from Build Settings — run Crownfall/Build Menu Scene or standalone builds boot into a UI-less arena.");
            list.Insert(menuAt + 1, new EditorBuildSettingsScene(ScenePath, true));
            EditorBuildSettings.scenes = list.ToArray();
        }

        // ------------------------------------------------------------------ lighting / post

        static void BuildLighting()
        {
            var sunGo = new GameObject("Sun");
            var sun = sunGo.AddComponent<Light>();
            sun.type = LightType.Directional;
            sun.color = new Color(1f, 0.95f, 0.82f);
            sun.intensity = 1.35f;
            sun.shadows = LightShadows.Soft;
            sun.shadowStrength = 0.9f;
            sunGo.transform.rotation = Quaternion.Euler(40f, 143f, 0f);

            RenderSettings.sun = sun;
            RenderSettings.ambientMode = AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = new Color(0.55f, 0.6f, 0.72f);
            RenderSettings.ambientEquatorColor = new Color(0.4f, 0.38f, 0.38f);
            RenderSettings.ambientGroundColor = new Color(0.17f, 0.15f, 0.13f);
            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.ExponentialSquared;
            RenderSettings.fogDensity = 0.006f;
            RenderSettings.fogColor = new Color(0.72f, 0.7f, 0.65f);

            string skyPath = $"{GenDir}/CrownfallSky.mat";
            var sky = AssetDatabase.LoadAssetAtPath<Material>(skyPath);
            if (sky == null)
            {
                sky = new Material(Shader.Find("Skybox/Procedural"));
                AssetDatabase.CreateAsset(sky, skyPath);
            }
            sky.SetFloat("_SunSize", 0.045f);
            sky.SetFloat("_AtmosphereThickness", 1.18f);
            sky.SetColor("_SkyTint", new Color(0.6f, 0.68f, 0.85f));
            sky.SetColor("_GroundColor", new Color(0.36f, 0.31f, 0.27f));
            sky.SetFloat("_Exposure", 1.35f);
            RenderSettings.skybox = sky;

            var pipeline = GraphicsSettings.defaultRenderPipeline as UniversalRenderPipelineAsset;
            if (pipeline != null)
            {
                try
                {
                    pipeline.supportsHDR = true;
                    pipeline.shadowDistance = 60f;
                    EditorUtility.SetDirty(pipeline);
                }
                catch (System.Exception e) { Debug.LogWarning("[CrownfallForge] URP asset tweak skipped: " + e.Message); }
            }
        }

        static void BuildPost()
        {
            string path = $"{GenDir}/CrownfallPost.asset";
            AssetDatabase.DeleteAsset(path);
            var profile = ScriptableObject.CreateInstance<VolumeProfile>();
            AssetDatabase.CreateAsset(profile, path);

            T Add<T>() where T : VolumeComponent
            {
                var comp = profile.Add<T>(true);
                AssetDatabase.AddObjectToAsset(comp, profile);
                return comp;
            }

            var bloom = Add<Bloom>();
            bloom.intensity.value = 0.65f;
            bloom.threshold.value = 0.92f;
            bloom.scatter.value = 0.62f;

            var vig = Add<Vignette>();
            vig.intensity.value = 0.26f;
            vig.smoothness.value = 0.42f;

            var ca = Add<ColorAdjustments>();
            ca.postExposure.value = 0.08f;
            ca.contrast.value = 10f;
            ca.saturation.value = 5f;

            var tone = Add<Tonemapping>();
            tone.mode.value = TonemappingMode.ACES;

            var wb = Add<WhiteBalance>();
            wb.temperature.value = 8f;

            AssetDatabase.SaveAssets();

            var volGo = new GameObject("PostFX");
            var vol = volGo.AddComponent<Volume>();
            vol.isGlobal = true;
            vol.priority = 10f;
            vol.sharedProfile = profile;
        }

        // ------------------------------------------------------------------ environment

        static Transform envRoot;

        static GameObject BuildEnvironment()
        {
            var root = new GameObject("Environment");
            envRoot = root.transform;

            var floor = FindPrefab("Battle Arena", "Floor1");
            var floorAlt = FindPrefab("Battle Arena", "Floor2");
            float tile = 4f;
            if (floor != null)
            {
                var b = MeasureBounds(floor);
                if (b.size.x > 0.5f) tile = b.size.x;
            }

            // square arena floor ~46x46 (uniform tile; alt tile only as rare accent)
            int half = Mathf.CeilToInt(23f / tile);
            for (int gx = -half; gx <= half; gx++)
                for (int gz = -half; gz <= half; gz++)
                {
                    bool accent = floorAlt != null && ((gx * 31 + gz * 17 + 1000) % 11 == 0);
                    var pf = accent ? floorAlt : floor;
                    Place(pf, new Vector3(gx * tile, 0f, gz * tile), ((gx * 7 + gz * 13) % 4) * 90f, 1f, true);
                }

            // fallback plane so the arena always has ground + nav geometry
            if (floor == null)
            {
                var plane = GameObject.CreatePrimitive(PrimitiveType.Plane);
                plane.transform.SetParent(envRoot);
                plane.transform.localScale = new Vector3(5f, 1f, 5f);
            }

            // perimeter walls with two gate gaps (north/south)
            var wall = FindPrefab("Battle Arena", "wall01");
            var wallAlt = FindPrefab("Battle Arena", "Mudwall01");
            float wallLen = 4f;
            if (wall != null)
            {
                var b = MeasureBounds(wall);
                if (b.size.x > 0.5f) wallLen = b.size.x;
            }
            float edge = 22f;
            int wcount = Mathf.CeilToInt(edge * 2f / wallLen);
            for (int i = 0; i <= wcount; i++)
            {
                float x = -edge + i * wallLen;
                bool gate = Mathf.Abs(x) < 3.4f;
                if (!gate)
                {
                    Place(wall, new Vector3(x, 0f, edge), 0f, 1f, true);
                    Place(wall, new Vector3(x, 0f, -edge), 180f, 1f, true);
                }
                Place(i % 3 == 0 && wallAlt != null ? wallAlt : wall, new Vector3(edge, 0f, -edge + i * wallLen), 90f, 1f, true);
                Place(i % 3 == 1 && wallAlt != null ? wallAlt : wall, new Vector3(-edge, 0f, -edge + i * wallLen), 270f, 1f, true);
            }

            var gatePrefab = FindPrefab("Battle Arena", "Gate") ?? FindPrefab("Battle Arena", "Entrance");
            Place(gatePrefab, new Vector3(0f, 0f, edge + 0.4f), 180f, 1f, true);
            Place(gatePrefab, new Vector3(0f, 0f, -edge - 0.4f), 0f, 1f, true);

            // cliff backdrop ring
            string[] cliffs = { "cliff01", "cliff02", "cliff03", "cliff04", "Cliff5", "Cliff6", "Cliff7", "Cliff8" };
            for (int i = 0; i < 16; i++)
            {
                float ang = i * 22.5f * Mathf.Deg2Rad;
                if (Mathf.Abs(Mathf.Cos(ang)) < 0.25f) continue; // leave sky at the gates
                var prefab = FindPrefab("Battle Arena", cliffs[i % cliffs.Length]);
                Vector3 pos = new Vector3(Mathf.Sin(ang), 0f, Mathf.Cos(ang)) * 31f;
                Place(prefab, pos, i * 22.5f + 180f, 1.25f + (i % 3) * 0.18f, true);
            }

            // pillars with torch fire (FireFlame is warm orange; StretchyFlame reads purple)
            string[] pillars = { "Pilar1", "Pilar2", "Pilar3", "Pilar4" };
            var flame = FindPrefab("MagicArsenal", "FireFlame") ?? FindPrefab("MagicArsenal", "StretchyFlame");
            Vector3[] pillarSpots =
            {
                new Vector3(-9f, 0f, -9f), new Vector3(9f, 0f, -9f),
                new Vector3(-9f, 0f, 9f), new Vector3(9f, 0f, 9f),
            };
            for (int i = 0; i < 4; i++)
            {
                var p = FindPrefab("Battle Arena", pillars[i]);
                var placed = Place(p, pillarSpots[i], i * 90f, 1f, true);
                if (placed == null) continue; // no floating fire over missing pillars
                var b = MeasureBoundsInstance(placed);
                float topY = b.size.y > 0.5f ? b.max.y - envRoot.position.y + 0.1f : 3.4f;
                Vector3 firePos = pillarSpots[i] + Vector3.up * topY;
                if (flame != null)
                {
                    var f = (GameObject)PrefabUtility.InstantiatePrefab(flame);
                    f.transform.SetParent(envRoot);
                    f.transform.position = firePos;
                    f.transform.localScale = Vector3.one * 0.6f;
                }
                var lightGo = new GameObject("TorchLight");
                lightGo.transform.SetParent(envRoot);
                lightGo.transform.position = firePos + Vector3.up * 0.4f;
                var pl = lightGo.AddComponent<Light>();
                pl.type = LightType.Point;
                pl.color = new Color(1f, 0.62f, 0.32f);
                pl.intensity = 2.4f;
                pl.range = 10f;
                pl.shadows = LightShadows.None;
            }

            // center crystals
            Place(FindPrefab("Battle Arena", "Crystal2"), new Vector3(0f, 0f, 0f), 0f, 1.1f, true);
            Place(FindPrefab("Battle Arena", "Crystal1"), new Vector3(1.8f, 0f, 0.9f), 40f, 0.8f, true);
            Place(FindPrefab("Battle Arena", "Crystal3"), new Vector3(-1.5f, 0f, -1.2f), 210f, 0.75f, true);

            // props
            var rand = new System.Random(7);
            float R(float a, float b) => Mathf.Lerp(a, b, (float)rand.NextDouble());
            string[] barrels = { "Barrel01", "Barrel02", "Box", "Box02" };
            for (int i = 0; i < 10; i++)
            {
                float side = i % 2 == 0 ? 1f : -1f;
                Vector3 pos = new Vector3(R(-19f, 19f), 0f, side * R(17.5f, 20f));
                if (Mathf.Abs(pos.x) < 4.5f) pos.x += Mathf.Sign(pos.x + 0.1f) * 5f;
                Place(FindPrefab("Battle Arena", barrels[i % barrels.Length]), pos, R(0f, 360f), R(0.85f, 1.1f), true);
            }
            Place(FindPrefab("Battle Arena", "Chariot"), new Vector3(-16f, 0f, 6f), 40f, 1f, true);
            string[] bones = { "Bone1", "Bone2", "skull1", "skull2" };
            for (int i = 0; i < 8; i++)
                Place(FindPrefab("Battle Arena", bones[i % bones.Length]),
                    new Vector3(R(-18f, 18f), 0f, R(-18f, 18f)), R(0f, 360f), R(0.4f, 0.6f), false);

            string[] rocks = { "Stone01", "Stone02", "Stone03", "rock1", "rock2" };
            for (int i = 0; i < 10; i++)
            {
                Vector3 pos = new Vector3(R(-19f, 19f), 0f, R(-19f, 19f));
                if (pos.magnitude < 5f || Mathf.Abs(pos.z) > 14f) continue;
                Place(FindPrefab("Battle Arena", rocks[i % rocks.Length]), pos, R(0f, 360f), R(0.7f, 1.1f), true);
            }

            // grass dressing (no colliders)
            string[] grass = { "Grass01", "Grass02", "Grass03", "Grass04", "SolGrass01", "SolGrass02", "SolGrass03", "Flower01", "Flower02" };
            for (int i = 0; i < 90; i++)
            {
                Vector3 pos = new Vector3(R(-20f, 20f), 0f, R(-20f, 20f));
                if (pos.magnitude < 3.5f) continue;
                Place(FindPrefab("Battle Arena", grass[i % grass.Length]), pos, R(0f, 360f), R(0.8f, 1.35f), false);
            }

            // gate braziers with flames seated on the barrel rim
            foreach (float z in new[] { -edge, edge })
            {
                foreach (float x in new[] { -4.6f, 4.6f })
                {
                    var barrel = Place(FindPrefab("Battle Arena", "Barrel01"), new Vector3(x, 0f, z * 0.985f), 0f, 0.9f, true);
                    if (barrel == null || flame == null) continue;
                    float rimY = MeasureBoundsInstance(barrel).max.y - envRoot.position.y - 0.05f;
                    var f = (GameObject)PrefabUtility.InstantiatePrefab(flame);
                    f.transform.SetParent(envRoot);
                    f.transform.position = new Vector3(x, rimY, z * 0.985f);
                    f.transform.localScale = Vector3.one * 0.45f;
                }
            }

            // concealment bush chunks: big dense clumps of tall grass you can
            // walk right into and disappear (Brawl-Stars style). Fewer, bigger
            // and much denser than scattered dressing so they read as real cover.
            var bushField = new GameObject("BushField").AddComponent<BushField>();
            bushField.transform.SetParent(root.transform);
            Vector2[] patchCenters =
            {
                new Vector2(-12.5f, -6f), new Vector2(12.5f, 6f),
                new Vector2(0f, 13.5f),   new Vector2(0f, -13.5f),
                new Vector2(-14f, 8f),    new Vector2(14f, -8f),
            };
            const float patchRadius = 3.3f;
            // SolGrass are the fat leafy tufts â€” best "bush" read; a few tall
            // blades mixed in break up the silhouette
            string[] bushKinds = { "SolGrass01", "SolGrass02", "SolGrass03", "SolGrass01", "Grass02" };
            var patchList = new List<Vector4>();
            foreach (var c in patchCenters)
            {
                patchList.Add(new Vector4(c.x, 0f, c.y, patchRadius));
                // dense fill: rings of tufts from center out to the rim so the
                // whole disc is solid green with no bald patches
                int clumps = 34;
                for (int i = 0; i < clumps; i++)
                {
                    float a = (float)rand.NextDouble() * Mathf.PI * 2f;
                    // bias toward the rim a touch so the edge reads as a wall of green
                    float r2 = Mathf.Pow((float)rand.NextDouble(), 0.7f) * (patchRadius - 0.15f);
                    Vector3 pos = new Vector3(c.x + Mathf.Cos(a) * r2, 0f, c.y + Mathf.Sin(a) * r2);
                    Place(FindPrefab("Battle Arena", bushKinds[i % bushKinds.Length]),
                        pos, R(0f, 360f), R(2.3f, 3.1f), false);
                }
            }
            bushField.patches = patchList.ToArray();
            EditorUtility.SetDirty(bushField);

            // mark env static for batching
            foreach (var tr in root.GetComponentsInChildren<Transform>(true))
                GameObjectUtility.SetStaticEditorFlags(tr.gameObject, StaticEditorFlags.BatchingStatic);

            return root;
        }

        static void BuildNavMesh()
        {
            var go = new GameObject("NavMesh");
            var surface = go.AddComponent<NavMeshSurface>();
            surface.collectObjects = CollectObjects.All;
            surface.useGeometry = NavMeshCollectGeometry.PhysicsColliders;
            surface.layerMask = Physics.DefaultRaycastLayers;
            surface.BuildNavMesh();

            // NavMeshData is [PreferBinarySerialization]; embedding it forces the
            // whole scene file binary, which git's text normalization then corrupts
            // on CI. Persist it as its own asset (marked binary in .gitattributes)
            // so the scene stays Force-Text YAML.
            string navPath = $"{GenDir}/CrownfallNavMesh.asset";
            AssetDatabase.DeleteAsset(navPath);
            AssetDatabase.CreateAsset(surface.navMeshData, navPath);
            EditorUtility.SetDirty(surface);
        }

        // ------------------------------------------------------------------ fighters

        /// PhotonView + sync component for a fighter that exists on the network
        /// (scene AI rigs and the per-class human prefabs).
        static void AddFighterNetView(GameObject rig)
        {
            var sync = rig.AddComponent<FighterNetSync>();
            var view = rig.AddComponent<PhotonView>();
            view.OwnershipTransfer = OwnershipOption.Fixed;
            view.Synchronization = ViewSynchronization.UnreliableOnChange;
            view.observableSearch = PhotonView.ObservableSearch.Manual;
            view.ObservedComponents = new List<Component> { sync };
        }

        /// Per-class fighter prefabs for PhotonNetwork.Instantiate, built by the
        /// same rig pipeline as the scene fighters. Humans only: no AI brain, no
        /// nav agent; team/name arrive as instantiation data at spawn.
        static void BuildNetPrefabs(Dictionary<ClassId, AnimatorOverrideController> overrides)
        {
            if (!AssetDatabase.IsValidFolder("Assets/Crownfall/Resources"))
                AssetDatabase.CreateFolder("Assets/Crownfall", "Resources");
            if (!AssetDatabase.IsValidFolder("Assets/Crownfall/Resources/Net"))
                AssetDatabase.CreateFolder("Assets/Crownfall/Resources", "Net");

            var netSpecs = new[]
            {
                new FighterSpec { cls = ClassId.Knight, element = ElementId.Light, team = Team.Azure, name = "Knight", skin = HeroSkins[ClassId.Knight], isPlayerVariant = true },
                new FighterSpec { cls = ClassId.Greatsword, element = ElementId.Earth, team = Team.Azure, name = "Warbrand", skin = HeroSkins[ClassId.Greatsword], isPlayerVariant = true },
                new FighterSpec { cls = ClassId.Duelist, element = ElementId.Storm, team = Team.Azure, name = "Duelist", skin = HeroSkins[ClassId.Duelist], isPlayerVariant = true },
                new FighterSpec { cls = ClassId.Mage, element = ElementId.Frost, team = Team.Azure, name = "Mage", skin = HeroSkins[ClassId.Mage], isPlayerVariant = true },
                new FighterSpec { cls = ClassId.Warhammer, element = ElementId.Storm, team = Team.Azure, name = "Juggernaut", skin = HeroSkins[ClassId.Warhammer], isPlayerVariant = true },
            };

            foreach (var spec in netSpecs)
            {
                var rig = BuildFighter(spec, overrides[spec.cls], Vector3.zero, Quaternion.identity);
                Object.DestroyImmediate(rig.GetComponent<AIController>());
                Object.DestroyImmediate(rig.GetComponent<NavMeshAgent>());
                var pc = rig.GetComponent<PlayerController>();
                if (pc != null) pc.enabled = false; // FighterNetSync enables for the owner
                AddFighterNetView(rig);
                rig.name = "Fighter_" + spec.cls;

                string path = $"Assets/Crownfall/Resources/Net/Fighter_{spec.cls}.prefab";
                AssetDatabase.DeleteAsset(path);
                PrefabUtility.SaveAsPrefabAsset(rig, path);
                Object.DestroyImmediate(rig);
            }
        }

        static GameObject BuildFighter(FighterSpec spec, AnimatorOverrideController aoc, Vector3 pos, Quaternion rot)
        {
            var kit = ClassKits.Get(spec.cls);
            var root = new GameObject($"{(spec.isPlayerVariant ? "Player_" : "")}{spec.name}_{spec.cls}");
            root.transform.SetPositionAndRotation(pos, rot);

            // model (external Dungeon Mason path or legacy modular-pack name)
            bool externalSkin = spec.skin.Contains("/");
            var charPrefab = LoadHeroPrefab(spec.skin);
            GameObject model;
            if (charPrefab != null)
            {
                model = (GameObject)PrefabUtility.InstantiatePrefab(charPrefab);
                PrefabUtility.UnpackPrefabInstance(model, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
            }
            else
            {
                Debug.LogError("[CrownfallForge] Missing character prefab " + spec.skin);
                model = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            }
            model.name = "Model";
            model.transform.SetParent(root.transform, false);
            model.transform.localPosition = Vector3.zero;
            model.transform.localRotation = Quaternion.identity;

            var anim = model.GetComponentInChildren<Animator>();
            if (anim == null) anim = model.AddComponent<Animator>();
            anim.runtimeAnimatorController = aoc;
            anim.applyRootMotion = false;
            anim.cullingMode = AnimatorCullingMode.AlwaysAnimate;

            if (externalSkin)
            {
                PrepareExternalHero(model, spec.cls);
            }
            else
            {
                // modular-pack weapons ship on loose "weaponShield_l/r" holder
                // nodes whose motion is baked per-clip — move them to hand bones
                ReparentWeaponsToHands(model.transform);
                if (spec.cls == ClassId.Warhammer) ApplyHammerLoadout(model.transform);
            }

            // physics + brain components
            var cc = root.AddComponent<CharacterController>();
            cc.center = new Vector3(0f, 0.95f, 0f);
            cc.height = 1.85f;
            cc.radius = 0.38f;
            cc.slopeLimit = 50f;
            cc.stepOffset = 0.4f;

            var id = root.AddComponent<CombatantIdentity>();
            id.displayName = spec.name;
            id.team = spec.team;
            id.classId = spec.cls;
            id.element = spec.element;
            id.isPlayer = spec.isPlayerVariant;

            root.AddComponent<Health>();
            root.AddComponent<Stamina>();
            var motor = root.AddComponent<CombatMotor>();

            var agent = root.AddComponent<NavMeshAgent>();
            agent.radius = 0.45f;
            agent.height = 1.9f;
            agent.speed = kit.runSpeed;
            agent.acceleration = 40f;
            agent.angularSpeed = 0f;
            agent.stoppingDistance = 1.1f;
            agent.autoBraking = false;
            agent.obstacleAvoidanceType = ObstacleAvoidanceType.LowQualityObstacleAvoidance;
            agent.avoidancePriority = 30 + spec.slot * 5 + (spec.team == Team.Crimson ? 3 : 0);

            var ai = root.AddComponent<AIController>();
            ai.aggression = spec.aggression > 0f ? spec.aggression : 0.6f;
            if (spec.isPlayerVariant)
            {
                ai.enabled = false;
                root.AddComponent<PlayerController>();
            }

            // weapon: trail + enchant aura
            var weapon = FindWeaponTransform(model.transform);
            if (weapon != null)
            {
                var wb = RendererBounds(weapon);
                Vector3 tipPos = FarthestCorner(wb, root.transform.position + Vector3.up * 1.2f);

                var tipGo = new GameObject("WeaponTip");
                tipGo.transform.SetParent(weapon, true);
                tipGo.transform.position = tipPos;
                motor.weaponTip = tipGo.transform;

                var trailGo = new GameObject("Trail");
                trailGo.transform.SetParent(weapon, true);
                trailGo.transform.position = Vector3.Lerp(wb.center, tipPos, 0.65f);
                var trail = trailGo.AddComponent<TrailRenderer>();
                trail.time = 0.16f;
                trail.minVertexDistance = 0.06f;
                trail.startWidth = 0.32f;
                trail.endWidth = 0.02f;
                trail.emitting = false;
                trail.numCapVertices = 4;
                var trailMat = new Material(Shader.Find("Sprites/Default"));
                Color ec = ElementColors.Get(spec.element);
                trail.material = trailMat;
                var grad = new Gradient();
                grad.SetKeys(
                    new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(ec, 0.25f), new GradientColorKey(ec, 1f) },
                    new[] { new GradientAlphaKey(0.85f, 0f), new GradientAlphaKey(0.5f, 0.5f), new GradientAlphaKey(0f, 1f) });
                trail.colorGradient = grad;
                motor.weaponTrail = trail;

                var enchant = FindPrefab("MagicArsenal", ElementNames[(int)spec.element] + "Enchant");
                if (enchant != null)
                {
                    var e = (GameObject)PrefabUtility.InstantiatePrefab(enchant);
                    e.transform.SetParent(weapon, true);
                    e.transform.position = Vector3.Lerp(wb.center, tipPos, 0.4f);
                    e.transform.localScale = Vector3.one * 0.55f;
                    motor.enchantFx = e;
                }
            }

            BuildTeamRing(root.transform, id.TeamColor);
            BuildWorldBar(root.transform, id.team);
            root.AddComponent<HitFlash>();

            SetLayerRecursive(root, 2); // Ignore Raycast: cameras/projectiles ignore, melee overlap still hits
            return root;
        }

        static void BuildTeamRing(Transform root, Color color)
        {
            var quad = GameObject.CreatePrimitive(PrimitiveType.Quad);
            Object.DestroyImmediate(quad.GetComponent<Collider>());
            quad.name = "TeamRing";
            quad.transform.SetParent(root, false);
            quad.transform.localPosition = new Vector3(0f, 0.06f, 0f);
            quad.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
            quad.transform.localScale = Vector3.one * 2.1f;

            var mat = new Material(Shader.Find("Sprites/Default"));
            mat.mainTexture = GetRingTexture();
            mat.color = new Color(color.r, color.g, color.b, 0.8f);
            quad.GetComponent<MeshRenderer>().sharedMaterial = mat;
            quad.GetComponent<MeshRenderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        }

        static Texture2D ringTexCache;
        static Texture2D GetRingTexture()
        {
            string path = $"{GenDir}/TeamRing.asset";
            if (ringTexCache == null) ringTexCache = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
            if (ringTexCache != null) return ringTexCache;

            int n = 128;
            var tex = new Texture2D(n, n, TextureFormat.RGBA32, false);
            for (int y = 0; y < n; y++)
                for (int x = 0; x < n; x++)
                {
                    float dx = (x - n / 2f) / (n / 2f);
                    float dy = (y - n / 2f) / (n / 2f);
                    float r = Mathf.Sqrt(dx * dx + dy * dy);
                    float band = Mathf.Clamp01(1f - Mathf.Abs(r - 0.72f) / 0.16f);
                    band *= band;
                    if (r > 0.98f) band = 0f;
                    tex.SetPixel(x, y, new Color(1f, 1f, 1f, band));
                }
            tex.Apply();
            AssetDatabase.CreateAsset(tex, path);
            ringTexCache = tex;
            return tex;
        }

        static void BuildWorldBar(Transform root, Team team)
        {
            var barGo = new GameObject("WorldBar", typeof(Canvas));
            barGo.transform.SetParent(root, false);
            barGo.transform.localPosition = new Vector3(0f, 2.1f, 0f);
            var canvas = barGo.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.WorldSpace;
            var rt = barGo.GetComponent<RectTransform>();
            rt.sizeDelta = new Vector2(120f, 16f);
            barGo.transform.localScale = Vector3.one * 0.0145f;
            var group = barGo.AddComponent<CanvasGroup>();

            // fillAmount is silently ignored on sprite-less Images, so the fills
            // MUST carry a real sprite or the bar renders full forever
            var bgSprite = LoadSprite($"{LayerLabSprites}/Slider/Slider_Basic01_Bg.png");
            var ghostSprite = LoadSprite($"{LayerLabSprites}/Slider/Slider_Basic01_Fill_White.png");
            // designed pre-colored fills per team; the pack has no Basic01 red,
            // so Crimson borrows Basic04's designed red
            var fillSprite = team == Team.Azure
                ? LoadSprite($"{LayerLabSprites}/Slider/Slider_Basic01_Fill_Blue.png")
                : LoadSprite($"{LayerLabSprites}/Slider/Slider_Basic04_Fill_Red.png");

            Image MakeImg(string n, Color c, Vector2 size, Sprite sprite)
            {
                var go = new GameObject(n, typeof(RectTransform));
                var r = go.GetComponent<RectTransform>();
                r.SetParent(barGo.transform, false);
                r.sizeDelta = size;
                var img = go.AddComponent<Image>();
                img.sprite = sprite;
                img.color = c;
                img.raycastTarget = false;
                return img;
            }

            MakeImg("Bg", Color.white, new Vector2(120f, 16f), bgSprite);
            var ghost = MakeImg("Ghost", new Color(1f, 0.85f, 0.7f, 0.9f), new Vector2(114f, 11f), ghostSprite);
            ghost.type = Image.Type.Filled;
            ghost.fillMethod = Image.FillMethod.Horizontal;
            var fill = MakeImg("Fill", Color.white, new Vector2(114f, 11f), fillSprite);
            fill.type = Image.Type.Filled;
            fill.fillMethod = Image.FillMethod.Horizontal;

            var bar = barGo.AddComponent<WorldHealthBar>();
            bar.health = root.GetComponent<Health>();
            bar.fill = fill;
            bar.ghost = ghost;
            bar.group = group;
            // relation recolor at runtime swaps DESIGNED sprites, never tints:
            // allies read green, enemies red, from the local player's seat
            bar.allyFill = LoadSprite($"{LayerLabSprites}/Slider/Slider_Basic04_Fill_Green.png");
            bar.enemyFill = LoadSprite($"{LayerLabSprites}/Slider/Slider_Basic04_Fill_Red.png");
        }

        // ------------------------------------------------------------------ wiring

        internal static void WireEffects(GameEffects fx)
        {
            var sets = new List<GameEffects.ElementSet>();
            for (int i = 0; i < ElementNames.Length; i++)
            {
                string el = ElementNames[i];
                sets.Add(new GameEffects.ElementSet
                {
                    id = (ElementId)i,
                    slashHit = FindPrefab("MagicArsenal", el + "SlashHit"),
                    missile = FindPrefab("MagicArsenal", el + "MissileNormal"),
                    explosion = FindPrefab("MagicArsenal", el + "ExplosionNormal"),
                    muzzle = FindPrefab("MagicArsenal", el + "MuzzleBig"),
                    nova = FindPrefab("MagicArsenal", "Nova" + el),
                    enchant = FindPrefab("MagicArsenal", el + "Enchant"),
                    charge = FindPrefab("MagicArsenal", el + "Charge"),
                    slash = FindPrefab("MagicArsenal", el + "Slash"),
                    cleave = FindPrefab("MagicArsenal", el + "Cleave"),
                    sphereBlast = FindPrefab("MagicArsenal", el + "SphereBlast"),
                    pillar = FindPrefab("MagicArsenal", el + "PillarBlast"),
                    castSound = FindClip("magic_cast_" + el.ToLowerInvariant()),
                    impactSound = FindClip(el.ToLowerInvariant() + "impact"),
                });
            }
            fx.elements = sets.ToArray();
            fx.respawnFlash = FindPrefab("MagicArsenal", "NovaLife");
            fx.stunFx = FindPrefab("MagicArsenal", "LightOrbitSphere");

            var dmgGo = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/DamageNumbersPro/Demo/Prefabs/3D/Red Shadow.prefab");
            if (dmgGo != null) fx.damageNumberPrefab = dmgGo.GetComponent<DamageNumber>();
            var blockGo = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/DamageNumbersPro/Demo/Prefabs/3D/Clear.prefab");
            if (blockGo != null) fx.blockedNumberPrefab = blockGo.GetComponent<DamageNumber>();

            fx.swingLight = FindClip("Fly2");
            fx.swingHeavy = FindClip("Fly3");
            fx.meleeImpact = FindClip("genericimpact");
            fx.blockImpact = FindClip("earthimpact");
            fx.rollWhoosh = FindClip("Fly");
            fx.deathCry = FindClip("Cry2");
            fx.uiTick = FindClip("magic_cast_generic02");
            fx.uiFight = FindClip("Explosion1");
            fx.uiVictory = FindClip("magic_cast_light");
            fx.uiDefeat = FindClip("magic_cast_shadow");
            fx.killDing = FindClip("lightimpact2");
            EditorUtility.SetDirty(fx);
        }

        /// Wire the shared UiKit theme (fonts + designed sprite table) onto any
        /// UI host — the arena's HUDController or the menu scene's MenuHud.
        internal static void WireKit(UiKit hud)
        {
            // the "Outline" variants have hollow glyph faces â€” unreadable at title
            // sizes; the plain SDF is the filled face
            hud.fontBig = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>($"{LayerLabFonts}/LilitaOne-Regular SDF.asset");
            hud.fontMid = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>($"{LayerLabFonts}/LilitaOne-Regular Outline_Extended ASCII_72 SDF.asset");
            hud.fontSmall = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>($"{LayerLabFonts}/LilitaOne-Regular Outline_Extended ASCII_40 SDF.asset");
            if (hud.fontBig == null) hud.fontBig = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>($"{LayerLabFonts}/LilitaOne-Regular Outline 210 SDF.asset");
            if (hud.fontMid == null) hud.fontMid = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>($"{LayerLabFonts}/LilitaOne-Regular Outline 72 SDF.asset");
            if (hud.fontSmall == null) hud.fontSmall = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>($"{LayerLabFonts}/LilitaOne-Regular Outline 40 SDF.asset");

            hud.barBgBasic = LoadSprite($"{LayerLabSprites}/Slider/Slider_Basic01_Bg.png");
            hud.barFillBasic = LoadSprite($"{LayerLabSprites}/Slider/Slider_Basic01_Fill_White.png");
            hud.bar4Bg = LoadSprite($"{LayerLabSprites}/Slider/Slider_Basic04_Bg.png");
            hud.bar4FillRed = LoadSprite($"{LayerLabSprites}/Slider/Slider_Basic04_Fill_Red.png");
            hud.bar4FillWhite = LoadSprite($"{LayerLabSprites}/Slider/Slider_Basic04_Fill_White.png");
            hud.bar4Divider = LoadSprite($"{LayerLabSprites}/Slider/Slider_Basic04_DividerLine.png");
            hud.bar4Gloss = LoadSprite($"{LayerLabSprites}/Slider/Slider_Basic04_Fill_Highlight.png");

            hud.frameRound = LoadSprite($"{LayerLabSprites}/Frame/BasicFrame_Round12.png");
            hud.frameCircle = LoadSprite($"{LayerLabSprites}/Frame/BasicFrame_Circle77.png");
            hud.bannerNavy = LoadSprite($"{LayerLabSprites}/Frame/BannerFrame01_Single_Navy.png");
            hud.plateRound = LoadSprite($"{LayerLabSprites}/Label/Label_Round01_White.png");
            hud.popupNavy = LoadSprite($"{LayerLabSprites}/Popup/Popup01_Single_Navy.png");
            hud.ribbonBlue = LoadSprite($"{LayerLabSprites}/Label/Title_Ribbon_Bg_Blue.png");
            hud.ribbonOrange = LoadSprite($"{LayerLabSprites}/Label/Title_Ribbon_Bg_Orange.png");
            hud.ribbonYellow = LoadSprite($"{LayerLabSprites}/Label/Title_Ribbon_Bg_Yellow.png");
            hud.cardKnight = LoadSprite($"{LayerLabSprites}/Frame/StageFrame_Single_Bg_n_Blue.png");
            hud.cardWarbrand = LoadSprite($"{LayerLabSprites}/Frame/StageFrame_Single_Bg_n_Yellow.png");
            hud.cardDuelist = LoadSprite($"{LayerLabSprites}/Frame/StageFrame_Single_Bg_n.png");
            hud.cardMage = LoadSprite($"{LayerLabSprites}/Frame/StageFrame_Single_Bg_n_Purple.png");
            hud.profileRing = LoadSprite($"{LayerLabSprites}/Frame/ProfileFrame01_Border.png");
            hud.profileInner = LoadSprite($"{LayerLabSprites}/Frame/ProfileFrame01_Inner_Blue.png");
            hud.trapBlue = LoadSprite($"{LayerLabSprites}/Label/Label_Trapezoid_Single_Blue.png");
            hud.trapOrange = LoadSprite($"{LayerLabSprites}/Label/Label_Trapezoid_Single_Orange.png");

            hud.resourcePill = LoadSprite($"{LayerLabSprites}/UI_Etc/ResourceBar_Bg.png");
            hud.resourceBtnGreen = LoadSprite($"{LayerLabSprites}/UI_Etc/ResourceBar_Btn_Single_Green.png");
            hud.resourceAdd = LoadSprite($"{LayerLabSprites}/UI_Etc/ResourceBar_Btn_Icon_Add.png");
            hud.iconCoinBar = LoadSprite($"{LayerLabSprites}/UI_Etc/ResourceBar_Icon_Coin.png");
            hud.iconGemBar = LoadSprite($"{LayerLabSprites}/UI_Etc/ResourceBar_Icon_Gem_Blue.png");
            hud.alertDot = LoadSprite($"{LayerLabSprites}/UI_Etc/Alert_Dot_Bg.png");
            hud.squareBlue = LoadSprite($"{LayerLabSprites}/Button/Button_Square05_Blue.png");
            hud.menuShop = LoadSprite($"{LayerLabSprites}/IconMisc/Icon_MenuIcon02_Shop.png");
            hud.menuCards = LoadSprite($"{LayerLabSprites}/IconMisc/Icon_MenuIcon02_Cards.png");
            hud.menuInbox = LoadSprite($"{LayerLabSprites}/IconMisc/Icon_MenuIcon04_Inbox.png");
            hud.menuGift = LoadSprite($"{LayerLabSprites}/IconMisc/Icon_MenuIcon04_Reward.png");
            hud.menuTrophy = LoadSprite($"{LayerLabSprites}/IconMisc/Icon_MenuIcon04_Trophy.png");
            hud.iconChestGold = LoadSprite($"{LayerLabSprites}/IconMisc/Icon_ChestIcon_Gold01_l.png");
            hud.iconCoinBig = LoadSprite($"{LayerLabSprites}/IconMisc/Icon_ImageIcon_Coin01_l.png");
            hud.iconPouch = LoadSprite($"{LayerLabSprites}/IconMisc/Icon_ImageIcon_GoldPouch.png");
            hud.levelBadge = LoadSprite($"{LayerLabSprites}/Slider/Slider_Level02_Badge.png");
            hud.focusFrame = LoadSprite($"{LayerLabSprites}/Frame/StageFrame_Single_Focus.png");

            hud.btnGreen = LoadSprite($"{LayerLabSprites}/Button/Button01_225_Green.png");
            hud.btnBlue = LoadSprite($"{LayerLabSprites}/Button/Button01_225_Blue.png");
            hud.btnYellow = LoadSprite($"{LayerLabSprites}/Button/Button01_225_Yellow.png");
            hud.btnRed = LoadSprite($"{LayerLabSprites}/Button/Button01_225_Red.png");
            hud.btnGray = LoadSprite($"{LayerLabSprites}/Button/Button01_225_BlueGray.png");
            hud.btnCircle = LoadSprite($"{LayerLabSprites}/Button/Button_Circle147_Navy.png");

            hud.switchOn = LoadSprite($"{LayerLabSprites}/UI_Etc/Switch_Bg_On.png");
            hud.switchOff = LoadSprite($"{LayerLabSprites}/UI_Etc/Switch_Bg_Off.png");
            hud.knobOn = LoadSprite($"{LayerLabSprites}/UI_Etc/Switch_Handle_On.png");
            hud.knobOff = LoadSprite($"{LayerLabSprites}/UI_Etc/Switch_Handle_Off.png");
            hud.knobWhite = LoadSprite($"{LayerLabSprites}/UI_Etc/Switch_Handle_White.png");

            const string Picto = LayerLabSprites + "/Icon_PictoIcons/128";
            hud.iconCrown = LoadSprite($"{LayerLabSprites}/IconMisc/Icon_ImageIcon_Crown_Gold.png");
            hud.icoShield = LoadSprite($"{Picto}/Pictoicon_Shield.png");
            hud.icoAxe = LoadSprite($"{Picto}/Pictoicon_Axe.png");
            hud.icoSword = LoadSprite($"{Picto}/Pictoicon_Sword.png");
            hud.icoWand = LoadSprite($"{Picto}/Pictoicon_Wand_0.png");
            hud.icoSkill = LoadSprite($"{Picto}/Pictoicon_Magic.png");
            hud.icoPlay = LoadSprite($"{Picto}/Pictoicon_Control_Play.png");
            hud.icoMovie = LoadSprite($"{Picto}/Pictoicon_Movie.png");
            hud.icoGear = LoadSprite($"{Picto}/Pictoicon_Setting.png");
            hud.icoPower = LoadSprite($"{Picto}/Pictoicon_Poweroff.png");
            hud.icoPause = LoadSprite($"{Picto}/Pictoicon_Control_Pause.png");
            hud.icoHome = LoadSprite($"{Picto}/Pictoicon_Home_0.png");
            hud.icoRefresh = LoadSprite($"{Picto}/Pictoicon_Refresh.png");
            hud.icoTarget = LoadSprite($"{Picto}/Pictoicon_Target.png");
            hud.icoSkull = LoadSprite($"{Picto}/Pictoicon_Skull.png");
            hud.icoTimer = LoadSprite($"{Picto}/Pictoicon_Time.png");
            hud.icoVolume = LoadSprite($"{Picto}/Pictoicon_Volume.png");
            hud.icoCamera = LoadSprite($"{Picto}/Pictoicon_Camera.png");
            hud.icoShake = LoadSprite($"{Picto}/Pictoicon_Haptic.png");
            hud.icoClose = LoadSprite($"{Picto}/PictoIcon_Close.png");
            hud.icoCheck = LoadSprite($"{Picto}/PictoIcon_Check.png");
            hud.icoBack = LoadSprite($"{Picto}/Pictoicon_Arrow_Backward.png");

            // ---- 2026-07 UI redesign: backdrops, modal chrome, cards, login, fx
            const string Demo = "Assets/Layer Lab/GUI Pro-CasualGame/ResourcesData/Sprites/Demo/Demo_Background";
            const string Fx = "Assets/Layer Lab/GUI Pro-CasualGame/ResourcesData/Particles/Texture";
            const string FxPrefabs = "Assets/Layer Lab/GUI Pro-CasualGame/Prefabs/Prefabs_DemoScene_Particle";

            hud.bgLayer1 = LoadSprite($"{Demo}/Background_09_Purple1.png");
            hud.bgGlowTop = LoadSprite($"{Demo}/Background_09_Purple3_GlowTop.png");
            hud.bgGlowBottom = LoadSprite($"{Demo}/Background_09_Purple4_GlowBottom.png");
            hud.dimNavy = LoadSprite($"{Demo}/Background_ScreenDimed_Navy.png");
            hud.dimBlack = LoadSprite($"{Demo}/Background_ScreenDimed_Black.png");
            hud.screenGlow = LoadSprite($"{Demo}/Background_ScreenGlow.png");

            hud.slideNavy = LoadSprite($"{LayerLabSprites}/Popup/Popup_Slide02_Single_Navy.png");
            hud.slideTopBar = LoadSprite($"{LayerLabSprites}/Popup/Popup_Slide02_Single_Navy_TopBar.png");
            hud.slideTopGlow = LoadSprite($"{LayerLabSprites}/Popup/Popup_Slide02_Single_Navy_TopGlow.png");
            hud.panelNavy = LoadSprite($"{LayerLabSprites}/Frame/PanelFrame02_Round_Single_Navy.png");
            hud.cardChampBlue = LoadSprite($"{LayerLabSprites}/Frame/CardFrame08_Single_Blue.png");
            hud.cardChampPurple = LoadSprite($"{LayerLabSprites}/Frame/CardFrame08_Single_Purple.png");
            hud.cardChampFocus = LoadSprite($"{LayerLabSprites}/Frame/CardFrame08_Focus.png");
            hud.cardChampGlow = LoadSprite($"{LayerLabSprites}/Frame/CardFrame08_Glow_1.png");
            hud.cardEventBg = LoadSprite($"{LayerLabSprites}/Frame/CardFrame06_Bg_Blue.png");
            hud.cardShopBlue = LoadSprite($"{LayerLabSprites}/Frame/CardFrame06_Bg_Blue.png");
            hud.cardShopYellow = LoadSprite($"{LayerLabSprites}/Frame/CardFrame06_Bg_Yellow.png");
            hud.cardShopPurple = LoadSprite($"{LayerLabSprites}/Frame/CardFrame06_Bg_Purple.png");
            hud.inputBg = LoadSprite($"{LayerLabSprites}/UI_Etc/InputField01_Bg_n.png");
            hud.icoAccount = LoadSprite($"{LayerLabSprites}/UI_Etc/InputField_Icon_Account.png");
            hud.flagPurple = LoadSprite($"{LayerLabSprites}/Label/Title_Flag01_Purple.png");
            hud.flagBlue = LoadSprite($"{LayerLabSprites}/Label/Title_Flag01_Blue.png");
            hud.dividerL = LoadSprite($"{LayerLabSprites}/Label/Title_Line_Divider_Left.png");
            hud.dividerR = LoadSprite($"{LayerLabSprites}/Label/Title_Line_Divider_Right.png");
            hud.lvlBg = LoadSprite($"{LayerLabSprites}/Slider/Slider_Level02_Bg.png");
            hud.lvlFill = LoadSprite($"{LayerLabSprites}/Slider/Slider_Level02_Fill_Blue.png");
            hud.fxGlow = LoadSprite($"{Fx}/fx_glow.png");
            hud.fxCircleGlow = LoadSprite($"{Fx}/fx_circle_glow.png");
            hud.fxRays = LoadSprite($"{Fx}/fx_rotate_line.png");
            hud.fxStar = LoadSprite($"{Fx}/fx_star_yellow.png");
            hud.icoStatHp = LoadSprite($"{LayerLabSprites}/IconMisc/Icon_StatsIcon_Hp01.png");
            hud.icoStatDmg = LoadSprite($"{LayerLabSprites}/IconMisc/Icon_StatsIcon_Damage.png");
            hud.icoStatSpd = LoadSprite($"{LayerLabSprites}/IconMisc/Icon_StatsIcon_Speed.png");
            hud.icoTrophyBig = LoadSprite($"{LayerLabSprites}/IconMisc/Icon_ImageIcon_Trophy_l.png");
            hud.icoStar = LoadSprite($"{LayerLabSprites}/IconMisc/Icon_ImageIcon_Star01_l.png");
            hud.shopCoinSmall = LoadSprite($"{LayerLabSprites}/Icon_ShopItems/ShopItem_Coin_2.png");
            hud.shopCoinBig = LoadSprite($"{LayerLabSprites}/Icon_ShopItems/ShopItem_Coin_4.png");
            hud.shopChest = LoadSprite($"{LayerLabSprites}/Icon_ShopItems/ShopItem_SpecialChest_Purple.png");
            hud.icoMedalGold = LoadSprite($"{LayerLabSprites}/IconMisc/Icon_ImageIcon_Medal_Gold.png");
            hud.icoGemGold = LoadSprite($"{LayerLabSprites}/IconMisc/Icon_ImageIcon_GemGold.png");

            // ---- designed composites for the de-tinted widgets (2026-07-22
            // owner feedback: never fake a colored widget by tinting a white base)
            hud.rowNavy = LoadSprite($"{LayerLabSprites}/Frame/ListFrame02_Single_Navy.png");
            hud.toastBar = LoadSprite($"{LayerLabSprites}/Popup/ToastMessage_Topbar_Single_Purple.png");
            hud.panelTopbarNavy = LoadSprite($"{LayerLabSprites}/Frame/PanelFrame04_TopbarDivided_Single_Navy.png");
            hud.bar4FillGreen = LoadSprite($"{LayerLabSprites}/Slider/Slider_Basic04_Fill_Green.png");
            hud.barBg2 = LoadSprite($"{LayerLabSprites}/Slider/Slider_Basic02_Bg.png");
            hud.barFillYellow = LoadSprite($"{LayerLabSprites}/Slider/Slider_Basic02_Fill_Yellow.png");
            hud.allyBarBg = LoadSprite($"{LayerLabSprites}/Slider/Slider_Icon04_Bg.png");
            hud.allyFillGreen = LoadSprite($"{LayerLabSprites}/Slider/Slider_Icon04_Fill_Green.png");
            hud.allyFillRed = LoadSprite($"{LayerLabSprites}/Slider/Slider_Icon04_Fill_Red.png");
            hud.circleDark = LoadSprite($"{LayerLabSprites}/Button/Button_Circle128_Dark.png");
            hud.itemBlue = LoadSprite($"{LayerLabSprites}/Frame/ItemFrame01_Single_Blue.png");
            hud.itemPurple = LoadSprite($"{LayerLabSprites}/Frame/ItemFrame01_Single_Purple.png");
            hud.itemYellow = LoadSprite($"{LayerLabSprites}/Frame/ItemFrame01_Single_Yellow.png");
            hud.itemRed = LoadSprite($"{LayerLabSprites}/Frame/ItemFrame01_Single_Red.png");
            hud.itemGreen = LoadSprite($"{LayerLabSprites}/Frame/ItemFrame01_Single_Green.png");

            // ---- menu scene 2026-07: the pack Lobby's own composition pieces
            hud.bgStage = LoadSprite($"{Demo}/Background_02.png");
            hud.userInfoTop = LoadSprite($"{LayerLabSprites}/UI_Etc/UserInfo01_Top.png");
            hud.userInfoBottom = LoadSprite($"{LayerLabSprites}/UI_Etc/UserInfo01_Bottom.png");
            hud.userInfoIcon = LoadSprite($"{LayerLabSprites}/UI_Etc/UserInfo_Profile_Icon.png");
            hud.lvl3Bg = LoadSprite($"{LayerLabSprites}/Slider/Slider_Level03_Bg.png");
            hud.lvl3Fill = LoadSprite($"{LayerLabSprites}/Slider/Slider_Level03_Fill_Blue.png");
            hud.lvl3Badge = LoadSprite($"{LayerLabSprites}/Slider/Slider_Level03_Badge.png");
            hud.btnBattleYellow = LoadSprite($"{LayerLabSprites}/Button/Button02_Yellow.png");
            hud.btnBattleBlue = LoadSprite($"{LayerLabSprites}/Button/Button02_Blue.png");
            hud.btnSideBlue = LoadSprite($"{LayerLabSprites}/Button/Button03_Blue.png");
            hud.btnSidePurple = LoadSprite($"{LayerLabSprites}/Button/Button03_Purple.png");
            hud.btnSquareNavy = LoadSprite($"{LayerLabSprites}/Button/Button_Square06_Navy.png");
            hud.alertTextRed = LoadSprite($"{LayerLabSprites}/UI_Etc/Alert_Text_s_Red.png");
            hud.alertTextGreen = LoadSprite($"{LayerLabSprites}/UI_Etc/Alert_Text_s_Green.png");
            hud.resourceBtnYellow = LoadSprite($"{LayerLabSprites}/UI_Etc/ResourceBar_Btn_Single_Yellow.png");
            hud.resourceBtnPurple = LoadSprite($"{LayerLabSprites}/UI_Etc/ResourceBar_Btn_Single_Purple.png");
            hud.iconGemPurple = LoadSprite($"{LayerLabSprites}/UI_Etc/ResourceBar_Icon_Gem_Purple.png");
            hud.card3Blue = LoadSprite($"{LayerLabSprites}/Frame/CardFrame03_Single_Blue.png");
            hud.card3Green = LoadSprite($"{LayerLabSprites}/Frame/CardFrame03_Single_Green.png");
            hud.card3Orange = LoadSprite($"{LayerLabSprites}/Frame/CardFrame03_Single_Orange.png");
            hud.card3Purple = LoadSprite($"{LayerLabSprites}/Frame/CardFrame03_Single_Purple.png");
            hud.card3Dim = LoadSprite($"{LayerLabSprites}/Frame/CardFrame03_Single_Dim.png");
            hud.card3Glow = LoadSprite($"{LayerLabSprites}/Frame/CardFrame03_Glow.png");
            hud.trapGreen = LoadSprite($"{LayerLabSprites}/Label/Label_Trapezoid_Single_Green.png");
            hud.trapPurple = LoadSprite($"{LayerLabSprites}/Label/Label_Trapezoid_Single_Purple.png");
            hud.ribbonIcon = LoadSprite($"{LayerLabSprites}/Label/Title_Ribbon_Icon.png");
            hud.icoBattleSword = LoadSprite($"{LayerLabSprites}/IconMisc/Icon_ImageIcon_Knife_Battle.png");
            hud.barFillBlue = LoadSprite($"{LayerLabSprites}/Slider/Slider_Basic01_Fill_Blue.png");
            hud.vsText = LoadSprite("Assets/Layer Lab/GUI Pro-CasualGame/ResourcesData/Sprites/Demo/Demo_Image/Image_Vs_text.png");
            hud.icoEnergy = LoadSprite($"{LayerLabSprites}/UI_Etc/ResourceBar_Icon_Energy.png");
            hud.icoHammer = LoadSprite($"{Picto}/Pictoicon_Hammer.Png"); // capital .Png — pack quirk

            // generated champion portraits (menu forge output; may not exist yet)
            if (hud.championPortraits == null || hud.championPortraits.Length != 5)
                hud.championPortraits = new Sprite[5];
            for (int i = 0; i < 5; i++)
                hud.championPortraits[i] =
                    AssetDatabase.LoadAssetAtPath<Sprite>($"{GenDir}/Portraits/Champion_{i}.png");

            hud.fxSparklePrefab = AssetDatabase.LoadAssetAtPath<GameObject>($"{FxPrefabs}/Fx_Sparkle_Star01_CustomColor_Yellow.prefab");
            hud.fxConfettiPrefab = AssetDatabase.LoadAssetAtPath<GameObject>($"{FxPrefabs}/Fx_Spread_Star01.prefab");
            hud.fxRotateLightPrefab = AssetDatabase.LoadAssetAtPath<GameObject>($"{FxPrefabs}/Fx_Rotate_Light01.prefab");
            if (hud.fxSparklePrefab == null || hud.fxConfettiPrefab == null)
                Debug.LogWarning("[CrownfallForge] UI particle prefabs missing — bursts will no-op");

            EditorUtility.SetDirty(hud);
        }

        // ------------------------------------------------------------------ util

        static readonly Dictionary<string, GameObject> prefabCache = new Dictionary<string, GameObject>();

        internal static GameObject FindPrefab(string folderHint, string name)
        {
            string key = folderHint + "/" + name;
            if (prefabCache.TryGetValue(key, out var cached)) return cached;

            GameObject found = null;
            foreach (var guid in AssetDatabase.FindAssets($"\"{name}\" t:Prefab"))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                if (!path.Contains(folderHint)) continue;
                if (!string.Equals(Path.GetFileNameWithoutExtension(path), name, System.StringComparison.OrdinalIgnoreCase)) continue;
                found = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                break;
            }
            if (found == null)
                Debug.LogWarning($"[CrownfallForge] Prefab not found: {folderHint}/{name}");
            prefabCache[key] = found;
            return found;
        }

        static AudioClip FindClip(string name)
        {
            foreach (var guid in AssetDatabase.FindAssets($"\"{name}\" t:AudioClip"))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                if (string.Equals(Path.GetFileNameWithoutExtension(path), name, System.StringComparison.OrdinalIgnoreCase))
                    return AssetDatabase.LoadAssetAtPath<AudioClip>(path);
            }
            Debug.LogWarning("[CrownfallForge] AudioClip not found: " + name);
            return null;
        }

        internal static Sprite LoadSprite(string path)
        {
            var s = AssetDatabase.LoadAssetAtPath<Sprite>(path);
            var importer = AssetImporter.GetAtPath(path) as TextureImporter;
            bool needsReimport = false;
            if (importer != null)
            {
                if (importer.textureType != TextureImporterType.Sprite)
                {
                    importer.textureType = TextureImporterType.Sprite;
                    needsReimport = true;
                }
                // UI sprites must render pixel-exact on mobile: compressed formats
                // (ASTC/PVRTC) can decode to white/garbage on some GPUs, which is
                // exactly the "white box" menu-icon bug. Pin iOS + Android to
                // uncompressed RGBA32 so every hub icon is guaranteed to show.
                needsReimport |= ForceUncompressed(importer, "iPhone");
                needsReimport |= ForceUncompressed(importer, "Android");
                if (needsReimport)
                {
                    importer.SaveAndReimport();
                    s = AssetDatabase.LoadAssetAtPath<Sprite>(path);
                }
            }
            if (s == null) Debug.LogWarning("[CrownfallForge] Sprite not found: " + path);
            return s;
        }

        static bool ForceUncompressed(TextureImporter importer, string platform)
        {
            var ps = importer.GetPlatformTextureSettings(platform);
            if (ps.overridden && ps.format == TextureImporterFormat.RGBA32) return false;
            ps.overridden = true;
            ps.format = TextureImporterFormat.RGBA32;
            ps.textureCompression = TextureImporterCompression.Uncompressed;
            importer.SetPlatformTextureSettings(ps);
            return true;
        }

        static GameObject Place(GameObject prefab, Vector3 pos, float yaw, float scale, bool ensureCollider)
        {
            if (prefab == null) return null;
            var go = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
            go.transform.SetParent(envRoot);
            go.transform.position = pos;
            go.transform.rotation = Quaternion.Euler(0f, yaw, 0f);
            go.transform.localScale = Vector3.one * scale;
            if (ensureCollider)
            {
                if (go.GetComponentInChildren<Collider>() == null)
                    foreach (var mf in go.GetComponentsInChildren<MeshFilter>())
                    {
                        if (mf.sharedMesh == null) continue;
                        var mc = mf.gameObject.AddComponent<MeshCollider>();
                        mc.sharedMesh = mf.sharedMesh;
                    }
            }
            else
            {
                // decoration (grass, bushes, flames) must never block movement â€”
                // several Battle Arena prefabs ship with their own colliders
                foreach (var col in go.GetComponentsInChildren<Collider>())
                    Object.DestroyImmediate(col);
            }
            return go;
        }

        static Bounds MeasureBounds(GameObject prefab)
        {
            var temp = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
            var b = MeasureBoundsInstance(temp);
            Object.DestroyImmediate(temp);
            return b;
        }

        static Bounds MeasureBoundsInstance(GameObject go)
        {
            var rends = go.GetComponentsInChildren<Renderer>();
            if (rends.Length == 0) return new Bounds(go.transform.position, Vector3.zero);
            var b = rends[0].bounds;
            foreach (var r in rends) b.Encapsulate(r.bounds);
            return b;
        }

        internal static void ReparentWeaponsToHands(Transform model)
        {
            Transform FindDeep(Transform t, string name)
            {
                foreach (var tr in t.GetComponentsInChildren<Transform>(true))
                    if (string.Equals(tr.name, name, System.StringComparison.OrdinalIgnoreCase))
                        return tr;
                return null;
            }

            var pairs = new (string holder, string hand)[]
            {
                ("weaponShield_l", "hand_l"),
                ("weaponShield_r", "hand_r"),
            };
            foreach (var (holderName, handName) in pairs)
            {
                var holder = FindDeep(model, holderName);
                var hand = FindDeep(model, handName);
                if (holder == null || hand == null) continue;
                for (int i = holder.childCount - 1; i >= 0; i--)
                    holder.GetChild(i).SetParent(hand, true);
            }
        }

        /// Swap the two-hander's sword for the modular pack's Hammer01 with a
        /// neon storm-glow emissive material. Runs on any Warhammer rig: scene
        /// fighters, net prefab, menu showcase and portrait models.
        internal static void ApplyHammerLoadout(Transform model)
        {
            var sword = FindWeaponTransform(model);
            if (sword == null)
            {
                Debug.LogWarning("[CrownfallForge] Hammer loadout: no weapon node found");
                return;
            }
            var hammerPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(
                "Assets/ModularRPGHeroesPBR/Prefabs/Weapons/Hammer01.prefab");
            if (hammerPrefab == null)
            {
                Debug.LogWarning("[CrownfallForge] Hammer loadout: Hammer01.prefab missing");
                return;
            }
            var hammer = (GameObject)PrefabUtility.InstantiatePrefab(hammerPrefab);
            PrefabUtility.UnpackPrefabInstance(hammer, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
            hammer.name = "Hammer01";
            hammer.transform.SetParent(sword.parent, false);
            hammer.transform.localPosition = sword.localPosition;
            hammer.transform.localRotation = sword.localRotation;
            hammer.transform.localScale = sword.localScale;

            var glow = LoadOrCreateNeonHammerMat(hammer);
            if (glow != null)
                foreach (var r in hammer.GetComponentsInChildren<Renderer>())
                    r.sharedMaterial = glow;

            Object.DestroyImmediate(sword.gameObject);
        }

        static Material LoadOrCreateNeonHammerMat(GameObject hammer)
        {
            string path = $"{GenDir}/NeonHammerGlow.mat";
            var existing = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (existing != null) return existing;
            var srcRend = hammer.GetComponentInChildren<Renderer>();
            if (srcRend == null || srcRend.sharedMaterial == null) return null;
            var mat = new Material(srcRend.sharedMaterial);
            mat.EnableKeyword("_EMISSION");
            mat.globalIlluminationFlags = MaterialGlobalIlluminationFlags.None;
            // storm cyan pushed past bloom threshold so the head glows
            mat.SetColor("_EmissionColor", new Color(0.35f, 0.85f, 1f) * 2.2f);
            AssetDatabase.CreateAsset(mat, path);
            return mat;
        }

        static Transform FindWeaponTransform(Transform model)
        {
            string[] keys = { "sword", "wand", "bow", "axe", "hammer", "blade", "staff" };
            Transform best = null;
            float bestScore = float.MinValue;
            foreach (var tr in model.GetComponentsInChildren<Transform>())
            {
                string n = tr.name.ToLowerInvariant();
                if (n.Contains("shield")) continue;
                bool match = keys.Any(k => n.Contains(k));
                if (!match) continue;
                var rend = tr.GetComponentInChildren<Renderer>();
                if (rend == null) continue;

                // prefer weapons parented under a hand bone over back decorations
                bool inHand = false;
                for (var p = tr.parent; p != null && p != model; p = p.parent)
                    if (p.name.ToLowerInvariant().Contains("hand")) { inHand = true; break; }

                float score = rend.bounds.size.magnitude + (inHand ? 100f : 0f);
                if (score > bestScore) { bestScore = score; best = tr; }
            }
            return best;
        }

        static Bounds RendererBounds(Transform t)
        {
            var rends = t.GetComponentsInChildren<Renderer>();
            var b = rends[0].bounds;
            foreach (var r in rends) b.Encapsulate(r.bounds);
            return b;
        }

        static Vector3 FarthestCorner(Bounds b, Vector3 from)
        {
            Vector3 best = b.center;
            float bestDist = 0f;
            for (int i = 0; i < 8; i++)
            {
                var c = new Vector3(
                    (i & 1) == 0 ? b.min.x : b.max.x,
                    (i & 2) == 0 ? b.min.y : b.max.y,
                    (i & 4) == 0 ? b.min.z : b.max.z);
                float d = (c - from).sqrMagnitude;
                if (d > bestDist) { bestDist = d; best = c; }
            }
            return best;
        }

        static void SetLayerRecursive(GameObject go, int layer)
        {
            go.layer = layer;
            foreach (Transform child in go.transform)
                SetLayerRecursive(child.gameObject, layer);
        }
    }
}
