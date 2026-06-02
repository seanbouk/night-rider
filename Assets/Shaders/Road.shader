// Super-scaler road. Unlit (flat NES-ish look). Procedurally draws asphalt with
// edge lines and a dashed centre line, scrolling along the road's length.
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
        _BaseColor   ("Asphalt", Color) = (0.12, 0.12, 0.13, 1)
        _LineColor   ("Line",    Color) = (0.85, 0.80, 0.45, 1)
        _Tiling      ("Dashes per metre along road", Float) = 0.1
        _DashRatio   ("Centre dash on-fraction", Range(0,1)) = 0.5
        _CentreWidth ("Centre line width (across)", Range(0,0.5)) = 0.05
        _EdgeWidth   ("Edge line width (across)",   Range(0,0.5)) = 0.05
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

            CBUFFER_START(UnityPerMaterial)
                float4 _BaseColor;
                float4 _LineColor;
                float  _Tiling;
                float  _DashRatio;
                float  _CentreWidth;
                float  _EdgeWidth;
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

            half4 frag (Varyings IN) : SV_Target
            {
                // Flow toward the camera regardless of lane direction.
                // uv.y is world distance; _RoadScroll is world metres; _Tiling = dashes/metre.
                float dir = sign(dot(normalize(IN.tangentWS), _RoadFlowDir));
                float v = (IN.uv.y + _RoadScroll * dir) * _Tiling;
                float u = IN.uv.x;

                half3 col = _BaseColor.rgb;

                // Edge lines down both sides.
                if (u < _EdgeWidth || u > 1.0 - _EdgeWidth)
                    col = _LineColor.rgb;

                // Dashed centre line.
                bool onCentre = abs(u - 0.5) < _CentreWidth;
                bool onDash   = frac(v) < _DashRatio;
                if (onCentre && onDash)
                    col = _LineColor.rgb;

                return half4(col, 1.0);
            }
            ENDHLSL
        }
    }
}
