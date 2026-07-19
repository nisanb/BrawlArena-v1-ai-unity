using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

namespace BrawlArena
{
    /// <summary>
    /// Camera rig personalities. TopDownBrawl is the classic 3/4 mobile-brawler
    /// framing; ActionThirdPerson drops low behind the hero so the horizon is
    /// visible and the world reads big (action-MMORPG experiment).
    /// </summary>
    public enum BrawlCameraStyle
    {
        TopDownBrawl,
        ActionThirdPerson,
    }

    /// <summary>
    /// Brawler chase camera: third-person 3/4 angle with a player-driven
    /// orbit. Default framing sits at a ~42.5 deg pitch and ~13.0 unit
    /// distance for wider peripheral coverage, so offscreen threats (long-range
    /// archer shots, HUD-adjacent nameplates) stay legible. Mobile play keeps
    /// yaw stable so drag-aim always maps to the same world frame; optional
    /// desktop orbit hooks remain available. The focus point leads slightly
    /// ahead of the target's planar velocity so runners get screen space in
    /// front of them instead of behind. Small perlin shake for hit feedback.
    /// Before a target exists (character select / loading), it slowly orbits
    /// the arena as a backdrop vista.
    /// </summary>
    public class BrawlCamera : MonoBehaviour
    {
        [Tooltip("Rig personality. ActionThirdPerson overrides the rig fields below in Awake/SetStyle.")]
        public BrawlCameraStyle style = BrawlCameraStyle.TopDownBrawl;
        [Tooltip("Vertical follow-focus offset used by ActionThirdPerson so the frame centers the chest and horizon instead of the feet.")]
        public float focusHeight = 1.5f;

        public Transform target;
        [Tooltip("Follow offset at yaw 0; pitch and distance are derived from it.")]
        // (0, 8.8, -9.6) => pitch ~42.5 deg, distance ~13.0 (ORDER R rig
        // pass): slightly lower pitch + longer distance than before so the
        // cliff ring/horizon parallax enters the top of frame, paired with
        // the wider 54 deg fov set in ArenaSceneBuilder.BuildCamera. This is
        // the single source of truth for the rig — the scene builder must
        // not override it.
        public Vector3 offset = new Vector3(0f, 8.8f, -9.6f);
        // 0.09 pairs with the snappier top-down motor: instant accel/stop
        // needs a tighter follow or the camera reads behind the character.
        public float smoothTime = 0.09f;
        public float vistaOrbitSpeed = 4f;

        [Header("Movement Lead")]
        [Tooltip("Max planar distance the focus point leads ahead of the target's velocity.")]
        public float movementLeadMax = 2.4f;
        [Tooltip("Lerp rate (per second) smoothing the lead offset toward its target value.")]
        public float movementLeadSmoothing = 4f;

        [Header("Orbit")]
        [Tooltip("Degrees per reference-resolution pixel of horizontal drag.")]
        public float dragYawSensitivity = 0.22f;
        public float dragPitchSensitivity = 0.1f;
        public float minPitch = 30f;
        public float maxPitch = 62f;
        [Tooltip("Max degrees/second the camera auto-swings behind the run direction.")]
        public float autoTurnSpeed = 30f;
        [Tooltip("Disabled on mobile so action-button drags keep a stable world direction.")]
        public bool movementAutoTurn;
        [Tooltip("Seconds after a manual drag before the slow auto-turn resumes.")]
        public float autoTurnGraceAfterDrag = 1.6f;

        [Header("Obstruction Avoidance")]
        [Tooltip("Pull the camera in when level geometry blocks the line to the player.")]
        public bool avoidObstructions = true;
        [Tooltip("Layers that can obstruct the camera. The followed player's hierarchy is always ignored.")]
        public LayerMask obstructionMask = 1 << 9; // WorldBlocker
        [Min(0.05f)] public float obstructionSphereRadius = 0.38f;
        [Min(0f)] public float obstructionPadding = 0.18f;
        [Tooltip("Never pull closer than this; very near visible blockers are hidden for the frame instead.")]
        [Min(0.1f)] public float minimumObstructionDistance = 3.2f;
        [Tooltip("Fast response used when geometry appears between the player and camera.")]
        [Min(0.01f)] public float obstructionPullInTime = 0.045f;
        [Tooltip("Slower recovery prevents the camera popping back after clearing a wall.")]
        [Min(0.01f)] public float obstructionRecoveryTime = 0.24f;

        static BrawlCamera instance;

