---
name: code-style-check
description: 代码风格与命名规范检查，所有 agent 提交代码前必须调用
---

# code-style-check — 代码风格与命名规范

## 触发时机
- 每次 agent 写完代码后
- 提交/PR 之前

## 检查项

### 命名规范
- [ ] World 状态变量：`WorldValence`、`WorldArousal`、`WorldDecayLevel` — PascalCase 带 World 前缀
- [ ] EmotionVector 字段：`valence`、`arousal`、`temporality`、`sociality`、`certainty` — lowercase 全拼
- [ ] 永久事件：past tense — `TreeBranchBroke`、`AnimalArrived`、`WeaverBirdDeparted`
- [ ] 私有字段：`_camelCase`（如 `_saveData`、`_ruleEngine`）
- [ ] 公共属性：`PascalCase`（如 `CurrentEEnv`、`WorldSave`）
- [ ] 常量/静态只读：`UPPER_SNAKE_CASE`（如 `MAX_CATCHUP_DAYS`、`BASE_DRIVE`）
- [ ] 方法名：PascalCase 动词开头（`InjectEmotion`、`SimulatePass`、`GetWorldState`）
- [ ] 布尔变量：`is`/`has`/`can`/`should` 前缀（`isPresent`、`hasArrived`、`canExpand`）

### 命名空间
- [ ] Layer 1 情感引擎：`GlimmerDiary.Sentiment`
- [ ] Layer 2 世界模拟器：`GlimmerDiary.Core`、`GlimmerDiary.Data`、`GlimmerDiary.Utils`
- [ ] Layer 3 视觉：`GlimmerDiary.Flora`、`GlimmerDiary.Visual`
- [ ] 编辑器：`GlimmerDiary.Editor`
- [ ] 测试：`GlimmerDiary.Tests`

### 注释规范
- [ ] 所有 `public` 方法必须有 XML 文档注释（`/// <summary>`）
- [ ] 类级注释说明所属层和职责（参见现有代码 `AnimalDriveSystem.cs` 头注释）
- [ ] 复杂算法有行内注释解释意图（不解释语法）
- [ ] 注释密度与周围代码一致

### 代码结构
- [ ] 无 `#region` 折叠（项目不使用）
- [ ] 无 magic number：所有可调参数外提为 `const` 或 ScriptableObject 字段
- [ ] 无残留的 `Debug.Log` 调用（测试代码除外）
- [ ] 无注释掉的代码块（删除而非注释）
- [ ] using 语句按 System → UnityEngine → 第三方 → 项目命名空间排序

### 项目特定
- [ ] 无世界重置或时间旅行 API（即使 `#if UNITY_EDITOR` 也不允许）
- [ ] VFX 参数不直接绑定到 raw sentiment score
- [ ] Mesh 生成不放在 `Update()` 中（使用 Job System 或 `Start()` 预计算）
- [ ] 无 UI 暴露内部参数（Valence/Arousal/Certainty 等）

## 执行方式
1. 读取被修改的文件
2. 逐项检查上述清单
3. 输出违规项（如有）
4. 违规项修复后才能进入下一阶段

## 例外规则
- 测试文件（`Tests/`、`Editor/*SmokeTest*`）豁免 XML 注释要求
- `main.cs` 作为 MonoBehaviour 测试容器豁免 Unity API 限制
- `WorldManager.cs` 作为唯一胶水层，允许直接接触所有 Layer 2 系统
