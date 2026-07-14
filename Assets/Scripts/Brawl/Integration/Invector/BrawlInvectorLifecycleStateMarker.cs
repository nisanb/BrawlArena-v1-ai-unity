using UnityEngine;

namespace BrawlArena
{
    /// <summary>
    /// Trace-only marker for project lifecycle states. It never mutates Brawl
    /// gameplay, Invector resources, Rigidbody/collider state, or Animator
    /// parameters.
    /// </summary>
    public sealed class BrawlInvectorLifecycleStateMarker : StateMachineBehaviour
    {
        [SerializeField]
        BrawlInvectorLifecyclePresentation presentation;

        public BrawlInvectorLifecyclePresentation Presentation => presentation;

        public void Configure(BrawlInvectorLifecyclePresentation value)
        {
            presentation = value;
        }

        public override void OnStateEnter(
            Animator animator,
            AnimatorStateInfo stateInfo,
            int layerIndex)
        {
            animator.GetComponent<InvectorBrawlerAnimationDriver>()?
                .NotifyLifecycleStateEntered(presentation);
        }

        public override void OnStateExit(
            Animator animator,
            AnimatorStateInfo stateInfo,
            int layerIndex)
        {
            animator.GetComponent<InvectorBrawlerAnimationDriver>()?
                .NotifyLifecycleStateExited(presentation);
        }
    }
}
