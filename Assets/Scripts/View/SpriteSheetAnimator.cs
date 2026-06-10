// Plays a horizontal sprite sheet (N x 1 frames) on a SpriteRenderer, slicing the
// frames in code (no Sprite Editor needed). Billboards to the camera for the
// super-scaler look and renders unlit (self-illuminated). Can hide a placeholder
// mesh (the capsule) with a debug toggle to bring it back.

using System.Collections.Generic;
using UnityEngine;

namespace NightRider.View
{
    [RequireComponent(typeof(SpriteRenderer))]
    public class SpriteSheetAnimator : MonoBehaviour
    {
        [Header("Sheet")]
        public Texture2D sheet;
        [Min(1)] public int frames = 6;
        public float fps = 12f;
        public float pixelsPerUnit = 100f;
        public Vector2 pivot = new(0.5f, 0.5f);   // 0.5,0 = bottom-centre (sits on the road)

        [Header("Look")]
        [Tooltip("Always face the camera (screen-space billboard).")]
        public bool billboard = true;
        [Tooltip("Self-illuminated (unlit) — ignores scene lighting.")]
        public bool unlit = true;
        [Tooltip("Snap every sheet pixel to the nearest NES palette colour at load " +
                 "(keeps all colours; needs Read/Write on the texture).")]
        public bool snapToNes = false;

        [Header("Audio")]
        [Tooltip("Play the gallop hoof SFX as this animation hits frames 0-3 (tick on the rider).")]
        public bool galloping = false;

        [Header("Debug")]
        [Tooltip("Optional placeholder mesh to hide (e.g. the capsule).")]
        public Renderer debugMesh;
        public bool showDebugMesh = false;

        SpriteRenderer _sr;
        Sprite[] _sprites;
        float _t;
        int _lastFrame = -1;
        Camera _cam;

        // Current frame, so an apparition can mirror the rider in sync.
        public Sprite CurrentSprite => _sr != null ? _sr.sprite : null;
        public int SortingOrder => _sr != null ? _sr.sortingOrder : 0;

        void Awake()
        {
            _sr = GetComponent<SpriteRenderer>();

            if (unlit)
            {
                var sh = Shader.Find("Sprites/Default");   // built-in, unlit
                if (sh != null) _sr.sharedMaterial = new Material(sh);
            }
            if (snapToNes) sheet = NesSnapped(sheet);
            Slice();
        }

        // Snap every pixel to its nearest NES colour (per unique colour, cached),
        // keeping the original alpha so transparency/edges are untouched. Returns a
        // new texture; the source asset is left alone.
        static Texture2D NesSnapped(Texture2D src)
        {
            if (src == null) return src;
            if (!src.isReadable)
            {
                Debug.LogWarning($"SpriteSheetAnimator: enable Read/Write on '{src.name}' to NES-snap it; using it un-snapped.");
                return src;
            }

            var px = src.GetPixels32();
            var cache = new Dictionary<Color32, Color32>();
            for (int i = 0; i < px.Length; i++)
            {
                Color32 c = px[i];
                if (!cache.TryGetValue(c, out var snapped))
                {
                    snapped = (Color32)Nes.Snap(new Color(c.r / 255f, c.g / 255f, c.b / 255f));
                    snapped.a = c.a;                       // keep original transparency
                    cache[c] = snapped;
                }
                px[i] = snapped;
            }

            var tex = new Texture2D(src.width, src.height, TextureFormat.RGBA32, false)
            {
                name = src.name + "_NES",
                filterMode = FilterMode.Point,
                wrapMode = src.wrapMode,
            };
            tex.SetPixels32(px);
            tex.Apply();
            return tex;
        }

        void Slice()
        {
            if (sheet == null || frames < 1) return;
            _sprites = new Sprite[frames];
            float w = sheet.width / (float)frames;
            float h = sheet.height;
            for (int i = 0; i < frames; i++)
                _sprites[i] = Sprite.Create(sheet, new Rect(i * w, 0f, w, h), pivot, pixelsPerUnit);
            _sr.sprite = _sprites[0];
        }

        void LateUpdate()
        {
            if (_sprites != null && _sprites.Length > 0 && fps > 0f)
            {
                _t += Time.deltaTime * fps;
                if (_t >= _sprites.Length) _t %= _sprites.Length;
                int frame = (int)_t;
                _sr.sprite = _sprites[frame];

                if (frame != _lastFrame)
                {
                    _lastFrame = frame;
                    if (galloping && frame <= 3) Sfx.Play(SfxId.Gallop);   // hoofbeat per gallop frame
                }
            }

            if (billboard)
            {
                if (_cam == null) _cam = Camera.main;
                if (_cam != null) transform.rotation = _cam.transform.rotation;
            }

            if (debugMesh != null && debugMesh.enabled != showDebugMesh)
                debugMesh.enabled = showDebugMesh;
        }
    }
}
