using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;

namespace Crownfall
{
    /// Unattended skill/combo evidence probe. Started by the automation harness:
    /// waits for the match, then forces the class skill on one fighter of each
    /// class in turn while logging skill activations (cooldown edge + animator
    /// Skill-tag state) and capturing periodic screenshots. Writes <dir>/data.txt
    /// when done so the harness can poll for completion.
    public class SkillProbe : MonoBehaviour
    {
        public static void Begin(string dir, float seconds = 45f)
        {
            var go = new GameObject("SkillProbe");
            DontDestroyOnLoad(go);
            var p = go.AddComponent<SkillProbe>();
            p.outDir = Path.GetFullPath(dir);
            p.duration = seconds;
        }

        string outDir;
        float duration;
        readonly StringBuilder log = new StringBuilder();
        readonly Dictionary<CombatMotor, bool> wasReady = new Dictionary<CombatMotor, bool>();
        readonly Dictionary<CombatMotor, bool> inSkillAnim = new Dictionary<CombatMotor, bool>();

        IEnumerator Start()
        {
            Application.runInBackground = true; // screenshots never flush unfocused otherwise
            Directory.CreateDirectory(outDir);

            float bootWait = 0f;
            while ((MatchManager.I == null || MatchManager.I.State != MatchState.Fighting) && bootWait < 40f)
            {
                bootWait += Time.deltaTime;
                yield return null;
            }
            if (MatchManager.I == null || MatchManager.I.State != MatchState.Fighting)
            {
                Line("ERROR match never reached Fighting");
                Finish();
                yield break;
            }
            Line($"fighting after {bootWait:0.0}s");

            float t = 0f;
            float nextShotAt = 2f;
            float nextForceAt = 1.5f;
            int shot = 0;
            int forceClass = 0;

            while (t < duration && MatchManager.I.State == MatchState.Fighting)
            {
                t += Time.deltaTime;

                foreach (var m in FindObjectsByType<CombatMotor>(FindObjectsSortMode.None))
                {
                    if (m.IsDead || m.Identity == null) continue;

                    bool ready = m.SkillReady;
                    if (wasReady.TryGetValue(m, out bool was) && was && !ready)
                        Line($"{t:0.0}s SKILL_FIRED {m.Identity.displayName} ({m.Kit.id}) " +
                             $"stamina={m.Stamina.Current:0}");
                    wasReady[m] = ready;

                    bool inSkill = m.Anim != null &&
                                   m.Anim.GetCurrentAnimatorStateInfo(0).IsTag("Skill");
                    if (inSkill && (!inSkillAnim.TryGetValue(m, out bool wasIn) || !wasIn))
                        Line($"{t:0.0}s SKILL_ANIM {m.Identity.displayName} ({m.Kit.id})");
                    inSkillAnim[m] = inSkill;
                }

                // rotate a forced skill through the classes so every routine runs
                if (t >= nextForceAt)
                {
                    nextForceAt = t + 6f;
                    var want = (ClassId)(forceClass % 4);
                    forceClass++;
                    foreach (var m in FindObjectsByType<CombatMotor>(FindObjectsSortMode.None))
                    {
                        if (m.IsDead || m.Kit == null || m.Kit.id != want) continue;
                        if (!m.SkillReady) continue;
                        m.RequestSkill();
                        Line($"{t:0.0}s FORCED {want} on {m.Identity.displayName}");
                        break;
                    }
                }

                if (t >= nextShotAt && shot < 8)
                {
                    nextShotAt = t + 5f;
                    shot++;
                    ScreenCapture.CaptureScreenshot(Path.Combine(outDir, $"shot{shot:00}.png"));
                }

                yield return null;
            }

            Line($"done at {t:0.0}s state={MatchManager.I.State}");
            Finish();
        }

        void Line(string s)
        {
            log.AppendLine(s);
            Debug.Log("[SkillProbe] " + s);
        }

        void Finish()
        {
            File.WriteAllText(Path.Combine(outDir, "data.txt"), log.ToString());
            Destroy(gameObject);
        }
    }
}
