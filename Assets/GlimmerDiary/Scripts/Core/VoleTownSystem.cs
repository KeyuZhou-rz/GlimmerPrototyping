using System.Collections.Generic;
using UnityEngine;
using GlimmerDiary.Data;

namespace GlimmerDiary.Core
{
    // 田鼠镇（V1 清单 D3 / §4.3）：部落化 ×1。
    //
    // 红线：文明的复杂度加在痕迹的语法上，不加在动物的 AI 上——本系统一行田鼠 AI 都不碰，
    // 只读土堆记录（vole history），在"成气候"时落下一条小径记录（痕迹语法的升级）。
    //
    // 单写者：WorldSaveData.voleTrails。每 SimulatePass 恰好 Tick 一次（在 EraSystem 之后，
    // 读到当日最新章节——镇散判据之一）。
    //
    // 生命周期：活跃土堆 ≥ 阈值 → 成形（快照土堆键连线）→ 镇在每日重现（不老化）
    //   → 镇散（章节入衰，或活跃数跌破阈值持续 N 天）→ lapsed 冻结 → 痕迹层按 FadeDays 淡出。
    // 塌洞照旧永久（不可逆原则）——小径不是疤，是"还活着的惯例"，惯例可以被遗忘。
    public class VoleTownSystem
    {
        public const int MoundsForTown          = 5;   // §4.3 阈值（与 EraSystem.MoundsToTown 同值）
        public const int BelowThresholdToLapse  = 5;   // 活跃数跌破阈值持续 N 天 → 镇散
        private const int ActiveWindowDays      = 12;  // 与 EraSystem.CountActiveMounds 同窗口

        private readonly WorldSaveData _save;

        public VoleTownSystem(WorldSaveData save)
        {
            _save = save;
            _save.voleTrails ??= new List<VoleTrailRecord>();   // 旧档零迁移
        }

        public void Tick(GameDateTime time)
        {
            int today = time.ToAbsoluteDays();
            var active = ActiveMounds(time);
            var trail  = _save.voleTrails.Find(t => !t.lapsed);

            if (trail == null)
            {
                // 成形：土堆成气候 → 踩出小径（快照当批土堆键，按出生日排序成链）
                if (active.Count >= MoundsForTown)
                {
                    active.Sort((x, y) => x.birthDay.CompareTo(y.birthDay));
                    var rec = new VoleTrailRecord
                    {
                        formedDateKey = time.ToKeyString(),
                        zone          = "center",
                        moundKeys     = active.ConvertAll(m => m.fullKey)
                    };
                    _save.voleTrails.Add(rec);
                    Debug.Log($"[VoleTown] 小径成形 @{rec.formedDateKey}（{rec.moundKeys.Count} 土堆连线）");
                }
                return;
            }

            // 镇散判据一：纪元入衰（洪水/大旱把镇打散了——EraSystem 当日已拍板）
            if (_save.eraState != null && _save.eraState.chapter == EraSystem.Decline)
            {
                Lapse(trail, time);
                return;
            }

            // 镇散判据二：活跃土堆跌破阈值持续 N 天（镇是"还活着的惯例"，惯例断了就散）
            trail.belowThresholdDays = active.Count >= MoundsForTown ? 0 : trail.belowThresholdDays + 1;
            if (trail.belowThresholdDays >= BelowThresholdToLapse)
                Lapse(trail, time);
        }

        private void Lapse(VoleTrailRecord trail, GameDateTime time)
        {
            trail.lapsed       = true;
            trail.lapseDateKey = time.ToKeyString();
            Debug.Log($"[VoleTown] 镇散，小径停止重现 @{trail.lapseDateKey}（{VoleTrailRecord.FadeDays} 日内淡回草里）");
        }

        // 当前活跃土堆（窗口内 + lowland/center），复用 TraceKeyUtil 枚举——与 EraSystem 同口径
        private List<TraceKeyUtil.MoundRecord> ActiveMounds(GameDateTime now)
        {
            var all = new List<TraceKeyUtil.MoundRecord>();
            TraceKeyUtil.EnumVoleMoundRecords(_save, all);
            int today = now.ToAbsoluteDays();
            all.RemoveAll(m => today - m.birthDay > ActiveWindowDays
                            || (m.zone != "lowland" && m.zone != "center"));
            return all;
        }

        // ── 称谓漂移（D3 措辞升级）：信与世界志里"田鼠"的称呼随密度漂移。
        // 只看数量不验真伪——信可能把几群动物讲成一个镇子，误读即魅力（验收三问③）。
        // L2/L3 文本层共用（BehaviorNarrator 的 {voleWho}、TraceCaptionUI 的点击语料）。
        public static string VoleAppellation(WorldSaveData save)
        {
            if (save?.eraState != null && save.eraState.chapter == EraSystem.Town) return "镇子";
            int mounds = EraSystem.CountActiveMounds(save, save.gameTime);
            return mounds >= MoundsForTown ? "镇子"
                 : mounds >= 2               ? "它们"
                 :                             "田鼠";
        }
    }
}
