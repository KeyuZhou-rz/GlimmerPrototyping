# Glimmer Prototyping — 数据流与持久化说明

> 本文记录玩家输入、世界模拟、视觉呈现与磁盘持久化之间的完整数据流，以及测试链的运作方式。

---

## 1. 总体原则

- **世界状态独立存在**：`WorldSaveData` 是单一真相源，不因一次日记输入而被直接改写。
- **情绪有惯性**：环境情绪 `E_env` 以 `α = 0.1~0.3` 缓慢靠近玩家输入 `E_current`。
- **追加-only 日志**：`journal_log.json` 与 `worldEvents` 只增不减。
- **三层架构边界**：
  - L1 `SentimentEngine`：纯文本分析，无 Unity API、无世界状态。
  - L2 `WorldSimulator`：接收 `EmotionVector` 作为参数，写入 `_saveData`。
  - L3 `Visual`：只读世界状态，绑定 `E_env`，VFX 带平滑滞后。

---

## 2. 持久化文件与 SaveSystem

**文件位置**（运行时）：

```
Application.persistentDataPath/GlimmerDiary/
  world_state.json    # 世界存档根节点
  journal_log.json    # 日记原文 + 情绪向量的追加日志
```

**唯一 I/O 入口**：`Assets/GlimmerDiary/Scripts/Utils/SaveSystem.cs`

```csharp
SaveSystem.SaveWorldState(_saveData);       // 覆盖写 world_state.json
SaveSystem.AppendJournalEntry(entry);       // 加载 journal_log.json → 加一条 → 整写回
SaveSystem.LoadWorldState();                // 读 world_state.json
```

- 格式：Unity `JsonUtility`，`[Serializable]` 普通 C# 类。
- 无加密、无压缩、无二进制。

---

## 3. 存档根节点 `WorldSaveData`

`Assets/GlimmerDiary/Scripts/Data/WorldStateData.cs`

| 字段 | 类型 | 说明 |
|---|---|---|
| `gameTime` | `GameDateTime` | 世界历（Y/M/D，30 天/月，12 月/年） |
| `lastTickRealTime` | `string` | 真实时间锚点（ISO-8601）。空 = 全新世界 |
| `currentEEnv` | `EmotionVector` | 当前环境情绪（V/A/T/S/C） |
| `emotionHistory` | `List<EnvEmotionSnapshot>` | 每次提交后的情绪快照 |
| `animals` | `List<AnimalEntity>` | 动物实体 + 历史 |
| `plants` | `List<PlantEntity>` | 植物实体 + 历史 |
| `locations` | `List<LocationEntity>` | 区域实体 + 历史 |
| `worldEvents` | `List<WorldEvent>` | **追加-only** 永久事件日志 |
| `pendingChronicles` | `List<WorldChronicleEntry>` | 待展示给玩家的叙事条目 |
| `shownChronicles` | `List<WorldChronicleEntry>` | 已展示叙事条目 |

### 追加-only 事件示例

`Assets/GlimmerDiary/Scripts/Data/AnimalStateData.cs`

```csharp
[Serializable]
public class WorldEvent
{
    public string type;       // TreeBranchBroke / AnimalArrived / QuietConvergence ...
    public string sourceId;
    public string targetId;
    public string gameDate;   // "Y1-M9-D1"
    public string payload;    // e.g. branch direction "E-2"
}
```

`pendingChronicles` 仅在“追赶期”批量丢弃，避免给玩家灌入大量过时信件；`worldEvents` 永不删除。

---

## 4. 运行时数据流：新建世界 30 天预跑

`Assets/GlimmerDiary/Scripts/WorldManager.cs`

### 4.1 启动时 `Awake()`

```csharp
_saveData = SaveSystem.LoadWorldState() ?? WorldInitializer.CreateNewWorld();
EmotionInertia.Restore(_saveData.currentEEnv, _saveData.emotionHistory);
```

若 `lastTickRealTime` 为空，说明是全新世界：

```csharp
WorldTick(30, isCatchUp: true);          // 30 天中性预跑
SaveSystem.SaveWorldState(_saveData);
```

### 4.2 `WorldTick(deltaDays, isCatchUp)`

