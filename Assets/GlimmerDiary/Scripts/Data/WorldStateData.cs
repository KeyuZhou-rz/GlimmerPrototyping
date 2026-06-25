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

        // 所有实体
        public List<AnimalEntity>   animals   = new();
        public List<PlantEntity>    plants    = new();
        public List<LocationEntity> locations = new();

        // 世界志（待显示 / 已显示）
        public List<WorldChronicleEntry> pendingChronicles = new();
        public List<WorldChronicleEntry> shownChronicles   = new();

        // 世界事件日志（离散跃迁，append-only，永不删除）
        public List<WorldEvent> worldEvents = new();
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
