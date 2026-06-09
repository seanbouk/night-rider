// The screen-space canvas that ALL game UI lives on, built at runtime. It renders
// in Screen Space - Camera mode so URP draws it into the camera colour BEFORE the
// CRT fullscreen pass (AfterPostProcessing) — which is what lets the CRT mosaic,
// scanlines, bloom, and horizontal blur cover the HUD and shop, and bleed active
// colour into the black side pillars.
//
// Layout mirrors the old IMGUI framing: a centred 4:3 area on a 40x30 grid where
// each cell is one 8x8 NES tile. The black SIDE PILLARS (the TV doesn't reach the
// edge) are real Images on the canvas so the blur bleeds into them. Auto-creates
// itself if absent; add it to a scene object to tweak the inspector values.

using UnityEngine;
using UnityEngine.UI;

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
        [Tooltip("Black NES side pillars: this many columns each side (the TV doesn't reach the edge).")]
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

        Canvas _canvas;
        bool _built;

        void Awake()
        {
            if (_instance != null && _instance != this) { Destroy(this); return; }
            _instance = this;
            Build();
        }

        void OnDestroy() { if (_instance == this) _instance = null; }

        void LateUpdate()
        {
            if (_canvas != null && _canvas.worldCamera == null) _canvas.worldCamera = Camera.main;
        }

        public Camera Cam => _canvas != null && _canvas.worldCamera != null ? _canvas.worldCamera : Camera.main;

        // Screen point (px, origin bottom-left) -> local anchored position in `frame`.
        public bool ScreenToFrame(RectTransform frame, Vector3 screenPos, out Vector2 local)
            => RectTransformUtility.ScreenPointToLocalPointInRectangle(frame, screenPos, Cam, out local);

        public float CellWidth(RectTransform frame)  => frame.rect.width  / cols;
        public float CellHeight(RectTransform frame) => frame.rect.height / rows;

        // ---- build -------------------------------------------------------------

        void Build()
        {
            if (_built) return;
            _built = true;

            var go = new GameObject("GameUiCanvas", typeof(RectTransform));
            go.transform.SetParent(transform, false);
            _canvas = go.AddComponent<Canvas>();
            _canvas.renderMode = RenderMode.ScreenSpaceCamera;
            _canvas.worldCamera = Camera.main;
            _canvas.planeDistance = planeDistance;
            _canvas.sortingOrder = 100;
            var scaler = go.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ConstantPixelSize;
            var root = (RectTransform)go.transform;

            Frame       = Make43Frame("Frame", root);
            WorldBars   = MakePanel("WorldBars", Frame);
            HudPanel    = MakePanel("Hud", Frame);
            PickupPanel = MakePanel("Pickups", Frame);
            PausePanel  = MakePanel("Pause", Frame);

            MenuRoot = MakePanel("Menu", root);                  // full-screen, after Frame -> on top
            var bg = MakeImage(MenuRoot, menuBackground);
            bg.name = "Bg";
            Stretch(bg.rectTransform);
            MenuFrame = Make43Frame("MenuFrame", MenuRoot);
            MenuRoot.gameObject.SetActive(false);

            BuildPillars();
        }

        void BuildPillars()
        {
            if (sideColumns <= 0) return;
            AddPillar("PillarL", 0f, (float)sideColumns / cols);
            AddPillar("PillarR", (float)(cols - sideColumns) / cols, 1f);
        }

        void AddPillar(string name, float xMin, float xMax)
        {
            var img = MakeImage(HudPanel, pillarColor);
            img.name = name;
            var rt = img.rectTransform;
            rt.anchorMin = new Vector2(xMin, 0f);
            rt.anchorMax = new Vector2(xMax, 1f);
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;
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
            var go = new GameObject(name, typeof(RectTransform));
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
