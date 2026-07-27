# 日常氛围绑定 (Ambient Atmosphere Binding) — 垂直切片 Phase 1

打通"日常氛围"通路：天气、天光、植被的情绪响应，让世界在没有日记输入时也持续呼吸
（CLAUDE.md 原则一：世界有自己的生命）。Layer 2 世界状态与 Layer 3 视觉组件之间此前
**没有任何一根线接通**——本切片只接线，不新增视觉功能。

## 背景（接线前的断点）

- `EmotionWeatherController.rainIntensity/windIntensity/thunderIntensity` 全项目无代码写入，只有 Inspector 默认值。
- `LightManager` 用内部加速假时钟（`TimeOfDay += deltaTime*multiplier`），与绑定真实墙钟的 `NaturalRhythmSystem` 互不知情。
- `EcosystemManager.globalValence/globalArousal` 是裸露的 Inspector 滑条，没人从 E_env 写它。
- `NaturalRhythmSystem.Tick()` 只在 Awake 和日历推进时调用，会话内空闲时冻结，光照不随真实时间流逝。

## 架构

```
Layer 2（只读入口）                    Layer 3（绑定层）              视觉组件
GetWorldState()   ─┐
GetRhythmState()  ─┼─→ WorldAtmosphereBinder ─→ EmotionWeatherController（雨/风/雷/雾）
WorldSave.currentEEnv ─┘   Update: 读+算目标+平滑    LightManager（太阳角度/环境光）
                           LateUpdate: 写入          EcosystemManager（植被 V/A → 风/植物/草）
```

绑定层纪律：只读世界状态；不 `using GlimmerDiary.Core`（返回值用 `var` 承接）；
不调用 `OnJournalSubmitted`/`InjectEmotion`/`SimulatePass`/`WorldTick`；
自带展示层平滑（指数趋近，独立于 E_env 自身的惯性 α），Layer 2 快照按天跳变落地时画面不"跳"。

## 映射表

| 视觉目标 | 世界状态源 | 映射 | 备注 |
|---|---|---|---|
| `rainIntensity` | `State.Rainfall` [0,1] | `Lerp(0.5, −1, Rainfall)`（2026-07-27：原 `1 − 2·Rainfall` 要到 Rainfall>0.5 才落第一滴雨；现 ≈1/3 起有细雨） | 目标字段语义 [-1雨, +1晴]，符号相反需翻转 |
| `windIntensity` | `State.WindSpeed` [0,1] | 直接映射 | 范围一致 |
| `thunderIntensity` | `WindSpeed × Rainfall` | 双阈值门控（均 >0.45 才爬升，2026-07-27 自 0.6 下调） | 雷暴要稀有，不能常态化；阈值是审美判断的保守起点，Inspector 可调 |
| `dimness`（新字段） | `State.FogDensity` | 直接映射 | 信号 3 晦明此前无消费者，本切片接上 |
| `LightManager.SetTimePercent` | `rhythm.dayProgress` | 直接传入，不平滑 | dayProgress 本身连续微变；平滑会在午夜 1→0 回绕处出错 |
| `EcosystemManager.SetEmotionState` | `eEnv.V`, `eEnv.A` | 平滑后传入 | 内部已级联 WindSystem/PlantController/GrassSystem，链路本就通 |
| `WaterGenerator.SetDisplayLevel01`（Water transform Y） | `Registry.GetLocation("riverbank").waterLevel` [0,1] | 两段线性（0→dryY −2.2，0.3→kneeY 0.6，1→fullY 1.3，高度场单位 ×terrain.scale） | 平滑 0.05（τ≈20s），比天气慢一个量级；loc 缺失静默跳过；dryY 低于河床 −2.06 → 允许完全断流；锚定 waterLevel≈0.69→Y≈1.0（= 旧烘焙视觉，开机无跳变）；深度渐变 shader 自动呈现变浅/收窄，shader 零改动（2026-07-18） |

## 数据结构变更（提案 20260702-01，COMPATIBLE）

| 文件 | 字段 | 类型 | 用途 |
|---|---|---|---|
| `Data/EmotionData.cs` `NaturalRhythmState` | `dayProgress` | float 0~1 | 一天中的真实时刻（0=午夜），供渲染层转太阳角度 |

**为什么需要**：`lightIntensity` 是正弦算完的亮度值，上午/下午同亮度无法反推时刻。
考虑过让 LightManager 自己读 `DateTime.Now`——被否决：那样"现在几点"就有两个独立计算点，
违反单一写者纪律。世界的时间只有一个来源。

## 所有权

| 字段 | 单一写者 | 读取者 | 序列化 |
|---|---|---|---|
| `NaturalRhythmState.dayProgress` | `NaturalRhythmSystem.Tick()`（复用内部 hour：`hour/24`） | `WorldAtmosphereBinder`（只读） | 否（运行时快照，不入 WorldSaveData，零迁移） |
| `EmotionWeatherController.rainIntensity/windIntensity/thunderIntensity/dimness` | `WorldAtmosphereBinder.LateUpdate` | 组件自身 | 否 |
| `EcosystemManager.globalValence/globalArousal` | `WorldAtmosphereBinder`（经 `SetEmotionState`） | 组件内级联 | 否 |
| `LightManager.TimeOfDay` | `driveExternally=true` 时：binder 经 `SetTimePercent`；false 时：内部时钟 | 组件自身 | 否 |
| `Water.transform.localPosition.y` | `WorldAtmosphereBinder.LateUpdate`（经 `WaterGenerator.SetDisplayLevel01`） | `ZoneMap.WaterY` 实时读 renderer bounds——动物/植树离水判定自动跟随水位 | 否 |