        float yaw;
        float pitch;
        float distance;
        Vector3 followVelocity;
        Vector3 lastTargetPos;
        Vector3 lastLeadSamplePos;
        Vector3 leadOffset;
        float noAutoTurnUntil;
        float shakeAmplitude;
        float shakeUntil;
        float fovKickAmplitude;
        float fovKickUntil;
        float fovKickDuration;
        Vector3 punchOffset;
        Camera attachedCamera;
        float baseFieldOfView;
        float obstructionDistance;
        float obstructionVelocity;

        const float PunchDecayPerSecond = 9f;
        readonly Dictionary<Renderer, bool> hiddenOccluders = new Dictionary<Renderer, bool>();

        static readonly RaycastHit[] ObstructionHits = new RaycastHit[64];

        void Awake()
        {
            instance = this;
            attachedCamera = GetComponent<Camera>();
            if (attachedCamera != null) baseFieldOfView = attachedCamera.fieldOfView;
            // Only the action style forces its rig; TopDownBrawl scenes keep
            // whatever tuning they were built with.
            if (style == BrawlCameraStyle.ActionThirdPerson) ConfigureStyle();
            DeriveRigFromOffset();
        }

        /// <summary>Current style of the live camera; TopDownBrawl when none exists.</summary>
        public static BrawlCameraStyle ActiveStyle =>
            instance != null ? instance.style : BrawlCameraStyle.TopDownBrawl;

        /// <summary>Switches rig personality at runtime and re-snaps behind the target.</summary>
        public void SetStyle(BrawlCameraStyle newStyle)
        {
            style = newStyle;
            ConfigureStyle();
            if (target != null) SetTarget(target);
        }

        void ConfigureStyle()
        {
            if (attachedCamera == null) attachedCamera = GetComponent<Camera>();
            if (style == BrawlCameraStyle.ActionThirdPerson)
            {
                offset = new Vector3(0f, 2.2f, -8.5f); // pitch ~14.5 deg, distance ~8.8
                minPitch = 4f;
                maxPitch = 50f;
                movementAutoTurn = true;
                autoTurnSpeed = 90f;
                if (attachedCamera != null)
                {
                    attachedCamera.fieldOfView = 60f;
                    attachedCamera.farClipPlane = 500f;
                }
            }
            else
            {
                offset = new Vector3(0f, 8.8f, -9.6f);
                minPitch = 30f;
                maxPitch = 62f;
                movementAutoTurn = false;
                autoTurnSpeed = 30f;
                if (attachedCamera != null)
                {
                    attachedCamera.fieldOfView = 54f;
                    attachedCamera.farClipPlane = 200f;
                }
            }
            if (attachedCamera != null) baseFieldOfView = attachedCamera.fieldOfView;
            DeriveRigFromOffset();
        }

        void OnDestroy()
        {
            RestoreHiddenOccluders();
            if (instance == this) instance = null;
        }

        void OnDisable()
        {
            RestoreHiddenOccluders();
        }

        void DeriveRigFromOffset()
        {
            pitch = Mathf.Clamp(Mathf.Atan2(offset.y, -offset.z) * Mathf.Rad2Deg, minPitch, maxPitch);
            distance = new Vector2(offset.y, offset.z).magnitude;
            obstructionDistance = distance;
            obstructionVelocity = 0f;
        }

        void Start()
        {
            if (target == null)
            {
                var player = FindFirstObjectByType<PlayerBrawlerInput>();
                if (player != null) SetTarget(player.transform);
            }
            else
            {
                SetTarget(target);
            }
        }

        public void SetTarget(Transform t)
        {
            target = t;
            if (target == null) return;
            DeriveRigFromOffset();
            // Action style starts framed behind the hero; classic keeps the
            // stable world-north yaw drag-aim relies on.
            yaw = style == BrawlCameraStyle.ActionThirdPerson
                ? Mathf.Repeat(target.eulerAngles.y, 360f)
                : 0f;
            lastTargetPos = target.position;
            lastLeadSamplePos = target.position;
            leadOffset = Vector3.zero;
            ApplyRig(FocusPoint(target.position), true);
            followVelocity = Vector3.zero;
            obstructionVelocity = 0f;
        }

        /// <summary>Manual orbit from the HUD drag zone, in screen pixels.</summary>
        public void AddOrbit(float deltaX, float deltaY)
        {
            // Normalize to the 1920-wide reference so the feel matches on any
            // resolution/DPI.
            float scale = 1920f / Mathf.Max(1, Screen.width);
            yaw = Mathf.Repeat(yaw + deltaX * scale * dragYawSensitivity, 360f);
            pitch = Mathf.Clamp(pitch - deltaY * scale * dragPitchSensitivity, minPitch, maxPitch);
            noAutoTurnUntil = Time.time + autoTurnGraceAfterDrag;
        }

