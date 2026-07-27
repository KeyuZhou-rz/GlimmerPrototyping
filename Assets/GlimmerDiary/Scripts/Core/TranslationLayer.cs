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

        // 脉冲增益（2026-07-27 双时间尺度）：注入回响以多大强度叠进当日信号。
        // 0.85 ≈ 当天应答"强但不满格"——满格留给持续书写后 E_env 也到位的状态。
        public float impulseGain = 0.85f;

        public TranslationLayer()
        {
            InitializeCurves();
        }

        private void InitializeCurves()
        {
            // 信号 1 降水 Wetness：V -1(大雨) → 0(无雨) → 1(无雨)
            // 低段抬陡（2026-07-27）：V=-0.3 给 0.45——中度悲伤不再是"天上什么都没发生"
            _vToWetness = new AnimationCurve(
                new Keyframe(-1f, 1f),
                new Keyframe(-0.3f, 0.45f),
                new Keyframe(0f, 0f),
                new Keyframe(1f, 0f)
            );

            // 信号 2 躁动 Agitation：A 0(无风) → 1(强风)；中段抬高，满档 0.95（过 0.7 絮阈）
            _aToAgitation = new AnimationCurve(
                new Keyframe(0f, 0.05f),
                new Keyframe(0.5f, 0.4f),
                new Keyframe(1f, 0.95f)
            );

            // 信号 3 晦明 Dimness：C 0(浓雾) → 1(无雾)；低 C 端更沉
            _cToDimness = new AnimationCurve(
                new Keyframe(0f, 0.85f),
                new Keyframe(0.5f, 0.25f),
                new Keyframe(1f, 0f)
            );
        }

        // 产出无状态快信号 1/2/3/7（纯函数 of E_env + impulse + rhythm）
        // impulse（2026-07-27）：写日记当日的全强回响，按 impulseGain 叠在 E_env 上再过曲线——
        // 曲线形状不变，"当天即可见的天气应答"由脉冲提供，惯性仍住在 E_env 的慢积分里。
        // [SEAM] 有状态信号 4/5/6 后续迁入此层：
        //   - 信号 4 繁盛 Flourishing：V-integral f += α·((V+1)/2 − f)（同 TickTree baobab vitality 形状）
        //   - 信号 5 衰败 Decay：V<-0.3 → +0.01 else -0.005（可回 0，无棘轮底）
        //   - 信号 6 聚拢 Convergence：S·0.5+0.3，α=0.1
        public WorldSignals Translate(EmotionVector e, NaturalRhythmState r, EmotionVector impulse = null)
        {
            float v = e.V, a = e.A, c = e.C, t = e.T;
            if (impulse != null)
            {
                v = Mathf.Clamp(v + impulse.V * impulseGain, -1f, 1f);
                a = Mathf.Clamp01(a + impulse.A * impulseGain);
                c = Mathf.Clamp01(c + impulse.C * impulseGain);
                t = Mathf.Clamp01(t + impulse.T * impulseGain);
            }

            float wetness   = _vToWetness.Evaluate(v);
            float agitation = _aToAgitation.Evaluate(a);
            float dimness   = _cToDimness.Evaluate(c);

            // 信号 7 苍穹 Firmament：近直接 T-read；雨天无星、白天无星
            float firmament = t * (1f - wetness) * (1f - r.lightIntensity);

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
