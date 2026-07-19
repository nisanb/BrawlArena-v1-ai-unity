using UnityEngine;
using UnityEngine.Rendering;

namespace BrawlArena
{
    /// <summary>
    /// Presentation-only procedural motion layered on top of the heavy
    /// Animator: run bob, acceleration/turn lean, idle breathing, a Ward Step
    /// burst, Super windup, hit flinch, and a soft contact blob shadow.
    ///
    /// Runs in LateUpdate, strictly after Mecanim has posed the skeleton for
    /// the frame, and only ever nudges the additive local offset of one
    /// "visual root" transform. That transform is resolved once at Configure
    /// time by walking up from the Humanoid Hips bone until its parent is the
    /// brawler root -- on this project's heavy rigs the Animator itself
    /// (like BrawlerController, the CharacterController, and every collider)
    /// lives on that same root GameObject, so the visual root is a sibling
    /// bone-hierarchy anchor, never the gameplay transform. The gameplay
    /// transform/collider are never written by this component.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class BrawlerMotionFlourish : MonoBehaviour
    {
        const string BlobShadowChildName = "BlobShadow";

        const float RunBobAmplitude = 0.045f;
        const float RunBobPelvisRollMaxDeg = 2.5f;
        const float RunBobMinHz = 0.6f;
        const float RunBobMaxHz = 2f;

        const float AccelLeanMaxDeg = 7f;
        const float BrakeLeanMaxDeg = 5f;
        const float LeanSmoothPerSecond = 6f;
        const float AccelReferenceMetersPerSecondSquared = 24f;

        const float TurnBankMaxDeg = 6f;
        const float TurnBankReferenceDegPerSecond = 220f;

        const float IdleSpeedThreshold = 0.3f;
        const float IdleBreatheHz = 0.45f;
        const float IdleBreatheAmplitude = 0.012f;
        const float IdleSwayAmplitude = 0.006f;

        const float WardStretchLeanDeg = 12f;
        const float WardStretchYDip = -0.03f;
        const float WardStretchApproachPerSecond = 20f;
        const float WardRecoverySeconds = 0.12f;
        const float WardRecoveryOvershoot = 0.03f;

        const float SuperCrouchY = -0.09f;
        const float SuperBackLeanDeg = 4f;
        const float SuperReleaseSeconds = 0.1f;
        const float SuperReleasePopY = 0.02f;

        const float HitFlinchLeanDeg = 6f;
        const float HitFlinchSmoothPerSecond = 10f;

        const float ReducedMotionMultiplier = 0.5f;

        static Texture2D cachedBlobTexture;
        static Mesh cachedBlobMesh;

        BrawlerController controller;
        Transform visualRoot;
        Vector3 restLocalPosition;
        Quaternion restLocalRotation;

        float bobPhase;
        float pelvisRollDeg;

        float leanPitchDeg;
        Vector3 previousVelocity;
        bool hasPreviousVelocity;

        float turnBankDeg;
        float previousYaw;
        bool hasPreviousYaw;

        float breathePhase;
        float idleFade;

        bool wasWardStepping;
        bool wardRecovering;
        float wardRecoveryElapsed;
        float wardStretchPitchDeg;
        float wardStretchY;

        bool superWindingUp;
        float superWindupElapsed;
        float superWindupDuration;
        bool superReleasing;
        float superReleaseElapsed;
        float superPoseY;
        float superPosePitchDeg;

        float flinchLeanDeg;

        /// <summary>True once a valid visual root has been resolved.</summary>
        public bool IsPresenting => visualRoot != null;

        /// <summary>
        /// Locates the visual root and caches its rest pose. Safe to call once,
        /// from production assembly, after the controller/motor/animator are
        /// already wired up. Never touches the gameplay transform.
        /// </summary>
        public void Configure(BrawlerController owner)
        {
            controller = owner;
            if (owner == null)
            {
                enabled = false;
                return;
            }

            visualRoot = ResolveVisualRoot(owner);
            if (visualRoot == null)
            {
                // No resolvable bone/mesh anchor distinct from the gameplay
                // root -- there is nothing safe to offset, so stay dormant
                // rather than risk nudging the gameplay transform.
                enabled = false;
                return;
            }

            restLocalPosition = visualRoot.localPosition;
            restLocalRotation = visualRoot.localRotation;
            EnsureBlobShadow(owner);
        }

        /// <summary>
        /// Requested by BrawlerController just before a Super's windup wait so
        /// the crouch-and-release reads even though the physical windup is a
        /// short, fixed delay. Idempotent per call; a new request restarts it.
        /// </summary>
        public void PresentSuperWindup(float seconds)
        {
            if (!IsPresenting) return;
            superWindupDuration = Mathf.Max(0.01f, seconds);
            superWindupElapsed = 0f;
            superWindingUp = true;
            superReleasing = false;
            superReleaseElapsed = 0f;
        }

        void LateUpdate()
        {
            if (controller == null || visualRoot == null || !Application.isPlaying) return;
            if (controller.IsDead)
            {
                // Ease back to rest instead of freezing mid-flourish on death.
                visualRoot.localPosition = restLocalPosition;
                visualRoot.localRotation = restLocalRotation;
                hasPreviousVelocity = false;
                hasPreviousYaw = false;
                return;
            }

            float dt = Mathf.Max(Time.deltaTime, 0.0001f);
            float motionScale = AccessibilitySettings.ReducedMotionEnabled
                ? ReducedMotionMultiplier
                : 1f;

            Vector3 velocity = controller.Motor != null ? controller.Motor.Velocity : Vector3.zero;
            velocity.y = 0f;
            float planarSpeed = velocity.magnitude;
            float speed01 = Mathf.Clamp01(planarSpeed / Mathf.Max(0.1f, controller.moveSpeed));

            UpdateRunBob(dt, speed01, motionScale);
            UpdateAccelerationLean(dt, velocity, motionScale);
            UpdateTurnBank(dt, motionScale);
            UpdateIdleBreathing(dt, planarSpeed, motionScale);
            UpdateWardStepBurst(dt, motionScale);
            UpdateSuperWindup(dt, motionScale);
            UpdateHitFlinch(dt, velocity, motionScale);

            Compose();
        }

        void UpdateRunBob(float dt, float speed01, float motionScale)
        {
            float strideHz = Mathf.Lerp(RunBobMinHz, RunBobMaxHz, speed01);
            bobPhase += strideHz * dt * Mathf.PI * 2f;
            if (bobPhase > Mathf.PI * 2000f) bobPhase -= Mathf.PI * 2000f;
            pelvisRollDeg = Mathf.Sin(bobPhase) * Mathf.Lerp(0f, RunBobPelvisRollMaxDeg, speed01) *
                            motionScale;
        }

        float CurrentBobY(float speed01, float motionScale)
        {
            return Mathf.Sin(bobPhase) * ComputeBobAmplitude(speed01, motionScale);
        }

        /// <summary>Peak run-bob amplitude for the given normalized planar speed.</summary>
        static float ComputeBobAmplitude(float speed01, float motionScale)
        {
            return RunBobAmplitude * Mathf.Clamp01(speed01) * motionScale;
        }

        void UpdateAccelerationLean(float dt, Vector3 velocity, float motionScale)
        {
            if (!hasPreviousVelocity)
            {
                previousVelocity = velocity;
                hasPreviousVelocity = true;
                return;
            }

            Vector3 accel = (velocity - previousVelocity) / dt;
            previousVelocity = velocity;

            Vector3 forward = controller.transform.forward;
            forward.y = 0f;
            if (forward.sqrMagnitude < 0.0001f) forward = Vector3.forward;
            forward.Normalize();

            float forwardAccel = Vector3.Dot(accel, forward);
            float targetPitch = ComputeLeanTargetDeg(forwardAccel, motionScale);

            leanPitchDeg = Mathf.Lerp(leanPitchDeg, targetPitch,
                1f - Mathf.Exp(-LeanSmoothPerSecond * dt));
        }

        /// <summary>
        /// Forward/back lean target in degrees: accelerating forward leans up
        /// to AccelLeanMaxDeg, braking/backward leans up to -BrakeLeanMaxDeg.
        /// </summary>
        static float ComputeLeanTargetDeg(float forwardAccel, float motionScale)
        {
            float normalized = Mathf.Clamp(
                forwardAccel / AccelReferenceMetersPerSecondSquared, -1f, 1f);
            return (normalized >= 0f
                ? normalized * AccelLeanMaxDeg
                : normalized * BrakeLeanMaxDeg) * motionScale;
        }

        void UpdateTurnBank(float dt, float motionScale)
        {
            float yaw = controller.transform.eulerAngles.y;
            if (!hasPreviousYaw)
            {
                previousYaw = yaw;
                hasPreviousYaw = true;
                return;
            }

            float yawRate = Mathf.DeltaAngle(previousYaw, yaw) / dt;
            previousYaw = yaw;

            float targetBank = ComputeTurnBankTargetDeg(yawRate, motionScale);
            turnBankDeg = Mathf.Lerp(turnBankDeg, targetBank,
                1f - Mathf.Exp(-LeanSmoothPerSecond * dt));
        }

        /// <summary>Roll-into-turn target in degrees, clamped to ±TurnBankMaxDeg.</summary>
        static float ComputeTurnBankTargetDeg(float yawRatePerSecond, float motionScale)
        {
            float normalized = Mathf.Clamp(yawRatePerSecond / TurnBankReferenceDegPerSecond, -1f, 1f);
            return -normalized * TurnBankMaxDeg * motionScale;
        }

        void UpdateIdleBreathing(float dt, float planarSpeed, float motionScale)
        {
            breathePhase += IdleBreatheHz * dt * Mathf.PI * 2f;
            if (breathePhase > Mathf.PI * 2000f) breathePhase -= Mathf.PI * 2000f;

            // Fade the breathing/sway out smoothly as the character picks up
            // speed instead of a hard cut at the threshold.
            idleFade = Mathf.Clamp01(1f - planarSpeed / IdleSpeedThreshold);
            idleFade *= motionScale;
        }

        void UpdateWardStepBurst(float dt, float motionScale)
        {
            bool stepping = controller.WardStepping;
            if (stepping)
            {
                wardRecovering = false;
                wardRecoveryElapsed = 0f;
                wardStretchPitchDeg = Mathf.Lerp(wardStretchPitchDeg,
                    WardStretchLeanDeg * motionScale,
                    1f - Mathf.Exp(-WardStretchApproachPerSecond * dt));
                wardStretchY = Mathf.Lerp(wardStretchY, WardStretchYDip * motionScale,
                    1f - Mathf.Exp(-WardStretchApproachPerSecond * dt));
            }
            else if (wasWardStepping && !wardRecovering)
            {
                // Rising edge of "step just ended": begin the settle window.
                wardRecovering = true;
                wardRecoveryElapsed = 0f;
            }
            else if (wardRecovering)
            {
                wardRecoveryElapsed += dt;
                float t = Mathf.Clamp01(wardRecoveryElapsed / WardRecoverySeconds);
                // First half overshoots past rest, second half settles home.
                float overshoot = WardRecoveryOvershoot * motionScale;
                float y = t < 0.5f
                    ? Mathf.SmoothStep(wardStretchY, overshoot, t / 0.5f)
                    : Mathf.SmoothStep(overshoot, 0f, (t - 0.5f) / 0.5f);
                wardStretchY = y;
                wardStretchPitchDeg = Mathf.SmoothStep(wardStretchPitchDeg, 0f, t);
                if (t >= 1f)
                {
                    wardRecovering = false;
                    wardStretchY = 0f;
                    wardStretchPitchDeg = 0f;
                }
            }
            wasWardStepping = stepping;
        }

        void UpdateSuperWindup(float dt, float motionScale)
        {
            if (superWindingUp)
            {
                superWindupElapsed += dt;
                float t = Mathf.Clamp01(superWindupElapsed / superWindupDuration);
                superPoseY = Mathf.SmoothStep(0f, SuperCrouchY * motionScale, t);
                superPosePitchDeg = Mathf.SmoothStep(0f, SuperBackLeanDeg * motionScale, t);
                if (t >= 1f)
                {
                    superWindingUp = false;
                    superReleasing = true;
                    superReleaseElapsed = 0f;
                }
                return;
            }

            if (superReleasing)
            {
                superReleaseElapsed += dt;
                float t = Mathf.Clamp01(superReleaseElapsed / SuperReleaseSeconds);
                float pop = SuperReleasePopY * motionScale;
                superPoseY = t < 0.5f
                    ? Mathf.SmoothStep(SuperCrouchY * motionScale, pop, t / 0.5f)
                    : Mathf.SmoothStep(pop, 0f, (t - 0.5f) / 0.5f);
                superPosePitchDeg = Mathf.SmoothStep(SuperBackLeanDeg * motionScale, 0f, t);
                if (t >= 1f)
                {
                    superReleasing = false;
                    superPoseY = 0f;
                    superPosePitchDeg = 0f;
                }
            }
        }

        void UpdateHitFlinch(float dt, Vector3 velocity, float motionScale)
        {
            bool knockbackActive = controller.KnockbackActive;
            float target = knockbackActive ? HitFlinchLeanDeg * motionScale : 0f;
            flinchLeanDeg = Mathf.Lerp(flinchLeanDeg, target,
                1f - Mathf.Exp(-HitFlinchSmoothPerSecond * dt));
        }

        void Compose()
        {
            float velocityMagnitude = previousVelocity.magnitude;
            float speed01 = Mathf.Clamp01(velocityMagnitude / Mathf.Max(0.1f, controller.moveSpeed));
            float motionScale = AccessibilitySettings.ReducedMotionEnabled
                ? ReducedMotionMultiplier
                : 1f;

            float bobY = CurrentBobY(speed01, motionScale);
            float breatheY = Mathf.Sin(breathePhase) * IdleBreatheAmplitude * idleFade;
            float swayX = Mathf.Cos(breathePhase * 0.5f) * IdleSwayAmplitude * idleFade;

            Vector3 offset = new Vector3(
                swayX,
                bobY + breatheY + wardStretchY + superPoseY,
                0f);

            // Forward/back lean (pitch, local X) plus an away-from-hit flinch
            // that adds into the same axis; turn bank + run-bob pelvis roll
            // share the roll axis (local Z).
            float pitchDeg = leanPitchDeg + wardStretchPitchDeg + superPosePitchDeg + flinchLeanDeg;
            float rollDeg = turnBankDeg + pelvisRollDeg;

            visualRoot.localPosition = restLocalPosition + offset;
            // Left-multiplied so pitch/roll are expressed in the controller
            // root's own axes (lean forward relative to facing, bank relative
            // to facing) rather than the bone's own rest-pose axes -- some
            // imported rigs bake a +/-90 degree axis-conversion rotation onto
            // this anchor, which would otherwise cross-contaminate roll into
            // an unintended axis.
            visualRoot.localRotation =
                Quaternion.Euler(pitchDeg, 0f, rollDeg) * restLocalRotation;
        }

        /// <summary>
        /// Walks up from the Humanoid Hips bone until its parent is the
        /// brawler root, returning that direct child -- the stable, never
        /// directly-animated anchor above the whole animated skeleton. Falls
        /// back to the first direct child carrying a renderer for a Generic
        /// (non-Humanoid) rig.
        /// </summary>
        static Transform ResolveVisualRoot(BrawlerController owner)
        {
            Transform root = owner.transform;
            Animator animator = owner.GetComponent<Animator>();
            if (animator != null && animator.isHuman)
            {
                Transform hips = animator.GetBoneTransform(HumanBodyBones.Hips);
                Transform candidate = WalkToDirectChild(hips, root);
                if (candidate != null) return candidate;
            }

            for (int i = 0; i < root.childCount; i++)
            {
                Transform child = root.GetChild(i);
                if (child.GetComponentInChildren<Renderer>(true) != null) return child;
            }
            return null;
        }

        static Transform WalkToDirectChild(Transform bone, Transform root)
        {
            if (bone == null || root == null) return null;
            Transform current = bone;
            while (current.parent != null && current.parent != root)
                current = current.parent;
            return current.parent == root ? current : null;
        }

        /// <summary>
        /// Grounds the character with a cheap procedural blob shadow, working
        /// regardless of real-time shadow quality on low-tier devices. Parented
        /// under the controller root (not the scaled visual-root bone) so it
        /// stays glued to the ground contact point and is immune to that
        /// bone's own non-uniform import scale. Idempotent: skipped if a
        /// "BlobShadow" child already exists.
        /// </summary>
        static void EnsureBlobShadow(BrawlerController owner)
        {
            if (owner.transform.Find(BlobShadowChildName) != null) return;

            Shader shader = Shader.Find("Sprites/Default");
            if (shader == null) shader = Shader.Find("Universal Render Pipeline/Unlit");
            if (shader == null) return;

            // Built by hand instead of CreatePrimitive: the primitive ships a
            // concave MeshCollider whose deferred Destroy still exists for one
            // physics sync under the brawler's dynamic Rigidbody, which PhysX
            // rejects with a per-spawn error.
            var quad = new GameObject(BlobShadowChildName);
            quad.AddComponent<MeshFilter>().sharedMesh = GetOrCreateBlobMesh();
            quad.AddComponent<MeshRenderer>();

            Transform quadTransform = quad.transform;
            quadTransform.SetParent(owner.transform, false);
            quadTransform.localPosition = new Vector3(0f, 0.02f, 0f);
            quadTransform.localRotation = Quaternion.Euler(90f, 0f, 0f);

            float radius = owner.Motor != null ? owner.Motor.CollisionRadius : 0.65f;
            float diameter = Mathf.Max(0.1f, radius * 1.5f);
            quadTransform.localScale = new Vector3(diameter, diameter, 1f);

            var material = new Material(shader) { name = "BrawlerBlobShadow_Runtime" };
            material.mainTexture = GetOrCreateBlobTexture();
            if (material.HasProperty("_Color")) material.color = new Color(0f, 0f, 0f, 0.35f);

            MeshRenderer meshRenderer = quad.GetComponent<MeshRenderer>();
            meshRenderer.sharedMaterial = material;
            meshRenderer.shadowCastingMode = ShadowCastingMode.Off;
            meshRenderer.receiveShadows = false;
            meshRenderer.lightProbeUsage = LightProbeUsage.Off;
            meshRenderer.reflectionProbeUsage = UnityEngine.Rendering.ReflectionProbeUsage.Off;
        }

        /// <summary>Collider-free unit quad in the XY plane, shared by every blob shadow.</summary>
        static Mesh GetOrCreateBlobMesh()
        {
            if (cachedBlobMesh != null) return cachedBlobMesh;
            cachedBlobMesh = new Mesh
            {
                name = "BrawlerBlobShadowQuad",
                vertices = new[]
                {
                    new Vector3(-0.5f, -0.5f, 0f), new Vector3(0.5f, -0.5f, 0f),
                    new Vector3(-0.5f, 0.5f, 0f), new Vector3(0.5f, 0.5f, 0f),
                },
                uv = new[]
                {
                    new Vector2(0f, 0f), new Vector2(1f, 0f),
                    new Vector2(0f, 1f), new Vector2(1f, 1f),
                },
                triangles = new[] { 0, 2, 1, 2, 3, 1 },
            };
            cachedBlobMesh.RecalculateNormals();
            return cachedBlobMesh;
        }

        /// <summary>Builds a soft radial-alpha circle once and reuses it for every blob shadow.</summary>
        internal static Texture2D GetOrCreateBlobTexture()
        {
            if (cachedBlobTexture != null) return cachedBlobTexture;

            const int size = 32;
            var texture = new Texture2D(size, size, TextureFormat.RGBA32, false)
            {
                name = "BrawlerBlobShadow",
                wrapMode = TextureWrapMode.Clamp,
            };
            Vector2 center = new Vector2(size / 2f, size / 2f);
            float maxRadius = size / 2f;
            var pixels = new Color[size * size];
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float d = Vector2.Distance(new Vector2(x + 0.5f, y + 0.5f), center) / maxRadius;
                    float alpha = Mathf.Clamp01(1f - d);
                    alpha *= alpha;
                    pixels[y * size + x] = new Color(0f, 0f, 0f, alpha);
                }
            }
            texture.SetPixels(pixels);
            texture.Apply();
            cachedBlobTexture = texture;
            return cachedBlobTexture;
        }

#if UNITY_EDITOR
        /// <summary>Test-only accessor for the resolved visual root, if any.</summary>
        internal Transform DebugVisualRoot => visualRoot;
#endif
    }
}
