using BorrowedSeconds.Sim;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Controls;

namespace BorrowedSeconds.Game
{
    /// <summary>
    /// What a pad's buttons are called, by the pad in use: Xbox names unless the Input System
    /// recognises a PlayStation or Nintendo controller. South/East/West/North are positions, so
    /// a Switch Pro controller's bottom button (Borrow, confirm) is its B.
    /// </summary>
    public sealed class PadNames
    {
        public string South, East, West, North, Shoulders, LeftTrigger, Select, Start, Stick = "Stick", Dpad = "D-pad";
        public string Family;

        public static readonly PadNames Xbox = new PadNames
        { Family = "Xbox", South = "A", East = "B", West = "X", North = "Y", Shoulders = "LB/RB", LeftTrigger = "LT", Select = "Select", Start = "Start" };
        public static readonly PadNames DualShock4 = new PadNames
        { Family = "PlayStation", South = "Cross", East = "Circle", West = "Square", North = "Triangle", Shoulders = "L1/R1", LeftTrigger = "L2", Select = "Share", Start = "Options" };
        public static readonly PadNames DualSense = new PadNames
        { Family = "PlayStation", South = "Cross", East = "Circle", West = "Square", North = "Triangle", Shoulders = "L1/R1", LeftTrigger = "L2", Select = "Create", Start = "Options" };
        public static readonly PadNames DualShock3 = new PadNames
        { Family = "PlayStation", South = "Cross", East = "Circle", West = "Square", North = "Triangle", Shoulders = "L1/R1", LeftTrigger = "L2", Select = "Select", Start = "Start" };
        public static readonly PadNames Nintendo = new PadNames
        { Family = "Nintendo", South = "B", East = "A", West = "Y", North = "X", Shoulders = "L/R", LeftTrigger = "ZL", Select = "\u2212", Start = "+" };
        /// <summary>The browser build's on-screen controls on a phone or tablet, by the names on their buttons.</summary>
        public static readonly PadNames Touch = new PadNames
        { Family = "Touch", South = "Borrow", East = "Back", West = "Rewind", North = "Restart", Shoulders = "Tap", LeftTrigger = "Focus", Select = "Hint", Start = "Pause", Stick = "D-pad", Dpad = "D-pad" };

#if UNITY_WEBGL && !UNITY_EDITOR
        // a browser hands every pad over in its "standard" layout; only its id string says what it is
        // (Chrome: "DualSense Wireless Controller (STANDARD GAMEPAD Vendor: 054c Product: 0ce6)",
        // Firefox: "054c-0ce6-Sony Interactive Entertainment Wireless Controller")
        public static PadNames For(Gamepad pad)
        {
            string id = (pad?.description.product ?? "").ToLowerInvariant();
            if (id.Contains("054c") || id.Contains("sony") || id.Contains("dualsense") || id.Contains("dualshock"))
                return id.Contains("0ce6") || id.Contains("0df2") || id.Contains("dualsense") ? DualSense : DualShock4;
            if (id.Contains("057e") || id.Contains("nintendo") || id.Contains("pro controller")) return Nintendo;
            return Xbox;
        }
#else
        public static PadNames For(Gamepad pad) => pad switch
        {
            UnityEngine.InputSystem.DualShock.DualSenseGamepadHID => DualSense,
            UnityEngine.InputSystem.DualShock.DualShock3GamepadHID => DualShock3,
            UnityEngine.InputSystem.DualShock.DualShockGamepad => DualShock4,
            UnityEngine.InputSystem.Switch.SwitchProController => Nintendo,
            _ => Xbox,
        };
#endif

        /// <summary>Replaces {South}, {East}, {West}, {North}, {Shoulders}, {LeftTrigger}, {Select}, {Start} in a tip.</summary>
        public string Fill(string text) => string.IsNullOrEmpty(text) || text.IndexOf('{') < 0 ? text : text
            .Replace("{South}", South).Replace("{East}", East).Replace("{West}", West).Replace("{North}", North)
            .Replace("{Shoulders}", Shoulders).Replace("{LeftTrigger}", LeftTrigger).Replace("{Select}", Select).Replace("{Start}", Start);
    }