## 改动清单

| 文件 | 改动 | 兼容性 |
|---|---|---|
| `GlimmerDiary/Scripts/Data/EmotionData.cs` | `NaturalRhythmState` + `dayProgress` | 新增字段，不序列化，零风险 |
| `GlimmerDiary/Scripts/Core/NaturalRhythmSystem.cs` | `DayProgress` 属性 + Tick 内赋值 | 纯新增 |
| `GlimmerDiary/Scripts/WorldManager.cs` | 空闲心跳：`Update()` 每 ~30 真实秒（`[SerializeField]` 可调）调 `NaturalRhythm.Tick(gameTime)` 并重译无状态信号 1/2/3/7（`Environment.ConsumeSignals`，信号 7 苍穹含墙钟光照因子，不重译会冻结），不碰 worldEvents/animals/plants/emotionHistory，**不是** SimulatePass | 纯调度新增，不改现有调度顺序 |
| `ImportedAssets/PleebieJeebies/Scripts/LightManager.cs` | `driveExternally` 开关 + `SetTimePercent(t01)` | 默认 false，旧场景行为不变 |
| `Script/EmotionWeatherController.cs` | `dimness` + `dimnessFogWeight` 字段；雾公式 `Min(0.01, 0.015·rain + weight·dimness)` | dimness=0 时退化回原公式（加法不减法） |
| `Script/WorldAtmosphereBinder.cs` | **新建**，Layer 3 绑定层 | — |
| 场景 `PlantGrowthTemplate` | 根级空物体 `WorldAtmosphereBinder`，引用 WeatherManager / DayNightAndLightController / EcosystemManager；顺手补上 `LightManager.DirectionalLight` 引用（原为空，太阳旋转/颜色静默跳过，只有环境光在动） | 无 binder 的场景不受影响 |

## 落地进度

- **2026-07-02**：全部落地。编译零报错；层边界 grep 自查零违规（顺手修正
  `layer-boundary-check.md` 中 L3 路径 `Assets/PleebieJeebies/` → `Assets/ImportedAssets/PleebieJeebies/`，
  原路径与实际不符会漏检）。
- **Play Mode 验证**（场景一 FloodAndVoleRelocation，连续 5 天 V=-0.8）：
  - E_env V=-0.50 → Rain=0.44 → `rainIntensity` 平滑爬向 0.12（=1−2×0.44）✅
  - Wind=0.32 → `windIntensity`=0.318 ✅；Fog=0.23 → `dimness`=0.226 ✅
  - 植被 `globalValence`=-0.503 / `globalArousal`=0.552，与 E_env 一致 ✅
  - `LightManager.TimeOfDay` 与真实墙钟吻合（22:53 ≈ 1373 分钟），`driveExternally`=true ✅
  - 空闲心跳：静置后 `TimeOfDay`/`dayProgress` 随墙钟前进 ✅（见下）
  - 雷暴门控：Wind=0.32 < 0.6，thunder 保持 0 ✅（稀有性生效）
- **2026-07-18**：新增水面升降通路——`loc.waterLevel` 此前只活在 JSON（宪法⑤：无痕不成环），
  现由 binder 读 `riverbank.waterLevel` → 平滑（0.05）→ `WaterGenerator.SetDisplayLevel01`
  升降 Water transform（不重建网格）。两段线性映射，允许旱季完全断流；
  StylizedWater 的深度渐变自动呈现变浅/收窄，shader 零改动。
  `WaterGenerator` 附 ContextMenu 干/满预览（仅展示层，不碰世界状态）。

## 明确留到下一轮

- QuietConvergence 视觉/痕迹表达（等氛围基调定下来）
- 季节的视觉承接；StarVisibility / CreatureAbundance / DecayLevel 可视化（无现成视觉承接端）
- 动物 3D 呈现（目前纯数据模拟）
- `pendingChronicles` 最小展示面
- `EcosystemManager` Inspector 滑条收口（绑定生效后滑条下一帧即被覆盖，暂不改公开 API）

## 关键不变式

- 绑定层永远只读世界状态；视觉参数绑定 E_env（经 State/信号），不绑 E_current 原始情绪。
- 展示层平滑是渲染关切，独立于情绪惯性 α——两层平滑各管各的。
- `dayProgress` 单一写者是 `NaturalRhythmSystem.Tick()`；渲染层不得自读 `DateTime.Now` 另起时钟。
- 空闲心跳只重算节律快照 + 重译无状态信号（`ConsumeSignals`，零积分），永不推进日历、永不触发模拟、永不碰有状态积分（Soil/Decay 等只在 SimulatePass 走 `UpdateFromEEnv`）。
