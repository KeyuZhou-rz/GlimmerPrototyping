# Workflow: feature-new-entity — 新增实体类型

## 触发条件
新增动物物种、植物实体、地点区域、世界事件类型。

## 涉及角色
**data-persistence-agent** → **system-architect** → **rendering-visual-expert** → **qa-tester**

## 门控点
`data-propose` skill — 用户审批新实体数据结构和跨实体关系后才能进入实现阶段。

---

## Phase 1: 提案（data-persistence-agent）

**输入**: 新实体需求（物种名、生态角色、与现有实体的关系）

**步骤**:
1. 生成 `data-propose` 提案，覆盖：
   - **实体 ID**（snake_case，如 `monitor_lizard`）
   - **数据结构字段**：是否持久化、内部状态类型、行为输出字段
   - **与现有实体的关系**：天敌/共生/中立/无
   - **空间语义**：home zone / territory / 迁移模式
   - **跨实体因果链设计**：读谁的状态 → 影响自己的什么
2. 确认所有关系符合单一写者原则：
   - 该实体的 `internalState` 和 `behavior` 由 `AnimalDriveSystem` 独占
   - 如涉及离散事件（如季节触发）→ 由 `NarrativeRuleEngine` 独占
3. 提交用户审批
4. **阻塞等待** — 用户确认实体设计

**输出**: 经批准的新实体数据结构提案

---

## Phase 2: 数据结构（data-persistence-agent）

**输入**: 批准的提案

**步骤**:
1. 在 `Data/` 中新增或扩展数据结构类：
   - 如在现有 `WorldEntityData.cs` 中添加字段 → COMPATIBLE 变更
   - 如新增独立的 `Data/` 文件 → NEW 变更
2. 更新 `WorldInitializer.CreateNewWorld()` — 新实体初始状态（位置、内部状态默认值、isPresent 等）
3. 更新 `EntityRegistry` — 如需新索引方式（如新的 `GetEntity(string id)` 重载）
4. 更新 `SaveSystem` — 如序列化结构变化
5. 调用 `layer-boundary-check` — 确认 Data 层无违规
6. 调用 `code-style-check`
7. 通知 system-architect 和 rendering-visual-expert

**输出**: 新数据结构 + WorldInitializer 更新 + 序列化兼容

---

## Phase 3: 运行时逻辑（system-architect）

**输入**: 新实体数据结构 + 跨实体关系定义

**步骤**:
1. 在 `AnimalDriveSystem` 中新增 `Tick{SpeciesName}()` 方法：
   - 从 Snap 字典读取跨实体字段（绝不直接读实时字段）
   - 定义内部状态演化规则（anxiety/hunger/territory 等的 delta 计算）
   - 实现 `Argmax` 行为仲裁（urgency 加权 + 现任加成 SOFT_BAND）
   - 定义 `CauseFactor` 传递（跨实体成因链路）
2. 在 `AnimalDriveSystem.Tick()` 的 switch 中注册新物种
3. 在 `BehaviorNarrator` 中新增行为文本模板（多语言）
4. 如涉及离散事件（如季节触发抵达/离开）：
   - 在 `NarrativeRuleEngine` 中新增规则条件
   - 或创建新的 `NarrativeRuleSO` 资产（使用 `WorldRuleCreator`）
5. 在 `AnimalDriveTuning` 中新增调优参数（如需要）— 需先走 `data-propose` 提案
6. 调用 `layer-boundary-check` — 确认 Core 层无违规
7. 调用 `code-style-check`

**输出**: 新实体的完整运行时逻辑

---

## Phase 4: 视觉表现（rendering-visual-expert）

**输入**: 新实体的 BehaviorOutput + WorldState 接口

**步骤**:
1. 从 `WorldManager.Instance.GetWorldState()` 和 `Registry` 读取新实体状态
2. 创建/调整 Prefab：
   - 如需新建 Prefab：MCP L1 — 告知用户后创建
   - 如需修改已有 Prefab：MCP L2 — **先 plan → 用户逐条审批**
3. 绑定视觉参数到实体状态（如 vitality → 颜色/大小、isPresent → 显隐）
4. 如涉及动画：绑定 Animator 参数到 BehaviorOutput.drive
5. 检查性能：L-System / GPU Instancing / MaterialPropertyBlock
6. 调用 `layer-boundary-check` — 确保渲染层不写世界状态
7. 调用 `code-style-check`

**输出**: 新实体的视觉表现

---

## Phase 5: 测试（qa-tester）

**输入**: 新实体的完整实现（数据 + 逻辑 + 视觉）

**步骤**:
1. 新增 UTF EditMode 测试：
   - 新实体内部状态演化（正常 + 边界）
   - 新实体行为仲裁逻辑（各 drive 的触发条件）
   - 新实体与已有实体的跨实体因果链
   - 新实体在 Edge case 下的行为（所有其他实体缺席时）
2. 新增 UTF PlayMode 集成测试（如有 Prefab 和场景依赖）
3. 验证架构合约：
   - 单一写者：新实体字段是否只被 AnimalDriveSystem 写入
   - 快照协议：跨实体读取是否通过 Snap
   - 追加写入：新实体产生的事件是否为 append-only
4. 运行全量回归测试
5. 输出测试报告

**输出**: 新实体的完整测试覆盖 + 测试报告

---

## 边界规则
- 如新实体不涉及视觉表现（纯数据实体，如统计数据）→ 跳过 Phase 4
- 如新实体不涉及运行时逻辑（纯视觉装饰）→ 跳过 Phase 3，Phase 1 由 rendering-visual-expert 发起
- 如新实体仅涉及调优参数变更 → 退化为 `data-model-change` workflow
