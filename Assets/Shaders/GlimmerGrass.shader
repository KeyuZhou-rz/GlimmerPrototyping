Shader "Glimmer/Grass"
{
    // 草簇专用着色器（demo2 对齐轮）：Glimmer/Toon 的 GPU instancing 分叉。
    // 与 Graphics.DrawMeshInstanced 配套（GrassSystem）——Toon 本体不支持 instancing。
    // 实心低模草簇：无贴图无 alpha，根→尖顶点渐变 + 风摆（属性名对齐 GrassSystem 的 MPB 写入）。
    Properties
    {
        _RootColor ("Root Color", Color) = (0.62, 0.60, 0.40, 1)
        _TipColor  ("Tip Color",  Color) = (0.80, 0.76, 0.55, 1)

        [Header(Toon shading)]
        _ShadeBands  ("Shade Bands",      Range(1,6)) = 3
        _Posterize   ("Posterize Amount", Range(0,1)) = 0.6
        _AmbientBoost("Ambient Boost",    Range(0,2)) = 1.0
        _ShadowTint  ("Shadow Tint",      Color) = (0.34, 0.40, 0.50, 1)
        _RimStrength ("Rim Strength", Range(0,1)) = 0.06
        _RimPower    ("Rim Power",    Range(0.5,8)) = 3.5

        [Header(Wind. driven by GrassSystem MPB)]
        _WindDirection ("Wind Direction", Vector) = (1, 0, 0, 0)
        _WindStrength  ("Wind Strength",  Float) = 0.8
        _WindFrequency ("Wind Frequency", Float) = 1.2
    }

    SubShader
    {
        Tags { "RenderType"="Opaque" "RenderPipeline"="UniversalPipeline" }

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode"="UniversalForward" }
            Cull Off   // 交叉面片双面可见

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag

            // DrawMeshInstanced 必需三件套之一
            #pragma multi_compile_instancing
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile_fragment _ _SHADOWS_SOFT
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
                UNITY_VERTEX_INPUT_INSTANCE_ID   // 必需三件套之二
            };

            struct Varyings
            {
                float4 positionHCS : SV_POSITION;
                float3 positionWS  : TEXCOORD0;
                float3 normalWS    : TEXCOORD1;
                float2 uv          : TEXCOORD2;
                float  fogFactor   : TEXCOORD3;
            };

            CBUFFER_START(UnityPerMaterial)
                half4  _RootColor, _TipColor;
                half   _ShadeBands, _Posterize, _AmbientBoost;
                half4  _ShadowTint;
                half   _RimStrength, _RimPower;
                float4 _WindDirection;   // MPB 逐批写入：普通 uniform 即可，无需逐实例 buffer
                float  _WindStrength, _WindFrequency;
            CBUFFER_END

            // 踩踏点（全局，WorldTraceBinder 每帧 SetGlobalVectorArray 写入；
            // 数组长度 16 与 binder 的 TRAMPLE_MAX 耦合——两侧同改）。
            // xy = 世界 XZ 中心, z = 半径, w = 强度 0..1。编辑态默认 0 → 无压痕。
            float  _TrampleCount;
            float4 _TramplePoints[16];

            Varyings vert(Attributes IN)
            {
                Varyings OUT;
                UNITY_SETUP_INSTANCE_ID(IN);   // 必需三件套之三：绑定逐实例 ObjectToWorld

                float3 posOS = IN.positionOS.xyz;

                // 风摆：以簇根为轴，uv.y² 加权（根部锚定），世界位相去同步
                float3 pivotWS = TransformObjectToWorld(float3(0, 0, 0));
                float phase = pivotWS.x * 0.9 + pivotWS.z * 1.4;
                float sway = sin(_Time.y * _WindFrequency + phase) * _WindStrength * 0.12;
                float w = IN.uv.y * IN.uv.y;
                float2 windDir = normalize(_WindDirection.xz + float2(1e-4, 0));

                OUT.positionWS = TransformObjectToWorld(posOS);
                OUT.positionWS.xz += windDir * sway * w;

                // 踩踏：近踩踏点的草外倒 + 压扁，权重沿用 w=uv.y²（根部锚定）
                int trampleN = (int)_TrampleCount;
                [loop] for (int t = 0; t < trampleN; t++)
                {
                    float2 c   = _TramplePoints[t].xy;
                    float  rad = max(_TramplePoints[t].z, 1e-3);
                    float  str = _TramplePoints[t].w;
                    float  d   = distance(pivotWS.xz, c);
                    float  fall = saturate(1.0 - d / rad) * str;
                    if (fall > 0.0)
                    {
                        float2 dir = d > 1e-3 ? (pivotWS.xz - c) / d : float2(1, 0);
                        OUT.positionWS.xz += dir * fall * 0.35 * w;
                        OUT.positionWS.y  -= (OUT.positionWS.y - pivotWS.y) * fall * 0.6 * w;
                    }
                }

                OUT.positionHCS = TransformWorldToHClip(OUT.positionWS);
                OUT.normalWS    = TransformObjectToWorldNormal(IN.normalOS);
                OUT.uv          = IN.uv;
                OUT.fogFactor   = ComputeFogFactor(OUT.positionHCS.z);
                return OUT;
            }

            half4 frag(Varyings IN) : SV_Target
            {
                half3 albedo = lerp(_RootColor.rgb, _TipColor.rgb, IN.uv.y);

                half3 col = GlimmerToonLight(normalize(IN.normalWS), IN.positionWS, albedo,
                                             _ShadeBands, _Posterize, _AmbientBoost,
                                             _ShadowTint.rgb, _RimStrength, _RimPower);

                col = MixFog(col, IN.fogFactor);
                return half4(col, 1.0);
            }
            ENDHLSL
        }

        // 不含 ShadowCaster/DepthOnly：草簇不投影（castShadows=false），
        // URP Lit 的 pass 也不会跟随风摆，留着反而错位。
    }
}
