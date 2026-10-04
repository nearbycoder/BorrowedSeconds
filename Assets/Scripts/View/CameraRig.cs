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

        void LateUpdate()
        {
            if (Cam == null) return;
            float dt = Time.unscaledDeltaTime;
            float k = 1f - Mathf.Exp(-4f * dt);
            focus = Vector3.Lerp(focus, targetFocus, k);
            distance = Mathf.Lerp(distance, targetDistance, k);

            // spring for punch-ins
            punchVel += (-punch * 90f - punchVel * 14f) * dt;
            punch += punchVel * dt;

            shake = Mathf.MoveTowards(shake, 0f, dt * 2.5f);
            float t = Time.unscaledTime;
            shakeOffset = new Vector3(Mathf.PerlinNoise(seed, t * 28f) - 0.5f, Mathf.PerlinNoise(seed + 7f, t * 28f) - 0.5f, 0f) * shake * 0.5f;

            float driftYaw = Mathf.Sin(t * 0.13f) * 0.8f;
            float driftPitch = Mathf.Sin(t * 0.17f + 1.3f) * 0.5f;
            var rot = Quaternion.Euler(Pitch + driftPitch, driftYaw, 0f);
            float d = distance * (1f - Mathf.Clamp(punch, -0.2f, 0.2f));
            Cam.transform.position = focus - rot * Vector3.forward * d;
            Cam.transform.rotation = rot;
            Cam.transform.position += Cam.transform.right * shakeOffset.x + Cam.transform.up * shakeOffset.y;
        }
    }
}
