using System;
using System.Collections.Generic;
using UnityEngine;
using GlimmerDiary.Data;

namespace GlimmerDiary.Core
{
    // 文本层（只读行为输出 + 世界事件，永不读内部状态数值）
    //
    // 职责：把动物的 BehaviorOutput（who / drive / zone / cause）和离散世界事件，
    //       按 cause 查表选"最相关环境细节"，产出世界志（pendingChronicles）。
    //       承接从 4 条退役 EntityRelation 迁移来的 textTemplates。
    //
    // 暗示而不直说：文本描述行为，细节暗示其跨实体成因，但永不点破因果。
    public class BehaviorNarrator
    {
        private readonly EntityRegistry _registry;
        private readonly WorldSaveData  _save;
        private WorldEnvironmentState   _env;
        private NaturalRhythmState      _rhythm;

        // 去重：同一叙事键的冷却（游戏日），避免持续行为每 tick 刷屏
        private readonly Dictionary<string, int> _lastNarratedDay = new();
        private int _eventCursor;   // 已叙事过的世界事件游标

        private const int BEHAVIOR_COOLDOWN_DAYS = 20;

        public BehaviorNarrator(EntityRegistry registry, WorldSaveData save)
        {
            _registry    = registry;
            _save        = save;
            _eventCursor = save.worldEvents?.Count ?? 0;   // 不补叙历史事件
        }

        public void SetEnvironment(WorldEnvironmentState env, NaturalRhythmState rhythm)
        {
            _env    = env;
            _rhythm = rhythm;
        }

        public void Narrate(GameDateTime time)
        {
            // 1. 跨实体成因的持续行为（细节暗示行为背后的跨实体原因，永不点破）
            NarrateBehavior("deer_mouse",     "Retreat",     CauseFactor.BirdAbsent,        DeerMouseBirdAbsent, time);
            NarrateBehavior("vole",           "Expand",      CauseFactor.DeerMouseWithdrew, VoleExpand,          time);
            NarrateBehavior("fox",            "Patrol",      CauseFactor.RodentExpansion,   FoxPatrolRodent,     time);
            NarrateBehavior("migratory_bird", "EarlyDepart", CauseFactor.FoxNearby,         BirdEarlyDepartFox,  time);

            // 2. 离散世界事件（迁移自 Relation_WeaverHabitatLost + 新增）
            int count = _save.worldEvents?.Count ?? 0;
            for (int i = _eventCursor; i < count; i++)
                NarrateEvent(_save.worldEvents[i], time);
            _eventCursor = count;
        }

        // 行为叙事：实体当前 drive+cause 命中且冷却已过 → 产出一条
        private void NarrateBehavior(string speciesId, string drive, string cause, string[] templates, GameDateTime time)
        {
            var a = _registry.GetAnimal(speciesId);
            if (a?.behavior == null) return;
            if (a.behavior.drive != drive || a.behavior.cause != cause) return;

            string key = $"{speciesId}:{drive}:{cause}";
            if (!CooldownPassed(key, time, BEHAVIOR_COOLDOWN_DAYS)) return;

            Emit($"behavior_{key}", Pick(templates), time);
            _lastNarratedDay[key] = ToDays(time);
        }

        private void NarrateEvent(WorldEvent e, GameDateTime time)
        {
            string[] templates = e.type switch
            {
                WorldEventType.WeaverBirdDeparted => WeaverDeparted,
                WorldEventType.WeaverBirdReturned => WeaverReturned,
                WorldEventType.AnimalDeparted     => BirdDeparted,
                WorldEventType.TreeFlowered       => TreeFlowered,
                _                            => null
            };
            if (templates == null) return;
            Emit($"event_{e.type}", Pick(templates), time);
        }

