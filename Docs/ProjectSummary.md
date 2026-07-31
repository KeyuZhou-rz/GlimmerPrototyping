# Glimmer — 项目总览与进度

> 最后更新：2026-07-20
> 分支：`feat/ph0-ph1`

---

## 一、项目是什么

**Glimmer** 是一个以非洲稀树草原为意象的**情感响应式世界**。玩家通过书写日记与世界互动，世界以自己的节奏缓慢回应——不是即时反馈，而是像天气一样渗透进每一片草叶。

**核心意象**：非洲草原夜景，猴面包树，隐匿的动物，无垠天空。

**参照基准**：Mountain——无留存机制、无每日奖励、无通知施压、无进度条、无社交功能。驱动力只有好奇心与情感投射。

### 三条宪法

1. **世界有自己的生命**——它独立于用户输入运行，不是日记的即时反应器。
2. **情感有惯性**——E_env 以 α≈0.1–0.3 缓慢趋近 E_current，视觉参数永远滞后于情绪输入。
3. **不可逆变化有重量**——永久世界事件不可撤销，不存在 undo / 时间旅行。
4. **机制必须互联**——孤立机制不上线；新规则必须说明它读谁的字段、谁对它的输出做出反应。
5. **无痕不成环**——因果链的每一步必须在场景中留下可见证据；纯 JSON 里的模拟深度 = 零深度。

---

## 二、架构

```
┌─────────────────┐     EmotionVector      ┌──────────────────┐     只读世界状态     ┌──────────────────┐
│  Layer 1         │     (V, A, T, S, C)    │  Layer 2          │                     │  Layer 3          │
│  SentimentEngine │ ─────────────────────→ │  WorldSimulator   │ ──────────────────→ │  Visual / Audio   │
│                  │                         │                   │                     │                   │
│  纯文本分析       │                         │  翻译层→信号→世界  │                     │  只读 E_env        │
│  无 Unity 依赖    │                         │  时钟/事件/实体    │                     │  自平滑绑定        │
│  输出归一化浮点    │                         │  永久变更不可逆    │                     │  不写世界状态       │
└─────────────────┘                         └──────────────────┘                     └──────────────────┘
```

**层间纪律**：
- L1 不碰 Unity API、不读世界状态——纯分析。
- L2 接收 EmotionVector 作为参数，不主动拉取；所有永久变更写入 PersistentWorldState。
- L3 只读世界状态，绑定 E_env（非 E_current），自带展示层平滑，不回写。

---

## 三、世界内容

### 3.1 情绪向量

| 维度 | 代号 | 语义 | 当前消费现状 |
|---|---|---|---|
| 效价 Valence | V | 正面/负面 | → 雨/衰败/树活力/候鸟（主力维度） |
| 唤醒 Arousal | A | 平静/激动 | → 风/狐狸/断枝规则 |
| 时间性 Temporality | T | 紧迫/停滞 | → 星空信号 + 规则引擎（读不到 T 的 bug 已修，2026-07-20） |
| 社会性 Sociality | S | 聚合/孤立 | → 仅 CreatureAbundance（死端，暂不激活） |
| 确定性 Certainty | C | 确定/不确定 | → 雾/鹿鼠焦虑/注入速率 α |

E_env 注入公式：α = Lerp(0.1, 0.3, C)——越确定的世界响应越快。每日向零基线回落 α=0.05。

### 3.2 动物（5 只）

| 物种 | 位置 | 内部状态 | 行为驱动 |
|---|---|---|---|
| 田鼠 vole | 低洼地 | shelterSecurity / foodStock / expansionPressure | Expand / Forage / Burrow |
| 狐狸 fox | 东侧高地 | hunger / safety / territoryStability | Foraging / Patrol / Avoidance / Rest |
| 鹿鼠 deer_mouse | 东侧高地 | anxiety（+activityRange 0.8） | Retreat / Explore / Routine |
| 候鸟 migratory_bird | 不在场 | migrationUrge / settlementComfort | Away / Depart / EarlyDepart / Settle |
| 织巢鸟 weaver_bird | 草原中央 | 无向量，纯事件驱动 | Nest / Away |

