# Demo 视觉对齐实施计划（地形效果修复 · 第二轮）

> 状态：**A 节（天空/雾）已落地**（2026-07-10，岩画方向升级版 —— 见 `Docs/SkySanRockArt.md`）；B 节 B1 已落地（demo2 轮）；其余待执行
> 参考图：`Assets/demo.jpg`；基线场景：`Assets/Scenes/PlantGrowthTemplate.unity`
> 方向决策：**草原化重译** — 采用 demo 的价值结构（苍白低饱和地面、暖光冷影、重雾、远山溶解），色相用旱季稻草黄/沙色，不照抄冬景冰蓝；**保留真实昼夜**，通过暖化日光渐变让全天带暖冷对比；巨石/树木实例化**推迟到下一轮**。

## 背景与根因

上一轮已落地 Glimmer 统一渲染体系（GlimmerToonCore 单一光照源 + 地形/道具/雨三组 shader、幂等 Setup 菜单、WorldAtmosphereBinder L2→L3 绑定），但画面与 demo.jpg 差距仍大。根因核查（截图 + 源码验证）：

1. **天空盒完全错误**：场景天空盒 `sky.mat` = 内置 Cubemap shader（fileID 103）+ 星云贴图 → 白天地形漂浮在黑色星空里。
2. **UpdateSkybox 静默失效（bug）**：`EmotionWeatherController.UpdateSkybox()` 写的是 Procedural 天空盒属性（`_SkyTint`/`_AtmosphereThickness`/`_Exposure`），cubemap 材质没有这些属性 → 天气/昼夜从未影响过天空。
3. **运行时光照抹掉金色时刻**：`LightManager.UpdateLighting`（LightManager.cs:86-95）每帧用 `SunlightPreset` 渐变覆写阳光颜色（正午=纯白）、太阳旋转、`RenderSettings.ambientLight` → 编辑态调好的暖光进 Play 就消失。
4. **地形太平、太绿、雾太远**：场景 `detailAmplitude=0`（区域内完全平坦）；调色板偏薄荷绿/橄榄绿（demo 是苍白低饱和）；`fogEnd=380` 远超地形对角线（160×scale1≈226）→ 远山永远不会溶进雾里。
5. **高度带脱钩隐患**：`_lowlandHeight` 等只在运行时 `Generate()` 推入材质，`scale≠1` 时 .mat 存值与几何脱钩。

## 改动清单（按执行顺序）

### 0. 检查点提交 + 清理

- ✅ checkpoint commit（整个视觉 overhaul 之前全部未提交）。
- 删除误复制的 `Assets/Shaders/StylizedWater 1.shader`（+.meta）。
- `ProjectSettings/EditorBuildSettings.asset`：把 `PlantGrowthTemplate.unity` 设为启用场景（当前只列了空壳 SampleScene）。

### A. 氛围基线（最大增量，先修好「画框」再调地面）

**A1 · 新建渐变天空** — CREATE `Assets/Shaders/GlimmerSkyGradient.shader` + `Assets/Materials/Glimmer/SkyGradient.mat`

- Background queue、Cull Off ZWrite Off、unlit。属性：
  - `_SkyTop` (0.34,0.38,0.44) 灰蓝天顶 / `_SkyHorizon` (0.66,0.62,0.55) **暖灰地平线（必须≈雾色）** / `_GroundCol` (0.45,0.41,0.35)
  - `_HorizonBlur` 0.35、`_SunTint` (1.0,0.72,0.42) 琥珀晕、`_SunGlow` 0.9、`_SunSize` 12、`_Exposure` 1.0
  - `_StarCube`（复用现有星云 cubemap，guid ef178774…）+ `_StarBlend` 0..1 —— 夜晚渐显星空，呼应北极星「稀树草原夜空」
- 片元：viewDir.y 三段垂直渐变 + `_MainLightPosition` 方向太阳辉光（skybox pass 拿不到则退化为控制器写入的 `_SunDir`）+ 夜晚 lerp 到星空 cubemap。
- 赋给 `RenderSettings.skybox`（sky.mat 留盘备份）。

