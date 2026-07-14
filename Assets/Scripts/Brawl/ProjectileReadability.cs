using System;
using UnityEngine;
using UnityEngine.Rendering;

namespace BrawlArena
{
    /// <summary>The combat risk communicated independently from source team.</summary>
    public enum ProjectileThreatType
    {
        Burn = 0,
        Control = 1,
        Chain = 2,
        Precision = 3,
    }

    public enum ProjectileAttackTier
    {
        Basic = 0,
        Super = 1,
    }

    /// <summary>In-flight encoding of the projectile's authoritative wall rule.</summary>
    public enum ProjectileWorldInteraction
    {
        StopsOnWorld = 0,
        PassesWorld = 1,
    }

    /// <summary>Presentation-only result of the authoritative projectile sweep.</summary>
    public enum ProjectileImpactOutcome
    {
        DirectHit = 0,
        WorldBlocked = 1,
        RangeExpired = 2,
    }

    /// <summary>
    /// Small Brawl-owned presentation payload. Team color remains fixed by the
    /// launch owner; this accent and glyph communicate roster-specific danger.
    /// </summary>
    [Serializable]
    public struct ProjectileReadabilityProfile
    {
        public bool configured;
        public ProjectileThreatType threat;
        public Color accent;
        [Range(0.7f, 1.6f)] public float cueScale;
        [Range(0.025f, 0.18f)] public float trailWidth;
        [Range(0.08f, 0.8f)] public float trailDuration;
        [Range(1f, 8f)] public float pulseSpeed;

        public static ProjectileReadabilityProfile ForRoster(string rosterId,
            SpellSchool school)
        {
            string id = (rosterId ?? string.Empty).ToLowerInvariant();
            if (id == "fire" || school == SpellSchool.Fire)
            {
                return Create(ProjectileThreatType.Burn,
                    new Color(1f, 0.48f, 0.08f, 1f), 1.08f, 0.09f, 0.42f, 4.4f);
            }
            if (id == "frost" || school == SpellSchool.Frost)
            {
                return Create(ProjectileThreatType.Control,
                    new Color(0.38f, 0.95f, 1f, 1f), 1.12f, 0.1f, 0.52f, 2.8f);
            }
            if (id == "storm" || school == SpellSchool.Storm)
            {
                return Create(ProjectileThreatType.Chain,
                    new Color(0.86f, 0.68f, 1f, 1f), 1.04f, 0.075f, 0.34f, 6.2f);
            }

            // Thorn and any future physical projectile use the precision cue.
            return Create(ProjectileThreatType.Precision,
                new Color(0.72f, 1f, 0.3f, 1f), 0.94f, 0.055f, 0.28f, 3.6f);
        }

        public ProjectileReadabilityProfile Sanitized(string rosterId,
            SpellSchool school)
        {
            ProjectileReadabilityProfile value = configured
                ? this
                : ForRoster(rosterId, school);
            value.configured = true;
            value.accent.r = Mathf.Clamp01(value.accent.r);
            value.accent.g = Mathf.Clamp01(value.accent.g);
            value.accent.b = Mathf.Clamp01(value.accent.b);
            value.accent.a = Mathf.Clamp(value.accent.a, 0.45f, 1f);
            value.cueScale = Mathf.Clamp(value.cueScale, 0.7f, 1.6f);
            value.trailWidth = Mathf.Clamp(value.trailWidth, 0.025f, 0.18f);
            value.trailDuration = Mathf.Clamp(value.trailDuration, 0.08f, 0.8f);
            value.pulseSpeed = Mathf.Clamp(value.pulseSpeed, 1f, 8f);
            return value;
        }

        static ProjectileReadabilityProfile Create(ProjectileThreatType threat,
            Color accent, float cueScale, float trailWidth, float trailDuration,
            float pulseSpeed)
        {
            return new ProjectileReadabilityProfile
            {
                configured = true,
                threat = threat,
                accent = accent,
                cueScale = cueScale,
                trailWidth = trailWidth,
                trailDuration = trailDuration,
                pulseSpeed = pulseSpeed,
            };
        }
    }

