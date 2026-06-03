// Rides a lane, and hops to a neighbouring lane on left/right input.
//
// Accelerates from a standstill toward its cruise speed. Rear-ends a slower
// carriage ahead on the same lane (a one-shot speed exchange, then both re-
// accelerate), and won't tunnel through it. A hop asks the LaneNetwork for the
// neighbour on the pressed side, refuses if a carriage is alongside there, else
// projects onto that lane and slides across (ease-out) while still moving.

using UnityEngine;
using UnityEngine.InputSystem;

namespace NightRider.World
{
    public class LaneFollower : MonoBehaviour
    {
        [Header("Riding")]
        [Tooltip("The lane currently being ridden.")]
        public Lane lane;
        [Tooltip("Adjacency source. Auto-found in the scene if left empty.")]
        public LaneNetwork network;

        [Header("Speed")]
        [Min(0f), Tooltip("Cruise (ideal) speed in world units per second.")]
        public float speed = 12f;
        [Min(0f), Tooltip("Acceleration toward cruise speed (units/s^2). Rider starts from 0.")]
        public float acceleration = 8f;
        [Min(0f), Tooltip("Deceleration when slowing (units/s^2).")]
        public float braking = 16f;

        [Range(0f, 1f), Tooltip("Start position along the lane (0..1).")]
        public float t = 0f;
        [Tooltip("Lift above the road so the rider sits on it, not through it.")]
        public float heightOffset = 1f;
        [Tooltip("If the lane isn't closed, stop at the end instead of looping.")]
        public bool stopAtEnd = false;

        [Header("Traffic")]
        [Min(0f), Tooltip("Gap at which we rear-end the carriage ahead (world units).")]
        public float bumpDistance = 3f;
        [Min(0f), Tooltip("Hard minimum gap — never overlap the carriage ahead.")]
        public float minGap = 2f;
        [Min(0f), Tooltip("Block a lane change if a carriage is within this distance on the target lane.")]
        public float laneChangeClearance = 4f;

        [Header("Lane switching")]
        [Min(0.01f), Tooltip("Seconds to slide across when hopping to a neighbour.")]
        public float laneChangeTime = 0.25f;
        [Min(1f), Tooltip("Ease-out punch: higher = snappier kick off the line, softer landing.")]
        public float easeOutPower = 3f;

        [Header("Editor")]
        [Min(0.1f), Tooltip("Size of the start-pose Scene gizmo.")]
        public float startGizmoSize = 8f;

        // Blend state while a hop animates. _blend reaches 1 when settled.
        Lane _from;
        float _fromT;
        float _blend = 1f;
        float _blendDuration = 0.25f;

        // Speed accelerates from 0 toward `speed`. RoadScroll reads CurrentSpeed.
        float _currentSpeed;
        public float CurrentSpeed => _currentSpeed;
        Carriage _contact;   // carriage we're currently touching (for one-shot bumps)

        void Awake()
        {
            if (network == null) network = FindAnyObjectByType<LaneNetwork>();
        }

        void Update()
        {
            if (lane == null || !lane.IsValid) return;

            HandleInput();
            Advance(Time.deltaTime);
            Place(Time.deltaTime);
        }

        void HandleInput()
        {
            if (_blend < 1f) return;          // ignore input mid-hop
            var kb = Keyboard.current;
            if (kb == null || network == null) return;

            int side = 0;
            if (kb.leftArrowKey.wasPressedThisFrame || kb.aKey.wasPressedThisFrame) side = -1;
            else if (kb.rightArrowKey.wasPressedThisFrame || kb.dKey.wasPressedThisFrame) side = +1;
            if (side == 0) return;

            if (network.TryGetNeighbor(lane, t, side, out var neighbor) && neighbor != null)
            {
                lane.EvaluateWorld(t, out var worldPos, out _, out _);
                float landT = neighbor.ProjectWorldPoint(worldPos, out _);
                // Can't hop into a lane where a carriage is alongside.
                if (!Carriage.Occupied(neighbor, landT, laneChangeClearance))
                    SwitchTo(neighbor, landT);
            }
        }

        void SwitchTo(Lane target, float targetT)
        {
            _from = lane;
            _fromT = t;
            lane = target;
            t = targetT;
            _blend = 0f;
            _blendDuration = Mathf.Max(0.01f, laneChangeTime);
        }

