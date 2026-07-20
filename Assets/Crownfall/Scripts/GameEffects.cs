using System.Collections;
using UnityEngine;
using DamageNumbersPro;

namespace Crownfall
{
    /// Scene singleton catalog of VFX/SFX, wired by the forge. All spawn helpers
    /// are defensive: a missing prefab never breaks combat.
    public class GameEffects : MonoBehaviour
    {
        [System.Serializable]
        public class ElementSet
        {
            public ElementId id;
            public GameObject slashHit;
            public GameObject missile;
            public GameObject explosion;
            public GameObject muzzle;
            public GameObject nova;
            public GameObject enchant;
            public GameObject charge;       // cast wind-up glow at the wand tip
            public GameObject slash;        // melee light swing arc
            public GameObject cleave;       // melee heavy swing arc
            public GameObject sphereBlast;  // nova radial burst
            public GameObject pillar;       // nova ground pillar
            public AudioClip castSound;
            public AudioClip impactSound;
        }

        public ElementSet[] elements;
        public GameObject respawnFlash;
        public GameObject stunFx;

        public DamageNumber damageNumberPrefab;
        public DamageNumber blockedNumberPrefab;

        public AudioClip swingLight;
        public AudioClip swingHeavy;
        public AudioClip meleeImpact;
        public AudioClip blockImpact;
        public AudioClip rollWhoosh;
        public AudioClip deathCry;
        public AudioClip uiTick;
        public AudioClip uiFight;
        public AudioClip uiVictory;
        public AudioClip uiDefeat;
        public AudioClip killDing;

        public static GameEffects I { get; private set; }

        float baseTimeScale = 1f;
        Coroutine hitstopRoutine;

        void Awake() { I = this; baseTimeScale = 1f; }

        public ElementSet Set(ElementId el)
        {
            if (elements == null) return null;
            foreach (var s in elements) if (s != null && s.id == el) return s;
            return null;
        }

        // ------------------------------------------------------------------ vfx

        public void MeleeImpact(ElementId el, Vector3 pos, bool blocked, bool heavy)
        {
            var set = Set(el);
            SpawnTemp(set?.slashHit, pos, Quaternion.identity, blocked ? 0.7f : (heavy ? 1.25f : 1f));
            PlayAt(blocked ? blockImpact : meleeImpact, pos, blocked ? 0.9f : 0.8f, Random.Range(0.92f, 1.08f));
            if (set != null && !blocked) PlayAt(set.impactSound, pos, 0.35f, Random.Range(0.95f, 1.1f));
        }

        public void Explosion(ElementId el, Vector3 pos)
        {
            var set = Set(el);
            SpawnTemp(set?.explosion, pos, Quaternion.identity, 1f);
            if (set != null) PlayAt(set.impactSound, pos, 0.75f, Random.Range(0.92f, 1.05f));
        }

        public void Muzzle(ElementId el, Vector3 pos, Quaternion rot)
        {
            SpawnTemp(Set(el)?.muzzle, pos, rot, 0.9f);
        }

        /// Cast wind-up VFX at the wand tip; returns a handle the caster destroys on
        /// release. Self-destructs after 2s as a safety net if the cast is interrupted.
        public GameObject SpawnCharge(ElementId el, Transform at, float scale)
        {
            if (at == null) return null;
            var set = Set(el);
            var prefab = set != null && set.charge != null ? set.charge : set?.muzzle;
            if (prefab == null) return null;
            var go = Instantiate(prefab, at.position, at.rotation, at);
            go.transform.localPosition = Vector3.zero;
            if (!Mathf.Approximately(scale, 1f)) go.transform.localScale *= scale;
            Destroy(go, 2f);
            return go;
        }

        /// Elemental slash/cleave arc thrown along a melee swing.
        public void SlashArc(ElementId el, Vector3 pos, Quaternion rot, bool heavy)
        {
            var set = Set(el);
            var prefab = heavy && set != null && set.cleave != null ? set.cleave : set?.slash;
            SpawnTemp(prefab, pos, rot, heavy ? 1.35f : 1f);
        }

        public void Nova(ElementId el, Vector3 pos)
        {
            var set = Set(el);
            // a "get off me" panic burst should read BIG: core nova + a radial sphere
            // shockwave + a ground pillar erupting through the caster
            SpawnTemp(set?.nova, pos + Vector3.up * 0.1f, Quaternion.identity, 1.15f);
            SpawnTemp(set?.sphereBlast, pos + Vector3.up * 0.5f, Quaternion.identity, 1.2f);
            SpawnTemp(set?.pillar, pos, Quaternion.identity, 1f);
            if (set != null) PlayAt(set.impactSound, pos, 1f, 0.8f);
        }

        public void RespawnFlash(Vector3 pos)
        {
            SpawnTemp(respawnFlash, pos + Vector3.up * 0.1f, Quaternion.identity, 1f);
        }

