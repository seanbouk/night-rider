// A trading station: a block placed over one or more lanes. When the rider
// crosses the block's centre (halfway through), it opens the trading menu.
//
// Orient the GameObject so its forward (blue Z) runs along the road; size sets
// the block's width x height x depth in world units. Prices are uniform for all
// items for now (sell < buy). Posts hold no inventory yet.

using UnityEngine;
using NightRider.View;

namespace NightRider.World
{
    public class TradingPost : MonoBehaviour
    {
        [Tooltip("Block size in world units (width x height x depth). Place over the lanes.")]
        public Vector3 size = new(24f, 6f, 8f);

        [Header("Prices (uniform for all items, sell < buy)")]
        [Min(0)] public int buyPrice = 20;
        [Min(0)] public int sellPrice = 10;

        public Color gizmoColor = new(1f, 0.85f, 0.2f, 0.22f);

        [Tooltip("Show a translucent placeholder block in play. Turn off once skinned.")]
        public bool showBlock = true;

        LaneFollower _rider;
        TradingMenu _menu;
        bool _armed = true;     // false after triggering, until the rider leaves
        bool _hasPrev;
        float _prevAlong;

        void Start()
        {
            if (showBlock) BuildVisual();
        }

        void BuildVisual()
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            Destroy(go.GetComponent<Collider>());
            go.name = "PostVisual";
            go.transform.SetParent(transform, false);
            go.transform.localScale = size;

            var mr = go.GetComponent<MeshRenderer>();
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;

            var m = new Material(Shader.Find("Universal Render Pipeline/Unlit"));
            m.SetFloat("_Surface", 1f);                        // transparent
            m.SetFloat("_Blend", 0f);                          // alpha blend
            m.SetFloat("_ZWrite", 0f);
            m.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
            m.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            m.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            m.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
            m.SetColor("_BaseColor", gizmoColor);
            mr.sharedMaterial = m;
        }

        void Update()
        {
            if (Time.timeScale == 0f) return;                  // menu open / paused
            if (_rider == null) _rider = FindAnyObjectByType<LaneFollower>();
            if (_rider == null) return;

            Vector3 to = _rider.transform.position - transform.position;
            float along = Vector3.Dot(to, transform.forward);
            float side  = Vector3.Dot(to, transform.right);
            float vert  = Vector3.Dot(to, transform.up);

            bool inside = Mathf.Abs(side)  <= size.x * 0.5f
                       && Mathf.Abs(vert)  <= size.y * 0.5f
                       && Mathf.Abs(along) <= size.z * 0.5f;

            // Halfway = the rider crosses the centre plane (along flips sign) inside.
            if (_armed && inside && _hasPrev && Mathf.Sign(along) != Mathf.Sign(_prevAlong))
            {
                if (_menu == null) _menu = FindAnyObjectByType<TradingMenu>();
                if (_menu != null) _menu.Open(this);
                _armed = false;
            }
            if (!inside) _armed = true;        // re-arm once clear of the block

            _prevAlong = along;
            _hasPrev = true;
        }

        void OnDrawGizmos()
        {
            Gizmos.matrix = Matrix4x4.TRS(transform.position, transform.rotation, Vector3.one);
            Gizmos.color = gizmoColor;
            Gizmos.DrawCube(Vector3.zero, size);
            Gizmos.color = new Color(gizmoColor.r, gizmoColor.g, gizmoColor.b, 1f);
            Gizmos.DrawWireCube(Vector3.zero, size);
        }
    }
}
