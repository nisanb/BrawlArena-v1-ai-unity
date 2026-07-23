using System;
using System.Threading.Tasks;
using UnityEngine;
using Unity.Services.Core;
using Unity.Services.Authentication;

namespace Crownfall.Backend
{
    /// Backend front door. Initializes Unity Gaming Services, signs the player
    /// in anonymously, then pulls their Cloud Save mirror before handing control
    /// back to the menu. Everything is best-effort: if the device is offline or
    /// the project is not configured, the game keeps running on local PlayerPrefs
    /// and simply never syncs. Nothing here is allowed to throw into gameplay.
    ///
    /// Boot is driven by RuntimeInitializeOnLoadMethod so it also covers direct
    /// arena / automation loads that bypass the menu scene. The rig (pump + admin
    /// overlay) is a single DontDestroyOnLoad object, so no scene/forge edits are
    /// needed and it survives menu<->arena transitions.
    public static class CrownfallServices
    {
        /// Completes once init + sign-in + first cloud pull have resolved (or
        /// failed gracefully). Never faults — always transitions to completed.
        /// The boot splash awaits this so the hub renders post-sync data.
        public static Task Ready { get; private set; }

        /// True only when sign-in succeeded and the cloud is reachable this run.
        public static bool Online { get; private set; }

        /// UGS player id once signed in, else null. This is the stable identity
        /// that keys the player's Cloud Save data across devices.
        public static string PlayerId { get; private set; }

        /// Last backend status line, surfaced in the admin overlay.
        public static string Status { get; private set; } = "cold";

        static bool booted;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void Boot()
        {
            if (booted) return;
            booted = true;
            Ready = InitAsync();
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void SpawnRig()
        {
            CrownfallCloudPump.EnsureExists();
        }

        static async Task InitAsync()
        {
            try
            {
                Status = "initializing";
                await UnityServices.InitializeAsync();

                if (!AuthenticationService.Instance.IsSignedIn)
                {
                    Status = "signing in";
                    await AuthenticationService.Instance.SignInAnonymouslyAsync();
                }

                PlayerId = AuthenticationService.Instance.PlayerId;
                Online = true;
                Status = "syncing";

                // Reconcile local <-> cloud before anyone reads player data.
                await CrownfallCloud.PullAsync();

                // Only now start mirroring local changes upward, so the pull's
                // own writes don't bounce straight back as a push.
                CrownfallCloud.BeginWatching();
                Status = "online";
                Debug.Log($"[Crownfall] Backend online. PlayerId={PlayerId}");
            }
            catch (Exception e)
            {
                Online = false;
                Status = "offline: " + e.Message;
                Debug.LogWarning("[Crownfall] Backend offline, running local-only. " + e.Message);
            }
        }

        /// Whether the async chain has resolved (used to gate the boot splash).
        public static bool ReadyCompleted =>
            Ready == null || Ready.IsCompleted;

        /// Signs the current anonymous player out and clears the local session.
        /// Used by the admin overlay to test a fresh-device flow.
        public static async Task SignOutAndForgetAsync()
        {
            try
            {
                if (AuthenticationService.Instance.IsSignedIn)
                    AuthenticationService.Instance.SignOut(true);
            }
            catch (Exception e) { Debug.LogWarning("[Crownfall] sign-out: " + e.Message); }
            Online = false;
            PlayerId = null;
            Status = "signed out";
            await Task.CompletedTask;
        }
    }
}
