# 动物状态系统设计（Animal State System）

> 所属层：**Layer 2 — WorldSimulator**
> 状态：定稿 v1，作为 C# 实现蓝本
> 关联：`WorldManager` / `EntityRegistry` / `NaturalRhythmSystem` / `WorldEnvironmentSystem` / `NarrativeRuleEngine`

---

## 0. 一句话定位

每个动物/植物实体持有一组**连续内部状态向量**，由时间与世界参数（`E_env`、天气、水位、植被、季节）驱动逐 tick 演化；每 tick 末由内部状态 + 邻居快照计算出**单一主导行为（BehaviorDrive）+ 成因（CauseFactor）**，写入实体的行为输出字段。文本层（`NarrativeRuleEngine` / 世界志）**只读行为输出，永不读内部状态数值**。

跨实体的因果链（织巢鸟离场 → 鹿鼠焦虑 → 鹿鼠收缩 → 田鼠扩张 → 狐狸巡逻）**完全通过状态向量自然传导**，不写任何一条 if(A) then B 的联动规则。

---

## 1. 接入现有架构

### 1.1 tick 的真实定义

世界**事件驱动**，不是实时连续。唯一推进点是 `WorldManager.OnJournalSubmitted`：

```
OnJournalSubmitted:
  EmotionInertia.Update(emotion)          // E_env 惯性逼近
  gameTime.Advance(1)                      // 推进 1 游戏日  ← 1 tick = 1 game day
  NaturalRhythm.Tick()
  Environment.UpdateFromEEnv(...)          // 天气/雾/降雨
  PropagateEnvironmentToLocations()        // 降雨 → 各 zone.waterLevel
  ── 此处插入 AnimalDriveSystem.Tick() ──   // ★ 新增
  ruleEngine.Evaluate(...)                 // 文本层：读行为输出产出世界志
  relationSystem.Evaluate(...)             // 见 1.2
  SaveSystem.Save(...)
```

**所有速率以"每 tick / 每游戏日"为单位**。日记节奏不规律没关系——无论现实隔多久，每条日记恰好推进 1 游戏日，内部状态按 1 个 step 演化。

> 与 CLAUDE.md「世界有自己的生命，不要纯反应最新日记」的对齐：动物**不直接读日记内容**，只读经过惯性平滑的 `E_env` 与天气。同一份日记下，动物按**自身内部动力学**演化，不同实体反应不同、且有相位滞后——独立生命由内部状态机保证，而非由日记内容驱动。

### 1.2 与 EntityRelationSystem 的关系（架构决策）

现状两套"实体改实体"机制：

| 系统 | 机制 | 去留 |
|---|---|---|
| `EntityRelationSystem` | 声明式 `EntityRelationSO` 规则，优先级级联，直接改字段 | **退居二线**：只保留少数*真正离散、一次性、需要作者手写*的脚本事件（如剧情性的地貌永久变化）。动物之间的连续耦合**全部迁移**到本系统的状态传导。 |
| `AnimalDriveSystem`（本设计，新增） | 内部状态向量 → 行为输出，跨实体走状态快照 | **接管**所有动物/植物的连续动力学与涌现联动 |
| `NarrativeRuleEngine` | 环境 + 实体状态 → 世界志文本 | **保持不变**，作为文本层；新增读取 `BehaviorOutput` 的能力 |

决策理由：用户核心诉求是"联动自然涌现、零硬编码联动规则"。`EntityRelationSO` 的本质就是硬编码联动，与该诉求冲突。因此动物耦合迁出，但不立即删除 `EntityRelationSystem`——它仍是承载"作者指定的、不该涌现的"离散事件的合法工具。两者**职责切分清晰、互不重叠**即可共存。

---

## 2. 空间模型：离散分区 + 邻接图

5 个 zone 已存在于 `LocationEntity`。拓扑（静态，写死在代码 `ZoneTopology`，不进存档）：

