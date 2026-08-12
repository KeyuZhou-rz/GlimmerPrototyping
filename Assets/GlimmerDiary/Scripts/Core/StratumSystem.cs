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

        // ── 出露（§4.5 出露两法）────────────────────────────────
        public const float StormArousalThreshold = 0.7f;   // 风暴夜（E_env 唤醒过阈）
        public const float StormExposeChance     = 0.08f;  // 每件地层对象/风暴夜
        public const float VoleDigExposeChance   = 0.15f;  // 每件地层对象/同区新洞

        private readonly WorldSaveData _save;

        public StratumSystem(WorldSaveData save)
        {
            _save = save;
            _save.strata ??= new List<StratumRecord>();   // 旧档零迁移
        }

        public void Tick(GameDateTime time, WorldEnvironmentState env, EmotionVector eEnv)
        {
            int today = time.ToAbsoluteDays();
            string todayKey = time.ToKeyString();

            Intake(time, today);
            Sediment(env);
            Expose(time, today, todayKey, eEnv);
        }

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

        private void Expose(GameDateTime time, int today, string todayKey, EmotionVector eEnv)
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

            if (!stormNight && dugZones.Count == 0) return;

            foreach (var s in _save.strata)
            {
                if (s.exposed || s.depth < StratumRecord.RelicMaxDepth) continue;
                if (stormNight && Roll(s.sourceKey, todayKey, "storm", StormExposeChance))
                { ExposeOne(s, todayKey, "storm"); continue; }
                if (dugZones.Contains(s.zone) && Roll(s.sourceKey, todayKey, "vole_dig", VoleDigExposeChance))
                    ExposeOne(s, todayKey, "vole_dig");
            }
        }

        private void ExposeOne(StratumRecord s, string todayKey, string by)
        {
            s.exposed        = true;
            s.exposedDateKey = todayKey;
            s.exposedBy      = by;
            Debug.Log($"[Stratum] 出露 @{todayKey}（{(by == "storm" ? "风暴剥蚀" : "田鼠翻土")}）{s.sourceKey}");
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
