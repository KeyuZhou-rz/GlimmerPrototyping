# Gameplay Lab Protocol

> 文件名按当前项目约定保留为 `GameplayLabLrotocol.md`。
> 本文是手动五维输入玩法实验的唯一实施契约。后续实验控制器、场景配置、Debug Log 和测试步骤必须遵守本文；任何偏离先修改本文并重新审批，不得在代码中临时发明语义。

## 0. 文档状态

| 项目 | 状态 |
|---|---|
| 协议版本 | `gameplay-lab/1` |
| 数据提案 | `20260726-01`，待设计者明确批准 |
| 代码实现 | 未开始 |
| 场景接线 | 未开始 |
| 存档格式变更 | 无 |
| L2 接口变更 | 无 |
| 网络接入 | 不在本轮范围 |

规范词：

- **必须**：实现和实验不得偏离。
- **不得**：违反即判定该次样本无效。
- **建议**：允许基于实验结果修订，但修订前记录原因。

---

## 1. 实验目的

本轮不验证 Sentiment Engine 的联网质量，也不验证正式日记 UI。外部工具负责把真实文本解析成五维向量，研究者把结果手动录入 Unity，再用显式 Tick 推进世界。

本轮只回答：

1. 一次五维输入经过 `E_env` 惯性后，世界是否产生值得继续观察的变化。
2. 世界自主推进后，变化是否留下玩家能发现的痕迹。
3. 玩家能否在不看内部数值的情况下，从环境、痕迹和世界志理解至少三环因果。
4. 玩家是否主动想知道下一天会发生什么。

### 1.1 不在本轮范围

- WSL、HTTP、TCP、UDP 或任何网络传输。
- 正式日记输入界面。
- 玩家可见的五维、Tick、快进、重置或状态面板。
- 新的生态规则、新的跨机制关系或新的永久事件。
- 修改 `EmotionVector`、`JournalEntry`、`WorldSaveData` 或 SaveSystem 格式。
- 生产遥测、云端日志或日记文本采集。
- 用实验结果证明真实等待、长期留存或跨日回访已经成立。

手动 Tick 可以验证因果可读性和观察兴趣，不能替代真实时间节奏测试。

---

## 2. 架构边界

实验链固定为：

```text
外部文本
  → 外部 Sentiment Engine
  → 五维向量 + 外部 sampleId
  → 研究者手动录入 GameplayLab Inspector
  → ManualGameplayLab 只做校验与编排
  → WorldManager.OnJournalSubmitted(EmotionVector)
  → EmotionInertia / SimulatePass / SaveSystem
  → 研究者显式执行 WorldTick(1)
  → SaveSystem.SaveWorldState
  → L3 只读世界状态并平滑显示
```

边界要求：

- 外部 Sentiment Engine 是 L1，Unity 实验控制器不分析文本。
- `ManualGameplayLab` 是测试编排层，不拥有世界状态。
- `ManualGameplayLab` 不直接写 `E_env`、环境、实体、事件、地点或痕迹。
- L2 仍只通过现有参数入口接收 `EmotionVector`。
- L3 仍只读 `E_env` 和世界状态。
- 世界事件、永久损伤和历史继续 append-only。
- 控制器不得提供存档重置、事件删除、撤销或时间倒退。

---

## 3. 当前代码事实

以下行为是协议基础，不得凭印象改写。

### 3.1 提交事务

`WorldManager.OnJournalSubmitted(EmotionVector)` 会构造 `rawText="[test]"` 的 `JournalEntry`，再调用正式入口。

正式入口按以下顺序执行：

```text
WallClockDeltaDays
→ 必要时先 WorldTick(catch-up)
→ InjectEmotion
→ 当前日期 SimulatePass
→ SaveWorldState
→ AppendJournalEntry
```

因此：

- 一次 Submit 会追加一条情绪历史。
- 一次 Submit 会在当前游戏日期运行一次响应式 `SimulatePass()`。
- 一次 Submit 不主动把游戏日期加一天。
- 一次 Submit 会写 `world_state.json` 和 `journal_log.json`。
- 测试重载实际会写 `[test]` 日记；本轮接受这一事实，但 Debug Log 不得把它描述为“不写日志”。

### 3.2 世界 Tick

`WorldManager.WorldTick(1)` 按以下顺序执行：

```text
gameTime + 1 day
→ EmotionInertia.Relax(alpha=0.05)
→ NaturalRhythm.Tick
→ SimulatePass
```

