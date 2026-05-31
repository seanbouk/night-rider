// Lightweight chase camera: sits behind and above a target, looking slightly
// ahead of it. Frame-rate-independent smoothing so the feel is consistent.
// (We can swap to Cinemachine later if the camera wants more polish.)

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

        [Min(0f), Tooltip("Position follow snappiness. Higher = tighter.")]
        public float followSharpness = 8f;

        [Min(0f), Tooltip("Rotation follow snappiness. Higher = tighter.")]
        public float rotationSharpness = 8f;

        void LateUpdate()
        {
            if (target == null) return;

            Vector3 desiredPos = target.position
                                 - target.forward * distance
                                 + Vector3.up * height;

            float posT = 1f - Mathf.Exp(-followSharpness * Time.deltaTime);
            transform.position = Vector3.Lerp(transform.position, desiredPos, posT);

            Vector3 lookPoint = target.position + target.forward * lookAhead;
            Quaternion desiredRot = Quaternion.LookRotation(lookPoint - transform.position, Vector3.up);

            float rotT = 1f - Mathf.Exp(-rotationSharpness * Time.deltaTime);
            transform.rotation = Quaternion.Slerp(transform.rotation, desiredRot, rotT);
        }
    }
}
