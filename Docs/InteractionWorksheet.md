# 交互设计工作台（机制与实体总账）

> **这是游戏的根。**本文档由 Claude 盘点代码事实（2026-07-13，三路穷尽式盘点），
> **交互设计由设计者亲自填写**——所有「设计」「痕迹表达」栏留空即为待你决策。
> 两条宪法原则已入 `CLAUDE.md`：**机制互联** / **无痕不成环**。
> 事实锚点均为 file:line，可直接核对。

---

## 0. 已拍板决策（2026-07-13 讨论定稿）

| 决策项 | 结论 |
|---|---|
| 参照基准 | **Mountain**：无留存机制、无每日奖励、无通知、无进度条、无社交压力；驱动力=好奇心+情感投射 |
| 玩家视角 | **B 方案**：固定机位 + 点击痕迹推近的舞台式运镜（KRZ 剧场感，契合岩画平面性） |
| 变化提醒 | **痕迹锚定的感叹号式标记**——引导玩家注意变化的机制，配合视角推近；注意红线：标记指向场景位置，**永不暴露数值** |
| L1 | **不等 SentimentEngine**：切片用 `SubmitEmotion(V,A,C)` 手动滑条打桩，两线并行 |
| 横向连接 1 | **雨洗痕迹**（Rainfall 调制痕迹寿命）——切片必做 |
| 横向连接 2 | **星穹绑定**（StarVisibility→天空 `_StarBlend`）——切片必做 |
| 天气缺口 | 雨/雪/旱对动物、树木的影响不完善——**经本工作台设计后逐一修正**，不即兴入库 |
| 第一小时 | 玩家到达一个**已经活过的世界**（初始化后先跑 ~30 天中性 WorldTick 再交给玩家） |

手动五维玩法实验的权威协议见 `Docs/GameplayLabLrotocol.md`。批准后的 `20260726-03` 先隔离网络与正式输入 UI，由研究者在 Inspector 调整五维；一次命令固定执行“注入当前向量 → WorldTick 一天 → 保存”，无输入日使用独立自主 Tick。两种命令都不暴露给玩家。

**验收三问**（五天自测）：① 第三天早晨你是"惦记"还是"该测试了"？② 不看代码，能指着场景某处痕迹讲出三环以上的来历吗？③ 发生过归因错误吗——错得像通信中的误读，还是像 bug？

---

## 1. 实体总表

### 1.1 动物（5 只，`WorldEntityData.cs:24`）

| speciesId | 初始位置 | 内部状态向量（各物种私有） | 行为 drive 候选 |
|---|---|---|---|
| vole 田鼠 | lowland | shelterSecurity / foodStock / expansionPressure | Expand / Forage / Burrow |
| fox 狐狸 | highland_east | hunger / safety / territoryStability | Foraging / Patrol / Avoidance / Rest |
| deer_mouse 鹿鼠 | highland_east | anxiety（+activityRange 0.8） | Retreat / Explore / Routine |
| migratory_bird 候鸟 | 不在场 | migrationUrge / settlementComfort | Away / Depart / EarlyDepart / Settle |
| weaver_bird 织巢鸟 | center | （无向量，纯事件驱动） | Nest / Away |

通用字段：isPresent / location / facingDirection / familyState / lastSeenDate / activityRange / behavior(drive+cause+intensity+zone) / history(append-only)。
**跨实体成因标签**（文本层优先选用，`AnimalStateData.cs:63`）：FoxNearby / BirdAbsent / DeerMouseWithdrew / RodentExpansion。

### 1.2 植物（3 株，`WorldEntityData.cs:47`）

| plantId | 位置 | 活跃状态 | 备注 |
|---|---|---|---|
| baobab_main 猴面包树 | center | vitality（E_env.V 积分 α=0.03）/ floweringReadiness / isFlowering / permanentDamages / 4 根主枝 | 唯一有内部模拟的植物 |
| dandelion_riverbank 蒲公英 | riverbank | **惰性**（初始值后无任何运行时写者） | 待设计激活 |
| grass_stone_edge 石边草 | stone_area | **惰性**（同上） | 待设计激活 |

