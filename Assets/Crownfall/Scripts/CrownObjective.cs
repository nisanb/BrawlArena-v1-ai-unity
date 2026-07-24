using System.Collections.Generic;
using UnityEngine;

namespace Crownfall
{
    public enum CrownState { Hidden, Loose, Carried }

    /// The objective the game is named for.
    ///
    /// A pure 3v3 deathmatch gave players no reason to be anywhere in particular:
    /// fights happened wherever two AI happened to collide, nothing built tension,
    /// and a losing team had no way back. The Crown fixes all three at once — it is
    /// a single contested point on the map, it pays out over time so holding it is a
    /// countdown the other team can hear, and it is stealable, so no lead is safe.
    ///
    /// Rules:
    ///   - spawns at the arena centre a few seconds into the fight
    ///   - anyone can claim it by walking over it
    ///   - the carrying team scores a point every `secondsPerPoint`
    ///   - the carrier is marked for everyone, takes extra damage and moves slower
    ///   - killing the carrier drops it where they fell; it is briefly uncontestable
    ///   - a crown left lying returns to the centre so play never stalls in a corner
    ///
    /// Built entirely at runtime (mesh, material, light, beam) so the arena scene
    /// needs no forge rebuild to gain the mode.
    public class CrownObjective : MonoBehaviour
    {
        public static CrownObjective I { get; private set; }

        // ---- tuning
        public float pickupRadius = 2.2f;
        /// How far above/below the crown a fighter still counts as standing on it.
        const float VerticalReach = 3.2f;
        /// Hover height over the ground it settled on.
        const float RestHeight = 0.9f;
        public float secondsPerPoint = 2.6f;
        public float returnAfterLoose = 14f;
        public float dropCooldown = 1.2f;
        public float spawnDelay = 6f;
        // ---- the weight of the crown
        // A measured autopilot match ran away 12-34 once one side got a long
        // uncontested hold: the crown paid out forever at a fixed, survivable cost,
        // so leading compounded into more leading. The burden now RAMPS with how
        // long you have held it, and a hold has a hard ceiling. Uneasy lies the head
        // — a long reign is supposed to end.
        public const float CarrierDamageMultiplier = 1.2f;      // the moment you take it
        public const float CarrierDamageMultiplierMax = 1.85f;  // after a full reign
        public const float CarrierSpeedMultiplier = 0.95f;
        public const float CarrierSpeedMultiplierMax = 0.8f;
        /// Seconds of continuous carry over which the burden climbs to its maximum.
        public const float BurdenRampSeconds = 18f;
        /// Hard cap on one unbroken reign; the crown then returns to the centre and
        /// the arena resets around a fresh contest.
        public const float MaxHoldSeconds = 26f;

        /// 0 = just picked it up, 1 = fully burdened.
        public float CarrierBurden01 => State == CrownState.Carried
            ? Mathf.Clamp01((Time.time - carriedSince) / BurdenRampSeconds) : 0f;

        public float CurrentCarrierDamageMultiplier =>
            Mathf.Lerp(CarrierDamageMultiplier, CarrierDamageMultiplierMax, CarrierBurden01);
        public float CurrentCarrierSpeedMultiplier =>
            Mathf.Lerp(CarrierSpeedMultiplier, CarrierSpeedMultiplierMax, CarrierBurden01);

        public CrownState State { get; private set; } = CrownState.Hidden;
        public CombatMotor Carrier { get; private set; }
        public Team CarrierTeam => Carrier != null && Carrier.Identity != null
            ? Carrier.Identity.team : Team.Azure;

        Vector3 homePoint;
        float looseSince;
        float claimableAt;
        float pointAccrual;
        float carriedSince;
        Team? lastHoldingTeam;
        Transform visual;
        Transform beam;
        Light glow;
        Material crownMat;
        Material beamMat;

        static readonly Color GoldColor = new Color(1f, 0.82f, 0.28f);

        void Awake()
        {
            I = this;
            BuildVisual();
            SetVisible(false);
        }

        void OnDestroy() { if (I == this) I = null; }

        /// Called by MatchManager when a crown-enabled match starts fighting.
        public void BeginMatch(Vector3 centre)
        {
            homePoint = centre;
            State = CrownState.Hidden;
            Carrier = null;
            pointAccrual = 0f;
            SetVisible(false);
            CancelInvoke(nameof(SpawnAtHome));
            Invoke(nameof(SpawnAtHome), spawnDelay);
        }

        public void EndMatch()
        {
            CancelInvoke(nameof(SpawnAtHome));
            State = CrownState.Hidden;
            Carrier = null;
            SetVisible(false);
        }

        void SpawnAtHome()
        {
            PlaceLoose(homePoint, announce: "THE CROWN HAS RISEN");
        }

