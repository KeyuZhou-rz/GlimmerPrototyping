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

        public void Tick(GameDateTime time, WorldEnvironmentState env = null)
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

            TickDandelion(time, env);

            // [SEAM] 翻译层落地后，在此注入气候基线（信号 4 繁盛 / 信号 5 衰败）：
            //   foreach loc: loc.vegetationDensity = Lerp(loc.vegetationDensity, climateBaseline(loc), α);
            // 此前不接，loc.vegetationDensity 仅保持初值 + 上方虫害衰减。
        }

        // 蒲公英（§5.3 风的实体化）：风峰日 → 絮飘；下风区湿度合适 → 落种事件。
        // 下风区按拓扑唯一确定：riverbank 只有唯一邻居 lowland（模拟层无风向概念，
        // 不新建；将来多风向区再议——设计决策）。事件是 N 日后新绒苗（L3 派生）
        // 与缺席信的事实源；append-only，不写任何额外状态。
        private void TickDandelion(GameDateTime time, WorldEnvironmentState env)
        {
            if (env == null) return;
            var dan = _registry.GetPlant("dandelion_riverbank");
            if (dan == null || !dan.isAlive || !dan.isFlowering) return;
            if (env.WindSpeed <= DandelionDriftThreshold) return;

            // 冷却：距上次落种 ≥ DandelionDriftCooldownDays（从 append-only 日志回读，无额外状态）
            int today = time.ToAbsoluteDays();
            if (_save.worldEvents != null)
                for (int i = _save.worldEvents.Count - 1; i >= 0; i--)
                {
                    var e = _save.worldEvents[i];
                    if (e.type != WorldEventType.DandelionSeedsDrifted) continue;
                    if (today - GameDateTime.ParseKey(e.gameDate).ToAbsoluteDays() < DandelionDriftCooldownDays)
                        return;
                    break;
                }

            // 落种门：下风区湿度（风大天干，絮飘了也不生——三层因果都不点破）
            var lowland = _registry.GetLocation(DandelionDownwindZone);
            if (lowland == null || lowland.soilMoisture <= DandelionSproutMoisture) return;

            _save.worldEvents.Add(new WorldEvent
            {
                type     = WorldEventType.DandelionSeedsDrifted,
                sourceId = "dandelion_riverbank",
                targetId = DandelionDownwindZone,   // 字段约定同 EmergentMomentDetector.Emit：targetId=zone
                gameDate = time.ToKeyString(),
                payload  = null
            });
            Debug.Log($"[WorldEvent] {WorldEventType.DandelionSeedsDrifted}  @{DandelionDownwindZone}");
        }

        // 蒲公英参数（矩阵未给数值——初值 playtest 调）
        private const float DandelionDriftThreshold    = 0.7f;   // WindSpeed 峰值线（信号 2 Agitation←E_env.A）
        private const int   DandelionDriftCooldownDays = 5;      // 落种冷却（絮可以天天飘，种不必天天落）
        private const float DandelionSproutMoisture    = 0.4f;   // 下风区落种湿度门
        private const string DandelionDownwindZone     = "lowland";
    }
}
