// Carousel cloud band over the raster sky. A greyscale strip (a few rows tall,
// several screens wide, wrapping horizontally) scrolls with the camera's yaw: each
// screen column maps LINEARLY to a yaw offset (equal degrees per screen-x), added
// to the camera yaw to pick the texture column. Linear (not perspective/atan) on
// purpose — it keeps the NES pixel grid honest (a texture column stays a straight
// screen column, a row stays a row) and the scroll perfectly uniform, at the cost
// of a little edge-accuracy you can't see on a flat backdrop. `_DegreesPerLoop`
// sets how big the cloud features read (it needn't divide 360 cleanly).
//
// There's no real additive blend on the NES, so we fake it the hardware way: the
// sky is one flat colour per scanline (the gradient), so per row we take
//     snap( skyColour + cloudShade * tint * strength )
// — black shades add nothing (sky shows through), brighter shades push the row
// toward the tint and then snap to a NES-legal colour. Below the cutoff we clip
// so the real sky shows untouched.
//
// Drawn by CloudLayer.cs on a frustum-filling quad, after the sky, before the
// world. Reads the sky's gradient via the global _SkyGradient (set by SkyBackground).

Shader "NightRider/Cloud"
{
    Properties
    {
        [NoScaleOffset] _CloudTex ("Clouds (greyscale strip, wrap X)", 2D) = "black" {}
        _CloudTint ("Cloud light tint (additive)", Color) = (1, 1, 1, 1)
        _Strength  ("Additive strength", Range(0, 2)) = 1
        _BandCenter ("Band centre (screen v)", Range(0, 1)) = 0.62
        _BandHeight ("Band height (screen v)", Range(0.01, 1)) = 0.18
        _DegreesPerLoop ("Degrees per texture loop (feature size)", Float) = 360
        _CloudCut ("Cutoff (below = sky shows)", Range(0, 1)) = 0.04
        _CamYaw ("Camera yaw deg (set by script)", Float) = 0
        _HFovDeg ("Horizontal FOV deg (set by script)", Float) = 75
    }

    SubShader
    {
        Tags { "Queue" = "Background+10" "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" }

        Pass
        {
            Cull Off
            ZWrite Off
            ZTest Always

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "NesPalette.hlsl"

            struct Attributes { float3 positionOS : POSITION; float2 uv : TEXCOORD0; };
            struct Varyings   { float4 positionHCS : SV_POSITION; float2 uv : TEXCOORD0; };

            TEXTURE2D(_CloudTex);    SAMPLER(sampler_CloudTex);
            TEXTURE2D(_SkyGradient); SAMPLER(sampler_SkyGradient);   // global, set by SkyBackground

            CBUFFER_START(UnityPerMaterial)
                float4 _CloudTint;
                float  _Strength;
                float  _BandCenter;
                float  _BandHeight;
                float  _DegreesPerLoop;
                float  _CloudCut;
                float  _CamYaw;
                float  _HFovDeg;
            CBUFFER_END

            Varyings vert (Attributes IN)
            {
                Varyings o;
                o.positionHCS = TransformObjectToHClip(IN.positionOS);
                o.uv = IN.uv;                       // quad fills the view; uv = (0..1, 0..1)
                return o;
            }

            half4 frag (Varyings IN) : SV_Target
            {
                // Vertical band: map the strip to a slice of screen height.
                float bv = (IN.uv.y - (_BandCenter - _BandHeight * 0.5)) / max(_BandHeight, 1e-4);
                if (bv < 0.0 || bv > 1.0) clip(-1);

                // Horizontal: linear yaw across the screen -> texture column.
                float az = _CamYaw + (IN.uv.x - 0.5) * _HFovDeg;
                float u  = frac(az / max(_DegreesPerLoop, 1e-3));   // loop the strip

                float grey = SAMPLE_TEXTURE2D(_CloudTex, sampler_CloudTex, float2(u, bv)).r;
                if (grey < _CloudCut) clip(-1);                  // show the real sky

                // Additive over this row's flat sky colour, in sRGB, then NES-snap.
                half3  skyL = SAMPLE_TEXTURE2D(_SkyGradient, sampler_SkyGradient, float2(0.5, IN.uv.y)).rgb;
                float3 srgb = LinearToSRGB(skyL) + grey * _Strength * _CloudTint.rgb;
                return half4(NesSnap(saturate(srgb)), 1.0);
            }
            ENDHLSL
        }
    }
}
