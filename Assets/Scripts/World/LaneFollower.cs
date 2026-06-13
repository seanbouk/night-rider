// Rides a lane, and hops to a neighbouring lane on left/right input.
//
// Accelerates from a standstill toward its cruise speed. Rear-ends a slower
// carriage ahead on the same lane (a one-shot speed exchange, then both re-
// accelerate), and won't tunnel through it. A hop asks the LaneNetwork for the
// neighbour on the pressed side, refuses if a carriage is alongside there, else
// projects onto that lane and slides across (ease-out) while still moving.

using UnityEngine;
using NightRider.View;   // Hud (HUD-space pickup FX)

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
        [Range(0f, 1f), Tooltip("How much YOU slow on impact: your speed becomes this x the carriage's speed. Lower = slowed more.")]
        public float bumpSlowdown = 0.5f;
        [Min(0f), Tooltip("How hard you SHOVE the carriage: its speed jumps to this x your speed at impact. Higher = harder.")]
        public float bumpPush = 1f;
        [Min(0f), Tooltip("Hard minimum gap — never overlap the carriage ahead.")]
        public float minGap = 2f;
        [Min(0f), Tooltip("Block a lane change if a carriage is within this distance on the target lane.")]
        public float laneChangeClearance = 4f;
        [Range(0f, 1f), Tooltip("Energy a carriage loses each time you rear-end it.")]
        public float bumpEnergyDamage = 0.4f;
        [Min(0f), Tooltip("How close to a wreck the rider must be to run through and collect it.")]
        public float collectDistance = 3f;

        [Header("Attack")]
        [Min(0f), Tooltip("How far to the side an attack reaches (target range + apparition throw).")]
        public float attackReach = 6f;
        [Range(0f, 1f), Tooltip("Energy removed per attack. 1 = one-shot kill.")]
        public float attackDamage = 1f;
        [Tooltip("Rider sprite the apparition mirrors (synced frames).")]
        public SpriteSheetAnimator riderSprite;
        [Tooltip("Ghost material using the NightRider/Apparition shader.")]
        public Material apparitionMaterial;
        [Min(0.05f), Tooltip("Seconds the apparition lives (shoots out, then flashes).")]
        public float apparitionLife = 0.4f;
        [Min(0f), Tooltip("How far the apparition shoots to the side.")]
        public float apparitionReach = 4f;

        [Header("Lane switching")]
        [Min(0.01f), Tooltip("Seconds to slide across when hopping to a neighbour.")]
        public float laneChangeTime = 0.25f;
        [Min(1f), Tooltip("Ease-out punch: higher = snappier kick off the line, softer landing.")]
        public float easeOutPower = 3f;

        [Header("Editor")]
        [Min(0.1f), Tooltip("Size of the start-pose Scene gizmo.")]
        public float startGizmoSize = 8f;

        // Lateral slide for a lane hop: an offset that eases to zero. Captured from
        // the current visual pose, so rapid re-switches mid-hop don't snap.
        Vector3 _slideOffset;
        Quaternion _slideRot;
        float _slideTime;
        bool _sliding;

        // Speed accelerates from 0 toward `speed`. RoadScroll reads CurrentSpeed.
        float _currentSpeed;
        public float CurrentSpeed => _currentSpeed;
        Carriage _contact;   // carriage we're currently touching (for one-shot bumps)
        Hud _hud;

        void Awake()
        {
            if (network == null) network = FindAnyObjectByType<LaneNetwork>();
        }

        // Snap onto the lane at our start pose immediately. Start() is guaranteed to
        // run before any Update() this frame, so anything that reads our transform on
        // frame 0 — notably the carriage spawner's Seed() — sees the rider already on
        // the lane. Without this the seed can run first, anchor to the authored/default
        // transform, and scatter the opening carriages far down the lane (stranded on
        // the horizon at startup).
        void Start()
        {
            if (lane != null && lane.IsValid) Place(0f);
        }

        void Update()
        {
            if (Time.timeScale == 0f) return;        // paused (e.g. trading menu open)
            if (TradingMenu.ClosedFrame == Time.frameCount) return;  // swallow the EXIT press
            if (lane == null || !lane.IsValid) return;

            HandleInput();
            HandleAttack();
            Advance(Time.deltaTime);
            Place(Time.deltaTime);
            CollectWrecks();
        }

        void HandleInput()
        {
            if (network == null) return;

            int side = 0;
            if (Controls.Left) side = -1;
            else if (Controls.Right) side = +1;
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
            // Start the slide from where we visually are now.
            Vector3 prevPos = transform.position;
            Quaternion prevRot = transform.rotation;

            lane = target;
            t = targetT;
            target.EvaluateWorld(t, out var pos, out _, out _);

            _slideOffset = prevPos - (pos + Vector3.up * heightOffset);
            _slideRot = prevRot;
            _slideTime = 0f;
            _sliding = true;

            Sfx.Play(SfxId.ChangeLane);
        }

        // < attacks the lane to the left, > the lane to the right.
        void HandleAttack()
        {
            if (Controls.B || Controls.AttackLeft) Attack(-1);         // B / < or L = attack left
            else if (Controls.A || Controls.AttackRight) Attack(+1);   // A / > or ; = attack right
        }

        void Attack(int side)
        {
            Sfx.Play(SfxId.Attack);
            SpawnApparition(side);

            if (network == null) return;
            if (!network.TryGetNeighbor(lane, t, side, out var nb) || nb == null) return;

            lane.EvaluateWorld(t, out var worldPos, out _, out _);
            float tN = nb.ProjectWorldPoint(worldPos, out _);

            // A trading post on that lane? Open it instead of hitting a carriage.
            var post = TradingPost.At(nb, tN, attackReach);
            if (post != null) { post.Trigger(); return; }

            var target = Carriage.NearestTo(nb, tN, attackReach);
            if (target == null) return;

            // Lupin right away; carriage just dies (dark, brakes to a stop) — no shove.
            if (_hud == null) _hud = FindAnyObjectByType<Hud>();
            if (_hud != null) _hud.SpawnPickup(target.transform.position, ItemType.Lupins);
            target.Hit(attackDamage);
            target.Collect();
        }

        // A ghost apparition of the rider shoots out to the attacked side.
        void SpawnApparition(int side)
        {
            if (apparitionMaterial == null || riderSprite == null) return;

            var go = new GameObject("Apparition");
            go.AddComponent<SpriteRenderer>().sharedMaterial = apparitionMaterial;
            go.AddComponent<Apparition>()
              .Init(riderSprite, transform, side, apparitionReach, apparitionLife, riderSprite.SortingOrder - 1);
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
                    _currentSpeed = bumpSlowdown * ahead.CurrentSpeed;
                    ahead.Bump(mine * bumpPush);
                    ahead.Hit(bumpEnergyDamage);
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
        }

        void Place(float dt)
        {
            lane.EvaluateWorld(t, out var pos, out var fwd, out _);
            Vector3 basePos = pos + Vector3.up * heightOffset;
            Quaternion targetRot = Upright(fwd, transform.rotation);

            if (_sliding)
            {
                _slideTime += dt;
                float k = Mathf.Clamp01(_slideTime / Mathf.Max(0.01f, laneChangeTime));
                float e = 1f - Mathf.Pow(1f - k, easeOutPower);   // ease-out
                transform.position = basePos + _slideOffset * (1f - e);
                transform.rotation = Quaternion.Slerp(_slideRot, targetRot, e);
                if (k >= 1f) _sliding = false;
            }
            else
            {
                transform.position = basePos;
                transform.rotation = targetRot;
            }
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

        // Run through wrecks on our lane: collect (spawn a HUD pickup) and remove.
        void CollectWrecks()
        {
            if (_hud == null) _hud = FindAnyObjectByType<Hud>();
            float len = lane.Length;
            if (len < 0.0001f) return;

            var all = Carriage.All;
            for (int i = all.Count - 1; i >= 0; i--)
            {
                var c = all[i];
                if (c == null || !c.IsWreck || c.Collected || c.lane != lane) continue;

                // Signed along-lane distance: >0 ahead of us, <0 behind us.
                float dtt = c.t - t;
                if (lane.Closed) { if (dtt > 0.5f) dtt -= 1f; else if (dtt < -0.5f) dtt += 1f; }
                float signed = dtt * len;

                // Only once it's just slipped behind us — i.e. we've driven through it.
                if (signed >= 0f || signed < -collectDistance) continue;

                // Collect once; leave the wreck in the world to be culled off-camera.
                if (_hud != null) _hud.SpawnPickup(c.transform.position, ItemType.Lupins);
                c.Collect();
            }
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