    /// <summary>
    /// Polls keyboard, mouse and gamepad once per frame. Keeps the most recently pressed held
    /// direction so diagonal mashing still feels predictable on a grid.
    /// </summary>
    public sealed class InputReader
    {
        public int HeldDir = -1;      // direction currently held (most recent wins)
        public int PressedDir = -1;   // direction newly pressed this frame
        public bool Borrow, Focus, Rewind, Restart, Pause, Confirm, Back, CycleNext, CyclePrev, Hint;
        /// <summary>The restart key/button is down this frame (Restart is only the press).</summary>
        public bool RestartHeld;
        public bool PointerMoved, Click;
        /// <summary>Mouse wheel this frame: +1 up, -1 down, 0 none.</summary>
        public int Scroll;
        public Vector2 Pointer;
        public bool AnyKey;
        public bool UsingGamepad;
        /// <summary>The browser's on-screen touch controls are in use (WebBridge). They count as a pad as well
        /// (UsingGamepad, with <see cref="PadNames.Touch"/> names), so aim and hints work as they do on one.</summary>
        public bool UsingTouch;
        /// <summary>A tap on the game this frame, at <see cref="Pointer"/>: menus take it as a click, a level as aiming.</summary>
        public bool Tap;
        /// <summary>Button names for the pad in use (Xbox names until a pad is seen).</summary>
        public PadNames Pad = PadNames.Xbox;
        /// <summary>Focus toggles on each press instead of lasting while held (a Settings option).</summary>
        public bool FocusToggle;
        bool focusWasDown, focusLatched;
        /// <summary>The keyboard binding table (KeyBindings), indexed by KeyAction.</summary>
        public Key[] Keys = KeyBindings.DefaultKeys();
        /// <summary>A keyboard key pressed this frame (Key.None if none): the Controls page listens to it.</summary>
        public Key PressedKey;

        readonly float[] heldSince = new float[4];
        Vector2 lastPointer;

        /// <summary>Switches a toggled Focus off (a level starts, pauses or ends).</summary>
        public void ReleaseFocus() => focusLatched = false;

