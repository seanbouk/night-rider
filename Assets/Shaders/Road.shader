// Super-scaler road. Unlit (flat NES look). Two GREYSCALE+ALPHA layered textures —
// TRACKS on the bottom with GRASS on top. Each layer's own alpha is a hard cutout
// (>= 0.5): grass shows where its alpha covers; otherwise tracks show where THEIR
// alpha covers; where neither covers, the road itself is cut out (clipped) so the
// sky/ground behind shows through. Each layer is tinted by a MULTIPLY colour
// (white in the texture becomes that colour, greys become darker shades of it),
// then the visible pixel is snapped to the nearest NES palette colour. Tint +
// snap are done in sRGB so the multiply reads intuitively and the snap is
// colour-correct.
//
// The textures are wide (width = length), so U (width) runs ALONG the road and
// V (height) runs ACROSS it.
//
// Scroll is VIEW-RELATIVE. Globals set by RoadScroll.cs:
//   _RoadScroll  (float)  - accumulated scroll in world metres
//   _RoadFlowDir (vector) - camera forward

Shader "NightRider/Road"
{
    Properties
    {
        [NoScaleOffset] _TracksTex ("Tracks (bottom, greyscale + alpha)", 2D) = "white" {}
        [NoScaleOffset] _GrassTex  ("Grass (top, greyscale + alpha)", 2D) = "black" {}
        _TracksColor ("Tracks multiply (white = this)", Color) = (1, 1, 1, 1)
        _GrassColor  ("Grass multiply (white = this)",  Color) = (1, 1, 1, 1)
        _TracksTiling ("Tracks tiling (repeats/metre along, repeats across)", Vector) = (0.1, 1, 0, 0)
        _GrassTiling  ("Grass tiling (repeats/metre along, repeats across)",  Vector) = (0.1, 1, 0, 0)
        _MaxDistance  ("Max render distance from camera (0 = off)", Float) = 0
    }

    SubShader
    {
        Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" }

        Pass
        {
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Color.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv         : TEXCOORD0;
                float4 tangentOS  : TANGENT;   // road length-direction (baked by RoadMeshBuilder)
            };

            struct Varyings
            {
                float4 positionHCS : SV_POSITION;
                float2 uv          : TEXCOORD0;
                float3 tangentWS   : TEXCOORD1;
                float3 positionWS  : TEXCOORD2;
            };

            TEXTURE2D(_TracksTex); SAMPLER(sampler_TracksTex);
            TEXTURE2D(_GrassTex);  SAMPLER(sampler_GrassTex);

            CBUFFER_START(UnityPerMaterial)
                float4 _TracksColor;
                float4 _GrassColor;
                float4 _TracksTiling;
                float4 _GrassTiling;
                float  _MaxDistance;
            CBUFFER_END

            float  _RoadScroll;
            float3 _RoadFlowDir;
            half4  _GroundColor;   // below-horizon sky colour (sRGB), set by SkyBackground

            // NES 2C02 palette (0-255 sRGB).
            static const float3 NES[64] =
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

            // Snap an sRGB colour to the nearest NES colour, returned linear.
            half3 SnapNes(float3 srgb)
            {
                float3 best = NES[0] / 255.0;
                float bestD = 1e9;
                [unroll]
                for (int i = 0; i < 64; i++)
                {
                    float3 p = NES[i] / 255.0;
                    float3 d = srgb - p;
                    float dist = dot(d, d);
                    if (dist < bestD) { bestD = dist; best = p; }
                }
                return SRGBToLinear(best);
            }

            Varyings vert (Attributes IN)
            {
                Varyings OUT;
                OUT.positionHCS = TransformObjectToHClip(IN.positionOS.xyz);
                OUT.uv          = IN.uv;
                OUT.tangentWS   = TransformObjectToWorldDir(IN.tangentOS.xyz);
                OUT.positionWS  = TransformObjectToWorld(IN.positionOS.xyz);
                return OUT;
            }

            half4 frag (Varyings IN) : SV_Target
            {
                // Hard far edge: drop road past _MaxDistance from the camera, so the
                // sky shows beyond it (dial it to the sky's horizon).
                if (_MaxDistance > 0.0 && distance(IN.positionWS, _WorldSpaceCameraPos) > _MaxDistance)
                    clip(-1);

                float dir   = sign(dot(normalize(IN.tangentWS), _RoadFlowDir));
                float along = IN.uv.y + _RoadScroll * dir;

                float2 tUV = float2(along * _TracksTiling.x, IN.uv.x * _TracksTiling.y);
                float2 gUV = float2(along * _GrassTiling.x,  IN.uv.x * _GrassTiling.y);

                half4 grass  = SAMPLE_TEXTURE2D(_GrassTex,  sampler_GrassTex,  gUV);
                half4 tracks = SAMPLE_TEXTURE2D(_TracksTex, sampler_TracksTex, tUV);

                // Tracks: lerp from the GROUND colour (dark texels) up to the tracks
                // multiply colour (bright texels), so the dark end of the asset merges
                // into the below-horizon ground colour instead of crushing to black.
                // Grass stays a plain multiply (white -> the colour). Both in sRGB.
                float  tShade  = LinearToSRGB(tracks.rgb).g;
                float3 gTracks = lerp(LinearToSRGB(_GroundColor.rgb), LinearToSRGB(_TracksColor.rgb), tShade);
                float3 gGrass  = LinearToSRGB(grass.rgb) * LinearToSRGB(_GrassColor.rgb);

                // Each layer's alpha is a hard cutout: grass over tracks, and where
                // NEITHER covers, clip the road away so the sky/ground shows through.
                float3 chosen;
                if (grass.a >= 0.5)       chosen = gGrass;
                else if (tracks.a >= 0.5) chosen = gTracks;
                else { clip(-1); return half4(0, 0, 0, 0); }

                return half4(SnapNes(saturate(chosen)), 1.0);
            }
            ENDHLSL
        }
    }
}
