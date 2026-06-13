// Director-driven carriage traffic. Instead of a fixed-rate "pick a random side and
// maybe bail" loop (which thinned out on single-lane stretches and near posts), a
// DIRECTOR schedules the next carriage for some random time N..M from now. When it
// fires it looks at the lanes the rider can currently be on (current + reachable
// same-direction neighbours), drops any lane with a VISIBLE trading post ahead, and
// spawns one carriage a set distance ahead on a random remaining lane. If no lane is
// suitable it just skips and reschedules — so cadence stays even regardless of lane
// length or straights, and the road near a post stays clear.
//
// Carriages are culled once they fall behind the camera. Visual: a carriage sprite
// (cols x rows sheet) — random colour row, side-based column; capsule fallback.

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

        [Header("Director")]
        [Min(0)] public int maxCarriages = 8;
        [Min(0.05f), Tooltip("Shortest wait before the next carriage (seconds).")]
        public float minInterval = 1.5f;
        [Min(0.05f), Tooltip("Longest wait before the next carriage (seconds).")]
        public float maxInterval = 3.5f;
        [Min(0), Tooltip("Carriages to pre-seed across the road ahead at start (avoids a barren opening).")]
        public int seedCount = 4;
        [Tooltip("Log each spawn attempt and why it did/didn't produce a carriage (to debug long gaps).")]
        public bool logSpawns = false;

        [Header("Placement")]
        [Min(1f), Tooltip("How far ahead of the rider (along the lane) to spawn.")]
        public float spawnAhead = 50f;
        [Min(0f), Tooltip("Random +/- variation on the spawn distance, so they don't all appear at the same range.")]
        public float spawnAheadJitter = 12f;
        [Min(0f), Tooltip("Distance behind the camera at which to cull a carriage.")]
        public float despawnBehind = 25f;
        [Min(0f), Tooltip("Don't spawn within this distance of existing traffic.")]
        public float occupyClearance = 6f;
        [Min(0f), Tooltip("A lane is skipped while a trading post is within this distance ahead on it (keeps post approaches clear). 0 = off.")]
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
        readonly List<(Lane lane, float baseT)> _cands = new();
        float _nextSpawn;
        bool _seeded;
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
            _seeded = false;
            ScheduleNext();
        }

        void Update()
        {
            if (rider == null) return;

            Despawn();

            if (!_seeded && rider.lane != null && rider.lane.IsValid)
            {
                _seeded = true;
                Seed();
            }

            if (Time.time >= _nextSpawn)
            {
                ScheduleNext();
                if (_spawned.Count >= maxCarriages) Log($"skip: at cap ({_spawned.Count}/{maxCarriages})");
                else TrySpawnOne();
            }
        }

        void ScheduleNext() => _nextSpawn = Time.time + Random.Range(minInterval, Mathf.Max(minInterval, maxInterval));

        void Log(string msg) { if (logSpawns) Debug.Log($"[Carriages] {msg}", this); }

        void Despawn()
        {
            if (view == null) return;
            Vector3 camPos = view.position, camFwd = view.forward;
            for (int i = _spawned.Count - 1; i >= 0; i--)
            {
                var c = _spawned[i];
                if (c == null) { _spawned.RemoveAt(i); continue; }
                float ahead = Vector3.Dot(c.transform.position - camPos, camFwd);
                if (ahead < -despawnBehind)
                {
                    Log($"despawned: {ahead:F0}m along cam-fwd (cull < {-despawnBehind})");
                    _spawned.RemoveAt(i);
                    Destroy(c.gameObject);
                }
            }
        }

        // The lanes the rider can currently be on (current + reachable neighbours),
        // minus any with a visible post ahead. Each candidate carries the lane's t at
        // the rider's position, so spawnAhead is measured from there.
        void GatherCandidates()
        {
            _cands.Clear();
            var here = rider.lane;
            if (here == null || !here.IsValid) { Log("skip: rider has no valid lane"); return; }

            if (PostAhead(here, rider.t)) Log($"  current '{here.name}': post ahead -> excluded");
            else _cands.Add((here, rider.t));

            here.EvaluateWorld(rider.t, out var rpos, out _, out _);
            for (int side = -1; side <= 1; side += 2)
            {
                if (network == null || !network.TryGetNeighbor(here, rider.t, side, out var nb) || nb == null || !nb.IsValid)
                { Log($"  side {side}: no neighbour"); continue; }
                float baseT = nb.ProjectWorldPoint(rpos, out _);
                if (PostAhead(nb, baseT)) Log($"  side {side} '{nb.name}': post ahead -> excluded");
                else _cands.Add((nb, baseT));
            }
        }

        // One carriage on a random suitable lane, a (jittered) distance ahead. Tries
        // the candidates in random order and takes the first clear spot; skips if none.
        void TrySpawnOne()
        {
            GatherCandidates();
            if (_cands.Count == 0) { Log("skip: no suitable lane (current + neighbours excluded/absent)"); return; }

            int start = Random.Range(0, _cands.Count);
            for (int k = 0; k < _cands.Count; k++)
            {
                var (lane, _) = _cands[(start + k) % _cands.Count];
                float ahead = spawnAhead + Random.Range(-spawnAheadJitter, spawnAheadJitter);
                if (TrySpawnAhead(lane, ahead))
                {
                    float fwd = view != null ? Vector3.Dot(_spawned[_spawned.Count - 1].transform.position - view.position, view.forward) : 0f;
                    Log($"spawned on '{lane.name}' (+{ahead:F0}m along lane), but {fwd:F0}m along cam-fwd, now {_spawned.Count}/{maxCarriages}");
                    return;
                }
            }
            Log("skip: all candidate spots occupied / off-lane");
        }

        // Pre-seed the visible road so the opening isn't empty: a spread of carriages
        // from near the rider out to spawnAhead, on whatever lanes are suitable.
        void Seed()
        {
            for (int i = 0; i < seedCount && _spawned.Count < maxCarriages; i++)
            {
                GatherCandidates();
                if (_cands.Count == 0) break;
                var (lane, _) = _cands[Random.Range(0, _cands.Count)];
                float ahead = Mathf.Lerp(spawnAhead * 0.25f, spawnAhead, (i + 0.5f) / Mathf.Max(1, seedCount));
                TrySpawnAhead(lane, ahead + Random.Range(-spawnAheadJitter, spawnAheadJitter));
            }
        }

        // Place a point `ahead` metres in front of the rider (world space) and snap it
        // onto the lane. Robust to spline t-parameterisation/direction — the spawn is
        // always genuinely ahead in view, never behind (which the lane-t maths could do
        // on bends). Skips if the snapped point isn't actually ahead (loops/sharp curves).
        bool TrySpawnAhead(Lane lane, float ahead)
        {
            if (rider == null || lane.Length < 0.0001f) return false;

            Vector3 from = rider.transform.position;
            Vector3 fwd  = rider.transform.forward;
            float t = lane.ProjectWorldPoint(from + fwd * ahead, out var nearest);

            float realAhead = Vector3.Dot(nearest - from, fwd);
            if (realAhead < ahead * 0.25f)
            {
                Log($"  '{lane.name}': snapped point only {realAhead:F0}m ahead (wanted ~{ahead:F0}) -> skip");
                return false;
            }
            if (Carriage.Occupied(lane, t, occupyClearance))
            {
                Log($"  '{lane.name}' t={t:F2}: occupied (within {occupyClearance}m)");
                return false;
            }

            Spawn(lane, t);
            return true;
        }

        // True if a trading post is within postApproachClear ahead on this lane (so we
        // keep the whole lane clear until the rider passes it).
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

            // Place it on the lane NOW. Otherwise the GameObject stays at the world
            // origin until Carriage.Update() runs (a frame later), and Despawn() —
            // which runs at the top of our Update — would cull it for being "behind"
            // the camera (origin reads as hundreds of metres back once the rider has
            // driven away from it). That was the cause of the long empty stretches.
            car.PlaceOnLane();

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
