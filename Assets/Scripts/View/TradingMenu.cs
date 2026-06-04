// Full-screen trading menu on the 40x30 HUD grid. Opened by a TradingPost; it
// pauses the game (Time.timeScale = 0). Keyboard-only, mapping to a joypad:
//   Up/Down (W/S)    - select item
//   Left/Right (A/D) - sell (-) / buy (+) the selected item (arrows hint this)
//   >  (A button)    - TRADE
//   <  (B button)    - EXIT
//
// One of these in the scene; posts find it. NOTE: OnGUI / Play mode only.

using UnityEngine;
using UnityEngine.InputSystem;
using NightRider.World;

namespace NightRider.View
{
    public class TradingMenu : MonoBehaviour
    {
        // Frame the menu unpaused on, so the rider can swallow the same EXIT press.
        public static int ClosedFrame = -1;

        [Header("Data")]
        public PlayerState player;
        [Tooltip("Font for glyphs. Assign an emoji-capable font if emoji show as boxes.")]
        public Font font;

        [Header("Grid")]
        public int cols = 40;
        public int rows = 30;

        [Header("Colours")]
        public Color background = new(0.06f, 0.05f, 0.10f, 1f);
        public Color text = Color.white;
        public Color dim  = new(0.7f, 0.7f, 0.75f, 1f);
        public Color good = new(0.4f, 1f, 0.5f, 1f);
        public Color bad  = new(1f, 0.45f, 0.4f, 1f);
        public Color highlight = new(1f, 1f, 1f, 0.12f);

        public bool IsOpen { get; private set; }

        TradingPost _post;
        int[] _delta;
        int _sel;
        Texture2D _tex;
        GUIStyle _label;
        float _ox, _oy, _cw, _ch;

        const int CCursor = 3, CEmoji = 5, CName = 7, COwned = 14, CBuy = 19, CSell = 24, CDelta = 31;

        public void Open(TradingPost post)
        {
            if (player == null) player = FindAnyObjectByType<PlayerState>();
            _post = post;
            _delta = new int[player != null ? player.items.Count : 0];
            _sel = 0;
            IsOpen = true;
            Time.timeScale = 0f;
        }

        public void Close()
        {
            IsOpen = false;
            ClosedFrame = Time.frameCount;
            Time.timeScale = 1f;
        }

        void Update()
        {
            if (!IsOpen || player == null) return;
            var kb = Keyboard.current;
            int n = player.items.Count;
            if (kb == null || n == 0) return;

            if (kb.upArrowKey.wasPressedThisFrame   || kb.wKey.wasPressedThisFrame) _sel = (_sel - 1 + n) % n;
            if (kb.downArrowKey.wasPressedThisFrame || kb.sKey.wasPressedThisFrame) _sel = (_sel + 1) % n;
            if (kb.leftArrowKey.wasPressedThisFrame  || kb.aKey.wasPressedThisFrame) Adjust(-1);
            if (kb.rightArrowKey.wasPressedThisFrame || kb.dKey.wasPressedThisFrame) Adjust(+1);

            if (kb.periodKey.wasPressedThisFrame) TryCommit();   // >  = A = trade
            if (kb.commaKey.wasPressedThisFrame)  Close();        // <  = B = exit
        }

        void Adjust(int dir)
        {
            var it = player.items[_sel];
            int d = _delta[_sel] + dir;

            if (it.type == ItemType.Heads)
                d = Mathf.Clamp(d, 0, Mathf.Max(0, 1 - it.count));   // buy-only, one, kept forever
            else if (d < -it.count)
                d = -it.count;                                       // can't oversell

            _delta[_sel] = d;
        }

        int GoldChange()
        {
            int g = 0;
            for (int i = 0; i < player.items.Count; i++)
            {
                int d = _delta[i];
                g += d > 0 ? -d * _post.buyPrice : (-d) * _post.sellPrice;
            }
            return g;
        }

        bool AnyDelta()
        {
            foreach (var d in _delta) if (d != 0) return true;
            return false;
        }

        void TryCommit()
        {
            int change = GoldChange();
            if (!AnyDelta() || player.gold + change < 0) return;
            player.gold += change;
            for (int i = 0; i < player.items.Count; i++) player.Add(player.items[i].type, _delta[i]);
            System.Array.Clear(_delta, 0, _delta.Length);
        }