因此：

- 一次 Tick 恰好推进一个游戏日。
- 一次 Tick 恰好运行一次自主 `SimulatePass()`。
- Tick 不追加情绪历史。
- Tick 不追加 JournalEntry。
- Tick 本身不调用 `SaveWorldState()`；实验控制器必须在 Tick 成功后立即保存。

### 3.3 一个输入实验周期有两次模拟

一个标准输入实验周期包含：

1. Submit 在日期 D 运行一次响应式模拟。
2. Tick 把日期推进到 D+1，再运行一次自主模拟。

这两次模拟语义不同，不得合并计数，也不得把 Submit 称为 Tick。

---

## 4. 数据结构变更提案

### 4.1 基本信息

- **提案 ID**：`20260726-01`
- **发起 agent**：OpenCode
- **影响范围**：`NEW`
- **审批状态**：待设计者明确回复“批准 20260726-01”后方可实现

提案只新增一个实验组件及其运行时状态，不修改任何现有 Data 类型、公开接口或存档字段。

### 4.2 新增类型

| 文件 | 类型 | 可见性 | 用途 |
|---|---|---|---|
| `Assets/GlimmerDiary/Scripts/Utils/ManualGameplayLab.cs` | `ManualGameplayLab : MonoBehaviour` | `public`，Unity 组件要求 | 手动输入、命令编排、校验和 Debug Log |
| 同文件 | `GameplayLabPhase` | `private enum` | 约束 `Idle / AwaitingTick / Invalid` 状态机 |

### 4.3 新增序列化字段

| 字段名 | 类型 | 默认值 | Inspector 范围 | 用途 | 持久化到世界存档 |
|---|---|---:|---|---|---|
| `sampleId` | `string` | `""` | 非空、去首尾空格 | 对应外部样本，不包含原文 | 否 |
| `valence` | `float` | `0f` | `[-1, 1]` | 外部 V | 否 |
| `arousal` | `float` | `0.3f` | `[0, 1]` | 外部 A | 否 |
| `temporality` | `float` | `1f` | `[0, 1]` | 外部 T | 否 |
| `sociality` | `float` | `0f` | `[0, 1]` | 外部 S | 否 |
| `certainty` | `float` | `0.5f` | `[0, 1]` | 外部 C | 否 |

Inspector 的 `[Range]` 只帮助录入，命令执行时仍必须显式检查范围、`NaN` 和 `Infinity`。

### 4.4 新增非序列化运行时字段

| 字段名 | 类型 | 初值 | 用途 |
|---|---|---|---|
| `_phase` | `GameplayLabPhase` | `Idle` | 阻止重复提交和非法 Tick |
| `_sessionId` | `string` | Play 启动时生成 | 关联同一 Play 会话的日志 |
| `_cycleSequence` | `int` | `0` | 每个实验周期单调递增 |
| `_activeCycleId` | `string` | `null` | 关联 Submit 与后续 Tick |
| `_activeSampleId` | `string` | `null` | 记录当前待完成周期的外部样本 |
| `_activeInput` | `EmotionVector` | `null` | 锁定本周期已提交五维；Tick 日志不得重读 Inspector |
| `_submittedSampleIds` | `HashSet<string>` | 空集合 | 阻止同一 Play 会话重复提交同一外部样本 |
| `_submittedAbsoluteDay` | `int` | `-1` | 验证 Submit 不推进日期、Tick 恰好推进一天 |
| `_preWorldEventCount` | `int` | `0` | 当前操作开始前的世界事件基线 |
| `_preChronicleCount` | `int` | `0` | 当前操作开始前的世界志总数（pending + shown） |
| `_preEmotionHistoryCount` | `int` | `0` | 当前操作开始前的情绪历史基线 |
| `_prePermanentDamageCount` | `int` | `0` | 当前操作开始前的永久损伤基线 |
| `_prePermanentTerrainCount` | `int` | `0` | 当前操作开始前的永久地貌变化基线 |

以上字段只存在于内存，停止 Play 后丢弃。

### 4.5 命令方法

实现使用无参数、`private` 的 `[ContextMenu]` 方法，不新增 public/internal 方法签名。

| Context Menu | 方法语义 | 是否改变世界 |
|---|---|---|
| `Gameplay Lab/Submit External Vector` | 校验并提交当前 Inspector 五维 | 是 |
| `Gameplay Lab/Tick One Day` | 推进一个游戏日并保存 | 是 |
| `Gameplay Lab/Log Snapshot` | 输出当前状态 | 否 |