```
[riverbank 河岸]──[lowland 低洼地]──[center 草原中央]──[highland_east 东侧高地]
                                          │
                                    [stone_area 石头区]
```

```
riverbank      ↔ lowland
lowland        ↔ riverbank, center
center         ↔ lowland, highland_east, stone_area
highland_east  ↔ center
stone_area     ↔ center
```

空间语义：

- **猴面包树 / 织巢鸟** 固定在 `center`。
- **候鸟** 绑定 `riverbank`。
- **鹿鼠** `activityRange(0-1)` → 以 `highland_east` 为核心向外占据的 zone 数；收缩时退回核心，腾出的相邻 zone 变"无主"。
- **田鼠** home = `lowland`；`Expand` = 认领一个**相邻且无主**的 zone（向 `center` 方向即"向东"，逼近狐狸领地）。
- **狐狸** territory = `{highland_east, center}`（其巡逻范围）。田鼠认领的 zone 落入 territory ⇒ `territoryStability ↓`。

"邻接 + 无主判定"让所有 `location 变化` 成为**离散、可单测、带方向**的事件，文本层好描述。

---

## 3. 数据结构

### 3.1 内部状态挂在实体上（持久化）

内部状态是世界记忆的一部分，随 `WorldSaveData` 序列化。给现有实体新增**可空的内部状态子对象**（旧存档读出为 null → 首 tick 惰性初始化，向后兼容）。

```csharp
// 追加到 AnimalEntity
public AnimalInternalState internalState;   // 物种相关字段，见 §5
public BehaviorOutput      behavior;        // 每 tick 重算的行为输出

// 追加到 PlantEntity（仅 baobab 使用）
public PlantInternalState  internalState;
```

```csharp
[Serializable]
public class BehaviorOutput
{
    public string drive;          // BehaviorDrive 枚举名，见 §5
    public string cause;          // CauseFactor 枚举名，见 §6
    public string causeTargetId;  // 成因指向的实体/zone（如 "vole" / "riverbank"）
    public float  intensity;      // 主导压力值 0-1，文本层可用于措辞强弱
    public string zone;           // 当前 location 的副本，便于文本层一站读取
}
```

> **序列化约束（已确认）**：存档走 `JsonUtility`（见 `SaveSystem`），它**不支持多态 / `[SerializeReference]`**。因此内部状态不能用"每物种一个子类 + 多态基类"。采用**扁平 union `AnimalInternalState`**：所有物种字段合并进一个 `[Serializable]` 类，各物种只读写属于自己的字段。5 个物种字段总量很小，扁平化最稳、可单测。新增维度 = 加一个字段；文本层只读 `BehaviorOutput`，零改动。猴面包树用独立的 `PlantInternalState`（含 `BranchState[]`）。
> 旧存档兼容：内部状态字段含 `initialized` 标记，首 tick 惰性初始化（`AnimalDriveSystem.EnsureInitialized`）。

### 3.2 行为输出字段复用现有列

行为驱动改的是**已存在的输出字段**，并复用 `EntityStateHelper.ChangeAnimalState(...)` 写 `history`（保持 append-only 一致）：

| BehaviorDrive 效果 | 落到的现有字段 |
|---|---|
| 移动 / 觅食 / 巡逻 / 迁移 | `location`, `facingDirection`, `primaryPath` |
| 鹿鼠收缩/扩张 | `activityRange` |
| 候鸟来去 | `isPresent`（+ 事件，见 §7） |
| 猴面包树开花 | `isFlowering`, `lastFlowerDate` |
| 断枝 | `permanentDamages`（append-only） |

---

## 4. 双缓冲 tick（顺序无关性的结构保证）

`AnimalDriveSystem.Tick()` 内部，对**动物/植物的内部状态与行为输出**做快照计算：

