Shader "Glimmer/Terrain"
{
    // 程序化地形专用：高度带配色（河岸沙→平原草→高地干草）+ 坡度断崖色，
    // 刻面硬法线 + GlimmerToonCore 光照 + URP 雾。
    // _lowlandHeight/_plainHeight/_highlandHeight 由 TerrainGenerator 在生成后写入。
    Properties
    {
        [Header(Height band palette)]
        _SandColor    ("Riverbank Sand",   Color) = (0.76, 0.68, 0.50, 1)
        _LowlandColor ("Lowland Grass",    Color) = (0.45, 0.54, 0.28, 1)
        _PlainsColor  ("Plains Grass",     Color) = (0.52, 0.58, 0.30, 1)
        _HighlandColor("Highland Dry",     Color) = (0.62, 0.58, 0.34, 1)
        _PeakColor    ("Mountain Peak",    Color) = (0.55, 0.50, 0.46, 1)

        [Header(Band placement. world Y pushed by TerrainGenerator)]
        _lowlandHeight ("Lowland Y",  Float) = 0
        _plainHeight   ("Plains Y",   Float) = 5
        _highlandHeight("Highland Y", Float) = 10
        _BandSoftness  ("Band Softness", Range(0.05, 4)) = 1.0
        _BandNoiseAmp  ("Band Edge Noise", Range(0, 3)) = 0.7

        [Header(Cliff by slope)]
        _CliffColor    ("Cliff Color", Color) = (0.42, 0.36, 0.30, 1)
        _CliffStart    ("Cliff Slope Start", Range(0,1)) = 0.55
        _CliffSharp    ("Cliff Slope Sharpness", Range(0.01,0.5)) = 0.12

        [Header(Facet variation)]
        _FacetVariation ("Meadow Patch Strength", Range(0,0.5)) = 0.10
        _FacetScale     ("Facet Noise Scale", Float) = 0.35

        [Header(Toon shading. keep in sync with Glimmer Toon)]
        _ShadeBands  ("Shade Bands",      Range(1,6)) = 3
        _Posterize   ("Posterize Amount", Range(0,1)) = 0.65
        _AmbientBoost("Ambient Boost",    Range(0,2)) = 0.85
        _ShadowTint  ("Shadow Tint",      Color) = (0.20, 0.32, 0.40, 1)
        _RimStrength ("Rim Strength", Range(0,1)) = 0.08
        _RimPower    ("Rim Power",    Range(0.5,8)) = 3.5
    }

    SubShader
    {
        Tags { "RenderType"="Opaque" "RenderPipeline"="UniversalPipeline" }

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode"="UniversalForward" }

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag

            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile_fragment _ _SHADOWS_SOFT
            #pragma multi_compile_fragment _ _ADDITIONAL_LIGHT_SHADOWS
            #pragma multi_compile _ _ADDITIONAL_LIGHTS_VERTEX _ADDITIONAL_LIGHTS
            #pragma multi_compile_fog

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            #include "GlimmerToonCore.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv         : TEXCOORD0;
            };

            struct Varyings
            {
                float4 positionHCS : SV_POSITION;
                float3 positionWS  : TEXCOORD0;
                float2 uv          : TEXCOORD1;
                float  fogFactor   : TEXCOORD2;
            };

            CBUFFER_START(UnityPerMaterial)
                half4 _SandColor, _LowlandColor, _PlainsColor, _HighlandColor, _PeakColor;
                float _lowlandHeight, _plainHeight, _highlandHeight;
                float _BandSoftness, _BandNoiseAmp;
                half4 _CliffColor;
                float _CliffStart, _CliffSharp;
                float _FacetVariation, _FacetScale;
                half  _ShadeBands, _Posterize, _AmbientBoost;
                half4 _ShadowTint;
                half  _RimStrength, _RimPower;
            CBUFFER_END

            float Hash2(float2 p)
            {
                return frac(sin(dot(p, float2(127.1, 311.7))) * 43758.5453);
            }

            float ValueNoise(float2 p)
            {
                float2 i = floor(p);
                float2 f = frac(p);
                f = f * f * (3.0 - 2.0 * f);
                float a = Hash2(i);
                float b = Hash2(i + float2(1, 0));
                float c = Hash2(i + float2(0, 1));
                float d = Hash2(i + float2(1, 1));
                return lerp(lerp(a, b, f.x), lerp(c, d, f.x), f.y);
            }

            Varyings vert(Attributes IN)
            {
                Varyings OUT;
                OUT.positionWS  = TransformObjectToWorld(IN.positionOS.xyz);
                OUT.positionHCS = TransformWorldToHClip(OUT.positionWS);
                OUT.uv          = IN.uv;
                OUT.fogFactor   = ComputeFogFactor(OUT.positionHCS.z);
                return OUT;
            }

            half4 frag(Varyings IN) : SV_Target
            {
                // 刻面硬法线：保持低多边形语言
                float3 normalWS = normalize(cross(ddy(IN.positionWS), ddx(IN.positionWS)));

                // --- 高度带配色：边界加低频噪声扰动，避免等高线感 ---
                // 频率必须低（波长几十米）：高频噪声在平地上会画出同心环纹
                float bandNoise = (ValueNoise(IN.positionWS.xz * 0.035) - 0.5) * 2.0 * _BandNoiseAmp;
                float y = IN.positionWS.y + bandNoise;
                float soft = max(0.05, _BandSoftness);

                // 沙带贴着 lowland 高度以下（河岸）
                float tSand  = 1.0 - smoothstep(_lowlandHeight - soft, _lowlandHeight + soft * 0.6, y);
                float tPlain = smoothstep(_plainHeight - soft * 2.0, _plainHeight + soft, y);
                float tHigh  = smoothstep(_highlandHeight - soft * 2.0, _highlandHeight + soft, y);
                // 山峰：高于 highland 一段距离后渐变为岩色
                float peakY  = _highlandHeight + max(2.0, (_highlandHeight - _plainHeight));
                float tPeak  = smoothstep(peakY - soft * 2.0, peakY + soft * 2.0, y);

                half3 col = _LowlandColor.rgb;
                col = lerp(col, _PlainsColor.rgb,  tPlain);
                col = lerp(col, _HighlandColor.rgb, tHigh);
                col = lerp(col, _PeakColor.rgb,     tPeak);
                col = lerp(col, _SandColor.rgb,     tSand);

                // --- 坡度断崖：法线越平（斜坡/崖壁）越染岩色 ---
                float slope = 1.0 - saturate(normalWS.y);           // 0 平地, 1 竖直
                float cliffT = smoothstep(_CliffStart, _CliffStart + _CliffSharp, slope);
                col = lerp(col, _CliffColor.rgb, cliffT);

                // --- 大尺度草甸色斑：低频值噪声代替逐格 hash（hash 网格会和
                //     三角面产生干涉点纹）。塞尔达式的草原是数米级的软色块。---
                float meadow = ValueNoise(IN.positionWS.xz * 0.045);
                float meadow2 = ValueNoise(IN.positionWS.xz * 0.013 + 37.0);
                float jitter = (meadow * 0.6 + meadow2 * 0.4 - 0.5) * 2.0;
                col *= 1.0 + jitter * _FacetVariation;

                half3 lit = GlimmerToonLight(normalWS, IN.positionWS, col,
                                             _ShadeBands, _Posterize, _AmbientBoost,
                                             _ShadowTint.rgb, _RimStrength, _RimPower);

                lit = MixFog(lit, IN.fogFactor);
                return half4(lit, 1.0);
            }
            ENDHLSL
        }

        UsePass "Universal Render Pipeline/Lit/ShadowCaster"
        UsePass "Universal Render Pipeline/Lit/DepthOnly"
    }
}
