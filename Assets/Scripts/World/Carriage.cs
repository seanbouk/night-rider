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
        [Min(0f), Tooltip("Gap at which we rear-end the carriage in front.")]
        public float bumpDistance = 3f;
        [Min(0f), Tooltip("Hard minimum gap — never overlap the carriage in front.")]
        public float minGap = 2f;
        public float heightOffset = 0.4f;

        public float CurrentSpeed { get; private set; }
        Carriage _contact;   // carriage in front we're touching (one-shot bumps)

        void OnEnable()  { if (!All.Contains(this)) All.Add(this); CurrentSpeed = idealSpeed; }
        void OnDisable() { All.Remove(this); }

        // Knocked forward by the rider — jump to (at least) the given speed, then
        // re-accelerate back down to idealSpeed.
        public void Bump(float speed) => CurrentSpeed = Mathf.Max(CurrentSpeed, speed);

        void Update()
        {
            if (lane == null || !lane.IsValid) return;
            float len = lane.Length;
            if (len < 0.0001f) return;

            float dt = Time.deltaTime;
            CurrentSpeed = Mathf.MoveTowards(CurrentSpeed, idealSpeed, acceleration * dt);

            // Rear-end the carriage in front (one-shot knock, like the rider).
            var ahead = NearestAhead(lane, t, out float gap, this);
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
                if (c == null || c == ignore || c.lane != lane) continue;
                float dt = c.t - t;
                if (lane.Closed) { if (dt < 0f) dt += 1f; }
                else if (dt < 0f) continue;
                float dist = dt * len;
                if (dist < gapDist) { gapDist = dist; nearest = c; }
            }
            return nearest;
        }

        // Is any carriage within `clearance` world units of position t on the lane
        // (either direction)? Used to block a lane change into occupied space.
        public static bool Occupied(Lane lane, float t, float clearance)
        {
            float len = lane.Length;
            if (len < 0.0001f) return false;

            foreach (var c in All)
            {
                if (c == null || c.lane != lane) continue;
                float dt = Mathf.Abs(c.t - t);
                if (lane.Closed) dt = Mathf.Min(dt, 1f - dt);
                if (dt * len < clearance) return true;
            }
            return false;
        }
    }
}
