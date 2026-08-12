using System;
using System.Collections.Generic;

namespace GlimmerDiary.Data
{
    // 游戏内时间（简化日历：每月 30 天，每年 12 月）
    [Serializable]
    public class GameDateTime
    {
        public int year  = 1;
        public int month = 1;   // 1-12
        public int day   = 1;   // 1-30

        public string ToDisplayString() => $"第{year}年 {month}月{day}日";
        public string ToKeyString()     => $"Y{year}-M{month}-D{day}";

        // 世界绝对日序（Y1-M1-D1 = 1）。冷却/年龄/日期差的唯一公式——
        // 各系统不得私抄 (year-1)*360+... 副本，改日历制式只改这里。
        public int ToAbsoluteDays() => (year - 1) * 360 + (month - 1) * 30 + day;

        // 解析 ToKeyString 产出的 "Y1-M3-D12"（唯一解析器，容错：格式不符返回默认日期）
        public static GameDateTime ParseKey(string key)
        {
            var d = new GameDateTime();
            if (string.IsNullOrEmpty(key)) return d;
            var parts = key.Split('-');
            if (parts.Length != 3) return d;
            int.TryParse(parts[0].TrimStart('Y'), out d.year);
            int.TryParse(parts[1].TrimStart('M'), out d.month);
            int.TryParse(parts[2].TrimStart('D'), out d.day);
            return d;
        }

        public void Advance(int days = 1)
        {
            day += days;
            while (day   > 30) { day   -= 30; month++; }
            while (month > 12) { month -= 12; year++;  }
        }
    }

    // 世界存档根节点（序列化为 world_state.json）
    [Serializable]
    public class WorldSaveData
    {
        public GameDateTime gameTime = new();

        // 墙钟锚点：上次存档的真实时刻（ISO-8601 "o"）；空 = 新世界，不做 catch-up
        public string lastTickRealTime;

        // 情感历史
        public List<EnvEmotionSnapshot> emotionHistory = new();
        public EmotionVector             currentEEnv;
        // 情绪脉冲（2026-07-27 双时间尺度）：写日记当日的全强回响，随自主日快衰。
        // 旧存档无此字段 → null → EmotionInertiaSystem.Restore 按零脉冲处理。
        public EmotionVector             currentImpulse;

        // 延迟消化（2026-08-03）：注入后尚未释放的脉冲 + 墙钟释放时刻（ISO）。
        // 玩家写完日记就关 app 时靠它们把"世界还没想完的事"带到下次启动。
        // 旧存档无此字段 → null → 按无 pending 处理。
        public EmotionVector             pendingImpulse;
        public string                    pendingImpulseReleaseRealTime;

        // 所有实体
        public List<AnimalEntity>   animals   = new();
        public List<PlantEntity>    plants    = new();
        public List<LocationEntity> locations = new();

        // 世界志（待显示 / 已显示）
        public List<WorldChronicleEntry> pendingChronicles = new();
        public List<WorldChronicleEntry> shownChronicles   = new();

        // 世界事件日志（离散跃迁，append-only，永不删除）
        public List<WorldEvent> worldEvents = new();

        // 规则冷却表（NarrativeRuleEngine 唯一写者；重启重建，冷却跨 session 存活——
        // 原内存字典重启清零，久未登录会立即重触发，见矩阵补全文档 §5 横切注记）
        public List<RuleCooldownRecord> ruleCooldowns = new();

        // 环境积分态（V1 清单 D1）：DroughtDebt/DecayLevel 等按日积分的字段。
        // 此前只活内存，重启靠 catch-up 重算、>90 天深层旱债静默丢失。
        // 旧存档无此字段 → null → WorldEnvironmentSystem 保持构造默认，由积分自然追上。
        public EnvironmentStateSave    environmentState;

        // 纪元钟状态（V1 清单 D2，EraSystem 唯一写者）。
        // 旧存档 → null → EraSystem 构造时按"荒年"（世界初始态）建立。
        public EraStateSave            eraState;

        // 田鼠镇小径（V1 清单 D3，VoleTownSystem 唯一写者）。
        // 旧存档 → null → VoleTownSystem 构造时建空表。
        public List<VoleTrailRecord>   voleTrails;

        // 新生地层（V1 清单 D5，StratumSystem 唯一写者）。
        // 旧存档 → null → StratumSystem 构造时建空表。记录只增不删（不可逆原则）。
        public List<StratumRecord>     strata;

        // 侧翼（V1 清单 D7/D8，WingSystem 唯一写者）。
        // 旧档 → null → WingSystem 构造时建立。侧翼永远只是侧翼：无实体、无地图、不可抵达。
        public WingState               wings;
    }

    // ── 侧翼（V1 §5：舞台边界之外的世界）──────────────────────────
    // 两翼正好接现有拓扑两端：西=riverbank 方向（来水、来风、来者），
    // 东=highland_east 方向（去处、远行）。每翼只持 2~3 个慢变量，周粒度漂移。
    [Serializable]
    public class WingState
    {
        public WingSideState west = new();   // 来向：草原/上游
        public WingSideState east = new();   // 去向：高地/下游

        // 河通道：上游夜雨延迟队列（1~2 日后抵达）。记录留着不删——
        // "哪几场雨到过这里"本身就是编年史素材；消费与否看 arriveDateKey，不看删除。
        public List<UpstreamRainRecord> upstreamRains = new();

