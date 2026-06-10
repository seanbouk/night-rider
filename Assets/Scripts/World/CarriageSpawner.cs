// Spawns carriages ahead of the rider in its current and adjacent lanes, and
// culls them once they fall behind the camera. Spawning is rider-relative (a
// fixed distance ahead on a chosen lane), so density stays steady on long lanes
// and we never spawn oncoming traffic (adjacency is same-direction only).
//
// Visual: a carriage sprite (cols x rows sheet) — random colour row, side-based
// column. Falls back to a capsule if no sheet is assigned.

using System.Collections.Generic;
using UnityEngine;
using NightRider.View;

namespace NightRider.World
{
    public class CarriageSpawner : MonoBehaviour
    {
        [Header("Refs")]
        public LaneFollower rider;
        public LaneNetwork network;
        [Tooltip("Viewpoint for despawn-behind. Defaults to main camera.")]
        public Transform view;

        [Header("Population")]
        [Min(0)] public int maxCarriages = 8;
        [Min(1f), Tooltip("How far ahead of the rider (along the lane) to spawn.")]
        public float spawnAhead = 50f;
        [Min(0f), Tooltip("Distance behind the camera at which to cull a carriage.")]
        public float despawnBehind = 25f;
        [Min(0.05f), Tooltip("Seconds between spawn attempts.")]
        public float spawnInterval = 0.4f;
        [Min(0f), Tooltip("Don't spawn within this distance of existing traffic.")]
        public float occupyClearance = 6f;
        [Min(0f), Tooltip("Keep trading-post approaches clear: while a post is within this distance ahead on a lane, spawn NOTHING on that lane (whole lane, not just the gap) until the rider passes it. 0 = off.")]
        public float postApproachClear = 100f;

        [Header("Carriage")]
        [Range(0f, 1f)] public float speedFraction = 0.5f;
        [Min(0f)] public float acceleration = 8f;
        [Min(0f)] public float wreckBrake = 40f;
        public float heightOffset = 0.4f;

        [Header("Sprite")]
        public Texture2D carriageSheet;
        [Tooltip("Shared NES sprite material (NightRider/NesSprite). Falls back to a runtime instance if unset.")]
        public Material nesMaterial;
        public int sheetCols = 3, sheetRows = 4;
        public float pixelsPerUnit = 100f;
        public Vector2 pivot = new(0.5f, 0.5f);
        [Tooltip("View-angle (degrees) past which a carriage shows its left/right view.")]
        public float viewAngleThreshold = 18f;

        [Header("NES palette — one per carriage row/type (top to bottom)")]
        [Tooltip("Per type: three hex RRGGBB (snapped to NES, + transparent) and a vividness bias. " +
                 "Defaults: grey + brown + (yellow / green / blue / red) by row.")]
        public CarriagePalette[] carriagePalettes =
        {
            new() { c2 = "d9bf33" },   // row 0 accent: yellow
            new() { c2 = "40b333" },   // row 1 accent: green
            new() { c2 = "3359d9" },   // row 2 accent: blue
            new() { c2 = "cc2e26" },   // row 3 accent: red
        };

        readonly List<Carriage> _spawned = new();
        float _timer;
        Material _nesMat;   // shared NesSprite material; per-row colours come from a property block

        Material NesMat()
        {
            if (nesMaterial != null) return nesMaterial;
            if (_nesMat == null)
            {
                var sh = Shader.Find("NightRider/NesSprite");
                if (sh != null) _nesMat = new Material(sh);
            }
            return _nesMat;
        }

        void OnEnable()
        {
            if (view == null && Camera.main != null) view = Camera.main.transform;
            if (network == null) network = FindAnyObjectByType<LaneNetwork>();
        }

        void Update()
        {
            if (rider == null) return;

            Despawn();

            _timer += Time.deltaTime;
            if (_timer >= spawnInterval && _spawned.Count < maxCarriages)
            {
                _timer = 0f;
                TrySpawn();
            }
        }

