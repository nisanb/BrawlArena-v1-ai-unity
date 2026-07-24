using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace Crownfall.EditorTools
{
    /// The single source of truth for what every fighter is made of.
    ///
    /// WHY THIS EXISTS. Before this, each class was instantiated from a different
    /// vendor pack (Mini Legion / RPG Tiny Hero / WizardPBR) at three different
    /// unit scales, and weapons were attached four different ad-hoc ways:
    /// `ReparentWeaponsToHands` dropped meshes onto hand bones at identity,
    /// `ApplyHammerLoadout` silently no-oped, and the Mage's staff hung off the
    /// PELVIS. Three of six classes ended up with no weapon transform at all,
    /// which also cost them their weapon tip, trail and elemental enchant aura.
    ///
    /// THE FIX. Every fighter is now the one ModularRPGHeroes rig. That rig ships
    /// a complete modular wardrobe (9 cloths, 7 helms, 7 hairs, ...) and — this is
    /// the important part — a complete weapon library already parented under
    /// `root/weaponShield_l` and `root/weaponShield_r`, where the PACK AUTHOR has
    /// hand-placed every single weapon's local offset so it sits correctly in the
    /// grip. A loadout is therefore nothing but a set of `SetActive` toggles.
    ///
    /// NEVER reparent or re-offset a weapon node. The holder nodes are animated
    /// per-clip by the pack (that is how the weapon tracks the hand through a
    /// swing), so moving a weapon out of its holder is what made weapons drift
    /// off hands in the first place. Keeping every class on the clip family that
    /// matches its holder animation is what removes the drift for good.
    public static class CrownfallLoadout
    {
        /// One MRH animation set. `Family` is both the clip-folder name and the
        /// identity of the generated animator controller.
        public enum Family { SwordShield, SingleTwoHandSword, DoubleSwords, MagicWand }

        public struct Spec
        {
            /// Which clip family this class fights with. Drives the animator
            /// controller AND the holder-node animation, so it must match the
            /// weapons below (a wand loadout playing greatsword clips is exactly
            /// the mismatch that used to look broken).
            public Family family;
            /// Modular body renderers to enable. Everything else is disabled.
            public string[] parts;
            /// Weapon nodes under weaponShield_l/r to enable, left hand first.
            /// Names are the pack's own (Sword7_L, Shield8, Hammer2_L, Wand2_R...).
            public string[] weapons;
            /// The node the weapon tip / trail / enchant aura attach to — must be
            /// one of `weapons`. Usually the striking weapon, not the shield.
            public string focus;
        }

        /// The base rig. Any BasicCharacters prefab carries the full wardrobe and
        /// the full weapon library; we take one and toggle from there.
        public const string BasePrefab =
            "Assets/ModularRPGHeroesPBR/Prefabs/BasicCharacters/BasicMale01.prefab";

        /// Hero loadouts (player rigs + net prefabs).
        ///
        /// Knight is the only class on SwordShield because it is the only family
        /// that ships Defend/DefendHit clips — and the Knight is the only blocker
        /// in the kit. Warhammer finally holds an actual hammer (Hammer2_L); it
        /// was a bare-fisted Rock Golem before.
        public static readonly Dictionary<ClassId, Spec> Hero = new Dictionary<ClassId, Spec>
        {
            [ClassId.Knight] = new Spec
            {
                family = Family.SwordShield,
                parts = new[] { "Belt1", "Cloth5", "Face5", "Glove5", "Hair6", "Shoe6", "ShoulderPad4" },
                // Sword3, NOT Sword7. Sword7 measures 1.88m on a ~1.75m rig — a
                // greatsword, which swamped the shield and read as a joke in the
                // motion probe. It is also what the Greatsword class carries, so
                // the two classes were wielding the identical blade. The low-index
                // swords are the one-handers (holder offsets ~0.048 vs Sword7's).
                weapons = new[] { "Shield8", "Sword3_R" },
                focus = "Sword3_R",
            },
            [ClassId.Greatsword] = new Spec
            {
                family = Family.SingleTwoHandSword,
                parts = new[] { "Cloth9", "Face5", "Glove6", "Hair6Half", "Helm3", "BackPack2", "Shoe6", "ShoulderPad1" },
                weapons = new[] { "Sword7_L" },
                focus = "Sword7_L",
            },
            [ClassId.Warhammer] = new Spec
            {
                family = Family.SingleTwoHandSword,
                parts = new[] { "Cloth6", "Crown2", "Face2", "Glove5", "Hair2", "Shoe2", "ShoulderPad6" },
                weapons = new[] { "Hammer2_L" },
                focus = "Hammer2_L",
            },
            [ClassId.Duelist] = new Spec
            {
                family = Family.DoubleSwords,
                parts = new[] { "Cloth8", "Glove7", "Helm7", "Shoe5", "ShoulderPad4" },
                weapons = new[] { "Sword1_L", "Sword2_R" },
                focus = "Sword2_R",
            },
            [ClassId.Mage] = new Spec
            {
                family = Family.MagicWand,
                parts = new[] { "Belt2", "Cloth2", "Face5", "Glove6", "Hair3Half", "Hat1", "Shoe2" },
                weapons = new[] { "Wand2_R" },
                focus = "Wand2_R",
            },
            [ClassId.Healer] = new Spec
            {
                family = Family.MagicWand,
                parts = new[] { "Belt1", "Cloth7", "Face3", "Glove5", "Hair4", "Backpack3", "Shoe3" },
                weapons = new[] { "Wand1_R" },
                focus = "Wand1_R",
            },
        };

        /// AI-understudy loadouts: same family and same weapon class, different
        /// wardrobe, so twins on the field still read apart at a glance.
        public static readonly Dictionary<ClassId, Spec> Understudy = new Dictionary<ClassId, Spec>
        {
            [ClassId.Knight] = new Spec
            {
                family = Family.SwordShield,
                parts = new[] { "Cloth7", "Face2", "Glove7", "Hair1Half", "Helm2", "Shoe2", "ShoulderPad3" },
                weapons = new[] { "Shield4", "Sword2_R" },   // one-hander, see hero note
                focus = "Sword2_R",
            },
            [ClassId.Greatsword] = new Spec
            {
                family = Family.SingleTwoHandSword,
                parts = new[] { "Cloth8", "Face3", "Glove7", "Hair1Half", "Helm1", "BackPack1", "Shoe3", "ShoulderPad3" },
                weapons = new[] { "Sword6_L" },
                focus = "Sword6_L",
            },
            [ClassId.Warhammer] = new Spec
            {
                family = Family.SingleTwoHandSword,
                parts = new[] { "Cloth5", "Face3", "Glove3", "Helm5", "Shoe5" },
                weapons = new[] { "Hammer2_L" },
                focus = "Hammer2_L",
            },
            [ClassId.Duelist] = new Spec
            {
                family = Family.DoubleSwords,
                parts = new[] { "Cloth9", "Glove3", "Helm6", "Shoe4", "ShoulderPad6" },
                weapons = new[] { "Sword4_L", "Sword3_R" },
                focus = "Sword3_R",
            },
            [ClassId.Mage] = new Spec
            {
                family = Family.MagicWand,
                parts = new[] { "Cloth4", "Face1", "Glove7", "Hair1Half", "Hat2", "Shoe1" },
                weapons = new[] { "Wand3_R" },
                focus = "Wand3_R",
            },
            [ClassId.Healer] = new Spec
            {
                family = Family.MagicWand,
                parts = new[] { "Belt3", "Cloth9", "Crown2", "Face5", "Glove4", "Hair6", "Shoe1" },
                weapons = new[] { "Wand1_R" },
                focus = "Wand1_R",
            },
        };

        public static Spec Get(ClassId cls, bool understudy) =>
            understudy && Understudy.ContainsKey(cls) ? Understudy[cls] : Hero[cls];

        /// The clip folder backing a family. `DoubleSwords` is the odd one out —
        /// the pack pluralises the folder but not the controller.
        public static string AnimFolder(Family f) => f switch
        {
            Family.SwordShield => "SwordShield",
            Family.SingleTwoHandSword => "SingleTwoHandSword",
            Family.DoubleSwords => "DoubleSwords",
            Family.MagicWand => "MagicWand",
            _ => "NoWeapon",
        };

        /// Per-family clip-name suffix. The pack names clips
        /// `NormalAttack01_SwordShield`, `Combo01_DoubleSword` (SINGULAR here,
        /// unlike the folder), `Roll_MagicWand`, `..._SingleTwohandSword`
        /// (lower-case 'h' in "Twohand" — the pack is inconsistent and matching
        /// is case-insensitive precisely because of this).
        public static string ClipSuffix(Family f) => f switch
        {
            Family.SwordShield => "SwordShield",
            Family.SingleTwoHandSword => "SingleTwohandSword",
            Family.DoubleSwords => "DoubleSword",
            Family.MagicWand => "MagicWand",
            _ => "noWeapon",
        };

        /// Ranged families cast instead of swinging; melee families strike.
        public static bool IsCaster(Family f) => f == Family.MagicWand;

        /// Only SwordShield ships Defend/DefendHit, so only it can block.
        public static bool CanBlock(Family f) => f == Family.SwordShield;

        // ---------------------------------------------------------------- apply

        /// Toggle a freshly-instantiated base rig into `spec`. Returns the focus
        /// weapon transform (tip/trail/enchant anchor), or null if it is missing.
        ///
        /// Everything not named in the spec is switched OFF, so this is
        /// idempotent and a class can never inherit a stray mesh from the base
        /// prefab's own composition (the old code left the Duelist holding a
        /// sword AND a shield AND a second sword for exactly that reason).
        public static Transform Apply(GameObject model, Spec spec)
        {
            var wanted = new HashSet<string>(spec.parts, System.StringComparer.OrdinalIgnoreCase);
            var wantedWeapons = new HashSet<string>(spec.weapons, System.StringComparer.OrdinalIgnoreCase);
            Transform focus = null;
            var seenParts = new HashSet<string>(System.StringComparer.OrdinalIgnoreCase);
            var seenWeapons = new HashSet<string>(System.StringComparer.OrdinalIgnoreCase);

            foreach (var rend in model.GetComponentsInChildren<Renderer>(true))
            {
                var t = rend.transform;
                bool inHolder = IsUnderHolder(t, model.transform);
                bool on;
                if (inHolder)
                {
                    on = wantedWeapons.Contains(t.name);
                    if (on)
                    {
                        seenWeapons.Add(t.name);
                        if (string.Equals(t.name, spec.focus, System.StringComparison.OrdinalIgnoreCase))
                            focus = t;
                    }
                }
                else
                {
                    on = wanted.Contains(t.name);
                    if (on) seenParts.Add(t.name);
                }
                t.gameObject.SetActive(on);
            }

            // A silently-missing part is how the Warhammer ended up unarmed, so
            // every unmatched name is reported loudly rather than shrugged off.
            foreach (var miss in spec.parts.Where(p => !seenParts.Contains(p)))
                Debug.LogWarning($"[CrownfallLoadout] body part '{miss}' not found on the base rig");
            foreach (var miss in spec.weapons.Where(w => !seenWeapons.Contains(w)))
                Debug.LogError($"[CrownfallLoadout] weapon node '{miss}' not found on the base rig — this class will fight unarmed");
            if (focus == null)
                Debug.LogError($"[CrownfallLoadout] focus weapon '{spec.focus}' missing — no weapon tip, trail or enchant aura will be built");

            return focus;
        }

        /// Weld the loadout's weapons rigidly into the hand bones.
        ///
        /// The pack does NOT parent weapons to hands. It parks them on the
        /// `weaponShield_l/r` holder nodes (children of `root`) and animates
        /// those holders with per-clip rotation curves so they shadow the hand.
        /// That works only as long as every clip carries the curves — and three
        /// do not: `NormalAttack01_SwordShield`, `NormalAttack02_SwordShield`
        /// and `Attack01_MagicWand`, i.e. the Knight's light AND heavy and the
        /// Mage's heavy. On those the holder stays frozen in root space while
        /// the arm swings, throwing the weapon up to 0.9m out of the hand
        /// (measured). Cross-fades make it worse: the holder curves blend on
        /// their own schedule from the arm, which is the long-standing
        /// "weapons drift off hands on fast transitions" complaint.
        ///
        /// The fix is not to re-author 139 clips. Sample ONE reference clip that
        /// does carry the curves, read each weapon's pose in HAND space there —
        /// that pose is the grip the pack author intended — then reparent the
        /// weapon under the hand bone with exactly that offset. The authored
        /// grip survives, the weapon is now rigid, and every clip behaves
        /// including the three broken ones.
        ///
        /// This is deliberately NOT the old `ReparentWeaponsToHands`, which
        /// dropped weapons onto hands at identity/whatever-pose and is what made
        /// swords stick out of wrists at right angles.
        public static void WeldWeaponsToHands(GameObject model, Spec spec)
        {
            var anim = model.GetComponentInChildren<Animator>();
            if (anim == null || !anim.isHuman)
            {
                Debug.LogError("[CrownfallLoadout] cannot weld weapons: rig has no humanoid Animator");
                return;
            }
            var handL = anim.GetBoneTransform(HumanBodyBones.LeftHand);
            var handR = anim.GetBoneTransform(HumanBodyBones.RightHand);
            if (handL == null || handR == null)
            {
                Debug.LogError("[CrownfallLoadout] cannot weld weapons: missing hand bones");
                return;
            }

            var reference = FindGripReferenceClip(spec.family);
            if (reference == null)
            {
                Debug.LogError($"[CrownfallLoadout] no clip in {AnimFolder(spec.family)} carries holder curves; " +
                               "weapons left on their holders");
                return;
            }
            // pose the rig at the authored grip before measuring
            reference.SampleAnimation(model, 0f);

            // measure first, reparent second — reparenting mid-scan would move
            // the very transforms still being measured
            var plan = new List<(Transform weapon, Transform hand, Vector3 pos, Quaternion rot)>();
            foreach (var rend in model.GetComponentsInChildren<Renderer>(true))
            {
                var t = rend.transform;
                if (!t.gameObject.activeSelf) continue;
                var holder = HolderOf(t, model.transform);
                if (holder == null) continue;
                bool left = holder.name.EndsWith("_l", System.StringComparison.OrdinalIgnoreCase);
                var hand = left ? handL : handR;
                plan.Add((t, hand,
                    hand.InverseTransformPoint(t.position),
                    Quaternion.Inverse(hand.rotation) * t.rotation));
            }

            foreach (var (weapon, hand, pos, rot) in plan)
            {
                weapon.SetParent(hand, false);
                weapon.localPosition = pos;
                weapon.localRotation = rot;
            }
        }

        /// The first clip in a family that actually animates the holders,
        /// preferring Idle because it is the pack's neutral authored grip.
        static AnimationClip FindGripReferenceClip(Family family)
        {
            string dir = "Assets/ModularRPGHeroesPBR/Animations/" + AnimFolder(family);
            if (!System.IO.Directory.Exists(dir)) return null;

            var files = System.IO.Directory.GetFiles(dir, "*.fbx")
                .Select(f => f.Replace('\\', '/'))
                .OrderByDescending(f => System.IO.Path.GetFileName(f)
                    .StartsWith("Idle", System.StringComparison.OrdinalIgnoreCase))
                .ToList();

            foreach (var f in files)
            {
                var clip = AssetDatabase.LoadAllAssetsAtPath(f).OfType<AnimationClip>()
                    .FirstOrDefault(c => !c.name.StartsWith("__preview"));
                if (clip == null) continue;
                if (AnimationUtility.GetCurveBindings(clip).Any(b => b.path.Contains("weaponShield")))
                    return clip;
            }
            return null;
        }

        /// The weaponShield_l/r node a transform hangs under, or null.
        static Transform HolderOf(Transform t, Transform root)
        {
            for (var p = t; p != null && p != root.parent; p = p.parent)
                if (p.name.StartsWith("weaponShield", System.StringComparison.OrdinalIgnoreCase))
                    return p;
            return null;
        }

        static bool IsUnderHolder(Transform t, Transform root)
        {
            for (var p = t; p != null && p != root.parent; p = p.parent)
                if (p.name.StartsWith("weaponShield", System.StringComparison.OrdinalIgnoreCase))
                    return true;
            return false;
        }
    }
}