        void PlaceLoose(Vector3 pos, string announce)
        {
            // settle onto the ground so a crown dropped on a ledge is still reachable
            if (Physics.Raycast(pos + Vector3.up * 4f, Vector3.down, out var hit, 12f,
                    Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore))
                pos = hit.point;

            transform.position = pos + Vector3.up * RestHeight;
            Carrier = null;
            State = CrownState.Loose;
            looseSince = Time.time;
            claimableAt = Time.time + dropCooldown;
            SetVisible(true);
            if (visual != null) visual.SetParent(transform, false);
            if (!string.IsNullOrEmpty(announce)) MatchManager.I?.AnnounceCrown(announce);
            GameEffects.I?.RespawnFlash(pos);
        }

        void Update()
        {
            var mm = MatchManager.I;
            if (mm == null || mm.State != MatchState.Fighting || mm.Paused) return;
            if (State == CrownState.Hidden) return;

            if (State == CrownState.Carried) UpdateCarried(mm);
            else UpdateLoose(mm);

            AnimateVisual();
        }

        void UpdateCarried(MatchManager mm)
        {
            if (Carrier == null || Carrier.IsDead || !Carrier.gameObject.activeInHierarchy)
            {
                Vector3 where = Carrier != null ? Carrier.transform.position : homePoint;
                PlaceLoose(where, DropAnnouncement());
                return;
            }

            // ride above the carrier's head
            transform.position = Carrier.transform.position + Vector3.up * 2.35f;

            if (Time.time - carriedSince >= MaxHoldSeconds)
            {
                PlaceLoose(homePoint, "THE CROWN SLIPS AWAY");
                return;
            }

            pointAccrual += Time.deltaTime;
            if (pointAccrual >= secondsPerPoint)
            {
                pointAccrual -= secondsPerPoint;
                mm.AddCrownPoint(CarrierTeam);
            }
        }

        void UpdateLoose(MatchManager mm)
        {
            if (Time.time - looseSince > returnAfterLoose &&
                (transform.position - homePoint).sqrMagnitude > 4f)
            {
                PlaceLoose(homePoint, "CROWN RETURNED");
                return;
            }

            if (Time.time < claimableAt) return;

            // Planar test with a generous vertical tolerance, NOT a sphere: the crown
            // floats above whatever ground it settled on, and the arena centre sits on
            // raised terrain — a spherical check there is unreachable by a fighter
            // standing directly underneath it.
            CombatMotor best = null;
            float bestDist = pickupRadius * pickupRadius;
            foreach (var m in mm.AllFighters)
            {
                if (m == null || m.IsDead || !m.gameObject.activeInHierarchy) continue;
                Vector3 to = m.transform.position - transform.position;
                if (Mathf.Abs(to.y) > VerticalReach) continue;
                to.y = 0f;
                float d = to.sqrMagnitude;
                if (d < bestDist) { bestDist = d; best = m; }
            }
            if (best != null) Claim(best);
        }

        public void Claim(CombatMotor motor)
        {
            if (motor == null || motor.IsDead) return;
            bool teamChanged = lastHoldingTeam == null ||
                               (motor.Identity != null && lastHoldingTeam.Value != motor.Identity.team);
            bool playerInvolved = motor.Identity != null && motor.Identity.isPlayer;

            Carrier = motor;
            State = CrownState.Carried;
            pointAccrual = 0f;
            carriedSince = Time.time;
            if (motor.Identity != null) lastHoldingTeam = motor.Identity.team;
            SetVisible(true);
            motor.MarkRevealed(999f);   // no hiding in a bush with the crown

            // The crown changes hands every few seconds in a live match. Announcing
            // every single pickup turned the centre of the screen into a strobe, so
            // only the beats that change the situation get a callout — the HUD chip
            // carries the moment-to-moment state.
            if (playerInvolved)
                MatchManager.I?.AnnounceCrown("YOU HAVE THE CROWN");
            else if (teamChanged)
            {
                string who = motor.Identity != null ? motor.Identity.displayName : "SOMEONE";
                MatchManager.I?.AnnounceCrown(who.ToUpperInvariant() + " TAKES THE CROWN");
            }
            GameEffects.I?.RespawnFlash(motor.transform.position);

            // Taking it out of the other team's hands is the swing moment of the
            // mode; give it the weight of a takedown rather than a quiet pickup.
            if (teamChanged)
            {
                GameEffects.I?.Nova(ElementId.Light, motor.transform.position);
                OrbitCamera.I?.ShakeIfNear(motor.transform.position, 14f, 0.5f);
            }
        }

        /// Called by Health when the carrier dies so the drop is immediate rather
        /// than waiting a frame for the state poll.
        public void NotifyCarrierDown(CombatMotor motor)
        {
            if (State == CrownState.Carried && Carrier == motor)
                PlaceLoose(motor.transform.position, DropAnnouncement());
        }

        /// A drop is only newsworthy if the crown had actually been banking points,
        /// or if it was the player's. Otherwise the chip says it quietly.
        string DropAnnouncement()
        {
            bool wasPlayer = Carrier != null && Carrier.Identity != null && Carrier.Identity.isPlayer;
            if (wasPlayer) return "YOU LOST THE CROWN";
            return Time.time - carriedSince >= 4f ? "CROWN DROPPED" : null;
        }