    /// <summary>
    /// Generated child renderers added to a projectile clone once and then
    /// overwritten for every pool lease. No authored/vendor renderer or material
    /// is touched.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class ProjectileReadabilityLease : MonoBehaviour
    {
        const int CircleSegments = 32;

        LineRenderer teamHalo;
        LineRenderer threatGlyph;
        LineRenderer superHalo;
        LineRenderer splashRing;
        LineRenderer worldRuleGlyph;
        TrailRenderer trajectory;
        Transform threatTransform;
        Transform splashTransform;
        MaterialPropertyBlock teamBlock;
        MaterialPropertyBlock threatBlock;
        MaterialPropertyBlock superBlock;
        MaterialPropertyBlock splashBlock;
        MaterialPropertyBlock trailBlock;
        MaterialPropertyBlock worldRuleBlock;
        ProjectileReadabilityProfile profile;
        float configuredSplashRadius;
        bool configuredLease;

        public bool IsConfigured => configuredLease;
        public TeamId SourceTeam { get; private set; }
        public ProjectileThreatType Threat => profile.threat;
        public ProjectileAttackTier AttackTier { get; private set; }
        public float SplashRadius => configuredSplashRadius;
        public bool TrajectoryEmitting => trajectory != null && trajectory.emitting;
        public bool TeamHaloVisible => teamHalo != null && teamHalo.enabled;
        public bool SuperCueVisible => superHalo != null && superHalo.enabled;
        public bool SplashCueVisible => splashRing != null && splashRing.enabled;
        public bool WorldRuleCueVisible => worldRuleGlyph != null && worldRuleGlyph.enabled;
        public ProjectileWorldInteraction WorldInteraction { get; private set; }
        public Color TeamCueColor { get; private set; }
        public Color ThreatCueColor => profile.accent;

        public static ProjectileReadabilityLease GetOrCreate(GameObject projectileObject)
        {
            if (projectileObject == null) return null;
            ProjectileReadabilityLease lease =
                projectileObject.GetComponent<ProjectileReadabilityLease>();
            if (lease == null) lease = projectileObject.AddComponent<ProjectileReadabilityLease>();
            lease.EnsureRig();
            return lease;
        }

        public void Configure(TeamId sourceTeam, ProjectileReadabilityProfile readability,
            ProjectileAttackTier tier, float authoritativeBlastRadius)
        {
            Configure(sourceTeam, readability, tier, authoritativeBlastRadius,
                ProjectileWorldInteraction.StopsOnWorld);
        }

        public void Configure(TeamId sourceTeam, ProjectileReadabilityProfile readability,
            ProjectileAttackTier tier, float authoritativeBlastRadius,
            ProjectileWorldInteraction worldInteraction)
        {
            EnsureRig();
            ResetLease();

            SourceTeam = sourceTeam;
            profile = readability.Sanitized(string.Empty, SpellSchool.None);
            AttackTier = tier;
            WorldInteraction = worldInteraction;
            configuredSplashRadius = Mathf.Max(0f, authoritativeBlastRadius);
            TeamCueColor = TeamUtil.Color(sourceTeam);
            configuredLease = true;

            teamHalo.enabled = true;
            threatGlyph.enabled = true;
            superHalo.enabled = tier == ProjectileAttackTier.Super;
            splashRing.enabled = configuredSplashRadius > 0f;
            worldRuleGlyph.enabled = true;
            trajectory.enabled = true;
            trajectory.emitting = true;
            trajectory.time = profile.trailDuration;
            trajectory.startWidth = profile.trailWidth *
                (tier == ProjectileAttackTier.Super ? 1.45f : 1f);
            trajectory.endWidth = 0f;
            trajectory.Clear();

            float scale = profile.cueScale *
                (tier == ProjectileAttackTier.Super ? 1.22f : 1f);
            SetCircle(teamHalo, 0.31f * scale, Vector3.forward);
            SetThreatGlyph(profile.threat, 0.27f * scale);
            SetCircle(superHalo, 0.45f * scale, Vector3.forward);
            SetCircle(splashRing, 1f, Vector3.up);
            SetWorldRuleGlyph(worldInteraction, 0.24f * scale);
            RefreshSplashTransform();

            ApplyColor(teamHalo, ref teamBlock, TeamCueColor);
            ApplyColor(trajectory, ref trailBlock, TeamCueColor);
            ApplyColor(threatGlyph, ref threatBlock, profile.accent);
            ApplyColor(superHalo, ref superBlock,
                new Color(1f, 0.92f, 0.36f, 0.95f));
            ApplyColor(splashRing, ref splashBlock,
                new Color(profile.accent.r, profile.accent.g, profile.accent.b, 0.78f));
            ApplyColor(worldRuleGlyph, ref worldRuleBlock,
                new Color(1f, 1f, 1f, 0.92f));
        }

        public void ResetLease()
        {
            configuredLease = false;
            SourceTeam = TeamId.Blue;
            AttackTier = ProjectileAttackTier.Basic;
            WorldInteraction = ProjectileWorldInteraction.StopsOnWorld;
            profile = default;
            configuredSplashRadius = 0f;
            TeamCueColor = Color.clear;

            if (teamHalo != null) teamHalo.enabled = false;
            if (threatGlyph != null) threatGlyph.enabled = false;
            if (superHalo != null) superHalo.enabled = false;
            if (splashRing != null) splashRing.enabled = false;
            if (worldRuleGlyph != null) worldRuleGlyph.enabled = false;
            if (trajectory != null)
            {
                trajectory.emitting = false;
                trajectory.Clear();
                trajectory.enabled = false;
            }
            if (threatTransform != null) threatTransform.localScale = Vector3.one;
            ClearBlock(teamHalo, ref teamBlock);
            ClearBlock(threatGlyph, ref threatBlock);
            ClearBlock(superHalo, ref superBlock);
            ClearBlock(splashRing, ref splashBlock);
            ClearBlock(trajectory, ref trailBlock);
            ClearBlock(worldRuleGlyph, ref worldRuleBlock);
        }

        void Update()
        {
            if (!configuredLease) return;
            float pulse = 1f + Mathf.Sin(Time.time * profile.pulseSpeed) * 0.1f;
            if (threatTransform != null)
                threatTransform.localScale = Vector3.one * pulse;
            RefreshSplashTransform();
        }

        void RefreshSplashTransform()
        {
            if (splashTransform == null || configuredSplashRadius <= 0f) return;
            splashTransform.SetPositionAndRotation(transform.position, Quaternion.identity);
            Vector3 parentScale = transform.lossyScale;
            splashTransform.localScale = new Vector3(
                configuredSplashRadius / Mathf.Max(0.0001f, Mathf.Abs(parentScale.x)),
                1f / Mathf.Max(0.0001f, Mathf.Abs(parentScale.y)),
                configuredSplashRadius / Mathf.Max(0.0001f, Mathf.Abs(parentScale.z)));
        }

        void EnsureRig()
        {
            if (teamHalo != null && threatGlyph != null && superHalo != null &&
                splashRing != null && worldRuleGlyph != null && trajectory != null)
                return;
            if (teamHalo == null)
                teamHalo = FindLine("Brawl Team Source Halo") ??
                    CreateLine("Brawl Team Source Halo", transform, true, CircleSegments);
            if (threatGlyph == null)
                threatGlyph = FindLine("Brawl Threat Glyph") ??
                    CreateLine("Brawl Threat Glyph", transform, false, 5);
            threatTransform = threatGlyph.transform;
            if (superHalo == null)
                superHalo = FindLine("Brawl Super Tier Halo") ??
                    CreateLine("Brawl Super Tier Halo", transform, true, CircleSegments);
            if (splashRing == null)
                splashRing = FindLine("Brawl Splash Radius") ??
                    CreateLine("Brawl Splash Radius", transform, true, CircleSegments);
            splashTransform = splashRing.transform;
            if (worldRuleGlyph == null)
                worldRuleGlyph = FindLine("Brawl World Rule") ??
                    CreateLine("Brawl World Rule", transform, false, 5);

            if (trajectory == null)
            {
                Transform existingTrail = transform.Find("Brawl Trajectory Trail");
                trajectory = existingTrail != null
                    ? existingTrail.GetComponent<TrailRenderer>()
                    : null;
                if (trajectory == null)
                {
                    GameObject trailObject = new GameObject("Brawl Trajectory Trail");
                    trailObject.transform.SetParent(transform, false);
                    trailObject.layer = gameObject.layer;
                    trajectory = trailObject.AddComponent<TrailRenderer>();
                }
                ConfigureRenderer(trajectory);
                trajectory.sharedMaterial = ProjectileReadabilityRuntime.SharedCueMaterial;
                trajectory.alignment = LineAlignment.View;
                trajectory.minVertexDistance = 0.08f;
                trajectory.textureMode = LineTextureMode.Stretch;
                trajectory.numCornerVertices = 2;
                trajectory.numCapVertices = 2;
            }
            ResetLease();
        }

        LineRenderer FindLine(string name)
        {
            Transform child = transform.Find(name);
            return child != null ? child.GetComponent<LineRenderer>() : null;
        }

        static LineRenderer CreateLine(string name, Transform parent, bool loop,
            int positionCount)
        {
            GameObject lineObject = new GameObject(name);
            lineObject.transform.SetParent(parent, false);
            lineObject.layer = parent.gameObject.layer;
            LineRenderer line = lineObject.AddComponent<LineRenderer>();
            ConfigureRenderer(line);
            line.sharedMaterial = ProjectileReadabilityRuntime.SharedCueMaterial;
            line.useWorldSpace = false;
            line.loop = loop;
            line.positionCount = positionCount;
            line.widthMultiplier = name.Contains("Splash") ? 0.075f : 0.055f;
            line.numCornerVertices = 2;
            line.numCapVertices = 2;
            return line;
        }

        static void ConfigureRenderer(Renderer renderer)
        {
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            renderer.lightProbeUsage = LightProbeUsage.Off;
            renderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
            renderer.motionVectorGenerationMode = MotionVectorGenerationMode.ForceNoMotion;
        }

        static void SetCircle(LineRenderer line, float radius, Vector3 normal)
        {
            bool horizontal = normal == Vector3.up;
            for (int i = 0; i < CircleSegments; i++)
            {
                float angle = i * Mathf.PI * 2f / CircleSegments;
                float x = Mathf.Cos(angle) * radius;
                float y = Mathf.Sin(angle) * radius;
                line.SetPosition(i, horizontal
                    ? new Vector3(x, 0.04f, y)
                    : new Vector3(x, y, 0f));
            }
        }

        void SetThreatGlyph(ProjectileThreatType threat, float size)
        {
            threatGlyph.positionCount = 5;
            switch (threat)
            {
                case ProjectileThreatType.Burn:
                    threatGlyph.SetPosition(0, new Vector3(0f, size, -0.02f));
                    threatGlyph.SetPosition(1, new Vector3(size * 0.65f, -size, -0.02f));
                    threatGlyph.SetPosition(2, new Vector3(0f, -size * 0.45f, -0.02f));
                    threatGlyph.SetPosition(3, new Vector3(-size * 0.65f, -size, -0.02f));
                    threatGlyph.SetPosition(4, new Vector3(0f, size, -0.02f));
                    break;
                case ProjectileThreatType.Control:
                    threatGlyph.SetPosition(0, new Vector3(0f, size, -0.02f));
                    threatGlyph.SetPosition(1, new Vector3(size, 0f, -0.02f));
                    threatGlyph.SetPosition(2, new Vector3(0f, -size, -0.02f));
                    threatGlyph.SetPosition(3, new Vector3(-size, 0f, -0.02f));
                    threatGlyph.SetPosition(4, new Vector3(0f, size, -0.02f));
                    break;
                case ProjectileThreatType.Chain:
                    threatGlyph.SetPosition(0, new Vector3(-size, size * 0.8f, -0.02f));
                    threatGlyph.SetPosition(1, new Vector3(-size * 0.15f, size * 0.15f, -0.02f));
                    threatGlyph.SetPosition(2, new Vector3(-size * 0.45f, -size * 0.1f, -0.02f));
                    threatGlyph.SetPosition(3, new Vector3(size, -size * 0.8f, -0.02f));
                    threatGlyph.SetPosition(4, new Vector3(size * 0.2f, 0f, -0.02f));
                    break;
                default:
                    threatGlyph.SetPosition(0, new Vector3(-size, 0f, -0.02f));
                    threatGlyph.SetPosition(1, new Vector3(size, 0f, -0.02f));
                    threatGlyph.SetPosition(2, Vector3.zero);
                    threatGlyph.SetPosition(3, new Vector3(0f, -size, -0.02f));
                    threatGlyph.SetPosition(4, new Vector3(0f, size, -0.02f));
                    break;
            }
        }

        void SetWorldRuleGlyph(ProjectileWorldInteraction interaction, float size)
        {
            worldRuleGlyph.positionCount = 5;
            float z = -0.05f;
            if (interaction == ProjectileWorldInteraction.PassesWorld)
            {
                worldRuleGlyph.SetPosition(0, new Vector3(-size, -size * 0.6f, z));
                worldRuleGlyph.SetPosition(1, new Vector3(0f, 0f, z));
                worldRuleGlyph.SetPosition(2, new Vector3(-size, size * 0.6f, z));
                worldRuleGlyph.SetPosition(3, new Vector3(0f, 0f, z));
                worldRuleGlyph.SetPosition(4, new Vector3(size, 0f, z));
                return;
            }

            // A crossbar with capped ends reads as "stops at walls" before impact.
            worldRuleGlyph.SetPosition(0, new Vector3(-size, -size * 0.55f, z));
            worldRuleGlyph.SetPosition(1, new Vector3(-size, 0f, z));
            worldRuleGlyph.SetPosition(2, new Vector3(size, 0f, z));
            worldRuleGlyph.SetPosition(3, new Vector3(size, size * 0.55f, z));
            worldRuleGlyph.SetPosition(4, new Vector3(size, -size * 0.55f, z));
        }

        internal static void ApplyColor(Renderer renderer, ref MaterialPropertyBlock block,
            Color color)
        {
            if (renderer == null) return;
            if (block == null) block = new MaterialPropertyBlock();
            block.Clear();
            block.SetColor("_BaseColor", color);
            block.SetColor("_Color", color);
            renderer.SetPropertyBlock(block);
            if (renderer is LineRenderer line)
            {
                line.startColor = color;
                line.endColor = color;
            }
            else if (renderer is TrailRenderer trail)
            {
                trail.startColor = color;
                trail.endColor = new Color(color.r, color.g, color.b, 0f);
            }
        }

        static void ClearBlock(Renderer renderer, ref MaterialPropertyBlock block)
        {
            if (renderer == null) return;
            if (block == null) block = new MaterialPropertyBlock();
            block.Clear();
            renderer.SetPropertyBlock(null);
        }
    }

