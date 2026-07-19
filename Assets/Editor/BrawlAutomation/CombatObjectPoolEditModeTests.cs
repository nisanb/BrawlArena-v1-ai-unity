using System;
using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;

namespace BrawlArena.EditorAutomation
{
    public class CombatObjectPoolEditModeTests
    {
        readonly List<GameObject> testObjects = new List<GameObject>();
        readonly List<UnityEngine.Object> testAssets = new List<UnityEngine.Object>();
        int previousTargetFrameRate;

        [OneTimeSetUp]
        public void CaptureTargetFrameRate()
        {
            previousTargetFrameRate = Application.targetFrameRate;
        }

        [OneTimeTearDown]
        public void RestoreTargetFrameRate()
        {
            Application.targetFrameRate = previousTargetFrameRate;
        }

        [SetUp]
        public void SetUp()
        {
            FeedbackSettings.SetTestOverrides(null, null);
            DestroyPools();
        }

        [TearDown]
        public void TearDown()
        {
            FeedbackSettings.SetTestOverrides(null, null);
            DestroyPools();
            for (int i = testObjects.Count - 1; i >= 0; i--)
                if (testObjects[i] != null) UnityEngine.Object.DestroyImmediate(testObjects[i]);
            for (int i = testAssets.Count - 1; i >= 0; i--)
                if (testAssets[i] != null) UnityEngine.Object.DestroyImmediate(testAssets[i]);
            testObjects.Clear();
            testAssets.Clear();
            Application.targetFrameRate = previousTargetFrameRate;
        }

        [Test]
        public void ProjectileReuseDoesNotLeakSuperRadiusOrLaunchState()
        {
            GameObject prefab = CreatePrefab("ProjectilePoolTestPrefab");
            Projectile authored = prefab.AddComponent<Projectile>();
            authored.hitRadius = 0.3f;

            Projectile first = CombatObjectPool.SpawnProjectile(prefab,
                new Vector3(2f, 1f, 3f), Quaternion.identity);
            first.Launch(null, Vector3.right, 25f, 12f, null, 2f, 5f, 0.48f);
            Assert.That(ReadField<float>(first, "activeHitRadius"), Is.EqualTo(0.48f));

            first.Despawn();
            first.Despawn();
            Assert.IsFalse(first.gameObject.activeSelf, "Double release must leave the cache intact.");

            Projectile reused = CombatObjectPool.SpawnProjectile(prefab,
                Vector3.one, Quaternion.Euler(0f, 90f, 0f));
            Assert.AreSame(first, reused);
            Assert.IsNull(ReadField<BrawlerController>(reused, "owner"));
            Assert.AreEqual(0f, ReadField<float>(reused, "damage"));
            Assert.IsFalse(ReadField<bool>(reused, "launched"));

            reused.Launch(null, Vector3.forward, 10f, 8f, null);
            Assert.That(ReadField<float>(reused, "activeHitRadius"), Is.EqualTo(0.3f));
            Assert.That(reused.hitRadius, Is.EqualTo(0.3f),
                "The authored radius must never be mutated by a Super lease.");
            reused.Despawn();
        }

