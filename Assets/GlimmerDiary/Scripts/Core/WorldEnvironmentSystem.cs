using UnityEngine;
using GlimmerDiary.Data;

namespace GlimmerDiary.Core
{
    // 当前世界的完整环境状态，视觉层消费这个
    public class WorldEnvironmentState
    {
        public float SoilMoisture;      // 0-1，土壤湿度
        public float Rainfall;          // 0-1，当前降雨强度
        public float WindSpeed;         // 0-1
        public float FogDensity;        // 0-1
        public float StarVisibility;    // 0-1，星空清晰度
        public float VegetationDensity; // 0-1，植被密度
        public float DecayLevel;        // 0-1，枯败度
        public float CreatureAbundance; // 0-1，动物活跃度
        public float DroughtDebt;       // 0-1，旱债：实际雨对季节基准雨的累积亏欠
    }

    public class WorldEnvironmentSystem
    {
        public WorldEnvironmentState State { get; private set; }

        public WorldEnvironmentSystem()
        {
            State = new WorldEnvironmentState
            {
                SoilMoisture = 0.5f,
                VegetationDensity = 0.5f,
                DecayLevel = 0f,
                CreatureAbundance = 0.5f
            };
        }

        // 消费无状态天气信号 1/2/3/7（近 1:1 镜像到 State）。幂等、零积分——
        // 启动 / 空闲心跳 / 每日模拟都可安全调用。信号 7 苍穹读墙钟光照，
        // 不随心跳刷新会冻结在上次日记时刻（星空夜里不出现的根因）。
        public void ConsumeSignals(WorldSignals signals)
        {
            State.Rainfall       = signals.Wetness;
            State.WindSpeed      = signals.Agitation;
            State.FogDensity     = signals.Dimness;
            State.StarVisibility = signals.Firmament;
        }

        // 每日有状态积分 + 信号消费：仅每模拟 tick 调用恰好一次（SimulatePass）。
        // 启动/重置/心跳路径只走 ConsumeSignals，否则 Soil/Decay 等按"次"而非按"天"积分。
        // （信号 4/5/6 暂留此处：DecayLevel / CreatureAbundance / 全局 VegetationDensity / SoilMoisture）
        public void UpdateFromEEnv(EmotionVector eEnv, WorldSignals signals, NaturalRhythmState rhythm = null)
        {
            ConsumeSignals(signals);

            // 土壤湿度：天气的累积效应（缓慢靠近降雨强度）
            State.SoilMoisture = Mathf.Lerp(State.SoilMoisture, State.Rainfall, 0.15f);

            // 旱债（矩阵补全 §5.5 第 2 行）：实际雨对季节基准雨的累积亏欠。
            // 雨超基准自然扣减——"下雨扣减"与"雨季衰减"同一公式达成。
            // 本字段单写者在此；驱动层/痕迹层只读。
            if (rhythm != null)
            {
                State.DroughtDebt = Mathf.Clamp01(State.DroughtDebt +
                    (SeasonBaselineRain(rhythm.season) - State.Rainfall) * DroughtAccumulateK);
            }

            // 植被：湿度和衰败度的函数
            float targetVegetation = State.SoilMoisture * (1f - State.DecayLevel);
            State.VegetationDensity = Mathf.Lerp(State.VegetationDensity, targetVegetation, 0.05f);

            // 衰败度：V 长期偏负时缓慢累积，V 偏正时缓慢恢复；旱债过线后累积加速
            float decayDelta = eEnv.V < -0.3f ? 0.01f : -0.005f;
            if (State.DroughtDebt > DroughtDecayThreshold)
                decayDelta *= DroughtDecayMultiplier;
            State.DecayLevel = Mathf.Clamp01(State.DecayLevel + decayDelta);

            // 动物活跃度：季节 × E_env 的 S 维度
            State.CreatureAbundance = Mathf.Lerp(State.CreatureAbundance,
                                                  eEnv.S * 0.5f + 0.3f, 0.1f);
        }

        // ── 积分态存档（V1 D1）─────────────────────────────────
        // 只Snapshot/Restore 有状态字段；无状态信号镜像由 ConsumeSignals 每拍重译。
        public EnvironmentStateSave Snapshot() => new EnvironmentStateSave
        {
            soilMoisture      = State.SoilMoisture,
            vegetationDensity = State.VegetationDensity,
            decayLevel        = State.DecayLevel,
            creatureAbundance = State.CreatureAbundance,
            droughtDebt       = State.DroughtDebt
        };

        // 旧档 null → 保持构造默认（积分随后自然追上，不跳变）
        public void Restore(EnvironmentStateSave s)
        {
            if (s == null) return;
            State.SoilMoisture      = s.soilMoisture;
            State.VegetationDensity = s.vegetationDensity;
            State.DecayLevel        = s.decayLevel;
            State.CreatureAbundance = s.creatureAbundance;
            State.DroughtDebt       = s.droughtDebt;
        }

        // ── 旱债参数（playtest 调）──────────────────────────────
        // 季节基准雨：非洲草原夏雨型（雨季在夏季）
        // k=0.05：无雨秋季（基准 0.3）约 20 天过 0.3（decay 加速）、40 天过 0.6（地裂/收敛）；
        // 雨季没下透（实际雨 ≈ 基准一半）整个月缓慢积债——旱是"长期低雨"的累积，不是几天的晴
        public const float DroughtAccumulateK    = 0.05f;  // 每 tick 亏欠累积系数
        public const float DroughtDecayThreshold = 0.3f;   // 过线 → 草黄/decay↑（矩阵阈值 1）
        public const float DroughtDecayMultiplier= 1.5f;

        public static float SeasonBaselineRain(Season season) => season switch
        {
            Season.Spring => 0.35f,
            Season.Summer => 0.55f,
            Season.Autumn => 0.30f,
            Season.Winter => 0.15f,
            _             => 0.30f
        };
    }
}