    /// <summary>Short pooled result cue with no collider or gameplay path.</summary>
    [DisallowMultipleComponent]
    public sealed class ProjectileImpactReadability : MonoBehaviour
    {
        const int CircleSegments = 32;
        LineRenderer footprint;
        LineRenderer outcomeMark;
        MaterialPropertyBlock footprintBlock;
        MaterialPropertyBlock outcomeBlock;

        public bool IsConfigured { get; private set; }
        public ProjectileImpactOutcome Outcome { get; private set; }
        public TeamId SourceTeam { get; private set; }
        public ProjectileThreatType Threat { get; private set; }
        public ProjectileAttackTier AttackTier { get; private set; }
        public float SplashRadius { get; private set; }
        public Color TeamCueColor { get; private set; }
        public Color ThreatCueColor { get; private set; }

        internal void EnsureRig()
        {
            if (footprint != null && outcomeMark != null) return;
            if (footprint == null)
            {
                Transform child = transform.Find("Brawl Impact Footprint");
                footprint = child != null ? child.GetComponent<LineRenderer>() : null;
                if (footprint == null)
                    footprint = CreateLine("Brawl Impact Footprint", true, CircleSegments);
            }
            if (outcomeMark == null)
            {
                Transform child = transform.Find("Brawl Impact Outcome");
                outcomeMark = child != null ? child.GetComponent<LineRenderer>() : null;
                if (outcomeMark == null)
                    outcomeMark = CreateLine("Brawl Impact Outcome", false, 5);
            }
            ResetLease();
        }