第一版不得加入：

- `Tick N Days`。
- 自动循环 Tick。
- `Update()` 自动提交。
- Reset、Undo、Restore、Delete。
- 修改日期或直接设置 `E_env`。
- 玩家运行时 UI。

### 4.6 所有权

| 数据/动作 | 单一写者 | 读取者 |
|---|---|---|
| Inspector 待提交五维 | 研究者手动录入 | `ManualGameplayLab` |
| `EmotionVector` 临时对象 | `ManualGameplayLab` 在 Submit 时构造 | `WorldManager.OnJournalSubmitted` |
| `E_env` | `EmotionInertiaSystem` | L2、L3、Debug Snapshot |
| `gameTime` | `WorldManager.WorldTick` | 全部世界系统、Debug Snapshot |
| 世界状态/实体/事件 | 现有 L2 单写者 | L3、Debug Snapshot |
| 世界存档 | `SaveSystem` | `WorldManager`、实验验证 |
| Debug Log | `ManualGameplayLab` | 研究者，不被游戏系统读取 |

### 4.7 下游影响

- [ ] `WorldInitializer.CreateNewWorld()` 无需更新。
- [ ] SaveSystem 序列化无需适配。
- [ ] EntityRegistry 无需更新。
- [ ] AnimalDriveSystem 无需适配。
- [ ] NarrativeRuleEngine 无需适配。
- [ ] BehaviorNarrator 无需适配。
- [ ] L3 无需新增绑定。
- [x] 需要新增实验控制器验证。
- [x] 需要清理场景中的自动实验干扰。
- [x] 需要更新当日更改文档。

### 4.8 兼容性结论

- 旧存档完全不变。
- 新存档格式完全不变。
- 停止 Play 后实验组件运行时状态丢弃，不影响世界状态。
- 删除或禁用实验组件后，正式世界链恢复原状。
- 不建立旧接口兼容层，因为没有修改现有接口。

---

## 5. 外部样本契约

外部 Sentiment Engine 与 Unity 之间不建立网络契约，只建立人工交接契约。

### 5.1 外部记录必须包含

| 列 | 格式 | 说明 |
|---|---|---|
| `sampleId` | `EXT-YYYYMMDD-NNN` | 唯一标识，Unity 日志只记录此值 |
| `rawText` | UTF-8 文本 | 只保存在研究者外部记录，不复制到 Unity Console |
| `V` | `[-1,1]` | valence |
| `A` | `[0,1]` | arousal |
| `T` | `[0,1]` | temporality |
| `S` | `[0,1]` | sociality |
| `C` | `[0,1]` | certainty |
| `engineVersion` | 外部字符串 | 供实验复现，不传入 Unity |
| `analyzedAtUtc` | ISO-8601 | 供实验复现，不传入 Unity |

### 5.2 Unity 接收范围

Unity 只接收：

```text
sampleId + V + A + T + S + C
```

Unity 不接收：

- 原始文本。
- 分词、标签、概率分布或模型解释。
- 世界状态建议。
- 天气、动物、植物或事件目标。
- “应该产生什么结果”的外部指令。

### 5.3 录入规则

1. 研究者先在外部记录中锁定样本。
2. 研究者把相同 `sampleId` 和五维录入 Inspector。
3. 研究者逐项复核后才执行 Submit。
4. Submit 成功后不得修改 Inspector 值来解释已经发生的周期。
5. 发现录入错误时，该周期标为无效；不得通过反向输入或重置世界来修正。

`sampleId` 必须严格匹配正则 `^EXT-[0-9]{8}-[0-9]{3}$`。它只能是匿名实验编号，不得包含原文片段、参与者姓名、邮箱、设备标识或其他个人信息。

控制器必须阻止同一 Play 会话内重复提交相同 `sampleId`。跨 Play、跨设备和跨研究批次的唯一性由外部实验台账保证；Gameplay Lab 不把 sampleId 写入世界存档。

---

## 6. 控制器状态机

```text
                    Submit valid
        ┌────────────────────────────────┐
        │                                ▼
     [Idle]                         [AwaitingTick]
        ▲                                │
        │                                │ Tick One Day valid
        └────────────────────────────────┘

     [Idle] -- Tick One Day --> [Idle]
              autonomous cycle

     any phase -- invariant failure --> [Invalid]
     [Invalid] 只允许 Log Snapshot；停止本次 Play 后处理
```

