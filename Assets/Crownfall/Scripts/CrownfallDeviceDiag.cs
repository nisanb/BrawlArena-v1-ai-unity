using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace Crownfall
{
    /// On-device diagnostic overlay for TestFlight: a small boot heartbeat line
    /// plus any captured exceptions, drawn via IMGUI so it appears even when the
    /// scene camera renders nothing. Mobile builds only; inert in the editor.
    public class CrownfallDeviceDiag : MonoBehaviour
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Boot()
        {
            if (!Application.isMobilePlatform) return;
            var go = new GameObject("CrownfallDeviceDiag");
            DontDestroyOnLoad(go);
            go.AddComponent<CrownfallDeviceDiag>();
            Debug.Log("[CrownfallDiag] boot, scene=" + SceneManager.GetActiveScene().name);
        }

        readonly List<string> errors = new List<string>();
        int errorCount;

        // "pa" = active UI Images still using preserveAspect (the white-icon bug):
        // 0 means this build has the icon fix, >0 means it does not. Recomputed
        // periodically since FindObjects is not free.
        int preserveAspectImages = -1;
        int lastPaFrame = -999;

        int CountPreserveAspect()
        {
            int n = 0;
            foreach (var img in FindObjectsByType<Image>(FindObjectsInactive.Exclude, FindObjectsSortMode.None))
                if (img.preserveAspect) n++;
            return n;
        }

        void OnEnable() { Application.logMessageReceived += OnLog; }
        void OnDisable() { Application.logMessageReceived -= OnLog; }

        void OnLog(string condition, string stack, LogType type)
        {
            if (type != LogType.Exception && type != LogType.Error) return;
            errorCount++;
            if (errors.Count < 6)
            {
                string s = string.IsNullOrEmpty(stack) ? "" :
                    "\n" + (stack.Length > 260 ? stack.Substring(0, 260) : stack);
                errors.Add(condition + s);
            }
        }

        void OnGUI()
        {
            var style = new GUIStyle(GUI.skin.label)
            {
                fontSize = Mathf.Max(14, Screen.height / 55),
                wordWrap = true,
            };
            var mm = MatchManager.I;
            string foe = "";
            if (mm != null && mm.PlayerMotor != null && mm.State == MatchState.Fighting)
            {
                var pm = mm.PlayerMotor;
                CombatMotor nearest = null;
                float best = float.MaxValue;
                foreach (var e in mm.AliveEnemiesOf(pm.Identity.team))
                {
                    float d = (e.transform.position - pm.transform.position).sqrMagnitude;
                    if (d < best) { best = d; nearest = e; }
                }
                if (nearest != null)
                    foe = $"  foe:{nearest.Identity.displayName} {nearest.Health.Current:0}/{nearest.Health.Max:0}";
            }
            if (Time.frameCount - lastPaFrame > 30)
            {
                preserveAspectImages = CountPreserveAspect();
                lastPaFrame = Time.frameCount;
            }
            string beat = $"Crownfall v{Application.version}  {SceneManager.GetActiveScene().name}" +
                          $"  cam:{(Camera.main != null ? "ok" : "MISSING")}" +
                          $"  match:{(mm != null ? mm.State.ToString() : "MISSING")}" +
                          $"  pa:{preserveAspectImages}" + foe +
                          (errorCount > 0 ? $"  ERRORS:{errorCount}" : "");
            style.normal.textColor = errorCount > 0 ? Color.red : new Color(1f, 1f, 1f, 0.5f);
            GUI.Label(new Rect(14, 6, Screen.width - 28, 46), beat, style);

            // Draw a few icon textures via IMGUI (a render path completely
            // separate from UGUI). If these show but the UGUI icons are white,
            // the textures are fine and it's the Image render path; if these are
            // white too, it's the texture data itself.
            var hud = FindFirstObjectByType<HUDController>();
            if (hud != null)
            {
                var samples = new (string, Sprite)[]
                {
                    ("shop", hud.menuShop), ("inbox", hud.menuInbox), ("crown", hud.iconCrown),
                    ("gear", hud.icoGear), ("coin", hud.iconCoinBar), ("gem", hud.iconGemBar),
                };
                float x = 14f, y = Screen.height - 116f;
                var lbl = new GUIStyle(style) { fontSize = Mathf.Max(11, Screen.height / 80) };
                lbl.normal.textColor = Color.yellow;
                foreach (var (nm, sp) in samples)
                {
                    if (sp != null && sp.texture != null)
                    {
                        GUI.DrawTexture(new Rect(x, y, 84f, 84f), sp.texture, ScaleMode.ScaleToFit);
                        GUI.Label(new Rect(x, y + 84f, 100f, 22f), $"{nm} {sp.texture.width}", lbl);
                    }
                    else GUI.Label(new Rect(x, y, 100f, 84f), $"{nm} NULL", lbl);
                    x += 96f;
                }
            }

            if (errors.Count == 0) return;
            var errStyle = new GUIStyle(style) { fontSize = Mathf.Max(12, Screen.height / 70) };
            errStyle.normal.textColor = new Color(1f, 0.35f, 0.3f);
            float y = 52;
            foreach (var e in errors)
            {
                GUI.Label(new Rect(14, y, Screen.width - 28, 190), e, errStyle);
                y += 145;
            }
        }
    }
}
