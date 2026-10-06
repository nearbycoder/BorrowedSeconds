using BorrowedSeconds.Sim;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Controls;

namespace BorrowedSeconds.Game
{
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
        /// <summary>The keyboard binding table (KeyBindings), indexed by KeyAction.</summary>
        public Key[] Keys = KeyBindings.DefaultKeys();
        /// <summary>A keyboard key pressed this frame (Key.None if none): the Controls page listens to it.</summary>
        public Key PressedKey;

        readonly float[] heldSince = new float[4];
        Vector2 lastPointer;

        public void Poll()
        {
            var kb = Keyboard.current;
            var mouse = Mouse.current;
            var pad = Gamepad.current;
            PressedDir = -1;
            Borrow = Focus = Rewind = Restart = Pause = Confirm = Back = CycleNext = CyclePrev = Hint = Click = RestartHeld = false;
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
                    foreach (var k in kb.allKeys)
                        if (k.wasPressedThisFrame) { PressedKey = k.keyCode; break; }
                }
            }
            if (mouse != null)
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
            }

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
