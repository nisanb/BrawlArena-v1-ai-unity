using System;
using UnityEngine;

namespace BrawlArena
{
    public class Health : MonoBehaviour
    {
        [SerializeField] private float maxHealth = 100f;

        public float Max => maxHealth;
        public float Current { get; private set; }
        public bool IsDead => Current <= 0f;
        public bool Invulnerable { get; set; }

        /// <summary>(damage amount, attacker root object)</summary>
        public event Action<float, GameObject> Damaged;
        /// <summary>(amount actually restored)</summary>
        public event Action<float> Healed;
        /// <summary>(attacker root object)</summary>
        public event Action<GameObject> Died;
        public event Action Changed;

        void Awake()
        {
            Current = maxHealth;
        }

        public void SetMax(float value, bool refill = true)
        {
            maxHealth = Mathf.Max(1f, value);
            if (refill) Current = maxHealth;
            Changed?.Invoke();
        }

        /// <summary>
        /// Applies damage and returns the amount of health actually removed.
        /// Events receive this applied delta as well, so overkill, invulnerability
        /// and post-match damage can never inflate combat stats or Super charge.
        /// </summary>
        public float TakeDamage(float amount, GameObject attacker)
        {
            if (IsDead || Invulnerable || amount <= 0f) return 0f;
            if (MatchManager.Instance != null && MatchManager.Instance.State == MatchState.Ended)
                return 0f;

            float before = Current;
            Current = Mathf.Max(0f, Current - amount);
            float applied = before - Current;
            if (applied <= 0f) return 0f;

            BalanceTelemetryRuntime.RecordDamage(this, attacker, applied);
            Changed?.Invoke();
            Damaged?.Invoke(applied, attacker);
            if (Current <= 0f) Died?.Invoke(attacker);
            return applied;
        }

        public void Heal(float amount)
        {
            if (IsDead || amount <= 0f || Current >= maxHealth) return;
            float restored = Mathf.Min(amount, maxHealth - Current);
            Current += restored;
            BalanceTelemetryRuntime.RecordHealing(this, restored);
            Changed?.Invoke();
            Healed?.Invoke(restored);
        }

        public void Revive()
        {
            Current = maxHealth;
            Changed?.Invoke();
        }
    }
}
