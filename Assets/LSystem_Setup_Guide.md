# L-System 植物系统 - 完整初始化与调参指南

## 目录
1. [系统架构概览](#1-系统架构概览)
2. [创建 PlantDefinition 资源](#2-创建-plantdefinition-资源)
3. [配置 L-System 规则](#3-配置-l-system-规则)
4. [调整树干参数](#4-调整树干参数)
5. [配置树叶](#5-配置树叶)
6. [设置 EcosystemManager](#6-设置-ecosystemmanager)
7. [常见问题排查](#7-常见问题排查)
8. [预设参数参考](#8-预设参数参考)

---

## 1. 系统架构概览

```
┌─────────────────────────────────────────────────────────────┐
│                     EcosystemManager                         │
│  (场景中的主控制器，管理所有植物的生成和情绪响应)              │
└─────────────────────────────────────────────────────────────┘
                              │
                              ▼
┌─────────────────────────────────────────────────────────────┐
│                     PlantDefinition (ScriptableObject)       │
│  (定义一种植物的所有参数：L-System规则、树干、树叶等)         │
└─────────────────────────────────────────────────────────────┘
                              │
            ┌─────────────────┼─────────────────┐
            ▼                 ▼                 ▼
    ┌──────────────┐  ┌──────────────┐  ┌──────────────┐
    │LSystemPreset │  │TrunkSettings │  │FoliageSettings│
    │ (生长规则)    │  │ (树干外观)    │  │ (树叶外观)    │
    └──────────────┘  └──────────────┘  └──────────────┘
                              │
                              ▼
┌─────────────────────────────────────────────────────────────┐
│                    生成流程                                  │
│                                                              │
│  LSystemGenerator  →  TurtleInterpreter3D  →  TreeMeshBuilder│
│   (生成字符串)         (解释为几何数据)        (构建Mesh)      │
│                                                              │
│                              ↓                               │
│                      FoliageGenerator                        │
│                       (生成树叶)                              │
└─────────────────────────────────────────────────────────────┘
```

---

## 2. 创建 PlantDefinition 资源

### 步骤 1：在 Unity 中创建资源文件

1. 在 Project 窗口右键
2. 选择 `Create → GlimmerDiary → Flora → Plant Definition`
3. 命名为你想要的植物名称（如 "Acacia", "Shrub", "Pine"）

### 步骤 2：基础设置

在 Inspector 中配置：

```
Identity
├── Plant Name: "金合欢树"          // 显示名称
├── Category: Tree                   // Tree/Shrub/Grass/Flower
└── Preview Icon: (可选)             // 预览图标
```

---

## 3. 配置 L-System 规则

这是植物形态的核心。在 `L-System Configuration` 部分：

### 基础参数

```
L-System Preset
├── Axiom: "X"                       // 起始符号，通常用 X
│
├── Rules:                           // 生长规则列表
│   └── [0] Symbol: X
│       Replacement: "F+[[X]-X]-F[-FX]+X"
│
├── Base Iterations: 4               // 默认迭代次数 (越大越复杂)
├── Min Iterations: 2                // 最小迭代 (情绪低落时)
├── Max Iterations: 6                // 最大迭代 (情绪高涨时)
│
├── Base Angle: 25                   // 分支角度 (度)
├── Angle Variation: 5               // 角度随机变化范围
│
├── Base Step Length: 0.5            // 每段树枝长度
├── Length Decay Per Iteration: 0.9  // 每深一层长度衰减
│
├── Use 3D Rotation: true            // 启用3D旋转
├── Yaw Angle: 137.5                 // 黄金角，自然分布
└── Pitch Variation: 15              // 俯仰变化
```

### 常用规则模板

**自然树 (Natural Tree)**
```
Axiom: X
Rules:
  X → F+[[X]-X]-F[-FX]+X
  F → FF
```

**灌木 (Shrub)**
```
Axiom: X
Rules:
  X → [+FX][-FX][++FX][--FX]
  F → F
```

**金合欢 (Acacia - 伞形树冠)**
```
Axiom: X
Rules:
  X → FF[++X][--X][+X][-X]
  F → F
```

**松树 (Conifer)**
```
Axiom: X
Rules:
  X → F[&+X][&-X][&++X][&--X]FX
  F → F
```

**垂柳 (Weeping)**
```
Axiom: X
Rules:
  X → F[+X][--X]F[--X][+X]
  F → FF
```

---

## 4. 调整树干参数

在 `Trunk Settings` 部分：

```
Trunk Settings
├── Dimensions
│   ├── Base Radius: 0.15            // 树干底部半径
│   ├── Radius Decay: 0.75           // 每段半径衰减 (0.7-0.85)
│   └── Radial Segments: 8           // 圆柱面细分数 (6-12)
│
├── Shape Modifiers
│   ├── Base Bulge: 0                // 底部膨胀 (猴面包树用 1-2)
│   ├── Twist Per Segment: 0         // 扭曲度
│   └── Surface Noise: 0.05          // 表面噪声 (有机感)
│
└── Materials
    ├── Bark Material: (拖入材质)
    ├── Bark Tint Base: 棕色
    └── Bark Tint Variation: 深棕色
```

### 树干参数调整建议

| 植物类型 | Base Radius | Radius Decay | Radial Segments | Surface Noise |
|---------|-------------|--------------|-----------------|---------------|
| 大树    | 0.2 - 0.4   | 0.75 - 0.80  | 8 - 12          | 0.05 - 0.1    |
| 小树    | 0.1 - 0.2   | 0.80 - 0.85  | 6 - 8           | 0.03 - 0.05   |
| 灌木    | 0.05 - 0.1  | 0.85 - 0.90  | 4 - 6           | 0.02 - 0.03   |

---

## 5. 配置树叶

在 `Foliage Settings` 部分：

```
Foliage Settings
├── Leaf Type
│   ├── Leaf Shape: Oval             // 叶片形状
│   └── Distribution: BranchTips     // 分布方式
│
├── Leaf Dimensions
│   ├── Leaf Size Range: (0.1, 0.3)  // 最小/最大尺寸
│   └── Leaf Density: 0.7            // 密度 (0-1)
│
├── Leaf Clusters
│   ├── Leaves Per Cluster: 5        // 每簇叶片数
│   └── Cluster Spread: 0.3          // 簇内分散程度
│
├── Colors
│   ├── Leaf Color Base: 深绿色
│   ├── Leaf Color Tip: 浅绿色
│   └── Seasonal Variation: 0.3
│
└── Materials
    └── Leaf Material: (拖入材质)
```

### 叶片形状选择

| LeafShape | 适用场景 | 说明 |
|-----------|---------|------|
| Oval      | 大多数阔叶树 | 椭圆形，通用 |
| Pointed   | 金合欢、柳树 | 尖形，细长 |
| Round     | 杨树、白桦 | 圆形 |
| Needle    | 松树、杉树 | 针叶 |
| Fan       | 棕榈树 | 扇形 |
| Heart     | 装饰植物 | 心形 |
| Compound  | 槐树 | 复叶 |
| None      | 枯树、冬季 | 无叶 |

### 叶片分布方式

| Distribution   | 效果 | 适用 |
|---------------|------|------|
| BranchTips    | 只在枝条末端 | 大多数树 |
| AlongBranches | 沿枝条分布 | 灌木 |
| Clusters      | 成簇分布 | 自然感 |
| Umbrella      | 伞形树冠 | 金合欢 |
| Layered       | 分层 | 松树 |
| Weeping       | 下垂 | 垂柳 |

---

## 6. 设置 EcosystemManager

### 步骤 1：在场景中创建空物体

1. 创建空 GameObject，命名为 "Ecosystem"
2. 添加 `EcosystemManager` 组件

### 步骤 2：配置 Inspector

```
Ecosystem Manager
├── Scene Configuration
│   ├── Area Size: (100, 100)        // 生成区域大小
│   └── Ground Plane: (拖入地面)
│
├── Plant Prefabs (拖入你创建的 PlantDefinition)
│   ├── Baobab Definition: (主要大树)
│   ├── Acacia Definition: (中等树)
│   └── Shrub Definition: (灌木)
│
├── Density Settings
│   ├── Major Tree Count: 1          // 大树数量
│   ├── Medium Tree Count: 8         // 中树数量
│   └── Shrub Count: 20              // 灌木数量
│
├── Global Emotion State
│   ├── Global Valence: 0.5          // 情绪正负 (-1到1)
│   └── Global Arousal: 0.3          // 唤醒度 (0到1)
│
├── Placement
│   ├── Min Distance Between Trees: 5
│   ├── Min Distance From Center: 8
│   └── Ground Layer: (选择地面层)
│
└── Runtime
    ├── Generate On Start: true
    ├── Animate Growth On Generate: true
    └── Growth Stagger Delay: 0.5
```

### 步骤 3：设置地面 Layer

1. 选择你的地面物体
2. 在 Inspector 顶部设置 Layer 为 "Ground"（或新建一个）
3. 在 EcosystemManager 的 Ground Layer 中选择这个层

---

## 7. 常见问题排查

### 问题：树枝断裂/不连接

**原因：** TreeMeshBuilder 没有正确共享顶点

**解决：** 确保使用我们修复后的 TreeMeshBuilder.cs

**验证：** 添加以下调试代码到 PlantController：
```csharp
void OnDrawGizmos()
{
    if (segments != null)
        TurtleInterpreter3D.DrawGizmos(segments, transform, true, true);
}
```
黄色线应该连接每个segment到其父级。

---

### 问题：树木生成在空中/地下

**原因：** Ground Layer 设置不正确

**解决：**
1. 确保地面有 Collider
2. 确保 Ground Layer 正确设置
3. 检查 Raycast 是否能检测到地面

---

### 问题：植物太大/太小

**调整：**
- `Base Step Length`: 控制整体高度
- `Base Radius`: 控制粗细
- `PlantController.transform.localScale`: 最终缩放

---

### 问题：迭代次数过多导致卡顿

**症状：** 生成时间很长，或Unity卡死

**解决：**
- 降低 `Max Iterations`（建议不超过6）
- 减少同时生成的植物数量
- 增加 `Growth Stagger Delay`

---

### 问题：树枝方向奇怪

**检查：**
1. `Base Angle` 是否合理（15-45度）
2. `Yaw Angle` 是否设置（推荐137.5度黄金角）
3. 规则字符串是否正确

---

## 8. 预设参数参考

### 金合欢树 (Acacia) - 完整配置

```
L-System Preset:
  Axiom: X
  Rules: X → FF[++X][--X][+X][-X]
  Base Iterations: 4
  Base Angle: 35
  Angle Variation: 8
  Base Step Length: 0.6
  Length Decay: 0.85
  Yaw Angle: 137.5

Trunk Settings:
  Base Radius: 0.12
  Radius Decay: 0.78
  Radial Segments: 8
  Surface Noise: 0.04

Foliage Settings:
  Leaf Shape: Pointed
  Distribution: Umbrella
  Leaf Size Range: (0.08, 0.15)
  Leaves Per Cluster: 8
  Leaf Color Base: #2D5A27
  Leaf Color Tip: #4A7C43
```

### 灌木 (Shrub) - 完整配置

```
L-System Preset:
  Axiom: X
  Rules: X → [+FX][-FX][++FX][--FX]
  Base Iterations: 3
  Base Angle: 30
  Angle Variation: 10
  Base Step Length: 0.3
  Length Decay: 0.9
  Yaw Angle: 90

Trunk Settings:
  Base Radius: 0.04
  Radius Decay: 0.85
  Radial Segments: 5
  Surface Noise: 0.02

Foliage Settings:
  Leaf Shape: Oval
  Distribution: AlongBranches
  Leaf Size Range: (0.05, 0.12)
  Leaves Per Cluster: 4
  Leaf Color Base: #3D6B35
  Leaf Color Tip: #5A8C4F
```

### 松树 (Pine) - 完整配置

```
L-System Preset:
  Axiom: X
  Rules: X → F[&+X][&-X][&++X][&--X]FX
  Base Iterations: 4
  Base Angle: 25
  Angle Variation: 5
  Base Step Length: 0.5
  Length Decay: 0.88
  Yaw Angle: 90

Trunk Settings:
  Base Radius: 0.1
  Radius Decay: 0.82
  Radial Segments: 6
  Surface Noise: 0.03

Foliage Settings:
  Leaf Shape: Needle
  Distribution: Layered
  Leaf Size Range: (0.03, 0.08)
  Leaves Per Cluster: 12
  Leaf Color Base: #1E3D1A
  Leaf Color Tip: #2D5227
```

---

## 快速测试流程

1. **创建 PlantDefinition**
   - 使用上面的"灌木"预设参数（最简单）

2. **创建测试场景**
   - 添加一个 Plane 作为地面
   - 设置 Layer 为 "Ground"
   - 添加 Collider

3. **设置 EcosystemManager**
   - 只设置 Shrub Definition
   - Shrub Count: 5
   - 其他数量设为 0

4. **运行测试**
   - 按 Play
   - 应该看到5棵灌木生成

5. **调整参数**
   - 在运行时调整 `Global Valence` 和 `Global Arousal`
   - 观察植物响应

---

## 下一步

当基础系统工作后，你可以：

1. **添加材质** - 为树干和树叶创建合适的 Shader
2. **添加风动画** - 在 Shader 中实现顶点动画
3. **连接日记系统** - 让情绪数据驱动 `SetEmotionState()`
4. **添加生长动画** - 使用 `PlayGrowthAnimation()`

祝开发顺利！
