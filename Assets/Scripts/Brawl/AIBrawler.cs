using UnityEngine;

namespace BrawlArena
{
    public enum AIBrawlerObjective
    {
        None,
        Combat,
        Retreat,
        ControlZone,
        GemGrab,
        Experience,
    }

    /// <summary>Strategy-triangle archetype derived from a brawler's own stats.</summary>
    public enum AIRole
    {
        Warrior,
        Mage,
        Archer,
    }

    /// <summary>
    /// Backend-independent brawler brain used for both AI teammates and enemies.
    /// Melee units chase with a small flank offset; ranged units hold a firing
    /// band and kite. Everyone retreats briefly when badly hurt. One selected
    /// navigation component owns path translation; Brawl retains all tactics.
    /// </summary>
    [RequireComponent(typeof(BrawlerController))]
    public class AIBrawler : MonoBehaviour
    {
        public float thinkInterval = 0.25f;
        [Tooltip("0 = derive automatically from weapon type.")]
        public float preferredRange = 0f;
        public float retreatBelowPct = 0.28f;
        public float resumeAbovePct = 0.5f;
        [Min(2f)] public float experienceBoxSeekRange = 12f;

        BrawlerController self;
        [SerializeField, HideInInspector] MonoBehaviour navigationSource;
        [SerializeField, HideInInspector] bool navigationLocked;
        IBrawlerNavigation navigation;
        BrawlerController target;
        float nextThink;
        float attackReadyAt;
        float nextWardStepAt;
        bool retreating;
        float strafeSign;

        bool Ranged => self.projectilePrefab != null;
        public AIBrawlerObjective CurrentObjective { get; private set; }
        public AIRole Role { get; private set; }

        public static AIBrawlerObjective ResolveModeObjective(GameMode mode,
            bool retreating)
        {
            if (retreating) return AIBrawlerObjective.Retreat;
            if (mode == GameMode.ControlZone) return AIBrawlerObjective.ControlZone;
            if (mode == GameMode.GemGrab) return AIBrawlerObjective.GemGrab;
            return AIBrawlerObjective.Combat;
        }

        /// <summary>
        /// Melee (no projectile) is always Warrior. Ranged units split by
        /// range: long-band sniping is Archer, shorter-band casting is Mage.
        /// </summary>
        public static AIRole ResolveRole(bool hasProjectile, float attackRange)
        {
            if (!hasProjectile) return AIRole.Warrior;
            return attackRange >= 8f ? AIRole.Archer : AIRole.Mage;
        }

        /// <summary>
        /// Gem Grab lead-protection destination: holds a fraction of the way
        /// home instead of retreating all the way, so the carrier stays close
        /// enough for a fight to still reach them.
        /// </summary>
        public static Vector3 GemCarrierHoldPosition(Vector3 currentPosition,
            Vector3 homePosition, float towardHomeFraction)
        {
            return Vector3.Lerp(currentPosition, homePosition, Mathf.Clamp01(towardHomeFraction));
        }

        public IBrawlerNavigation Navigation
        {
            get
            {
                RestoreNavigationReference();
                return navigation;
            }
        }

        void Awake()
        {
            self = GetComponent<BrawlerController>();
            DiscoverNavigationComponents();
            strafeSign = Random.value < 0.5f ? -1f : 1f;
        }

        void OnEnable()
        {
            RestoreNavigationReference();
        }

        void Start()
        {
            InitializeNavigation();
            if (preferredRange <= 0f)
                preferredRange = Ranged ? 7.5f : self.attackRange * 0.8f;
            Role = ResolveRole(Ranged, self.attackRange);
            nextThink = Time.time + Random.Range(0f, thinkInterval);
        }

