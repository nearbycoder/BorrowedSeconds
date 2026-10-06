using System;
using UnityEngine.InputSystem;

namespace BorrowedSeconds.Game
{
    /// <summary>Keyboard actions the player can rebind (Settings > Controls).</summary>
    public enum KeyAction { Up, Down, Left, Right, Borrow, Focus, Rewind, Restart, Hint, AimPrev, AimNext, Pause }

    /// <summary>
    /// The keyboard binding table. Arrow keys (move), Esc (pause/back), Enter (confirm), Tab (aim),
    /// Backspace (rewind) and the mouse keep their meaning whatever is bound, so the game can always
    /// be driven back to the Controls page. Stored in the save as key names.
    /// </summary>
    public static class KeyBindings
    {
        public static readonly Key[] Defaults =
        {
            Key.W, Key.S, Key.A, Key.D, Key.Space, Key.LeftShift, Key.Z, Key.R, Key.H, Key.Q, Key.E, Key.P,
        };

        public static readonly string[] Labels =
        {
            "Move up", "Move down", "Move left", "Move right", "Borrow", "Focus (hold)", "Rewind (hold)",
            "Restart", "Hint", "Aim previous", "Aim next", "Pause",
        };

        public static int Count => Defaults.Length;

        public static Key[] DefaultKeys() => (Key[])Defaults.Clone();

        /// <summary>Keys with a fixed meaning, which can't be bound to anything else.</summary>
        public static bool Reserved(Key k) => k == Key.None || k == Key.Escape || k == Key.Enter || k == Key.NumpadEnter
            || k == Key.Tab || k == Key.Backspace || k == Key.UpArrow || k == Key.DownArrow || k == Key.LeftArrow || k == Key.RightArrow;

        /// <summary>Bindings from saved key names; anything missing, unknown or reserved falls back to its default.</summary>
        public static Key[] Load(string[] names)
        {
            var keys = DefaultKeys();
            if (names == null) return keys;
            for (int i = 0; i < keys.Length && i < names.Length; i++)
                if (Enum.TryParse(names[i], out Key k) && !Reserved(k)) keys[i] = k;
            // a hand-edited save could bind one key twice: keep the first, default the rest
            for (int i = 0; i < keys.Length; i++)
                for (int j = 0; j < i; j++)
                    if (keys[j] == keys[i]) keys[i] = Defaults[i];
            return keys;
        }

        public static string[] Save(Key[] keys) => Array.ConvertAll(keys, k => k.ToString());

        /// <summary>
        /// Binds <paramref name="action"/> to <paramref name="key"/>. If another action had that key, it
        /// takes this action's old key (a swap, never a duplicate). Returns that other action, or -1.
        /// </summary>
        public static int Assign(Key[] keys, KeyAction action, Key key)
        {
            int a = (int)action, other = Array.IndexOf(keys, key);
            if (other == a) return -1;
            if (other >= 0) keys[other] = keys[a];
            keys[a] = key;
            return other;
        }
    }
}
