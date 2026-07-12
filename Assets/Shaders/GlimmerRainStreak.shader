Shader "Glimmer/RainStreak"
{
    // 雨滴专用：竖向柔和渐隐的雨条，Additive-ish 混合但带雾衰减，
    // 颜色低饱和偏青，融入夜色而不是白色亮点。
    // 供 ParticleSystemRenderer(Stretched Billboard) 使用。
    Properties
    {
        _StreakColor ("Streak Color", Color) = (0.62, 0.72, 0.80, 0.55)
        _CoreBoost   ("Core Brightness", Range(0,2)) = 0.35
        _EdgeSoft    ("Horizontal Softness", Range(0.05, 0.5)) = 0.22
        _TipFade     ("Vertical Tip Fade", Range(0.05, 0.5)) = 0.30
        _FarFadeStart ("Far Fade Start", Float) = 40
        _FarFadeEnd   ("Far Fade End",   Float) = 75
    }

    SubShader
    {
        Tags { "RenderType"="Transparent" "Queue"="Transparent" "RenderPipeline"="UniversalPipeline" "IgnoreProjector"="True" }

        Pass
        {
            Name "RainForward"
            Tags { "LightMode"="UniversalForward" }

            Blend SrcAlpha One          // 加色但由 alpha 控制强度：雨在暗景里发微光，亮景里不刺眼
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
                half4 _StreakColor;
                half  _CoreBoost;
                half  _EdgeSoft;
                half  _TipFade;
                float _FarFadeStart, _FarFadeEnd;
            CBUFFER_END

            Varyings vert(Attributes IN)
            {
                Varyings OUT;
                float3 posWS    = TransformObjectToWorld(IN.positionOS.xyz);
                OUT.positionHCS = TransformWorldToHClip(posWS);
                OUT.color       = IN.color;
                // 远距硬渐隐：拉伸公告板在远处塌成亚像素竖线，与天空色带
                // 交叠时逐帧闪断（时隐时现="天上的灯在闪"）。线性雾衰减
                // 不够快，这里在 alpha 上直接掐掉远端
                float distEye = distance(posWS, _WorldSpaceCameraPos);
                OUT.color.a *= 1.0 - smoothstep(_FarFadeStart, _FarFadeEnd, distEye);
                OUT.uv          = IN.uv;
                OUT.fogFactor   = ComputeFogFactor(OUT.positionHCS.z);
                return OUT;
            }

            half4 frag(Varyings IN) : SV_Target
            {
                // 横向：中心亮、两侧柔和衰减（雨丝细芯）
                float dx = abs(IN.uv.x - 0.5) * 2.0;
                float horiz = 1.0 - smoothstep(1.0 - _EdgeSoft * 2.0, 1.0, dx);
                float core  = 1.0 - smoothstep(0.0, _EdgeSoft, dx);

                // 纵向：两端渐隐（拉伸公告板的头尾）
                float tip = smoothstep(0.0, _TipFade, IN.uv.y) *
                            (1.0 - smoothstep(1.0 - _TipFade, 1.0, IN.uv.y));

                half a = horiz * tip * _StreakColor.a * IN.color.a;
                half3 col = _StreakColor.rgb * (1.0 + core * _CoreBoost) * IN.color.rgb;

                // 随场景光沉暗：加色混合的雨丝夜里必须跟着环境一起熄灭，
                // 否则日落后每根雨丝都是黑天上的亮线 —— 远处积成持续闪烁的光斑
                Light mainLight = GetMainLight();
                half sceneLum = saturate(dot(mainLight.color, half3(0.3, 0.6, 0.1)) + 0.15);
                col *= sceneLum;

                // 雾：远处雨丝随场景一起衰减，避免"贴脸白线"
                col = MixFogColor(col, half3(0, 0, 0), IN.fogFactor);

                return half4(col, a);
            }
            ENDHLSL
        }
    }
}
