using UnityEngine;

namespace Crownfall.Backend
{
    /// The single DontDestroyOnLoad host for the backend runtime bits: it ticks
    /// the Cloud Save debounce and flushes a pending push when the app is
    /// backgrounded or quit (the moments a mobile player is most likely to lose
    /// unsynced progress). It also carries the admin overlay component so the
    /// whole backend rig is one object, spawned from code — no scene or forge
    /// wiring, and it survives menu<->arena scene loads.
    [DefaultExecutionOrder(-100)]
    public class CrownfallCloudPump : MonoBehaviour
    {
        static CrownfallCloudPump instance;

        public static void EnsureExists()
        {
            if (instance != null) return;
            var go = new GameObject("CrownfallBackend");
            DontDestroyOnLoad(go);
            instance = go.AddComponent<CrownfallCloudPump>();
            go.AddComponent<CrownfallAdmin>();
        }

        void Update()
        {
            CrownfallCloud.Tick(Time.unscaledDeltaTime);
        }

        void OnApplicationPause(bool paused)
        {
            if (paused) CrownfallCloud.FlushNow();
        }

        void OnApplicationQuit()
        {
            CrownfallCloud.FlushNow();
        }
    }
}