```
Tick(env, rhythm, gameTime):
  snapshot = 深拷贝所有动物/植物的 { internalState, isPresent, location, activityRange }
  foreach entity e:
      next_e.internalState = StepInternal(snapshot[e], snapshot[others], env, rhythm)  // 只读 snapshot
      next_e.behavior      = ResolveBehavior(next_e.internalState, snapshot[others], env)
  ApplyOutputs(next)        // 写回 location/activityRange/isPresent，经 EntityStateHelper 记 history
  DetectEvents(snapshot, next)   // 边沿检测 → 事件总线（§7）
```

- **铁律**：`StepInternal` / `ResolveBehavior` 只读 `snapshot`，绝不读其他实体的"本 tick 新值"。
- 因此 4 级链每 tick 推进一级，天然带 1 个游戏日的相位滞后——叙事上"先见鸟走，几天后才见狐狸不安"。
- 快照范围**只含动物+植物**（约 6 个对象，开销可忽略）。`env` / `rhythm` / `location.waterLevel` 等在本步之前已更新完毕，作为**只读输入**直接读 live 值即可。

---

## 5. 各实体内部状态 + 行为驱动

行为解析统一为 **urgency argmax + 现任加成 + 软阈值**：

- 每个 drive 算一个归一化 `urgency ∈ [0,1]`，取最大者为主导。
- 默认行为（Rest/Burrow/Routine/Settle）= 固定基线常数 `BASE ≈ 0.20`，仅在他者都不急时胜出。
- 软阈值：`smooth(x, k) = smoothstep(k-0.1, k+0.1, x)`，替代硬 `>k`，消抖。
- 现任加成：上一 tick 的 drive 其 urgency `+0.10`，须被明显超过才切换，防 flicker。

速率表中 `+x/d` = 每 tick（每游戏日）的 delta；`τ` = 走完整量程的数量级。

### 5.1 狐狸 fox（territory = {highland_east, center}）

内部状态：
| 字段 | 动力学 |
|---|---|
| `hunger` 0-1 | 基础 `+0.06/d`；觅食成功（Foraging 且 zone 内有猎物/高植被）→ `-0.4`；下降速率 × 占据 zone 的 `vegetationDensity` 与田鼠邻近度 |
| `safety` 0-1 | 候鸟在场且狐狸临近 `riverbank` → ↓；`riverbank/lowland.waterLevel` 异常（>0.7 或 <0.1）→ ↓；`E_env.A` 高 → ↓；平静（A 低、Rainfall 低）→ 缓慢 `+0.03/d` 回升 |
| `territoryStability` 0-1 | 田鼠 `location ∈ territory` 或田鼠 location 本 tick 变化 → ↓；长期无变化 → `+0.02/d` 回升 |

驱动 urgency：
```
forage = smooth(hunger, 0.7)
patrol = smooth(1 - territoryStability, 0.6)
avoid  = smooth(1 - safety, 0.7)
rest   = BASE
```
枚举 `FoxDrive { Foraging, Patrol, Avoidance, Rest }`。
输出：Foraging→向最近的高植被/猎物 zone 移动；Patrol→在 territory 内移动并 `territoryStability += 0.15`（重新宣示）；Avoidance→退向 `highland_east`。

### 5.2 田鼠 vole（home = lowland）

| 字段 | 动力学 |
|---|---|
| `shelterSecurity` 0-1 | `= 1 - lowland.waterLevel`（直接反映水位威胁） |
| `foodStock` 0-1 | `-0.05/d` 消耗；按占据 zone 的 `vegetationDensity` 补充；猴面包树开花期 → center 食物上扬，加速补充 |
| `expansionPressure` 0-1 | `foodStock<0.4` → ↑；鹿鼠 `activityRange` 下降（相邻 zone 腾空，机会出现）→ ↑ |

```
expand   = smooth(expansionPressure, 0.6) * hasFreeAdjacentZone   // 无空 zone 则 0
forage   = smooth(1 - foodStock, 0.6)
burrow   = BASE
```
枚举 `VoleDrive { Expand, Forage, Burrow }`。
输出：Expand→认领相邻无主 zone（向 center=向东，更新 `location`），并 `expansionPressure -= 0.4`（压力释放，见 §8）。

