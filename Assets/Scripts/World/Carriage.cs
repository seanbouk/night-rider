// A road user (a "carriage", placeholder capsule for now). Rides ONE lane in its
// forward direction at its own speed, accelerating back to idealSpeed after being
// knocked. Moves in real world space — no texture trickery — so its motion
// relative to the rider is plain physics and never slips against the road.
//
// All live carriages register here so the rider can find traffic ahead on a lane
// (to rear-end) and check whether a neighbouring lane is occupied (to block a hop).

using System.Collections.Generic;
using UnityEngine;
using NightRider.View;   // RoadScroll (shared apparent-speed for the road sweep)

namespace NightRider.World
{
    public class Carriage : MonoBehaviour
    {
        public static readonly List<Carriage> All = new();

        public Lane lane;
        [Range(0f, 1f)] public float t;
        [Min(0f)] public float idealSpeed = 6f;
        [Min(0f)] public float acceleration = 8f;
        [Min(0f), Tooltip("Deceleration to a stop once wrecked (units/s^2). Higher = stops sooner.")]
        public float wreckBrake = 40f;
        [Min(0f), Tooltip("Gap at which we rear-end the carriage in front.")]
        public float bumpDistance = 3f;
        [Min(0f), Tooltip("Hard minimum gap — never overlap the carriage in front.")]
        public float minGap = 2f;
        public float heightOffset = 0.4f;

        [Header("Energy")]
        [Range(0f, 1f), Tooltip("Runtime energy. Rider rear-ends reduce it; 0 = wrecked.")]
        public float energy = 1f;
        public Color barBack = new(0f, 0f, 0f, 0.6f);
        public Color barFill = new(0.3f, 1f, 0.4f, 0.9f);

        public float CurrentSpeed { get; private set; }
        public bool IsWreck => _destroyed;
        public bool Collected { get; private set; }   // lupin already granted for this wreck
        public void Collect() => Collected = true;

        Carriage _contact;   // carriage in front we're touching (one-shot bumps)
        bool _destroyed;
        Renderer _renderer;

        void OnEnable()  { if (!All.Contains(this)) All.Add(this); CurrentSpeed = idealSpeed; _renderer = GetComponentInChildren<Renderer>(); }
        void OnDisable() { All.Remove(this); }

        // Rider rear-ended us: lose energy. At zero we become a wreck — coast to a
        // stop (idealSpeed 0), dim to half-bright, and stop colliding (everyone
        // drives through). Collecting it is the rider's job.
        public void Hit(float fraction)
        {
            if (_destroyed) return;
            energy -= fraction;
            if (energy > 0f) return;

            energy = 0f;
            _destroyed = true;
            idealSpeed = 0f;
            if (_renderer != null)
            {
                Color c = _renderer.material.color;
                _renderer.material.color = new Color(c.r * 0.5f, c.g * 0.5f, c.b * 0.5f, c.a);
            }
        }

        // Knocked forward by the rider — jump to (at least) the given speed, then
        // re-accelerate back down to idealSpeed.
        public void Bump(float speed) => CurrentSpeed = Mathf.Max(CurrentSpeed, speed);