死端事实：`BranchState.integrity`（4 根主枝各有）初始化后**零读写**——断枝实际走规则→permanentDamages 计数边沿（`AnimalDriveSystem.cs:388`）；`growthStage/isAlive/plant.location` 无写者。

### 1.3 地点（5 处 + 拓扑，`WorldEntityData.cs:78`）

| locationId | waterLevel 初值 | soilMoisture | vegetationDensity | 雨水积累率 accRate / 渗透率 soakRate |
|---|---|---|---|---|
| riverbank 河岸 | 0.5 | 0.8 | 0.75 | 0.22 / 0.12 |
| lowland 低洼地 | 0.4 | 0.65 | 0.7 | **0.30** / 0.15（最容易积水） |
| center 草原中央 | 0.3 | 0.5 | 0.6 | 0.15 / 0.10 |
| highland_east 东侧高地 | 0.1 | 0.35 | 0.45 | 0.10 / 0.07 |
| stone_area 石头区 | 0.05 | 0.2 | 0.25 | 0.08 / 0.04 |

拓扑（`ZoneTopology.cs:13`）：riverbank─lowland─center─highland_east；center─stone_area。
每日自然消退：waterLevel −0.03、soilMoisture −0.02（`WorldManager.cs:266,279`）。
`permanentChanges`：append-only 永久地貌（现有：田鼠洞塌陷）。

### 1.4 全局环境状态（8 字段，`WorldEnvironmentSystem.cs:7`，运行时不进存档）

| 字段 | 来源 | 下游消费者 |
|---|---|---|
| Rainfall | 信号1 Wetness（←E_env.V） | 地点水位/湿度传播、狐狸 safety、叙事 {sky}、绑定器→雨粒子 |
| WindSpeed | 信号2 Agitation（←E_env.A） | 绑定器→风强/雷暴门控 |
| FogDensity | 信号3 Dimness（←E_env.C） | 叙事 {sky}、绑定器→雾 dimness |
| StarVisibility | 信号7 Firmament（←E_env.T×晴×夜） | **无消费者**（天空 `_StarBlend` 自己另算——链2要缝的就是这条） |
| SoilMoisture(全局) | Rainfall 积分 | **无消费者**（≠地点字段，信号4遗留） |
| VegetationDensity(全局) | SoilMoisture×(1−Decay) | **无消费者**（≠地点字段，信号5遗留） |
| DecayLevel | V<−0.3 累积 | 仅喂全局 VegetationDensity——**无视觉表达** |
| CreatureAbundance | E_env.S | **无消费者**（信号6遗留） |

### 1.5 情绪向量与时间

- **E_env(V,A,T,S,C)**：惯性注入 α=Lerp(0.1,0.3,C)（C 越高响应越快，`EmotionInertiaSystem.cs:31`）；每日向零基线回落 α=0.05。
- **维度消费现状**：V→雨/衰败/树活力/候鸟 | A→风/狐狸/断枝规则 | C→雾/鹿鼠基线焦虑/注入速率 | T→仅星空信号（**规则引擎读不到 T**，`NarrativeRuleEngine.cs:143`）| S→仅 CreatureAbundance（死端）。
- **时间一钟两粒度**（`NaturalRhythmSystem.cs:30`）：季节/年进度←世界日历（30天/月）；光照/dayProgress←真实墙钟。`GetSeasonBias`（季节→情绪偏置）**是死代码，无调用者**。
- **墙钟 catch-up**：缺席天数全额推进日历，重模拟最多 90 天；catch-up 期间世界志**直接丢弃**——Phase 3 摘要钩子空置（`WorldManager.cs:174`）。

---

## 2. 机制总表

### L2 模拟管线（每次 SimulatePass 的执行顺序，`WorldManager.cs:128-153`）

