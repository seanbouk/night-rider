// The game HUD, drawn on a strict 40x30 grid (one glyph per cell).
//
// Content (always drawn): a 4x4 head box (the player's head, later) computed
// horizontally centred; two compact inventory columns to its left (emoji x ###,
// three digits — names are kept in data but not shown here); and level + gold to
// its right. Layout is code-driven so the centring can't drift.
//
// Plus an optional (default-on) framing overlay: the 4:3 zoom-to-fit area, a
// pale graph-paper grid, NES side pillars, overscan rows, the HUD strip tint,
// and amber lines marking the central 35-column active width.
//
// NOTE: OnGUI only draws in Play mode. Emoji need an emoji-capable font — if the
// item glyphs show as boxes, assign one to Hud Font (or edit the emoji strings).

using System.Collections.Generic;
using UnityEngine;
using NightRider.World;

namespace NightRider.View
{
    public class Hud : MonoBehaviour
    {
        [Header("Data")]
        public PlayerState player;
        [Tooltip("Font for HUD glyphs. Assign an emoji-capable font if emoji show as boxes.")]
        public Font hudFont;

        [Header("Content colours")]
        public Color hudText    = Color.white;
        public Color headFill   = new(1f, 1f, 1f, 0.06f);
        public Color headBorder = new(1f, 1f, 1f, 0.5f);

        [Header("Pickup FX")]
        [Min(0.05f)] public float pickupDuration = 1.2f;
        [Tooltip("Seconds spent rising (solid) before it starts blinking.")]
        public float pickupRiseDuration = 0.4f;
        [Tooltip("How far a pickup rises, in grid cells.")]
        public float pickupRiseCells = 2f;
        [Tooltip("Blink half-period (seconds).")]
        public float pickupBlink = 0.12f;

        [Header("Framing overlay")]
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
        public int hudRows = 4;
        [Tooltip("Central columns the NES active width (280px / 35 tiles) covers.")]
        public int centerColumns = 35;

        [Header("Lines")]
        public float gridThickness = 1f;
        public float amberThickness = 2f;

        [Header("Overlay colours")]
        public Color pillar   = Color.black;
        public Color overscan = new(0f, 0f, 0f, 0.5f);
        public Color hudStrip = new(0.5f, 0f, 0.5f, 0.5f);
        public Color grid     = new(1f, 1f, 1f, 0.15f);
        public Color amber    = new(1f, 0.65f, 0f, 0.85f);

        [Header("Pause")]
        public Color pauseBack = Color.black;

        Texture2D _tex;
        GUIStyle _label;
        bool _paused;
        float _ox, _oy, _cw, _ch;   // grid origin + cell size, set each OnGUI

        void OnGUI()
        {
            EnsureTexture();

            // Zoom-to-fit a 4:3 area, centred.
            const float aspect = 4f / 3f;
            float sw = Screen.width, sh = Screen.height;
            float w, h;
            if (sw / sh > aspect) { h = sh; w = h * aspect; }
            else                  { w = sw; h = w / aspect; }
            _ox = (sw - w) * 0.5f;
            _oy = (sh - h) * 0.5f;
            _cw = w / cols;
            _ch = h / rows;

            EnsureLabel();
            if (showSafeArea) DrawOverlay(w, h);
            DrawContent();
            DrawPickups();
            if (_paused) DrawPause();
        }

        // 4:3 active-area screen rect (px), for shaders that dither at the 320px
        // virtual width (the attack apparition).
        void PublishScreenArea()
        {
            const float aspect = 4f / 3f;
            float sw = Screen.width, sh = Screen.height, w, h;
            if (sw / sh > aspect) { h = sh; w = h * aspect; }
            else                  { w = sw; h = w / aspect; }
            Shader.SetGlobalFloat("_HudAreaX", (sw - w) * 0.5f);
            Shader.SetGlobalFloat("_HudAreaW", w);
        }

        void DrawPause()
        {
            const string msg = "PAUSE";
            int c = (cols - msg.Length) / 2;
            int r = rows / 2;
            Fill(new Rect(_ox + (c - 2) * _cw, _oy + (r - 1) * _ch, (msg.Length + 4) * _cw, 3 * _ch), pauseBack);
            _label.normal.textColor = Color.white;
            for (int i = 0; i < msg.Length; i++)
                GUI.Label(new Rect(_ox + (c + i) * _cw, _oy + r * _ch, _cw, _ch), msg[i].ToString(), _label);
        }

        void EnsureLabel()
        {
            _label ??= new GUIStyle { alignment = TextAnchor.MiddleCenter };
            _label.font = hudFont;                 // null = default font
            _label.normal.textColor = hudText;
            _label.fontSize = Mathf.Max(6, Mathf.RoundToInt(_ch * 0.8f));
        }

        // ---------------------------------------------------------------- content

        void DrawContent()
        {
            if (player == null) player = FindAnyObjectByType<PlayerState>();
            if (player == null) return;

            int top = rows - overscanRows - hudRows;
            const int boxW = 4, boxH = 4;
            int boxCol = (cols - boxW) / 2;            // always horizontally centred

            // Inventory: two compact columns (emoji x ###), both left of the box,
            // dropped to the lower three rows.
            int colA = 5;
            int colB = boxCol - 7;
            int itemRow = top + 1;
            var items = player.items;
            for (int i = 0; i < 3 && i < items.Count; i++)     DrawCompact(colA, itemRow + i, items[i].emoji, items[i].count);
            for (int i = 0; i < 3 && i + 3 < items.Count; i++) DrawCompact(colB, itemRow + i, items[i + 3].emoji, items[i + 3].count);

            // Head box, 4x4, horizontally centred (fills the strip).
            DrawBox(boxCol, top, boxW, boxH);

            // Right of the box: level then gold (item-style), dropped down.
            int rightCol = boxCol + boxW + 1;
            DrawRun(rightCol, top + 1, player.level);
            DrawCompact(rightCol, top + 3, "💰", player.gold);
        }