        public bool IsCarriedBy(CombatMotor m) => State == CrownState.Carried && Carrier == m;

        // ------------------------------------------------------------------ visual

        void BuildVisual()
        {
            var root = new GameObject("CrownVisual").transform;
            root.SetParent(transform, false);
            visual = root;

            crownMat = MakeEmissive(GoldColor, 2.4f);

            // band
            var band = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            StripCollider(band);
            band.name = "Band";
            band.transform.SetParent(root, false);
            band.transform.localScale = new Vector3(0.46f, 0.10f, 0.46f);
            band.GetComponent<MeshRenderer>().sharedMaterial = crownMat;

            // points around the band
            const int Spikes = 8;
            for (int i = 0; i < Spikes; i++)
            {
                float a = i * Mathf.PI * 2f / Spikes;
                var spike = GameObject.CreatePrimitive(PrimitiveType.Cube);
                StripCollider(spike);
                spike.name = "Point" + i;
                spike.transform.SetParent(root, false);
                spike.transform.localPosition = new Vector3(Mathf.Cos(a) * 0.20f, 0.17f, Mathf.Sin(a) * 0.20f);
                spike.transform.localRotation = Quaternion.Euler(0f, -a * Mathf.Rad2Deg, 0f);
                spike.transform.localScale = new Vector3(0.09f, 0.26f, 0.09f);
                spike.GetComponent<MeshRenderer>().sharedMaterial = crownMat;
            }

            // a tall soft beam so the crown is findable from anywhere in the arena
            beamMat = MakeEmissive(GoldColor, 1.5f);
            beamMat.SetFloat("_Surface", 1f); // transparent
            beamMat.color = new Color(GoldColor.r, GoldColor.g, GoldColor.b, 0.16f);
            var b = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            StripCollider(b);
            b.name = "Beacon";
            b.transform.SetParent(transform, false);
            b.transform.localPosition = new Vector3(0f, 5.5f, 0f);
            b.transform.localScale = new Vector3(0.55f, 5.5f, 0.55f);
            b.GetComponent<MeshRenderer>().sharedMaterial = beamMat;
            beam = b.transform;

            var lightGo = new GameObject("CrownGlow");
            lightGo.transform.SetParent(transform, false);
            glow = lightGo.AddComponent<Light>();
            glow.type = LightType.Point;
            glow.color = GoldColor;
            glow.range = 7f;
            glow.intensity = 3.2f;
            glow.shadows = LightShadows.None;
        }

        static void StripCollider(GameObject go)
        {
            var c = go.GetComponent<Collider>();
            if (c != null) Destroy(c);
            go.layer = LayerMask.NameToLayer("Ignore Raycast");
        }

        static Material MakeEmissive(Color c, float intensity)
        {
            var shader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
            var m = new Material(shader);
            m.color = c;
            if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", c);
            m.EnableKeyword("_EMISSION");
            if (m.HasProperty("_EmissionColor")) m.SetColor("_EmissionColor", c * intensity);
            if (m.HasProperty("_Smoothness")) m.SetFloat("_Smoothness", 0.85f);
            if (m.HasProperty("_Metallic")) m.SetFloat("_Metallic", 0.9f);
            return m;
        }

        void SetVisible(bool on)
        {
            foreach (var r in GetComponentsInChildren<Renderer>(true)) r.enabled = on;
            if (glow != null) glow.enabled = on;
        }

        void AnimateVisual()
        {
            if (visual == null) return;
            visual.Rotate(Vector3.up, 68f * Time.deltaTime, Space.Self);
            float bob = Mathf.Sin(Time.time * 2.4f) * 0.11f;
            visual.localPosition = new Vector3(0f, bob, 0f);

            // the beacon only marks a LOOSE crown — a carried one is already marked
            // by the carrier themselves, and a beam riding a running player reads as
            // a bug rather than a highlight
            if (beam != null) beam.gameObject.SetActive(State == CrownState.Loose);

            if (glow != null)
                glow.intensity = 2.6f + Mathf.Sin(Time.time * 3.6f) * 0.7f;

            if (State == CrownState.Carried && crownMat != null && Carrier != null &&
                Carrier.Identity != null)
            {
                // tint toward the holding team so a glance reads who is banking points
                Color c = Color.Lerp(GoldColor, Carrier.Identity.TeamColor, 0.35f);
                crownMat.SetColor("_EmissionColor", c * 2.6f);
                if (glow != null) glow.color = c;
            }
            else if (crownMat != null)
            {
                crownMat.SetColor("_EmissionColor", GoldColor * 2.4f);
                if (glow != null) glow.color = GoldColor;
            }
        }

        /// Spawn the objective into a live scene if the forge never built one.
        public static CrownObjective Ensure()
        {
            if (I != null) return I;
            var go = new GameObject("Crown");
            return go.AddComponent<CrownObjective>();
        }
    }
}
