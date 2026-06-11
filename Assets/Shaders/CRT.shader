// CRT post-process. The MOSAIC hits the world but NOT the UI (the UI is rendered
// to _UITex by UiCanvas and composited here, crisp, via a 0.5 alpha cutout); the
// rest of the CRT (beam/scanlines, horizontal smear, halation) hits everything.
//
// We work in NES-ROW space (240 rows), resolution-independent — at the 720p web
// target that's 3 device rows per NES row, enough to show the beam shape. Three
// effects, all driven off the composited image:
//
//   BEAM/SCANLINES  one electron beam per NES row: a vertical Gaussian centred in
//                   the row. Its width grows with the row's brightness, so dark
//                   rows stay narrow (deep gaps) and bright rows fatten and fill
//                   the gap — this is what shapes the apparent gamut.
//   H-SMEAR         a normalised multi-tap horizontal Gaussian (brightness-
//                   preserving), so it reads as a smooth motion smear.
//   HALATION        an additive, brightness-gated 2D glow: bright pixels (by max
//                   channel) bleed in all directions; darks stay sharp.

Shader "NightRider/CRT"
{
    Properties
    {
        [ToggleUI] _Enabled ("Enable CRT", Float) = 1
        _PixelHeight ("Vertical resolution (NES rows)", Float) = 240

        [Header(Horizontal smear)]
        _HBlurWidth ("Smear width (NES px, sigma)", Range(0,3)) = 0.6

        [Header(Beam and scanlines)]
        _BeamWidth      ("Beam width (sub-row sigma, dark)", Range(0.05,1)) = 0.32
        _ScanlineDepth  ("Scanline depth (gap darkness)", Range(0,1)) = 0.6
        _BeamBrightWiden ("How much brightness fattens the beam", Range(0,3)) = 1.2

        [Header(Halation (brightness bloom))]
        _BloomAmount    ("Halation amount", Range(0,2)) = 0.5
        _BloomWidth     ("Halation width (NES px/rows, sigma)", Range(0.2,2)) = 1.2
        _BloomThreshold ("Halation threshold (below = sharp)", Range(0,1)) = 0.35
        _BloomCurve     ("Halation brightness curve", Range(0.5,4)) = 2.0
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

            // Tap radii (NES px / rows). Kept modest for a single pass; if we ever
            // want a much wider glow, that's the cue to go separable multi-pass.
            #define HSMEAR_R 3   // horizontal smear: +/- this many NES px
            #define HALO_R   2    // halation: +/- this many NES px/rows (square kernel)

            float _Enabled;
            float _PixelHeight;
            float _HBlurWidth;
            float _BeamWidth;
            float _ScanlineDepth;
            float _BeamBrightWiden;
            float _BloomAmount;
            float _BloomWidth;
            float _BloomThreshold;
            float _BloomCurve;

            TEXTURE2D(_UITex);   // UI rendered to its own RT (set globally by UiCanvas)

            float2 MosaicCell()
            {
                float vRes   = max(_PixelHeight, 1.0);
                float aspect = _ScreenParams.x / max(_ScreenParams.y, 1.0);
                return 1.0 / float2(vRes * aspect, vRes);   // (NES px, NES row) in uv
            }

            float MaxChan(half3 c) { return max(c.r, max(c.g, c.b)); }

            // World mosaiced (nearest to the NES grid) with the crisp UI cutout over it.
            half3 Composite(float2 uv, float2 cell)
            {
                float2 muv = (floor(uv / cell) + 0.5) * cell;
                half3 world = SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_PointClamp, muv).rgb;
                half4 ui    = SAMPLE_TEXTURE2D(_UITex, sampler_PointClamp, uv);
                return ui.a >= 0.5 ? ui.rgb : world;
            }

            half4 frag (Varyings input) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
                float2 uv = input.texcoord;

                if (_Enabled < 0.5)
                {
                    half4 ui = SAMPLE_TEXTURE2D(_UITex, sampler_PointClamp, uv);
                    half3 w  = SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_PointClamp, uv).rgb;
                    return half4(ui.a >= 0.5 ? ui.rgb : w, 1.0);
                }

                float2 cell = MosaicCell();

                // --- horizontal smear: normalised Gaussian along the pixel's row ----
                float sigH = max(_HBlurWidth, 1e-3);
                half3 base = 0;
                float wSum = 0;
                [unroll]
                for (int n = -HSMEAR_R; n <= HSMEAR_R; n++)
                {
                    float w = exp(-0.5 * (n * n) / (sigH * sigH));
                    base += Composite(uv + float2(n * cell.x, 0.0), cell) * w;
                    wSum += w;
                }
                base /= max(wSum, 1e-4);

                // --- beam / scanlines: vertical Gaussian, widened by brightness ------
                float f      = frac(uv.y / cell.y);                 // 0..1 within the NES row
                float sigB   = _BeamWidth * (1.0 + _BeamBrightWiden * MaxChan(base));
                float dvy    = (f - 0.5) / max(sigB, 1e-3);
                float beam   = exp(-0.5 * dvy * dvy);               // 1 at row centre
                beam         = lerp(1.0 - _ScanlineDepth, 1.0, beam);
                half3 col    = base * beam;

                // --- halation: additive brightness-gated 2D glow (all directions) ----
                float sigG = max(_BloomWidth, 1e-3);
                half3 glow = 0;
                float gSum = 0;
                [unroll]
                for (int gy = -HALO_R; gy <= HALO_R; gy++)
                [unroll]
                for (int gx = -HALO_R; gx <= HALO_R; gx++)
                {
                    float2 suv = uv + float2(gx * cell.x, gy * cell.y);
                    half3  c   = Composite(suv, cell);
                    float  b   = saturate((MaxChan(c) - _BloomThreshold) / max(1.0 - _BloomThreshold, 1e-3));
                    b          = pow(b, _BloomCurve);
                    float  sp  = exp(-0.5 * (gx * gx + gy * gy) / (sigG * sigG));
                    glow += c * (b * sp);
                    gSum += sp;
                }
                glow /= max(gSum, 1e-4);          // brightness-weighted average colour
                col = saturate(col + glow * _BloomAmount);

                return half4(col, 1.0);
            }
            ENDHLSL
        }
    }
}
