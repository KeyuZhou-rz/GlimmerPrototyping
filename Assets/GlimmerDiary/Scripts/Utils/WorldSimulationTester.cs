using UnityEngine;
using GlimmerDiary.Data;
using GlimmerDiary.Core;

namespace GlimmerDiary.Utils
{
    /// <summary>
    /// 挂在场景里任意 GameObject 上。
    /// Inspector 里选场景，右键 Component → Run Selected Scenario，
    /// 或勾选 runOnStart 后 Play。所有结果输出到 Console。
    /// </summary>
    public class WorldSimulationTester : MonoBehaviour
    {
        [Header("测试配置")]
        public bool runOnStart       = false;
        public TestScenario scenario = TestScenario.FloodAndVoleRelocation;

        public enum TestScenario
        {
            FloodAndVoleRelocation,  // 连续悲伤 → 低洼水位上涨 → 田鼠搬家
            BaobabBranchBreak,       // 持续极端情绪 → 猴面包树折枝（永久事件）
            MigratoryBirdArrival,    // 秋季+正面情绪 → 候鸟迁来
            LongTermDecay,           // 14天负面 → 衰败积累
            FullWeekCycle,           // 7天推进 → 验证时间和历史记录
            EmergentAnxietyChain,    // 狐狸临近 → 鹿鼠焦虑 → 收缩 → 田鼠扩张（状态涌现）
            LongChainForLetter,      // 30 天长链：断枝→离巢→退守→扩张→巡逻+候鸟+雨语料 → 攒一批世界志给"信"
            DroughtRecovery,         // 45 天无雨 → debt>0.6（地裂线）；再 18 天暴雨 → debt 回落线下
            DandelionDrift,          // 蒲公英：干风不落种（湿度门）→ 回湿+风峰 → 落种 lowland（冷却 ≤2 条）
            WaterRetention           // 植被捂水：雨后低洼（veg 0.7）水退慢、石头区（veg 0.25）干透 + 捂水语料
        }

        void Start()
        {
            if (runOnStart) RunScenario();
        }

        [ContextMenu("Run Selected Scenario")]
        public void RunScenario()
        {
            switch (scenario)
            {
                case TestScenario.FloodAndVoleRelocation: Test_FloodAndVoleRelocation(); break;
                case TestScenario.BaobabBranchBreak:      Test_BaobabBranchBreak();      break;
                case TestScenario.MigratoryBirdArrival:   Test_MigratoryBirdArrival();   break;
                case TestScenario.LongTermDecay:          Test_LongTermDecay();          break;
                case TestScenario.FullWeekCycle:          Test_FullWeekCycle();          break;
                case TestScenario.EmergentAnxietyChain:   Test_EmergentAnxietyChain();   break;
                case TestScenario.LongChainForLetter:     Test_LongChainForLetter();     break;
                case TestScenario.DroughtRecovery:        Test_DroughtRecovery();        break;
                case TestScenario.DandelionDrift:         Test_DandelionDrift();         break;
                case TestScenario.WaterRetention:         Test_WaterRetention();         break;
            }
        }

        // ─────────────────────────────────────────────
        // 场景一：连续悲伤情绪 → 低洼水位上涨 → 田鼠搬家
        //
        // 机制：每次提交 V=-0.8 使 E_env.V 缓慢下降 → Rainfall 上升
        //       PropagateEnvironmentToLocations 把 Rainfall 转化为
        //       lowland.waterLevel 累积；约第 5 天超过 0.65 阈值触发规则。
        //
        // 预期：vole.location 从 lowland 变为 highland_east
        //       pendingChronicles 里出现 vole_relocate_flood 条目
        // ─────────────────────────────────────────────
        void Test_FloodAndVoleRelocation()
        {
            Log("=== 场景一：洪水与田鼠搬家 ===");
            ResetWorld();

            for (int i = 0; i < 5; i++)
            {
                SubmitEmotion(V: -0.8f, A: 0.7f, C: 0.4f);
                Log($"  Day {i + 1}: E_env.V={EEnv.V:F2}  " +
                    $"Rain={EnvState.Rainfall:F2}  " +
                    $"Lowland.Water={GetLocation("lowland").waterLevel:F2}");
            }

            var vole = GetAnimal("vole");
            AssertEqual("vole.location", vole.location, "highland_east");
            AssertChronicleContains("vole_relocate_flood");
            LogWorldState();
        }

