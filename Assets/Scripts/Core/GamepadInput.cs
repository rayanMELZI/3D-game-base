using UnityEngine;

namespace FpsBase
{
    /// <summary>
    /// Standard Xbox controller on the legacy Input Manager (Windows / XInput
    /// numbering). Buttons are KeyCodes; the triggers and the D-pad are AXES
    /// (InputManager.asset: "Controller Left/Right Trigger" = 9th/10th axis,
    /// "Controller DPad X/Y" = 6th/7th axis), so their press edges are tracked
    /// here once per frame. Gameplay layout:
    ///   left stick move · click sprint · right stick look · click knife
    ///   RT fire · LT aim · RB lethal · LB tactical · A jump · X reload
    ///   B tap crouch / hold prone / slide while sprinting · Y next weapon
    ///   D-pad left/right previous/next weapon · View scoreboard · Menu pause
    /// Never read "Fire1": it also maps Left Ctrl (crouch would shoot).
    /// </summary>
    public static class GamepadInput
    {
        public const KeyCode A = KeyCode.JoystickButton0;
        public const KeyCode B = KeyCode.JoystickButton1;
        public const KeyCode X = KeyCode.JoystickButton2;
        public const KeyCode Y = KeyCode.JoystickButton3;
        public const KeyCode LB = KeyCode.JoystickButton4;
        public const KeyCode RB = KeyCode.JoystickButton5;
        public const KeyCode View = KeyCode.JoystickButton6;
        public const KeyCode Menu = KeyCode.JoystickButton7;
        public const KeyCode LeftStick = KeyCode.JoystickButton8;
        public const KeyCode RightStick = KeyCode.JoystickButton9;

        // Hysteresis so a trigger resting near the threshold can't chatter.
        private const float TriggerPress = 0.35f;
        private const float TriggerRelease = 0.2f;

        private static int sampledFrame = -1;
        private static bool rightHeld, rightWasHeld, leftHeld;
        private static int dpadX, dpadXBefore;

        private static void Sample()
        {
            if (sampledFrame == Time.frameCount)
                return;
            sampledFrame = Time.frameCount;
            rightWasHeld = rightHeld;
            dpadXBefore = dpadX;
            rightHeld = Trigger("Controller Right Trigger", rightHeld);
            leftHeld = Trigger("Controller Left Trigger", leftHeld);
            float x = Input.GetAxisRaw("Controller DPad X");
            dpadX = x > 0.5f ? 1 : (x < -0.5f ? -1 : 0);
        }

        private static bool Trigger(string axis, bool held)
        {
            float value = Input.GetAxisRaw(axis);
            return held ? value > TriggerRelease : value > TriggerPress;
        }

        /// <summary>Right trigger held (fire).</summary>
        public static bool FireHeld { get { Sample(); return rightHeld; } }
        /// <summary>Right trigger pressed this frame (semi-auto shots, reload interrupt).</summary>
        public static bool FireDown { get { Sample(); return rightHeld && !rightWasHeld; } }
        /// <summary>Left trigger held (aim down sights).</summary>
        public static bool AimHeld { get { Sample(); return leftHeld; } }
        /// <summary>-1 / +1 on the frame the D-pad is pushed left / right, else 0.</summary>
        public static int DPadXDown { get { Sample(); return dpadX != dpadXBefore ? dpadX : 0; } }

        /// <summary>
        /// Right stick, -1..1 per axis, after the radial dead zone from the
        /// settings and a squared response curve (fine aim near the centre,
        /// full speed at the edge). +Y = look up, flipped by "invert Y".
        /// </summary>
        public static Vector2 Look()
        {
            var raw = new Vector2(Input.GetAxisRaw("Controller Look X"), Input.GetAxisRaw("Controller Look Y"));
            float magnitude = raw.magnitude;
            float dead = GameSettings.ControllerDeadZone;
            if (magnitude <= dead)
                return Vector2.zero;
            float scaled = Mathf.Clamp01((magnitude - dead) / (1f - dead));
            var look = raw / magnitude * (scaled * scaled);
            if (GameSettings.ControllerInvertY)
                look.y = -look.y;
            return look;
        }
    }
}
