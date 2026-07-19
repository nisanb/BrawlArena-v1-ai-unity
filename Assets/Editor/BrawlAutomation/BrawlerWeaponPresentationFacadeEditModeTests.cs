using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using Object = UnityEngine.Object;

namespace BrawlArena.EditorAutomation
{
    public class BrawlerWeaponPresentationFacadeEditModeTests
    {
        readonly List<GameObject> created = new List<GameObject>();

        [TearDown]
        public void TearDown()
        {
            for (int i = created.Count - 1; i >= 0; i--)
            {
                if (created[i] != null) Object.DestroyImmediate(created[i]);
            }
            created.Clear();
        }

        [Test]
        public void ContractIsVisualOnlyAndContainsNoGameplayOrVendorTypes()
        {
            MethodInfo[] methods = typeof(IBrawlerWeaponPresentation).GetMethods();
            CollectionAssert.AreEquivalent(new[]
            {
                "PresentAim",
                "TryGetMuzzlePosition",
                "PresentMuzzle",
                "SetVisible",
                "ResetForRespawn",
            }, methods.Select(method => method.Name).ToArray());

            Type[] allowedTypes =
            {
                typeof(void),
                typeof(bool),
                typeof(Vector3),
                typeof(Vector3).MakeByRefType(),
            };
            foreach (MethodInfo method in methods)
            {
                CollectionAssert.Contains(allowedTypes, method.ReturnType, method.Name);
                foreach (ParameterInfo parameter in method.GetParameters())
                    CollectionAssert.Contains(allowedTypes, parameter.ParameterType, method.Name);

                string normalizedName = method.Name.ToLowerInvariant();
                StringAssert.DoesNotContain("damage", normalizedName);
                StringAssert.DoesNotContain("ammo", normalizedName);
                StringAssert.DoesNotContain("shoot", normalizedName);
                StringAssert.DoesNotContain("reload", normalizedName);
            }

            string root = Directory.GetParent(Application.dataPath)?.FullName;
            string contract = File.ReadAllText(Path.Combine(
                root, "Assets/Scripts/Brawl/Integration/IBrawlerWeaponPresentation.cs"))
                .ToLowerInvariant();
            StringAssert.DoesNotContain("damage", contract);
            StringAssert.DoesNotContain("ammo", contract);
            StringAssert.DoesNotContain("shoot", contract);
            StringAssert.DoesNotContain("reload", contract);
            StringAssert.DoesNotContain("invector", contract);
        }

        [Test]
        public void SameRootComponentSelectionRestoresAndLocks()
        {
            BrawlerController brawler = CreateBrawler("WeaponPresentationSelection");
            RecordingWeaponPresentation presentation =
                brawler.gameObject.AddComponent<RecordingWeaponPresentation>();

            Assert.DoesNotThrow(() => brawler.SetWeaponPresentation(presentation));
            Assert.AreSame(presentation, brawler.WeaponPresentation);

            SetPrivateField(brawler, "weaponPresentation", null);
            Assert.AreSame(presentation, brawler.WeaponPresentation,
                "The serialized component source must restore the selected owner.");

            InvokePrivate(brawler, "InitializeWeaponPresentation");
            Assert.IsTrue(GetPrivateField<bool>(brawler, "weaponPresentationLocked"));
            Assert.Throws<InvalidOperationException>(() =>
                brawler.SetWeaponPresentation(presentation));
        }

        [Test]
        public void SelectionRejectsNullPlainCrossRootAndDuplicateOwners()
        {
            BrawlerController brawler = CreateBrawler("WeaponPresentationValidation");
            Assert.Throws<ArgumentNullException>(() => brawler.SetWeaponPresentation(null));
            Assert.Throws<ArgumentException>(() =>
                brawler.SetWeaponPresentation(new PlainWeaponPresentation()));

            GameObject other = CreateObject("CrossRootWeaponPresentation");
            RecordingWeaponPresentation crossRoot =
                other.AddComponent<RecordingWeaponPresentation>();
            Assert.Throws<ArgumentException>(() => brawler.SetWeaponPresentation(crossRoot));

            brawler.gameObject.AddComponent<RecordingWeaponPresentation>();
            brawler.gameObject.AddComponent<ThrowingWeaponPresentation>();
            TargetInvocationException duplicate = Assert.Throws<TargetInvocationException>(() =>
                InvokePrivate(brawler, "InitializeWeaponPresentation"));
            Assert.IsInstanceOf<InvalidOperationException>(duplicate.InnerException);
        }

