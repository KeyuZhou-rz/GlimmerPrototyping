#ifndef GLIMMER_TOON_CORE_INCLUDED
#define GLIMMER_TOON_CORE_INCLUDED

// Glimmer 统一风格光照核心：所有实体着色器（地形/树木/道具）共用，
// 保证色阶数、阴影冷调、边缘光行为完全一致 —— 风格统一的单一事实来源。
// 依赖：调用方已 include URP Core.hlsl + Lighting.hlsl。

half3 GlimmerToonLight(float3 normalWS, float3 positionWS, half3 albedo,
                       half bands, half posterize, half ambientBoost,
                       half3 shadowTint, half rimStrength, half rimPower)
{
    float4 shadowCoord = TransformWorldToShadowCoord(positionWS);
    Light mainLight = GetMainLight(shadowCoord);

    float NdotL = saturate(dot(normalWS, mainLight.direction));
    float lit   = NdotL * mainLight.shadowAttenuation;

    // 色阶量化（toon）：_Posterize 控制硬/软过渡
    float hard = floor(lit * bands + 0.5) / bands;
    float toon = lerp(lit, hard, posterize);

    // SH 环境光防止死黑；阴影只做冷色调偏移，不额外压暗环境光
    // （环境光是阴影里唯一的照明来源，再乘暗值会得到死黑色块）
    half3 ambient   = SampleSH(normalWS) * ambientBoost;
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
        float hardA = floor(a * bands + 0.5) / bands;
        col += albedo * l.color * lerp(a, hardA, posterize * 0.5);
    }
#endif

    return col;
}

#endif
