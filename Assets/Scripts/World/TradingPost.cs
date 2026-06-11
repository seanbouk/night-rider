// A trading station pinned to a lane position (lane + t, like the rider). It
// doesn't move — it sits still vs the geometry while the scrolling road slides
// past it, so it reads as a ghost. Running into it on its lane, or attacking
// the lane it sits on, opens the trading menu and pauses; it takes no damage.
//
// Placeholder visual: a translucent capsule (a "ghost vehicle" until skinned).

using System;
using System.Collections.Generic;
using UnityEngine;
using NightRider.View;

namespace NightRider.World
{
    public class TradingPost : MonoBehaviour
    {
        public static readonly List<TradingPost> All = new();

        [Tooltip("Shown as the menu title, e.g. \"Sam's Trading Post\".")]
        public string postName = "Trading Post";

        public Lane lane;
        [Range(0f, 1f)] public float t;

        [Header("Fallback prices (used only if there's no PriceMap)")]
        [Min(0)] public int buyPrice = 20;
        [Min(0)] public int sellPrice = 18;

        [Header("Placeholder ghost")]
        public bool showGhost = true;
        [Tooltip("2x1 ghost sheet: right = before the head, left = after. (Capsule used if empty.)")]
        public Texture2D ghostSheet;
        [Tooltip("Material using NightRider/Apparition with Monotone off (dither, keep colours).")]
        public Material ghostMaterial;
        public float spritePixelsPerUnit = 100f;
        public Vector2 spritePivot = new(0.5f, 0.5f);
        public Color ghostColor = new(1f, 0.85f, 0.2f, 0.35f);
        public Vector3 ghostScale = new(0.8f, 0.8f, 2.0f);
        public float heightOffset = 0.6f;
        [Min(0f), Tooltip("Hide the post beyond this distance from the camera (0 = always visible). Match the road's Max Distance so it pops in at the horizon.")]
        public float maxRenderDistance = 0f;

        [Header("Trigger")]
        [Min(0f), Tooltip("Run-into range along the post's lane.")]
        public float runIntoDistance = 3f;
        [Min(0f), Tooltip("Distance the rider must get past before it can re-trigger.")]
        public float rearmDistance = 8f;

        Transform _ghost;
        LaneFollower _rider;
        TradingMenu _menu;
        Camera _cam;
        bool _armed = true;

        // Prices cached from the PriceMap at this post's (fixed) position.
        readonly Dictionary<ItemType, (int buy, int sell)> _prices = new();

        void OnEnable()  { if (!All.Contains(this)) All.Add(this); }
        void OnDisable() { All.Remove(this); }

        void Start()
        {
            if (showGhost) BuildGhost();
            CachePrices();
        }

        void CachePrices()
        {
            _prices.Clear();
            if (lane == null || !lane.IsValid) return;
            lane.EvaluateWorld(t, out var pos, out _, out _);
            var map = FindAnyObjectByType<PriceMap>();

            foreach (ItemType ty in Enum.GetValues(typeof(ItemType)))
            {
                int b = map != null ? map.BuyPrice(ty, pos)  : buyPrice;
                int s = map != null ? map.SellPrice(ty, pos) : sellPrice;
                _prices[ty] = (b, s);
            }
        }

        public int BuyPrice(ItemType t)  => _prices.TryGetValue(t, out var p) ? p.buy  : buyPrice;
        public int SellPrice(ItemType t) => _prices.TryGetValue(t, out var p) ? p.sell : sellPrice;

