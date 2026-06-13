// A world-space price field. Each good's base price comes from Perlin noise
// sampled at a world position (tune scale/offset/min/max per good). A trading
// post reads its prices from where it sits: buy = base (the market price),
// sell = base x (1 - marketCut), so selling always costs you the cut. Prices
// are rounded to "nice" numbers.
//
// One in the scene; posts find it. The Scene-view heatmap (gizmo grid) shows the
// selected good's field so you can place posts in cheap/dear regions.

using System;
using System.Collections.Generic;
using UnityEngine;

namespace NightRider.World
{
    [Serializable]
    public class GoodPrice
    {
        public ItemType type;
        [LogRange(0.0001f, 0.01f), Tooltip("Noise frequency (log slider). Smaller = larger, smoother price regions.")]
        public float noiseScale = 0.004f;
        [Tooltip("Shifts the noise (per-good 'seed').")]
        public Vector2 offset;
        public int min = 10;
        public int max = 120;
    }

    public class PriceMap : MonoBehaviour
    {
        public List<GoodPrice> goods = new();

        [Range(0f, 0.9f), Tooltip("Sell = buy x (1 - cut). The trader's cut, taken on selling.")]
        public float marketCut = 0.1f;

        [Header("Scene-view heatmap (Move tool positions it, Scale tool sizes it)")]
        public bool showHeatmap = true;
        public ItemType displayGood = ItemType.Lupins;
        [Min(1)] public int resolution = 32;

        void Reset()
        {
            ResetToDefaults();
            transform.localScale = new Vector3(400f, 1f, 400f);   // a usable starting size
        }

        [ContextMenu("Reset To Defaults")]
        public void ResetToDefaults()
        {
            goods = new List<GoodPrice>();
            foreach (ItemType t in Enum.GetValues(typeof(ItemType)))
                goods.Add(new GoodPrice { type = t });
        }

        GoodPrice Find(ItemType t)
        {
            foreach (var g in goods) if (g != null && g.type == t) return g;
            return null;
        }

        // Base market price for a good at a world position (the buy price, pre-round).
        public float BasePrice(ItemType t, Vector3 world)
        {
            var g = Find(t);
            if (g == null) return 0f;
            float n = Mathf.PerlinNoise(world.x * g.noiseScale + g.offset.x,
                                        world.z * g.noiseScale + g.offset.y);
            return Mathf.Lerp(g.min, g.max, Mathf.Clamp01(n));
        }

        // Normalized 0..1 field value for a good at a world position (what the
        // heatmap shows). 0 = cheapest, 1 = dearest. Drives WorldPalette.
        public float Normalized(ItemType t, Vector3 world)
        {
            var g = Find(t);
            if (g == null) return 0f;
            return Mathf.Clamp01(Mathf.PerlinNoise(world.x * g.noiseScale + g.offset.x,
                                                   world.z * g.noiseScale + g.offset.y));
        }

        public int BuyPrice(ItemType t, Vector3 world)  => RoundNice(BasePrice(t, world));
        public int SellPrice(ItemType t, Vector3 world) => RoundNice(BasePrice(t, world) * (1f - marketCut));

        // ints for low numbers, 5s a bit bigger, 10s when large.
        public static int RoundNice(float v)
        {
            if (v < 20f)  return Mathf.RoundToInt(v);
            if (v < 100f) return Mathf.RoundToInt(v / 5f) * 5;
            return Mathf.RoundToInt(v / 10f) * 10;
        }

        // Heatmap window comes from the transform: position = centre (Move tool),
        // X/Z scale = size (Scale tool), Y = draw height.
        void OnDrawGizmos()
        {
            if (!showHeatmap) return;
            var g = Find(displayGood);
            if (g == null) return;

            Vector3 c = transform.position;
            float sx = Mathf.Abs(transform.localScale.x);
            float sz = Mathf.Abs(transform.localScale.z);
            if (sx < 0.01f || sz < 0.01f) return;

            float cellX = sx / resolution, cellZ = sz / resolution;
            float x0 = c.x - sx * 0.5f, z0 = c.z - sz * 0.5f;
            Vector3 size = new(cellX * 0.95f, 0.05f, cellZ * 0.95f);

            for (int i = 0; i < resolution; i++)
            for (int j = 0; j < resolution; j++)
            {
                float wx = x0 + (i + 0.5f) * cellX;
                float wz = z0 + (j + 0.5f) * cellZ;
                float n = Mathf.Clamp01(Mathf.PerlinNoise(wx * g.noiseScale + g.offset.x,
                                                          wz * g.noiseScale + g.offset.y));
                Gizmos.color = new Color(n, 0.15f, 1f - n, 0.5f);   // cheap=blue, dear=red
                Gizmos.DrawCube(new Vector3(wx, c.y, wz), size);
            }
        }
    }
}