**动物间因果链示例**：织巢鸟离巢 → 鹿鼠焦虑↑ → activityRange↓ → center 腾空 → 田鼠扩张压力↑ → 进占 center → 进狐狸领地 → 狐狸领地稳定度↓ → Patrol 追击。

### 3.3 植物（3 株）

| 植物 | 位置 | 状态 |
|---|---|---|
| 猴面包树 baobab_main | 中央 | 唯一有内部模拟的植物（vitality 由 E_env.V 积分驱动，4 根主枝各有 integrity） |
| 蒲公英 dandelion | 河岸 | 惰性——初始化后无运行时写者 |
| 石边草 grass_stone | 石头区 | 惰性——同上 |

### 3.4 地点（5 处 + 拓扑）

```
riverbank ── lowland ── center ── highland_east
                         │
                    stone_area
```

每个地点拥有 waterLevel / soilMoisture / vegetationDensity / permanentChanges（append-only）。每日自然消退：水位 −0.03、湿度 −0.02。

### 3.5 时间系统

**一钟两粒度**：季节/年进度由世界日历驱动（30 天/月）；光照/dayProgress 跟随真实墙钟。缺席时自动 catch-up（最多重模拟 90 天）。

### 3.6 痕迹系统

玩家永远不直接看到动物——只看到它们的痕迹。痕迹是"无痕不成环"原则的原型实现：

| 痕迹 | 触发 | 寿命 |
|---|---|---|
| 土堆 | 田鼠扩张/洪水搬家 | 永久（上限 6 个） |
| 塌洞 | 永久地貌记录 | 永不移除 |
| 脚印串 | 位置迁移 | 8 游戏日 |
| 半程脚印 | 鹿鼠 activityRange<0.5 | 条件消失即撤 |
| 羽毛 | 候鸟/织巢鸟离场 | 6 游戏日 |
| 领地标记 | 狐狸巡逻 | 14 游戏日 |
| 压草痕 | QuietConvergence 涌现事件 | 5 游戏日 |

**痕迹寿命已接天气（链1+风，2026-07-20）**：`effectiveAge = age × (1 + Rainfall×0.5 + WindSpeed×0.3)`——雨天风天痕迹老得更快，不改最大寿命。新鲜痕迹（有效年龄 ≤1.5 日）头顶有占位标记，点击痕迹相机推近（B 方案）。

---

## 四、翻译层：情绪→信号→世界

翻译层是 L1 与 L2 之间的唯一中介。7 个信号把 5 维情绪向量转化为驱动世界的物理量：

| # | 信号 | 源维度 | 类型 | 目标 | 状态 |
|---|---|---|---|---|---|
| 1 | 降水 Wetness | V | 无状态 | Rainfall | ✅ 已迁入翻译层 |
| 2 | 躁动 Agitation | A | 无状态 | WindSpeed | ✅ 已迁入翻译层 |
| 3 | 晦明 Dimness | C | 无状态 | FogDensity | ✅ 已迁入翻译层 |
| 4 | 繁盛 Flourishing | V | 有状态积分 | vegetationDensity 气候基线 / tree.vitality | ⬜ SEAM 待接 |
| 5 | 衰败 Decay | V | 有状态积分 | DecayLevel / vegetationDensity | ⬜ SEAM 待接 |
| 6 | 聚拢 Convergence | S | 有状态积分 | CreatureAbundance | ⬜ SEAM 待接 |
| 7 | 苍穹 Firmament | T | 无状态 | StarVisibility | ✅ 已迁入翻译层 |

**世界字段所有权统一轨道**：每个世界字段只有唯一写者。**6/6 已完成确权**（2026-07-20 核实：tree.vitality 唯一写者 AnimalDriveSystem.TickTree；动物内部状态唯一写者 AnimalDriveSystem）。

---

## 五、模拟管线

每次 SimulatePass 按固定顺序执行：