```csharp
for (int d = 0; d < deltaDays; d++)
{
    AdvanceCalendar();                              // gameTime +1
    if (d >= simulateFrom) SimulatePass();          // 软帽：>90 天只模拟最后 90 天
}
```

30 天预跑因 `30 ≤ 90`，每天都会走 `SimulatePass()`。

### 4.3 `AdvanceCalendar()`

```csharp
_saveData.gameTime.Advance(1);
EmotionInertia.Relax(alpha: 0.05f);         // E_env 向基线缓慢漂移
NaturalRhythm.Tick(_saveData.gameTime);     // 季节、光强、日进度
```

### 4.4 `SimulatePass()` —— 核心模拟的一天

`WorldManager.cs` 中的执行顺序：

```csharp
private void SimulatePass()
{
    // 0. 翻译层：E_env + 节律 → 无状态信号 1/2/3/7
    var signals = Translation.Translate(EmotionInertia.CurrentEEnv, NaturalRhythm.State);

    // 1. 环境系统：有状态积分
    Environment.UpdateFromEEnv(EmotionInertia.CurrentEEnv, signals);

    // 2. 降雨 → 各区域水位 / 土壤
    PropagateEnvironmentToLocations();

    // 3. 植被
    _vegetationSystem.Tick(_saveData.gameTime);

    // 4. 动物驱动（快照机制：读上一天快照，避免顺序依赖）
    _driveSystem.SetEnvironment(Environment.State, NaturalRhythm.State);
    _driveSystem.Tick(_saveData.gameTime);

    // 5. 涌现时刻
    _emergentDetector.Detect(_saveData.gameTime);

    // 6. 叙事
    _narrator.SetEnvironment(Environment.State, NaturalRhythm.State);
    _narrator.Narrate(_saveData.gameTime);

    // 7. 规则引擎
    _ruleEngine.SetEnvironment(Environment.State, NaturalRhythm.State);
    _ruleEngine.Evaluate(_allRules, _saveData.gameTime);

    // 8. 实体关系
    _relationSystem.SetEnvironment(Environment.State, NaturalRhythm.State);
    _relationSystem.Evaluate(_allRelations, _saveData.gameTime);
}
```

---

## 5. 玩家提交日记时的完整链路

`WorldManager.OnJournalSubmitted(JournalEntry entry)`

```csharp
// 1) 处理真实时间缺席
int delta = WallClockDeltaDays();
if (delta > 0) WorldTick(delta, isCatchUp: delta > 1);

// 2) 注入情绪（E_env 靠近输入，而非直接设置）
InjectEmotion(entry);

// 3) 再模拟一天响应
SimulatePass();

// 4) 持久化
SaveSystem.SaveWorldState(_saveData);
SaveSystem.AppendJournalEntry(entry);
```

### 5.1 L1 → L2 → L3 完整映射

```
[玩家输入] 日记文本
    ↓
[L1] EmotionAnalyzer.Analyze(text) → (valence, arousal)
    文件：Assets/Emotion_engine_development/EmotionAnalyzer.cs
    ↓
[桥接] JournalEntry { entryId, realTimestamp, rawText, emotion }
    文件：Assets/GlimmerDiary/Scripts/Data/EmotionData.cs
    ↓
[L2] WorldManager.OnJournalSubmitted()
    文件：Assets/GlimmerDiary/Scripts/WorldManager.cs
    ↓
[L2] EmotionInertiaSystem.Update(eCurrent) → E_env 以 α=0.1~0.3 靠近输入
    文件：Assets/GlimmerDiary/Scripts/Core/EmotionInertiaSystem.cs
    ↓
[L2] TranslationLayer.Translate(E_env, Rhythm) → 7 个世界信号
    文件：Assets/GlimmerDiary/Scripts/Core/TranslationLayer.cs
    ↓
[L2] WorldEnvironmentSystem.UpdateFromEEnv() → Rainfall / WindSpeed / FogDensity / StarVisibility / SoilMoisture / VegetationDensity / DecayLevel
    文件：Assets/GlimmerDiary/Scripts/Core/WorldEnvironmentSystem.cs
    ↓
[L2] PropagateEnvironmentToLocations() → 各区域 waterLevel / soilMoisture
    文件：Assets/GlimmerDiary/Scripts/WorldManager.cs
    ↓
[L2] AnimalDriveSystem / Narrator / RuleEngine / RelationSystem → worldEvents + pendingChronicles
    ↓
[持久化] world_state.json + journal_log.json
    文件：Assets/GlimmerDiary/Scripts/Utils/SaveSystem.cs
    ↓
[L3] WorldAtmosphereBinder.Update() 读世界状态 → 平滑 → LateUpdate() 写天气控制器
    文件：Assets/Script/WorldAtmosphereBinder.cs
    ↓
[L3] EmotionWeatherController / WorldTraceBinder → 雨、风、雾、雷、星空、天空色、水面、地面痕迹
    文件：Assets/Script/EmotionWeatherController.cs
    文件：Assets/Script/WorldTraceBinder.cs
```

