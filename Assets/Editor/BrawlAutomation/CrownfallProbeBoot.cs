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
        const string KindKey = "crownfall.probe.kind";
        static double nextReadyAt;

        static CrownfallProbeBoot()
        {
            EditorApplication.update += Tick;
        }

        public static void Arm(string dir, int classIndex, string kind = "skill")
        {
            SessionState.SetString(DirKey, dir);
            SessionState.SetInt(ClassKey, classIndex);
            SessionState.SetString(KindKey, kind);
            if (!EditorApplication.isPlaying) EditorApplication.EnterPlaymode();
        }

        static void Tick()
        {
            if (!EditorApplication.isPlaying) return;
            string dir = SessionState.GetString(DirKey, "");
            if (string.IsNullOrEmpty(dir)) return;

            var mm = Crownfall.MatchManager.I;
            if (mm == null) return;

            string kind = SessionState.GetString(KindKey, "skill");

            // networked run: "net" = PUN offline loopback; "netlive" = the real
            // Photon cloud. Same state machine either way — quick-match,
            // ready-up, then evidence-probe the match.
            if (kind == "net" || kind == "netlive")
            {
                var net = Crownfall.CrownfallNet.I;
                if (net == null) return;
                if (net.Phase == Crownfall.NetPhase.Idle)
                {
                    Crownfall.CrownfallMeta.SelectedClass = SessionState.GetInt(ClassKey, 2);
                    net.QuickMatch(offlineSmoke: kind == "net");
                    // company-aware ready-up lives in CrownfallNet's auto pilot
                    net.EnableAutoOnline();
                    return;
                }
                if (net.Phase == Crownfall.NetPhase.InMatch)
                {
                    ClearKeys();
                    Crownfall.SkillProbe.Begin(dir);
                    Debug.Log("[CrownfallProbeBoot] net smoke probe running: " + dir);
                }
                return;
            }

            ClearKeys();
            int classIndex = SessionState.GetInt(ClassKey, 2);
            mm.AutoStart(classIndex, true);
            if (kind == "death") Crownfall.DeathPoseProbe.Begin(dir);
            else Crownfall.SkillProbe.Begin(dir);
            Debug.Log($"[CrownfallProbeBoot] probe armed: kind={kind} class={classIndex} dir={dir}");
        }

        static void ClearKeys()
        {
            SessionState.EraseString(DirKey);
            SessionState.EraseInt(ClassKey);
            SessionState.EraseString(KindKey);
        }
    }
}