| # | 机制 | 读 | 写 | 备注 |
|---|---|---|---|---|
| M4 | 翻译层 | E_env(V,A,C,T)+光照 | 信号1/2/3/7 | 无状态公式 |
| M5 | 环境系统 | 信号+E_env(V,S) | 全局环境 8 字段 | 4 字段是死端（§1.4） |
| M6 | 水位传播 | Rainfall | 5 地点 waterLevel/soilMoisture | 按地点分档积累 |
| M7 | 植被系统 | weaver.isPresent | highland_east.vegetationDensity | 虫害 −0.003/tick；**气候基线 SEAM 未接** |
| M8 | 动物驱力 | 快照+环境+E_env | 动物内部状态/位置/behavior | 双缓冲，1 日相位滞后 |
| M9 | 涌现检测 | 全局状态（只读） | QuietConvergence 事件 | 冷却90天+概率门 |
| M10 | 行为叙事 | behavior+worldEvents | pendingChronicles | 59 条模板/10 类 |
| M11 | 规则引擎 | 实体/情绪/时间字段 | 实体状态+chronicles | 5 条规则，每 pass 最多触发 2 条 |
| M12 | 关系系统 | — | — | **空转**（4 条关系全退役，被驱力系统接管） |

### 5 条规则（M11 详表）

| ruleId | 触发（AND） | 效果 | 冷却 |
|---|---|---|---|
| baobab_branch_broken | A>0.7 ∧ V<−0.4 | 断枝（永久）→级联织巢鸟离巢 | 60天 |
| vole_relocate_flood | lowland 水位>0.65 ∧ 田鼠在 lowland | 田鼠→highland_east | 14天 |
| migratory_bird_arrival | 9-11月 ∧ V>0.2 ∧ 不在场 | 候鸟→riverbank | 90天 |
| fox_path_deviation | A>0.6 | 狐狸 facing→E | 10天 |
| vole_burrow_abandoned | 田鼠在高地 ∧ lowland 水位>0.5 | 永久地貌：塌洞 | 30天 |

注意：规则冷却是**内存字典，重启清零**（`NarrativeRuleEngine.cs:16`）。

### 痕迹系统（L3，`WorldTraceBinder.cs`——已是"无痕不成环"的原型）

| 痕迹 | 触发 | 寿命 | 上限 |
|---|---|---|---|
| T1 土堆 | 田鼠扩张/洪水搬家 | 三阶段老化，**永久** | 最新 6 个 |
| T2 塌洞 | 永久地貌记录 | **永不移除**（不可逆原则） | 无 |
| T3 脚印串 | 任何位置迁移 | 8 游戏日；前 3 日每枚脚印各自压弯周围草 | — |
| T4 半程脚印 | 鹿鼠 activityRange<0.5（实时态） | 条件消失即撤 | 单实例 |
| T5 羽毛 | 候鸟/织巢鸟离场 | 6 游戏日 | — |
| T6 领地标记 | 狐狸巡逻 | 14 游戏日 | — |
| T7 压草痕×2 | QuietConvergence | 5 游戏日；只压弯草，不铺实体色片；形变尺度与 T3 脚印一致 | — |

附：草压痕通路（trample→`_TramplePoints[16]`→草 shader）已通电。
**当前痕迹寿命与天气无关**——链1（雨洗痕迹）就是要把 Rainfall 接进这里的老化公式。

### 叙事管道（M10/M11 → 谁读？）

```
生产端（全通）:  BehaviorNarrator ─┐
                NarrativeRuleEngine ─┼→ pendingChronicles ─→ ??? （无 UI 消费者）
                (RelationSystem 空转)┘        │
                                    catch-up 时整段丢弃（唯一"出队"）
事件端（部分通）: worldEvents → WorldTraceBinder（痕迹）✓
                            → BehaviorNarrator（转文案）✓
```

死端事实：`shownChronicles` 零写零读；`hasBeenShown` 恒 false 无翻转；`TreeBranchBroke`/`VoleClaimedZone` 有事件**无文案**；`AnimalArrived` 枚举**从未 Emit**。
**切片要建的"信"= pendingChronicles 的第一个展示消费者。**

