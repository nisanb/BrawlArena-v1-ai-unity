using System;
using Invector.IK;
using Invector.vShooter;
using UnityEngine;

namespace BrawlArena
{
    public enum InvectorWeaponPresentationSuppression
    {
        None = 0,
        GateClosed = 1,
        AnimatorUnavailable = 2,
        LifecycleState = 3,
        IgnoreIKTag = 4,
        IgnoreSupportHandIKTag = 5,
        MissingIKData = 6,
        InvalidIKPose = 7,
        RuntimeFault = 8,
        VisualHidden = 9,
    }

    /// <summary>
    /// Dormant, visual-only weapon presenter for the isolated Invector lab.
    /// It reads project-owned Invector IK-adjust assets and applies one guarded
    /// LateUpdate arm pass. It never enters the vendor shooter lifecycle.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class InvectorBrawlerWeaponPresentation :
        MonoBehaviour, IBrawlerWeaponPresentation
    {
        const float MinimumBoneLength = 0.0001f;
        const float MinimumHintSeparation = 0.001f;
        const float ReachSkin = 0.001f;
        const string IgnoreIKTag = "IgnoreIK";
        const string IgnoreSupportHandIKTag = "IgnoreSupportHandIK";

        [SerializeField, HideInInspector]
        Animator configuredAnimator;

        [SerializeField, HideInInspector]
        BrawlInvectorThirdPersonController configuredController;

        [SerializeField, HideInInspector]
        InvectorShooterMeleeInputAdapter configuredInput;

        [SerializeField, HideInInspector]
        Transform weaponVisualRoot;

        [SerializeField, HideInInspector]
        Transform muzzle;

        [SerializeField, HideInInspector]
        Transform supportHandTarget;

        [SerializeField, HideInInspector]
        Transform supportHintTarget;

        [SerializeField, HideInInspector]
        vWeaponIKAdjustList projectIKAdjustList;

        [SerializeField, HideInInspector]
        string weaponCategory = string.Empty;

        [SerializeField, HideInInspector]
        bool weaponHeldInLeftHand;

        [SerializeField, Range(0f, 1f)]
        float ikWeight = 1f;

        [SerializeField, HideInInspector]
        ParticleSystem[] muzzleEffects = Array.Empty<ParticleSystem>();

        [SerializeField, HideInInspector]
        Vector3[] muzzleEffectLocalPositions = Array.Empty<Vector3>();

        [SerializeField, HideInInspector]
        Quaternion[] muzzleEffectLocalRotations = Array.Empty<Quaternion>();

        [SerializeField, HideInInspector]
        InvectorBowPresentationRig bowPresentationRig;

        [SerializeField, HideInInspector]
        bool hasAuthoredPreviewPose;

        [SerializeField, HideInInspector]
        Vector3 previewWeaponHandLocalPosition;

        [SerializeField, HideInInspector]
        Vector3 previewWeaponHintLocalPosition;

        [SerializeField, HideInInspector]
        Vector3 previewWeaponHandLocalEuler;

        [SerializeField, HideInInspector]
        Vector3 previewSupportHandLocalPosition;

        [SerializeField, HideInInspector]
        Vector3 previewSupportHintLocalPosition;

        [SerializeField, HideInInspector]
        Vector3 previewSupportHandLocalEuler;

        [SerializeField, HideInInspector]
        bool hideAuthoredArrowInPreview;

        bool runtimeEnabled;
        bool previewEnabled;
        bool previewArrowWasActive;
        bool aimPresented;
        bool visible = true;
        bool tearingDown;
        Vector3 presentedAimDirection;
        [NonSerialized] vIKSolver weaponHandSolver;
        [NonSerialized] vIKSolver supportHandSolver;

        int aimRequestCount;
        int aimReleaseCount;
        int muzzlePositionRequestCount;
        int muzzlePresentationRequestCount;
        int muzzleEmissionCount;
        int visibilityRequestCount;
        int respawnResetCount;
        int gatedLateUpdateCount;
        int appliedIKPassCount;
        int suppressedIKPassCount;
        int supportHandSuppressionCount;
        int invalidPoseCount;
        int droppedRequestCount;
        int runtimeFaultCount;
        int gateEnableFailureCount;
        int previewPoseApplyCount;
        float lastPreviewWeaponHandDistance = -1f;
        float lastPreviewSupportHandDistance = -1f;
        InvectorWeaponPresentationSuppression lastSuppression;
        string lastFaultType = string.Empty;
        string lastInvalidPoseStage = string.Empty;

        public bool RuntimeEnabled => runtimeEnabled;
        public bool PreviewEnabled => previewEnabled;
        public bool LabRuntimeEnabled => RuntimeEnabled;
        public bool IsConfigured => HasConfiguredReferences && HasCompleteIKData();
        public bool IsDormantConfigured =>
            IsConfigured && !runtimeEnabled && !previewEnabled && !enabled &&
            !aimPresented;
        public bool HasRuntimeSolvers =>
            weaponHandSolver != null || supportHandSolver != null;
        public bool AimPresented => aimPresented;
        public bool Visible => visible;
        public Vector3 PresentedAimDirection => presentedAimDirection;
        public Animator ConfiguredAnimator => configuredAnimator;
        public BrawlInvectorThirdPersonController ConfiguredController =>
            configuredController;
        public Transform WeaponVisualRoot => weaponVisualRoot;
        public Transform Muzzle => muzzle;
        public Transform SupportHandTarget => supportHandTarget;
        public Transform SupportHintTarget => supportHintTarget;
        public string CurrentIKState => aimPresented
            ? (configuredController != null && configuredController.isCrouching
                ? vWeaponIKAdjust.CrouchingAimingState
                : vWeaponIKAdjust.StandingAimingState)
            : (configuredController != null && configuredController.isCrouching
                ? vWeaponIKAdjust.CrouchingState
                : vWeaponIKAdjust.StandingState);
        public vWeaponIKAdjustList ProjectIKAdjustList => projectIKAdjustList;
        public string WeaponCategory => weaponCategory;
        public bool WeaponHeldInLeftHand => weaponHeldInLeftHand;
        public InvectorBowPresentationRig BowPresentationRig => bowPresentationRig;
        public bool HasAuthoredPreviewPose => hasAuthoredPreviewPose;
        public Vector3 PreviewWeaponHandLocalPosition =>
            previewWeaponHandLocalPosition;
        public Vector3 PreviewWeaponHintLocalPosition =>
            previewWeaponHintLocalPosition;
        public Vector3 PreviewWeaponHandLocalEuler =>
            previewWeaponHandLocalEuler;
        public Vector3 PreviewSupportHandLocalPosition =>
            previewSupportHandLocalPosition;
        public Vector3 PreviewSupportHintLocalPosition =>
            previewSupportHintLocalPosition;
        public Vector3 PreviewSupportHandLocalEuler =>
            previewSupportHandLocalEuler;
        public bool HideAuthoredArrowInPreview => hideAuthoredArrowInPreview;
        public int RuntimeHelperCount => 0;
        public int AimRequestCount => aimRequestCount;
        public int AimReleaseCount => aimReleaseCount;
        public int MuzzlePositionRequestCount => muzzlePositionRequestCount;
        public int MuzzlePresentationRequestCount => muzzlePresentationRequestCount;
        public int MuzzleEmissionCount => muzzleEmissionCount;
        public int VisibilityRequestCount => visibilityRequestCount;
        public int RespawnResetCount => respawnResetCount;
        public int GatedLateUpdateCount => gatedLateUpdateCount;
        public int AppliedIKPassCount => appliedIKPassCount;
        public int SuppressedIKPassCount => suppressedIKPassCount;
        public int SupportHandSuppressionCount => supportHandSuppressionCount;
        public int InvalidPoseCount => invalidPoseCount;
        public int DroppedRequestCount => droppedRequestCount;
        public int RuntimeFaultCount => runtimeFaultCount;
        public int GateEnableFailureCount => gateEnableFailureCount;
        public int PreviewPoseApplyCount => previewPoseApplyCount;
        public float LastPreviewWeaponHandDistance =>
            lastPreviewWeaponHandDistance;
        public float LastPreviewSupportHandDistance =>
            lastPreviewSupportHandDistance;
        public InvectorWeaponPresentationSuppression LastSuppression => lastSuppression;
        public string LastFaultType => lastFaultType;
        public string LastInvalidPoseStage =>
            lastSuppression == InvectorWeaponPresentationSuppression.InvalidIKPose
                ? lastInvalidPoseStage
                : string.Empty;

        /// <summary>
        /// Exposes the exact support-arm pose selected for the current Invector
        /// weapon state so editor probes can distinguish an authored handle
        /// problem from a solver failure without mutating runtime state.
        /// </summary>
        public bool TryGetCurrentSupportIKPose(
            out Vector3 targetPosition,
            out Quaternion targetRotation,
            out Vector3 hintPosition)
        {
            targetPosition = Vector3.zero;
            targetRotation = Quaternion.identity;
            hintPosition = Vector3.zero;

            IKAdjust adjust = ResolveCurrentIKAdjust();
            if (adjust == null || adjust.supportHandOffset == null ||
                adjust.supportHintOffset == null || supportHandTarget == null ||
                supportHintTarget == null || projectIKAdjustList == null)
                return false;

            Vector3 globalPositionOffset = weaponHeldInLeftHand
                ? projectIKAdjustList.ikTargetPositionOffsetR
                : projectIKAdjustList.ikTargetPositionOffsetL;
            Vector3 globalRotationOffset = weaponHeldInLeftHand
                ? projectIKAdjustList.ikTargetRotationOffsetR
                : projectIKAdjustList.ikTargetRotationOffsetL;
            targetPosition = supportHandTarget.TransformPoint(
                globalPositionOffset + adjust.supportHandOffset.position);
            targetRotation = supportHandTarget.rotation *
                Quaternion.Euler(globalRotationOffset) *
                Quaternion.Euler(adjust.supportHandOffset.eulerAngles);
            hintPosition = supportHintTarget.TransformPoint(
                adjust.supportHintOffset.position);
            return IsFinite(targetPosition) && IsFinite(targetRotation) &&
                   IsFinite(hintPosition);
        }

        /// <summary>
        /// Builder-facing configuration. The caller supplies the complete
        /// project-owned visual hierarchy and IK data without activating it.
        /// </summary>
        public void Configure(
            Animator animator,
            BrawlInvectorThirdPersonController controller,
            Transform visualRoot,
            Transform muzzleTransform,
            Transform supportTarget,
            Transform supportHint,
            vWeaponIKAdjustList ikAdjustList,
            string category,
            bool heldInLeftHand,
            ParticleSystem[] effects)
        {
            ValidateConfigurationArguments(
                animator, controller, visualRoot, muzzleTransform,
                supportTarget, supportHint, ikAdjustList, category, effects);

            configuredAnimator = animator;
            configuredController = controller;
            configuredInput = controller.GetComponent<InvectorShooterMeleeInputAdapter>();
            if (configuredInput == null)
            {
                throw new ArgumentException(
                    "The weapon presenter requires the same-root Invector input adapter.",
                    nameof(controller));
            }
            weaponVisualRoot = visualRoot;
            muzzle = muzzleTransform;
            supportHandTarget = supportTarget;
            supportHintTarget = supportHint;
            projectIKAdjustList = ikAdjustList;
            weaponCategory = category;
            weaponHeldInLeftHand = heldInLeftHand;
            muzzleEffects = (ParticleSystem[])effects.Clone();
            bowPresentationRig = GetComponent<InvectorBowPresentationRig>();
            CaptureMuzzleEffectLocalPoses();
            if (!HasCompleteIKData())
            {
                throw new ArgumentException(
                    "The configured project IK-adjust list must contain all four weapon pose states.",
                    nameof(ikAdjustList));
            }
            if (bowPresentationRig != null && !bowPresentationRig.IsConfigured)
            {
                throw new ArgumentException(
                    "The optional bow-presentation rig must be completely configured before the weapon presenter.");
            }

            DisableRuntime();
            visible = true;
            ApplyVisibility(true);
            ResetRuntimeTrace();
        }

        /// <summary>
        /// Builder-owned menu pose, expressed in character-root space. It is
        /// consumed only by EnablePreview and cannot affect the production gate.
        /// </summary>
        public void ConfigurePreviewPose(
            Vector3 weaponHandLocalPosition,
            Vector3 weaponHintLocalPosition,
            Vector3 weaponHandLocalEuler,
            Vector3 supportHandLocalPosition,
            Vector3 supportHintLocalPosition,
            Vector3 supportHandLocalEuler,
            bool hideArrow)
        {
            if (!IsFinite(weaponHandLocalPosition) ||
                !IsFinite(weaponHintLocalPosition) ||
                !IsFinite(weaponHandLocalEuler) ||
                !IsFinite(supportHandLocalPosition) ||
                !IsFinite(supportHintLocalPosition) ||
                !IsFinite(supportHandLocalEuler))
            {
                throw new ArgumentException("The authored preview pose must be finite.");
            }

            previewWeaponHandLocalPosition = weaponHandLocalPosition;
            previewWeaponHintLocalPosition = weaponHintLocalPosition;
            previewWeaponHandLocalEuler = weaponHandLocalEuler;
            previewSupportHandLocalPosition = supportHandLocalPosition;
            previewSupportHintLocalPosition = supportHintLocalPosition;
            previewSupportHandLocalEuler = supportHandLocalEuler;
            hideAuthoredArrowInPreview = hideArrow;
            hasAuthoredPreviewPose = true;
        }

        /// <summary>
        /// Opens the presenter only for an active, project-owned Play-mode stack. Failure
        /// is reported through diagnostics and leaves the component dormant.
        /// </summary>
        public bool EnableRuntime()
        {
            if (!Application.isPlaying || !IsConfigured ||
                configuredAnimator == null || !configuredAnimator.enabled ||
                !configuredAnimator.isActiveAndEnabled ||
                !configuredAnimator.isHuman ||
                configuredAnimator.runtimeAnimatorController == null ||
                !TryCreateSolvers())
            {
                gateEnableFailureCount++;
                FailClosed(InvectorWeaponPresentationSuppression.AnimatorUnavailable, null);
                return false;
            }

            if (bowPresentationRig != null && !bowPresentationRig.EnableRuntime())
            {
                gateEnableFailureCount++;
                FailClosed(InvectorWeaponPresentationSuppression.AnimatorUnavailable, null);
                return false;
            }

            runtimeEnabled = true;
            previewEnabled = false;
            enabled = true;
            lastSuppression = InvectorWeaponPresentationSuppression.None;
            lastFaultType = string.Empty;
            return true;
        }

        public void DisableRuntime()
        {
            runtimeEnabled = false;
            previewEnabled = false;
            if (bowPresentationRig != null) bowPresentationRig.DisableRuntime();
            ResetPresentationState(true);
            ReleaseSolvers();
            if (enabled) enabled = false;
        }

        /// <summary>
        /// Opens only the project-owned pose presenter for a quarantined menu or
        /// editor preview. Gameplay requests remain gated by RuntimeEnabled, the
        /// bow scheduler stays dormant, and lifecycle states still release IK so
        /// the shared death/respawn/victory animation contract remains visible.
        /// </summary>
        public bool EnablePreview()
        {
            if (!IsConfigured || runtimeEnabled ||
                configuredAnimator == null || !configuredAnimator.enabled ||
                !configuredAnimator.isActiveAndEnabled ||
                !configuredAnimator.isHuman ||
                configuredAnimator.runtimeAnimatorController == null ||
                configuredController == null || configuredController.enabled ||
                configuredInput == null || configuredInput.enabled ||
                !TryCreateSolvers())
            {
                DisablePreview();
                return false;
            }

            previewEnabled = true;
            enabled = true;
            visible = true;
            ApplyVisibility(true);
            if (hideAuthoredArrowInPreview && bowPresentationRig != null &&
                bowPresentationRig.ArrowVisual != null)
            {
                previewArrowWasActive =
                    bowPresentationRig.ArrowVisual.gameObject.activeSelf;
                bowPresentationRig.ArrowVisual.gameObject.SetActive(false);
            }
            lastSuppression = InvectorWeaponPresentationSuppression.None;
            lastFaultType = string.Empty;
            return true;
        }

        public void DisablePreview()
        {
            previewEnabled = false;
            RestorePreviewArrow();
            ResetPresentationState(true);
            ReleaseSolvers();
            if (!runtimeEnabled && enabled) enabled = false;
        }

        /// <summary>Compatibility alias for the isolated migration lab.</summary>
        public bool EnableLabRuntime() => EnableRuntime();

        /// <summary>Compatibility alias for the isolated migration lab.</summary>
        public void DisableLabRuntime() => DisableRuntime();

        public void ResetRuntimeTrace()
        {
            if (runtimeEnabled || previewEnabled)
            {
                throw new InvalidOperationException(
                    "Disable the weapon-presentation runtime and preview gates before resetting its trace.");
            }

            aimRequestCount = 0;
            aimReleaseCount = 0;
            muzzlePositionRequestCount = 0;
            muzzlePresentationRequestCount = 0;
            muzzleEmissionCount = 0;
            visibilityRequestCount = 0;
            respawnResetCount = 0;
            gatedLateUpdateCount = 0;
            appliedIKPassCount = 0;
            suppressedIKPassCount = 0;
            supportHandSuppressionCount = 0;
            invalidPoseCount = 0;
            droppedRequestCount = 0;
            runtimeFaultCount = 0;
            gateEnableFailureCount = 0;
            previewPoseApplyCount = 0;
            lastPreviewWeaponHandDistance = -1f;
            lastPreviewSupportHandDistance = -1f;
            lastSuppression = InvectorWeaponPresentationSuppression.None;
            lastFaultType = string.Empty;
            if (bowPresentationRig != null) bowPresentationRig.ResetRuntimeTrace();
        }

        public void PresentAim(Vector3 worldDirection)
        {
            aimRequestCount++;
            if (!runtimeEnabled || !isActiveAndEnabled)
            {
                droppedRequestCount++;
                lastSuppression = InvectorWeaponPresentationSuppression.GateClosed;
                return;
            }

            if (!IsFinite(worldDirection))
            {
                invalidPoseCount++;
                ReleaseAim();
                return;
            }

            worldDirection.y = 0f;
            if (worldDirection.sqrMagnitude <= 0.000001f)
            {
                ReleaseAim();
                return;
            }

            presentedAimDirection = worldDirection.normalized;
            aimPresented = true;
            if (bowPresentationRig != null)
                bowPresentationRig.PresentAim(presentedAimDirection);
        }

        public bool TryGetMuzzlePosition(out Vector3 worldPosition)
        {
            muzzlePositionRequestCount++;
            worldPosition = Vector3.zero;
            if (!runtimeEnabled || !isActiveAndEnabled || muzzle == null ||
                !IsFinite(muzzle.position))
            {
                droppedRequestCount++;
                return false;
            }

            worldPosition = muzzle.position;
            return true;
        }

        public void PresentMuzzle(Vector3 worldPosition, Vector3 worldDirection)
        {
            muzzlePresentationRequestCount++;
            if (!runtimeEnabled || !isActiveAndEnabled || !visible)
            {
                droppedRequestCount++;
                return;
            }
            if (!IsFinite(worldPosition) || !IsFinite(worldDirection))
            {
                invalidPoseCount++;
                droppedRequestCount++;
                return;
            }

            if (worldDirection.sqrMagnitude <= 0.000001f)
            {
                worldDirection = muzzle != null ? muzzle.forward : transform.forward;
            }
            if (!IsFinite(worldDirection) || worldDirection.sqrMagnitude <= 0.000001f)
            {
                invalidPoseCount++;
                droppedRequestCount++;
                return;
            }

            try
            {
                if (!TryLookRotation(worldDirection, transform.up, out Quaternion rotation))
                {
                    invalidPoseCount++;
                    droppedRequestCount++;
                    return;
                }
                if (bowPresentationRig != null)
                    bowPresentationRig.PresentRelease(worldPosition, worldDirection);
                bool emitted = false;
                for (int i = 0; i < muzzleEffects.Length; i++)
                {
                    ParticleSystem effect = muzzleEffects[i];
                    if (effect == null) continue;
                    effect.transform.SetPositionAndRotation(worldPosition, rotation);
                    effect.Play(true);
                    emitted = true;
                }
                if (emitted) muzzleEmissionCount++;
            }
            catch (Exception exception)
            {
                FailClosed(InvectorWeaponPresentationSuppression.RuntimeFault, exception);
            }
        }

        public void SetVisible(bool value)
        {
            visibilityRequestCount++;
            if (!runtimeEnabled || !isActiveAndEnabled)
            {
                droppedRequestCount++;
                return;
            }

            try
            {
                visible = value;
                if (bowPresentationRig != null)
                    bowPresentationRig.SetVisible(value);
                ApplyVisibility(value);
                if (!value) ReleaseAim();
            }
            catch (Exception exception)
            {
                FailClosed(InvectorWeaponPresentationSuppression.RuntimeFault, exception);
            }
        }

        public void ResetForRespawn()
        {
            respawnResetCount++;
            try
            {
                ResetPresentationState(true);
                lastSuppression = InvectorWeaponPresentationSuppression.None;
            }
            catch (Exception exception)
            {
                FailClosed(InvectorWeaponPresentationSuppression.RuntimeFault, exception);
            }
        }

        void LateUpdate()
        {
            if (!runtimeEnabled && !previewEnabled) return;
            gatedLateUpdateCount++;

            ArmSnapshot snapshot = default;
            bool hasSnapshot = false;
            try
            {
                InvectorWeaponPresentationSuppression suppression = previewEnabled
                    ? EvaluatePreviewIKSuppression()
                    : EvaluateFullIKSuppression();
                if (suppression != InvectorWeaponPresentationSuppression.None)
                {
                    lastInvalidPoseStage = string.Empty;
                    SuppressIK(suppression);
                    if (suppression == InvectorWeaponPresentationSuppression.LifecycleState &&
                        runtimeEnabled)
                        ReleaseAim();
                    if (previewEnabled) ApplyPreviewArrowPosture();
                    return;
                }

                IKAdjust adjust = ResolveCurrentIKAdjust();
                if (adjust == null || !TryCreateSolvers())
                {
                    lastInvalidPoseStage = string.Empty;
                    SuppressIK(InvectorWeaponPresentationSuppression.MissingIKData);
                    return;
                }

                snapshot = new ArmSnapshot(weaponHandSolver, supportHandSolver);
                hasSnapshot = true;

                if (previewEnabled && hasAuthoredPreviewPose)
                {
                    if (!TryApplyAuthoredPreviewPose())
                    {
                        lastInvalidPoseStage = "PreviewPose";
                        snapshot.Restore();
                        invalidPoseCount++;
                        SuppressIK(InvectorWeaponPresentationSuppression.InvalidIKPose);
                        return;
                    }
                    lastSuppression = InvectorWeaponPresentationSuppression.None;
                }
                else if (weaponHeldInLeftHand)
                {
                    weaponHandSolver.SetIKWeight(0f);
                }
                else if (!TryApplyWeaponHandIK(adjust))
                {
                    lastInvalidPoseStage = "WeaponHand";
                    snapshot.Restore();
                    invalidPoseCount++;
                    SuppressIK(InvectorWeaponPresentationSuppression.InvalidIKPose);
                    return;
                }

                if (previewEnabled && hasAuthoredPreviewPose)
                {
                    lastSuppression = InvectorWeaponPresentationSuppression.None;
                }
                else if (HasAnimatorTag(IgnoreSupportHandIKTag))
                {
                    lastInvalidPoseStage = string.Empty;
                    supportHandSolver.SetIKWeight(0f);
                    supportHandSuppressionCount++;
                    lastSuppression =
                        InvectorWeaponPresentationSuppression.IgnoreSupportHandIKTag;
                }
                else if (!TryApplySupportHandIK(adjust))
                {
                    lastInvalidPoseStage = "SupportHand";
                    snapshot.Restore();
                    invalidPoseCount++;
                    SuppressIK(InvectorWeaponPresentationSuppression.InvalidIKPose);
                    return;
                }
                else
                {
                    lastSuppression = InvectorWeaponPresentationSuppression.None;
                }

                if (!snapshot.HasFiniteCurrentRotations())
                {
                    lastInvalidPoseStage = "PostSolveRotations";
                    snapshot.Restore();
                    invalidPoseCount++;
                    SuppressIK(InvectorWeaponPresentationSuppression.InvalidIKPose);
                    return;
                }

                lastInvalidPoseStage = string.Empty;
                appliedIKPassCount++;
            }
            catch (Exception exception)
            {
                if (hasSnapshot) snapshot.Restore();
                FailClosed(InvectorWeaponPresentationSuppression.RuntimeFault, exception);
            }
        }

        void OnDisable()
        {
            if (tearingDown) return;
            runtimeEnabled = false;
            previewEnabled = false;
            TeardownWithoutThrow();
        }

        void OnDestroy()
        {
            runtimeEnabled = false;
            previewEnabled = false;
            TeardownWithoutThrow();
        }

        bool TryCreateSolvers()
        {
            if (weaponHandSolver != null && supportHandSolver != null)
                return HasSolverBones(weaponHandSolver) && HasSolverBones(supportHandSolver);
            if (configuredAnimator == null || !configuredAnimator.isHuman) return false;

            HumanBodyBones weaponUpper = weaponHeldInLeftHand
                ? HumanBodyBones.LeftUpperArm : HumanBodyBones.RightUpperArm;
            HumanBodyBones weaponLower = weaponHeldInLeftHand
                ? HumanBodyBones.LeftLowerArm : HumanBodyBones.RightLowerArm;
            HumanBodyBones weaponHand = weaponHeldInLeftHand
                ? HumanBodyBones.LeftHand : HumanBodyBones.RightHand;
            HumanBodyBones supportUpper = weaponHeldInLeftHand
                ? HumanBodyBones.RightUpperArm : HumanBodyBones.LeftUpperArm;
            HumanBodyBones supportLower = weaponHeldInLeftHand
                ? HumanBodyBones.RightLowerArm : HumanBodyBones.LeftLowerArm;
            HumanBodyBones supportHand = weaponHeldInLeftHand
                ? HumanBodyBones.RightHand : HumanBodyBones.LeftHand;

            Transform weaponUpperBone = configuredAnimator.GetBoneTransform(weaponUpper);
            Transform weaponLowerBone = configuredAnimator.GetBoneTransform(weaponLower);
            Transform weaponHandBone = configuredAnimator.GetBoneTransform(weaponHand);
            Transform supportUpperBone = configuredAnimator.GetBoneTransform(supportUpper);
            Transform supportLowerBone = configuredAnimator.GetBoneTransform(supportLower);
            Transform supportHandBone = configuredAnimator.GetBoneTransform(supportHand);
            if (weaponUpperBone == null || weaponLowerBone == null || weaponHandBone == null ||
                supportUpperBone == null || supportLowerBone == null || supportHandBone == null)
                return false;

            // The explicit-bone constructor creates no hidden helper objects.
            // Solvers are rebuilt after each gate close, so no stale hint can survive.
            weaponHandSolver = new vIKSolver(
                transform, weaponUpperBone, weaponLowerBone, weaponHandBone);
            supportHandSolver = new vIKSolver(
                transform, supportUpperBone, supportLowerBone, supportHandBone);
            return HasSolverBones(weaponHandSolver) && HasSolverBones(supportHandSolver);
        }

        bool TryApplyWeaponHandIK(IKAdjust adjust)
        {
            if (adjust.weaponHandOffset == null || adjust.weaponHintOffset == null)
                return false;

            Transform hand = weaponHandSolver.endBone;
            Transform hint = weaponHandSolver.middleBone;
            Vector3 targetPosition =
                hand.TransformPoint(adjust.weaponHandOffset.position);
            Quaternion targetRotation = hand.rotation *
                Quaternion.Euler(adjust.weaponHandOffset.eulerAngles);
            Vector3 hintPosition =
                hint.TransformPoint(adjust.weaponHintOffset.position);

            return ApplySolverPose(
                weaponHandSolver, targetPosition, targetRotation, hintPosition);
        }

        bool TryApplyAuthoredPreviewPose()
        {
            Vector3 weaponTarget =
                transform.TransformPoint(previewWeaponHandLocalPosition);
            Vector3 supportTarget =
                transform.TransformPoint(previewSupportHandLocalPosition);
            bool weaponApplied = ApplySolverPose(
                weaponHandSolver,
                weaponTarget,
                transform.rotation * Quaternion.Euler(previewWeaponHandLocalEuler),
                transform.TransformPoint(previewWeaponHintLocalPosition));
            bool supportApplied = ApplySolverPose(
                supportHandSolver,
                supportTarget,
                transform.rotation * Quaternion.Euler(previewSupportHandLocalEuler),
                transform.TransformPoint(previewSupportHintLocalPosition));
            if (!weaponApplied || !supportApplied) return false;

            lastPreviewWeaponHandDistance = Vector3.Distance(
                weaponHandSolver.endBone.position, weaponTarget);
            lastPreviewSupportHandDistance = Vector3.Distance(
                supportHandSolver.endBone.position, supportTarget);
            if (!IsFinite(lastPreviewWeaponHandDistance) ||
                !IsFinite(lastPreviewSupportHandDistance))
                return false;
            previewPoseApplyCount++;
            return true;
        }

        bool TryApplySupportHandIK(IKAdjust adjust)
        {
            if (adjust.supportHandOffset == null || adjust.supportHintOffset == null ||
                supportHandTarget == null || supportHintTarget == null)
                return false;

            Vector3 globalPositionOffset = weaponHeldInLeftHand
                ? projectIKAdjustList.ikTargetPositionOffsetR
                : projectIKAdjustList.ikTargetPositionOffsetL;
            Vector3 globalRotationOffset = weaponHeldInLeftHand
                ? projectIKAdjustList.ikTargetRotationOffsetR
                : projectIKAdjustList.ikTargetRotationOffsetL;
            Vector3 targetPosition = supportHandTarget.TransformPoint(
                globalPositionOffset + adjust.supportHandOffset.position);
            Quaternion targetRotation = supportHandTarget.rotation *
                Quaternion.Euler(globalRotationOffset) *
                Quaternion.Euler(adjust.supportHandOffset.eulerAngles);
            Vector3 hintPosition = supportHintTarget.TransformPoint(
                adjust.supportHintOffset.position);

            return ApplySolverPose(
                supportHandSolver, targetPosition, targetRotation, hintPosition);
        }

        bool ApplySolverPose(
            vIKSolver solver,
            Vector3 targetPosition,
            Quaternion targetRotation,
            Vector3 hintPosition)
        {
            if (!HasSolverBones(solver) ||
                !IsFinite(targetPosition) || !IsFinite(targetRotation) ||
                !IsFinite(hintPosition) ||
                !IsReachable(solver, targetPosition, hintPosition))
                return false;

            solver.SetIKWeight(Mathf.Clamp01(ikWeight));
            solver.SetIKHintPosition(hintPosition);
            solver.SetIKPosition(targetPosition);
            solver.SetIKRotation(targetRotation);
            return IsFinite(solver.rootBone.rotation) &&
                   IsFinite(solver.middleBone.rotation) &&
                   IsFinite(solver.endBone.rotation);
        }

        static bool IsReachable(
            vIKSolver solver, Vector3 targetPosition, Vector3 hintPosition)
        {
            Vector3 rootPosition = solver.rootBone.position;
            float upperLength = Vector3.Distance(
                solver.rootBone.position, solver.middleBone.position);
            float lowerLength = Vector3.Distance(
                solver.middleBone.position, solver.endBone.position);
            if (!IsFinite(upperLength) || !IsFinite(lowerLength) ||
                upperLength <= MinimumBoneLength || lowerLength <= MinimumBoneLength)
                return false;

            Vector3 reach = targetPosition - rootPosition;
            float distance = reach.magnitude;
            float minimum = Mathf.Abs(upperLength - lowerLength) + ReachSkin;
            float maximum = upperLength + lowerLength - ReachSkin;
            if (!IsFinite(distance) || distance <= minimum || distance >= maximum)
                return false;

            Vector3 hintFromRoot = hintPosition - rootPosition;
            Vector3 lateralHint = Vector3.ProjectOnPlane(
                hintFromRoot, reach / distance);
            return IsFinite(lateralHint) &&
                   lateralHint.sqrMagnitude >=
                   MinimumHintSeparation * MinimumHintSeparation;
        }

        InvectorWeaponPresentationSuppression EvaluateFullIKSuppression()
        {
            if (!runtimeEnabled)
                return InvectorWeaponPresentationSuppression.GateClosed;
            if (configuredAnimator == null || !configuredAnimator.enabled ||
                !configuredAnimator.isActiveAndEnabled ||
                !configuredAnimator.isInitialized || !configuredAnimator.isHuman)
                return InvectorWeaponPresentationSuppression.AnimatorUnavailable;
            if (!visible || weaponVisualRoot == null ||
                !weaponVisualRoot.gameObject.activeInHierarchy)
                return InvectorWeaponPresentationSuppression.VisualHidden;
            if (configuredController.HasPendingLifecyclePresentationTrigger)
                return InvectorWeaponPresentationSuppression.LifecycleState;
            if (IsLifecycleState())
                return InvectorWeaponPresentationSuppression.LifecycleState;
            if (configuredController.HasPendingMeleePresentationTrigger ||
                configuredInput.IsPresentationAttackWindowOpen)
                return InvectorWeaponPresentationSuppression.IgnoreIKTag;
            return InvectorWeaponPresentationSuppression.None;
        }

        InvectorWeaponPresentationSuppression EvaluatePreviewIKSuppression()
        {
            if (!previewEnabled)
                return InvectorWeaponPresentationSuppression.GateClosed;
            if (configuredAnimator == null || !configuredAnimator.enabled ||
                !configuredAnimator.isActiveAndEnabled ||
                !configuredAnimator.isInitialized || !configuredAnimator.isHuman)
                return InvectorWeaponPresentationSuppression.AnimatorUnavailable;
            if (!visible || weaponVisualRoot == null ||
                !weaponVisualRoot.gameObject.activeInHierarchy)
                return InvectorWeaponPresentationSuppression.VisualHidden;
            if (IsLifecycleState())
                return InvectorWeaponPresentationSuppression.LifecycleState;
            if (HasAnimatorTag(IgnoreIKTag))
                return InvectorWeaponPresentationSuppression.IgnoreIKTag;
            return InvectorWeaponPresentationSuppression.None;
        }

        bool HasAnimatorTag(string tag)
        {
            if (configuredAnimator == null || !configuredAnimator.isInitialized)
                return false;
            int layers = configuredAnimator.layerCount;
            for (int i = 0; i < layers; i++)
            {
                if (configuredAnimator.GetCurrentAnimatorStateInfo(i).IsTag(tag))
                    return true;
                if (configuredAnimator.IsInTransition(i) &&
                    configuredAnimator.GetNextAnimatorStateInfo(i).IsTag(tag))
                    return true;
            }
            return false;
        }

        bool IsLifecycleState()
        {
            int layers = configuredAnimator.layerCount;
            for (int i = 0; i < layers; i++)
            {
                if (IsLifecycleHash(
                        configuredAnimator.GetCurrentAnimatorStateInfo(i).fullPathHash))
                    return true;
                if (configuredAnimator.IsInTransition(i) &&
                    IsLifecycleHash(
                        configuredAnimator.GetNextAnimatorStateInfo(i).fullPathHash))
                    return true;
            }
            return false;
        }

        static bool IsLifecycleHash(int hash)
        {
            return hash == BrawlInvectorLifecycleParameters.DeathState ||
                   hash == BrawlInvectorLifecycleParameters.RespawnState ||
                   hash == BrawlInvectorLifecycleParameters.VictoryState;
        }

        IKAdjust ResolveCurrentIKAdjust()
        {
            if (projectIKAdjustList == null) return null;
            vWeaponIKAdjust weaponIK = projectIKAdjustList.GetWeaponIK(weaponCategory);
            if (weaponIK == null) return null;
            string state = aimPresented
                ? (configuredController.isCrouching
                    ? vWeaponIKAdjust.CrouchingAimingState
                    : vWeaponIKAdjust.StandingAimingState)
                : (configuredController.isCrouching
                    ? vWeaponIKAdjust.CrouchingState
                    : vWeaponIKAdjust.StandingState);
            return weaponIK.GetIKAdjust(state, weaponHeldInLeftHand);
        }

        bool HasCompleteIKData()
        {
            if (projectIKAdjustList == null || string.IsNullOrWhiteSpace(weaponCategory))
                return false;
            try
            {
                vWeaponIKAdjust weaponIK =
                    projectIKAdjustList.GetWeaponIK(weaponCategory);
                if (weaponIK == null) return false;
                for (int i = 0; i < vWeaponIKAdjust.defaultNames.Length; i++)
                {
                    IKAdjust adjust = weaponIK.GetIKAdjust(
                        vWeaponIKAdjust.defaultNames[i], weaponHeldInLeftHand);
                    if (!IsFinite(adjust)) return false;
                }
                return true;
            }
            catch
            {
                return false;
            }
        }

        void SuppressIK(InvectorWeaponPresentationSuppression suppression)
        {
            if (weaponHandSolver != null) weaponHandSolver.SetIKWeight(0f);
            if (supportHandSolver != null) supportHandSolver.SetIKWeight(0f);
            suppressedIKPassCount++;
            lastSuppression = suppression;
        }

        void ReleaseAim()
        {
            if (aimPresented) aimReleaseCount++;
            aimPresented = false;
            presentedAimDirection = Vector3.zero;
            if (bowPresentationRig != null) bowPresentationRig.ReleaseAim();
            if (weaponHandSolver != null) weaponHandSolver.SetIKWeight(0f);
            if (supportHandSolver != null) supportHandSolver.SetIKWeight(0f);
        }

        void ResetPresentationState(bool restoreVisibility)
        {
            ReleaseAim();
            StopAndRestoreMuzzleEffects();
            if (bowPresentationRig != null) bowPresentationRig.ResetForRespawn();
            if (!restoreVisibility) return;
            visible = true;
            ApplyVisibility(true);
        }

        void ReleaseSolvers()
        {
            if (weaponHandSolver != null) weaponHandSolver.SetIKWeight(0f);
            if (supportHandSolver != null) supportHandSolver.SetIKWeight(0f);
            // Explicit-bone vIKSolver instances own no hidden GameObjects.
            weaponHandSolver = null;
            supportHandSolver = null;
        }

        void RestorePreviewArrow()
        {
            if (hideAuthoredArrowInPreview && bowPresentationRig != null &&
                bowPresentationRig.ArrowVisual != null)
            {
                bowPresentationRig.ArrowVisual.gameObject.SetActive(
                    previewArrowWasActive);
            }
            previewArrowWasActive = false;
        }

        void ApplyPreviewArrowPosture()
        {
            if (previewEnabled && hideAuthoredArrowInPreview &&
                bowPresentationRig != null &&
                bowPresentationRig.ArrowVisual != null)
            {
                bowPresentationRig.ArrowVisual.gameObject.SetActive(false);
            }
        }

        void TeardownWithoutThrow()
        {
            try
            {
                ResetPresentationState(true);
            }
            catch
            {
                aimPresented = false;
                presentedAimDirection = Vector3.zero;
                visible = true;
            }
            if (bowPresentationRig != null) bowPresentationRig.DisableRuntime();
            ReleaseSolvers();
        }

        void FailClosed(
            InvectorWeaponPresentationSuppression suppression,
            Exception exception)
        {
            if (exception != null)
            {
                runtimeFaultCount++;
                lastFaultType = exception.GetType().Name;
            }
            lastSuppression = suppression;
            runtimeEnabled = false;
            previewEnabled = false;

            tearingDown = true;
            try
            {
                ResetPresentationState(true);
                if (bowPresentationRig != null) bowPresentationRig.DisableRuntime();
                ReleaseSolvers();
                if (enabled) enabled = false;
            }
            catch
            {
                weaponHandSolver = null;
                supportHandSolver = null;
                aimPresented = false;
                presentedAimDirection = Vector3.zero;
            }
            finally
            {
                try
                {
                    RestorePreviewArrow();
                }
                catch
                {
                    previewArrowWasActive = false;
                }
                tearingDown = false;
            }
        }

        void CaptureMuzzleEffectLocalPoses()
        {
            muzzleEffectLocalPositions = new Vector3[muzzleEffects.Length];
            muzzleEffectLocalRotations = new Quaternion[muzzleEffects.Length];
            for (int i = 0; i < muzzleEffects.Length; i++)
            {
                ParticleSystem effect = muzzleEffects[i];
                if (effect == null) continue;
                muzzleEffectLocalPositions[i] = effect.transform.localPosition;
                muzzleEffectLocalRotations[i] = effect.transform.localRotation;
            }
        }

        void StopAndRestoreMuzzleEffects()
        {
            int count = Mathf.Min(
                muzzleEffects != null ? muzzleEffects.Length : 0,
                Mathf.Min(
                    muzzleEffectLocalPositions != null
                        ? muzzleEffectLocalPositions.Length : 0,
                    muzzleEffectLocalRotations != null
                        ? muzzleEffectLocalRotations.Length : 0));
            for (int i = 0; i < count; i++)
            {
                ParticleSystem effect = muzzleEffects[i];
                if (effect == null) continue;
                effect.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
                effect.transform.localPosition = muzzleEffectLocalPositions[i];
                effect.transform.localRotation = muzzleEffectLocalRotations[i];
            }
        }

        void ApplyVisibility(bool value)
        {
            if (weaponVisualRoot != null &&
                weaponVisualRoot.gameObject.activeSelf != value)
            {
                weaponVisualRoot.gameObject.SetActive(value);
            }
        }

        bool HasConfiguredReferences =>
            configuredAnimator != null && configuredController != null &&
            configuredInput != null &&
            configuredAnimator.gameObject == gameObject &&
            configuredController.gameObject == gameObject &&
            configuredInput.gameObject == gameObject &&
            weaponVisualRoot != null && weaponVisualRoot.IsChildOf(transform) &&
            muzzle != null && muzzle.IsChildOf(weaponVisualRoot) &&
            supportHandTarget != null && supportHandTarget.IsChildOf(weaponVisualRoot) &&
            supportHintTarget != null && supportHintTarget.IsChildOf(weaponVisualRoot) &&
            projectIKAdjustList != null && !string.IsNullOrWhiteSpace(weaponCategory) &&
            muzzleEffects != null &&
            (bowPresentationRig == null || bowPresentationRig.IsConfigured);

        void ValidateConfigurationArguments(
            Animator animator,
            BrawlInvectorThirdPersonController controller,
            Transform visualRoot,
            Transform muzzleTransform,
            Transform supportTarget,
            Transform supportHint,
            vWeaponIKAdjustList ikAdjustList,
            string category,
            ParticleSystem[] effects)
        {
            if (animator == null) throw new ArgumentNullException(nameof(animator));
            if (controller == null) throw new ArgumentNullException(nameof(controller));
            if (visualRoot == null) throw new ArgumentNullException(nameof(visualRoot));
            if (muzzleTransform == null) throw new ArgumentNullException(nameof(muzzleTransform));
            if (supportTarget == null) throw new ArgumentNullException(nameof(supportTarget));
            if (supportHint == null) throw new ArgumentNullException(nameof(supportHint));
            if (ikAdjustList == null) throw new ArgumentNullException(nameof(ikAdjustList));
            if (effects == null) throw new ArgumentNullException(nameof(effects));
            if (string.IsNullOrWhiteSpace(category))
                throw new ArgumentException("Weapon category is required.", nameof(category));
            if (animator.gameObject != gameObject || controller.gameObject != gameObject)
                throw new ArgumentException(
                    "The presenter, Animator, and project controller must share one root.");
            if (visualRoot == transform || !visualRoot.IsChildOf(transform))
                throw new ArgumentException(
                    "The weapon visual must be a dedicated child hierarchy.", nameof(visualRoot));
            if (!muzzleTransform.IsChildOf(visualRoot) ||
                !supportTarget.IsChildOf(visualRoot) ||
                !supportHint.IsChildOf(visualRoot))
                throw new ArgumentException(
                    "Muzzle and support targets must belong to the weapon visual hierarchy.");
            for (int i = 0; i < effects.Length; i++)
            {
                if (effects[i] == null || !effects[i].transform.IsChildOf(visualRoot))
                    throw new ArgumentException(
                        "Every muzzle effect must be a non-null child of the weapon visual.",
                        nameof(effects));
            }
        }

        static bool HasSolverBones(vIKSolver solver)
        {
            return solver != null && solver.rootBone != null &&
                   solver.middleBone != null && solver.endBone != null;
        }

        static bool IsFinite(IKAdjust adjust)
        {
            return adjust != null &&
                   IsFinite(adjust.weaponHandOffset) &&
                   IsFinite(adjust.weaponHintOffset) &&
                   IsFinite(adjust.supportHandOffset) &&
                   IsFinite(adjust.supportHintOffset);
        }

        static bool IsFinite(IKOffsetTransform offset)
        {
            return offset != null && IsFinite(offset.position) &&
                   IsFinite(offset.eulerAngles);
        }

        static bool IsFinite(Vector3 value)
        {
            return IsFinite(value.x) && IsFinite(value.y) && IsFinite(value.z);
        }

        static bool IsFinite(Quaternion value)
        {
            if (!IsFinite(value.x) || !IsFinite(value.y) ||
                !IsFinite(value.z) || !IsFinite(value.w)) return false;
            float magnitude = value.x * value.x + value.y * value.y +
                              value.z * value.z + value.w * value.w;
            return IsFinite(magnitude) && magnitude > 0.000001f;
        }

        static bool IsFinite(float value)
        {
            return !float.IsNaN(value) && !float.IsInfinity(value);
        }

        static bool TryLookRotation(
            Vector3 forward, Vector3 preferredUp, out Quaternion rotation)
        {
            rotation = Quaternion.identity;
            if (!IsFinite(forward) || forward.sqrMagnitude <= 0.000001f)
                return false;
            forward.Normalize();

            Vector3 up = IsFinite(preferredUp) ? preferredUp : Vector3.up;
            if (up.sqrMagnitude <= 0.000001f) up = Vector3.up;
            up.Normalize();
            if (Vector3.Cross(forward, up).sqrMagnitude <= 0.000001f)
            {
                up = Mathf.Abs(Vector3.Dot(forward, Vector3.up)) < 0.99f
                    ? Vector3.up : Vector3.forward;
            }

            rotation = Quaternion.LookRotation(forward, up);
            return IsFinite(rotation);
        }

        readonly struct ArmSnapshot
        {
            readonly Transform weaponUpper;
            readonly Transform weaponLower;
            readonly Transform weaponHand;
            readonly Transform supportUpper;
            readonly Transform supportLower;
            readonly Transform supportHand;
            readonly Quaternion weaponUpperRotation;
            readonly Quaternion weaponLowerRotation;
            readonly Quaternion weaponHandRotation;
            readonly Quaternion supportUpperRotation;
            readonly Quaternion supportLowerRotation;
            readonly Quaternion supportHandRotation;

            public ArmSnapshot(vIKSolver weapon, vIKSolver support)
            {
                weaponUpper = weapon.rootBone;
                weaponLower = weapon.middleBone;
                weaponHand = weapon.endBone;
                supportUpper = support.rootBone;
                supportLower = support.middleBone;
                supportHand = support.endBone;
                weaponUpperRotation = weaponUpper.rotation;
                weaponLowerRotation = weaponLower.rotation;
                weaponHandRotation = weaponHand.rotation;
                supportUpperRotation = supportUpper.rotation;
                supportLowerRotation = supportLower.rotation;
                supportHandRotation = supportHand.rotation;
            }

            public void Restore()
            {
                if (weaponUpper != null) weaponUpper.rotation = weaponUpperRotation;
                if (weaponLower != null) weaponLower.rotation = weaponLowerRotation;
                if (weaponHand != null) weaponHand.rotation = weaponHandRotation;
                if (supportUpper != null) supportUpper.rotation = supportUpperRotation;
                if (supportLower != null) supportLower.rotation = supportLowerRotation;
                if (supportHand != null) supportHand.rotation = supportHandRotation;
            }

            public bool HasFiniteCurrentRotations()
            {
                return weaponUpper != null && weaponLower != null && weaponHand != null &&
                       supportUpper != null && supportLower != null && supportHand != null &&
                       IsFinite(weaponUpper.rotation) && IsFinite(weaponLower.rotation) &&
                       IsFinite(weaponHand.rotation) && IsFinite(supportUpper.rotation) &&
                       IsFinite(supportLower.rotation) && IsFinite(supportHand.rotation);
            }
        }
    }
}