> **洪水迁移归属（重要，源于实测冲突）**：田鼠因 `lowland` 水位升高迁往 `highland_east` 是**"环境→实体"**，按 §1.2 边界归 **NarrativeRule `vole_relocate_flood`** 拥有，**本驱动系统不再用 Relocate 移动田鼠**。原因：驱动系统在管线中先于规则运行，若它先把 `vole.location` 改掉，会使该规则前置条件 `location==lowland` 失效、规则不触发（实测 bug）。`shelterSecurity` 仍计入状态向量供叙事/未来用，但不驱动移动。注意 `highland_east` 与 `lowland` 不相邻（规则是跨区"迁徙"，与驱动系统的逐区移动模型不同），这也是两者应分属不同所有者的佐证。验证见 Edit-mode `GlimmerDiary/Test Full Pipeline (Flood)`。

### 5.3 鹿鼠 deer_mouse（核心传导节点，core = highland_east）

| 字段 | 动力学 |
|---|---|
| `anxiety` 0-1 | 织巢鸟**不在场** → `+0.08/d`；`E_env.C` 低 → 抬高基础焦虑；狐狸在同/邻 zone → 急剧 `+0.25`；织巢鸟在场 → 持续 `-0.06/d` |
| `activityRange` 0-1（**复用现有字段**） | anxiety 高 → 向 anxiety 收缩：`activityRange → lerp(cur, 1-anxiety, 0.3)` |

```
retreat = smooth(anxiety, 0.6)
explore = smooth(1 - anxiety, 0.7) * 0.8    // anxiety<0.3 才探索，权重略低于退缩
routine = BASE
```
枚举 `DeerMouseDrive { Retreat, Explore, Routine }`。
`activityRange` 的变化正是田鼠 `expansionPressure` 的上游——整条传导链在此闭合，无任何显式联动代码。

### 5.4 候鸟 migratory_bird（bound = riverbank）

| 字段 | 动力学 |
|---|---|
| `migrationUrge` 0-1 | 由 `gameTime.month` 驱动：秋（9-11）↑、春（3-5）再次↑；`E_env.V` 长期偏负 → 加速 ↑ |
| `settlementComfort` 0-1 | `riverbank.waterLevel` 适中（≈0.4-0.6）→ 高；狐狸频繁出现在 `riverbank`（近 N tick 计数）→ ↓ |

```
depart      = smooth(migrationUrge, 0.8)                        // >0.8
earlyDepart = smooth(1-settlementComfort,0.7)*step(migrationUrge>0.4)
arrive      = seasonMatch && E_env.V>0 && !isPresent           // 不在场→在场
settle      = BASE                                              // 在场且稳定
```
枚举 `MigratoryBirdDrive { Depart, EarlyDepart, Arrive, Settle }`。
Arrive/Depart/EarlyDepart 翻转 `isPresent` → **发事件**（§7），不是连续行为。

### 5.5 织巢鸟 weaver_bird（fixed = center，事件驱动为主）

最小建模：默认 `isPresent=true`。当 `baobab_main` 发生**承载其巢的主枝断裂**事件 → `isPresent=false`（发 `WeaverBirdDeparted`）。长期（树 vitality 回升且无断枝 N tick）后可重新归巢。它的 `isPresent` 是鹿鼠 `anxiety` 的首要调节量——这条链由鹿鼠读 `snapshot[weaver].isPresent` 自然成立。

### 5.6 猴面包树 baobab（plant，被动影响源）

```csharp
[Serializable] public class PlantInternalState {
    public float vitality;                 // E_env.V 长期趋势的积分，τ≈整季
    public BranchState[] branches;         // 每根主枝独立 integrity
    public float floweringReadiness;       // vitality 高且季节匹配时积累，开花归零
    // permanentDamageLog 复用现有 PlantEntity.permanentDamages（append-only）
}
[Serializable] public class BranchState { public string id; public string dir; public float integrity; }
```

