using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

namespace BrawlArena
{
    /// <summary>
    /// Gem Grab mode: a mine at the arena center pops out gems on an interval.
    /// Brawlers collect them by walking over them; when a team holds
    /// <see cref="gemsToWin"/> or more, its victory countdown runs and winning
    /// requires keeping the lead until it hits zero. KO'd brawlers scatter
    /// everything they carried. Inactive (component disabled logic) unless the
    /// MatchManager mode is GemGrab.
    /// </summary>
    public class GemGrabManager : MonoBehaviour
    {
        public static GemGrabManager Instance { get; private set; }

        [Header("Rules")]
        public int gemsToWin = 10;
        public float countdownDuration = 15f;
        public float spawnInterval = 5f;
        public int maxLooseGems = 12;

        [Header("Wiring (optional)")]
        [Tooltip("Visual used for each gem; a glowing primitive is generated when empty.")]
        public GameObject gemPrefab;
        public GameObject spawnVfx;
        public GameObject pickupVfx;
        public Vector3 minePosition = Vector3.zero;

        public TeamId? CountdownTeam { get; private set; }
        public float CountdownRemaining { get; private set; }

        readonly List<Gem> looseGems = new List<Gem>();
        readonly Dictionary<BrawlerController, int> carried = new Dictionary<BrawlerController, int>();
        float nextSpawnAt;
        bool hooked;

        public bool ActiveMode =>
            MatchManager.Instance != null && MatchManager.Instance.mode == GameMode.GemGrab;

        void Awake()
        {
            Instance = this;
        }

        void OnDestroy()
        {
            if (Instance == this) Instance = null;
            if (hooked && MatchManager.Instance != null)
                MatchManager.Instance.Kill -= OnKill;
        }

        void Start()
        {
            if (MatchManager.Instance != null)
            {
                MatchManager.Instance.Kill += OnKill;
                hooked = true;
            }
        }

        public int TeamGems(TeamId team)
        {
            int sum = 0;
            foreach (var kv in carried)
                if (kv.Key != null && kv.Key.team == team) sum += kv.Value;
            return sum;
        }

        public int CarriedBy(BrawlerController b)
        {
            return b != null && carried.TryGetValue(b, out int n) ? n : 0;
        }

        /// <summary>Team currently holding more gems, or null when tied.</summary>
        public TeamId? LeadingTeam()
        {
            int blue = TeamGems(TeamId.Blue);
            int red = TeamGems(TeamId.Red);
            if (blue == red) return null;
            return blue > red ? TeamId.Blue : TeamId.Red;
        }

        public Gem NearestLooseGem(Vector3 from)
        {
            Gem best = null;
            float bestDist = float.MaxValue;
            foreach (var gem in looseGems)
            {
                if (gem == null || !gem.CanBePicked) continue;
                float d = (gem.transform.position - from).sqrMagnitude;
                if (d < bestDist)
                {
                    bestDist = d;
                    best = gem;
                }
            }
            return best;
        }

        void Update()
        {
            if (!ActiveMode) return;
            var mm = MatchManager.Instance;
            if (mm.State != MatchState.Playing)
            {
                if (mm.State == MatchState.Intro) nextSpawnAt = Time.time + spawnInterval * 0.5f;
                return;
            }

            looseGems.RemoveAll(g => g == null);

            if (Time.time >= nextSpawnAt && looseGems.Count < maxLooseGems)
            {
                nextSpawnAt = Time.time + spawnInterval;
                SpawnGemAtMine();
            }

            CollectTouchedGems();
            UpdateCountdown();
        }

        void SpawnGemAtMine()
        {
            // Ring around the mine so gems never stack on the exact same spot.
            float angle = Random.value * Mathf.PI * 2f;
            float radius = 1.2f + Random.value * 1.8f;
            Vector3 pos = minePosition + new Vector3(Mathf.Sin(angle) * radius, 0f, Mathf.Cos(angle) * radius);
            if (NavMesh.SamplePosition(pos, out NavMeshHit hit, 3f, NavMesh.AllAreas)) pos = hit.position;
            looseGems.Add(Gem.Create(this, minePosition + Vector3.up * 0.6f, pos));
            if (spawnVfx != null) BrawlerController.SpawnVfx(spawnVfx, pos + Vector3.up * 0.3f, Quaternion.identity, 2f);
        }

        void CollectTouchedGems()
        {
            var brawlers = MatchManager.Instance.GetBrawlers();
            for (int gi = looseGems.Count - 1; gi >= 0; gi--)
            {
                var gem = looseGems[gi];
                if (gem == null || !gem.CanBePicked) continue;
                foreach (var b in brawlers)
                {
                    if (b == null || b.IsDead || !b.CanAct) continue;
                    Vector3 d = b.transform.position - gem.transform.position;
                    d.y = 0f;
                    // Radius must exceed the largest AI stoppingDistance (1.5+)
                    // or bots park just outside pickup range forever.
                    if (d.sqrMagnitude > 1.8f * 1.8f) continue;
                    carried[b] = CarriedBy(b) + 1;
                    looseGems.RemoveAt(gi);
                    gem.CollectInto(b.transform);
                    if (pickupVfx != null)
                        BrawlerController.SpawnVfx(pickupVfx, gem.transform.position, Quaternion.identity, 1.5f);
                    break;
                }
            }
        }

