using System.Collections;
using System.IO;
using System.Text;
using UnityEngine;

namespace Crownfall
{
    /// Regression probe for the corpse-idle bug: kills the same enemy twice and
    /// samples its animator state 1s after each death. Death #2 is the case the
    /// leaked Respawn trigger used to break (Die state instantly exited to idle).
    /// Writes <dir>/data.txt when done.
    public class DeathPoseProbe : MonoBehaviour
    {
        public static void Begin(string dir)
        {
            var go = new GameObject("DeathPoseProbe");
            DontDestroyOnLoad(go);
            go.AddComponent<DeathPoseProbe>().outDir = Path.GetFullPath(dir);
        }

        string outDir;
        readonly StringBuilder log = new StringBuilder();

        IEnumerator Start()
        {
            Application.runInBackground = true;
            Directory.CreateDirectory(outDir);

            float w = 0f;
            while ((MatchManager.I == null || MatchManager.I.State != MatchState.Fighting) && w < 40f)
            {
                w += Time.deltaTime;
                yield return null;
            }
            if (MatchManager.I == null || MatchManager.I.State != MatchState.Fighting)
            {
                log.AppendLine("ERROR match never reached Fighting");
                Finish();
                yield break;
            }

            var player = MatchManager.I.PlayerMotor;
            CombatMotor victim = null;
            foreach (var m in FindObjectsByType<CombatMotor>(FindObjectsSortMode.None))
                if (m.Identity != null && player != null && m.Identity.team != player.Identity.team)
                {
                    victim = m;
                    break;
                }
            if (victim == null)
            {
                log.AppendLine("ERROR no victim found");
                Finish();
                yield break;
            }
            log.AppendLine("victim=" + victim.Identity.displayName + " (" + victim.Kit.id + ")");

            for (int death = 1; death <= 2; death++)
            {
                float alive = 0f;
                while (victim.Health.IsDead && alive < 12f)
                {
                    alive += Time.deltaTime;
                    yield return null;
                }
                // outlast spawn protection (Tuning.SpawnProtection) or the kill bounces
                yield return new WaitForSeconds(Tuning.SpawnProtection + 0.5f);

                victim.Health.TakeHit(new HitInfo
                {
                    attacker = player,
                    damage = 9999f,
                    direction = Vector3.forward,
                    point = victim.AimPoint,
                    element = ElementId.Arcane,
                    heavy = true,
                    unblockable = true,
                });
                yield return new WaitForSeconds(1.0f);

                var st = victim.Anim.GetCurrentAnimatorStateInfo(0);
                log.AppendLine($"death#{death} +1.0s: inDieState={st.IsTag("Die")} " +
                               $"motorState={victim.State} dead={victim.Health.IsDead}");
                if (death == 2)
                    ScreenCapture.CaptureScreenshot(Path.Combine(outDir, "death2.png"));
            }

            yield return new WaitForSeconds(0.5f); // let the screenshot flush
            Finish();
        }

        void Finish()
        {
            File.WriteAllText(Path.Combine(outDir, "data.txt"), log.ToString());
            Destroy(gameObject);
        }
    }
}