```
翻译层(M4) → 环境系统(M5) → 水位传播(M6) → 植被系统(M7)
→ 动物驱力(M8) → 涌现检测(M9) → 行为叙事(M10) → 规则引擎(M11) → 关系系统(M12-空转)
```

**5 条规则引擎规则**：

| 规则 | 触发 | 效果 | 冷却 |
|---|---|---|---|
| 断枝 | A>0.7 ∧ V<−0.4 | 永久断枝 → 织巢鸟离巢 | 60 天 |
| 田鼠洪水搬家 | lowland 水位>0.65 | 田鼠→高地 | 14 天 |
| 候鸟抵达 | 9-11月 ∧ V>0.2 | 候鸟→河岸 | 90 天 |
| 狐狸改道 | A>0.6 | 狐狸转向 | 10 天 |
| 田鼠弃洞 | 田鼠在高地 ∧ lowland 水位>0.5 | 永久地貌：塌洞 | 30 天 |

---

## 六、视觉系统

### 6.1 渲染体系

- **GlimmerToonCore**：统一光照源，所有 Glimmer shader 共用。
- **三组 shader**：GlimmerTerrain（地形）、GlimmerToon（道具/树）、RainStreak（雨丝）。
- **L-System 植被已弃**：转向 Ecosystem_nonL（prefab 实例化树），showAt 门控待实现。

### 6.2 天空：桑人岩画风格

天空 = 画在岩壁上的天空，不是摄影天空。三条材料规则：

1. **雕刻而非光学**——太阳是凿进岩壁的日轮图腾（刻环、断续弧、射线），不做镜头光晕/大气散射。
2. **岩面而非真空**——极轻矿物斑驳消色带，颜料盖在风化岩面上。
3. **图腾填彩**——四相色板（晨赭/午尘蓝/暮铜/夜炭）+ 日侧暖洗 + 色带 + 笔触。

已落地三轮迭代（基调 → 图腾精修 → TLD 完成度）。

### 6.3 L2→L3 绑定

**WorldAtmosphereBinder** 是首条 L2→L3 视觉通路（已通电）：
- 只读世界状态 → 自平滑 → LateUpdate 写入视觉组件
- 绑定：天气（雨/风/雷/雾）、天光（太阳角度/环境光）、植被（V/A→风/树/草）
- 后续视觉绑定复用此模式

### 6.4 痕迹视觉

草压痕通路已通电：trample → `_TramplePoints[16]` → 草 shader。WorldTraceBinder 管理 7 类痕迹的生命周期。
2026-07-20 起：痕迹有效年龄接天气（链1+风）；新鲜痕迹头顶占位标记 + 点击推近运镜（B 方案，占位实现）。

### 6.5 地形

- Demo2 轮已落地：B1 草原调色板 + 419 巨石 + 23k 草簇
- 地形生成器（TerrainGenerator.cs）用户禁改，保持粗粝形体
- 高度带脱钩隐患已记录（scale≠1 时 .mat 存值与几何脱钩）

---

## 七、进度总览

### 已完成 ✅

| 里程碑 | 内容 | 时间 |
|---|---|---|
| Phase 0 | 时钟与心跳拆分（WorldTick 自主 + InjectEmotion）；gameTime/season/lighting 三源统一 | 2026-07 |
| Phase 1 | EmergentMomentDetector → QuietConvergence 事件；charged fox + deer_mouse 有机压制 | 2026-07 |
| 翻译层 Step 1-2 | 无状态信号 1/2/3/7 迁入 TranslationLayer；ConsumeSignals / UpdateFromEEnv 拆分 | 2026-07 |
| 所有权轨道 4/6 | Rainfall/WindSpeed/FogDensity/StarVisibility 单写者确权；waterLevel/soilMoisture 单写者确权 | 2026-07 |
| L2→L3 大气绑定 | WorldAtmosphereBinder 首条通路通电（天气/天光/植被） | 2026-07 |
| 天空三轮迭代 | 桑人岩画方向定稿 + 图腾精修 + TLD 完成度 | 2026-07-10~12 |
| 反日点 NaN 修复 | pow 前 saturate 一行修 | 2026-07 |
| Demo2 地形轮 | B1 调色板 + 巨石 + 23k 草簇 | 2026-07 |
| 季节/日序/{sky}/速率表收敛 | 单一来源清单建立 | 2026-07 |
| Glimmer 统一渲染体系 | ToonCore + 三 shader + 幂等 Setup 菜单 | 2026-07 |
| 切片 2-9 实施轮 | 链2+T 修复 / 雨雾→动物×5 / 链1+风→痕迹 / 雨→vitality / 信占位 / 30 天预跑 / 标记与运镜 | 2026-07-20 |
| 所有权轨道 6/6 | tree.vitality、动物内部状态核实单写者 | 2026-07-20 |

