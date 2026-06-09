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

        public LaneFollower rider;
        [Tooltip("Lateral distance counted as 'same lane' (middle sprite).")]
        public float sameLaneThreshold = 3f;
        public bool unlit = true;

        SpriteRenderer _sr;
        Sprite[] _sprites;   // rows * cols
        int _row;
        Camera _cam;

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
                if (rider == null) rider = FindAnyObjectByType<LaneFollower>();
            }
            if (_sprites.Length == 0) return;

            int col = 1;   // same lane = middle
            if (rider != null)
            {
                float lateral = Vector3.Dot(transform.position - rider.transform.position, rider.transform.right);
                if (lateral < -sameLaneThreshold) col = 0;        // left of the rider
                else if (lateral > sameLaneThreshold) col = 2;    // right of the rider
            }
            col = Mathf.Clamp(col, 0, cols - 1);
            _sr.sprite = _sprites[_row * cols + col];

            if (_cam == null) _cam = Camera.main;
            if (_cam != null) transform.rotation = _cam.transform.rotation;
        }
    }
}
