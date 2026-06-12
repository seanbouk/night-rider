// The screen-space canvas that ALL game UI lives on, built at runtime.
//
// The UI must get the CRT's scanlines + horizontal blur, but NOT the mosaic
// (downsampling crisp pixel text to 240p wrecks it). One fullscreen pass can't
// mosaic the world yet skip the UI if they share a buffer, so the UI is rendered
// to its OWN texture by a dedicated UICamera and published as the global _UITex.
// CRT.shader mosaics the world, lays this crisp UI over it (hard 0.5 alpha
// cutout), then runs blur + scanlines across the whole composite. Net: mosaic =
// world only; blur + scanlines = everything. (The UI is thus only visible through
// the CRT pass — fine, it always runs in-game.)
//
// Layout mirrors the old IMGUI framing: a centred 4:3 area on a 40x30 grid where
// each cell is one 8x8 NES tile. The black SIDE PILLARS (the TV doesn't reach the
// edge) are real Images on the canvas so the blur bleeds into them. Auto-creates
// itself if absent; add it to a scene object to tweak the inspector values.

using UnityEngine;
using UnityEngine.UI;
using UnityEngine.Rendering.Universal;

namespace NightRider.View
{
    [DefaultExecutionOrder(-100)]
    public class UiCanvas : MonoBehaviour
    {
        static UiCanvas _instance;
        public static UiCanvas Instance
        {
            get
            {
                if (_instance == null)
                {
                    _instance = FindAnyObjectByType<UiCanvas>();
                    if (_instance == null) _instance = new GameObject("UiCanvas").AddComponent<UiCanvas>();
                }
                return _instance;
            }
        }

        [Tooltip("Grid width in 8x8 cells (NES 320px / 8 = 40).")]
        public int cols = 40;
        [Tooltip("Grid height in 8x8 cells (NES 240px / 8 = 30).")]
        public int rows = 30;
        [Tooltip("Inset the play area by this many columns each side; the black pillars fill from there out to the screen edge.")]
        public int sideColumns = 3;
        public Color pillarColor = Color.black;
        [Tooltip("UI plane distance from the camera (between near and far clip).")]
        public float planeDistance = 1f;
        [Tooltip("Opaque backdrop drawn behind the trading menu.")]
        public Color menuBackground = new(0.06f, 0.05f, 0.10f, 1f);

        public RectTransform Frame { get; private set; }       // centred 4:3 area (the grid)
        public RectTransform WorldBars { get; private set; }   // back: world-anchored bars
        public RectTransform HudPanel { get; private set; }    // HUD content + pillars
        public RectTransform PickupPanel { get; private set; } // pickup FX
        public RectTransform PausePanel { get; private set; }  // pause box
        public RectTransform MenuRoot { get; private set; }    // full-screen, on top (toggled)
        public RectTransform MenuFrame { get; private set; }   // 4:3 grid inside the menu
        public RectTransform TitleRoot { get; private set; }   // full-screen black, very top (title screen)
        public RectTransform TitleFrame { get; private set; }  // 4:3 grid inside the title

        Canvas _canvas;
        Camera _uiCam;            // renders the canvas to _uiRT only
        RenderTexture _uiRT;      // UI texture the CRT composites in (the global _UITex)
        int _uiLayer;             // layer the UICamera renders (and only it)
        static int s_uiLayer = 5; // same, for the static factory (NewRect) to stamp on creation
        bool _built;
        static readonly int UITexId = Shader.PropertyToID("_UITex");

        void Awake()
        {
            if (_instance != null && _instance != this) { Destroy(this); return; }
            _instance = this;
            Build();
        }

        void OnDestroy()
        {
            if (_uiCam != null) _uiCam.targetTexture = null;
            if (_uiRT != null) { _uiRT.Release(); Destroy(_uiRT); _uiRT = null; }
            if (_instance == this) _instance = null;
        }

        void LateUpdate()
        {
            if (!_built) return;
            EnsureRT();
            // Keep every UI object on the UI layer so the UICamera (and only it)
            // renders them — covers runtime-spawned glyphs/pickups/bars too.
            if (_canvas != null) SetLayerRecursively(_canvas.gameObject, _uiLayer);
            Shader.SetGlobalTexture(UITexId, _uiRT);
        }

        // The WORLD camera — sprites/bars project through this (the UICamera only
        // ever renders the flat canvas to its RT).
        public Camera Cam => Camera.main;