        public void Configure(ProjectileImpactOutcome outcome, TeamId sourceTeam,
            ProjectileReadabilityProfile readability, ProjectileAttackTier tier,
            float authoritativeBlastRadius)
        {
            EnsureRig();
            ResetLease();
            ProjectileReadabilityProfile profile = readability.Sanitized(
                string.Empty, SpellSchool.None);
            IsConfigured = true;
            Outcome = outcome;
            SourceTeam = sourceTeam;
            Threat = profile.threat;
            AttackTier = tier;
            SplashRadius = Mathf.Max(0f, authoritativeBlastRadius);
            TeamCueColor = TeamUtil.Color(sourceTeam);
            ThreatCueColor = profile.accent;

            float radius = SplashRadius > 0f ? SplashRadius :
                (tier == ProjectileAttackTier.Super ? 0.85f : 0.58f);
            SetCircle(footprint, radius);
            footprint.enabled = true;
            outcomeMark.enabled = true;
            outcomeMark.transform.localPosition = Vector3.up * 0.08f;
            SetOutcomeMark(outcome, Mathf.Clamp(radius * 0.52f, 0.3f, 1.25f));
            ProjectileReadabilityLease.ApplyColor(footprint, ref footprintBlock,
                new Color(TeamCueColor.r, TeamCueColor.g, TeamCueColor.b, 0.82f));
            ProjectileReadabilityLease.ApplyColor(outcomeMark, ref outcomeBlock,
                new Color(profile.accent.r, profile.accent.g, profile.accent.b, 0.96f));
        }