        void OnGUI()
        {
            if (!IsOpen || player == null || _post == null) return;
            GUI.depth = -100;

            const float aspect = 4f / 3f;
            float sw = Screen.width, sh = Screen.height, w, h;
            if (sw / sh > aspect) { h = sh; w = h * aspect; }
            else                  { w = sw; h = w / aspect; }
            _ox = (sw - w) * 0.5f; _oy = (sh - h) * 0.5f;
            _cw = w / cols; _ch = h / rows;

            EnsureStyle();
            Fill(new Rect(0, 0, sw, sh), background);

            string title = _post.postName;
            Text((cols - title.Length) / 2, 2, title, text);

            Text(CName,  5, "ITEM",  dim);
            Text(COwned, 5, "HAVE",  dim);
            Text(CBuy,   5, "BUY",   dim);
            Text(CSell,  5, "SELL",  dim);
            Text(CDelta, 5, "TRADE", dim);

            var items = player.items;
            for (int i = 0; i < items.Count; i++)
            {
                int r = 7 + i * 2;
                bool sel = i == _sel;
                if (sel) Fill(CellRect(CCursor, r, CDelta + 5 - CCursor, 1), highlight);

                var it = items[i];
                int d = _delta[i];
                bool isHead = it.type == ItemType.Heads;

                if (sel) Glyph(CCursor, r, ">", good);

                // A collected head reads as completed — collecting heads is the goal.
                if (isHead && it.count >= 1)
                {
                    Glyph(CEmoji, r, it.emoji, good);
                    Text(CName, r, it.name, good);
                    Text(COwned, r, "COLLECTED", good);
                    continue;
                }

                Glyph(CEmoji, r, it.emoji, text);
                Text(CName, r, it.name, text);
                Text(COwned, r, "x" + it.count, text);
                Text(CBuy, r, _post.buyPrice.ToString(), dim);
                Text(CSell, r, isHead ? "-" : _post.sellPrice.ToString(), dim);

                string ds = d > 0 ? "+" + d : d.ToString();
                if (sel) Glyph(CDelta - 2, r, "<", dim);
                Text(CDelta, r, ds, d == 0 ? dim : text);
                if (sel) Glyph(CDelta + ds.Length + 1, r, ">", dim);
            }

            int change = GoldChange();
            int result = player.gold + change;
            bool valid = result >= 0;

            int row = 7 + items.Count * 2 + 1;
            Text(CName, row,     "NET " + (change >= 0 ? "+" : "") + change, change >= 0 ? good : bad);
            Text(CName, row + 1, "GOLD " + player.gold + " -> " + result, valid ? text : bad);

            Text(CName,      row + 3, "A TRADE", (valid && AnyDelta()) ? good : dim);
            Text(CName + 10, row + 3, "B EXIT",  text);
        }

        // --- grid drawing -------------------------------------------------------

        Rect CellRect(int col, int row, int wc, int hc) =>
            new(_ox + col * _cw, _oy + row * _ch, wc * _cw, hc * _ch);

        void Text(int col, int row, string s, Color c)
        {
            _label.normal.textColor = c;
            for (int i = 0; i < s.Length; i++)
                GUI.Label(CellRect(col + i, row, 1, 1), s[i].ToString(), _label);
        }

        void Glyph(int col, int row, string g, Color c)
        {
            _label.normal.textColor = c;
            GUI.Label(CellRect(col, row, 1, 1), g, _label);
        }

        void EnsureStyle()
        {
            _label ??= new GUIStyle { alignment = TextAnchor.MiddleCenter };
            _label.font = font;
            _label.fontSize = Mathf.Max(6, Mathf.RoundToInt(_ch * 0.8f));

            if (_tex == null)
            {
                _tex = new Texture2D(1, 1) { hideFlags = HideFlags.HideAndDontSave };
                _tex.SetPixel(0, 0, Color.white); _tex.Apply();
            }
        }

        void Fill(Rect r, Color c)
        {
            var prev = GUI.color; GUI.color = c; GUI.DrawTexture(r, _tex); GUI.color = prev;
        }
    }
}