### L3 视觉系统与接线现状

| 系统 | 在场景? | 关键事实 |
|---|---|---|
| EmotionWeatherController | ✓ | 雨/雾/雷/风/天空盒总控；**rainIntensity 序列化=-1（永久暴雨）**；sceneWindZone=null（风空转）；currentWaterSaturation/SunlightIntensity 输出**无人读** |
| **WorldAtmosphereBinder** | **✗ 不在场景** | **L2→L3 全部 7 条绑定断电**——切片第一根要接的线 |
| LightManager | ✓ | binder 缺席→内部时钟自增（TimeMultiplier=10） |
| WorldTraceBinder | ✓ | 痕迹通路健康 |
| ZoneMap | ✓ | 5 zone 圆盘锚点+落地射线；支持 Anchor_ 子物体手动覆盖 |
| GrassSystem | ✓ | 23440 簇；SetEmotionState 钩子在但**无人调**；GrassPreset SO 整体无读者 |
| Ecosystem_nonL（prefab 树） | ✓ | showAt 门控**从未实现**（写入即固定）；TreeData.vitality+MPB 承诺**未落地** |
| Flora.EcosystemManager（L-System） | ✓（已禁用轨道） | 情绪扇出链完整但上游断（binder 缺席）；grassSystem 引用 null→会自建第二个实例 |
| WaterGenerator | ✓ | 水面几何是静态 inspector 值，**不读 location.waterLevel**——逻辑水位无视觉表达 |
| WindSystem | ✗（运行时自建） | 全局 shader 风变量的唯一写者 |

---

## 3. 现存连接网（事实——这是你要织密的那张网的现状）

**主干（情绪→环境→地点→动物）：**
V→雨→[lowland 积水→田鼠庇护↓→洪水搬家] / [河岸水位→候鸟舒适度]
A→[鹿鼠 baseline 之外的狐狸惊吓] / [狐狸 safety↓] / [断枝规则] / [狐狸改道规则]
C→[雾] / [鹿鼠基线焦虑] / [情绪注入速率 α]

**实体↔实体（驱力系统内，已替代退役关系）：**
- 织巢鸟不在 → 鹿鼠焦虑↑（+0.08/tick）→ activityRange↓
- 狐狸邻近 → 鹿鼠焦虑 +0.25
- 鹿鼠 range<0.6 → center 腾空 → 田鼠扩张压力↑ → Expand 进占 center
- 田鼠进狐狸领地{highland_east,center} → 狐狸领地稳定度↓ → Patrol 追击
- 候鸟在河岸 ∧ 狐狸邻近 → 狐狸 safety↓ / 候鸟舒适度↓ → EarlyDepart
- 断枝 → 织巢鸟离巢 → 虫害 → 高地植被↓（→ 田鼠食物间接↓）
- 树 vitality>0.5 ∧ 断枝后>30 tick → 织巢鸟归巢
- 树开花 ∧ 田鼠在 center → 田鼠食物 +0.05
- 邻区植被最高处 → 狐狸觅食目标

**天气→生命的现有直连（很薄，你怀疑得对）：**
- 狐狸：A<0.4 ∧ rain<0.3 → safety 恢复 +0.03（**唯一一条动物直读天气**）
- 其余全部经水位间接（田鼠庇护、候鸟舒适度）
- 树：vitality 只读 V，**不读雨/湿度**；雪、旱作为机制**不存在**

---

## 4. 断线与死端清单（连接机会——按接线成本分级）

> 状态刷新 2026-07-20（代码实证）：A1/A2/A5、B1/B5、C1 已接；D 级 T 规则消费已修。
> 详见 §6 切片清单逐项状态。

### A 级 · 两端都在，只差一根线（最便宜）

