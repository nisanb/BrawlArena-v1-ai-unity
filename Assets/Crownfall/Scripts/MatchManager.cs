using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Crownfall
{
    public enum MatchState { Menu, ClassSelect, Countdown, Fighting, Ended }

    /// Owns the 3v3 flow: class select, countdown, kills/score, respawns,
    /// sudden death and the end ceremony.
    public class MatchManager : MonoBehaviour
    {
        [Header("Wired by forge")]
        public GameObject[] playerVariants = new GameObject[4];
        public Transform[] azureSpawns;
        public Transform[] crimsonSpawns;

        public int killTarget = 10;
        public float matchDuration = 300f;

        public static MatchManager I { get; private set; }

        public MatchState State { get; private set; } = MatchState.Menu;
        public bool Paused { get; private set; }
        public float TimeLeft { get; private set; }
        public bool SuddenDeath { get; private set; }
        public CombatMotor PlayerMotor { get; private set; }
        public int ScoreAzure { get; private set; }
        public int ScoreCrimson { get; private set; }

        public event Action<MatchState> StateChanged;
        public event Action<bool> PausedChanged;
        public event Action<int, int> ScoreChanged;
        public event Action<CombatantIdentity, CombatantIdentity> KillFeed;
        public event Action<int> CountdownTick;
        public event Action<Team> MatchEndedEvent;
        public event Action<string> Announce;

        bool autopilot;
        public bool Autopilot
        {
            get => autopilot;
            set { autopilot = value; ApplyAutopilot(); }
        }

        readonly List<CombatMotor> all = new List<CombatMotor>();
        readonly Dictionary<CombatMotor, int> spawnSlot = new Dictionary<CombatMotor, int>();
        readonly List<CombatMotor> aliveScratch = new List<CombatMotor>();

        void Awake()
        {
            I = this;
            Time.timeScale = 1f;
            TimeLeft = matchDuration;
        }

        void Start()
        {
            int azureIdx = 0, crimsonIdx = 0;
            foreach (var h in FindObjectsByType<Health>(FindObjectsInactive.Include, FindObjectsSortMode.InstanceID))
            {
                var motor = h.GetComponent<CombatMotor>();
                if (motor == null) continue;
                all.Add(motor);
                var id = motor.GetComponent<CombatantIdentity>();
                if (id != null)
                {
                    if (id.isPlayer) spawnSlot[motor] = 0;
                    else if (id.team == Team.Azure) spawnSlot[motor] = ++azureIdx;
                    else spawnSlot[motor] = crimsonIdx++;
                }
                h.Died += killer => OnCombatantDied(motor, killer);
            }

            CrownfallSettings.Load();
            SetCursorFree(true);
            SetState(MatchState.Menu);
        }

        public void OpenClassSelect()
        {
            if (State == MatchState.Menu) SetState(MatchState.ClassSelect);
        }

        public void BackToMenu()
        {
            if (State == MatchState.ClassSelect) SetState(MatchState.Menu);
        }

        /// Autopilot exhibition match with a random champion.
        public void StartDemo()
        {
            if (State != MatchState.Menu && State != MatchState.ClassSelect) return;
            Autopilot = true;
            SelectClass(UnityEngine.Random.Range(0, playerVariants.Length));
        }

        public void TogglePause()
        {
            if (State != MatchState.Fighting) return;
            Paused = !Paused;
            GameEffects.I?.SetBaseTimeScale(Paused ? 0f : 1f);
            SetCursorFree(Paused);
            PausedChanged?.Invoke(Paused);
        }

        void Update()
        {
            if (State != MatchState.Fighting) return;

            if (!SuddenDeath)
            {
                TimeLeft -= Time.deltaTime;
                if (TimeLeft <= 0f)
                {
                    TimeLeft = 0f;
                    if (ScoreAzure != ScoreCrimson)
                    {
                        EndMatch(ScoreAzure > ScoreCrimson ? Team.Azure : Team.Crimson);
                    }
                    else
                    {
                        SuddenDeath = true;
                        Announce?.Invoke("SUDDEN DEATH");
                        GameEffects.I?.PlayUi(GameEffects.I.uiFight, 0.9f);
                    }
                }
            }

            var kb = UnityEngine.InputSystem.Keyboard.current;
            if (kb != null && kb.f1Key.wasPressedThisFrame && !Paused)
                Autopilot = !Autopilot;
            if (kb != null && kb.escapeKey.wasPressedThisFrame)
                TogglePause();
        }

        // ------------------------------------------------------------------ flow

        public void SelectClass(int classIndex)
        {
            if (State != MatchState.ClassSelect && State != MatchState.Menu) return;
            classIndex = Mathf.Clamp(classIndex, 0, playerVariants.Length - 1);

            for (int i = 0; i < playerVariants.Length; i++)
                if (playerVariants[i] != null) playerVariants[i].SetActive(i == classIndex);

            var chosen = playerVariants[classIndex];
            PlayerMotor = chosen != null ? chosen.GetComponent<CombatMotor>() : null;
            OrbitCamera.I?.SetTarget(PlayerMotor, true);
            ApplyAutopilot();

            StartCoroutine(CountdownRoutine());
        }

        IEnumerator CountdownRoutine()
        {
            SetState(MatchState.Countdown);
            SetCursorFree(false);
            for (int n = 3; n >= 1; n--)
            {
                CountdownTick?.Invoke(n);
                GameEffects.I?.PlayUi(GameEffects.I.uiTick, 0.6f);
                yield return new WaitForSeconds(1f);
            }
            CountdownTick?.Invoke(0);
            GameEffects.I?.PlayUi(GameEffects.I.uiFight, 0.9f);
            SetState(MatchState.Fighting);
        }

        void OnCombatantDied(CombatMotor victim, CombatMotor killer)
        {
            if (State != MatchState.Fighting) return;

            var victimId = victim.Identity;
            var killerId = killer != null ? killer.Identity : null;

            if (victimId.team == Team.Azure) ScoreCrimson++;
            else ScoreAzure++;
            ScoreChanged?.Invoke(ScoreAzure, ScoreCrimson);
            KillFeed?.Invoke(killerId, victimId);
            GameEffects.I?.PlayUi(GameEffects.I.killDing, 0.45f);

            if (SuddenDeath || ScoreAzure >= killTarget || ScoreCrimson >= killTarget)
            {
                EndMatch(victimId.team == Team.Azure ? Team.Crimson : Team.Azure);
                return;
            }

            StartCoroutine(RespawnRoutine(victim));
        }

        IEnumerator RespawnRoutine(CombatMotor victim)
        {
            yield return new WaitForSeconds(Tuning.RespawnSeconds);
            if (State != MatchState.Fighting) yield break;

            var id = victim.Identity;
            var spawns = id.team == Team.Azure ? azureSpawns : crimsonSpawns;
            int slot = spawnSlot.TryGetValue(victim, out var s) ? s : 0;
            var point = spawns != null && spawns.Length > 0 ? spawns[slot % spawns.Length] : victim.transform;

            victim.ResetForRespawn(point.position, point.rotation);
            GameEffects.I?.RespawnFlash(point.position);
        }

        void EndMatch(Team winner)
        {
            if (State == MatchState.Ended) return;
            SetState(MatchState.Ended);
            MatchEndedEvent?.Invoke(winner);

            bool playerWon = winner == Team.Azure;
            Announce?.Invoke(playerWon ? "VICTORY" : "DEFEAT");
            GameEffects.I?.PlayUi(playerWon ? GameEffects.I.uiVictory : GameEffects.I.uiDefeat, 0.9f);

            foreach (var m in all)
                if (m != null && m.gameObject.activeInHierarchy && !m.IsDead && m.Identity.team == winner)
                    m.PlayVictory();

            StartCoroutine(EndCeremony());
        }

        IEnumerator EndCeremony()
        {
            GameEffects.I?.SetBaseTimeScale(0.35f);
            yield return new WaitForSecondsRealtime(2.6f);
            GameEffects.I?.SetBaseTimeScale(1f);
            SetCursorFree(true);
        }

        public void Restart()
        {
            Time.timeScale = 1f;
            SceneManager.LoadScene(SceneManager.GetActiveScene().buildIndex);
        }

        void LateUpdate()
        {
            if (State == MatchState.Ended &&
                UnityEngine.InputSystem.Keyboard.current != null &&
                UnityEngine.InputSystem.Keyboard.current.rKey.wasPressedThisFrame)
                Restart();
        }

        // ------------------------------------------------------------------ queries

        public List<CombatMotor> AliveEnemiesOf(Team team)
        {
            aliveScratch.Clear();
            foreach (var m in all)
            {
                if (m == null || !m.gameObject.activeInHierarchy || m.IsDead) continue;
                if (m.Identity == null || m.Identity.team == team) continue;
                aliveScratch.Add(m);
            }
            return aliveScratch;
        }

        public int CountTargeting(CombatMotor candidate, AIController asker)
        {
            int n = 0;
            foreach (var m in all)
            {
                if (m == null || !m.gameObject.activeInHierarchy || m.IsDead) continue;
                var ai = m.GetComponent<AIController>();
                if (ai == null || ai == asker || !ai.enabled) continue;
                if (ai.CurrentTarget == candidate) n++;
            }
            return n;
        }

        // ------------------------------------------------------------------ helpers

        void ApplyAutopilot()
        {
            if (PlayerMotor == null) return;
            var ai = PlayerMotor.GetComponent<AIController>();
            if (ai != null) ai.enabled = autopilot;
        }

        void SetState(MatchState s)
        {
            State = s;
            StateChanged?.Invoke(s);
        }

        void SetCursorFree(bool free)
        {
            Cursor.lockState = free ? CursorLockMode.None : CursorLockMode.Locked;
            Cursor.visible = free;
        }

        /// Headless testing entry: pick a class and optionally enable autopilot.
        public void AutoStart(int classIndex, bool enableAutopilot)
        {
            Autopilot = enableAutopilot;
            SelectClass(classIndex);
        }
    }
}
