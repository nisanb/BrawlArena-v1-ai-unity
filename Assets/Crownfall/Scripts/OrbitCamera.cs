using UnityEngine;
using UnityEngine.InputSystem;

namespace Crownfall
{
    /// Third-person souls camera: mouse orbit, collision, lock-on framing,
    /// sprint FOV kick and impact shake.
    public class OrbitCamera : MonoBehaviour
    {
        public static OrbitCamera I { get; private set; }

        public CombatMotor target;
        public float distance = 4.4f;
        public float mouseSensitivity = 0.13f;

        /// Home-hub champion podium: when set (and no fight target), the menu
        /// camera does a close hero orbit instead of the wide arena sweep.
        [System.NonSerialized] public Transform menuFocus;

        public Vector3 PlanarForward => Quaternion.Euler(0f, yaw, 0f) * Vector3.forward;
        public Vector3 PlanarRight => Quaternion.Euler(0f, yaw, 0f) * Vector3.right;

        float yaw, pitch = 14f;
        float shake;
        Vector3 pivotSmoothed;
        Camera cam;
        float baseFov = 57f;
        Vector2 pendingOrbit;
        float menuYaw = 160f;

        /// External orbit input in degrees (touch drag). Consumed next LateUpdate.
        public void AddOrbitInput(Vector2 degrees) { pendingOrbit += degrees; }

        void Awake()
        {
            I = this;
            cam = GetComponent<Camera>();
            if (cam != null) baseFov = cam.fieldOfView;
        }

        public void SetTarget(CombatMotor t, bool snapBehind)
        {
            target = t;
            if (t != null && snapBehind)
            {
                yaw = t.transform.eulerAngles.y;
                pitch = 14f;
                pivotSmoothed = Pivot();
            }
        }

        Vector3 Pivot() => target != null ? target.transform.position + Vector3.up * 1.55f : transform.position;