        public void ResetLease()
        {
            IsConfigured = false;
            Outcome = ProjectileImpactOutcome.DirectHit;
            SourceTeam = TeamId.Blue;
            Threat = ProjectileThreatType.Burn;
            AttackTier = ProjectileAttackTier.Basic;
            SplashRadius = 0f;
            TeamCueColor = Color.clear;
            ThreatCueColor = Color.clear;
            if (footprint != null) footprint.enabled = false;
            if (outcomeMark != null) outcomeMark.enabled = false;
            ClearBlock(footprint, ref footprintBlock);
            ClearBlock(outcomeMark, ref outcomeBlock);
        }

        LineRenderer CreateLine(string name, bool loop, int positions)
        {
            GameObject lineObject = new GameObject(name);
            lineObject.layer = gameObject.layer;
            lineObject.transform.SetParent(transform, false);
            LineRenderer line = lineObject.AddComponent<LineRenderer>();
            line.sharedMaterial = ProjectileReadabilityRuntime.SharedCueMaterial;
            line.useWorldSpace = false;
            line.loop = loop;
            line.positionCount = positions;
            line.widthMultiplier = loop ? 0.075f : 0.11f;
            line.numCornerVertices = 2;
            line.numCapVertices = 2;
            line.shadowCastingMode = ShadowCastingMode.Off;
            line.receiveShadows = false;
            line.lightProbeUsage = LightProbeUsage.Off;
            line.reflectionProbeUsage = ReflectionProbeUsage.Off;
            return line;
        }

