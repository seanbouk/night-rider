// Maps each item type (and gold) to its 8x8 HUD icon. Assign the icon textures on
// one of these in the scene; the HUD, shop, and pickup FX read it. Returns null
// when absent/unassigned, so callers fall back to a flat colour square.

using System.Collections.Generic;
using UnityEngine;
using NightRider.World;

namespace NightRider.View
{
    public class ItemIcons : MonoBehaviour
    {
        static ItemIcons _instance;
        public static ItemIcons Instance
        {
            get { if (_instance == null) _instance = FindAnyObjectByType<ItemIcons>(); return _instance; }
        }

        [Header("8x8 item icons (Point filter, Read/Write on)")]
        public Texture2D lupins, tonics, arms, writs, relics, heads, gold;

        [Range(0, 64), Tooltip("Pixels with every channel at/below this (0-255) become transparent — keys out a black background.")]
        public int blackCutoff = 8;

        readonly Dictionary<Texture2D, Sprite> _cache = new();

        void Awake() { _instance = this; }
        void OnDestroy() { if (_instance == this) _instance = null; }

        public Sprite Of(ItemType t) => Make(t switch
        {
            ItemType.Lupins => lupins,
            ItemType.Tonics => tonics,
            ItemType.Arms   => arms,
            ItemType.Writs  => writs,
            ItemType.Relics => relics,
            ItemType.Heads  => heads,
            _               => null,
        });

        public Sprite Gold => Make(gold);

        Sprite Make(Texture2D tex)
        {
            if (tex == null) return null;
            if (!_cache.TryGetValue(tex, out var sp))
            {
                var keyed = KeyBlack(tex);
                sp = Sprite.Create(keyed, new Rect(0, 0, keyed.width, keyed.height), new Vector2(0.5f, 0.5f), 100f);
                _cache[tex] = sp;
            }
            return sp;
        }

        // A copy of `tex` with (near-)black pixels made transparent. Needs Read/Write.
        Texture2D KeyBlack(Texture2D tex)
        {
            if (!tex.isReadable)
            {
                Debug.LogWarning($"ItemIcons: enable Read/Write on '{tex.name}' to key its black out; using it as-is.");
                return tex;
            }

            var px = tex.GetPixels32();
            byte cut = (byte)Mathf.Clamp(blackCutoff, 0, 255);
            for (int i = 0; i < px.Length; i++)
            {
                var c = px[i];
                if (c.r <= cut && c.g <= cut && c.b <= cut) c.a = 0;
                px[i] = c;
            }

            var t = new Texture2D(tex.width, tex.height, TextureFormat.RGBA32, false)
            {
                name = tex.name + "_keyed",
                filterMode = tex.filterMode,
                wrapMode = tex.wrapMode,
            };
            t.SetPixels32(px);
            t.Apply();
            return t;
        }
    }
}
