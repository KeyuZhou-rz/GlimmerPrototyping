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

        // AnimationCurve 用于在 Unity Inspector 里可视化调整映射关系
        // 实际开发中建议暴露到 Inspector
        private AnimationCurve _vToRainfall;
        private AnimationCurve _vToStarVisibility;
        private AnimationCurve _aToWindSpeed;
        private AnimationCurve _cToFogDensity;

        public WorldEnvironmentSystem()
        {
            State = new WorldEnvironmentState
            {
                SoilMoisture = 0.5f,
                VegetationDensity = 0.5f,
                DecayLevel = 0f,
                CreatureAbundance = 0.5f
            };

            InitializeCurves();
        }

        private void InitializeCurves()
        {
            // V: -1(大雨) → 0(无雨) → 1(无雨)
            _vToRainfall = new AnimationCurve(
                new Keyframe(-1f, 1f),
                new Keyframe(-0.3f, 0.3f),
                new Keyframe(0f, 0f),
                new Keyframe(1f, 0f)
            );

            // V: -1(无星) → 0(少星) → 1(满天星)
            _vToStarVisibility = new AnimationCurve(
                new Keyframe(-1f, 0f),
                new Keyframe(0f, 0.4f),
                new Keyframe(1f, 1f)
            );

            // A: 0(无风) → 1(强风)
            _aToWindSpeed = new AnimationCurve(
                new Keyframe(0f, 0.05f),
                new Keyframe(0.5f, 0.3f),
                new Keyframe(1f, 0.9f)
            );

            // C: 0(浓雾) → 1(无雾)
            _cToFogDensity = new AnimationCurve(
                new Keyframe(0f, 0.8f),
                new Keyframe(0.5f, 0.2f),
                new Keyframe(1f, 0f)
            );
        }

        // 每次 E_env 更新后调用
        public void UpdateFromEEnv(EmotionVector eEnv, NaturalRhythmState rhythm)
        {
            // 天气：直接由 E_env 驱动
            State.Rainfall = _vToRainfall.Evaluate(eEnv.V);
            State.WindSpeed = _aToWindSpeed.Evaluate(eEnv.A);
            State.FogDensity = _cToFogDensity.Evaluate(eEnv.C);
            State.StarVisibility = _vToStarVisibility.Evaluate(eEnv.V)
                                   * (1f - State.Rainfall)            // 雨天无星
                                   * (1f - rhythm.lightIntensity);    // 白天无星

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
