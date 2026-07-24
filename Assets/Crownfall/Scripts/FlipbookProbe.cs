using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;

namespace Crownfall
{
    /// Motion-review flipbook recorder: drives the player motor through a fixed
    /// scripted sequence (AI frozen), captures 0.1s cells from the live game
    /// camera plus a character-locked side camera, and writes 4x4 contact sheets
    /// with per-tick clip/speed telemetry. Spawned by editor tooling only.
    public class FlipbookProbe : MonoBehaviour
    {
        public string outDir;
        public float interval = 0.1f;

        CombatMotor subject;
        Camera mainCam;
        Camera sideCam;
        readonly List<Texture2D> mainFrames = new List<Texture2D>();
        readonly List<Texture2D> sideFrames = new List<Texture2D>();
        readonly StringBuilder data = new StringBuilder();
        const int CellW = 480, CellH = 270;

        public static void Begin(string dir)
        {
            var go = new GameObject("FlipbookProbe");
            go.AddComponent<FlipbookProbe>().outDir = dir;
        }

        void Start()
        {
            Directory.CreateDirectory(outDir);
            subject = MatchManager.I.PlayerMotor;
            mainCam = Camera.main;

            foreach (var ai in FindObjectsByType<AIController>(FindObjectsSortMode.None)) ai.enabled = false;
            var pc = subject.GetComponent<PlayerController>();
            if (pc != null) pc.enabled = false;

            var sideGo = new GameObject("ProbeSideCam");
            sideCam = sideGo.AddComponent<Camera>();
            sideCam.enabled = false;
            sideCam.fieldOfView = 45f;

            StartCoroutine(Script());
            StartCoroutine(CaptureLoop());
        }

        IEnumerator Script()
        {
            var m = subject;
            CombatMotor enemy = null;
            foreach (var e in MatchManager.I.AliveEnemiesOf(m.Identity.team)) { enemy = e; break; }

            Phase("idle"); yield return Hold(0.5f, Vector3.zero, false);
            Phase("run-fwd"); yield return Hold(1.1f, Vector3.forward, false);
            Phase("run-right-turn"); yield return Hold(0.9f, Vector3.right, false);
            Phase("run-back"); yield return Hold(0.9f, Vector3.back, false);
            Phase("run-left"); yield return Hold(0.9f, Vector3.left, false);
            Phase("sprint"); yield return Hold(1.1f, Vector3.forward, true);
            Phase("stop"); yield return Hold(0.5f, Vector3.zero, false);
            m.LockTarget = enemy;
            Phase("strafe-locked"); yield return Hold(1.2f, Vector3.right, false);
            Phase("roll"); m.RequestRoll(Vector3.right); yield return Hold(1.1f, Vector3.zero, false);
            Phase("light1"); m.RequestLight(); yield return Hold(0.55f, Vector3.zero, false);
            Phase("light2"); m.RequestLight(); yield return Hold(0.6f, Vector3.zero, false);
            Phase("light3"); m.RequestLight(); yield return Hold(1.3f, Vector3.zero, false);
            Phase("heavy"); m.RequestHeavy(); yield return Hold(1.5f, Vector3.zero, false);
            // 2026-07-24: the phases above all attack from a standstill, so they
            // exercise none of the movement-authority / cancel-window work. These
            // three do: swing while pushing forward (authority ramp), swing then
            // immediately steer off (move-cancel out of recovery), and the skill.
            Phase("attack-while-moving"); m.RequestLight(); yield return Hold(1.0f, Vector3.forward, false);
            Phase("attack-move-cancel"); m.RequestLight(); yield return Hold(1.0f, Vector3.right, false);
            Phase("skill"); m.RequestSkill(); yield return Hold(1.8f, Vector3.zero, false);
            Phase("end"); yield return Hold(0.4f, Vector3.zero, false);
            Finish();
        }

        void Phase(string name) { data.AppendLine($"t={Time.time:0.00} PHASE {name}"); }

        IEnumerator Hold(float dur, Vector3 dir, bool sprint)
        {
            float end = Time.time + dur;
            while (Time.time < end)
            {
                subject.SetMoveInput(dir, sprint);
                yield return null;
            }
        }

        IEnumerator CaptureLoop()
        {
            var wait = new WaitForSeconds(interval);
            while (true)
            {
                CaptureOne();
                yield return wait;
            }
        }

        void CaptureOne()
        {
            var t = subject.transform;
            sideCam.transform.position = t.position + t.right * 3.6f + Vector3.up * 1.15f;
            sideCam.transform.LookAt(t.position + Vector3.up * 0.95f);

            mainFrames.Add(Render(mainCam));
            sideFrames.Add(Render(sideCam));

            string clip = "?";
            var infos = subject.Anim.GetCurrentAnimatorClipInfo(0);
            float bestW = -1f;
            foreach (var ci in infos)
                if (ci.weight > bestW && ci.clip != null) { bestW = ci.weight; clip = $"{ci.clip.name}({ci.weight:0.00})"; }
            float nt = subject.Anim.GetCurrentAnimatorStateInfo(0).normalizedTime;
            var v = subject.PlanarVelocity;
            data.AppendLine($"t={Time.time:0.00} state={subject.State} clip={clip} nt={nt:0.00} speed={v.magnitude:0.00} yaw={t.eulerAngles.y:0} pos=({t.position.x:0.0},{t.position.z:0.0})");
        }

        Texture2D Render(Camera cam)
        {
            var rt = RenderTexture.GetTemporary(CellW, CellH, 24);
            var prev = cam.targetTexture;
            var prevActive = RenderTexture.active;
            cam.targetTexture = rt;
            cam.Render();
            RenderTexture.active = rt;
            var tex = new Texture2D(CellW, CellH, TextureFormat.RGB24, false);
            tex.ReadPixels(new Rect(0, 0, CellW, CellH), 0, 0);
            tex.Apply();
            cam.targetTexture = prev;
            RenderTexture.active = prevActive;
            RenderTexture.ReleaseTemporary(rt);
            return tex;
        }

        void Finish()
        {
            StopAllCoroutines();
            WriteSheets(mainFrames, "sheet-main");
            WriteSheets(sideFrames, "sheet-side");
            File.WriteAllText(Path.Combine(outDir, "data.txt"), data.ToString());
            Debug.Log("[FlipbookProbe] DONE " + outDir + " frames=" + mainFrames.Count);
            Destroy(sideCam.gameObject);
            Destroy(gameObject);
        }

        void WriteSheets(List<Texture2D> frames, string prefix)
        {
            const int perSheet = 16;
            for (int s = 0; s * perSheet < frames.Count; s++)
            {
                var sheet = new Texture2D(CellW * 4, CellH * 4, TextureFormat.RGB24, false);
                for (int i = 0; i < perSheet; i++)
                {
                    int idx = s * perSheet + i;
                    int row = 3 - i / 4;
                    int col = i % 4;
                    var px = idx < frames.Count ? frames[idx].GetPixels() : new Color[CellW * CellH];
                    sheet.SetPixels(col * CellW, row * CellH, CellW, CellH, px);
                }
                sheet.Apply();
                File.WriteAllBytes(Path.Combine(outDir, $"{prefix}-{s:00}.png"), sheet.EncodeToPNG());
                Destroy(sheet);
            }
            foreach (var f in frames) Destroy(f);
        }
    }
}