### 6.1 Idle

允许：

- Submit External Vector，开始输入周期。
- Tick One Day，执行无输入的自主周期。
- Log Snapshot。

### 6.2 AwaitingTick

表示一个外部样本已成功提交，但尚未完成下一日自主 Tick。

允许：

- Tick One Day，完成该输入周期。
- Log Snapshot。

不得：

- 再次 Submit。
- 覆盖 `_activeSampleId`。
- 自动推进多天。

### 6.3 Invalid

以下情况进入 Invalid：

- Submit 前后游戏绝对日不一致，说明发生了未受控 catch-up。
- Submit 后 `emotionHistory` 没有恰好增加 1。
- Tick 后日期没有恰好增加 1。
- Tick 后 `emotionHistory` 发生变化。
- WorldManager 或必要世界数据为空。
- Submit 或 Tick 保存抛出异常。

Invalid 后：

- 不得继续 Submit 或 Tick。
- 必须保留错误日志。
- 必须停止 Play。
- 必须把该周期标记为无效。
- 不得在运行中的世界上执行反向修复。

---

## 7. 标准输入周期

一个标准周期固定执行以下步骤。

### 7.1 周期前提

- `ManualEmotionInjector` 已禁用。
- 所有 `WorldSimulationTester.runOnStart` 为 false。
- `ManualGameplayLab` 处于 Idle。
- WorldManager 已完成 Awake/Start。
- 基线存档的墙钟锚点属于当天，预期 `WallClockDeltaDays()==0`。
- Game View 对玩家可见，Inspector 和 Console 只对研究者可见。
- 外部样本已锁定并完成录入复核。

### 7.2 Submit 阶段

```text
T0  Validate sampleId and five dimensions; failure only logs SUBMIT_REJECTED
T1  Allocate cycleId and capture PRE_SUBMIT counters and absolute day
T2  Log PRE_SUBMIT
T3  Call WorldManager.OnJournalSubmitted(EmotionVector)
T4  Verify game day unchanged
T5  Verify emotionHistory delta == +1
T6  Add sampleId to session dedupe set; lock active sample/input; enter AwaitingTick
T7  Log POST_SUBMIT
```

玩家观察窗口：

- Submit 后至少等待 5 个真实秒，不操作 Tick。
- 只观察当天响应，不向玩家解释输入值。
- 记录玩家是否把变化理解成即时控制。

### 7.3 Tick 阶段

```text
T8   Capture fresh PRE_TICK counters and absolute day; Log PRE_TICK
T9   Call WorldManager.WorldTick(1)
T10  Verify absolute day delta == +1 against PRE_TICK baseline
T11  Verify emotionHistory delta == 0 against PRE_TICK baseline
T12  Call SaveSystem.SaveWorldState(WorldManager.WorldSave)
T13  Enter Idle
T14  Log POST_TICK using locked active sample/input and PRE_TICK deltas
T15  Clear active sample/input
```

玩家观察窗口：

- Tick 后至少等待 5 个真实秒，让 L3 平滑表现追上世界状态。
- 研究者不指出变化位置。
- 记录玩家是否主动寻找标记、痕迹或信件。

### 7.4 周期语义

```text
外部向量 E_current
  → Submit：E_env 以 alpha=lerp(0.1,0.3,C) 靠近 E_current
  → 日期 D 响应式模拟
  → 玩家观察当天
  → Tick：日期 D+1，E_env 先以 0.05 向 Baseline 回落
  → 日期 D+1 自主模拟
  → 保存
  → 玩家观察次日
```

外部数据只在 Submit 时进入一次。Tick 不重新读取 Inspector，不重复使用外部向量，也不重新调用 Sentiment Engine。

---

## 8. 自主周期

自主周期用于验证世界不依赖最新日记也会继续运行。

前提：`ManualGameplayLab` 处于 Idle。

```text
Increment cycleSequence and allocate a new activeCycleId
→ set sample=NONE and keep activeInput=null
→ capture fresh PRE_TICK counters and absolute day
→ Log PRE_TICK
→ WorldTick(1)
→ verify day delta == +1 against PRE_TICK baseline
→ verify emotionHistory delta == 0 against PRE_TICK baseline
→ SaveWorldState
→ Log POST_TICK with the same cycleId and sample=NONE
→ clear activeCycleId; remain Idle
```