        // 路通道痕迹（陌生脚印/离去标记/过路客链）。过期不删——
        // 可见性由 WingSystem.TraceVisible(年龄) 判定，binder 只画可见的。
        public List<WingTraceRecord>    traces = new();
    }

    [Serializable]
    public class WingSideState
    {
        public float drought01;         // 西：旱涝状态（0=雨季未断，1=大旱）；东：旱情烈度
        public float groupPressure01;   // 西：族群压力（高 → 陌生痕迹/过路客概率升）；东翼不用
    }

    // 上游夜雨（D7 河通道）：fell 在侧翼，arrive 在你这里——延迟即诗学。
    [Serializable]
    public class UpstreamRainRecord
    {
        public string fellDateKey;      // 上游下雨的那夜
        public string arriveDateKey;    // 水抵达 riverbank 的那天（fell 后 1~2 日）
        public float  amount;           // 水量（Propagate 消费时按区速率入账）
        public bool   announced;        // 抵达当日是否已发事件+编年史（WingSystem 写）
    }

    // 侧翼痕迹（D8 路通道）。三类共用一表，可见窗口各不同：
    //   stranger  西缘陌生脚印，数日蔓延入五区，12 日淡完；
    //   departure 你的动物自东缘离去留下的标记，21 日淡完；
    //   passerby  过路客脚印链，2~3 日横穿舞台即走，不停留。
    [Serializable]
    public class WingTraceRecord
    {
        public string id;               // stranger|{dateKey} / depart|{species}|{dateKey} / passer|{dateKey}
        public string kind;             // "stranger" / "departure" / "passerby"
        public string formedDateKey;
        public string species;          // departure 专用：谁走了（读 AnimalDeparted.sourceId）

        public const int StrangerFadeDays    = 12;
        public const int DepartureFadeDays   = 21;
        public const int PasserbyCrossDays   = 3;
        public const int StrangerMaxSegments = 5;   // 蔓延入五区（五区=五段）

        // 行为静态量放 Data 层（同 VoleTrailRecord.FadeDays 先例）：
        // WingSystem（L2）与 WorldTraceBinder（L3，不导入 Core）都要读，单源在此。

        // 可见窗口：stranger 12 日 / departure 21 日 / passerby 横穿期间（<3 日）
        public static bool IsVisible(WingTraceRecord t, int todayAbsDays)
        {
            int age = todayAbsDays - GameDateTime.ParseKey(t.formedDateKey).ToAbsoluteDays();
            switch (t.kind)
            {
                case "stranger":  return age <= StrangerFadeDays;
                case "departure": return age <= DepartureFadeDays;
                case "passerby":  return age <  PasserbyCrossDays;
                default:          return false;
            }
        }

        // 陌生脚印蔓延段数：成形当天 1 段（西缘），之后每天更深一段，至多五段
        public static int StrangerSegments(WingTraceRecord t, int todayAbsDays)
        {
            int age = todayAbsDays - GameDateTime.ParseKey(t.formedDateKey).ToAbsoluteDays();
            int n = 1 + age;
            return n < 1 ? 1 : (n > StrangerMaxSegments ? StrangerMaxSegments : n);
        }

        // 过路客横穿进度 0..1：第 0 天在西缘，最后一天到东缘（binder 按进度截断脚印链）
        public static float PasserbyProgress01(WingTraceRecord t, int todayAbsDays)
        {
            int age = todayAbsDays - GameDateTime.ParseKey(t.formedDateKey).ToAbsoluteDays();
            float p = (float)age / (PasserbyCrossDays - 1);
            return p < 0f ? 0f : (p > 1f ? 1f : p);
        }
    }

    // 环境积分态快照：只存有状态字段；Rainfall/WindSpeed/FogDensity/StarVisibility
    // 是无状态信号镜像，每拍由 ConsumeSignals 重译，不入档。
    [Serializable]
    public class EnvironmentStateSave
    {
        public float soilMoisture      = 0.5f;
        public float vegetationDensity = 0.5f;
        public float decayLevel;
        public float creatureAbundance = 0.5f;
        public float droughtDebt;
    }

    // 纪元钟状态：事件驱动的章节机（V1 §4.2）。不看表，只看累积计数。
    [Serializable]
    public class EraStateSave
    {
        public string chapter          = "wild_years";  // 世界初始态 = 荒年
        public string chapterStartDate;                 // ToKeyString
        public int    consecutiveRainyTicks;            // 连续雨超季节基准的 tick 数
        public int    abundantDays;                     // 丰年持续天数（定居前提）
        public int    voleHomeStreakDays;               // 田鼠在 lowland/center 连续在场天数
        public int    droughtStreakDays;                // droughtDebt>0.6 持续天数（衰章前提）
    }

    // 一条规则冷却记录：lastFiredDateKey 用 GameDateTime.ToKeyString() 格式
    [Serializable]
    public class RuleCooldownRecord
    {
        public string ruleId;
        public string lastFiredDateKey;
    }

    // 一条世界志条目
    [Serializable]
    public class WorldChronicleEntry
    {
        public string entryId;
        public string gameDate;      // 发生的游戏内日期
        public string eventId;       // 对应的事件规则 ID
        public string text;          // 最终生成的完整文本
        public bool   hasBeenShown;
        // 记忆双读（V1 D6）：缺席信点名过的痕迹键（collapse|…）。
        // 信件被阅读时，这些键对应的记录 witnessed 翻 true——读信知道了它，也算"见证"。
        public List<string> witnessKeys;
    }
}
