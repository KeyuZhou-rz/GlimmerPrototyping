# Gameplay Lab Protocol

> 文件名按当前项目约定保留为 `GameplayLabLrotocol.md`。
> 本文是手动五维玩法实验的唯一实施契约。代码、场景接线、Debug Log 和测试步骤必须遵守本文；改变语义前先修订并审批，不得在实现中临时发明规则。

## 0. 状态

| 项目 | 状态 |
|---|---|
| 协议版本 | `gameplay-lab/2` |
| 当前提案 | `20260726-03`，已批准 |
| 控制器 | 已实现，待场景接线与 Play 验证 |
| 网络接入 | 不在本轮范围 |
| 存档格式变更 | 无 |
| L2 接口变更 | 无 |

审批历史：

| 提案 | 决策 | 说明 |
|---|---|---|
| `20260726-01` | 已取代 | 原 Submit 与 Tick 分离、含 sampleId |
| `20260726-02` | 已取代 | 移除 sampleId，但仍分离 Submit 与 Tick |
| `20260726-03` | 已批准 | Inspector 五维与单日 Tick 合并为一个研究者命令 |

---

## 1. 实验目的

外部 Sentiment Engine 把文本解析成五维，研究者只把 `V/A/T/S/C` 手动录入 Unity。文本本身不影响世界，Unity 使用固定占位符记录实验日记。

本轮验证：

1. 五维经情绪惯性进入世界后，是否产生值得观察的变化。
2. 每推进一天，变化是否留下玩家能发现的环境或实体痕迹。
3. 玩家能否不看内部数值，从场景和世界志理解至少三环因果。
4. 玩家是否主动想知道下一天会发生什么。

本轮不验证：

- WSL、HTTP 或其他网络传输。
- 正式日记输入 UI。
- 原始文本存储质量。
- 真实等待节奏和长期留存。
- 新生态机制或新跨系统关系。

---

## 2. 数据提案 20260726-03

### 2.1 新增组件

| 文件 | 类型 | 用途 |
|---|---|---|
| `Assets/GlimmerDiary/Scripts/Utils/ManualGameplayLab.cs` | `ManualGameplayLab : MonoBehaviour` | 手动五维、单日 Tick、存档和研究日志 |

### 2.2 序列化字段

| 字段 | 类型 | 默认值 | 范围 | 持久化到世界存档 |
|---|---|---:|---|---|
| `valence` | `float` | `0` | `[-1,1]` | 否 |
| `arousal` | `float` | `0.3` | `[0,1]` | 否 |
| `temporality` | `float` | `1` | `[0,1]` | 否 |
| `sociality` | `float` | `0` | `[0,1]` | 否 |
| `certainty` | `float` | `0.5` | `[0,1]` | 否 |

### 2.3 运行时状态

| 字段 | 初值 | 用途 |
|---|---|---|
| `_state` | `Ready` | 协议错误后进入 `Invalid`，阻止继续写世界 |
| `_sessionId` | Play 时生成 | 关联一次 Play 会话 |
| `_cycleSequence` | `0` | 为每次 Tick 生成递增 cycleId |

不再存在 `sampleId`、`AwaitingTick`、`activeInput` 或样本去重。每个周期的输入、日期和计数都使用方法局部变量。

### 2.4 命令

| Context Menu | 语义 |
|---|---|
| `Gameplay Lab/Tick One Day With Current Vector` | 读取当前 Inspector 五维、注入、推进一天、验证、保存并写占位 JournalEntry |
| `Gameplay Lab/Tick One Day Without Input` | 不读取五维，只让世界自主推进一天并保存 |
| `Gameplay Lab/Log Snapshot` | 只输出当前状态，不改变世界 |

不得增加：

- `Update()` 自动输入或自动 Tick。
- `Tick N Days`。
- Reset、Undo、Restore、Delete。
- 玩家可见的五维、Tick 或状态 UI。
- 直接设置 `E_env`、天气、实体或 L3 参数。

---

## 3. 所有权和边界

```text
外部文本
  → 外部 Sentiment Engine
  → V/A/T/S/C
  → 研究者录入 Inspector
  → ManualGameplayLab 构造局部 EmotionVector
  → WorldManager.InjectEmotion
  → WorldManager.WorldTick(1)
  → SaveSystem
  → L3 只读世界状态
```

| 数据/动作 | 单一写者 | 读取者 |
|---|---|---|
| Inspector 五维 | 研究者 | `ManualGameplayLab` |
| 临时 `EmotionVector` | `ManualGameplayLab` 当前方法 | `EmotionInertiaSystem` |
| `E_env` | `EmotionInertiaSystem` | L2、L3、Debug Log |
| `gameTime` | `WorldManager.WorldTick` | 世界系统、Debug Log |
| 世界状态/实体/事件 | 现有 L2 单写者 | L3、Debug Log |
| 世界与 Journal 文件 | `SaveSystem` | WorldManager、实验验证 |