自主周期不得隐式复制上一次输入。`E_env` 只通过现有 `Relax(0.05)` 自主回落。

每个自主周期拥有独立 cycleId，并使 `_cycleSequence` 恰好增加 1；它不得复用上一个输入周期的 cycleId 或 Delta 基线。

---

## 9. Tick 与外部数据交互的强制规则

这是本文最重要的约束。

1. 一个外部样本在同一 Play 会话最多对应一次 Submit；跨会话唯一性由外部台账保证。
2. 一个 Submit 必须在下一次外部样本前完成恰好一次 Tick。
3. Submit 与对应 Tick 共用同一个 `cycleId`。
4. Tick 不读取当前 Inspector 五维；Inspector 值只是下一次 Submit 的待选输入。
5. Tick 不调用 `OnJournalSubmitted()`。
6. Submit 不调用 `WorldTick(1)`。
7. 不允许 `Submit → Submit → Tick`。
8. 不允许一个按钮执行 `Submit + Tick`。
9. 不允许一帧内连续执行多个 Tick。
10. 不允许用外部向量直接设置天气、环境、动物或视觉参数。
11. 无输入日必须使用自主周期，不得提交 Neutral 向量代替“无输入”。
12. 每个输入周期结束时世界日期必须精确增加一天，情绪历史必须精确增加一条。

### 9.1 为什么不提交 Neutral 表示无输入

Neutral 仍是一条真实输入，会：

- 让 `E_env` 朝 Neutral 靠近。
- 追加情绪历史。
- 运行响应式模拟。
- 写入 `[test]` JournalEntry。

无输入的正确语义是 `WorldTick(1)`，由 `Relax()` 让世界自主回落。

### 9.2 为什么不把 Submit 和 Tick 合成按钮

合并后无法区分：

- 玩家输入当天的响应。
- 世界经过一日后的自主变化。
- 哪一次模拟生成了事件或痕迹。
- 玩家是否把输入误读成即时天气按钮。

---

## 10. Debug Log 规范

Debug Log 是研究工具，不是玩家 UI，也不是生产遥测。

### 10.1 通用格式

每条日志必须是单行、稳定字段顺序、Invariant Culture 小数点格式：

```text
[GameplayLab:v1] phase=<PHASE> session=<SESSION> cycle=<CYCLE> sample=<SAMPLE> utc=<ISO8601> gameDate=<KEY> absDay=<INT> ...
```

格式要求：

- 浮点数统一 `F3`。
- 缺失值写 `NA`，不得省略必需键。
- `sampleId` 不得包含空格；空样本写 `NONE`。
- 日志不得换行。
- 日志不得使用本地化数字格式。
- 日志不得记录 rawText、summary、存档正文或完整路径。
- 成功使用 `Debug.Log`，拒绝使用 `Debug.LogWarning`，协议破坏使用 `Debug.LogError`。

本节只约束带 `[GameplayLab:v1]` 前缀的实验日志。现有 `SaveSystem` 会另外输出存档路径；导出实验数据时必须按前缀筛选，不把其他 Unity Console 行并入 Gameplay Lab 数据集。

### 10.2 Phase 枚举

| phase | 时点 | 是否改变世界 |
|---|---|---|
| `SESSION_READY` | 控制器初始化完成 | 否 |
| `SNAPSHOT` | 研究者主动读取 | 否 |
| `PRE_SUBMIT` | 提交调用前 | 否 |
| `POST_SUBMIT` | 提交和自动保存完成后 | 是 |
| `SUBMIT_REJECTED` | 输入校验失败 | 否 |
| `PRE_TICK` | Tick 调用前 | 否 |
| `POST_TICK` | Tick 和保存完成后 | 是 |
| `TICK_REJECTED` | 状态机拒绝 Tick | 否 |
| `PROTOCOL_INVALID` | 不变量破坏 | 可能已改变，周期作废 |

### 10.3 必需公共字段

所有非 REJECTED 日志必须包含：

```text
session cycle sample utc gameDate absDay labState
envV envA envT envS envC
rain wind fog stars soil vegetation decay creatures drought
lowlandWater lowlandSoil stoneWater stoneSoil
voleLocation voleBehavior
worldEvents pendingChronicles shownChronicles chronicleTotal emotionHistory
permanentDamages permanentTerrainChanges
```

字段来源：

