---
name: data-propose
description: 数据结构/接口变更提案 — 所有 agent 新增或修改数据字段前必须调用，生成标准化提案供用户审批
---

# data-propose — 数据结构与接口变更提案

## 触发时机
- 任何 agent 需要新增、修改、删除 Data/ 下的字段或类型
- 任何 public/internal 方法签名变更
- 任何 ScriptableObject 调优字段变更
- 任何 SaveSystem 序列化格式变更

## 核心原则
用户对代码有绝对掌握权。任何新增的计算、数据结构、数据接口必须由用户给出或由 agent 与用户共同定义。未经用户确认的变更不得进入实现阶段。

## 提案模板

每次变更必须生成以下格式的提案：

```markdown
## 数据结构变更提案

### 基本信息
- **提案 ID**: {DATE}-{序号}（如 20260630-01）
- **发起 agent**: {agent 名称}
- **影响范围**: {BREAKING / COMPATIBLE / NEW}
  - BREAKING: 旧存档无法反序列化，或下游系统编译失败
  - COMPATIBLE: 新增字段有默认值，旧代码不受影响
  - NEW: 全新数据结构，不涉及已有数据

### 变更详情

#### 新增字段
| 文件 | 字段名 | 类型 | 默认值 | 用途 |
|---|---|---|---|---|
| Data/XxxData.cs | fieldName | float | 0.5f | 描述用途 |

#### 修改字段
| 文件 | 字段名 | 旧类型 | 新类型 | 迁移策略 |
|---|---|---|---|---|
| Data/XxxData.cs | oldField | int | float | 旧值转换为新值的方式 |

#### 废弃字段（不删除）
| 文件 | 字段名 | 废弃原因 | 保留至版本 |
|---|---|---|---|
| Data/XxxData.cs | deprecatedField | 被 newField 替代 | v0.4 |

### 所有权
- **单一写者（独占写权限）**: {哪个系统拥有此字段的写权限}
- **读取者（只读）**: {哪些系统读取此字段}
- **序列化路径**: {是否持久化到 world_state.json}

### 下游影响
- [ ] WorldInitializer.CreateNewWorld() 需更新
- [ ] SaveSystem 序列化需适配
- [ ] EntityRegistry 需更新索引
- [ ] AnimalDriveSystem 需读取/写入新字段
- [ ] NarrativeRuleEngine 条件需适配
- [ ] BehaviorNarrator 文本模板需更新
- [ ] 渲染层需绑定新参数
- [ ] 测试需更新

### 兼容性验证
- [ ] 旧存档（无此字段）反序列化后使用默认值，不报错
- [ ] 新存档包含此字段，旧版本代码忽略它（JsonUtility 行为）
- [ ] 类型变更字段有显式迁移逻辑（不在序列化中隐式转换）
```

## 审批流程

```
agent 生成提案
    │
    ▼
用户审批
    ├─ ✅ 批准 → agent 进入实现阶段
    ├─ 🔄 修改 → agent 根据反馈修改提案
    └─ ❌ 否决 → 放弃此变更
```

## 执行方式
1. Agent 分析变更范围（哪些 Data/*.cs 文件受影响）
2. Agent 生成标准化提案（填充上述模板）
3. Agent 将提案提交用户
4. 等待用户确认后，agent 才允许写代码
5. 实现完成后，agent 在 commit message 中引用提案 ID

## 特殊场景

### ScriptableObject 调优参数
如 `AnimalDriveTuning` / `EmergentMomentTuning` 新增字段：
- 提案中需说明缺省值（`ScriptableObject.CreateInstance` 时的值）
- 旧 `Resources/Tuning/XxxTuning.asset` 需手动在 Unity Editor 中补上新字段值（agent 不可通过 MCP 修改）

### 方法签名变更
如 `AnimalDriveSystem.Tick()` 的参数变更：
- 提案中需列出所有调用点
- 说明旧签名的兼容策略（保留重载？直接修改所有调用方？）
