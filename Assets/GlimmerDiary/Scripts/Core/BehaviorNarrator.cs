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
            // 天气成因（§5.1：雨/雾压低活动——语料提醒，仅在真实下雨/起雾时由驱动层认领）
            NarrateBehavior("fox",            "Rest",        CauseFactor.WeatherHarsh,      FoxRestHarsh,        time);
            NarrateBehavior("migratory_bird", "Settle",      CauseFactor.WeatherHarsh,      BirdSettleHarsh,     time);

            // 2. 离散世界事件（迁移自 Relation_WeaverHabitatLost + 新增）
            int count = _save.worldEvents?.Count ?? 0;
            for (int i = _eventCursor; i < count; i++)
                NarrateEvent(_save.worldEvents[i], time);
            _eventCursor = count;

            // 3. 环境状态解释（§5.5 第 3 行：植被捂水的可读出口——暗示而不直说）
            NarrateWaterRetention(time);
        }

        // 环境语料：雨后低洼水位仍高 ∧ 植被茂密 → "水退得慢，草把水喝住了"。
        // 状态+冷却制（同行为叙事）：条件持续期间每 20 天最多一条，不刷屏。
        private void NarrateWaterRetention(GameDateTime time)
        {
            if (_env == null || _env.Rainfall >= 0.2f) return;
            var lowland = _registry.GetLocation("lowland");
            if (lowland == null || lowland.waterLevel < 0.5f || lowland.vegetationDensity < 0.6f) return;

            const string key = "env:water_retention";
            if (!CooldownPassed(key, time, BEHAVIOR_COOLDOWN_DAYS)) return;

            Emit("env_water_retention", Pick(WaterRetention), time);
            _lastNarratedDay[key] = time.ToAbsoluteDays();
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
            _lastNarratedDay[key] = time.ToAbsoluteDays();
        }

        private void NarrateEvent(WorldEvent e, GameDateTime time)
        {
            // 涌现时刻：按事件 payload 里的物种对分支语气——宿敌对（狐狸+啮齿）情感最重，
            // 中性对更淡。机制泛化，charge 靠 narrator 找回。
            if (e.type == WorldEventType.QuietConvergence)
            {
                Emit($"event_{e.type}",
                     Pick(IsChargedPair(e.payload) ? QuietConvergenceCharged : QuietConvergenceNeutral),
                     time);
                return;
            }

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

        // 宿敌对：捕食者（fox）与啮齿（deer_mouse / vole）罕见地共处
        private static bool IsChargedPair(string speciesCsv)
        {
            if (string.IsNullOrEmpty(speciesCsv)) return false;
            bool hasFox    = speciesCsv.Contains("fox");
            bool hasRodent = speciesCsv.Contains("deer_mouse") || speciesCsv.Contains("vole");
            return hasFox && hasRodent;
        }

        // ── 文案模板（迁移 + 新增；{date}{sky} 占位） ─────────────
        private static readonly string[] DeerMouseBirdAbsent =
{
    // 直接描述收缩行为，细节多余
    "{date} {sky} 洞口朝北的那只鹿鼠，今天没有走到河岸那边。在高地转了转，回去了。",
    
    // 用「洞口」这个意象暗示退守
    "{date} {sky} 鹿鼠把洞口重新清理了一遍。最近它待在里面的时间长了一些。",
    
    // 小报语气，带一点诙谐
    "{date} {sky} 鹿鼠就其近期活动范围缩减一事，未作任何说明。",
    
    // 用空间暗示：东侧变成了「别的东西的地方」
    "{date} {sky} 东侧高地靠近中央的那段路，这几天没有鹿鼠的脚印了。",
    
    // 暗示「等待」而不是「退缩」
    "{date} {sky} 鹿鼠在洞口坐了很久。它在等什么，或者不在等什么，不清楚。",
    
    // 用声音缺席来写
    "{date} {sky} 高地那边安静了一些。鹿鼠还在，只是动静小了。",
    
    // 和其他实体的空间关系
    "{date} {sky} 鹿鼠今天往石头区那边走了走，没有往东。脚印在石头边缘停住了。",
};
        private static readonly string[] VoleExpand =
{
    // 「试探」的动作感
    "{date} {sky} 低洼地那边的田鼠在中央方向挖了一个口子，不深，像是在探探情况。",
    
    // 小报语气，借「产权」暗示领地逻辑
    "{date} {sky} 田鼠一家就中央区域东侧边缘的使用权问题，采取了一些行动。",
    
    // 细节：留下的痕迹而不是行为本身
    "{date} {sky} 石头区和中央之间那段草地，出现了新翻的土。是今天的。",
    
    // 间接视角：从「土」的角度写
    "{date} {sky} 东侧低地新开了一个洞口，朝中央方向。洞口的土是湿的，刚挖不久。",
    
    // 时间暗示：「这阵子」
    "{date} {sky} 这阵子田鼠走得比以前远了一点。每次都在同一个地方停下来。",
    
    // 带一点轻微的「拟人化危机感」
    "{date} {sky} 田鼠在中央方向新立了一个洞口。狐狸那边还不知道。",
    
    // 用「没有反应」来写：世界其他部分的沉默
    "{date} {sky} 低地的田鼠往东走了一段，在那里待了一会儿。那一带没有别的动静。",
    
    // 带季节细节
    "{date} {sky} 田鼠把新洞口开在了向阳的坡上，朝东南。选址经过了一些考虑，看起来。",

    // 借场景里真实存在的巨石定位（视觉可兑现）
    "{date} {sky} 新翻的土堆在两块石头中间，颜色比周围深。是今天的。",
};


        // 狐狸巡逻：细节暗示"田鼠新洞口"这一跨实体成因
        private static readonly string[] FoxPatrolRodent =
{
    // 狐狸「注意到了」但不说原因
    "{date} {sky} 狐狸今天把东侧高地到中央这段走了两遍。第二遍走得慢一些。",
    
    // 嗅觉细节
    "{date} {sky} 狐狸在那堆新土前停下来，闻了一会儿，然后继续走了，没有表示意见。",
    
    // 小报正式感
    "{date} {sky} 狐狸就东侧高地边界事宜进行了例行巡察。",
    
    // 暗示「宣示主权」这个动作
    "{date} {sky} 狐狸在石头区和中央之间的草地上留了几个记号。这不是第一次了。",
    
    // 用「路线」暗示变化：走了以前不常走的地方
    "{date} {sky} 狐狸今晚走了低地那边，不是常走的路。在田鼠那片区域附近绕了一圈。",
    
    // 用时间暗示：「比平时早」
    "{date} {sky} 狐狸比平时早出现在东侧。月亮还没升起来，它就已经在那边了。",
    
    // 带一点不安感，但克制
    "{date} {sky} 狐狸今天的路线多拐了一个弯，绕过了新土那一带，又绕回来了。",
    
    // 用「停住」来写，不写原因
    "{date} {sky} 狐狸在东侧低地边缘停了很久。风从中央方向来。它最后往石头区走了。",

    // 记号与倒伏的草：痕迹本身在场景里可见
    "{date} {sky} 石头边上又多了几个记号。狐狸走过的地方，草是倒的。",
};

        // 候鸟提前离去：细节暗示"狐狸常来河岸"这一跨实体成因
        private static readonly string[] BirdEarlyDepartFox =
{
    // 「空」是视觉上最强的意象
    "{date} {sky} 河岸边空了。它们走得比往年早一些，没有留什么。",

    // 时间错位：「该走的时候还没到」
    "{date} {sky} 候鸟动身了，比该动身的时候早了将近半个月。河岸边那个影子最近来得很勤。",

    // 小报语气，「未作说明」
    "{date} {sky} 候鸟就提前离境一事，未作任何说明。水边的草上留了几根羽毛。",

    // 用「羽毛」这个遗留物来写离去
    "{date} {sky} 河岸的草叶上挂着几根羽毛，是昨天还是前天留下的。候鸟已经不在了。",
    
    // 间接写狐狸的影响，不点明
    "{date} {sky} 候鸟走了。这阵子河岸不太安静，可能和这个有关，也可能没有。",
    
    // 用声音的消失来写
    "{date} {sky} 河岸今早没有声音。鹿鼠往那边走了一下，站了一会儿，回去了。",
    
    // 带时间细节，暗示「急促」
    "{date} {sky} 候鸟昨晚走的。走得很快，没有平时那种盘旋。今天河岸是安静的。",
};

        private static readonly string[] WeaverDeparted =
{
    // 「巢」是最强的意象，不写鸟写巢
    "{date} {sky} 那个球形的巢随断枝倒下了。织巢鸟围着树转了一圈，往东南方向走了。",
    
    // 用「还挂着」来写过渡状态
    "{date} {sky} 织巢鸟走了。巢还挂在断枝上，风一吹就晃。",
    
    // 带时间感：不知道是哪天走的
    "{date} {sky} 树冠东侧安静了一阵子了。织巢鸟是哪天走的，说不清楚。",
    
    // 小报语气
    "{date} {sky} 织巢鸟就猴面包树东侧枝断裂一事，以集体迁离的方式作出回应。",
    
    // 用「树知道了」的拟人暗示
    "{date} {sky} 猴面包树东侧今天很安静。织巢鸟的事，树大概也知道了。",
    
    // 残留物的视角
    "{date} {sky} 断枝上还有一点巢的残骸。风每次吹过来，就掉一点下去。",
};

        private static readonly string[] WeaverReturned =
        {
            "{date} {sky} 树冠重新密起来。织巢鸟又回到了中央那棵树上。",

            "{date} {sky} 织巢鸟回来了，和邻居达成了暂时的友好关系。未来会怎么样，没人知道",

            "{date} {sky} 猴面包树哼起了歌 头顶上残破的鸟巢终于得到了修缮————织巢鸟回来了"
        };

        private static readonly string[] BirdDeparted =
        {
            "{date} {sky} 河岸边的影子少了几个。候鸟动身了。",

            "{date} {sky} 河岸的草丛里安静下来。它们走了，比往年早。",
        };
       private static readonly string[] TreeFlowered =
{
    // 「只有晚上才看得见」这个细节很重要
    "{date} {sky} 猴面包树开花了。只有晚上才看得清，白天又合上了。",
    
    // 用气味写
    "{date} {sky} 树顶上有什么在夜里开了。空气里多了点什么，说不清楚是什么气味。",
    
    // 带动物的连锁
    "{date} {sky} 猴面包树今夜开花。蝙蝠来了两只，在树冠附近绕。虫子也多了。",
    
    // 用「白」来写颜色
    "{date} {sky} 树顶上今晚多了一些白色的东西。花。明天白天会合上，但今晚是开着的。",
    
    // 稀有感
    "{date} {sky} 猴面包树开花了，不是每年都有这种事。",

    // 带地面的痕迹
    "{date} {sky} 猴面包树开花了。早晨树下有一些落下来的花瓣，白色，已经有点褐了。",
};

        // 涌现时刻 · 宿敌对（狐狸 + 啮齿）：永不点破，只写「挨着歇息」这个可观察的行为
        private static readonly string[] QuietConvergenceCharged =
{
    // 「谁也没有先动」是这一刻的核心
    "{date} {sky} 今晚有两个影子挨得近了些。平时它们总是绕着走，这次谁也没有先动。",

    // 用「距离」写，不写身份
    "{date} {sky} 该是猎与被猎的两边，今夜歇在了同一片草里。中间留着一点距离，但没有谁离开。",

    // 小报式的克制
    "{date} {sky} 据观察，两只素来回避彼此的动物，于今夜短暂共处。原因不明，未持续。",

    // 用「没有发生的事」来写
    "{date} {sky} 该追的没有追，该逃的没有逃。它们就那样待着，直到天色变了。",

    // 留一点不确定
    "{date} {sky} 不知是太累还是夜太静，那两个本不该在一起的，今晚靠着同一处歇下了。",

    // 压痕：这一夜在草地上留下的实物证据
    "{date} {sky} 草被压出两片挨着的浅坑，天亮以后还没弹回来。",
};

        // 涌现时刻 · 中性对（邻里）：更淡的语气
        private static readonly string[] QuietConvergenceNeutral =
{
    "{date} {sky} 几只动物今晚聚在树边，没有争抢，也没有走开。",

    "{date} {sky} 同一片地方今夜歇着不止一个身影。夜很安静，谁也没打扰谁。",

    "{date} {sky} 它们凑在一处过了一夜。说不上为什么，就是都在那儿。",

    "{date} {sky} 那片草塌下去一小块，形状像是有谁靠着睡了一夜。",
};

        // 天气 · 狐狸歇窝：雨/雾压低活动——写「没有出门」这件事，不点破原因
        private static readonly string[] FoxRestHarsh =
{
    "{date} {sky} 狐狸一整天没有出洞。气味都被压低了，出门不划算。",

    "{date} {sky} 东侧高地今天没有狐狸的脚印。这种天气，谁也懒得动。",

    "{date} {sky} 狐狸就今日狩猎计划取消一事，未作说明。洞口有它卧过的痕迹。",

    "{date} {sky} 狐狸趴在洞里，耳朵朝着外面。等天气过去。",
};

        // 天气 · 候鸟低伏：雨/雾压低活动频率
        private static readonly string[] BirdSettleHarsh =
{
    "{date} {sky} 候鸟今天没有怎么飞。它们挤在河岸的草里，等天气过去。",

    "{date} {sky} 河岸的鸟群安静了一天。虫子被打下去了，它们的动静也小了。",

    "{date} {sky} 候鸟就今日活动减少一事，未作任何说明。草叶上全是水。",
};

        // 环境 · 植被捂水（§5.5 第 3 行）：写「水退得慢」这件事，草是暗示，不点破机制
        private static readonly string[] WaterRetention =
{
    "{date} {sky} 石头边的地早就干透了，低洼的水却还在。草密的地方，水走得慢。",

    "{date} {sky} 低洼的水退得很慢。岸边的草把水喝住了，一寸一寸地喝。",
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
                .Replace("{sky}",  SkyPhrase.Pick(time, _env));
        }

        private static string Pick(params string[] options) =>
            options[UnityEngine.Random.Range(0, options.Length)];

        private bool CooldownPassed(string key, GameDateTime now, int cooldownDays)
        {
            if (!_lastNarratedDay.TryGetValue(key, out int last)) return true;
            return now.ToAbsoluteDays() - last >= cooldownDays;
        }
    }
}
