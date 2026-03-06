using System.Collections.Generic;
using GlimmerDiary.Diary;

namespace GlimmerDiary.Diary
{
    /// <summary>
    /// Keyword-dictionary based bilingual (Chinese + English) emotion analyzer.
    /// Returns valence in [-1, 1] and arousal in [0, 1].
    /// </summary>
    public static class EmotionAnalyzer
    {
        private struct EmotionWord
        {
            public float valence;   // [-1, 1]  negative ↔ positive
            public float arousal;   // [ 0, 1]  calm ↔ excited
            public EmotionWord(float v, float a) { valence = v; arousal = a; }
        }

        // ── Negation seeds ──────────────────────────────────────────────────────
        private static readonly HashSet<string> _negations = new HashSet<string>
        {
            // English
            "not", "no", "never", "neither", "nor", "without",
            // Chinese
            "不", "没", "无", "非", "别", "未", "莫"
        };

        // ── Intensity modifier seeds ────────────────────────────────────────────
        private static readonly HashSet<string> _intensifiers = new HashSet<string>
        {
            // English
            "very", "extremely", "so", "really", "absolutely", "incredibly",
            "terribly", "awfully", "deeply",
            // Chinese
            "非常", "极其", "十分", "特别", "相当", "超级", "很", "太", "极"
        };

        private const float IntensityMultiplier = 1.3f;

