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
    }
}
