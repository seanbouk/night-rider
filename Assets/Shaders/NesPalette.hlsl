// Shared NES 2C02 palette + nearest-colour snap, for shaders that NES-legalise
// their colours on the GPU (Road, Tree). Compare in sRGB, return linear so the
// result displays correctly in a linear colour project.
#ifndef NIGHTRIDER_NES_PALETTE_INCLUDED
#define NIGHTRIDER_NES_PALETTE_INCLUDED

#include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Color.hlsl"

static const float3 _NesPalette[64] =
{
    float3(84,84,84),   float3(0,30,116),   float3(8,16,144),   float3(48,0,136),
    float3(68,0,100),   float3(92,0,48),    float3(84,4,0),     float3(60,24,0),
    float3(32,42,0),    float3(8,58,0),     float3(0,64,0),     float3(0,60,0),
    float3(0,50,60),    float3(0,0,0),      float3(0,0,0),      float3(0,0,0),
    float3(152,150,152),float3(8,76,196),   float3(48,50,236),  float3(92,30,228),
    float3(136,20,176), float3(160,20,100), float3(152,34,32),  float3(120,60,0),
    float3(84,90,0),    float3(40,114,0),   float3(8,124,0),    float3(0,118,40),
    float3(0,102,120),  float3(0,0,0),      float3(0,0,0),      float3(0,0,0),
    float3(236,238,236),float3(76,154,236), float3(120,124,236),float3(176,98,236),
    float3(228,84,236), float3(236,88,180), float3(236,106,100),float3(212,136,32),
    float3(160,170,0),  float3(116,196,0),  float3(76,208,32),  float3(56,204,108),
    float3(56,180,204), float3(60,60,60),   float3(0,0,0),      float3(0,0,0),
    float3(236,238,236),float3(168,204,236),float3(188,188,236),float3(212,178,236),
    float3(236,174,236),float3(236,174,212),float3(236,180,176),float3(228,196,144),
    float3(204,210,120),float3(180,222,120),float3(168,226,144),float3(152,226,180),
    float3(160,214,228),float3(160,162,160),float3(0,0,0),      float3(0,0,0),
};

// srgb in 0..1 -> nearest NES colour, returned linear.
half3 NesSnap(float3 srgb)
{
    float3 best = _NesPalette[0] / 255.0;
    float bestD = 1e9;
    [unroll]
    for (int i = 0; i < 64; i++)
    {
        float3 p = _NesPalette[i] / 255.0;
        float3 d = srgb - p;
        float dist = dot(d, d);
        if (dist < bestD) { bestD = dist; best = p; }
    }
    return SRGBToLinear(best);
}

#endif
