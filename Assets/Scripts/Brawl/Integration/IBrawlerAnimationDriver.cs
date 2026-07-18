namespace BrawlArena
{
    /// <summary>
    /// Owns every runtime Animator write for one brawler body. Gameplay sends
    /// semantic requests without exposing Invector graph state names to combat
    /// or lifecycle code.
    /// </summary>
    public interface IBrawlerAnimationDriver
    {
        void TickLocomotion(float normalizedSpeed);
        void PlayBasicAttack();
        void PlaySuper();
        void PlayHitReaction();
        void PlayDeath();
        void PlayRespawn();
        void PlayVictory();

        /// <summary>
        /// Animation-derived hit timing for the currently equipped weapon clip.
        /// Returns fallbackSeconds whenever the backing Animator, override
        /// controller, or expected clip cannot be resolved.
        /// </summary>
        float GetAttackImpactDelay(bool strongAttack, float fallbackSeconds);

        /// <summary>
        /// Hit-stop: freezes presentation for the given seconds without
        /// touching Time.timeScale. A no-op is an acceptable implementation
        /// for test doubles. The latest call always wins over one in flight.
        /// </summary>
        void PauseAnimation(float seconds);
    }
}