        // ─────────────────────────────────────────────
        // 场景二：持续极端情绪 → 猴面包树折枝（永久事件）
        //
        // 机制：EmotionInertia alpha≈0.16，单次提交无法让 A 突破 0.7。
        //       需要约 10 次才能让 E_env.A 超过阈值，12 次确保触发。
        //       折枝后：冷却期 60 天内同样条件不再生成第二条世界志。
        //
        // 预期：baobab_main.permanentDamages 增加一条记录
        //       pendingChronicles 里出现 baobab_branch_broken 条目
        //       第 13 次提交不生成第二条
        // ─────────────────────────────────────────────
        void Test_BaobabBranchBreak()
        {
            Log("=== 场景二：猴面包树折枝 ===");
            ResetWorld();

            for (int i = 0; i < 12; i++)
            {
                SubmitEmotion(V: -0.9f, A: 0.85f, C: 0.3f);
                Log($"  Day {i + 1}: E_env.V={EEnv.V:F2}  E_env.A={EEnv.A:F2}");
            }

            var baobab = GetPlant("baobab_main");
            AssertTrue("baobab 有永久损伤记录", baobab.permanentDamages.Count > 0);
            AssertChronicleContains("baobab_branch_broken");

            // 冷却期内不应再次触发
            int countBefore = WorldManager.Instance._saveData.pendingChronicles.Count;
            SubmitEmotion(V: -0.9f, A: 0.85f, C: 0.3f);
            int countAfter  = WorldManager.Instance._saveData.pendingChronicles.Count;
            AssertTrue("冷却期内不重复触发", countAfter == countBefore);

            LogWorldState();
        }

        // ─────────────────────────────────────────────
        // 场景三：秋季（初始月份=9）+ 正面情绪 → 候鸟迁来
        //
        // 机制：初始世界 month=9 满足季节条件，E_env.V 约第 2 天超过 0.2
        //
        // 预期：migratory_bird.isPresent = True
        //       migratory_bird.location = riverbank
        //       pendingChronicles 里出现 migratory_bird_arrival 条目
        // ─────────────────────────────────────────────
        void Test_MigratoryBirdArrival()
        {
            Log("=== 场景三：候鸟迁来 ===");
            ResetWorld();

            AssertEqual("候鸟初始不在场",
                GetAnimal("migratory_bird").isPresent.ToString(), "False");

            for (int i = 0; i < 3; i++)
                SubmitEmotion(V: 0.6f, A: 0.4f, C: 0.7f);

            var bird = GetAnimal("migratory_bird");
            AssertEqual("候鸟迁来后 isPresent", bird.isPresent.ToString(), "True");
            AssertEqual("候鸟位置", bird.location, "riverbank");
            AssertChronicleContains("migratory_bird_arrival");
            LogWorldState();
        }

        // ─────────────────────────────────────────────
        // 场景四：连续 14 天负面情绪 → DecayLevel 累积
        //
        // 预期：Environment.State.DecayLevel 明显高于初始值（初始≈0）
        //       VegetationDensity 下降
        // ─────────────────────────────────────────────
        void Test_LongTermDecay()
        {
            Log("=== 场景四：长期衰败积累 ===");
            ResetWorld();

            float initDecay = EnvState.DecayLevel;
            float initVeg   = EnvState.VegetationDensity;
            Log($"  初始：Decay={initDecay:F3}  Vegetation={initVeg:F3}");

            for (int i = 0; i < 14; i++)
                SubmitEmotion(V: -0.7f, A: 0.3f, C: 0.2f);

            Log($"  14天后：Decay={EnvState.DecayLevel:F3}  " +
                $"Vegetation={EnvState.VegetationDensity:F3}");

            AssertTrue("DecayLevel 上升",      EnvState.DecayLevel      > initDecay);
            AssertTrue("VegetationDensity 下降", EnvState.VegetationDensity < initVeg);
            LogWorldState();
        }

