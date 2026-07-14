using UnityEngine;

namespace BrawlArena
{
    public enum BrawlInvectorLifecyclePresentation
    {
        None = 0,
        Death = 1,
        Respawn = 2,
        Victory = 3,
    }

    /// <summary>
    /// Project-owned names and hashes for the additive lifecycle overlay in
    /// the copied Invector controller. Runtime callers use semantic controller
    /// methods; the builder is the only owner of these graph names.
    /// </summary>
    public static class BrawlInvectorLifecycleParameters
    {
        public const string StateMachineName = "BrawlLifecycle";
        public const string DeathStateName = "Death";
        public const string RespawnStateName = "Respawn";
        public const string VictoryStateName = "Victory";
        public const string DeathTriggerName = "BrawlDeath";
        public const string RespawnTriggerName = "BrawlRespawn";
        public const string VictoryTriggerName = "BrawlVictory";

        public const string DeathFullPath = "FullBody.BrawlLifecycle.Death";
        public const string RespawnFullPath = "FullBody.BrawlLifecycle.Respawn";
        public const string VictoryFullPath = "FullBody.BrawlLifecycle.Victory";

        public static readonly int DeathTrigger = Animator.StringToHash(DeathTriggerName);
        public static readonly int RespawnTrigger = Animator.StringToHash(RespawnTriggerName);
        public static readonly int VictoryTrigger = Animator.StringToHash(VictoryTriggerName);
        public static readonly int DeathState = Animator.StringToHash(DeathFullPath);
        public static readonly int RespawnState = Animator.StringToHash(RespawnFullPath);
        public static readonly int VictoryState = Animator.StringToHash(VictoryFullPath);

        public static int TriggerHash(BrawlInvectorLifecyclePresentation presentation)
        {
            switch (presentation)
            {
                case BrawlInvectorLifecyclePresentation.Death:
                    return DeathTrigger;
                case BrawlInvectorLifecyclePresentation.Respawn:
                    return RespawnTrigger;
                case BrawlInvectorLifecyclePresentation.Victory:
                    return VictoryTrigger;
                default:
                    return 0;
            }
        }
    }

}
