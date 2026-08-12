using System.Collections.Generic;
using UnityEngine;
using GlimmerDiary.Data;

namespace GlimmerDiary.Core
{
    // 侧翼（V1 清单 D7+D8 / §5）：舞台边界之外的世界。
    //
    // 侧翼不是地图、不是场景、永远不可抵达——这里只有两三个慢变量和四件事：
    //   ① 慢变量漂移：西翼旱涝/族群压力、东翼旱情烈度，周粒度（每周一个确定性
    //      目标值，每天走近 1/7）——侧翼的天气不看你的日记，它有自己的日子；
    //   ② 河通道（D7）：西翼越湿，上游夜雨越勤；雨在侧翼落下，水 1~2 日后才到
    //      你这里——"你这里一滴雨没下，河水却涨了"。来水不直写水位：
    //      进 upstreamRains 队列，由 PropagateRainfallToLocations（waterLevel 单写者）消费；
    //   ③ 路通道（D8）：族群压力高 → 西缘出现陌生脚印、数日蔓延入五区；
    //      你的动物离场（AnimalDeparted）→ 东缘留离去标记、数周淡出；
    //      偶有过路客，脚印链 2~3 日横穿舞台，不停留；
    //   ④ 风通道（D8 最简）：西翼旱情烈度 = 地平线尘霾，L3 经 WorldManager 透传读取
    //      （本系统只持有数据，不碰任何视觉）。
    //
    // 单写者：WorldSaveData.wings。每 SimulatePass 恰好 Tick 一次（在 StratumSystem 之后——
    // 侧翼读当日的世界事件与章节终态）。一切掷签确定性：Fnv1a(钩子|日期)——
    // 同一存档同一天同一片侧翼，回放/重启可复现，不用 Random。
    //
    // 红线（§5）：侧翼永远只是侧翼——不可解锁、不可放大、不可前往、没有小地图。
    // 接口预留：EraSystem 等可读 WestDrought01 等静态查询（"西旱"章节等后续拍板再接）。
    public class WingSystem
    {
        // ── 慢变量漂移（周粒度）────────────────────────────────
        public const float DriftRatePerDay = 1f / 7f;   // 每天走向周目标的 1/7

        // ── 河通道 ───────────────────────────────────────────
        public const float RainChanceMin  = 0.03f;   // 西翼大旱时上游夜雨概率/夜
        public const float RainChanceMax  = 0.18f;   // 西翼雨季时概率/夜
        public const float RainAmountBase = 0.30f;   // 水量下限
        public const float RainAmountVar  = 0.50f;   // 水量浮动
        public const float RiverbankInflowRate = 0.5f;   // 抵达水量 → riverbank 水位（Propagate 消费）
        public const float LowlandInflowRate   = 0.1f;   // 漫流入低洼（下游扩散，小头）

        // ── 路通道 ───────────────────────────────────────────
        public const float StrangerChanceBase = 0.04f;   // 陌生脚印概率/日（压力 0 时）
        public const float StrangerChanceVar  = 0.12f;   // 压力满时加成
        public const float PasserbyChance     = 0.03f;   // 过路客概率/日（固定小概率）

        private readonly WorldSaveData _save;

        public WingSystem(WorldSaveData save)
        {
            _save = save;
            _save.wings ??= new WingState();   // 旧档零迁移
        }

        public void Tick(GameDateTime time)
        {
            int today = time.ToAbsoluteDays();
            string todayKey = time.ToKeyString();

            Drift(today);
            RollUpstreamRain(today, todayKey);
            AnnounceArrivals(time, today);
            RollStrangerMarks(today, todayKey);
            ScanDepartures(todayKey);
            RollPasserby(today, todayKey);
        }

        // ── ① 慢变量漂移：每周一个确定性目标，每天走近 1/7 ──────

