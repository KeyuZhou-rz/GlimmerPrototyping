Shader "Glimmer/SkyGradient"
{
    // 桑人岩画天空（基调决议见 Docs/SkySanRockArt.md）：
    //   底色 = 三段垂直渐变，地平线色由控制器逐帧设为当前雾色 → 远山溶解；
    //   岩面 = 低频斑驳（矿物沁色）+ 细颗粒（岩壁齿感），白天克制，只负责消色带；
    //   太阳 = 日轮图腾（实心颜料盘 + 盘内凹槽环 + 骨白/赭红双刻环 + 交替短射线，
    //          每类刻线配凿痕暗边 —— 凿进岩壁再填颜料，废摄影级连续光晕）；
    //   夜空 = 点描撒灰银河（ǀXam 神话：少女掷灰成河 —— 骨白灰烬点 + 赭红余烬
    //          + 炭黑暗裂谷纵贯）+ 骨白/赭红双色星点，
    //          整个星穹绕斜轴缓慢旋转 —— 世界自己的生命，与输入无关。
    // 零贴图、单 pass、无光照 include；_SunDir/_StarBlend/_SkyHorizon 由
    // EmotionWeatherController 独家驱动（单写者），shader 不读 URP 光源数据。
    Properties
    {
        [Header(Four stop gradient. all runtime colors driven by controller)]
        _SkyZenith      ("Zenith",           Color) = (0.36, 0.46, 0.56, 1)
        _SkyMid         ("Mid Sky",          Color) = (0.56, 0.60, 0.62, 1)
        _HorizonGlowCol ("Horizon Glow",     Color) = (0.78, 0.74, 0.66, 1)
        _SkyHorizon     ("Fog Line",         Color) = (0.66, 0.62, 0.55, 1)
        _GroundCol      ("Below Horizon",    Color) = (0.66, 0.62, 0.55, 1)
        _GlowHeight     ("Glow Band Top Y",  Range(0.05, 0.4)) = 0.14
        _MidHeight      ("Mid Sky Top Y",    Range(0.3, 0.8)) = 0.50
        _Exposure       ("Exposure",         Range(0, 2)) = 1.0

        [Header(Painterly banding. IGN dithered)]
        _BandingAmount ("Banding Amount", Range(0, 1)) = 0.55

        [Header(Sunward warm wash. TLD style azimuthal asymmetry)]
        _SunWashCol ("Wash Color",    Color) = (0.90, 0.82, 0.68, 1)
        _SunWashAmt ("Wash Strength", Range(0, 1)) = 0.15

        [Header(Totem sun. dir written by controller from the real light)]
        _SunDir        ("Sun Direction",        Vector) = (0, 1, 0, 0)
        _SunTint       ("Sun Pigment",          Color) = (1.0, 0.72, 0.42, 1)
        _SunSize       ("Sun Disc Size Deg",    Range(0.5, 30)) = 5
        _SunGlow       ("Totem Ring Strength",  Range(0, 3)) = 0.9
        _SunDiscStrength ("Disc Strength",      Range(0, 1)) = 1
        _SunEdgeRagged ("Disc Edge Raggedness", Range(0, 1)) = 0.45
        _TotemRayCount ("Totem Ray Count",      Range(6, 32)) = 18
        _TotemRayLen   ("Totem Ray Outer q",    Range(2.1, 4)) = 2.8
        _CarveShadow   ("Carve Groove Shadow",  Range(0, 1)) = 0.30

        [Header(Rock face weathering and cirrus strokes)]
        _GrainAmount  ("Grain Amount",  Range(0, 0.15)) = 0.028
        _GrainScale   ("Grain Scale",   Float) = 90
        _MottleScale  ("Stroke Scale",  Float) = 2.3
        _StrokeAmount ("Stroke Amount", Range(0, 0.4)) = 0.05

        [Header(Night. ash sky. blend written by controller)]
        _StarBlend   ("Star Blend",             Range(0, 1)) = 0
        _StarColorA  ("Star Bone White",        Color) = (0.92, 0.90, 0.84, 1)
        _StarColorB  ("Star Ochre Ember",       Color) = (0.85, 0.42, 0.28, 1)
        _StarDensity ("Star Density",           Range(4, 40)) = 14
        _StarSize    ("Star Size",              Range(0.02, 0.3)) = 0.10
        _AshColor    ("Ash Band Color",         Color) = (0.72, 0.70, 0.66, 1)
        _AshStrength ("Ash Band Strength",      Range(0, 1)) = 0.35
        _AshWidth    ("Ash Band Width",         Range(0.05, 0.6)) = 0.22
        _SkyRotSpeed ("Sky Wheel Deg Per Sec",  Range(0, 2)) = 0.06

        [Header(Ash stipple and dark rift)]
        _StippleDensity  ("Stipple Density",      Range(40, 240)) = 110
        _StippleSize     ("Stipple Dot Size",     Range(0.02, 0.4)) = 0.15
        _StippleStrength ("Stipple Strength",     Range(0, 2)) = 1.0
        _RiftWidth       ("Rift Width Frac",      Range(0.1, 0.8)) = 0.38
        _RiftDepth       ("Rift Darkness",        Range(0, 1)) = 0.60
        _RiftWander      ("Rift Wander",          Range(0, 0.3)) = 0.10
    }

    SubShader
    {
        Tags { "Queue"="Background" "RenderType"="Background" "PreviewType"="Skybox" "RenderPipeline"="UniversalPipeline" }
        Cull Off ZWrite Off

        Pass
        {
            Name "GlimmerSky"

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4  _SkyZenith, _SkyMid, _HorizonGlowCol, _SkyHorizon, _GroundCol;
                float  _GlowHeight, _MidHeight, _Exposure;
                float  _BandingAmount;
                half4  _SunWashCol;
                float  _SunWashAmt;
                float4 _SunDir;
                half4  _SunTint;
                float  _SunSize, _SunGlow, _SunDiscStrength, _SunEdgeRagged;
                float  _TotemRayCount, _TotemRayLen, _CarveShadow;
                float  _GrainAmount, _GrainScale, _MottleScale, _StrokeAmount;
                float  _StarBlend;
                half4  _StarColorA, _StarColorB;
                float  _StarDensity, _StarSize;
                half4  _AshColor;
                float  _AshStrength, _AshWidth, _SkyRotSpeed;
                float  _StippleDensity, _StippleSize, _StippleStrength;
                float  _RiftWidth, _RiftDepth, _RiftWander;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
            };

            struct Varyings
            {
                float4 positionHCS : SV_POSITION;
                float3 dirOS       : TEXCOORD0;   // 天穹方向（skybox 网格对齐世界轴）
            };

            // Dave Hoskins 风格整数无关 hash：sin-hash 在方向域高频采样时会出现
            // 平台相关的条纹伪影，这里必须用位运算无关的乘加 hash
            float Hash13(float3 p)
            {
                p = frac(p * 0.1031);
                p += dot(p, p.zyx + 31.32);
                return frac((p.x + p.y) * p.z);
            }

            float3 Hash33(float3 p)
            {
                p = frac(p * float3(0.1031, 0.1030, 0.0973));
                p += dot(p, p.yxz + 33.33);
                return frac((p.xxy + p.yxx) * p.zyx);
            }

            float ValueNoise3(float3 p)
            {
                float3 i = floor(p);
                float3 f = frac(p);
                f = f * f * (3.0 - 2.0 * f);
                float nx00 = lerp(Hash13(i),                      Hash13(i + float3(1, 0, 0)), f.x);
                float nx10 = lerp(Hash13(i + float3(0, 1, 0)),    Hash13(i + float3(1, 1, 0)), f.x);
                float nx01 = lerp(Hash13(i + float3(0, 0, 1)),    Hash13(i + float3(1, 0, 1)), f.x);
                float nx11 = lerp(Hash13(i + float3(0, 1, 1)),    Hash13(i + float3(1, 1, 1)), f.x);
                return lerp(lerp(nx00, nx10, f.y), lerp(nx01, nx11, f.y), f.z);
            }

            float Fbm3(float3 p)
            {
                float v = 0.0;
                float a = 0.5;
                [unroll]
                for (int k = 0; k < 3; k++)
                {
                    v += a * ValueNoise3(p);
                    p = p * 2.13 + 5.7;
                    a *= 0.5;
                }
                return v;   // ≈ [0, 0.875]
            }

            float3 RotateAround(float3 v, float3 axis, float ang)
            {
                float s = sin(ang);
                float c = cos(ang);
                return v * c + cross(axis, v) * s + axis * dot(axis, v) * (1.0 - c);
            }

            // Interleaved Gradient Noise（屏幕空间）：打散色带量化的马赫带。
            // 近看有台阶、远看是渐变的关键 —— 抖动幅度约半个色带宽
            float IGN(float2 px)
            {
                return frac(52.9829189 * frac(0.06711056 * px.x + 0.00583715 * px.y));
            }

            // 分段色带量化：t∈[0,1] 切 n 带，IGN 抖动半带宽，_BandingAmount 控混合
            float BandT(float t, float n, float dither)
            {
                float tq = floor(t * n + dither) / n;
                return lerp(t, tq, _BandingAmount);
            }

            // 星点/点描层：3D 晶格取最近特征点。点缩在格心 0.2~0.8 区间、半径 ≤0.15 格，
            // 保证单格采样不会裁掉邻格伸过来的点（省掉 27 格邻域查询）
            // fillThresh: 空格比例（星空稀疏 0.62，点描撒灰 0.25）
            // 返回 (亮度, 配色随机数)
            float2 StarLayer(float3 d, float density, float size, float fillThresh)
            {
                float3 p = d * density;
                float3 cell = floor(p);
                float3 f = p - cell;
                float3 h = Hash33(cell);
                float3 sp = 0.20 + 0.60 * h;
                float r = size * (0.55 + 0.90 * h.x);
                float dist = length(f - sp);
                // 亚像素反闪烁：点径钳到 ≥1 像素足迹，能量守恒压亮度。
                // 否则 1px 星点随天穹旋转跨像素边界时逐帧灭亮 —— 运行时"灯在闪"。
                // 足迹必须用 fwidth(d)（方向连续）折算：fwidth(dist) 在格界跳变，
                // 导数尖峰会画出淡色格框
                float px = density * length(fwidth(d));
                float rC = max(r, px * 1.2);
                float spot = 1.0 - smoothstep(rC * 0.4, rC, dist);
                spot *= saturate((r * r) / (rC * rC));
                float present = step(fillThresh, Hash13(cell + 19.19));
                float bright = 0.30 + 0.70 * h.z;
                return float2(spot * present * bright, h.y);
            }

            Varyings vert(Attributes IN)
            {
                Varyings OUT;
                OUT.positionHCS = TransformObjectToHClip(IN.positionOS.xyz);
                OUT.dirOS = IN.positionOS.xyz;
                return OUT;
            }

            half4 frag(Varyings IN) : SV_Target
            {
                float3 d = normalize(IN.dirOS);
                float y = d.y;
                float dith = IGN(IN.positionHCS.xy) - 0.5;

                // —— 1. 四停垂直渐变：雾线→地平辉带→中天→天顶。每停局部 t
                //       各自过色带量化（辉带3/中天4/天顶4 ≈ 11 带）——
                //       近看读出台阶（呼应地形三段色阶），远看仍是渐变 ——
                float3 sunDirW = normalize(_SunDir.xyz + float3(0, 1e-5, 0));
                half3 col;
                if (y <= 0.0)
                {
                    col = _GroundCol.rgb;   // 地平线以下：纯雾色（远地形=全雾）
                }
                else
                {
                    // 方位因子提前算：辉带色本身要按日侧/背日侧调制
                    float2 dXZ = normalize(d.xz + float2(1e-5, 0));
                    float2 sXZ = normalize(sunDirW.xz + float2(1e-5, 0));
                    float azim = dot(dXZ, sXZ) * 0.5 + 0.5;

                    // 辉带/中天方位调制（TLD 关键）：日侧满暖，背日侧收敛向冷色
                    // （否则黄昏琥珀辉带和玫瑰中天全方位铺开，背日侧也被染橙粉）
                    half3 coolGlow = lerp(_SkyMid.rgb, _SkyZenith.rgb, 0.60);
                    half3 glowCol = lerp(coolGlow, _HorizonGlowCol.rgb, 0.22 + 0.78 * pow(azim, 1.6));
                    half3 midCol = lerp(lerp(_SkyMid.rgb, _SkyZenith.rgb, 0.45),
                                        _SkyMid.rgb, 0.30 + 0.70 * pow(azim, 1.3));

                    // 雾线停：贴地一窄条纯雾色，远山溶解的锚
                    float tFog  = smoothstep(0.0, 0.03, y);
                    // 辉带停：雾线上方的地平线辉光带
                    float tGlow = BandT(smoothstep(0.03, _GlowHeight, y), 3.0, dith);
                    // 中天停
                    float tMid  = BandT(smoothstep(_GlowHeight, _MidHeight, y), 4.0, dith);
                    // 天顶停
                    float tZen  = BandT(smoothstep(_MidHeight, 0.95, y), 4.0, dith);

                    col = lerp(_SkyHorizon.rgb, glowCol, tGlow);
                    col = lerp(col, midCol,         tMid);
                    col = lerp(col, _SkyZenith.rgb, tZen);
                    // 雾线保底：最底 3% 强制回雾色（色带量化不许碰这条线）
                    col = lerp(_SkyHorizon.rgb, col, tFog);

                    // —— 1b. 日侧暖洗（TLD 式方位不对称）：日侧地平线暖亮、
                    //         背日侧冷沉。wash 也过色带（3 段），图形语言统一 ——
                    float horizProx = 1.0 - saturate(y / 0.55);
                    float wash = BandT(pow(azim, 2.2) * horizProx, 3.0, dith) * _SunWashAmt;
                    col = lerp(col, _SunWashCol.rgb, wash);
                    float coolSide = (1.0 - azim) * horizProx * 0.18;
                    col = lerp(col, _SkyZenith.rgb, BandT(coolSide, 3.0, dith));
                }

                // —— 2. 卷云笔触（先于图腾，颜料盖在笔触上）：水平拉长的
                //       两八度噪声 —— 竖向高频压扁成横长条，读作画笔拖过的
                //       干刷痕，不再是各向同性的均匀脏度 ——
                float weatherMask = smoothstep(0.015, 0.14, abs(y));
                float stroke1 = ValueNoise3(d * float3(1.3, 6.0, 1.3) * _MottleScale);
                float stroke2 = ValueNoise3(d * float3(1.3, 6.0, 1.3) * _MottleScale * 2.7 + 13.1);
                float stroke = smoothstep(0.35, 0.75, stroke1 * 0.65 + stroke2 * 0.35);
                half3 strokeCol = lerp(_SkyMid.rgb, _HorizonGlowCol.rgb, 0.5);
                col = lerp(col, strokeCol, stroke * _StrokeAmount * weatherMask);

                // —— 3. 日轮图腾：先凿刻后填彩。废连续光晕数学，全部元素是
                //       q(以日盘半径为单位的极径)/theta(绕日方位角) 空间里的
                //       离散刻画：盘内凹槽环、骨白全环、赭红断续环、交替短射线，
                //       每类刻线配凿痕暗边 —— 凿进岩面的深度感 ——
                float3 sunDir = sunDirW;
                // 稳定正交基：world-up 参考；太阳过天顶时退化到 world-x
                float3 upRef = abs(sunDir.y) > 0.98 ? float3(1, 0, 0) : float3(0, 1, 0);
                float3 sunT = normalize(cross(upRef, sunDir));
                float3 sunB = cross(sunDir, sunT);
                float ang = acos(clamp(dot(d, sunDir), -1.0, 1.0));
                float q = ang / radians(_SunSize);            // 1 = 日盘边缘
                float theta = atan2(dot(d, sunB), dot(d, sunT));

                if (q < _TotemRayLen + 0.7)   // 图腾影响圈外整段跳过
                {
                    // 手绘毛边：方向域低频噪声抖动极径（所有环/射线共用同一抖动，
                    // 像同一只手刻出来的）
                    float qr = q + (ValueNoise3(d * 42.0) - 0.5) * _SunEdgeRagged * 0.16;

                    // 日落隐没：太阳沉下地平线，整幅图腾一起走（也修掉旧版
                    // 光晕夜里透到地平线下的问题）
                    float sunUpMask = smoothstep(-0.06, 0.04, sunDir.y);
                    float ringStr = saturate(_SunGlow) * sunUpMask;

                    // — 日盘（实心颜料饼）+ 盘内暗赭凹槽环 —
                    float disc   = 1.0 - smoothstep(0.97, 1.03, qr);
                    float ringIn = 1.0 - smoothstep(0.05, 0.10, abs(qr - 0.62));

                    // — 外刻环 A：骨白完整圆 —
                    float ringA = 1.0 - smoothstep(0.028, 0.055, abs(qr - 1.38));

                    // — 外刻环 B：赭红断续弧（24 段 hash 择 ~65% 存在）—
                    float segB  = floor((theta / TWO_PI + 0.5) * 24.0);
                    float segOn = step(0.35, Hash13(float3(segB, 17.3, 4.7)));
                    float ringB = (1.0 - smoothstep(0.025, 0.05, abs(qr - 1.80))) * segOn;

                    // — 短射线：N 根离散刻线，骨白/赭红逐根交替，长度逐根 hash —
                    float rayIdx = floor((theta / TWO_PI + 0.5) * _TotemRayCount);
                    float3 rayH  = Hash33(float3(rayIdx, 7.7, 21.1));
                    float rayOn  = step(0.15, rayH.x);                    // ~85% 存在
                    float rayEnd = lerp(2.35, _TotemRayLen, rayH.y);
                    float rayCenter = (rayIdx + 0.5) / _TotemRayCount * TWO_PI - PI;
                    float dTheta = theta - rayCenter;
                    dTheta = dTheta - TWO_PI * round(dTheta / TWO_PI);
                    float slat = dTheta * q;                              // 弧长单位 → 平行边刻线
                    float rayRadial = smoothstep(1.98, 2.12, qr)
                                    * (1.0 - smoothstep(rayEnd - 0.10, rayEnd + 0.06, qr));
                    float rayBody = (1.0 - smoothstep(0.05, 0.09, abs(slat))) * rayRadial * rayOn;

                    // — 凿痕暗边：环外侧/射线单侧的细暗线（先刻后填彩）—
                    float shadowA = 1.0 - smoothstep(0.018, 0.045, abs(qr - 1.47));
                    float shadowB = (1.0 - smoothstep(0.015, 0.04, abs(qr - 1.89))) * segOn;
                    float shadowR = (1.0 - smoothstep(0.02, 0.05, abs(slat - 0.13))) * rayRadial * rayOn;
                    float carve = max(shadowA, max(shadowB, shadowR)) * ringStr;
                    col *= 1.0 - carve * _CarveShadow;

                    // — 填彩（平涂覆盖，非加法 —— 图腾平涂感的关键）—
                    col = lerp(col, _SunTint.rgb * 1.06, disc * 0.92 * _SunDiscStrength * sunUpMask);
                    col = lerp(col, _SunTint.rgb * 0.55, ringIn * disc * 0.85 * _SunDiscStrength * sunUpMask);
                    col = lerp(col, _StarColorA.rgb, ringA * 0.85 * ringStr);
                    col = lerp(col, _StarColorB.rgb, ringB * 0.80 * ringStr);
                    half3 rayCol = lerp(_StarColorA.rgb, _StarColorB.rgb, fmod(rayIdx, 2.0));
                    col = lerp(col, rayCol, rayBody * 0.75 * ringStr);

                    // 一丝暖染：q 基二次衰减，在图腾影响圈边界前归零（严禁用
                    // pow(cosA,n)——它在 q 空间衰减太慢，会在分支边界切出可见圆盘）
                    float airGlow = saturate(1.0 - q / (_TotemRayLen + 0.55));
                    col += _SunTint.rgb * airGlow * airGlow * 0.045 * _SunGlow * sunUpMask;
                }

                // —— 4. 岩壁细颗粒：透过颜料（材料统一），地平线处淡出 ——
                float grain = (ValueNoise3(d * _GrainScale) - 0.5) * 2.0;
                col *= 1.0 + grain * _GrainAmount * weatherMask;

                // —— 5. 夜空：点描撒灰银河（骨白灰烬+赭红余烬+炭黑裂谷三色系）——
                //        灰带主体从连续雾换成手点的灰烬颗粒，一道蜿蜒暗裂谷
                //        纵贯全带（真实银河暗带，也是雕刻里的凿槽）
                if (_StarBlend > 0.001)
                {
                    float3 axis = normalize(float3(0.30, 0.85, -0.43));   // 南天极式斜轴
                    float3 dr = RotateAround(d, axis, _Time.y * radians(_SkyRotSpeed));
                    float horizMask = smoothstep(0.04, 0.30, y);          // 星沉入地平线薄霭

                    // 带坐标：bandCoord=0 是带中线；fbm 撕出断续的手掷灰块
                    float3 bandN = normalize(float3(0.42, 0.18, 0.89));
                    float bandCoord = dot(dr, bandN);
                    float band = 1.0 - smoothstep(_AshWidth * 0.35, _AshWidth, abs(bandCoord));
                    float patches = smoothstep(0.30, 0.78, Fbm3(dr * 3.1 + 7.7));
                    float3 coreDir = normalize(cross(bandN, float3(0, 1, 0)));
                    float core = 0.55 + 0.45 * saturate(dot(dr, coreDir)); // 一侧更稠（银心感）
                    float ashMask = band * patches * core;

                    // 暗裂谷：中线附近蜿蜒（fbm 摆动），乘性吃掉灰带 + 轻刻天空底色
                    float riftOff = (Fbm3(dr * 2.2 + 31.7) - 0.44) * _RiftWander * 2.0;
                    float rift = 1.0 - smoothstep(_RiftWidth * 0.5 * _AshWidth,
                                                  _RiftWidth * _AshWidth,
                                                  abs(bandCoord - riftOff));
                    rift *= smoothstep(0.25, 0.6, patches);   // 裂谷只在灰块存在处显形

                    // 底层薄雾垫底 30%：防纯点墨感，点描坐在余灰上
                    half3 night = _AshColor.rgb * (ashMask * _AshStrength * 0.30);

                    // 点描撒灰双层：骨白灰烬为主，~25% 赭红余烬（掷灰传说的火种）。
                    // 亮度×2.2：点要比旧雾带亮一档才能读出"颗粒"而非"噪声"
                    float2 st1 = StarLayer(dr + 11.3, _StippleDensity, _StippleSize, 0.25);
                    float2 st2 = StarLayer(dr + 27.9, _StippleDensity * 1.9, _StippleSize * 0.6, 0.25);
                    half3 ashDot1 = lerp(_AshColor.rgb, _StarColorB.rgb * 0.9, step(0.75, st1.y));
                    half3 ashDot2 = lerp(_AshColor.rgb, _StarColorB.rgb * 0.9, step(0.75, st2.y));
                    float stippleMask = ashMask * _StippleStrength * 2.2;
                    night += ashDot1 * st1.x * stippleMask;
                    night += ashDot2 * st2.x * stippleMask * 0.55;

                    // 微尘星层先于裂谷（被暗尘遮蔽）
                    float2 s2 = StarLayer(dr + 3.7, _StarDensity * 2.3, _StarSize * 0.55, 0.62);
                    night += _StarColorA.rgb * s2.x * 0.35 * (0.5 + 0.5 * band);

                    // 裂谷压暗（凿槽吃掉灰与微尘）
                    night *= 1.0 - rift * _RiftDepth;

                    // 主星层后于裂谷：亮星穿谷而过，~14% 赭红余烬
                    float2 s1 = StarLayer(dr, _StarDensity, _StarSize, 0.62);
                    half3 starCol = lerp(_StarColorA.rgb, _StarColorB.rgb, step(0.86, s1.y));
                    night += starCol * s1.x;

                    // 裂谷轻刻天空底色：凿槽比夜空更深一线
                    col *= 1.0 - rift * band * 0.25 * _StarBlend * horizMask;
                    col += night * horizMask * _StarBlend;
                }

                return half4(col * _Exposure, 1.0);
            }
            ENDHLSL
        }
    }
}
