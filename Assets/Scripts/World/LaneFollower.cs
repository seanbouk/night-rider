// Rides a lane, and hops to a neighbouring lane on left/right input.
//
// Forward motion is constant ground speed along the current lane. A hop asks the
// LaneNetwork for the neighbour on the pressed side at the current position; if
// one exists, it projects onto that lane and slides across over laneChangeTime
// while still moving forward. (Full junction/fork following arrives at M5.)

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

        [Min(0f), Tooltip("Ground speed in world units per second.")]
        public float speed = 12f;
        [Range(0f, 1f), Tooltip("Start position along the lane (0..1).")]
        public float t = 0f;
        [Tooltip("Lift above the road so the rider sits on it, not through it.")]
        public float heightOffset = 1f;
        [Tooltip("If the lane isn't closed, stop at the end instead of looping.")]
        public bool stopAtEnd = false;

        [Header("Lane switching")]
        [Min(0.01f), Tooltip("Seconds to slide across when hopping to a neighbour.")]
        public float laneChangeTime = 0.25f;
        [Min(1f), Tooltip("Ease-out punch: higher = snappier kick off the line, softer landing.")]
        public float easeOutPower = 3f;

        // Blend state while a hop animates. _blend reaches 1 when settled.
        Lane _from;
        float _fromT;
        float _blend = 1f;
        float _blendDuration = 0.25f;

        void Awake()
        {
            if (network == null) network = FindFirstObjectByType<LaneNetwork>();
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
                SwitchTo(neighbor);
        }

        void SwitchTo(Lane target)
        {
            lane.EvaluateWorld(t, out var worldPos, out _, out _);
            float targetT = target.ProjectWorldPoint(worldPos, out _);

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

            t += speed * dt / len;
            if (t >= 1f)
            {
                bool canLoop = lane.Closed && !stopAtEnd;
                t = canLoop ? t - 1f : 1f;
            }

            // Keep the from-lane position advancing too, so the slide stays abreast.
            if (_from != null)
            {
                float lenF = _from.Length;
                if (lenF > 0.0001f)
                {
                    _fromT += speed * dt / lenF;
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
    }
}