        /// <summary>
        /// Installs the sole navigation planner before Start locks ownership.
        /// Production planners must be component-backed and share this root.
        /// </summary>
        public void SetNavigation(IBrawlerNavigation selectedNavigation)
        {
            if (selectedNavigation == null)
                throw new System.ArgumentNullException(nameof(selectedNavigation));
            if (navigationLocked)
                throw new System.InvalidOperationException(
                    "Brawler navigation must be selected before Start.");
            if (navigation != null &&
                !object.ReferenceEquals(navigation, selectedNavigation))
                throw new System.InvalidOperationException(
                    "An AI brawler can have only one navigation component.");
            if (!(selectedNavigation is MonoBehaviour source))
                throw new System.ArgumentException(
                    "Production brawler navigation must be a MonoBehaviour on the AI root.",
                    nameof(selectedNavigation));
            if (source.gameObject != gameObject)
                throw new System.ArgumentException(
                    "Component-backed brawler navigation must live on the AI root.",
                    nameof(selectedNavigation));

            navigation = selectedNavigation;
            navigationSource = source;
        }

        void InitializeNavigation()
        {
            EnsureNavigationSelected();
            float stoppingDistance = Ranged
                ? 0.5f
                : Mathf.Max(0.5f, self.attackRange * 0.65f);
            navigation.Initialize(self.moveSpeed, stoppingDistance);
            navigationLocked = true;
        }

        void EnsureNavigationSelected()
        {
            RestoreNavigationReference();
            DiscoverNavigationComponents();

            if (navigation == null)
                throw new System.InvalidOperationException(
                    "An heavy-backed AI brawler requires one configured navigation component before Start.");
        }

        void DiscoverNavigationComponents()
        {
            RestoreNavigationReference();
            MonoBehaviour[] components = GetComponents<MonoBehaviour>();
            for (int i = 0; i < components.Length; i++)
            {
                if (!(components[i] is IBrawlerNavigation candidate)) continue;
                if (navigation != null && !object.ReferenceEquals(navigation, candidate))
                    throw new System.InvalidOperationException(
                        "An AI brawler can have only one navigation component.");
                navigation = candidate;
                navigationSource = components[i];
            }
        }

        void RestoreNavigationReference()
        {
            if (navigation == null && navigationSource is IBrawlerNavigation restored)
                navigation = restored;
        }

        void Update()
        {
            if (!self.CanAct)
            {
                self.SetMoveInput(Vector3.zero);
                if (navigation != null && navigation.HasPath) navigation.ClearPath();
                return;
            }

            // Cast windups slow rather than root movement, so bots keep their
            // committed path while obeying the same 80% speed rule as players.
            if (Time.time >= nextThink)
            {
                Think();
                nextThink = Time.time + thinkInterval;
            }

            // A tracked target that slipped into grass is lost, not chased.
            if (target != null && target.Concealment != null &&
                target.Concealment.IsHiddenFrom(self))
                target = null;

            bool engaging = false;
            float distToTarget = float.MaxValue;
            if (target != null && !target.IsDead)
            {
                distToTarget = PlanarDistance(transform.position, target.transform.position);
                float engageRange = Ranged ? preferredRange + 1.5f : self.attackRange + 0.4f;
                if (self.SuperReady) engageRange = Mathf.Max(engageRange, self.SuperAimRange);
                if (distToTarget <= engageRange)
                {
                    engaging = true;
                    FaceTarget();
                    bool usedSuper = !retreating && self.SuperReady && self.TrySuper(target);
                    if (!usedSuper && !retreating && self.BasicAttackReady &&
                        Time.time >= attackReadyAt && self.TryAttack(target))
                        attackReadyAt = Time.time + self.attackCooldown + Random.Range(0.05f, 0.35f);
                }
            }
            navigation.SetExternalFacing(engaging);
            BufferNavigationIntent();
        }

        void BufferNavigationIntent()
        {
            if (navigation == null || !navigation.IsReady)
            {
                self.SetMoveInput(Vector3.zero);
                return;
            }

            Vector3 desired = navigation.DesiredVelocity;
            desired.y = 0f;
            self.SetMoveInput(Vector3.ClampMagnitude(
                desired / Mathf.Max(0.1f, self.CurrentSpeed), 1f));
        }

