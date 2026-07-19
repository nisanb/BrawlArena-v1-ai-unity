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

namespace Crownfall.EditorTools
{
    /// Builds the whole game: animator graph + per-class overrides from the
    /// ModularRPGHeroes clips, the arena scene, nine fighter rigs, HUD/VFX
    /// wiring, lighting, post-processing and the NavMesh.
    public static class CrownfallForge
    {
        const string GenDir = "Assets/Crownfall/Generated";
        const string ScenePath = "Assets/Crownfall/CrownfallArena.unity";
        const string AnimRoot = "Assets/ModularRPGHeroesPBR/Animations";
        const string CharDir = "Assets/ModularRPGHeroesPBR/Prefabs/BasicCharacters";
        const string LayerLabSprites = "Assets/Layer Lab/GUI Pro-CasualGame/ResourcesData/Sprites/Components";
        const string LayerLabFonts = "Assets/Layer Lab/GUI Pro-CasualGame/ResourcesData/Fonts";

        static readonly string[] ElementNames = { "Light", "Earth", "Frost", "Storm", "Shadow", "Fire", "Arcane" };

        // ------------------------------------------------------------------ entry points

        [MenuItem("Crownfall/Build All")]
        public static void BuildAll()
        {
            EnsureFolders();
            var overrides = BuildAnimators();
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

        static Dictionary<string, AnimationClip> LoadClips(string folder)
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
            foreach (var t in new[] { "AttackL", "AttackH", "Roll", "Hit", "Stagger", "Recover", "Die", "Respawn", "Victory", "BlockImpact" })
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
            var heavy = S("Heavy", "NormalAttack02", "Attack", 1.12f);
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
            T(block, roll, "Roll", duration: 0.07f);

            // combo chain + returns
            T(l1, l2, "AttackL", duration: 0.06f);
            T(l2, l3, "AttackL", duration: 0.06f);
            foreach (var atk in new[] { l1, l2, l3, heavy })
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
            };
            var folders = new Dictionary<ClassId, string>
            {
                { ClassId.Knight, $"{AnimRoot}/SwordShield" },
                { ClassId.Greatsword, $"{AnimRoot}/SingleTwoHandSword" },
                { ClassId.Duelist, $"{AnimRoot}/DoubleSwords" },
                { ClassId.Mage, $"{AnimRoot}/MagicWand" },
            };
            var mageMap = new Dictionary<string, string>
            {
                { "NormalAttack01", "Attack01" }, { "Combo01", "Attack01" },
                { "Combo02", "Attack01" }, { "NormalAttack02", "Attack02" },
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
            WireHud(hud);
            touch.circleSprite = hud.frameCircle;
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
                new FighterSpec { cls = ClassId.Knight, element = ElementId.Light, team = Team.Azure, name = "Kael", skin = "SwordAndShield01", slot = 0, isPlayerVariant = true },
                new FighterSpec { cls = ClassId.Greatsword, element = ElementId.Earth, team = Team.Azure, name = "Doran", skin = "SingleTwoHandSword01", slot = 0, isPlayerVariant = true },
                new FighterSpec { cls = ClassId.Duelist, element = ElementId.Storm, team = Team.Azure, name = "Vesper", skin = "DoubleSword01", slot = 0, isPlayerVariant = true },
                new FighterSpec { cls = ClassId.Mage, element = ElementId.Frost, team = Team.Azure, name = "Elyra", skin = "MagicWand01", slot = 0, isPlayerVariant = true },

                new FighterSpec { cls = ClassId.Greatsword, element = ElementId.Earth, team = Team.Azure, name = "Bram", skin = "SingleTwoHandSword02", slot = 1, aggression = 0.6f },
                new FighterSpec { cls = ClassId.Mage, element = ElementId.Frost, team = Team.Azure, name = "Lyra", skin = "MagicWand02", slot = 2, aggression = 0.55f },

                new FighterSpec { cls = ClassId.Knight, element = ElementId.Shadow, team = Team.Crimson, name = "Vex", skin = "SwordAndShield03", slot = 0, aggression = 0.62f },
                new FighterSpec { cls = ClassId.Duelist, element = ElementId.Fire, team = Team.Crimson, name = "Sable", skin = "DoubleSword02", slot = 1, aggression = 0.7f },
                new FighterSpec { cls = ClassId.Mage, element = ElementId.Arcane, team = Team.Crimson, name = "Morgath", skin = "MagicWand03", slot = 2, aggression = 0.58f },
            };

