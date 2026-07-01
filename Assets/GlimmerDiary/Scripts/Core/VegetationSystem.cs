using UnityEngine;
using GlimmerDiary.Data;

namespace GlimmerDiary.Core
{
    // 植被系统 —— loc.vegetationDensity 的唯一运行时写者（Layer 2 / WorldSimulator）
    //
    // 职责：把"环境/情绪信号 + 实体状态"汇聚到各地点的植被密度。
    //   - 虫害：织巢鸟不在 → 东侧高地植被连续衰减（从 AnimalDriveSystem.TickTree 迁入，
    //     原 Relation_InsectSurgeVegetation 的等价逻辑；单写者归此系统）。
    //   - 气候基线 [SEAM]：翻译层落地后，由信号 4 繁盛 / 信号 5 衰败注入；
    //     此前 loc.vegetationDensity 仅保持初值 + 虫害衰减。
    //
    // 边界：本系统是 loc.vegetationDensity 的唯一写者；AnimalDriveSystem 只读不写。
    //       与 waterLevel 同范本（PropagateEnvironmentToLocations 单写、驱动层只读）。
    public class VegetationSystem
    {
        private readonly EntityRegistry _registry;
        private readonly WorldSaveData  _save;
        private readonly float _insectVegDecay;  // 织巢鸟不在 → 东侧高地植被/d 衰减

        public VegetationSystem(EntityRegistry registry, WorldSaveData save, float insectVegDecay)
        {
            _registry       = registry;
            _save           = save;
            _insectVegDecay = insectVegDecay;
        }

        public void Tick(GameDateTime time)
        {
            // 虫害：织巢鸟不在 → 东侧高地植被衰减（逐字复刻原 TickTree 逻辑）
            // 相位：本系统在 drive.Tick 之前运行，读到的是上一 tick 提交的 weaver 状态，
            //       故虫害带 1-tick 相位滞后（符合系统快照协议：每 tick 推进一级）。
            var weaver = _registry.GetAnimal("weaver_bird");
            if (weaver != null && !weaver.isPresent)
            {
                var he = _registry.GetLocation("highland_east");
                if (he != null && he.vegetationDensity > 0.10f)
                    he.vegetationDensity = Mathf.Clamp01(he.vegetationDensity - _insectVegDecay);
            }

            // [SEAM] 翻译层落地后，在此注入气候基线（信号 4 繁盛 / 信号 5 衰败）：
            //   foreach loc: loc.vegetationDensity = Lerp(loc.vegetationDensity, climateBaseline(loc), α);
            // 此前不接，loc.vegetationDensity 仅保持初值 + 上方虫害衰减。
        }
    }
}
