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

        [Header("8x8 item icons (Point filter)")]
        public Texture2D lupins, tonics, arms, writs, relics, heads, gold;

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
                sp = Sprite.Create(tex, new Rect(0, 0, tex.width, tex.height), new Vector2(0.5f, 0.5f), 100f);
                _cache[tex] = sp;
            }
            return sp;
        }
    }
}
