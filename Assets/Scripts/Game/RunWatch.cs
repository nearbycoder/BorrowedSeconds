using UnityEngine;

namespace BorrowedSeconds.Game
{
    /// <summary>
    /// Timeout for a scripted run (autopilot, checks). The run only times out once its tick stops
    /// advancing for both 300 frames and 10 s, or after a generous cap. A loaded machine rendering a
    /// few frames a second slows a replay down (a frame is clamped to 0.1 s of game time), and a
    /// window the compositor stops drawing gets no frames at all; neither must fail it.
    /// </summary>
    public sealed class RunWatch
    {
        readonly LevelSession session;
        readonly float started, cap, stall;
        float lastMove;
        int lastTick, frames, stillFrames;
        const int StallFrames = 300;

        public RunWatch(LevelSession session, float capSeconds = 300f, float stallSeconds = 10f)
        {
            this.session = session;
            started = lastMove = Time.realtimeSinceStartup;
            cap = capSeconds;
            stall = stallSeconds;
            lastTick = session.Tick;
        }

        /// <summary>Call once a frame; true while the run may keep going.</summary>
        public bool Alive()
        {
            float now = Time.realtimeSinceStartup;
            frames++;
            stillFrames++;
            // intros, hit-stops and pauses hold the tick on purpose
            if (session.Tick != lastTick || session.State != LevelSession.Mode.Playing || session.Paused)
            {
                lastTick = session.Tick;
                lastMove = now;
                stillFrames = 0;
            }
            return now - started < cap && (now - lastMove < stall || stillFrames < StallFrames);
        }

        public float Elapsed => Time.realtimeSinceStartup - started;

        /// <summary>Why a run that didn't win ended, for the log.</summary>
        public string Why(LevelSession current)
        {
            string cause = current != session ? "replaced"
                : session.Cur.Dead ? "died"
                : Elapsed >= cap ? "over the cap"
                : $"no tick for {stillFrames} frames";
            return $"{cause}: tick={session.Tick} mode={session.State} {Elapsed:0.0}s at {frames / Mathf.Max(Elapsed, 0.01f):0} fps";
        }
    }
}