| # | 源（有数据） | 目标（有钩子） | 备注 |
|---|---|---|---|
| A1 | env.StarVisibility | 天空 `_StarBlend` | ✅ 已接（binder→starVisibility→`_StarBlend`，EmotionWeatherController.cs:464）；随 A2 场景保存通电 |
| A2 | WorldAtmosphereBinder prefab | 场景 | 🔄 binder 在场景但未存盘；已并入 `GlimmerVisualSetup.Run()` 防丢（2026-07-20），保存场景即闭合 |
| A3 | rhythm.dayProgress | LightManager | ✅ 随 A2（binder:124-126） |
| A4 | E_env.V/A | Flora 生态扇出（风/树/草） | ✅ 随 A2；Flora L-System 轨道已禁用，扇出走 nonL SetRainfall + 风 shader |
| A5 | pendingChronicles | （信 UI——待建） | ✅ 信占位消费者已建（`ChronicleLetter.cs`，L 键，hasBeenShown/shownChronicles 唯一写者） |

### B 级 · 有数据源，视觉端要新写表达

| # | 源 | 缺什么 | 
|---|---|---|
| B1 | location.waterLevel | ✅ 已接（`WaterGenerator.SetDisplayLevel01` 两段线性 + binder 驱动，2026-07-18） |
| B2 | DecayLevel | 无凋萎/枯败可视化 |
| B3 | loc.vegetationDensity | 虫害啃掉的植被无视觉（草密度不联动） |
| B4 | season/lightIntensity | 无季节视觉表达 |
| B5 | Rainfall | ✅ 已接（链1+风：`effectiveAge = age × (1 + Rainfall×0.5 + WindSpeed×0.3)`，WorldTraceBinder，2026-07-20） |

### C 级 · 有钩子，数据源/逻辑要新建

| # | 钩子 | 缺什么 |
|---|---|---|
| C1 | TreePlacement.showAt | ✅ 已接（`SetRainfall` + `_showAtThresholds` 运行时门控，binder 每帧推送；非原设想的 emotionDensity，切片 1 拍板改道） |
| C2 | TreeData.vitality + MPB | 整条 MPB 凋萎通路未落地 |
| C3 | currentWaterSaturation/SunlightIntensity | 输出悬空，植物系统不读 |
| C4 | GrassPreset（valenceToHealth 等曲线） | 整个 SO 无读者 |
| C5 | Phase 3 catch-up 摘要 | 钩子空置，现在直接丢弃 |
| C6 | BranchState.integrity | 4 根主枝个体状态无人消费 |
| C7 | GetSeasonBias（季节→情绪偏置） | 死代码，无调用者 |

### D 级 · 机制本身不存在（要新造）

雪（无任何代码）；旱（无独立状态，只有水位自然衰减）；蒲公英/石边草的任何行为；E_env.S 的实质消费。
~~E_env.T 的规则消费~~ ✅ 已修（2026-07-20，NarrativeRuleEngine eenv 上下文补 T）。

---

## 5. 交互设计矩阵（工作台——「设计」「每环痕迹」由你填写）

> 每行填写前过两条原则：它连到哪个现有系统？每一环玩家看什么？
> 「现状」列是代码事实。设计栏空着 = 待你决策。

### 5.1 天气 × 生命（你点名的缺口）

