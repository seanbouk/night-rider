// Carriage sprite from a cols x rows sheet (e.g. 3 x 4). Each ROW is a colour
// (picked at random on spawn); the COLUMN is the view based on which side of the
// rider the carriage is on — left column when it's to your left, middle when in
// your lane, right when to your right. Billboards, unlit (self-illuminated).

using UnityEngine;
using NightRider.World;

namespace NightRider.View
{
    // A NES 4-colour palette for one carriage type: three colours as hex RRGGBB
    // (parsed then snapped to the NES hardware palette) + transparent. Source tone
    // maps dark->mid->light via the NesSprite shader. Hex (not Color) because the
    // Unity colour picker is unreliable on Color fields inside arrays.
    [System.Serializable]
    public class CarriagePalette
    {
        [Tooltip("Hex RRGGBB taken from the art (matched exactly), drawn NES-snapped.")] public string c0 = "808080";   // grey
        [Tooltip("Hex RRGGBB taken from the art (matched exactly), drawn NES-snapped.")] public string c1 = "73451a";   // brown
        [Tooltip("Hex RRGGBB taken from the art (matched exactly), drawn NES-snapped.")] public string c2 = "d9bf33";   // accent
        [Min(0f), Tooltip("NES snap bias toward vivid for THIS carriage type. 0 = geometric nearest (drab); ~1+ = jump to bright NES colours.")]
        public float vividness = 1.2f;

        // The colour you picked, used to MATCH art pixels. What's DRAWN is this
        // snapped to the NES palette (CarriageSprite, biased toward vivid).
        public Color Raw0 => Nes.ParseHex(c0, Color.gray);
        public Color Raw1 => Nes.ParseHex(c1, Color.gray);
        public Color Raw2 => Nes.ParseHex(c2, Color.gray);
    }

    [RequireComponent(typeof(SpriteRenderer))]
    public class CarriageSprite : MonoBehaviour
    {
        public Texture2D sheet;
        [Min(1)] public int cols = 3;
        [Min(1)] public int rows = 4;
        public float pixelsPerUnit = 100f;
        public Vector2 pivot = new(0.5f, 0.5f);

        [Tooltip("View-angle (degrees) past which we show the left/right view instead of middle.")]
        public float viewAngleThreshold = 18f;

        // Assigned by the spawner: the shared NesSprite material + one palette per row.
        public Material material;
        public CarriagePalette[] palettes;

        static readonly int Match0 = Shader.PropertyToID("_Match0");
        static readonly int Match1 = Shader.PropertyToID("_Match1");
        static readonly int Match2 = Shader.PropertyToID("_Match2");
        static readonly int Out0 = Shader.PropertyToID("_Out0");
        static readonly int Out1 = Shader.PropertyToID("_Out1");
        static readonly int Out2 = Shader.PropertyToID("_Out2");

        SpriteRenderer _sr;
        MaterialPropertyBlock _mpb;
        Sprite[] _sprites;   // rows * cols
        int _row;
        Camera _cam;
        Carriage _carriage;   // parent, for the wreck flash

        void Awake() => _sr = GetComponent<SpriteRenderer>();

        // NES recolour: the shared material + this row's three palette colours,
        // snapped to NES hardware colours, pushed per-renderer via a property block.
        void ApplyPalette()
        {
            if (material == null)
            {
                var sh = Shader.Find("NightRider/NesSprite");
                if (sh != null) material = new Material(sh);
            }
            if (material != null) _sr.sharedMaterial = material;

            if (palettes == null || palettes.Length == 0) return;
            var pal = palettes[_row % palettes.Length];
            _mpb ??= new MaterialPropertyBlock();
            _sr.GetPropertyBlock(_mpb);
            // Linear values: the sprite texture is sampled in linear (sRGB asset,
            // linear project), and SetColor uploads raw — so pass .linear to match
            // the sampled pixels and to draw the intended colour.
            _mpb.SetColor(Match0, pal.Raw0.linear);
            _mpb.SetColor(Match1, pal.Raw1.linear);
            _mpb.SetColor(Match2, pal.Raw2.linear);
            _mpb.SetColor(Out0, Nes.SnapVivid(pal.Raw0, pal.vividness).linear);
            _mpb.SetColor(Out1, Nes.SnapVivid(pal.Raw1, pal.vividness).linear);
            _mpb.SetColor(Out2, Nes.SnapVivid(pal.Raw2, pal.vividness).linear);
            _sr.SetPropertyBlock(_mpb);
        }

        void Slice()
        {
            if (sheet == null) return;
            _sprites = new Sprite[rows * cols];
            float w = sheet.width / (float)cols;
            float h = sheet.height / (float)rows;
            for (int r = 0; r < rows; r++)
                for (int c = 0; c < cols; c++)
                    // row 0 = top of the image (texture y is bottom-up)
                    _sprites[r * cols + c] = Sprite.Create(sheet, new Rect(c * w, (rows - 1 - r) * h, w, h), pivot, pixelsPerUnit);
        }

        void LateUpdate()
        {
            // Lazy slice: the spawner assigns `sheet`/`rider` after AddComponent,
            // i.e. after Awake has already run.
            if (_sprites == null)
            {
                if (sheet == null) return;
                Slice();
                _row = rows > 0 ? Random.Range(0, rows) : 0;
                _carriage = GetComponentInParent<Carriage>();
                ApplyPalette();
            }
            if (_sprites.Length == 0) return;

            if (_cam == null) _cam = Camera.main;

            // Column by the angle we're viewing it at (works on bends): the carriage
            // turned LEFT of our view means we see its RIGHT, and vice versa.
            int col = 1;   // aligned -> back/middle view
            Vector3 view = _cam != null ? _cam.transform.forward : Vector3.forward;
            Vector3 cf = transform.parent != null ? transform.parent.forward : transform.forward;
            view.y = 0f; cf.y = 0f;
            if (view.sqrMagnitude > 1e-4f && cf.sqrMagnitude > 1e-4f)
            {
                float ang = Vector3.SignedAngle(view, cf, Vector3.up);
                if (ang < -viewAngleThreshold) col = 2;
                else if (ang > viewAngleThreshold) col = 0;
            }
            col = Mathf.Clamp(col, 0, cols - 1);
            _sr.sprite = _sprites[_row * cols + col];

            // Out of energy: flash (placeholder for a destroyed sprite).
            _sr.enabled = _carriage == null || !_carriage.IsWreck || Mathf.Repeat(Time.time, 0.2f) < 0.1f;

            if (_cam != null) transform.rotation = _cam.transform.rotation;
        }
    }
}