        private void Drift(int today)
        {
            var w = _save.wings;
            w.west.drought01        = Approach(w.west.drought01,        WeeklyTarget("west_drought", today));
            w.west.groupPressure01  = Approach(w.west.groupPressure01,  WeeklyTarget("west_pressure", today));
            w.east.drought01        = Approach(w.east.drought01,        WeeklyTarget("east_drought", today));
        }

        private static float Approach(float v, float target)
            => Mathf.Lerp(v, target, DriftRatePerDay);

        // 周目标：Fnv1a(翼|量|周序) → [0,1]——同一周全档一致，跨周才换目标
        private static float WeeklyTarget(string tag, int absDay)
            => (TraceKeyUtil.Fnv1a($"wing|{tag}|{absDay / 7}") & 0xFFFF) / 65535f;

        // ── ② 河通道：上游夜雨 → 延迟队列；抵达日发事件+编年史 ──

        private void RollUpstreamRain(int today, string todayKey)
        {
            float wet = 1f - _save.wings.west.drought01;
            float chance = Mathf.Lerp(RainChanceMin, RainChanceMax, wet);
            if (!Roll($"wing_rain|{todayKey}", chance)) return;

            float amount = RainAmountBase + RainAmountVar * Unit($"wing_rain_amt|{todayKey}");
            int delay = 1 + (TraceKeyUtil.Fnv1a($"wing_rain_dly|{todayKey}") & 1);   // 1~2 日
            var arrive = GameDateTime.ParseKey(todayKey);
            arrive.Advance(delay);
            _save.wings.upstreamRains.Add(new UpstreamRainRecord
            {
                fellDateKey   = todayKey,
                arriveDateKey = arrive.ToKeyString(),
                amount        = amount
            });
            Debug.Log($"[Wing] 上游夜雨 @{todayKey}（量 {amount:F2}，{delay} 日后抵达）");
        }

        // 抵达当日：发 WorldEvent（append-only）+ 编年史条目。
        // 水位入账不在这里——PropagateRainfallToLocations 是 waterLevel 单写者，它自己读队列。
        private void AnnounceArrivals(GameDateTime time, int today)
        {
            foreach (var r in _save.wings.upstreamRains)
            {
                if (r.announced) continue;
                if (GameDateTime.ParseKey(r.arriveDateKey).ToAbsoluteDays() != today) continue;
                r.announced = true;
                _save.worldEvents.Add(new WorldEvent
                {
                    type     = WorldEventType.WingUpstreamRain,
                    gameDate = time.ToKeyString(),
                    payload  = r.fellDateKey   // 雨是哪夜下的——信里算得出"迟了几天"
                });
                AddChronicle(time, "event_WingUpstreamRain",
                    "夜里河水涨了一点。这些日子你这里一滴雨也没下——水是从西边来的。");
                Debug.Log($"[Wing] 上游来水抵达 @{time.ToKeyString()}（下雨于 {r.fellDateKey}）");
            }
        }

        // ── ③ 路通道：陌生脚印 / 离去标记 / 过路客 ─────────────

        // 西缘陌生脚印：族群压力越高越可能出现；同时最多一串活跃（淡完才会来下一串）
        private void RollStrangerMarks(int today, string todayKey)
        {
            if (HasActiveTrace("stranger", today)) return;
            float p = _save.wings.west.groupPressure01;
            if (!Roll($"wing_stranger|{todayKey}", StrangerChanceBase + StrangerChanceVar * p)) return;

            _save.wings.traces.Add(new WingTraceRecord
            {
                id = $"stranger|{todayKey}", kind = "stranger", formedDateKey = todayKey
            });
            _save.worldEvents.Add(new WorldEvent
            {
                type = WorldEventType.StrangerPassed, gameDate = todayKey, payload = "stranger"
            });
            AddChronicle(GameDateTime.ParseKey(todayKey),
                "event_StrangerPassed",
                "西边的草里出现了一串你没见过的脚印。它们不像是路过——它们每天都更深一点。");
            Debug.Log($"[Wing] 陌生脚印出现 @{todayKey}（族群压力 {p:F2}）");
        }

