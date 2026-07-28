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

        [Header(Baked terrain AO. written by Bake Terrain AO menu)]
        _TerrainAO       ("Terrain AO", 2D) = "white" {}
        // x=worldMinX y=worldMinZ z=worldSize w=strength(0=off, 烘焙前保持 0)
        _TerrainAOBounds ("AO Bounds (minX, minZ, size, strength)", Vector) = (0, 0, 100, 0)
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
                float  moisture    : TEXCOORD4;   // Batch 4：簇心湿度（zone 混合后）
                float  trample     : TEXCOORD5;   // 簇级最大踩踏权重；形变、风摆和草叶色共用
            };

            CBUFFER_START(UnityPerMaterial)
                half4  _RootColor, _TipColor;
                half   _ShadeBands, _Posterize, _AmbientBoost;
                half4  _ShadowTint;
                half   _RimStrength, _RimPower;
                float4 _WindDirection;   // MPB 逐批写入：普通 uniform 即可，无需逐实例 buffer
                float  _WindStrength, _WindFrequency;
                float4 _TerrainAOBounds;
            CBUFFER_END

            TEXTURE2D(_TerrainAO);
            SAMPLER(sampler_TerrainAO);

            // 踩踏点（全局，WorldTraceBinder 每帧 SetGlobalVectorArray 写入；
            // 数组长度 16 与 binder 的 TRAMPLE_MAX 耦合——两侧同改）。
            // xy = 世界 XZ 中心, z = 半径, w = 强度 0..1。编辑态默认 0 → 无压痕。
            float  _TrampleCount;
            float4 _TramplePoints[16];

            // Batch 4 草色通路（全局，WorldAtmosphereBinder LateUpdate 写入）：
            // zone 锚点+湿度（仿 _TramplePoints；xy=世界XZ中心, z=半径, w 未用），
            // vert 按簇心距离加权混湿度；季节/旱/衰败为全局标量+色。
            // 编辑态默认：无 zone（_GrassZoneCount=0 → 用默认湿度）、白季节、零旱零衰 = 无影响。
            float  _GrassZoneCount;
            float4 _GrassZoneAnchors[8];
            float  _GrassZoneMoisture[8];
            float  _GrassDefaultMoisture;
            half4  _GrassMoistDry, _GrassMoistWet;
            half4  _GrassSeasonTint;
            half4  _GrassDroughtTint;
            float  _GrassDroughtAmt;
            float  _GrassDecay, _GrassDecayDesat, _GrassDecayDarken;
            float  _GrassColorEnable;   // 0=编辑态无 binder（四级全旁路，旧观感）；1=play 通路开

            Varyings vert(Attributes IN)
            {
                Varyings OUT;
                UNITY_SETUP_INSTANCE_ID(IN);   // 必需三件套之三：绑定逐实例 ObjectToWorld

                float3 posOS = IN.positionOS.xyz;

                float3 pivotWS = TransformObjectToWorld(float3(0, 0, 0));
                float w = IN.uv.y * IN.uv.y;
                OUT.positionWS = TransformObjectToWorld(posOS);

                // 踩踏：单次 16 点循环同时形变并求每簇最大权重，不扩大既有循环预算。
                float maxTrample = 0.0;
                int trampleN = (int)_TrampleCount;
                [loop] for (int t = 0; t < trampleN; t++)
                {
                    float2 c   = _TramplePoints[t].xy;
                    float  rad = max(_TramplePoints[t].z, 1e-3);
                    float  str = _TramplePoints[t].w;
                    float  d   = distance(pivotWS.xz, c);
                    float  fall = saturate(1.0 - d / rad) * str;
                    maxTrample = max(maxTrample, fall);
                    if (fall > 0.0)
                    {
                        float2 dir = d > 1e-3 ? (pivotWS.xz - c) / d : float2(1, 0);
                        OUT.positionWS.xz += dir * fall * 0.35 * w;
                        OUT.positionWS.y  -= (OUT.positionWS.y - pivotWS.y) * fall * 0.6 * w;
                    }
                }

                // 风摆仍以簇根为轴；受压簇稍稳，避免 1.1m 压痕被大幅摆动冲淡。
                float phase = pivotWS.x * 0.9 + pivotWS.z * 1.4;
                float sway = sin(_Time.y * _WindFrequency + phase) * _WindStrength * 0.12;
                float2 windDir = normalize(_WindDirection.xz + float2(1e-4, 0));
                OUT.positionWS.xz += windDir * sway * w * lerp(1.0, 0.45, maxTrample);

                OUT.positionHCS = TransformWorldToHClip(OUT.positionWS);
                OUT.normalWS    = TransformObjectToWorldNormal(IN.normalOS);
                OUT.uv          = IN.uv;
                OUT.fogFactor   = ComputeFogFactor(OUT.positionHCS.z);
                OUT.trample     = maxTrample;

                // Batch 4：簇心到各 zone 锚点距离加权混湿度（影响圈 = 半径×2.5，软边平方衰减）；
                // 所有权重≈0（远离任何 zone）时回落全局默认湿度。
                float mSum = 0.0, wSum = 0.0;
                int zn = (int)_GrassZoneCount;
                [loop] for (int zi = 0; zi < zn; zi++)
                {
                    float4 a = _GrassZoneAnchors[zi];
                    float zd = distance(pivotWS.xz, a.xy);
                    float zw = saturate(1.0 - zd / max(a.z * 2.5, 1e-3));
                    zw *= zw;
                    mSum += zw * _GrassZoneMoisture[zi];
                    wSum += zw;
                }
                OUT.moisture = wSum > 1e-4 ? mSum / wSum : _GrassDefaultMoisture;
                return OUT;
            }

            half4 frag(Varyings IN) : SV_Target
            {
                half3 albedo = lerp(_RootColor.rgb, _TipColor.rgb, IN.uv.y);

                // Batch 4 四级调色链（映射表 = GrassPreset，binder 换算后写入）：
                // ① 湿度（逐簇）：干 straw ↔ 湿青绿；② 季节：夏青冬金
                // ③ 旱枯黄（droughtDebt 过 0.3 起混入）；④ 衰败：去饱和 + 压暗
                // _GrassColorEnable=0（编辑态无 binder）时①②旁路、③④量为零 → 完全旧观感。
                albedo *= lerp(half3(1, 1, 1), lerp(_GrassMoistDry.rgb, _GrassMoistWet.rgb, IN.moisture), _GrassColorEnable);
                albedo *= lerp(half3(1, 1, 1), _GrassSeasonTint.rgb, _GrassColorEnable);
                albedo = lerp(albedo, _GrassDroughtTint.rgb, _GrassDroughtAmt);
                // ④ 衰败（DecayLevel）：去饱和 + 压暗
                half lum = dot(albedo, half3(0.299, 0.587, 0.114));
                albedo = lerp(albedo, lum.xxx, _GrassDecay * _GrassDecayDesat);
                albedo *= 1.0 - _GrassDecay * _GrassDecayDarken;

                // 压痕只发生在草叶本身：去饱和并压暗叶尖，不引入地表色片/decal。
                half pressedLum = dot(albedo, half3(0.299, 0.587, 0.114));
                albedo = lerp(albedo, pressedLum.xxx, IN.trample * 0.28);
                albedo *= 1.0 - IN.trample * lerp(0.14, 0.24, saturate(IN.uv.y));

                // 批次4（07-28）：草随地形 AO 同沉——洼里的草和洼里的地吃同一层稀薄天光，
                // 消除地面/植被"两张皮"。strength=0（未烘焙）时无效果。
                float aoTex = SAMPLE_TEXTURE2D(_TerrainAO, sampler_TerrainAO,
                                               (IN.positionWS.xz - _TerrainAOBounds.xy) / _TerrainAOBounds.z).r;
                half ao = lerp(1.0h, (half)(aoTex * 2.0), (half)_TerrainAOBounds.w);

                half3 col = GlimmerToonLight(normalize(IN.normalWS), IN.positionWS, albedo,
                                             _ShadeBands, _Posterize, _AmbientBoost,
                                             _ShadowTint.rgb, _RimStrength, _RimPower, ao);

                col = MixFog(col, IN.fogFactor);
                return half4(col, 1.0);
            }
            ENDHLSL
        }

        // 不含 ShadowCaster/DepthOnly：草簇不投影（castShadows=false），
        // URP Lit 的 pass 也不会跟随风摆，留着反而错位。
    }
}
