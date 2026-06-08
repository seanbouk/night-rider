// Discovers which lanes are navigable neighbours, automatically.
//
// Each lane is pre-sampled into a polyline; all sample points go into a 2D
// spatial grid (cell = maxNeighbourDistance). For each sample we look only at
// the 9 surrounding cells for the nearest same-direction lane within the lateral
// band, per side. Runs of consecutive samples with the same neighbour become an
// adjacency "region" (start/end along the lane).
//
// No analytic nearest-point search, so the bake is ~linear in total samples:
// a full bake is cheap, and it just re-bakes whole on any edit (debounced) and
// once at runtime. Gizmo connector lines are cached at bake time.

using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Splines;

namespace NightRider.World
{
    [ExecuteAlways]
    public class LaneNetwork : MonoBehaviour
    {
        [Header("Sampling")]
        [Min(0.1f), Tooltip("World units between samples along each lane.")]
        public float sampleSpacing = 2f;

        [Header("Neighbour test")]
        [Min(0f), Tooltip("Ignore lanes closer than this (avoids self-ish overlaps).")]
        public float minNeighbourDistance = 0.5f;
        [Min(0.1f), Tooltip("Ignore lanes farther than this to the side. ~1.5x lane spacing.")]
        public float maxNeighbourDistance = 6f;
        [Range(-1f, 1f), Tooltip("Min tangent dot for 'same direction'. 1=identical, 0=perpendicular.")]
        public float headingDotThreshold = 0.5f;

        [Header("Editor")]
        public bool autoRebakeInEditor = true;
        [Tooltip("Log each full bake's duration to the console.")]
        public bool logTimings = false;
        public Color leftColor  = new(1f, 0.35f, 0.35f);
        public Color rightColor = new(0.35f, 1f, 0.45f);

        public struct Link
        {
            public Lane from, to;
            public int side;          // -1 = neighbour on the left, +1 = on the right
            public float tStart, tEnd;
        }

        readonly List<Link> _links = new();
        readonly List<(Vector3 a, Vector3 b, int side)> _gizmoSegments = new();

        struct LaneSamples { public Lane lane; public int n; public Vector3[] pos, fwd, right; }

        // ---------------------------------------------------------------- lifecycle

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

        // ---------------------------------------------------------------- query

        public bool TryGetNeighbor(Lane lane, float t, int side, out Lane neighbor)
        {
            foreach (var l in _links)
                if (l.from == lane && l.side == side && t >= l.tStart && t <= l.tEnd) { neighbor = l.to; return true; }
            neighbor = null;
            return false;
        }

        [ContextMenu("Bake Now")]
        public void BakeNow() => Bake();

        // ---------------------------------------------------------------- bake

        public void Bake()
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();

            _links.Clear();
            _gizmoSegments.Clear();

            var lanes = new List<Lane>();
            foreach (var l in FindObjectsByType<Lane>())
                if (l != null && l.IsValid && !l.excludeFromAutoLink) lanes.Add(l);