**A2 · 重写 UpdateSkybox（修 bug 2）** — MODIFY `Assets/Script/EmotionWeatherController.cs`

- 替换死掉的 Procedural 属性写入，改驱动 SkyGradient；序列化字段换成 sunny/storm 两组（top/horizon/ground/sunGlow）+ 夜空 navy 两色。
- `badT = smoothedRainIntensity ⊕ dimness` 同雾公式；复用 UpdateRain 已算好的 `dayLight` 因子做昼夜压暗，`_StarBlend = 1 - dayLight`。
- **关键**：UpdateRain 最终 `fogCol` 存成员变量，`_SkyHorizon` 直接设为它 → 地平线与雾无缝，远山溶解。
- 单写者纪律不变：EmotionWeatherController 仍是 RenderSettings.fog* + 天空材质的唯一写者。

**A3 · 暖化日光渐变（保昼夜）** — MODIFY `Assets/ImportedAssets/PleebieJeebies/Scripts/LightPresets/SunlightPreset.asset` + 场景

- 白天 `DirectionalColour` 关键帧纯白 → 暖琥珀 (1.0,0.87,0.68)，晨昏更深的橙；`AmbientColour` 白天改偏冷 (0.55,0.62,0.72) → 全天暖光冷影。
- 场景 `LightManager.SunDirection` 10 → 215（光从右侧来，同 Preview Golden Hour）。强度 1.15 不动（LightManager 不写 intensity）。
- 若 E4 验证发现效果仍被时钟破坏，升级备选：LightManager 加 `lockSunAngle` 开关（只锁旋转、颜色照跑）。

**A4 · 雾距重调（匹配 160 单位地形）** — `EmotionWeatherController` 默认值 + `GlimmerVisualSetup.SetupWeatherDefaults()` + 场景 RenderSettings 三处同步：

- sunny 60/380 → **45/210**（远端≈地形边缘，远山可溶）；storm 18/130 → **16/95**
- `sunnyFogColor` (0.58,0.66,0.72) 冷青 → **(0.66,0.62,0.55) 暖灰**；storm → (0.20,0.22,0.26)
- `GlimmerVisualSetup.Run()` 增加幂等 `SetupSky()`（建/载 SkyGradient.mat、赋 skybox、填星空 cubemap）；`PreviewGoldenHour` 同步新雾值，保证预览=基线。

### B. 地形形体与配色

**B1 · 调色板草原化** — `Assets/Materials/Glimmer/Terrain_Glimmer.mat` + `GlimmerVisualSetup.SetupTerrain()`（两处同值）：

| 属性 | 现值 | 新值（旱季草原） |
|---|---|---|
| _SandColor | (0.72,0.64,0.46) | (0.80,0.73,0.58) 苍白骨沙 |
| _LowlandColor | (0.40,0.50,0.27) 绿 | (0.66,0.63,0.44) 干稻草 |
| _PlainsColor | (0.48,0.53,0.27) 绿 | (0.72,0.68,0.50) 苍白干草 |
| _HighlandColor | (0.56,0.53,0.31) | (0.70,0.63,0.47) 日晒褪色 |
| _PeakColor | (0.50,0.46,0.42) | (0.52,0.50,0.50) 冷板岩 |
| _CliffColor | (0.42,0.36,0.30) | (0.34,0.33,0.35) 深板岩 |
| _ShadowTint | (0.30,0.38,0.46) | (0.34,0.40,0.50) 冷蓝更明确 |

- 其余：`_AmbientBoost` 0.9→0.95、`_BandSoftness` 1.3→1.4、`_BandNoiseAmp` 0.8→0.7。原则：色带彼此靠近（近单色苍白），暖冷对比交给光照。

**B2 · 恢复起伏** — 场景 `TerrainGenerator.detailAmplitude` 0 → **0.4**。刻面法线会把起伏直接变成明暗刻面——上限 0.8，0.4 安全。

