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
        public const float BasicAttackReloadInterval = 2.25f;
        public const float ArcaneFlowCapacity = 60f;
        public const float WardStepCost = 20f;
        public const float WardStepDistance = 2.75f;
        public const float WardStepDuration = 0.16f;
        public const float WardRegenPerSecond = 8f;
        public const float WardRegenDelay = 0.75f;
        public const float CastMovementMultiplier = 0.8f;
        public const float AutoAimCorrectionDegrees = 12f;

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
    }
}