        void UpdateCountdown()
        {
            int blue = TeamGems(TeamId.Blue);
            int red = TeamGems(TeamId.Red);

            TeamId? leader = null;
            if (blue >= gemsToWin && blue > red) leader = TeamId.Blue;
            else if (red >= gemsToWin && red > blue) leader = TeamId.Red;

            if (leader != CountdownTeam)
            {
                CountdownTeam = leader;
                CountdownRemaining = countdownDuration;
                return;
            }
            if (CountdownTeam == null) return;

            CountdownRemaining -= Time.deltaTime;
            if (CountdownRemaining <= 0f)
                MatchManager.Instance.DeclareWinner(CountdownTeam.Value);
        }

        void OnKill(BrawlerController victim, BrawlerController attacker)
        {
            if (!ActiveMode) return;
            int dropped = CarriedBy(victim);
            if (dropped <= 0) return;
            carried[victim] = 0;

            // Scatter in a fan around the corpse.
            for (int i = 0; i < dropped; i++)
            {
                float angle = (i / (float)dropped) * Mathf.PI * 2f + Random.value * 0.6f;
                float radius = 1.1f + Random.value * 1.3f;
                Vector3 pos = victim.transform.position +
                              new Vector3(Mathf.Sin(angle) * radius, 0f, Mathf.Cos(angle) * radius);
                if (NavMesh.SamplePosition(pos, out NavMeshHit hit, 3f, NavMesh.AllAreas)) pos = hit.position;
                looseGems.Add(Gem.Create(this, victim.transform.position + Vector3.up * 1f, pos));
            }
        }
    }

    /// <summary>
    /// One collectible gem. Pops out of its origin in a small arc, then bobs
    /// and spins in place until collected, when it zips into the collector and
    /// disappears. Pure transform animation — pickup is distance-polled by the
    /// manager, so no physics setup is needed.
    /// </summary>
    public class Gem : MonoBehaviour
    {
        const float FlyOutDuration = 0.45f;
        const float CollectDuration = 0.22f;

        Vector3 flyFrom;
        Vector3 restPos;
        float stateStart;
        Transform collector;
        enum State { FlyOut, Idle, Collected }
        State state;

        public bool CanBePicked => state == State.Idle;

        public static Gem Create(GemGrabManager mgr, Vector3 from, Vector3 restPosition)
        {
            var go = new GameObject("Gem");
            var gem = go.AddComponent<Gem>();
            gem.flyFrom = from;
            gem.restPos = restPosition;
            gem.stateStart = Time.time;
            gem.state = State.FlyOut;
            go.transform.position = from;

            GameObject visual;
            if (mgr != null && mgr.gemPrefab != null)
            {
                visual = Instantiate(mgr.gemPrefab, go.transform, false);
            }
            else
            {
                visual = GameObject.CreatePrimitive(PrimitiveType.Cube);
                Object.Destroy(visual.GetComponent<Collider>());
                visual.transform.SetParent(go.transform, false);
                visual.transform.localScale = new Vector3(0.34f, 0.5f, 0.34f);
                visual.transform.localRotation = Quaternion.Euler(45f, 0f, 45f);
                var mr = visual.GetComponent<MeshRenderer>();
                var shader = Shader.Find("Universal Render Pipeline/Lit");
                if (shader != null)
                {
                    var m = new Material(shader);
                    m.SetColor("_BaseColor", new Color(0.2f, 1f, 0.55f));
                    m.EnableKeyword("_EMISSION");
                    m.SetColor("_EmissionColor", new Color(0.1f, 1.6f, 0.6f));
                    mr.sharedMaterial = m;
                }
            }
            visual.name = "Visual";
            return gem;
        }

        public void CollectInto(Transform target)
        {
            collector = target;
            state = State.Collected;
            stateStart = Time.time;
        }

        void Update()
        {
            float t = Time.time - stateStart;
            switch (state)
            {
                case State.FlyOut:
                {
                    float k = Mathf.Clamp01(t / FlyOutDuration);
                    Vector3 pos = Vector3.Lerp(flyFrom, restPos, k);
                    pos.y += Mathf.Sin(k * Mathf.PI) * 1.2f; // arc
                    transform.position = pos;
                    if (k >= 1f)
                    {
                        state = State.Idle;
                        stateStart = Time.time;
                    }
                    break;
                }
                case State.Idle:
                {
                    transform.position = restPos + Vector3.up * (0.45f + Mathf.Sin(Time.time * 2.4f) * 0.09f);
                    transform.rotation = Quaternion.Euler(0f, Time.time * 95f % 360f, 0f);
                    break;
                }
                case State.Collected:
                {
                    if (collector == null)
                    {
                        Destroy(gameObject);
                        return;
                    }
                    float k = Mathf.Clamp01(t / CollectDuration);
                    transform.position = Vector3.Lerp(
                        transform.position, collector.position + Vector3.up * 1.4f, k);
                    transform.localScale = Vector3.one * (1f - k);
                    if (k >= 1f) Destroy(gameObject);
                    break;
                }
            }
        }
    }
}
