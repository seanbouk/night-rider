// Full-screen trading menu on the 40x30 UI grid, drawn via the screen-space
// UiCanvas (so the CRT pass covers it). Opened by a TradingPost; it pauses the
// game (Time.timeScale = 0). Keyboard-only, mapping to a joypad:
//   Up/Down (W/S)    - select item
//   Left/Right (A/D) - sell (-) / buy (+) the selected item (arrows hint this);
//                      the first sell-press from zero dumps the whole stack
//   >  (A button)    - TRADE
//   <  (B button)    - EXIT
//
// One of these in the scene; posts find it. The opaque backdrop is drawn by
// UiCanvas (MenuRoot); this only fills in the grid text + selection highlight.

using UnityEngine;
using NightRider.World;

namespace NightRider.View
{
    public class TradingMenu : MonoBehaviour
    {
        // Frame the menu unpaused on, so the rider can swallow the same EXIT press.
        public static int ClosedFrame = -1;
        public static bool Active;   // true while a menu is open (pause defers to it)

        [Header("Data")]
        public PlayerState player;
        [Tooltip("Font for glyphs. Assign an emoji-capable font if emoji show as boxes.")]
        public Font font;

        [Header("Colours")]
        public Color text = Color.white;
        public Color dim  = new(0.7f, 0.7f, 0.75f, 1f);
        public Color good = new(0.4f, 1f, 0.5f, 1f);
        public Color bad  = new(1f, 0.45f, 0.4f, 1f);
        public Color highlight = new(1f, 1f, 1f, 0.12f);

        public bool IsOpen { get; private set; }

        TradingPost _post;
        int[] _delta;
        int _sel;
        GlyphGrid _menu;

        const int CCursor = 3, CEmoji = 5, CName = 7, COwned = 14, CBuy = 19, CSell = 24, CDelta = 31;

        public void Open(TradingPost post)
        {
            if (player == null) player = FindAnyObjectByType<PlayerState>();
            _post = post;
            _delta = new int[player != null ? player.items.Count : 0];
            _sel = 0;
            IsOpen = true;
            Active = true;
            Time.timeScale = 0f;
            Sfx.Play(SfxId.OpenShop);
        }

        public void Close()
        {
            IsOpen = false;
            Active = false;
            ClosedFrame = Time.frameCount;
            Time.timeScale = 1f;
            Sfx.Play(SfxId.CloseShop);
        }

        void Update()
        {
            if (!IsOpen || player == null) return;
            int n = player.items.Count;
            if (n == 0) return;

            if (Controls.Up)   { _sel = (_sel - 1 + n) % n; Sfx.Play(SfxId.NavShop); }
            if (Controls.Down) { _sel = (_sel + 1) % n;     Sfx.Play(SfxId.NavShop); }
            if (Controls.Left)  Sfx.Play(Adjust(-1) ? SfxId.ShopDown : SfxId.NavShop);   // sell -, sweeps down
            if (Controls.Right) Sfx.Play(Adjust(+1) ? SfxId.ShopUp   : SfxId.NavShop);   // buy +, sweeps up
            if (Controls.A) Sfx.Play(TryCommit() ? SfxId.Purchase : SfxId.NavShop);      // A / > = trade
            if (Controls.B) Close();        // B / < = exit (plays CloseShop)
        }

        // Returns true if the quantity actually changed (false if clamped/blocked).
        bool Adjust(int dir)
        {
            var it = player.items[_sel];
            int prev = _delta[_sel];

            // First sell-press from zero dumps the whole stack (quick "sell all");
            // press buy to dial it back. Heads aren't sellable.
            int d = (dir < 0 && prev == 0 && it.type != ItemType.Heads) ? -it.count : prev + dir;

            if (it.type == ItemType.Heads)
            {
                bool owned = MusicDirector.Instance != null && MusicDirector.Instance.Has(_post);
                d = Mathf.Clamp(d, 0, owned ? 0 : 1);   // buy this post's head once
            }
            else if (d < -it.count)
                d = -it.count;                                       // can't oversell

            // Can't buy more than the basket can afford — refuse any change that
            // would put projected gold in the red (no debt). prev was affordable,
            // so reverting to it is always safe.
            _delta[_sel] = d;
            if (player.gold + GoldChange() < 0) _delta[_sel] = prev;
            return _delta[_sel] != prev;
        }

