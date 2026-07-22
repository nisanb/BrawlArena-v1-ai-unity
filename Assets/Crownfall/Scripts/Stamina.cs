using UnityEngine;

namespace Crownfall
{
    [DisallowMultipleComponent]
    public class Stamina : MonoBehaviour
    {
        public float Max { get; private set; } = 100f;
        public float Current { get; private set; } = 100f;
        public float Fraction => Max > 0f ? Current / Max : 0f;

        CombatMotor motor;
        float regenBlockedUntil;

        public void Configure(CombatMotor owner, float max)
        {
            motor = owner;
            Max = Current = max;
        }

        /// Actions are allowed while at least a sliver remains (souls-style deficit spending).
        public bool CanAfford(float cost) => Current >= cost * 0.35f;

        public bool TrySpend(float cost)
        {
            if (!CanAfford(cost)) return false;
            Current = Mathf.Max(0f, Current - cost);
            regenBlockedUntil = Time.time + Tuning.StaminaRegenDelay;
            return true;
        }

        /// Direct drain (block chip, sprint). Returns true if it emptied the pool.
        public bool Drain(float amount, float regenDelay = 0.6f)
        {
            Current = Mathf.Max(0f, Current - amount);
            regenBlockedUntil = Mathf.Max(regenBlockedUntil, Time.time + regenDelay);
            return Current <= 0f;
        }

        public void RefillFull() { Current = Max; }

        /// Puppet mirror for the streamed value (no regen runs on puppets).
        public void NetSet(float current) { Current = Mathf.Clamp(current, 0f, Max); }

        void Update()
        {
            if (motor == null || motor.IsDead || motor.IsPuppet) return;
            if (Time.time < regenBlockedUntil) return;

            var state = motor.State;
            if (state == MotorState.Attacking || state == MotorState.Rolling || state == MotorState.Staggered)
                return;
            if (motor.IsSprinting) return;

            float rate = Tuning.StaminaRegenPerSec;
            if (motor.IsBlockingHeld) rate *= 0.4f;
            Current = Mathf.Min(Max, Current + rate * Time.deltaTime);
        }
    }
}
