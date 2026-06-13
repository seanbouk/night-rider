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

        [Tooltip("Draw direction arrows along this lane in the Scene view, coloured by heading " +
                 "(opposite directions get complementary colours), so you can see which way it runs.")]
        public bool drawDirection = true;

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

        /// Rightward direction at t (left is -this). Uses world up, not the
        /// spline's own up vector — the world is flat, and a spline's up flips
        /// when a track is mirrored/reversed, which we must not inherit.
        public Vector3 RightAt(float t)
        {
            EvaluateWorld(t, out _, out var fwd, out _);
            return Vector3.Cross(Vector3.up, fwd).normalized;
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

#if UNITY_EDITOR
        // Scene-view direction guide: arrows along the lane, coloured by compass
        // heading (atan2 of the forward). Same colour = same direction (with);
        // complementary colour = opposite (against). Drawn with zTest = Always so it
        // always wins over the spline's own (blue) gizmo — no z-fighting.
        void OnDrawGizmos()
        {
            if (!drawDirection || !IsValid) return;

            var prevZ = UnityEditor.Handles.zTest;
            UnityEditor.Handles.zTest = UnityEngine.Rendering.CompareFunction.Always;

            int steps = Mathf.Clamp(Mathf.RoundToInt(Length / 3f), 6, 240);
            Vector3 prev = Vector3.zero;
            bool havePrev = false;
            for (int i = 0; i <= steps; i++)
            {
                EvaluateWorld(i / (float)steps, out var pos, out var fwd, out _);
                pos += Vector3.up * 0.4f;
                UnityEditor.Handles.color = HeadingColor(fwd);
                if (havePrev) UnityEditor.Handles.DrawAAPolyLine(4f, prev, pos);
                if (i % 4 == 0) DrawArrow(pos, fwd);
                prev = pos;
                havePrev = true;
            }

            UnityEditor.Handles.zTest = prevZ;
        }

        // Four-way palette by dominant axis: vertical = red (+Z) / green (-Z),
        // horizontal = yellow (+X) / blue (-X). Same colour = same direction (with);
        // its pair-mate = against. Flips at the 45-degree diagonal.
        static Color HeadingColor(Vector3 fwd)
        {
            if (Mathf.Abs(fwd.z) >= Mathf.Abs(fwd.x))
                return fwd.z >= 0f ? Color.red : new Color(0.2f, 1f, 0.3f);          // up / down
            return fwd.x >= 0f ? new Color(1f, 0.86f, 0.1f) : new Color(0.25f, 0.55f, 1f);  // right / left
        }

        static void DrawArrow(Vector3 pos, Vector3 fwd)
        {
            fwd = fwd.normalized;
            if (fwd.sqrMagnitude < 1e-4f) return;
            Vector3 right = Vector3.Cross(Vector3.up, fwd).normalized;
            const float s = 1.4f;
            Vector3 tip = pos + fwd * s;
            UnityEditor.Handles.DrawAAPolyLine(4f, pos, tip);
            UnityEditor.Handles.DrawAAPolyLine(4f,
                tip - fwd * (s * 0.55f) + right * (s * 0.4f),
                tip,
                tip - fwd * (s * 0.55f) - right * (s * 0.4f));
        }
#endif
    }
}