            if (lanes.Count >= 2)
            {
                // 1) Pre-sample each lane into a flat polyline (pos, flat forward, right).
                var ls = new LaneSamples[lanes.Count];
                for (int li = 0; li < lanes.Count; li++)
                {
                    var lane = lanes[li];
                    int n = Mathf.Max(2, Mathf.CeilToInt(lane.Length / sampleSpacing));
                    var pos = new Vector3[n + 1];
                    var fwd = new Vector3[n + 1];
                    var right = new Vector3[n + 1];
                    for (int i = 0; i <= n; i++)
                    {
                        lane.EvaluateWorld(i / (float)n, out var p, out var f, out _);
                        Vector3 flat = Vector3.ProjectOnPlane(f, Vector3.up);
                        flat = flat.sqrMagnitude > 1e-6f ? flat.normalized : Vector3.forward;
                        pos[i] = p;
                        fwd[i] = flat;
                        right[i] = Vector3.Cross(Vector3.up, flat);   // world up: flat-world, flip-robust
                    }
                    ls[li] = new LaneSamples { lane = lane, n = n, pos = pos, fwd = fwd, right = right };
                }

                // 2) Spatial grid of every sample point (cell = maxNeighbourDistance).
                float cell = Mathf.Max(0.1f, maxNeighbourDistance);
                var grid = new Dictionary<(int, int), List<(int li, int si)>>();
                for (int li = 0; li < ls.Length; li++)
                    for (int si = 0; si <= ls[li].n; si++)
                    {
                        var key = Cell(ls[li].pos[si], cell);
                        if (!grid.TryGetValue(key, out var list)) grid[key] = list = new List<(int, int)>();
                        list.Add((li, si));
                    }

                // 3) For each sample, nearest same-direction neighbour per side, via the grid.
                float minD = minNeighbourDistance, maxD = maxNeighbourDistance;
                for (int li = 0; li < ls.Length; li++)
                {
                    var A = ls[li];
                    var leftLane  = new Lane[A.n + 1];
                    var rightLane = new Lane[A.n + 1];
                    var leftPos   = new Vector3[A.n + 1];
                    var rightPos  = new Vector3[A.n + 1];

                    for (int i = 0; i <= A.n; i++)
                    {
                        Vector3 pA = A.pos[i], fA = A.fwd[i], rA = A.right[i];
                        (int cx, int cz) = Cell(pA, cell);
                        float bestL = maxD, bestR = maxD;

                        for (int dx = -1; dx <= 1; dx++)
                        for (int dz = -1; dz <= 1; dz++)
                        {
                            if (!grid.TryGetValue((cx + dx, cz + dz), out var bucket)) continue;
                            foreach (var (lj, sj) in bucket)
                            {
                                if (lj == li) continue;
                                Vector3 pB = ls[lj].pos[sj];
                                float d = Vector3.Distance(pA, pB);
                                if (d < minD || d > maxD) continue;
                                if (Vector3.Dot(fA, ls[lj].fwd[sj]) < headingDotThreshold) continue;   // not same direction

                                if (Vector3.Dot(pB - pA, rA) >= 0f)
                                {
                                    if (d < bestR) { bestR = d; rightLane[i] = ls[lj].lane; rightPos[i] = pB; }
                                }
                                else
                                {
                                    if (d < bestL) { bestL = d; leftLane[i] = ls[lj].lane; leftPos[i] = pB; }
                                }
                            }
                        }
                    }

                    BuildSpans(A, leftLane, leftPos, -1);
                    BuildSpans(A, rightLane, rightPos, +1);
                }
            }

#if UNITY_EDITOR
            _dirty = false;
#endif
            if (logTimings)
                Debug.Log($"[LaneNetwork] Full bake: {lanes.Count} lanes, {_links.Count} links, {sw.Elapsed.TotalMilliseconds:F2} ms.", this);
        }

        static (int, int) Cell(Vector3 p, float cell) =>
            (Mathf.FloorToInt(p.x / cell), Mathf.FloorToInt(p.z / cell));

        // Group consecutive same-neighbour samples into regions (>= 2 samples).
        void BuildSpans(LaneSamples a, Lane[] perSample, Vector3[] bPos, int side)
        {
            int n = a.n, i = 0;
            while (i <= n)
            {
                Lane cur = perSample[i];
                if (cur == null) { i++; continue; }

                int start = i;
                while (i <= n && perSample[i] == cur) i++;
                int end = i - 1;
                if (end <= start) continue;

                _links.Add(new Link { from = a.lane, to = cur, side = side, tStart = start / (float)n, tEnd = end / (float)n });

                int step = Mathf.Max(1, (end - start) / 10);   // ~10 connector lines per span
                for (int s = start; s <= end; s += step)
                    _gizmoSegments.Add((a.pos[s], bPos[s], side));
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

        // ---------------------------------------------------------------- editor re-bake

#if UNITY_EDITOR
        bool _dirty;
        double _lastEdit;
        int _knownCount = -1;
        const double Debounce = 0.12;

        void OnSplineChanged(Spline s, int knot, SplineModification mod)
        {
            _dirty = true;
            _lastEdit = UnityEditor.EditorApplication.timeSinceStartup;
        }

        void Update()
        {
            if (Application.isPlaying || !autoRebakeInEditor) return;

            var lanes = FindObjectsByType<Lane>();
            if (lanes.Length != _knownCount) { _knownCount = lanes.Length; _dirty = true; _lastEdit = UnityEditor.EditorApplication.timeSinceStartup; }
            foreach (var l in lanes)
                if (l != null && l.transform.hasChanged) { l.transform.hasChanged = false; _dirty = true; _lastEdit = UnityEditor.EditorApplication.timeSinceStartup; }

            if (_dirty && GUIUtility.hotControl == 0 && !UnityEditor.EditorGUIUtility.editingTextField
                && UnityEditor.EditorApplication.timeSinceStartup - _lastEdit > Debounce)
                Bake();
        }
#endif
    }
}
