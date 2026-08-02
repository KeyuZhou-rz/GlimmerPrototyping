using GlimmerDiary.Data;
using GlimmerDiary.Diary;

namespace GlimmerDiary.Core
{
    // ─────────────────────────────────────────────────────────────
    // L1 占位情绪分析（关键词桩）
    //
    // 正式 SentimentEngine（L1）落地前，把日记文本映射为五维 EmotionVector：
    //   V/A 复用 EmotionAnalyzer（中英词典 + 否定/强化词处理）；
    //   T/S/C 用小关键词表，无命中取中性。
    // 真引擎落地后只换实现、不换接口（Analyze 签名不变）。
    //
    // Layer 1 纪律：纯函数，无 Unity API，不读世界状态，只进不出。
    // ─────────────────────────────────────────────────────────────
    public static class SentimentStub
    {
        // 时间感 T（0~1）：思绪飘向过去/未来 → T 升（离开当下）；无时间词 → 中性 0.5
        private static readonly string[] _timeWords =
        {
            "以前", "曾经", "昨天", "过去", "回忆", "小时候", "当年",
            "明天", "以后", "未来", "希望", "打算", "等着", "期待",
            "yesterday", "before", "tomorrow", "future", "hope", "memory", "past"
        };

        // 社会性 S（0~1）：提到他人/关系 → S 升；无社会词 → 低位 0.15（Neutral 锚 S=0）
        private static readonly string[] _socialWords =
        {
            "我们", "朋友", "家人", "他们", "大家", "同事", "同学", "陪", "聚会",
            "妈妈", "爸爸", "孩子", "恋人", "伙伴", "一起",
            "friend", "family", "together", "we ", "they", "people", "mom", "dad"
        };

        // 确定性 C（0~1）：确定词升、犹疑词降，中性 0.5
        private static readonly string[] _certainWords =
        {
            "一定", "肯定", "确实", "总是", "必须", "绝对", "当然",
            "must", "always", "definitely", "certainly", "sure"
        };
        private static readonly string[] _uncertainWords =
        {
            "也许", "可能", "大概", "不知道", "不确定", "或许", "说不定", "好像",
            "maybe", "perhaps", "probably", "unsure", "guess"
        };

        public static EmotionVector Analyze(string text)
        {
            var (v, a) = EmotionAnalyzer.Analyze(text ?? "");
            string lower = (text ?? "").ToLower();

            int timeHits   = CountHits(lower, _timeWords);
            int socialHits = CountHits(lower, _socialWords);
            int certHits   = CountHits(lower, _certainWords);
            int uncertHits = CountHits(lower, _uncertainWords);

            return new EmotionVector
            {
                V = v,
                A = a,
                T = Clamp01(0.5f + 0.25f * System.Math.Min(timeHits, 2)),
                S = Clamp01(socialHits > 0 ? 0.3f + 0.25f * socialHits : 0.15f),
                C = Clamp01(0.5f + 0.2f * (certHits - uncertHits)),
                summary = "stub 关键词分析"
            };
        }

        private static int CountHits(string lower, string[] words)
        {
            int n = 0;
            foreach (var w in words)
                if (lower.Contains(w)) n++;
            return n;
        }

        private static float Clamp01(float x) =>
            x < 0f ? 0f : (x > 1f ? 1f : x);
    }
}
