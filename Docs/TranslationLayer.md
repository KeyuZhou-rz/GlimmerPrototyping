# 翻译层 (Translation Layer) — 7 信号表 v1 FROZEN

翻译层把 `EmotionVector(V, A, T, S, C)` 翻译成驱动世界的"信号"。信号是情绪→世界的唯一中介；
所有世界字段最终由信号驱动。信号分两类：

- **无状态快信号**：纯函数 of (E_env, rhythm)，每 tick 重算。本类信号落在 `TranslationLayer.Translate`。
- **有状态积分**：带惯性，逐 tick 缓慢逼近（α 小），体现"情绪有惯性"原则。

> v1 表已 FROZEN。落地按"先立单一写者、再接信号"分格推进（见下方所有权表与进度）。

## 7 信号表

| # | 中文 | 英文字段 | 源情绪维 | 类型 | 公式 / 形状 | 目标消费者 | 状态 |
|---|---|---|---|---|---|---|---|
| 1 | 降水 | `Wetness` | V | 无状态 | 曲线 V→[0,1]（-1=大雨, 0/1=无雨） | `State.Rainfall`（→ PropagateEnvironment 水/湿） | ✅ Step 2 |
| 2 | 躁动 | `Agitation` | A | 无状态 | 曲线 A→[0,1]（0=无风, 1=强风） | `State.WindSpeed` | ✅ Step 2 |
| 3 | 晦明 | `Dimness` | C | 无状态 | 曲线 C→[0,1]（0=浓雾, 1=无雾） | `State.FogDensity` | ✅ Step 2 |
| 4 | 繁盛 | `Flourishing` | V | 有状态 | V-integral `f += α·((V+1)/2 − f)`（同 baobab vitality） | `loc.vegetationDensity` 气候基线 / `tree.vitality` | ⬜ SEAM（待 tree 格） |
| 5 | 衰败 | `Decay` | V | 有状态 | `V<-0.3 → +0.01 else -0.005`（可回 0，无棘轮底） | `State.DecayLevel` / `loc.vegetationDensity` | ⬜ SEAM（待 tree 格；现留 UpdateFromEEnv） |
| 6 | 聚拢 | `Convergence` | S | 有状态 | `S·0.5+0.3`，α=0.1 | `State.CreatureAbundance` / 动物 abundance | ⬜ SEAM（待 animal 格；现留 UpdateFromEEnv） |
| 7 | 苍穹 | `Firmament` | T | 无状态 | 近直接 T-read `T·(1-Wetness)·(1-light)` | `State.StarVisibility` | ✅ Step 2 |

## 世界字段所有权表（Track A — 单写者确权）

> 状态列只反映**所有权**（Track A：单写者核实 ✅ / 设计多写者 ⚠）。信号 4/5/6 迁入翻译层是另一条轨道（Track B，见"信号来源"列与下方进度），**不影响所有权**。