| 字段组 | 来源 |
|---|---|
| `gameDate/absDay` | `WorldSave.gameTime` |
| `envV..envC` | `EmotionInertia.CurrentEEnv` |
| 环境九项 | `WorldManager.GetWorldState()` |
| 地点值 | `WorldSave.locations` |
| 田鼠位置 | `WorldSave.animals` 中 `speciesId=vole` 的 `location` |
| 田鼠行为 | 同一实体的 `behavior.drive`；实体、behavior 或 drive 为空时写 `NA` |
| 世界志计数 | `WorldSave.pendingChronicles` + `shownChronicles` |
| 其他计数 | `WorldSave` 的事件、情绪历史、植物永久损伤和地点永久变化列表 |

### 10.4 输入字段

`PRE_SUBMIT` 和 `POST_SUBMIT` 额外包含：

```text
inV inA inT inS inC
```

`PRE_TICK` 和 `POST_TICK` 不重新读取 Inspector。输入周期使用 `_activeInput` 中已经锁定的五维；自主周期写：

```text
inV=NA inA=NA inT=NA inS=NA inC=NA
```

### 10.5 Delta 字段

`POST_SUBMIT` 和 `POST_TICK` 必须包含：

```text
dayDelta historyDelta worldEventDelta chronicleDelta permanentDamageDelta permanentTerrainDelta
```

预期：

| phase | dayDelta | historyDelta |
|---|---:|---:|
| `POST_SUBMIT` | `0` | `1` |
| `POST_TICK` | `1` | `0` |

`chronicleDelta` 使用 `pending + shown` 的总数计算，打开信件导致 pending 移到 shown 不会产生负增量。但 catch-up 可以把多条 pending 压缩成一封缺席信，因此世界志不是 append-only；`chronicleDelta` 只作观察值，允许为负。`worldEventDelta`、`historyDelta`、`permanentDamageDelta` 和 `permanentTerrainDelta` 属于 append-only 检查，出现负数即协议无效。

### 10.6 拒绝与错误码

REJECTED/INVALID 日志必须包含：

```text
code=<ERROR_CODE> detail=<NO_SPACES_SHORT_DETAIL>
```

空或非法格式的 sampleId 不得原样进入日志，此时写 `sample=NONE`。格式合法但重复的匿名 sampleId 可以写入 `DUPLICATE_SAMPLE_ID` 日志。`detail` 只能使用固定短语，不得回显用户输入。

固定错误码：

| code | 含义 |
|---|---|
| `WORLD_NOT_READY` | WorldManager 或必要数据为空 |
| `INVALID_PHASE` | 当前状态不允许该命令 |
| `EMPTY_SAMPLE_ID` | Submit 没有 sampleId |
| `INVALID_SAMPLE_ID` | sampleId 不符合匿名编号格式 |
| `DUPLICATE_SAMPLE_ID` | 当前 Play 会话已提交过该 sampleId |
| `NON_FINITE_VALUE` | 五维包含 NaN/Infinity |
| `OUT_OF_RANGE` | 五维越界 |
| `CATCHUP_DETECTED` | Submit 前后日期变化 |
| `HISTORY_DELTA_MISMATCH` | 情绪历史增量不符 |
| `DAY_DELTA_MISMATCH` | Tick 日期增量不符 |
| `NEGATIVE_APPEND_DELTA` | 任一 append-only 计数下降 |
| `SUBMIT_PERSISTENCE_UNCERTAIN` | Submit 在世界存盘或 Journal 追加阶段抛错，磁盘可能部分成功 |
| `SAVE_FAILED` | Tick 后世界保存失败 |
| `UNEXPECTED_EXCEPTION` | 未分类异常 |

### 10.7 示例

```text
[GameplayLab:v1] phase=PRE_SUBMIT session=S-8F31 cycle=C-0003 sample=EXT-20260726-003 utc=2026-07-26T10:20:31.120Z gameDate=Y1-M2-D1 absDay=31 labState=Idle inV=-0.800 inA=0.700 inT=0.900 inS=0.200 inC=0.600 envV=0.000 envA=0.300 envT=1.000 envS=0.000 envC=0.500 rain=0.000 wind=0.300 fog=0.500 stars=0.000 soil=0.500 vegetation=0.500 decay=0.000 creatures=0.500 drought=0.000 lowlandWater=0.000 lowlandSoil=0.500 stoneWater=0.000 stoneSoil=0.500 voleLocation=lowland voleBehavior=Routine worldEvents=0 pendingChronicles=0 shownChronicles=0 chronicleTotal=0 emotionHistory=0 permanentDamages=0 permanentTerrainChanges=0
```

