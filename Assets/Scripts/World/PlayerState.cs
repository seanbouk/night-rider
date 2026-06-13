// Stub player state: inventory, gold, and level. Nothing mutates it yet — it
// just exists to feed the HUD. Counts default to 0 (set them in the Inspector
// to test the display). Item emoji/name are editable per stack.

using System;
using System.Collections.Generic;
using UnityEngine;

namespace NightRider.World
{
    public enum ItemType { Lupins, Tonics, Arms, Writs, Relics, Heads }

    // Placeholder flat-colour icons until real three-colour 8x8 sprites land.
    public static class ItemColors
    {
        public static Color Of(ItemType t) => t switch
        {
            ItemType.Lupins => new Color(1f,    0f,    1f),    // magenta
            ItemType.Tonics => new Color(0.25f, 0.9f,  0.4f),  // green
            ItemType.Arms   => new Color(0.9f,  0.2f,  0.2f),  // red
            ItemType.Writs  => new Color(0.95f, 0.85f, 0.35f), // parchment
            ItemType.Relics => new Color(0.3f,  0.8f,  0.95f), // cyan
            ItemType.Heads  => new Color(0.95f, 0.95f, 0.95f), // bone
            _               => Color.white,
        };
    }

    [Serializable]
    public class ItemStack
    {
        public ItemType type;
        public string emoji;
        public string name;
        public int count;
    }

    public class PlayerState : MonoBehaviour
    {
        public int gold = 200;
        [Tooltip("Display name of the current level (you'll never see the number). " +
                 "Driven by heads collected via SetRank — set in the Inspector only to preview.")]
        public string level = "Harmless";

        [Tooltip("Rank titles by number of heads collected (index 0 = none). " +
                 "The last entry is the cap once you run past the table.")]
        public string[] ranks =
        {
            "Harmless", "Highwayman", "Turnpikeman",
            "Restless", "Appirator", "Phantom", "Elite",
        };

        [Tooltip("Six stacks: col A = first three, col B = last three.")]
        public List<ItemStack> items = new();

        void Reset() => ResetToDefaults();

        [ContextMenu("Reset To Defaults")]
        public void ResetToDefaults()
        {
            gold = 200;
            level = "Harmless";
            items = new List<ItemStack>
            {
                new() { type = ItemType.Lupins, emoji = "🌷", name = "Lupins", count = 0 },
                new() { type = ItemType.Tonics, emoji = "🧪", name = "Tonics", count = 0 },
                new() { type = ItemType.Arms,   emoji = "🗡", name = "Arms",   count = 0 },
                new() { type = ItemType.Writs,  emoji = "📜", name = "Writs",  count = 0 },
                new() { type = ItemType.Relics, emoji = "🏺", name = "Relics", count = 0 },
                new() { type = ItemType.Heads,  emoji = "💀", name = "Heads",  count = 0 },
            };
        }

        // Set the displayed rank from how many heads have been collected
        // (clamped to the table). Called whenever a head is bought.
        public void SetRank(int headsCollected)
        {
            if (ranks == null || ranks.Length == 0) return;
            level = ranks[Mathf.Clamp(headsCollected, 0, ranks.Length - 1)];
        }

        // Add (or remove) count for an item type. No-op if the type isn't present.
        public void Add(ItemType type, int amount)
        {
            foreach (var s in items)
                if (s.type == type) { s.count = Mathf.Max(0, s.count + amount); return; }
        }
    }
}
