# L-System 植物生成系统 — 文件说明

> 本文档说明 `Assets/Scripts/Lsystem`（旧版 / v1）与 `Assets/Scripts/Lsystemv2`（新版 / v2）
> 两个文件夹中每个文件的作用，并标注重复、过时或可能无用的文件。
> 仅作说明用途，**未对任何代码做修改**。

---

## 1. 总览

| | **Lsystem (v1)** | **Lsystemv2 (v2)** |
|---|---|---|
| 命名空间 | 全局（无 namespace） | `GlimmerDiary.Flora` |
| 维度 | 2D（仅绕 Z 轴旋转） | 完整 3D（Yaw / Pitch / Roll） |
| 参数载体 | `TreeParameters` ScriptableObject | `PlantDefinition` ScriptableObject |
| 规模 | 单棵树 | 单株 → 整片生态系统（树/灌木/草/风） |
| 状态 | **遗留版本，已被 v2 取代**，但仍被部分场景引用 | **当前主线版本** |

结论：**v2 是 v1 的全面升级与重写**。两者类名有重叠（`LSystemGenerator`、`TreeMeshBuilder`、`TurtleInterpreter*`），
但因 v2 在 `GlimmerDiary.Flora` 命名空间下，**不会产生编译冲突**。

---

## 2. Lsystem (v1) — 遗留 2D 版本

| 文件 | 作用 |
|---|---|
| `LSystemGenerator.cs` | L-System 字符串生成器（静态类）。内置一套默认分支规则，提供确定性 `Generate` 与简单随机 `GenerateStochastic`。 |
| `TurtleInterpreter.cs` | 海龟解释器。把 L-System 字符串解析成 `BranchSegment` 列表。**仅 2D**（`+/-` 只绕 Z 轴），含一个根据深度（≥5）判断是否长叶的 `leafJudge`。 |
| `TreeMeshBuilder.cs` | 把分支段构建成网格。支持圆柱体 `Build`（含十字交叉叶片 `AddCrossedLeaf`，2 个子网格：树干 + 叶）和扁平 `BuildFlat`。 |
| `TreeParameters.cs` | ScriptableObject，定义 v1 的树参数（axiom、迭代、角度、宽度衰减等）。菜单 `GlimmerDiary/Tree Parameters`。 |
| `TreeController.cs` | MonoBehaviour 入口。读取 `TreeParameters` + 情绪(valence/arousal) → 生成字符串 → 解释 → 建网格，并驱动简单生长动画。 |
| `TreeParams.asset` | `TreeParameters` 的一个实例资产（angle 19.1，initialWidth 0.472）。 |
| `*.meta` | Unity 为每个资产生成的元文件，**勿手动删除**。 |

### ⚠️ v1 的重复 / 注意点
- **`TreeMeshBuilder.cs` 第 2、4 行** `using TMPro;` 与 `using UnityEngine.SocialPlatforms.GameCenter;` 是**多余的 using**，代码并未用到。
- **`TreeMeshBuilder.Build`** 中 `SetTriangles(wood_triangles, 0)` 调用了**两次**（第 46 行与第 52 行），第二次为冗余。
- 项目里存在**两个 `TreeParams.asset`**：
  - `Assets/Scripts/Lsystem/TreeParams.asset`（angle 19.1）
  - `Assets/TreeParams.asset`（angle 25，initialWidth 0.1）—— 位于 Assets 根目录，**疑似散落的重复资产**，二者数值不同，需确认哪一个才是场景实际引用的。

---

## 3. Lsystemv2 (v2) — 当前 3D 生态版本

### 核心管线（生成一株植物）
| 文件 | 作用 |
|---|---|
| `PlantDefinition.cs` | **数据中枢**。`PlantDefinition` ScriptableObject 定义一种植物的全部属性，并包含多个可序列化子类：`LSystemPreset`、`TrunkSettings`、`FoliageSettings`、`WindResponse`、`GrowthSettings`，以及枚举 `PlantCategory / LeafShape / LeafDistribution / GrowthSequence`。 |
| `LSystemGenerator3D.cs` | L-System 字符串生成器（静态类，**类名实为 `LSystemGenerator`**）。支持确定性、加权随机、情绪驱动迭代次数；内含 `Presets` 静态规则库与 `AnalyzeString` 复杂度分析工具。 |
| `TurtleInterpreter3D.cs` | **完整 3D 海龟解释器**。支持 `F G f + - ^ & / \ \| [ ] !` 全套符号，记录父段索引（供网格顶点共享）、向性(tropism)、宽度曲线、深度衰减等。 |
| `TreeMeshBuilder.cs` | 由分支段构建高质量树干网格（**相邻段共享顶点**，消除接缝）。支持基部膨大(baobab)、表面噪声、扭转、UV2 生长编码、顶点色风权重，并输出**叶片附着点**。另有 `BuildFlat`（2D 风格）。 |
| `FoliageGenerator.cs` | 在叶片附着点上生成叶片网格。支持 Oval/Pointed/Round/Needle/Fan/Heart/Compound 七种叶形，**双面几何**（正反面法线正确），可导出 GPU 实例矩阵。 |
| `PlantController.cs` | **单株植物 MonoBehaviour 入口**。串联 生成器→解释器→树干网格→叶片，管理材质、情绪属性、生长动画。 |

