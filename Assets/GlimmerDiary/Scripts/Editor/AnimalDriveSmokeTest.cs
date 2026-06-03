#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;
using GlimmerDiary.Data;
using GlimmerDiary.Core;

namespace GlimmerDiary.Editor
{
    // Edit-mode 冒烟测试：不依赖 Play mode / WorldManager，直接驱动 AnimalDriveSystem。
    // 菜单 GlimmerDiary/Test Anxiety Chain 触发，结果输出到 Console。
    public static class AnimalDriveSmokeTest
    {
        [MenuItem("GlimmerDiary/Test Anxiety Chain")]
        public static void RunEmergentAnxietyChain()
        {
            var save  = WorldInitializer.CreateNewWorld();
            var reg   = new EntityRegistry();
            reg.Initialize(save);
            var drive = new AnimalDriveSystem(reg, save);
            drive.SetEnvironment(null, null);   // P2/P3 动力学不读 env/rhythm
            var narrator = new BehaviorNarrator(reg, save);
            narrator.SetEnvironment(null, null);

            var dm   = reg.GetAnimal("deer_mouse");
            var vole = reg.GetAnimal("vole");
            var fox  = reg.GetAnimal("fox");

            Debug.Log("=== EmergentAnxietyChain (EditMode) ===");
            Debug.Log($"初始: 鹿鼠 range={dm.activityRange:F2} @{dm.location} | " +
                      $"田鼠 @{vole.location} | 狐狸 @{fox.location} | 织巢鸟 present={reg.GetAnimal("weaver_bird").isPresent}");

            bool   voleExpanded = false;
            int    expandDay    = -1;
            string expandCause  = "";

            for (int i = 0; i < 24; i++)
            {
                // 中性偏低确定性，直接设 E_env（绕过情绪惯性，结果确定）
                save.currentEEnv = new EmotionVector { V = 0f, A = 0.3f, T = 1f, S = 0f, C = 0.45f };
                save.gameTime.Advance(1);
                drive.Tick(save.gameTime);
                narrator.Narrate(save.gameTime);

                Debug.Log($"Day {i + 1}: 鹿鼠[range={dm.activityRange:F2} {dm.behavior.drive}] " +
                          $"田鼠[{vole.behavior.drive} @{vole.location}] 狐狸[{fox.behavior.drive}/{fox.behavior.cause} @{fox.location}]");

                if (!voleExpanded && (vole.behavior.drive == "Expand" || vole.location == "center"))
                {
                    voleExpanded = true;
                    expandDay    = i + 1;
                    expandCause  = vole.behavior.cause;
                }
            }

            bool a1 = dm.activityRange < 0.5f;
            bool a2 = voleExpanded;
            bool a3 = expandCause == "DeerMouseWithdrew";
            bool a4 = save.pendingChronicles.Exists(c => c.eventId == "behavior_vole:Expand:DeerMouseWithdrew");
            bool a5 = save.pendingChronicles.Exists(c => c.eventId == "behavior_fox:Patrol:RodentExpansion");
            Debug.Log($"[{(a1 ? "PASS" : "FAIL")}] 鹿鼠 activityRange < 0.5  (实际 {dm.activityRange:F2})");
            Debug.Log($"[{(a2 ? "PASS" : "FAIL")}] 田鼠发生扩张  (第 {expandDay} 天)");
            Debug.Log($"[{(a3 ? "PASS" : "FAIL")}] 扩张成因为跨实体项 DeerMouseWithdrew  (实际 {expandCause})");
            Debug.Log($"[{(a4 ? "PASS" : "FAIL")}] 文本层产出田鼠扩张文案 (cause=DeerMouseWithdrew)");
            Debug.Log($"[{(a5 ? "PASS" : "FAIL")}] 文本层产出狐狸巡逻文案 (cause=RodentExpansion)");
            foreach (var c in save.pendingChronicles)
                Debug.Log($"    世界志: \"{c.text}\"");
        }

