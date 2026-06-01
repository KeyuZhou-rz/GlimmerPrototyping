# Glimmer Diary — 项目结构与数据流向

## 核心理念

这不是一个有机制的游戏，而是一个记住用户情感历史的活世界。世界拥有自己的生命，独立运转，不对玩家输入做直接响应。

---

## 目录结构

```
Assets/GlimmerDiary/
├── Scripts/
│   ├── Core/                        ← 世界模拟层（Layer 2）
│   │   ├── EmotionInertiaSystem.cs  ← 情感惯性：E_env 的指数平滑更新
│   │   └── NaturalRhythmSystem.cs   ← 自然节律：季节/周期对世界的基线漂移
│   ├── Data/                        ← 纯数据结构（无 Unity 依赖）
│   │   └── EmotionData.cs           ← EmotionVector / JournalEntry / Snapshot 定义
│   └── Utils/                       ← 工具类（待填充）
├── ScriptableObjects/               ← 叙事规则模板（Week 4）
└── Resources/
    └── SaveData/                    ← 存档 JSON 文件
```

---

## 三层架构（边界不可穿越）

```
Layer 1: SentimentEngine
  输入: 日记原文
  输出: EmotionVector(V, A, T, S, C)
  规则: 不调用 Unity API，不访问世界状态

        ↓ EmotionVector（参数传入，不主动拉取）

Layer 2: WorldSimulator
  拥有: EmotionInertiaSystem（E_env）
        NaturalRhythmSystem（季节节律）
        EventSystem（永久性世界事件）
  规则: 所有永久变更写入只追加日志

        ↓ E_env（世界情感状态，非 E_current）

Layer 3: Visual / Audio
  规则: 只读世界状态，不写入
        所有参数绑定 E_env，不绑定 E_current
```

---

## 数据流向

```
玩家写日记
    │
    ▼
SentimentEngine → EmotionVector { V, A, T, S, C }
    │
    ▼
EmotionInertiaSystem.Update(eCurrent)
    │   alpha = Lerp(0.1, 0.3, C)   ← C 越高响应越快
    │   E_env(t) = α·E_current + (1-α)·E_env(t-1)
    ▼
CurrentEEnv（世界情感状态）
    │
    ├── NaturalRhythmSystem.ApplyBiasTo(eEnv)
    │       季节偏置（strength=0.05）独立叠加
    │
    ▼
Visual/Audio 层读取并渲染世界
```

---

## EmotionVector 维度说明

| 字段 | 含义 | 范围 |
|------|------|------|
| V | 效价（Valence）| -1.0 ~ 1.0 |
| A | 唤醒度（Arousal）| 0.0 ~ 1.0 |
| T | 时间感（Temporality）| 0.0 ~ 1.0 |
| S | 社会性（Sociality）| 0.0 ~ 1.0 |
| C | 确定性（Certainty）| 0.0 ~ 1.0 |

---

## 自然节律偏置（NaturalRhythmSystem）

| 季节 | V | A | T | S |
|------|---|---|---|---|
| 春 | +0.08 | +0.05 | — | — |
| 夏 | +0.04 | +0.12 | -0.08 | — |
| 秋 | -0.08 | — | +0.10 | — |
| 冬 | -0.04 | -0.10 | — | -0.06 |

周末额外：S +0.08

偏置以 `strength=0.05` 权重叠加，不直接覆盖 E_env。
