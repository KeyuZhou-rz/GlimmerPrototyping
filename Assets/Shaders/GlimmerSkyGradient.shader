Shader "Glimmer/SkyGradient"
{
    // 桑人岩画天空（基调决议见 Docs/SkySanRockArt.md）：
    //   底色 = 三段垂直渐变，地平线色由控制器逐帧设为当前雾色 → 远山溶解；
    //   岩面 = 低频斑驳（矿物沁色）+ 细颗粒（岩壁齿感），白天克制，只负责消色带；
    //   太阳 = 颜料日盘（毛边圆 + 分段晕环，像赭石一圈圈涂上去，不做摄影级辉光）；
    //   夜空 = 撒灰银河（ǀXam 神话：少女掷灰成河）+ 骨白/赭红双色星点，
    //          整个星穹绕斜轴缓慢旋转 —— 世界自己的生命，与输入无关。
    // 零贴图、单 pass、无光照 include；_SunDir/_StarBlend/_SkyHorizon 由
    // EmotionWeatherController 独家驱动（单写者），shader 不读 URP 光源数据。
    Properties
    {
        [Header(Vertical gradient. horizon set to fog color by controller)]
        _SkyTop      ("Sky Top",            Color) = (0.34, 0.38, 0.44, 1)
        _SkyHorizon  ("Sky Horizon",        Color) = (0.66, 0.62, 0.55, 1)
        _GroundCol   ("Below Horizon",      Color) = (0.45, 0.41, 0.35, 1)
        _HorizonBlur ("Horizon Blur",       Range(0.02, 1)) = 0.35
        _Exposure    ("Exposure",           Range(0, 2)) = 1.0

        [Header(Pigment sun. dir written by controller from the real light)]
        _SunDir        ("Sun Direction",        Vector) = (0, 1, 0, 0)
        _SunTint       ("Sun Pigment",          Color) = (1.0, 0.72, 0.42, 1)
        _SunSize       ("Sun Disc Size Deg",    Range(0.5, 30)) = 5
        _SunGlow       ("Halo Strength",        Range(0, 3)) = 0.9
        _SunDiscStrength ("Disc Strength",      Range(0, 1)) = 1
        _SunEdgeRagged ("Disc Edge Raggedness", Range(0, 1)) = 0.45
        _HaloPosterize ("Halo Posterize",       Range(0, 1)) = 0.6

        [Header(Rock face weathering)]
        _GrainAmount  ("Grain Amount",  Range(0, 0.15)) = 0.028
        _GrainScale   ("Grain Scale",   Float) = 90
        _MottleAmount ("Mottle Amount", Range(0, 0.3)) = 0.06
        _MottleScale  ("Mottle Scale",  Float) = 2.3

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
                half4  _SkyTop, _SkyHorizon, _GroundCol;
                float  _HorizonBlur, _Exposure;
                float4 _SunDir;
                half4  _SunTint;
                float  _SunSize, _SunGlow, _SunDiscStrength, _SunEdgeRagged, _HaloPosterize;
                float  _GrainAmount, _GrainScale, _MottleAmount, _MottleScale;
                float  _StarBlend;
                half4  _StarColorA, _StarColorB;
                float  _StarDensity, _StarSize;
                half4  _AshColor;
                float  _AshStrength, _AshWidth, _SkyRotSpeed;
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

            // 星点层：3D 晶格取最近特征点。星点缩在格心 0.2~0.8 区间、半径 ≤0.15 格，
            // 保证单格采样不会裁掉邻格伸过来的星（省掉 27 格邻域查询）
            // 返回 (亮度, 配色随机数)
            float2 StarLayer(float3 d, float density, float size)
            {
                float3 p = d * density;
                float3 cell = floor(p);
                float3 f = p - cell;
                float3 h = Hash33(cell);
                float3 sp = 0.20 + 0.60 * h;
                float r = size * (0.55 + 0.90 * h.x);
                float spot = 1.0 - smoothstep(r * 0.4, r, length(f - sp));
                float present = step(0.62, Hash13(cell + 19.19));   // 稀疏：并非每格都有星
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

                // —— 1. 三段垂直渐变（岩面底色，地平线 = 雾色 → 天地一体）——
                float hb = max(_HorizonBlur, 0.02);
                // pow<1 把暖地平线带抬高一点，接近 demo 的宽暖带
                float tUp = smoothstep(0.0, 1.0, pow(saturate(y / hb), 0.8));
                float tDn = smoothstep(0.0, 1.0, saturate(-y / (hb * 0.6)));
                half3 col = lerp(lerp(_SkyHorizon.rgb, _GroundCol.rgb, tDn),
                                 lerp(_SkyHorizon.rgb, _SkyTop.rgb, tUp),
                                 step(0.0, y));

                // —— 2. 颜料日盘：毛边圆一笔盖上去（非叠加），晕环分段像一圈圈颜料washes ——
                float3 sunDir = normalize(_SunDir.xyz + float3(0, 1e-5, 0));
                float cosA = dot(d, sunDir);
                float edgeN = (ValueNoise3(d * 42.0) - 0.5) * _SunEdgeRagged * 0.02;
                float cosR = cos(radians(_SunSize));
                float disc = smoothstep(cosR - 0.006, cosR + 0.004, cosA + edgeN);
                float hSun = saturate(cosA);
                float inner = pow(hSun, 42.0);
                inner = lerp(inner, floor(inner * 3.0) / 3.0, _HaloPosterize);
                float outer = pow(hSun, 5.0) * 0.14;
                col = lerp(col, _SunTint.rgb * 1.06, disc * 0.92 * _SunDiscStrength);
                col += _SunTint.rgb * (inner + outer) * _SunGlow;

                // —— 3. 岩面风化：斑驳 + 颗粒。地平线附近淡出，保住与雾的无缝拼接 ——
                float weatherMask = smoothstep(0.015, 0.14, abs(y));
                float mottle = Fbm3(d * _MottleScale);
                col *= 1.0 - _MottleAmount * mottle * weatherMask;
                float grain = (ValueNoise3(d * _GrainScale) - 0.5) * 2.0;
                col *= 1.0 + grain * _GrainAmount * weatherMask;

                // —— 4. 夜空：撒灰银河 + 双色星点，绕斜天轴整体缓转 ——
                if (_StarBlend > 0.001)
                {
                    float3 axis = normalize(float3(0.30, 0.85, -0.43));   // 南天极式斜轴
                    float3 dr = RotateAround(d, axis, _Time.y * radians(_SkyRotSpeed));
                    float horizMask = smoothstep(0.04, 0.30, y);          // 星沉入地平线薄霭

                    // 银河 = 大圆灰带：法线定带的走向；fbm 撕出断续的手掷灰块
                    float3 bandN = normalize(float3(0.42, 0.18, 0.89));
                    float band = 1.0 - smoothstep(_AshWidth * 0.35, _AshWidth, abs(dot(dr, bandN)));
                    float patches = smoothstep(0.30, 0.78, Fbm3(dr * 3.1 + 7.7));
                    float3 coreDir = normalize(cross(bandN, float3(0, 1, 0)));
                    float core = 0.55 + 0.45 * saturate(dot(dr, coreDir)); // 一侧更稠（银心感）
                    half3 night = _AshColor.rgb * (band * patches * core * _AshStrength);

                    // 主星层：~14% 赭红余烬，其余骨白；微尘层沿灰带加密
                    float2 s1 = StarLayer(dr, _StarDensity, _StarSize);
                    float2 s2 = StarLayer(dr + 3.7, _StarDensity * 2.3, _StarSize * 0.55);
                    half3 starCol = lerp(_StarColorA.rgb, _StarColorB.rgb, step(0.86, s1.y));
                    night += starCol * s1.x;
                    night += _StarColorA.rgb * s2.x * 0.35 * (0.5 + 0.5 * band);

                    col += night * horizMask * _StarBlend;
                }

                return half4(col * _Exposure, 1.0);
            }
            ENDHLSL
        }
    }
}
