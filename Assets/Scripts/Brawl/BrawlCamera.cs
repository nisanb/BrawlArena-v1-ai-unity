using UnityEngine;

namespace BrawlArena
{
    /// <summary>
    /// Classic brawler chase camera: fixed 3/4 top-down angle, smooth follow,
    /// small perlin shake for hit feedback.
    /// </summary>
    public class BrawlCamera : MonoBehaviour
    {
        public Transform target;
        public Vector3 offset = new Vector3(0f, 11.5f, -7.5f);
        public float smoothTime = 0.12f;

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
            float pitch = Mathf.Atan2(offset.y, -offset.z) * Mathf.Rad2Deg;
            transform.rotation = Quaternion.Euler(pitch, 0f, 0f);

            if (target == null)
            {
                var player = FindFirstObjectByType<PlayerBrawlerInput>();
                if (player != null) target = player.transform;
            }
            if (target != null) transform.position = target.position + offset;
        }

        void LateUpdate()
        {
            if (target == null) return;
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