        Vector3 FocusPoint(Vector3 basePosition)
        {
            return style == BrawlCameraStyle.ActionThirdPerson
                ? basePosition + Vector3.up * focusHeight
                : basePosition;
        }

        Vector3 RigOffset(float rigDistance)
        {
            return Quaternion.Euler(pitch, yaw, 0f) * new Vector3(0f, 0f, -rigDistance);
        }

        void ApplyRig(Vector3 focus, bool snap)
        {
            float clearDistance = ResolveObstructionDistance(focus);
            if (snap)
            {
                obstructionDistance = clearDistance;
                obstructionVelocity = 0f;
            }
            else
            {
                float responseTime = clearDistance < obstructionDistance
                    ? obstructionPullInTime
                    : obstructionRecoveryTime;
                obstructionDistance = Mathf.SmoothDamp(
                    obstructionDistance,
                    clearDistance,
                    ref obstructionVelocity,
                    responseTime,
                    Mathf.Infinity,
                    Time.deltaTime);
            }

            Vector3 desired = focus + RigOffset(obstructionDistance);
            transform.position = snap
                ? desired
                : Vector3.SmoothDamp(transform.position, desired, ref followVelocity, smoothTime);
            transform.rotation = Quaternion.Euler(pitch, yaw, 0f);
        }

        float ResolveObstructionDistance(Vector3 focus)
        {
            RestoreHiddenOccluders();
            if (!avoidObstructions || target == null || distance <= 0.001f)
                return distance;

            Vector3 direction = RigOffset(distance).normalized;
            int hitCount = Physics.SphereCastNonAlloc(
                focus,
                obstructionSphereRadius,
                direction,
                ObstructionHits,
                distance,
                obstructionMask,
                QueryTriggerInteraction.Ignore);

            float nearest = distance;
            for (int i = 0; i < hitCount; i++)
            {
                Collider hitCollider = ObstructionHits[i].collider;
                if (hitCollider == null || IsFollowTargetCollider(hitCollider)) continue;

                float candidate = ObstructionHits[i].distance - obstructionPadding;
                if (candidate < minimumObstructionDistance && HideOccluder(hitCollider))
                    continue;
                if (candidate < nearest) nearest = candidate;
            }

            return Mathf.Clamp(nearest, Mathf.Min(minimumObstructionDistance, distance), distance);
        }

        bool IsFollowTargetCollider(Collider hitCollider)
        {
            Transform hitTransform = hitCollider.transform;
            return hitTransform == target || hitTransform.IsChildOf(target);
        }

        bool HideOccluder(Collider hitCollider)
        {
            Renderer renderer = hitCollider.GetComponent<Renderer>();
            if (renderer == null) renderer = hitCollider.GetComponentInParent<Renderer>();
            if (renderer == null) return false;
            if (hiddenOccluders.ContainsKey(renderer)) return true;
            if (!renderer.enabled) return false;

            hiddenOccluders.Add(renderer, true);
            renderer.enabled = false;
            return true;
        }

        void RestoreHiddenOccluders()
        {
            if (hiddenOccluders.Count == 0) return;
            foreach (var pair in hiddenOccluders)
                if (pair.Key != null) pair.Key.enabled = pair.Value;
            hiddenOccluders.Clear();
        }

        void LateUpdate()
        {
#if UNITY_EDITOR
            // Editor-only style flip for live iteration on any scene.
            var kb = Keyboard.current;
            if (kb != null && kb.f5Key.wasPressedThisFrame)
                SetStyle(style == BrawlCameraStyle.ActionThirdPerson
                    ? BrawlCameraStyle.TopDownBrawl
                    : BrawlCameraStyle.ActionThirdPerson);
#endif
            if (target == null)
            {
                RestoreHiddenOccluders();
                // Idle vista: slow orbit around the arena center.
                transform.RotateAround(new Vector3(0f, 1f, 0f), Vector3.up, vistaOrbitSpeed * Time.deltaTime);
                transform.LookAt(new Vector3(0f, 1.5f, 0f));
                return;
            }

            if (movementAutoTurn) UpdateAutoTurn();
            else lastTargetPos = target.position;
            UpdateMovementLead();
            ApplyRig(FocusPoint(target.position + leadOffset), false);

            if (!AccessibilitySettings.ReducedMotionEnabled && Time.time < shakeUntil)
            {
                float t = Time.time * 37f;
                transform.position += transform.rotation * new Vector3(
                    Mathf.PerlinNoise(t, 0.5f) - 0.5f,
                    Mathf.PerlinNoise(0.5f, t) - 0.5f,
                    0f) * (shakeAmplitude * 2f);
            }
            else
            {
                shakeAmplitude = 0f;
                shakeUntil = 0f;
            }

            ApplyImpactFeel();
        }

