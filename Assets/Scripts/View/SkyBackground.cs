// Raster sky backdrop. Builds a frustum-filling quad behind everything (parented
// to the camera) and a 1xN gradient texture: black at the top fading to the sky
// colour at the horizon, each row snapped to a NES-legal colour — as if the NES
// background-colour register were rewritten on every horizontal blank (one flat
// colour per scanline). Below the horizon is left black (the ground/road covers
// it). The whole thing is just the camera background, so the road/sprites/HUD all
// draw on top, and the CRT pass mosaics/scanlines it like everything else.
//
// Add to the main camera. It auto-finds the NightRider/Sky shader and forces the
// camera to clear to solid black (so Unity's skybox doesn't fight it).

using UnityEngine;

namespace NightRider.View
{
    public class SkyBackground : MonoBehaviour
    {
        [Tooltip("Optional; falls back to Shader.Find(\"NightRider/Sky\").")]
        public Shader skyShader;

        [Tooltip("Sky colour at the horizon (fades to black toward the top). Snapped to NES.")]
        public Color skyColor = new(0.16f, 0.20f, 0.42f);   // dusky blue
        [Range(0f, 1f), Tooltip("Horizon height as a fraction up the screen; the sky gradient fills from here to the top.")]
        public float horizon = 0.5f;
        [Min(2), Tooltip("Gradient rows = NES scanlines (match the CRT's Pixel Height, 240).")]
        public int rows = 240;
        [Min(1f), Tooltip("Distance in front of the camera to place the backdrop quad (anywhere within the clip planes).")]
        public float distance = 100f;

        Camera _cam;
        Transform _quad;
        Material _mat;
        Texture2D _grad;
        Color _lastSky;
        float _lastHorizon;
        int _lastRows = -1;

        void Awake()
        {
            _cam = GetComponent<Camera>();
            if (_cam == null) _cam = Camera.main;
            if (_cam != null)
            {
                _cam.clearFlags = CameraClearFlags.SolidColor;   // no skybox to fight the backdrop
                _cam.backgroundColor = Color.black;
            }
            BuildQuad();
            Rebuild();
        }

        void BuildQuad()
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Quad);
            go.name = "SkyBackdrop";
            var col = go.GetComponent<Collider>();
            if (col != null) Destroy(col);

            _quad = go.transform;
            _quad.SetParent(transform, false);

            var mr = go.GetComponent<MeshRenderer>();
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            mr.receiveShadows = false;

            var sh = skyShader != null ? skyShader : Shader.Find("NightRider/Sky");
            _mat = new Material(sh);
            mr.sharedMaterial = _mat;
        }

        void LateUpdate()
        {
            if (_cam == null) return;

            // Fill the frustum at `distance` (quad is a child, so it tracks the camera).
            float h = 2f * distance * Mathf.Tan(_cam.fieldOfView * 0.5f * Mathf.Deg2Rad);
            _quad.localPosition = new Vector3(0f, 0f, distance);
            _quad.localRotation = Quaternion.identity;
            _quad.localScale = new Vector3(h * _cam.aspect, h, 1f);

            if (skyColor != _lastSky || !Mathf.Approximately(horizon, _lastHorizon) || rows != _lastRows)
                Rebuild();
        }

        // 1xN gradient: black at the top, sky colour at the horizon, black below it,
        // every row snapped to NES.
        void Rebuild()
        {
            int n = Mathf.Max(2, rows);
            if (_grad == null || _grad.height != n)
            {
                if (_grad != null) Destroy(_grad);
                _grad = new Texture2D(1, n, TextureFormat.RGBA32, false)
                {
                    name = "SkyGradient",
                    filterMode = FilterMode.Point,
                    wrapMode = TextureWrapMode.Clamp,
                };
            }

            var px = new Color32[n];
            for (int y = 0; y < n; y++)
            {
                float v = (y + 0.5f) / n;                       // 0 bottom .. 1 top
                Color c = Color.black;
                if (v >= horizon)
                {
                    float t = (v - horizon) / Mathf.Max(1e-4f, 1f - horizon);   // 0 at horizon .. 1 at top
                    c = Color.Lerp(skyColor, Color.black, t);
                }
                px[y] = (Color32)Nes.Snap(c);
            }
            _grad.SetPixels32(px);
            _grad.Apply();
            _mat.SetTexture("_Gradient", _grad);

            _lastSky = skyColor;
            _lastHorizon = horizon;
            _lastRows = rows;
        }
    }
}