### 5.2 情绪惯性公式

`Assets/GlimmerDiary/Scripts/Core/EmotionInertiaSystem.cs`

```csharp
float alpha = Mathf.Lerp(0.1f, 0.3f, eCurrent.C);
CurrentEEnv.V = alpha * eCurrent.V + (1 - alpha) * CurrentEEnv.V;
// ... A, T, S, C 同理
```

确定性 `C` 越高，世界对这次输入反应越快。

---

## 6. 翻译层信号

`Assets/GlimmerDiary/Scripts/Core/TranslationLayer.cs`

| 信号 | 来源 | 语义 |
|---|---|---|
| 1 Wetness | `E_env.V` 曲线 | 负向 valence → 大雨；正向 → 无雨 |
| 2 Agitation | `E_env.A` 曲线 | 高 arousal → 大风 |
| 3 Dimness | `E_env.C` 曲线 | 低 certainty → 大雾 |
| 7 Firmament | `E_env.T × (1 - wetness) × (1 - lightIntensity)` | 星空可见度 |

信号 1/2/3/7 为**无状态**；信号 4/5/6 当前仍留在消费端做有状态积分。

---

## 7. 测试链

### 7.1 Edit-Mode 烟雾测试

`Assets/GlimmerDiary/Scripts/Editor/AnimalDriveSmokeTest.cs`

Unity 菜单 `GlimmerDiary/*` 下 12 条测试：

- `Test Anxiety Chain`
- `Test Weaver Chain`
- `Test Full Pipeline (Flood)`
- `Test Full Pipeline (Bird Arrival)`
- `Test Long Run (Health)`
- `Test Silent Advance (30d)`
- `Test Quiet Convergence` 系列 ×3
- `Test Vegetation Pest`
- `Test Soil Moisture`
- `Test Translation Layer`

`BuildPipeline()` 在内存中重建整条 L2 管线，提供三种操作：

```csharp
submit();      // 走完整一天
inject(e);     // 注入情绪 + 模拟，但不推进日历
worldTick(n);  // 连续 N 天自主推进
```

Edit-Mode 测试**不写磁盘**。

### 7.2 Play-Mode 场景测试

`Assets/GlimmerDiary/Scripts/Utils/WorldSimulationTester.cs`

Inspector 可选 7 种 scenario：

- `FloodAndVoleRelocation`
- `BaobabBranchBreak`
- `MigratoryBirdArrival`
- `LongTermDecay`
- `FullWeekCycle`
- `EmergentAnxietyChain`
- `LongChainForLetter`

关键方法：

```csharp
SubmitEmotion(V, A, C);   // 调 WorldManager.Instance.OnJournalSubmitted()
ResetWorld();             // 用 WorldInitializer.CreateNewWorld() 重新初始化
```

### 7.3 30 天预跑本身即测试链

每次新存档的 `WorldManager.Awake()` 都会自动执行：

```csharp
WorldTick(30, isCatchUp: true);
SaveSystem.SaveWorldState(_saveData);
```

它保证玩家进入的永远是一个“已经有 30 天历史的世界”，而不是空白状态。

---

## 8. 持久化时点与内存/磁盘分工

### 8.1 何时落盘