        [Test]
        public void SemanticCallsReachSelectedOwnerAndMuzzleFallsBackToSpellOrigin()
        {
            BrawlerController brawler = CreateBrawler("WeaponPresentationSemantics");
            Transform spellOrigin = CreateObject("SpellOrigin").transform;
            spellOrigin.SetParent(brawler.transform, false);
            spellOrigin.localPosition = new Vector3(0.25f, 1.4f, 0.8f);
            SetPrivateField(brawler, "spellOrigin", spellOrigin);

            Vector3 legacyMuzzle = spellOrigin.position;
            Assert.AreEqual(legacyMuzzle, brawler.AttackPreviewOrigin,
                "An actor without a presenter must retain the authored SpellOrigin.");

            RecordingWeaponPresentation presentation =
                brawler.gameObject.AddComponent<RecordingWeaponPresentation>();
            presentation.MuzzlePosition = new Vector3(8f, 4f, -2f);
            presentation.HasMuzzle = true;
            brawler.SetWeaponPresentation(presentation);

            Vector3 aim = new Vector3(0.4f, 0f, 0.9f).normalized;
            InvokePrivate(brawler, "PresentWeaponAim", aim);
            Assert.AreEqual(aim, presentation.LastAim);
            Assert.AreEqual(presentation.MuzzlePosition, brawler.AttackPreviewOrigin);
            InvokePrivate(brawler, "PresentWeaponMuzzle", presentation.MuzzlePosition, aim);
            Assert.AreEqual(1, presentation.MuzzleCount);
            Assert.AreEqual(presentation.MuzzlePosition, presentation.LastMuzzlePosition);
            Assert.AreEqual(aim, presentation.LastMuzzleDirection);

            SetPrivateField(brawler, "skins", Array.Empty<SkinnedMeshRenderer>());
            InvokePrivate(brawler, "SetSkinsVisible", false);
            Assert.IsFalse(presentation.LastVisible);
            InvokePrivate(brawler, "ResetWeaponForRespawn");
            Assert.AreEqual(1, presentation.RespawnResetCount);

            presentation.HasMuzzle = false;
            Assert.AreEqual(legacyMuzzle, brawler.AttackPreviewOrigin,
                "A presenter without a ready socket must fall back to SpellOrigin.");
            Assert.AreEqual(0, brawler.WeaponPresentationFailureCount);

            presentation.HasMuzzle = true;
            presentation.MuzzlePosition = new Vector3(float.NaN, 0f, 0f);
            Assert.AreEqual(legacyMuzzle, brawler.AttackPreviewOrigin,
                "An invalid visual socket must not poison Brawl targeting or spawning.");
            Assert.AreEqual(1, brawler.WeaponPresentationFailureCount);
            Assert.AreEqual("ResolveMuzzle",
                brawler.LastWeaponPresentationFailureOperation);
        }

