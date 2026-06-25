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

        // 静息基线：无日记输入的自主日，E_env 朝此向量缓慢回落（默认中性零向量，可调）
        public EmotionVector Baseline { get; set; }
            = new EmotionVector { V = 0f, A = 0f, T = 0f, S = 0f, C = 0f };

        public EmotionInertiaSystem()
        {
            CurrentEEnv = EmotionVector.Neutral();
            History = new List<EnvEmotionSnapshot>();
        }

        // 从存档恢复状态（Awake 时调用）
        public void Restore(EmotionVector savedEEnv, List<EnvEmotionSnapshot> savedHistory)
        {
            CurrentEEnv = savedEEnv ?? EmotionVector.Neutral();
            History     = savedHistory ?? new List<EnvEmotionSnapshot>();
        }

        // alpha由C维度动态决定：C越高响应越快
        public void Update(EmotionVector eCurrent)
        {
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
    }
}