| 世界字段 | 单一写者 | 信号来源 | 所有权状态 |
|---|---|---|---|
| `State.Rainfall / WindSpeed / FogDensity / StarVisibility` | `WorldEnvironmentSystem.UpdateFromEEnv` | 信号 1/2/3/7（翻译层产出） | ✅ Step 2 |
| `loc.waterLevel` | `PropagateEnvironmentToLocations` | `State.Rainfall`（= 信号 1 Wetness） | ✅ 本就干净 |
| `loc.soilMoisture` | `PropagateEnvironmentToLocations` | `State.Rainfall`（= 信号 1 Wetness） | ✅ Step 1b |
| `loc.vegetationDensity` | `VegetationSystem.Tick` | 虫害（weaver 缺席）+ 气候基线[SEAM 信号 4/5] | ✅ Step 1 |
| `State.DecayLevel` | `WorldEnvironmentSystem.UpdateFromEEnv` | 信号 5（待迁 = Track B） | ✅ 单写者核实 |
| `State.CreatureAbundance` | `WorldEnvironmentSystem.UpdateFromEEnv` | 信号 6（待迁 = Track B） | ✅ 单写者核实（非 drive/detector） |
| `State.SoilMoisture / 全局 VegetationDensity` | `WorldEnvironmentSystem.UpdateFromEEnv` | `State.Rainfall` / `DecayLevel` | ✅ 单写者核实（全局遗留） |
| `tree.vitality / floweringReadiness` | `AnimalDriveSystem.TickTree` | 信号 4 繁盛（待迁 = Track B） | ✅ 单写者核实（internalState，drive 独占；`ChangePlantState` 不触及） |
| `tree.isFlowering / lastFlowerDate` | `AnimalDriveSystem.TickTree` | 季节 + 繁盛 | ✅ 单写者核实（TickTree 经 `ChangePlantState`；grep 确认无活跃规则/关系资产写此字段） |
| 动物 `behavior` / `internalState` | `AnimalDriveSystem` | — | ✅ 单写者核实（drive 独占 invariant；`ChangeAnimalState` 不触及） |
| `QuietConvergence` 世界事件 | `EmergentMomentDetector` | 信号 6 聚拢（待迁 = Track B） | ✅ 单写者核实（detector 独占，append-only） |
| 动物 `isPresent` / `location` | `AnimalDriveSystem`（连续）+ `NarrativeRuleEngine`/`EntityRelationSystem`（离散事件） | — | ⚠ 设计多写者（连续 drive + 离散规则/关系，统一经 `ChangeAnimalState`；drive 侧走 Snap 协议读） |

## 落地进度

- **Step 1** (2026-06-30)：`VegetationSystem` 立为 `loc.vegetationDensity` 单写者；虫害从 `TickTree` 迁出（1-tick 滞后）；气候基线留 SEAM。见记忆 `translation-step1-vegetation-owner`。
- **Step 1b** (2026-07-01)：`PropagateEnvironmentToLocations` 扩写 `loc.soilMoisture`（分区 soakRate）。
- **Step 2** (2026-07-01)：新建 `TranslationLayer`，落地无状态信号 1/2/3/7；`UpdateFromEEnv` 退化为消费者；苍穹由 V 驱动改 T-read。有状态 4/5/6 暂留原处。见记忆 `translation-step2-env-weather`。
- **所有权表核实** (2026-07-02)：核实 cells 5/6 单写者不变式（见上表 ✅/⚠）。tree `vitality`/`floweringReadiness`（internalState，drive 独占）、`isFlowering`/`lastFlowerDate`（TickTree，grep 确认无活跃规则/关系资产写）均 ✅；animal `behavior`/`internalState`（drive 独占）、`QuietConvergence`（detector 独占）、`State.CreatureAbundance`（UpdateFromEEnv）均 ✅；`isPresent`/`location` 为设计多写者（drive 连续 + 规则/关系离散），已文档化。**Track A（所有权表）目标达成**：每个世界字段单写者或文档化的设计多写者。见记忆 `translation-ownership-table-track`。
- **待办（Track B，可选、更深轨道）**：信号 4/5/6 迁入 `TranslationLayer`（tree 格 → `tree.vitality` / `loc.vegetationDensity` 气候基线；animal 格 → `CreatureAbundance`）。不影响所有权表。

## 关键不变式

- 7 信号本身只有**一个 owner**：翻译层（`TranslationLayer`）。
- 世界字段各有**单一写者**或**文档化的设计多写者**（上表 ⚠ 行 = 事件驱动，连续系统 + 离散规则/关系）；消费者只读。
- 有状态信号遵守"情绪有惯性"：α 小（0.05~0.15），不直接由原始情绪赋值。
- 无状态快信号可被有状态/其他信号门控（如信号 7 苍穹被信号 1 Wetness 雨门控、被 lightIntensity 光门控）。
- 永不暴露世界重置 / 时间旅行（即使翻译层重构也不破坏永久世界状态）。
