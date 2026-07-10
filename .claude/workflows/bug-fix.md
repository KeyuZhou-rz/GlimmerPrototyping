# Workflow: bug-fix — 缺陷修复

## 触发条件
发现 bug（用户报告、qa-tester 发现、agent 自检发现）

## 涉及角色
**qa-tester** → **对应写域 agent** → **qa-tester**

## 门控点
无。修复在已有数据结构范围内，不新增字段或接口。如需新增字段 → 切换至 `data-model-change` workflow。

---

## Phase 1: 复现与定位（qa-tester）

**输入**: Bug 描述（现象、步骤、预期 vs 实际行为）

**步骤**:
1. 编写 UTF 测试复现 bug（**红测试** — 先写失败的测试）
   ```csharp
   [Test]
   public void Tick_FoxPresent_DeerMouseAnxietyDoesNotExceedOne()
   {
       // Bug: 鹿鼠焦虑在某些条件下超过 [0,1] 范围
       // 复现步骤: ...
       Assert.That(dm.internalState.anxiety, Is.InRange(0f, 1f),
           $"鹿鼠焦虑 {dm.internalState.anxiety:F2} 超出 [0,1]");
   }
   ```
2. 确认测试在当前代码下失败（红）
3. 根据代码审查和日志定位到具体文件和系统
4. 确定 bug 归属的写域 agent：
   - `AnimalDriveSystem` 内部状态计算 → system-architect
   - `SaveSystem` 序列化/反序列化 → data-persistence-agent
   - `NarrativeRuleEngine` 规则评估 → system-architect
   - Shader / 渲染 → rendering-visual-expert
5. 输出 bug 报告（复现步骤 + 失败测试 + 定位分析），指派给对应 agent

**输出**: 红测试 + Bug 报告 + Agent 指派

---

## Phase 2: 修复（对应写域 agent）

**输入**: Bug 报告 + 红测试 + 定位分析

**步骤**:
1. 在写域范围内诊断根因：
   - 检查是否违反单一写者原则（其他系统篡改了本系统的字段？）
   - 检查是否违反快照协议（读了其他实体的实时字段而非 Snap？）
   - 检查是否边界条件处理缺失（null、0、1、空列表）
2. 在写域范围内修复（不跨域修改）：
   - 修复代码必须在 agent 的写域内
   - 如根因在另一个系统的写域 → 通知该 agent（不自行跨域修复）
3. 修后自检：
   - 是否改变了数据结构？（是 → 中止，切换至 `data-model-change` workflow）
   - 是否改变了方法签名？（是 → 中止，走 `data-propose` 提案）
   - 是否跨越写域边界？（是 → 中止，与相关 agent 协调）
4. 调用 `layer-boundary-check` — 确认修复不引入架构违规
5. 调用 `code-style-check`
6. 确认 qa-tester 的红测试现在通过（绿）

**输出**: 修复代码 + 红测试现已通过

---

## Phase 3: 验证（qa-tester）

**输入**: 修复代码 + 红测试已通过

**步骤**:
1. 运行复现测试 — 确认通过
2. 运行全量回归测试 — 确保无回归
3. 运行 `layer-boundary-check` 全量扫描 — 确保修复未引入新违规
4. 检查相关边界条件是否仍有覆盖缺口：
   - 如修复只覆盖了正常路径，补充边界测试
   - 如修复暴露了新的边界，新增测试
5. 输出修复验证报告

**输出**: 修复验证报告 + （可选）补充测试

---

## 边界规则

### 禁止在 bug-fix 中做的事情
- ❌ 新增数据结构字段 — 走 `data-model-change`
- ❌ 改变方法签名 — 走 `data-propose` 提案
- ❌ 跨写域修复 — 通知对应 agent
- ❌ 为修复而引入世界重置 API — 铁律禁止
- ❌ 添加 `#if UNITY_EDITOR` 特殊路径绕过逻辑 — 编辑器测试应能调用相同的逻辑
- ❌ 删除或清空 `worldEvents` / `permanentDamages` / `emotionHistory` — append-only 铁律

### 允许在 bug-fix 中做的事情
- ✅ 修改运行时逻辑（在写域内）
- ✅ 调整调优参数默认值
- ✅ 修复边界条件（null 检查、范围 clamp）
- ✅ 修复序列化/反序列化格式（如 `lastTickRealTime` 未被持久化）
- ✅ 补充日志输出（仅测试代码）

---

## 快速诊断指南

| 症状 | 常见根因 | 对应 agent |
|---|---|---|
| 动物内部状态 NaN / 越界 | `Mathf.Clamp01` 缺失 | system-architect |
| 行为输出不变（固定为一个 drive） | `Argmax` 权重失衡或 SOFT_BAND 过大 | system-architect |
| 跨实体因果链断裂 | 读了实时字段而非 Snap | system-architect |
| 存档加载后状态异常 | 新字段无默认值，反序列化返回 0/null | data-persistence-agent |
| 情绪漂移不生效 | `EmotionInertia.Relax()` 未被调用 | system-architect |
| 世界事件日志丢失 | 某处代码 `Clear()` 或被错误移除 | data-persistence-agent |
| Shader 参数不更新 | `MaterialPropertyBlock` 未正确应用 | rendering-visual-expert |
| 天气效果与 E_env 不同步 | 绑定到了 E_current 而非 E_env | rendering-visual-expert |