        public void Poll()
        {
            var kb = Keyboard.current;
            var mouse = Mouse.current;
            var pad = Gamepad.current;
            PressedDir = -1;
            Borrow = Focus = Rewind = Restart = Pause = Confirm = Back = CycleNext = CyclePrev = Hint = Click = RestartHeld = Tap = PointerMoved = false;
            // the page hides its touch controls itself on a key press or a real mouse; a pad is reported below
            bool touch = DisplayOptions.Web && WebBridge.TouchActive;
            AnyKey = false;
            Scroll = 0;
            PressedKey = Key.None;

            bool[] held = new bool[4];
            bool[] down = new bool[4];
            if (kb != null)
            {
                Read(kb, KeyAction.Up, kb.upArrowKey, Dirs.N, held, down);
                Read(kb, KeyAction.Right, kb.rightArrowKey, Dirs.E, held, down);
                Read(kb, KeyAction.Down, kb.downArrowKey, Dirs.S, held, down);
                Read(kb, KeyAction.Left, kb.leftArrowKey, Dirs.W, held, down);
                Borrow |= Pressed(kb, KeyAction.Borrow);
                Focus |= Held(kb, KeyAction.Focus);
                Rewind |= Held(kb, KeyAction.Rewind) || kb.backspaceKey.isPressed;
                Restart |= Pressed(kb, KeyAction.Restart);
                RestartHeld |= Held(kb, KeyAction.Restart);
                Pause |= kb.escapeKey.wasPressedThisFrame || Pressed(kb, KeyAction.Pause);
                Confirm |= kb.enterKey.wasPressedThisFrame || kb.spaceKey.wasPressedThisFrame || kb.numpadEnterKey.wasPressedThisFrame;
                Back |= kb.escapeKey.wasPressedThisFrame;
                // Tab always aims forward: Shift is Focus, so Shift+Tab must not flip the direction
                CycleNext |= kb.tabKey.wasPressedThisFrame || Pressed(kb, KeyAction.AimNext);
                CyclePrev |= Pressed(kb, KeyAction.AimPrev);
                Hint |= Pressed(kb, KeyAction.Hint);
                AnyKey |= kb.anyKey.wasPressedThisFrame;
                if (kb.anyKey.wasPressedThisFrame)
                {
                    UsingGamepad = false;
                    UsingTouch = false;
                    foreach (var k in kb.allKeys)
                        if (k.wasPressedThisFrame) { PressedKey = k.keyCode; break; }
                }
            }
            // while the touch controls are up, a touch never reaches the game as a mouse
            if (mouse != null && touch) lastPointer = mouse.position.ReadValue();
            else if (mouse != null)
            {
                Pointer = mouse.position.ReadValue();
                PointerMoved = (Pointer - lastPointer).sqrMagnitude > 4f;
                if (PointerMoved) UsingGamepad = false;
                lastPointer = Pointer;
                Borrow |= mouse.leftButton.wasPressedThisFrame;
                Click = mouse.leftButton.wasPressedThisFrame;
                Focus |= mouse.rightButton.isPressed;
                float wheel = mouse.scroll.ReadValue().y;
                Scroll = wheel > 0.01f ? 1 : wheel < -0.01f ? -1 : 0;
                AnyKey |= mouse.leftButton.wasPressedThisFrame;
            }
            if (pad != null)
            {
                Pad = PadNames.For(pad);
                var stick = pad.leftStick.ReadValue();
                var dpad = pad.dpad.ReadValue();
                var v = dpad.sqrMagnitude > 0.1f ? dpad : stick;
                int sd = -1;
                if (v.magnitude > 0.5f)
                    sd = Mathf.Abs(v.x) > Mathf.Abs(v.y) ? (v.x > 0 ? Dirs.E : Dirs.W) : (v.y > 0 ? Dirs.N : Dirs.S);
                for (int d = 0; d < 4; d++)
                {
                    bool h = sd == d;
                    if (h && !held[d])
                    {
                        held[d] = true;
                        if (heldSince[d] <= 0f) down[d] = true;
                    }
                }
                if (pad.buttonSouth.wasPressedThisFrame) { Borrow = true; Confirm = true; }
                Focus |= pad.leftTrigger.ReadValue() > 0.4f;
                Rewind |= pad.buttonWest.isPressed;
                Restart |= pad.buttonNorth.wasPressedThisFrame;
                RestartHeld |= pad.buttonNorth.isPressed;
                Pause |= pad.startButton.wasPressedThisFrame;
                Back |= pad.buttonEast.wasPressedThisFrame;
                CycleNext |= pad.rightShoulder.wasPressedThisFrame;
                CyclePrev |= pad.leftShoulder.wasPressedThisFrame;
                Hint |= pad.selectButton.wasPressedThisFrame;
                // any button, trigger or stick switches the hints to the pad (Select alone must, too)
                bool padAny = v.magnitude > 0.5f || pad.leftTrigger.ReadValue() > 0.4f || pad.rightTrigger.ReadValue() > 0.4f
                    || pad.buttonSouth.wasPressedThisFrame || pad.buttonEast.wasPressedThisFrame || pad.buttonWest.wasPressedThisFrame
                    || pad.buttonNorth.wasPressedThisFrame || pad.startButton.wasPressedThisFrame || pad.selectButton.wasPressedThisFrame
                    || pad.leftShoulder.wasPressedThisFrame || pad.rightShoulder.wasPressedThisFrame;
                AnyKey |= pad.buttonSouth.wasPressedThisFrame || pad.startButton.wasPressedThisFrame;
                if (padAny) UsingGamepad = true;
                if (padAny && touch) { WebBridge.OtherInput(); touch = false; }
            }
            if (touch) PollTouch(held, down);
            else if (UsingTouch) { UsingTouch = false; UsingGamepad = false; }
            // Focus as read from the devices is "held"; in toggle mode each fresh press flips it instead
            bool focusDown = Focus;
            if (FocusToggle && focusDown && !focusWasDown) focusLatched = !focusLatched;
            focusWasDown = focusDown;
            if (FocusToggle) Focus = focusLatched;

            float now = Clock.Now;
            for (int d = 0; d < 4; d++)
            {
                if (held[d])
                {
                    if (heldSince[d] <= 0f) heldSince[d] = now;
                }
                else heldSince[d] = 0f;
                if (down[d]) PressedDir = d;
            }
            // most recently pressed held direction wins
            HeldDir = -1;
            float best = -1f;
            for (int d = 0; d < 4; d++)
                if (held[d] && heldSince[d] > best) { best = heldSince[d]; HeldDir = d; }
        }

