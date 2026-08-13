using System.Collections.Generic;
using UnityEngine;
using GlimmerDiary.Data;

namespace GlimmerDiary.Core
{
    // 新生地层（V1 清单 D5+D6 / §4.5）：持久型痕迹的终点不再是删除，而是沉降。
    //
    // 不是新机制，是现有痕迹寿命曲线的延长——本系统只做三件事：
    //   ① 入土：土堆挤出"只留最新 N"窗口 / 小径淡完 / 塌洞出生 → 建 StratumRecord；
    //   ② 沉降：每个未出露记录每天 depth += 速率 × (1 + 风 + 雨 + 草)——
    //      风大沙埋快、雨多泥盖快、草盛根缠快；埋得深 = 那几年风大，深度本身就是记录；
    //   ③ 出露：风暴夜（高唤醒）或田鼠在隔壁打洞，把沉下去的旧物翻回地表。
    //
    // 单写者：WorldSaveData.strata。每 SimulatePass 恰好 Tick 一次（在 VoleTownSystem 之后——
    // 小径当日 lapsed 淡完即入土，隔日边界不跨拍）。记录只增不删（不可逆原则③）；
    // 渲染预算阀（MaxPerZone）只影响 L3 画不画，不影响记录存在。
    //
    // 出露掷签是确定性的：Fnv1a(键|日期|钩子)——同一存档同一天结果一致，可测试可复现，
    // 不用 Random（存档回放/重启后场景必须长出同一片地层）。
    public class StratumSystem
    {
        // ── 沉降速率（playtest 调；§4.5 未给数值的取初值）──────────
        public const float BaseRate       = 0.04f;   // 中性天气每天沉降深度（~25 天沉过遗存档）
        public const float WindFactor     = 1.2f;    // 风大沙埋快
        public const float RainFactor     = 0.8f;    // 雨多泥盖快
        public const float GrassFactor    = 0.6f;    // 草盛根缠快

        // ── 出露（§4.5 出露两法 + 2026-08-13 物理钩子修订）──────────────
        public const float StormArousalThreshold = 0.7f;   // 风暴夜（E_env 唤醒过阈）
        public const float StormExposeChance     = 0.08f;  // 每件地层对象/风暴夜
        public const float VoleDigExposeChance   = 0.15f;  // 每件地层对象/同区新洞
        // 物理钩子（不依赖情绪日记——遗物各配各的物理）：
        public const float HeavyRainThreshold    = 0.7f;   // 大雨夜（Rainfall 过阈）剥蚀岩棚画
        public const float HeavyRainExposeChance = 0.10f;  // 岩棚画/大雨夜
        public const float RiverRiseExposeChance = 0.12f;  // 磨盘/上游来水抵达日

        private readonly WorldSaveData _save;

        public StratumSystem(WorldSaveData save)
        {
            _save = save;
            _save.strata ??= new List<StratumRecord>();   // 旧档零迁移
            EnsureDeepRelics();
        }

        // ── 深层遗物（V1 D9，"韵而不案" 2026-08-13 拍板）────────────────
        // 四件各一：岩棚画/石环/石器散布/磨盘石。属于"更早的居住者/过客"——
        // 不命名、不描述形貌；田鼠不是制造者，是无意识的考古学家（挖出它们的可能是田鼠）。
        // 世界创建即长眠于此（沉底档、恒考古语气：witnessed 恒 false——它们比你早，这是设定）。
        // 旧档同样补播：它们"一直在那里"，只是此前无人翻出。
        private static readonly (string relicKind, string zone)[] DeepRelicSeeds =
        {
            ("painting",     "stone_area"),      // 岩棚画：石头区的石壁上
            ("stone_circle", "highland_east"),   // 石环：东边高地的缓坡
            ("tool_scatter", "center"),          // 石器散布：台地中央一侧
            ("quern",        "riverbank"),       // 磨盘石：河边（磨过什么是河边的事）
        };

        private void EnsureDeepRelics()
        {
            foreach (var (relicKind, zone) in DeepRelicSeeds)
            {
                string key = $"deeprelic|{relicKind}";
                if (FindStratum(key) != null) continue;
                _save.strata.Add(new StratumRecord
                {
                    sourceKey       = key,
                    kind            = "deeprelic",
                    relicKind       = relicKind,
                    zone            = zone,
                    buriedDateKey   = "Y1-M9-D1",   // 世界起源日——先于一切章节
                    chapterOrdinal  = 0,
                    chapterAtBurial = EraSystem.WildYears,
                    depth           = StratumRecord.RelicMaxDepth,   // 出生即沉底档
                    witnessed       = false                          // 恒考古（无 witnessed 通道）
                });
            }
        }