        void Think()
        {
            target = PickTarget();
            CurrentObjective = AIBrawlerObjective.None;

            float hpPct = self.Health.Current / Mathf.Max(1f, self.Health.Max);
            // Heavy gem carriers play safer: bail out of fights earlier.
            float retreatAt = retreatBelowPct + (CarriedGems() >= 4 ? 0.17f : 0f);
            if (retreating && hpPct >= resumeAbovePct) retreating = false;
            else if (!retreating && hpPct <= retreatAt) retreating = true;
            // HP never regenerates, so a hurt bot with no pursuer must fight
            // on or it would hide in a corner for the rest of the match.
            if (retreating && (target == null ||
                PlanarDistance(transform.position, target.transform.position) > 12f))
                retreating = false;

            TryTacticalWardStep();

            if (!navigation.IsReady) return;
            if (retreating) CurrentObjective = AIBrawlerObjective.Retreat;
            if (!retreating && ThinkControlZone()) return;
            if (!retreating && ThinkGems()) return;
            if (!retreating && ThinkExperienceBox()) return;
            if (target == null)
            {
                if (navigation.HasPath) navigation.ClearPath();
                return;
            }

            CurrentObjective = retreating
                ? AIBrawlerObjective.Retreat
                : AIBrawlerObjective.Combat;

            Vector3 myPos = transform.position;
            Vector3 tPos = target.transform.position;
            Vector3 away = myPos - tPos;
            away.y = 0f;
            away = away.sqrMagnitude > 0.01f ? away.normalized : -transform.forward;

            Vector3 dest;
            if (retreating)
            {
                dest = myPos + away * 6f;
            }
            else if (Ranged)
            {
                dest = RoleBandDestination(myPos, tPos, away, PlanarDistance(myPos, tPos));
            }
            else
            {
                // Approach with a small flank offset so allies don't stack up.
                Vector3 flank = Vector3.Cross(Vector3.up, away) * (strafeSign * 1.2f);
                dest = tPos + flank;
            }

            if (navigation.TrySamplePosition(dest, 3f, out Vector3 sampledDestination))
                dest = sampledDestination;
            navigation.SetDestination(dest);
        }

        void TryTacticalWardStep()
        {
            if (target == null || target.IsDead || Time.time < nextWardStepAt) return;

            Vector3 toTarget = target.transform.position - transform.position;
            toTarget.y = 0f;
            float distance = toTarget.magnitude;
            if (distance <= 0.01f) return;

            Vector3 direction = Vector3.zero;
            bool protectingLead = GemGrabManager.Instance != null &&
                                  GemGrabManager.Instance.ActiveMode &&
                                  GemGrabManager.Instance.CountdownTeam == self.team &&
                                  CarriedGems() > 0;
            if ((retreating || protectingLead) && distance < 7f)
            {
                direction = -toTarget.normalized;
            }
            else if (!Ranged && distance > self.attackRange + 2.2f &&
                     self.WardFlow >= self.wardStepCost * 2f)
            {
                // Melee bots reserve one charge instead of dumping all three
                // with frame-perfect reactions.
                direction = toTarget.normalized;
            }
            else if (Ranged && distance < preferredRange * 0.48f)
            {
                direction = -toTarget.normalized;
            }

            if (direction.sqrMagnitude <= 0.01f || !self.TryWardStep(direction)) return;
            nextWardStepAt = Time.time + Random.Range(0.65f, 0.9f);
        }

        int CarriedGems()
        {
            return GemGrabManager.Instance != null ? GemGrabManager.Instance.CarriedBy(self) : 0;
        }

