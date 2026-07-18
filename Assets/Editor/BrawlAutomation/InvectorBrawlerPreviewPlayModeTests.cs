using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;

namespace BrawlArena.EditorAutomation.Tests
{
    /// <summary>
    /// Bounded live proof for the fail-closed semantic preview boundary. The
    /// test intentionally opens no Brawler runtime gate or vendor scheduler.
    /// </summary>
    public sealed class InvectorBrawlerPreviewPlayModeTests
    {
        const int PoseFrames = 8;
        const int VictoryPollFrames = 180;
        const float PoseTolerance = 0.005f;

        [UnityTest]
        [Category("InvectorThornPresentation")]
        public IEnumerator RosterPreviewsKeepExactIdentityEquipmentAndLifecycleBoundary()
        {
            yield return new EnterPlayMode();
            string[] expectedIds = { "fire", "frost", "storm", "thorn" };
            string[] expectedPaths =
            {
                InvectorMigrationPilotBuilder.ProductionHumanPrefabPath,
                InvectorRimeMigrationBuilder.ProductionHumanPrefabPath,
                InvectorTempestMigrationBuilder.ProductionHumanPrefabPath,
                InvectorThornMigrationBuilder.ProductionHumanPrefabPath,
            };
            var failures = new List<string>();
            BrawlerDefinition[] roster =
                ArenaSceneBuilder.BuildRosterFromExistingAssets();

            for (int roleIndex = 0; roleIndex < expectedIds.Length; roleIndex++)
            {
                string id = expectedIds[roleIndex];
                bool thorn = id == InvectorThornMigrationBuilder.RosterId;
                BrawlerDefinition definition = roster.SingleOrDefault(value =>
                    value != null && value.id == id);
                GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(
                    expectedPaths[roleIndex]);
                if (definition == null || prefab == null ||
                    definition.invectorHumanPrefab != prefab)
                {
                    failures.Add(id + ": exact production Human asset did not resolve.");
                    continue;
                }

                GameObject preview = Object.Instantiate(prefab);
                InvectorBowPresentationRig bowRig =
                    preview.GetComponent<InvectorBowPresentationRig>();
                bool authoredArrowWasActive = bowRig != null &&
                    bowRig.ArrowVisual != null &&
                    bowRig.ArrowVisual.gameObject.activeSelf;
                BrawlerPreviewAdapter.Prepare(preview, definition);
                BrawlerPreviewAdapter.ShowIdle(preview, definition, 0.3f);

                for (int frame = 0; frame < PoseFrames; frame++)
                    yield return null;

                Animator animator = preview.GetComponent<Animator>();
                InvectorBrawlerWeaponPresentation presenter =
                    preview.GetComponent<InvectorBrawlerWeaponPresentation>();
                BrawlInvectorThirdPersonController controller =
                    preview.GetComponent<BrawlInvectorThirdPersonController>();
                InvectorShooterMeleeInputAdapter input =
                    preview.GetComponent<InvectorShooterMeleeInputAdapter>();
                InvectorHumanRuntimeGate humanGate =
                    preview.GetComponent<InvectorHumanRuntimeGate>();
                InvectorBrawlerPrefabIdentity identity =
                    preview.GetComponent<InvectorBrawlerPrefabIdentity>();
                bool exactIdentity = identity != null &&
                    identity.Matches(id, InvectorBrawlerPrefabRole.Human);
                bool exactEquipment = presenter != null &&
                    presenter.WeaponCategory == (thorn
                        ? InvectorThornMigrationBuilder.WeaponCategory
                        : InvectorMigrationPilotBuilder.WeaponCategory) &&
                    presenter.WeaponHeldInLeftHand == thorn &&
                    presenter.HasAuthoredPreviewPose == thorn &&
                    presenter.HideAuthoredArrowInPreview == thorn &&
                    presenter.WeaponVisualRoot != null &&
                    presenter.WeaponVisualRoot.GetComponentsInChildren<Renderer>(true).Length > 0;
                bool previewGate = animator != null && animator.enabled &&
                    presenter != null && presenter.enabled &&
                    presenter.PreviewEnabled && !presenter.RuntimeEnabled;
                bool gameplayGatesClosed = controller != null && !controller.enabled &&
                    input != null && !input.enabled &&
                    humanGate != null && !humanGate.enabled;
                bool behaviourCensus = preview
                    .GetComponentsInChildren<Behaviour>(true)
                    .All(behaviour => behaviour.enabled ==
                        (behaviour == animator || behaviour == presenter));
                bool poseFaultFree = presenter != null &&
                    presenter.InvalidPoseCount == 0 && presenter.RuntimeFaultCount == 0;
                bool poseApplied = presenter.AppliedIKPassCount > 0;
                bool thornPose = !thorn ||
                    (presenter.PreviewPoseApplyCount > 0 &&
                     presenter.LastPreviewWeaponHandDistance < PoseTolerance &&
                     presenter.LastPreviewSupportHandDistance < PoseTolerance);
                bool thornArrowHidden = !thorn ||
                    (bowRig != null && bowRig.ArrowVisual != null &&
                     !bowRig.ArrowVisual.gameObject.activeSelf);

                int fullBodyLayer = animator.GetLayerIndex("FullBody");
                int idleFullBodyHash = animator
                    .GetCurrentAnimatorStateInfo(fullBodyLayer).fullPathHash;
                bool idleObserved = idleFullBodyHash != 0;
                BrawlerPreviewAdapter.ShowVictory(preview, definition);
                bool victoryObserved = false;
                bool victoryDistinct = false;
                for (int frame = 0; frame < VictoryPollFrames; frame++)
                {
                    animator.Update(1f / 60f);
                    yield return null;
                    AnimatorStateInfo current =
                        animator.GetCurrentAnimatorStateInfo(fullBodyLayer);
                    AnimatorStateInfo next =
                        animator.GetNextAnimatorStateInfo(fullBodyLayer);
                    victoryObserved =
                        current.fullPathHash ==
                            BrawlInvectorLifecycleParameters.VictoryState ||
                        (animator.IsInTransition(fullBodyLayer) &&
                         next.fullPathHash ==
                            BrawlInvectorLifecycleParameters.VictoryState);
                    if (victoryObserved)
                    {
                        victoryDistinct = current.fullPathHash != idleFullBodyHash ||
                            next.fullPathHash != idleFullBodyHash;
                        break;
                    }
                }

                for (int frame = 0; frame < 4; frame++)
                    yield return null;
                bool victorySuppressedIK = presenter.LastSuppression ==
                    InvectorWeaponPresentationSuppression.LifecycleState;
                thornArrowHidden &= !thorn ||
                    !bowRig.ArrowVisual.gameObject.activeSelf;
                poseFaultFree &= presenter.InvalidPoseCount == 0 &&
                    presenter.RuntimeFaultCount == 0 && presenter.PreviewEnabled &&
                    !presenter.RuntimeEnabled;

                bool previewFailClosed = true;
                if (thorn)
                {
                    MethodInfo failClosed = typeof(InvectorBrawlerWeaponPresentation)
                        .GetMethod("FailClosed",
                            BindingFlags.Instance | BindingFlags.NonPublic);
                    failClosed?.Invoke(presenter, new object[]
                    {
                        InvectorWeaponPresentationSuppression.RuntimeFault,
                        new System.InvalidOperationException(
                            "Preview fail-closed regression proof."),
                    });
                    previewFailClosed = failClosed != null &&
                        !presenter.PreviewEnabled && !presenter.RuntimeEnabled &&
                        !presenter.enabled && presenter.RuntimeFaultCount == 1 &&
                        bowRig.ArrowVisual.gameObject.activeSelf ==
                            authoredArrowWasActive;
                }

                if (!exactIdentity || !exactEquipment || !previewGate ||
                    !gameplayGatesClosed || !behaviourCensus || !idleObserved ||
                    !poseApplied || !poseFaultFree || !thornPose ||
                    !thornArrowHidden ||
                    !victoryObserved || !victoryDistinct ||
                    !victorySuppressedIK || !previewFailClosed)
                {
                    failures.Add($"{id}: identity={exactIdentity}, " +
                        $"equipment={exactEquipment}, preview={previewGate}, " +
                        $"gates={gameplayGatesClosed}, behaviours={behaviourCensus}, " +
                        $"idle={idleObserved}, poseApplied={poseApplied}, " +
                        $"thornPose={thornPose}, " +
                        $"posePasses={presenter.PreviewPoseApplyCount}, " +
                    $"weaponError={presenter.LastPreviewWeaponHandDistance:F6}, " +
                    $"supportError={presenter.LastPreviewSupportHandDistance:F6}, " +
                    $"invalid={presenter.InvalidPoseCount}, " +
                        $"faultFree={poseFaultFree}, arrowHidden={thornArrowHidden}, " +
                    $"victory={victoryObserved}, lifecycleSuppression={victorySuppressedIK}, " +
                        $"failClosed={previewFailClosed}");
                }

                Object.Destroy(preview);
                yield return null;
            }
            bool rosterOrderExact =
                roster.Select(value => value.id).SequenceEqual(expectedIds);
            bool failuresEmpty = failures.Count == 0;
            string failureEvidence = string.Join("\n", failures);
            yield return new ExitPlayMode();

            Assert.That(rosterOrderExact, Is.True,
                "Existing-asset roster order changed.");
            Assert.That(failuresEmpty, Is.True, failureEvidence);
        }
    }
}
