using UnityEngine;
using UnityEngine.InputSystem;

namespace BrawlArena
{
    /// <summary>
    /// Feeds the local player's intent into BrawlerController: virtual joystick
    /// and attack button on touch devices, WASD/arrows + Space/J in the editor.
    /// Attacks auto-aim at the nearest enemy in range.
    /// </summary>
    [RequireComponent(typeof(BrawlerController))]
    public class PlayerBrawlerInput : MonoBehaviour
    {
        BrawlerController self;
        Camera cam;

        void Awake()
        {
            self = GetComponent<BrawlerController>();
        }

        void Start()
        {
            cam = Camera.main;
        }

        void Update()
        {
            Vector2 input = Vector2.zero;
            var hud = BrawlHUD.Instance;
            if (hud != null && hud.Joystick != null) input = hud.Joystick.Value;

            var kb = Keyboard.current;
            if (kb != null)
            {
                Vector2 keys = Vector2.zero;
                if (kb.wKey.isPressed || kb.upArrowKey.isPressed) keys.y += 1f;
                if (kb.sKey.isPressed || kb.downArrowKey.isPressed) keys.y -= 1f;
                if (kb.dKey.isPressed || kb.rightArrowKey.isPressed) keys.x += 1f;
                if (kb.aKey.isPressed || kb.leftArrowKey.isPressed) keys.x -= 1f;
                if (keys.sqrMagnitude > 0.01f) input = keys;
            }
            input = Vector2.ClampMagnitude(input, 1f);

            Vector3 world = new Vector3(input.x, 0f, input.y);
            if (cam != null)
            {
                float yaw = cam.transform.eulerAngles.y;
                world = Quaternion.Euler(0f, yaw, 0f) * world;
            }
            self.SetMoveInput(world);

            bool attack = false;
            if (hud != null && (hud.ConsumeAttackPressed() || hud.AttackHeld)) attack = true;
            if (kb != null && (kb.spaceKey.isPressed || kb.jKey.isPressed)) attack = true;

            if (attack) self.TryAttackAuto();

            bool sprint = false;
            if (hud != null && hud.SprintHeld) sprint = true;
            if (kb != null && (kb.leftShiftKey.isPressed || kb.rightShiftKey.isPressed)) sprint = true;
            self.SetSprintInput(sprint);
        }
    }
}
