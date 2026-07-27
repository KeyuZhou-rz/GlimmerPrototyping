using UnityEngine;
using GlimmerDiary.Core;
using GlimmerDiary.Data;

namespace GlimmerDiary.Utils
{
    public class ManualEmotionInjector : MonoBehaviour
    {
        [Header("手动情绪参数")]
        [Range(-1, 1)] public float valence = 0f;
        [Range(0, 1)] public float arousal = 0.3f;
        [Range(0, 1)] public float temporality = 1f;
        [Range(0, 1)] public float sociality = 0f;
        [Range(0, 1)] public float certainty = 0.5f;


        void Update()
        {
            var wm = WorldManager.Instance;
            if (wm == null) return;

            var currentVec = new EmotionVector{ V = valence, A = arousal, T = temporality, S = sociality, C = certainty};

            wm.InjectEmotion(new JournalEntry { emotion = currentVec});
            wm.Environment.ConsumeSignals(
                wm.Translation.Translate(wm.EmotionInertia.CurrentEEnv, wm.GetRhythmState(), wm.EmotionInertia.Impulse)

            );


        }

    }
}