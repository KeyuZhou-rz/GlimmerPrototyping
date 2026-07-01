using UnityEngine;
using GlimmerDiary.Data;

namespace GlimmerDiary.Core
{
    // 翻译层 —— 7 个情绪→世界信号的唯一产出者（Layer 2 / WorldSimulator）
    //
    // 职责：把 EmotionVector 翻译成驱动世界的"信号"。信号是无状态快函数（1/2/3/7）
    //   或有状态积分（4/5/6，后续迁入）。本步（Step 2）只落地无状态天气信号：
    //   - 信号 1 降水 Wetness   ← E_env.V（曲线，原 WorldEnvironmentSystem._vToRainfall 1:1 搬入）
    //   - 信号 2 躁动 Agitation ← E_env.A（曲线，原 _aToWindSpeed 1:1）
    //   - 信号 3 晦明 Dimness   ← E_env.C（曲线，原 _cToFogDensity 1:1）
    //   - 信号 7 苍穹 Firmament ← E_env.T（近直接 T-read，替换原 V 驱动的 StarVisibility）
    //
    // 边界：本类是 7 信号本身的唯一写者。WorldEnvironmentSystem 消费信号写 State.* 天气字段。
    //       有状态信号 4 繁盛 / 5 衰败 / 6 聚拢 暂留 WorldEnvironmentSystem / TickTree，后续格子迁入。
    public class TranslationLayer
    {
        // 无状态快信号曲线（信号 1/2/3）
        private AnimationCurve _vToWetness;     // 信号 1 降水：V → Wetness
        private AnimationCurve _aToAgitation;   // 信号 2 躁动：A → Agitation
        private AnimationCurve _cToDimness;     // 信号 3 晦明：C → Dimness

        public TranslationLayer()
        {
            InitializeCurves();
        }

        private void InitializeCurves()
        {
            // 信号 1 降水 Wetness：V -1(大雨) → 0(无雨) → 1(无雨)
            _vToWetness = new AnimationCurve(
                new Keyframe(-1f, 1f),
                new Keyframe(-0.3f, 0.3f),
                new Keyframe(0f, 0f),
                new Keyframe(1f, 0f)
            );

            // 信号 2 躁动 Agitation：A 0(无风) → 1(强风)
            _aToAgitation = new AnimationCurve(
                new Keyframe(0f, 0.05f),
                new Keyframe(0.5f, 0.3f),
                new Keyframe(1f, 0.9f)
            );

            // 信号 3 晦明 Dimness：C 0(浓雾) → 1(无雾)
            _cToDimness = new AnimationCurve(
                new Keyframe(0f, 0.8f),
                new Keyframe(0.5f, 0.2f),
                new Keyframe(1f, 0f)
            );
        }

        // 产出无状态快信号 1/2/3/7（纯函数 of E_env + rhythm）
        // [SEAM] 有状态信号 4/5/6 后续迁入此层：
        //   - 信号 4 繁盛 Flourishing：V-integral f += α·((V+1)/2 − f)（同 TickTree baobab vitality 形状）
        //   - 信号 5 衰败 Decay：V<-0.3 → +0.01 else -0.005（可回 0，无棘轮底）
        //   - 信号 6 聚拢 Convergence：S·0.5+0.3，α=0.1
        public WorldSignals Translate(EmotionVector e, NaturalRhythmState r)
        {
            float wetness   = _vToWetness.Evaluate(e.V);
            float agitation = _aToAgitation.Evaluate(e.A);
            float dimness   = _cToDimness.Evaluate(e.C);

            // 信号 7 苍穹 Firmament：近直接 T-read；雨天无星、白天无星
            float firmament = e.T * (1f - wetness) * (1f - r.lightIntensity);

            return new WorldSignals
            {
                Wetness   = wetness,
                Agitation = agitation,
                Dimness   = dimness,
                Firmament = firmament
            };
        }
    }

    // 翻译层产出的世界信号集（瞬态，不序列化）
    // 信号 4/5/6 字段待后续格子迁入时追加。
    public struct WorldSignals
    {
        public float Wetness;     // 信号 1 降水 0-1
        public float Agitation;   // 信号 2 躁动 0-1
        public float Dimness;     // 信号 3 晦明 0-1
        public float Firmament;   // 信号 7 苍穹 0-1
    }
}