        void Advance(float dt)
        {
            float len = lane.Length;
            if (len < 0.0001f) return;

            // Accelerate toward cruise speed (rider starts from 0).
            float rate = _currentSpeed < speed ? acceleration : braking;
            _currentSpeed = Mathf.MoveTowards(_currentSpeed, speed, rate * dt);

            // Carriage ahead on our lane?
            var ahead = Carriage.NearestAhead(lane, t, out float gap);

            // Rear-end knock, once per contact: we drop to half its speed, it
            // jumps to ours; both then re-accelerate toward their ideals.
            if (ahead != null && gap <= bumpDistance)
            {
                if (_contact != ahead)
                {
                    _contact = ahead;
                    float mine = _currentSpeed;
                    _currentSpeed = 0.5f * ahead.CurrentSpeed;
                    ahead.Bump(mine);
                }
            }
            else if (ahead == null || gap > bumpDistance * 1.5f)
            {
                _contact = null;   // separated — allow a fresh bump next time
            }

            t += _currentSpeed * dt / len;
            if (t >= 1f)
            {
                bool canLoop = lane.Closed && !stopAtEnd;
                t = canLoop ? t - 1f : 1f;
            }

            // Hard floor: never tunnel through the carriage ahead.
            if (ahead != null)
            {
                float gapT = ahead.t - t;
                if (lane.Closed && gapT < 0f) gapT += 1f;
                float minGapT = minGap / len;
                if (gapT < minGapT)
                {
                    t = ahead.t - minGapT;
                    if (t < 0f) t += 1f;
                    if (_currentSpeed > ahead.CurrentSpeed) _currentSpeed = ahead.CurrentSpeed;
                }
            }

            // Keep the from-lane position advancing too, so the slide stays abreast.
            if (_from != null)
            {
                float lenF = _from.Length;
                if (lenF > 0.0001f)
                {
                    _fromT += _currentSpeed * dt / lenF;
                    if (_fromT >= 1f && _from.Closed) _fromT -= 1f;
                }
            }
        }

        void Place(float dt)
        {
            lane.EvaluateWorld(t, out var pos, out var fwd, out _);
            Vector3 finalPos = pos + Vector3.up * heightOffset;
            Quaternion finalRot = Upright(fwd, transform.rotation);

            if (_blend < 1f && _from != null)
            {
                _blend += dt / _blendDuration;
                _from.EvaluateWorld(_fromT, out var fpos, out var ffwd, out _);

                Vector3 fromPos = fpos + Vector3.up * heightOffset;
                Quaternion fromRot = Upright(ffwd, finalRot);

                // Ease-out: fast off the line, decelerating into the landing.
                float x = Mathf.Clamp01(_blend);
                float e = 1f - Mathf.Pow(1f - x, easeOutPower);
                finalPos = Vector3.Lerp(fromPos, finalPos, e);
                finalRot = Quaternion.Slerp(fromRot, finalRot, e);

                if (_blend >= 1f) _from = null;
            }

            transform.position = finalPos;
            transform.rotation = finalRot;
        }

        // Face along travel, but always upright. Forward is flattened to the
        // ground plane and up is world up, so a mirrored/reversed lane (whose
        // own up vector is flipped) never turns the rider upside-down.
        static Quaternion Upright(Vector3 forward, Quaternion fallback)
        {
            Vector3 flat = Vector3.ProjectOnPlane(forward, Vector3.up);
            return flat.sqrMagnitude < 1e-6f
                ? fallback
                : Quaternion.LookRotation(flat.normalized, Vector3.up);
        }

        // Scene-view marker for the start pose (lane + t), so it's visible while
        // authoring. In edit mode this is the start; in play it tracks the rider.
        void OnDrawGizmos()
        {
            if (lane == null || !lane.IsValid) return;

            lane.EvaluateWorld(Mathf.Clamp01(t), out var pos, out var fwd, out _);
            Vector3 p = pos + Vector3.up * heightOffset;
            Vector3 flat = Vector3.ProjectOnPlane(fwd, Vector3.up);
            flat = flat.sqrMagnitude > 1e-6f ? flat.normalized : Vector3.forward;
            Vector3 side = Vector3.Cross(Vector3.up, flat);

            float s = startGizmoSize;
            Gizmos.color = new Color(0.2f, 1f, 0.4f, 0.9f);
            Gizmos.DrawWireSphere(p, s);

            // Travel-direction arrow.
            Vector3 tip = p + flat * s * 3f;
            Gizmos.DrawLine(p, tip);
            Gizmos.DrawLine(tip, tip - flat * s + side * s * 0.6f);
            Gizmos.DrawLine(tip, tip - flat * s - side * s * 0.6f);
        }
    }
}