        void Update()
        {
            if (lane == null || !lane.IsValid) return;
            float len = lane.Length;
            if (len < 0.0001f) return;

            float dt = Time.deltaTime;
            // Wrecks brake hard to a stop; live carriages ease to cruise.
            float rate = _destroyed ? wreckBrake : acceleration;
            CurrentSpeed = Mathf.MoveTowards(CurrentSpeed, idealSpeed, rate * dt);

            // Rear-end the carriage in front (one-shot knock, like the rider).
            // Wrecks don't interact with traffic at all.
            Carriage ahead = null;
            float gap = float.MaxValue;
            if (!_destroyed) ahead = NearestAhead(lane, t, out gap, this);

            if (ahead != null && gap <= bumpDistance)
            {
                if (_contact != ahead)
                {
                    _contact = ahead;
                    float mine = CurrentSpeed;
                    CurrentSpeed = 0.5f * ahead.CurrentSpeed;
                    ahead.Bump(mine);
                }
            }
            else if (ahead == null || gap > bumpDistance * 1.5f)
            {
                _contact = null;
            }

            // Move WITH the road: add the road-scroll sweep (toward the camera)
            // so we don't slip against the scrolling surface.
            lane.EvaluateWorld(t, out _, out var fwd0, out _);
            Vector3 flat0 = Vector3.ProjectOnPlane(fwd0, Vector3.up);
            float sweep = 0f;
            if (flat0.sqrMagnitude > 1e-6f)
                sweep = -Mathf.Sign(Vector3.Dot(flat0.normalized, RoadScroll.FlowForward)) * RoadScroll.ExtraSpeed;

            t += (CurrentSpeed + sweep) * dt / len;
            if (t >= 1f) { if (lane.Closed) t -= 1f; else t = 1f; }
            if (t <  0f) { if (lane.Closed) t += 1f; else t = 0f; }   // sweep can push backward

            // Hard floor: never tunnel through the carriage in front.
            if (ahead != null)
            {
                float gapT = ahead.t - t;
                if (lane.Closed && gapT < 0f) gapT += 1f;
                float minGapT = minGap / len;
                if (gapT < minGapT)
                {
                    t = ahead.t - minGapT;
                    if (t < 0f) t += 1f;
                    if (CurrentSpeed > ahead.CurrentSpeed) CurrentSpeed = ahead.CurrentSpeed;
                }
            }

            lane.EvaluateWorld(t, out var pos, out var fwd, out _);
            transform.position = pos + Vector3.up * heightOffset;
            Vector3 flat = Vector3.ProjectOnPlane(fwd, Vector3.up);
            if (flat.sqrMagnitude > 1e-6f)
                transform.rotation = Quaternion.LookRotation(flat.normalized, Vector3.up);
        }

        // Nearest carriage AHEAD of position t on the lane (in its travel direction).
        // gapDist is the world distance to it. Null if none.
        public static Carriage NearestAhead(Lane lane, float t, out float gapDist, Carriage ignore = null)
        {
            gapDist = float.MaxValue;
            Carriage nearest = null;
            float len = lane.Length;
            if (len < 0.0001f) return null;

            foreach (var c in All)
            {
                if (c == null || c == ignore || c.IsWreck || c.lane != lane) continue;
                float dt = c.t - t;
                if (lane.Closed) { if (dt < 0f) dt += 1f; }
                else if (dt < 0f) continue;
                float dist = dt * len;
                if (dist < gapDist) { gapDist = dist; nearest = c; }
            }
            return nearest;
        }

        // Nearest LIVE carriage to position t on the lane, within range (either
        // direction). Used for attacks. Null if none.
        public static Carriage NearestTo(Lane lane, float t, float range)
        {
            float len = lane.Length;
            if (len < 0.0001f) return null;

            Carriage best = null;
            float bestDist = range;
            foreach (var c in All)
            {
                if (c == null || c.IsWreck || c.lane != lane) continue;
                float dt = Mathf.Abs(c.t - t);
                if (lane.Closed) dt = Mathf.Min(dt, 1f - dt);
                float d = dt * len;
                if (d <= bestDist) { bestDist = d; best = c; }
            }
            return best;
        }

        // Is any carriage within `clearance` world units of position t on the lane
        // (either direction)? Used to block a lane change into occupied space.
        public static bool Occupied(Lane lane, float t, float clearance)
        {
            float len = lane.Length;
            if (len < 0.0001f) return false;

            foreach (var c in All)
            {
                if (c == null || c.IsWreck || c.lane != lane) continue;
                float dt = Mathf.Abs(c.t - t);
                if (lane.Closed) dt = Mathf.Min(dt, 1f - dt);
                if (dt * len < clearance) return true;
            }
            return false;
        }

        // Energy bar above the carriage (screen-projected) while it's damaged but
        // still alive. Hidden at full energy and once wrecked.
        void OnGUI()
        {
            if (_destroyed || energy >= 1f) return;
            var cam = Camera.main;
            if (cam == null) return;

            Vector3 sp = cam.WorldToScreenPoint(transform.position + Vector3.up * 3f);
            if (sp.z <= 0f) return;

            const float w = 44f, h = 5f;
            float x = sp.x - w * 0.5f;
            float y = Screen.height - sp.y;
            BarFill(new Rect(x, y, w, h), barBack);
            BarFill(new Rect(x, y, w * Mathf.Clamp01(energy), h), barFill);
        }

        static Texture2D _white;
        static void BarFill(Rect r, Color c)
        {
            if (_white == null)
            {
                _white = new Texture2D(1, 1) { hideFlags = HideFlags.HideAndDontSave };
                _white.SetPixel(0, 0, Color.white); _white.Apply();
            }
            var prev = GUI.color; GUI.color = c; GUI.DrawTexture(r, _white); GUI.color = prev;
        }
    }
}
