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
        //   inject    —— 仅注入情绪 + 一次响应式模拟（不推进日历）。
        //   注：对应 2026-08-03 前的 InjectEmotion+SimulatePass；A 方案后 OnJournalSubmitted
        //   不再跑 SimulatePass，此算子保留用于"逐日演化"类测试的建模，不再镜像提交路径
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
            var translation    = new TranslationLayer();
            var drive          = new AnimalDriveSystem(reg, save);
            var vegetation     = new VegetationSystem(reg, save, 0.003f); // 虫害衰减率 = AnimalDriveTuning.insectVegDecay 默认
            var narrator       = new BehaviorNarrator(reg, save);
            var ruleEngine     = new NarrativeRuleEngine(reg, save);
            var relationSystem = new EntityRelationSystem(reg, save);
            var emergentTuning = ScriptableObject.CreateInstance<EmergentMomentTuning>();
            var detector       = new EmergentMomentDetector(reg, save, emergentTuning);
            var eraSystem      = new EraSystem(save);
            var voleTown       = new VoleTownSystem(save);
            var stratumSys     = new StratumSystem(save);

            var allRules = new List<NarrativeRuleSO>(Resources.LoadAll<NarrativeRuleSO>("Rules"));
            var retired  = new HashSet<string>
            {
                "deer_mouse_anxious", "vole_territory_expand",
                "weaver_habitat_lost", "insect_surge_vegetation"
            };
            var allRelations = new List<EntityRelationSO>(Resources.LoadAll<EntityRelationSO>("Relations"));
            allRelations.RemoveAll(r => r != null && retired.Contains(r.relationId));

            rhythm.Tick(save.gameTime);
            // 初始化只消费无状态信号（镜像 WorldManager.Start）：启动不是一天，不多走积分
            env.ConsumeSignals(translation.Translate(inertia.CurrentEEnv, rhythm.State));

            // 一次完整模拟（不推进日历），对应 WorldManager.SimulatePass
            void Simulate()
            {
                var signals = translation.Translate(inertia.CurrentEEnv, rhythm.State);
                env.UpdateFromEEnv(inertia.CurrentEEnv, signals, rhythm.State);   // 镜像 WorldManager：旱债需季节基准
                save.environmentState = env.Snapshot();   // 镜像 WorldManager.SimulatePass：积分态落档
                // 水位/湿度传播直接复用 WorldManager 的静态实现——速率表单一来源
                WorldManager.PropagateRainfallToLocations(save, env.State.Rainfall);
                vegetation.Tick(save.gameTime, env.State);   // loc.vegetationDensity 单一写者 + 蒲公英落种；驱动层只读
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
                eraSystem.Tick(save.gameTime, env.State, rhythm.State, reg);   // 纪元钟每日最后拍板
                voleTown.Tick(save.gameTime);   // 镜像 WorldManager：纪元钟之后，读当日最新章节
                stratumSys.Tick(save.gameTime, env.State, inertia.CurrentEEnv);   // 镜像 WorldManager：小径之后，当日淡完即入土
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
            return new Pipeline
            {
                save = save, reg = reg, submit = Submit, inject = Inject, worldTick = WorldTick,
                detector = detector, emergentTuning = emergentTuning, eraSystem = eraSystem,
                voleTown = voleTown, stratumSys = stratumSys, env = env
            };
        }

        // 管线算子集合（EditMode 测试共享）
        private class Pipeline
        {
            public WorldSaveData                save;
            public EntityRegistry               reg;
            public System.Action<float,float,float> submit;     // 完整一天
            public System.Action<float,float,float> inject;     // 注入 + 响应式模拟（不推进日历）
            public System.Action<int,bool>          worldTick;  // 自主推进 N 天
            public EmergentMomentDetector           detector;       // 涌现时刻检测器（测试显式驱动）
            public EmergentMomentTuning             emergentTuning; // 可改 baseP/winterP=1 做确定性
            public EraSystem                        eraSystem;      // 纪元钟（可挂起测预跑语义）
            public VoleTownSystem                   voleTown;       // 田鼠镇（小径成形/镇散，D3）
            public StratumSystem                    stratumSys;     // 新生地层（入土/沉降/出露，D5/D6）
            public WorldEnvironmentSystem           env;            // 环境系统（直拍 Tick 时喂 env.State）
        }

        // 世界绝对日序（用于断言推进天数）——公式收敛到 GameDateTime 单一来源
        private static int AbsDay(GameDateTime t) => t.ToAbsoluteDays();

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

        // ── Phase 1：涌现式相遇（QuietConvergence） ────────────────────────────

        private static int CountConvergence(WorldSaveData save)
        {
            int n = 0;
            foreach (var e in save.worldEvents)
                if (e.type == WorldEventType.QuietConvergence) n++;
            return n;
        }

        // 直接安排一个「良性 + 暖 + 静 + 同 zone」的簇：fox + weaver 都在 center 休憩。
        // 注意：调用前需先跑过一遍管线，确保 baobab.internalState 已初始化。
        private static void ArrangeBenignClusterAtCenter(EntityRegistry reg, WorldSaveData save)
        {
            var fox = reg.GetAnimal("fox");
            fox.isPresent = true; fox.location = "center";
            fox.behavior ??= new BehaviorOutput();
            fox.behavior.drive = "Rest"; fox.behavior.zone = "center";

            var weaver = reg.GetAnimal("weaver_bird");
            weaver.isPresent = true; weaver.location = "center";
            weaver.behavior ??= new BehaviorOutput();
            weaver.behavior.drive = "Nest"; weaver.behavior.zone = "center";

            reg.GetPlant("baobab_main").internalState.vitality = 0.9f;  // 暖
            save.currentEEnv.A = 0.1f;                                  // 静
        }

        // 测试 1：确定性——WouldFire 谓词（无随机）+ 冷却间隔（p=1）。
        [MenuItem("GlimmerDiary/Test Quiet Convergence (Deterministic)")]
        public static void RunQuietConvergence_Deterministic()
        {
            var p = BuildPipeline();
            var save = p.save; var reg = p.reg;
            Debug.Log("=== QuietConvergence Deterministic (EditMode) ===");

            // 初始化：跑一天管线让 baobab.internalState 存在
            p.submit(0.5f, 0.2f, 0.6f);

            // 基准：良性+暖+静+同zone簇 → 命中
            ArrangeBenignClusterAtCenter(reg, save);
            bool baseFire = p.detector.WouldFire(out string z, out string csv);

            // 逐项翻转单一前提 → 应否决
            ArrangeBenignClusterAtCenter(reg, save);
            reg.GetAnimal("fox").behavior.drive = "Foraging";              // (a) 捕猎 ≠ 休憩
            bool noHunt = !p.detector.WouldFire(out _, out _);

            ArrangeBenignClusterAtCenter(reg, save);
            save.currentEEnv.A = 0.9f;                                     // (b) 高唤醒
            bool noArousal = !p.detector.WouldFire(out _, out _);

            ArrangeBenignClusterAtCenter(reg, save);
            reg.GetPlant("baobab_main").internalState.vitality = 0.1f;     // (c) 无暖沉积
            bool noCold = !p.detector.WouldFire(out _, out _);

            ArrangeBenignClusterAtCenter(reg, save);
            reg.GetAnimal("weaver_bird").isPresent = false;               // (d) 只剩一只 → 无簇
            bool noSingle = !p.detector.WouldFire(out _, out _);

            // 概率门设 1 → 谓词命中即触发；验证冷却
            p.emergentTuning.baseP = 1f; p.emergentTuning.winterP = 1f;
            ArrangeBenignClusterAtCenter(reg, save);
            int before = CountConvergence(save);
            p.detector.Detect(save.gameTime);                             // 首次触发
            int after1 = CountConvergence(save);
            p.detector.Detect(save.gameTime);                            // 同日再调 → 冷却挡住
            int after2 = CountConvergence(save);
            for (int i = 0; i < p.emergentTuning.cooldownDays; i++) save.gameTime.Advance(1);
            ArrangeBenignClusterAtCenter(reg, save);
            p.detector.Detect(save.gameTime);                            // 超过冷却 → 再触发
            int after3 = CountConvergence(save);

            bool t1 = baseFire;
            bool t2 = noHunt && noArousal && noCold && noSingle;
            bool t3 = (after1 - before) == 1;
            bool t4 = after2 == after1;
            bool t5 = (after3 - after2) == 1;

            Debug.Log($"[{(t1 ? "PASS" : "FAIL")}] 良性+暖+静+同zone簇 → WouldFire 命中 (@{z} [{csv}])");
            Debug.Log($"[{(t2 ? "PASS" : "FAIL")}] 翻转任一前提 → 否决 (hunt阻={noHunt} arousal阻={noArousal} cold阻={noCold} single阻={noSingle})");
            Debug.Log($"[{(t3 ? "PASS" : "FAIL")}] p=1 → Detect 触发一次");
            Debug.Log($"[{(t4 ? "PASS" : "FAIL")}] 冷却内重复 Detect 不再触发");
            Debug.Log($"[{(t5 ? "PASS" : "FAIL")}] 超过冷却后再次触发");
        }

        // 测试 2：organic 可行性闸门——drive.Tick 真跑（不 re-assert），统计 WouldFire 可触发天数占比。
        [MenuItem("GlimmerDiary/Test Quiet Convergence (Organic Feasibility)")]
        public static void RunQuietConvergence_Organic()
        {
            var p = BuildPipeline();
            var save = p.save; var reg = p.reg;
            Debug.Log("=== QuietConvergence Organic Feasibility (EditMode, drive 真跑) ===");

            const int DAYS = 200;
            int triggerableDays = 0, foxDmDays = 0, foxWeaverDays = 0, otherDays = 0;
            var pairCounts = new Dictionary<string, int>();

            for (int d = 0; d < DAYS; d++)
            {
                p.submit(0.6f, 0.2f, 0.6f);   // 长期暖+静；drive 真跑，绝不 re-assert
                if (!p.detector.WouldFire(out string zone, out string csv)) continue;

                triggerableDays++;
                pairCounts.TryGetValue(csv, out int c); pairCounts[csv] = c + 1;
                bool fox = csv.Contains("fox");
                bool rodent = csv.Contains("deer_mouse") || csv.Contains("vole");
                if      (fox && rodent)               foxDmDays++;
                else if (fox && csv.Contains("weaver_bird")) foxWeaverDays++;
                else                                  otherDays++;
            }

            Debug.Log($"可触发天数占比: {triggerableDays}/{DAYS} = {(float)triggerableDays / DAYS:P0}");
            foreach (var kv in pairCounts)
                Debug.Log($"    簇 [{kv.Key}]: {kv.Value} 天");
            Debug.Log($"诊断 · 宿敌对(fox+啮齿)可触发天数 = {foxDmDays}  ← 决定 drive 层后续阶段优先级");
            Debug.Log($"诊断 · fox+weaver = {foxWeaverDays}   其他 = {otherDays}");

            bool alive = triggerableDays > 0;
            Debug.Log($"[{(alive ? "PASS" : "FAIL")}] 机制在活世界中可触发（总可触发天数 > 0）");
            if (foxDmDays == 0)
                Debug.Log("[INFO] 宿敌对在当前 tuning 下 organically 不可触发 → 需 drive 层使能（已排期，超出 Phase 1 read-only 边界）");
        }

        // 测试 3：统计——隔离随机门，强制永久可触发，断言触发率 ≈ baseP。
        [MenuItem("GlimmerDiary/Test Quiet Convergence (Rate)")]
        public static void RunQuietConvergence_Rate()
        {
            var p = BuildPipeline();
            var save = p.save; var reg = p.reg;
            p.submit(0.5f, 0.2f, 0.6f);   // 初始化 internalState

            p.emergentTuning.cooldownDays = 1;          // 让随机门成为唯一节流
            float targetP = p.emergentTuning.baseP;     // 默认 ~0.12

            const int TRIALS = 4000;
            int fires = 0;
            Random.InitState(20260624);
            for (int i = 0; i < TRIALS; i++)
            {
                ArrangeBenignClusterAtCenter(reg, save);
                // 每次跨年（ToDays +360 > cooldown），且固定非冬季月（M9）避免 winterP 干扰
                save.gameTime.year = i + 1; save.gameTime.month = 9; save.gameTime.day = 1;
                int before = CountConvergence(save);
                p.detector.Detect(save.gameTime);
                if (CountConvergence(save) > before) fires++;
            }

            float observed = (float)fires / TRIALS;
            bool within = Mathf.Abs(observed - targetP) < 0.03f;
            Debug.Log("=== QuietConvergence Rate (EditMode, 隔离随机门) ===");
            Debug.Log($"[{(within ? "PASS" : "FAIL")}] 触发率 ≈ baseP  (观测 {observed:P1}, 目标 {targetP:P1}, TRIALS={TRIALS})");
        }

        // 虫害经 VegetationSystem 生效：断枝 → 织巢鸟离场 → highland_east 植被衰减（1-tick 滞后）。
        [MenuItem("GlimmerDiary/Test Vegetation Pest")]
        public static void RunVegetationPest()
        {
            var p = BuildPipeline();
            var save = p.save; var reg = p.reg;
            var he = reg.GetLocation("highland_east");
            var weaver = reg.GetAnimal("weaver_bird");
            var tree = reg.GetPlant("baobab_main");

            Debug.Log("=== VegetationPest (EditMode) ===");
            for (int i = 0; i < 3; i++) p.submit(0.5f, 0.2f, 0.6f);   // 初始化 internalState
            float vegBefore = he.vegetationDensity;
            Debug.Log($"断枝前: highland_east.veg={vegBefore:F3}  织巢鸟 present={weaver.isPresent}");

            // 模拟断枝（NarrativeRule 拥有）→ TickTree 边沿 → 织巢鸟离场
            tree.permanentDamages.Add(new PermanentDamageRecord
            {
                date = save.gameTime.ToKeyString(), damageType = "branch_broken",
                description = "smoketest", triggeredBy = "smoketest"
            });

            for (int i = 0; i < 10; i++) p.submit(0.5f, 0.2f, 0.6f);
            float vegAfter = he.vegetationDensity;
            Debug.Log($"断枝后 10 天: highland_east.veg={vegAfter:F3}  织巢鸟 present={weaver.isPresent}  (衰减 {vegBefore - vegAfter:F3})");

            bool weaverGone = !weaver.isPresent;
            bool declined   = vegAfter < vegBefore - 1e-4f;
            Debug.Log($"[{(weaverGone ? "PASS" : "FAIL")}] 织巢鸟经断枝离场");
            Debug.Log($"[{(declined   ? "PASS" : "FAIL")}] 虫害经 VegetationSystem 衰减 highland_east 植被（1-tick 滞后）");
        }

        // 土壤湿度经 PropagateEnvironment 生效：大雨蓄水（分区保水差）+ 无雨蒸发。
        [MenuItem("GlimmerDiary/Test Soil Moisture")]
        public static void RunSoilMoisture()
        {
            var p = BuildPipeline();
            var save = p.save; var reg = p.reg;
            var lowland = reg.GetLocation("lowland");
            var stone   = reg.GetLocation("stone_area");

            Debug.Log("=== SoilMoisture (EditMode) ===");
            float lowInit = lowland.soilMoisture, stoneInit = stone.soilMoisture;
            Debug.Log($"初始: lowland={lowInit:F3}  stone_area={stoneInit:F3}");

            // 连续大雨 6 天（V=-1 → Rainfall=1.0）
            for (int i = 0; i < 6; i++) p.submit(-1f, 0.3f, 0.5f);
            float lowRain = lowland.soilMoisture, stoneRain = stone.soilMoisture;
            Debug.Log($"大雨 6 天后: lowland={lowRain:F3}  stone_area={stoneRain:F3}");

            // 连续无雨 6 天（V=0.6 → Rainfall=0）
            for (int i = 0; i < 6; i++) p.submit(0.6f, 0.3f, 0.6f);
            float lowDry = lowland.soilMoisture, stoneDry = stone.soilMoisture;
            Debug.Log($"无雨 6 天后: lowland={lowDry:F3}  stone_area={stoneDry:F3}");

            bool a1 = lowRain  > lowInit  + 1e-4f;      // 雨天蓄水
            bool a2 = lowRain  > stoneRain + 1e-4f;      // 分区保水差（lowland 比 stone_area 更保湿）
            bool a3 = lowDry   < lowRain  - 1e-4f;       // 无雨蒸发（lowland）
            bool a4 = stoneDry < stoneRain - 1e-4f;      // 无雨蒸发（stone_area）
            Debug.Log($"[{(a1 ? "PASS" : "FAIL")}] 大雨 → lowland 蓄水  ({lowInit:F3}→{lowRain:F3})");
            Debug.Log($"[{(a2 ? "PASS" : "FAIL")}] 分区保水差  lowland({lowRain:F3}) > stone_area({stoneRain:F3})");
            Debug.Log($"[{(a3 ? "PASS" : "FAIL")}] 无雨 → lowland 蒸发  ({lowRain:F3}→{lowDry:F3})");
            Debug.Log($"[{(a4 ? "PASS" : "FAIL")}] 无雨 → stone_area 蒸发  ({stoneRain:F3}→{stoneDry:F3})");
        }

        // 翻译层无状态信号 1/2/3/7：降水/躁动/晦明/苍穹。直接调 Translate（绕过 submit 的 T=1 硬编码），
        // 用固定 lightIntensity=0 的 NaturalRhythmState，消除墙钟光照的非确定性。
        [MenuItem("GlimmerDiary/Test Translation Layer")]
        public static void RunTranslationLayer()
        {
            var nightRhythm = new NaturalRhythmState { lightIntensity = 0f };  // 夜晚，确定性
            var tl = new TranslationLayer();

            EmotionVector Vec(float V, float A, float T, float C) =>
                new EmotionVector { V = V, A = A, T = T, S = 0f, C = C };

            Debug.Log("=== TranslationLayer (EditMode) ===");

            // 信号 1 降水 Wetness：V=-1 大雨，V=0.6 无雨
            var sRain = tl.Translate(Vec(-1f, 0.3f, 1f, 0.5f), nightRhythm);
            var sDry  = tl.Translate(Vec(0.6f, 0.3f, 1f, 0.5f), nightRhythm);
            Debug.Log($"信号1 Wetness: V=-1 → {sRain.Wetness:F3}   V=0.6 → {sDry.Wetness:F3}");
            Debug.Log($"[{(sRain.Wetness > 0.9f ? "PASS" : "FAIL")}] 信号1 V=-1 → Wetness 高  ({sRain.Wetness:F3})");
            Debug.Log($"[{(sDry.Wetness  < 0.05f ? "PASS" : "FAIL")}] 信号1 V=0.6 → Wetness≈0  ({sDry.Wetness:F3})");

            // 信号 2 躁动 Agitation：A=1 强，A=0 弱
            var sWind1 = tl.Translate(Vec(0f, 1f, 1f, 0.5f), nightRhythm);
            var sWind0 = tl.Translate(Vec(0f, 0f, 1f, 0.5f), nightRhythm);
            Debug.Log($"[{(sWind1.Agitation > 0.8f ? "PASS" : "FAIL")}] 信号2 A=1 → Agitation 高  ({sWind1.Agitation:F3})");
            Debug.Log($"[{(sWind0.Agitation < 0.1f  ? "PASS" : "FAIL")}] 信号2 A=0 → Agitation 低  ({sWind0.Agitation:F3})");

            // 信号 3 晦明 Dimness：C=0 浓雾，C=1 无雾
            var sFog0 = tl.Translate(Vec(0f, 0.3f, 1f, 0f), nightRhythm);
            var sFog1 = tl.Translate(Vec(0f, 0.3f, 1f, 1f), nightRhythm);
            Debug.Log($"[{(sFog0.Dimness > 0.7f ? "PASS" : "FAIL")}] 信号3 C=0 → Dimness 高  ({sFog0.Dimness:F3})");
            Debug.Log($"[{(sFog1.Dimness < 0.05f ? "PASS" : "FAIL")}] 信号3 C=1 → Dimness≈0  ({sFog1.Dimness:F3})");

            // 信号 7 苍穹 Firmament：T 敏感 + 雨门控（夜晚 lightIntensity=0，去除光照干扰）
            var sT1 = tl.Translate(Vec(0.5f, 0.3f, 1f, 0.5f), nightRhythm);  // 无雨
            var sT0 = tl.Translate(Vec(0.5f, 0.3f, 0f, 0.5f), nightRhythm);
            Debug.Log($"信号7 Firmament: T=1 → {sT1.Firmament:F3}   T=0 → {sT0.Firmament:F3}   大雨T=1 → {sRain.Firmament:F3}");
            Debug.Log($"[{(sT1.Firmament > sT0.Firmament + 1e-4f ? "PASS" : "FAIL")}] 信号7 T 敏感: T=1 ({sT1.Firmament:F3}) > T=0 ({sT0.Firmament:F3})");
            Debug.Log($"[{(sRain.Firmament < sT1.Firmament - 1e-4f ? "PASS" : "FAIL")}] 信号7 雨门控: 大雨 T=1 ({sRain.Firmament:F3}) < 无雨 ({sT1.Firmament:F3})");

            // 情绪脉冲（2026-07-27 双时间尺度 + 2026-08-03 延迟消化）：
            // 注入后脉冲先挂 pending（不当即可见）；释放后全强应答；自主日快衰；E_env 慢积分不动
            var inertia = new EmotionInertiaSystem();   // 初始 E_env = Neutral(V=0)
            inertia.Update(Vec(-1f, 0.3f, 1f, 0.5f));   // 一篇最悲伤的日记
            bool impulseStillZero = inertia.Impulse.V == 0f && inertia.Impulse.A == 0f
                                 && inertia.Impulse.T == 0f && inertia.Impulse.S == 0f
                                 && inertia.Impulse.C == 0f;
            Debug.Log($"[{(impulseStillZero && inertia.PendingImpulse != null ? "PASS" : "FAIL")}] " +
                      $"延迟消化: 注入后脉冲挂 pending（live 仍零={impulseStillZero}, pending={(inertia.PendingImpulse != null ? "有" : "无")}）");
            inertia.ForceReleasePending();              // 等效于消化期满
            var sDay1 = tl.Translate(inertia.CurrentEEnv, nightRhythm, inertia.Impulse);
            float eEnvAfter1 = inertia.CurrentEEnv.V;
            inertia.Relax(); inertia.Relax();           // 两个自主日
            var sDay3 = tl.Translate(inertia.CurrentEEnv, nightRhythm, inertia.Impulse);
            Debug.Log($"脉冲: 释放当日 Wetness={sDay1.Wetness:F3} (E_env.V 仅 {eEnvAfter1:F2})   两自主日后 {sDay3.Wetness:F3}");
            Debug.Log($"[{(sDay1.Wetness > 0.6f ? "PASS" : "FAIL")}] 脉冲释放即可见: Wetness={sDay1.Wetness:F3} > 0.6（E_env 只走 alpha=0.2）");
            Debug.Log($"[{(sDay1.Wetness > sDay3.Wetness + 0.2f ? "PASS" : "FAIL")}] 脉冲快衰: 两日后 {sDay3.Wetness:F3} 明显回落");
            Debug.Log($"[{(Mathf.Abs(eEnvAfter1 - (-0.2f)) < 1e-4f ? "PASS" : "FAIL")}] 慢通道不动: E_env.V={eEnvAfter1:F2}（alpha=0.2 惯性不变）");
        }

        // ── V1 D2：纪元钟（五章节状态机） ────────────────────────────
        // 剧本：雨季（连雨 5 tick）→ 定居（丰年 10 天 + 田鼠在场 10 天 + 3 土堆）
        //       → 镇（5 土堆）→ 衰（洪水）→ 雨季（雨回来）。
        // 田鼠位置/土堆记录直接安排（同既有测试注入 permanentDamages 的手法），
        // 每日复位防驱动层 organic 移动干扰断言。
        [MenuItem("GlimmerDiary/Test Era Clock")]
        public static void RunEraClock()
        {
            var p = BuildPipeline();
            var save = p.save; var reg = p.reg;
            var vole = reg.GetAnimal("vole");
            var lowland = reg.GetLocation("lowland");

            Debug.Log("=== EraClock (EditMode, V1 §4.2) ===");
            Debug.Log($"初始章节: {save.eraState.chapter}");

            int ChapterTurns() => save.worldEvents.FindAll(e => e.type == WorldEventType.ChapterTurned).Count;
            int ChapterLetters() => save.pendingChronicles.FindAll(c => c.eventId.StartsWith("chapter_turned:")).Count;

            // 阶段 1：连雨 6 天 → 荒年→雨季（第 5 tick 雨 streak 达标， debt≈0）
            for (int i = 0; i < 6; i++) p.submit(-1f, 0.3f, 0.5f);
            bool toRain = save.eraState.chapter == EraSystem.RainSeason;
            Debug.Log($"阶段1后: chapter={save.eraState.chapter}  debt={save.environmentState.droughtDebt:F2} 低洼水位={lowland.waterLevel:F2}");
            Debug.Log($"[{(toRain ? "PASS" : "FAIL")}] 连雨 5+ tick → 进入雨季/丰年");

            // 阶段 2：排涝 + 田鼠定居 center + 3 条土堆记录，丰年满 10 天 → 定居
            for (int m = 0; m < 3; m++)
                vole.history.Add(new StateChangeRecord
                {
                    date = save.gameTime.ToKeyString(), field = "location",
                    fromValue = "lowland", toValue = "center", triggeredBy = "vole_expansion"
                });
            for (int i = 0; i < 12; i++)
            {
                vole.location = "center"; vole.isPresent = true;
                lowland.waterLevel = 0.3f;   // 排涝防搬家规则干扰
                p.submit(0.2f, 0.3f, 0.6f);
            }
            bool toSettlement = save.eraState.chapter == EraSystem.Settlement;
            Debug.Log($"阶段2后: chapter={save.eraState.chapter}  abundantDays={save.eraState.abundantDays} 田鼠在场streak={save.eraState.voleHomeStreakDays} 活跃土堆={EraSystem.CountActiveMounds(save, save.gameTime)}");
            Debug.Log($"[{(toSettlement ? "PASS" : "FAIL")}] 丰年持续∧田鼠在场∧3土堆 → 进入定居");

            // 阶段 3：补 5 条土堆记录 → 镇（日期逐日铺开：同日同键的重复记录
            // 在痕迹层会被归并，与"镇是多日踩出来的"语义一致，也避免虚假计入）
            for (int m = 0; m < 5; m++)
            {
                save.gameTime.Advance(m == 0 ? 0 : 1);
                vole.history.Add(new StateChangeRecord
                {
                    date = save.gameTime.ToKeyString(), field = "location",
                    fromValue = "lowland", toValue = "center", triggeredBy = "vole_expansion"
                });
            }
            vole.location = "center"; lowland.waterLevel = 0.3f;
            p.submit(0.2f, 0.3f, 0.6f);
            bool toTown = save.eraState.chapter == EraSystem.Town;
            Debug.Log($"[{(toTown ? "PASS" : "FAIL")}] 土堆活跃数 ≥5 → 进入镇（当日活跃土堆={EraSystem.CountActiveMounds(save, save.gameTime)}）");

            // 阶段 4：洪水（lowland 水位 > 0.65，与田鼠搬家规则同源）→ 衰
            lowland.waterLevel = 0.7f;
            p.submit(-0.5f, 0.3f, 0.5f);
            bool toDecline = save.eraState.chapter == EraSystem.Decline;
            Debug.Log($"[{(toDecline ? "PASS" : "FAIL")}] 洪水 → 镇散入衰");

            // 阶段 5：排涝 + 连雨 6 天 → 衰→雨季（章节可倒退/循环，不是线性升级）
            for (int i = 0; i < 6; i++)
            {
                lowland.waterLevel = 0.3f;
                p.submit(-1f, 0.3f, 0.5f);
            }
            bool backToRain = save.eraState.chapter == EraSystem.RainSeason;
            Debug.Log($"[{(backToRain ? "PASS" : "FAIL")}] 衰章后雨回来 → 重返雨季（循环坐庄）");

            // 每次翻页：ChapterTurned 事件（封闭层数据锚）+ 编年史信各一
            bool t6 = ChapterTurns() == 5;
            bool t7 = ChapterLetters() == 5;
            Debug.Log($"[{(t6 ? "PASS" : "FAIL")}] 5 次翻页 = 5 条 ChapterTurned 事件（实际 {ChapterTurns()}）");
            Debug.Log($"[{(t7 ? "PASS" : "FAIL")}] 5 次翻页 = 5 封编年史信（实际 {ChapterLetters()}）");
            foreach (var c in save.pendingChronicles)
                if (c.eventId.StartsWith("chapter_turned:"))
                    Debug.Log($"    编年史[{c.eventId}]: \"{c.text}\"");
        }

        // ── V1 D2 附：纪元钟预跑挂起 ─────────────────────────────
        // 挂起语义：新世界预跑期间连雨也不翻页（章节叙事从玩家到达起算，
        // ChapterTurned 锚必有信对应）；解挂后同一部机器照常翻页（挂起是门，不是熄火）。
        [MenuItem("GlimmerDiary/Test Era Clock (PreRun Suspended)")]
        public static void RunEraClockSuspended()
        {
            var p = BuildPipeline();
            var save = p.save;
            Debug.Log("=== EraClock PreRunSuspended (EditMode) ===");

            p.eraSystem.Suspended = true;
            for (int i = 0; i < 8; i++) p.submit(-1f, 0.3f, 0.5f);   // 连雨 8 天，平时第 5 天就翻页
            bool heldBack = save.eraState.chapter == EraSystem.WildYears
                         && !save.worldEvents.Exists(e => e.type == WorldEventType.ChapterTurned);
            Debug.Log($"[{(heldBack ? "PASS" : "FAIL")}] 挂起期间连雨 8 天不翻页（chapter={save.eraState.chapter}）");

            p.eraSystem.Suspended = false;
            for (int i = 0; i < 6; i++) p.submit(-1f, 0.3f, 0.5f);
            bool resumes = save.eraState.chapter == EraSystem.RainSeason
                        && save.worldEvents.Exists(e => e.type == WorldEventType.ChapterTurned);
            Debug.Log($"[{(resumes ? "PASS" : "FAIL")}] 解挂后照常翻页入雨季（chapter={save.eraState.chapter}）");
        }

        // ── V1 D1：环境积分态入档 ───────────────────────────────────
        // 跑几天攒出非默认的 droughtDebt/湿度 → JsonUtility 整档往返 →
        // 新 EnvironmentSystem Restore 后积分态逐字段相等（断电不丢）。
        [MenuItem("GlimmerDiary/Test Environment Persistence")]
        public static void RunEnvironmentPersistence()
        {
            var p = BuildPipeline();
            var save = p.save;
            Debug.Log("=== EnvironmentPersistence (EditMode, V1 D1) ===");

            for (int i = 0; i < 8; i++) p.submit(0.6f, 0.3f, 0.6f);   // 连晴攒旱债
            float debtBefore  = save.environmentState.droughtDebt;
            float moistBefore = save.environmentState.soilMoisture;
            Debug.Log($"攒档: droughtDebt={debtBefore:F3}  soilMoisture={moistBefore:F3}  chapter={save.eraState.chapter}");

            string json = JsonUtility.ToJson(save);
            var save2 = JsonUtility.FromJson<WorldSaveData>(json);

            var env2 = new WorldEnvironmentSystem();
            env2.Restore(save2.environmentState);
            bool a1 = Mathf.Approximately(env2.State.DroughtDebt, debtBefore)
                   && Mathf.Approximately(env2.State.SoilMoisture, moistBefore);
            bool a2 = save2.eraState != null && save2.eraState.chapter == save.eraState.chapter;
            bool a3 = debtBefore > 1e-4f;   // 确有攒出旱债（断言不是空转）

            Debug.Log($"[{(a1 ? "PASS" : "FAIL")}] 旱债/湿度经 JsonUtility 往返 + Restore 逐字段复原");
            Debug.Log($"[{(a2 ? "PASS" : "FAIL")}] 纪元章节随档往返（{save2.eraState?.chapter}）");
            Debug.Log($"[{(a3 ? "PASS" : "FAIL")}] 8 天连晴确实攒出旱债（{debtBefore:F3} > 0）");
        }

        // ── V1 D3：田鼠镇小径 + 称谓漂移 ─────────────────────────────
        // 成形（活跃土堆 ≥5 → 小径记录快照土堆键）→ 镇散（洪水入衰当日 lapsed）。
        // 称谓三档另起对照组验证（纯函数读档，不走管线）。
        // 田鼠位置/水位每日复位防驱动层 organic 干扰（同 RunEraClock 手法）。
        [MenuItem("GlimmerDiary/Test Vole Town")]
        public static void RunVoleTown()
        {
            var p = BuildPipeline();
            var save = p.save; var reg = p.reg;
            var vole = reg.GetAnimal("vole");
            var lowland = reg.GetLocation("lowland");

            Debug.Log("=== VoleTown (EditMode, V1 D3) ===");

            // 阶段 1：连雨 6 天 → 雨季（定居前置，同 RunEraClock 阶段 1）
            for (int i = 0; i < 6; i++) p.submit(-1f, 0.3f, 0.5f);

            // 阶段 2：5 条土堆记录逐日铺开 + 丰年满 10 天 → 定居；
            // 期间小径应已自行成形（5 土堆在窗口内 ≥ VoleTownSystem.MoundsForTown）
            for (int m = 0; m < 5; m++)
            {
                save.gameTime.Advance(m == 0 ? 0 : 1);
                vole.history.Add(new StateChangeRecord
                {
                    date = save.gameTime.ToKeyString(), field = "location",
                    fromValue = "lowland", toValue = "center", triggeredBy = "vole_expansion"
                });
            }
            string whoAtFormation = null;
            for (int i = 0; i < 12; i++)
            {
                vole.location = "center"; vole.isPresent = true;
                lowland.waterLevel = 0.3f;
                p.submit(0.2f, 0.3f, 0.6f);
                // 成形当拍采样称谓——记录逐日出窗后活跃数回落， loop 末尾再读必掉档
                if (whoAtFormation == null && save.voleTrails != null && save.voleTrails.Count > 0)
                    whoAtFormation = VoleTownSystem.VoleAppellation(save);
            }

            bool formed = save.voleTrails != null && save.voleTrails.Count == 1 && !save.voleTrails[0].lapsed;
            Debug.Log($"[{(formed ? "PASS" : "FAIL")}] 活跃土堆 ≥5 → 小径成形（voleTrails={save.voleTrails?.Count ?? 0}）");
            bool keys = formed && save.voleTrails[0].moundKeys.Count == 5;
            Debug.Log($"[{(keys ? "PASS" : "FAIL")}] 小径快照 5 个土堆键（实际 {(formed ? save.voleTrails[0].moundKeys.Count : -1)}）");
            bool whoTown = whoAtFormation == "镇子";
            Debug.Log($"[{(whoTown ? "PASS" : "FAIL")}] 成形当拍称谓=\"镇子\"（实际 \"{whoAtFormation ?? "n/a"}\"）");

            // 阶段 3：洪水 → 章节入衰 → 当日镇散，小径冻结待淡出（塌洞照旧永久，小径是惯例）
            lowland.waterLevel = 0.7f;
            p.submit(-0.5f, 0.3f, 0.5f);
            bool lapsed = formed && save.voleTrails[0].lapsed && !string.IsNullOrEmpty(save.voleTrails[0].lapseDateKey);
            Debug.Log($"[{(lapsed ? "PASS" : "FAIL")}] 洪水入衰 → 镇散小径冻结（lapsed={(formed ? save.voleTrails[0].lapsed.ToString() : "n/a")}，chapter={save.eraState.chapter}）");

            // 称谓对照组（纯函数）：荒年无堆"田鼠" → 2 堆"它们" → 5 堆"镇子"
            var q = BuildPipeline();
            string who0 = VoleTownSystem.VoleAppellation(q.save);
            var qvole = q.reg.GetAnimal("vole");
            for (int m = 0; m < 2; m++)
            {
                q.save.gameTime.Advance(m == 0 ? 0 : 1);
                qvole.history.Add(new StateChangeRecord
                {
                    date = q.save.gameTime.ToKeyString(), field = "location",
                    fromValue = "lowland", toValue = "center", triggeredBy = "vole_expansion"
                });
            }
            string who2 = VoleTownSystem.VoleAppellation(q.save);
            for (int m = 0; m < 3; m++)
            {
                q.save.gameTime.Advance(1);
                qvole.history.Add(new StateChangeRecord
                {
                    date = q.save.gameTime.ToKeyString(), field = "location",
                    fromValue = "lowland", toValue = "center", triggeredBy = "vole_expansion"
                });
            }
            string who5 = VoleTownSystem.VoleAppellation(q.save);
            bool drift = who0 == "田鼠" && who2 == "它们" && who5 == "镇子";
            Debug.Log($"[{(drift ? "PASS" : "FAIL")}] 称谓三档漂移（\"{who0}\" → \"{who2}\" → \"{who5}\"）");
        }

        // ── V1 D4：缺席信章节语气 ────────────────────────────────────
        // 窗口内翻过纪元章节 → chapter_turned 条目按永久事件档（salience 3）必进信，
        // 且整封换编年史语气；翻页单独足以成信（不能被当噪音丢掉）。
        [MenuItem("GlimmerDiary/Test Absence Letter Chapter Tone")]
        public static void RunAbsenceChapterTone()
        {
            Debug.Log("=== AbsenceChapterTone (EditMode, V1 D4) ===");

            bool s3 = AbsenceLetterComposer.Salience("chapter_turned:settlement->town") == 3;
            Debug.Log($"[{(s3 ? "PASS" : "FAIL")}] chapter_turned:* salience = 3（永久事件档）");

            var segment = new List<WorldChronicleEntry>
            {
                new WorldChronicleEntry { eventId = "behavior_fox:Patrol:RodentExpansion",
                                          text = "第1年 9月1日 狐狸把东侧走了两遍。" },
                new WorldChronicleEntry { eventId = "chapter_turned:settlement->town",
                                          text = "第1年 9月2日 土堆之间踩出了路。——这一页，世界有了镇子。" },
            };
            var letter = AbsenceLetterComposer.Compose(segment, 0, null, "第1年 9月3日", chapterCrossed: true);
            bool a1 = letter != null && letter.text.Contains("土堆之间踩出了路");
            bool a2 = letter != null && letter.text.StartsWith("你离开的这些天，世界翻过了一页——");
            Debug.Log($"[{(a1 ? "PASS" : "FAIL")}] chapter_turned 条目进信");
            Debug.Log($"[{(a2 ? "PASS" : "FAIL")}] 跨章窗口整封换编年史语气开头");

            // 对照：同一 segment 不带翻页标记 → 日常语气开头（条目仍按 salience 进信）
            var plain = AbsenceLetterComposer.Compose(segment, 0, null, "第1年 9月3日");
            bool a3 = plain != null && plain.text.StartsWith("你离开的这些天——")
                                   && !plain.text.Contains("翻过了一页");
            Debug.Log($"[{(a3 ? "PASS" : "FAIL")}] 无翻页标记 → 保持日常语气");

            // 窗口只有翻页、无任何逐日条目 → 仍成信（翻页单独足以成信）
            var onlyTurn = AbsenceLetterComposer.Compose(
                new List<WorldChronicleEntry>(), 0, null, "第1年 9月3日", chapterCrossed: true);
            bool a4 = onlyTurn != null && onlyTurn.text.Contains("翻过了一页");
            Debug.Log($"[{(a4 ? "PASS" : "FAIL")}] 窗口只翻页无其他条目 → 仍成信");
        }

        // ── V1 D5：新生地层——入土、沉降、三阶段 ─────────────────────
        // 玩家视角：土堆不会凭空消失——太老的会沉下去，先塌成旧迹，再沉成只余一点土色。
        // 直拍 stratumSys.Tick（不走全日管线）：排除驱动层额外造堆/纪元钟翻页的干扰，单变量验证。
        [MenuItem("GlimmerDiary/Test Strata Sedimentation")]
        public static void RunStrata()
        {
            var p = BuildPipeline();
            var save = p.save; var reg = p.reg;
            var vole = reg.GetAnimal("vole");

            Debug.Log("=== Strata (EditMode, V1 D5) ===");

            // 阶段 1：7 条土堆记录逐日铺开（> MoundKeepCount=6）→ 最老一条出窗入土
            for (int m = 0; m < 7; m++)
            {
                save.gameTime.Advance(m == 0 ? 0 : 1);
                vole.history.Add(new StateChangeRecord
                {
                    date = save.gameTime.ToKeyString(), field = "location",
                    fromValue = "lowland", toValue = "center", triggeredBy = "vole_expansion"
                });
                p.stratumSys.Tick(save.gameTime, p.env.State, save.currentEEnv);
            }
            bool buried1 = save.strata.Count == 1 && save.strata[0].kind == "mound";
            Debug.Log($"[{(buried1 ? "PASS" : "FAIL")}] 第 7 堆出生 → 最老土堆出窗入土（strata={save.strata.Count}）");

            // 阶段 2：深度只增不减（沉降是积分，不是状态切换）
            float d0 = save.strata[0].depth;
            for (int d = 0; d < 10; d++)
            {
                save.gameTime.Advance(1);
                p.stratumSys.Tick(save.gameTime, p.env.State, save.currentEEnv);
            }
            bool grew = save.strata[0].depth > d0 + 0.3f;   // 10 天中性天气 ≈ +0.4 起
            Debug.Log($"[{(grew ? "PASS" : "FAIL")}] 沉降 10 天深度累计（{d0:F2} → {save.strata[0].depth:F2}）");

            // 阶段 3：跨过 RelicMaxDepth → 地层档（不可点由 binder 读 depth 判定，这里验数据侧过阈）
            save.strata[0].depth = StratumRecord.RelicMaxDepth - 0.01f;
            save.gameTime.Advance(1);
            p.stratumSys.Tick(save.gameTime, p.env.State, save.currentEEnv);
            bool crossed = save.strata[0].depth >= StratumRecord.RelicMaxDepth && !save.strata[0].exposed;
            Debug.Log($"[{(crossed ? "PASS" : "FAIL")}] 深度沉过遗存档上限 → 进入地层档（depth={save.strata[0].depth:F2}）");

            // 阶段 4：塌洞出生即入土（方案 A）——键格式与 binder T2 口径一致，断代锚=荒年
            var center = save.locations.Find(l => l.locationId == "center");
            string cdate = save.gameTime.ToKeyString();
            center.permanentChanges.Add(new PermanentTerrainRecord
                { date = cdate, changeType = "burrow_collapse", description = "测试塌洞" });
            p.stratumSys.Tick(save.gameTime, p.env.State, save.currentEEnv);
            string ckey = $"collapse|center|{cdate}|burrow_collapse";
            var col = save.strata.Find(s => s.sourceKey == ckey);
            bool colOk = col != null && col.kind == "collapse"
                      && col.chapterOrdinal == 0 && col.chapterAtBurial == EraSystem.WildYears;
            Debug.Log($"[{(colOk ? "PASS" : "FAIL")}] 塌洞出生即入土 + 断代锚=荒年（{(col != null ? col.chapterAtBurial : "未入土")}）");

            // 断代句对照：翻过一次页后入土的记录，章节锚随之更新
            //（塌洞键 = loc|date|type——第二条须换日期才入得了土）
            save.worldEvents.Add(new WorldEvent
                { type = WorldEventType.ChapterTurned, gameDate = cdate, targetId = EraSystem.RainSeason });
            save.gameTime.Advance(1);
            string cdate2 = save.gameTime.ToKeyString();
            center.permanentChanges.Add(new PermanentTerrainRecord
                { date = cdate2, changeType = "burrow_collapse", description = "测试塌洞二" });
            p.stratumSys.Tick(save.gameTime, p.env.State, save.currentEEnv);
            string ckey2 = $"collapse|center|{cdate2}|burrow_collapse";
            var col2 = save.strata.Find(s => s.sourceKey == ckey2);
            bool chapter2 = col2 != null && col2.chapterOrdinal == 1
                         && col2.chapterAtBurial == EraSystem.RainSeason
                         && StratumSystem.LayerPhrase(col2) == "雨季正盛的时候";
            Debug.Log($"[{(chapter2 ? "PASS" : "FAIL")}] 翻页后入土 → 封闭层第 1 层 + 断代句随章节（{(col2 != null ? StratumSystem.LayerPhrase(col2) : "未入土")}）");
        }

        // ── V1 D6：出露两法（风暴剥蚀 / 田鼠翻土，确定性掷签）─────────
        // 掷签 = Fnv1a(键|日期|钩子)——测试内自算期望值找必中/必不中键，双向断言机制本身。
        [MenuItem("GlimmerDiary/Test Stratum Exposure")]
        public static void RunExposure()
        {
            var p = BuildPipeline();
            var save = p.save; var reg = p.reg;
            var vole = reg.GetAnimal("vole");

            Debug.Log("=== Exposure (EditMode, V1 D6) ===");

            string todayKey = save.gameTime.ToKeyString();
            // 自校验掷签：在候选键里找一枚必中/一枚必不中（确定性哈希，结果可复现）
            string hitKey = null, missKey = null, shallowKey = null;
            for (int i = 0; i < 500 && (hitKey == null || missKey == null || shallowKey == null); i++)
            {
                string k = $"mound|test{i}";
                bool hit = (TraceKeyUtil.Fnv1a($"{k}|{todayKey}|storm") & 0xFFFF) / 65536f
                         < StratumSystem.StormExposeChance;
                if (hit && hitKey == null) hitKey = k;
                if (!hit && missKey == null) missKey = k;
                if (hit && k != hitKey && shallowKey == null) shallowKey = k;   // 浅层对照也用必中键
            }
            save.strata.Add(new StratumRecord { sourceKey = hitKey,  kind = "mound", zone = "center",
                                                buriedDateKey = todayKey, depth = 1.5f });
            save.strata.Add(new StratumRecord { sourceKey = missKey, kind = "mound", zone = "center",
                                                buriedDateKey = todayKey, depth = 1.5f });
            save.strata.Add(new StratumRecord { sourceKey = shallowKey, kind = "mound", zone = "center",
                                                buriedDateKey = todayKey, depth = 0.5f });   // 未沉底，无出露资格

            // 法一：风暴夜（E_env.A > 0.7）——直喂高唤醒向量，不走惯性慢化
            var storm = new EmotionVector { V = 0f, A = 0.9f, T = 1f, S = 0f, C = 0.5f };
            p.stratumSys.Tick(save.gameTime, p.env.State, storm);
            var hitRec = save.strata.Find(s => s.sourceKey == hitKey);
            bool e1 = hitRec.exposed && hitRec.exposedBy == "storm" && hitRec.exposedDateKey == todayKey;
            Debug.Log($"[{(e1 ? "PASS" : "FAIL")}] 风暴夜：必中签出露（exposedBy={hitRec.exposedBy ?? "n/a"}）");
            bool e2 = !save.strata.Find(s => s.sourceKey == missKey).exposed;
            Debug.Log($"[{(e2 ? "PASS" : "FAIL")}] 风暴夜：必不中签按兵不动");
            bool e3 = !save.strata.Find(s => s.sourceKey == shallowKey).exposed;
            Debug.Log($"[{(e3 ? "PASS" : "FAIL")}] 未沉过遗存档的旧物无出露资格（深度门）");

            // 法二：田鼠翻土——次日同区新增土堆（打洞）→ 同区沉底旧物掷签
            save.gameTime.Advance(1);
            string day2 = save.gameTime.ToKeyString();
            string digHit = null, digOtherZone = null;
            for (int i = 0; i < 500 && (digHit == null || digOtherZone == null); i++)
            {
                string k = $"mound|dig{i}";
                bool hit = (TraceKeyUtil.Fnv1a($"{k}|{day2}|vole_dig") & 0xFFFF) / 65536f
                         < StratumSystem.VoleDigExposeChance;
                if (hit && digHit == null) digHit = k;
                if (hit && k != digHit && digOtherZone == null) digOtherZone = k;
            }
            save.strata.Add(new StratumRecord { sourceKey = digHit, kind = "mound", zone = "center",
                                                buriedDateKey = day2, depth = 1.5f });
            save.strata.Add(new StratumRecord { sourceKey = digOtherZone, kind = "mound", zone = "lowland",
                                                buriedDateKey = day2, depth = 1.5f });   // 隔壁区不刨
            vole.history.Add(new StateChangeRecord
            {
                date = day2, field = "location",
                fromValue = "lowland", toValue = "center", triggeredBy = "vole_expansion"   // 当日 center 新洞
            });
            var calm = new EmotionVector { V = 0.5f, A = 0.5f, T = 1f, S = 0f, C = 0.5f };
            p.stratumSys.Tick(save.gameTime, p.env.State, calm);
            var digRec = save.strata.Find(s => s.sourceKey == digHit);
            bool e4 = digRec.exposed && digRec.exposedBy == "vole_dig";
            Debug.Log($"[{(e4 ? "PASS" : "FAIL")}] 田鼠翻土：同区必中签出露（exposedBy={digRec.exposedBy ?? "n/a"}）");
            bool e5 = !save.strata.Find(s => s.sourceKey == digOtherZone).exposed;
            Debug.Log($"[{(e5 ? "PASS" : "FAIL")}] 田鼠翻土：隔壁区的旧物不受影响（区域门）");

            // 出露后沉降冻结（停在地面等玩家发现，不再下沉）
            float dBefore = digRec.depth;
            save.gameTime.Advance(1);
            p.stratumSys.Tick(save.gameTime, p.env.State, calm);
            bool e6 = Mathf.Approximately(digRec.depth, dBefore);
            Debug.Log($"[{(e6 ? "PASS" : "FAIL")}] 出露后深度冻结（{dBefore:F2} → {digRec.depth:F2}）");
        }

        // ── V1 D6：记忆双读（witnessed 两写入路径 + 双语域语料）──────
        // ① 在场见证：非 catch-up 拍后统一打戳；② 读信回执：MarkWitnessed 把点名记录翻真。
        // 同一对象，见证过 → 记忆语气；没见证 → 考古语气。
        [MenuItem("GlimmerDiary/Test Witnessed Dual Register")]
        public static void RunWitnessed()
        {
            var p = BuildPipeline();
            var save = p.save; var reg = p.reg;
            var vole = reg.GetAnimal("vole");

            Debug.Log("=== Witnessed (EditMode, V1 D6) ===");

            // 存量记录（快照前）不应被打戳——只有"本拍新生"算在场见证
            vole.history.Add(new StateChangeRecord
            {
                date = save.gameTime.ToKeyString(), field = "location",
                fromValue = "lowland", toValue = "center", triggeredBy = "vole_expansion"
            });
            var mark = WorldManager.WitnessSnapshot(save);
            vole.history.Add(new StateChangeRecord
            {
                date = save.gameTime.ToKeyString(), field = "location",
                fromValue = "lowland", toValue = "center", triggeredBy = "vole_expansion"
            });
            save.worldEvents.Add(new WorldEvent
                { type = WorldEventType.ChapterTurned, gameDate = save.gameTime.ToKeyString(),
                  targetId = EraSystem.RainSeason });
            WorldManager.WitnessStampNew(save, mark);
            bool w1 = !vole.history[0].witnessed && vole.history[1].witnessed
                   && save.worldEvents[save.worldEvents.Count - 1].witnessed;
            Debug.Log($"[{(w1 ? "PASS" : "FAIL")}] 在场打戳：只戳本拍新生（旧记录不动）");

            // 读信回执：点名塌洞键 → 源记录与（可能已入土的）地层记录同步翻真
            var center = save.locations.Find(l => l.locationId == "center");
            string cdate = save.gameTime.ToKeyString();
            string ck = $"collapse|center|{cdate}|burrow_collapse";
            center.permanentChanges.Add(new PermanentTerrainRecord
                { date = cdate, changeType = "burrow_collapse", description = "测试塌洞" });
            save.strata.Add(new StratumRecord
                { sourceKey = ck, kind = "collapse", zone = "center", buriedDateKey = cdate });
            WorldManager.MarkWitnessed(save, new List<string> { ck });
            bool w2 = center.permanentChanges[center.permanentChanges.Count - 1].witnessed
                   && save.strata.Find(s => s.sourceKey == ck).witnessed;
            Debug.Log($"[{(w2 ? "PASS" : "FAIL")}] 读信回执：源记录与地层记录同步翻真");

            // witnessKeys 随信落档（Compose → 条目，ChronicleLetter 读信时回放给 MarkWitnessed）
            //（条目须达进信档——salience 0 的日常噪音不成信，witnessKeys 也无处依附）
            var letter = AbsenceLetterComposer.Compose(
                new List<WorldChronicleEntry>
                    { new WorldChronicleEntry { eventId = "event_TreeBranchBroke",
                                                text = "第1年 9月1日 猴面包树断了一根枝。" } },
                0, null, "第1年 9月3日", collapseWitnessKeys: new List<string> { ck });
            bool w3 = letter != null && letter.witnessKeys != null && letter.witnessKeys.Contains(ck);
            Debug.Log($"[{(w3 ? "PASS" : "FAIL")}] witnessKeys 随信落档");

            // 双语域语料：同键同断代，见证与否决定说法；{layer} 必被替换
            string mem  = TraceCaptionBank.Pick("relic", "k1", witnessed: true,  layerPhrase: "镇子还在的时候");
            string arch = TraceCaptionBank.Pick("relic", "k1", witnessed: false, layerPhrase: "镇子还在的时候");
            bool w4 = mem != arch && !mem.Contains("{layer}") && !arch.Contains("{layer}");
            Debug.Log($"[{(w4 ? "PASS" : "FAIL")}] 记忆/考古双语域分句 + 断代句注入");
            string noLayer = TraceCaptionBank.Pick("exposed", "k2", witnessed: false);
            bool w5 = !noLayer.Contains("{layer}");   // 缺断代句时回退"很久以前"，不留占位符
            Debug.Log($"[{(w5 ? "PASS" : "FAIL")}] 断代句缺省回退（不留 {{layer}} 占位符）");
        }
    }
}
#endif