        /// <summary>The on-screen controls, as one more pad: the d-pad, Borrow, Focus and Rewind (held), Restart
        /// (held), Hint, Pause, Back and OK, and taps on the game.</summary>
        void PollTouch(bool[] held, bool[] down)
        {
            UsingTouch = true;
            UsingGamepad = true;
            Pad = PadNames.Touch;
            int h = WebBridge.Held, p = WebBridge.Pressed;
            bool Is(int bits, int b) => (bits & b) != 0;
            int[] dirs = { WebBridge.Button.Up, WebBridge.Button.Right, WebBridge.Button.Down, WebBridge.Button.Left };
            for (int d = 0; d < 4; d++)
            {
                // a tap shorter than a frame is still a press (the page latches it until read)
                if (Is(h, dirs[d])) held[d] = true;
                if (Is(p, dirs[d])) down[d] = true;
            }
            Borrow |= Is(p, WebBridge.Button.Borrow);
            Focus |= Is(h, WebBridge.Button.Focus);
            Rewind |= Is(h, WebBridge.Button.Rewind);
            Restart |= Is(p, WebBridge.Button.Restart);
            RestartHeld |= Is(h, WebBridge.Button.Restart);
            Hint |= Is(p, WebBridge.Button.Hint);
            Pause |= Is(p, WebBridge.Button.Pause) || Is(p, WebBridge.Button.Back); // Back is Esc: it pauses, too
            Back |= Is(p, WebBridge.Button.Back);
            Confirm |= Is(p, WebBridge.Button.Confirm);
            CycleNext |= Is(p, WebBridge.Button.AimNext);
            AnyKey |= p != 0;
            if (WebBridge.TakeTap(out var at))
            {
                Pointer = at;
                PointerMoved = true; // a tap selects what it lands on, as a mouse moved there would
                Click = true;
                Tap = true;
                AnyKey = true;
            }
        }

        void Read(Keyboard kb, KeyAction a, KeyControl arrow, int dir, bool[] held, bool[] down)
        {
            if (Held(kb, a) || arrow.isPressed) held[dir] = true;
            if (Pressed(kb, a) || arrow.wasPressedThisFrame) down[dir] = true;
        }

        // a bound Shift, Ctrl or Alt accepts either side, as players expect
        static Key Twin(Key k) => k switch
        {
            Key.LeftShift => Key.RightShift, Key.RightShift => Key.LeftShift,
            Key.LeftCtrl => Key.RightCtrl, Key.RightCtrl => Key.LeftCtrl,
            Key.LeftAlt => Key.RightAlt, Key.RightAlt => Key.LeftAlt,
            _ => Key.None,
        };

        bool Held(Keyboard kb, KeyAction a)
        {
            var k = Keys[(int)a];
            if (k == Key.None) return false;
            var t = Twin(k);
            return kb[k].isPressed || (t != Key.None && kb[t].isPressed);
        }

        bool Pressed(Keyboard kb, KeyAction a)
        {
            var k = Keys[(int)a];
            if (k == Key.None) return false;
            var t = Twin(k);
            return kb[k].wasPressedThisFrame || (t != Key.None && kb[t].wasPressedThisFrame);
        }

        /// <summary>What a key is called on this keyboard's layout, for hints and the Controls page.</summary>
        public static string KeyLabel(Key k)
        {
            switch (k)
            {
                case Key.LeftShift: case Key.RightShift: return "Shift";
                case Key.LeftCtrl: case Key.RightCtrl: return "Ctrl";
                case Key.LeftAlt: case Key.RightAlt: return "Alt";
                case Key.Space: return "Space";
                case Key.None: return "-";
            }
            var kb = Keyboard.current;
            string name = kb != null ? kb[k].displayName : null;
            if (string.IsNullOrEmpty(name)) name = k.ToString();
            return name.Length == 1 ? name.ToUpperInvariant() : name;
        }

        public string KeyName(KeyAction a) => KeyLabel(Keys[(int)a]);

        /// <summary>The four move keys as one hint ("WASD"), or "Arrows" if they don't spell anything short.</summary>
        public string MoveKeysName()
        {
            string s = KeyName(KeyAction.Up) + KeyName(KeyAction.Left) + KeyName(KeyAction.Down) + KeyName(KeyAction.Right);
            return s.Length == 4 ? s : "Arrows";
        }
    }
}
