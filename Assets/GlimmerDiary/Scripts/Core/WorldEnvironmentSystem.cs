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
        public void UpdateFromEEnv(EmotionVector eEnv, WorldSignals signals)
        {
            ConsumeSignals(signals);

            // 土壤湿度：天气的累积效应（缓慢靠近降雨强度）
            State.SoilMoisture = Mathf.Lerp(State.SoilMoisture, State.Rainfall, 0.15f);

            // 植被：湿度和衰败度的函数
            float targetVegetation = State.SoilMoisture * (1f - State.DecayLevel);
            State.VegetationDensity = Mathf.Lerp(State.VegetationDensity, targetVegetation, 0.05f);

            // 衰败度：V 长期偏负时缓慢累积，V 偏正时缓慢恢复
            float decayDelta = eEnv.V < -0.3f ? 0.01f : -0.005f;
            State.DecayLevel = Mathf.Clamp01(State.DecayLevel + decayDelta);

            // 动物活跃度：季节 × E_env 的 S 维度
            State.CreatureAbundance = Mathf.Lerp(State.CreatureAbundance,
                                                  eEnv.S * 0.5f + 0.3f, 0.1f);
        }
    }
}
