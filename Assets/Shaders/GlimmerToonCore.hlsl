#ifndef GLIMMER_TOON_CORE_INCLUDED
#define GLIMMER_TOON_CORE_INCLUDED

// Glimmer 统一风格光照核心：所有实体着色器（地形/树木/道具）共用，
// 保证色阶数、阴影冷调、边缘光行为完全一致 —— 风格统一的单一事实来源。
// 依赖：调用方已 include URP Core.hlsl + Lighting.hlsl。

half3 GlimmerToonLight(float3 normalWS, float3 positionWS, half3 albedo,
                       half bands, half posterize, half ambientBoost,
                       half3 shadowTint, half rimStrength, half rimPower, half ao = 1)
{
    float4 shadowCoord = TransformWorldToShadowCoord(positionWS);
    Light mainLight = GetMainLight(shadowCoord);

    float NdotL = saturate(dot(normalWS, mainLight.direction));
    float lit   = NdotL * mainLight.shadowAttenuation;

    // 色阶量化（toon）：_Posterize 控制硬/软过渡。
    // 软台阶：台阶保留（岩画平面感），但每档边缘以像素自适应宽度羽化，
    // 消除 floor 硬切线的"素材感"色块，明暗过渡读作天鹅绒。
    float s    = lit * bands;
    float i    = floor(s);
    float f    = s - i;
    float w    = clamp(fwidth(s) * 2.0, 0.08, 0.45);   // 过渡带宽度：近处可见柔化，远处抗闪
    float hard = (i + smoothstep(0.5 - w, 0.5 + w, f)) / bands;
    float toon = lerp(lit, hard, posterize);

    // SH 环境光防止死黑；阴影只做冷色调偏移，不额外压暗环境光
    // （环境光是阴影里唯一的照明来源，再乘暗值会得到死黑色块）
    // 2026-07-27 修正：投影处环境光按 shadowAttenuation 适度衰减（最多 38%）。
    // 低日角度下地面 NdotL 极小，直射项弱、环境光占主导，投下的长影会被
    // 环境光完全淹没（草原黄昏失去长影）；保留 62% 底光避免死黑。
    half3 ambient   = SampleSH(normalWS) * ambientBoost;
    ambient *= lerp(0.62, 1.0, mainLight.shadowAttenuation);
    // 批次4（07-28）：地形烘焙 AO 只压环境光——直射光不动，正午对比依然干净；
    // 低洼处"天光天生稀薄"是环境光现象。ao=1（默认）时无效果，树木/道具调用方不变。
    ambient *= ao;
    half3 shadowCol = lerp(shadowTint, half3(1, 1, 1), toon);
    half3 col = albedo * (ambient * lerp(shadowCol, half3(1,1,1), 0.5) + mainLight.color * toon * shadowCol);

    // 边缘光：乘主光颜色并偏向受光面，夜晚剪影保持暗部干净
    float3 viewDir = GetWorldSpaceNormalizeViewDir(positionWS);
    float rim = pow(1.0 - saturate(dot(normalWS, viewDir)), rimPower);
    col += rim * rimStrength * mainLight.color * (0.25 + 0.75 * toon);

    // 附加光源（闪电 LightningLight 等）：同样过色阶，避免瞬间破坏风格
#if defined(_ADDITIONAL_LIGHTS)
    uint lightCount = GetAdditionalLightsCount();
    for (uint li = 0u; li < lightCount; li++)
    {
        Light l = GetAdditionalLight(li, positionWS);
        float nl = saturate(dot(normalWS, l.direction));
        float a  = nl * l.distanceAttenuation * l.shadowAttenuation;
        float sa = a * bands;
        float ia = floor(sa);
        float fa = sa - ia;
        float wa = clamp(fwidth(sa) * 2.0, 0.08, 0.45);
        float hardA = (ia + smoothstep(0.5 - wa, 0.5 + wa, fa)) / bands;
        col += albedo * l.color * lerp(a, hardA, posterize * 0.5);
    }
#endif

    return col;
}

#endif
