using UnityEngine;

namespace Crownfall
{
    /// Magic bolt: brief homing, then straight flight. Rolling through it with
    /// i-frames lets it pass; blocking eats most of it.
    public class Projectile : MonoBehaviour
    {
        CombatMotor owner;
        ElementId element;
        float damage, poiseDamage, speed;
        CombatMotor homingTarget;
        float homingUntil;
        float dieAt;

        public static void Fire(CombatMotor owner, ElementId element, Vector3 origin, Vector3 aimPoint,
            CombatMotor homingTarget, float damage, float poiseDamage, float speed,
            float visualScale = 1f)
        {
            var go = new GameObject("Bolt_" + element);
            go.transform.position = origin;
            if (visualScale != 1f) go.transform.localScale = Vector3.one * visualScale;
            Vector3 dir = aimPoint - origin;
            go.transform.rotation = dir.sqrMagnitude > 0.001f ? Quaternion.LookRotation(dir) : owner.transform.rotation;

            var p = go.AddComponent<Projectile>();
            p.owner = owner;
            p.element = element;
            p.damage = damage;
            p.poiseDamage = poiseDamage;
            p.speed = speed;
            p.homingTarget = homingTarget;
            p.homingUntil = Time.time + 0.45f;
            p.dieAt = Time.time + 3.5f;

            GameEffects.I?.AttachMissileVisual(go.transform, element);
        }

        void Update()
        {
            if (Time.time >= dieAt) { Explode(transform.position); return; }

            if (homingTarget != null && !homingTarget.IsDead && Time.time < homingUntil)
            {
                Quaternion want = Quaternion.LookRotation(homingTarget.AimPoint - transform.position);
                transform.rotation = Quaternion.RotateTowards(transform.rotation, want, 140f * Time.deltaTime);
            }

            float step = speed * Time.deltaTime;

            // environment
            if (Physics.Raycast(transform.position, transform.forward, out var hit, step + 0.1f,
                    Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore))
            {
                Explode(hit.point);
                return;
            }

            transform.position += transform.forward * step;

            // combatants (they live on IgnoreRaycast, so check by proximity)
            var mm = MatchManager.I;
            if (mm == null || owner == null || owner.Identity == null) return;
            foreach (var victim in mm.AliveEnemiesOf(owner.Identity.team))
            {
                Vector3 near = victim.transform.position + Vector3.up * 1.05f;
                if ((near - transform.position).sqrMagnitude > 0.72f * 0.72f) continue;
                if (victim.IsInvulnerable) continue; // rolled through it

                var info = new HitInfo
                {
                    attacker = owner,
                    damage = damage,
                    poiseDamage = poiseDamage,
                    direction = transform.forward,
                    point = transform.position,
                    element = element,
                };
                var res = victim.Health.TakeHit(info);
                if (res.landed)
                {
                    GameEffects.I?.ShowDamage(victim.AimPoint, res.damageDealt, res.blocked);
                    // a bolt that connects should freeze and kick like a melee hit —
                    // landing one gave the caster nothing back before
                    bool playerInvolved = victim.Identity.isPlayer ||
                                          (owner.Identity != null && owner.Identity.isPlayer);
                    if (playerInvolved)
                    {
                        GameEffects.I?.Hitstop(res.killed ? Tuning.HitstopHeavy : Tuning.HitstopLight);
                        OrbitCamera.I?.Shake(res.killed ? 0.42f : 0.24f);
                    }
                }
                Explode(transform.position);
                return;
            }
        }

        void Explode(Vector3 at)
        {
            GameEffects.I?.Explosion(element, at);
            Destroy(gameObject);
        }
    }
}
