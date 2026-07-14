using System.Collections.Generic;
using UnityEngine;

namespace BrawlArena
{
    /// <summary>
    /// Brawler chase camera: third-person 3/4 angle with a player-driven
    /// orbit. Mobile play keeps yaw stable so drag-aim always maps to the same
    /// world frame; optional desktop orbit hooks remain available. Small perlin shake for hit
    /// feedback. Before a target exists (character select / loading), it
    /// slowly orbits the arena as a backdrop vista.
    /// </summary>
    public class BrawlCamera : MonoBehaviour
    {
        public Transform target;
        [Tooltip("Follow offset at yaw 0; pitch and distance are derived from it.")]
        public Vector3 offset = new Vector3(0f, 7.2f, -8.2f);
        public float smoothTime = 0.12f;
        public float vistaOrbitSpeed = 4f;

        [Header("Orbit")]
        [Tooltip("Degrees per reference-resolution pixel of horizontal drag.")]
        public float dragYawSensitivity = 0.22f;
        public float dragPitchSensitivity = 0.1f;
        public float minPitch = 26f;
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
        [Min(0.1f)] public float minimumObstructionDistance = 2.8f;
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
        float noAutoTurnUntil;
        float shakeAmplitude;
        float shakeUntil;
        float obstructionDistance;
        float obstructionVelocity;
        readonly Dictionary<Renderer, bool> hiddenOccluders = new Dictionary<Renderer, bool>();

        static readonly RaycastHit[] ObstructionHits = new RaycastHit[64];

        void Awake()
        {
            instance = this;
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
            yaw = 0f;
            lastTargetPos = target.position;
            ApplyRig(target.position, true);
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
            ApplyRig(target.position, false);

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

        public static void Shake(float amplitude, float duration)
        {
            if (instance == null || AccessibilitySettings.ReducedMotionEnabled) return;
            instance.shakeAmplitude = Mathf.Max(instance.shakeAmplitude, amplitude);
            instance.shakeUntil = Time.time + duration;
        }
    }
}
