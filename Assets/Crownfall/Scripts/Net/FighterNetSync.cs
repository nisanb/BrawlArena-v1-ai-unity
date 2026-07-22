using Photon.Pun;
using UnityEngine;

namespace Crownfall
{
    public enum NetEvent : byte
    {
        AttackLight, AttackHeavy, Skill, Roll, Flinch, Stagger, Die, Respawn,
        Bolt, Nova, BlockImpact, Victory
    }

    /// Network shadow of one fighter. On the owner it streams pose/vitals and
    /// relays the motor's one-shot actions as RPCs; on everyone else it turns the
    /// fighter into an interpolated puppet (CombatMotor.IsPuppet short-circuits
    /// local simulation). Damage crossing clients is forwarded to the victim's
    /// owner, which is the single authority for its own health.
    [RequireComponent(typeof(CombatMotor))]
    public class FighterNetSync : MonoBehaviourPun, IPunObservable
    {
        public bool IsMine => photonView == null || photonView.IsMine;
        public CombatMotor Motor { get; private set; }

        Vector3 netPos;
        float netYaw;
        float netMoveX, netMoveZ, netLocoRate;
        byte netState;
        float netHp, netPoise, netStamina;
        bool netBlocking, netSprint;
        float lerpSpeed = 12f;

        static readonly int HashMoveX = Animator.StringToHash("MoveX");
        static readonly int HashMoveZ = Animator.StringToHash("MoveZ");
        static readonly int HashLocoRate = Animator.StringToHash("LocoRate");
        static readonly int HashBlocking = Animator.StringToHash("Blocking");

        void Awake()
        {
            Motor = GetComponent<CombatMotor>();
            netPos = transform.position;
            netYaw = transform.eulerAngles.y;
        }

        void Start()
        {
            Motor.Net = this;
            if (IsMine)
                Motor.NetAction += OnOwnerAction;
            // networked spawns need to enter the match bookkeeping; scene rigs
            // are already registered by MatchManager.Start
            if (photonView != null && !photonView.IsSceneView)
                MatchManager.I?.RegisterNetworkedFighter(Motor, photonView.InstantiationData);
        }

        void OnDestroy()
        {
            if (Motor != null) Motor.NetAction -= OnOwnerAction;
        }

        // ------------------------------------------------------------------ streaming

        public void OnPhotonSerializeView(PhotonStream stream, PhotonMessageInfo info)
        {
            if (stream.IsWriting)
            {
                stream.SendNext(transform.position);
                stream.SendNext(transform.eulerAngles.y);
                stream.SendNext((byte)Motor.State);
                stream.SendNext(Motor.Health.Current);
                stream.SendNext(Motor.Health.Poise);
                stream.SendNext(Motor.Stamina.Current);
                stream.SendNext(Motor.AnimMoveX);
                stream.SendNext(Motor.AnimMoveZ);
                stream.SendNext(Motor.IsBlockingHeld);
                stream.SendNext(Motor.IsSprinting);
            }
            else
            {
                netPos = (Vector3)stream.ReceiveNext();
                netYaw = (float)stream.ReceiveNext();
                netState = (byte)stream.ReceiveNext();
                netHp = (float)stream.ReceiveNext();
                netPoise = (float)stream.ReceiveNext();
                netStamina = (float)stream.ReceiveNext();
                netMoveX = (float)stream.ReceiveNext();
                netMoveZ = (float)stream.ReceiveNext();
                netBlocking = (bool)stream.ReceiveNext();
                netSprint = (bool)stream.ReceiveNext();

                // hard-snap after big gaps (teleports, respawns, late join)
                if ((transform.position - netPos).sqrMagnitude > 25f)
                    transform.position = netPos;

                Motor.ApplyNetVitals(netHp, netPoise, netStamina, (MotorState)netState);
            }
        }

        void Update()
        {
            if (IsMine || Motor == null) return;

            // puppet interpolation: chase the streamed pose
            transform.position = Vector3.Lerp(transform.position, netPos, lerpSpeed * Time.deltaTime);
            var e = transform.eulerAngles;
            e.y = Mathf.LerpAngle(e.y, netYaw, lerpSpeed * Time.deltaTime);
            transform.eulerAngles = e;

            var anim = Motor.Anim;
            if (anim != null)
            {
                anim.SetFloat(HashMoveX, netMoveX);
                anim.SetFloat(HashMoveZ, netMoveZ);
                anim.SetFloat(HashLocoRate, netSprint ? 1.15f : 1f);
                anim.SetBool(HashBlocking, netBlocking);
            }
        }

        // ------------------------------------------------------------------ owner events -> puppets

        void OnOwnerAction(NetEvent ev, Vector3 v, int extraViewId, bool flag)
        {
            if (photonView == null || PhotonNetwork.OfflineMode) return;
            photonView.RPC(nameof(RPC_Event), RpcTarget.Others, (byte)ev, v, extraViewId, flag);
        }

        [PunRPC]
        void RPC_Event(byte evByte, Vector3 v, int extraViewId, bool flag)
        {
            var ev = (NetEvent)evByte;
            Motor.PuppetPlayback(ev, v, extraViewId, flag);
        }

        // ------------------------------------------------------------------ damage forwarding

        /// Called on the ATTACKER's client when its strike touched a fighter it
        /// doesn't own. The hit is applied for real on the victim's owner.
        public void ForwardHit(HitInfo hit)
        {
            int attackerView = hit.attacker != null && hit.attacker.Net != null &&
                               hit.attacker.Net.photonView != null
                ? hit.attacker.Net.photonView.ViewID : -1;
            photonView.RPC(nameof(RPC_TakeHit), photonView.Owner,
                hit.damage, hit.poiseDamage, hit.direction, hit.point,
                (byte)hit.element, hit.heavy, hit.unblockable, attackerView);
        }

        [PunRPC]
        void RPC_TakeHit(float dmg, float poiseDmg, Vector3 dir, Vector3 point,
            byte element, bool heavy, bool unblockable, int attackerView)
        {
            if (!IsMine || Motor.IsDead) return;
            var attackerSync = Find(attackerView);
            var hit = new HitInfo
            {
                attacker = attackerSync != null ? attackerSync.Motor : null,
                damage = dmg,
                poiseDamage = poiseDmg,
                direction = dir,
                point = point,
                element = (ElementId)element,
                heavy = heavy,
                unblockable = unblockable,
            };
            var res = Motor.Health.TakeHit(hit);
            if (res.landed)
                BroadcastImpact(point, (ElementId)element, res.damageDealt, res.blocked, heavy);
        }

        /// Impact cosmetics (spark + damage number) shown on every client.
        public void BroadcastImpact(Vector3 point, ElementId el, float dmg, bool blocked, bool heavy)
        {
            if (photonView == null || PhotonNetwork.OfflineMode)
            {
                GameEffects.I?.MeleeImpact(el, point, blocked, heavy);
                GameEffects.I?.ShowDamage(point, dmg, blocked);
                return;
            }
            photonView.RPC(nameof(RPC_Impact), RpcTarget.All, point, (byte)el, dmg, blocked, heavy);
        }

        [PunRPC]
        void RPC_Impact(Vector3 point, byte el, float dmg, bool blocked, bool heavy)
        {
            GameEffects.I?.MeleeImpact((ElementId)el, point, blocked, heavy);
            GameEffects.I?.ShowDamage(point, dmg, blocked);
        }

        public static FighterNetSync Find(int viewId)
        {
            if (viewId < 0) return null;
            var pv = PhotonView.Find(viewId);
            return pv != null ? pv.GetComponent<FighterNetSync>() : null;
        }
    }
}
