using System;
using System.Collections.Generic;
using System.Text;
using GlimmerDiary.Data;

namespace GlimmerDiary.Core
{
    // ─────────────────────────────────────────────────────────────
    // 缺席信合成器（矩阵补全 §5.5 第 1 行 / C5，后续优先级 1）
    //
    // catch-up 期间产生的逐日世界志不再整段丢弃：按 salience 取 ≤maxEntries
    // 条合成一封"你离开的这些天——"，信末点名窗口内新增的永久痕迹（断枝/塌洞），
    // 引导玩家去现场确认——缺席期变化的归因桥。
    //
    // 纯函数：只做文本聚合，不碰任何模拟状态。整段无 ≥1 级条目且窗内
    // 无永久痕迹时返回 null——不给玩家塞"什么也没发生"的噪音（Mountain 原则）。
    // ─────────────────────────────────────────────────────────────
    public static class AbsenceLetterComposer
    {
        public const string LetterEventId = "absence_letter";

        // segment：catch-up 区间新产生的 chronicle 段（按时间先后）
        // branchBreaksInWindow：同窗口 TreeBranchBroke 事件数（07-31 起有独立世界志文案）
        // collapseLocationNames：同窗口新增 burrow_collapse 的 location displayName 列表
        // 返回合成信条目；不值得写信时返回 null
        public static WorldChronicleEntry Compose(
            List<WorldChronicleEntry> segment,
            int branchBreaksInWindow,
            List<string> collapseLocationNames,
            string todayDisplay,
            int maxEntries = 5,
            NaturalRhythmState rhythm = null)   // §5.5 优先级 6：季节尾注（秋/冬一句，春夏不写）
        {
            // ① salience 分级：级别降序，同级新→旧（segment 本身旧→新，index 降序即新→旧）
            var picked = new List<WorldChronicleEntry>();
            for (int tier = 3; tier >= 1; tier--)
            {
                for (int i = segment.Count - 1; i >= 0; i--)
                {
                    if (Salience(segment[i].eventId) != tier) continue;
                    picked.Add(segment[i]);
                    if (picked.Count >= maxEntries) break;
                }
                if (picked.Count >= maxEntries) break;
            }

            bool hasPointers = branchBreaksInWindow > 0
                            || (collapseLocationNames != null && collapseLocationNames.Count > 0);
            if (picked.Count == 0 && !hasPointers) return null;

            // ② 合成信体：头部 + 入选条目原文（模板已含日期）+ 永久痕迹点名句
            var sb = new StringBuilder();
            sb.AppendLine("你离开的这些天——");
            foreach (var e in picked)
                sb.AppendLine(e.text.Trim());

            if (branchBreaksInWindow == 1)
                sb.AppendLine("猴面包树断了一根枝，如今还看得出来。");
            else if (branchBreaksInWindow > 1)
                sb.AppendLine($"猴面包树断了 {branchBreaksInWindow} 根枝，如今还看得出来。");

            if (collapseLocationNames != null)
                foreach (var name in collapseLocationNames)
                    sb.AppendLine($"{name}多了一处塌洞，水退了也留在那里。");

            // 季节尾注：信的落款带上季节的语气（秋起风、冬转静；春夏留白）
            switch (rhythm?.season)
            {
                case Season.Autumn: sb.AppendLine("风开始多了。"); break;
                case Season.Winter: sb.AppendLine("夜里安静得很。"); break;
            }

            return new WorldChronicleEntry
            {
                entryId      = Guid.NewGuid().ToString(),
                gameDate     = todayDisplay,
                eventId      = LetterEventId,
                text         = sb.ToString().TrimEnd(),
                hasBeenShown = false
            };
        }

        // 3=永久事件 2=迁移/到离场/涌现 1=天气极值 0=日常噪音（不进信）
        public static int Salience(string eventId)
        {
            switch (eventId)
            {
                case "baobab_branch_broken":
                case "vole_burrow_abandoned":
                case "event_TreeBranchBroke":
                    return 3;

                case "vole_relocate_flood":
                case "migratory_bird_arrival":
                case "event_AnimalDeparted":
                case "event_WeaverBirdDeparted":
                case "event_WeaverBirdReturned":
                case "event_QuietConvergence":
                    return 2;

                default:
                    // WeatherHarsh 语料系列："behavior_fox:Rest:WeatherHarsh" 等
                    if (eventId != null && eventId.StartsWith("behavior_")
                        && eventId.Contains("Harsh"))
                        return 1;
                    return 0;
            }
        }
    }
}