        /// Orbiting "dizzy stars" above a staggered fighter. Caller destroys it.
        public GameObject SpawnStun(Transform owner)
        {
            if (stunFx == null) return null;
            var go = Instantiate(stunFx, owner);
            go.transform.localPosition = new Vector3(0f, 1.95f, 0f);
            go.transform.localScale = Vector3.one * 0.5f;
            return go;
        }

        public void AttachMissileVisual(Transform parent, ElementId el)
        {
            var prefab = Set(el)?.missile;
            if (prefab == null)
            {
                // fallback: glowing sphere + light
                var s = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                Object.Destroy(s.GetComponent<Collider>());
                s.transform.SetParent(parent, false);
                s.transform.localScale = Vector3.one * 0.25f;
                var l = parent.gameObject.AddComponent<Light>();
                l.color = ElementColors.Get(el);
                l.range = 5f;
                l.intensity = 2.2f;
                return;
            }

            var vis = Instantiate(prefab, parent);
            vis.transform.localPosition = Vector3.zero;
            vis.transform.localRotation = Quaternion.identity;

            // strip the pack's own movers so our Projectile owns motion
            foreach (var mb in vis.GetComponentsInChildren<MonoBehaviour>(true))
            {
                if (mb == null) continue;
                string n = mb.GetType().Name;
                if (n.Contains("Projectile") || n.Contains("Beam") || n.Contains("FireProjectile"))
                    mb.enabled = false;
            }
        }

        void SpawnTemp(GameObject prefab, Vector3 pos, Quaternion rot, float scale)
        {
            if (prefab == null) return;
            var go = Instantiate(prefab, pos, rot);
            if (!Mathf.Approximately(scale, 1f)) go.transform.localScale *= scale;
            Destroy(go, 4f);
        }

        // ------------------------------------------------------------------ numbers

        int dmgPopCount;
        public void ShowDamage(Vector3 pos, float amount, bool blocked)
        {
            var prefab = blocked && blockedNumberPrefab != null ? blockedNumberPrefab : damageNumberPrefab;
            if (prefab == null) return;
            // Fan simultaneous popups apart on a golden-angle spiral plus a vertical
            // stagger so a flurry of hits never fuses into an unreadable stack of
            // digits (the "-2320" / "1601" soup reviewers flagged).
            int n = dmgPopCount++;
            float ang = n * 2.39996323f;
            Vector3 offset = new Vector3(Mathf.Cos(ang), 0f, Mathf.Sin(ang)) * 0.72f
                             + Vector3.up * (0.5f + (n % 5) * 0.34f);
            prefab.Spawn(pos + offset, Mathf.Max(1f, Mathf.Round(amount)));
        }

        // ------------------------------------------------------------------ audio

        public void PlaySwing(Vector3 pos, bool heavy)
        {
            PlayAt(heavy ? swingHeavy : swingLight, pos, 0.4f, Random.Range(1.05f, 1.25f));
        }

        public void PlayCast(ElementId el, Vector3 pos)
        {
            PlayAt(Set(el)?.castSound, pos, 0.65f, Random.Range(0.95f, 1.08f));
        }

        public void PlayRoll(Vector3 pos) => PlayAt(rollWhoosh, pos, 0.3f, Random.Range(0.95f, 1.15f));
        public void PlayDeath(Vector3 pos) => PlayAt(deathCry, pos, 0.75f, Random.Range(0.9f, 1.05f));

        public void PlayUi(AudioClip clip, float vol = 0.8f)
        {
            if (clip == null) return;
            var go = new GameObject("UiSfx");
            var src = go.AddComponent<AudioSource>();
            src.spatialBlend = 0f;
            src.volume = vol;
            src.clip = clip;
            src.Play();
            Destroy(go, clip.length + 0.2f);
        }

        public void PlayAt(AudioClip clip, Vector3 pos, float vol, float pitch)
        {
            if (clip == null) return;
            var go = new GameObject("Sfx");
            go.transform.position = pos;
            var src = go.AddComponent<AudioSource>();
            src.spatialBlend = 0.82f;
            src.maxDistance = 32f;
            src.rolloffMode = AudioRolloffMode.Linear;
            src.volume = vol;
            src.pitch = pitch;
            src.clip = clip;
            src.Play();
            Destroy(go, clip.length / Mathf.Max(0.5f, pitch) + 0.2f);
        }

        // ------------------------------------------------------------------ hitstop

        public void SetBaseTimeScale(float scale)
        {
            baseTimeScale = scale;
            if (hitstopRoutine == null) Time.timeScale = scale;
        }

        public void Hitstop(float seconds)
        {
            if (hitstopRoutine != null) StopCoroutine(hitstopRoutine);
            hitstopRoutine = StartCoroutine(HitstopRoutine(seconds));
        }

        IEnumerator HitstopRoutine(float seconds)
        {
            Time.timeScale = 0.05f;
            yield return new WaitForSecondsRealtime(seconds);
            Time.timeScale = baseTimeScale;
            hitstopRoutine = null;
        }
    }
}
