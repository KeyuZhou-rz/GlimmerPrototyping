# Workflow: visual-binding — 视觉表现绑定

## 触发条件
将已有的世界状态参数绑定到新的视觉表现（新天气效果、新植被类型、新 Shader 参数、VFX 扩展）。

## 涉及角色
**system-architect**（如需新数据接口）→ **rendering-visual-expert** → **qa-tester**

## 门控点
- 如需新数据接口：`data-propose` skill → 用户审批
- 如需修改 Unity 资产：MCP plan → 用户审批

---

## Phase 1: 接口确认（system-architect）

**输入**: 视觉需求（需要读取什么世界参数）

**步骤**:
1. 检查现有 `WorldManager` 是否已提供足够的只读接口：
   - `GetWorldState()` → `WorldEnvironmentState`（天气、雾、衰败、植被密度）
   - `GetRhythmState()` → `NaturalRhythmState`（季节、光照、周节律）
   - `WorldSave` → `WorldSaveData`（E_env、实体列表）
   - `Registry` → `EntityRegistry`（单个实体查询）
2. 判断是否需要新接口：
   - 已有接口足够 → 直接提供接口文档，跳过提案
   - 需要新只读 getter（如新的聚合计算参数）→ 添加 `public` getter（不涉及数据结构变更）
   - 需要新数据结构字段（如扩展 `WorldEnvironmentState`）→ **走 `data-propose` 提案**
3. 如需 `data-propose`：
   - 发起提案（新字段仅在 `WorldEnvironmentState` 或 `NaturalRhythmState` 中，且为只读）
   - 等待用户审批
4. 接口就绪后，通知 rendering-visual-expert

**输出**: 可用接口清单 +（可选）批准的 data-propose 提案

---

## Phase 2: 视觉实现（rendering-visual-expert）

**输入**: 可用的只读接口

**步骤**:
1. **参数读取**：
   - 在 `Update()` 中从 `WorldManager.Instance` 读取参数
   - 应用平滑曲线（`Mathf.Lerp` / `SmoothDamp` / `AnimationCurve`）
   - 参数变化速率控制（alpha 0.1~0.3，比情绪输入慢）
2. **视觉脚本实现**：
   - 绑定到 Material 参数 → 使用 `MaterialPropertyBlock`（不创建新 Material 实例）
   - 绑定到粒子系统 → `ParticleSystem.main` / `ParticleSystem.emission`
   - 绑定到 VFX Graph → `VFX.SetFloat` / `VFX.SetVector3`
   - 绑定到光照 → `Light.intensity` / `Light.color`
   - 绑定到 Shader 全局参数 → `Shader.SetGlobalFloat` / `Shader.SetGlobalVector`
3. **资产操作**（如需）：
   - 新建 Material → MCP L1：告知后执行
   - 新建 ShaderGraph / VFX Graph → MCP L1：告知后执行
   - 修改已有 Material 参数 → MCP L2：**先 plan → 用户逐条审批**
   - 修改 Prefab → MCP L3：**默认禁止，仅用户主动要求**
4. **性能验证**：
   - `Update` / `LateUpdate` 职责分离
   - `Update`：读世界状态 + 计算参数
   - `LateUpdate`：应用参数到渲染组件
   - 检查无每帧分配（避免 `new` 在 Update 中）
5. 调用 `layer-boundary-check` — 确保渲染层不写世界状态
6. 调用 `code-style-check`

**输出**: 视觉脚本 +（可选）新建/修改的 Unity 资产

---

## Phase 3: 验证（qa-tester）

**输入**: 视觉绑定实现

**步骤**:
1. 验证视觉层契约：
   - 渲染层是否只通过 `GetWorldState()` / `GetRhythmState()` 读取
   - 渲染层是否写入世界状态（grep 检查）
   - VFX 参数是否绑定到 E_env（不是 E_current）
2. 验证性能：
   - `Update` / `LateUpdate` 分离是否遵守
   - 是否有 `new` 分配在 Update 中（GC 压力）
3. 验证资产完整性（如涉及 MCP 操作）：
   - 新建资产是否存在
   - 资产引用是否完整
4. 输出验证报告

**输出**: 视觉绑定验证报告

---

## 边界规则
- 如视觉需求涉及新增数据接口 → **必须走 data-propose 提案**，不得在渲染层中直接 new WorldEnvironmentState
- 如视觉需求仅使用已有接口 → 跳过 Phase 1，rendering-visual-expert 可直接开始
- 如需求涉及天气/VFX → 确认 `EmotionWeatherController` 是唯一入口点
- 如需求涉及植物/L-System → 确认 `EcosystemManager` 协调各植物控制器
