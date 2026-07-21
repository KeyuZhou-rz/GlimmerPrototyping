Shader "Glimmer/Toon"
{
    // 树木/道具统一着色器：贴图 × 主色 → Glimmer toon 光照 → URP 雾。
    // 与 Glimmer/Terrain 共享 GlimmerToonCore，保证全场景光照语言一致。
    // cache-bump: 2026-07-21 强制全量变体按当前 67 关键字空间重编译（清 66 态陈旧缓存）
    Properties
    {
        _BaseMap   ("Albedo", 2D) = "white" {}
        _BaseColor ("Tint", Color) = (1,1,1,1)
        [Toggle(_USE_VERTEX_COLOR)] _UseVertexColor ("Multiply Vertex Color", Float) = 0

        [Header(Toon shading)]
        _ShadeBands  ("Shade Bands",      Range(1,6)) = 3
        _Posterize   ("Posterize Amount", Range(0,1)) = 0.65
        _AmbientBoost("Ambient Boost",    Range(0,2)) = 0.85
        _ShadowTint  ("Shadow Tint",      Color) = (0.20, 0.32, 0.40, 1)

        [Header(Rim)]
        _RimStrength ("Rim Strength", Range(0,1)) = 0.12
        _RimPower    ("Rim Power",    Range(0.5,8)) = 3.0

        [Header(Wind sway. leaves and thin props only)]
        _SwayAmount ("Sway Amount", Range(0,0.3)) = 0.0
        _SwaySpeed  ("Sway Speed",  Float) = 1.2

        [Header(Alpha clip)]
        [Toggle(_ALPHATEST_ON)] _AlphaClip ("Alpha Clip", Float) = 0
        _Cutoff ("Cutoff", Range(0,1)) = 0.5
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

            #pragma shader_feature_local _USE_VERTEX_COLOR
            #pragma shader_feature_local_fragment _ALPHATEST_ON
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
                float3 normalOS   : NORMAL;
                float2 uv         : TEXCOORD0;
                float4 color      : COLOR;
            };

            struct Varyings
            {
                float4 positionHCS : SV_POSITION;
                float3 positionWS  : TEXCOORD0;
                float3 normalWS    : TEXCOORD1;
                float2 uv          : TEXCOORD2;
                float4 color       : COLOR;
                float  fogFactor   : TEXCOORD3;
            };

            TEXTURE2D(_BaseMap); SAMPLER(sampler_BaseMap);

            CBUFFER_START(UnityPerMaterial)
                float4 _BaseMap_ST;
                half4  _BaseColor;
                half   _ShadeBands, _Posterize, _AmbientBoost;
                half4  _ShadowTint;
                half   _RimStrength, _RimPower;
                half   _SwayAmount, _SwaySpeed;
                half   _Cutoff;
            CBUFFER_END

            Varyings vert(Attributes IN)
            {
                Varyings OUT;
                float3 posOS = IN.positionOS.xyz;

                // 顶点风摆：以物体高度加权，根部不动
                if (_SwayAmount > 0.001)
                {
                    float3 pivotWS = TransformObjectToWorld(float3(0,0,0));
                    float phase = pivotWS.x * 0.7 + pivotWS.z * 1.3;
                    float sway = sin(_Time.y * _SwaySpeed + phase) * _SwayAmount;
                    posOS.x += sway * max(0, posOS.y);
                }

                OUT.positionWS  = TransformObjectToWorld(posOS);
                OUT.positionHCS = TransformWorldToHClip(OUT.positionWS);
                OUT.normalWS    = TransformObjectToWorldNormal(IN.normalOS);
                OUT.uv          = TRANSFORM_TEX(IN.uv, _BaseMap);
                OUT.color       = IN.color;
                OUT.fogFactor   = ComputeFogFactor(OUT.positionHCS.z);
                return OUT;
            }

            half4 frag(Varyings IN) : SV_Target
            {
                half4 tex = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, IN.uv);
                half3 albedo = tex.rgb * _BaseColor.rgb;
            #ifdef _USE_VERTEX_COLOR
                albedo *= IN.color.rgb;
            #endif
            #ifdef _ALPHATEST_ON
                clip(tex.a * _BaseColor.a - _Cutoff);
            #endif

                half3 col = GlimmerToonLight(normalize(IN.normalWS), IN.positionWS, albedo,
                                             _ShadeBands, _Posterize, _AmbientBoost,
                                             _ShadowTint.rgb, _RimStrength, _RimPower);

                col = MixFog(col, IN.fogFactor);
                return half4(col, 1.0);
            }
            ENDHLSL
        }

        UsePass "Universal Render Pipeline/Lit/ShadowCaster"
        UsePass "Universal Render Pipeline/Lit/DepthOnly"
    }

    FallBack "Universal Render Pipeline/Lit"
}