| 交互对 | 现状（事实） | 设计（你填） | 每环痕迹（你填） |
|---|---|---|---|
| 雨 → 田鼠 | 仅经 lowland 水位间接（庇护↓/搬家规则） | 雨造成土壤湿度 水位变化 mouse每tick读取相关值并作出反应 结构与其他焦虑值等相仿| 雨摧毁巢穴->相关痕迹 |
| 雨 → 狐狸 | rain<0.3 时 safety 恢复（唯一直连） | 雨造成土壤湿度 水位变化 导致狐狸活动度&&捕猎意愿下降 | 除了活动频率 语料可出现 +|
| 雨 → 鹿鼠 | 无 | 同田鼠 主要发散为活动度下降 考虑巢穴 相关痕迹  | |
| 雨 → 候鸟 | 仅经河岸水位间接（舒适度） | 同上考虑 活动频率下降 + 语料提醒| |
| 雨 → 织巢鸟 | 无 | 同 | |
| 雨 → 猴面包树 | 无（vitality 只读 V） | 【切片做数据】vitality 公式加 Rainfall 调制：雨季 vitality 恢复加速、旱季减速。视觉（叶子颜色/密度/开花）等 MPB 通路落地后再接——数据是视觉的前提 | 当前无（等 MPB 落地后：雨季更绿/开花，旱季落叶） |
| 雨 → 蒲公英/石边草 | 植物本身惰性 | 【切片不做】实体惰性，先不动 | — |
| 旱（长期低雨） | 机制不存在（仅水位每日 −0.03 自然消退） | 【切片不做】需要新累积状态 + 阈值定义，成本 D 级。但它是未来季节/水位视觉/草色的统一上游，等基础视觉通路齐了再做 | 当前无 |
| 雪 | 机制不存在（含冬季无任何视觉/模拟表达） | 【不做】非洲草原无雪 | — |
| 雾 → 动物 | 无（雾只进 {sky} 文案和画面） | 【切片做】dimness > 阈值时动物 activityModifier *= 0.5，跟雨→动物同一模式批量做 | 雾天动物痕迹减少（同雨天的逻辑——活动少了痕迹就少） |
| 风 → 痕迹/树/草 | 视觉有（shader 风），模拟零 | 【切片做】和链1一起：痕迹有效年龄叠加 WindSpeed 因子，风大时脚印/羽毛被吹散更快 | 大风后痕迹比平常更模糊——与雨洗痕迹同属"天气擦除痕迹"的信号 |

### 5.2 已拍板的两条横向连接（规格你填）

| 交互对 | 现状 | 设计（你填：公式/阈值/例外） |
|---|---|---|
| 雨 → 痕迹寿命（链1） | 痕迹老化只看游戏日龄 | `effectiveAge = age × (1 + Rainfall × rainFactor + WindSpeed × windFactor)`，其中 Rainfall/WindSpeed 取痕迹存在期间的日均值（或直接用当前值近似）。不改最大寿命，改有效年龄——雨天风天痕迹老得更快。rainFactor≈0.5, windFactor≈0.3，具体数值 playtest 调 | 
| StarVisibility → 天空星穹（链2） | 天空自算 wNight×badT，L2 的 T 维白算 | Binder（或 sky 脚本）读 `env.StarVisibility` → 写 `_StarBlend`。两行。映射方向：高 T（紧迫）→ 星更亮/更多？低 T（停滞）→ 星更暗/更少？语义等设计者定 |

### 5.3 生命 × 生命（现有网的加密）

| 交互对 | 现状 | 设计（你填） | 每环痕迹（你填） |
|---|---|---|---|
| 开花 → 更多动物 | 仅田鼠 center 加食 | 【切片不做】等树的开花视觉落地后一起做。方向：开花→吸引昆虫→织巢鸟更活跃→更多痕迹近树 | 当前无（等视觉后：树周多虫/鸟迹） |
| 虫害 → 视觉/更多下游 | 只削高地植被数值 | 【切片不做】需要草 shader 支持区域颜色/密度，成本 C 级 | 当前无（等草 shader 后：枯黄斑块） |
| 蒲公英 → ? | 完全惰性 | 【切片不做】实体惰性 | — |
| 石边草 → ? | 完全惰性 | 【切片不做】实体惰性 | — |
| 候鸟在场 → ?（在场期间的正效应） | 仅狐狸 safety↓ | 【切片不做】可加：候鸟在→吃虫→虫害减，等虫害视觉有了再一起做 | 语料为主 |
| 主枝个体（integrity） → ? | 字段闲置 | 【切片不做】断枝规则已经产生 permanentDamages + 离巢级联，integrity 的粒度（4 根主枝各自状态）等树的视觉精度够了再启用 | 断枝本身可见（枝少了），但哪根断了当前无区分 |

### 5.4 情绪维度死端（T 与 S 的去处）