        // ── 文案模板（迁移 + 新增；{date}{sky} 占位） ─────────────
        private static readonly string[] DeerMouseBirdAbsent =
        {
            "{date} {sky} 东侧的鹿鼠比以前少见了。",
            "{date} {sky} 鹿鼠只在洞口附近活动，不再往东走了。",
        };
        private static readonly string[] VoleExpand =
        {
            "{date} {sky} 低地那边出现了新的土堆，朝东。田鼠在试探。",
            "{date} {sky} 田鼠往东多走了一段，停了一会儿，又回来了。",
        };
        // 狐狸巡逻：细节暗示"田鼠新洞口"这一跨实体成因
        private static readonly string[] FoxPatrolRodent =
        {
            "{date} {sky} 田鼠新挖的洞口，土还是新的。狐狸在那一带多绕了几圈。",
            "{date} {sky} 狐狸沿着东侧高地走了一遍，停在那堆新土前闻了闻。",
        };
        // 候鸟提前离去：细节暗示"狐狸常来河岸"这一跨实体成因
        private static readonly string[] BirdEarlyDepartFox =
        {
            "{date} {sky} 候鸟走得比往年早。这阵子，狐狸常在河岸附近。",
            "{date} {sky} 芦苇丛空了。它们没等到该走的时候——河岸边那个影子来得太勤了。",
        };
        private static readonly string[] WeaverDeparted =
        {
            "{date} {sky} 织巢鸟的巢随那根枝倒下了。它们围着树转了一圈，然后往东南飞走了。",
            "{date} {sky} 断枝上那个球形的巢，昨天还在，今天不见了。织巢鸟走了。",
        };
        private static readonly string[] WeaverReturned =
        {
            "{date} {sky} 树冠重新密起来。织巢鸟又回到了中央那棵树上。",
        };
        private static readonly string[] BirdDeparted =
        {
            "{date} {sky} 河岸边的影子少了几个。候鸟动身了。",
            "{date} {sky} 芦苇丛里安静下来。它们走了，比往年早。",
        };
        private static readonly string[] TreeFlowered =
        {
            "{date} {sky} 猴面包树夜里开了花。空气里多了点甜味，虫子也多了。",
        };

        // ── 工具 ───────────────────────────────────────────────────
        private void Emit(string eventId, string template, GameDateTime time)
        {
            string filled = FillTemplate(template, time);
            _save.pendingChronicles.Add(new WorldChronicleEntry
            {
                entryId      = Guid.NewGuid().ToString(),
                gameDate     = time.ToDisplayString(),
                eventId      = eventId,
                text         = filled,
                hasBeenShown = false
            });
            Debug.Log($"[BehaviorNarrator] {eventId} → {filled}");
        }

        private string FillTemplate(string template, GameDateTime time)
        {
            return template
                .Replace("{date}", time.ToDisplayString())
                .Replace("{sky}",  PickSky(time));
        }

        private string PickSky(GameDateTime time)
        {
            float moon = (time.day - 1) / 29f;
            float rain = _env?.Rainfall  ?? 0f;
            float fog  = _env?.FogDensity ?? 0f;
            if (fog  > 0.6f) return Pick("雾气漫上来", "雾还没散", "看不见远处");
            if (rain > 0.5f) return Pick("雨还在下", "雨声很密", "积水反着光");
            if (moon < 0.1f) return Pick("新月，天很黑", "星群清晰", "没有月亮");
            if (moon > 0.9f) return Pick("满月", "月光很亮", "影子很清楚");
            return Pick("月亮将圆未圆", "夜风很轻", "星群偏移");
        }

        private static string Pick(params string[] options) =>
            options[UnityEngine.Random.Range(0, options.Length)];

        private bool CooldownPassed(string key, GameDateTime now, int cooldownDays)
        {
            if (!_lastNarratedDay.TryGetValue(key, out int last)) return true;
            return ToDays(now) - last >= cooldownDays;
        }

        private static int ToDays(GameDateTime d) =>
            (d.year - 1) * 360 + (d.month - 1) * 30 + d.day;
    }
}
