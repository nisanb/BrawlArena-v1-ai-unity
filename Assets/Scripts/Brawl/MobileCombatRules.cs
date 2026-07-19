using UnityEngine;

namespace BrawlArena
{
    /// <summary>
    /// Time-independent rules for the mobile cast and Ward Step loop. Keeping
    /// the arithmetic here makes input/controller wiring easy to verify in
    /// EditMode without advancing a scene or coroutine.
    /// </summary>
    public static class MobileCombatRules
    {
        public const int BasicAttackChargeCapacity = 3;
        public const float BasicAttackReloadInterval = 1.6f;
        public const float ArcaneFlowCapacity = 60f;
        public const float WardStepCost = 25f;
        public const float WardStepDistance = 3.2f;
        public const float WardStepDuration = 0.42f;
        public const float WardRegenPerSecond = 8f;
        public const float WardRegenDelay = 0.75f;
        public const float AutoAimCorrectionDegrees = 12f;

        /// <summary>
        /// The dodge roll's invulnerability window. Souls-grade dodges are
        /// only worth committing to when a well-timed roll beats the hit.
        /// </summary>
        public const float RollInvulnerabilitySeconds = 0.3f;

        /// <summary>Souls commitment: a melee windup roots most of your speed.</summary>
        public const float MeleeWindupMovementMultiplier = 0.3f;
        /// <summary>Souls commitment: a draw/cast windup slows movement hard.</summary>
        public const float RangedWindupMovementMultiplier = 0.45f;
        /// <summary>Recovery keeps partial commitment after the hit lands.</summary>
        public const float RecoveryMovementMultiplier = 0.55f;

        /// <summary>Bounded facing turn rate used by committed attack/Super presentation.</summary>
        public const float CombatTurnRateDegreesPerSecond = 540f;

        /// <summary>Half of this arc, either side of the committed direction, is a valid melee hit.</summary>
        public const float MeleeArcDegrees = 100f;

        /// <summary>Hit-stop applied to the attacker on a confirmed basic hit.</summary>
        public const float HitStopLightAttacker = 0.075f;
        /// <summary>Hit-stop applied to the victim on a confirmed basic hit.</summary>
        public const float HitStopLightVictim = 0.095f;
        /// <summary>Hit-stop applied to the attacker on a confirmed Super hit.</summary>
        public const float HitStopHeavyAttacker = 0.13f;
        /// <summary>Hit-stop applied to the victim on a confirmed Super hit.</summary>
        public const float HitStopHeavyVictim = 0.17f;

        /// <summary>Minimum spacing between two hit-reaction plays on the same body.</summary>
        public const float HitReactionThrottleSeconds = 0.35f;

        /// <summary>Local-player camera feel: FOV kick when a Super fires.</summary>
        public const float SuperFovKickDegrees = 5f;
        public const float SuperFovKickSeconds = 0.35f;
        /// <summary>Local-player camera feel: directional punch when hit.</summary>
        public const float HitPunchAmplitude = 0.3f;

        public const float AttackImpactDelayClipFraction = 0.45f;
        public const float AttackImpactDelayMinSeconds = 0.14f;
        public const float AttackImpactDelayMaxSeconds = 1.1f;

        public static bool TrySpendBasicAttackCharge(ref int current)
        {
            current = Mathf.Clamp(current, 0, BasicAttackChargeCapacity);
            if (current <= 0) return false;
            current--;
            return true;
        }

        /// <summary>
        /// Advances the one-at-a-time basic-attack reload clock. The partial
        /// clock is retained while more shots are spent, so firing a second
        /// charge cannot restart or accelerate the charge already reloading.
        /// </summary>
        public static void RegenerateBasicAttackCharges(ref int current,
            ref float reloadElapsed, float reloadInterval, float deltaSeconds)
        {
            current = Mathf.Clamp(current, 0, BasicAttackChargeCapacity);
            reloadElapsed = Mathf.Max(0f, reloadElapsed);
            if (current >= BasicAttackChargeCapacity)
            {
                reloadElapsed = 0f;
                return;
            }

            reloadInterval = reloadInterval > 0f
                ? reloadInterval
                : BasicAttackReloadInterval;
            if (deltaSeconds <= 0f) return;

            reloadElapsed += deltaSeconds;
            while (current < BasicAttackChargeCapacity &&
                   reloadElapsed + 0.0001f >= reloadInterval)
            {
                reloadElapsed = Mathf.Max(0f, reloadElapsed - reloadInterval);
                current++;
            }

            if (current >= BasicAttackChargeCapacity) reloadElapsed = 0f;
        }

