using System;
using UnityEngine;

namespace BrawlArena
{
    /// <summary>
    /// Project-owned bow/string/arrow staging driven only by Brawl's semantic
    /// weapon-presentation calls. It never launches a projectile or enters an
    /// Invector shooter, ammo, damage, or weapon lifecycle.
    /// </summary>
    [DefaultExecutionOrder(110)]
    [DisallowMultipleComponent]
    public sealed class InvectorBowPresentationRig : MonoBehaviour
    {
        const float MinimumReleaseHold = 0.01f;

        [SerializeField, HideInInspector]
        Transform arrowVisual;

        [SerializeField, HideInInspector]
        Transform nockPoint;

        [SerializeField, HideInInspector]
        Vector3 arrowNockLocalPoint;

        [SerializeField, HideInInspector]
        Vector3 arrowTipLocalPoint;

        [SerializeField, HideInInspector]
        LineRenderer bowString;

        [SerializeField, HideInInspector]
        Transform stringTopAnchor;

        [SerializeField, HideInInspector]
        Transform stringRestAnchor;

        [SerializeField, HideInInspector]
        Transform stringBottomAnchor;

        [SerializeField, Min(MinimumReleaseHold)]
        float releaseHoldSeconds = 0.08f;

        // These values are authored prefab configuration, not transient runtime
        // state. Persist them so a freshly loaded production prefab can restore
        // Arrow2 without first entering a builder-only Configure call.
        [SerializeField, HideInInspector]
        Vector3 authoredArrowLocalPosition;

        [SerializeField, HideInInspector]
        Quaternion authoredArrowLocalRotation = Quaternion.identity;

        [SerializeField, HideInInspector]
        bool authoredArrowActive;

        [SerializeField, HideInInspector]
        bool hasAuthoredArrowBaseline;

        bool runtimeEnabled;
        bool aimStaged;
        bool releasePending;
        bool presentationVisible = true;
        float releaseRestoreAt;

        int aimStageCount;
        int releaseCount;
        int arrowRestoreCount;

        public bool RuntimeEnabled => runtimeEnabled;
        public bool AimStaged => aimStaged;
        public bool ReleasePending => releasePending;
        public bool PresentationVisible => presentationVisible;
        public Transform ArrowVisual => arrowVisual;
        public Transform NockPoint => nockPoint;
        public Vector3 ArrowNockLocalPoint => arrowNockLocalPoint;
        public Vector3 ArrowTipLocalPoint => arrowTipLocalPoint;
        public LineRenderer BowString => bowString;
        public Transform StringTopAnchor => stringTopAnchor;
        public Transform StringRestAnchor => stringRestAnchor;
        public Transform StringBottomAnchor => stringBottomAnchor;
        public int AimStageCount => aimStageCount;
        public int ReleaseCount => releaseCount;
        public int ArrowRestoreCount => arrowRestoreCount;

        public bool IsConfigured =>
            arrowVisual != null && arrowVisual.IsChildOf(transform) &&
            arrowVisual.parent != transform &&
            nockPoint != null && nockPoint.IsChildOf(transform) &&
            bowString != null && bowString.transform.IsChildOf(transform) &&
            stringTopAnchor != null && stringTopAnchor.IsChildOf(transform) &&
            stringRestAnchor != null && stringRestAnchor.IsChildOf(transform) &&
            stringBottomAnchor != null && stringBottomAnchor.IsChildOf(transform) &&
            bowString.positionCount == 3 && !bowString.useWorldSpace &&
            HasValidAuthoredArrowBaseline &&
            IsFinite(arrowNockLocalPoint) && IsFinite(arrowTipLocalPoint) &&
            (arrowTipLocalPoint - arrowNockLocalPoint).sqrMagnitude > 0.000001f;

        bool HasValidAuthoredArrowBaseline =>
            hasAuthoredArrowBaseline &&
            IsFinite(authoredArrowLocalPosition) &&
            IsFinite(authoredArrowLocalRotation) &&
            QuaternionMagnitudeSquared(authoredArrowLocalRotation) > 0.000001f;

        public bool IsDormantConfigured =>
            IsConfigured && !runtimeEnabled && !enabled && !aimStaged &&
            !releasePending;

