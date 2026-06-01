using System;

namespace GlimmerDiary.Data
{
    public enum Season { Spring, Summer, Autumn, Winter }

    [Serializable]
    public class RhythmSnapshot
    {
        public string realDate;
        public Season season;
        public float yearProgress;    // 0.0 ~ 1.0
        public float weekProgress;    // 0.0 ~ 1.0
    }

    // 传给 WorldEnvironmentSystem 的实时节律状态
    [Serializable]
    public class NaturalRhythmState
    {
        public Season season;
        public float yearProgress;    // 0.0 ~ 1.0
        public float weekProgress;    // 0.0 ~ 1.0
        public float lightIntensity;  // 0=夜晚, 1=正午，基于实际时刻
    }

    [Serializable]
    public class EmotionVector
    {
        public float V;  // 效价 -1.0 ~ 1.0
        public float A;  // 唤醒度 0.0 ~ 1.0
        public float T;  // 时间感 0.0 ~ 1.0
        public float S;  // 社会性 0.0 ~ 1.0
        public float C;  // 确定性 0.0 ~ 1.0
        public string summary;

        public static EmotionVector Neutral()
        {
            return new EmotionVector
            { V = 0f, A = 0.3f, T = 1f, S = 0f, C = 0.5f };
        }
    }

    [Serializable]
    public class JournalEntry
    {
        public string entryId;
        public string realTimestamp;
        public string rawText;
        public EmotionVector emotion;
    }

    [Serializable]
    public class EnvEmotionSnapshot
    {
        public string realDate;
        public EmotionVector eEnv;
    }
}