        public static bool TrySpendWardFlow(ref float current, float cost = WardStepCost)
        {
            current = Mathf.Max(0f, current);
            cost = Mathf.Max(0f, cost);
            if (current + 0.0001f < cost) return false;
            current = Mathf.Max(0f, current - cost);
            return true;
        }

        public static float RegenerateWardFlow(float current, float capacity,
            float ratePerSecond, float deltaSeconds)
        {
            capacity = Mathf.Max(0f, capacity);
            current = Mathf.Clamp(current, 0f, capacity);
            if (ratePerSecond <= 0f || deltaSeconds <= 0f) return current;
            return Mathf.Min(capacity, current + ratePerSecond * deltaSeconds);
        }

        /// <summary>
        /// Rotates a committed planar direction toward a live target by no more
        /// than the authored correction cone. Manual casts simply skip this rule.
        /// </summary>
        public static Vector3 LimitAimCorrection(Vector3 committedDirection,
            Vector3 desiredDirection, float maxDegrees = AutoAimCorrectionDegrees)
        {
            committedDirection.y = 0f;
            desiredDirection.y = 0f;
            if (committedDirection.sqrMagnitude <= 0.0001f)
                return desiredDirection.sqrMagnitude > 0.0001f
                    ? desiredDirection.normalized
                    : Vector3.forward;
            if (desiredDirection.sqrMagnitude <= 0.0001f)
                return committedDirection.normalized;

            float radians = Mathf.Max(0f, maxDegrees) * Mathf.Deg2Rad;
            return Vector3.RotateTowards(committedDirection.normalized,
                desiredDirection.normalized, radians, 0f).normalized;
        }

        /// <summary>
        /// Movement commitment during a basic attack. Souls-grade feel means
        /// a swing is a decision: the windup roots most movement, and the
        /// recovery keeps partial commitment until the animation releases.
        /// </summary>
        public static float AttackPhaseMovementMultiplier(bool isMelee, bool preImpact)
        {
            if (!preImpact) return RecoveryMovementMultiplier;
            return isMelee ? MeleeWindupMovementMultiplier : RangedWindupMovementMultiplier;
        }

        /// <summary>
        /// Longer knocks take longer to resolve, but every knock keeps the same
        /// fast-start/slow-finish shape regardless of distance.
        /// </summary>
        public static float KnockbackDuration(float distance)
        {
            return Mathf.Clamp(0.2f + Mathf.Max(0f, distance) * 0.075f, 0.2f, 0.6f);
        }

        /// <summary>
        /// Ease-out cubic: fast off the hit, decaying into the landing. Callers
        /// multiply the result by the total knockback distance for the
        /// displacement covered by time t01.
        /// </summary>
        public static float KnockbackProgress(float t01)
        {
            float remaining = 1f - Mathf.Clamp01(t01);
            return 1f - remaining * remaining * remaining;
        }

        /// <summary>
        /// Animation-derived hit timing: a resolvable clip scales into a
        /// bounded delay; a missing/zero-length clip falls back unmodified.
        /// </summary>
        public static float ResolveAnimationImpactDelay(float clipLengthSeconds, float fallbackSeconds)
        {
            if (clipLengthSeconds <= 0f) return fallbackSeconds;
            return Mathf.Clamp(clipLengthSeconds * AttackImpactDelayClipFraction,
                AttackImpactDelayMinSeconds, AttackImpactDelayMaxSeconds);
        }

        /// <summary>
        /// CharacterSkill's AttackSpeed progression scales attackHitDelay
        /// directly; reapplying that as a ceiling keeps a trained hero's
        /// animation-derived delay from ever exceeding the purchased speed-up.
        /// </summary>
        public static float ApplyAttackSpeedProgression(float derivedSeconds, float progressedFallbackSeconds)
        {
            if (derivedSeconds <= 0f) return progressedFallbackSeconds;
            return Mathf.Min(derivedSeconds, progressedFallbackSeconds);
        }
    }
}
