// Marker for a navigable lane. Sits on a single-spline Spline Container.
// Holds per-lane settings and the world-space spline helpers that both the
// adjacency bake (LaneNetwork) and the rider (LaneFollower) rely on.

using UnityEngine;
using UnityEngine.Splines;
using Unity.Mathematics;
using Unity.Collections;

namespace NightRider.World
{
    [RequireComponent(typeof(SplineContainer))]
    [DisallowMultipleComponent]
    public class Lane : MonoBehaviour
    {
        [Tooltip("Skip this lane when auto-linking neighbours. Safety hatch for " +
                 "the rare case two parallel same-direction lanes shouldn't be jumpable.")]
        public bool excludeFromAutoLink = false;

        SplineContainer _container;
        public SplineContainer Container => _container ? _container : _container = GetComponent<SplineContainer>();

        public bool IsValid => Container != null && Container.Spline != null && Container.Spline.Count >= 2;
        public bool Closed  => IsValid && Container.Spline.Closed;
        public float Length => Container != null ? Container.CalculateLength() : 0f;

        /// World-space pose at normalized distance t in [0,1].
        public void EvaluateWorld(float t, out Vector3 pos, out Vector3 forward, out Vector3 up)
        {
            Container.Evaluate(t, out float3 p, out float3 tan, out float3 u);
            pos     = (Vector3)p;
            forward = ((Vector3)tan).normalized;
            up      = ((Vector3)u).normalized;
        }

        /// Rightward direction at t (left is -this).
        public Vector3 RightAt(float t)
        {
            EvaluateWorld(t, out _, out var fwd, out var up);
            return Vector3.Cross(up, fwd).normalized;
        }

        /// Closest point on this lane to a world position; returns its t.
        public float ProjectWorldPoint(Vector3 worldPos, out Vector3 nearest)
        {
            var native = new NativeSpline(Container.Spline, transform.localToWorldMatrix, Allocator.Temp);
            SplineUtility.GetNearestPoint(native, (float3)worldPos, out float3 n, out float t);
            native.Dispose();
            nearest = (Vector3)n;
            return t;
        }
    }
}
