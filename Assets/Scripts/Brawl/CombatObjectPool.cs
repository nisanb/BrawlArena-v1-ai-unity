using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace BrawlArena
{
    /// <summary>
    /// Scene-local, lazy pools for the short-lived combat objects created most
    /// often during a match. Each prefab has a fixed retained capacity; bursts
    /// beyond that capacity still render, but their overflow instances are
    /// destroyed when released instead of growing the pool forever.
    /// </summary>
    public sealed class CombatObjectPool : MonoBehaviour
    {
        public const int ProjectileCapacityPerPrefab = 32;
        public const int VfxCapacityPerPrefab = 48;

        enum PoolKind
        {
            Projectile,
            Vfx,
        }

        readonly struct PoolKey : IEquatable<PoolKey>
        {
            public readonly int PrefabId;
            public readonly PoolKind Kind;

            public PoolKey(GameObject prefab, PoolKind kind)
            {
                PrefabId = prefab.GetInstanceID();
                Kind = kind;
            }

            public PoolKey(int prefabId, PoolKind kind)
            {
                PrefabId = prefabId;
                Kind = kind;
            }

            public bool Equals(PoolKey other)
            {
                return PrefabId == other.PrefabId && Kind == other.Kind;
            }

            public override bool Equals(object obj)
            {
                return obj is PoolKey other && Equals(other);
            }

            public override int GetHashCode()
            {
                unchecked
                {
                    return (PrefabId * 397) ^ (int)Kind;
                }
            }
        }

        sealed class Bucket
        {
            public readonly GameObject Prefab;
            public readonly int Capacity;
            public readonly bool AllowsRetention;
            public readonly List<CombatPooledObject> Available =
                new List<CombatPooledObject>();
            public readonly HashSet<CombatPooledObject> Leased =
                new HashSet<CombatPooledObject>();
            public int RetainedCount;

            public Bucket(GameObject prefab, int capacity, bool allowsRetention)
            {
                Prefab = prefab;
                Capacity = capacity;
                AllowsRetention = allowsRetention;
            }
        }

        static CombatObjectPool instance;
        static int runtimeGeneration;

        readonly Dictionary<PoolKey, Bucket> buckets =
            new Dictionary<PoolKey, Bucket>();
        // Instances created by Prewarm before their spawn kind is known. They
        // are adopted into the real projectile/VFX bucket on first Acquire.
        readonly Dictionary<int, List<CombatPooledObject>> prewarmedByPrefab =
            new Dictionary<int, List<CombatPooledObject>>();
        MatchManager observedMatch;
        int generation;
        int sceneHandle;
        bool shuttingDown;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics()
        {
            // A generation also covers Enter Play Mode configurations that keep
            // scene objects alive while resetting static fields.
            instance = null;
            unchecked { runtimeGeneration++; }
        }

        void Awake()
        {
            InitializeForScene(gameObject.scene.handle);
        }

        void InitializeForScene(int handle)
        {
            generation = runtimeGeneration;
            sceneHandle = handle;
            instance = this;
        }

        void OnDestroy()
        {
            Shutdown();
            if (instance == this) instance = null;
        }

        /// <summary>Gets a projectile instance without eagerly warming a pool.</summary>
        public static Projectile SpawnProjectile(GameObject prefab, Vector3 position,
            Quaternion rotation)
        {
            if (prefab == null) return null;

            CombatObjectPool pool = GetForActiveScene();
            GameObject instanceObject = pool.Acquire(prefab, PoolKind.Projectile,
                position, rotation);
            if (instanceObject == null) return null;

            Projectile projectile = instanceObject.GetComponent<Projectile>();
            if (projectile == null) projectile = instanceObject.AddComponent<Projectile>();
            projectile.PrepareForReuse();
            CombatPhysics.SetLayerRecursively(instanceObject, CombatPhysics.ProjectileLayer);
            CombatFeedback.ResetEmbeddedSfx(instanceObject, true);
            return projectile;
        }

        /// <summary>Gets and restarts a VFX instance for the supplied lifetime.</summary>
        public static GameObject SpawnVfx(GameObject prefab, Vector3 position,
            Quaternion rotation, float lifetime)
        {
            if (prefab == null) return null;

            CombatObjectPool pool = GetForActiveScene();
            GameObject instanceObject = pool.Acquire(prefab, PoolKind.Vfx,
                position, rotation);
            if (instanceObject == null) return null;

            CombatPhysics.SetLayerRecursively(instanceObject, CombatPhysics.VfxLayer);
            PooledVfxLease lease = instanceObject.GetComponent<PooledVfxLease>();
            if (lease == null) lease = instanceObject.AddComponent<PooledVfxLease>();
            lease.Begin(Mathf.Max(0f, lifetime));
            return instanceObject;
        }

        /// <summary>
        /// Eagerly instantiates up to count inactive, reusable clones of a
        /// projectile or VFX prefab so the first mid-combat spawn never pays an
        /// Instantiate hitch. Warm clones are consumed by whichever spawn path
        /// first uses the prefab. Idempotent: instances already warm, pooled,
        /// or leased for the prefab count toward the requested total.
        /// </summary>
        public static void Prewarm(GameObject prefab, int count)
        {
            if (prefab == null || count <= 0) return;
            GetForActiveScene().PrewarmInstances(prefab, count);
        }

        /// <summary>
        /// Self-destructive or stateful third-party scripts cannot be made
        /// reusable safely: some destroy child lights, retain elapsed timers,
        /// or permanently change the clone hierarchy. Those prefabs still use
        /// the common timed lease, but are destroyed on release rather than
        /// retained.
        /// </summary>
        public static bool IsVfxPoolEligible(GameObject prefab)
        {
            if (prefab == null) return false;
            MonoBehaviour[] behaviours = prefab.GetComponentsInChildren<MonoBehaviour>(true);
            for (int i = 0; i < behaviours.Length; i++)
            {
                MonoBehaviour behaviour = behaviours[i];
                if (behaviour == null) continue;
                Type type = behaviour.GetType();
                string typeName = type.FullName;
                if (string.Equals(typeName, "MagicArsenal.MagicLightFade",
                        StringComparison.Ordinal) ||
                    type.Name.StartsWith("RFX4_", StringComparison.Ordinal) ||
                    (!string.IsNullOrEmpty(typeName) &&
                     typeName.StartsWith("RFX4_", StringComparison.Ordinal)))
                    return false;
            }
            return true;
        }

        /// <summary>
        /// Releases a pool-owned object. Releasing the same object more than
        /// once is safe and still returns true; false means it was never owned
        /// by this pool and the caller should use its normal destruction path.
        /// </summary>
        public static bool Release(GameObject instanceObject)
        {
            if (instanceObject == null) return false;
            CombatPooledObject marker = instanceObject.GetComponent<CombatPooledObject>();
            if (marker == null || marker.Owner == null) return false;
            return marker.Owner.ReleaseOwned(marker);
        }

        static CombatObjectPool GetForActiveScene()
        {
            Scene activeScene = SceneManager.GetActiveScene();
            int activeHandle = activeScene.IsValid() ? activeScene.handle : 0;
            if (instance != null && !instance.shuttingDown &&
                instance.generation == runtimeGeneration &&
                instance.sceneHandle == activeHandle)
                return instance;

            // Remove pools left by a no-domain/no-scene-reload play session.
            CombatObjectPool reusable = null;
            CombatObjectPool[] existing = UnityEngine.Object.FindObjectsByType<CombatObjectPool>(
                FindObjectsInactive.Include, FindObjectsSortMode.None);
            for (int i = 0; i < existing.Length; i++)
            {
                CombatObjectPool candidate = existing[i];
                if (candidate == null) continue;
                if (!candidate.shuttingDown && candidate.generation == runtimeGeneration &&
                    candidate.sceneHandle == activeHandle && reusable == null)
                {
                    reusable = candidate;
                    continue;
                }
                candidate.ShutdownAndDestroy();
            }
            if (reusable != null)
            {
                instance = reusable;
                return instance;
            }

            GameObject root = new GameObject("[Combat Object Pool]");
            root.hideFlags = HideFlags.HideInHierarchy;
            if (activeScene.IsValid() && activeScene.isLoaded && root.scene != activeScene)
                SceneManager.MoveGameObjectToScene(root, activeScene);
            instance = root.AddComponent<CombatObjectPool>();
            // EditMode AddComponent does not invoke MonoBehaviour.Awake, while
            // runtime does. Explicit initialization keeps both paths identical.
            instance.InitializeForScene(activeHandle);
            return instance;
        }

        void PrewarmInstances(GameObject prefab, int count)
        {
            if (shuttingDown) return;
            ObserveCurrentMatch();

            int prefabId = prefab.GetInstanceID();
            if (!prewarmedByPrefab.TryGetValue(prefabId, out List<CombatPooledObject> stash))
            {
                stash = new List<CombatPooledObject>();
                prewarmedByPrefab.Add(prefabId, stash);
            }

            int warm = stash.Count;
            if (buckets.TryGetValue(new PoolKey(prefabId, PoolKind.Projectile),
                    out Bucket projectiles))
                warm += projectiles.Available.Count + projectiles.Leased.Count;
            if (buckets.TryGetValue(new PoolKey(prefabId, PoolKind.Vfx), out Bucket effects))
                warm += effects.Available.Count + effects.Leased.Count;

            count = Mathf.Min(count, ProjectileCapacityPerPrefab);
            for (; warm < count; warm++)
            {
                GameObject created = Instantiate(prefab, transform);
                CombatPooledObject marker = created.GetComponent<CombatPooledObject>();
                if (marker == null) marker = created.AddComponent<CombatPooledObject>();
                // The spawn kind is unknown until first use; Acquire
                // re-initializes the marker into its real bucket on adoption.
                marker.Initialize(this, prefabId, (int)PoolKind.Vfx, false);
                marker.PrepareForPool();
                if (created.activeSelf) created.SetActive(false);
                marker.RestoreWhileInactive(transform);
                stash.Add(marker);
            }
        }

        GameObject Acquire(GameObject prefab, PoolKind kind, Vector3 position,
            Quaternion rotation)
        {
            ObserveCurrentMatch();

            PoolKey key = new PoolKey(prefab, kind);
            if (!buckets.TryGetValue(key, out Bucket bucket))
            {
                int capacity = kind == PoolKind.Projectile
                    ? ProjectileCapacityPerPrefab
                    : VfxCapacityPerPrefab;
                bool allowsRetention = kind != PoolKind.Vfx || IsVfxPoolEligible(prefab);
                bucket = new Bucket(prefab, capacity, allowsRetention);
                buckets.Add(key, bucket);
            }

            CombatPooledObject marker = null;
            while (bucket.Available.Count > 0 && marker == null)
            {
                int last = bucket.Available.Count - 1;
                marker = bucket.Available[last];
                bucket.Available.RemoveAt(last);
            }

            if (marker == null &&
                prewarmedByPrefab.TryGetValue(key.PrefabId,
                    out List<CombatPooledObject> stash))
            {
                while (stash.Count > 0 && marker == null)
                {
                    int last = stash.Count - 1;
                    marker = stash[last];
                    stash.RemoveAt(last);
                }
                if (stash.Count == 0) prewarmedByPrefab.Remove(key.PrefabId);
                if (marker != null)
                {
                    bool adoptedRetained = bucket.AllowsRetention &&
                        bucket.RetainedCount < bucket.Capacity;
                    marker.Initialize(this, key.PrefabId, (int)key.Kind, adoptedRetained);
                    if (adoptedRetained) bucket.RetainedCount++;
                }
            }

            if (marker == null)
            {
                GameObject created = Instantiate(prefab, position, rotation, transform);
                marker = created.GetComponent<CombatPooledObject>();
                if (marker == null) marker = created.AddComponent<CombatPooledObject>();

                bool retained = bucket.AllowsRetention &&
                    bucket.RetainedCount < bucket.Capacity;
                marker.Initialize(this, key.PrefabId, (int)key.Kind, retained);
                if (retained) bucket.RetainedCount++;
            }

            marker.BeginLease();
            bucket.Leased.Add(marker);
            marker.RestoreForReuse(transform, position, rotation);
            if (!marker.gameObject.activeSelf) marker.gameObject.SetActive(true);
            return marker.gameObject;
        }

        bool ReleaseOwned(CombatPooledObject marker)
        {
            if (marker == null || marker.Owner != this) return false;
            if (!marker.IsLeased) return true;

            PoolKey key = new PoolKey(marker.PrefabId, (PoolKind)marker.Kind);
            if (!buckets.TryGetValue(key, out Bucket bucket))
            {
                marker.BeginReturn();
                marker.PrepareForPool();
                marker.StopRetaining();
                DestroyObject(marker.gameObject);
                return true;
            }

            bucket.Leased.Remove(marker);
            marker.BeginReturn();
            marker.PrepareForPool();
            if (marker.gameObject.activeSelf) marker.gameObject.SetActive(false);

            bool canRetain = !shuttingDown && marker.Retained &&
                bucket.Prefab != null && bucket.Available.Count < bucket.Capacity &&
                marker.gameObject.scene.handle == sceneHandle;
            if (canRetain)
            {
                marker.RestoreWhileInactive(transform);
                bucket.Available.Add(marker);
            }
            else
            {
                if (marker.Retained) bucket.RetainedCount--;
                marker.StopRetaining();
                DestroyObject(marker.gameObject);
            }
            return true;
        }

        internal void HandleUnexpectedDisable(CombatPooledObject marker)
        {
            if (!shuttingDown && marker != null && marker.IsLeased)
                ReleaseOwned(marker);
        }

        internal void HandleDestroyed(CombatPooledObject marker)
        {
            if (shuttingDown || marker == null) return;
            if (prewarmedByPrefab.TryGetValue(marker.PrefabId,
                    out List<CombatPooledObject> stash))
                stash.Remove(marker);
            PoolKey key = new PoolKey(marker.PrefabId, (PoolKind)marker.Kind);
            if (!buckets.TryGetValue(key, out Bucket bucket)) return;

            bucket.Leased.Remove(marker);
            bucket.Available.Remove(marker);
            if (marker.Retained) bucket.RetainedCount = Mathf.Max(0, bucket.RetainedCount - 1);
        }

        void ObserveCurrentMatch()
        {
            MatchManager current = MatchManager.Instance;
            if (observedMatch == current) return;
            if (observedMatch != null) observedMatch.MatchEnded -= OnMatchEnded;
            observedMatch = current;
            if (observedMatch != null) observedMatch.MatchEnded += OnMatchEnded;
        }

        void OnMatchEnded(TeamId? winner)
        {
            ReleaseAllLeased();
        }

        void ReleaseAllLeased()
        {
            var active = new List<CombatPooledObject>();
            foreach (Bucket bucket in buckets.Values)
                active.AddRange(bucket.Leased);
            for (int i = 0; i < active.Count; i++)
            {
                CombatPooledObject marker = active[i];
                if (marker != null) ReleaseOwned(marker);
            }
        }

        void Shutdown()
        {
            if (shuttingDown) return;
            shuttingDown = true;
            if (observedMatch != null) observedMatch.MatchEnded -= OnMatchEnded;
            observedMatch = null;

            // Clear live gameplay state synchronously; scene/root destruction
            // then owns the actual Unity object destruction.
            foreach (Bucket bucket in buckets.Values)
            {
                foreach (CombatPooledObject marker in bucket.Leased)
                {
                    if (marker == null) continue;
                    marker.BeginReturn();
                    marker.PrepareForPool();
                    if (marker.gameObject.activeSelf) marker.gameObject.SetActive(false);
                }
                bucket.Leased.Clear();
                bucket.Available.Clear();
            }
            buckets.Clear();
            foreach (List<CombatPooledObject> stash in prewarmedByPrefab.Values)
                stash.Clear();
            prewarmedByPrefab.Clear();
        }

        void ShutdownAndDestroy()
        {
            Shutdown();
            DestroyObject(gameObject);
        }

        static void DestroyObject(GameObject target)
        {
            if (target == null) return;
            if (Application.isPlaying) Destroy(target);
            else DestroyImmediate(target);
        }
    }

    /// <summary>Ownership and authored-state snapshot for one pooled clone.</summary>
    sealed class CombatPooledObject : MonoBehaviour
    {
        readonly struct TransformState
        {
            public readonly Transform Target;
            public readonly Transform Parent;
            public readonly Vector3 LocalPosition;
            public readonly Quaternion LocalRotation;
            public readonly Vector3 LocalScale;
            public readonly bool ActiveSelf;

            public TransformState(Transform target)
            {
                Target = target;
                Parent = target.parent;
                LocalPosition = target.localPosition;
                LocalRotation = target.localRotation;
                LocalScale = target.localScale;
                ActiveSelf = target.gameObject.activeSelf;
            }

            public void Restore()
            {
                if (Target == null) return;
                if (Parent != null && Target.parent != Parent) Target.SetParent(Parent, false);
                Target.localPosition = LocalPosition;
                Target.localRotation = LocalRotation;
                Target.localScale = LocalScale;
                if (Target.gameObject.activeSelf != ActiveSelf)
                    Target.gameObject.SetActive(ActiveSelf);
            }
        }

        TransformState[] childStates = Array.Empty<TransformState>();
        ParticleSystem[] particles = Array.Empty<ParticleSystem>();
        TrailRenderer[] trails = Array.Empty<TrailRenderer>();
        Rigidbody[] bodies = Array.Empty<Rigidbody>();
        Animator[] animators = Array.Empty<Animator>();
        Vector3 authoredRootScale;

        public CombatObjectPool Owner { get; private set; }
        public int PrefabId { get; private set; }
        public int Kind { get; private set; }
        public bool Retained { get; private set; }
        public bool IsLeased { get; private set; }

        public void Initialize(CombatObjectPool owner, int prefabId, int kind, bool retained)
        {
            Owner = owner;
            PrefabId = prefabId;
            Kind = kind;
            Retained = retained;
            authoredRootScale = transform.localScale;

            Transform[] transforms = GetComponentsInChildren<Transform>(true);
            childStates = new TransformState[Mathf.Max(0, transforms.Length - 1)];
            for (int i = 1; i < transforms.Length; i++)
                childStates[i - 1] = new TransformState(transforms[i]);
            particles = GetComponentsInChildren<ParticleSystem>(true);
            trails = GetComponentsInChildren<TrailRenderer>(true);
            bodies = GetComponentsInChildren<Rigidbody>(true);
            animators = GetComponentsInChildren<Animator>(true);
        }

        public void BeginLease()
        {
            IsLeased = true;
        }

        public void BeginReturn()
        {
            IsLeased = false;
        }

        public void StopRetaining()
        {
            Retained = false;
        }

        public void RestoreForReuse(Transform poolRoot, Vector3 position, Quaternion rotation)
        {
            transform.SetParent(poolRoot, false);
            RestoreChildState();
            transform.SetPositionAndRotation(position, rotation);
            transform.localScale = authoredRootScale;
        }

        public void RestoreWhileInactive(Transform poolRoot)
        {
            transform.SetParent(poolRoot, false);
            RestoreChildState();
            transform.localPosition = Vector3.zero;
            transform.localRotation = Quaternion.identity;
            transform.localScale = authoredRootScale;
        }

        public void PrepareForPool()
        {
            Projectile projectile = GetComponent<Projectile>();
            if (projectile != null) projectile.ResetForPool();
            PooledVfxLease effect = GetComponent<PooledVfxLease>();
            if (effect != null) effect.ResetForPool();
            ProjectileImpactReadability impactCue =
                GetComponent<ProjectileImpactReadability>();
            if (impactCue != null) impactCue.ResetLease();

            for (int i = 0; i < particles.Length; i++)
            {
                ParticleSystem system = particles[i];
                if (system == null) continue;
                var main = system.main;
                main.stopAction = ParticleSystemStopAction.None;
                system.Stop(false, ParticleSystemStopBehavior.StopEmittingAndClear);
                system.Clear(false);
            }

            for (int i = 0; i < trails.Length; i++)
                if (trails[i] != null) trails[i].Clear();

            CombatFeedback.ResetEmbeddedSfx(gameObject, false);

            for (int i = 0; i < bodies.Length; i++)
            {
                if (bodies[i] == null) continue;
                bodies[i].linearVelocity = Vector3.zero;
                bodies[i].angularVelocity = Vector3.zero;
            }

            for (int i = 0; i < animators.Length; i++)
            {
                if (animators[i] == null) continue;
                animators[i].Rebind();
                animators[i].Update(0f);
            }
        }

        public void RestartVfx()
        {
            for (int i = 0; i < particles.Length; i++)
            {
                ParticleSystem system = particles[i];
                if (system == null) continue;
                var main = system.main;
                main.stopAction = ParticleSystemStopAction.None;
                system.Stop(false, ParticleSystemStopBehavior.StopEmittingAndClear);
                system.Clear(false);
                system.Play(false);
            }

            for (int i = 0; i < trails.Length; i++)
                if (trails[i] != null) trails[i].Clear();

            for (int i = 0; i < animators.Length; i++)
            {
                if (animators[i] == null) continue;
                animators[i].Rebind();
                animators[i].Update(0f);
            }

            CombatFeedback.ResetEmbeddedSfx(gameObject, true);
        }

        void RestoreChildState()
        {
            for (int i = 0; i < childStates.Length; i++) childStates[i].Restore();
        }

        void OnDisable()
        {
            if (IsLeased && Owner != null) Owner.HandleUnexpectedDisable(this);
        }

        void OnDestroy()
        {
            if (Owner != null) Owner.HandleDestroyed(this);
        }
    }

    /// <summary>Returns a pooled effect after its requested presentation time.</summary>
    sealed class PooledVfxLease : MonoBehaviour
    {
        float releaseAt;
        bool running;

        public void Begin(float lifetime)
        {
            running = true;
            releaseAt = Time.time + lifetime;
            CombatPooledObject marker = GetComponent<CombatPooledObject>();
            if (marker != null) marker.RestartVfx();
        }

        public void ResetForPool()
        {
            running = false;
            releaseAt = 0f;
        }

        void Update()
        {
            if (running && Time.time >= releaseAt)
                CombatObjectPool.Release(gameObject);
        }
    }
}
