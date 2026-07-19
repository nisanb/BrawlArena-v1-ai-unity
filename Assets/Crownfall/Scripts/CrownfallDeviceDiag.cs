using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

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
            string beat = $"Crownfall f{Time.frameCount}  {SceneManager.GetActiveScene().name}" +
                          $"  cam:{(Camera.main != null ? "ok" : "MISSING")}" +
                          $"  match:{(mm != null ? mm.State.ToString() : "MISSING")}" +
                          (errorCount > 0 ? $"  ERRORS:{errorCount}" : "");
            style.normal.textColor = errorCount > 0 ? Color.red : new Color(1f, 1f, 1f, 0.5f);
            GUI.Label(new Rect(14, 6, Screen.width - 28, 46), beat, style);

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