### 编排 / 环境 / 渲染
| 文件 | 作用 |
|---|---|
| `PlantGrowthSequencer.cs` | 让多个 `PlantController` 按顺序/交错/循环播放生长动画。可自动发现子物体中的植物。 |
| `EcosystemManager.cs` | **整片生态系统管理器**。按密度程序化布置 baobab/acacia/灌木，避让中心区，驱动草系统与风系统，支持全局情绪平滑过渡。文件内还定义了 `BiomePreset` ScriptableObject。 |
| `GrassSystem.cs` | GPU 实例化草地系统（`Graphics.DrawMeshInstanced`），可渲染海量草叶并响应风与情绪。文件内还定义了 `GrassPreset` ScriptableObject。 |
| `WindSystem.cs` | 全局风控制器（单例）。生成基础风 + 阵风 + 湍流，受情绪影响，并通过 `Shader.SetGlobalXxx` 把风数据广播给所有植被着色器。 |
| `Vegetation.shader` | URP 植被着色器 `GlimmerDiary/Vegetation`。支持风动画、生长溶解、树皮/叶片模式、次表面散射，读取 `WindSystem` 设置的全局风变量。 |
| `PlantPresets.cs` | 两部分：① 顶部**大段注释**，是 Baobab/Acacia/Shrub/草 等预设的**手动配置说明书**；② 底部 `PlantPresets` 静态类，用代码 `ApplyXxxPreset(def)` 给 `PlantDefinition` 填充预设值。 |
| `*.meta` | Unity 元文件。 |

### ⚠️ v2 的重复 / 可能无用项
- **`PlantPresets.cs` 与项目根目录的 `Assets/LSystem_Setup_Guide.md`（460 行）内容重叠**：两者都在描述如何手动创建 Baobab/Acacia 等预设。注释版说明与独立 md 指南属于**信息冗余**，建议二选一维护。
- **`LSystemGenerator3D.cs` 中的 `Presets` 静态字典**（BinaryTree / NaturalTree / Baobab / Acacia / Shrub / Weeping / Conifer …）目前看起来**未被任何代码引用**——实际预设来自 `PlantPresets.cs` 和 `.asset` 资产。属潜在死代码（保留作参考库亦可）。
- **`PlantPresets.cs` 的 `ApplyXxxPreset` 方法**未在当前代码中被调用（预设通常直接做成 `.asset`），是否保留取决于是否还需运行时生成预设。
- 文件名 `LSystemGenerator3D.cs` 与 `TreeMeshBuilder.cs` 中的**类名**分别是 `LSystemGenerator` 和 `TreeMeshBuilder`，**文件名与类名不一致**（v1 也各有同名类），阅读时容易混淆，但因命名空间隔离不影响编译。

---

## 4. 重复 / 命名冲突一览（跨两个文件夹）

| 类名 | v1 位置 | v2 位置 | 是否冲突 |
|---|---|---|---|
| `LSystemGenerator` | `Lsystem/LSystemGenerator.cs`（全局） | `Lsystemv2/LSystemGenerator3D.cs`（`GlimmerDiary.Flora`） | 否（命名空间隔离） |
| `TurtleInterpreter` / `TurtleInterpreter3D` | `Lsystem/TurtleInterpreter.cs` | `Lsystemv2/TurtleInterpreter3D.cs` | 否（不同类名+命名空间） |
| `TreeMeshBuilder` | `Lsystem/TreeMeshBuilder.cs`（全局） | `Lsystemv2/TreeMeshBuilder.cs`（`GlimmerDiary.Flora`） | 否（命名空间隔离） |
| `BranchSegment` (struct) | `TurtleInterpreter` 内 | `TurtleInterpreter3D` 内 | 否（嵌套类型） |

---

## 5. 引用情况（决定能否删除 v1）

- **v1 仍被引用**：`Assets/Scenes/SampleScene.unity`、`Assets/1.unity`、`Assets/_Recovery/*.unity` 中含有 `TreeController` / `TreeParameters`。
- **v2 被引用**：`Assets/1.unity`、`Assets/GlimmerDiary/Flora/Prefabs/PlantGrowthTemplate.prefab`。

> 因此**不能直接删除 v1 文件夹**——会让上述场景里的脚本组件丢失（变成 Missing Script）。
> 若要废弃 v1，应先把场景迁移到 v2 的 `PlantController`，再删除并清理对应 `.meta` 与散落的 `Assets/TreeParams.asset`。

---

## 6. 建议（仅建议，未执行）

1. **清理 v1 小瑕疵**：移除 `TreeMeshBuilder.cs` 里多余的 `using`，删掉重复的 `SetTriangles(wood_triangles,0)`。
2. **统一预设文档**：`PlantPresets.cs` 注释 与 `LSystem_Setup_Guide.md` 二选一。
3. **处理重复资产**：确认 `Assets/TreeParams.asset` 与 `Lsystem/TreeParams.asset` 哪个在用，删除/归位另一个。
4. **评估死代码**：`LSystemGenerator3D.Presets`、`PlantPresets.ApplyXxxPreset` 若确无引用，可移入文档或删除。
5. **长期**：把场景从 v1 迁移到 v2 后整体下线 `Lsystem` 文件夹。
