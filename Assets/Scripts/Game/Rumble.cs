using UnityEngine;
using UnityEngine.InputSystem;

namespace BorrowedSeconds.Game
{
    /// <summary>
    /// Short motor pulses on the gamepad in use (Settings > Controller vibration): a borrow, the
    /// debt falling due, a default, a dial latching, settling a level. A pulse replaces the one
    /// before and stops by itself; <see cref="Stop"/> silences the pad at once (pause, focus loss,
    /// a new level, an unplugged pad, quitting).
    /// </summary>
    public sealed class Rumble
    {
        Gamepad pad;
        float until = -1f;

        /// <summary>The pad a pulse is running on (null when quiet).</summary>
        public Gamepad Active => pad;

        public void Pulse(Gamepad target, float low, float high, float seconds)
        {
            if (target == null || !target.added) return;
            if (pad != null && pad != target) Stop();
            pad = target;
            pad.SetMotorSpeeds(low, high);
            until = Time.realtimeSinceStartup + seconds; // real time: hit-stop and Focus slow the game, not the motors
        }

        /// <summary>Ends a pulse whose time is up (once a frame).</summary>
        public void Tick()
        {
            if (pad != null && Time.realtimeSinceStartup >= until) Stop();
        }

        public void Stop()
        {
            if (pad != null && pad.added) pad.ResetHaptics();
            pad = null;
            until = -1f;
        }
    }
}
