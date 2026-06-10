// The game HUD, drawn on the 40x30 UI grid (one glyph per 8x8 cell) via the
// screen-space UiCanvas, so the CRT pass covers it.
//
// Content: a 4x4 head box (the player's head, later) horizontally centred; two
// compact inventory columns to its left (emoji x ###); level + gold to its right.
// Layout is code-driven so the centring can't drift. The black NES side pillars
// are drawn by UiCanvas (not here).
//
// NOTE: emoji need an emoji-capable font — if the item glyphs show as boxes, assign
// one to Hud Font (or edit the emoji strings).

using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
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
        public Color hudText  = Color.white;
        public Color headFill = new(1f, 1f, 1f, 0.06f);
        [Tooltip("Placeholder square for the gold counter.")]
        public Color goldColor = new(1f, 0.84f, 0f);

        [Header("Pickup FX")]
        [Min(0.05f)] public float pickupDuration = 1.2f;
        [Tooltip("Seconds spent rising (solid) before it starts blinking.")]
        public float pickupRiseDuration = 0.4f;
        [Tooltip("How far a pickup rises, in grid cells.")]
        public float pickupRiseCells = 2f;
        [Tooltip("Blink half-period (seconds).")]
        public float pickupBlink = 0.12f;
        [Tooltip("Pickup token size, in grid cells. (Colour comes from the item.)")]
        public float pickupSizeCells = 1.5f;

        [Header("Layout")]
        [Tooltip("Overscan-unsafe rows at the very bottom (content sits above them).")]
        public int overscanRows = 1;
        [Tooltip("Reserved HUD rows, just above the bottom overscan row.")]
        public int hudRows = 4;

        [Header("Pause")]
        public Color pauseBack = Color.black;

        GlyphGrid _hud, _pause;
        readonly List<Image> _pickViews = new();
        bool _paused;

        // ----------------------------------------------------------- sim / input

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

        // 4:3 active-area screen rect (px), for shaders that dither at the 320px
        // virtual width (the attack apparition / trading-post ghost) + the per-frame
        // ghost-flicker parity. (Unchanged from the IMGUI version.)
        void PublishScreenArea()
        {
            const float aspect = 4f / 3f;
            float sw = Screen.width, sh = Screen.height, w, h;
            if (sw / sh > aspect) { h = sh; w = h * aspect; }
            else                  { w = sw; h = w / aspect; }
            Shader.SetGlobalFloat("_HudAreaX", (sw - w) * 0.5f);
            Shader.SetGlobalFloat("_HudAreaW", w);
            Shader.SetGlobalFloat("_GhostFlicker", Time.frameCount & 1);
        }

        // ----------------------------------------------------------- drawing

        void LateUpdate()
        {
            if (player == null) player = FindAnyObjectByType<PlayerState>();
            var ui = UiCanvas.Instance;
            _hud   ??= new GlyphGrid(ui, ui.HudPanel, hudFont);
            _pause ??= new GlyphGrid(ui, ui.PausePanel, hudFont);

            DrawContent(ui);
            DrawPickups(ui);
            DrawPause(ui);
        }

        void DrawContent(UiCanvas ui)
        {
            _hud.Begin();
            if (player != null)
            {
                int cols = ui.cols, rows = ui.rows;
                int top = rows - overscanRows - hudRows;
                const int boxW = 4, boxH = 4;
                int boxCol = (cols - boxW) / 2;            // always horizontally centred

                // Inventory: two compact columns (emoji x ###), left of the box.
                int colA = 5;
                int colB = boxCol - 7;
                int itemRow = top + 1;
                var items = player.items;
                for (int i = 0; i < 3 && i < items.Count; i++)     DrawCompact(colA, itemRow + i, ItemColors.Of(items[i].type), items[i].count);
                for (int i = 0; i < 3 && i + 3 < items.Count; i++) DrawCompact(colB, itemRow + i, ItemColors.Of(items[i + 3].type), items[i + 3].count);

                // Head box, 4x4, horizontally centred (faint reserved slot).
                _hud.Fill(boxCol, top, boxW, boxH, headFill);

                // Right of the box: level then gold (item-style), dropped down.
                int rightCol = boxCol + boxW + 1;
                _hud.Run(rightCol, top + 1, player.level, hudText);
                DrawCompact(rightCol, top + 3, goldColor, player.gold);
            }
            _hud.End();
        }

        // [icon]  ×  ### (zero-padded to three digits, clamped 0-999) — 5 cells.
        // Icon is a placeholder full-cell colour square until real 8x8 sprites land.
        void DrawCompact(int col, int row, Color icon, int count)
        {
            _hud.Fill(col, row, 1, 1, icon);
            _hud.Glyph(col + 1, row, "×", hudText);
            _hud.Run(col + 2, row, Mathf.Clamp(count, 0, 999).ToString("D3"), hudText);
        }

        void DrawPause(UiCanvas ui)
        {
            _pause.Begin();
            if (_paused)
            {
                const string msg = "PAUSE";
                int c = (ui.cols - msg.Length) / 2;
                int r = ui.rows / 2;
                _pause.Fill(c - 2, r - 1, msg.Length + 4, 3, pauseBack);
                _pause.Run(c, r, msg, Color.white);
            }
            _pause.End();
        }

        // ----------------------------------------------------------- pickups

        class Pickup { public ItemType type; public Vector2 screen; public float elapsed; }
        readonly List<Pickup> _pickups = new();

        // HUD-space pickup at a world point: a gem appears there, rises, blinks, then
        // vanishes and credits the item. (Public API unchanged.)
        public void SpawnPickup(Vector3 worldPos, ItemType type)
        {
            if (player == null) player = FindAnyObjectByType<PlayerState>();
            var cam = Camera.main;
            if (cam == null) return;

            Vector3 sp = cam.WorldToScreenPoint(worldPos);
            if (sp.z <= 0f) return;

            _pickups.Add(new Pickup { type = type, screen = new Vector2(sp.x, sp.y) });
        }

        void DrawPickups(UiCanvas ui)
        {
            int n = _pickups.Count;
            while (_pickViews.Count < n)
            {
                var img = UiCanvas.MakeImage(ui.PickupPanel, Color.white);
                img.rectTransform.localRotation = Quaternion.Euler(0f, 0f, 45f);   // diamond/gem
                _pickViews.Add(img);
            }

            float cw = ui.CellWidth(ui.Frame), ch = ui.CellHeight(ui.Frame);

            for (int i = 0; i < _pickViews.Count; i++)
            {
                var v = _pickViews[i];
                if (i >= n) { v.enabled = false; continue; }

                var p = _pickups[i];
                // Phase 1: rise (solid). Phase 2: hold at top and blink.
                float riseK = Mathf.Clamp01(p.elapsed / pickupRiseDuration);
                float rise  = pickupRiseCells * ch * riseK;
                bool visible = p.elapsed < pickupRiseDuration
                    || Mathf.Repeat(p.elapsed - pickupRiseDuration, pickupBlink * 2f) < pickupBlink;

                v.enabled = visible;
                if (!visible) continue;

                ui.ScreenToFrame(ui.Frame, new Vector3(p.screen.x, p.screen.y, 0f), out var local);
                v.rectTransform.anchoredPosition = new Vector2(local.x, local.y + rise);
                v.rectTransform.sizeDelta = new Vector2(pickupSizeCells * cw, pickupSizeCells * ch);
                v.color = ItemColors.Of(p.type);
            }
        }
    }
}