| 字段 | 动力学 / 影响 |
|---|---|
| `vitality` | `+= alpha*(E_env.V_norm - vitality)`，alpha≈0.03（极慢积分） |
| `branches[i].integrity` | 极端天气（`E_env.A` 高 **且** `E_env.V` 低）→ ↓；归零 → **断枝事件**（§7）→ 织巢鸟巢损毁 |
| `floweringReadiness` | `vitality` 高且季节匹配 → 缓慢累积；越阈值 → 开花（`isFlowering=true`, 归零）→ center 夜间食物上扬（→ 田鼠 `foodStock`，见 §9 待确认项） |

植物无 BehaviorDrive，输出是**事件 + 对 zone/邻居的被动影响**。`vitality` 持续低 → 树冠稀疏 → `center.lightIntensity`/植被受影响（后续可接植物生长层）。

---

## 6. 成因追踪（文本层的命门）

`ResolveBehavior` 选出主导 drive 时，记录**贡献最大的输入项**为 `CauseFactor`：

```csharp
enum CauseFactor {
    None,
    Hunger, WaterRising, LowVitality,       // 自身/环境项
    RodentExpansion, FoxNearby, BirdAbsent,  // ★ 跨实体项
    SeasonShift, EmotionBleak                 // 世界项
}
```

**选取偏置（写进规范）**：挑 cause 时，**优先跨实体项**（`RodentExpansion`/`FoxNearby`/`BirdAbsent`），即使其数值贡献不是最大。压力函数内部把"自身生理项"与"外部实体项"分开累计，cause 优先取外部项的最大者；外部项全为 0 时才回退到自身/环境项。

理由：用户已确认接受此取舍。代价是有时狐狸明明最饿，文本却在暗示"田鼠来了"——但这正是"暗示行为背后的跨实体原因、永不直说"的叙事价值所在。

**文本层接口**：`NarrativeRuleEngine` 拿到 `(who, drive, zone, cause, causeTargetId, intensity)` 后，按 `cause` 查表选"最相关环境细节"：
| cause | 暗示细节示例 |
|---|---|
| `RodentExpansion` | "田鼠新洞口的土还是新的" |
| `FoxNearby` | "狐狸最近常在河岸附近" |
| `BirdAbsent` | "树上那只织巢鸟有阵子没叫了" |
| `WaterRising` | "低洼地的水又浸上来一指" |

---

## 7. 事件总线（离散跃迁，区别于连续状态）

边沿检测放在 `ApplyOutputs` 之后、`DetectEvents` 内。事件命名遵循 CLAUDE.md **过去时**约定，永久事件写 append-only 日志：

```csharp
[Serializable] public class WorldEvent {
    public string type;       // 过去时：TreeBranchBroke / AnimalArrived / AnimalDeparted / TreeFlowered / WeaverBirdDeparted
    public string sourceId;
    public string targetId;   // 受影响实体
    public string gameDate;
    public string payload;    // 如断枝方向 "E-2"
}
```

两类消费者：
1. **文本层** —— 事件是"大新闻"，优先级高于日常行为描写。
2. **其他实体** —— `TreeBranchBroke → WeaverBirdDeparted → weaver.isPresent=false`。写入的是 next 状态，鹿鼠**下一 tick** 才读到，链条滞后仍成立。

跃迁清单：`AnimalArrived`/`AnimalDeparted`（候鸟）、`TreeBranchBroke`（→`permanentDamages` append）、`TreeFlowered`、`WeaverBirdDeparted`/`WeaverBirdReturned`、`VoleClaimedZone`。

---

## 8. 反馈阻尼（防循环饱和）

铁律：**每个行为必须对触发它的状态写回负反馈**，否则状态焊死在 0/1 边界。

