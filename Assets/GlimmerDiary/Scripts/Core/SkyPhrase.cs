using GlimmerDiary.Data;

namespace GlimmerDiary.Core
{
    // {sky} 占位文案的唯一选择器 —— BehaviorNarrator / NarrativeRuleEngine /
    // EntityRelationSystem 共用。此前三处各持一份复制并已分叉（narrator 丢了
    // "月牙"分支），收敛于此：改月相/天气措辞只改这一处。
    //
    // 月相按游戏日推导（30 天/月，纯文本风味，无渲染对应）；
    // 雨/雾读全局环境状态 —— 与画面天气同源（Rainfall→雨粒子、FogDensity→dimness）。
    public static class SkyPhrase
    {
        public static string Pick(GameDateTime time, WorldEnvironmentState env, NaturalRhythmState rhythm = null)
        {
            float moon = (time.day - 1) / 29f;   // 0=新月 … 1=满月
            float rain = env?.Rainfall   ?? 0f;
            float fog  = env?.FogDensity ?? 0f;

            if (fog  > 0.6f) return PickRandom("雾气漫上来", "雾还没散", "看不见远处");
            if (rain > 0.5f) return PickRandom("雨还在下",   "雨声很密", "积水反着光");
            if (moon < 0.1f) return PickRandom("新月，天很黑", "星群清晰",   "没有月亮");
            if (moon > 0.9f) return PickRandom("满月",         "月光很亮",   "影子很清楚");
            if (moon < 0.5f) return PickRandom("月牙高悬",     "月牙偏西",   "细细的一弯月");
            // 默认分支掺季节语气（§5.5 优先级 6：GetSeasonBias 改作文案偏置——
            // 秋天的世界志更常提风，冬天更常提静）。纯文案，零模拟成本；
            // 季节取 rhythm.season（NaturalRhythmSystem.GetSeason 是唯一映射，不重算）
            switch (rhythm?.season)
            {
                case Season.Autumn:
                    return PickRandom("风开始多了", "夜风有了凉意", "天高了一些", "月亮将圆未圆");
                case Season.Winter:
                    return PickRandom("夜里很静", "寒气落下来了", "星子很冷", "月亮将圆未圆");
                default:
                    return PickRandom("月亮将圆未圆", "星群偏移", "夜风很轻");
            }
        }

        private static string PickRandom(params string[] options) =>
            options[UnityEngine.Random.Range(0, options.Length)];
    }
}