        /// <summary>
        /// Control Zone is the primary mode objective. Bots return when outside,
        /// hold a spread tactical point when safe, and keep close combat inside
        /// the authoritative boundary instead of chasing away from the objective.
        /// </summary>
        bool ThinkControlZone()
        {
            ControlZoneManager zone = ControlZoneManager.Instance;
            if (zone == null || !zone.ActiveMode) return false;

            CurrentObjective = AIBrawlerObjective.ControlZone;
            Vector3 destination = zone.TacticalPoint(self.team, strafeSign);
            bool inside = zone.Contains(transform.position);
            float enemyDistance = target != null && !target.IsDead
                ? PlanarDistance(transform.position, target.transform.position)
                : float.MaxValue;
            float immediateCombatRange = Ranged
                ? Mathf.Max(preferredRange + 1.5f, 6f)
                : self.attackRange + 3f;

            if (inside && enemyDistance <= immediateCombatRange)
            {
                // Warriors drive straight into melee; Archers/Mages keep
                // their combat band even while holding the objective.
                Vector3 engageDestination = target.transform.position;
                if (Role != AIRole.Warrior)
                {
                    Vector3 myPosNow = transform.position;
                    Vector3 awayNow = myPosNow - target.transform.position;
                    awayNow.y = 0f;
                    awayNow = awayNow.sqrMagnitude > 0.01f ? awayNow.normalized : -transform.forward;
                    engageDestination = RoleBandDestination(myPosNow, target.transform.position,
                        awayNow, enemyDistance);
                }
                destination = zone.ClampInside(engageDestination, 0.8f);
            }
            else if (inside &&
                     ((self.team == TeamId.Blue &&
                       zone.State == ControlZoneState.BlueControlled) ||
                      (self.team == TeamId.Red &&
                       zone.State == ControlZoneState.RedControlled)))
                destination = zone.TacticalPoint(self.team, strafeSign);

            if (navigation.TrySamplePosition(destination, 3f,
                    out Vector3 sampledDestination))
                destination = zone.ClampInside(sampledDestination, 0.55f);
            navigation.SetDestination(destination);
            return true;
        }

        /// <summary>
        /// Gem Grab priorities. True when this think tick already chose a
        /// destination: collect nearby loose gems unless an enemy is breathing
        /// down our neck, and once our team's countdown is running, carriers
        /// hold a defensible midfield position (not a full retreat home) to
        /// protect the lead without stalling the match out of reach.
        /// </summary>
        bool ThinkGems()
        {
            var mgr = GemGrabManager.Instance;
            if (mgr == null || !mgr.ActiveMode) return false;

            // Protect the lead: countdown running for us and we hold gems.
            // Holding midfield (rather than sprinting all the way home) keeps
            // the carrier reachable so the countdown can still be contested
            // instead of stalling the match in an unreachable corner.
            if (mgr.CountdownTeam == self.team && CarriedGems() > 0)
            {
                Vector3 home = ArenaLayout.TeamHomePosition(self.team, transform.position.x);
                Vector3 hold = GemCarrierHoldPosition(transform.position, home, 0.4f);
                if (navigation.TrySamplePosition(
                        hold, 4f, out Vector3 sampledHold))
                {
                    CurrentObjective = AIBrawlerObjective.GemGrab;
                    navigation.SetDestination(sampledHold);
                    return true;
                }
            }

            var gem = mgr.NearestLooseGem(transform.position);
            if (gem == null) return false;

            float gemDist = PlanarDistance(transform.position, gem.transform.position);
            float enemyDist = target != null && !target.IsDead
                ? PlanarDistance(transform.position, target.transform.position)
                : float.MaxValue;

            // Fight instead when an enemy is close and the gem isn't a snap grab.
            if (enemyDist < 5f && gemDist > 3f) return false;

            CurrentObjective = AIBrawlerObjective.GemGrab;
            navigation.SetDestination(gem.transform.position);
            return true;
        }