| 行为 | 对触发源的回写 |
|---|---|
| Fox.Foraging（成功） | `hunger -= 0.4` |
| Fox.Patrol | `territoryStability += 0.15` |
| Vole.Forage（成功） | `foodStock += 0.3` |
| Vole.Expand | `expansionPressure -= 0.4`，新 zone → `foodStock += 0.2` |
| Vole.Relocate | 迁到低水位 zone → `shelterSecurity` 随新 zone 抬升 |
| DeerMouse.Retreat | `anxiety -= 0.05`（退缩缓解，但织巢鸟不在则净值仍可能上升） |
| Bird.Settle | `settlementComfort += 0.03` |

叠加各状态的自然回归项，系统在中间地带**振荡**而非卡死——这正是语料库"持续变化、不自我重复"的来源。

---

## 9. 待确认 / 已知不一致

1. **开花 → 谁的 foodStock**：用户原文写"开花 → 鹿鼠 foodStock 上升"，但鹿鼠(deer_mouse)内部状态是 anxiety/activityRange，**没有 foodStock**；有 foodStock 的是田鼠(vole)。**本设计取**：开花 → `center` zone 夜间食物上扬 → 提升**田鼠 foodStock** 的补充速率，并轻微 `deer_mouse.anxiety -= `（食物丰沛→更敢活动）。实现时按此口径，若需改回请提出。
2. **weaver_bird 归巢条件**：暂定"树 vitality 回升 + 连续 N(=30) tick 无断枝"。N 可调。
3. **狐狸"频繁出现在河岸"窗口**：暂定近 `W=10` tick 内 fox.location=riverbank 的计数 / W 作为频率。

---

## 10. 实施阶段（进度）

- **[✅ DONE] P1 骨架**：`ZoneTopology`（邻接表）+ 内部状态类（扁平 union）+ `BehaviorOutput`/`CauseFactor`/`WorldEvent` 数据结构 + `AnimalDriveSystem` 双缓冲框架。挂进 `WorldManager` 管线、编译通过。
- **[✅ DONE] P2 打通一条链**：`deer_mouse` + `vole` + `weaver_bird`，跑通"狐狸临近 → 鹿鼠 anxiety → activityRange 收缩 → 田鼠 expansionPressure → 田鼠向 center 扩张"。Edit-mode 冒烟测试三断言全 PASS（菜单 `GlimmerDiary/Test Anxiety Chain`）。
- **[✅ DONE] P3 补全**：`fox`（hunger/safety/territoryStability）、`migratory_bird`（内部态 + Depart/EarlyDepart；迁来仍由 NarrativeRule 拥有）、`baobab`（vitality/flowering）+ **事件总线**（`TreeBranchBroke`/`WeaverBirdDeparted`/`TreeFlowered`/`AnimalDeparted`/...）。断枝→织巢鸟离场→鹿鼠焦虑链 5 断言全 PASS（菜单 `GlimmerDiary/Test Weaver Chain`）。
- **[🟡 大部分] P4 文本层对接**：新增 `BehaviorNarrator`（只读 `BehaviorOutput`+`cause`+`worldEvents`），承接 4 条退役 `EntityRelation` 的 textTemplates。**已迁移/新增文案**：deer_mouse(BirdAbsent)、vole(DeerMouseWithdrew)、fox(RodentExpansion)、候鸟 EarlyDepart(FoxNearby)、WeaverBirdDeparted/Returned、AnimalDeparted、TreeFlowered。`WorldRuleCreator` 的 4 条 relation 生成已标 DEPRECATED（不再生成；narrative rule 生成保留）。冒烟测试已覆盖 fox→deer_mouse→vole→fox 五连环 + 文案产出，全 PASS。~~**待补**：fox(Hunger)等次要文案~~ ✅ 已补（2026-07-28：FoxForageHunger 2 条＋旱环境语料 DroughtRiverbank 2 条，见 Docs/更改_2026-07-28.md）；在真实 `WorldManager` 管线（聚焦编辑器 Play mode）跑一次端到端确认退役过滤 + narrator 接入正确。
- **[✅ DONE] P5 调参**：~29 个速率/阈值常量外提为 `AnimalDriveTuning` ScriptableObject（`AnimalDriveSystem` 构造时注入，缺省回退字段默认值=原常量；菜单 `GlimmerDiary/Create Animal Drive Tuning` 生成 `Resources/Tuning/AnimalDriveTuning.asset`，`WorldManager` 启动自动加载，Inspector 调参运行时即时生效）。长程仿真 `GlimmerDiary/Test Long Run (Health)`：120 天月度起伏，4 项健康断言全 PASS。

