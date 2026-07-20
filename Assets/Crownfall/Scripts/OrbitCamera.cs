using System.Collections.Generic;
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
        float crowdLift;
        static readonly Team[] BothTeams = { Team.Azure, Team.Crimson };
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
            // death cam: ease higher and pull back so being downed reads as its own beat
            if (target.IsDead) pitch = Mathf.Lerp(pitch, 32f, 2.4f * Time.deltaTime);

            pivotSmoothed = Vector3.Lerp(pivotSmoothed, Pivot(), 14f * Time.deltaTime);

            Quaternion rot = Quaternion.Euler(pitch, yaw, 0f);
            float wantDist = (locked ? distance + 0.5f : distance) + (target.IsDead ? 3.4f : 0f);
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

            // combatant bodies crowd the lens (they live on IgnoreRaycast so the
            // wall cast can't push off them); lift the camera to see over a body
            // jammed against it — the low-HP scrum frames were near-unreadable
            float wantLift = 0f;
            if (mm != null && target != null && target.Identity != null)
            {
                Vector3 lensDir = desired - pivotSmoothed;
                float lensLen = lensDir.magnitude;
                if (lensLen > 0.05f)
                {
                    lensDir /= lensLen;
                    foreach (var team in BothTeams)
                        foreach (var other in mm.AliveEnemiesOf(team))
                        {
                            if (other == null || other == target || other.IsDead) continue;
                            Vector3 op = other.transform.position + Vector3.up;
                            float proj = Vector3.Dot(op - pivotSmoothed, lensDir);
                            if (proj < 0.3f || proj > lensLen + 0.5f) continue;
                            float perp = Vector3.Distance(op, pivotSmoothed + lensDir * proj);
                            if (perp < 0.95f) wantLift = Mathf.Max(wantLift, (0.95f - perp) / 0.95f);
                        }
                }
            }
            crowdLift = Mathf.MoveTowards(crowdLift, wantLift, 3.5f * Time.deltaTime);
            desired += Vector3.up * crowdLift * 1.05f;

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

            UpdateFoliage(transform.position, pivotSmoothed);
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

        // ------------------------------------------------------------- foliage
        // Tall bushes between the lens and the player buried the frame mid-fight
        // (all three reviewers' top gripe). They carry no colliders (removed for
        // walkability) so the collision spherecast can never pull the camera past
        // them — instead we shrink any tall foliage sitting in the camera->player
        // corridor down out of the way, and grow it back the instant the sightline
        // is clear. Concealment is position-based, so this never changes who can
        // hide where — only whether the camera can see through the cover it's in.
        struct FoliageItem { public Transform t; public Vector3 baseScale; public float k; }
        readonly List<FoliageItem> foliageItems = new List<FoliageItem>();
        bool foliageScanned;

        void ScanFoliage()
        {
            foliageScanned = true;
            var rends = FindObjectsByType<Renderer>(FindObjectsSortMode.None);
            var seen = new HashSet<Transform>();
            foreach (var r in rends)
            {
                if (r == null) continue;
                string n = r.transform.name.ToLowerInvariant();
                bool isFoliage = n.Contains("grass") || n.Contains("bush") ||
                                 n.Contains("flower") || n.Contains("tree") || n.Contains("plant");
                if (!isFoliage) continue;
                if (r.bounds.size.y < 1.1f) continue; // only tall tufts block the lens
                var root = r.transform;
                if (!seen.Add(root)) continue;
                foliageItems.Add(new FoliageItem { t = root, baseScale = root.localScale, k = 1f });
            }
        }

        void UpdateFoliage(Vector3 camPos, Vector3 pivot)
        {
            if (!foliageScanned) ScanFoliage();
            Vector3 seg = pivot - camPos;
            float segLen = seg.magnitude;
            if (segLen < 0.05f) return;
            Vector3 dir = seg / segLen;
            for (int i = 0; i < foliageItems.Count; i++)
            {
                var fo = foliageItems[i];
                if (fo.t == null) continue;
                Vector3 p = fo.t.position + Vector3.up * 0.7f;
                // Distance to the camera->pivot corridor (projection CLAMPED so tufts
                // hugging the player when you stand INSIDE a bush count too), plus a
                // plain radius around the lens itself (lateral clumps around a nestled
                // camera). Either one blinds the shot, so either one shrinks the tuft.
                float proj = Mathf.Clamp(Vector3.Dot(p - camPos, dir), 0f, segLen);
                float segDist = Vector3.Distance(p, camPos + dir * proj);
                float camDist = Vector3.Distance(p, camPos);
                bool blocking = (proj < segLen - 0.15f && segDist < 1.35f) || camDist < 2.1f;
                fo.k = Mathf.MoveTowards(fo.k, blocking ? 0.06f : 1f, 7f * Time.deltaTime);
                fo.t.localScale = fo.baseScale * fo.k;
                foliageItems[i] = fo;
            }
        }
    }
}
