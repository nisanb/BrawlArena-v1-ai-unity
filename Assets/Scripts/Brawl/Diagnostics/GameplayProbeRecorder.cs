#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace BrawlArena.EditorAutomation
{
    /// <summary>
    /// Restores the editor probe's roster selection after the play-mode domain
    /// reload and before Arena scene objects run Awake/Start.
    /// </summary>
    static class GameplayProbePendingSelectionBootstrap
    {
        const string PendingPlayCharacterIndexKey =
            "BrawlArena.GameplayProbe.PendingPlayCharacterIndex";

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void RestorePendingSelection()
        {
            int characterIndex = SessionState.GetInt(
                PendingPlayCharacterIndexKey, -1);
            if (characterIndex < 0) return;
            MatchSetup.CharacterIndex = characterIndex;
            MatchSetup.FromMenu = true;
        }
    }

    /// <summary>
    /// Records ordered gameplay views and synchronized presentation telemetry.
    /// The recorder resolves both hands and the configured weapon presentation;
    /// it never guesses a weapon from the first renderer under a hand bone.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class GameplayProbeRecorder : MonoBehaviour
    {
        [Serializable]
        sealed class FrameTelemetry
        {
            public int tick;
            public int frameCount;
            public float time;
            public float[] subjectPos;
            public float subjectYaw;
            public float[] rightHandPos;
            public float[] rightHandEuler;
            public float[] leftHandPos;
            public float[] leftHandEuler;
            public float[] rightLowerArmPos;
            public float[] leftLowerArmPos;
            public float[] rightUpperArmPos;
            public float[] leftUpperArmPos;
            public float[] weaponVisualPos;
            public float[] weaponVisualEuler;
            public float[] weaponGripPos;
            public float weaponGripDistance;
            public float[] supportGripPos;
            public float supportGripDistance;
            public float[] supportTargetPos;
            public float[] supportTargetEuler;
            public float[] effectiveSupportTargetPos;
            public float[] effectiveSupportHintPos;
            public float supportHandDistance;
            public float supportReachDistance;
            public float supportReachMargin;
            public float supportHintLateral;
            public float[] supportHintPos;
            public string weaponCategory;
            public bool weaponHeldInLeftHand;
            public string weaponHandBone;
            public string supportHandBone;
            public string ikState;
            public bool aimPresented;
            public string ikSuppression;
            public string invalidIKPoseStage;
            public string animClips;
            public string lastAction;
            public bool lastActionSucceeded;
            public bool canAct;
        }

        [Serializable]
        sealed class TelemetryWrapper
        {
            public FrameTelemetry[] frames;
        }

        sealed class ViewCapture
        {
            public readonly string name;
            public readonly bool writeOrderedFrames;
            public Camera camera;
            public RenderTexture renderTexture;
            public Texture2D frame;
            public Texture2D sheet;
            public int cellCount;
            public int sheetIndex;
            public int frameIndex;

            public ViewCapture(string name, bool writeOrderedFrames)
            {
                this.name = name;
                this.writeOrderedFrames = writeOrderedFrames;
            }
        }

        struct SubjectObservation
        {
            public BrawlerController actor;
            public Animator animator;
            public HeavyWeaponPresentation presenter;
            public ScriptedBrawlerDriver driver;
            public Transform rightUpperArm;
            public Transform rightLowerArm;
            public Transform rightHand;
            public Transform leftUpperArm;
            public Transform leftLowerArm;
            public Transform leftHand;
            public Transform weaponHand;
            public Transform supportHand;
            public Transform weaponVisual;
            public Transform supportTarget;
            public Transform supportHint;
            public Vector3 weaponGripPoint;
            public float weaponGripDistance;
            public bool hasWeaponGrip;
            public Vector3 supportGripPoint;
            public float supportGripDistance;
            public bool hasSupportGrip;
            public Vector3 effectiveSupportTarget;
            public Vector3 effectiveSupportHint;
            public bool hasEffectiveSupportPose;
        }

        public BrawlerController subject;
        public int captureEveryNFrames = 6;
        public int cellWidth = 800;
        public int cellHeight = 600;
        public int gridCols = 4;
        public int gridRows = 4;

        readonly ViewCapture hands = new ViewCapture("hands", true);
        readonly ViewCapture support = new ViewCapture("support", true);
        readonly ViewCapture supportOpposite = new ViewCapture("support-opposite", true);
        readonly ViewCapture front = new ViewCapture("front", true);
        readonly ViewCapture side = new ViewCapture("side", true);
        readonly ViewCapture main = new ViewCapture("main", false);
        readonly List<FrameTelemetry> telemetry = new List<FrameTelemetry>();
        readonly List<AnimatorClipInfo> clipInfoBuffer = new List<AnimatorClipInfo>();
        readonly StringBuilder clipNames = new StringBuilder();

        Color32[] blankSheetPixels;
        string outputDirectory;
        int captureCountdown;
        int tick;
        bool isRecording;

        public bool IsRecording => isRecording;

        public void StartRecording(string outputDirAbsolute)
        {
            if (string.IsNullOrWhiteSpace(outputDirAbsolute))
                throw new ArgumentException(
                    "A recording output directory is required.", nameof(outputDirAbsolute));
            if (!Path.IsPathRooted(outputDirAbsolute))
                throw new ArgumentException(
                    "The recording output directory must be absolute.", nameof(outputDirAbsolute));

            if (isRecording) StopRecording();

            outputDirectory = Path.GetFullPath(outputDirAbsolute);
            Directory.CreateDirectory(outputDirectory);
            string markerPath = Path.Combine(outputDirectory, "recording-complete.marker");
            if (File.Exists(markerPath)) File.Delete(markerPath);

            cellWidth = Mathf.Max(1, cellWidth);
            cellHeight = Mathf.Max(1, cellHeight);
            gridCols = Mathf.Max(1, gridCols);
            gridRows = Mathf.Max(1, gridRows);
            captureEveryNFrames = Mathf.Max(1, captureEveryNFrames);

            int sheetWidth = cellWidth * gridCols;
            int sheetHeight = cellHeight * gridRows;
            EnsureBlankPixels(sheetWidth * sheetHeight);
            PrepareView(hands, sheetWidth, sheetHeight);
            PrepareView(support, sheetWidth, sheetHeight);
            PrepareView(supportOpposite, sheetWidth, sheetHeight);
            PrepareView(front, sheetWidth, sheetHeight);
            PrepareView(side, sheetWidth, sheetHeight);
            PrepareView(main, sheetWidth, sheetHeight);
            EnsureProbeCamera(hands, "Gameplay Probe Hands Camera", 34f);
            EnsureProbeCamera(support, "Gameplay Probe Support Camera", 28f);
            EnsureProbeCamera(
                supportOpposite, "Gameplay Probe Opposite Support Camera", 28f);
            EnsureProbeCamera(front, "Gameplay Probe Front Camera", 38f);
            EnsureProbeCamera(side, "Gameplay Probe Side Camera", 38f);

            telemetry.Clear();
            clipInfoBuffer.Clear();
            clipNames.Clear();
            captureCountdown = 0;
            tick = 0;
            isRecording = true;
        }

        public void StopRecording()
        {
            if (!isRecording) return;
            isRecording = false;

            FlushSheet(hands);
            FlushSheet(support);
            FlushSheet(supportOpposite);
            FlushSheet(front);
            FlushSheet(side);
            FlushSheet(main);

            var wrapper = new TelemetryWrapper { frames = telemetry.ToArray() };
            File.WriteAllText(
                Path.Combine(outputDirectory, "frames.json"),
                JsonUtility.ToJson(wrapper, true));
            File.WriteAllBytes(
                Path.Combine(outputDirectory, "recording-complete.marker"),
                Array.Empty<byte>());
        }

        void LateUpdate()
        {
            if (!isRecording) return;
            if (captureCountdown > 0)
            {
                captureCountdown--;
                return;
            }

            captureCountdown = Mathf.Max(1, captureEveryNFrames) - 1;
            CaptureTick();
        }

        void CaptureTick()
        {
            SubjectObservation observation = ObserveSubject();
            if (observation.actor != null && observation.rightHand != null &&
                observation.leftHand != null)
            {
                PositionHandsCamera(hands.camera, observation);
                CaptureView(hands, hands.camera);
                PositionSupportCamera(support.camera, observation);
                CaptureView(support, support.camera);
                PositionOppositeSupportCamera(supportOpposite.camera, observation);
                CaptureView(supportOpposite, supportOpposite.camera);
                PositionFullPoseCamera(front.camera, observation, true);
                CaptureView(front, front.camera);
                PositionFullPoseCamera(side.camera, observation, false);
                CaptureView(side, side.camera);
            }

            Camera mainCamera = Camera.main;
            if (mainCamera != null) CaptureView(main, mainCamera);

            AppendTelemetry(observation);
            tick++;
        }

        SubjectObservation ObserveSubject()
        {
            var observation = new SubjectObservation();
            if (subject == null) return observation;

            observation.actor = subject;
            observation.presenter =
                subject.GetComponent<HeavyWeaponPresentation>();
            observation.driver = subject.GetComponent<ScriptedBrawlerDriver>();
            observation.animator = subject.GetComponentInChildren<Animator>(true);
            if (observation.animator == null || !observation.animator.isHuman)
                return observation;

            observation.rightUpperArm = observation.animator.GetBoneTransform(
                HumanBodyBones.RightUpperArm);
            observation.rightLowerArm = observation.animator.GetBoneTransform(
                HumanBodyBones.RightLowerArm);
            observation.rightHand = observation.animator.GetBoneTransform(
                HumanBodyBones.RightHand);
            observation.leftUpperArm = observation.animator.GetBoneTransform(
                HumanBodyBones.LeftUpperArm);
            observation.leftLowerArm = observation.animator.GetBoneTransform(
                HumanBodyBones.LeftLowerArm);
            observation.leftHand = observation.animator.GetBoneTransform(
                HumanBodyBones.LeftHand);

            // The heavy stack parents the weapon visual to one hand bone with
            // no IK; hand-edness is observable from the hierarchy itself.
            observation.weaponVisual = observation.presenter != null
                ? observation.presenter.WeaponVisualRoot : null;
            bool heldLeft = observation.weaponVisual != null &&
                observation.leftHand != null &&
                observation.weaponVisual.IsChildOf(observation.leftHand);
            observation.weaponHand = heldLeft
                ? observation.leftHand : observation.rightHand;
            observation.supportHand = heldLeft
                ? observation.rightHand : observation.leftHand;
            observation.supportTarget = null;
            observation.supportHint = null;

            if (observation.weaponHand != null && observation.weaponVisual != null)
            {
                observation.hasWeaponGrip = TryClosestMeshPoint(
                    observation.weaponVisual,
                    observation.weaponHand.position,
                    out observation.weaponGripPoint);
                if (observation.hasWeaponGrip)
                {
                    observation.weaponGripDistance = Vector3.Distance(
                        observation.weaponHand.position,
                        observation.weaponGripPoint);
                }
            }
            if (observation.supportHand != null && observation.weaponVisual != null)
            {
                observation.hasSupportGrip = TryClosestMeshPoint(
                    observation.weaponVisual,
                    observation.supportHand.position,
                    out observation.supportGripPoint);
                if (observation.hasSupportGrip)
                {
                    observation.supportGripDistance = Vector3.Distance(
                        observation.supportHand.position,
                        observation.supportGripPoint);
                }
            }
            // No IK on the heavy stack: there is never an effective support
            // pose to report.
            observation.hasEffectiveSupportPose = false;
            return observation;
        }

        public static bool TryClosestMeshPoint(
            Transform root, Vector3 worldPoint, out Vector3 closestPoint)
        {
            closestPoint = Vector3.zero;
            float bestSqrDistance = float.PositiveInfinity;
            bool found = false;

            MeshFilter[] filters = root.GetComponentsInChildren<MeshFilter>(true);
            for (int filterIndex = 0; filterIndex < filters.Length; filterIndex++)
            {
                MeshFilter filter = filters[filterIndex];
                Mesh mesh = filter.sharedMesh;
                Renderer renderer = filter.GetComponent<Renderer>();
                if (mesh == null || !mesh.isReadable || renderer == null ||
                    !renderer.enabled || !renderer.gameObject.activeInHierarchy)
                    continue;

                Vector3[] vertices = mesh.vertices;
                int[] triangles = mesh.triangles;
                for (int i = 0; i + 2 < triangles.Length; i += 3)
                {
                    Vector3 a = filter.transform.TransformPoint(vertices[triangles[i]]);
                    Vector3 b = filter.transform.TransformPoint(vertices[triangles[i + 1]]);
                    Vector3 c = filter.transform.TransformPoint(vertices[triangles[i + 2]]);
                    Vector3 candidate = ClosestPointOnTriangle(worldPoint, a, b, c);
                    float sqrDistance = (candidate - worldPoint).sqrMagnitude;
                    if (sqrDistance >= bestSqrDistance) continue;
                    bestSqrDistance = sqrDistance;
                    closestPoint = candidate;
                    found = true;
                }
            }
            return found;
        }

        static Vector3 ClosestPointOnTriangle(
            Vector3 point, Vector3 a, Vector3 b, Vector3 c)
        {
            Vector3 ab = b - a;
            Vector3 ac = c - a;
            Vector3 ap = point - a;
            float d1 = Vector3.Dot(ab, ap);
            float d2 = Vector3.Dot(ac, ap);
            if (d1 <= 0f && d2 <= 0f) return a;

            Vector3 bp = point - b;
            float d3 = Vector3.Dot(ab, bp);
            float d4 = Vector3.Dot(ac, bp);
            if (d3 >= 0f && d4 <= d3) return b;

            float vc = d1 * d4 - d3 * d2;
            if (vc <= 0f && d1 >= 0f && d3 <= 0f)
                return a + (d1 / (d1 - d3)) * ab;

            Vector3 cp = point - c;
            float d5 = Vector3.Dot(ab, cp);
            float d6 = Vector3.Dot(ac, cp);
            if (d6 >= 0f && d5 <= d6) return c;

            float vb = d5 * d2 - d1 * d6;
            if (vb <= 0f && d2 >= 0f && d6 <= 0f)
                return a + (d2 / (d2 - d6)) * ac;

            float va = d3 * d6 - d5 * d4;
            if (va <= 0f && d4 - d3 >= 0f && d5 - d6 >= 0f)
                return b + ((d4 - d3) / ((d4 - d3) + (d5 - d6))) * (c - b);

            float denominator = 1f / (va + vb + vc);
            float v = vb * denominator;
            float w = vc * denominator;
            return a + ab * v + ac * w;
        }

        static void PositionHandsCamera(Camera camera, SubjectObservation observation)
        {
            if (camera == null) return;
            Bounds bounds = CreateArmBounds(observation);
            Vector3 direction = observation.actor.transform.forward * 0.9f +
                observation.actor.transform.right * 0.55f + Vector3.up * 0.18f;
            FitCamera(camera, bounds, direction, 1.35f);
        }

        static void PositionSupportCamera(
            Camera camera, SubjectObservation observation)
        {
            if (camera == null || observation.supportHand == null) return;
            var bounds = new Bounds(observation.supportHand.position, Vector3.one * 0.04f);
            Transform supportLowerArm = observation.weaponHand != null &&
                observation.weaponHand == observation.leftHand
                    ? observation.rightLowerArm
                    : observation.leftLowerArm;
            Encapsulate(ref bounds, supportLowerArm);
            Encapsulate(ref bounds, observation.supportTarget);
            if (observation.hasSupportGrip)
                bounds.Encapsulate(observation.supportGripPoint);
            bounds.Expand(new Vector3(0.16f, 0.16f, 0.16f));
            Vector3 direction = observation.actor.transform.forward * 0.75f -
                observation.actor.transform.right * 0.65f + Vector3.up * 0.08f;
            FitCamera(camera, bounds, direction, 1.15f);
        }

        static void PositionOppositeSupportCamera(
            Camera camera, SubjectObservation observation)
        {
            if (camera == null || observation.supportHand == null) return;
            var bounds = new Bounds(observation.supportHand.position, Vector3.one * 0.04f);
            Transform supportLowerArm = observation.weaponHand != null &&
                observation.weaponHand == observation.leftHand
                    ? observation.rightLowerArm
                    : observation.leftLowerArm;
            Encapsulate(ref bounds, supportLowerArm);
            Encapsulate(ref bounds, observation.supportTarget);
            if (observation.hasSupportGrip)
                bounds.Encapsulate(observation.supportGripPoint);
            bounds.Expand(new Vector3(0.16f, 0.16f, 0.16f));
            Vector3 direction = observation.actor.transform.forward * 0.75f +
                observation.actor.transform.right * 0.65f + Vector3.up * 0.12f;
            FitCamera(camera, bounds, direction, 1.55f);
        }

        static void PositionFullPoseCamera(
            Camera camera, SubjectObservation observation, bool isFront)
        {
            if (camera == null) return;
            Bounds bounds = CreatePresentationBounds(observation);
            Vector3 direction = isFront
                ? observation.actor.transform.forward
                : observation.actor.transform.right;
            direction = (direction + Vector3.up * 0.06f).normalized;
            FitCamera(camera, bounds, direction, 1.18f);
        }

        static Bounds CreateArmBounds(SubjectObservation observation)
        {
            Vector3 center = observation.actor.transform.position + Vector3.up;
            var bounds = new Bounds(center, Vector3.one * 0.05f);
            Encapsulate(ref bounds, observation.rightUpperArm);
            Encapsulate(ref bounds, observation.rightLowerArm);
            Encapsulate(ref bounds, observation.rightHand);
            Encapsulate(ref bounds, observation.leftUpperArm);
            Encapsulate(ref bounds, observation.leftLowerArm);
            Encapsulate(ref bounds, observation.leftHand);
            Encapsulate(ref bounds, observation.supportTarget);
            if (observation.hasWeaponGrip)
                bounds.Encapsulate(observation.weaponGripPoint);
            bounds.Expand(new Vector3(0.38f, 0.28f, 0.38f));
            return bounds;
        }

        static Bounds CreatePresentationBounds(SubjectObservation observation)
        {
            Vector3 root = observation.actor.transform.position;
            var bounds = new Bounds(root + Vector3.up, Vector3.one * 0.1f);
            bounds.Encapsulate(root + Vector3.up * 0.18f);
            bounds.Encapsulate(root + Vector3.up * 1.75f);

            Renderer[] renderers = observation.actor.GetComponentsInChildren<Renderer>(true);
            for (int i = 0; i < renderers.Length; i++)
            {
                Renderer renderer = renderers[i];
                if (renderer is ParticleSystemRenderer || !renderer.enabled ||
                    !renderer.gameObject.activeInHierarchy)
                    continue;
                bounds.Encapsulate(renderer.bounds);
            }
            return bounds;
        }

        static void Encapsulate(ref Bounds bounds, Transform target)
        {
            if (target != null) bounds.Encapsulate(target.position);
        }

        static void FitCamera(
            Camera camera, Bounds bounds, Vector3 viewDirection, float padding)
        {
            Vector3 direction = viewDirection.sqrMagnitude > 0.001f
                ? viewDirection.normalized : Vector3.forward;
            float vertical = Mathf.Max(0.25f, bounds.extents.y);
            float horizontal = Mathf.Max(bounds.extents.x, bounds.extents.z);
            float verticalRadians = camera.fieldOfView * Mathf.Deg2Rad * 0.5f;
            float horizontalRadians = Mathf.Atan(
                Mathf.Tan(verticalRadians) * Mathf.Max(0.1f, camera.aspect));
            float distance = Mathf.Max(
                vertical / Mathf.Tan(verticalRadians),
                horizontal / Mathf.Tan(horizontalRadians));
            distance = Mathf.Max(0.8f, distance * padding);
            camera.transform.position = bounds.center + direction * distance;
            camera.transform.LookAt(bounds.center, Vector3.up);
        }

        void CaptureView(ViewCapture view, Camera camera)
        {
            if (view.renderTexture == null || view.sheet == null ||
                view.frame == null || camera == null)
                return;

            RenderTexture previousTarget = camera.targetTexture;
            try
            {
                camera.targetTexture = view.renderTexture;
                camera.Render();

                RenderTexture previousActive = RenderTexture.active;
                try
                {
                    RenderTexture.active = view.renderTexture;
                    view.frame.ReadPixels(
                        new Rect(0f, 0f, cellWidth, cellHeight), 0, 0, false);
                    view.frame.Apply(false, false);
                }
                finally
                {
                    RenderTexture.active = previousActive;
                }

                int column = view.cellCount % gridCols;
                int row = view.cellCount / gridCols;
                int destinationX = column * cellWidth;
                int destinationY = (gridRows - row - 1) * cellHeight;
                view.sheet.SetPixels32(
                    destinationX, destinationY, cellWidth, cellHeight,
                    view.frame.GetPixels32());
                view.sheet.Apply(false, false);

                if (view.writeOrderedFrames)
                {
                    string frameDirectory = Path.Combine(outputDirectory, view.name);
                    string frameName = string.Format("frame-{0:000000}.png", view.frameIndex);
                    File.WriteAllBytes(
                        Path.Combine(frameDirectory, frameName),
                        view.frame.EncodeToPNG());
                }

                view.frameIndex++;
                view.cellCount++;
                if (view.cellCount >= gridCols * gridRows) FlushSheet(view);
            }
            finally
            {
                camera.targetTexture = previousTarget;
                if (view.camera == camera) camera.enabled = false;
            }
        }

        void AppendTelemetry(SubjectObservation observation)
        {
            Vector3 subjectPosition = observation.actor != null
                ? observation.actor.transform.position : Vector3.zero;
            float subjectYaw = observation.actor != null
                ? observation.actor.transform.eulerAngles.y : 0f;
            bool canAct = observation.actor != null && observation.actor.CanAct;
            float supportDistance = observation.supportHand != null &&
                observation.hasEffectiveSupportPose
                ? Vector3.Distance(
                    observation.supportHand.position,
                    observation.effectiveSupportTarget)
                : -1f;
            float supportReachDistance = -1f;
            float supportReachMargin = -1f;
            float supportHintLateral = -1f;
            bool supportIsRight = observation.weaponHand != null &&
                observation.weaponHand == observation.leftHand;
            Transform supportUpperArm = supportIsRight
                ? observation.rightUpperArm
                : observation.leftUpperArm;
            Transform supportLowerArm = supportIsRight
                ? observation.rightLowerArm
                : observation.leftLowerArm;
            Transform supportHand = supportIsRight
                ? observation.rightHand
                : observation.leftHand;
            if (supportHand != null && supportUpperArm != null &&
                supportLowerArm != null && observation.hasEffectiveSupportPose)
            {
                Vector3 root = supportUpperArm.position;
                Vector3 reach = observation.effectiveSupportTarget - root;
                supportReachDistance = reach.magnitude;
                float maximum = Vector3.Distance(root, supportLowerArm.position) +
                    Vector3.Distance(supportLowerArm.position, supportHand.position);
                supportReachMargin = maximum - supportReachDistance;
                if (supportReachDistance > 0.0001f)
                {
                    supportHintLateral = Vector3.ProjectOnPlane(
                        observation.effectiveSupportHint - root,
                        reach / supportReachDistance).magnitude;
                }
            }

            telemetry.Add(new FrameTelemetry
            {
                tick = tick,
                frameCount = Time.frameCount,
                time = Time.time,
                subjectPos = ToArray(subjectPosition),
                subjectYaw = subjectYaw,
                rightHandPos = PositionArray(observation.rightHand),
                rightHandEuler = EulerArray(observation.rightHand),
                leftHandPos = PositionArray(observation.leftHand),
                leftHandEuler = EulerArray(observation.leftHand),
                rightLowerArmPos = PositionArray(observation.rightLowerArm),
                leftLowerArmPos = PositionArray(observation.leftLowerArm),
                rightUpperArmPos = PositionArray(observation.rightUpperArm),
                leftUpperArmPos = PositionArray(observation.leftUpperArm),
                weaponVisualPos = PositionArray(observation.weaponVisual),
                weaponVisualEuler = EulerArray(observation.weaponVisual),
                weaponGripPos = observation.hasWeaponGrip
                    ? ToArray(observation.weaponGripPoint) : ToArray(Vector3.zero),
                weaponGripDistance = observation.hasWeaponGrip
                    ? observation.weaponGripDistance : -1f,
                supportGripPos = observation.hasSupportGrip
                    ? ToArray(observation.supportGripPoint) : ToArray(Vector3.zero),
                supportGripDistance = observation.hasSupportGrip
                    ? observation.supportGripDistance : -1f,
                supportTargetPos = PositionArray(observation.supportTarget),
                supportTargetEuler = EulerArray(observation.supportTarget),
                effectiveSupportTargetPos = observation.hasEffectiveSupportPose
                    ? ToArray(observation.effectiveSupportTarget) : ToArray(Vector3.zero),
                effectiveSupportHintPos = observation.hasEffectiveSupportPose
                    ? ToArray(observation.effectiveSupportHint) : ToArray(Vector3.zero),
                supportHandDistance = supportDistance,
                supportReachDistance = supportReachDistance,
                supportReachMargin = supportReachMargin,
                supportHintLateral = supportHintLateral,
                supportHintPos = PositionArray(observation.supportHint),
                weaponCategory = string.Empty,
                weaponHeldInLeftHand = observation.weaponHand != null &&
                    observation.weaponHand == observation.leftHand,
                weaponHandBone = observation.weaponHand != null &&
                    observation.weaponHand == observation.leftHand
                        ? HumanBodyBones.LeftHand.ToString()
                        : HumanBodyBones.RightHand.ToString(),
                supportHandBone = observation.weaponHand != null &&
                    observation.weaponHand == observation.leftHand
                        ? HumanBodyBones.RightHand.ToString()
                        : HumanBodyBones.LeftHand.ToString(),
                ikState = string.Empty,
                aimPresented = observation.presenter != null &&
                    observation.presenter.AimPresented,
                ikSuppression = string.Empty,
                invalidIKPoseStage = string.Empty,
                animClips = CollectClipNames(observation.animator),
                lastAction = observation.driver != null
                    ? observation.driver.LastAction : string.Empty,
                lastActionSucceeded = observation.driver != null &&
                    observation.driver.LastActionSucceeded,
                canAct = canAct,
            });
        }

        string CollectClipNames(Animator animator)
        {
            clipNames.Clear();
            if (animator == null || !animator.isActiveAndEnabled ||
                !animator.isInitialized || animator.runtimeAnimatorController == null)
                return string.Empty;

            for (int layer = 0; layer < animator.layerCount; layer++)
            {
                clipInfoBuffer.Clear();
                animator.GetCurrentAnimatorClipInfo(layer, clipInfoBuffer);
                for (int i = 0; i < clipInfoBuffer.Count; i++)
                {
                    AnimationClip clip = clipInfoBuffer[i].clip;
                    if (clip == null) continue;
                    if (clipNames.Length > 0) clipNames.Append(',');
                    clipNames.Append(clip.name);
                }
            }
            return clipNames.ToString();
        }

        static float[] PositionArray(Transform target)
        {
            return ToArray(target != null ? target.position : Vector3.zero);
        }

        static float[] EulerArray(Transform target)
        {
            return ToArray(target != null ? target.eulerAngles : Vector3.zero);
        }

        static float[] ToArray(Vector3 value)
        {
            return new[] { value.x, value.y, value.z };
        }

        void PrepareView(ViewCapture view, int sheetWidth, int sheetHeight)
        {
            if (view.renderTexture == null ||
                view.renderTexture.width != cellWidth ||
                view.renderTexture.height != cellHeight)
            {
                DestroyRenderTexture(view);
                view.renderTexture = new RenderTexture(
                    cellWidth, cellHeight, 24, RenderTextureFormat.ARGB32)
                {
                    name = "Gameplay Probe " + view.name + " RenderTexture",
                    filterMode = FilterMode.Bilinear,
                    wrapMode = TextureWrapMode.Clamp,
                };
                view.renderTexture.Create();
            }
            else if (!view.renderTexture.IsCreated())
            {
                view.renderTexture.Create();
            }

            if (view.frame == null || view.frame.width != cellWidth ||
                view.frame.height != cellHeight)
            {
                DestroyFrameTexture(view);
                view.frame = new Texture2D(
                    cellWidth, cellHeight, TextureFormat.RGB24, false)
                {
                    name = "Gameplay Probe " + view.name + " Frame",
                    filterMode = FilterMode.Bilinear,
                    wrapMode = TextureWrapMode.Clamp,
                };
            }

            if (view.sheet == null || view.sheet.width != sheetWidth ||
                view.sheet.height != sheetHeight)
            {
                DestroySheetTexture(view);
                view.sheet = new Texture2D(
                    sheetWidth, sheetHeight, TextureFormat.RGB24, false)
                {
                    name = "Gameplay Probe " + view.name + " Contact Sheet",
                    filterMode = FilterMode.Bilinear,
                    wrapMode = TextureWrapMode.Clamp,
                };
            }

            view.cellCount = 0;
            view.sheetIndex = 0;
            view.frameIndex = 0;
            if (view.writeOrderedFrames)
                Directory.CreateDirectory(Path.Combine(outputDirectory, view.name));
            ClearSheet(view);
        }

        void EnsureProbeCamera(ViewCapture view, string cameraName, float fieldOfView)
        {
            if (view.camera == null)
            {
                var cameraObject = new GameObject(cameraName)
                {
                    hideFlags = HideFlags.HideAndDontSave,
                };
                view.camera = cameraObject.AddComponent<Camera>();
            }

            view.camera.enabled = false;
            view.camera.targetTexture = view.renderTexture;
            view.camera.clearFlags = CameraClearFlags.Skybox;
            view.camera.backgroundColor = Color.black;
            // Gameplay VFX remains visible in the main-camera sheet. Excluding
            // the dedicated VFX layer here keeps hand/shaft contact readable
            // during bright Attack01/Attack02 muzzle and impact bursts.
            view.camera.cullingMask = ~(1 << 12);
            view.camera.nearClipPlane = 0.03f;
            view.camera.farClipPlane = 500f;
            view.camera.fieldOfView = fieldOfView;
            view.camera.aspect = (float)cellWidth / Mathf.Max(1, cellHeight);
            view.camera.allowHDR = true;
        }

        void EnsureBlankPixels(int pixelCount)
        {
            if (blankSheetPixels == null || blankSheetPixels.Length != pixelCount)
                blankSheetPixels = new Color32[pixelCount];
        }

        void ClearSheet(ViewCapture view)
        {
            if (view.sheet == null || blankSheetPixels == null) return;
            view.sheet.SetPixels32(blankSheetPixels);
            view.sheet.Apply(false, false);
        }

        void FlushSheet(ViewCapture view)
        {
            if (view.cellCount <= 0 || view.sheet == null) return;

            view.sheet.Apply(false, false);
            string fileName = string.Format(
                "sheet-{0}-{1:000}.png", view.name, view.sheetIndex);
            File.WriteAllBytes(
                Path.Combine(outputDirectory, fileName),
                view.sheet.EncodeToPNG());
            view.sheetIndex++;
            view.cellCount = 0;
            ClearSheet(view);
        }

        void OnDisable()
        {
            if (isRecording) StopRecording();
        }

        void OnDestroy()
        {
            if (isRecording) StopRecording();
            DestroyView(hands, true);
            DestroyView(support, true);
            DestroyView(supportOpposite, true);
            DestroyView(front, true);
            DestroyView(side, true);
            DestroyView(main, false);
            blankSheetPixels = null;
        }

        void DestroyView(ViewCapture view, bool destroyCamera)
        {
            if (destroyCamera && view.camera != null)
            {
                view.camera.targetTexture = null;
                Destroy(view.camera.gameObject);
                view.camera = null;
            }
            DestroyRenderTexture(view);
            DestroyFrameTexture(view);
            DestroySheetTexture(view);
        }

        void DestroyRenderTexture(ViewCapture view)
        {
            if (view.renderTexture == null) return;
            if (view.camera != null && view.camera.targetTexture == view.renderTexture)
                view.camera.targetTexture = null;
            view.renderTexture.Release();
            Destroy(view.renderTexture);
            view.renderTexture = null;
        }

        void DestroyFrameTexture(ViewCapture view)
        {
            if (view.frame == null) return;
            Destroy(view.frame);
            view.frame = null;
        }

        void DestroySheetTexture(ViewCapture view)
        {
            if (view.sheet == null) return;
            Destroy(view.sheet);
            view.sheet = null;
        }
    }
}
#endif