控制器不直接写任何世界字段，也不调用 L3。

---

## 4. 单日输入周期

### 4.1 目标语义

研究者完成一次操作：

```text
调整 Inspector 五维
→ 点击 Tick One Day With Current Vector
→ 游戏日期 +1
→ 只运行一次 SimulatePass
→ 保存
```

### 4.2 强制顺序

```text
1. 确认 Play Mode、state=Ready、WorldManager 数据完整
2. 校验五维有限且在合法范围
3. cycleSequence +1，生成 cycleId
4. 捕获 PRE_TICK 日期和计数
5. 输出 PRE_TICK
6. 构造局部 EmotionVector
7. 构造 JournalEntry：
     entryId = Guid
     realTimestamp = 当前 ISO-8601
     rawText = "[manual gameplay lab]"
     emotion = 当前局部向量
8. WorldManager.InjectEmotion(entry)
9. WorldManager.WorldTick(1)
10. 验证日期 +1、emotionHistory +1、append-only 计数未下降
11. SaveSystem.SaveWorldState
12. SaveSystem.AppendJournalEntry
13. 输出 POST_TICK
```

### 4.3 为什么不使用 OnJournalSubmitted

`OnJournalSubmitted()` 本身会运行一次响应式 `SimulatePass()`。如果随后再执行 `WorldTick(1)`，一次按钮会模拟两遍世界。

Lab 使用：

```text
InjectEmotion
→ WorldTick(1)
```

这样一个按钮只推进一天并只运行一次 `SimulatePass()`。

`WorldTick(1)` 会先执行 `EmotionInertia.Relax(0.05)`，所以新输入在当天模拟前会向静息基线回落 5%。这是 `20260726-03` 明确接受的实验语义。

### 4.4 输入生命周期

Inspector 五维只是待使用参数。点击时复制为局部 `EmotionVector`，方法结束后不保留。

```text
Tick A 读取当时的 Inspector 值
→ Tick A 完成
→ 修改 Inspector
→ Tick B 读取新值
```

Tick 不读取文本，不调用外部 Sentiment Engine，也不直接控制任何世界参数。

---

## 5. 自主周期

无输入日使用独立命令：

```text
1. 确认 Play Mode、state=Ready、WorldManager 数据完整
2. cycleSequence +1，生成 cycleId
3. 捕获 PRE_TICK 日期和计数
4. 输出 PRE_TICK，输入五维写 NA
5. WorldManager.WorldTick(1)
6. 验证日期 +1、emotionHistory +0、append-only 计数未下降
7. SaveSystem.SaveWorldState
8. 输出 POST_TICK
```

不得提交 Neutral 代替无输入。Neutral 仍会新增一条情绪历史；自主周期只依靠现有 `Relax(0.05)`。

---

## 6. 状态机

```text
[Ready] -- valid vector Tick --> [Ready]
[Ready] -- autonomous Tick --> [Ready]
[Ready] -- protocol/persistence failure --> [Invalid]
[Invalid] -- any mutating command --> rejected
```

`Log Snapshot` 在 Invalid 状态仍可使用。

协议失败可能发生在世界已经改变之后，因此控制器不得尝试回滚。研究者必须停止本次 Play，并将该周期标为无效。

---

## 7. Debug Log v2

### 7.1 格式

```text
[GameplayLab:v2] phase=<PHASE> session=<SESSION> cycle=<CYCLE> utc=<ISO8601> gameDate=<KEY> absDay=<INT> ...
```

要求：

- 单行、稳定字段顺序。
- 浮点统一 Invariant Culture `F3`。
- 缺失值写 `NA`。
- 不记录外部原文、summary 或完整存档正文。
- 导出时只收集 `[GameplayLab:v2]` 前缀行。

### 7.2 Phase

| phase | 时点 |
|---|---|
| `SNAPSHOT` | 只读快照 |
| `PRE_TICK` | 世界变化前 |
| `POST_TICK` | 单日 Tick、验证与存盘后 |
| `TICK_REJECTED` | 命令未改变世界 |
| `SNAPSHOT_REJECTED` | 快照不可用 |
| `PROTOCOL_INVALID` | 世界可能已部分改变，周期作废 |

### 7.3 必需字段

