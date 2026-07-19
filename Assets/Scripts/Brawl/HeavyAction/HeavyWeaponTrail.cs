using System.Collections;
using UnityEngine;
using UnityEngine.Rendering;

namespace BrawlArena
{
    /// <summary>
    /// Per-swing weapon trail for souls-style brawlers: a TrailRenderer
    /// anchored at the weapon tip (the SpellOrigin muzzle child, or the
    /// visual's renderer-bounds far end when no socket exists) that stays
    /// dark until the animation driver flashes it for an attack window. The
    /// slower committed swings get a long bright arc that reads at gameplay
    /// zoom. Visual-only: never gameplay resource, timing, or damage
    /// authority.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class HeavyWeaponTrail : MonoBehaviour
    {
        const string TipSocketName = "SpellOrigin";
        const string TrailChildName = "WeaponTrail";
        const string FallbackTipName = "TrailTip";
        const float TrailTimeSeconds = 0.26f;
        const float BaseStartWidth = 0.32f;
        const float MinVertexDistance = 0.02f;
        const float FlashColorBoost = 1.6f;
        const int CapVertices = 4;

        [SerializeField, HideInInspector]
        Color trailColor = Color.white;

        [SerializeField, HideInInspector]
        float widthScale = 1f;

        [SerializeField, HideInInspector]
        Transform tipAnchor;

        TrailRenderer trail;
        Material trailMaterial;
        Coroutine flashRoutine;

        public Color TrailColor => trailColor;
        public float WidthScale => widthScale;
        public Transform TipAnchor => tipAnchor;
        public bool Flashing => flashRoutine != null;

        /// <summary>
        /// Builder-facing wiring: per-hero trail tint and width, keeping any
        /// previously assigned tip anchor.
        /// </summary>
        public void Configure(Color color, float configuredWidthScale)
        {
            Configure(color, configuredWidthScale, tipAnchor);
        }

        /// <summary>
        /// Builder-facing wiring with an explicit tip anchor; a null tip falls
        /// back to the SpellOrigin socket / renderer-bounds search at Awake.
        /// </summary>
        public void Configure(Color color, float configuredWidthScale, Transform tip)
        {
            trailColor = color;
            widthScale = Mathf.Max(0.01f, configuredWidthScale);
            tipAnchor = tip;
            if (trail != null) ApplyTrailLook();
        }

        /// <summary>
        /// Enables trail emission for the given window with a brief bright
        /// start-color pulse. The latest call always wins over one in flight.
        /// Safe no-op outside play mode or before Awake built the trail.
        /// </summary>
        public void Flash(float seconds)
        {
            if (seconds <= 0f || trail == null || !Application.isPlaying ||
                !isActiveAndEnabled) return;

            if (flashRoutine != null) StopCoroutine(flashRoutine);
            trail.Clear();
            Color boosted = trailColor * FlashColorBoost;
            boosted.a = 1f;
            trail.startColor = boosted;
            trail.emitting = true;
            flashRoutine = StartCoroutine(EndFlashAfter(seconds));
        }

        void Awake()
        {
            if (!Application.isPlaying) return;

            var child = new GameObject(TrailChildName);
            child.transform.SetParent(ResolveTipAnchor(), false);
            child.transform.localPosition = Vector3.zero;
            trail = child.AddComponent<TrailRenderer>();
            trail.emitting = false;
            trail.autodestruct = false;
            trail.alignment = LineAlignment.View;
            trail.textureMode = LineTextureMode.Stretch;
            trail.shadowCastingMode = ShadowCastingMode.Off;
            trail.receiveShadows = false;
            trailMaterial = CreateTrailMaterial();
            trail.sharedMaterial = trailMaterial;
            ApplyTrailLook();
        }

        void ApplyTrailLook()
        {
            trail.time = TrailTimeSeconds;
            trail.startWidth = BaseStartWidth * widthScale;
            trail.endWidth = 0f;
            trail.numCapVertices = CapVertices;
            trail.minVertexDistance = MinVertexDistance;
            trail.startColor = trailColor;
            Color faded = trailColor;
            faded.a = 0f;
            trail.endColor = faded;
        }

        Transform ResolveTipAnchor()
        {
            if (tipAnchor != null) return tipAnchor;

            tipAnchor = FindDescendant(transform, TipSocketName);
            if (tipAnchor != null) return tipAnchor;

            // No muzzle socket: anchor at the far end of the weapon visual's
            // combined renderer bounds so the trail still leads the swing.
            var fallback = new GameObject(FallbackTipName);
            fallback.transform.SetParent(transform, false);
            fallback.transform.position = ComputeRendererBoundsFarEnd();
            tipAnchor = fallback.transform;
            return tipAnchor;
        }

        Vector3 ComputeRendererBoundsFarEnd()
        {
            Renderer[] renderers = GetComponentsInChildren<Renderer>(true);
            bool found = false;
            Bounds combined = default;
            for (int i = 0; i < renderers.Length; i++)
            {
                Renderer candidate = renderers[i];
                if (candidate == null ||
                    (!(candidate is MeshRenderer) &&
                     !(candidate is SkinnedMeshRenderer))) continue;
                if (!found)
                {
                    combined = candidate.bounds;
                    found = true;
                }
                else
                {
                    combined.Encapsulate(candidate.bounds);
                }
            }
            if (!found) return transform.position;

            Vector3 origin = transform.position;
            Vector3 center = combined.center;
            Vector3 extents = combined.extents;
            Vector3 farthest = center;
            float bestSqr = -1f;
            for (int x = -1; x <= 1; x += 2)
            for (int y = -1; y <= 1; y += 2)
            for (int z = -1; z <= 1; z += 2)
            {
                Vector3 corner = center + Vector3.Scale(
                    extents, new Vector3(x, y, z));
                float sqr = (corner - origin).sqrMagnitude;
                if (sqr <= bestSqr) continue;
                bestSqr = sqr;
                farthest = corner;
            }
            return farthest;
        }

        static Material CreateTrailMaterial()
        {
            Shader shader = Shader.Find("Universal Render Pipeline/Particles/Unlit");
            if (shader == null) shader = Shader.Find("Sprites/Default");
            var material = new Material(shader);
            if (material.HasProperty("_BaseColor"))
                material.SetColor("_BaseColor", Color.white);
            // Additive-friendly transparent surface; the URP particle shader
            // reads these hidden properties directly, so no keyword juggling.
            if (material.HasProperty("_Surface")) material.SetFloat("_Surface", 1f);
            if (material.HasProperty("_Blend")) material.SetFloat("_Blend", 2f);
            if (material.HasProperty("_SrcBlend"))
                material.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
            if (material.HasProperty("_DstBlend"))
                material.SetFloat("_DstBlend", (float)BlendMode.One);
            if (material.HasProperty("_ZWrite")) material.SetFloat("_ZWrite", 0f);
            material.SetOverrideTag("RenderType", "Transparent");
            material.renderQueue = (int)RenderQueue.Transparent;
            return material;
        }

        IEnumerator EndFlashAfter(float seconds)
        {
            yield return new WaitForSeconds(seconds);
            flashRoutine = null;
            EndFlash();
        }

        void EndFlash()
        {
            if (trail == null) return;
            trail.emitting = false;
            trail.startColor = trailColor;
        }

        void OnDisable()
        {
            if (flashRoutine != null)
            {
                StopCoroutine(flashRoutine);
                flashRoutine = null;
            }
            EndFlash();
        }

        void OnDestroy()
        {
            if (trailMaterial != null) Destroy(trailMaterial);
        }

        static Transform FindDescendant(Transform root, string childName)
        {
            if (root == null) return null;
            Transform[] children = root.GetComponentsInChildren<Transform>(true);
            for (int i = 0; i < children.Length; i++)
                if (children[i] != null && children[i].name == childName)
                    return children[i];
            return null;
        }
    }
}