        static void SetCircle(LineRenderer line, float radius)
        {
            for (int i = 0; i < CircleSegments; i++)
            {
                float angle = i * Mathf.PI * 2f / CircleSegments;
                line.SetPosition(i, new Vector3(Mathf.Cos(angle) * radius, 0.04f,
                    Mathf.Sin(angle) * radius));
            }
        }

        void SetOutcomeMark(ProjectileImpactOutcome outcome, float size)
        {
            if (outcome == ProjectileImpactOutcome.WorldBlocked)
            {
                outcomeMark.SetPosition(0, new Vector3(-size, 0f, size));
                outcomeMark.SetPosition(1, new Vector3(size, 0f, -size));
                outcomeMark.SetPosition(2, Vector3.zero);
                outcomeMark.SetPosition(3, new Vector3(-size, 0f, -size));
                outcomeMark.SetPosition(4, new Vector3(size, 0f, size));
            }
            else if (outcome == ProjectileImpactOutcome.RangeExpired)
            {
                outcomeMark.SetPosition(0, new Vector3(0f, 0f, size));
                outcomeMark.SetPosition(1, new Vector3(size, 0f, 0f));
                outcomeMark.SetPosition(2, new Vector3(0f, 0f, -size));
                outcomeMark.SetPosition(3, new Vector3(-size, 0f, 0f));
                outcomeMark.SetPosition(4, new Vector3(0f, 0f, size));
            }
            else
            {
                outcomeMark.SetPosition(0, new Vector3(-size, 0f, 0f));
                outcomeMark.SetPosition(1, new Vector3(-size * 0.25f, 0f, -size * 0.7f));
                outcomeMark.SetPosition(2, new Vector3(size, 0f, size * 0.65f));
                outcomeMark.SetPosition(3, new Vector3(size * 0.35f, 0f, 0f));
                outcomeMark.SetPosition(4, new Vector3(-size * 0.25f, 0f, -size * 0.7f));
            }
        }

