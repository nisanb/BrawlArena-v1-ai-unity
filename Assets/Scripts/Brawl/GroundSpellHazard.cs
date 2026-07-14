using UnityEngine;

namespace BrawlArena
{
    /// <summary>
    /// Short authoritative area left by a Fire projectile. It queries the
    /// MatchManager roster instead of relying on trigger callbacks, matching
    /// projectile and melee team/authority rules and remaining deterministic
    /// when visual prefabs contain their own colliders.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class GroundSpellHazard : MonoBehaviour
    {
        const float PulseInterval = 0.45f;
        const int CircleSegments = 32;

        BrawlerController owner;
        MatchManager matchManager;
        float radius;
        float totalBurnDamage;
        float burnDuration;
        float burnTickInterval;
        float expiresAt;
        float nextPulseAt;
        LineRenderer ring;
        Material ringMaterial;
        bool shuttingDown;

        public BrawlerController Owner => owner;
        public float Radius => radius;
        public float RemainingLifetime => Mathf.Max(0f, expiresAt - Time.time);

        public static GroundSpellHazard SpawnFire(BrawlerController owner, Vector3 impactPoint,
            float impactDamage, SpellSpecialty specialty)
        {
            if (owner == null || impactDamage <= 0f ||
                specialty.school != SpellSchool.Fire ||
                (MatchManager.Instance != null &&
                 MatchManager.Instance.State == MatchState.Ended))
                return null;

            SpellSpecialty payload = specialty.Sanitized();
            // Fallbacks keep old serialized Fire definitions functional until
            // their newly added fields are refreshed by GameFlow.
            float effectRadius = payload.groundEffectRadius > 0f
                ? payload.groundEffectRadius
                : 1.8f;
            float effectDuration = payload.groundEffectDuration > 0f
                ? payload.groundEffectDuration
                : 3.2f;
            float groundFraction = payload.groundBurnFraction > 0f
                ? payload.groundBurnFraction
                : 0.18f;
            float statusDuration = payload.burnDuration > 0f
                ? Mathf.Min(payload.burnDuration, 2.4f)
                : 1.6f;
            float tickInterval = payload.burnTickInterval > 0f
                ? payload.burnTickInterval
                : 0.6f;

            var hazardObject = new GameObject("Fire Ground Hazard");
            hazardObject.transform.position = ProjectToGround(impactPoint);
            GroundSpellHazard hazard = hazardObject.AddComponent<GroundSpellHazard>();
            hazard.Initialize(owner, Mathf.Clamp(effectRadius, 0.5f, 5f),
                impactDamage * Mathf.Clamp01(groundFraction),
                Mathf.Clamp(effectDuration, 0.5f, 8f), statusDuration,
                Mathf.Clamp(tickInterval, 0.2f, 2f));
            return hazard;
        }

        void Initialize(BrawlerController source, float effectRadius, float burnDamage,
            float lifetime, float statusDuration, float statusTickInterval)
        {
            owner = source;
            radius = effectRadius;
            totalBurnDamage = Mathf.Max(0.1f, burnDamage);
            burnDuration = statusDuration;
            burnTickInterval = statusTickInterval;
            expiresAt = Time.time + lifetime;
            nextPulseAt = Time.time;
            matchManager = MatchManager.Instance;
            if (matchManager != null) matchManager.MatchEnded += OnMatchEnded;

            CombatPhysics.SetLayerRecursively(gameObject, CombatPhysics.VfxLayer);
            CreateRing();
            Pulse();
        }

        void Update()
        {
            if (shuttingDown) return;
            if (owner == null || Time.time >= expiresAt ||
                (MatchManager.Instance != null &&
                 MatchManager.Instance.State == MatchState.Ended))
            {
                DestroySelf();
                return;
            }

            if (Time.time >= nextPulseAt) Pulse();
            if (ring != null)
            {
                float fade = Mathf.Clamp01(RemainingLifetime / 0.55f);
                Color color = new Color(1f, 0.24f, 0.035f, 0.72f * fade);
                ring.startColor = color;
                ring.endColor = color;
            }
        }

        void Pulse()
        {
            nextPulseAt = Time.time + PulseInterval;
            MatchManager manager = MatchManager.Instance;
            if (manager == null || owner == null || totalBurnDamage <= 0f) return;

            Vector3 lineOfSightOrigin = transform.position + Vector3.up * 0.12f;
            var brawlers = manager.GetBrawlers();
            for (int i = 0; i < brawlers.Count; i++)
            {
                BrawlerController target = brawlers[i];
                if (target == null || target == owner || target.IsDead ||
                    target.team == owner.team)
                    continue;

                Vector3 delta = target.transform.position - transform.position;
                delta.y = 0f;
                float reach = radius + target.CombatHitRadius;
                if (delta.sqrMagnitude > reach * reach ||
                    !CombatPhysics.HasLineOfSight(lineOfSightOrigin, target.CombatAimPoint))
                    continue;
                target.ApplySpellBurn(owner, totalBurnDamage, burnDuration, burnTickInterval);
            }
        }

        void CreateRing()
        {
            Shader shader = Shader.Find("Sprites/Default");
            if (shader == null) shader = Shader.Find("Universal Render Pipeline/Unlit");
            if (shader == null) return;

            ringMaterial = new Material(shader) { name = "FireGroundHazard_Runtime" };
            ring = gameObject.AddComponent<LineRenderer>();
            ring.sharedMaterial = ringMaterial;
            ring.useWorldSpace = false;
            ring.loop = true;
            ring.positionCount = CircleSegments;
            ring.widthMultiplier = 0.09f;
            ring.numCornerVertices = 2;
            ring.numCapVertices = 2;
            ring.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            ring.receiveShadows = false;
            Color color = new Color(1f, 0.24f, 0.035f, 0.72f);
            ring.startColor = color;
            ring.endColor = color;
            for (int i = 0; i < CircleSegments; i++)
            {
                float angle = i * Mathf.PI * 2f / CircleSegments;
                ring.SetPosition(i, new Vector3(Mathf.Cos(angle) * radius, 0.06f,
                    Mathf.Sin(angle) * radius));
            }
        }

        static Vector3 ProjectToGround(Vector3 point)
        {
            Vector3 origin = point + Vector3.up * 4f;
            int groundLayer = CombatPhysics.GroundLayer;
            int mask = groundLayer >= 0 ? 1 << groundLayer : Physics.DefaultRaycastLayers;
            if (Physics.Raycast(origin, Vector3.down, out RaycastHit hit, 12f, mask,
                    QueryTriggerInteraction.Ignore))
                return hit.point + Vector3.up * 0.02f;

            // Combat arenas are planar; retaining X/Z is safer than allowing a
            // target's center-height impact to leave a floating damage volume.
            point.y = 0.02f;
            return point;
        }

        void OnMatchEnded(TeamId? winner)
        {
            DestroySelf();
        }

        void DestroySelf()
        {
            if (shuttingDown) return;
            shuttingDown = true;
            if (Application.isPlaying) Destroy(gameObject);
            else DestroyImmediate(gameObject);
        }

        void OnDestroy()
        {
            if (matchManager != null) matchManager.MatchEnded -= OnMatchEnded;
            matchManager = null;
            if (ringMaterial == null) return;
            if (Application.isPlaying) Destroy(ringMaterial);
            else DestroyImmediate(ringMaterial);
        }
    }
}