| 维度 | 现状 | 设计（你填） |
|---|---|---|
| T（时间性） | 仅星空信号；规则引擎读不到 | 【切片做链2】StarVisibility→`_StarBlend` 接上。规则引擎读不到 T 是 bug（`NarrativeRuleEngine.cs:143`）——修掉，让规则也能用 T。额外消费暂时不加 |
| S（社会性） | 仅 CreatureAbundance（死端） | 【切片不做】Glimmer 设定里玩家是唯一的人——社会性维度在这个世界本来就该是低的。暂不退役但也不激活，等将来有多角色/社群概念时再重新设计 |

### 5.5 空白行（自由添加）

| 交互对 | 现状 | 设计 | 每环痕迹 |
|---|---|---|---|
| | | | |
| | | | |

---

## 6. 切片工程清单（2026-07-14 定稿；2026-07-20 实施轮：1-9 全部代码落地，待 playtest 验证）

1. **接电**：WorldAtmosphereBinder 挂进场景（A2），修正 rainIntensity 序列化残留；第三条腿从 `SetEmotionState(V,A)` 改为 `SetRainfall(rainfall)` → nonL EcosystemManager（TreePlacement）showAt 门控
   ✅ binder 在场（待场景存盘闭合）+ 残留清零 + showAt 门控已通；防丢已并入 `GlimmerVisualSetup.Run()`
2. **链2**：StarVisibility→`_StarBlend`（A1，两行）+ 修 NarrativeRuleEngine 读不到 T 的 bug
   ✅ 星空通路代码已在（binder→starVisibility→`_StarBlend`）；规则引擎 eenv 上下文已补 T
3. **雨→动物活动度 ×5**（田鼠/狐狸/鹿鼠/候鸟/织巢鸟）：Rainfall 调制 activityModifier，批量同模式
   ✅ `ActivityModifier(zone)`：读所在 zone 水位/湿度（雨经 M6 间接作用，按 §5.1 矩阵）；狐狸/候鸟加 WeatherHarsh 语料（仅真实下雨/起雾时认领）；田鼠巢穴痕迹按裁定缓做
4. **链1 + 风→痕迹**：`effectiveAge = age × (1 + Rainfall × rainFactor + WindSpeed × windFactor)`（B5 + B 级风）
   ✅ WorldTraceBinder：weatherMul 全类型痕迹生效，rainFactor 0.5 / windFactor 0.3 inspector 可调
5. **雾→动物活动度**：dimness 阈值 → activityModifier，跟第 3 项同模式
   ✅ 并入 `ActivityModifier`（FogDensity > 0.5 → ×(1−0.5)，阈值/强度 tuning 可调）
6. **雨→猴面包树 vitality**：vitality 公式加 Rainfall 调制（先数据，视觉等 MPB 落地）
   ✅ TickTree：vitality 目标值加 center 湿度偏置（treeRainVitalityBias 0.2）
7. **信**：pendingChronicles 首个展示消费者（A5）+ hasBeenShown/shownChronicles 迁移逻辑
   ✅ 占位实现 `ChronicleLetter.cs`：L 键开关，每次 ≤3 条，无通知压力；形态待设计稿换皮
8. **回填**：CreateNewWorld 后 30 天中性 WorldTick
   ✅ WorldManager.Awake：lastTickRealTime 为空 → WorldTick(30, isCatchUp:true)（世界志噪音丢弃，worldEvents 保留作痕迹历史）
9. **标记与运镜**：痕迹感叹号 + B 方案点击推近
   ✅ 占位实现：新鲜痕迹（有效年龄 ≤1.5 日）头顶悬浮亮点 + TraceClickable/CameraPusher/TraceInput（推近-停留-返回）；场景机位设置待编辑器内保存

以下切片不做，排到后续：
- 旱（D 级，需新累积状态）
- 生命×生命加密（等视觉基础设施 C1-C4 先落地）
- 开花/虫害/断枝主枝视觉
- S 维度激活
- 蒲公英/石边草激活
- 田鼠巢穴雨毁痕迹（§5.1 痕迹栏，形态待设计者定）
