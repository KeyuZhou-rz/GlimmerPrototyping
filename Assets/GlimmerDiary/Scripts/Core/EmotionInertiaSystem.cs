using System;
using System.Collections.Generic;
using GlimmerDiary.Data;
using UnityEngine;

namespace GlimmerDiary.Core
{
    public class EmotionInertiaSystem
    {
        public EmotionVector CurrentEEnv { get; private set; }
        public List<EnvEmotionSnapshot> History { get; private set; }

        // 情绪脉冲（2026-07-27 双时间尺度）：注入瞬间的全强回响，快衰减。
        // E_env 是世界的性情（慢变量），Impulse 是"世界听见了"的那一下（快变量）——
        // 翻译层信号 = 曲线(E_env + gain·Impulse)，写日记的当天天气就有可见应答；
        // 不持续书写则脉冲一两天内散尽，只有反复出现的情绪才驻留进 E_env。
        // 注意零点是真零向量（不是 Neutral()——Neutral 的 A=0.3/T=1 叠进信号会凭空起风亮星）。
        public EmotionVector Impulse { get; private set; }

        // 脉冲衰减（每自主日）：一天后余 45%，两天 20%，三天 ~9% —— 回响挂一两天即散
        private const float ImpulseRelaxAlpha = 0.55f;

        // 静息基线：无日记输入的自主日，E_env 朝此向量缓慢回落（默认中性零向量，可调）
        public EmotionVector Baseline { get; set; }
            = new EmotionVector { V = 0f, A = 0f, T = 0f, S = 0f, C = 0f };

        public EmotionInertiaSystem()
        {
            CurrentEEnv = EmotionVector.Neutral();
            Impulse     = ZeroVector();
            History     = new List<EnvEmotionSnapshot>();
        }

        // 从存档恢复状态（Awake 时调用）；savedImpulse 为 null（旧存档）时按无脉冲处理
        public void Restore(EmotionVector savedEEnv, List<EnvEmotionSnapshot> savedHistory,
                            EmotionVector savedImpulse = null)
        {
            CurrentEEnv = savedEEnv ?? EmotionVector.Neutral();
            History     = savedHistory ?? new List<EnvEmotionSnapshot>();
            Impulse     = savedImpulse ?? ZeroVector();
        }

        // alpha由C维度动态决定：C越高响应越快
        public void Update(EmotionVector eCurrent)
        {
            // 快通道：当日回响直接取注入向量全强（当天天气即可见应答）
            Impulse = CloneVector(eCurrent);

            // 慢通道：E_env 仍以惯性积分（世界的性情不因一篇日记转向）
            float alpha = Mathf.Lerp(0.1f, 0.3f, eCurrent.C);

            CurrentEEnv.V = alpha * eCurrent.V + (1 - alpha) * CurrentEEnv.V;
            CurrentEEnv.A = alpha * eCurrent.A + (1 - alpha) * CurrentEEnv.A;
            CurrentEEnv.T = alpha * eCurrent.T + (1 - alpha) * CurrentEEnv.T;
            CurrentEEnv.S = alpha * eCurrent.S + (1 - alpha) * CurrentEEnv.S;
            CurrentEEnv.C = alpha * eCurrent.C + (1 - alpha) * CurrentEEnv.C;

            History.Add(new EnvEmotionSnapshot
            {
                realDate = DateTime.Now.ToString("yyyy-MM-dd"),
                eEnv = CloneVector(CurrentEEnv)
            });
        }

        // 自主回落：每个自主日历日调用一次（非日记注入路径）
        // E_env 朝 Baseline 缓慢收敛，体现「情绪有惯性」——alpha 越小惯性越强
        // 不写入 History（历史快照保持以日记为锚，避免膨胀）
        public void Relax(float alpha = 0.05f)
        {
            CurrentEEnv.V = alpha * Baseline.V + (1 - alpha) * CurrentEEnv.V;
            CurrentEEnv.A = alpha * Baseline.A + (1 - alpha) * CurrentEEnv.A;
            CurrentEEnv.T = alpha * Baseline.T + (1 - alpha) * CurrentEEnv.T;
            CurrentEEnv.S = alpha * Baseline.S + (1 - alpha) * CurrentEEnv.S;
            CurrentEEnv.C = alpha * Baseline.C + (1 - alpha) * CurrentEEnv.C;

            // 脉冲快衰：回响挂一两天即散，与 E_env 的慢回落分离
            Impulse.V *= 1f - ImpulseRelaxAlpha;
            Impulse.A *= 1f - ImpulseRelaxAlpha;
            Impulse.T *= 1f - ImpulseRelaxAlpha;
            Impulse.S *= 1f - ImpulseRelaxAlpha;
            Impulse.C *= 1f - ImpulseRelaxAlpha;
        }

        // 供叙事规则层查询最近N天V维度均值
        public float GetAverageV(int days)
        {
            int count = Mathf.Min(days, History.Count);
            if (count == 0) return 0f;

            float sum = 0f;
            for (int i = History.Count - count; i < History.Count; i++)
                sum += History[i].eEnv.V;
            return sum / count;
        }

        private EmotionVector CloneVector(EmotionVector src)
        {
            return new EmotionVector
            { V = src.V, A = src.A, T = src.T, S = src.S, C = src.C };
        }

        // 脉冲零点：真零向量（不用 Neutral()，见 Impulse 注释）
        private static EmotionVector ZeroVector()
        {
            return new EmotionVector
            { V = 0f, A = 0f, T = 0f, S = 0f, C = 0f };
        }
    }
}
