using System;
using System.Collections.Generic;
using Invector.vMelee;
using UnityEngine;

namespace BrawlArena
{
    /// <summary>
    /// Terminal presentation firewall for Animator-authored Invector melee
    /// windows. StateMachineBehaviours may report their timing here, but they
    /// cannot activate an Invector hit source or apply damage.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class BrawlInvectorMeleePresentationManager : vMeleeManager
    {
        int suppressedAttackWindowCount;
        int suppressedAttackWindowEnableCount;
        int suppressedAttackWindowDisableCount;
        int suppressedListAttackWindowCount;
        int suppressedSingleAttackWindowCount;
        int blockedDamageHitCount;

        public int SuppressedAttackWindowCount => suppressedAttackWindowCount;
        public int SuppressedAttackWindowEnableCount => suppressedAttackWindowEnableCount;
        public int SuppressedAttackWindowDisableCount => suppressedAttackWindowDisableCount;
        public int SuppressedListAttackWindowCount => suppressedListAttackWindowCount;
        public int SuppressedSingleAttackWindowCount => suppressedSingleAttackWindowCount;
        public int BlockedDamageHitCount => blockedDamageHitCount;

        public void ResetPresentationTrace()
        {
            suppressedAttackWindowCount = 0;
            suppressedAttackWindowEnableCount = 0;
            suppressedAttackWindowDisableCount = 0;
            suppressedListAttackWindowCount = 0;
            suppressedSingleAttackWindowCount = 0;
            blockedDamageHitCount = 0;
        }

        public override void SetActiveAttack(
            List<string> bodyParts,
            vAttackType type,
            bool active = true,
            int damageMultiplier = 0,
            int recoilID = 0,
            int reactionID = 0,
            bool ignoreDefense = false,
            bool activeRagdoll = false,
            float senselessTime = 0,
            string attackName = "")
        {
            suppressedListAttackWindowCount++;
            RecordSuppressedAttackWindow(active);
        }

        public override void SetActiveAttack(
            string bodyPart,
            vAttackType type,
            bool active = true,
            int damageMultiplier = 0,
            int recoilID = 0,
            int reactionID = 0,
            bool ignoreDefense = false,
            bool activeRagdoll = false,
            float senselessTime = 0,
            string attackName = "")
        {
            suppressedSingleAttackWindowCount++;
            RecordSuppressedAttackWindow(active);
        }

        public override void OnDamageHit(ref vHitInfo hitInfo)
        {
            blockedDamageHitCount++;
            throw new NotSupportedException(
                "Invector melee damage is disabled; Brawl combat remains the sole damage authority.");
        }

        void RecordSuppressedAttackWindow(bool active)
        {
            suppressedAttackWindowCount++;
            if (active)
                suppressedAttackWindowEnableCount++;
            else
                suppressedAttackWindowDisableCount++;
        }
    }
}
