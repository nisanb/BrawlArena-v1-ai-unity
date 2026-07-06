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

        public void TakeDamage(float amount, GameObject attacker)
        {
            if (IsDead || Invulnerable || amount <= 0f) return;
            Current = Mathf.Max(0f, Current - amount);
            Changed?.Invoke();
            Damaged?.Invoke(amount, attacker);
            if (Current <= 0f) Died?.Invoke(attacker);
        }

        public void Heal(float amount)
        {
            if (IsDead || amount <= 0f || Current >= maxHealth) return;
            float restored = Mathf.Min(amount, maxHealth - Current);
            Current += restored;
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