        /// <summary>Builder-only configuration; leaves the rig dormant.</summary>
        public void Configure(
            Transform arrow,
            Transform nock,
            Vector3 arrowLocalNock,
            Vector3 arrowLocalTip,
            LineRenderer stringRenderer,
            Transform topAnchor,
            Transform restAnchor,
            Transform bottomAnchor,
            float releaseHold = 0.08f)
        {
            if (arrow == null) throw new ArgumentNullException(nameof(arrow));
            if (nock == null) throw new ArgumentNullException(nameof(nock));
            if (stringRenderer == null)
                throw new ArgumentNullException(nameof(stringRenderer));
            if (topAnchor == null) throw new ArgumentNullException(nameof(topAnchor));
            if (restAnchor == null) throw new ArgumentNullException(nameof(restAnchor));
            if (bottomAnchor == null)
                throw new ArgumentNullException(nameof(bottomAnchor));
            if (!arrow.IsChildOf(transform) || arrow.parent == transform ||
                !nock.IsChildOf(transform) ||
                !stringRenderer.transform.IsChildOf(transform) ||
                !topAnchor.IsChildOf(transform) ||
                !restAnchor.IsChildOf(transform) ||
                !bottomAnchor.IsChildOf(transform))
            {
                throw new ArgumentException(
                    "Bow staging references must belong to the assembled character hierarchy.");
            }
            if (!IsFinite(arrowLocalNock) || !IsFinite(arrowLocalTip) ||
                (arrowLocalTip - arrowLocalNock).sqrMagnitude <= 0.000001f)
            {
                throw new ArgumentException(
                    "The authored arrow needs distinct finite nock and tip points.");
            }
            if (!IsFinite(arrow.localPosition) ||
                !IsFinite(arrow.localRotation) ||
                QuaternionMagnitudeSquared(arrow.localRotation) <= 0.000001f)
            {
                throw new ArgumentException(
                    "The authored arrow needs a finite local transform baseline.");
            }

            arrowVisual = arrow;
            nockPoint = nock;
            arrowNockLocalPoint = arrowLocalNock;
            arrowTipLocalPoint = arrowLocalTip;
            bowString = stringRenderer;
            stringTopAnchor = topAnchor;
            stringRestAnchor = restAnchor;
            stringBottomAnchor = bottomAnchor;
            releaseHoldSeconds = Mathf.Max(MinimumReleaseHold, releaseHold);
            authoredArrowLocalPosition = arrow.localPosition;
            authoredArrowLocalRotation = arrow.localRotation;
            authoredArrowActive = arrow.gameObject.activeSelf;
            hasAuthoredArrowBaseline = true;
            presentationVisible = true;

            DisableRuntime();
            ResetRuntimeTrace();
        }

        public bool EnableRuntime()
        {
            if (!Application.isPlaying || !IsConfigured)
            {
                DisableRuntime();
                return false;
            }

            runtimeEnabled = true;
            enabled = true;
            presentationVisible = true;
            RestoreAuthoredArrow(false);
            SetStringDrawn(false);
            return true;
        }

        public void DisableRuntime()
        {
            runtimeEnabled = false;
            ResetPresentation(true);
            if (enabled) enabled = false;
        }

        public void PresentAim(Vector3 worldDirection)
        {
            if (!runtimeEnabled || !isActiveAndEnabled) return;
            if (!IsFinite(worldDirection) ||
                worldDirection.sqrMagnitude <= 0.000001f)
            {
                ReleaseAim();
                return;
            }

            releasePending = false;
            aimStaged = true;
            aimStageCount++;
            ApplyArrowAtNock();
            SetArrowActive(presentationVisible);
            SetStringDrawn(true);
        }

        public void ReleaseAim()
        {
            aimStaged = false;
            if (!releasePending)
                RestoreAuthoredArrow(false);
            SetStringDrawn(false);
        }

        public void PresentRelease(Vector3 worldPosition, Vector3 worldDirection)
        {
            if (!runtimeEnabled || !isActiveAndEnabled || !presentationVisible ||
                !IsFinite(worldPosition) || !IsFinite(worldDirection))
                return;

            aimStaged = false;
            releasePending = true;
            releaseRestoreAt = Time.unscaledTime + releaseHoldSeconds;
            releaseCount++;
            SetArrowActive(false);
            SetStringDrawn(false);
        }

