using System;
using Invector;
using Invector.IK;
using Invector.vShooter;
using UnityEditor;
using UnityEngine;

namespace BrawlArena.EditorAutomation
{
    /// <summary>
    /// Disposable Play Mode adapter that lets Invector's stock IK Adjust
    /// window edit Brawl's project-owned IK assets with its normal Scene
    /// handles. It creates only hidden transform helpers and an inactive,
    /// disabled weapon descriptor; no vendor input, shooter, ammo, projectile,
    /// damage, or equipment authority is opened.
    /// </summary>
    public sealed class BrawlInvectorIKCalibrationBridge :
        vIShooterIKController, IDisposable
    {
        static BrawlInvectorIKCalibrationBridge current;

        GameObject actorObject;
        Transform actorTransform;
        InvectorBrawlerWeaponPresentation presenter;
        Animator animator;
        vIKSolver leftIK;
        vIKSolver rightIK;
        vShooterWeapon descriptor;
        GameObject descriptorObject;
        Transform descriptorHandOffset;
        bool lockAiming;
        bool lockHipFireAiming;
        bool editingGlobalOffset;
        bool disposed;

        public GameObject gameObject => actorObject;
        public vIKSolver LeftIK => leftIK;
        public vIKSolver RightIK => rightIK;
        public vWeaponIKAdjustList WeaponIKAdjustList
        {
            get => presenter != null ? presenter.ProjectIKAdjustList : null;
            set => throw new InvalidOperationException(
                "Brawl calibration uses the presenter-configured IK list.");
        }
        public vWeaponIKAdjust CurrentWeaponIK =>
            WeaponIKAdjustList != null && descriptor != null
                ? WeaponIKAdjustList.GetWeaponIK(descriptor.weaponCategory)
                : null;
        public IKAdjust CurrentIKAdjust => CurrentWeaponIK != null
            ? CurrentWeaponIK.GetIKAdjust(
                CurrentIKAdjustState,
                presenter != null && presenter.WeaponHeldInLeftHand)
            : null;
        public bool LockAiming
        {
            get => lockAiming;
            set
            {
                lockAiming = value;
                if (presenter == null) return;
                presenter.PresentAim(
                    value && actorTransform != null
                        ? actorTransform.forward
                        : Vector3.zero);
                UpdateWeaponIK();
            }
        }
        public bool LockHipFireAiming
        {
            get => lockHipFireAiming;
            set => lockHipFireAiming = value;
        }
        public vShooterWeapon CurrentActiveWeapon => descriptor;
        public bool EditingIKGlobalOffset
        {
            get => editingGlobalOffset;
            set => editingGlobalOffset = value;
        }
        public bool IsAiming => presenter != null && presenter.AimPresented;
        public bool IsCrouching
        {
            get => presenter != null && presenter.ConfiguredController != null &&
                presenter.ConfiguredController.isCrouching;
            set
            {
                if (presenter != null && presenter.ConfiguredController != null)
                    presenter.ConfiguredController.isCrouching = value;
                UpdateWeaponIK();
            }
        }
        public bool IsLeftWeapon =>
            presenter != null && presenter.WeaponHeldInLeftHand;
        public Vector3 AimPosition => actorTransform != null
            ? actorTransform.position + actorTransform.forward * 10f
            : Vector3.zero;
        public string CurrentIKAdjustState => presenter != null
            ? presenter.CurrentIKState : string.Empty;
        public string CurrentIKAdjustStateWithTag => descriptor != null
            ? descriptor.weaponCategory + "@" + CurrentIKAdjustState
            : CurrentIKAdjustState;
        public bool IsUsingCustomIKAdjust => false;
        public bool IsIgnoreIK => false;
        public bool IsSupportHandIKEnabled => true;
        public string CustomIKAdjustState => string.Empty;

        public event IKUpdateEvent onStartUpdateIK;
        public event IKUpdateEvent onFinishUpdateIK;

        public static BrawlInvectorIKCalibrationBridge Bind(
            BrawlerController actor)
        {
            if (!Application.isPlaying)
                throw new InvalidOperationException(
                    "The Invector IK calibration bridge requires Play Mode.");
            if (actor == null)
                throw new ArgumentNullException(nameof(actor));

            current?.Dispose();

            BrawlInvectorIKCalibrationBridge bridge =
                new BrawlInvectorIKCalibrationBridge();
            bridge.Configure(actor);
            current = bridge;

            vShooterIKAdjustWindow.InitEditorWindow();
            vShooterIKAdjustWindow window = vShooterIKAdjustWindow.curWindow;
            if (window == null)
            {
                bridge.Dispose();
                throw new InvalidOperationException(
                    "Invector's stock IK Adjust window did not open.");
            }
            window.ikController = bridge;
            window.selected = null;
            window.referenceSelected = null;
            window.Repaint();
            Selection.activeGameObject = actor.gameObject;
            SceneView.RepaintAll();
            Debug.Log("[BrawlIKCalibration] Bound stock IK window to probe subject " +
                actor.name + ".");
            return bridge;
        }

        [MenuItem("Brawl Arena/Gameplay Probe/Bind Probe Subject To Stock IK Window")]
        static void BindProbeSubjectToStockIKWindow()
        {
            GameplayProbeRecorder recorder =
                UnityEngine.Object.FindFirstObjectByType<GameplayProbeRecorder>();
            if (recorder == null || recorder.subject == null)
                throw new InvalidOperationException(
                    "No active GameplayProbe subject is available to calibrate.");
            Bind(recorder.subject);
        }

        [MenuItem("Brawl Arena/Gameplay Probe/Seat Support Handle In Palm")]
        static void SeatSupportHandleInPalmFromMenu()
        {
            if (!Application.isPlaying)
                throw new InvalidOperationException(
                    "Support-handle calibration requires Play Mode.");

            GameplayProbeRecorder recorder =
                UnityEngine.Object.FindFirstObjectByType<GameplayProbeRecorder>();
            if (recorder == null || recorder.subject == null)
                throw new InvalidOperationException(
                    "No active GameplayProbe subject is available for support calibration.");
            if (current == null && vShooterIKAdjustWindow.curWindow != null)
            {
                current = vShooterIKAdjustWindow.curWindow.ikController as
                    BrawlInvectorIKCalibrationBridge;
            }
            if (current == null || current.actorObject != recorder.subject.gameObject)
                Bind(recorder.subject);
            if (current == null || current.actorObject != recorder.subject.gameObject)
                throw new InvalidOperationException(
                    "The stock IK window is not bound to the active GameplayProbe subject.");
            InvectorBrawlerWeaponPresentation activePresenter =
                recorder.subject.GetComponent<InvectorBrawlerWeaponPresentation>();
            if (activePresenter == null || activePresenter.WeaponVisualRoot == null ||
                activePresenter.SupportHandTarget == null)
                throw new InvalidOperationException(
                    "No active calibrated staff presenter was found.");

            Animator animator = activePresenter.ConfiguredAnimator;
            HumanBodyBones supportBone = activePresenter.WeaponHeldInLeftHand
                ? HumanBodyBones.RightHand : HumanBodyBones.LeftHand;
            Transform supportHand = animator.GetBoneTransform(supportBone);
            if (supportHand == null ||
                !GameplayProbeRecorder.TryClosestMeshPoint(
                    activePresenter.WeaponVisualRoot,
                    supportHand.position,
                    out Vector3 staffSurfacePoint))
                throw new InvalidOperationException(
                    "The support hand or readable staff surface is unavailable.");

            Transform target = activePresenter.SupportHandTarget;
            // Humanoid hand transforms sit at the wrist rather than the palm.
            // Cinder's visually accepted weapon hand has a 7 cm wrist-to-staff
            // surface distance, so preserve that anatomical offset instead of
            // incorrectly snapping the wrist bone onto the mesh surface.
            Vector3 handToSurface = staffSurfacePoint - supportHand.position;
            const float DesiredWristSurfaceDistance = 0.07f;
            float moveDistance = Mathf.Max(
                0f, handToSurface.magnitude - DesiredWristSurfaceDistance);
            target.position = supportHand.position +
                handToSurface.normalized * moveDistance;
            Selection.activeTransform = target;
            SceneView.lastActiveSceneView?.FrameSelected();
            Debug.LogFormat(
                "[BrawlIKCalibration] Support handle seated with a 7 cm " +
                "wrist-to-staff surface offset. " +
                "localPosition={0:R},{1:R},{2:R}; localEuler={3:R},{4:R},{5:R}",
                target.localPosition.x, target.localPosition.y, target.localPosition.z,
                target.localEulerAngles.x, target.localEulerAngles.y,
                target.localEulerAngles.z);
        }

        [MenuItem("Brawl Arena/Gameplay Probe/Close Invector Settings Overlay Safely")]
        static void CloseInvectorSettingsOverlaySafely()
        {
            Invector.vCharacterController.vCheckForProjectSettings.isClosed = true;
            SceneView.RepaintAll();
        }

        void Configure(BrawlerController actor)
        {
            actorObject = actor.gameObject;
            actorTransform = actor.transform;
            presenter = actor.GetComponent<InvectorBrawlerWeaponPresentation>();
            animator = presenter != null ? presenter.ConfiguredAnimator : null;
            if (presenter == null || animator == null || !animator.isHuman ||
                presenter.ProjectIKAdjustList == null)
            {
                throw new InvalidOperationException(
                    "The selected actor has no complete Brawl Invector weapon presentation.");
            }

            leftIK = new vIKSolver(animator, AvatarIKGoal.LeftHand);
            rightIK = new vIKSolver(animator, AvatarIKGoal.RightHand);
            HideSolver(leftIK);
            HideSolver(rightIK);

            descriptorObject = new GameObject("Brawl IK Calibration Weapon Descriptor");
            descriptorObject.hideFlags = HideFlags.HideAndDontSave;
            descriptorObject.SetActive(false);
            descriptor = descriptorObject.AddComponent<vShooterWeapon>();
            descriptor.enabled = false;
            descriptor.weaponCategory = presenter.WeaponCategory;
            descriptor.isLeftWeapon = presenter.WeaponHeldInLeftHand;
            descriptor.handIKTarget = presenter.SupportHandTarget;
            descriptorHandOffset = descriptor.handIKTargetOffset;
            if (descriptorHandOffset != null)
                descriptorHandOffset.gameObject.hideFlags = HideFlags.HideAndDontSave;

            UpdateWeaponIK();
            EditorApplication.update += Tick;
        }

        public void UpdateWeaponIK()
        {
            if (leftIK == null || rightIK == null) return;
            leftIK.UpdateIK();
            rightIK.UpdateIK();
            IKAdjust adjust = CurrentIKAdjust;
            if (adjust == null) return;

            vIKSolver weapon = IsLeftWeapon ? leftIK : rightIK;
            vIKSolver support = IsLeftWeapon ? rightIK : leftIK;
            ApplyOffset(weapon.endBoneOffset, adjust.weaponHandOffset);
            ApplyOffset(weapon.middleBoneOffset, adjust.weaponHintOffset);
            ApplyOffset(support.endBoneOffset, adjust.supportHandOffset);
            ApplyOffset(support.middleBoneOffset, adjust.supportHintOffset);
        }

        public void SetCustomIKAdjustState(string value) { }
        public void ResetCustomIKAdjustState() { }

        void Tick()
        {
            if (disposed) return;
            if (!Application.isPlaying || actorObject == null)
            {
                Dispose();
                return;
            }
            if (leftIK == null || rightIK == null) return;
            onStartUpdateIK?.Invoke();
            leftIK.UpdateIK();
            rightIK.UpdateIK();
            onFinishUpdateIK?.Invoke();
        }

        static void ApplyOffset(Transform target, IKOffsetTransform offset)
        {
            if (target == null || offset == null) return;
            target.localPosition = offset.position;
            target.localEulerAngles = offset.eulerAngles;
        }

        static void HideSolver(vIKSolver solver)
        {
            if (solver == null) return;
            HideTransform(solver.endBoneRef);
            HideTransform(solver.middleBoneRef);
            HideTransform(solver.endBoneOffset);
            HideTransform(solver.middleBoneOffset);
        }

        static void HideTransform(Transform target)
        {
            if (target != null)
                target.gameObject.hideFlags = HideFlags.HideAndDontSave;
        }

        public void Dispose()
        {
            if (disposed) return;
            disposed = true;
            EditorApplication.update -= Tick;
            if (vShooterIKAdjustWindow.curWindow != null &&
                ReferenceEquals(vShooterIKAdjustWindow.curWindow.ikController, this))
            {
                vShooterIKAdjustWindow.curWindow.ikController = null;
                vShooterIKAdjustWindow.curWindow.Repaint();
            }
            DestroySolver(leftIK);
            DestroySolver(rightIK);
            if (descriptorHandOffset != null)
                UnityEngine.Object.DestroyImmediate(descriptorHandOffset.gameObject);
            if (descriptorObject != null) UnityEngine.Object.DestroyImmediate(descriptorObject);
            if (ReferenceEquals(current, this)) current = null;
            actorObject = null;
            actorTransform = null;
            presenter = null;
            animator = null;
            leftIK = null;
            rightIK = null;
            descriptor = null;
            descriptorObject = null;
            descriptorHandOffset = null;
        }

        static void DestroySolver(vIKSolver solver)
        {
            if (solver == null) return;
            if (solver.endBoneRef != null)
                UnityEngine.Object.DestroyImmediate(solver.endBoneRef.gameObject);
            if (solver.middleBoneRef != null)
                UnityEngine.Object.DestroyImmediate(solver.middleBoneRef.gameObject);
        }
    }
}
