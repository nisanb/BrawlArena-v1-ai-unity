using System;
using System.IO;
using Invector.IK;
using Invector.vShooter;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace BrawlArena.EditorAutomation
{
    /// <summary>
    /// Deterministic editor-only IK calibration bootstrap. It replaces the
    /// current clean edit-time scene with an unsaved isolated lab, enters Play
    /// Mode, instantiates exactly one production-human roster prefab, waits for
    /// its Humanoid Animator and presenter, binds Invector's stock IK window,
    /// selects the real support target, pauses, and writes a machine-readable
    /// ready contract. It never starts MatchManager, AI, physical input, or
    /// Invector's ProjectSettings importer.
    /// </summary>
    [InitializeOnLoad]
    public static class BrawlIKCalibrationBootstrap
    {
        const string PendingRosterKey =
            "BrawlArena.IKCalibration.PendingRoster";
        const string OriginalSceneKey =
            "BrawlArena.IKCalibration.OriginalScene";
        const string RestoreSceneKey =
            "BrawlArena.IKCalibration.RestoreScene";
        const double ReadyTimeoutSeconds = 20.0;
        const float MinimumReadyReachMargin = 0.001f;
        const float MinimumReadyHintLateral = 0.05f;

        static GameObject subjectContainer;
        static BrawlerController subject;
        static GameplayProbeRecorder recorder;
        static double playStartedAt;
        static bool subjectCreated;
        static int presenterEnabledAtFrame = -1;

        [Serializable]
        sealed class ReadyContract
        {
            public bool ready;
            public string rosterId;
            public string prefabPath;
            public string sceneName;
            public string subjectPath;
            public string supportTargetPath;
            public string weaponCategory;
            public string ikState;
            public int presenterUpdateCount;
            public string ikSuppression;
            public string invalidIKPoseStage;
            public float supportReachDistance;
            public float supportReachMargin;
            public float supportHintLateral;
            public string utc;
        }

        [Serializable]
        sealed class FailureContract
        {
            public bool ready;
            public string rosterId;
            public string error;
            public string utc;
        }

        static BrawlIKCalibrationBootstrap()
        {
            EditorApplication.update -= Tick;
            EditorApplication.update += Tick;
            EditorApplication.playModeStateChanged -= OnPlayModeStateChanged;
            EditorApplication.playModeStateChanged += OnPlayModeStateChanged;
        }

        public static string Begin(string requestedRosterId)
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
                throw new InvalidOperationException(
                    "IK calibration bootstrap must begin in Edit Mode.");

            string roster = NormalizeRoster(requestedRosterId);
            string prefabPath = PrefabPathForRoster(roster);
            if (AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath) == null)
                throw new InvalidOperationException(
                    "Missing production calibration prefab: " + prefabPath);

            Scene original = SceneManager.GetActiveScene();
            if (original.IsValid() && original.isDirty)
                throw new InvalidOperationException(
                    "Save the active scene before entering the isolated IK calibration lab.");

            ClearContract(roster, true);
            ClearContract(roster, false);
            SessionState.SetString(PendingRosterKey, roster);
            SessionState.SetString(
                OriginalSceneKey,
                original.IsValid() ? original.path : string.Empty);
            SessionState.SetBool(RestoreSceneKey, true);

            EditorSceneManager.NewScene(
                NewSceneSetup.EmptyScene, NewSceneMode.Single);
            EditorApplication.EnterPlaymode();
            return "Armed isolated IK calibration bootstrap for roster " + roster + ".";
        }

        static void Tick()
        {
            if (!EditorApplication.isPlaying)
            {
                TryRestoreOriginalScene();
                return;
            }

            string roster = SessionState.GetString(PendingRosterKey, string.Empty);
            if (string.IsNullOrEmpty(roster))
                return;

            try
            {
                if (playStartedAt <= 0.0)
                    playStartedAt = EditorApplication.timeSinceStartup;
                if (EditorApplication.timeSinceStartup - playStartedAt >
                    ReadyTimeoutSeconds)
                {
                    throw new TimeoutException(
                        "The production prefab did not become calibration-ready within " +
                        ReadyTimeoutSeconds + " seconds.");
                }

                if (!subjectCreated)
                {
                    CreateSubject(roster);
                    subjectCreated = true;
                    return;
                }

                TryFinish(roster);
            }
            catch (Exception exception)
            {
                Fail(roster, exception);
            }
        }

        static void CreateSubject(string roster)
        {
            Scene labScene = SceneManager.GetActiveScene();
            if (!labScene.IsValid() || !labScene.isLoaded ||
                labScene.rootCount != 0)
            {
                throw new InvalidOperationException(
                    "The calibration bootstrap requires one empty isolated runtime scene.");
            }

            string prefabPath = PrefabPathForRoster(roster);
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
            if (prefab == null)
                throw new InvalidOperationException(
                    "The production calibration prefab is unavailable: " + prefabPath);

            subjectContainer = new GameObject("BrawlIKCalibrationSubjectRoot");
            SceneManager.MoveGameObjectToScene(subjectContainer, labScene);
            subjectContainer.SetActive(false);

            GameObject instance = PrefabUtility.InstantiatePrefab(
                prefab, subjectContainer.transform) as GameObject;
            if (instance == null)
                throw new InvalidOperationException(
                    "Could not instantiate the production calibration prefab.");
            instance.name = DisplayNameForRoster(roster) + "CalibrationSubject";
            instance.transform.localPosition = Vector3.zero;
            instance.transform.localRotation = Quaternion.identity;

            subject = instance.GetComponent<BrawlerController>();
            InvectorBrawlerPrefabIdentity identity =
                instance.GetComponent<InvectorBrawlerPrefabIdentity>();
            if (subject == null || identity == null ||
                !string.Equals(identity.RosterId, roster, StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    "The instantiated production prefab identity does not match roster " +
                    roster + ".");
            }

            InvectorBrawlerWeaponPresentation presenter =
                instance.GetComponent<InvectorBrawlerWeaponPresentation>();
            Animator animator = presenter != null
                ? presenter.ConfiguredAnimator : null;
            if (presenter == null || animator == null || !animator.isHuman)
                throw new InvalidOperationException(
                    "The production calibration prefab has no configured Humanoid presenter.");

            // No gameplay authority is needed in this lab. The Humanoid
            // Animator and visual-only presenter are enabled explicitly below.
            subject.enabled = false;
            PlayerBrawlerInput playerInput =
                instance.GetComponent<PlayerBrawlerInput>();
            if (playerInput != null) playerInput.enabled = false;
            AIBrawler ai = instance.GetComponent<AIBrawler>();
            if (ai != null) ai.enabled = false;
            InvectorShooterMeleeInputAdapter adapter =
                instance.GetComponent<InvectorShooterMeleeInputAdapter>();
            if (adapter != null) adapter.enabled = false;
            animator.enabled = true;
            presenter.enabled = true;

            // Production variants are intentionally authored inactive until
            // selected by GameFlow. This isolated lab is that explicit owner,
            // so activate only the already verified sole instance after all
            // gameplay/input/AI authority has been closed.
            instance.SetActive(true);
            subjectContainer.SetActive(true);

            InvectorBrawlerPrefabIdentity[] identities =
                subjectContainer.GetComponentsInChildren<
                    InvectorBrawlerPrefabIdentity>(true);
            if (labScene.rootCount != 1 ||
                subjectContainer.transform.childCount != 1 ||
                identities.Length != 1 || identities[0].gameObject != instance)
            {
                throw new InvalidOperationException(
                    "The isolated calibration lab must contain exactly one production " +
                    "prefab. sceneRoots=" + labScene.rootCount +
                    ", containerChildren=" + subjectContainer.transform.childCount +
                    ", identities=" + identities.Length +
                    ", identityPaths=" + IdentityPaths(identities) +
                    ", instancePath=" + HierarchyPath(instance.transform) + ".");
            }
        }

        static void TryFinish(string roster)
        {
            if (subject == null || !subject.gameObject.activeInHierarchy)
                throw new InvalidOperationException(
                    "The isolated calibration subject was destroyed or disabled.");

            InvectorBrawlerWeaponPresentation presenter =
                subject.GetComponent<InvectorBrawlerWeaponPresentation>();
            Animator animator = presenter != null
                ? presenter.ConfiguredAnimator : null;
            if (presenter == null || animator == null || !animator.enabled ||
                !animator.isActiveAndEnabled || !animator.isHuman ||
                !animator.isInitialized)
                return;

            if (!presenter.RuntimeEnabled)
            {
                if (!presenter.EnableRuntime())
                    throw new InvalidOperationException(
                        "The production weapon presenter failed to enable in the isolated lab.");
                presenterEnabledAtFrame = Time.frameCount;
                return;
            }
            if (Time.frameCount <= presenterEnabledAtFrame ||
                presenter.GatedLateUpdateCount <= 0)
                return;

            if (recorder == null)
            {
                recorder =
                    subject.gameObject.AddComponent<GameplayProbeRecorder>();
                recorder.subject = subject;
                recorder.StartRecording(Path.Combine(
                    ProjectRoot,
                    "Automation",
                    "probe_ik-calibration-" + roster));
            }

            // This is the only Invector settings action permitted here. Never
            // import or modify ProjectSettings from the vendor onboarding UI.
            Invector.vCharacterController.vCheckForProjectSettings.isClosed = true;

            BrawlInvectorIKCalibrationBridge bridge =
                BrawlInvectorIKCalibrationBridge.Bind(subject);
            if (bridge.CurrentWeaponIK == null || bridge.CurrentIKAdjust == null ||
                !string.Equals(
                    presenter.WeaponCategory,
                    InvectorMigrationPilotBuilder.WeaponCategory,
                    StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    "The stock IK window did not resolve the project wizard-staff record.");
            }

            Transform supportTarget = presenter.SupportHandTarget;
            if (supportTarget == null ||
                !supportTarget.IsChildOf(presenter.WeaponVisualRoot))
                throw new InvalidOperationException(
                    "The production presenter has no owned support-hand target.");

            Selection.activeTransform = supportTarget;
            vShooterIKAdjustWindow window = vShooterIKAdjustWindow.curWindow;
            bool supportIsRight = presenter.WeaponHeldInLeftHand;
            vIKSolver supportIK = supportIsRight
                ? bridge.RightIK
                : bridge.LeftIK;
            window.referenceSelected = supportIK.endBone;
            window.selected = supportIK.endBoneOffset;
            window.position = new Rect(1720f, 40f, 760f, 850f);
            window.Repaint();
            SceneView.RepaintAll();

            Vector3 effectiveTarget = Vector3.zero;
            Vector3 effectiveHint = Vector3.zero;
            Quaternion effectiveRotation = Quaternion.identity;
            bool hasSupportPose = presenter.TryGetCurrentSupportIKPose(
                out effectiveTarget, out effectiveRotation, out effectiveHint);
            Transform shoulder = animator.GetBoneTransform(
                supportIsRight
                    ? HumanBodyBones.RightUpperArm
                    : HumanBodyBones.LeftUpperArm);
            Transform elbow = animator.GetBoneTransform(
                supportIsRight
                    ? HumanBodyBones.RightLowerArm
                    : HumanBodyBones.LeftLowerArm);
            Transform hand = animator.GetBoneTransform(
                supportIsRight
                    ? HumanBodyBones.RightHand
                    : HumanBodyBones.LeftHand);
            float supportReachDistance = -1f;
            float supportReachMargin = -1f;
            float supportHintLateral = -1f;
            if (hasSupportPose && shoulder != null && elbow != null && hand != null)
            {
                Vector3 reach = effectiveTarget - shoulder.position;
                supportReachDistance = reach.magnitude;
                float maximum = Vector3.Distance(
                        shoulder.position, elbow.position) +
                    Vector3.Distance(elbow.position, hand.position);
                supportReachMargin = maximum - supportReachDistance;
                if (supportReachDistance > 0.0001f)
                {
                    supportHintLateral = Vector3.ProjectOnPlane(
                        effectiveHint - shoulder.position,
                        reach / supportReachDistance).magnitude;
                }
            }

            if (!hasSupportPose ||
                presenter.LastSuppression !=
                    InvectorWeaponPresentationSuppression.None ||
                !string.IsNullOrEmpty(presenter.LastInvalidPoseStage) ||
                supportReachMargin <= MinimumReadyReachMargin ||
                supportHintLateral < MinimumReadyHintLateral)
            {
                return;
            }

            var contract = new ReadyContract
            {
                ready = true,
                rosterId = roster,
                prefabPath = PrefabPathForRoster(roster),
                sceneName = SceneManager.GetActiveScene().name,
                subjectPath = HierarchyPath(subject.transform),
                supportTargetPath = HierarchyPath(supportTarget),
                weaponCategory = presenter.WeaponCategory,
                ikState = bridge.CurrentIKAdjustState,
                presenterUpdateCount = presenter.GatedLateUpdateCount,
                ikSuppression = presenter.LastSuppression.ToString(),
                invalidIKPoseStage = presenter.LastInvalidPoseStage,
                supportReachDistance = supportReachDistance,
                supportReachMargin = supportReachMargin,
                supportHintLateral = supportHintLateral,
                utc = DateTime.UtcNow.ToString("O"),
            };
            File.WriteAllText(
                ContractPath(roster, true),
                JsonUtility.ToJson(contract, true));
            SessionState.EraseString(PendingRosterKey);
            EditorApplication.isPaused = true;
        }

        static void Fail(string roster, Exception exception)
        {
            var contract = new FailureContract
            {
                ready = false,
                rosterId = roster,
                error = exception.GetType().Name + ": " + exception.Message,
                utc = DateTime.UtcNow.ToString("O"),
            };
            File.WriteAllText(
                ContractPath(roster, false),
                JsonUtility.ToJson(contract, true));
            SessionState.EraseString(PendingRosterKey);
            Debug.LogError("[BrawlIKCalibrationBootstrap] " + contract.error);
            EditorApplication.isPaused = true;
        }

        static void OnPlayModeStateChanged(PlayModeStateChange state)
        {
            if (state == PlayModeStateChange.EnteredEditMode)
                TryRestoreOriginalScene();
        }

        static void TryRestoreOriginalScene()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode ||
                !SessionState.GetBool(RestoreSceneKey, false))
                return;

            string originalPath =
                SessionState.GetString(OriginalSceneKey, string.Empty);

            // Restore synchronously before clearing SessionState. A delayed
            // delegate can be lost if calibration work triggers an immediate
            // assembly reload after exiting Play Mode.
            if (!string.IsNullOrEmpty(originalPath))
                EditorSceneManager.OpenScene(originalPath, OpenSceneMode.Single);
            else
                EditorSceneManager.NewScene(
                    NewSceneSetup.EmptyScene, NewSceneMode.Single);

            SessionState.SetBool(RestoreSceneKey, false);
            SessionState.EraseString(OriginalSceneKey);
            SessionState.EraseString(PendingRosterKey);
            subjectContainer = null;
            subject = null;
            recorder = null;
            subjectCreated = false;
            presenterEnabledAtFrame = -1;
            playStartedAt = 0.0;
        }

        static string NormalizeRoster(string value)
        {
            string roster = (value ?? string.Empty).Trim().ToLowerInvariant();
            if (roster == "frost")
                return roster;
            throw new ArgumentException(
                "Calibration roster must be frost.", nameof(value));
        }

        static string PrefabPathForRoster(string roster)
        {
            switch (roster)
            {
                case "frost":
                    return InvectorRimeMigrationBuilder.ProductionHumanPrefabPath;
                default:
                    throw new ArgumentOutOfRangeException(nameof(roster));
            }
        }

        static string DisplayNameForRoster(string roster)
        {
            switch (roster)
            {
                case "frost": return "Rime";
                default: return roster;
            }
        }

        static string HierarchyPath(Transform target)
        {
            string path = target.name;
            while (target.parent != null)
            {
                target = target.parent;
                path = target.name + "/" + path;
            }
            return path;
        }

        static string IdentityPaths(InvectorBrawlerPrefabIdentity[] identities)
        {
            if (identities == null || identities.Length == 0) return "<none>";
            string value = string.Empty;
            for (int i = 0; i < identities.Length; i++)
            {
                if (i > 0) value += ";";
                value += identities[i] != null
                    ? HierarchyPath(identities[i].transform) + "@" + identities[i].RosterId
                    : "<null>";
            }
            return value;
        }

        static string ProjectRoot =>
            Directory.GetParent(Application.dataPath).FullName;

        static string ContractPath(string roster, bool ready)
        {
            string file = ready
                ? "ik-calibration-ready-" + roster + ".json"
                : "ik-calibration-failed-" + roster + ".json";
            return Path.Combine(ProjectRoot, "Automation", file);
        }

        static void ClearContract(string roster, bool ready)
        {
            string path = ContractPath(roster, ready);
            if (File.Exists(path)) File.Delete(path);
        }
    }
}
