Shader "Custom/StylizedWater"
{
    Properties
    {
        [Header(Depth gradient)]
        // 参考场景的水：暗、低饱和的青绿，几乎静止。
        _ShallowColor ("Shallow Color", Color) = (0.12, 0.30, 0.32, 0.70)
        _DeepColor    ("Deep Color",    Color) = (0.03, 0.11, 0.15, 0.95)
        _DepthMax     ("Depth Max (world units)",  Float) = 1.4
        _DepthFalloff ("Depth Falloff power",      Range(0.2, 4)) = 1.0

        [Header(Highlight)]
        _HighlightColor ("Highlight Color",          Color) = (0.50, 0.68, 0.66, 1.0)  // 暗淡冷光，非纯白
        _HiThreshold    ("Highlight Threshold NdotL", Range(-1,1)) = 0.72              // 阈值更高 → 只有零星反光
        _HiSoftness     ("Highlight Softness",        Range(0.001,0.5)) = 0.05

        [Header(Flow wave along Z)]
        _FlowAmp   ("Flow Amplitude",  Range(0,0.3)) = 0.04   // 水面近乎平静
        _FlowFreq  ("Flow Frequency",  Float) = 0.6
        _FlowSpeed ("Flow Speed",      Float) = 0.6

        [Header(Lateral wave along X)]
        _LatAmp   ("Lateral Amplitude", Range(0,0.3)) = 0.025
        _LatFreq  ("Lateral Frequency", Float) = 0.25
        _LatSpeed ("Lateral Speed",     Float) = 0.4
    }

    SubShader
    {
        Tags { "RenderType"="Transparent" "Queue"="Transparent" "RenderPipeline"="UniversalPipeline" }

        Pass
        {
            Name "ForwardUnlit"
            Tags { "LightMode"="UniversalForward" }

            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            Cull Back

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_fog
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            // 提供 _CameraDepthTexture / SampleSceneDepth：从中重建“水面到河床”的真实水深。
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv         : TEXCOORD0;   // uv.x = 0 中心 → 1 岸边；uv.y = 沿流向参数
            };

            struct Varyings
            {
                float4 positionHCS : SV_POSITION;
                float3 positionWS  : TEXCOORD0;
                float  fogFactor   : TEXCOORD1;
            };

            CBUFFER_START(UnityPerMaterial)
                float4 _ShallowColor;
                float4 _DeepColor;
                float  _DepthMax;
                float  _DepthFalloff;
                float4 _HighlightColor;
                float  _HiThreshold;
                float  _HiSoftness;
                float  _FlowAmp;
                float  _FlowFreq;
                float  _FlowSpeed;
                float  _LatAmp;
                float  _LatFreq;
                float  _LatSpeed;
            CBUFFER_END

            Varyings vert(Attributes IN)
            {
                Varyings OUT;
                float3 posOS = IN.positionOS.xyz;
                float  t = _Time.y;

                // 两层 sin：一层沿水流方向 (Z) 传播，一层横向低频 (X)。
                float flow = sin(posOS.z * _FlowFreq + t * _FlowSpeed) * _FlowAmp;
                float lat  = sin(posOS.x * _LatFreq  + t * _LatSpeed ) * _LatAmp;
                posOS.y += flow + lat;

                OUT.positionWS  = TransformObjectToWorld(posOS);
                OUT.positionHCS = TransformWorldToHClip(OUT.positionWS);
                OUT.fogFactor   = ComputeFogFactor(OUT.positionHCS.z);
                return OUT;
            }

            half4 frag(Varyings IN) : SV_Target
            {
                // --- 真实水深：水面片元 vs 其后方不透明河床的线性视深之差 ---
                float2 screenUV   = IN.positionHCS.xy / _ScreenParams.xy;
                float  sceneRaw   = SampleSceneDepth(screenUV);
                float  sceneEye   = LinearEyeDepth(sceneRaw, _ZBufferParams);          // 河床
                float  surfaceEye = LinearEyeDepth(IN.positionHCS.z, _ZBufferParams);  // 水面
                float  waterDepth = max(0.0, sceneEye - surfaceEye);

                float depthT = saturate(waterDepth / max(1e-4, _DepthMax));
                depthT = pow(depthT, _DepthFalloff);   // 调整浅→深的过渡曲线

                // 浅水→深水颜色与透明度都做插值（深水更深、更不透明）。
                float3 col   = lerp(_ShallowColor.rgb, _DeepColor.rgb, depthT);
                float  alpha = lerp(_ShallowColor.a,   _DeepColor.a,   depthT);

                // --- 低多边形高光：屏幕空间导数求面法线，超阈值给接近白色高亮 ---
                float3 normalWS = normalize(cross(ddy(IN.positionWS), ddx(IN.positionWS)));
                Light  mainLight = GetMainLight();
                float  NdotL = dot(normalWS, mainLight.direction);
                float  hi = smoothstep(_HiThreshold, _HiThreshold + _HiSoftness, NdotL);

                col = lerp(col, _HighlightColor.rgb, hi);

                // 水随场景光照明暗：夜里沉入夜色，不再自发光
                half3 sceneLight = SampleSH(float3(0, 1, 0)) + mainLight.color;
                col *= saturate(sceneLight) * 0.85 + 0.06;

                // 水面与陆地同雾衰减，保持远景一体
                col = MixFog(col, IN.fogFactor);

                return half4(col, alpha);
            }
            ENDHLSL
        }
    }
}