        [Test]
        public void BasicRangedShotsSkipCastAndSecondaryImpactWhileSuperRetainsThem()
        {
            GameObject ownerObject = new GameObject("RangedVfxPolicyOwner");
            testObjects.Add(ownerObject);
            ownerObject.AddComponent<Tests.BrawlFacadeTestMotor>();
            ownerObject.AddComponent<Tests.BrawlFacadeTestAnimationDriver>();
            BrawlerController owner = ownerObject.AddComponent<BrawlerController>();

            GameObject basicProjectilePrefab = CreatePrefab("BasicProjectilePrefab");
            basicProjectilePrefab.AddComponent<Projectile>();
            GameObject superProjectilePrefab = CreatePrefab("SuperProjectilePrefab");
            superProjectilePrefab.AddComponent<Projectile>();
            GameObject basicImpact = CreatePrefab("BasicImpactVfx");
            GameObject superImpact = CreatePrefab("SuperImpactVfx");
            GameObject primaryCast = CreatePrefab("PrimaryCastVfx");
            GameObject secondaryCast = CreatePrefab("SecondaryCastVfx");
            GameObject secondaryImpact = CreatePrefab("SecondaryImpactVfx");

            owner.projectilePrefab = basicProjectilePrefab;
            owner.superProjectilePrefab = superProjectilePrefab;
            owner.projectileSpeed = 12f;
            owner.superProjectileSpeed = 20f;
            owner.attackRange = 8f;
            owner.superProjectileBlastRadius = 2.25f;
            owner.impactVfx = basicImpact;
            owner.superImpactVfx = superImpact;
            owner.castVfx = primaryCast;
            owner.secondaryCastVfx = secondaryCast;
            owner.secondaryImpactVfx = secondaryImpact;

            InvokePrivate(owner, "FireProjectile", null, Vector3.forward);

            CombatObjectPool pool = UnityEngine.Object.FindFirstObjectByType<CombatObjectPool>(
                FindObjectsInactive.Include);
            Assert.NotNull(pool);
            Projectile[] shots = pool.GetComponentsInChildren<Projectile>(true);
            Assert.AreEqual(1, shots.Length);
            Projectile basicShot = shots[0];
            Assert.AreSame(basicImpact, ReadField<GameObject>(basicShot, "impactVfx"));
            Assert.IsNull(ReadField<GameObject>(basicShot, "secondaryImpactVfx"));
            Assert.IsNull(pool.transform.Find("PrimaryCastVfx(Clone)"));
            Assert.IsNull(pool.transform.Find("SecondaryCastVfx(Clone)"));

            InvokePrivate(owner, "FireSuperProjectile", null, Vector3.forward);

            shots = pool.GetComponentsInChildren<Projectile>(true);
            Assert.AreEqual(2, shots.Length);
            Projectile superShot = shots[0] == basicShot ? shots[1] : shots[0];
            Assert.AreSame(superImpact, ReadField<GameObject>(superShot, "impactVfx"));
            Assert.AreSame(secondaryImpact,
                ReadField<GameObject>(superShot, "secondaryImpactVfx"));
            Assert.That(ReadField<float>(superShot, "blastRadius"), Is.EqualTo(2.25f));
            Assert.NotNull(pool.transform.Find("PrimaryCastVfx(Clone)"));
            Assert.NotNull(pool.transform.Find("SecondaryCastVfx(Clone)"));
        }

        [Test]
        public void ProjectileEmbeddedAudioHonorsDisabledSfxWithoutChangingAuthoredMix()
        {
            GameObject prefab = CreatePrefab("ProjectileAudioPoolTestPrefab");
            prefab.AddComponent<Projectile>();
            AudioSource authoredAudio = prefab.AddComponent<AudioSource>();
            authoredAudio.playOnAwake = true;
            authoredAudio.volume = 0.37f;
            authoredAudio.mute = false;
            AudioClip clip = AudioClip.Create("ProjectilePoolAudio", 4410, 1, 44100, false);
            testAssets.Add(clip);
            authoredAudio.clip = clip;
            FeedbackSettings.SetTestOverrides(false, null);

            Projectile projectile = CombatObjectPool.SpawnProjectile(prefab,
                Vector3.zero, Quaternion.identity);
            AudioSource instanceAudio = projectile.GetComponent<AudioSource>();

            Assert.IsFalse(instanceAudio.isPlaying);
            Assert.IsFalse(instanceAudio.mute);
            Assert.That(instanceAudio.volume, Is.EqualTo(0.37f));
            projectile.Despawn();
        }

