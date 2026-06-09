// Carriage sprite from a cols x rows sheet (e.g. 3 x 4). Each ROW is a colour
// (picked at random on spawn); the COLUMN is the view based on which side of the
// rider the carriage is on — left column when it's to your left, middle when in
// your lane, right when to your right. Billboards, unlit (self-illuminated).

using UnityEngine;
using NightRider.World;

namespace NightRider.View
{
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
        public bool unlit = true;

        SpriteRenderer _sr;
        Sprite[] _sprites;   // rows * cols
        int _row;
        Camera _cam;
        Carriage _carriage;   // parent, for the wreck flash

        void Awake()
        {
            _sr = GetComponent<SpriteRenderer>();
            if (unlit)
            {
                var sh = Shader.Find("Sprites/Default");
                if (sh != null) _sr.sharedMaterial = new Material(sh);
            }
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