        int GoldChange()
        {
            int g = 0;
            for (int i = 0; i < player.items.Count; i++)
            {
                int d = _delta[i];
                var ty = player.items[i].type;
                g += d > 0 ? -d * _post.BuyPrice(ty) : (-d) * _post.SellPrice(ty);
            }
            return g;
        }

        bool AnyDelta()
        {
            foreach (var d in _delta) if (d != 0) return true;
            return false;
        }

        // Returns true if a trade was actually committed.
        bool TryCommit()
        {
            int change = GoldChange();
            if (!AnyDelta() || player.gold + change < 0) return false;
            player.gold += change;
            bool boughtHead = false;
            for (int i = 0; i < player.items.Count; i++)
            {
                if (_delta[i] > 0 && player.items[i].type == ItemType.Heads) boughtHead = true;
                player.Add(player.items[i].type, _delta[i]);
            }
            System.Array.Clear(_delta, 0, _delta.Length);
            if (boughtHead) MusicDirector.Instance.Acquire(_post);   // switch to this head + its track
            return true;
        }

        // Icon cell: the item's sprite when available, else a flat colour square.
        void DrawItemIcon(int col, int row, Sprite icon, Color fallback)
        {
            if (icon != null) _menu.Icon(col, row, 1, 1, icon, Color.white);
            else _menu.Fill(col, row, 1, 1, fallback);
        }

        // ----------------------------------------------------------- drawing

        void LateUpdate()
        {
            var ui = UiCanvas.Instance;
            _menu ??= new GlyphGrid(ui, ui.MenuFrame, font);

            bool open = IsOpen && player != null && _post != null;
            if (ui.MenuRoot.gameObject.activeSelf != open) ui.MenuRoot.gameObject.SetActive(open);
            if (!open) { _menu.Begin(); _menu.End(); return; }

            int cols = ui.cols;
            _menu.Begin();

            string title = _post.postName;
            _menu.Run((cols - title.Length) / 2, 2, title, text);

            _menu.Run(CName,  5, "ITEM",  dim);
            _menu.Run(COwned, 5, "HAVE",  dim);
            _menu.Run(CBuy,   5, "BUY",   dim);
            _menu.Run(CSell,  5, "SELL",  dim);
            _menu.Run(CDelta, 5, "TRADE", dim);

            var items = player.items;
            var icons = ItemIcons.Instance;
            for (int i = 0; i < items.Count; i++)
            {
                int r = 7 + i * 2;
                bool sel = i == _sel;
                if (sel) _menu.Fill(CCursor, r, CDelta + 5 - CCursor, 1, highlight);

                var it = items[i];
                int d = _delta[i];
                bool isHead = it.type == ItemType.Heads;
                Sprite icon = icons != null ? icons.Of(it.type) : null;

                if (sel) _menu.Glyph(CCursor, r, ">", good);

                // A collected head reads as completed — collecting heads is the goal.
                if (isHead && MusicDirector.Instance != null && MusicDirector.Instance.Has(_post))
                {
                    DrawItemIcon(CEmoji, r, icon, good);
                    _menu.Run(CName, r, it.name, good);
                    _menu.Run(COwned, r, "COLLECTED", good);
                    continue;
                }

                DrawItemIcon(CEmoji, r, icon, ItemColors.Of(it.type));
                _menu.Run(CName, r, it.name, text);
                _menu.Run(COwned, r, "x" + it.count, text);
                _menu.Run(CBuy, r, _post.BuyPrice(it.type).ToString(), dim);
                _menu.Run(CSell, r, isHead ? "-" : _post.SellPrice(it.type).ToString(), dim);

                string ds = d > 0 ? "+" + d : d.ToString();
                if (sel) _menu.Glyph(CDelta - 2, r, "<", dim);
                _menu.Run(CDelta, r, ds, d == 0 ? dim : text);
                if (sel) _menu.Glyph(CDelta + ds.Length + 1, r, ">", dim);
            }

            int change = GoldChange();
            int result = player.gold + change;
            bool valid = result >= 0;

            int row = 7 + items.Count * 2 + 1;
            _menu.Run(CName, row,     "NET " + (change >= 0 ? "+" : "") + change, change >= 0 ? good : bad);
            _menu.Run(CName, row + 1, "GOLD " + player.gold + " -> " + result, valid ? text : bad);

            _menu.Run(CName,      row + 3, "A TRADE", (valid && AnyDelta()) ? good : dim);
            _menu.Run(CName + 10, row + 3, "B EXIT",  text);

            _menu.End();
        }
    }
}