        [Test]
        public void ThrowingPresentationIsContainedForEverySemanticOperation()
        {
            BrawlerController brawler = CreateBrawler("ThrowingWeaponPresentation");
            Transform spellOrigin = CreateObject("SpellOrigin").transform;
            spellOrigin.SetParent(brawler.transform, false);
            spellOrigin.localPosition = new Vector3(-0.2f, 1.25f, 0.6f);
            SetPrivateField(brawler, "spellOrigin", spellOrigin);

            ThrowingWeaponPresentation presentation =
                brawler.gameObject.AddComponent<ThrowingWeaponPresentation>();
            brawler.SetWeaponPresentation(presentation);

            Assert.DoesNotThrow(() => InvokePrivate(
                brawler, "PresentWeaponAim", Vector3.forward));
            Assert.AreEqual(spellOrigin.position, brawler.AttackPreviewOrigin);
            Assert.DoesNotThrow(() => InvokePrivate(
                brawler, "PresentWeaponMuzzle", spellOrigin.position, Vector3.forward));
            Assert.DoesNotThrow(() => InvokePrivate(
                brawler, "PresentWeaponVisibility", false));
            Assert.DoesNotThrow(() => InvokePrivate(brawler, "ResetWeaponForRespawn"));

            Assert.AreEqual(5, brawler.WeaponPresentationFailureCount);
            Assert.AreEqual("ResetForRespawn",
                brawler.LastWeaponPresentationFailureOperation);
            Assert.AreEqual(typeof(NotSupportedException).FullName,
                brawler.LastWeaponPresentationFailureType);
            Assert.AreEqual("ResetForRespawn",
                brawler.LastWeaponPresentationFailureMessage);
            Assert.IsInstanceOf<NotSupportedException>(
                brawler.LastWeaponPresentationFailure);
        }

        [Test]
        public void ThrowingAimReleaseCannotLeaveAttackOrSuperRoutineOwned()
        {
            BrawlerController brawler = CreateBrawler("ThrowingAimRelease");
            ThrowingWeaponPresentation presentation =
                brawler.gameObject.AddComponent<ThrowingWeaponPresentation>();
            brawler.SetWeaponPresentation(presentation);

            InvokePrivate(brawler, "PresentWeaponAim", Vector3.forward);
            var attack = (System.Collections.IEnumerator)InvokePrivate(
                brawler, "AttackRoutine", null, Vector3.forward);
            Assert.IsTrue(attack.MoveNext());
            Assert.IsInstanceOf<WaitForSeconds>(attack.Current);
            // The routine continues into its authored post-impact recovery window
            // (attackMoveLock - hitDelay) before releasing attackRoutine ownership.
            Assert.IsTrue(attack.MoveNext());
            Assert.IsInstanceOf<WaitForSeconds>(attack.Current);
            Assert.IsFalse(attack.MoveNext());
            Assert.IsNull(GetPrivateField<Coroutine>(brawler, "attackRoutine"));
            Assert.AreEqual(2, brawler.WeaponPresentationFailureCount,
                "Aim acquire and finally-release must both remain contained.");

            SetPrivateField(brawler, "superInProgress", true);
            InvokePrivate(brawler, "PresentWeaponAim", Vector3.forward);
            var super = (System.Collections.IEnumerator)InvokePrivate(
                brawler, "SuperRoutine", null, Vector3.forward);
            Assert.IsTrue(super.MoveNext());
            Assert.IsInstanceOf<WaitForSeconds>(super.Current);
            Assert.IsFalse(super.MoveNext());
            Assert.IsFalse(GetPrivateField<bool>(brawler, "superInProgress"));
            Assert.IsNull(GetPrivateField<Coroutine>(brawler, "superRoutine"));
            Assert.AreEqual(4, brawler.WeaponPresentationFailureCount);
            Assert.AreEqual("PresentAim", brawler.LastWeaponPresentationFailureOperation);
            Assert.AreEqual("PresentAim", brawler.LastWeaponPresentationFailureMessage);
        }