        void Update()
        {
            if (lane == null || !lane.IsValid) return;

            // Pin the ghost to lane + t (fixed; the road scrolls past it).
            lane.EvaluateWorld(t, out var pos, out var fwd, out _);
            if (_ghost != null)
            {
                _ghost.position = pos + Vector3.up * heightOffset;
                Vector3 flat = Vector3.ProjectOnPlane(fwd, Vector3.up);
                if (flat.sqrMagnitude > 1e-6f) _ghost.rotation = Quaternion.LookRotation(flat.normalized, Vector3.up);

                // Hide it past the cutoff so it pops in at the road's horizon.
                if (maxRenderDistance > 0f)
                {
                    if (_cam == null) _cam = Camera.main;
                    if (_cam != null)
                    {
                        bool visible = Vector3.Distance(_ghost.position, _cam.transform.position) <= maxRenderDistance;
                        if (_ghost.gameObject.activeSelf != visible) _ghost.gameObject.SetActive(visible);
                    }
                }
            }

            if (Time.timeScale == 0f) return;        // menu open / paused
            if (_rider == null) _rider = FindAnyObjectByType<LaneFollower>();
            if (_rider == null) return;

            // Run into it: rider on the same lane, close along it.
            bool sameLane = _rider.lane == lane;
            float gap = AlongGap(_rider.t);
            if (_armed && sameLane && gap <= runIntoDistance) Trigger();
            if (!sameLane || gap > rearmDistance) _armed = true;
        }

        // Open the menu (run-into and the rider's attack both call this). Guarded
        // so it doesn't immediately re-open after you EXIT.
        public void Trigger()
        {
            if (!_armed) return;
            if (_menu == null) _menu = FindAnyObjectByType<TradingMenu>();
            if (_menu != null) _menu.Open(this);
            _armed = false;
        }

        float AlongGap(float riderT)
        {
            float len = lane.Length;
            float dt = Mathf.Abs(t - riderT);
            if (lane.Closed) dt = Mathf.Min(dt, 1f - dt);
            return dt * len;
        }

        // Nearest post on the given lane within range of t (world units). For attacks.
        public static TradingPost At(Lane ln, float tt, float range)
        {
            if (ln == null || !ln.IsValid) return null;
            float len = ln.Length;
            foreach (var p in All)
            {
                if (p == null || p.lane != ln) continue;
                float dt = Mathf.Abs(p.t - tt);
                if (ln.Closed) dt = Mathf.Min(dt, 1f - dt);
                if (dt * len <= range) return p;
            }
            return null;
        }

        void BuildGhost()
        {
            // Sprite ghost (billboards + dithers via the material).
            if (ghostSheet != null)
            {
                var sprite = new GameObject("Ghost");
                sprite.transform.SetParent(transform, false);
                var sr = sprite.AddComponent<SpriteRenderer>();
                if (ghostMaterial != null) sr.sharedMaterial = ghostMaterial;
                var ts = sprite.AddComponent<TradingPostSprite>();
                ts.sheet = ghostSheet;
                ts.pixelsPerUnit = spritePixelsPerUnit;
                ts.pivot = spritePivot;
                _ghost = sprite.transform;
                return;
            }

            // Capsule fallback.
            var root = new GameObject("Ghost");
            root.transform.SetParent(transform, false);
            _ghost = root.transform;

            var cap = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            Destroy(cap.GetComponent<Collider>());
            cap.transform.SetParent(root.transform, false);
            cap.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);   // lie longways
            cap.transform.localScale = ghostScale;

            var mr = cap.GetComponent<MeshRenderer>();
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;

            var m = new Material(Shader.Find("Universal Render Pipeline/Unlit"));
            m.SetFloat("_Surface", 1f);                        // transparent
            m.SetFloat("_Blend", 0f);
            m.SetFloat("_ZWrite", 0f);
            m.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
            m.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            m.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            m.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
            m.SetColor("_BaseColor", ghostColor);
            mr.sharedMaterial = m;
        }

        // Scene-view placement aid (edit mode): marker at lane + t.
        void OnDrawGizmos()
        {
            if (lane == null || !lane.IsValid) return;
            lane.EvaluateWorld(Mathf.Clamp01(t), out var pos, out _, out _);
            Gizmos.color = new Color(ghostColor.r, ghostColor.g, ghostColor.b, 0.9f);
            Gizmos.DrawWireSphere(pos + Vector3.up * heightOffset, 2f);
        }
    }
}
