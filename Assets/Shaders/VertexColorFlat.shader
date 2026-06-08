Shader "Custom/VertexColorFlat"
{
    Properties
    {
        [Header(Toon shading)]
        _ShadeBands  ("Shade Bands",       Range(1,6)) = 3
        _Posterize   ("Posterize Amount",  Range(0,1)) = 0.65   // 略软的色阶，阴影过渡更柔和
        _AmbientBoost("Ambient Boost",     Range(0,2)) = 0.85   // 压暗整体，贴近夜森林冷调
        _ShadowTint  ("Shadow Tint",       Color) = (0.20, 0.32, 0.40, 1)  // 冷调深青阴影

        [Header(Facet variation)]
        _FacetVariation ("Facet Brightness Jitter", Range(0,0.5)) = 0.08
        _FacetHueShift  ("Facet Warm-Cool Shift",   Range(0,0.3)) = 0.04

        [Header(Height shading)]
        _HeightTint   ("Height Tint Color", Color) = (1.0, 0.97, 0.88, 1)
        _HeightTintAmt("Height Tint Amount", Range(0,1)) = 0.0
        _HeightRange  ("Height Range",       Vector) = (0, 8, 0, 0)

        [Header(Rim)]
        _RimColor    ("Rim Color",    Color) = (1,1,1,1)
        _RimStrength ("Rim Strength", Range(0,1)) = 0.12
        _RimPower    ("Rim Power",    Range(0.5,8)) = 3.0

        [Header(Spec glint)]
        _SpecColor2  ("Spec Color",    Color) = (1,1,1,1)
        _SpecStrength("Spec Strength", Range(0,1)) = 0.0
        _SpecSharp   ("Spec Sharpness", Range(1,64)) = 24
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

            // 阴影相关关键字
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile_fragment _ _SHADOWS_SOFT
            #pragma multi_compile_fragment _ _ADDITIONAL_LIGHT_SHADOWS
            #pragma multi_compile _ _ADDITIONAL_LIGHTS_VERTEX _ADDITIONAL_LIGHTS

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                float4 color      : COLOR;      // 区域基色
                float2 uv         : TEXCOORD0;  // uv.x/uv.y = 逐格随机种子
            };

            struct Varyings
            {
                float4 positionHCS : SV_POSITION;
                float3 positionWS  : TEXCOORD0;
                float4 color       : COLOR;
                float2 seed        : TEXCOORD1;
            };

            CBUFFER_START(UnityPerMaterial)
                float  _ShadeBands;
                float  _Posterize;
                float  _AmbientBoost;
                float4 _ShadowTint;
                float  _FacetVariation;
                float  _FacetHueShift;
                float4 _HeightTint;
                float  _HeightTintAmt;
                float4 _HeightRange;
                float4 _RimColor;
                float  _RimStrength;
                float  _RimPower;
                float4 _SpecColor2;
                float  _SpecStrength;
                float  _SpecSharp;
            CBUFFER_END

            Varyings vert(Attributes IN)
            {
                Varyings OUT;
                OUT.positionWS  = TransformObjectToWorld(IN.positionOS.xyz);
                OUT.positionHCS = TransformWorldToHClip(OUT.positionWS);
                OUT.color       = IN.color;
                OUT.seed        = IN.uv;
                return OUT;
            }

            half4 frag(Varyings IN) : SV_Target
            {
                // 每个三角面的硬法线（屏幕空间导数）→ 稳定的低多边形刻面
                float3 normalWS = normalize(cross(ddy(IN.positionWS), ddx(IN.positionWS)));
                float3 viewDir  = GetWorldSpaceNormalizeViewDir(IN.positionWS);

                float3 albedo = IN.color.rgb;

                // --- 逐面变化：亮度抖动 + 冷暖偏移，打破同色平板感 ---
                float jitter = (IN.seed.x - 0.5) * 2.0;          // -1..1
                albedo *= 1.0 + jitter * _FacetVariation;
                float warm = (IN.seed.y - 0.5) * 2.0 * _FacetHueShift;
                albedo += float3(warm, warm * 0.3, -warm);       // 暖向红、冷向蓝

                // --- 高度色调：高处偏暖亮，制造空间层次 ---
                float hT = saturate((IN.positionWS.y - _HeightRange.x) /
                                    max(1e-4, _HeightRange.y - _HeightRange.x));
                albedo = lerp(albedo, albedo * _HeightTint.rgb, hT * _HeightTintAmt);

                // --- 主光 + 阴影，色阶化 ---
                float4 shadowCoord = TransformWorldToShadowCoord(IN.positionWS);
                Light mainLight = GetMainLight(shadowCoord);
                float NdotL = saturate(dot(normalWS, mainLight.direction));
                float lit   = NdotL * mainLight.shadowAttenuation;

                // 色阶量化（toon），_Posterize 控制硬/软
                float hard = floor(lit * _ShadeBands + 0.5) / _ShadeBands;
                float toon = lerp(lit, hard, _Posterize);

                // 环境光（SH）：天空/地面渐变，避免阴影死黑，带出体积感
                half3 ambient = SampleSH(normalWS) * _AmbientBoost;
                // 背光面染上冷色阴影
                half3 shadowCol = lerp(_ShadowTint.rgb, half3(1,1,1), toon);

                half3 lighting = (ambient + mainLight.color * toon);
                half3 col = albedo * lighting * shadowCol;

                // --- Fresnel 描边：勾出刻面边缘轮廓 ---
                float rim = pow(1.0 - saturate(dot(normalWS, viewDir)), _RimPower);
                col += _RimColor.rgb * rim * _RimStrength;

                // --- 可选高光闪点（toon spec），默认关 ---
                if (_SpecStrength > 0.0)
                {
                    float3 h = normalize(mainLight.direction + viewDir);
                    float ndh = saturate(dot(normalWS, h));
                    float spec = step(0.5, pow(ndh, _SpecSharp));
                    col += _SpecColor2.rgb * spec * _SpecStrength * (lit > 0.01 ? 1.0 : 0.0);
                }

                return half4(col, 1.0);
            }
            ENDHLSL
        }

        UsePass "Universal Render Pipeline/Lit/ShadowCaster"
    }
}
