# 天空基调：桑人岩画（San Rock Art Sky）

> 状态：**已定稿并实施**（2026-07-10）
> 决策人：用户（三问三答：复杂度=岩画质感档 / 夜空=程序化撒灰 / 白天颗粒=克制档）
> 实现：`Assets/Shaders/GlimmerSkyGradient.shader` + `EmotionWeatherController.UpdateSkybox` + `GlimmerVisualSetup.SetupSky`

## 视觉基调

北星像（非洲稀树草原之夜）的天空不是摄影天空，而是**画在岩壁上的天空**——
借桑人（San）岩画的材料语言：赭石、木炭、骨白、蛋彩，画面是矿物颜料沁进
岩面的效果。天空 = 世界这幅「活岩画」的底壁。

三条材料规则（所有天空视觉判断的依据）：

1. **颜料而非光学** —— 太阳是一饼画上去的赭石颜料（毛边圆 + 分段晕环，
   像一圈圈 wash），不做镜头光晕/大气散射。
2. **岩面而非真空** —— 天空有极轻的岩壁颗粒和矿物斑驳（白天 2-3% 亮度扰动，
   功能是消色带 + 承接岩画气质；不能明显到偏离 demo.jpg 的干净渐变）。
3. **传说而非星表** —— 夜空是 ǀXam 传说「少女掷灰成河」的银河：
   程序化撒灰带（fbm 撕出断续灰块，一侧稠向银心）+ 骨白/赭红双色星点
   （赭红余烬 ~14%），不用照片感星云 cubemap（星云贴图弃用，留盘不删）。

## 复杂度边界（本轮明确不做）

- ❌ 月亮/月相
- ❌ 事件星辰（永久世界事件点亮不灭星 —— 需要 L2→L3 新绑定通路，留给未来轮）
- ❌ 云（雾+雨已承担坏天气叙事）
- ✅ 唯一的「活」元素：整个星穹绕斜天轴以 0.06°/s 缓慢旋转 ——
  世界自己的生命，与用户输入无关（原则 1）。

## 数据流（单写者纪律）

```
WorldAtmosphereBinder (L3绑定层，只读世界状态)
    └─> EmotionWeatherController.rainIntensity / dimness
            └─> UpdateRain()    → 雾距/雾色（含昼夜压暗）→ _fogColThisFrame
            └─> UpdateSkybox()  → SkyGradient.mat 全部运行时属性（唯一写者）
                  _SkyTop/_GroundCol : sunny↔storm 按 badT 混合 × nightMul
                  _SkyHorizon        : = 本帧雾色 → 远山溶进天空（demo 核心）
                  _SunDir            : 每帧从真实太阳读方向（昼夜自动走）
                  _SunGlow/_SunDiscStrength : 坏天气收晕、日盘隐入云层
                  _StarBlend         : (1-dayLight) × (1-badT×stormStarHide)
LightManager: 只管平行光颜色/旋转 + ambientLight，不碰天空盒
```

- badT = `clamp01(smoothedRainIntensity + dimness×dimnessFogWeight)`，与雾公式同源
  —— 天和雾必须一起变脏，否则天地脱节。
- 星空被情绪调制的路径：坏天气(E_env→雨/晦明)→badT→星减；这是「天气遮蔽」
  而非「情绪直连」，符合层边界（Layer 3 只读 E_env 的间接产物）。

## 色板（晴天基线 → 暴雨端）

| 属性 | 晴天 | 暴雨 | 备注 |
|---|---|---|---|
| _SkyTop | (0.34,0.38,0.44) 灰蓝 | (0.22,0.24,0.28) | × nightMul（夜地板 0.16）|
| _SkyHorizon | = 雾色 (0.66,0.62,0.55) 暖灰 | = 雾色 (0.20,0.22,0.26) | 控制器逐帧写 |
| _GroundCol | (0.45,0.41,0.35) | (0.24,0.23,0.22) | 地平线以下 |
| _SunTint | (1.0,0.72,0.42) 琥珀赭 | 同 | 晕环强度 0.9→0.15 |
| _StarColorA | (0.92,0.90,0.84) 骨白 | — | 主星 86% |
| _StarColorB | (0.85,0.42,0.28) 赭红余烬 | — | 主星 14%（step 0.86）|
| _AshColor | (0.72,0.70,0.66) 灰烬 | — | 银河带，强度 0.35 |

雾距（A4 落地值）：sunny 45/210、storm 16/95；`sunnyFogColor` 暖灰 (0.66,0.62,0.55)。

## 关键实现注记

- **Hash 选型**：方向域高频采样必须用 Hoskins 乘加 hash（`Hash13/Hash33`），
  sin-hash 会出平台相关条纹。
- **星点单格采样**：特征点缩在格心 0.2~0.8、半径≤0.15 格 → 星不会跨格被裁，
  省掉 27 邻格查询。
- **风化遮罩**：颗粒/斑驳在 |y|<0.015 处淡出 → 地平线带保持纯雾色，
  远山溶解不被颗粒打断。
- **编辑态=运行时**：`GlimmerVisualSetup.PreviewSky` 与 `UpdateSkybox` 同一套
  映射；进 Play 由控制器接管。SetupSky 幂等，sky.mat（星云版）留盘备份。