        // Screen point (px, origin bottom-left) -> anchored position for a child of a
        // centre-anchored panel (Pickups / WorldBars), as an offset from the canvas
        // centre. All our panels are centred on the canvas and (ConstantPixelSize)
        // 1 unit = 1 px, so this is the old IMGUI screen-pixel placement — but with
        // no RectTransformUtility false-return path that would leave a glyph
        // unplaced/invisible. `frame` is unused (kept for call-site clarity).
        public bool ScreenToFrame(RectTransform frame, Vector3 screenPos, out Vector2 local)
        {
            var root = (RectTransform)(_canvas != null ? _canvas.transform : transform);
            Vector2 size = root.rect.size;
            local = new Vector2(
                (screenPos.x / Mathf.Max(Screen.width,  1f) - 0.5f) * size.x,
                (screenPos.y / Mathf.Max(Screen.height, 1f) - 0.5f) * size.y);
            return true;
        }

        public float CellWidth(RectTransform frame)  => frame.rect.width  / cols;
        public float CellHeight(RectTransform frame) => frame.rect.height / rows;

        // ---- build -------------------------------------------------------------

        void Build()
        {
            if (_built) return;
            _built = true;

            _uiLayer = LayerMask.NameToLayer("UI");
            if (_uiLayer < 0) _uiLayer = 5;   // built-in "UI" layer
            s_uiLayer = _uiLayer;

            // Dedicated camera that renders ONLY the canvas, into _uiRT. Lowest depth
            // so it runs before the main camera (whose CRT pass reads the result).
            var camGo = new GameObject("UICamera");
            camGo.transform.SetParent(transform, false);
            _uiCam = camGo.AddComponent<Camera>();
            _uiCam.orthographic = true;
            _uiCam.clearFlags = CameraClearFlags.SolidColor;
            _uiCam.backgroundColor = new Color(0f, 0f, 0f, 0f);   // transparent
            _uiCam.cullingMask = 1 << _uiLayer;                    // canvas only, never the world
            _uiCam.depth = -100;
            _uiCam.nearClipPlane = 0.1f;
            _uiCam.farClipPlane = 100f;
            _uiCam.allowMSAA = false;
            _uiCam.allowHDR = false;
            // Use the CRT-free renderer (index 1 = Mobile_Renderer in PC_RPAsset) so
            // the CRT fullscreen pass does NOT run on the UI render (it would mosaic
            // the UI and feed _uiRT back into itself). The main camera keeps index 0.
            var camData = _uiCam.GetUniversalAdditionalCameraData();
            if (camData != null) camData.SetRenderer(1);

            var go = new GameObject("GameUiCanvas", typeof(RectTransform));
            go.transform.SetParent(transform, false);
            _canvas = go.AddComponent<Canvas>();
            // Screen Space - Camera bound to the UICamera: the canvas renders into
            // _uiRT (and only via that camera), so the main camera never draws it.
            // CRT.shader composites _uiRT over the mosaiced world.
            _canvas.renderMode = RenderMode.ScreenSpaceCamera;
            _canvas.worldCamera = _uiCam;
            _canvas.planeDistance = planeDistance;
            _canvas.sortingOrder = 100;
            var scaler = go.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ConstantPixelSize;
            var root = (RectTransform)go.transform;

            Frame       = Make43Frame("Frame", root);
            WorldBars   = MakePanel("WorldBars", Frame);
            PickupPanel = MakePanel("Pickups", Frame);   // before Hud -> tucks behind the black HUD bar
            HudPanel    = MakePanel("Hud", Frame);
            PausePanel  = MakePanel("Pause", Frame);

            MenuRoot = MakePanel("Menu", root);                  // full-screen, after Frame -> on top
            var bg = MakeImage(MenuRoot, menuBackground);
            bg.name = "Bg";
            Stretch(bg.rectTransform);
            MenuFrame = Make43Frame("MenuFrame", MenuRoot);
            MenuRoot.gameObject.SetActive(false);

            // Title screen: full-screen black backdrop + a 4:3 grid, above everything.
            TitleRoot = MakePanel("Title", root);
            var tbg = MakeImage(TitleRoot, Color.black);
            tbg.name = "Bg";
            Stretch(tbg.rectTransform);
            TitleFrame = Make43Frame("TitleFrame", TitleRoot);
            TitleRoot.gameObject.SetActive(false);

            BuildPillars();

            SetLayerRecursively(go, _uiLayer);
            EnsureRT();
        }

