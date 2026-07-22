using System.Collections;
using System.Collections.Generic;
using Photon.Pun;
using UnityEngine;

namespace Crownfall
{
    /// Match-level network authority, attached next to MatchManager with a scene
    /// PhotonView. The master client owns the clock, the score and respawn
    /// scheduling; everyone else mirrors. Fighter-level state lives on
    /// FighterNetSync — this only carries match flow.
    [RequireComponent(typeof(MatchManager))]
    public class NetMatchLink : MonoBehaviourPun, IPunObservable
    {
        public static NetMatchLink I { get; private set; }

        MatchManager mm;
        // flat assignment quads: actorNumber, team, slot, classIdx
        int[] assignments = new int[0];

        void Awake()
        {
            I = this;
            mm = GetComponent<MatchManager>();
        }

        // ------------------------------------------------------------------ start

        /// Master only: turn the ready roster into slot assignments and start
        /// everyone. Join order alternates teams so a 2-player room is a 1v1
        /// across teams (each side backfilled by AI).
        public void MasterStartMatch(List<CrownfallNet.RosterEntry> roster)
        {
            var flat = new List<int>();
            for (int i = 0; i < roster.Count && i < 6; i++)
            {
                flat.Add(roster[i].actorNumber);
                flat.Add(i % 2);      // team: even joiners Azure, odd Crimson
                flat.Add(i / 2);      // slot within team 0..2
                flat.Add(Mathf.Clamp(roster[i].classIdx, 0, 3));
            }
            photonView.RPC(nameof(RPC_StartMatch), RpcTarget.All, flat.ToArray());
        }

        [PunRPC]
        void RPC_StartMatch(int[] flat)
        {
            assignments = flat;
            mm.StartOnlineMatch(flat);
            CrownfallNet.I?.MarkInMatch();
        }

        // ------------------------------------------------------------------ kills / score

        /// Called on the client that owns the victim when it dies.
        public void ReportKill(int victimViewId, int killerViewId)
        {
            photonView.RPC(nameof(RPC_ReportKill), RpcTarget.MasterClient, victimViewId, killerViewId);
        }

        [PunRPC]
        void RPC_ReportKill(int victimViewId, int killerViewId)
        {
            if (!PhotonNetwork.IsMasterClient) return;
            var victim = FighterNetSync.Find(victimViewId);
            if (victim == null || victim.Motor.Identity == null) return;

            int scoreAzure = mm.ScoreAzure;
            int scoreCrimson = mm.ScoreCrimson;
            if (victim.Motor.Identity.team == Team.Azure) scoreCrimson++;
            else scoreAzure++;

            photonView.RPC(nameof(RPC_KillBroadcast), RpcTarget.All,
                scoreAzure, scoreCrimson, killerViewId, victimViewId);

            if (mm.SuddenDeath || scoreAzure >= mm.killTarget || scoreCrimson >= mm.killTarget)
            {
                photonView.RPC(nameof(RPC_EndMatch), RpcTarget.All,
                    (int)(victim.Motor.Identity.team == Team.Azure ? Team.Crimson : Team.Azure));
                return;
            }
            StartCoroutine(MasterRespawnAfterDelay(victimViewId));
        }

        [PunRPC]
        void RPC_KillBroadcast(int scoreAzure, int scoreCrimson, int killerViewId, int victimViewId)
        {
            var killer = FighterNetSync.Find(killerViewId);
            var victim = FighterNetSync.Find(victimViewId);
            mm.NetApplyKill(scoreAzure, scoreCrimson,
                killer != null ? killer.Motor.Identity : null,
                victim != null ? victim.Motor.Identity : null);
        }

        IEnumerator MasterRespawnAfterDelay(int victimViewId)
        {
            yield return new WaitForSeconds(Tuning.RespawnSeconds);
            if (mm.State != MatchState.Fighting) yield break;
            photonView.RPC(nameof(RPC_Respawn), RpcTarget.All, victimViewId);
        }

        [PunRPC]
        void RPC_Respawn(int victimViewId)
        {
            var sync = FighterNetSync.Find(victimViewId);
            if (sync == null) return;
            // the owner repositions for real; everyone shows the flash and the
            // puppet snaps via the stream's teleport threshold
            mm.NetApplyRespawn(sync);
        }

        [PunRPC]
        void RPC_EndMatch(int winnerTeam)
        {
            mm.NetApplyEnd((Team)winnerTeam);
        }

        /// Master only: the clock ran out.
        public void MasterTimeUp()
        {
            if (mm.SuddenDeath) return;
            if (mm.ScoreAzure != mm.ScoreCrimson)
                photonView.RPC(nameof(RPC_EndMatch), RpcTarget.All,
                    (int)(mm.ScoreAzure > mm.ScoreCrimson ? Team.Azure : Team.Crimson));
            else
                photonView.RPC(nameof(RPC_SuddenDeath), RpcTarget.All);
        }

        [PunRPC]
        void RPC_SuddenDeath()
        {
            mm.NetEnterSuddenDeath();
        }

        // ------------------------------------------------------------------ leavers

        /// Master: a human left mid-match — bring their slot's AI understudy back.
        public void MasterHandleLeaver(int actorNumber)
        {
            for (int i = 0; i + 3 < assignments.Length; i += 4)
            {
                if (assignments[i] != actorNumber) continue;
                photonView.RPC(nameof(RPC_ReviveAiSlot), RpcTarget.All,
                    assignments[i + 1], assignments[i + 2]);
                return;
            }
        }

        [PunRPC]
        void RPC_ReviveAiSlot(int team, int slot)
        {
            mm.NetActivateAiSlot((Team)team, slot);
        }

        // ------------------------------------------------------------------ clock stream

        public void OnPhotonSerializeView(PhotonStream stream, PhotonMessageInfo info)
        {
            if (stream.IsWriting)
            {
                stream.SendNext(mm.TimeLeft);
                stream.SendNext(mm.SuddenDeath);
            }
            else
            {
                mm.NetApplyClock((float)stream.ReceiveNext(), (bool)stream.ReceiveNext());
            }
        }
    }
}