            var playerVariants = new GameObject[4];
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
            }
            mm.playerVariants = playerVariants;

            EditorUtility.SetDirty(mm);
            EditorSceneManager.SaveScene(scene, ScenePath);

            var list = EditorBuildSettings.scenes.Where(s => s.path != ScenePath).ToList();
            list.Insert(0, new EditorBuildSettingsScene(ScenePath, true));
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

            // pillars with torch fire
            string[] pillars = { "Pilar1", "Pilar2", "Pilar3", "Pilar4" };
            var flame = FindPrefab("MagicArsenal", "StretchyFlame") ?? FindPrefab("MagicArsenal", "FireFlame");
            Vector3[] pillarSpots =
            {
                new Vector3(-9f, 0f, -9f), new Vector3(9f, 0f, -9f),
                new Vector3(-9f, 0f, 9f), new Vector3(9f, 0f, 9f),
            };
            for (int i = 0; i < 4; i++)
            {
                var p = FindPrefab("Battle Arena", pillars[i]);
                var placed = Place(p, pillarSpots[i], i * 90f, 1f, true);
                float topY = 3.4f;
                if (placed != null)
                {
                    var b = MeasureBoundsInstance(placed);
                    if (b.size.y > 0.5f) topY = b.max.y - envRoot.position.y + 0.15f;
                }
                Vector3 firePos = pillarSpots[i] + Vector3.up * topY;
                if (flame != null)
                {
                    var f = (GameObject)PrefabUtility.InstantiatePrefab(flame);
                    f.transform.SetParent(envRoot);
                    f.transform.position = firePos;
                    f.transform.localScale = Vector3.one * 0.8f;
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

            // gate braziers with team-hint flames
            foreach (float z in new[] { -edge, edge })
            {
                foreach (float x in new[] { -4.6f, 4.6f })
                {
                    Place(FindPrefab("Battle Arena", "Barrel01"), new Vector3(x, 0f, z * 0.985f), 0f, 0.9f, true);
                    if (flame != null)
                    {
                        var f = (GameObject)PrefabUtility.InstantiatePrefab(flame);
                        f.transform.SetParent(envRoot);
                        f.transform.position = new Vector3(x, 1.05f, z * 0.985f);
                        f.transform.localScale = Vector3.one * 0.6f;
                    }
                }
            }

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

        static GameObject BuildFighter(FighterSpec spec, AnimatorOverrideController aoc, Vector3 pos, Quaternion rot)
        {
            var kit = ClassKits.Get(spec.cls);
            var root = new GameObject($"{(spec.isPlayerVariant ? "Player_" : "")}{spec.name}_{spec.cls}");
            root.transform.SetPositionAndRotation(pos, rot);

            // model
            var charPrefab = AssetDatabase.LoadAssetAtPath<GameObject>($"{CharDir}/{spec.skin}.prefab");
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
            BuildWorldBar(root.transform, id.TeamColor);

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

        static void BuildWorldBar(Transform root, Color teamColor)
        {
            var barGo = new GameObject("WorldBar", typeof(Canvas));
            barGo.transform.SetParent(root, false);
            barGo.transform.localPosition = new Vector3(0f, 2.3f, 0f);
            var canvas = barGo.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.WorldSpace;
            var rt = barGo.GetComponent<RectTransform>();
            rt.sizeDelta = new Vector2(120f, 14f);
            barGo.transform.localScale = Vector3.one * 0.012f;
            var group = barGo.AddComponent<CanvasGroup>();

            Image MakeImg(string n, Color c, Vector2 size)
            {
                var go = new GameObject(n, typeof(RectTransform));
                var r = go.GetComponent<RectTransform>();
                r.SetParent(barGo.transform, false);
                r.sizeDelta = size;
                var img = go.AddComponent<Image>();
                img.color = c;
                img.raycastTarget = false;
                return img;
            }

            MakeImg("Bg", new Color(0.05f, 0.05f, 0.08f, 0.85f), new Vector2(120f, 14f));
            var ghost = MakeImg("Ghost", new Color(1f, 0.85f, 0.7f, 0.9f), new Vector2(114f, 9f));
            ghost.type = Image.Type.Filled;
            ghost.fillMethod = Image.FillMethod.Horizontal;
            var fill = MakeImg("Fill", teamColor, new Vector2(114f, 9f));
            fill.type = Image.Type.Filled;
            fill.fillMethod = Image.FillMethod.Horizontal;

            var bar = barGo.AddComponent<WorldHealthBar>();
            bar.health = root.GetComponent<Health>();
            bar.fill = fill;
            bar.ghost = ghost;
            bar.group = group;
        }

        // ------------------------------------------------------------------ wiring

        static void WireEffects(GameEffects fx)
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
                    explosion = FindPrefab("MagicArsenal", el + "ExplosionSmall"),
                    muzzle = FindPrefab("MagicArsenal", el + "MuzzleNormal"),
                    nova = FindPrefab("MagicArsenal", "Nova" + el),
                    enchant = FindPrefab("MagicArsenal", el + "Enchant"),
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

        static void WireHud(HUDController hud)
        {
            hud.fontBig = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>($"{LayerLabFonts}/LilitaOne-Regular Outline_Extended ASCII_210 SDF.asset");
            hud.fontMid = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>($"{LayerLabFonts}/LilitaOne-Regular Outline_Extended ASCII_72 SDF.asset");
            hud.fontSmall = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>($"{LayerLabFonts}/LilitaOne-Regular Outline_Extended ASCII_40 SDF.asset");
            if (hud.fontBig == null) hud.fontBig = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>($"{LayerLabFonts}/LilitaOne-Regular Outline 210 SDF.asset");
            if (hud.fontMid == null) hud.fontMid = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>($"{LayerLabFonts}/LilitaOne-Regular Outline 72 SDF.asset");
            if (hud.fontSmall == null) hud.fontSmall = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>($"{LayerLabFonts}/LilitaOne-Regular Outline 40 SDF.asset");

            hud.barBgTrapezoid = LoadSprite($"{LayerLabSprites}/Slider/Slider_BasicTrapezoid01_Bg.png");
            hud.barFillTrapezoid = LoadSprite($"{LayerLabSprites}/Slider/Slider_BasicTrapezoid01_Fill_White.png");
            hud.barBgBasic = LoadSprite($"{LayerLabSprites}/Slider/Slider_Basic01_Bg.png");
            hud.barFillBasic = LoadSprite($"{LayerLabSprites}/Slider/Slider_Basic01_Fill_White.png");
            hud.frameRound = LoadSprite($"{LayerLabSprites}/Frame/BasicFrame_Round12.png");
            hud.frameCircle = LoadSprite($"{LayerLabSprites}/Frame/BasicFrame_Circle77.png");
            hud.bannerNavy = LoadSprite($"{LayerLabSprites}/Frame/BannerFrame01_Single_Navy.png");
            EditorUtility.SetDirty(hud);
        }

        // ------------------------------------------------------------------ util

        static readonly Dictionary<string, GameObject> prefabCache = new Dictionary<string, GameObject>();

        static GameObject FindPrefab(string folderHint, string name)
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

        static Sprite LoadSprite(string path)
        {
            var s = AssetDatabase.LoadAssetAtPath<Sprite>(path);
            if (s == null)
            {
                var importer = AssetImporter.GetAtPath(path) as TextureImporter;
                if (importer != null && importer.textureType != TextureImporterType.Sprite)
                {
                    importer.textureType = TextureImporterType.Sprite;
                    importer.SaveAndReimport();
                    s = AssetDatabase.LoadAssetAtPath<Sprite>(path);
                }
            }
            if (s == null) Debug.LogWarning("[CrownfallForge] Sprite not found: " + path);
            return s;
        }

        static GameObject Place(GameObject prefab, Vector3 pos, float yaw, float scale, bool ensureCollider)
        {
            if (prefab == null) return null;
            var go = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
            go.transform.SetParent(envRoot);
            go.transform.position = pos;
            go.transform.rotation = Quaternion.Euler(0f, yaw, 0f);
            go.transform.localScale = Vector3.one * scale;
            if (ensureCollider && go.GetComponentInChildren<Collider>() == null)
            {
                foreach (var mf in go.GetComponentsInChildren<MeshFilter>())
                {
                    if (mf.sharedMesh == null) continue;
                    var mc = mf.gameObject.AddComponent<MeshCollider>();
                    mc.sharedMesh = mf.sharedMesh;
                }
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