### 进行中 🔄

| 工作项 | 状态 | 备注 |
|---|---|---|
| 切片 playtest 验证 | 待做 | 切片 1-9 代码全部落地（2026-07-20），需进 Play 逐条验收；场景存盘已闭合（2026-07-31 核实 HEAD 场景含 binder/ManualTicking/CameraPusher）；切片 9 正式机位待编辑器内取景保存 |
| DemoVisualAlignment 执行 | A 节已落地；B1 已落地（demo2 轮）；B2/B3 被"TerrainGenerator 禁改"取代、D 节随 07-28 C5 挂起（2026-07-31 销案批注） | 仅余 B4 高度带脱钩修复（隐患级） |
| SentimentEngine (L1) | 开发中 | 垂直切片暂用 SubmitEmotion stub 填位；清账日（2026-07-31）后排期为下一大块 |
| 清账日（2026-07-31） | 已落地 | wave0 批提交（含修复 HEAD 编译断裂）/月相恢复/C3 销案/断枝·占区文案/季节语气偏置；详见 更改_2026-07-31.md |

### 切片工程清单（2026-07-14 定稿；2026-07-20 全部代码落地）

这是通往垂直切片的工程路线，按优先级排列：

| # | 任务 | 级别 | 状态 |
|---|---|---|---|
| 1 | WorldAtmosphereBinder 挂入场景 + 修正 rainIntensity 残留 + nonL showAt 门控 | 接电 | ✅（防丢已并入 GlimmerVisualSetup；场景存盘待编辑器操作） |
| 2 | 链2：StarVisibility → `_StarBlend`（两行）+ 修 NarrativeRuleEngine 读不到 T | A 级 | ✅ |
| 3 | 雨→动物活动度 ×5（田鼠/狐狸/鹿鼠/候鸟/织巢鸟）| 批量 | ✅（经所在 zone 水位/湿度 + WeatherHarsh 语料；田鼠巢穴痕迹缓做） |
| 4 | 链1 + 风→痕迹：`effectiveAge = age × (1 + Rainfall×rF + WindSpeed×wF)` | B 级 | ✅（0.5/0.3，inspector 可调） |
| 5 | 雾→动物活动度（同第 3 项模式） | 批量 | ✅（并入 ActivityModifier） |
| 6 | 雨→猴面包树 vitality（先数据，视觉等 MPB） | 数据 | ✅（center 湿度偏置 0.2） |
| 7 | 信：pendingChronicles 首个展示消费者 + hasBeenShown 迁移 | A 级 | ✅（占位 L 键面板，形态待设计稿） |
| 8 | 回填：CreateNewWorld 后 30 天中性 WorldTick | 运维 | ✅ |
| 9 | 标记与运镜：痕迹感叹号 + B 方案点击推近 | 交互 | ✅（占位标记/运镜；场景机位设置待编辑器内完成） |

**切片不做**（排到后续）：旱机制（D 级）、生命×生命加密、开花/虫害视觉、S 维度激活、蒲公英/石边草激活。

---

## 八、已知断线与死端

按接线成本分级。状态刷新 2026-07-20（切片 2-9 实施轮后）。

### A 级·两端都在，只差一根线

