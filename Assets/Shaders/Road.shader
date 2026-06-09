// Super-scaler road. Unlit (flat NES-ish look). Two greyscale layers tiled along
// the road and composited: TRACKS on the bottom, GRASS on top. Each layer reads
// its texture's brightness — pixels at/above a per-layer threshold show as that
// layer's solid colour; darker pixels are TRANSPARENT (hard clip, no alpha blend,
// so the layer below — or the background — shows through). Grass wins wherever
// both layers are lit. Colours are plain material properties so gameplay can
// recolour the world at runtime (material.SetColor("_GrassColor", ...)).
//
// The scroll is VIEW-RELATIVE so it always flows toward the camera, on every
// lane, regardless of which way that lane actually runs — it's a speed cue for
// the rider, not a traffic sim. Two globals drive it, set by RoadScroll.cs:
//   _RoadScroll  (float)  - accumulated scroll, from rider speed x multiplier
//   _RoadFlowDir (vector) - the camera's forward direction
// Per fragment we flip the scroll sign by sign(dot(roadDir, camFwd)), so the
// surface of an oncoming lane flows the same way on screen as your own.

Shader "NightRider/Road"
{
    Properties
    {
        [Header(Tracks   bottom layer)]
        _TracksTex   ("Tracks (greyscale)", 2D) = "black" {}
        _TracksColor ("Tracks colour", Color) = (0.42, 0.27, 0.17, 1)   // brown
        _TracksCut   ("Tracks white threshold", Range(0,1)) = 0.5
        _TracksTiling("Tracks tiling (across, along per metre)", Vector) = (1, 0.1, 0, 0)

        [Header(Grass   top layer)]
        _GrassTex    ("Grass (greyscale)", 2D) = "black" {}
        _GrassColor  ("Grass colour", Color) = (0.20, 0.55, 0.20, 1)     // green
        _GrassCut    ("Grass white threshold", Range(0,1)) = 0.5
        _GrassTiling ("Grass tiling (across, along per metre)", Vector) = (1, 0.1, 0, 0)
    }

    SubShader
    {
        // Cutout: opaque where a layer is lit, clipped (see-through) where not.
        Tags { "RenderType" = "TransparentCutout" "Queue" = "AlphaTest" "RenderPipeline" = "UniversalPipeline" }

        Pass
        {
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

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
            };

            TEXTURE2D(_TracksTex);  SAMPLER(sampler_TracksTex);
            TEXTURE2D(_GrassTex);   SAMPLER(sampler_GrassTex);

            CBUFFER_START(UnityPerMaterial)
                float4 _TracksColor;
                float4 _GrassColor;
                float4 _TracksTiling;
                float4 _GrassTiling;
                float  _TracksCut;
                float  _GrassCut;
            CBUFFER_END

            // Set globally by RoadScroll.cs
            float  _RoadScroll;
            float3 _RoadFlowDir;

            Varyings vert (Attributes IN)
            {
                Varyings OUT;
                OUT.positionHCS = TransformObjectToHClip(IN.positionOS.xyz);
                OUT.uv          = IN.uv;
                OUT.tangentWS   = TransformObjectToWorldDir(IN.tangentOS.xyz);
                return OUT;
            }

            // Greyscale brightness of a layer's texel.
            float Grey(TEXTURE2D_PARAM(tex, smp), float2 uv)
            {
                half3 c = SAMPLE_TEXTURE2D(tex, smp, uv).rgb;
                return dot(c, float3(0.299, 0.587, 0.114));
            }

            half4 frag (Varyings IN) : SV_Target
            {
                // Flow toward the camera regardless of lane direction.
                // uv.y is world distance (metres); _RoadScroll is world metres.
                float dir = sign(dot(normalize(IN.tangentWS), _RoadFlowDir));
                float along = IN.uv.y + _RoadScroll * dir;

                float2 tUV = float2(IN.uv.x * _TracksTiling.x, along * _TracksTiling.y);
                float2 gUV = float2(IN.uv.x * _GrassTiling.x,  along * _GrassTiling.y);

                // Brightness >= threshold => the layer is lit (opaque), else transparent.
                float tracksOn = step(_TracksCut, Grey(TEXTURE2D_ARGS(_TracksTex, sampler_TracksTex), tUV));
                float grassOn  = step(_GrassCut,  Grey(TEXTURE2D_ARGS(_GrassTex,  sampler_GrassTex),  gUV));

                // Nothing lit -> hole (background shows through).
                clip(max(tracksOn, grassOn) - 0.5);

                // Tracks underneath, grass painted on top.
                half3 col = lerp(_TracksColor.rgb, _GrassColor.rgb, grassOn);
                return half4(col, 1.0);
            }
            ENDHLSL
        }
    }
}
