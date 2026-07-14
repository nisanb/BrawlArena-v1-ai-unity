using System;
using System.Collections.Generic;
using System.IO;
using BrawlArena;
using Invector.IK;
using Invector.vShooter;
using NUnit.Framework;
using UnityEngine;

namespace BrawlArena.Editor.Tests
{
    public sealed class InvectorWeaponPresentationRuntimeEditModeTests
    {
        readonly List<UnityEngine.Object> objects = new List<UnityEngine.Object>();

        [TearDown]
        public void TearDown()
        {
            for (int i = objects.Count - 1; i >= 0; i--)
            {
                if (objects[i] != null) UnityEngine.Object.DestroyImmediate(objects[i]);
            }
            objects.Clear();
        }

        [Test]
        public void PresenterImplementsVisualBoundaryAndStaysDormantInEditMode()
        {
            var root = Track(new GameObject("WeaponPresenterRoot"));
            Animator animator = root.AddComponent<Animator>();
            root.AddComponent<Rigidbody>();
            root.AddComponent<CapsuleCollider>();
            var controller = root.AddComponent<BrawlInvectorThirdPersonController>();
            var presenter = root.AddComponent<InvectorBrawlerWeaponPresentation>();

            Transform visual = Child(root.transform, "WeaponVisual");
            Transform muzzle = Child(visual, "SpellOrigin");
            Transform support = Child(visual, "SupportHandTarget");
            Transform hint = Child(visual, "SupportHintTarget");
            vWeaponIKAdjustList list = CreateCompleteIKData("CinderStaff");

            presenter.Configure(
                animator, controller, visual, muzzle, support, hint,
                list, "CinderStaff", false, Array.Empty<ParticleSystem>());

            Assert.IsInstanceOf<IBrawlerWeaponPresentation>(presenter);
            Assert.IsTrue(presenter.IsConfigured);
            Assert.IsTrue(presenter.IsDormantConfigured);
            Assert.IsFalse(presenter.enabled);
            Assert.IsFalse(presenter.EnableLabRuntime(),
                "The explicit runtime gate must reject edit-time activation.");
            Assert.IsFalse(presenter.LabRuntimeEnabled);
            Assert.IsTrue(presenter.IsDormantConfigured);
            Assert.AreEqual(1, presenter.GateEnableFailureCount);
            Assert.AreEqual(0, presenter.RuntimeHelperCount,
                "The explicit-bone solver path must not create hidden helpers.");

            presenter.PresentAim(Vector3.forward);
            Assert.AreEqual(1, presenter.AimRequestCount);
            Assert.AreEqual(1, presenter.DroppedRequestCount);
            Assert.IsFalse(presenter.AimPresented);
            Assert.IsFalse(presenter.TryGetMuzzlePosition(out _));
            Assert.AreEqual(2, presenter.DroppedRequestCount);
        }

        [Test]
        public void HitProxyOwnsExactlyOneLayeredTriggerSphereAndNoCallbacks()
        {
            var root = Track(new GameObject("HitProxy"));
            var proxy = root.AddComponent<BrawlerHitProxy>();

            proxy.Configure(new Vector3(0f, 1f, 0f), 0.65f);

            Collider[] colliders = root.GetComponents<Collider>();
            Assert.AreEqual(1, colliders.Length);
            Assert.AreSame(proxy.TriggerCollider, colliders[0]);
            Assert.IsInstanceOf<SphereCollider>(colliders[0]);
            Assert.IsTrue(colliders[0].isTrigger);
            Assert.AreEqual(CombatPhysics.BrawlerHitboxLayer, root.layer);
            Assert.IsTrue(proxy.IsConfigured);

            string source = ReadProjectFile(
                "Assets/Scripts/Brawl/Integration/Invector/BrawlerHitProxy.cs");
            StringAssert.DoesNotContain("OnTrigger", source);
            StringAssert.DoesNotContain("OnCollision", source);
            StringAssert.DoesNotContain("SetLayerRecursively", source);
        }

        [Test]
        public void PresenterSourceKeepsVendorCombatAndSecondSchedulersTerminal()
        {
            string source = ReadProjectFile(
                "Assets/Scripts/Brawl/Integration/Invector/InvectorBrawlerWeaponPresentation.cs");
            string[] forbiddenTokens =
            {
                "vShooterManager",
                "vShooterWeaponBase",
                ".Shoot(",
                "ReloadWeapon",
                "UseAmmo",
                "AddAmmo",
                "ApplyDamage",
                "SetActiveAttack",
                "base.LateUpdate",
                "Debug.Log",
                "SetLayerRecursively",
                "animator.SetTrigger",
                "animator.SetInteger",
                "animator.SetFloat",
                "CrossFade",
                "void FixedUpdate(",
                "void Update(",
            };
            foreach (string token in forbiddenTokens)
                StringAssert.DoesNotContain(token, source, token);

            Assert.AreEqual(1, CountOccurrences(source, "void LateUpdate()"));
            StringAssert.Contains("if (!runtimeEnabled) return;", source);
            StringAssert.Contains("LabRuntimeEnabled => RuntimeEnabled", source);
            StringAssert.Contains("new vIKSolver(", source);
            StringAssert.Contains("The explicit-bone constructor creates no hidden helper objects.", source);
            StringAssert.DoesNotContain(
                "new vIKSolver(configuredAnimator", source,
                "The Animator constructor allocates hidden helper GameObjects.");
            StringAssert.Contains("HasAnimatorTag(IgnoreIKTag)", source);
            StringAssert.Contains("HasAnimatorTag(IgnoreSupportHandIKTag)", source);
            StringAssert.Contains("IsLifecycleState()", source);
            StringAssert.Contains("IsReachable(solver, targetPosition, hintPosition)", source);
        }

        vWeaponIKAdjustList CreateCompleteIKData(string category)
        {
            var list = Track(ScriptableObject.CreateInstance<vWeaponIKAdjustList>());
            var weapon = Track(ScriptableObject.CreateInstance<vWeaponIKAdjust>());
            weapon.weaponCategories = new List<string> { category };
            weapon.ikAdjustsLeft = NewDefaultStates();
            weapon.ikAdjustsRight = NewDefaultStates();
            list.weaponIKAdjusts = new List<vWeaponIKAdjust> { weapon };
            return list;
        }

        static List<IKAdjust> NewDefaultStates()
        {
            var states = new List<IKAdjust>();
            for (int i = 0; i < vWeaponIKAdjust.defaultNames.Length; i++)
                states.Add(new IKAdjust(vWeaponIKAdjust.defaultNames[i]));
            return states;
        }

        Transform Child(Transform parent, string name)
        {
            var child = Track(new GameObject(name));
            child.transform.SetParent(parent, false);
            return child.transform;
        }

        T Track<T>(T value) where T : UnityEngine.Object
        {
            objects.Add(value);
            return value;
        }

        static string ReadProjectFile(string relativePath)
        {
            string projectRoot = Directory.GetParent(Application.dataPath)?.FullName;
            Assert.IsNotNull(projectRoot);
            return File.ReadAllText(Path.Combine(projectRoot, relativePath));
        }

        static int CountOccurrences(string source, string value)
        {
            int count = 0;
            int index = 0;
            while ((index = source.IndexOf(value, index, StringComparison.Ordinal)) >= 0)
            {
                count++;
                index += value.Length;
            }
            return count;
        }
    }
}