        // emoji  ×  ### (zero-padded to three digits, clamped 0-999) — 5 cells.
        void DrawCompact(int col, int row, string emoji, int count)
        {
            DrawGlyph(col, row, emoji);
            DrawGlyph(col + 1, row, "×");
            DrawRun(col + 2, row, Mathf.Clamp(count, 0, 999).ToString("D3"));
        }

        void DrawBox(int col, int row, int wCells, int hCells)
        {
            var r = new Rect(_ox + col * _cw, _oy + row * _ch, wCells * _cw, hCells * _ch);
            Fill(r, headFill);
            const float b = 2f;
            Fill(new Rect(r.x, r.y, r.width, b), headBorder);
            Fill(new Rect(r.x, r.yMax - b, r.width, b), headBorder);
            Fill(new Rect(r.x, r.y, b, r.height), headBorder);
            Fill(new Rect(r.xMax - b, r.y, b, r.height), headBorder);
        }

        // Draw a run of single-cell characters from (col,row) rightward.
        void DrawRun(int col, int row, string s)
        {
            for (int i = 0; i < s.Length; i++) DrawGlyph(col + i, row, s[i].ToString());
        }

        // Draw one glyph (string so emoji surrogate pairs stay intact) in one cell.
        void DrawGlyph(int col, int row, string glyph)
        {
            GUI.Label(new Rect(_ox + col * _cw, _oy + row * _ch, _cw, _ch), glyph, _label);
        }

        // ---------------------------------------------------------------- pickups

        class Pickup { public string glyph; public ItemType type; public Vector2 gui; public float elapsed; }
        readonly List<Pickup> _pickups = new();

        // HUD-space pickup at a world point: it appears there, rises, blinks, then
        // vanishes and credits the item.
        public void SpawnPickup(Vector3 worldPos, ItemType type)
        {
            if (player == null) player = FindAnyObjectByType<PlayerState>();
            var cam = Camera.main;
            if (cam == null) return;

            Vector3 sp = cam.WorldToScreenPoint(worldPos);
            if (sp.z <= 0f) return;

            string glyph = type.ToString();
            if (player != null)
                foreach (var it in player.items) if (it.type == type) { glyph = it.emoji; break; }

            _pickups.Add(new Pickup { glyph = glyph, type = type, gui = new Vector2(sp.x, Screen.height - sp.y) });
        }

        void Update()
        {
            PublishScreenArea();

            // Start toggles pause (unless the trade menu, which has its own pause, is up).
            if (!TradingMenu.Active && Controls.Start)
            {
                _paused = !_paused;
                Time.timeScale = _paused ? 0f : 1f;
            }

            if (_pickups.Count == 0) return;
            if (player == null) player = FindAnyObjectByType<PlayerState>();

            for (int i = _pickups.Count - 1; i >= 0; i--)
            {
                _pickups[i].elapsed += Time.deltaTime;
                if (_pickups[i].elapsed >= pickupDuration)
                {
                    if (player != null) player.Add(_pickups[i].type, 1);   // credit on vanish
                    _pickups.RemoveAt(i);
                }
            }
        }

        void DrawPickups()
        {
            foreach (var p in _pickups)
            {
                // Phase 1: rise (solid). Phase 2: hold at top and blink.
                float riseK = Mathf.Clamp01(p.elapsed / pickupRiseDuration);
                float y = p.gui.y - pickupRiseCells * _ch * riseK;

                bool visible = p.elapsed < pickupRiseDuration
                    || Mathf.Repeat(p.elapsed - pickupRiseDuration, pickupBlink * 2f) < pickupBlink;
                if (!visible) continue;

                GUI.Label(new Rect(p.gui.x - _cw * 0.5f, y - _ch * 0.5f, _cw, _ch), p.glyph, _label);
            }
        }

        // ---------------------------------------------------------------- overlay

        void DrawOverlay(float w, float h)
        {
            Rect Cell(int c0, int c1, int r0, int r1) =>
                new(_ox + c0 * _cw, _oy + r0 * _ch, (c1 - c0) * _cw, (r1 - r0) * _ch);

            // Region tints.
            Fill(Cell(0, cols, 0, overscanRows), overscan);
            Fill(Cell(0, cols, rows - overscanRows, rows), overscan);
            Fill(Cell(0, cols, rows - overscanRows - hudRows, rows - overscanRows), hudStrip);

            // Side pillars.
            Fill(Cell(0, sideColumns, 0, rows), pillar);
            Fill(Cell(cols - sideColumns, cols, 0, rows), pillar);

            // Graph-paper grid.
            float gt = gridThickness * 0.5f;
            for (int c = 0; c <= cols; c++) { float gx = _ox + c * _cw; Fill(new Rect(gx - gt, _oy, gridThickness, h), grid); }
            for (int r = 0; r <= rows; r++) { float gy = _oy + r * _ch; Fill(new Rect(_ox, gy - gt, w, gridThickness), grid); }

            // Amber verticals marking the central NES-active columns.
            float margin = (cols - centerColumns) * 0.5f;
            float at = amberThickness * 0.5f;
            Fill(new Rect(_ox + margin * _cw - at, _oy, amberThickness, h), amber);
            Fill(new Rect(_ox + (cols - margin) * _cw - at, _oy, amberThickness, h), amber);
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