        public void Tick(GameDateTime time, WorldEnvironmentState env, EmotionVector eEnv)
        {
            int today = time.ToAbsoluteDays();
            string todayKey = time.ToKeyString();

            Intake(time, today);
            Sediment(env);
            Expose(time, today, todayKey, eEnv, env);
        }

        // 预跑挂起（同 EraSystem.Suspended 口径）：新世界 30 天中性预跑期间不掷深层遗物
        // 的出露签——它们的长眠不该结束在玩家到达之前。catch-up（真实缺席）不在此列：
        // 你不在的日子里它们照样可能被翻出来，信会告诉你。
        public bool Suspended;

        // ── ① 入土 ───────────────────────────────────────────────

        private void Intake(GameDateTime time, int today)
        {
            int chapterOrdinal = CountChapterTurns(today, out string chapterNow);

            // 土堆：不在地表可见集（最新 N 个）里的 → 入土
            var visible = new List<TraceKeyUtil.MoundRecord>();
            TraceKeyUtil.EnumVisibleMounds(_save, visible);
            var visibleKeys = new HashSet<string>();
            foreach (var v in visible) visibleKeys.Add(v.fullKey);

            var allMounds = new List<TraceKeyUtil.MoundRecord>();
            TraceKeyUtil.EnumVoleMoundRecords(_save, allMounds);
            foreach (var m in allMounds)
            {
                if (visibleKeys.Contains(m.fullKey) || FindStratum(m.fullKey) != null) continue;
                Bury(m.fullKey, "mound", m.zone, time, chapterOrdinal, chapterNow, m.witnessed);
            }

            // 小径：镇散且淡满 FadeDays → 入土（与 binder "淡完即撤" 同缝）
            if (_save.voleTrails != null)
                foreach (var tr in _save.voleTrails)
                {
                    if (!tr.lapsed) continue;
                    string vk = $"vtrail|{tr.formedDateKey}";
                    if (today - GameDateTime.ParseKey(tr.lapseDateKey).ToAbsoluteDays() <= VoleTrailRecord.FadeDays) continue;
                    if (FindStratum(vk) != null) continue;
                    Bury(vk, "vtrail", tr.zone, time, chapterOrdinal, chapterNow, tr.witnessed);
                }

            // 塌洞：出生即入土缓慢沉降（批次三拍板方案 A——"永久"体现在记录永不删，
            // 视觉沿 遗存→地层 缓沉；键格式与 binder T2 分支一致）
            if (_save.locations != null)
                foreach (var loc in _save.locations)
                {
                    if (loc.permanentChanges == null) continue;
                    foreach (var pc in loc.permanentChanges)
                    {
                        if (pc.changeType != "burrow_collapse") continue;
                        string ck = $"collapse|{loc.locationId}|{pc.date}|{pc.changeType}";
                        if (FindStratum(ck) != null) continue;
                        Bury(ck, "collapse", loc.locationId, time, chapterOrdinal, chapterNow, pc.witnessed);
                    }
                }
        }

        private void Bury(string sourceKey, string kind, string zone,
                          GameDateTime time, int chapterOrdinal, string chapterNow, bool witnessed)
        {
            _save.strata.Add(new StratumRecord
            {
                sourceKey       = sourceKey,
                kind            = kind,
                zone            = zone,
                buriedDateKey   = time.ToKeyString(),
                chapterOrdinal  = chapterOrdinal,
                chapterAtBurial = chapterNow,
                depth           = 0f,
                witnessed       = witnessed
            });
            Debug.Log($"[Stratum] 入土 {kind} @{time.ToKeyString()}（{zone}，第 {chapterOrdinal} 层下）{sourceKey}");
        }

        // ── ② 沉降（积分：深度只增不减；出露后停在地面，不再下沉）──────

        private void Sediment(WorldEnvironmentState env)
        {
            float wind = env?.WindSpeed ?? 0f;
            float rain = env?.Rainfall  ?? 0f;
            foreach (var s in _save.strata)
            {
                if (s.exposed) continue;
                float grass = FindZoneGrass(s.zone);
                s.depth += BaseRate * (1f + wind * WindFactor + rain * RainFactor + grass * GrassFactor);
            }
        }

