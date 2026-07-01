# Workflow: data-model-change — 数据结构变更

## 触发条件
需要新增、修改、废弃 `Data/` 下的任何字段或类型。

## 涉及角色
**data-persistence-agent** → **system-architect** → **qa-tester**

## 门控点
`data-propose` skill — 用户审批后才能进入实现阶段。

---

## Phase 1: 提案（data-persistence-agent）
1. 分析变更范围
2. 评估兼容性：BREAKING / COMPATIBLE / NEW
3. 生成 data-propose 提案
4. 提交用户审批 → 阻塞等待

## Phase 2: 数据结构实现（data-persistence-agent）
1. 修改 Data/*.cs：新增字段带默认值，废弃字段加 [Obsolete] 不删除
2. 更新 WorldInitializer.CreateNewWorld()
3. 更新 SaveSystem（如需要）
4. 自检旧存档兼容性
5. layer-boundary-check + code-style-check
6. 通知 system-architect 和 qa-tester

## Phase 3: 运行时适配（system-architect）
1. 在拥有新字段的系统中适配逻辑
2. layer-boundary-check + code-style-check

## Phase 4: 测试（qa-tester）
1. 更新受影响的测试 + 新增边界测试 + 序列化往返测试
2. 运行全量测试 → 输出报告

## Phase 5: 收尾
1. 更新维护清单
2. Commit: 引用提案 ID + BREAKING/COMPATIBLE 标注
