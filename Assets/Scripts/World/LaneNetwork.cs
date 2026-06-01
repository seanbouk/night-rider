// Discovers which lanes are navigable neighbours, automatically.
//
// For each lane it walks the spline; at each step it finds the nearest other
// lane that is (a) within a lateral band and (b) heading the same way. Runs of
// consecutive steps with the same neighbour become an adjacency "region" with a
// start/end along the lane — so adjacency is regional, not global.
//
// Scaling strategy (so authoring stays fast as the map grows to dozens of lanes):
//  - Links/gizmos are keyed per from-lane, so any one lane can be recomputed
//    on its own.
//  - Each lane caches a world bounding box; the per-sample neighbour search is
//    broad-phase culled to lanes whose boxes are within maxNeighbourDistance
//    (turns the cost from ~lanes^2 toward lanes x local-neighbours).
//  - In the editor an edit recomputes ONLY the changed lane plus the lanes whose
//    boxes overlap its old/new footprint — so a single edit's cost is bounded by
//    local density, not total map size. Debounced so a drag updates once.
// Runtime bakes once. Set logTimings to print bake/update durations.

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
        [Tooltip("Log bake / incremental-update durations to the console.")]
        public bool logTimings = false;
        public Color leftColor  = new(1f, 0.35f, 0.35f);
        public Color rightColor = new(0.35f, 1f, 0.45f);

        public struct Link
        {
            public Lane from, to;
            public int side;          // -1 = neighbour on the left, +1 = on the right
            public float tStart, tEnd;
        }

        // Keyed by from-lane so a single lane can be recomputed in isolation.
        readonly Dictionary<Lane, List<Link>> _linksByLane = new();
        readonly Dictionary<Lane, List<(Vector3 a, Vector3 b, int side)>> _gizmoByLane = new();
        readonly Dictionary<Lane, Bounds> _bounds = new();

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

        void Start() => Bake();   // runtime: build once, all lanes present

        // ---------------------------------------------------------------- query

        /// Neighbour on the given side (-1 left / +1 right) at position t, if any.
        public bool TryGetNeighbor(Lane lane, float t, int side, out Lane neighbor)
        {
            neighbor = null;
            if (lane == null || !_linksByLane.TryGetValue(lane, out var links)) return false;
            foreach (var l in links)
                if (l.side == side && t >= l.tStart && t <= l.tEnd) { neighbor = l.to; return true; }
            return false;
        }

        [ContextMenu("Bake Now")]
        public void BakeNow()
        {
            bool prev = logTimings;
            logTimings = true;
            Bake();
            logTimings = prev;
        }

        // ---------------------------------------------------------------- full bake

        List<Lane> CollectLanes()
        {
            var list = new List<Lane>();
            foreach (var l in FindObjectsByType<Lane>())
                if (l != null && l.IsValid && !l.excludeFromAutoLink) list.Add(l);
            return list;
        }

        public void Bake()
        {
            var sw = logTimings ? System.Diagnostics.Stopwatch.StartNew() : null;

            _linksByLane.Clear();
            _gizmoByLane.Clear();
            _bounds.Clear();

            var lanes = CollectLanes();
            if (lanes.Count >= 2)
            {
                foreach (var l in lanes) _bounds[l] = ComputeWorldBounds(l);

                var natives = BuildNatives(lanes);
                try { foreach (var a in lanes) RecomputeLane(a, lanes, natives); }
                finally { DisposeNatives(natives); }
            }

#if UNITY_EDITOR
            _known.Clear();
            foreach (var l in lanes) _known.Add(l);
            _dirty.Clear();
#endif
            if (sw != null)
                Debug.Log($"[LaneNetwork] Full bake: {lanes.Count} lanes, {CountLinks()} links, {sw.Elapsed.TotalMilliseconds:F2} ms.", this);
        }

        int CountLinks()
        {
            int c = 0;
            foreach (var kv in _linksByLane) c += kv.Value.Count;
            return c;
        }

        // Recompute one lane's outgoing links + gizmo lines. `natives` must contain
        // this lane and every lane whose box is within maxNeighbourDistance of it.
        void RecomputeLane(Lane a, List<Lane> allLanes, Dictionary<Lane, NativeSpline> natives)
        {
            _linksByLane.Remove(a);
            _gizmoByLane.Remove(a);
            if (a == null || !a.IsValid || a.excludeFromAutoLink) return;

            if (!_bounds.TryGetValue(a, out var ab)) { ab = ComputeWorldBounds(a); _bounds[a] = ab; }
            Bounds search = ab;
            search.Expand(2f * maxNeighbourDistance);   // +maxNeighbourDistance each side

            int n = Mathf.Max(2, Mathf.CeilToInt(a.Length / sampleSpacing));
            var leftAt  = new Lane[n + 1];
            var rightAt = new Lane[n + 1];

            for (int i = 0; i <= n; i++)
            {
                float tA = i / (float)n;
                a.EvaluateWorld(tA, out var posA, out var fwdA, out _);
                // World up, not the spline's up: flat world, and a mirrored/reversed
                // track's up flips (would swap L/R).
                Vector3 rightDir = Vector3.Cross(Vector3.up, fwdA).normalized;

                float bestLeft = float.MaxValue, bestRight = float.MaxValue;

                foreach (var b in allLanes)
                {
                    if (b == a || b == null || !b.IsValid || b.excludeFromAutoLink) continue;
                    if (!_bounds.TryGetValue(b, out var bb) || !search.Intersects(bb)) continue; // broad-phase cull
                    if (!natives.TryGetValue(b, out var nb)) continue;

                    float d = SplineUtility.GetNearestPoint(nb, (float3)posA, out float3 nearest, out float tB);
                    if (d < minNeighbourDistance || d > maxNeighbourDistance) continue;

                    Vector3 fwdB = ((Vector3)SplineUtility.EvaluateTangent(nb, tB)).normalized;
                    if (Vector3.Dot(fwdA, fwdB) < headingDotThreshold) continue; // not same direction

                    Vector3 offset = (Vector3)nearest - posA;
                    if (Vector3.Dot(offset, rightDir) >= 0f)
                    {
                        if (d < bestRight) { bestRight = d; rightAt[i] = b; }
                    }
                    else
                    {
                        if (d < bestLeft) { bestLeft = d; leftAt[i] = b; }
                    }
                }
            }

            var links = new List<Link>();
            BuildSpansInto(links, a, n, leftAt, -1);
            BuildSpansInto(links, a, n, rightAt, +1);
            if (links.Count == 0) return;

            _linksByLane[a] = links;

            const int steps = 10;
            var segs = new List<(Vector3, Vector3, int)>();
            foreach (var l in links)
            {
                if (!natives.TryGetValue(l.to, out var nbTo)) continue;
                for (int s = 0; s <= steps; s++)
                {
                    float tA = Mathf.Lerp(l.tStart, l.tEnd, s / (float)steps);
                    a.EvaluateWorld(tA, out var pa, out _, out _);
                    SplineUtility.GetNearestPoint(nbTo, (float3)pa, out float3 pb, out _);
                    segs.Add((pa, (Vector3)pb, l.side));
                }
            }
            _gizmoByLane[a] = segs;
        }

        // Group consecutive same-neighbour samples into regions. Single-sample
        // blips are dropped as noise (need at least two samples in a row).
        void BuildSpansInto(List<Link> into, Lane from, int n, Lane[] perSample, int side)
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
                    into.Add(new Link
                    {
                        from = from, to = cur, side = side,
                        tStart = start / (float)n, tEnd = end / (float)n
                    });
            }
        }

        // World-space AABB of the lane's spline (SplineUtility extension that
        // takes a transform and bounds the transformed control points).
        Bounds ComputeWorldBounds(Lane lane)
            => lane.Container.Spline.GetBounds(lane.transform.localToWorldMatrix);

        Dictionary<Lane, NativeSpline> BuildNatives(IEnumerable<Lane> set)
        {
            var d = new Dictionary<Lane, NativeSpline>();
            foreach (var l in set)
            {
                if (l == null || !l.IsValid || d.ContainsKey(l)) continue;
                d[l] = new NativeSpline(l.Container.Spline, l.transform.localToWorldMatrix, Allocator.Temp);
            }
            return d;
        }

        void DisposeNatives(Dictionary<Lane, NativeSpline> d)
        {
            foreach (var kv in d) kv.Value.Dispose();
        }

        void OnDrawGizmos()
        {
            foreach (var kv in _gizmoByLane)
                foreach (var seg in kv.Value)
                {
                    Gizmos.color = seg.side < 0 ? leftColor : rightColor;
                    Gizmos.DrawLine(seg.a, seg.b);
                }
        }

        // ---------------------------------------------------------------- editor: incremental