        // ─────────────────────────────────────────────
        // 场景五：完整 7 天推进
        //
        // 预期：gameTime.day = 8（从第 1 天推进 7 次）
        //       emotionHistory 条目数 >= 7
        // ─────────────────────────────────────────────
        void Test_FullWeekCycle()
        {
            Log("=== 场景五：完整 7 天推进 ===");
            ResetWorld();

            Log($"  起始日期：{WorldManager.Instance._saveData.gameTime.ToDisplayString()}");

            for (int i = 0; i < 7; i++)
                SubmitEmotion(V: 0f, A: 0.3f, C: 0.5f);

            var dt = WorldManager.Instance._saveData.gameTime;
            Log($"  7天后日期：{dt.ToDisplayString()}");

            AssertEqual("日期推进 7 天", dt.day.ToString(), "8");
            AssertTrue("情绪历史 >= 7 条",
                WorldManager.Instance._saveData.emotionHistory.Count >= 7);
            LogWorldState();
        }

        // ─────────────────────────────────────────────
        // 场景六：状态涌现链（AnimalDriveSystem，零硬编码联动）
        //
        // 机制：狐狸初始在 highland_east，与鹿鼠同区 → 鹿鼠 anxiety 每 tick 飙升
        //       → activityRange 收缩 → center 腾空 → 田鼠 expansionPressure 累积
        //       → 田鼠向 center 扩张。整条链只靠内部状态向量传导，无 if(A)then(B)。
        //
        // 预期：鹿鼠 activityRange 明显下降（< 初始 0.8）
        //       田鼠最终 behavior.drive == "Expand" 或 location == "center"
        //       田鼠扩张时 behavior.cause == "DeerMouseWithdrew"（暗示跨实体成因）
        //       相位滞后：田鼠读的是鹿鼠"上一 tick"的 activityRange
        // ─────────────────────────────────────────────
        void Test_EmergentAnxietyChain()
        {
            Log("=== 场景六：状态涌现链（焦虑传导 → 田鼠扩张）===");
            ResetWorld();

            var dm   = GetAnimal("deer_mouse");
            var vole = GetAnimal("vole");
            Log($"  初始: 鹿鼠 range={dm.activityRange:F2} @{dm.location} | " +
                $"田鼠 @{vole.location} | 狐狸 @{GetAnimal("fox").location}");

            bool   voleExpanded = false;
            int    expandDay    = -1;
            string expandCause  = "";
            for (int i = 0; i < 14; i++)
            {
                // 中性偏低确定性：让 E_env 平稳，专注观察实体间传导
                SubmitEmotion(V: 0f, A: 0.3f, C: 0.45f);

                Log($"  Day {i + 1}: 鹿鼠[range={dm.activityRange:F2} {dm.behavior.drive}/{dm.behavior.cause}] " +
                    $"田鼠[{vole.behavior.drive}/{vole.behavior.cause} @{vole.location}]");

                if (!voleExpanded && (vole.behavior.drive == "Expand" || vole.location == "center"))
                {
                    voleExpanded = true;
                    expandDay    = i + 1;
                    expandCause  = vole.behavior.cause;   // 捕获扩张当刻的成因
                }
            }

            AssertTrue("鹿鼠 activityRange 收缩（< 0.5）", dm.activityRange < 0.5f);
            AssertTrue("田鼠发生扩张（drive=Expand 或 已在 center）", voleExpanded);
            if (voleExpanded)
                AssertEqual($"田鼠扩张(第{expandDay}天)成因为跨实体项", expandCause, "DeerMouseWithdrew");
            Log($"  末态: 鹿鼠 range={dm.activityRange:F2} | 田鼠 @{vole.location} " +
                $"drive={vole.behavior.drive} cause={vole.behavior.cause}");
            LogWorldState();
        }

