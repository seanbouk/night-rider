// Trading-post ghost from a 2x1 sheet: right sprite before you own a head, left
// sprite after. Always billboards to the camera (it's a ghost, so it faces you
// even on lanes running the other way). The see-through (every-other-screen-
// column) comes from the material (NightRider/Apparition with monotone off).

using UnityEngine;
using NightRider.World;

namespace NightRider.View
{
    [RequireComponent(typeof(SpriteRenderer))]
    public class TradingPostSprite : MonoBehaviour
    {
        public Texture2D sheet;
        public float pixelsPerUnit = 100f;
        public Vector2 pivot = new(0.5f, 0.5f);

        [Tooltip("Snap the sheet's colours to the NES palette at load (needs Read/Write on the texture).")]
        public bool snapToNes = true;

        [Header("Super-scaler stepped scaling")]
        [Tooltip("Snap on-screen width up to whole steps of this many mosaic pixels (8 = one NES tile).")]
        public float sizeStepPixels = 8f;
        [Tooltip("Mosaic vertical resolution — match the CRT's Pixel Height (240).")]
        public float mosaicHeight = 240f;

        SpriteRenderer _sr;
        Sprite _before, _after;
        PlayerState _player;
        Camera _cam;

        void Awake() => _sr = GetComponent<SpriteRenderer>();

        void LateUpdate()
        {
            // Lazy slice (the builder assigns `sheet` after AddComponent).
            if (_before == null)
            {
                if (sheet == null) return;
                if (snapToNes) sheet = Nes.SnapTexture(sheet);
                float w = sheet.width / 2f, h = sheet.height;
                _after  = Sprite.Create(sheet, new Rect(0f, 0f, w, h), pivot, pixelsPerUnit);   // left = after
                _before = Sprite.Create(sheet, new Rect(w,  0f, w, h), pivot, pixelsPerUnit);   // right = before
                _player = FindAnyObjectByType<PlayerState>();
            }

            _sr.sprite = HasHead() ? _after : _before;

            if (_cam == null) _cam = Camera.main;
            if (_cam != null) transform.rotation = _cam.transform.rotation;
            SuperScaler.SnapWidth(transform, _cam, _sr.sprite, mosaicHeight, sizeStepPixels);
        }

        bool HasHead()
        {
            if (_player == null) return false;
            foreach (var it in _player.items)
                if (it.type == ItemType.Heads && it.count >= 1) return true;
            return false;
        }
    }
}
