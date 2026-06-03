// Development HUD. For now it only draws a framing overlay so the game can be
// composed against its target display before any art/shader work is committed:
//
//   - a 4:3 area, zoom-to-fit and centred (outside it is left transparent so the
//     full wide view stays usable for debugging),
//   - a pale graph-paper grid over a 40x30 layout (square cells),
//   - solid-black side pillars (the NES only really used the middle columns),
//   - 50% black top & bottom rows (unsafe from overscan),
//   - a 50% purple strip (reserved for the HUD) just above the bottom row,
//   - two amber verticals marking the central 35 columns: the NES drew 280 px
//     (35 tiles) across the full width (pixels weren't square). We keep square
//     pixels for now; the amber lines just mark where that active width sits.
//
// Real HUD elements will live in this component later; the overlay is an
// optional (default-on) dev aid. NOTE: OnGUI only draws in Play mode.

using UnityEngine;

namespace NightRider.View
{
    public class Hud : MonoBehaviour
    {
        [Tooltip("Draw the 4:3 / 40x30 framing overlay (development aid).")]
        public bool showSafeArea = true;

        [Header("Grid")]
        public int cols = 40;
        public int rows = 30;
        [Tooltip("Solid-black pillar columns on each side.")]
        public int sideColumns = 3;
        [Tooltip("Overscan-unsafe rows at the very top and very bottom.")]
        public int overscanRows = 1;
        [Tooltip("Reserved HUD rows, just above the bottom overscan row.")]
        public int hudRows = 3;
        [Tooltip("Central columns the NES active width (280px / 35 tiles) covers.")]
        public int centerColumns = 35;

        [Header("Lines")]
        public float gridThickness = 1f;
        public float amberThickness = 2f;

        [Header("Colours")]
        public Color pillar   = Color.black;
        public Color overscan = new(0f, 0f, 0f, 0.5f);
        public Color hud      = new(0.5f, 0f, 0.5f, 0.5f);
        public Color grid     = new(1f, 1f, 1f, 0.15f);
        public Color amber    = new(1f, 0.65f, 0f, 0.85f);

        Texture2D _tex;

        void OnGUI()
        {
            if (!showSafeArea) return;
            EnsureTexture();

            // Zoom-to-fit a 4:3 area, centred. Outside it is left transparent.
            const float aspect = 4f / 3f;
            float sw = Screen.width, sh = Screen.height;
            float w, h;
            if (sw / sh > aspect) { h = sh; w = h * aspect; }   // wide screen: fit by height
            else                  { w = sw; h = w / aspect; }   // tall screen: fit by width
            float ox = (sw - w) * 0.5f;
            float oy = (sh - h) * 0.5f;

            float cw = w / cols, ch = h / rows;

            // Rect for a grid span: columns [c0,c1), rows [r0,r1) from the top-left.
            Rect Cell(int c0, int c1, int r0, int r1) =>
                new(ox + c0 * cw, oy + r0 * ch, (c1 - c0) * cw, (r1 - r0) * ch);

            // Region tints.
            Fill(Cell(0, cols, 0, overscanRows), overscan);
            Fill(Cell(0, cols, rows - overscanRows, rows), overscan);
            Fill(Cell(0, cols, rows - overscanRows - hudRows, rows - overscanRows), hud);

            // Side pillars.
            Fill(Cell(0, sideColumns, 0, rows), pillar);
            Fill(Cell(cols - sideColumns, cols, 0, rows), pillar);

            // Graph-paper grid, over everything.
            float gt = gridThickness * 0.5f;
            for (int c = 0; c <= cols; c++) { float gx = ox + c * cw; Fill(new Rect(gx - gt, oy, gridThickness, h), grid); }
            for (int r = 0; r <= rows; r++) { float gy = oy + r * ch; Fill(new Rect(ox, gy - gt, w, gridThickness), grid); }

            // Amber verticals marking the central NES-active columns.
            float margin = (cols - centerColumns) * 0.5f;
            float at = amberThickness * 0.5f;
            float lx = ox + margin * cw;
            float rx = ox + (cols - margin) * cw;
            Fill(new Rect(lx - at, oy, amberThickness, h), amber);
            Fill(new Rect(rx - at, oy, amberThickness, h), amber);
        }

        void Fill(Rect r, Color c)
        {
            var prev = GUI.color;
            GUI.color = c;
            GUI.DrawTexture(r, _tex);
            GUI.color = prev;
        }

        void EnsureTexture()
        {
            if (_tex != null) return;
            _tex = new Texture2D(1, 1) { hideFlags = HideFlags.HideAndDontSave };
            _tex.SetPixel(0, 0, Color.white);
            _tex.Apply();
        }
    }
}
