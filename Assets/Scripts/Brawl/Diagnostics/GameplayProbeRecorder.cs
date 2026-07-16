#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;

namespace BrawlArena.EditorAutomation
{
    /// <summary>
    /// Records manually rendered gameplay views into contact sheets alongside
    /// synchronized animation and weapon-pose telemetry.
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
            public float[] handPos;
            public float[] weaponPos;
            public float[] weaponEuler;
            public string animClips;
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
            public Camera camera;
            public RenderTexture renderTexture;
            public Texture2D sheet;
            public int cellCount;
            public int sheetIndex;

            public ViewCapture(string name)
            {
                this.name = name;
            }
        }

        struct SubjectObservation
        {
            public BrawlerController actor;
            public Animator animator;
            public Transform hand;
            public Transform weapon;
        }

        public BrawlerController subject;
        public int captureEveryNFrames = 3;
        public int cellWidth = 480;
        public int cellHeight = 270;
        public int gridCols = 4;
        public int gridRows = 4;

        readonly ViewCapture closeup = new ViewCapture("closeup");
        readonly ViewCapture side = new ViewCapture("side");
        readonly ViewCapture main = new ViewCapture("main");
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
                throw new ArgumentException("A recording output directory is required.", nameof(outputDirAbsolute));
            if (!Path.IsPathRooted(outputDirAbsolute))
                throw new ArgumentException("The recording output directory must be absolute.", nameof(outputDirAbsolute));

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
            PrepareView(closeup, sheetWidth, sheetHeight);
            PrepareView(side, sheetWidth, sheetHeight);
            PrepareView(main, sheetWidth, sheetHeight);
            EnsureProbeCamera(closeup, "Gameplay Probe Closeup Camera", 42f);
            EnsureProbeCamera(side, "Gameplay Probe Side Camera", 50f);

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

            FlushSheet(closeup);
            FlushSheet(side);
            FlushSheet(main);

            var wrapper = new TelemetryWrapper
            {
                frames = telemetry.ToArray(),
            };
            string json = JsonUtility.ToJson(wrapper, true);
            File.WriteAllText(Path.Combine(outputDirectory, "frames.json"), json);
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

            if (observation.actor != null && observation.hand != null && closeup.camera != null)
            {
                PositionCloseupCamera(closeup.camera, observation.actor.transform, observation.hand);
                CaptureView(closeup, closeup.camera);
            }

            if (observation.actor != null && side.camera != null)
            {
                PositionSideCamera(side.camera, observation.actor.transform);
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
            observation.animator = subject.GetComponentInChildren<Animator>(true);
            if (observation.animator == null || !observation.animator.isHuman)
                return observation;

            observation.hand = observation.animator.GetBoneTransform(HumanBodyBones.RightHand);
            if (observation.hand == null) return observation;

            Renderer weaponRenderer = observation.hand.GetComponentInChildren<Renderer>(true);
            if (weaponRenderer != null) observation.weapon = weaponRenderer.transform;
            return observation;
        }

        static void PositionCloseupCamera(Camera camera, Transform actor, Transform hand)
        {
            Vector3 offset = actor.right * 0.65f + actor.forward * 0.9f + Vector3.up * 0.35f;
            camera.transform.position = hand.position + offset.normalized * 1.2f;
            camera.transform.LookAt(hand.position, Vector3.up);
        }

        static void PositionSideCamera(Camera camera, Transform actor)
        {
            camera.transform.position = actor.position + actor.right * 3.5f + Vector3.up * 1.2f;
            camera.transform.LookAt(actor.position + Vector3.up, Vector3.up);
        }

        void CaptureView(ViewCapture view, Camera camera)
        {
            if (view.renderTexture == null || view.sheet == null || camera == null) return;

            RenderTexture previousTarget = camera.targetTexture;
            try
            {
                camera.targetTexture = view.renderTexture;
                camera.Render();

                int column = view.cellCount % gridCols;
                int row = view.cellCount / gridCols;
                int destinationX = column * cellWidth;
                int destinationY = (gridRows - row - 1) * cellHeight;

                RenderTexture previousActive = RenderTexture.active;
                try
                {
                    RenderTexture.active = view.renderTexture;
                    view.sheet.ReadPixels(
                        new Rect(0f, 0f, cellWidth, cellHeight),
                        destinationX, destinationY, false);
                    view.sheet.Apply(false, false);
                }
                finally
                {
                    RenderTexture.active = previousActive;
                }

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
            Vector3 subjectPosition = Vector3.zero;
            float subjectYaw = 0f;
            bool canAct = false;
            if (observation.actor != null)
            {
                subjectPosition = observation.actor.transform.position;
                subjectYaw = observation.actor.transform.eulerAngles.y;
                canAct = observation.actor.CanAct;
            }

            Vector3 handPosition = observation.hand != null
                ? observation.hand.position
                : Vector3.zero;
            Vector3 weaponPosition = observation.weapon != null
                ? observation.weapon.position
                : Vector3.zero;
            Vector3 weaponEuler = observation.weapon != null
                ? observation.weapon.eulerAngles
                : Vector3.zero;

            telemetry.Add(new FrameTelemetry
            {
                tick = tick,
                frameCount = Time.frameCount,
                time = Time.time,
                subjectPos = ToArray(subjectPosition),
                subjectYaw = subjectYaw,
                handPos = ToArray(handPosition),
                weaponPos = ToArray(weaponPosition),
                weaponEuler = ToArray(weaponEuler),
                animClips = CollectClipNames(observation.animator),
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

            if (view.sheet == null ||
                view.sheet.width != sheetWidth ||
                view.sheet.height != sheetHeight)
            {
                DestroyTexture(view);
                view.sheet = new Texture2D(sheetWidth, sheetHeight, TextureFormat.RGB24, false)
                {
                    name = "Gameplay Probe " + view.name + " Contact Sheet",
                    filterMode = FilterMode.Bilinear,
                    wrapMode = TextureWrapMode.Clamp,
                };
            }

            view.cellCount = 0;
            view.sheetIndex = 0;
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
            view.camera.cullingMask = ~0;
            view.camera.nearClipPlane = 0.03f;
            view.camera.farClipPlane = 500f;
            view.camera.fieldOfView = fieldOfView;
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
            byte[] png = view.sheet.EncodeToPNG();
            string fileName = string.Format(
                "sheet-{0}-{1:000}.png", view.name, view.sheetIndex);
            File.WriteAllBytes(Path.Combine(outputDirectory, fileName), png);
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
            DestroyView(closeup, true);
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
            DestroyTexture(view);
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

        void DestroyTexture(ViewCapture view)
        {
            if (view.sheet == null) return;
            Destroy(view.sheet);
            view.sheet = null;
        }
    }
}
#endif