        private float FindZoneGrass(string zone)
        {
            if (_save.locations == null) return 0.3f;
            foreach (var l in _save.locations)
                if (l.locationId == zone) return l.vegetationDensity;
            return 0.3f;   // 无此区（如 center 边带）：按中性草量
        }

        // ── ③ 出露（确定性掷签；只有沉入地层档的才够格被"翻出"）────────

        private void Expose(GameDateTime time, int today, string todayKey, EmotionVector eEnv, WorldEnvironmentState env)
        {
            // 法一：风暴剥蚀——高唤醒风暴夜后，每件沉底旧物掷签
            bool stormNight = eEnv != null && eEnv.A > StormArousalThreshold;

            // 法二：田鼠翻土——当日某区新增土堆（打洞），同区沉底旧物掷签
            var dugZones = new List<string>();
            var mounds = new List<TraceKeyUtil.MoundRecord>();
            TraceKeyUtil.EnumVoleMoundRecords(_save, mounds);
            foreach (var m in mounds)
                if (m.birthDay == today && !dugZones.Contains(m.zone))
                    dugZones.Add(m.zone);

            // 物理钩子（2026-08-13 修订，遗物各配各的物理，不依赖情绪日记）：
            //   大雨夜冲刷 → 剥蚀岩棚画（stone_area 无田鼠打洞，风暴又锁情绪日记——
            //   画皮是冲出来的，不是吹出来的）；
            //   上游来水抵达日（河通道已有事件）→ 河水涨落翻出河边磨盘。
            bool heavyRain = (env?.Rainfall ?? 0f) >= HeavyRainThreshold;
            bool riverRise = false;
            if (_save.wings?.upstreamRains != null)
                foreach (var r in _save.wings.upstreamRains)
                    if (r.arriveDateKey == todayKey) { riverRise = true; break; }

            if (!stormNight && dugZones.Count == 0 && !heavyRain && !riverRise) return;

            foreach (var s in _save.strata)
            {
                if (s.exposed || s.depth < StratumRecord.RelicMaxDepth) continue;
                if (Suspended && s.kind == "deeprelic") continue;   // 预跑不掷深层遗物（它们的长眠不结束在玩家到达前）
                if (stormNight && Roll(s.sourceKey, todayKey, "storm", StormExposeChance))
                { ExposeOne(s, time, "storm"); continue; }
                if (dugZones.Contains(s.zone) && Roll(s.sourceKey, todayKey, "vole_dig", VoleDigExposeChance))
                { ExposeOne(s, time, "vole_dig"); continue; }
                if (s.kind == "deeprelic" && s.relicKind == "painting" && heavyRain
                    && Roll(s.sourceKey, todayKey, "heavy_rain", HeavyRainExposeChance))
                { ExposeOne(s, time, "heavy_rain"); continue; }
                if (s.kind == "deeprelic" && s.relicKind == "quern" && riverRise
                    && Roll(s.sourceKey, todayKey, "river_rise", RiverRiseExposeChance))
                    ExposeOne(s, time, "river_rise");
            }
        }

        private void ExposeOne(StratumRecord s, GameDateTime time, string by)
        {
            string todayKey    = time.ToKeyString();
            s.exposed        = true;
            s.exposedDateKey = todayKey;
            s.exposedBy      = by;
            Debug.Log($"[Stratum] 出露 @{todayKey}（{ByLabel(by)}）{s.sourceKey}");

            // 深层遗物（V1 D9）：出露是永久事件——进 worldEvents（append-only）+ 编年史，
            // 缺席信按 salience 3 必提。编年史句给推断不给答案（恒考古语气，认出-only）。
            if (s.kind == "deeprelic")
            {
                _save.worldEvents.Add(new WorldEvent
                {
                    type     = WorldEventType.DeepRelicSurfaced,
                    targetId = s.zone,
                    gameDate = todayKey,
                    payload  = s.relicKind
                });
                _save.pendingChronicles.Add(new WorldChronicleEntry
                {
                    entryId      = System.Guid.NewGuid().ToString(),
                    gameDate     = time.ToDisplayString(),
                    eventId      = "event_DeepRelicSurfaced",
                    text         = $"{time.ToDisplayString()} {DeepRelicChronicleLine(s.relicKind, by)}",
                    hasBeenShown = false
                });
            }
        }