        // 离去标记：你的动物自东缘离去（AnimalDeparted）→ 东缘留一串远行的印子
        private void ScanDepartures(string todayKey)
        {
            if (_save.worldEvents == null) return;
            foreach (var e in _save.worldEvents)
            {
                if (e.type != WorldEventType.AnimalDeparted) continue;
                string id = $"depart|{e.sourceId}|{e.gameDate}";
                if (FindTrace(id) != null) continue;
                _save.wings.traces.Add(new WingTraceRecord
                {
                    id = id, kind = "departure", formedDateKey = todayKey, species = e.sourceId
                });
                Debug.Log($"[Wing] 离去标记 @{todayKey}（{e.sourceId} 自东缘离去）");
            }
        }

        // 过路客：固定小概率；2~3 日横穿，不停留。同时最多一位
        private void RollPasserby(int today, string todayKey)
        {
            if (HasActiveTrace("passerby", today)) return;
            if (!Roll($"wing_passerby|{todayKey}", PasserbyChance)) return;

            _save.wings.traces.Add(new WingTraceRecord
            {
                id = $"passer|{todayKey}", kind = "passerby", formedDateKey = todayKey
            });
            _save.worldEvents.Add(new WorldEvent
            {
                type = WorldEventType.StrangerPassed, gameDate = todayKey, payload = "passerby"
            });
            AddChronicle(GameDateTime.ParseKey(todayKey),
                "event_StrangerPassed",
                "有一串脚印从西边的草里来，到东边的山里去。它们没打算留下。");
            Debug.Log($"[Wing] 过路客上路 @{todayKey}");
        }

        // ── 查询（binder/测试共用，行为逻辑单源）────────────────

        public WingTraceRecord FindTrace(string id)
        {
            if (_save.wings?.traces == null) return null;
            foreach (var t in _save.wings.traces)
                if (t.id == id) return t;
            return null;
        }

        // 痕迹可见窗口/蔓延段数/横穿进度：行为静态量在 WingTraceRecord（Data 层）——
        // binder（L3，不导入 Core）与测试都经那里读，此处只委托。
        public static bool TraceVisible(WingTraceRecord t, int todayAbsDays)
            => WingTraceRecord.IsVisible(t, todayAbsDays);

        // 活跃（可见）同类痕迹是否已存在——同时最多一串/一位
        private bool HasActiveTrace(string kind, int today)
        {
            if (_save.wings?.traces == null) return false;
            foreach (var t in _save.wings.traces)
                if (t.kind == kind && WingTraceRecord.IsVisible(t, today)) return true;
            return false;
        }

        // ── 侧翼慢变量的对外只读（EraSystem/L3 透传经此，勿直读字段）──

        public static float WestDrought01(WorldSaveData s)       => s?.wings?.west.drought01 ?? 0f;
        public static float WestGroupPressure01(WorldSaveData s) => s?.wings?.west.groupPressure01 ?? 0f;
        public static float EastDrought01(WorldSaveData s)       => s?.wings?.east.drought01 ?? 0f;

        // ── 内部 ───────────────────────────────────────────────

        // 确定性掷签 / 单位哈希（同 StratumSystem 口径：同档同日同钩子同结果）
        private static bool Roll(string key, float chance)
            => Unit(key) < chance;
        private static float Unit(string key)
            => (TraceKeyUtil.Fnv1a(key) & 0xFFFF) / 65536f;

        private void AddChronicle(GameDateTime time, string eventId, string text)
        {
            _save.pendingChronicles.Add(new WorldChronicleEntry
            {
                entryId      = System.Guid.NewGuid().ToString(),
                gameDate     = time.ToDisplayString(),
                eventId      = eventId,
                text         = $"{time.ToDisplayString()} {text}",
                hasBeenShown = false
            });
        }
    }
}
