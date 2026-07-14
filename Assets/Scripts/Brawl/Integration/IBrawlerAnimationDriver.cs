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
    }
}
