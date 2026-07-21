using UnityEditor;
using UnityEngine;

namespace BrawlAutomation
{
    /// Play-mode bootstrapper for the Crownfall skill probe. The harness action
    /// stores the request in SessionState (survives the play-mode domain reload),
    /// this watcher fires it once the match manager exists in play mode.
    [InitializeOnLoad]
    public static class CrownfallProbeBoot
    {
        const string DirKey = "crownfall.probe.dir";
        const string ClassKey = "crownfall.probe.class";

        static CrownfallProbeBoot()
        {
            EditorApplication.update += Tick;
        }

        public static void Arm(string dir, int classIndex)
        {
            SessionState.SetString(DirKey, dir);
            SessionState.SetInt(ClassKey, classIndex);
            if (!EditorApplication.isPlaying) EditorApplication.EnterPlaymode();
        }

        static void Tick()
        {
            if (!EditorApplication.isPlaying) return;
            string dir = SessionState.GetString(DirKey, "");
            if (string.IsNullOrEmpty(dir)) return;

            var mm = Crownfall.MatchManager.I;
            if (mm == null) return;

            SessionState.EraseString(DirKey);
            int classIndex = SessionState.GetInt(ClassKey, 2);
            SessionState.EraseInt(ClassKey);

            mm.AutoStart(classIndex, true);
            Crownfall.SkillProbe.Begin(dir);
            Debug.Log($"[CrownfallProbeBoot] probe armed: class={classIndex} dir={dir}");
        }
    }
}