1. **新世界上一次存盘**：30 天预跑结束后 `SaveWorldState`
2. **玩家每次提交日记后**：`SaveWorldState` + `AppendJournalEntry`
3. **真实时间追赶后**：启动或回到游戏时若 `WallClockDeltaDays() > 0`，先 `WorldTick(catch-up)` 再存盘

### 8.2 存盘的

- `WorldSaveData` 及其所有嵌套实体、历史、永久损伤/地形变更
- `emotionHistory` 与 `currentEEnv`
- `worldEvents` 与 `pending/shownChronicles`
- `journal_log.json`

### 8.3 不存盘、运行时再重建的

| 运行时系统 | 重建来源 |
|---|---|
| `WorldEnvironmentSystem` | 从 `gameTime` + `E_env` + signals 重新积分 |
| `NaturalRhythmSystem` | 从 `gameTime` 和真实时间推导 |
| `AnimalDriveSystem` / `BehaviorNarrator` / `NarrativeRuleEngine` / `EntityRelationSystem` | `Awake()` 时从 `_saveData` 重建 |
| `EntityRegistry` | `Awake()` 时从列表重建字典 |

---

## 9. 版本迁移

目前没有正式版本号字段。兼容性靠“懒初始化”兜底：

`Assets/GlimmerDiary/Scripts/Core/AnimalDriveSystem.cs`

```csharp
private void EnsureInitialized()
{
    foreach (var a in _save.animals)
    {
        if (a.internalState == null || !a.internalState.initialized)
            a.internalState = InitFor(a.speciesId);
        a.behavior ??= new BehaviorOutput { zone = a.location };
    }
    // ...
}
```

旧存档缺少的字段会在加载后自动补默认值。

---

## 10. 关键文件速查

| 文件 | 职责 |
|---|---|
| `Assets/GlimmerDiary/Scripts/WorldManager.cs` | L2 编排器：预跑、追赶、日记提交、模拟 |
| `Assets/GlimmerDiary/Scripts/Utils/SaveSystem.cs` | 唯一 JSON I/O 入口 |
| `Assets/GlimmerDiary/Scripts/Data/WorldStateData.cs` | `WorldSaveData`、`GameDateTime`、`WorldChronicleEntry` |
| `Assets/GlimmerDiary/Scripts/Data/EmotionData.cs` | `EmotionVector`、`JournalEntry`、`EnvEmotionSnapshot` |
| `Assets/GlimmerDiary/Scripts/Data/SaveData.cs` | `JournalLog` |
| `Assets/GlimmerDiary/Scripts/Data/WorldEntityData.cs` | `AnimalEntity`、`PlantEntity`、`LocationEntity` |
| `Assets/GlimmerDiary/Scripts/Data/AnimalStateData.cs` | `WorldEvent`、`BehaviorOutput`、`AnimalInternalState` |
| `Assets/GlimmerDiary/Scripts/Core/EmotionInertiaSystem.cs` | E_env 惯性、历史、Relax |
| `Assets/GlimmerDiary/Scripts/Core/TranslationLayer.cs` | E_env → 7 信号 |
| `Assets/GlimmerDiary/Scripts/Core/WorldEnvironmentSystem.cs` | 信号 + E_env → 天气状态 |
| `Assets/GlimmerDiary/Scripts/Core/NaturalRhythmSystem.cs` | 季节、光强、日进度 |
| `Assets/GlimmerDiary/Scripts/Core/WorldInitializer.cs` | 新世界工厂 |
| `Assets/Script/WorldAtmosphereBinder.cs` | L3：世界状态 → 平滑视觉目标 |
| `Assets/Script/EmotionWeatherController.cs` | L3：粒子、风、雾、雷、天空 |
| `Assets/Script/WorldTraceBinder.cs` | L3：地面痕迹、草被踩踏 |
| `Assets/Emotion_engine_development/EmotionAnalyzer.cs` | L1：文本 → (V, A) |
| `Assets/GlimmerDiary/Scripts/Editor/AnimalDriveSmokeTest.cs` | Edit-Mode 测试链 |
| `Assets/GlimmerDiary/Scripts/Utils/WorldSimulationTester.cs` | Play-Mode 测试场景 |
