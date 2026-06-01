// Discovers which lanes are navigable neighbours, automatically.
//
// For each lane it walks the spline; at each step it finds the nearest other
// lane that is (a) within a lateral band and (b) heading the same way. Runs of
// consecutive steps with the same neighbour become an adjacency "region" with a
// start/end along the lane — so adjacency is regional, not global.
//
// Bake is deterministic: same geometry -> same links. Gizmos draw every link so
// you can verify before pressing Play. Results aren't serialized (recomputed) —
// fine while the map's small; we serialize later for a big world.
//
// Cost note: a bake is ~O(lanes^2 * samples), so it must NOT run per frame. In
// the editor it re-bakes only when geometry actually changes (spline edits via
// Spline.Changed; moved/added lanes via a cheap transform/count poll), debounced
// so a drag bakes once when it settles. At runtime it bakes once. Gizmo lines
// are cached at bake time so drawing never projects.

using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Splines;
using Unity.Mathematics;
using Unity.Collections;

namespace NightRider.World
{
    [ExecuteAlways]
    public class LaneNetwork : MonoBehaviour
    {
        [Header("Sampling")]
        [Min(0.1f), Tooltip("World units between samples along each lane.")]
        public float sampleSpacing = 1f;

        [Header("Neighbour test")]
        [Min(0f), Tooltip("Ignore lanes closer than this (avoids self-ish overlaps).")]
        public float minNeighbourDistance = 0.5f;
        [Min(0.1f), Tooltip("Ignore lanes farther than this to the side. ~1.5x lane spacing.")]
        public float maxNeighbourDistance = 6f;
        [Range(-1f, 1f), Tooltip("Min tangent dot for 'same direction'. 1=identical, 0=perpendicular.")]
        public float headingDotThreshold = 0.5f;

        [Header("Editor")]
        public bool autoRebakeInEditor = true;
        public Color leftColor  = new(1f, 0.35f, 0.35f);
        public Color rightColor = new(0.35f, 1f, 0.45f);

        public struct Link
        {
            public Lane from, to;
            public int side;          // -1 = neighbour on the left, +1 = on the right
            public float tStart, tEnd;
        }

        readonly List<Link> _links = new();
        public IReadOnlyList<Link> Links => _links;

        // Connector lines, precomputed at bake time so OnDrawGizmos never projects.
        readonly List<(Vector3 a, Vector3 b, int side)> _gizmoSegments = new();

        void OnEnable()
        {
#if UNITY_EDITOR
            Spline.Changed += OnSplineChanged;
#endif
            Bake();
        }

        void OnDisable()
        {
#if UNITY_EDITOR
            Spline.Changed -= OnSplineChanged;
#endif
        }

        void Start() => Bake();   // runtime: all lanes exist by now

#if UNITY_EDITOR
        bool _dirty;
        double _lastEditTime;
        int _lastLaneCount = -1;
        const double RebakeDebounce = 0.15;

        void OnSplineChanged(Spline spline, int knot, SplineModification mod) => MarkDirty();

        void MarkDirty()
        {
            _dirty = true;
            _lastEditTime = UnityEditor.EditorApplication.timeSinceStartup;
        }

        // Cheap checks every editor tick; the expensive bake runs only after
        // edits settle (debounced). Spline edits arrive via Spline.Changed;
        // moved or newly added lanes are caught by the transform/count poll.
        void Update()
        {
            if (Application.isPlaying || !autoRebakeInEditor) return;

            var lanes = FindObjectsByType<Lane>();
            if (lanes.Length != _lastLaneCount) { _lastLaneCount = lanes.Length; MarkDirty(); }
            foreach (var l in lanes)
                if (l.transform.hasChanged) { l.transform.hasChanged = false; MarkDirty(); }

            if (_dirty && UnityEditor.EditorApplication.timeSinceStartup - _lastEditTime > RebakeDebounce)
            {
                _dirty = false;
                Bake();
            }
        }
#endif

        [ContextMenu("Bake Now")]
        public void BakeNow()
        {
            Bake();
            Debug.Log($"[LaneNetwork] Baked {_links.Count} adjacency region(s).", this);
        }

        /// Neighbour on the given side (-1 left / +1 right) at position t, if any.
        public bool TryGetNeighbor(Lane lane, float t, int side, out Lane neighbor)
        {
            foreach (var l in _links)
                if (l.from == lane && l.side == side && t >= l.tStart && t <= l.tEnd)
                {
                    neighbor = l.to;
                    return true;
                }
            neighbor = null;
            return false;
        }