        // (Re)create the UI render texture at screen size and bind it as _UITex.
        void EnsureRT()
        {
            int w = Mathf.Max(1, Screen.width);
            int h = Mathf.Max(1, Screen.height);
            if (_uiRT != null && _uiRT.width == w && _uiRT.height == h) return;

            if (_uiRT != null) { _uiCam.targetTexture = null; _uiRT.Release(); Destroy(_uiRT); }
            // 24-bit depth/stencil: URP's render graph requires a camera-output RT to
            // have a depth buffer (and UGUI masks can use the stencil).
            _uiRT = new RenderTexture(w, h, 24, RenderTextureFormat.ARGB32)
            {
                name = "UITex",
                filterMode = FilterMode.Point,   // crisp 1:1 when the CRT samples it
                wrapMode = TextureWrapMode.Clamp,
            };
            _uiRT.Create();
            _uiCam.targetTexture = _uiRT;
            Shader.SetGlobalTexture(UITexId, _uiRT);
        }

        static void SetLayerRecursively(GameObject go, int layer)
        {
            if (go.layer != layer) go.layer = layer;
            var t = go.transform;
            for (int i = 0; i < t.childCount; i++)
                SetLayerRecursively(t.GetChild(i).gameObject, layer);
        }

        // How far (px) the pillars overrun the frame to reach the real screen edge.
        // The inner edge stays anchored to the play-area boundary; the outer/top/
        // bottom edges run well past any window so the world never peeks out on a
        // wider-than-4:3 (or taller) display.
        const float PillarOverscan = 4000f;

        void BuildPillars()
        {
            if (sideColumns <= 0) return;
            AddPillar("PillarL", -1);
            AddPillar("PillarR", +1);
        }

        void AddPillar(string name, int side)
        {
            var img = MakeImage(HudPanel, pillarColor);
            img.name = name;
            var rt = img.rectTransform;
            float inner = (float)sideColumns / cols;   // play-area boundary (frame fraction)

            if (side < 0)   // left pillar: screen edge .. inner boundary
            {
                rt.anchorMin = new Vector2(0f, 0f);
                rt.anchorMax = new Vector2(inner, 1f);
                rt.offsetMin = new Vector2(-PillarOverscan, -PillarOverscan);
                rt.offsetMax = new Vector2(0f, PillarOverscan);
            }
            else            // right pillar: inner boundary .. screen edge
            {
                rt.anchorMin = new Vector2(1f - inner, 0f);
                rt.anchorMax = new Vector2(1f, 1f);
                rt.offsetMin = new Vector2(0f, -PillarOverscan);
                rt.offsetMax = new Vector2(PillarOverscan, PillarOverscan);
            }
        }

        RectTransform Make43Frame(string name, Transform parent)
        {
            var rt = NewRect(name, parent);
            rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0.5f, 0.5f);
            var fit = rt.gameObject.AddComponent<AspectRatioFitter>();
            fit.aspectMode = AspectRatioFitter.AspectMode.FitInParent;
            fit.aspectRatio = 4f / 3f;
            return rt;
        }

        RectTransform MakePanel(string name, Transform parent)
        {
            var rt = NewRect(name, parent);
            Stretch(rt);
            return rt;
        }

        // ---- shared factory (used by GlyphGrid, Hud pickups, Carriage bars) -----

        public static Font DefaultFont => Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");

        public static Text MakeText(Transform parent, Font font)
        {
            var rt = NewRect("glyph", parent);
            rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0.5f, 0.5f);
            var t = rt.gameObject.AddComponent<Text>();
            t.font = font != null ? font : DefaultFont;
            t.alignment = TextAnchor.MiddleCenter;
            t.horizontalOverflow = HorizontalWrapMode.Overflow;
            t.verticalOverflow = VerticalWrapMode.Overflow;
            t.raycastTarget = false;
            return t;
        }

        public static Image MakeImage(Transform parent, Color c)
        {
            var rt = NewRect("fill", parent);
            rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0.5f, 0.5f);
            var img = rt.gameObject.AddComponent<Image>();
            img.color = c;
            img.raycastTarget = false;
            return img;
        }

        static RectTransform NewRect(string name, Transform parent)
        {
            var go = new GameObject(name, typeof(RectTransform)) { layer = s_uiLayer };
            go.transform.SetParent(parent, false);
            return (RectTransform)go.transform;
        }

        static void Stretch(RectTransform rt)
        {
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;
        }
    }
}
