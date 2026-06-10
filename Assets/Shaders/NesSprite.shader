// NES-style 4-colour sprite. Hard alpha cutout (transparent = the 4th "colour",
// no variable alpha), then each opaque pixel is matched to the nearest of three
// MATCH colours (the raw colours you picked from the art) and drawn as the
// corresponding OUT colour (that swatch snapped to the NES palette).
//
// Matching against the raw picks (not the snapped ones) is deliberate: a pixel
// that exactly equals a swatch you took from the asset then lands on it reliably;
// the NES snap only affects what's drawn, not what's matched. All six colours are
// per-renderer (a MaterialPropertyBlock), in LINEAR space to match the sampled
// (sRGB) texture in this linear-colour project.

Shader "NightRider/NesSprite"
{
    Properties
    {
        [PerRendererData] _MainTex ("Sprite Texture", 2D) = "white" {}
        _Match0 ("Match 0", Color) = (0.5, 0.5, 0.5, 1)
        _Match1 ("Match 1", Color) = (0.45, 0.27, 0.1, 1)
        _Match2 ("Match 2", Color) = (0.85, 0.75, 0.2, 1)
        _Out0 ("Out 0", Color) = (0.5, 0.5, 0.5, 1)
        _Out1 ("Out 1", Color) = (0.45, 0.27, 0.1, 1)
        _Out2 ("Out 2", Color) = (0.85, 0.75, 0.2, 1)
        _Cutoff ("Alpha cutoff", Range(0,1)) = 0.5
    }

    SubShader
    {
        Tags { "Queue"="Transparent" "RenderType"="Transparent" "PreviewType"="Plane" "CanUseSpriteAtlas"="True" "RenderPipeline"="UniversalPipeline" }
        Cull Off
        ZWrite Off
        Blend SrcAlpha OneMinusSrcAlpha

        Pass
        {
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            struct Attributes { float3 positionOS : POSITION; float4 color : COLOR; float2 uv : TEXCOORD0; };
            struct Varyings   { float4 positionHCS : SV_POSITION; float2 uv : TEXCOORD0; };

            TEXTURE2D(_MainTex);
            SAMPLER(sampler_MainTex);

            CBUFFER_START(UnityPerMaterial)
                float4 _Match0; float4 _Match1; float4 _Match2;
                float4 _Out0;   float4 _Out1;   float4 _Out2;
                float  _Cutoff;
            CBUFFER_END

            Varyings vert (Attributes IN)
            {
                Varyings o;
                VertexPositionInputs p = GetVertexPositionInputs(IN.positionOS);
                o.positionHCS = p.positionCS;
                o.uv = IN.uv;
                return o;
            }

            float Dist2(float3 a, float3 b) { float3 d = a - b; return dot(d, d); }

            half4 frag (Varyings IN) : SV_Target
            {
                half4 tex = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, IN.uv);
                clip(tex.a - _Cutoff);                       // hard cutout (4th colour = transparent)

                float3 p = tex.rgb;
                float d0 = Dist2(p, _Match0.rgb);
                float d1 = Dist2(p, _Match1.rgb);
                float d2 = Dist2(p, _Match2.rgb);

                half3 c = (d0 <= d1 && d0 <= d2) ? _Out0.rgb
                        : (d1 <= d2)             ? _Out1.rgb
                        :                          _Out2.rgb;
                return half4(c, 1.0);
            }
            ENDHLSL
        }
    }
}