        [Test]
        public void VfxReuseClearsTransientComponentsAndRestoresHierarchy()
        {
            GameObject prefab = CreatePrefab("VfxResetTestPrefab");
            ParticleSystem authoredParticles = prefab.AddComponent<ParticleSystem>();
            var authoredMain = authoredParticles.main;
            authoredMain.stopAction = ParticleSystemStopAction.Destroy;
            prefab.AddComponent<TrailRenderer>();
            AudioSource authoredAudio = prefab.AddComponent<AudioSource>();
            authoredAudio.playOnAwake = false;
            AudioClip clip = AudioClip.Create("PoolResetClip", 4410, 1, 44100, false);
            testAssets.Add(clip);
            authoredAudio.clip = clip;

            GameObject child = new GameObject("AuthoredChild");
            child.transform.SetParent(prefab.transform, false);
            child.transform.localPosition = new Vector3(1f, 2f, 3f);

            GameObject first = CombatObjectPool.SpawnVfx(prefab, Vector3.zero,
                Quaternion.identity, 5f);
            ParticleSystem particles = first.GetComponent<ParticleSystem>();
            TrailRenderer trail = first.GetComponent<TrailRenderer>();
            AudioSource audio = first.GetComponent<AudioSource>();
            Transform reusedChild = first.transform.Find("AuthoredChild");

            particles.Emit(4);
            trail.AddPosition(Vector3.zero);
            trail.AddPosition(Vector3.one);
            audio.timeSamples = 512;
            reusedChild.localPosition = Vector3.one * 99f;

            Assert.IsTrue(CombatObjectPool.Release(first));
            Assert.IsTrue(CombatObjectPool.Release(first), "Release must be idempotent.");
            Assert.IsFalse(first.activeSelf);
            Assert.AreEqual(0, particles.particleCount);
            Assert.AreEqual(0, trail.positionCount);
            Assert.AreEqual(0, audio.timeSamples);

            GameObject reused = CombatObjectPool.SpawnVfx(prefab, Vector3.right,
                Quaternion.identity, 5f);
            Assert.AreSame(first, reused);
            Assert.AreEqual(ParticleSystemStopAction.None,
                reused.GetComponent<ParticleSystem>().main.stopAction);
            Assert.That(reused.transform.Find("AuthoredChild").localPosition,
                Is.EqualTo(new Vector3(1f, 2f, 3f)));
            CombatObjectPool.Release(reused);
        }

        [Test]
        public void MatchEndReleasesEffectsAndProjectileUnsubscribesWhenReturned()
        {
            GameObject managerObject = new GameObject("PoolLifecycleMatchManager");
            testObjects.Add(managerObject);
            MatchManager manager = managerObject.AddComponent<MatchManager>();
            InvokePrivate(manager, "Awake");

            GameObject projectilePrefab = CreatePrefab("SubscriptionProjectilePrefab");
            projectilePrefab.AddComponent<Projectile>();
            GameObject vfxPrefab = CreatePrefab("SubscriptionVfxPrefab");

            Projectile projectile = CombatObjectPool.SpawnProjectile(projectilePrefab,
                Vector3.zero, Quaternion.identity);
            projectile.Launch(null, Vector3.forward, 10f, 5f, null);
            Assert.AreEqual(1, CountMatchEndSubscriptions(manager, projectile));

            projectile.Despawn();
            Assert.AreEqual(0, CountMatchEndSubscriptions(manager, projectile));
            projectile = CombatObjectPool.SpawnProjectile(projectilePrefab,
                Vector3.zero, Quaternion.identity);
            projectile.Launch(null, Vector3.forward, 10f, 5f, null);
            Assert.AreEqual(1, CountMatchEndSubscriptions(manager, projectile),
                "Reuse must not accumulate MatchEnded callbacks.");

            GameObject effect = CombatObjectPool.SpawnVfx(vfxPrefab, Vector3.zero,
                Quaternion.identity, 30f);
            manager.DeclareWinner(TeamId.Blue);

            Assert.IsFalse(projectile.gameObject.activeSelf);
            Assert.IsFalse(effect.activeSelf);
            Assert.AreEqual(0, CountMatchEndSubscriptions(manager, projectile));
            Assert.IsNull(ReadField<BrawlerController>(projectile, "owner"));
            Assert.IsFalse(ReadField<bool>(projectile, "launched"));
        }

        [Test]
        public void ZeroLifetimeVfxReturnsThroughItsLease()
        {
            GameObject prefab = CreatePrefab("TimedVfxPrefab");
            GameObject effect = CombatObjectPool.SpawnVfx(prefab, Vector3.zero,
                Quaternion.identity, 0f);
            // String-based lookup needs the namespace for this internal runtime
            // component; the short name can depend on Unity's type-cache state.
            Component lease = effect.GetComponent("BrawlArena.PooledVfxLease");
            Assert.NotNull(lease);

            InvokePrivate(lease, "Update");

            Assert.IsFalse(effect.activeSelf);
        }

