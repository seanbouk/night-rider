// The NES super-scaler "sprite pops between a few sizes" trick: the hardware
// couldn't scale a sprite, so games stored a few pre-sized copies. We fake it by
// snapping a billboarded sprite's on-screen WIDTH UP to whole steps of `stepPixels`
// mosaic pixels each frame, so size jumps in tiles while position glides smoothly.

using UnityEngine;

namespace NightRider.View
{
    public static class SuperScaler
    {
        // Sets t.localScale uniformly so a sprite's projected width snaps up to whole
        // `stepPixels` mosaic tiles. Measured at scale 1 from the sprite bounds.
        public static void SnapWidth(Transform t, Camera cam, Sprite sprite, float mosaicHeight, float stepPixels)
        {
            if (sprite == null) return;
            float f = StepFactor(t.position, cam, sprite.bounds.extents.x, mosaicHeight, stepPixels);
            if (f > 0f) t.localScale = Vector3.one * f;
        }

        // Uniform scale factor so something of half-width `worldHalfWidth` (at scale 1)
        // snaps its on-screen width up to whole `stepPixels` mosaic tiles. Returns 0
        // to skip (no camera / behind it / degenerate). Used by sprites and the tree
        // billboards (which apply it to their own base size).
        public static float StepFactor(Vector3 worldPos, Camera cam, float worldHalfWidth, float mosaicHeight, float stepPixels)
        {
            if (cam == null || worldHalfWidth <= 0f) return 0f;

            Vector3 r = cam.transform.right;
            Vector3 a = cam.WorldToScreenPoint(worldPos - r * worldHalfWidth);
            Vector3 b = cam.WorldToScreenPoint(worldPos + r * worldHalfWidth);
            if (a.z <= 0f || b.z <= 0f) return 0f;             // behind camera

            float mh   = mosaicHeight > 1f ? mosaicHeight : 240f;
            float step = stepPixels  >= 1f ? stepPixels  : 8f;

            float widthMosaic = Mathf.Abs(b.x - a.x) * mh / Mathf.Max(Screen.height, 1);
            if (widthMosaic < 1e-3f) return 0f;

            float snapped = Mathf.Max(step, Mathf.Ceil(widthMosaic / step) * step);   // always round up
            return snapped / widthMosaic;
        }
    }
}
