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
        [Tooltip("Solid background behind the bottom HUD rows.")]
        public Color hudStripColor = Color.black;

        [Header("Pickup FX")]
        [Min(0.05f)] public float pickupDuration = 1.2f;
        [Tooltip("Seconds spent rising (solid) before it starts blinking.")]
        public float pickupRiseDuration = 0.4f;
        [Tooltip("How far a pickup rises, in grid cells.")]
        public float pickupRiseCells = 2f;
        [Tooltip("Blink half-period (seconds).")]
        public float pickupBlink = 0.12f;
        [Tooltip("Pickup token size in whole grid cells (floored to keep pixels crisp; 1 = native icon size, matches the HUD icons).")]
        public float pickupSizeCells = 1f;

        [Header("Layout")]
        [Tooltip("Overscan-unsafe rows at the very bottom (content sits above them).")]
        public int overscanRows = 1;
        [Tooltip("Reserved HUD rows, just above the bottom overscan row.")]
        public int hudRows = 4;

        [Header("Pause")]
        public Color pauseBack = Color.black;

        GlyphGrid _hud, _pause;
        readonly List<Image> _pickViews = new();
        Image _headView;
        bool _paused;

        // Snap the HUD palette to NES-legal once (these UI colours aren't snapped
        // anywhere else; keep alpha so the faint head-fill stays faint).
        void Awake()
        {
            hudText       = Nes.SnapKeepAlpha(hudText);
            goldColor     = Nes.SnapKeepAlpha(goldColor);
            hudStripColor = Nes.SnapKeepAlpha(hudStripColor);
            headFill      = Nes.SnapKeepAlpha(headFill);
            pauseBack     = Nes.SnapKeepAlpha(pauseBack);
        }

        // ----------------------------------------------------------- sim / input

        void Update()
        {
            PublishScreenArea();

            // Start toggles pause (unless the trade menu or title screen is up).
            if (!TradingMenu.Active && !TitleScreen.Active && Controls.Start)
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

            int cols = ui.cols, rows = ui.rows;
            int top = rows - overscanRows - hudRows;
            _hud.Fill(0, top, cols, hudRows + overscanRows, hudStripColor);   // solid black behind the bottom HUD rows

            if (player != null)
            {
                const int boxW = 4, boxH = 4;
                int boxCol = (cols - boxW) / 2;            // always horizontally centred

                // Inventory: two compact columns (icon x ###), left of the box.
                int colA = 5;
                int colB = boxCol - 7;
                int itemRow = top + 1;
                var items = player.items;
                var icons = ItemIcons.Instance;
                for (int i = 0; i < 3 && i < items.Count; i++)
                    DrawCompact(colA, itemRow + i, icons != null ? icons.Of(items[i].type) : null, ItemColors.Of(items[i].type), items[i].count);
                for (int i = 0; i < 3 && i + 3 < items.Count; i++)
                    DrawCompact(colB, itemRow + i, icons != null ? icons.Of(items[i + 3].type) : null, ItemColors.Of(items[i + 3].type), items[i + 3].count);

                // Head box, 4x4 (= 32x32px), horizontally centred. Faint when empty;
                // the current head's image fills it (the default slot shows no head).
                _hud.Fill(boxCol, top, boxW, boxH, headFill);
                ShowHead(ui, boxCol, top, boxW, boxH);

                // Right of the box: level then gold (item-style), dropped down.
                int rightCol = boxCol + boxW + 1;
                _hud.Run(rightCol, top + 1, player.level, hudText);
                DrawCompact(rightCol, top + 3, icons != null ? icons.Gold : null, goldColor, player.gold);
            }
            else if (_headView != null) _headView.enabled = false;
            _hud.End();
        }

        // The current head's 32x32 image in the head box (hidden on the default slot).
        void ShowHead(UiCanvas ui, int col, int row, int w, int h)
        {
            if (_headView == null)
            {
                _headView = UiCanvas.MakeImage(ui.HudPanel, Color.white);   // after the grid layers -> on top of the box
                _headView.name = "Head";
                _headView.preserveAspect = true;
            }

            var sp = MusicDirector.Instance != null ? MusicDirector.Instance.CurrentHeadSprite : null;
            var rt = _headView.rectTransform;
            float C = ui.cols, R = ui.rows;
            rt.anchorMin = new Vector2(col / C, 1f - (row + h) / R);
            rt.anchorMax = new Vector2((col + w) / C, 1f - row / R);
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;

            _headView.enabled = sp != null;
            if (sp != null) _headView.sprite = sp;
        }

        // [icon]  ×  ### (zero-padded to three digits, clamped 0-999) — 5 cells.
        // Uses the 8x8 icon sprite when available; falls back to a colour square.
        void DrawCompact(int col, int row, Sprite icon, Color tint, int count)
        {
            if (icon != null) _hud.Icon(col, row, 1, 1, icon, Color.white);
            else              _hud.Fill(col, row, 1, 1, tint);
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
                _pickViews.Add(img);
            }

            float cw = ui.CellWidth(ui.Frame), ch = ui.CellHeight(ui.Frame);
            float px = cw / 8f;                  // screen px per NES pixel (8 NES px / cell)
            var icons = ItemIcons.Instance;

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

                // Snap position to the NES pixel grid (chunky, grid-aligned motion).
                ui.ScreenToFrame(ui.Frame, new Vector3(p.screen.x, p.screen.y, 0f), out var local);
                v.rectTransform.anchoredPosition = new Vector2(
                    Mathf.Round(local.x / px) * px,
                    Mathf.Round((local.y + rise) / px) * px);

                // Whole cells -> integer icon scale (crisp); 1 cell matches the HUD icons.
                int cells = Mathf.Max(1, Mathf.FloorToInt(pickupSizeCells));
                float sizeNes = cells * 8f;
                v.rectTransform.sizeDelta = new Vector2(sizeNes * px, sizeNes * px);

                var sp = icons != null ? icons.Of(p.type) : null;
                v.sprite = sp;
                v.color = sp != null ? Color.white : ItemColors.Of(p.type);
            }
        }
    }
}
