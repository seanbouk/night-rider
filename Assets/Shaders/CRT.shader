// CRT post-process. The MOSAIC must hit the world but NOT the UI (downsampling
// crisp UI text wrecks it), while the HORIZONTAL BLUR and SCANLINES hit everything.
// So the UI is rendered to its own texture (_UITex, set globally by UiCanvas) and
// composited here AFTER the mosaic but BEFORE blur/scanlines:
//
//   world (_BlitTexture)  -> mosaic (nearest, 240p)        [world only]
//   UI (_UITex)           -> hard 0.5 cutout, laid on top  [crisp, no mosaic]
//   then  -> horizontal blur -> scanlines                  [the whole composite]
//
//   1. MOSAIC  — snap to a 320x240-ish grid, point-sampled (no interpolation).
//   2. H-BLUR  — slight bleed between neighbouring mosaic columns.
//   3. SCANLINES — 3x granularity: beam in the middle row, bloom rows above/below.

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

            TEXTURE2D(_UITex);   // UI rendered to its own RT (set globally by UiCanvas)

            float2 MosaicCell()
            {
                float vRes   = max(_PixelHeight, 1.0);
                float aspect = _ScreenParams.x / max(_ScreenParams.y, 1.0);
                return 1.0 / float2(vRes * aspect, vRes);
            }

            // World, mosaiced (nearest-neighbour to the 240p grid).
            half3 World(float2 uv, float2 cell)
            {
                float2 muv = (floor(uv / cell) + 0.5) * cell;
                return SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_PointClamp, muv).rgb;
            }

            // UI (crisp, hard cutout) over the mosaiced world.
            half3 Composite(float2 uv, float2 cell)
            {
                half4 ui = SAMPLE_TEXTURE2D(_UITex, sampler_PointClamp, uv);
                return ui.a >= 0.5 ? ui.rgb : World(uv, cell);
            }

            half4 frag (Varyings input) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
                float2 uv = input.texcoord;

                if (_Enabled < 0.5)
                {
                    // No CRT: still need the UI laid over the (un-mosaiced) world.
                    half4 ui = SAMPLE_TEXTURE2D(_UITex, sampler_PointClamp, uv);
                    half3 w  = SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_PointClamp, uv).rgb;
                    return half4(ui.a >= 0.5 ? ui.rgb : w, 1.0);
                }

                float2 cell = MosaicCell();

                // Horizontal blur across the composite (world mosaic + UI on top).
                half3 c  = Composite(uv, cell);
                half3 cL = Composite(uv - float2(cell.x, 0.0), cell);
                half3 cR = Composite(uv + float2(cell.x, 0.0), cell);
                float w  = saturate(_HBlur) * 0.5;
                half3 col = c * (1.0 - 2.0 * w) + (cL + cR) * w;

                // Scanlines at _SubRows x granularity, with brightness-fed bloom.
                float sub      = max(_SubRows, 1.0);
                float f        = frac(uv.y * max(_PixelHeight, 1.0));
                float beamHalf = 0.5 / sub;
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
