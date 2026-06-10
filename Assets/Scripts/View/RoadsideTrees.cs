// Trees flanking the whole road. From the rider's lane we walk OUTWARD to the
// outermost lane on each side (following same-direction neighbours), so a 3-lane
// road reads tree·lane·lane·lane·tree no matter which lane you're in. An oncoming
// lane isn't a same-direction neighbour, so it gets a treed median (agreed rule).
//
// No extra splines: the trees are a regularly spaced treadmill of upright billboard
// quads, computed each frame from the rider's position and the road scroll, so they
// stream past at the road-texture speed and keep going until behind the camera.
//
// Add to a plain GameObject; assign the rider, the LaneNetwork, and a
// NightRider/Tree material (wood + leaf textures + multiply colours).

using UnityEngine;
using NightRider.World;

namespace NightRider.View
{
    public class RoadsideTrees : MonoBehaviour
    {
        [Header("Refs")]
        public LaneFollower rider;
        public LaneNetwork network;
        [Tooltip("Material using NightRider/Tree (wood + leaf textures + multiply colours).")]
        public Material treeMaterial;

        [Header("Placement")]
        [Min(2)] public int perSide = 12;           // trees in the procession, each side
        [Min(0.5f)] public float spacing = 8f;       // metres between trees
        [Min(0f)] public float behind = 12f;         // keep trees alive this far behind the rider before recycling
        [Min(0f)] public float edgeOffset = 3f;      // lateral distance from the outer lane centre
        [Min(1f)] public float maxDistance = 80f;    // hide trees beyond this (match the road's far clip)

        [Header("Billboard size")]
        [Min(0.01f)] public float treeWidth = 2.5f;
        [Min(0.01f)] public float treeHeight = 4f;
        public float sizeStepPixels = 8f;
        public float mosaicHeight = 240f;

        Transform[] _trees;   // [0, perSide) = left side, [perSide, 2*perSide) = right side
        Camera _cam;
        float _scroll;

        void Awake()
        {
            if (rider == null) rider = FindAnyObjectByType<LaneFollower>();
            if (network == null) network = FindAnyObjectByType<LaneNetwork>();
            BuildPool();
        }

        void BuildPool()
        {
            _trees = new Transform[perSide * 2];
            for (int i = 0; i < _trees.Length; i++)
            {
                var go = GameObject.CreatePrimitive(PrimitiveType.Quad);
                go.name = "Tree";
                var col = go.GetComponent<Collider>();
                if (col != null) Destroy(col);
                go.transform.SetParent(transform, false);

                var mr = go.GetComponent<MeshRenderer>();
                if (treeMaterial != null) mr.sharedMaterial = treeMaterial;
                mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                mr.receiveShadows = false;

                go.SetActive(false);
                _trees[i] = go.transform;
            }
        }

        void LateUpdate()
        {
            if (_trees == null || rider == null || rider.lane == null || !rider.lane.IsValid) return;
            var lane = rider.lane;
            float len = lane.Length;
            if (len < 0.001f) return;
            if (_cam == null) _cam = Camera.main;

            // Treadmill advances at the road's APPARENT speed (real motion + scroll
            // desync), so trees lock to the road texture.
            _scroll += (rider.CurrentSpeed + RoadScroll.ExtraSpeed) * Time.deltaTime;
            float window = perSide * spacing;

            for (int s = 0; s < 2; s++)
            {
                int side = s == 0 ? -1 : 1;
                for (int k = 0; k < perSide; k++)
                {
                    var tr = _trees[s * perSide + k];
                    float ahead = Mathf.Repeat(k * spacing - _scroll, window) - behind;   // -behind .. window-behind
                    if (ahead > maxDistance) { Hide(tr); continue; }

                    float t = rider.t + ahead / len;
                    if (lane.Closed) t -= Mathf.Floor(t);
                    else if (t < 0f || t > 1f) { Hide(tr); continue; }

                    // Walk to the outermost lane on this side, then place on its open edge.
                    OuterEdge(lane, t, side, out var el, out var et);
                    el.EvaluateWorld(et, out var pos, out _, out _);
                    Vector3 world = pos + el.RightAt(et) * (side * edgeOffset);

                    float f = SuperScaler.StepFactor(world, _cam, treeWidth * 0.5f, mosaicHeight, sizeStepPixels);
                    if (f <= 0f) { Hide(tr); continue; }   // behind the camera

                    if (!tr.gameObject.activeSelf) tr.gameObject.SetActive(true);
                    Billboard(tr, world);                                        // upright, faces camera
                    tr.localScale = new Vector3(treeWidth * f, treeHeight * f, 1f);
                    tr.position = world + Vector3.up * (treeHeight * f * 0.5f);   // base on the road edge
                }
            }
        }

        // Follow same-direction neighbours outward on `side` until the road's edge.
        void OuterEdge(Lane lane, float t, int side, out Lane edgeLane, out float edgeT)
        {
            edgeLane = lane;
            edgeT = t;
            for (int i = 0; i < 8 && network != null; i++)   // guard against loops
            {
                if (!network.TryGetNeighbor(edgeLane, edgeT, side, out var nb) || nb == null || nb == edgeLane || !nb.IsValid)
                    break;
                edgeLane.EvaluateWorld(edgeT, out var wp, out _, out _);
                edgeT = nb.ProjectWorldPoint(wp, out _);
                edgeLane = nb;
            }
        }

        // Yaw-only billboard: face the camera but stay vertical (no tilt = no float).
        void Billboard(Transform tr, Vector3 world)
        {
            if (_cam == null) return;
            Vector3 toCam = _cam.transform.position - world;
            toCam.y = 0f;
            if (toCam.sqrMagnitude > 1e-4f) tr.rotation = Quaternion.LookRotation(toCam.normalized, Vector3.up);
        }

        static void Hide(Transform tr)
        {
            if (tr.gameObject.activeSelf) tr.gameObject.SetActive(false);
        }
    }
}