        // ── Emotion keyword dictionary ──────────────────────────────────────────
        // Format: keyword → (valence, arousal)
        // High valence / High arousal
        // High valence / Low arousal
        // Low  valence / High arousal
        // Low  valence / Low arousal
        private static readonly Dictionary<string, EmotionWord> _dict =
            new Dictionary<string, EmotionWord>
        {
            // ── High valence / High arousal ────────────────────────────────────
            { "开心",   new EmotionWord( 0.80f, 0.65f) },
            { "高兴",   new EmotionWord( 0.80f, 0.60f) },
            { "兴奋",   new EmotionWord( 0.75f, 0.85f) },
            { "激动",   new EmotionWord( 0.70f, 0.88f) },
            { "快乐",   new EmotionWord( 0.85f, 0.65f) },
            { "欢乐",   new EmotionWord( 0.82f, 0.68f) },
            { "喜悦",   new EmotionWord( 0.82f, 0.62f) },
            { "热情",   new EmotionWord( 0.70f, 0.80f) },
            { "振奋",   new EmotionWord( 0.72f, 0.82f) },
            { "愉快",   new EmotionWord( 0.78f, 0.60f) },
            { "狂喜",   new EmotionWord( 0.90f, 0.95f) },
            { "幸福",   new EmotionWord( 0.88f, 0.55f) },
            { "美好",   new EmotionWord( 0.75f, 0.45f) },
            { "有趣",   new EmotionWord( 0.65f, 0.60f) },
            { "好玩",   new EmotionWord( 0.65f, 0.62f) },
            { "精彩",   new EmotionWord( 0.75f, 0.70f) },
            { "棒",     new EmotionWord( 0.70f, 0.60f) },
            { "太好了", new EmotionWord( 0.85f, 0.72f) },

            { "happy",    new EmotionWord( 0.80f, 0.62f) },
            { "excited",  new EmotionWord( 0.75f, 0.88f) },
            { "joyful",   new EmotionWord( 0.82f, 0.65f) },
            { "elated",   new EmotionWord( 0.85f, 0.80f) },
            { "thrilled", new EmotionWord( 0.80f, 0.85f) },
            { "delighted",new EmotionWord( 0.80f, 0.70f) },
            { "cheerful", new EmotionWord( 0.75f, 0.65f) },
            { "ecstatic", new EmotionWord( 0.90f, 0.95f) },
            { "enthusiastic", new EmotionWord( 0.72f, 0.82f) },
            { "great",    new EmotionWord( 0.70f, 0.55f) },
            { "wonderful",new EmotionWord( 0.80f, 0.58f) },
            { "fantastic",new EmotionWord( 0.82f, 0.72f) },
            { "amazing",  new EmotionWord( 0.80f, 0.75f) },
            { "good",     new EmotionWord( 0.60f, 0.45f) },
            { "fun",      new EmotionWord( 0.68f, 0.65f) },
            { "love",     new EmotionWord( 0.82f, 0.68f) },

            // ── High valence / Low arousal ─────────────────────────────────────
            { "平静",   new EmotionWord( 0.50f, 0.15f) },
            { "满足",   new EmotionWord( 0.72f, 0.25f) },
            { "轻松",   new EmotionWord( 0.65f, 0.22f) },
            { "放松",   new EmotionWord( 0.60f, 0.18f) },
            { "舒适",   new EmotionWord( 0.65f, 0.20f) },
            { "宁静",   new EmotionWord( 0.55f, 0.12f) },
            { "安心",   new EmotionWord( 0.62f, 0.18f) },
            { "温暖",   new EmotionWord( 0.68f, 0.28f) },
            { "感激",   new EmotionWord( 0.70f, 0.35f) },
            { "感动",   new EmotionWord( 0.68f, 0.45f) },

            { "calm",      new EmotionWord( 0.50f, 0.15f) },
            { "peaceful",  new EmotionWord( 0.58f, 0.12f) },
            { "content",   new EmotionWord( 0.70f, 0.22f) },
            { "relaxed",   new EmotionWord( 0.60f, 0.18f) },
            { "comfortable",new EmotionWord( 0.62f, 0.20f) },
            { "serene",    new EmotionWord( 0.55f, 0.12f) },
            { "grateful",  new EmotionWord( 0.70f, 0.32f) },
            { "thankful",  new EmotionWord( 0.68f, 0.30f) },
            { "satisfied", new EmotionWord( 0.72f, 0.25f) },
            { "okay",      new EmotionWord( 0.20f, 0.20f) },
            { "fine",      new EmotionWord( 0.22f, 0.18f) },
            { "alright",   new EmotionWord( 0.20f, 0.20f) },

            // ── Low valence / High arousal ─────────────────────────────────────
            { "愤怒",   new EmotionWord(-0.82f, 0.88f) },
            { "生气",   new EmotionWord(-0.75f, 0.80f) },
            { "焦虑",   new EmotionWord(-0.70f, 0.80f) },
            { "紧张",   new EmotionWord(-0.50f, 0.75f) },
            { "恐惧",   new EmotionWord(-0.80f, 0.85f) },
            { "害怕",   new EmotionWord(-0.72f, 0.78f) },
            { "担心",   new EmotionWord(-0.55f, 0.65f) },
            { "烦躁",   new EmotionWord(-0.65f, 0.78f) },
            { "崩溃",   new EmotionWord(-0.85f, 0.90f) },
            { "痛苦",   new EmotionWord(-0.88f, 0.82f) },
            { "绝望",   new EmotionWord(-0.90f, 0.70f) },
            { "恨",     new EmotionWord(-0.85f, 0.88f) },
            { "讨厌",   new EmotionWord(-0.70f, 0.72f) },
            { "惊吓",   new EmotionWord(-0.65f, 0.88f) },

            { "angry",    new EmotionWord(-0.82f, 0.88f) },
            { "anxious",  new EmotionWord(-0.68f, 0.80f) },
            { "scared",   new EmotionWord(-0.75f, 0.82f) },
            { "afraid",   new EmotionWord(-0.72f, 0.78f) },
            { "nervous",  new EmotionWord(-0.50f, 0.75f) },
            { "worried",  new EmotionWord(-0.55f, 0.68f) },
            { "furious",  new EmotionWord(-0.88f, 0.95f) },
            { "frustrated",new EmotionWord(-0.65f, 0.75f) },
            { "desperate",new EmotionWord(-0.85f, 0.78f) },
            { "panicked", new EmotionWord(-0.80f, 0.92f) },
            { "hate",     new EmotionWord(-0.85f, 0.82f) },
            { "annoyed",  new EmotionWord(-0.60f, 0.68f) },

            // ── Low valence / Low arousal ──────────────────────────────────────
            { "难过",   new EmotionWord(-0.75f, 0.28f) },
            { "伤心",   new EmotionWord(-0.78f, 0.30f) },
            { "悲伤",   new EmotionWord(-0.80f, 0.25f) },
            { "失落",   new EmotionWord(-0.65f, 0.22f) },
            { "沮丧",   new EmotionWord(-0.70f, 0.28f) },
            { "无聊",   new EmotionWord(-0.40f, 0.15f) },
            { "疲惫",   new EmotionWord(-0.45f, 0.18f) },
            { "疲倦",   new EmotionWord(-0.42f, 0.16f) },
            { "孤独",   new EmotionWord(-0.72f, 0.20f) },
            { "寂寞",   new EmotionWord(-0.65f, 0.18f) },
            { "抑郁",   new EmotionWord(-0.85f, 0.18f) },
            { "消沉",   new EmotionWord(-0.70f, 0.16f) },
            { "后悔",   new EmotionWord(-0.65f, 0.30f) },
            { "委屈",   new EmotionWord(-0.68f, 0.32f) },

            { "sad",       new EmotionWord(-0.75f, 0.25f) },
            { "lonely",    new EmotionWord(-0.70f, 0.18f) },
            { "tired",     new EmotionWord(-0.42f, 0.16f) },
            { "bored",     new EmotionWord(-0.40f, 0.15f) },
            { "depressed", new EmotionWord(-0.85f, 0.18f) },
            { "unhappy",   new EmotionWord(-0.72f, 0.28f) },
            { "miserable", new EmotionWord(-0.85f, 0.22f) },
            { "hopeless",  new EmotionWord(-0.88f, 0.20f) },
            { "disappointed",new EmotionWord(-0.65f, 0.28f) },
            { "regret",    new EmotionWord(-0.60f, 0.32f) },
            { "gloomy",    new EmotionWord(-0.65f, 0.20f) },
            { "melancholy",new EmotionWord(-0.72f, 0.22f) },
            { "bad",       new EmotionWord(-0.55f, 0.30f) },
        };

