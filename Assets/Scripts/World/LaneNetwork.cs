// Discovers which lanes are navigable neighbours, automatically.
//
// For each lane it walks the spline; at each step it finds the nearest other
// lane that is (a) within a lateral band and (b) heading the same way. Runs of
// consecutive steps with the same neighbour become an adjacency "region" with a
// start/end along the lane — so adjacency is regional, not global.
//
// Bake is deterministic: same geometry -> same links. Gizmos draw every link so
// you can verify before pressing Play. Re-bakes live in the editor; bakes once
// at runtime. Results aren't serialized (recomputed) — fine while the map's
// small; we serialize later for a big world.

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

        void OnEnable() => Bake();
        void Start()    => Bake();   // runtime safety: all lanes exist by now

        void Update()
        {
#if UNITY_EDITOR
            if (!Application.isPlaying && autoRebakeInEditor) Bake();
#endif
        }

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

            var lanes = FindObjectsByType<Lane>(FindObjectsSortMode.None);
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
            }
            finally
            {
                foreach (var kv in natives) kv.Value.Dispose();
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
            const int steps = 10;
            foreach (var l in _links)
            {
                if (l.from == null || l.to == null) continue;
                Gizmos.color = l.side < 0 ? leftColor : rightColor;
                for (int s = 0; s <= steps; s++)
                {
                    float tA = Mathf.Lerp(l.tStart, l.tEnd, s / (float)steps);
                    l.from.EvaluateWorld(tA, out var pa, out _, out _);
                    l.to.ProjectWorldPoint(pa, out var pb);
                    Gizmos.DrawLine(pa, pb);
                }
            }
        }
    }
}
