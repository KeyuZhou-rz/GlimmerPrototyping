using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using GlimmerDiary.Data;

namespace GlimmerDiary.Core
{
    // 涌现时刻检测器（只读旁路系统）——丰富度从现有 drive 系统里长出来，而非节日表。
    //
    // 在 AnimalDriveSystem.Tick 之后、BehaviorNarrator.Narrate 之前运行。
    // 像 narrator 一样只读 behavior 输出 + 实体位置，绝不写实体状态
    // （以免破坏 AnimalDriveSystem 那套精心维护的顺序无关性）。
    // 唯一副作用：命中时向 append-only worldEvents 追加一条 QuietConvergence。
    //
    // 三条铁律：挂在世界自己的逻辑上（动物状态 + 季节 + 情感沉积，不是人类日期）；
    //           稀有（长 cooldown）；永不宣告（只交给 narrator 斜着写一条世界志）。
    public class EmergentMomentDetector
    {
        // 良性白名单：双方都处在休憩/良性 drive 才算"挨着歇息"。
        // 用白名单而非黑名单：新增 drive 默认不算 tender（如 Foraging 是捕猎，不是共处而安）。
        private static readonly HashSet<string> BenignDrives = new()
        {
            "Routine",  // deer_mouse
            "Burrow",   // vole
            "Rest",     // fox
            "Settle",   // migratory_bird
            "Nest",     // weaver_bird
        };

        private readonly EntityRegistry        _registry;
        private readonly WorldSaveData         _save;
        private readonly EmergentMomentTuning  _tuning;

        private int _lastFiredDay = int.MinValue / 2;   // 距上次触发的游戏日（冷却）

        public EmergentMomentDetector(EntityRegistry registry, WorldSaveData save, EmergentMomentTuning tuning = null)
        {
            _registry = registry;
            _save     = save;
            _tuning   = tuning != null ? tuning : ScriptableObject.CreateInstance<EmergentMomentTuning>();
        }

        // ── 纯谓词：无随机、无冷却。供确定性单测与 organic 可行性统计调用。──
        // 命中条件（全部成立）：
        //   1) 空间汇聚：某 zone 内 >=2 只在场动物（取最大簇，平手偏好 center）
        //   2) 良性：簇内每只动物的 drive 都在白名单
        //   3) 平静：E_env.A 低
        //   4) 暖沉积：猴面包树 vitality 高
        public bool WouldFire(out string zone, out string speciesCsv)
        {
            zone = null; speciesCsv = null;

            // 3) 平静
            float arousal = _save.currentEEnv?.A ?? 1f;
            if (arousal >= _tuning.calmA) return false;

            // 4) 暖沉积（猴面包树 vitality = E_env.V 的长期积分）
            var tree = _registry.GetPlant("baobab_main");
            float vitality = tree?.internalState?.vitality ?? 0f;
            if (vitality <= _tuning.warmVitality) return false;

            // 1) 空间汇聚：按 zone 聚合在场动物
            var byZone = new Dictionary<string, List<AnimalEntity>>();
            foreach (var a in _save.animals)
            {
                if (a == null || !a.isPresent || string.IsNullOrEmpty(a.location)) continue;
                if (!byZone.TryGetValue(a.location, out var list))
                    byZone[a.location] = list = new List<AnimalEntity>();
                list.Add(a);
            }

            // 取最大簇；平手偏好 center
            List<AnimalEntity> cluster = null; string clusterZone = null;
            foreach (var kv in byZone)
            {
                if (kv.Value.Count < 2) continue;
                bool better = cluster == null
                    || kv.Value.Count > cluster.Count
                    || (kv.Value.Count == cluster.Count && kv.Key == "center" && clusterZone != "center");
                if (better) { cluster = kv.Value; clusterZone = kv.Key; }
            }
            if (cluster == null) return false;

            // 2) 良性：簇内每只都在白名单
            foreach (var a in cluster)
                if (a.behavior == null || !BenignDrives.Contains(a.behavior.drive)) return false;

            zone       = clusterZone;
            speciesCsv = string.Join(",", cluster.Select(a => a.speciesId).OrderBy(s => s));
            return true;
        }

        // ── 完整检测：冷却（确定性稀有） → 谓词 → 概率门（是概率不是保证） → emit。──
        public void Detect(GameDateTime now)
        {
            if (ToDays(now) - _lastFiredDay < _tuning.cooldownDays) return;
            if (!WouldFire(out string zone, out string speciesCsv)) return;

            float p = IsLongestNight(now) ? _tuning.winterP : _tuning.baseP;
            if (Random.value >= p) return;

            Emit(zone, speciesCsv, now);
            _lastFiredDay = ToDays(now);
        }

        // 最长的夜 = 冬季（仅用 gameTime.month，单一日历，不读真实墙钟 / rhythm.season）
        private static bool IsLongestNight(GameDateTime now) =>
            now.month == 12 || now.month == 1 || now.month == 2;

        // 字段约定同 AnimalDriveSystem.Emit：sourceId=施动者、targetId=zone、payload=细节(物种csv)
        private void Emit(string zone, string speciesCsv, GameDateTime now)
        {
            _save.worldEvents.Add(new WorldEvent
            {
                type     = WorldEventType.QuietConvergence,
                sourceId = "world",
                targetId = zone,
                gameDate = now.ToKeyString(),
                payload  = speciesCsv
            });
            Debug.Log($"[WorldEvent] {WorldEventType.QuietConvergence}  @{zone}  [{speciesCsv}]");
        }

        private static int ToDays(GameDateTime d) =>
            (d.year - 1) * 360 + (d.month - 1) * 30 + d.day;
    }
}