        // Default returned when no emotion keywords are found
        private static readonly (float valence, float arousal) _neutral = (0f, 0.2f);

        // ── Public API ──────────────────────────────────────────────────────────

        /// <summary>
        /// Analyzes <paramref name="text"/> and returns (valence, arousal).
        /// valence ∈ [-1, 1], arousal ∈ [0, 1].
        /// Returns (0, 0.2) when no emotion words are found.
        /// </summary>
        public static (float valence, float arousal) Analyze(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
                return _neutral;

            // English tokens (lower-cased, split by non-letter chars)
            string lower = text.ToLower();
            string[] englishTokens = lower.Split(
                new char[] { ' ', ',', '.', '!', '?', ';', ':', '\t', '\n', '\r', '"', '\'', '-', '(', ')' },
                System.StringSplitOptions.RemoveEmptyEntries);

            float totalValence = 0f;
            float totalArousal = 0f;
            int   hitCount     = 0;

            // ── English matching (exact token lookup) ──────────────────────────
            for (int i = 0; i < englishTokens.Length; i++)
            {
                string token = englishTokens[i];
                if (!_dict.TryGetValue(token, out EmotionWord ew))
                    continue;

                float v = ew.valence;
                float a = ew.arousal;

                // Negation: look back up to 3 tokens
                if (HasNegationBefore_English(englishTokens, i))
                    v = -v;

                // Intensity: look back up to 3 tokens
                if (HasIntensifierBefore_English(englishTokens, i))
                {
                    v *= IntensityMultiplier;
                    a *= IntensityMultiplier;
                }

                totalValence += v;
                totalArousal += a;
                hitCount++;
            }

            // ── Chinese matching (substring scan) ─────────────────────────────
            foreach (var kv in _dict)
            {
                string keyword = kv.Key;
                // Skip pure-ASCII keywords (already handled above)
                if (IsAsciiOnly(keyword))
                    continue;

                int pos = 0;
                while ((pos = text.IndexOf(keyword, pos)) >= 0)
                {
                    float v = kv.Value.valence;
                    float a = kv.Value.arousal;

                    // Negation: look at up to 2 characters immediately before
                    if (HasNegationBefore_Chinese(text, pos))
                        v = -v;

                    // Intensity: look at up to 3 characters immediately before
                    if (HasIntensifierBefore_Chinese(text, pos))
                    {
                        v *= IntensityMultiplier;
                        a *= IntensityMultiplier;
                    }

                    totalValence += v;
                    totalArousal += a;
                    hitCount++;

                    pos += keyword.Length;
                }
            }

            if (hitCount == 0)
                return _neutral;

            float avgValence = UnityEngine.Mathf.Clamp(totalValence / hitCount, -1f, 1f);
            float avgArousal = UnityEngine.Mathf.Clamp(totalArousal / hitCount,  0f, 1f);
            return (avgValence, avgArousal);
        }

        /// <summary>
        /// Convenience method: analyzes <paramref name="entry"/>.rawText,
        /// writes results back, sets isAnalyzed = true, and returns the updated entry.
        /// </summary>
        public static DiaryEntry AnalyzeEntry(DiaryEntry entry)
        {
            var (v, a) = Analyze(entry.rawText);
            entry.valence    = v;
            entry.arousal    = a;
            entry.isAnalyzed = true;
            return entry;
        }

        // ── Helpers ────────────────────────────────────────────────────────────

        private static bool HasNegationBefore_English(string[] tokens, int idx)
        {
            int start = System.Math.Max(0, idx - 3);
            for (int j = start; j < idx; j++)
                if (_negations.Contains(tokens[j]))
                    return true;
            return false;
        }

        private static bool HasIntensifierBefore_English(string[] tokens, int idx)
        {
            int start = System.Math.Max(0, idx - 3);
            for (int j = start; j < idx; j++)
                if (_intensifiers.Contains(tokens[j]))
                    return true;
            return false;
        }

        private static bool HasNegationBefore_Chinese(string text, int keywordPos)
        {
            // Check 1–2 characters before the keyword position
            int start = System.Math.Max(0, keywordPos - 2);
            string prefix = text.Substring(start, keywordPos - start);
            foreach (string neg in _negations)
                if (!IsAsciiOnly(neg) && prefix.Contains(neg))
                    return true;
            return false;
        }

        private static bool HasIntensifierBefore_Chinese(string text, int keywordPos)
        {
            // Check 1–3 characters before the keyword position
            int start = System.Math.Max(0, keywordPos - 3);
            string prefix = text.Substring(start, keywordPos - start);
            foreach (string intensifier in _intensifiers)
                if (!IsAsciiOnly(intensifier) && prefix.Contains(intensifier))
                    return true;
            return false;
        }

        private static bool IsAsciiOnly(string s)
        {
            foreach (char c in s)
                if (c > 127) return false;
            return true;
        }
    }
}
