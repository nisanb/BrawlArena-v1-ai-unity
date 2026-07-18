using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;

namespace BrawlArena.EditorAutomation
{
    /// <summary>
    /// Scene-free coverage for BrawlerMotionFlourish's pure presentation math
    /// and its visual-anchor/blob-shadow resolution on a synthetic hierarchy.
    /// Every target here is a private (static) member reached via reflection,
    /// matching this codebase's existing EditMode test convention rather than
    /// loosening access modifiers purely for testability.
    /// </summary>
    public class MotionFlourishEditModeTests
    {
        readonly List<GameObject> testObjects = new List<GameObject>();

        [TearDown]
        public void TearDown()
        {
            for (int i = testObjects.Count - 1; i >= 0; i--)
                if (testObjects[i] != null) Object.DestroyImmediate(testObjects[i]);
            testObjects.Clear();
        }

        [Test]
        public void BobAmplitudeScalesWithSpeedAndClampsAtFullSpeed()
        {
            float atZero = InvokeStatic<float>("ComputeBobAmplitude", 0f, 1f);
            float atQuarter = InvokeStatic<float>("ComputeBobAmplitude", 0.25f, 1f);
            float atHalf = InvokeStatic<float>("ComputeBobAmplitude", 0.5f, 1f);
            float atFull = InvokeStatic<float>("ComputeBobAmplitude", 1f, 1f);
            float pastFull = InvokeStatic<float>("ComputeBobAmplitude", 2f, 1f);
            float authoredAmplitude = GetConstFloat("RunBobAmplitude");

            Assert.AreEqual(0f, atZero, 1e-5f, "Standing still must not bob.");
            Assert.Less(atQuarter, atHalf, "Bob amplitude must grow with speed.");
            Assert.Less(atHalf, atFull, "Bob amplitude must grow with speed.");
            Assert.AreEqual(authoredAmplitude, atFull, 1e-5f,
                "Full speed must reach exactly the authored peak amplitude.");
            Assert.AreEqual(atFull, pastFull, 1e-5f,
                "Speed fractions above 1 must clamp rather than overshoot the peak.");
        }

        [Test]
        public void BobAmplitudeHalvesUnderReducedMotion()
        {
            float full = InvokeStatic<float>("ComputeBobAmplitude", 1f, 1f);
            float reduced = InvokeStatic<float>("ComputeBobAmplitude", 1f, 0.5f);

            Assert.AreEqual(full * 0.5f, reduced, 1e-5f,
                "Reduced motion must halve amplitude, not disable the flourish.");
        }

        [Test]
        public void AccelerationLeanClampsToAuthoredForwardAndBrakeMaxDegrees()
        {
            float accelMax = GetConstFloat("AccelLeanMaxDeg");
            float brakeMax = GetConstFloat("BrakeLeanMaxDeg");

            float atRest = InvokeStatic<float>("ComputeLeanTargetDeg", 0f, 1f);
            float hardAccelerate = InvokeStatic<float>("ComputeLeanTargetDeg", 1000f, 1f);
            float hardBrake = InvokeStatic<float>("ComputeLeanTargetDeg", -1000f, 1f);

            Assert.AreEqual(0f, atRest, 1e-5f);
            Assert.AreEqual(accelMax, hardAccelerate, 1e-4f,
                "Large forward acceleration must clamp at the authored forward-lean cap.");
            Assert.AreEqual(-brakeMax, hardBrake, 1e-4f,
                "Large braking/backward acceleration must clamp at the authored brake-lean cap.");
        }

        [Test]
        public void TurnBankClampsToAuthoredMaxDegreesAndOpposesYawDirection()
        {
            float bankMax = GetConstFloat("TurnBankMaxDeg");

            float atRest = InvokeStatic<float>("ComputeTurnBankTargetDeg", 0f, 1f);
            float turningRight = InvokeStatic<float>("ComputeTurnBankTargetDeg", 1000f, 1f);
            float turningLeft = InvokeStatic<float>("ComputeTurnBankTargetDeg", -1000f, 1f);

            Assert.AreEqual(0f, atRest, 1e-5f);
            Assert.AreEqual(-bankMax, turningRight, 1e-4f);
            Assert.AreEqual(bankMax, turningLeft, 1e-4f);
        }

        [Test]
        public void ResolveVisualRootFallsBackToFirstRendererChildWithoutHumanoidAnimator()
        {
            BrawlerController owner = CreateInactiveOwner("VisualRootOwner");

            GameObject mesh = new GameObject("Mesh");
            testObjects.Add(mesh);
            mesh.transform.SetParent(owner.transform, false);
            mesh.AddComponent<MeshRenderer>();

            Transform resolved = InvokeStatic<Transform>("ResolveVisualRoot", owner);

            Assert.AreSame(mesh.transform, resolved,
                "With no Humanoid Animator, the first renderer-bearing child must be used.");
        }

        [Test]
        public void ResolveVisualRootReturnsNullWithNoChildren()
        {
            BrawlerController owner = CreateInactiveOwner("VisualRootOwnerEmpty");

            Transform resolved = InvokeStatic<Transform>("ResolveVisualRoot", owner);

            Assert.IsNull(resolved,
                "With no distinct visual child, there is nothing safe to offset.");
        }

        [Test]
        public void EnsureBlobShadowIsIdempotentOnASyntheticHierarchy()
        {
            BrawlerController owner = CreateInactiveOwner("BlobShadowOwner");

            InvokeStaticVoid("EnsureBlobShadow", owner);
            InvokeStaticVoid("EnsureBlobShadow", owner);

            int matches = 0;
            for (int i = 0; i < owner.transform.childCount; i++)
                if (owner.transform.GetChild(i).name == "BlobShadow") matches++;

            Assert.AreEqual(1, matches,
                "A second Configure/Ensure pass must not create a duplicate blob shadow.");

            Transform blob = owner.transform.Find("BlobShadow");
            Assert.NotNull(blob);
            Assert.IsNull(blob.GetComponent<Collider>(),
                "The blob shadow must not carry a collider (visual-only, non-gameplay).");
        }

        BrawlerController CreateInactiveOwner(string name)
        {
            // Awake() must not run here: EnsureMotorSelected() would throw
            // without a configured motor, and neither static helper under
            // test needs the actor to be initialized.
            GameObject go = new GameObject(name);
            go.SetActive(false);
            testObjects.Add(go);
            return go.AddComponent<BrawlerController>();
        }

        static T InvokeStatic<T>(string methodName, params object[] arguments)
        {
            MethodInfo method = FindMethod(methodName);
            return (T)method.Invoke(null, arguments);
        }

        static void InvokeStaticVoid(string methodName, params object[] arguments)
        {
            MethodInfo method = FindMethod(methodName);
            method.Invoke(null, arguments);
        }

        static MethodInfo FindMethod(string methodName)
        {
            MethodInfo method = typeof(BrawlerMotionFlourish).GetMethod(methodName,
                BindingFlags.Static | BindingFlags.NonPublic);
            Assert.NotNull(method, "Missing expected method: " + methodName);
            return method;
        }

        static float GetConstFloat(string fieldName)
        {
            FieldInfo field = typeof(BrawlerMotionFlourish).GetField(fieldName,
                BindingFlags.Static | BindingFlags.NonPublic);
            Assert.NotNull(field, "Missing expected constant: " + fieldName);
            return (float)field.GetRawConstantValue();
        }
    }
}