```text
session cycle utc gameDate absDay labState
inV inA inT inS inC
envV envA envT envS envC
rain wind fog stars soil vegetation decay creatures drought
lowlandWater lowlandSoil stoneWater stoneSoil
voleLocation voleBehavior
worldEvents pendingChronicles shownChronicles chronicleTotal emotionHistory
permanentDamages permanentTerrainChanges
```

`POST_TICK` 追加：

```text
dayDelta historyDelta worldEventDelta chronicleDelta
permanentDamageDelta permanentTerrainDelta
```

输入周期预期：

```text
dayDelta=1 historyDelta=1
```

自主周期预期：

```text
dayDelta=1 historyDelta=0
inV=NA inA=NA inT=NA inS=NA inC=NA
```

`chronicleDelta` 只作观察值。世界志可能被聚合，不作为 append-only 断言。

### 7.4 错误码

| code | 含义 |
|---|---|
| `NOT_PLAYING` | 未进入 Play Mode |
| `SESSION_NOT_READY` | 组件尚未完成 Awake |
| `INVALID_STATE` | 当前 Play 已因协议错误失效 |
| `WORLD_NOT_READY` | WorldManager 或必要集合为空 |
| `NON_FINITE_VALUE` | 五维含 NaN/Infinity |
| `OUT_OF_RANGE` | 五维越界 |
| `DAY_DELTA_MISMATCH` | Tick 未恰好推进一天 |
| `HISTORY_DELTA_MISMATCH` | 情绪历史增量不符 |
| `NEGATIVE_APPEND_DELTA` | 事件或永久记录计数下降 |
| `CYCLE_EXECUTION_FAILED` | 注入或模拟抛出异常 |
| `CYCLE_PERSISTENCE_UNCERTAIN` | 输入周期的双文件写入失败，磁盘可能部分成功 |
| `SAVE_FAILED` | 自主周期世界保存失败 |

---

## 8. 已知限制

### 8.1 双文件非原子

输入周期先写 `world_state.json`，再追加 `journal_log.json`。如果第二次写入失败，世界可能已保存但 Journal 缺失。控制器必须进入 Invalid、禁止重试，不得假装事务成功。

### 8.2 运行时环境积分未持久化

`DroughtDebt`、`DecayLevel`、全局 SoilMoisture 等位于 `WorldEnvironmentState`，不在当前 `WorldSaveData` 中。POST_TICK 日志记录的是当时运行状态，不代表重启后全部恢复。

本提案不修改存档格式。跨重启只验收现有 `WorldSaveData`：日期、`currentEEnv`、实体、地点、事件、世界志和永久记录。

---

## 9. 场景前置条件

- [ ] `ManualEmotionInjector` 已禁用。
- [ ] 所有 `WorldSimulationTester.runOnStart` 为 false。
- [ ] 没有 `SaveSystemTester`。
- [ ] 场景内只有一个 `ManualGameplayLab`。
- [ ] Game View 对参与者可见，Inspector 和 Console 只对研究者可见。
- [ ] 痕迹点击、相机推近和世界志可用。

场景修改需单独提交计划并批准；控制器代码完成不等于已接线。

---

## 10. 实现验收

### 10.1 输入周期

- [ ] 一次点击日期恰好 +1。
- [ ] 一次点击只运行一个 WorldTick/SimulatePass。
- [ ] emotionHistory 恰好 +1。
- [ ] Journal 追加一条 `[manual gameplay lab]`。
- [ ] POST_TICK 使用点击时的五维。

### 10.2 自主周期

- [ ] 日期恰好 +1。
- [ ] emotionHistory 不增加。
- [ ] Journal 不增加。
- [ ] 输入字段全部为 NA。

### 10.3 安全与日志

- [ ] 越界或非有限输入不改变世界。
- [ ] Invalid 后两个 Tick 命令均被拒绝。
- [ ] Snapshot 不改变世界。
- [ ] 日志不含原始文本。
- [ ] append-only 计数不下降。

### 10.4 工具验证

- [ ] `dotnet build Assembly-CSharp.csproj --no-restore` 0 error。
- [ ] Unity 刷新编译完成，Console 无新增 error。
- [ ] 连续 3 个输入周期通过。
- [ ] 连续 3 个自主周期通过。
- [ ] Tick 后退出重启，现有 `WorldSaveData` 字段保留。

---

## 11. 变更流程

以下变化必须重新提案：

- 增加批量或自动 Tick。
- 改变“一次按钮 = 一天 = 一次 SimulatePass”。
- 改用 `OnJournalSubmitted()`。
- 修改 Debug Log v2 必需字段。
- 修改 WorldManager、EmotionVector 或存档格式。
- 把实验控制器暴露为玩家功能。
- 接入 WSL 或网络。