        // P3 链：断枝（NarrativeRule 拥有）→ 织巢鸟离场 → 鹿鼠焦虑 + 文本层产出文案。
        // 隔离狐狸，以单独观察"织巢鸟在/不在 → 鹿鼠焦虑"这条上游链。
        [MenuItem("GlimmerDiary/Test Weaver Chain")]
        public static void RunWeaverChain()
        {
            var save  = WorldInitializer.CreateNewWorld();
            var reg   = new EntityRegistry();
            reg.Initialize(save);
            var drive = new AnimalDriveSystem(reg, save);
            drive.SetEnvironment(null, null);
            var narrator = new BehaviorNarrator(reg, save);
            narrator.SetEnvironment(null, null);

            var fox    = reg.GetAnimal("fox");
            fox.isPresent = false;   // 隔离狐狸，焦虑只由织巢鸟在/不在驱动
            var dm     = reg.GetAnimal("deer_mouse");
            var weaver = reg.GetAnimal("weaver_bird");
            var tree   = reg.GetPlant("baobab_main");

            Debug.Log("=== WeaverChain (EditMode, 狐狸已隔离) ===");

            // Phase A：织巢鸟在场，平静高确定性 → 鹿鼠焦虑下行
            for (int i = 0; i < 4; i++)
            {
                save.currentEEnv = new EmotionVector { V = 0f, A = 0.3f, T = 1f, S = 0f, C = 0.6f };
                save.gameTime.Advance(1);
                drive.Tick(save.gameTime);
                narrator.Narrate(save.gameTime);
                Debug.Log($"A Day {i + 1}: 织巢鸟 present={weaver.isPresent} | 鹿鼠 anx={dm.internalState.anxiety:F2} range={dm.activityRange:F2}");
            }
            float anxBefore = dm.internalState.anxiety;

            // 模拟 NarrativeRule 断枝：写入一条永久损伤（驱动系统对其结果做反应）
            tree.permanentDamages.Add(new PermanentDamageRecord
            {
                date = save.gameTime.ToKeyString(), damageType = "branch_broken",
                description = "主枝东侧第一根侧枝断落，断口朝下", triggeredBy = "smoketest"
            });
            Debug.Log("** 模拟断枝：baobab_main 写入一条 permanentDamage **");

            // Phase B：驱动系统检测断枝边沿 → 织巢鸟离场 → 鹿鼠焦虑回升
            for (int i = 0; i < 10; i++)
            {
                save.currentEEnv = new EmotionVector { V = 0f, A = 0.3f, T = 1f, S = 0f, C = 0.6f };
                save.gameTime.Advance(1);
                drive.Tick(save.gameTime);
                narrator.Narrate(save.gameTime);
                Debug.Log($"B Day {i + 1}: 织巢鸟 present={weaver.isPresent} | 鹿鼠 anx={dm.internalState.anxiety:F2} range={dm.activityRange:F2}");
            }

            bool weaverGone   = !weaver.isPresent;
            bool anxietyRose  = dm.internalState.anxiety > anxBefore + 0.1f;
            bool hasBreakEvt  = save.worldEvents.Exists(e => e.type == WorldEventType.TreeBranchBroke);
            bool hasDepartEvt = save.worldEvents.Exists(e => e.type == WorldEventType.WeaverBirdDeparted);
            bool hasProse     = save.pendingChronicles.Exists(c => c.eventId == "event_WeaverBirdDeparted");

            Debug.Log($"[{(weaverGone   ? "PASS" : "FAIL")}] 织巢鸟离场");
            Debug.Log($"[{(anxietyRose  ? "PASS" : "FAIL")}] 鹿鼠焦虑回升  (前 {anxBefore:F2} → 后 {dm.internalState.anxiety:F2})");
            Debug.Log($"[{(hasBreakEvt  ? "PASS" : "FAIL")}] 事件 TreeBranchBroke 已发");
            Debug.Log($"[{(hasDepartEvt ? "PASS" : "FAIL")}] 事件 WeaverBirdDeparted 已发");
            Debug.Log($"[{(hasProse     ? "PASS" : "FAIL")}] 文本层产出织巢鸟离场文案");
            var prose = save.pendingChronicles.Find(c => c.eventId == "event_WeaverBirdDeparted");
            if (prose != null) Debug.Log($"    → \"{prose.text}\"");
        }
    }
}
#endif