```text
[GameplayLab:v1] phase=POST_SUBMIT session=S-8F31 cycle=C-0003 sample=EXT-20260726-003 utc=2026-07-26T10:20:31.184Z gameDate=Y1-M2-D1 absDay=31 labState=AwaitingTick inV=-0.800 inA=0.700 inT=0.900 inS=0.200 inC=0.600 envV=-0.176 envA=0.388 envT=0.978 envS=0.044 envC=0.522 rain=0.176 wind=0.388 fog=0.478 stars=0.000 soil=0.451 vegetation=0.498 decay=0.000 creatures=0.482 drought=0.009 lowlandWater=0.053 lowlandSoil=0.451 stoneWater=0.018 stoneSoil=0.451 voleLocation=lowland voleBehavior=Forage worldEvents=0 pendingChronicles=1 shownChronicles=0 chronicleTotal=1 emotionHistory=1 permanentDamages=0 permanentTerrainChanges=0 dayDelta=0 historyDelta=1 worldEventDelta=0 chronicleDelta=1 permanentDamageDelta=0 permanentTerrainDelta=0
```

```text
[GameplayLab:v1] phase=POST_TICK session=S-8F31 cycle=C-0003 sample=EXT-20260726-003 utc=2026-07-26T10:21:04.502Z gameDate=Y1-M2-D2 absDay=32 labState=Idle inV=-0.800 inA=0.700 inT=0.900 inS=0.200 inC=0.600 envV=-0.167 envA=0.369 envT=0.929 envS=0.042 envC=0.496 rain=0.167 wind=0.369 fog=0.504 stars=0.000 soil=0.408 vegetation=0.493 decay=0.000 creatures=0.466 drought=0.018 lowlandWater=0.103 lowlandSoil=0.408 stoneWater=0.030 stoneSoil=0.408 voleLocation=lowland voleBehavior=Forage worldEvents=0 pendingChronicles=2 shownChronicles=0 chronicleTotal=2 emotionHistory=1 permanentDamages=0 permanentTerrainChanges=0 dayDelta=1 historyDelta=0 worldEventDelta=0 chronicleDelta=1 permanentDamageDelta=0 permanentTerrainDelta=0
```

示例数值只说明格式，不是玩法期望值，不得写入断言或调优目标。

---

## 11. 存档与实验基线

### 11.1 存档时点

| 动作 | 世界存档 | Journal Log |
|---|---|---|
| Submit | 由 `OnJournalSubmitted` 自动写 | 自动追加 `[test]` |
| Tick | 控制器在 `WorldTick(1)` 后立即写 | 不写 |
| Snapshot | 不写 | 不写 |
| Rejected | 不写 | 不写 |

`OnJournalSubmitted` 的世界存盘与 Journal 追加不是原子事务。如果调用抛出异常，可能出现 `world_state.json` 已更新但 `[test]` JournalEntry 尚未追加的部分成功状态。控制器必须记录 `SUBMIT_PERSISTENCE_UNCERTAIN`、进入 Invalid、停止本轮 Play，且不得自动重试同一 sampleId；自动重试可能制造重复的永久历史。

### 11.2 基线要求

- 实验控制器不提供 Reset 或 Restore。
- 每个比较组使用独立、一次性的实验存档副本。
- 基线配置在运行游戏之前由研究者准备，不对参与者暴露。
- 基线必须完成新世界 30 天预跑。
- 基线不得包含自动 tester 写入的 `[test]` 历史。
- 跨真实日期复用旧基线会触发 catch-up；若 Submit 日志出现 `dayDelta != 0`，样本立即作废。
- 不得修改一个参与者已经持续使用的世界来恢复基线。

---

## 12. 场景前置条件

在任何 Gameplay Lab 测试前必须确认：

- [ ] `ManualEmotionInjector` 已禁用。
- [ ] 所有 `WorldSimulationTester.runOnStart` 为 false。
- [ ] 没有 `SaveSystemTester`。
- [ ] 没有自动 Reset 菜单被执行。
- [ ] `ManualGameplayLab` 只有一个实例。
- [ ] `WorldManager` 只有一个实例。
- [ ] Game View 对参与者可见。
- [ ] Inspector、Console 和内部数值不对参与者可见。
- [ ] 痕迹点击与相机推近链正常。
- [ ] 世界志可由研究者验证，但参与者不接受机制讲解。