        /// <summary>
        /// Souls-impact camera feel: a decaying positional punch along the
        /// hit direction plus a short FOV kick for Supers/KOs. Both are
        /// presentation-only, gated by Reduced Motion, and self-restoring so
        /// no other system ever needs to clean them up.
        /// </summary>
        void ApplyImpactFeel()
        {
            if (punchOffset.sqrMagnitude > 0.000001f)
            {
                transform.position += punchOffset;
                punchOffset *= Mathf.Exp(-PunchDecayPerSecond * Time.deltaTime);
                if (punchOffset.sqrMagnitude < 0.000004f) punchOffset = Vector3.zero;
            }

            if (attachedCamera == null) return;
            float remaining = fovKickUntil - Time.time;
            if (remaining > 0f && fovKickDuration > 0.0001f &&
                !AccessibilitySettings.ReducedMotionEnabled)
            {
                float falloff = Mathf.Clamp01(remaining / fovKickDuration);
                attachedCamera.fieldOfView = baseFieldOfView +
                    fovKickAmplitude * falloff;
            }
            else if (fovKickAmplitude != 0f)
            {
                attachedCamera.fieldOfView = baseFieldOfView;
                fovKickAmplitude = 0f;
                fovKickUntil = 0f;
            }
        }

        /// <summary>
        /// Slow auto-orbit while the player strafes: ease the yaw toward the
        /// movement heading, weighted by how sideways the motion is so running
        /// straight ahead (or kiting backwards) never spins the camera.
        /// </summary>
        void UpdateAutoTurn()
        {
            Vector3 vel = (target.position - lastTargetPos) / Mathf.Max(Time.deltaTime, 0.0001f);
            lastTargetPos = target.position;
            if (Time.time < noAutoTurnUntil) return;
            vel.y = 0f;
            if (vel.sqrMagnitude < 4f) return;

            float headingYaw = Mathf.Atan2(vel.x, vel.z) * Mathf.Rad2Deg;
            float delta = Mathf.DeltaAngle(yaw, headingYaw);
            // 1 when moving fully sideways, 0 when parallel to the view axis.
            float lateral = Mathf.Abs(Mathf.Sin(delta * Mathf.Deg2Rad));
            if (lateral < 0.1f) return;
            float step = autoTurnSpeed * lateral * Time.deltaTime;
            yaw = Mathf.Repeat(yaw + Mathf.Clamp(delta, -step, step), 360f);
        }

        /// <summary>
        /// Shifts the follow focus point ahead of the target's planar
        /// velocity so a running player has framed space in front of them
        /// instead of behind. Decays to zero once the target stops; never
        /// applied to shake or the idle vista orbit.
        /// </summary>
        void UpdateMovementLead()
        {
            Vector3 vel = (target.position - lastLeadSamplePos) / Mathf.Max(Time.deltaTime, 0.0001f);
            lastLeadSamplePos = target.position;
            vel.y = 0f;

            Vector3 desiredLead = Vector3.ClampMagnitude(vel, movementLeadMax);
            leadOffset = Vector3.Lerp(leadOffset, desiredLead, Mathf.Clamp01(movementLeadSmoothing * Time.deltaTime));
        }

        public static void Shake(float amplitude, float duration)
        {
            if (instance == null || AccessibilitySettings.ReducedMotionEnabled) return;
            instance.shakeAmplitude = Mathf.Max(instance.shakeAmplitude, amplitude);
            instance.shakeUntil = Time.time + duration;
        }

        /// <summary>
        /// Short outward FOV kick (e.g. Super release, nearby KO). Positive
        /// degrees widen the frame then decay linearly over the duration.
        /// </summary>
        public static void Kick(float degrees, float duration)
        {
            if (instance == null || AccessibilitySettings.ReducedMotionEnabled) return;
            if (duration <= 0f) return;
            instance.fovKickAmplitude = Mathf.Max(instance.fovKickAmplitude, degrees);
            instance.fovKickDuration = duration;
            instance.fovKickUntil = Time.time + duration;
        }

        /// <summary>
        /// Directional positional punch along a world-space hit direction —
        /// the frame visibly takes the hit with the local player.
        /// </summary>
        public static void Punch(Vector3 worldDirection, float amplitude)
        {
            if (instance == null || AccessibilitySettings.ReducedMotionEnabled) return;
            worldDirection.y = 0f;
            if (worldDirection.sqrMagnitude <= 0.0001f || amplitude <= 0f) return;
            instance.punchOffset = Vector3.ClampMagnitude(
                instance.punchOffset + worldDirection.normalized * amplitude, 0.9f);
        }
    }
}