        // ─────────────────────────────────────────────
        // 场景七：长链叙事（为"信"攒一批世界志）
        //
        // 链：12 天极端风暴 → 断枝 → 织巢鸟离巢 → 鹿鼠焦虑退守 → center 腾空
        //     → 田鼠扩张 → 狐狸巡逻宣示；再 5 天温和正面 → 秋季候鸟迁来；
        //     末 3 天暴雨 → WeatherHarsh 语料。
        // 预期：pendingChronicles 留下 ~8+ 条，按 L 在信里逐批读完。
        // ─────────────────────────────────────────────
        void Test_LongChainForLetter()
        {
            Log("=== 场景七：长链叙事（信的世界志来源）===");
            ResetWorld();

            // 阶段一（1-12 天）：极端负面+高唤醒 → 断枝 + 可能的洪水搬家
            for (int i = 0; i < 12; i++)
            {
                SubmitEmotion(V: -0.9f, A: 0.85f, C: 0.3f);
                var dm = GetAnimal("deer_mouse");
                Log($"  D{i + 1}: 织巢鸟在场={GetAnimal("weaver_bird").isPresent} " +
                    $"鹿鼠[anx={dm.internalState.anxiety:F2} range={dm.activityRange:F2} {dm.behavior.drive}]");
            }

            // 阶段二（13-22 天）：中性 → 鹿鼠退守 → center 腾空 → 田鼠扩张 → 狐狸巡逻
            for (int i = 0; i < 10; i++)
            {
                SubmitEmotion(V: 0f, A: 0.3f, C: 0.45f);
                var vole = GetAnimal("vole");
                Log($"  D{13 + i}: 田鼠[{vole.behavior.drive} @{vole.location}] " +
                    $"狐狸[{GetAnimal("fox").behavior.drive}]");
            }

            // 阶段三（23-27 天）：温和正面 → 秋季候鸟迁来（初始 month=9 满足 9-11 月窗口）
            for (int i = 0; i < 5; i++)
                SubmitEmotion(V: 0.6f, A: 0.4f, C: 0.7f);
            var bird = GetAnimal("migratory_bird");
            Log($"  D27: 候鸟在场={bird.isPresent} @{bird.location}");

            // 阶段四（28-30 天）：暴雨 → WeatherHarsh 语料（狐狸歇窝/候鸟低伏）
            for (int i = 0; i < 3; i++)
                SubmitEmotion(V: -0.8f, A: 0.7f, C: 0.4f);

            var pending = WorldManager.Instance._saveData.pendingChronicles;
            Log($"  ── 信的世界志：{pending.Count} 条待显示（按 L 阅读）──");
            foreach (var c in pending)
                Log($"    · [{c.eventId}] {c.text}");
            LogWorldState();
        }

        // ─────────────────────────────────────────────
        // 场景八：旱债累积与雨季恢复（Batch 2 droughtDebt 验证）
        //
        // 机制：V≥0 → Wetness=0 → 每 tick debt += (季节基准雨 0.30 − 0)×0.05
        //       45 天无雨 → debt≈0.68 过 0.6 地裂/收敛线；
        //       再 18 天 V=-0.9 暴雨 → debt 每日扣减，回落线下。
        // 预期：debt 峰值 > 0.6；雨季后 debt < 0.6（ binder 地裂随之撤出）
        // ─────────────────────────────────────────────
        void Test_DroughtRecovery()
        {
            Log("=== 场景八：旱债累积与雨季恢复 ===");
            ResetWorld();

            for (int i = 0; i < 45; i++)
            {
                SubmitEmotion(V: 0.5f, A: 0.3f, C: 0.5f);
                if ((i + 1) % 5 == 0)
                    Log($"  旱 D{i + 1}: debt={EnvState.DroughtDebt:F3} Rain={EnvState.Rainfall:F2}");
            }
            float peak = EnvState.DroughtDebt;
            AssertTrue($"旱债峰值过地裂线 0.6（实际 {peak:F2}）", peak > 0.6f);

            for (int i = 0; i < 18; i++)
            {
                SubmitEmotion(V: -0.9f, A: 0.5f, C: 0.4f);
                Log($"  雨 D{i + 1}: debt={EnvState.DroughtDebt:F3} Rain={EnvState.Rainfall:F2} E_env.V={EEnv.V:F2}");
            }
            AssertTrue($"雨季后旱债回落地裂线下（实际 {EnvState.DroughtDebt:F2}）",
                EnvState.DroughtDebt < 0.6f);
            LogWorldState();
        }