        private static string ByLabel(string by) => by switch
        {
            "storm"      => "风暴剥蚀",
            "heavy_rain" => "大雨冲刷",
            "river_rise" => "河水涨落",
            _            => "田鼠翻土"
        };

        // 出露编年史句：只说"翻出来了什么、谁翻的"，制造者留白（占位各一，设计者并行线扩写）
        private static string DeepRelicChronicleLine(string relicKind, string by)
        {
            bool storm = by == "storm";
            return relicKind switch
            {
                "painting"     => by == "heavy_rain"
                                        ? "那场大雨过后，石头那边被冲出一块有画的石板。赭石的点，骨白的线。画它的人没有留名。"
                                        : storm ? "那场风过后，石头那边露出一块有画的石板。赭石的点，骨白的线。画它的人没有留名。"
                                                : "田鼠打洞带出一角石板——上面有画。赭石的点，骨白的线。画它的人没有留名。",
                "stone_circle" => storm ? "那场风过后，东边高地上露出几块立着的石头，围成一个缺口的圆。摆的人没留下别的。"
                                        : "田鼠打洞碰到硬东西——东边高地上露出几块立着的石头，围成一个缺口的圆。摆的人没留下别的。",
                "tool_scatter" => storm ? "那场风过后，台地中央散出一小簇石片。崩口太整齐了，整齐不是河水的习惯。"
                                        : "田鼠打洞带出一小簇石片。崩口太整齐了，整齐不是河水的习惯。",
                "quern"        => by == "river_rise"
                                        ? "河水涨落一回，河边卧出一块扁石头，中间凹下去一块。磨过什么，磨的人自己知道。"
                                        : storm ? "那场风过后，河边卧出一块扁石头，中间凹下去一块。磨过什么，磨的人自己知道。"
                                                : "田鼠打洞碰到一块扁石头——中间凹下去一块。磨过什么，磨的人自己知道。",
                _              => "有什么被翻出来了。很老，比这里的谁都老。"
            };
        }

        // 确定性掷签：同档同日同钩子结果一致（回放/重启可复现）
        private static bool Roll(string key, string dateKey, string tag, float chance)
        {
            int h = TraceKeyUtil.Fnv1a($"{key}|{dateKey}|{tag}");
            return (h & 0xFFFF) / 65536f < chance;
        }

        // ── 封闭层断代（§4.5：翻页次数 = 地层分界线）────────────────

        // 入土时已发生的翻页次数 + 当时章节（ChapterTurned 日志回放，零新增字段）
        private int CountChapterTurns(int todayAbsDays, out string chapterNow)
        {
            int n = 0;
            chapterNow = _save.eraState != null ? _save.eraState.chapter : EraSystem.WildYears;
            if (_save.worldEvents == null) return n;
            string chapter = EraSystem.WildYears;
            foreach (var e in _save.worldEvents)
            {
                if (e.type != WorldEventType.ChapterTurned) continue;
                if (GameDateTime.ParseKey(e.gameDate).ToAbsoluteDays() > todayAbsDays) continue;
                n++;
                if (!string.IsNullOrEmpty(e.targetId)) chapter = e.targetId;
            }
            if (n > 0) chapterNow = chapter;
            return n;
        }

        // 断代句（{layer} 占位注入点击语料）：由入土章节给出相对年代感。
        // 占位各一，设计者并行线扩写。
        public static string LayerPhrase(StratumRecord s)
        {
            if (s == null) return "很久以前";
            if (s.kind == "deeprelic") return "世界诞生之初";   // D9：深层遗物恒最古层（chapterOrdinal=0，先于一切章节）
            return s.chapterAtBurial switch
            {
                EraSystem.WildYears  => "世界还是荒年的时候",
                EraSystem.RainSeason => "雨季正盛的时候",
                EraSystem.Settlement => "它们刚住下的时候",
                EraSystem.Town       => "镇子还在的时候",
                EraSystem.Decline    => "镇子刚散的时候",
                _                    => "很久以前"
            };
        }

        // ── 查询 ─────────────────────────────────────────────────

        public StratumRecord FindStratum(string sourceKey)
        {
            if (_save.strata == null) return null;
            foreach (var s in _save.strata)
                if (s.sourceKey == sourceKey) return s;
            return null;
        }
    }
}