        public void SetVisible(bool value)
        {
            presentationVisible = value;
            if (!value)
            {
                aimStaged = false;
                releasePending = false;
                SetArrowActive(false);
                SetStringDrawn(false);
                return;
            }

            if (aimStaged)
            {
                ApplyArrowAtNock();
                SetArrowActive(true);
                SetStringDrawn(true);
            }
            else if (!releasePending)
            {
                RestoreAuthoredArrow(false);
                SetStringDrawn(false);
            }
        }

        public void ResetForRespawn()
        {
            ResetPresentation(true);
        }

        public void ResetRuntimeTrace()
        {
            if (runtimeEnabled)
                throw new InvalidOperationException(
                    "Disable the bow-presentation runtime gate before resetting its trace.");
            aimStageCount = 0;
            releaseCount = 0;
            arrowRestoreCount = 0;
        }

        void LateUpdate()
        {
            if (!runtimeEnabled) return;

            if (releasePending && Time.unscaledTime >= releaseRestoreAt)
            {
                releasePending = false;
                RestoreAuthoredArrow(true);
            }
            else if (aimStaged)
            {
                ApplyArrowAtNock();
            }

            SetStringDrawn(aimStaged && presentationVisible);
        }

        void OnDisable()
        {
            if (!runtimeEnabled) return;
            runtimeEnabled = false;
            ResetPresentation(true);
        }

        void OnDestroy()
        {
            runtimeEnabled = false;
            ResetPresentation(true);
        }

        void ResetPresentation(bool restoreVisibility)
        {
            aimStaged = false;
            releasePending = false;
            if (restoreVisibility) presentationVisible = true;
            RestoreAuthoredArrow(false);
            SetStringDrawn(false);
        }

        void ApplyArrowAtNock()
        {
            if (arrowVisual == null || nockPoint == null) return;
            arrowVisual.rotation = nockPoint.rotation;
            Vector3 stagedNock = arrowVisual.TransformPoint(arrowNockLocalPoint);
            arrowVisual.position += nockPoint.position - stagedNock;
        }

        void RestoreAuthoredArrow(bool countRestore)
        {
            // Old or partially rebuilt prefabs do not have the serialized
            // baseline sentinel. Leave their authored transform untouched and
            // let IsConfigured keep the runtime gate closed.
            if (arrowVisual == null || !HasValidAuthoredArrowBaseline) return;
            arrowVisual.localPosition = authoredArrowLocalPosition;
            arrowVisual.localRotation = authoredArrowLocalRotation;
            SetArrowActive(presentationVisible && authoredArrowActive);
            if (countRestore) arrowRestoreCount++;
        }

        void SetArrowActive(bool value)
        {
            if (arrowVisual != null && arrowVisual.gameObject.activeSelf != value)
                arrowVisual.gameObject.SetActive(value);
        }

        void SetStringDrawn(bool drawn)
        {
            if (bowString == null || stringTopAnchor == null ||
                stringRestAnchor == null || stringBottomAnchor == null)
                return;

            Transform lineTransform = bowString.transform;
            bowString.SetPosition(
                0, lineTransform.InverseTransformPoint(stringTopAnchor.position));
            bowString.SetPosition(
                1,
                lineTransform.InverseTransformPoint(
                    drawn && nockPoint != null
                        ? nockPoint.position
                        : stringRestAnchor.position));
            bowString.SetPosition(
                2, lineTransform.InverseTransformPoint(stringBottomAnchor.position));
        }

        static bool IsFinite(Vector3 value)
        {
            return IsFinite(value.x) && IsFinite(value.y) && IsFinite(value.z);
        }

        static bool IsFinite(Quaternion value)
        {
            return IsFinite(value.x) && IsFinite(value.y) &&
                   IsFinite(value.z) && IsFinite(value.w);
        }

        static float QuaternionMagnitudeSquared(Quaternion value)
        {
            return value.x * value.x + value.y * value.y +
                   value.z * value.z + value.w * value.w;
        }

        static bool IsFinite(float value)
        {
            return !float.IsNaN(value) && !float.IsInfinity(value);
        }
    }
}
