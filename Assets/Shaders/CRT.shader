// CRT post-process, as a single URP fullscreen pass. Drive it with a "Full Screen
// Pass Renderer Feature" on the URP Renderer, material = CRTMat, injection
// AfterRenderingPostProcessing with color fetch.
//
// Now that ALL game UI renders on a Screen Space - Camera canvas (in the camera
// colour, before this pass), this one pass covers the world AND the HUD/shop:
//   1. MOSAIC  — snaps sampling to a 320x240-ish grid. Only the 240p height is
//      fixed; the column count is derived from the window aspect so the tiles stay
//      square (a wide debug window just shows more columns; the shipped 4:3 window
//      lands on ~320 across).
//   2. H-BLUR  — slight horizontal bleed between neighbouring mosaic columns. This
//      also bleeds active-area colour a little into the black NES side pillars.
//   3. SCANLINES — at 3x the mosaic's vertical granularity: each 240p line is three
//      rows — a bright BEAM in the middle, BLOOM rows above/below that the brightest
//      colours bleed into (dark pixels keep dark gaps).

Shader "NightRider/CRT"
{
    Properties
    {
        [ToggleUI] _Enabled ("Enable CRT", Float) = 1

        [Header(Mosaic)]
        _PixelHeight ("Vertical resolution (240p)", Float) = 240

        [Header(Horizontal blur)]
        _HBlur ("Horizontal blur", Range(0,1)) = 0.35

        [Header(Scanlines)]
        _SubRows          ("Sub-rows per line (3 = beam + 2 bloom)", Float) = 3
        _ScanlineStrength ("Scanline strength", Range(0,1)) = 0.7
        _BeamSoftness     ("Beam edge softness", Range(0.001,0.5)) = 0.15
        _BloomStrength    ("Bloom into gap rows", Range(0,1)) = 0.5
    }

    SubShader
    {
        Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" }
        ZWrite Off
        Cull Off

        Pass
        {
            Name "CRT"

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.core/Runtime/Utilities/Blit.hlsl"

            float _Enabled;
            float _PixelHeight;
            float _HBlur;
            float _SubRows;
            float _ScanlineStrength;
            float _BeamSoftness;
            float _BloomStrength;

            half3 SampleScreen(float2 uv)
            {
                return SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, uv).rgb;
            }

            half4 frag (Varyings input) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
                float2 uv = input.texcoord;

                half3 raw = SampleScreen(uv);
                if (_Enabled < 0.5) return half4(raw, 1.0);

                // 1. Mosaic: square 240p tiles (columns scale with window aspect).
                float vRes   = max(_PixelHeight, 1.0);
                float aspect = _ScreenParams.x / max(_ScreenParams.y, 1.0);
                float2 res   = float2(vRes * aspect, vRes);
                float2 cell  = 1.0 / res;
                float2 muv   = (floor(uv * res) + 0.5) * cell;

                // 2. Horizontal blur across neighbouring mosaic columns.
                half3 c  = SampleScreen(muv);
                half3 cL = SampleScreen(muv - float2(cell.x, 0.0));
                half3 cR = SampleScreen(muv + float2(cell.x, 0.0));
                float w  = saturate(_HBlur) * 0.5;
                half3 col = c * (1.0 - 2.0 * w) + (cL + cR) * w;

                // 3. Scanlines at _SubRows x granularity. Beam = middle 1/_SubRows of
                //    the line; the gap rows are lifted by the pixel's brightness.
                float sub      = max(_SubRows, 1.0);
                float f        = frac(uv.y * vRes);          // 0..1 within one 240p line
                float beamHalf = 0.5 / sub;                  // middle row half-width
                float d        = abs(f - 0.5);
                float beam     = 1.0 - smoothstep(beamHalf, beamHalf + _BeamSoftness, d);
                float luma     = max(col.r, max(col.g, col.b));
                float bloom    = (1.0 - beam) * saturate(_BloomStrength) * luma;
                float scan     = saturate(beam + bloom);
                col *= lerp(1.0, scan, saturate(_ScanlineStrength));

                return half4(col, 1.0);
            }
            ENDHLSL
        }
    }
}
