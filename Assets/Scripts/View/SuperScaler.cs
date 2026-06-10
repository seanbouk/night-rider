// The NES super-scaler "sprite pops between a few sizes" trick: the hardware
// couldn't scale a sprite, so games stored a few pre-sized copies. We fake it by
// snapping a billboarded sprite's on-screen WIDTH UP to whole steps of `stepPixels`
// mosaic pixels each frame, so size jumps in tiles while position glides smoothly.

using UnityEngine;

namespace NightRider.View
{
    public static class SuperScaler
    {
        // Sets t.localScale so the sprite's projected width is the next whole step of
        // `stepPixels` mosaic pixels (mosaicHeight = the CRT's vertical resolution).
        // Measured at scale 1 from the sprite bounds, so there's no feedback.
        public static void SnapWidth(Transform t, Camera cam, Sprite sprite, float mosaicHeight, float stepPixels)
        {
            if (cam == null || sprite == null) return;

            Vector3 pos = t.position;
            float halfW = sprite.bounds.extents.x;          // world half-width at scale 1
            Vector3 r = cam.transform.right;
            Vector3 a = cam.WorldToScreenPoint(pos - r * halfW);
            Vector3 b = cam.WorldToScreenPoint(pos + r * halfW);
            if (a.z <= 0f || b.z <= 0f) return;             // behind camera — leave last scale

            float mh   = mosaicHeight > 1f ? mosaicHeight : 240f;
            float step = stepPixels  >= 1f ? stepPixels  : 8f;

            float widthMosaic = Mathf.Abs(b.x - a.x) * mh / Mathf.Max(Screen.height, 1);
            if (widthMosaic < 1e-3f) return;

            float snapped = Mathf.Max(step, Mathf.Ceil(widthMosaic / step) * step);   // always round up
            t.localScale = Vector3.one * (snapped / widthMosaic);
        }
    }
}