**B3 · 断崖收紧 + 关全局台地** — 场景 `useTerrace` 1 → **0**（全局台地在平地出现 Minecraft 阶梯，代码注释本身就说参考风格应关闭）；shader 侧收紧坡度断崖：`_CliffStart` 0.45 → **0.42**、`_CliffSharp` 0.18 → **0.16**。仅当截图验证崖壁仍太平滑时，才在 `TerrainGenerator.GetHeight` 加**边界掩码台地**（按 nx 距 b1/b2 接近度 lerp 到 Terrace(h)，只阶梯化崖坡不动平地）。

**B4 · 高度带脱钩修复（bug 5）** — `TerrainGenerator.cs`：三行 `SetFloat("_lowlandHeight"…)` 抽成 `PushBandHeights()`，`OnValidate()`/`OnEnable()` 也调用（null 保护，不在 OnValidate 跑整个 Generate）。

### D. 水面（小改，独立）

`Assets/Materials/StylizedWater.mat`（shader 默认值同步）。草原方向：苍白低饱和青而非冰蓝：

- `_ShallowColor` (0.30,0.62,0.62,0.55) → **(0.52,0.66,0.64,0.60)** 苍白灰青
- `_DeepColor` (0.05,0.20,0.30,0.92) → **(0.16,0.30,0.34,0.90)** 低饱和暗青
- `_HighlightColor` (0.676,0.801,0.843) → **(0.84,0.88,0.86)** 近白冷闪
- shader 已乘场景光（夜里自动沉暗），若夜晚发灰把地板 `+0.06` 降到 `+0.04`。

### E. 验证闭环

1. `Tools/Glimmer/Setup Visual Style`（含新 SetupSky）→ read_console 查错。
2. 编辑态基线：`Tools/Glimmer/Preview Golden Hour` → ClaudeViewCapture 截 overview + maincam。
3. 对照 demo.jpg 五检：暖灰渐变天空非黑空 / 远山溶进地平线 / 地面苍白稻草非薄荷绿 / 暖亮面冷影面 / 水苍白低饱和。只迭代数据直到静帧成立。
4. **运行时真测**：`Tools/Glimmer/Playtest Clear Capture`（内置 Simulate 快进，规避失焦不走帧）→ 验证 A3 暖日光在昼夜时钟下存活；若正午仍白平淡 → 启用 lockSunAngle 备选。
5. 暴雨态：`Playtest Rain Capture` → 天空同步压暗（UpdateSkybox 已修）、雾收拢 16/95、天-雾-地平线一体。
6. PostFX 交叉检查：`GlimmerPostFX.asset` ColorAdjustments saturation=+8 可能顶撞低饱和调色板 → 视截图降到 0 或 -5；确认太阳辉光不撞 Bloom threshold 1.6 爆白。
7. 分模块提交：`feat(sky)` / `feat(terrain)` / `feat(water)`。

## 风险

- **[高] 昼夜时钟 vs 金色时刻**：A3 改渐变资产是纪律内最小解；E4 实测后才决定是否加 lockSunAngle。
- **[中] skybox pass 拿不到 `_MainLightPosition`**：退化方案 `_SunDir` 由控制器写。
- **[中] PostFX +8 饱和度顶撞苍白调色板**：E6 校准。
- **[低] 边界掩码台地引入等高线环纹**：优先 shader 坡度断崖（零代码），几何台地仅按需。

## 下一轮（明确推迟）

- 程序化低模巨石：RockGenerator（icosphere+噪声位移+非共享顶点刻面）+ RockPlacement 散布（复用 TreePlacement 的拒绝采样模式，崖底+前景带）。demo 前景体量感主要靠这个。
- 树木实例化落地：`TreePlacement.Populate()` 结果 → Instantiate（三区 prefab 池：水边枯树/平原疏树/崖上针叶），保留 showAt 情绪门控钩子。
- 草簇（十字面片 + Glimmer/Toon alpha-clip + 摆动）。
