// Stub player state: inventory, gold, and level. Nothing mutates it yet — it
// just exists to feed the HUD. Counts default to 0 (set them in the Inspector
// to test the display). Item emoji/name are editable per stack.

using System;
using System.Collections.Generic;
using UnityEngine;

namespace NightRider.World
{
    public enum ItemType { Lupins, Tonics, Arms, Writs, Relics, Heads }

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
        [Tooltip("Display name of the current level (you'll never see the number).")]
        public string level = "Harmless";

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

        // Add (or remove) count for an item type. No-op if the type isn't present.
        public void Add(ItemType type, int amount)
        {
            foreach (var s in items)
                if (s.type == type) { s.count = Mathf.Max(0, s.count + amount); return; }
        }
    }
}