        static void ClearBlock(Renderer renderer, ref MaterialPropertyBlock block)
        {
            if (renderer == null) return;
            if (block == null) block = new MaterialPropertyBlock();
            block.Clear();
            renderer.SetPropertyBlock(null);
        }
    }

    /// <summary>Runtime material/template cache owned only by Brawl presentation.</summary>
    public static class ProjectileReadabilityRuntime
    {
        static Material sharedCueMaterial;
        static GameObject impactTemplate;

        internal static Material SharedCueMaterial
        {
            get
            {
                if (sharedCueMaterial != null) return sharedCueMaterial;
                Shader shader = Shader.Find("Sprites/Default");
                if (shader == null) shader = Shader.Find("Universal Render Pipeline/Unlit");
                if (shader == null) return null;
                sharedCueMaterial = new Material(shader)
                {
                    name = "Brawl Projectile Readability Runtime",
                    hideFlags = HideFlags.HideAndDontSave,
                };
                return sharedCueMaterial;
            }
        }

        public static ProjectileImpactReadability SpawnImpactCue(Vector3 position,
            ProjectileImpactOutcome outcome, TeamId sourceTeam,
            ProjectileReadabilityProfile profile, ProjectileAttackTier tier,
            float authoritativeBlastRadius)
        {
            GameObject template = GetImpactTemplate();
            if (template == null) return null;
            GameObject instance = CombatObjectPool.SpawnVfx(template, position,
                Quaternion.identity, tier == ProjectileAttackTier.Super ? 0.65f : 0.45f);
            if (instance == null) return null;
            ProjectileImpactReadability cue =
                instance.GetComponent<ProjectileImpactReadability>();
            if (cue == null) cue = instance.AddComponent<ProjectileImpactReadability>();
            cue.Configure(outcome, sourceTeam, profile, tier, authoritativeBlastRadius);
            return cue;
        }

        static GameObject GetImpactTemplate()
        {
            if (impactTemplate != null) return impactTemplate;
            impactTemplate = new GameObject("[Brawl Projectile Impact Cue Template]");
            impactTemplate.hideFlags = HideFlags.HideAndDontSave;
            ProjectileImpactReadability cue =
                impactTemplate.AddComponent<ProjectileImpactReadability>();
            cue.EnsureRig();
            impactTemplate.SetActive(false);
            return impactTemplate;
        }
    }
}