        void LateUpdate()
        {
            var mm = MatchManager.I;

            if (target == null)
            {
                if (mm != null && (mm.State == MatchState.Menu || mm.State == MatchState.ClassSelect))
                {
                    if (menuFocus != null && menuFocus.gameObject.activeInHierarchy)
                    {
                        // hero showcase: face the champion head-on with a slow sway
                        float sway = Mathf.Sin(Time.time * 0.45f) * 24f;
                        Vector3 pivot = menuFocus.position + Vector3.up * 1.1f;
                        Vector3 dir = Quaternion.Euler(0f, menuFocus.eulerAngles.y + sway, 0f) * Vector3.forward;
                        Vector3 wanted = pivot + dir * 3.6f;
                        wanted.y = pivot.y + 0.5f;
                        transform.position = Vector3.Lerp(transform.position, wanted, 3.2f * Time.deltaTime);
                        var look = Quaternion.LookRotation(pivot + Vector3.up * 0.05f - transform.position);
                        transform.rotation = Quaternion.Slerp(transform.rotation, look, 3.2f * Time.deltaTime);
                    }
                    else
                    {
                        // cinematic slow orbit around the arena behind the menus
                        menuYaw += 3.5f * Time.deltaTime;
                        Vector3 pivot = new Vector3(0f, 1.2f, 0f);
                        Vector3 wanted = pivot + Quaternion.Euler(0f, menuYaw, 0f) * new Vector3(0f, 0f, -17.5f);
                        wanted.y = 7.4f;
                        transform.position = Vector3.Lerp(transform.position, wanted, 1.6f * Time.deltaTime);
                        var look = Quaternion.LookRotation(pivot + Vector3.up * 0.4f - transform.position);
                        transform.rotation = Quaternion.Slerp(transform.rotation, look, 1.6f * Time.deltaTime);
                    }
                }
                return;
            }

            bool fighting = mm != null && mm.State == MatchState.Fighting && !mm.Paused;
            var mouse = Mouse.current;

            var lockTarget = target.LockTarget;
            bool locked = lockTarget != null && !lockTarget.IsDead;

            Vector2 orbit = pendingOrbit;
            pendingOrbit = Vector2.zero;

            if (fighting && !locked && mouse != null && Cursor.lockState == CursorLockMode.Locked)
                orbit += mouse.delta.ReadValue() * mouseSensitivity * CrownfallSettings.Sensitivity;

            if (!locked)
            {
                yaw += orbit.x;
                pitch -= orbit.y;
            }
            else
            {
                // frame player and target
                Vector3 to = lockTarget.AimPoint - target.transform.position;
                to.y = 0f;
                if (to.sqrMagnitude > 0.05f)
                {
                    float desiredYaw = Quaternion.LookRotation(to).eulerAngles.y;
                    yaw = Mathf.LerpAngle(yaw, desiredYaw, 5.5f * Time.deltaTime);
                }
                float dist = Vector3.Distance(target.transform.position, lockTarget.transform.position);
                float desiredPitch = Mathf.Lerp(10f, 22f, Mathf.InverseLerp(2f, 12f, dist));
                pitch = Mathf.Lerp(pitch, desiredPitch, 4f * Time.deltaTime);

                if (fighting && mouse != null && Cursor.lockState == CursorLockMode.Locked)
                    orbit += mouse.delta.ReadValue() * mouseSensitivity * CrownfallSettings.Sensitivity;
                yaw += orbit.x * 0.35f;
                pitch -= orbit.y * 0.35f;
            }
            pitch = Mathf.Clamp(pitch, -28f, 62f);

            pivotSmoothed = Vector3.Lerp(pivotSmoothed, Pivot(), 14f * Time.deltaTime);

            Quaternion rot = Quaternion.Euler(pitch, yaw, 0f);
            float wantDist = locked ? distance + 0.5f : distance;
            Vector3 desired = pivotSmoothed - rot * Vector3.forward * wantDist
                              + rot * Vector3.right * 0.35f;

            // collision: keep the camera out of walls (combatants live on IgnoreRaycast)
            Vector3 castDir = desired - pivotSmoothed;
            float castLen = castDir.magnitude;
            if (castLen > 0.01f &&
                Physics.SphereCast(pivotSmoothed, 0.25f, castDir.normalized, out var hitInfo, castLen,
                    Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore))
            {
                desired = pivotSmoothed + castDir.normalized * Mathf.Max(0.4f, hitInfo.distance - 0.05f);
            }

            // impact shake
            if (shake > 0.001f)
            {
                float t = Time.unscaledTime * 34f;
                desired += new Vector3(
                    (Mathf.PerlinNoise(t, 0.3f) - 0.5f),
                    (Mathf.PerlinNoise(0.6f, t) - 0.5f), 0f) * shake * 0.55f;
                shake = Mathf.MoveTowards(shake, 0f, 3.2f * Time.unscaledDeltaTime);
            }

            transform.SetPositionAndRotation(desired, rot);

            if (locked)
            {
                Vector3 look = (lockTarget.AimPoint + pivotSmoothed) * 0.5f + Vector3.up * 0.2f - transform.position;
                if (look.sqrMagnitude > 0.01f)
                    transform.rotation = Quaternion.Slerp(transform.rotation, Quaternion.LookRotation(look), 0.6f);
            }

            if (cam != null)
            {
                float want = baseFov;
                if (target.IsSprinting) want = baseFov + 8f;
                else if (locked) want = baseFov - 2f;
                cam.fieldOfView = Mathf.Lerp(cam.fieldOfView, want, 6f * Time.deltaTime);
            }
        }

        public void Shake(float amount)
        {
            if (!CrownfallSettings.ShakeEnabled) return;
            shake = Mathf.Max(shake, amount);
        }

        public void ShakeIfNear(Vector3 pos, float radius, float amount)
        {
            if (target == null) return;
            if ((target.transform.position - pos).sqrMagnitude <= radius * radius) Shake(amount);
        }
    }
}