        /// <summary>
        /// XP boxes are a secondary opportunity. Bots only divert when no mode
        /// objective already claimed the tick and no enemy is close enough to
        /// demand an immediate combat response.
        /// </summary>
        bool ThinkExperienceBox()
        {
            MatchExperienceSystem system = MatchExperienceSystem.Instance;
            if (system == null || !system.Active) return false;

            HeroMatchProgression progression = self.GetComponent<HeroMatchProgression>();
            if (progression == null || progression.Level >= HeroMatchProgression.MaxLevel)
                return false;

            ExperienceBox box = system.NearestExperienceBox(transform.position);
            if (box == null) return false;

            float boxDistance = PlanarDistance(transform.position, box.transform.position);
            if (boxDistance > experienceBoxSeekRange) return false;

            float enemyDistance = target != null && !target.IsDead
                ? PlanarDistance(transform.position, target.transform.position)
                : float.MaxValue;
            float immediateCombatRange = Ranged
                ? Mathf.Max(preferredRange + 1f, 6f)
                : self.attackRange + 2.5f;
            if (enemyDistance <= immediateCombatRange) return false;

            navigation.SetDestination(box.transform.position);
            CurrentObjective = AIBrawlerObjective.Experience;
            return true;
        }

        BrawlerController PickTarget()
        {
            if (MatchManager.Instance == null) return null;
            BrawlerController best = null;
            float bestScore = float.MinValue;
            bool zoneActive = ControlZoneManager.Instance != null &&
                               ControlZoneManager.Instance.ActiveMode;
            foreach (var b in MatchManager.Instance.GetBrawlers())
            {
                if (b == null || b == self || b.team == self.team || b.IsDead) continue;
                // Bots cannot target what grass concealment hides from them.
                if (b.Concealment != null && b.Concealment.IsHiddenFrom(self)) continue;
                float d = PlanarDistance(transform.position, b.transform.position);
                float score = -d;
                // Archers finish low-health targets fastest since they can
                // safely poke from range.
                score += (1f - b.Health.Current / Mathf.Max(1f, b.Health.Max)) *
                         (Role == AIRole.Archer ? 5f : 3f);
                // Warriors care about the zone twice as much as anyone else:
                // holding it is their whole job.
                if (zoneActive && ControlZoneManager.Instance.Contains(b.transform.position))
                    score += Role == AIRole.Warrior ? 8f : 4f;
                if (b.IsPlayer) score += 0.5f;

                AIRole targetRole = ResolveRole(b.projectilePrefab != null, b.attackRange);
                if (Role == AIRole.Archer && targetRole == AIRole.Mage) score += 2f;
                if (Role == AIRole.Mage && targetRole == AIRole.Warrior) score += 2f;

                if (score > bestScore)
                {
                    bestScore = score;
                    best = b;
                }
            }
            return best;
        }

        void FaceTarget()
        {
            Vector3 dir = target.transform.position - transform.position;
            dir.y = 0f;
            if (dir.sqrMagnitude < 0.01f) return;
            self.Motor?.Face(dir, false);
        }

        /// <summary>
        /// Range-keeping steer for Ranged roles: Archers hold [attackRange*0.6,
        /// attackRange] and back off sharply once an enemy closes under 4.5m;
        /// Mages hold the flatter [5, attackRange] band. Only called for
        /// Archer/Mage — Warriors always close straight to melee range.
        /// </summary>
        Vector3 RoleBandDestination(Vector3 myPos, Vector3 targetPos, Vector3 away,
            float enemyDistance)
        {
            if (Role == AIRole.Archer)
            {
                float bandMin = self.attackRange * 0.6f;
                float bandMax = self.attackRange;
                if (enemyDistance < 4.5f) return myPos + away * 3f;
                if (enemyDistance < bandMin) return myPos + away * 2.5f;
                if (enemyDistance > bandMax) return targetPos + away * self.attackRange;
                return myPos + Vector3.Cross(Vector3.up, away) * (strafeSign * 2.5f);
            }

            // Mage.
            const float mageBandMin = 5f;
            float mageBandMax = self.attackRange;
            if (enemyDistance < mageBandMin) return myPos + away * 2.5f;
            if (enemyDistance > mageBandMax) return targetPos + away * self.attackRange;
            return myPos + Vector3.Cross(Vector3.up, away) * (strafeSign * 2.5f);
        }

        static float PlanarDistance(Vector3 a, Vector3 b)
        {
            a.y = 0f;
            b.y = 0f;
            return Vector3.Distance(a, b);
        }
    }
}
