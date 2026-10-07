using System.Collections.Generic;
using UnityEngine;

namespace BorrowedSeconds.View
{
    /// <summary>Diorama camera: frames the board at a fixed pitch, with drift, shake and punch-ins.</summary>
    public sealed class CameraRig : MonoBehaviour
    {
        public Camera Cam;
        public float Pitch = 56f;
        public float Fov = 30f;
        public bool ShakeEnabled = true;
        /// <summary>Slides the framed board sideways, as a fraction of the half screen width (menus sit left).</summary>
        public float ShiftX;
        /// <summary>Distance multiplier (title framing pulls back a little).</summary>
        public float Zoom = 1f;
        /// <summary>World-space shift of the framed point (the trailer leans toward the action).</summary>
        public Vector3 Offset;
        float shiftCur, zoomCur = 1f;
        Vector3 focus, targetFocus;
        float distance, targetDistance;
        float shake, punch, punchVel;
        Vector3 shakeOffset;
        float seed;

        public void Init(Camera cam)
        {
            Cam = cam;
            Cam.fieldOfView = Fov;
            seed = Random.value * 100f;
        }

        /// <summary>
        /// Fits the board bounds with room for the HUD (top) and the pocket watch (bottom). Given the
        /// board's tiles, it then pulls back and slides the view, as little as it can, until no tile
        /// sits under a HUD corner or the watch (a dial under the watch on a 4:3 screen, say).
        /// </summary>
        public void Frame(Bounds b, bool snap, IReadOnlyList<Vector3> tiles = null)
        {
            float aspect = Mathf.Max(1f, Cam.aspect);
            float tan = Mathf.Tan(Fov * 0.5f * Mathf.Deg2Rad);
            float sin = Mathf.Sin(Pitch * Mathf.Deg2Rad);
            float w = b.size.x + 1.4f;
            float h = b.size.z * sin + 1.2f * Mathf.Cos(Pitch * Mathf.Deg2Rad) + 2.6f;
            targetDistance = Mathf.Max(w / (2f * tan * aspect), h / (2f * tan));
            targetFocus = b.center + new Vector3(0, 0, -0.15f);
            if (tiles != null && tiles.Count > 0) ClearHud(tiles);
            if (snap)
            {
                distance = targetDistance;
                focus = targetFocus;
            }
        }

        /// <summary>Tiles under the HUD at the last framing (0 when it found a clear view).</summary>
        public int HudOverlap { get; private set; }
        /// <summary>How far the last framing pulled back for the HUD (1 = not at all).</summary>
        public float HudPullback { get; private set; } = 1f;

        void ClearHud(IReadOnlyList<Vector3> tiles)
        {
            float baseDistance = targetDistance;
            var baseFocus = targetFocus;
            int best = int.MaxValue;
            float bestK = 1f;
            Vector3 bestFocus = baseFocus;
            // smallest pull-back first, then the smallest slide; slides are in world units (z is up-screen)
            float[] dzs = { 0f, -0.3f, 0.3f, -0.6f, 0.6f, -0.9f, 0.9f, -1.2f, 1.2f, -1.6f, 1.6f, -2f, 2f };
            float[] dxs = { 0f, 0.5f, -0.5f, 1f, -1f, 1.5f, -1.5f, 2f, -2f };
            for (float k = 1f; k <= 1.401f && best > 0; k += 0.025f)
                foreach (float dz in dzs)
                {
                    foreach (float dx in dxs)
                    {
                        var f = baseFocus + new Vector3(dx, 0f, dz);
                        int n = Overlap(tiles, f, baseDistance * k);
                        if (n < best) { best = n; bestK = k; bestFocus = f; }
                        if (best == 0) break;
                    }
                    if (best == 0) break;
                }
            HudOverlap = best;
            HudPullback = bestK;
            targetDistance = baseDistance * bestK;
            targetFocus = bestFocus;
        }

        // HUD keep-out boxes in canvas units (the UI scales so the canvas is at least 1920x1080):
        // the title (top left), the clock (top right), the key hints (bottom left) and the watch
        static readonly Vector4[] LeftBoxes = { new Vector4(0f, -170f, 720f, 0f), new Vector4(0f, 0f, 760f, 120f) };   // x0, y0, x1, y1 from the left edge; y < 0 from the top
        static readonly Vector4 ClockBox = new Vector4(-420f, -150f, 0f, 0f);  // from the top-right corner
        static readonly Vector4 WatchBox = new Vector4(-130f, 0f, 130f, 285f); // from the bottom centre, up to the bow

