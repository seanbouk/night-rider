// Moves a transform along a lane's spline at a constant ground speed, facing
// forward. M2 version: no input, no lane-switching yet — just rides the lane.
// (The full LaneNavigator with left/right hops arrives at M5.)

using UnityEngine;
using UnityEngine.Splines;
using Unity.Mathematics;

namespace NightRider.World
{
    public class LaneFollower : MonoBehaviour
    {
        [Tooltip("The lane (single-spline Spline Container) to ride.")]
        public SplineContainer lane;

        [Min(0f), Tooltip("Ground speed in world units per second.")]
        public float speed = 12f;

        [Range(0f, 1f), Tooltip("Start position along the lane (0..1).")]
        public float t = 0f;

        [Tooltip("Lift above the road so the rider sits on it, not through it.")]
        public float heightOffset = 1f;

        [Tooltip("If the lane isn't closed, stop at the end instead of looping.")]
        public bool stopAtEnd = false;

        void Update()
        {
            if (lane == null || lane.Spline == null || lane.Spline.Count < 2) return;

            float length = lane.CalculateLength();
            if (length < 0.0001f) return;

            t += (speed * Time.deltaTime) / length;

            if (t >= 1f)
            {
                bool canLoop = lane.Spline.Closed && !stopAtEnd;
                t = canLoop ? t - 1f : 1f;
            }

            lane.Evaluate(t, out float3 pos, out float3 tan, out float3 up);
            Vector3 p   = (Vector3)pos;
            Vector3 fwd = ((Vector3)tan).normalized;
            Vector3 upv = ((Vector3)up).normalized;

            transform.position = p + upv * heightOffset;
            if (fwd.sqrMagnitude > 0.0001f)
                transform.rotation = Quaternion.LookRotation(fwd, upv);
        }
    }
}
