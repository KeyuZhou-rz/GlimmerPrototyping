# Gameplay Lab Protocol

> 手动五维玩法实验的实现规范。当前原则：只保留推进世界所必需的字段和调用，不建立实验框架。

## 1. 当前实现

| 项目 | 结论 |
|---|---|
| 协议版本 | `gameplay-lab/3` |
| 当前脚本 | `Assets/GlimmerDiary/Scripts/Utils/ManualTicking.cs` |
| 当前类型 | `GlimmerDiary.Utils.ManualTicking` |
| 当前方案 | 设计者直接批准的最小实现 `20260726-04` |
| 网络 | 不在本轮范围 |
| 存档格式 | 不变 |

`ManualGameplayLab` 的 session、cycle、状态机、结构化日志和计数校验方案已撤销，不再作为开发依据。

## 2. Inspector 字段

控制器只提供五个字段：

| 字段 | 范围 | 默认值 |
|---|---:|---:|
| `valence` | `[-1,1]` | `0` |
| `arousal` | `[0,1]` | `0.3` |
| `temporality` | `[0,1]` | `1` |
| `sociality` | `[0,1]` | `0` |
| `certainty` | `[0,1]` | `0.5` |

不增加 sampleId、sessionId、cycleId、输入文本、状态枚举或调试统计字段。

## 3. 带输入的单日 Tick

Context Menu：`Tick One Day With Current Vector`

固定调用顺序：

```text
读取 Inspector 五维
→ 构造局部 EmotionVector
→ 构造占位 JournalEntry
→ WorldManager.InjectEmotion(entry)
→ WorldManager.WorldTick(1, false, false)
→ SaveSystem.SaveWorldState(world.WorldSave)
→ SaveSystem.AppendJournalEntry(entry)
```

占位日记字段：

```text
entryId       = 新 Guid
realTimestamp = 当前 ISO-8601 时间
rawText       = "[manual gameplay lab]"
emotion       = 当前局部 EmotionVector
```

一次命令推进恰好一天，并只通过 `WorldTick` 运行一次 `SimulatePass`。

控制器不得调用 `OnJournalSubmitted()`；该方法自身会运行一次响应式 `SimulatePass`，再接 `WorldTick` 会使一次点击模拟两遍世界。

当前调用顺序会让 `InjectEmotion` 更新后的 `E_env` 在 `WorldTick` 开始时先执行一次 `Relax(0.05)`，再参与当天模拟。这是本实验接受的语义。

## 4. 无输入的单日 Tick

Context Menu：`Tick One Day Without Input`

固定调用顺序：

```text
WorldManager.WorldTick(1, false, false)
→ SaveSystem.SaveWorldState(world.WorldSave)
```

无输入日不得提交 Neutral。Neutral 仍会调用情绪惯性更新并新增一条情绪历史；真正的无输入只依赖 `WorldTick` 内的自主 `Relax()`。

## 5. 脚本边界

- 控制器只调用现有 `InjectEmotion`、`WorldTick` 和 `SaveSystem`。
- 控制器不直接写 `E_env`、gameTime、环境、实体、事件或 L3 参数。
- 控制器没有 `Awake`、`Start` 或 `Update`。
- 控制器不提供批量 Tick、自动 Tick、Reset、Undo 或 Restore。
- 控制器只通过普通 Console 行报告 Tick 完成或异常。
- 控制器不向玩家显示五维和 Tick 操作。

## 6. WorldTick 内部链

脚本不手动调用以下系统。`WorldTick(1)` 已按固定顺序执行：

```text
gameTime.Advance(1)
→ EmotionInertia.Relax(0.05)
→ NaturalRhythm.Tick
→ TranslationLayer.Translate
→ WorldEnvironmentSystem.UpdateFromEEnv
→ location 水位/湿度传播
→ VegetationSystem.Tick
→ AnimalDriveSystem.Tick
→ EmergentMomentDetector.Detect
→ BehaviorNarrator.Narrate
→ NarrativeRuleEngine.Evaluate
→ EntityRelationSystem.Evaluate
```

## 7. 已知限制

1. `SaveWorldState` 与 `AppendJournalEntry` 是两个非原子文件写入；第二次失败时可能出现世界已保存、Journal 未追加。
2. `WorldEnvironmentState` 的部分运行时积分不在 `WorldSaveData` 中，重启后不会全部恢复。
3. Context Menu 不是 Inspector 按钮，需要从组件标题右键或右上角菜单执行。
4. 脚本会修改并保存真实实验世界，操作不可撤销。

## 8. 场景前置条件

- [x] `ManualEmotionInjector` 已禁用。（2026-08-03 场景 YAML 单行禁用并提交 db8983b——每帧注入会锁死情绪惯性，demo 打包前已拆）
- [x] 所有 `WorldSimulationTester.runOnStart` 为 false。（07-26 已核实场景两处实例均为 0）
- [x] 场景内挂载一个 `ManualTicking`。（07-26 已挂 ManualGameplayLab GO 并随 bb0518c 落盘）
- [ ] 进入 Play Mode 后再执行 Context Menu。
- [ ] Inspector 和 Tick 操作只对研究者可见。

## 9. 最小验收

带输入 Tick：

- [ ] 日期增加一天。
- [ ] `emotionHistory` 增加一条。
- [ ] `journal_log.json` 增加一条占位日记。
- [ ] 世界和 L3 更新。

无输入 Tick：

- [ ] 日期增加一天。
- [ ] `emotionHistory` 不增加。
- [ ] Journal 不增加。
- [ ] 世界仍自主更新。

工具验证：

- [ ] `dotnet build Assembly-CSharp.csproj --no-restore` 0 error。
- [ ] Unity 编译完成且 Console 无新增 error。
