#if UNITY_EDITOR
using System.Collections.Generic;
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

        // 端到端：忠实复刻 WorldManager 全管线
        // （EmotionInertia → 天气 → 水位传播 → 驱动系统 → 文本层 → 规则 → 关系[已退役过滤]）。
        // 复刻 WorldManager 的管线（EditMode 无法走 MonoBehaviour），分解为可独立调用的算子：
        //   submit    —— 完整一天（推进日历 + 注入情绪 + 模拟），供既有逐日测试使用
        //   inject    —— 仅注入情绪 + 一次响应式模拟（不推进日历），对应 InjectEmotion+SimulatePass
        //   worldTick —— 自主推进 N 天（每天 Advance+Relax+模拟），对应 WorldManager.WorldTick
        private static Pipeline BuildPipeline()
        {
            var save = WorldInitializer.CreateNewWorld();
            var reg  = new EntityRegistry();
            reg.Initialize(save);

            var inertia        = new EmotionInertiaSystem();
            inertia.Restore(save.currentEEnv, save.emotionHistory);
            var rhythm         = new NaturalRhythmSystem();
            var env            = new WorldEnvironmentSystem();
            var drive          = new AnimalDriveSystem(reg, save);
            var narrator       = new BehaviorNarrator(reg, save);
            var ruleEngine     = new NarrativeRuleEngine(reg, save);
            var relationSystem = new EntityRelationSystem(reg, save);

            var allRules = new List<NarrativeRuleSO>(Resources.LoadAll<NarrativeRuleSO>("Rules"));
            var retired  = new HashSet<string>
            {
                "deer_mouse_anxious", "vole_territory_expand",
                "weaver_habitat_lost", "insect_surge_vegetation"
            };
            var allRelations = new List<EntityRelationSO>(Resources.LoadAll<EntityRelationSO>("Relations"));
            allRelations.RemoveAll(r => r != null && retired.Contains(r.relationId));

            rhythm.Tick(save.gameTime);
            env.UpdateFromEEnv(inertia.CurrentEEnv, rhythm.State);

            // 一次完整模拟（不推进日历），对应 WorldManager.SimulatePass
            void Simulate()
            {
                env.UpdateFromEEnv(inertia.CurrentEEnv, rhythm.State);
                float rain = env.State.Rainfall;
                foreach (var loc in save.locations)
                {
                    float accRate = loc.locationId switch
                    {
                        "lowland"       => 0.30f,
                        "riverbank"     => 0.22f,
                        "center"        => 0.15f,
                        "highland_east" => 0.10f,
                        "stone_area"    => 0.08f,
                        _               => 0.15f
                    };
                    loc.waterLevel = Mathf.Clamp01(loc.waterLevel + rain * accRate - 0.03f);
                }
                save.currentEEnv    = inertia.CurrentEEnv;
                save.emotionHistory = inertia.History;
                drive.SetEnvironment(env.State, rhythm.State);
                drive.Tick(save.gameTime);
                narrator.SetEnvironment(env.State, rhythm.State);
                narrator.Narrate(save.gameTime);
                ruleEngine.SetEnvironment(env.State, rhythm.State);
                ruleEngine.Evaluate(allRules, save.gameTime);
                relationSystem.SetEnvironment(env.State, rhythm.State);
                relationSystem.Evaluate(allRelations, save.gameTime);
            }

            // 仅注入情绪 + 一次响应式模拟（不推进日历）
            void Inject(float V, float A, float C)
            {
                inertia.Update(new EmotionVector { V = V, A = A, T = 1f, S = 0f, C = C });
                Simulate();
            }

            // 自主推进 N 天：每天 Advance→Relax→刷新季节→模拟；catch-up 丢弃逐日世界志噪音
            void WorldTick(int deltaDays, bool isCatchUp)
            {
                if (deltaDays <= 0) return;
                int chronicleMark = save.pendingChronicles.Count;
                for (int d = 0; d < deltaDays; d++)
                {
                    save.gameTime.Advance(1);
                    inertia.Relax();
                    rhythm.Tick(save.gameTime);
                    Simulate();
                }
                if (isCatchUp)
                {
                    int extra = save.pendingChronicles.Count - chronicleMark;
                    if (extra > 0) save.pendingChronicles.RemoveRange(chronicleMark, extra);
                }
            }

            // 单日全管线（保持既有逐日测试语义：每次 submit 推进一天，不回落）
            void Submit(float V, float A, float C)
            {
                inertia.Update(new EmotionVector { V = V, A = A, T = 1f, S = 0f, C = C });
                save.gameTime.Advance(1);
                rhythm.Tick(save.gameTime);
                Simulate();
            }

            Debug.Log($"[Pipeline] Rules={allRules.Count}  Relations={allRelations.Count} (retired 4)");
            return new Pipeline { save = save, reg = reg, submit = Submit, inject = Inject, worldTick = WorldTick };
        }

        // 管线算子集合（EditMode 测试共享）
        private class Pipeline
        {
            public WorldSaveData                save;
            public EntityRegistry               reg;
            public System.Action<float,float,float> submit;     // 完整一天
            public System.Action<float,float,float> inject;     // 注入 + 响应式模拟（不推进日历）
            public System.Action<int,bool>          worldTick;  // 自主推进 N 天
        }

        // 世界绝对日序（用于断言推进天数）：每月30天、每年12月
        private static int AbsDay(GameDateTime t) => (t.year - 1) * 360 + (t.month - 1) * 30 + t.day;

        [MenuItem("GlimmerDiary/Test Full Pipeline (Flood)")]
        public static void RunFullPipelineFlood()
        {
            var p = BuildPipeline();
            var save = p.save; var reg = p.reg; var submit = p.submit;
            var vole = reg.GetAnimal("vole");
            Debug.Log("=== FullPipeline Flood (EditMode, 真实管线) ===");
            for (int i = 0; i < 6; i++)
            {
                submit(-0.8f, 0.7f, 0.4f);
                Debug.Log($"Day {i + 1}: lowland.water={reg.GetLocation("lowland").waterLevel:F2} | " +
                          $"E_env V={save.currentEEnv.V:F2} A={save.currentEEnv.A:F2} | 田鼠 @{vole.location} ({vole.behavior.drive})");
            }

            bool atHigh   = vole.location == "highland_east";
            bool hasFlood = save.pendingChronicles.Exists(c => c.eventId == "vole_relocate_flood");
            bool noRaw    = !save.pendingChronicles.Exists(c => c.text.Contains("{date}") || c.text.Contains("{sky}"));
            Debug.Log($"[{(atHigh   ? "PASS" : "FAIL")}] 田鼠迁往 highland_east (NarrativeRule 拥有)  实际 @{vole.location}");
            Debug.Log($"[{(hasFlood ? "PASS" : "FAIL")}] 世界志含 vole_relocate_flood");
            Debug.Log($"[{(noRaw    ? "PASS" : "FAIL")}] 世界志无未替换的 {{date}}/{{sky}} 死字符串");
            foreach (var c in save.pendingChronicles)
                Debug.Log($"    世界志[{c.eventId}]: \"{c.text}\"");
        }

        // 一个状态变量在长程仿真中的统计量
        private class Stat
        {
            public readonly string name;
            public float min = float.PositiveInfinity, max = float.NegativeInfinity, sum;
            public int n, pinHi, pinLo; public bool anyBad;
            public Stat(string n) { name = n; }
            public void Add(float v)
            {
                if (float.IsNaN(v) || float.IsInfinity(v)) anyBad = true;
                min = Mathf.Min(min, v); max = Mathf.Max(max, v); sum += v; n++;
                if (v >= 0.99f) pinHi++; if (v <= 0.01f) pinLo++;
            }
            public float Range => max - min;
            public float Mean  => n > 0 ? sum / n : 0f;
            public float PinHi => n > 0 ? (float)pinHi / n : 0f;
            public float PinLo => n > 0 ? (float)pinLo / n : 0f;
        }

        // P5：长程仿真 —— 120 游戏日、月度起伏的情绪，验证系统健康振荡、无饱和、无 NaN。
        [MenuItem("GlimmerDiary/Test Long Run (Health)")]
        public static void RunLongRunHealth()
        {
            var p = BuildPipeline();
            var save = p.save; var reg = p.reg; var submit = p.submit;
            var fox = reg.GetAnimal("fox");
            var vole = reg.GetAnimal("vole");
            var dm = reg.GetAnimal("deer_mouse");
            var tree = reg.GetPlant("baobab_main");

            var stats = new Dictionary<string, Stat>();
            Stat S(string k) { if (!stats.TryGetValue(k, out var s)) stats[k] = s = new Stat(k); return s; }

            Debug.Log("=== LongRun Health (EditMode, 120 天月度起伏) ===");
            const int DAYS = 120;
            for (int d = 1; d <= DAYS; d++)
            {
                float phase = 2f * Mathf.PI * d / 30f;          // 30 天一个月周期
                float V = 0.6f * Mathf.Sin(phase);              // -0.6 .. +0.6
                float A = 0.5f - 0.3f * Mathf.Sin(phase);       // 低效价时唤醒高（利于偶发断枝）
                float C = 0.55f + 0.15f * Mathf.Sin(phase);     // 0.4 .. 0.7
                submit(V, A, C);

                S("fox.hunger").Add(fox.internalState.hunger);
                S("fox.safety").Add(fox.internalState.safety);
                S("fox.territory").Add(fox.internalState.territoryStability);
                S("vole.food").Add(vole.internalState.foodStock);
                S("vole.expand").Add(vole.internalState.expansionPressure);
                S("vole.shelter").Add(vole.internalState.shelterSecurity);
                S("dm.anxiety").Add(dm.internalState.anxiety);
                S("dm.range").Add(dm.activityRange);
                S("tree.vitality").Add(tree.internalState.vitality);
                S("lowland.water").Add(reg.GetLocation("lowland").waterLevel);
            }

            Debug.Log($"{"变量",-16} {"min",6} {"max",6} {"mean",6} {"range",6} {"pinHi%",7} {"pinLo%",7}");
            bool allFinite = true, allInRange = true; int alive = 0;
            foreach (var s in stats.Values)
            {
                Debug.Log($"{s.name,-16} {s.min,6:F2} {s.max,6:F2} {s.Mean,6:F2} {s.Range,6:F2} {s.PinHi*100,6:F0}% {s.PinLo*100,6:F0}%");
                if (s.anyBad) allFinite = false;
                if (s.min < -0.001f || s.max > 1.001f) allInRange = false;
                if (s.Range > 0.15f) alive++;
            }

            // 健康判据
            bool a1 = allFinite && allInRange;
            bool a2 = alive >= 4;                                  // 至少 4 个变量在起伏（系统未死板）
            bool a3 = S("vole.expand").PinHi < 0.90f;              // 扩张压力未长期饱和（§8 阻尼生效）
            bool a4 = S("fox.territory").PinLo < 0.90f;            // 领地稳定度未长期归零（Patrol 反馈生效）

            int events = save.worldEvents.Count;
            int chronicles = save.pendingChronicles.Count;
            Debug.Log($"离散事件={events}  世界志={chronicles}  断枝={tree.permanentDamages.Count}");
            Debug.Log($"[{(a1 ? "PASS" : "FAIL")}] 全程无 NaN/越界（所有变量 ∈ [0,1]）");
            Debug.Log($"[{(a2 ? "PASS" : "FAIL")}] 系统活跃：{alive} 个变量 range>0.15（健康振荡）");
            Debug.Log($"[{(a3 ? "PASS" : "FAIL")}] vole.expansionPressure 未长期饱和（pinHi {S("vole.expand").PinHi*100:F0}%）");
            Debug.Log($"[{(a4 ? "PASS" : "FAIL")}] fox.territoryStability 未长期归零（pinLo {S("fox.territory").PinLo*100:F0}%）");
        }

        [MenuItem("GlimmerDiary/Test Full Pipeline (Bird Arrival)")]
        public static void RunFullPipelineBird()
        {
            var p = BuildPipeline();
            var save = p.save; var reg = p.reg; var submit = p.submit;
            var bird = reg.GetAnimal("migratory_bird");
            var vole = reg.GetAnimal("vole");
            Debug.Log("=== FullPipeline BirdArrival (EditMode, 真实管线) ===");
            Debug.Log($"初始: 候鸟 present={bird.isPresent} | 田鼠 @{vole.location} | 月份={save.gameTime.month}(秋)");

            // 秋季 + 正效价：候鸟迁来由 NarrativeRule 拥有；驱动系统先于规则运行，不应干扰
            for (int i = 0; i < 4; i++)
            {
                submit(0.6f, 0.4f, 0.7f);
                Debug.Log($"Day {i + 1}: E_env V={save.currentEEnv.V:F2} | 候鸟 present={bird.isPresent} @{bird.location} ({bird.behavior.drive}) | 田鼠 @{vole.location}");
            }

            bool arrived  = bird.isPresent && bird.location == "riverbank";
            bool hasArr   = save.pendingChronicles.Exists(c => c.eventId == "migratory_bird_arrival");
            bool voleStay = vole.location == "lowland";   // 无洪水、未到扩张期 → 田鼠不应迁移（预期）
            Debug.Log($"[{(arrived  ? "PASS" : "FAIL")}] 候鸟迁来 riverbank (NarrativeRule 拥有，驱动系统未干扰)");
            Debug.Log($"[{(hasArr   ? "PASS" : "FAIL")}] 世界志含 migratory_bird_arrival");
            Debug.Log($"[{(voleStay ? "PASS" : "FAIL")}] 田鼠留在 lowland（此情境本就不该迁移）实际 @{vole.location}");
            foreach (var c in save.pendingChronicles)
                Debug.Log($"    世界志[{c.eventId}]: \"{c.text}\"");
        }

        // Phase 0：统一时钟 + 自主心跳。提交 1 篇 → 静默推进 30 天，断言：
        //   gameTime 进了 30 天 / E_env 朝基线收敛 / 动物状态演化 / 世界志无逐日噪音 / 事件日志 append-only。
        [MenuItem("GlimmerDiary/Test Silent Advance (30d)")]
        public static void RunSilentAdvance30()
        {
            var p    = BuildPipeline();
            var save = p.save; var reg = p.reg;
            var dm   = reg.GetAnimal("deer_mouse");
            var vole = reg.GetAnimal("vole");

            Debug.Log("=== SilentAdvance30 (EditMode, Phase 0 自主心跳) ===");

            // 1) 提交 1 篇：强负向、偏唤醒情绪（注入 + 一次响应式模拟，不推进日历）
            p.inject(-0.7f, 0.6f, 0.5f);

            int    dayAfterInject = AbsDay(save.gameTime);
            var    eAfterInject   = new EmotionVector
            { V = save.currentEEnv.V, A = save.currentEEnv.A, T = save.currentEEnv.T,
              S = save.currentEEnv.S, C = save.currentEEnv.C };
            float  dmAnxBefore    = dm.internalState.anxiety;
            float  voleFoodBefore = vole.internalState.foodStock;
            int    eventsBefore   = save.worldEvents.Count;
            int    pendingBefore  = save.pendingChronicles.Count;
            Debug.Log($"提交后: 第{AbsDay(save.gameTime)}日  E_env V={eAfterInject.V:F2} A={eAfterInject.A:F2} C={eAfterInject.C:F2} | " +
                      $"鹿鼠 anx={dmAnxBefore:F2} | 田鼠 food={voleFoodBefore:F2} | 事件={eventsBefore} 世界志={pendingBefore}");

            // 2) 静默推进 30 天（catch-up：丢弃逐日世界志噪音）
            p.worldTick(30, true);

            int    dayAfter30 = AbsDay(save.gameTime);
            var    eAfter30   = save.currentEEnv;
            Debug.Log($"30天后: 第{dayAfter30}日  E_env V={eAfter30.V:F2} A={eAfter30.A:F2} C={eAfter30.C:F2} | " +
                      $"鹿鼠 anx={dm.internalState.anxiety:F2} | 田鼠 food={vole.internalState.foodStock:F2} | " +
                      $"事件={save.worldEvents.Count} 世界志={save.pendingChronicles.Count}");

            // 断言
            bool t1 = (dayAfter30 - dayAfterInject) == 30;

            // 基线为零向量：每个注入后非零分量的绝对值应减小（朝 0 收敛）
            bool t2 = ConvergedToZero(eAfterInject.V, eAfter30.V)
                   && ConvergedToZero(eAfterInject.A, eAfter30.A)
                   && ConvergedToZero(eAfterInject.C, eAfter30.C);

            bool t3 = !Mathf.Approximately(dm.internalState.anxiety, dmAnxBefore)
                   || !Mathf.Approximately(vole.internalState.foodStock, voleFoodBefore);

            // catch-up 丢弃逐日噪音：世界志净增 ≤ 1（Phase 0 期望 0）
            int pendingAdded = save.pendingChronicles.Count - pendingBefore;
            bool t4 = pendingAdded <= 1;

            // 永久事件日志 append-only：计数只增不减
            bool t5 = save.worldEvents.Count >= eventsBefore;

            Debug.Log($"[{(t1 ? "PASS" : "FAIL")}] gameTime 推进 30 天  (实际 {dayAfter30 - dayAfterInject})");
            Debug.Log($"[{(t2 ? "PASS" : "FAIL")}] E_env 朝基线(零)收敛  (V {eAfterInject.V:F2}→{eAfter30.V:F2}, A {eAfterInject.A:F2}→{eAfter30.A:F2}, C {eAfterInject.C:F2}→{eAfter30.C:F2})");
            Debug.Log($"[{(t3 ? "PASS" : "FAIL")}] 动物内部状态发生演化");
            Debug.Log($"[{(t4 ? "PASS" : "FAIL")}] 世界志无逐日噪音  (净增 {pendingAdded} 条)");
            Debug.Log($"[{(t5 ? "PASS" : "FAIL")}] worldEvents append-only  (前 {eventsBefore} → 后 {save.worldEvents.Count})");
        }

        // 朝零基线收敛：要么本就接近零，要么绝对值确有减小
        private static bool ConvergedToZero(float before, float after)
        {
            if (Mathf.Abs(before) < 0.02f) return true;
            return Mathf.Abs(after) < Mathf.Abs(before) - 1e-4f;
        }
    }
}
#endif
