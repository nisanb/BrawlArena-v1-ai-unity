using System.Collections;
using System.IO;
using System.Text;
using UnityEngine;

namespace Crownfall
{
    /// Hit-registration lab: teleports the player and one enemy into a matrix of
    /// distance/angle trials, swings, and records whether damage landed. Also
    /// forces a stagger and captures its presentation. Editor tooling only.
    public class HitLabProbe : MonoBehaviour
    {
        public string outDir;
        readonly StringBuilder log = new StringBuilder();
        CombatMotor player, dummy;
        int hits, trials;

        public static void Begin(string dir)
        {
            var go = new GameObject("HitLabProbe");
            go.AddComponent<HitLabProbe>().outDir = dir;
        }

        void Start()
        {
            Directory.CreateDirectory(outDir);
            player = MatchManager.I.PlayerMotor;
            foreach (var ai in FindObjectsByType<AIController>(FindObjectsSortMode.None)) ai.enabled = false;
            var pc = player.GetComponent<PlayerController>();
            if (pc != null) pc.enabled = false;

            foreach (var e in MatchManager.I.AliveEnemiesOf(player.Identity.team)) { dummy = e; break; }
            StartCoroutine(RunLab());
        }

        IEnumerator RunLab()
        {
            var basePos = new Vector3(8f, 0f, 6f);

            log.AppendLine("== light attacks: distance x approach angle ==");
            float[] dists = { 1.2f, 1.8f, 2.4f, 3.0f, 3.6f };
            float[] angles = { 0f, 40f, 80f };
            foreach (float dist in dists)
                foreach (float ang in angles)
                    yield return Trial(basePos, dist, ang, false);

            log.AppendLine("== heavy attacks ==");
            foreach (float dist in new[] { 1.8f, 2.8f, 3.8f })
                yield return Trial(basePos, dist, 0f, true);

            log.AppendLine("== strafing target ==");
            for (int i = 0; i < 3; i++)
                yield return MovingTrial(basePos, 2.2f);

            log.AppendLine($"== TOTAL {hits}/{trials} landed ==");

            // stagger presentation: hammer the dummy until poise breaks
            log.AppendLine("== stagger visual check ==");
            yield return ResetPair(basePos, 1.6f, 0f);
            int swings = 0;
            while (dummy.State != MotorState.Staggered && swings < 8)
            {
                player.RequestLight();
                swings++;
                yield return new WaitForSeconds(0.55f);
            }
            log.AppendLine($"stagger after {swings} swings: state={dummy.State}");
            for (int i = 0; i < 3; i++)
            {
                ScreenCapture.CaptureScreenshot(Path.Combine(outDir, $"stagger-{i}.png"));
                yield return new WaitForSeconds(0.45f);
            }

            File.WriteAllText(Path.Combine(outDir, "results.txt"), log.ToString());
            Debug.Log("[HitLab] DONE " + outDir);
            Destroy(gameObject);
        }

        IEnumerator ResetPair(Vector3 basePos, float dist, float angleDeg)
        {
            Vector3 offset = Quaternion.Euler(0f, angleDeg, 0f) * Vector3.forward * dist;
            player.ResetForRespawn(basePos, Quaternion.identity); // faces +Z
            dummy.ResetForRespawn(basePos + offset, Quaternion.LookRotation(-offset));
            player.LockTarget = null;
            yield return new WaitForSeconds(1.7f); // outlive spawn protection
        }

        IEnumerator Trial(Vector3 basePos, float dist, float angleDeg, bool heavy)
        {
            yield return ResetPair(basePos, dist, angleDeg);
            float before = dummy.Health.Current;
            if (heavy) player.RequestHeavy(); else player.RequestLight();
            yield return new WaitForSeconds(heavy ? 1.5f : 1.0f);
            float dmg = before - dummy.Health.Current;
            trials++;
            if (dmg > 0.1f) hits++;
            log.AppendLine($"{(heavy ? "HEAVY" : "light")} dist={dist:0.0} angle={angleDeg:0} -> {(dmg > 0.1f ? $"HIT {dmg:0.#}" : "MISS")}");
        }

        IEnumerator MovingTrial(Vector3 basePos, float dist)
        {
            yield return ResetPair(basePos, dist, 0f);
            float before = dummy.Health.Current;
            // dummy strafes hard while the player swings
            float end = Time.time + 1.0f;
            player.RequestLight();
            while (Time.time < end)
            {
                dummy.SetMoveInput(Vector3.right, false);
                yield return null;
            }
            dummy.SetMoveInput(Vector3.zero, false);
            float dmg = before - dummy.Health.Current;
            trials++;
            if (dmg > 0.1f) hits++;
            log.AppendLine($"light vs strafing dist={dist:0.0} -> {(dmg > 0.1f ? $"HIT {dmg:0.#}" : "MISS")}");
        }
    }
}