        // ─────────────────────────────────────────────
        // 场景九：蒲公英风絮落种（Batch 3 §5.3 验证）
        //
        // 链：WindSpeed>0.7（A_env≳0.83 → Agitation 过线）∧ 蒲公英在场开花
        //     ∧ lowland.soilMoisture>0.4 → worldEvents 落种 DandelionSeedsDrifted。
        // 阶段：A 静风旱化（低洼 0.65→~0.35）→ B 干风 14 天（风峰但干 → 不落种）
        //       → C 回湿 12 天（湿度回 ~0.8）→ D 湿风 10 天（风峰+湿 → 落种）。
        // 预期：B 末 0 条；D 末 ≥1 条且 ≤2 条（冷却 5 天）；targetId=lowland。
        // ─────────────────────────────────────────────
        void Test_DandelionDrift()
        {
            Log("=== 场景九：蒲公英风絮落种 ===");
            ResetWorld();
            AssertEqual("初始无落种事件", CountDriftEvents().ToString(), "0");

            for (int i = 0; i < 15; i++) SubmitEmotion(V: 0.5f, A: 0.2f, C: 0.5f);   // 静风旱化
            Log($"  旱化后: moisture={GetLocation("lowland").soilMoisture:F2} Wind={EnvState.WindSpeed:F2}");

            for (int i = 0; i < 14; i++) SubmitEmotion(V: 0.5f, A: 1.0f, C: 0.5f);   // 干风
            Log($"  干风后: Wind={EnvState.WindSpeed:F2} moisture={GetLocation("lowland").soilMoisture:F2} " +
                $"落种数={CountDriftEvents()}");
            AssertTrue("风峰且低洼干 → 不落种（湿度门）", CountDriftEvents() == 0);

            for (int i = 0; i < 12; i++) SubmitEmotion(V: -0.9f, A: 0.3f, C: 0.4f);  // 回湿
            Log($"  回湿后: moisture={GetLocation("lowland").soilMoisture:F2}");

            for (int i = 0; i < 10; i++)                                             // 湿风
            {
                SubmitEmotion(V: 0.5f, A: 1.0f, C: 0.5f);
                Log($"  湿风 D{i + 1}: Wind={EnvState.WindSpeed:F2} " +
                    $"moisture={GetLocation("lowland").soilMoisture:F2} 落种数={CountDriftEvents()}");
            }
            int n = CountDriftEvents();
            AssertTrue($"湿风后落种 ≥1（实际 {n}）", n >= 1);
            AssertTrue($"冷却 5 天 → 10 天内 ≤2 条（实际 {n}）", n <= 2);
            var events = WorldManager.Instance._saveData.worldEvents;
            for (int i = events.Count - 1; i >= 0; i--)
                if (events[i].type == WorldEventType.DandelionSeedsDrifted)
                { AssertEqual("落种目标区=lowland（拓扑唯一下风）", events[i].targetId, "lowland"); break; }
            LogWorldState();
        }

        int CountDriftEvents()
        {
            int n = 0;
            var events = WorldManager.Instance._saveData.worldEvents;
            if (events != null)
                foreach (var e in events)
                    if (e.type == WorldEventType.DandelionSeedsDrifted) n++;
            return n;
        }

