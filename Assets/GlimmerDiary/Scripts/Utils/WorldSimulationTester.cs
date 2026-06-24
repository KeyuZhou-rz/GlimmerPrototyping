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
            EmergentAnxietyChain     // 狐狸临近 → 鹿鼠焦虑 → 收缩 → 田鼠扩张（状态涌现）
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
