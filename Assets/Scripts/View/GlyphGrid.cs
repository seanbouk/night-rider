// Retained-mode, pooled drawer over the 40x30 UI grid. Mimics the old IMGUI calls
// (one glyph per cell, cell-rect fills) so the HUD/menu draw code ports almost 1:1
// — but the glyphs are pooled UGUI Text/Image under the screen-space canvas, so the
// CRT pass covers them. Call Begin(), issue Glyph/Run/Fill, then End() each frame.
//
// Fills live on a layer behind the text layer, so backgrounds/highlights never
// cover glyphs regardless of allocation order.

using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace NightRider.View
{
    public class GlyphGrid
    {
        readonly UiCanvas _ui;
        readonly RectTransform _fillLayer;
        readonly RectTransform _textLayer;
        readonly Font _font;
        readonly List<Text> _texts = new();
        readonly List<Image> _images = new();
        int _ti, _ii;

        public GlyphGrid(UiCanvas ui, RectTransform parent, Font font)
        {
            _ui = ui;
            _font = font != null ? font : UiCanvas.DefaultFont;
            _fillLayer = Layer("Fills", parent);   // behind
            _textLayer = Layer("Text", parent);    // in front
        }

        public void Begin() { _ti = 0; _ii = 0; }

        public void End()
        {
            for (int i = _ti; i < _texts.Count; i++)  _texts[i].gameObject.SetActive(false);
            for (int i = _ii; i < _images.Count; i++) _images[i].gameObject.SetActive(false);
        }

        public void Glyph(int col, int row, string ch, Color c)
        {
            var t = NextText();
            SetCell(t.rectTransform, col, row, 1, 1);
            t.text = ch;
            t.color = c;
            t.fontSize = FontSize();
        }

        public void Run(int col, int row, string s, Color c)
        {
            for (int i = 0; i < s.Length; i++) Glyph(col + i, row, s[i].ToString(), c);
        }

        public void Fill(int col, int row, int wCells, int hCells, Color c)
        {
            var img = NextImage();
            SetCell(img.rectTransform, col, row, wCells, hCells);
            img.color = c;
        }

        // ---- internals ---------------------------------------------------------

        int FontSize() => Mathf.Max(6, Mathf.RoundToInt(_textLayer.rect.height / _ui.rows * 0.8f));

        void SetCell(RectTransform rt, int col, int row, int w, int h)
        {
            float C = _ui.cols, R = _ui.rows;
            rt.anchorMin = new Vector2(col / C, 1f - (row + h) / R);   // row 0 at top
            rt.anchorMax = new Vector2((col + w) / C, 1f - row / R);
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;
        }

        Text NextText()
        {
            Text t = _ti < _texts.Count ? _texts[_ti] : null;
            if (t == null) { t = UiCanvas.MakeText(_textLayer, _font); _texts.Add(t); }
            _ti++;
            if (!t.gameObject.activeSelf) t.gameObject.SetActive(true);
            return t;
        }

        Image NextImage()
        {
            Image img = _ii < _images.Count ? _images[_ii] : null;
            if (img == null) { img = UiCanvas.MakeImage(_fillLayer, Color.white); _images.Add(img); }
            _ii++;
            if (!img.gameObject.activeSelf) img.gameObject.SetActive(true);
            return img;
        }

        static RectTransform Layer(string name, RectTransform parent)
        {
            var go = new GameObject(name, typeof(RectTransform));
            var rt = (RectTransform)go.transform;
            rt.SetParent(parent, false);
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;
            return rt;
        }
    }
}
