using UnityEngine;

namespace BorrowedSeconds
{
    /// <summary>
    /// Presentation time. Unscaled real time normally; while Time.captureFramerate is set (demo
    /// recording) it follows the fixed capture step, which Time.unscaled* ignores.
    /// </summary>
    public static class Clock
    {
        public static float Dt => Time.captureDeltaTime > 0f ? Time.deltaTime : Time.unscaledDeltaTime;
        public static float Now => Time.captureDeltaTime > 0f ? Time.time : Time.unscaledTime;
    }
}
