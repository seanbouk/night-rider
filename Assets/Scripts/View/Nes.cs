// NES hardware palette + a "snap any colour to the nearest NES colour" helper.
// The NES can only show these ~54 distinct colours (the 2C02 palette, 64 slots
// with some repeated blacks). Snapping our chosen sprite colours to this table
// keeps the art honest to the hardware. Snap once in code (cheap) rather than
// per-pixel in a shader.

using System.Collections.Generic;
using UnityEngine;

namespace NightRider.View
{
    public static class Nes
    {
        // Classic 2C02 NES palette (64 entries). Duplicates/blacks are harmless —
        // nearest-match just never picks the worse of two identical colours.
        static readonly Color32[] Palette =
        {
            new(84,84,84,255),   new(0,30,116,255),   new(8,16,144,255),   new(48,0,136,255),
            new(68,0,100,255),   new(92,0,48,255),    new(84,4,0,255),     new(60,24,0,255),
            new(32,42,0,255),    new(8,58,0,255),     new(0,64,0,255),     new(0,60,0,255),
            new(0,50,60,255),    new(0,0,0,255),      new(0,0,0,255),      new(0,0,0,255),
            new(152,150,152,255),new(8,76,196,255),   new(48,50,236,255),  new(92,30,228,255),
            new(136,20,176,255), new(160,20,100,255), new(152,34,32,255),  new(120,60,0,255),
            new(84,90,0,255),    new(40,114,0,255),   new(8,124,0,255),    new(0,118,40,255),
            new(0,102,120,255),  new(0,0,0,255),      new(0,0,0,255),      new(0,0,0,255),
            new(236,238,236,255),new(76,154,236,255), new(120,124,236,255),new(176,98,236,255),
            new(228,84,236,255), new(236,88,180,255), new(236,106,100,255),new(212,136,32,255),
            new(160,170,0,255),  new(116,196,0,255),  new(76,208,32,255),  new(56,204,108,255),
            new(56,180,204,255), new(60,60,60,255),   new(0,0,0,255),      new(0,0,0,255),
            new(236,238,236,255),new(168,204,236,255),new(188,188,236,255),new(212,178,236,255),
            new(236,174,236,255),new(236,174,212,255),new(236,180,176,255),new(228,196,144,255),
            new(204,210,120,255),new(180,222,120,255),new(168,226,144,255),new(152,226,180,255),
            new(160,214,228,255),new(160,162,160,255),new(0,0,0,255),      new(0,0,0,255),
        };

        // Parse "RRGGBB" or "#RRGGBB" (and other HTML forms) to a Color; returns
        // fallback if blank/invalid. Used so palettes can be typed as hex (the Unity
        // colour picker is unreliable on Color fields inside arrays).
        public static Color ParseHex(string hex, Color fallback)
        {
            if (string.IsNullOrWhiteSpace(hex)) return fallback;
            string s = hex.Trim();
            if (s[0] != '#') s = "#" + s;
            return ColorUtility.TryParseHtmlString(s, out var c) ? c : fallback;
        }

        // Nearest NES colour (Euclidean in RGB). Alpha is ignored/forced opaque —
        // NES sprites have no variable alpha.
        public static Color Snap(Color c) => SnapVivid(c, 0f);

        // Nearest NES colour, keeping the input alpha (for UI colours that fade).
        public static Color SnapKeepAlpha(Color c)
        {
            var s = Snap(c);
            s.a = c.a;
            return s;
        }

        // Like Snap, but biased toward brighter / more saturated NES colours so dark
        // saturated picks (e.g. a dark green or blue) jump to a vivid NES entry of
        // roughly the same hue instead of the geometrically-nearest drab one.
        //   bias 0   = pure nearest
        //   bias ~1+ = strongly prefer vivid
        // Neutral inputs (greys/near-blacks) are unaffected: the vivid NES colours
        // are RGB-distant from them, so the distance term still wins.
        public static Color SnapVivid(Color c, float bias)
        {
            int best = 0;
            float bestCost = float.MaxValue;
            for (int i = 0; i < Palette.Length; i++)
            {
                Color p = Palette[i];
                float dr = p.r - c.r, dg = p.g - c.g, db = p.b - c.b;
                float dist = dr * dr + dg * dg + db * db;

                float mx = Mathf.Max(p.r, Mathf.Max(p.g, p.b));
                float mn = Mathf.Min(p.r, Mathf.Min(p.g, p.b));
                float vivid = (mx - mn) * mx;          // chroma * brightness, 0..1

                float cost = dist - bias * vivid;
                if (cost < bestCost) { bestCost = cost; best = i; }
            }
            Color o = Palette[best];
            o.a = 1f;
            return o;
        }

        // Return a copy of `src` with every pixel snapped to its nearest NES colour
        // (cached per unique colour, original alpha preserved so transparency/edges
        // are untouched). Keeps all colours — no palette reduction. Needs Read/Write
        // on the source; returns it unchanged (with a warning) if not readable.
        public static Texture2D SnapTexture(Texture2D src)
        {
            if (src == null) return src;
            if (!src.isReadable)
            {
                Debug.LogWarning($"Nes.SnapTexture: enable Read/Write on '{src.name}' to NES-snap it; using it un-snapped.");
                return src;
            }

            var px = src.GetPixels32();
            var cache = new Dictionary<Color32, Color32>();
            for (int i = 0; i < px.Length; i++)
            {
                Color32 c = px[i];
                if (!cache.TryGetValue(c, out var snapped))
                {
                    snapped = (Color32)Snap(new Color(c.r / 255f, c.g / 255f, c.b / 255f));
                    snapped.a = c.a;                       // keep original transparency
                    cache[c] = snapped;
                }
                px[i] = snapped;
            }

            var tex = new Texture2D(src.width, src.height, TextureFormat.RGBA32, false)
            {
                name = src.name + "_NES",
                filterMode = FilterMode.Point,
                wrapMode = src.wrapMode,
            };
            tex.SetPixels32(px);
            tex.Apply();
            return tex;
        }
    }
}
