using System;
using UnityEngine;

namespace Crownfall
{
    [DisallowMultipleComponent]
    public class Health : MonoBehaviour
    {
        public float Max { get; private set; } = 100f;
        public float Current { get; private set; } = 100f;
        public float Poise { get; private set; } = 40f;
        public float MaxPoise { get; private set; } = 40f;
        public bool IsDead { get; private set; }

        public CombatMotor Motor { get; private set; }
        public CombatantIdentity Identity { get; private set; }

        public event Action<HitInfo, HitResult> Damaged;
        public event Action<CombatMotor> Died;
        public event Action Revived;

        float lastPoiseHitTime = -99f;

        public void Configure(CombatMotor motor, float maxHp, float maxPoise)
        {
            Motor = motor;
            Identity = GetComponent<CombatantIdentity>();
            Max = Current = maxHp;
            MaxPoise = Poise = maxPoise;
        }

        void Update()
        {
            if (Motor != null && Motor.IsPuppet) return;
            if (!IsDead && Poise < MaxPoise && Time.time - lastPoiseHitTime > Tuning.PoiseRegenDelay)
                Poise = Mathf.Min(MaxPoise, Poise + Tuning.PoiseRegenPerSec * Time.deltaTime);
        }

        public HitResult TakeHit(HitInfo hit)
        {
            var res = new HitResult();
            if (IsDead) return res;
            if (Motor != null && Motor.IsInvulnerable) return res;

            bool blocked = !hit.unblockable && Motor != null && Motor.IsBlockEffectiveAgainst(hit);
            float dmg = hit.damage;

            if (blocked)
            {
                dmg = hit.damage * (Motor.Kit != null ? Motor.Kit.blockDamageFactor : 0.3f);
                bool guardBroke = Motor.OnBlockedHit(hit);
                if (guardBroke)
                {
                    blocked = false;
                    dmg = hit.damage * 0.6f;
                    Poise = MaxPoise;
                    res.staggered = true;
                    Motor.EnterStagger();
                }
                res.blocked = blocked;
            }

            Current = Mathf.Max(0f, Current - dmg);
            res.landed = true;
            res.damageDealt = dmg;
            Motor?.MarkRevealed(0.8f); // taking a hit briefly reveals you

            if (!blocked && !res.staggered)
            {
                Poise -= hit.poiseDamage;
                lastPoiseHitTime = Time.time;
                if (Current > 0f && Poise <= 0f)
                {
                    Poise = MaxPoise;
                    res.staggered = true;
                    Motor?.EnterStagger();
                }
                else if (Current > 0f)
                {
                    Motor?.NotifyHitReact(hit);
                }
            }

            if (Current <= 0f)
            {
                IsDead = true;
                res.killed = true;
                Motor?.EnterDeath();
                Died?.Invoke(hit.attacker);
            }

            Damaged?.Invoke(hit, res);
            return res;
        }

        /// Restore HP (allies only, capped at Max). Returns the amount actually
        /// healed so callers can suppress the popup/vfx when already topped off.
        public float Heal(float amount)
        {
            if (IsDead || amount <= 0f) return 0f;
            float before = Current;
            Current = Mathf.Min(Max, Current + amount);
            return Current - before;
        }

        public void ReviveFull()
        {
            IsDead = false;
            Current = Max;
            Poise = MaxPoise;
            Revived?.Invoke();
        }

        /// Puppet mirror: vitals streamed from the owning client. No events fire
        /// here — deaths/kill-feed flow through the network match authority.
        public void NetApply(float current, float poise, bool dead)
        {
            Current = current;
            Poise = poise;
            IsDead = dead;
        }
    }
}
