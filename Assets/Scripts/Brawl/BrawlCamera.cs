using UnityEngine;

namespace BrawlArena
{
    /// <summary>
    /// Brawler chase camera: fixed third-person 3/4 angle, smooth follow, small
    /// perlin shake for hit feedback. Before a target exists (character select /
    /// loading), it slowly orbits the arena as a backdrop vista.
    /// </summary>
    public class BrawlCamera : MonoBehaviour
    {
        public Transform target;
        [Tooltip("Follow offset; pitch is derived from it so the target stays centered.")]
        public Vector3 offset = new Vector3(0f, 7.2f, -8.2f);
        public float smoothTime = 0.12f;
        public float vistaOrbitSpeed = 4f;

        static BrawlCamera instance;

        Vector3 velocity;
        float shakeAmplitude;
        float shakeUntil;

        void Awake()
        {
            instance = this;
        }

        void OnDestroy()
        {
            if (instance == this) instance = null;
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
            float pitch = Mathf.Atan2(offset.y, -offset.z) * Mathf.Rad2Deg;
            transform.rotation = Quaternion.Euler(pitch, 0f, 0f);
            transform.position = target.position + offset;
            velocity = Vector3.zero;
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

            Vector3 pos = Vector3.SmoothDamp(transform.position, target.position + offset, ref velocity, smoothTime);
            if (Time.time < shakeUntil)
            {
                float t = Time.time * 37f;
                pos += new Vector3(
                    Mathf.PerlinNoise(t, 0.5f) - 0.5f,
                    Mathf.PerlinNoise(0.5f, t) - 0.5f,
                    0f) * (shakeAmplitude * 2f);
            }
            else
            {
                shakeAmplitude = 0f;
            }
            transform.position = pos;
        }

        public static void Shake(float amplitude, float duration)
        {
            if (instance == null) return;
            instance.shakeAmplitude = Mathf.Max(instance.shakeAmplitude, amplitude);
            instance.shakeUntil = Time.time + duration;
        }
    }
}
