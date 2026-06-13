// Recolours the programmatically-tinted world elements (sky, ground, tree leaf,
// tree bark, lane grass, road tracks) by the LOCAL PriceMap value of an associated
// good, so the world hints at what's valuable where you are. Each binding lerps a
// cheap->dear colour by that good's normalized price (0..1) sampled at the rider.
//
// Sky/ground are pushed to SkyBackground (sRGB, as it snaps them). Leaf/bark/grass/
// tracks go to the Tree/Road materials as .linear (those shaders LinearToSRGB their
// tint). Original material colours are cached and restored on disable, so stopping
// play doesn't leave the shared materials mutated.
//
// Add to a scene object; assign PriceMap, the rider, SkyBackground, and the Tree /
// Road materials. Bindings come with sensible defaults — reassign goods/colours freely.

using System;
using UnityEngine;
using NightRider.View;

namespace NightRider.World
{
    public class WorldPalette : MonoBehaviour
    {
        public enum Element { Sky, Ground, TreeLeaf, TreeBark, LaneGrass, RoadTracks }

        [Serializable]
        public class Binding
        {
            public Element element;
            public ItemType good;
            [Tooltip("Colour where the good is cheapest (price field = 0).")]
            public Color cheap = Color.green;
            [Tooltip("Colour where the good is dearest (price field = 1).")]
            public Color dear = Color.magenta;
        }

        [Header("Refs")]
        public PriceMap priceMap;
        [Tooltip("Sampled here (the rider). Defaults to the LaneFollower.")]
        public Transform reference;
        public SkyBackground sky;
        [Tooltip("Shared NightRider/Tree material (_LeafColor, _WoodColor).")]
        public Material treeMaterial;
        [Tooltip("Shared NightRider/Road material (_GrassColor, _TracksColor).")]
        public Material roadMaterial;

        [Header("Bindings — good -> element, cheap..dear colour")]
        public Binding[] bindings = DefaultBindings();

        static readonly int LeafId   = Shader.PropertyToID("_LeafColor");
        static readonly int WoodId   = Shader.PropertyToID("_WoodColor");
        static readonly int GrassId  = Shader.PropertyToID("_GrassColor");
        static readonly int TracksId = Shader.PropertyToID("_TracksColor");

        Color _leaf0, _wood0, _grass0, _tracks0;
        Color _sky0, _ground0;
        bool _cached;

        void OnEnable()
        {
            if (priceMap == null) priceMap = FindAnyObjectByType<PriceMap>();
            if (sky == null) sky = FindAnyObjectByType<SkyBackground>();
            if (reference == null)
            {
                var r = FindAnyObjectByType<LaneFollower>();
                if (r != null) reference = r.transform;
            }
            CacheOriginals();
        }

        void CacheOriginals()
        {
            if (_cached) return;
            if (treeMaterial != null) { _leaf0 = treeMaterial.GetColor(LeafId);  _wood0 = treeMaterial.GetColor(WoodId); }
            if (roadMaterial != null) { _grass0 = roadMaterial.GetColor(GrassId); _tracks0 = roadMaterial.GetColor(TracksId); }
            if (sky != null) { _sky0 = sky.skyColor; _ground0 = sky.groundColor; }
            _cached = true;
        }

        void OnDisable()
        {
            if (!_cached) return;
            if (treeMaterial != null) { treeMaterial.SetColor(LeafId, _leaf0);  treeMaterial.SetColor(WoodId, _wood0); }
            if (roadMaterial != null) { roadMaterial.SetColor(GrassId, _grass0); roadMaterial.SetColor(TracksId, _tracks0); }
            if (sky != null) { sky.skyColor = _sky0; sky.groundColor = _ground0; }
        }

        void LateUpdate()
        {
            if (priceMap == null || reference == null || bindings == null) return;
            Vector3 p = reference.position;
            foreach (var b in bindings)
            {
                if (b == null) continue;
                Color c = Color.Lerp(b.cheap, b.dear, priceMap.Normalized(b.good, p));
                Apply(b.element, c);
            }
        }

        void Apply(Element e, Color c)
        {
            switch (e)
            {
                // Sky snaps its colours in sRGB -> pass straight through.
                case Element.Sky:    if (sky != null) sky.skyColor = c;    break;
                case Element.Ground: if (sky != null) sky.groundColor = c; break;
                // Material tints are read via LinearToSRGB() -> pass linear.
                case Element.TreeLeaf:   if (treeMaterial != null) treeMaterial.SetColor(LeafId, c.linear);   break;
                case Element.TreeBark:   if (treeMaterial != null) treeMaterial.SetColor(WoodId, c.linear);   break;
                case Element.LaneGrass:  if (roadMaterial != null) roadMaterial.SetColor(GrassId, c.linear);  break;
                case Element.RoadTracks: if (roadMaterial != null) roadMaterial.SetColor(TracksId, c.linear); break;
            }
        }

        static Binding[] DefaultBindings() => new[]
        {
            new Binding { element = Element.TreeLeaf,   good = ItemType.Lupins, cheap = new Color(0.20f, 0.45f, 0.18f), dear = new Color(0.85f, 0.20f, 0.85f) },
            new Binding { element = Element.LaneGrass,  good = ItemType.Tonics, cheap = new Color(0.18f, 0.30f, 0.16f), dear = new Color(0.30f, 0.95f, 0.40f) },
            new Binding { element = Element.RoadTracks, good = ItemType.Arms,   cheap = new Color(0.30f, 0.22f, 0.16f), dear = new Color(0.85f, 0.20f, 0.18f) },
            new Binding { element = Element.Ground,     good = ItemType.Writs,  cheap = new Color(0.05f, 0.05f, 0.07f), dear = new Color(0.85f, 0.75f, 0.40f) },
            new Binding { element = Element.Sky,        good = ItemType.Relics, cheap = new Color(0.12f, 0.14f, 0.32f), dear = new Color(0.25f, 0.80f, 0.95f) },
            new Binding { element = Element.TreeBark,   good = ItemType.Heads,  cheap = new Color(0.22f, 0.16f, 0.12f), dear = new Color(0.90f, 0.88f, 0.82f) },
        };
    }
}
