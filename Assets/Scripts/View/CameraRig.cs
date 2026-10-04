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

        /// <summary>Fits the board bounds with room for the HUD (top) and the pocket watch (bottom).</summary>
        public void Frame(Bounds b, bool snap)
        {
            float aspect = Mathf.Max(1f, Cam.aspect);
            float tan = Mathf.Tan(Fov * 0.5f * Mathf.Deg2Rad);
            float sin = Mathf.Sin(Pitch * Mathf.Deg2Rad);
            float w = b.size.x + 1.4f;
            float h = b.size.z * sin + 1.2f * Mathf.Cos(Pitch * Mathf.Deg2Rad) + 2.6f;
            targetDistance = Mathf.Max(w / (2f * tan * aspect), h / (2f * tan));
            targetFocus = b.center + new Vector3(0, 0, -0.15f);
            if (snap)
            {
                distance = targetDistance;
                focus = targetFocus;
            }
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
