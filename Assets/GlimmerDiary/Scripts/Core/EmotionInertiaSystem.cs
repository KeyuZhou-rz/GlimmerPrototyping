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

        // 延迟消化（2026-08-03）：注入的脉冲不再立即生效，先挂 pending，
        // 2-4 分钟后才释放为 live Impulse——"世界想了一会儿"，回响不是即时的。
        // E_env 慢积分与 History 保持即时（世界的性情先变、不可见），
        // 只有可见的天气回响迟到。pending 不参与 Relax 衰减——它是"还没说出口的话"。
        public EmotionVector PendingImpulse { get; private set; }          // null = 无待释放
        public DateTime       PendingReleaseRealTime { get; private set; } // 墙钟释放时刻

        // 消化时长：随机 120-240 秒，避免机械感（30 秒心跳粒度下抖动不可感知）
        private const float PendingDelayMinSeconds = 120f;
        private const float PendingDelayMaxSeconds = 240f;

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

        // 从存档恢复状态（Awake 时调用）；savedImpulse 为 null（旧存档）时按无脉冲处理。
        // pending 两参为 2026-08-03 延迟消化新增：恢复后不主动兑现，
        // 由 WorldManager 在 catch-up 前统一调 TryReleasePending（缺席到期的回响先落地再被 Relax 衰减）。
        public void Restore(EmotionVector savedEEnv, List<EnvEmotionSnapshot> savedHistory,
                            EmotionVector savedImpulse = null,
                            EmotionVector savedPendingImpulse = null,
                            string savedPendingReleaseRealTime = null)
        {
            CurrentEEnv = savedEEnv ?? EmotionVector.Neutral();
            History     = savedHistory ?? new List<EnvEmotionSnapshot>();
            Impulse     = savedImpulse ?? ZeroVector();
            PendingImpulse = savedPendingImpulse;
            // 时刻解析失败按"已到点"处理——下次心跳即释放，不让 pending 永远挂死
            PendingReleaseRealTime =
                DateTime.TryParse(savedPendingReleaseRealTime, null,
                    System.Globalization.DateTimeStyles.RoundtripKind, out var t)
                    ? t : DateTime.Now;
        }

        // alpha由C维度动态决定：C越高响应越快
        public void Update(EmotionVector eCurrent)
        {
            // 快通道改延迟消化：脉冲先挂 pending，几分钟后才成为可见回响。
            // 防呆：上一篇还没消化完又写新的——先把旧的兑现（不无声吞掉），再排队新的。
            if (PendingImpulse != null) ForceReleasePending();
            PendingImpulse = CloneVector(eCurrent);
            PendingReleaseRealTime = DateTime.Now.AddSeconds(
                UnityEngine.Random.Range(PendingDelayMinSeconds, PendingDelayMaxSeconds));

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

        // 到点释放 pending 为 live Impulse（由 WorldManager 的 30 秒心跳与启动 catch-up 前各泵一次）。
        // 返回是否发生了释放——释放后当拍即应重译天气。
        public bool TryReleasePending()
        {
            if (PendingImpulse == null || DateTime.Now < PendingReleaseRealTime) return false;
            ForceReleasePending();
            return true;
        }

        // 无条件兑现 pending（测试与"新日记覆盖旧 pending"防呆用）
        public void ForceReleasePending()
        {
            if (PendingImpulse == null) return;
            Impulse = PendingImpulse;
            PendingImpulse = null;
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
