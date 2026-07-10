---
name: layer-boundary-check
description: 架构边界与单一写者原则校验
---

# layer-boundary-check

## 触发时机
每次跨文件修改后、提交前、Workflow 各阶段完成后、qa-tester 周期性验证。

## L1 违规
grep `using UnityEngine` in `Assets/Emotion_engine_development/` (排除 main.cs)
grep `WorldManager|WorldSaveData|AnimalEntity|NarrativeRuleEngine` in `Assets/Emotion_engine_development/`

## L2 违规
grep `new EmotionAnalyzer|EmotionAnalyzer\.Analyze|Python|subprocess` in `Assets/GlimmerDiary/Scripts/Core/` and `Data/`
grep `Material\s+\w+|Shader\.|Mesh\.|Camera\.|RenderSettings|GameObject\.|Transform\.|Renderer\.` in `Assets/GlimmerDiary/Scripts/Core/` and `Data/`
grep `currentEEnv\s*=\s*new EmotionVector` in `Assets/GlimmerDiary/Scripts/` (允许 EmotionInertiaSystem.cs, WorldManager.cs)
grep `UnityEngine\.UI|UnityEngine\.EventSystems|Canvas|Button` in `Assets/GlimmerDiary/Scripts/Core/` and `Data/`

## L3 违规
grep `currentEEnv\s*=|\.behavior\s*=|\s*\.internalState\s*=|\.activityRange\s*=|\s*\.location\s*=\s*"|\.isPresent\s*=|\.waterLevel\s*=|\.vitality\s*=` in `Assets/Scripts/` and `Assets/Script/` and `Assets/ImportedAssets/PleebieJeebies/`
grep `new AnimalEntity|new PlantEntity|new LocationEntity|new WorldSaveData|new WorldEnvironmentState` in `Assets/Scripts/` and `Assets/Script/` and `Assets/ImportedAssets/PleebieJeebies/`
grep `OnJournalSubmitted|InjectEmotion|SimulatePass|WorldTick|ReinitializeWithSave` in `Assets/Scripts/` and `Assets/Script/` and `Assets/ImportedAssets/PleebieJeebies/`

## 单一写者违规
S1: grep `\.behavior\s*=|\s*\.internalState\s*=\s*new Internal|\.activityRange\s*=` in `Assets/GlimmerDiary/Scripts/Core/` (排除 AnimalDriveSystem.cs, BehaviorNarrator.cs; 允许 WorldInitializer.cs)
S2: grep `PermanentDamageRecord|\.permanentDamages\.Add` in `Assets/GlimmerDiary/Scripts/Core/` (排除 NarrativeRuleEngine.cs; 允许 AnimalDriveSystem.cs TickTree)
S3: grep `File\.Write|FileStream|StreamWriter|JsonUtility\.ToJson|JsonConvert\.` in `Assets/GlimmerDiary/Scripts/` (允许 SaveSystem.cs)
S4: grep `\.Initialize\(|\.Clear\(|\.Add\(.*Entity` in `Assets/GlimmerDiary/Scripts/` (允许 EntityRegistry.cs, WorldManager.cs)

## 追加写入约束
A1: grep `worldEvents\.Remove|worldEvents\.Clear` in `Assets/GlimmerDiary/Scripts/`
A2: grep `permanentDamages\.Remove|permanentDamages\.Clear` in `Assets/GlimmerDiary/Scripts/`
A3: grep `emotionHistory\.Remove|emotionHistory\.Clear` in `Assets/GlimmerDiary/Scripts/`

## 执行方式
快速扫描: git diff 文件 + 仅报告新增行违规
全量扫描: 所有目录 + 完整违规列表

## 输出格式
列出每层违规项、文件路径、行号、建议修复方案。
