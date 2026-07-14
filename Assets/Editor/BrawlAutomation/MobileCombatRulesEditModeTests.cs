using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace BrawlArena.EditorAutomation
{
    public class MobileCombatRulesEditModeTests
    {
        const float Tolerance = 0.0001f;

        [Test]
        public void ArcaneFlowCapacityIsFixedAtSixty()
        {
            Assert.That(MobileCombatRules.ArcaneFlowCapacity, Is.EqualTo(60f));
        }

        [Test]
        public void ThreeWardStepsSpendAllFlowAndFourthStepFails()
        {
            float flow = MobileCombatRules.ArcaneFlowCapacity;

            Assert.IsTrue(MobileCombatRules.TrySpendWardFlow(ref flow));
            Assert.That(flow, Is.EqualTo(40f).Within(Tolerance));
            Assert.IsTrue(MobileCombatRules.TrySpendWardFlow(ref flow));
            Assert.That(flow, Is.EqualTo(20f).Within(Tolerance));
            Assert.IsTrue(MobileCombatRules.TrySpendWardFlow(ref flow));
            Assert.That(flow, Is.EqualTo(0f).Within(Tolerance));

            Assert.IsFalse(MobileCombatRules.TrySpendWardFlow(ref flow));
            Assert.That(flow, Is.EqualTo(0f).Within(Tolerance));
        }

        [Test]
        public void WardFlowWaitsForDelayThenRegeneratesAtEightPerSecondAndClamps()
        {
            Assert.That(MobileCombatRules.WardRegenDelay, Is.EqualTo(0.75f));
            Assert.That(MobileCombatRules.WardRegenPerSecond, Is.EqualTo(8f));

            float flow = 0f;
            flow = AdvanceFlowAfterDelay(flow,
                MobileCombatRules.WardRegenDelay - 0.01f, 1f);
            Assert.That(flow, Is.EqualTo(0f).Within(Tolerance),
                "Flow must remain empty during the post-step regeneration delay.");

            flow = AdvanceFlowAfterDelay(flow,
                MobileCombatRules.WardRegenDelay, 1f);
            Assert.That(flow, Is.EqualTo(8f).Within(Tolerance));

            flow = AdvanceFlowAfterDelay(flow,
                MobileCombatRules.WardRegenDelay, 20f);
            Assert.That(flow,
                Is.EqualTo(MobileCombatRules.ArcaneFlowCapacity).Within(Tolerance));
        }

        [Test]
        public void CastMovementMultiplierIsEightyPercent()
        {
            Assert.That(MobileCombatRules.CastMovementMultiplier, Is.EqualTo(0.8f));
        }

        [Test]
        public void AutoAimCorrectionIsLimitedToTwelveDegrees()
        {
            Vector3 corrected = MobileCombatRules.LimitAimCorrection(
                Vector3.forward, Vector3.right);

            Assert.That(Vector3.Angle(Vector3.forward, corrected),
                Is.EqualTo(12f).Within(0.001f));
            Assert.That(corrected.magnitude, Is.EqualTo(1f).Within(Tolerance));
            Assert.That(corrected.y, Is.EqualTo(0f).Within(Tolerance));
        }

        [Test]
        public void ZeroCorrectionPreservesCommittedManualDirection()
        {
            Vector3 committed = new Vector3(0.3f, 0f, 0.7f).normalized;
            Vector3 corrected = MobileCombatRules.LimitAimCorrection(
                committed, Vector3.left, 0f);

            Assert.That(Vector3.Angle(committed, corrected),
                Is.EqualTo(0f).Within(0.001f));
        }

        [Test]
        public void HeldAttackGestureProducesNoReleaseIntent()
        {
            CreatePointerFixture(out GameObject root, out EventSystem eventSystem,
                out AttackButtonWidget widget);
            try
            {
                widget.OnPointerDown(Pointer(eventSystem, 7, new Vector2(100f, 100f)));

                Assert.IsTrue(widget.ConsumePressed());
                Assert.IsFalse(widget.ConsumePressed(), "A press is a one-shot edge.");
                Assert.IsTrue(widget.Held);
                Assert.IsFalse(widget.ConsumeReleased(out _),
                    "Holding across cooldowns must never create a cast intent.");

                widget.OnDrag(Pointer(eventSystem, 7, new Vector2(220f, 100f)));
                Assert.IsFalse(widget.ConsumeReleased(out _));
            }
            finally
            {
                Object.DestroyImmediate(root);
            }
        }

        [Test]
        public void AttackReleaseIsConsumableExactlyOnceAndUsesFinalPointerPosition()
        {
            CreatePointerFixture(out GameObject root, out EventSystem eventSystem,
                out AttackButtonWidget widget);
            try
            {
                widget.OnPointerDown(Pointer(eventSystem, 3, new Vector2(20f, 30f)));
                Assert.IsTrue(widget.ConsumePressed());
                widget.OnPointerUp(Pointer(eventSystem, 3, new Vector2(120f, 80f)));

                Assert.IsTrue(widget.ConsumeReleased(out Vector2 drag));
                Assert.That(drag, Is.EqualTo(new Vector2(100f, 50f)));
                Assert.IsFalse(widget.ConsumeReleased(out _));
                Assert.IsFalse(widget.Held);

                widget.OnPointerDown(Pointer(eventSystem, 3, new Vector2(40f, 40f)));
                Assert.IsTrue(widget.ConsumePressed(),
                    "A consumed release must admit the next action tap.");
            }
            finally
            {
                Object.DestroyImmediate(root);
            }
        }

        [Test]
        public void ForeignPointerCannotCompleteAttackGesture()
        {
            CreatePointerFixture(out GameObject root, out EventSystem eventSystem,
                out AttackButtonWidget widget);
            try
            {
                widget.OnPointerDown(Pointer(eventSystem, 1, Vector2.zero));
                widget.OnDrag(Pointer(eventSystem, 2, new Vector2(300f, 0f)));
                widget.OnPointerUp(Pointer(eventSystem, 2, new Vector2(300f, 0f)));

                Assert.IsTrue(widget.Held);
                Assert.IsFalse(widget.ConsumeReleased(out _));

                widget.OnPointerUp(Pointer(eventSystem, 1, new Vector2(80f, 0f)));
                Assert.IsTrue(widget.ConsumeReleased(out Vector2 drag));
                Assert.That(drag, Is.EqualTo(new Vector2(80f, 0f)));
            }
            finally
            {
                Object.DestroyImmediate(root);
            }
        }

        [Test]
        public void SecondTouchCancelsAttackAndTakesOverAsCameraOrbit()
        {
            CreatePointerFixture(out GameObject root, out EventSystem eventSystem,
                out AttackButtonWidget widget);
            try
            {
                widget.cameraOrbitEnabled = true;
                widget.OnPointerDown(Pointer(eventSystem, 11, new Vector2(100f, 100f)));
                Assert.IsTrue(widget.ConsumePressed());
                Assert.IsTrue(widget.Held);

                widget.OnPointerDown(Pointer(eventSystem, 12, new Vector2(300f, 100f)));

                Assert.IsFalse(widget.Held, "The pending one-finger cast must be cancelled.");
                Assert.IsTrue(widget.OrbitHeld, "The second touch owns camera orbit.");
                Assert.IsTrue(widget.ConsumeCancelled());
                Assert.IsFalse(widget.ConsumeCancelled(), "Cancellation is a one-shot edge.");
                Assert.IsFalse(widget.ConsumeReleased(out _),
                    "Camera takeover must never masquerade as a tap release.");

                widget.OnPointerUp(Pointer(eventSystem, 11, new Vector2(180f, 100f)));
                Assert.IsTrue(widget.OrbitHeld,
                    "Releasing the cancelled attack pointer must not end the orbit.");
                widget.OnPointerUp(Pointer(eventSystem, 12, new Vector2(340f, 120f)));
                Assert.IsFalse(widget.OrbitHeld);
                Assert.IsFalse(widget.ConsumeReleased(out _));
            }
            finally
            {
                Object.DestroyImmediate(root);
            }
        }

        [TestCase(PointerEventData.InputButton.Right)]
        [TestCase(PointerEventData.InputButton.Middle)]
        public void ExplicitMouseOrbitNeverQueuesAttack(PointerEventData.InputButton button)
        {
            CreatePointerFixture(out GameObject root, out EventSystem eventSystem,
                out AttackButtonWidget widget);
            try
            {
                var cameraObject = new GameObject("OrbitCamera");
                cameraObject.transform.SetParent(root.transform);
                BrawlCamera orbitCamera = cameraObject.AddComponent<BrawlCamera>();
                FieldInfo cameraField = typeof(AttackButtonWidget).GetField("cameraController",
                    BindingFlags.Instance | BindingFlags.NonPublic);
                FieldInfo yawField = typeof(BrawlCamera).GetField("yaw",
                    BindingFlags.Instance | BindingFlags.NonPublic);
                Assert.NotNull(cameraField);
                Assert.NotNull(yawField);
                cameraField.SetValue(widget, orbitCamera);
                float yawBefore = (float)yawField.GetValue(orbitCamera);

                widget.cameraOrbitEnabled = true;
                widget.OnPointerDown(Pointer(eventSystem, -1, Vector2.zero, button));

                Assert.IsTrue(widget.OrbitHeld);
                Assert.IsFalse(widget.Held);
                Assert.IsFalse(widget.ConsumePressed());
                Assert.IsFalse(widget.ConsumeCancelled());
                Assert.IsFalse(widget.ConsumeReleased(out _));

                PointerEventData drag = Pointer(eventSystem, -1, new Vector2(80f, 30f), button);
                drag.delta = new Vector2(80f, 30f);
                widget.OnDrag(drag);
                Assert.That((float)yawField.GetValue(orbitCamera), Is.Not.EqualTo(yawBefore),
                    "The explicit camera gesture must reach BrawlCamera.AddOrbit.");
                widget.OnPointerUp(Pointer(eventSystem, -1, new Vector2(80f, 30f), button));

                Assert.IsFalse(widget.OrbitHeld);
                Assert.IsFalse(widget.ConsumeReleased(out _));
            }
            finally
            {
                Object.DestroyImmediate(root);
            }
        }

        [Test]
        public void GenericActionWidgetDoesNotEnableCameraTakeover()
        {
            CreatePointerFixture(out GameObject root, out EventSystem eventSystem,
                out AttackButtonWidget widget);
            try
            {
                widget.OnPointerDown(Pointer(eventSystem, 21, Vector2.zero));
                Assert.IsTrue(widget.ConsumePressed());

                widget.OnPointerDown(Pointer(eventSystem, 22, new Vector2(60f, 0f)));

                Assert.IsTrue(widget.Held,
                    "Ward/Ritual widgets must retain ownership of their first pointer.");
                Assert.IsFalse(widget.OrbitHeld);
                Assert.IsFalse(widget.ConsumeCancelled());

                widget.OnPointerUp(Pointer(eventSystem, 21, new Vector2(40f, 0f)));
                Assert.IsTrue(widget.ConsumeReleased(out Vector2 drag));
                Assert.That(drag, Is.EqualTo(new Vector2(40f, 0f)));

                widget.OnPointerDown(Pointer(eventSystem, -1, Vector2.zero,
                    PointerEventData.InputButton.Right));
                Assert.IsFalse(widget.Held);
                Assert.IsFalse(widget.OrbitHeld);
                Assert.IsFalse(widget.ConsumePressed(),
                    "Right-click over Ward/Ritual artwork must not activate it.");
            }
            finally
            {
                Object.DestroyImmediate(root);
            }
        }

        [Test]
        public void RightCastSurfaceOwnsHalfScreenAndActionButtonsStayAboveIt()
        {
            var hudObject = new GameObject("HudLayoutTest");
            hudObject.SetActive(false);
            var hud = hudObject.AddComponent<BrawlHUD>();
            var gameplay = new GameObject("GameplayRoot", typeof(RectTransform));
            try
            {
                InvokeHudBuilder(hud, "BuildAttackButton", gameplay.transform);
                InvokeHudBuilder(hud, "BuildWardStepControls", gameplay.transform);
                InvokeHudBuilder(hud, "BuildSuperButton", gameplay.transform);

                RectTransform surface = hud.RightCastSurface;
                Assert.NotNull(surface);
                Assert.That(surface.anchorMin, Is.EqualTo(new Vector2(0.5f, 0f)));
                Assert.That(surface.anchorMax, Is.EqualTo(Vector2.one));
                Assert.IsTrue(surface.GetComponent<Image>().raycastTarget);
                Assert.IsTrue(surface.GetComponent<AttackButtonWidget>().cameraOrbitEnabled);

                Transform cast = gameplay.transform.Find("CastButton");
                Transform ward = gameplay.transform.Find("WardStepButton");
                Transform ritual = gameplay.transform.Find("RitualButton");
                Assert.NotNull(cast);
                Assert.NotNull(ward);
                Assert.NotNull(ritual);
                Assert.IsFalse(cast.GetComponent<Image>().raycastTarget,
                    "The CAST orb is an affordance, not a second gesture owner.");
                Assert.IsNull(cast.GetComponent<Button>());
                Assert.IsNull(cast.GetComponent<AttackButtonWidget>());

                Assert.Greater(ward.GetSiblingIndex(), surface.GetSiblingIndex());
                Assert.Greater(ritual.GetSiblingIndex(), surface.GetSiblingIndex());
                Assert.IsTrue(ward.GetComponent<Image>().raycastTarget);
                Assert.IsTrue(ritual.GetComponent<Image>().raycastTarget);
                Assert.IsFalse(ward.GetComponent<AttackButtonWidget>().cameraOrbitEnabled);
                Assert.IsFalse(ritual.GetComponent<AttackButtonWidget>().cameraOrbitEnabled);
            }
            finally
            {
                Object.DestroyImmediate(gameplay);
                Object.DestroyImmediate(hudObject);
            }
        }

        static float AdvanceFlowAfterDelay(float current,
            float elapsedSinceSpend, float deltaSeconds)
        {
            if (elapsedSinceSpend < MobileCombatRules.WardRegenDelay)
                return current;

            return MobileCombatRules.RegenerateWardFlow(current,
                MobileCombatRules.ArcaneFlowCapacity,
                MobileCombatRules.WardRegenPerSecond,
                deltaSeconds);
        }

        static void CreatePointerFixture(out GameObject root, out EventSystem eventSystem,
            out AttackButtonWidget widget)
        {
            root = new GameObject("AttackGestureTestRoot");
            eventSystem = root.AddComponent<EventSystem>();
            var button = new GameObject("AttackButton");
            button.transform.SetParent(root.transform);
            widget = button.AddComponent<AttackButtonWidget>();
        }

        static void InvokeHudBuilder(BrawlHUD hud, string methodName, Transform root)
        {
            MethodInfo method = typeof(BrawlHUD).GetMethod(methodName,
                BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.NotNull(method, methodName + " builder should exist.");
            method.Invoke(hud, new object[] { root });
        }

        static PointerEventData Pointer(EventSystem eventSystem, int pointerId, Vector2 position,
            PointerEventData.InputButton button = PointerEventData.InputButton.Left)
        {
            return new PointerEventData(eventSystem)
            {
                pointerId = pointerId,
                position = position,
                button = button,
            };
        }
    }
}