        public void Bake()
        {
            _links.Clear();
            _gizmoSegments.Clear();

            var lanes = FindObjectsByType<Lane>();
            if (lanes.Length < 2) return;

            // World-space native splines, built once and reused for projection.
            var natives = new Dictionary<Lane, NativeSpline>(lanes.Length);
            foreach (var l in lanes)
                if (l.IsValid)
                    natives[l] = new NativeSpline(l.Container.Spline, l.transform.localToWorldMatrix, Allocator.Temp);

            try
            {
                foreach (var a in lanes)
                {
                    if (!a.IsValid || a.excludeFromAutoLink) continue;

                    int n = Mathf.Max(2, Mathf.CeilToInt(a.Length / sampleSpacing));
                    var leftAt  = new Lane[n + 1];
                    var rightAt = new Lane[n + 1];

                    for (int i = 0; i <= n; i++)
                    {
                        float tA = i / (float)n;
                        a.EvaluateWorld(tA, out var posA, out var fwdA, out _);
                        // World up, not the spline's up: flat world, and a
                        // mirrored/reversed track's up flips (would swap L/R).
                        Vector3 rightA = Vector3.Cross(Vector3.up, fwdA).normalized;

                        float bestLeft = float.MaxValue, bestRight = float.MaxValue;

                        foreach (var b in lanes)
                        {
                            if (b == a || !b.IsValid || b.excludeFromAutoLink) continue;

                            var nb = natives[b];
                            float d = SplineUtility.GetNearestPoint(nb, (float3)posA, out float3 nearest, out float tB);
                            if (d < minNeighbourDistance || d > maxNeighbourDistance) continue;

                            Vector3 fwdB = ((Vector3)SplineUtility.EvaluateTangent(nb, tB)).normalized;
                            if (Vector3.Dot(fwdA, fwdB) < headingDotThreshold) continue; // not same direction

                            Vector3 offset = (Vector3)nearest - posA;
                            if (Vector3.Dot(offset, rightA) >= 0f)
                            {
                                if (d < bestRight) { bestRight = d; rightAt[i] = b; }
                            }
                            else
                            {
                                if (d < bestLeft) { bestLeft = d; leftAt[i] = b; }
                            }
                        }
                    }

                    BuildSpans(a, n, leftAt, -1);
                    BuildSpans(a, n, rightAt, +1);
                }

                CacheGizmos(natives);
            }
            finally
            {
                foreach (var kv in natives) kv.Value.Dispose();
            }
        }

        // Precompute connector lines once per bake (reusing the bake's native
        // splines), so OnDrawGizmos is just DrawLine calls — no per-repaint
        // projection or allocation.
        void CacheGizmos(Dictionary<Lane, NativeSpline> natives)
        {
            const int steps = 10;
            foreach (var l in _links)
            {
                if (l.from == null || l.to == null || !natives.TryGetValue(l.to, out var nbTo)) continue;
                for (int s = 0; s <= steps; s++)
                {
                    float tA = Mathf.Lerp(l.tStart, l.tEnd, s / (float)steps);
                    l.from.EvaluateWorld(tA, out var pa, out _, out _);
                    SplineUtility.GetNearestPoint(nbTo, (float3)pa, out float3 pb, out _);
                    _gizmoSegments.Add((pa, (Vector3)pb, l.side));
                }
            }
        }

        // Group consecutive same-neighbour samples into regions. Single-sample
        // blips are dropped as noise (need at least two samples in a row).
        void BuildSpans(Lane from, int n, Lane[] perSample, int side)
        {
            int i = 0;
            while (i <= n)
            {
                Lane cur = perSample[i];
                if (cur == null) { i++; continue; }

                int start = i;
                while (i <= n && perSample[i] == cur) i++;
                int end = i - 1;

                if (end > start)
                    _links.Add(new Link
                    {
                        from = from, to = cur, side = side,
                        tStart = start / (float)n, tEnd = end / (float)n
                    });
            }
        }

        void OnDrawGizmos()
        {
            foreach (var seg in _gizmoSegments)
            {
                Gizmos.color = seg.side < 0 ? leftColor : rightColor;
                Gizmos.DrawLine(seg.a, seg.b);
            }
        }
    }
}
