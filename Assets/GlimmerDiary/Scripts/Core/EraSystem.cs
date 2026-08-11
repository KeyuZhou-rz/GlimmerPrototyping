using System;
using UnityEngine;
using GlimmerDiary.Data;

namespace GlimmerDiary.Core
{
    // 纪元钟（V1 清单 §4.2）：事件驱动的章节机。
    // 不看表，只看累积状态；章节可倒退，循环坐庄，不是线性升级。
    //
    // 单写者：WorldSaveData.eraState。每 SimulatePass 恰好 Tick 一次。
    // 每次转换 → ① worldEvents 追加 ChapterTurned（兼作新生地层封闭层数据锚，§4.5）
    //            ② 一封编年史信进 pendingChronicles（占位文案，设计者并行线替换为每章 ≥5 条）。
    // 没有横幅，没有"进入新时代"——章节只以信的语气被玩家读到。
    public class EraSystem
    {
        public const string WildYears  = "wild_years";   // 荒年（世界初始态）
        public const string RainSeason = "rain_season";  // 雨季/丰年
        public const string Settlement = "settlement";   // 定居
        public const string Town       = "town";         // 镇
        public const string Decline    = "decline";      // 衰

        // ── 阈值（playtest 调；§4.2 未给数值的取初值）──────────────
        const int   RainStreakNeeded       = 5;     // 连续 N tick 雨超季节基准 → 雨季
        const int   AbundantDaysToSettle   = 10;    // 丰年持续天数（定居前提之一）
        const int   VoleHomeStreakNeeded   = 10;    // 田鼠在 lowland/center 连续在场 M 天
        const int   MoundsToSettle         = 3;     // 定居：土堆活跃数 ≥ k
        const int   MoundsToTown           = 5;     // 镇：§4.3 阈值
        const int   DroughtStreakToDecline = 10;    // 衰：droughtDebt>0.6 持续天数
        const float FloodWaterLevel        = 0.65f; // 衰：lowland 水位（与 vole_relocate_flood 规则同源）
        const int   MoundActiveWindowDays  = 12;    // 土堆"活跃"窗口（≈T1 湿→干→沉降窗口）

        private readonly WorldSaveData _save;

        // 挂起开关（运行态，不入档）：新世界 30 天中性预跑期间由 WorldManager 挂上——
        // 章节叙事从玩家到达起算，保证每个 ChapterTurned 锚都有对应的信（宪法⑤：不留无叙述的锚）。
        // 当前调参下预跑全程无雨、本就不会翻页，这是给未来调参漂移的保险丝。
        public bool Suspended;

        public EraSystem(WorldSaveData save)
        {
            _save = save;
            _save.eraState ??= new EraStateSave();   // 旧档零迁移：按荒年建立
        }

        public string CurrentChapter => _save.eraState.chapter;

        public void Tick(GameDateTime time, WorldEnvironmentState env, NaturalRhythmState rhythm, EntityRegistry registry)
        {
            if (Suspended) return;
            var st = _save.eraState;

            // 累积计数（全部读已有状态，一行都不新造）
            float baseline = WorldEnvironmentSystem.SeasonBaselineRain(rhythm.season);
            st.consecutiveRainyTicks = env.Rainfall > baseline ? st.consecutiveRainyTicks + 1 : 0;
            st.droughtStreakDays     = env.DroughtDebt > 0.6f   // §4.2 衰章阈值（勿与 0.3 衰败加速线混）
                                     ? st.droughtStreakDays + 1 : 0;
            var vole = registry.GetAnimal("vole");
            bool voleHome = vole != null && vole.isPresent
                            && (vole.location == "lowland" || vole.location == "center");
            st.voleHomeStreakDays = voleHome ? st.voleHomeStreakDays + 1 : 0;
            if (st.chapter == RainSeason) st.abundantDays++; else st.abundantDays = 0;

            int  mounds = CountActiveMounds(_save, time);
            bool flood  = (registry.GetLocation("lowland")?.waterLevel ?? 0f) > FloodWaterLevel;
            bool rainComeback = st.consecutiveRainyTicks >= RainStreakNeeded
                                && env.DroughtDebt < WorldEnvironmentSystem.DroughtDecayThreshold;
            bool collapsing = flood || st.droughtStreakDays >= DroughtStreakToDecline;

            string next = st.chapter switch
            {
                WildYears  => rainComeback ? RainSeason : WildYears,
                RainSeason => env.DroughtDebt > 0.6f ? WildYears   // 丰年夭折，退回荒年
                           : (st.abundantDays >= AbundantDaysToSettle
                              && st.voleHomeStreakDays >= VoleHomeStreakNeeded
                              && mounds >= MoundsToSettle) ? Settlement : RainSeason,
                Settlement => collapsing ? Decline
                           : mounds >= MoundsToTown ? Town : Settlement,
                Town       => collapsing ? Decline : Town,
                Decline    => rainComeback ? RainSeason : Decline,
                _          => WildYears
            };

            if (next != st.chapter) Transition(st.chapter, next, time);
        }