### P5 长程仿真发现（120 天月度起伏）
- **无饱和 / 无 NaN / 全程 ∈ [0,1]**：`vole.expansionPressure` pinHi **0%**（§8 负反馈生效，扩张压力不 runaway）；`fox.territoryStability` 最低 0.40、pinLo **0%**（Patrol 重宣示托住领地稳定度）。9/10 变量 range>0.15，系统持续振荡不死板。涌现事件丰富：候鸟来去、田鼠扩张、狐狸巡逻、**2 次断枝→织巢鸟离场→回归**。
- **两个偏斜均衡（可调，非 bug）**：`fox.hunger` mean **0.90**、pinHi 77%（狐狸长期偏饿——`foxForageRelief` 相对 `foxHungerGain` 偏弱 / Foraging 阈值偏高）；`dm.anxiety` mean **0.92**、`dm.range` 77% 趋零（鹿鼠长期紧张——狐狸领地与其核心区重叠 + 断枝周期性赶走织巢鸟）。属合理的草原捕食张力；想缓和直接在 SO 调 `foxForageRelief↑` / `dmAnxCalmBird↑` / `dmFoxSpike↓`，无需改代码。

### 架构决策落地（P4 提前的一部分）
4 条"实体→实体"耦合的 `EntityRelationSO` 已从关系系统活动集**剔除**（`WorldManager.RetiredRelationIds`，资产保留可逆）：`deer_mouse_anxious` / `vole_territory_expand` / `weaver_habitat_lost` / `insect_surge_vegetation`。其状态效果由 `AnimalDriveSystem` 接管，文案由 `BehaviorNarrator` 接管。
**保留不动**：`NarrativeRuleSO`（断枝、候鸟迁来等"情绪/环境→离散事件"），驱动系统只对其结果做反应（如 `permanentDamages` 边沿 → 织巢鸟离场），绝不竞争同一字段。

### 已知 / 运行环境注意
- Edit-mode 冒烟测试（`Assets/GlimmerDiary/Scripts/Editor/AnimalDriveSmokeTest.cs`）绕过 `WorldManager`，直接驱动系统，确定性、无需 Play mode。菜单：`GlimmerDiary/Test Anxiety Chain`、`GlimmerDiary/Test Weaver Chain`。
- 后台（编辑器未聚焦）Play mode 不推进帧，故端到端验证用 Edit-mode 菜单或聚焦编辑器手动跑 `WorldSimulationTester`。

---

## 附录 A：参数常量（首版默认，待 P5 调参）

```
BASE_DRIVE            = 0.20
INCUMBENT_BONUS       = 0.10
SOFT_THRESHOLD_BAND   = 0.10

fox.hungerGain        = 0.06 /d      fox.forageRelief      = 0.40
fox.safetyRecover     = 0.03 /d      fox.territoryRecover  = 0.02 /d
fox.patrolReassert    = 0.15

vole.foodDecay        = 0.05 /d      vole.forageRelief     = 0.30
vole.expandRelief     = 0.40         vole.expandFoodBonus  = 0.20

deerMouse.anxietyGainNoBird = 0.08 /d   deerMouse.anxietyCalmBird = 0.06 /d
deerMouse.foxNearbySpike    = 0.25      deerMouse.rangeLerp       = 0.30

baobab.vitalityAlpha  = 0.03         baobab.weaverReturnTicks = 30
fox.riverbankWindow   = 10
```
