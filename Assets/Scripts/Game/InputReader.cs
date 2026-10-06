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
        public bool PointerMoved, Click;
        /// <summary>Mouse wheel this frame: +1 up, -1 down, 0 none.</summary>
        public int Scroll;
        public Vector2 Pointer;
        public bool AnyKey;
        public bool UsingGamepad;

        readonly float[] heldSince = new float[4];
        Vector2 lastPointer;

        public void Poll()
        {
            var kb = Keyboard.current;
            var mouse = Mouse.current;
            var pad = Gamepad.current;
            PressedDir = -1;
            Borrow = Focus = Rewind = Restart = Pause = Confirm = Back = CycleNext = CyclePrev = Hint = Click = false;
            AnyKey = false;
            Scroll = 0;

            bool[] held = new bool[4];
            bool[] down = new bool[4];
            if (kb != null)
            {
                Read(kb.wKey, kb.upArrowKey, Dirs.N, held, down);
                Read(kb.dKey, kb.rightArrowKey, Dirs.E, held, down);
                Read(kb.sKey, kb.downArrowKey, Dirs.S, held, down);
                Read(kb.aKey, kb.leftArrowKey, Dirs.W, held, down);
                Borrow |= kb.spaceKey.wasPressedThisFrame;
                Focus |= kb.leftShiftKey.isPressed || kb.rightShiftKey.isPressed;
                Rewind |= kb.zKey.isPressed || kb.backspaceKey.isPressed;
                Restart |= kb.rKey.wasPressedThisFrame;
                Pause |= kb.escapeKey.wasPressedThisFrame || kb.pKey.wasPressedThisFrame;
                Confirm |= kb.enterKey.wasPressedThisFrame || kb.spaceKey.wasPressedThisFrame || kb.numpadEnterKey.wasPressedThisFrame;
                Back |= kb.escapeKey.wasPressedThisFrame;
                CycleNext |= kb.tabKey.wasPressedThisFrame && !kb.shiftKey.isPressed || kb.eKey.wasPressedThisFrame;
                CyclePrev |= kb.tabKey.wasPressedThisFrame && kb.shiftKey.isPressed || kb.qKey.wasPressedThisFrame;
                Hint |= kb.hKey.wasPressedThisFrame;
                AnyKey |= kb.anyKey.wasPressedThisFrame;
                if (kb.anyKey.wasPressedThisFrame) UsingGamepad = false;
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

        static void Read(KeyControl a, KeyControl b, int dir, bool[] held, bool[] down)
        {
            if (a.isPressed || b.isPressed) held[dir] = true;
            if (a.wasPressedThisFrame || b.wasPressedThisFrame) down[dir] = true;
        }
    }
}
