// Roadside tree: a billboard quad with two greyscale layers — WOOD (trunk/branches)
// with LEAF on top — each tinted by its own multiply colour (white in the texture
// becomes that colour), composited by the leaf's alpha (leaf over wood), the rest
// transparent (hard cutout). The visible pixel is snapped to the nearest NES
// colour. Tint + snap in sRGB so it's colour-correct in a linear project.

Shader "NightRider/Tree"
{
    Properties
    {
        [NoScaleOffset] _WoodTex ("Wood (greyscale + alpha)", 2D) = "black" {}
        [NoScaleOffset] _LeafTex ("Leaf (greyscale + alpha)", 2D) = "black" {}
        _WoodColor ("Wood multiply (white = this)", Color) = (0.5, 0.35, 0.2, 1)
        _LeafColor ("Leaf multiply (white = this)", Color) = (0.2, 0.6, 0.25, 1)
        _Cutoff ("Alpha cutoff", Range(0,1)) = 0.5
    }

    SubShader
    {
        Tags { "Queue" = "AlphaTest" "RenderType" = "TransparentCutout" "RenderPipeline" = "UniversalPipeline" }

        Pass
        {
            Cull Off
            ZWrite On

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "NesPalette.hlsl"

            struct Attributes { float4 positionOS : POSITION; float2 uv : TEXCOORD0; };
            struct Varyings   { float4 positionHCS : SV_POSITION; float2 uv : TEXCOORD0; };

            TEXTURE2D(_WoodTex); SAMPLER(sampler_WoodTex);
            TEXTURE2D(_LeafTex); SAMPLER(sampler_LeafTex);

            CBUFFER_START(UnityPerMaterial)
                float4 _WoodColor;
                float4 _LeafColor;
                float  _Cutoff;
            CBUFFER_END

            Varyings vert (Attributes IN)
            {
                Varyings o;
                o.positionHCS = TransformObjectToHClip(IN.positionOS.xyz);
                o.uv = IN.uv;
                return o;
            }

            half4 frag (Varyings IN) : SV_Target
            {
                half4 leaf = SAMPLE_TEXTURE2D(_LeafTex, sampler_LeafTex, IN.uv);
                half4 wood = SAMPLE_TEXTURE2D(_WoodTex, sampler_WoodTex, IN.uv);

                float3 col;
                if (leaf.a >= _Cutoff)
                    col = LinearToSRGB(leaf.rgb) * LinearToSRGB(_LeafColor.rgb);
                else if (wood.a >= _Cutoff)
                    col = LinearToSRGB(wood.rgb) * LinearToSRGB(_WoodColor.rgb);
                else
                {
                    clip(-1);            // transparent (around the tree)
                    col = 0;
                }
                return half4(NesSnap(saturate(col)), 1.0);
            }
            ENDHLSL
        }
    }
}
