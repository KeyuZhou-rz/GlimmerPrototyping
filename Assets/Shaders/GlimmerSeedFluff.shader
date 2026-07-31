Shader "Glimmer/SeedFluff"
{
    // 种子絮专用：稀疏纤维轮廓，普通 alpha 混合（絮是浅色实物，不加色发光），
    // 随场景光沉暗 + 雾衰减（夜里不发亮）。供 ParticleSystemRenderer(Billboard) 使用。
    // 工艺同 Glimmer/RainStreak：硬边几何语言，无摄影式光晕。
    Properties
    {
        _FluffColor  ("Fluff Color", Color) = (0.90, 0.88, 0.78, 0.85)
        _EdgeSoft    ("Edge Softness", Range(0.05, 0.9)) = 0.55
        _FarFadeStart ("Far Fade Start", Float) = 40
        _FarFadeEnd   ("Far Fade End",   Float) = 75
    }

    SubShader
    {
        Tags { "RenderType"="Transparent" "Queue"="Transparent" "RenderPipeline"="UniversalPipeline" "IgnoreProjector"="True" }

        Pass
        {
            Name "FluffForward"
            Tags { "LightMode"="UniversalForward" }

            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            Cull Off

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_fog

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                float4 color      : COLOR;      // 粒子系统传入（含 alpha 淡入淡出）
                float2 uv         : TEXCOORD0;
            };

            struct Varyings
            {
                float4 positionHCS : SV_POSITION;
                float4 color       : COLOR;
                float2 uv          : TEXCOORD0;
                float  fogFactor   : TEXCOORD1;
            };

            CBUFFER_START(UnityPerMaterial)
                half4 _FluffColor;
                half  _EdgeSoft;
                float _FarFadeStart, _FarFadeEnd;
            CBUFFER_END

            Varyings vert(Attributes IN)
            {
                Varyings OUT;
                float3 posWS    = TransformObjectToWorld(IN.positionOS.xyz);
                OUT.positionHCS = TransformWorldToHClip(posWS);
                OUT.color       = IN.color;
                // 远距渐隐（同 RainStreak：亚像素点与天空色带交叠会闪断）
                float distEye = distance(posWS, _WorldSpaceCameraPos);
                OUT.color.a *= 1.0 - smoothstep(_FarFadeStart, _FarFadeEnd, distEye);
                OUT.uv          = IN.uv;
                OUT.fogFactor   = ComputeFogFactor(OUT.positionHCS.z);
                return OUT;
            }

            half4 frag(Varyings IN) : SV_Target
            {
                // 七束纤维围绕小绒芯，打破实心圆盘的发光球观感。
                float2 p = (IN.uv - 0.5) * 2.0;
                float r = length(p);
                float theta = atan2(p.y, p.x);
                float feather = lerp(0.025, 0.09, _EdgeSoft);
                float radial = (1.0 - smoothstep(0.78 - feather, 0.78 + feather, r))
                             * smoothstep(0.08, 0.20, r);
                float rays = pow(saturate(0.5 + 0.5 * cos(theta * 7.0)), 8.0);
                float hub = 1.0 - smoothstep(0.08, 0.18, r);
                float disc = saturate(max(radial * (0.18 + 0.82 * rays), hub * 0.55));

                half a = disc * _FluffColor.a * IN.color.a;
                half3 col = _FluffColor.rgb * IN.color.rgb;

                // 随场景光沉暗：浅色絮夜里必须熄进夜色，不能当自发光点
                Light mainLight = GetMainLight();
                half sceneLum = saturate(dot(mainLight.color, half3(0.3, 0.6, 0.1)) + 0.15);
                col *= sceneLum;

                col = MixFog(col, IN.fogFactor);

                return half4(col, a);
            }
            ENDHLSL
        }
    }
}
