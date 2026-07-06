using UnityEngine;

namespace BrawlArena
{
    /// <summary>
    /// Brawler chase camera: third-person 3/4 angle with a player-driven
    /// orbit. Dragging the right side of the screen (CameraDragZone) yaws and
    /// pitches the rig, and while the player strafes the camera slowly swings
    /// back behind the movement direction. Small perlin shake for hit
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
        [Tooltip("Seconds after a manual drag before the slow auto-turn resumes.")]
        public float autoTurnGraceAfterDrag = 1.6f;

        static BrawlCamera instance;

        float yaw;
        float pitch;
        float distance;
        Vector3 followVelocity;
        Vector3 lastTargetPos;
        float noAutoTurnUntil;
        float shakeAmplitude;
        float shakeUntil;

        void Awake()
        {
            instance = this;
            DeriveRigFromOffset();
        }

        void OnDestroy()
        {
            if (instance == this) instance = null;
        }

        void DeriveRigFromOffset()
        {
            pitch = Mathf.Clamp(Mathf.Atan2(offset.y, -offset.z) * Mathf.Rad2Deg, minPitch, maxPitch);
            distance = new Vector2(offset.y, offset.z).magnitude;
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

        Vector3 RigOffset()
        {
            return Quaternion.Euler(pitch, yaw, 0f) * new Vector3(0f, 0f, -distance);
        }

        void ApplyRig(Vector3 focus, bool snap)
        {
            Vector3 desired = focus + RigOffset();
            transform.position = snap
                ? desired
                : Vector3.SmoothDamp(transform.position, desired, ref followVelocity, smoothTime);
            transform.rotation = Quaternion.Euler(pitch, yaw, 0f);
        }

        void LateUpdate()
        {
            if (target == null)
            {
                // Idle vista: slow orbit around the arena center.
                transform.RotateAround(new Vector3(0f, 1f, 0f), Vector3.up, vistaOrbitSpeed * Time.deltaTime);
                transform.LookAt(new Vector3(0f, 1.5f, 0f));
                return;
            }

            UpdateAutoTurn();
            ApplyRig(target.position, false);

            if (Time.time < shakeUntil)
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
            if (instance == null) return;
            instance.shakeAmplitude = Mathf.Max(instance.shakeAmplitude, amplitude);
            instance.shakeUntil = Time.time + duration;
        }
    }
}