        // ─────────────────────────────────────────────
        // 场景十：植被捂水（Batch 3 §5.5 第 3 行验证）
        //
        // 机制：日消退 = 0.03 − 0.015×veg —— 低洼（veg 0.7）退 0.0195/天，
        //       石头区（veg 0.25）退 0.026/天。
        // 阶段：10 天暴雨（两区都蓄水）→ 10 天停雨。
        // 预期：停雨后低洼水位仍 >0.6（≈1.0 顶格），石头区干透 <0.05；
        //       捂水语料 env_water_retention 进入世界志（雨停+水位高+veg 高）。
        // ─────────────────────────────────────────────
        void Test_WaterRetention()
        {
            Log("=== 场景十：植被捂水 ===");
            ResetWorld();

            for (int i = 0; i < 10; i++) SubmitEmotion(V: -0.9f, A: 0.4f, C: 0.4f);
            Log($"  暴雨后: lowland.water={GetLocation("lowland").waterLevel:F2} " +
                $"stone.water={GetLocation("stone_area").waterLevel:F2}");

            for (int i = 0; i < 10; i++)
            {
                SubmitEmotion(V: 0.5f, A: 0.3f, C: 0.5f);
                Log($"  停雨 D{i + 1}: lowland={GetLocation("lowland").waterLevel:F2} " +
                    $"stone={GetLocation("stone_area").waterLevel:F2} Rain={EnvState.Rainfall:F2}");
            }

            float low = GetLocation("lowland").waterLevel;
            float st  = GetLocation("stone_area").waterLevel;
            AssertTrue($"低洼（veg 0.7）水退慢：停雨 10 天仍 >0.6（实际 {low:F2}）", low > 0.6f);
            AssertTrue($"石头区（veg 0.25）干透：<0.05（实际 {st:F2}）", st < 0.05f);
            AssertChronicleContains("env_water_retention");
            LogWorldState();
        }

        // ─── 工具方法 ────────────────────────────────

        void SubmitEmotion(float V, float A, float C, float T = 1f, float S = 0f)
        {
            WorldManager.Instance.OnJournalSubmitted(
                new EmotionVector { V = V, A = A, T = T, S = S, C = C });
        }
        
        void ResetWorld()
        {
            WorldManager.Instance.ReinitializeWithSave(
                WorldInitializer.CreateNewWorld());
            Log("  世界已重置为初始状态");
        }

        

        void AssertEqual(string label, string actual, string expected)
        {
            bool pass   = actual == expected;
            string icon = pass ? "✓" : "✗";
            string msg  = $"  [{icon}] {label}: 期望={expected}  实际={actual}";
            if (pass) Log(msg); else LogError(msg);
        }

        void AssertTrue(string label, bool condition)
        {
            string icon = condition ? "✓" : "✗";
            string msg  = $"  [{icon}] {label}";
            if (condition) Log(msg); else LogError(msg);
        }

        void AssertChronicleContains(string eventId)
        {
            var chronicles = WorldManager.Instance._saveData.pendingChronicles;
            bool found     = chronicles.Exists(c => c.eventId == eventId);
            AssertTrue($"世界志包含事件 [{eventId}]", found);

            if (found)
            {
                var entry = chronicles.Find(c => c.eventId == eventId);
                Log($"    → \"{entry.text}\"");
            }
        }

        void LogWorldState()
        {
            var env  = WorldManager.Instance.GetWorldState();
            var save = WorldManager.Instance._saveData;

            Log("\n  ── 当前世界状态 ──");
            Log($"  日期: {save.gameTime.ToDisplayString()}");
            Log($"  E_env: V={save.currentEEnv.V:F2}  A={save.currentEEnv.A:F2}  C={save.currentEEnv.C:F2}");
            Log($"  天气: Rain={env.Rainfall:F2}  Wind={env.WindSpeed:F2}  Fog={env.FogDensity:F2}");
            Log($"  Stars={env.StarVisibility:F2}  Decay={env.DecayLevel:F2}");
            Log($"  世界志队列: {save.pendingChronicles.Count} 条待显示");
        }

        void Log(string msg)      => Debug.Log(msg);
        void LogError(string msg) => Debug.LogError(msg);

        // ─── 快捷属性 ─────────────────────────────────
        EmotionVector         EEnv     => WorldManager.Instance._saveData.currentEEnv;
        WorldEnvironmentState EnvState => WorldManager.Instance.GetWorldState();

        AnimalEntity   GetAnimal  (string id) => WorldManager.Instance.Registry.GetAnimal(id);
        PlantEntity    GetPlant   (string id) => WorldManager.Instance.Registry.GetPlant(id);
        LocationEntity GetLocation(string id) => WorldManager.Instance.Registry.GetLocation(id);
    }
}