| 源 | 目标 | 备注 |
|---|---|---|
| env.StarVisibility | 天空 `_StarBlend` | ✅ 已接（链2，随 binder 通电） |
| pendingChronicles | 信 UI | ✅ 已接（ChronicleLetter 占位消费者） |
| rhythm.dayProgress | LightManager | ✅ 随 binder（注：binder 在场景未存盘，保存即闭合；防丢已并入 GlimmerVisualSetup） |

### B 级·有数据源，视觉端要新写

| 源 | 缺什么 |
|---|---|
| location.waterLevel | ✅ 已接（2026-07-18 两段线性水面 + binder 驱动） |
| DecayLevel | 无凋萎/枯败可视化 |
| loc.vegetationDensity | 虫害啃掉的植被无视觉 |
| season | 无季节视觉表达 |
| Rainfall | ✅ 已接（链1+风：痕迹有效年龄天气公式） |

### C 级·有钩子，数据源/逻辑要新建

~~TreePlacement.showAt 门控~~ ✅（SetRainfall + 阈值，2026-07 落地）。
剩余：TreeData.vitality+MPB 凋萎通路、GrassPreset 曲线消费者、Phase 3 catch-up 摘要、BranchState.integrity 消费者、GetSeasonBias 调用者。

### D 级·机制本身不存在

雪（非洲无雪，不做）、旱（需新累积状态）、蒲公英/石边草行为、E_env.S 的实质消费。
~~E_env.T 的规则消费~~ ✅（规则引擎 eenv 补 T，2026-07-20）。

---

## 九、叙事管道现状

```
生产端:  BehaviorNarrator ─┐
         NarrativeRuleEngine ┼→ pendingChronicles ─→ 信（ChronicleLetter 占位，L 键）✅
         (RelationSystem 空转)┘       │
                               catch-up 时整段丢弃（唯一"出队"）

事件端:  worldEvents → WorldTraceBinder（痕迹）✓
                     → BehaviorNarrator（转文案）✓
```

- 信（2026-07-20 占位落地）：每次打开 ≤3 条，读完 hasBeenShown 翻 true 并迁 shownChronicles（ChronicleLetter 是唯一写者）；无通知压力，形态待设计稿
- `TreeBranchBroke` / `VoleClaimedZone` 有事件无文案；`AnimalArrived` 枚举从未 Emit
- WeatherHarsh 语料已上线：狐狸歇窝/候鸟低伏，仅真实下雨/起雾时由驱动层认领

---

## 十、项目文件结构

```
Assets/
  _Core/
    SentimentEngine/        # L1 — 无 Unity 依赖
    WorldSimulator/         # L2 — 世界状态、时钟、事件
    DataPersistence/        # 追加式日志 + 世界事件日志
  _Visual/
    Shaders/                # GlimmerToonCore + Terrain/Toon/RainStreak/SkyGradient
    VFX/                    # VFX Graph
    Plants/                 # Ecosystem_nonL（prefab 树）
  _Audio/                   # FMOD 封装
  _UI/                      # 输入层，不直接写世界状态

Docs/
  交互设计工作台_2026-07-21_矩阵补全.md   # 机制交互设计工作台（唯一工作台，设计者填写）
  TranslationLayer.md       # 翻译层 7 信号表 v1 FROZEN
  AmbientAtmosphereBinding.md  # L2→L3 大气绑定切片文档
  SkySanRockArt.md          # 天空桑人岩画定稿
  DemoVisualAlignment.md    # Demo 视觉对齐实施计划
  Demo2TerrainDetail.md     # Demo2 地形精细化记录
  ProjectSummary.md         # 本文档
```

---

## 十一、验收三问

切片完成后五天自测：

1. 第三天早晨你是"惦记"还是"该测试了"？
2. 不看代码，能指着场景某处痕迹讲出三环以上的来历吗？
3. 发生过归因错误吗——错得像通信中的误读，还是像 bug？

---

*本文档基于 CLAUDE.md 宪法、交互设计工作台事实盘点、以及历次开发迭代记录整理。机制交互设计决策权归设计者（交互设计工作台 §5 空行勿代填）。*