        private void Transition(string from, string to, GameDateTime time)
        {
            var st = _save.eraState;
            st.chapter          = to;
            st.chapterStartDate = time.ToKeyString();
            st.abundantDays     = 0;

            _save.worldEvents.Add(new WorldEvent
            {
                type     = WorldEventType.ChapterTurned,
                sourceId = from,
                targetId = to,
                gameDate = time.ToKeyString(),
                payload  = ""
            });

            string text = Pick(ChronicleFor(to)).Replace("{date}", time.ToDisplayString());
            _save.pendingChronicles.Add(new WorldChronicleEntry
            {
                entryId      = Guid.NewGuid().ToString(),
                gameDate     = time.ToDisplayString(),
                eventId      = $"chapter_turned:{from}->{to}",
                text         = text,
                hasBeenShown = false
            });
            Debug.Log($"[EraSystem] 章节翻页 {from} → {to} @{time.ToKeyString()}");
        }

        // 土堆活跃数（V1 §4.3 口径）：lowland/center 近窗口内的田鼠扩张/搬家记录条数。
        // 与 WorldTraceBinder T1 土堆同一数据源（vole history 的 location 变更记录），
        // 批次二（田鼠镇小径）复用本函数——勿另抄副本。
        public static int CountActiveMounds(WorldSaveData save, GameDateTime now)
        {
            if (save?.animals == null) return 0;
            int today = now.ToAbsoluteDays(), n = 0;
            foreach (var a in save.animals)
            {
                if (a.speciesId != "vole" || a.history == null) continue;
                foreach (var rec in a.history)
                {
                    if (rec.field != "location") continue;
                    if (rec.triggeredBy != "vole_expansion" && rec.triggeredBy != "vole_relocate_flood") continue;
                    if (today - GameDateTime.ParseKey(rec.date).ToAbsoluteDays() > MoundActiveWindowDays) continue;
                    // 扩张土堆按 center 计；搬家新洞口按目的地（同 WorldTraceBinder.cs:279 口径）
                    string zone = rec.triggeredBy == "vole_expansion" ? "center"
                                : (string.IsNullOrEmpty(rec.toValue) ? "lowland" : rec.toValue);
                    if (zone == "lowland" || zone == "center") n++;
                }
            }
            return n;
        }

        // ── 编年史信文案（占位：每章 1 条，设计者并行线扩到 ≥5 条/章）──────────
        private static string[] ChronicleFor(string chapter) => chapter switch
        {
            RainSeason => new[] { "{date} 雨一场接一场地落下来。低洼地的水退得越来越慢。——这一页，世界是湿的。" },
            Settlement => new[] { "{date} 田鼠不再只是路过。洞口的新土连日不散，像有人决定住下来。——这一页，世界有了住户。" },
            Town       => new[] { "{date} 土堆之间踩出了路。这已经不是几只田鼠的事了。——这一页，世界有了镇子。" },
            Decline    => new[] { "{date} 水漫上来（或旱得太久）之后，洞口一个接一个安静了。——这一页，镇子散了。" },
            _          => new[] { "{date} 风比雨多。大地回到只有草和石头的年月。——这一页，世界是荒的。" }
        };

        private static string Pick(string[] options) =>
            options[UnityEngine.Random.Range(0, options.Length)];
    }
}
