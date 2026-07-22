using System.Collections.Generic;
using Photon.Pun;
using UnityEngine;

namespace Crownfall
{
    /// Online half of MatchManager. Clients spawn + own their fighter, the
    /// master client owns the clock/score/respawns and drives AI understudies
    /// for unclaimed slots. Everything arrives through NetMatchLink RPCs.
    public partial class MatchManager
    {
        public bool OnlineMode { get; private set; }

        int myTeam = -1, mySlot = -1;

        /// Runs on every client when the master's start order lands.
        public void StartOnlineMatch(int[] flat)
        {
            if (State != MatchState.Menu && State != MatchState.ClassSelect) return;
            OnlineMode = true;
            IsDemo = false;
            Autopilot = false;

            // clear the podium showcase, restore the AI cast
            foreach (var rig in aiRigs)
                if (rig != null) rig.SetActive(true);
            if (podiumLight != null) podiumLight.gameObject.SetActive(false);
            if (OrbitCamera.I != null) OrbitCamera.I.menuFocus = null;

            // humans arrive as network prefabs — every local variant sits out
            for (int i = 0; i < playerVariants.Length; i++)
                if (playerVariants[i] != null) playerVariants[i].SetActive(false);

            int myActor = PhotonNetwork.LocalPlayer.ActorNumber;
            int myClass = CrownfallMeta.SelectedClass;
            for (int i = 0; i + 3 < flat.Length; i += 4)
            {
                int actor = flat[i], team = flat[i + 1], slot = flat[i + 2], cls = flat[i + 3];
                if (actor == myActor)
                {
                    myTeam = team;
                    mySlot = slot;
                    myClass = cls;
                }
                // bench the AI understudy whose seat a human took
                DeactivateAiSlot((Team)team, slot);
            }

            NetRefreshAiOwnership();

            var spawns = (Team)myTeam == Team.Azure ? azureSpawns : crimsonSpawns;
            var point = spawns != null && spawns.Length > 0 ? spawns[mySlot % spawns.Length] : transform;
            PhotonNetwork.Instantiate("Net/Fighter_" + (ClassId)myClass,
                point.position, point.rotation, 0,
                new object[] { myTeam, mySlot, myClass, PhotonNetwork.NickName });

            StartCoroutine(CountdownRoutine());
        }

        /// Called by FighterNetSync.Start on every client for every spawned human fighter.
        public void RegisterNetworkedFighter(CombatMotor motor, object[] data)
        {
            if (motor == null || data == null || data.Length < 4) return;
            var id = motor.GetComponent<CombatantIdentity>();
            id.team = (Team)(int)data[0];
            id.displayName = (string)data[3];
            id.isPlayer = motor.Net != null && motor.Net.IsMine;
            motor.RebindTeamVisuals();

            all.Add(motor);
            spawnSlot[motor] = (int)data[1];
            motor.Health.Died += killer => OnCombatantDied(motor, killer);

            var pc = motor.GetComponent<PlayerController>();
            if (motor.Net != null && motor.Net.IsMine)
            {
                if (pc != null) pc.enabled = true;
                PlayerMotor = motor;
                OrbitCamera.I?.SetTarget(motor, true);
            }
            else if (pc != null) pc.enabled = false;
        }

        // ---------------------------------------------------------------- slots

        CombatMotor FindAiRig(Team team, int slot)
        {
            foreach (var m in all)
            {
                if (m == null || m.Identity == null || m.Identity.team != team) continue;
                if (m.GetComponent<AIController>() == null) continue;
                if (spawnSlot.TryGetValue(m, out var s) && s == slot) return m;
            }
            return null;
        }

        void DeactivateAiSlot(Team team, int slot)
        {
            // Azure slot 0 belongs to the local playerVariants, already benched
            if (team == Team.Azure && slot == 0) return;
            var rig = FindAiRig(team, slot);
            if (rig != null) rig.gameObject.SetActive(false);
        }

        /// A human left mid-match: their slot's AI understudy takes over.
        public void NetActivateAiSlot(Team team, int slot)
        {
            var rig = FindAiRig(team, slot);
            if (rig == null && team == Team.Azure && slot == 0)
                rig = playerVariants[0] != null ? playerVariants[0].GetComponent<CombatMotor>() : null;
            if (rig == null) return;
            rig.gameObject.SetActive(true);
            var spawns = team == Team.Azure ? azureSpawns : crimsonSpawns;
            var point = spawns != null && spawns.Length > 0 ? spawns[slot % spawns.Length] : rig.transform;
            rig.ResetForRespawn(point.position, point.rotation);
            NetRefreshAiOwnership();
        }

        /// AI runs only on the master client while online.
        public void NetRefreshAiOwnership()
        {
            if (!OnlineMode) return;
            foreach (var m in all)
            {
                if (m == null) continue;
                var ai = m.GetComponent<AIController>();
                if (ai != null) ai.enabled = PhotonNetwork.IsMasterClient && m.gameObject.activeInHierarchy;
            }
        }

        // ---------------------------------------------------------------- mirrors

        public void NetApplyKill(int scoreAzure, int scoreCrimson,
            CombatantIdentity killer, CombatantIdentity victim)
        {
            if (!OnlineMode) return; // stray RPC from a match this client never entered
            ScoreAzure = scoreAzure;
            ScoreCrimson = scoreCrimson;
            ScoreChanged?.Invoke(ScoreAzure, ScoreCrimson);
            if (victim != null) KillFeed?.Invoke(killer, victim);
            GameEffects.I?.PlayUi(GameEffects.I.killDing, 0.45f);
        }

        public void NetApplyRespawn(FighterNetSync sync)
        {
            if (!OnlineMode) return;
            if (sync == null || !sync.IsMine) return; // owner repositions; puppets follow the stream
            var victim = sync.Motor;
            var id = victim.Identity;
            var spawns = id.team == Team.Azure ? azureSpawns : crimsonSpawns;
            int slot = spawnSlot.TryGetValue(victim, out var s) ? s : 0;
            var point = spawns != null && spawns.Length > 0 ? spawns[slot % spawns.Length] : victim.transform;
            victim.ResetForRespawn(point.position, point.rotation);
            GameEffects.I?.RespawnFlash(point.position);
        }

        public void NetApplyEnd(Team winner)
        {
            if (!OnlineMode) return;
            EndMatch(winner);
        }

        public void NetApplyClock(float timeLeft, bool suddenDeath)
        {
            if (!OnlineMode || PhotonNetwork.IsMasterClient) return; // master IS the clock
            TimeLeft = timeLeft;
            if (suddenDeath && !SuddenDeath)
            {
                SuddenDeath = true;
                Announce?.Invoke("SUDDEN DEATH");
            }
            else SuddenDeath = suddenDeath;
        }

        public void NetEnterSuddenDeath()
        {
            SuddenDeath = true;
            Announce?.Invoke("SUDDEN DEATH");
            GameEffects.I?.PlayUi(GameEffects.I.uiFight, 0.9f);
        }
    }
}
