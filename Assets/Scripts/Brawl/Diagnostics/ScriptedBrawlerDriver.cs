#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using UnityEngine;

namespace BrawlArena.EditorAutomation
{
    /// <summary>
    /// Drives one BrawlerController through a deterministic scripted timeline
    /// using the same public intent API as PlayerBrawlerInput, so gameplay
    /// probes never touch physical input devices. Directions are world-space
    /// XZ, not camera-relative, to keep repeated runs comparable.
    /// </summary>
    [RequireComponent(typeof(BrawlerController))]
    public class ScriptedBrawlerDriver : MonoBehaviour
    {
        [Serializable]
        public class ProbeStep
        {
            public float at;
            public string action;
            public float[] dir;
            public float duration;
        }

        [Serializable]
        public class ProbeScenario
        {
            public string name;
            public List<ProbeStep> steps = new List<ProbeStep>();
        }

        BrawlerController self;
        ProbeScenario scenario;
        readonly HashSet<int> fired = new HashSet<int>();
        float startTime;
        int activeMoveStep = -1;

        public string ScenarioName => scenario != null ? scenario.name : string.Empty;
        public bool IsFinished { get; private set; }

        void Awake()
        {
            self = GetComponent<BrawlerController>();
        }

        void OnEnable()
        {
            startTime = Time.time;
            fired.Clear();
            activeMoveStep = -1;
            IsFinished = false;
        }

        public void LoadScenario(string json)
        {
            scenario = JsonUtility.FromJson<ProbeScenario>(json);
            if (scenario == null || scenario.steps == null || scenario.steps.Count == 0)
                throw new ArgumentException("GameplayProbe scenario has no steps.");
            scenario.steps.Sort((a, b) => a.at.CompareTo(b.at));
            startTime = Time.time;
            fired.Clear();
            activeMoveStep = -1;
            IsFinished = false;
        }

        static Vector3 ToWorld(float[] dir)
        {
            if (dir == null || dir.Length < 2) return Vector3.zero;
            var world = new Vector3(dir[0], 0f, dir[1]);
            return world.sqrMagnitude > 0.0001f ? world.normalized : Vector3.zero;
        }

        void Update()
        {
            if (scenario == null || self == null) return;
            float elapsed = Time.time - startTime;

            for (int i = 0; i < scenario.steps.Count; i++)
            {
                if (elapsed < scenario.steps[i].at || fired.Contains(i)) continue;
                fired.Add(i);
                Fire(i, scenario.steps[i], elapsed);
            }

            Vector3 move = Vector3.zero;
            if (activeMoveStep >= 0)
            {
                var m = scenario.steps[activeMoveStep];
                if (elapsed <= m.at + m.duration) move = ToWorld(m.dir);
                else activeMoveStep = -1;
            }
            self.SetMoveInput(move);

            if (!IsFinished && fired.Count == scenario.steps.Count && activeMoveStep < 0)
            {
                IsFinished = true;
                Debug.Log("[GameplayProbe] scenario '" + scenario.name +
                          "' finished at t=" + elapsed.ToString("F2"));
            }
        }

        void Fire(int index, ProbeStep step, float elapsed)
        {
            Vector3 dir = ToWorld(step.dir);
            bool ok = true;
            switch (step.action)
            {
                case "move": activeMoveStep = index; break;
                case "stop": activeMoveStep = -1; break;
                case "attack_auto": ok = self.TryAttackAuto(); break;
                case "attack_dir": ok = self.TryAttackDirection(dir); break;
                case "super_auto": ok = self.TrySuperAuto(); break;
                case "super_dir": ok = self.TrySuperDirection(dir); break;
                case "ward_step":
                    ok = self.TryWardStep(dir.sqrMagnitude > 0.01f ? dir : transform.forward);
                    break;
                default:
                    Debug.LogWarning("[GameplayProbe] unknown action '" + step.action + "'");
                    return;
            }
            Debug.Log("[GameplayProbe] t=" + elapsed.ToString("F2") + " " +
                      step.action + " -> " + ok);
        }

        void OnDisable()
        {
            if (self != null) self.SetMoveInput(Vector3.zero);
        }
    }
}
#endif