        [Test]
        public void SelfDestructiveMagicLightVfxUsesDestroyFallback()
        {
            GameObject prefab = CreatePrefab("SelfDestructiveVfxPrefab");
            GameObject lightChild = new GameObject("DisposableLight");
            lightChild.transform.SetParent(prefab.transform, false);
            lightChild.AddComponent<Light>();
            lightChild.AddComponent<MagicArsenal.MagicLightFade>();

            Assert.IsFalse(CombatObjectPool.IsVfxPoolEligible(prefab));
            GameObject first = CombatObjectPool.SpawnVfx(prefab, Vector3.zero,
                Quaternion.identity, 1f);
            int firstId = first.GetInstanceID();
            CombatObjectPool.Release(first);
            Assert.IsTrue(first == null, "An ineligible clone must not enter the reusable cache.");

            GameObject second = CombatObjectPool.SpawnVfx(prefab, Vector3.zero,
                Quaternion.identity, 1f);
            Assert.AreNotEqual(firstId, second.GetInstanceID());
            CombatObjectPool.Release(second);
        }

        [Test]
        public void StatefulRfx4VfxUsesDestroyFallback()
        {
            GameObject prefab = CreatePrefab("StatefulRfx4VfxPrefab");
            GameObject activatedChild = new GameObject("DelayedChild");
            activatedChild.transform.SetParent(prefab.transform, false);
            RFX4_StartDelay stateful = prefab.AddComponent<RFX4_StartDelay>();
            stateful.ActivatedGameObject = activatedChild;

            Assert.IsFalse(CombatObjectPool.IsVfxPoolEligible(prefab));
            GameObject first = CombatObjectPool.SpawnVfx(prefab, Vector3.zero,
                Quaternion.identity, 1f);
            int firstId = first.GetInstanceID();
            CombatObjectPool.Release(first);
            Assert.IsTrue(first == null, "An RFX4 clone must not retain its timer state.");

            GameObject second = CombatObjectPool.SpawnVfx(prefab, Vector3.zero,
                Quaternion.identity, 1f);
            Assert.AreNotEqual(firstId, second.GetInstanceID());
            CombatObjectPool.Release(second);
        }

        [Test]
        public void VfxPoolRetainsNoMoreThanItsPerPrefabCapacity()
        {
            GameObject prefab = CreatePrefab("BoundedVfxPrefab");
            var active = new List<GameObject>();
            for (int i = 0; i < CombatObjectPool.VfxCapacityPerPrefab + 5; i++)
                active.Add(CombatObjectPool.SpawnVfx(prefab, Vector3.zero,
                    Quaternion.identity, 10f));

            for (int i = 0; i < active.Count; i++) CombatObjectPool.Release(active[i]);

            CombatObjectPool pool = UnityEngine.Object.FindFirstObjectByType<CombatObjectPool>(
                FindObjectsInactive.Include);
            Assert.NotNull(pool);
            Assert.AreEqual(CombatObjectPool.VfxCapacityPerPrefab, pool.transform.childCount);
        }

        GameObject CreatePrefab(string name)
        {
            GameObject prefab = new GameObject(name);
            prefab.SetActive(false);
            testObjects.Add(prefab);
            return prefab;
        }

        static T ReadField<T>(object target, string fieldName)
        {
            FieldInfo field = target.GetType().GetField(fieldName,
                BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.NotNull(field, fieldName);
            return (T)field.GetValue(target);
        }

        static void InvokePrivate(object target, string methodName, params object[] arguments)
        {
            MethodInfo method = target.GetType().GetMethod(methodName,
                BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.NotNull(method, methodName);
            method.Invoke(target, arguments);
        }

        static int CountMatchEndSubscriptions(MatchManager manager, object subscriber)
        {
            FieldInfo eventField = typeof(MatchManager).GetField("MatchEnded",
                BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.NotNull(eventField);
            Delegate handlers = eventField.GetValue(manager) as Delegate;
            if (handlers == null) return 0;

            int count = 0;
            Delegate[] callbacks = handlers.GetInvocationList();
            for (int i = 0; i < callbacks.Length; i++)
                if (ReferenceEquals(callbacks[i].Target, subscriber)) count++;
            return count;
        }

        static void DestroyPools()
        {
            CombatObjectPool[] pools = UnityEngine.Object.FindObjectsByType<CombatObjectPool>(
                FindObjectsInactive.Include, FindObjectsSortMode.None);
            for (int i = 0; i < pools.Length; i++)
                if (pools[i] != null) UnityEngine.Object.DestroyImmediate(pools[i].gameObject);
        }
    }
}