        /// <summary>How many tiles (by their top face, corners inset) would sit under the HUD or off screen.</summary>
        int Overlap(IReadOnlyList<Vector3> tiles, Vector3 f, float d)
        {
            var rot = Quaternion.Euler(Pitch, 0f, 0f);
            var pos = f - rot * Vector3.forward * d;
            var view = Matrix4x4.Scale(new Vector3(1, 1, -1)) * Matrix4x4.TRS(pos, rot, Vector3.one).inverse;
            var proj = Matrix4x4.Perspective(Fov, Cam.aspect, 0.3f, 1000f) * view;
            float sw = Mathf.Max(1, UnityEngine.Screen.width), sh = Mathf.Max(1, UnityEngine.Screen.height);
            float s = Mathf.Min(sw / 1920f, sh / 1080f);
            float cw = sw / s, ch = sh / s;
            int n = 0;
            foreach (var t in tiles)
            {
                bool hit = false;
                for (int c = 0; c < 5 && !hit; c++)
                {
                    var p = t + (c == 0 ? Vector3.zero : new Vector3(c % 2 == 0 ? 0.4f : -0.4f, 0f, c < 3 ? 0.4f : -0.4f));
                    var clip = proj * new Vector4(p.x, p.y, p.z, 1f);
                    float x = (clip.x / clip.w * 0.5f + 0.5f) * cw, y = (clip.y / clip.w * 0.5f + 0.5f) * ch;
                    hit = x < 24f || x > cw - 24f || y < 24f || y > ch - 24f;
                    foreach (var r in LeftBoxes)
                        hit |= x >= r.x && x <= r.z && (r.y < 0f ? y >= ch + r.y : y <= r.w);
                    hit |= x >= cw + ClockBox.x && y >= ch + ClockBox.y;
                    hit |= x >= cw * 0.5f + WatchBox.x && x <= cw * 0.5f + WatchBox.z && y <= WatchBox.w;
                }
                if (hit) n++;
            }
            return n;
        }

        public void Shake(float amount)
        {
            if (ShakeEnabled) shake = Mathf.Max(shake, amount);
        }

        public void Punch(float amount) => punchVel += amount;

        /// <summary>Jumps to the current framing with no easing, shake or punch (for cuts).</summary>
        public void Snap()
        {
            focus = targetFocus;
            distance = targetDistance;
            zoomCur = Zoom;
            shiftCur = ShiftX;
            shake = punch = punchVel = 0f;
        }

        void LateUpdate()
        {
            if (Cam == null) return;
            float dt = Clock.Dt;
            float k = 1f - Mathf.Exp(-4f * dt);
            focus = Vector3.Lerp(focus, targetFocus, k);
            distance = Mathf.Lerp(distance, targetDistance, k);

            // spring for punch-ins
            punchVel += (-punch * 90f - punchVel * 14f) * dt;
            punch += punchVel * dt;

            shake = Mathf.MoveTowards(shake, 0f, dt * 2.5f);
            float t = Clock.Now;
            shakeOffset = new Vector3(Mathf.PerlinNoise(seed, t * 28f) - 0.5f, Mathf.PerlinNoise(seed + 7f, t * 28f) - 0.5f, 0f) * shake * 0.5f;

            float driftYaw = Mathf.Sin(t * 0.13f) * 0.8f;
            float driftPitch = Mathf.Sin(t * 0.17f + 1.3f) * 0.5f;
            var rot = Quaternion.Euler(Pitch + driftPitch, driftYaw, 0f);
            zoomCur = Mathf.Lerp(zoomCur, Zoom, 1f - Mathf.Exp(-3f * dt));
            float d = distance * zoomCur * (1f - Mathf.Clamp(punch, -0.2f, 0.2f));
            Cam.transform.position = focus + Offset - rot * Vector3.forward * d;
            Cam.transform.rotation = rot;
            Cam.transform.position += Cam.transform.right * shakeOffset.x + Cam.transform.up * shakeOffset.y;
            shiftCur = Mathf.Lerp(shiftCur, ShiftX, 1f - Mathf.Exp(-3f * dt));
            float halfW = d * Mathf.Tan(Fov * 0.5f * Mathf.Deg2Rad) * Cam.aspect;
            Cam.transform.position -= Cam.transform.right * shiftCur * halfW;
        }
    }
}
