// Chase camera: sits behind and above a target, looking slightly ahead of it.
//
// Default is LOCKED TIGHT — the camera is rigidly pinned to the target's pose
// every frame (no smoothing). Exponential smoothing always trails a moving
// target by velocity/sharpness, which (a) glides in over a second at startup and
// (b) shifts the framing when speed changes (e.g. slowing in a traffic jam, the
// trailing gap shrinks and the camera creeps in). A rigid follow has neither
// problem and reads as the fixed-camera NES look. Turn `lockTight` off for the
// old cushioned smoothing (with a first-frame snap to kill the startup glide).

using UnityEngine;

namespace NightRider.View
{
    public class ChaseCamera : MonoBehaviour
    {
        [Tooltip("What to follow (the rider).")]
        public Transform target;

        [Min(0f), Tooltip("How far behind the target to sit.")]
        public float distance = 8f;

        [Min(0f), Tooltip("How far above the target to sit.")]
        public float height = 4f;

        [Tooltip("Look this far ahead of the target instead of straight at it.")]
        public float lookAhead = 4f;

        [Tooltip("Rigidly pin to the target's pose every frame (no smoothing lag). " +
                 "On = no startup glide, no traffic-jam drift, framing never shifts with speed.")]
        public bool lockTight = true;

        [Tooltip("When smoothing (lockTight off), still jump straight to the pose on the first frame.")]
        public bool snapAtStart = true;

        [Min(0f), Tooltip("Position smoothing (only when lockTight is off). Higher = tighter.")]
        public float followSharpness = 8f;

        [Min(0f), Tooltip("Rotation smoothing (only when lockTight is off). Higher = tighter.")]
        public float rotationSharpness = 8f;

        bool _hasPose;

        void OnEnable() => _hasPose = false;

        // Force an instant re-lock on the next frame (call after a teleport/respawn).
        public void Snap() => _hasPose = false;

        void LateUpdate()
        {
            if (target == null) return;

            Vector3 desiredPos = target.position
                                 - target.forward * distance
                                 + Vector3.up * height;

            // Aim from where the camera WILL sit (desiredPos), not where it currently
            // lags to — so the look stays correct even mid-catch-up.
            Vector3 lookPoint = target.position + target.forward * lookAhead;
            Quaternion desiredRot = Quaternion.LookRotation(lookPoint - desiredPos, Vector3.up);

            bool rigid = lockTight || (snapAtStart && !_hasPose);
            _hasPose = true;

            if (rigid)
            {
                transform.SetPositionAndRotation(desiredPos, desiredRot);
                return;
            }

            float posT = 1f - Mathf.Exp(-followSharpness * Time.deltaTime);
            transform.position = Vector3.Lerp(transform.position, desiredPos, posT);

            float rotT = 1f - Mathf.Exp(-rotationSharpness * Time.deltaTime);
            transform.rotation = Quaternion.Slerp(transform.rotation, desiredRot, rotT);
        }
    }
}
