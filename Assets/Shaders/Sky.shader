// Raster sky: a screen-space vertical gradient drawn BEHIND everything (the road
// and sprites render on top). The colour for each row comes from a 1xN gradient
// texture built on the CPU — black at the top fading to the sky colour at the
// horizon, each row snapped to a NES-legal colour — so it reads like the NES
// background-colour register being rewritten on every horizontal blank.
//
// Rendered by SkyBackground.cs on a frustum-filling quad: Background queue, no
// depth write, so opaque geometry drawn afterwards covers the lower part.

Shader "NightRider/Sky"
{
    Properties
    {
        _Gradient ("Gradient (1 x rows)", 2D) = "black" {}
    }

    SubShader
    {
        Tags { "Queue" = "Background" "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" }

        Pass
        {
            Cull Off
            ZWrite Off
            ZTest Always

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            struct Attributes { float3 positionOS : POSITION; float2 uv : TEXCOORD0; };
            struct Varyings   { float4 positionHCS : SV_POSITION; float2 uv : TEXCOORD0; };

            TEXTURE2D(_Gradient);
            SAMPLER(sampler_Gradient);

            Varyings vert (Attributes IN)
            {
                Varyings o;
                o.positionHCS = TransformObjectToHClip(IN.positionOS);
                o.uv = IN.uv;                       // quad fills the view; uv.y = 0 bottom .. 1 top
                return o;
            }

            half4 frag (Varyings IN) : SV_Target
            {
                half3 c = SAMPLE_TEXTURE2D(_Gradient, sampler_Gradient, float2(0.5, IN.uv.y)).rgb;
                return half4(c, 1.0);
            }
            ENDHLSL
        }
    }
}