---

## 13. 实现验收

### 13.1 数据校验

- [ ] 空 `sampleId` 被拒绝且世界不变。
- [ ] 非法格式或包含空格的 `sampleId` 被拒绝且世界不变。
- [ ] 同一 Play 会话重复 `sampleId` 被拒绝且世界不变。
- [ ] V 越界被拒绝且世界不变。
- [ ] A/T/S/C 越界被拒绝且世界不变。
- [ ] NaN/Infinity 被拒绝且世界不变。
- [ ] 拒绝日志不包含 rawText。

### 13.2 状态机

- [ ] Idle 可以 Submit。
- [ ] AwaitingTick 再次 Submit 被拒绝。
- [ ] AwaitingTick 可以恰好 Tick 一天并回到 Idle。
- [ ] Idle Tick 产生 `sample=NONE` 的自主周期。
- [ ] Invalid 后 Submit/Tick 均被拒绝。
- [ ] Snapshot 在任何状态都不改变世界。

### 13.3 世界不变量

- [ ] POST_SUBMIT `dayDelta=0`。
- [ ] POST_SUBMIT `historyDelta=1`。
- [ ] POST_TICK `dayDelta=1`。
- [ ] POST_TICK `historyDelta=0`。
- [ ] worldEvents、永久变化和情绪历史计数不下降；chronicleDelta 只观察、不做 append-only 断言。
- [ ] Tick 后世界存档已更新。
- [ ] 外部向量没有直接写入天气或 L3。

### 13.4 日志格式

- [ ] 所有成功日志使用 `[GameplayLab:v1]`。
- [ ] 字段顺序稳定。
- [ ] 浮点值为 `F3` 和英文小数点。
- [ ] 每条日志单行。
- [ ] sampleId、sessionId、cycleId 可关联。
- [ ] 不记录 rawText、summary 或存档正文。

### 13.5 Unity 验证

- [ ] `dotnet build Assembly-CSharp.csproj --no-restore` 0 error。
- [ ] Unity 刷新和编译完成。
- [ ] Console 无新增 error。
- [ ] 连续完成 3 个输入周期，状态机不漂移。
- [ ] 完成 3 个自主周期，不产生 JournalEntry。
- [ ] 退出并重启后，最后一次 Tick 的日期、`currentEEnv`、实体、地点、事件和世界志等 `WorldSaveData` 字段保留。
- [ ] 明确记录当前限制：`DroughtDebt`、`DecayLevel`、全局 SoilMoisture 等 `WorldEnvironmentState` 运行时积分不在现有存档中，本提案不验收其跨重启连续性。

---

## 14. 实验执行模板

每个实验组使用以下顺序：

```text
1. 启动独立实验存档
2. Log Snapshot
3. 外部锁定 sampleId + 五维
4. Inspector 录入并双人/二次复核
5. Submit External Vector
6. 观察至少 5 秒并记录当天反应
7. Tick One Day
8. 观察至少 5 秒并记录次日反应
9. 重复下一样本，或执行 sample=NONE 的自主周期
10. 导出 Console 日志与外部观察表
```

每个参与者不得看到：

- 五维数值。
- Submit/Tick 操作。
- Console。
- 实验期望结果。
- 动物内部状态或位置字段。

---

## 15. 变更流程

后续开发必须按以下顺序：

```text
批准提案 20260726-01
→ 实现 ManualGameplayLab
→ 编译验证
→ 提交场景修改计划并批准
→ 禁用自动注入/自动测试并接线控制器
→ 执行本文第 13 节验收
→ 更新本文状态和当日更改文档
→ 才能开始玩法实验
```

以下变化必须回到本文重新提案：

- 新增或重命名实验字段。
- 增加批量 Tick、自动 Tick 或 Submit+Tick 合并命令。
- 修改输入与 Tick 的一对一关系。
- 修改 Debug Log schema 或必需字段。
- 修改现有 WorldManager 接口。
- 修改存档格式。
- 把实验控制器暴露为玩家功能。
- 接入 WSL 或网络传输。

---

## 16. 审批记录

| 提案 | 决策 | 日期 | 备注 |
|---|---|---|---|
| `20260726-01` | 待审批 | — | 批准后才允许实现控制器 |
