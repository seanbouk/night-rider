// Ghost apparition for the rider's attack. Takes the rider sprite, recolours it
// monotone blue->white by luminance, and fakes see-through the NES way: no alpha
// blending — instead it CLIPS every other SCREEN column, where the screen is the
// 320px-wide 4:3 active area (published by the Hud as _HudAreaX/_HudAreaW). The
// sprite's transparent background is also a hard cutout.

Shader "NightRider/Apparition"
{
    Properties
    {
        [PerRendererData] _MainTex ("Sprite Texture", 2D) = "white" {}
        _GhostDark  ("Ghost dark (blue)",  Color) = (0.15, 0.25, 0.9, 1)
        _GhostLight ("Ghost light (white)", Color) = (0.85, 0.95, 1.0, 1)
        [ToggleUI] _Monotone ("Monotone recolour", Float) = 1
        _Cutoff     ("Alpha cutoff", Range(0,1)) = 0.5
    }

    SubShader
    {
        Tags { "Queue"="Transparent" "RenderType"="Transparent" "PreviewType"="Plane" "CanUseSpriteAtlas"="True" }
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
            struct Varyings   { float4 positionHCS : SV_POSITION; float2 uv : TEXCOORD0; float4 screenPos : TEXCOORD1; };

            TEXTURE2D(_MainTex);
            SAMPLER(sampler_MainTex);

            CBUFFER_START(UnityPerMaterial)
                float4 _GhostDark;
                float4 _GhostLight;
                float  _Monotone;
                float  _Cutoff;
            CBUFFER_END

            float _HudAreaX;   // screen x (px) of the 4:3 area's left edge
            float _HudAreaW;   // 4:3 area width (px)

            Varyings vert (Attributes IN)
            {
                Varyings o;
                VertexPositionInputs p = GetVertexPositionInputs(IN.positionOS);
                o.positionHCS = p.positionCS;
                o.screenPos   = p.positionNDC;
                o.uv = IN.uv;
                return o;
            }

            half4 frag (Varyings IN) : SV_Target
            {
                half4 tex = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, IN.uv);
                clip(tex.a - _Cutoff);                                  // hard cutout (no alpha blend)

                // Drop every other column of the 320px-wide 4:3 active area.
                float2 sp = IN.screenPos.xy / max(IN.screenPos.w, 1e-5);
                float screenX = sp.x * _ScreenParams.x;
                float col = (screenX - _HudAreaX) / max(_HudAreaW, 1.0) * 320.0;
                if (fmod(floor(col), 2.0) >= 1.0) clip(-1);

                // Monotone blue -> white by luminance, or keep the sprite's colours.
                float lum = dot(tex.rgb, float3(0.299, 0.587, 0.114));
                half3 c = _Monotone > 0.5 ? lerp(_GhostDark.rgb, _GhostLight.rgb, lum) : tex.rgb;
                return half4(c, 1.0);
            }
            ENDHLSL
        }
    }
}