        [Test]
        public void FacadeCallsAreVisualOnlyAndPreserveBrawlTimingAndProjectileAuthority()
        {
            string root = Directory.GetParent(Application.dataPath)?.FullName;
            Assert.IsNotNull(root);
            string facade = File.ReadAllText(Path.Combine(
                root, "Assets/Scripts/Brawl/BrawlerController.cs"));
            string contract = File.ReadAllText(Path.Combine(
                root, "Assets/Scripts/Brawl/Integration/IBrawlerWeaponPresentation.cs"));

            string attack = Extract(facade,
                "IEnumerator AttackRoutine(", "IEnumerator SuperRoutine(");
            AssertOrdered(attack,
                "TryPresent(AnimationPresentationOperation.PlayBasicAttack)",
                "yield return new WaitForSeconds(hitDelay)",
                "if (projectilePrefab != null) FireProjectile(target, worldDirection)");
            string projectile = Extract(facade,
                "void FireProjectile(", "void FireSuperProjectile(");
            AssertOrdered(projectile,
                "Vector3 muzzle = SpellMuzzlePosition",
                "CombatObjectPool.SpawnProjectile(",
                "if (proj == null) return",
                "PresentWeaponMuzzle(muzzle, dir)",
                "proj.Launch(");
            Assert.AreEqual(1, CountOccurrences(projectile,
                "CombatObjectPool.SpawnProjectile("));
            Assert.AreEqual(1, CountOccurrences(projectile, "proj.Launch("));

            string superProjectile = Extract(facade,
                "void FireSuperProjectile(", "void MeleeStrike(");
            AssertOrdered(superProjectile,
                "CombatObjectPool.SpawnProjectile(",
                "if (proj == null) return",
                "PresentWeaponMuzzle(muzzle, dir)",
                "proj.Launch(");

            string attackFinally = Extract(facade,
                "IEnumerator AttackRoutine(", "IEnumerator SuperRoutine(");
            AssertOrdered(attackFinally,
                "finally",
                "PresentWeaponAim(Vector3.zero)",
                "attackRoutine = null");
            string superFinally = Extract(facade,
                "IEnumerator SuperRoutine(", "void FireProjectile(");
            AssertOrdered(superFinally,
                "finally",
                "PresentWeaponAim(Vector3.zero)",
                "superInProgress = false",
                "superRoutine = null");

            string respawn = Extract(facade,
                "IEnumerator RespawnRoutine(", "void CancelSpawnProtectionOnOffense(");
            AssertOrdered(respawn,
                "Teleport(spawn)",
                "Health.Revive()",
                "ResetWeaponForRespawn()",
                "PresentWeaponVisibility(true)",
                "TryPresent(AnimationPresentationOperation.PlayRespawn)");

            string death = Extract(facade,
                "void OnDied(", "IEnumerator RespawnRoutine(");
            AssertOrdered(death,
                "moveInput = Vector3.zero",
                "PresentWeaponVisibility(false)",
                "TryPresent(AnimationPresentationOperation.PlayDeath)",
                "Motor?.Stop(true)");

            StringAssert.DoesNotContain("vShooter", contract);
            StringAssert.DoesNotContain("vMelee", contract);
            StringAssert.DoesNotContain("Animator", contract);
        }

        [Test]
        public void AssemblyPathHasNoLegacyRecursiveHitboxLayerWriter()
        {
            string root = Directory.GetParent(Application.dataPath)?.FullName;
            string facade = File.ReadAllText(Path.Combine(
                root, "Assets/Scripts/Brawl/BrawlerController.cs"));
            string assembly = File.ReadAllText(Path.Combine(root,
                "Assets/Scripts/Brawl/Integration/BrawlerCharacterAssembly.cs"));
            StringAssert.DoesNotContain("CombatPhysics.SetLayerRecursively", facade);
            StringAssert.DoesNotContain(
                "CombatPhysics.SetLayerRecursively", assembly);
            string legacyDirectory = Path.Combine(root,
                "Assets/Scripts/Brawl/Integration/Legacy");
            Assert.IsTrue(!Directory.Exists(legacyDirectory) ||
                Directory.GetFiles(legacyDirectory, "*.cs").Length == 0);
        }

