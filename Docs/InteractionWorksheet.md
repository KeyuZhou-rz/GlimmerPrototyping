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
| T3 脚印串 | 任何位置迁移 | 8 游戏日 | — |
| T4 半程脚印 | 鹿鼠 activityRange<0.5（实时态） | 条件消失即撤 | 单实例 |
| T5 羽毛 | 候鸟/织巢鸟离场 | 6 游戏日 | — |
| T6 领地标记 | 狐狸巡逻 | 14 游戏日 | — |
| T7 压草痕×2 | QuietConvergence | 5 游戏日 | — |

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

### A 级 · 两端都在，只差一根线（最便宜）

| # | 源（有数据） | 目标（有钩子） | 备注 |
|---|---|---|---|
| A1 | env.StarVisibility | 天空 `_StarBlend` | **=链2，切片必做** |
| A2 | WorldAtmosphereBinder prefab | 场景 | **挂上即通电 7 条绑定** |
| A3 | rhythm.dayProgress | LightManager | A2 的一部分 |
| A4 | E_env.V/A | Flora 生态扇出（风/树/草） | A2 的一部分；注意 Flora 轨道已禁用，需决定扇出给谁 |
| A5 | pendingChronicles | （信 UI——待建） | 消费者不存在，但生产端全通 |

### B 级 · 有数据源，视觉端要新写表达

| # | 源 | 缺什么 | 
|---|---|---|
| B1 | location.waterLevel | 水面几何/材质不读它——涨水看不见 |
| B2 | DecayLevel | 无凋萎/枯败可视化 |
| B3 | loc.vegetationDensity | 虫害啃掉的植被无视觉（草密度不联动） |
| B4 | season/lightIntensity | 无季节视觉表达 |
| B5 | Rainfall | 痕迹老化公式不读它（**=链1，切片必做**） |

### C 级 · 有钩子，数据源/逻辑要新建

| # | 钩子 | 缺什么 |
|---|---|---|
| C1 | TreePlacement.showAt | 门控逻辑从未实现 + 无 emotionDensity 数据源 |
| C2 | TreeData.vitality + MPB | 整条 MPB 凋萎通路未落地 |
| C3 | currentWaterSaturation/SunlightIntensity | 输出悬空，植物系统不读 |
| C4 | GrassPreset（valenceToHealth 等曲线） | 整个 SO 无读者 |
| C5 | Phase 3 catch-up 摘要 | 钩子空置，现在直接丢弃 |
| C6 | BranchState.integrity | 4 根主枝个体状态无人消费 |
| C7 | GetSeasonBias（季节→情绪偏置） | 死代码，无调用者 |

### D 级 · 机制本身不存在（要新造）

雪（无任何代码）；旱（无独立状态，只有水位自然衰减）；蒲公英/石边草的任何行为；E_env.T 的规则消费；E_env.S 的实质消费。

---

## 5. 交互设计矩阵（工作台——「设计」「每环痕迹」由你填写）

> 每行填写前过两条原则：它连到哪个现有系统？每一环玩家看什么？
> 「现状」列是代码事实。设计栏空着 = 待你决策。

### 5.1 天气 × 生命（你点名的缺口）

| 交互对 | 现状（事实） | 设计（你填） | 每环痕迹（你填） |
|---|---|---|---|
| 雨 → 田鼠 | 仅经 lowland 水位间接（庇护↓/搬家规则） | | |
| 雨 → 狐狸 | rain<0.3 时 safety 恢复（唯一直连） | | |
| 雨 → 鹿鼠 | 无 | | |
| 雨 → 候鸟 | 仅经河岸水位间接（舒适度） | | |
| 雨 → 织巢鸟 | 无 | | |
| 雨 → 猴面包树 | 无（vitality 只读 V） | | |
| 雨 → 蒲公英/石边草 | 植物本身惰性 | | |
| 旱（长期低雨） | 机制不存在（仅水位每日 −0.03 自然消退） | | |
| 雪 | 机制不存在（含冬季无任何视觉/模拟表达） | | |
| 雾 → 动物 | 无（雾只进 {sky} 文案和画面） | | |
| 风 → 痕迹/树/草 | 视觉有（shader 风），模拟零 | | |

### 5.2 已拍板的两条横向连接（规格你填）

| 交互对 | 现状 | 设计（你填：公式/阈值/例外） |
|---|---|---|
| 雨 → 痕迹寿命（链1） | 痕迹老化只看游戏日龄 | |
| StarVisibility → 天空星穹（链2） | 天空自算 wNight×badT，L2 的 T 维白算 | |

### 5.3 生命 × 生命（现有网的加密）

| 交互对 | 现状 | 设计（你填） | 每环痕迹（你填） |
|---|---|---|---|
| 开花 → 更多动物 | 仅田鼠 center 加食 | | |
| 虫害 → 视觉/更多下游 | 只削高地植被数值 | | |
| 蒲公英 → ? | 完全惰性 | | |
| 石边草 → ? | 完全惰性 | | |
| 候鸟在场 → ?（在场期间的正效应） | 仅狐狸 safety↓ | | |
| 主枝个体（integrity） → ? | 字段闲置 | | |

### 5.4 情绪维度死端（T 与 S 的去处）

| 维度 | 现状 | 设计（你填） |
|---|---|---|
| T（时间性） | 仅星空信号；规则引擎读不到 | |
| S（社会性） | 仅 CreatureAbundance（死端） | |

### 5.5 空白行（自由添加）

| 交互对 | 现状 | 设计 | 每环痕迹 |
|---|---|---|---|
| | | | |
| | | | |

---

## 6. 切片工程清单（设计定稿后的施工顺序——供参考，可调）

1. **接电**：WorldAtmosphereBinder 挂进场景（A2），修正 rainIntensity 序列化残留
2. **链2**：StarVisibility→`_StarBlend`（A1，两行）
3. **链1**：Rainfall→痕迹老化（B5，按你 5.2 的规格）
4. **信**：pendingChronicles 首个展示消费者（A5）+ hasBeenShown/shownChronicles 迁移逻辑
5. **标记与运镜**：痕迹感叹号 + B 方案点击推近
6. **回填**：CreateNewWorld 后 30 天中性 WorldTick
7. 你在 §5 填好的交互，按 A→B→C→D 成本排队入库