#if UNITY_EDITOR
        readonly HashSet<Lane> _known = new();
        readonly HashSet<Lane> _dirty = new();
        double _lastEditTime;
        const double RebakeDebounce = 0.12;

        void OnSplineChanged(Spline s, int knot, SplineModification mod)
        {
            foreach (var l in _known)
                if (l != null && l.IsValid && l.Container.Spline == s) { _dirty.Add(l); break; }
            _lastEditTime = UnityEditor.EditorApplication.timeSinceStartup;
        }

        void Update()
        {
            if (Application.isPlaying || !autoRebakeInEditor) return;

            var lanes = FindObjectsByType<Lane>();
            var currentSet = new HashSet<Lane>();
            bool changed = false;

            foreach (var l in lanes)
            {
                if (l == null) continue;
                currentSet.Add(l);
                if (!_known.Contains(l)) { _dirty.Add(l); changed = true; }
                if (l.transform.hasChanged) { l.transform.hasChanged = false; _dirty.Add(l); changed = true; }
            }

            bool removals = false;
            foreach (var k in _known) if (!currentSet.Contains(k)) { removals = true; break; }
            if (removals) changed = true;

            if (changed) _lastEditTime = UnityEditor.EditorApplication.timeSinceStartup;

            if ((_dirty.Count > 0 || removals) &&
                UnityEditor.EditorApplication.timeSinceStartup - _lastEditTime > RebakeDebounce)
            {
                UpdateIncremental(lanes, currentSet);
            }
        }

        void UpdateIncremental(Lane[] lanesArr, HashSet<Lane> currentSet)
        {
            var sw = logTimings ? System.Diagnostics.Stopwatch.StartNew() : null;

            var current = new List<Lane>();
            foreach (var l in lanesArr)
                if (l != null && l.IsValid && !l.excludeFromAutoLink) current.Add(l);

            // Footprints (expanded) of everything that changed: removed lanes'
            // old boxes, and dirty lanes' old + new boxes.
            var regions = new List<Bounds>();

            foreach (var k in _known)
            {
                if (currentSet.Contains(k)) continue;             // still here
                if (_bounds.TryGetValue(k, out var ob)) { var e = ob; e.Expand(2f * maxNeighbourDistance); regions.Add(e); }
                _bounds.Remove(k);
                _linksByLane.Remove(k);
                _gizmoByLane.Remove(k);
            }

            foreach (var d in _dirty)
            {
                if (d == null || !currentSet.Contains(d)) continue;
                if (_bounds.TryGetValue(d, out var ob)) { var e = ob; e.Expand(2f * maxNeighbourDistance); regions.Add(e); }
                var nb = ComputeWorldBounds(d);
                _bounds[d] = nb;
                var en = nb; en.Expand(2f * maxNeighbourDistance); regions.Add(en);
            }

            foreach (var l in current)
                if (!_bounds.ContainsKey(l)) _bounds[l] = ComputeWorldBounds(l);

            // Affected = dirty lanes + any lane whose box overlaps a changed region.
            var affected = new HashSet<Lane>();
            foreach (var d in _dirty) if (d != null && currentSet.Contains(d)) affected.Add(d);
            foreach (var l in current)
            {
                if (affected.Contains(l)) continue;
                var lb = _bounds[l];
                foreach (var reg in regions) if (reg.Intersects(lb)) { affected.Add(l); break; }
            }

            if (affected.Count > 0)
            {
                // Natives for the affected lanes and any lane they might project onto.
                var need = new HashSet<Lane>(affected);
                foreach (var a in affected)
                {
                    var sb = _bounds[a]; sb.Expand(2f * maxNeighbourDistance);
                    foreach (var b in current)
                        if (b != a && sb.Intersects(_bounds[b])) need.Add(b);
                }

                var natives = BuildNatives(need);
                try { foreach (var a in affected) RecomputeLane(a, current, natives); }
                finally { DisposeNatives(natives); }
            }

            _known.Clear();
            foreach (var l in currentSet) _known.Add(l);
            _dirty.Clear();

            if (sw != null)
                Debug.Log($"[LaneNetwork] Incremental: {affected.Count} lane(s) recomputed, {CountLinks()} links, {sw.Elapsed.TotalMilliseconds:F2} ms.", this);
        }
#endif
    }
}
