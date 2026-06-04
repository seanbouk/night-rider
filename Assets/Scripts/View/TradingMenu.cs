// Full-screen trading menu on the 40x30 HUD grid. Opened by a TradingPost; it
// pauses the game (Time.timeScale = 0). You adjust a buy(+)/sell(-) delta per
// item, see the running net gold (red if you can't afford it), then TRADE to
// apply the whole thing atomically — or EXIT to cancel and unpause.
//
// One of these in the scene; posts find it. NOTE: OnGUI / Play mode only.

using UnityEngine;
using NightRider.World;

namespace NightRider.View
{
    public class TradingMenu : MonoBehaviour
    {
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

        public bool IsOpen { get; private set; }

        TradingPost _post;
        int[] _delta;                 // per item: + buying, - selling
        Texture2D _tex;
        GUIStyle _label;
        float _ox, _oy, _cw, _ch;

        // Item row columns.
        const int CEmoji = 4, CName = 6, COwned = 13, CBuy = 17, CSell = 21,
                  CMinus = 26, CDelta = 28, CPlus = 31, CLine = 34;

        public void Open(TradingPost post)
        {
            if (player == null) player = FindAnyObjectByType<PlayerState>();
            _post = post;
            _delta = new int[player != null ? player.items.Count : 0];
            IsOpen = true;
            Time.timeScale = 0f;
        }

        public void Close()
        {
            IsOpen = false;
            Time.timeScale = 1f;
        }

        void OnGUI()
        {
            if (!IsOpen || player == null || _post == null) return;
            GUI.depth = -100;                 // on top of the HUD

            // Grid geometry (same 4:3 zoom-to-fit as the HUD).
            const float aspect = 4f / 3f;
            float sw = Screen.width, sh = Screen.height, w, h;
            if (sw / sh > aspect) { h = sh; w = h * aspect; }
            else                  { w = sw; h = w / aspect; }
            _ox = (sw - w) * 0.5f; _oy = (sh - h) * 0.5f;
            _cw = w / cols; _ch = h / rows;

            EnsureStyle();
            Fill(new Rect(0, 0, sw, sh), background);

            Text(CName, 2, "TRADING  POST", text);

            // Headers.
            Text(CName,  5, "ITEM",  dim);
            Text(COwned, 5, "HAVE",  dim);
            Text(CBuy,   5, "BUY",   dim);
            Text(CSell,  5, "SELL",  dim);
            Text(CDelta - 1, 5, "TRADE", dim);

            var items = player.items;
            int goldChange = 0;

            for (int i = 0; i < items.Count; i++)
            {
                int r = 7 + i * 2;
                var it = items[i];
                int d = _delta[i];

                Glyph(CEmoji, r, it.emoji, text);
                Text(CName, r, it.name, text);
                Text(COwned, r, "x" + it.count, text);
                Text(CBuy, r, _post.buyPrice.ToString(), dim);
                Text(CSell, r, _post.sellPrice.ToString(), dim);

                // -  delta  +
                bool canSellMore = d > -it.count;
                GUI.enabled = canSellMore;
                if (GUI.Button(CellRect(CMinus, r, 2, 1), "-")) _delta[i] = d - 1;
                GUI.enabled = true;
                Text(CDelta, r, d > 0 ? "+" + d : d.ToString(), d == 0 ? dim : text);
                if (GUI.Button(CellRect(CPlus, r, 2, 1), "+")) _delta[i] = d + 1;

                // Line gold effect, and accumulate the net.
                int line = d > 0 ? -d * _post.buyPrice : (-d) * _post.sellPrice;
                goldChange += line;
                if (d != 0) Text(CLine, r, (line >= 0 ? "+" : "") + line, line >= 0 ? good : bad);
            }

            int resultGold = player.gold + goldChange;
            bool valid = resultGold >= 0;

            int totalsRow = 7 + items.Count * 2 + 1;
            Text(CName, totalsRow, "NET " + (goldChange >= 0 ? "+" : "") + goldChange, goldChange >= 0 ? good : bad);
            Text(CName, totalsRow + 1, "GOLD " + player.gold + " -> " + resultGold, valid ? text : bad);

            int btnRow = totalsRow + 3;
            GUI.enabled = valid && AnyDelta();
            if (GUI.Button(CellRect(CName, btnRow, 8, 2), "TRADE")) Commit(goldChange);
            GUI.enabled = true;
            if (GUI.Button(CellRect(CName + 10, btnRow, 6, 2), "EXIT")) Close();
        }

        bool AnyDelta()
        {
            foreach (var d in _delta) if (d != 0) return true;
            return false;
        }

        void Commit(int goldChange)
        {
            player.gold += goldChange;
            for (int i = 0; i < player.items.Count; i++) player.Add(player.items[i].type, _delta[i]);
            System.Array.Clear(_delta, 0, _delta.Length);
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