        BrawlerController CreateBrawler(string name)
        {
            GameObject root = CreateObject(name);
            Health health = root.AddComponent<Health>();
            health.SetMax(100f, true);
            Tests.BrawlFacadeTestMotor motor =
                root.AddComponent<Tests.BrawlFacadeTestMotor>();
            Tests.BrawlFacadeTestAnimationDriver animation =
                root.AddComponent<Tests.BrawlFacadeTestAnimationDriver>();
            BrawlerController brawler = root.AddComponent<BrawlerController>();
            brawler.SetMotor(motor);
            brawler.SetAnimationDriver(animation);
            PropertyInfo healthProperty = typeof(BrawlerController).GetProperty(
                nameof(BrawlerController.Health),
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            Assert.IsNotNull(healthProperty);
            healthProperty.SetValue(brawler, health);
            return brawler;
        }

        GameObject CreateObject(string name)
        {
            var gameObject = new GameObject(name);
            created.Add(gameObject);
            return gameObject;
        }

        static object InvokePrivate(object target, string methodName, params object[] arguments)
        {
            MethodInfo method = target.GetType().GetMethod(
                methodName, BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.IsNotNull(method, "Missing private method: " + methodName);
            return method.Invoke(target, arguments);
        }

        static T GetPrivateField<T>(object target, string fieldName)
        {
            FieldInfo field = target.GetType().GetField(
                fieldName, BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.IsNotNull(field, "Missing private field: " + fieldName);
            return (T)field.GetValue(target);
        }

        static void SetPrivateField(object target, string fieldName, object value)
        {
            FieldInfo field = target.GetType().GetField(
                fieldName, BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.IsNotNull(field, "Missing private field: " + fieldName);
            field.SetValue(target, value);
        }

        static string Extract(string source, string startMarker, string endMarker)
        {
            int start = source.IndexOf(startMarker, StringComparison.Ordinal);
            Assert.GreaterOrEqual(start, 0, "Missing source marker: " + startMarker);
            int end = source.IndexOf(endMarker, start + startMarker.Length,
                StringComparison.Ordinal);
            Assert.Greater(end, start, "Missing source marker: " + endMarker);
            return source.Substring(start, end - start);
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

        static void AssertOrdered(string source, params string[] markers)
        {
            int prior = -1;
            foreach (string marker in markers)
            {
                int index = source.IndexOf(marker, prior + 1, StringComparison.Ordinal);
                Assert.Greater(index, prior, "Expected ordered source marker: " + marker);
                prior = index;
            }
        }

        sealed class PlainWeaponPresentation : IBrawlerWeaponPresentation
        {
            public void PresentAim(Vector3 worldDirection) { }
            public bool TryGetMuzzlePosition(out Vector3 worldPosition)
            {
                worldPosition = default;
                return false;
            }
            public void PresentMuzzle(Vector3 worldPosition, Vector3 worldDirection) { }
            public void SetVisible(bool visible) { }
            public void ResetForRespawn() { }
        }

        sealed class RecordingWeaponPresentation : MonoBehaviour, IBrawlerWeaponPresentation
        {
            public bool HasMuzzle { get; set; }
            public Vector3 MuzzlePosition { get; set; }
            public Vector3 LastAim { get; private set; }
            public int MuzzleCount { get; private set; }
            public Vector3 LastMuzzlePosition { get; private set; }
            public Vector3 LastMuzzleDirection { get; private set; }
            public bool LastVisible { get; private set; } = true;
            public int RespawnResetCount { get; private set; }

            public void PresentAim(Vector3 worldDirection) => LastAim = worldDirection;
            public bool TryGetMuzzlePosition(out Vector3 worldPosition)
            {
                worldPosition = MuzzlePosition;
                return HasMuzzle;
            }
            public void PresentMuzzle(Vector3 worldPosition, Vector3 worldDirection)
            {
                MuzzleCount++;
                LastMuzzlePosition = worldPosition;
                LastMuzzleDirection = worldDirection;
            }
            public void SetVisible(bool visible) => LastVisible = visible;
            public void ResetForRespawn() => RespawnResetCount++;
        }

        sealed class ThrowingWeaponPresentation : MonoBehaviour, IBrawlerWeaponPresentation
        {
            public void PresentAim(Vector3 worldDirection) =>
                throw new NotSupportedException(nameof(PresentAim));
            public bool TryGetMuzzlePosition(out Vector3 worldPosition)
            {
                worldPosition = default;
                throw new NotSupportedException(nameof(TryGetMuzzlePosition));
            }
            public void PresentMuzzle(Vector3 worldPosition, Vector3 worldDirection) =>
                throw new NotSupportedException(nameof(PresentMuzzle));
            public void SetVisible(bool visible) =>
                throw new NotSupportedException(nameof(SetVisible));
            public void ResetForRespawn() =>
                throw new NotSupportedException(nameof(ResetForRespawn));
        }
    }
}
