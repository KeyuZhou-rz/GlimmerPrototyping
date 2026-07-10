using System;
using GlimmerDiary.Data;
using UnityEngine;

namespace GlimmerDiary.Core
{
    // 自然节律系统：提供独立于玩家输入的世界情感基线漂移
    // 依据现实时间计算季节/周期，输出对 E_env 的自然偏移量
    public class NaturalRhythmSystem
    {
        public Season CurrentSeason { get; private set; }
        public float YearProgress { get; private set; }    // 0.0 ~ 1.0
        public float WeekProgress { get; private set; }    // 0.0 ~ 1.0 (周一=0, 周日≈1)
        public float LightIntensity { get; private set; }  // 0=夜晚, 1=正午
        public float DayProgress { get; private set; }     // 0.0 ~ 1.0，一天中的真实时刻（0=午夜）
        public NaturalRhythmState CurrentState { get; private set; }
        public NaturalRhythmState State => CurrentState;   // WorldManager 使用的简写
        public RhythmSnapshot LastSnapshot { get; private set; }



        public NaturalRhythmSystem()
        {
            Tick(new GameDateTime());   // 默认 Y1-M1-D1，确保构造后 CurrentState 有效
        }

        // 更新节律状态：一钟两粒度
        //   季节 / yearProgress —— 取自 gameTime（catch-up 推进的世界日历）
        //   光照 / weekProgress —— 取自真实墙钟（你的昼夜 / 周作息）
        public void Tick(GameDateTime gameTime)
        {
            var now = DateTime.Now;

            // —— 日历粒度：世界日历（每月30天，每年12月 → 360天）——
            CurrentSeason = GetSeason(gameTime.month);
            int dayOfYear = (gameTime.month - 1) * 30 + gameTime.day;   // 1..360
            YearProgress  = (dayOfYear - 1) / 360f;

            // —— 亚日粒度：真实墙钟 ——
            // 光照强度：正午=1, 6am/6pm=0, 夜间=0（正弦曲线）
            float hour = now.Hour + now.Minute / 60f;
            LightIntensity = Mathf.Clamp01(Mathf.Sin((hour - 6f) / 12f * Mathf.PI));
            DayProgress = hour / 24f;   // 0=午夜，单一写者：仅此处赋值

            // 周一=0, 周日=6 → 归一化到 0~1（保留真实生活周节律）
            int dow = ((int)now.DayOfWeek + 6) % 7;
            WeekProgress = dow / 6f;

            CurrentState = new NaturalRhythmState
            {
                season = CurrentSeason,
                yearProgress = YearProgress,
                weekProgress = WeekProgress,
                lightIntensity = LightIntensity,
                dayProgress = DayProgress
            };

            LastSnapshot = new RhythmSnapshot
            {
                realDate = now.ToString("yyyy-MM-dd"),
                season = CurrentSeason,
                yearProgress = YearProgress,
                weekProgress = WeekProgress
            };
        }

        // 返回基于节律的 E_env 自然漂移量（小幅偏置，非直接赋值）
        // 季节影响 V/A，周末影响 S
        public EmotionVector GetSeasonBias()
        {
            var bias = new EmotionVector();

            switch (CurrentSeason)
            {
                case Season.Spring:
                    bias.V = 0.08f;  // 生机：正向效价
                    bias.A = 0.05f;  // 轻度活跃
                    break;
                case Season.Summer:
                    bias.V = 0.04f;
                    bias.A = 0.12f;  // 高唤醒
                    bias.T = -0.08f; // 时间感模糊（暑热中）
                    break;
                case Season.Autumn:
                    bias.V = -0.08f; // 轻度负向效价
                    bias.T = 0.10f;  // 时间感加强（岁月流逝）
                    break;
                case Season.Winter:
                    bias.V = -0.04f;
                    bias.A = -0.10f; // 低唤醒
                    bias.S = -0.06f; // 社会性收缩
                    break;
            }

            // 周末社会性微升
            if (WeekProgress > 0.71f) // 周六/周日
                bias.S += 0.08f;

            return bias;
        }

        // 将季节偏置以 strength 权重叠加到 eEnv（供 WorldSimulator 调用）
        public void ApplyBiasTo(EmotionVector eEnv, float strength = 0.05f)
        {
            EmotionVector bias = GetSeasonBias();
            eEnv.V = Mathf.Clamp(eEnv.V + bias.V * strength, -1f, 1f);
            eEnv.A = Mathf.Clamp(eEnv.A + bias.A * strength, 0f, 1f);
            eEnv.T = Mathf.Clamp(eEnv.T + bias.T * strength, 0f, 1f);
            eEnv.S = Mathf.Clamp(eEnv.S + bias.S * strength, 0f, 1f);
        }


        private static Season GetSeason(int month)
        {
            return month switch
            {
                12 or 1 or 2 => Season.Winter,
                3 or 4 or 5 => Season.Spring,
                6 or 7 or 8 => Season.Summer,
                _ => Season.Autumn
            };
        }
    }
}
