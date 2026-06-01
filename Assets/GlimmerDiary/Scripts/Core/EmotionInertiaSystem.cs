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

        public EmotionInertiaSystem()
        {
            CurrentEEnv = EmotionVector.Neutral();
            History = new List<EnvEmotionSnapshot>();
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