        void Despawn()
        {
            if (view == null) return;
            Vector3 camPos = view.position, camFwd = view.forward;
            for (int i = _spawned.Count - 1; i >= 0; i--)
            {
                var c = _spawned[i];
                if (c == null) { _spawned.RemoveAt(i); continue; }
                if (Vector3.Dot(c.transform.position - camPos, camFwd) < -despawnBehind)
                {
                    _spawned.RemoveAt(i);
                    Destroy(c.gameObject);
                }
            }
        }

        void TrySpawn()
        {
            var here = rider.lane;
            if (here == null || !here.IsValid) return;

            // Pick the current lane (0) or a neighbour (-1 / +1).
            int side = Random.Range(0, 3) - 1;
            Lane lane;
            float baseT;
            if (side == 0)
            {
                lane = here;
                baseT = rider.t;
            }
            else
            {
                if (network == null || !network.TryGetNeighbor(here, rider.t, side, out lane) || lane == null || !lane.IsValid)
                    return;
                here.EvaluateWorld(rider.t, out var rpos, out _, out _);
                baseT = lane.ProjectWorldPoint(rpos, out _);
            }

            float len = lane.Length;
            if (len < 0.0001f) return;

            float t = baseT + spawnAhead / len;
            if (lane.Closed) t -= Mathf.Floor(t);
            else if (t > 1f) return;

            if (Carriage.Occupied(lane, t, occupyClearance)) return;
            if (PostAhead(lane, baseT)) return;   // a post is coming up on this lane — keep it empty

            Spawn(lane, t);
        }

        // True if a trading post is coming up on this lane: within postApproachClear
        // ahead of the rider and not yet passed. While so, we spawn NOTHING on the
        // lane (the whole lane, not just the gap before the post), so the approach
        // stays clutter-free; once the rider passes the post it wraps behind and
        // spawning resumes.
        bool PostAhead(Lane lane, float baseT)
        {
            if (postApproachClear <= 0f) return false;
            float len = lane.Length;
            foreach (var post in TradingPost.All)
            {
                if (post == null || post.lane != lane) continue;
                float postDist = Forward(baseT, post.t, lane.Closed) * len;
                if (postDist > 0f && postDist <= postApproachClear) return true;
            }
            return false;
        }

        // Forward arc fraction from a to b along the lane (wraps for closed loops).
        static float Forward(float a, float b, bool closed)
        {
            float d = b - a;
            if (closed) d -= Mathf.Floor(d);   // into [0,1)
            return d;
        }

        void Spawn(Lane lane, float t)
        {
            var go = new GameObject("Carriage");

            var car = go.AddComponent<Carriage>();
            car.lane = lane;
            car.t = t;
            car.idealSpeed = (rider != null ? rider.speed : 12f) * speedFraction;
            car.acceleration = acceleration;
            car.wreckBrake = wreckBrake;
            car.heightOffset = heightOffset;

            var vis = new GameObject("Visual");
            vis.transform.SetParent(go.transform, false);

            if (carriageSheet != null)
            {
                vis.AddComponent<SpriteRenderer>();
                var cs = vis.AddComponent<CarriageSprite>();
                cs.sheet = carriageSheet;
                cs.cols = sheetCols;
                cs.rows = sheetRows;
                cs.pixelsPerUnit = pixelsPerUnit;
                cs.pivot = pivot;
                cs.viewAngleThreshold = viewAngleThreshold;
                cs.material = NesMat();
                cs.palettes = carriagePalettes;
            }
            else
            {
                var cap = GameObject.CreatePrimitive(PrimitiveType.Capsule);
                Destroy(cap.GetComponent<Collider>());
                cap.transform.SetParent(vis.transform, false);
                cap.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
                cap.transform.localScale = new Vector3(0.7f, 0.7f, 1.8f);
            }

            _spawned.Add(car);
        }
    }
}
