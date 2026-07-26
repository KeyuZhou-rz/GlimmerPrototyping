using System;
using UnityEngine;
using GlimmerDiary.Data;

namespace GlimmerDiary.Utils
{
    /// <summary>Developer-only controls for advancing the world one day at a time.</summary>
    public class ManualTicking : MonoBehaviour
    {
        [Header("手动注入情绪参数")]
        [SerializeField, Range(-1f, 1f)] private float valence;
        [SerializeField, Range(0f, 1f)] private float arousal = 0.3f;
        [SerializeField, Range(0f, 1f)] private float temporality = 1f;
        [SerializeField, Range(0f, 1f)] private float sociality;
        [SerializeField, Range(0f, 1f)] private float certainty = 0.5f;

        [ContextMenu("Tick One Day With Current Vector")]
        private void TickOneDayWithCurrentVector()
        {
            if (!TryGetWorld(out WorldManager world)) return;

            var input = new EmotionVector
            {
                V = valence,
                A = arousal,
                T = temporality,
                S = sociality,
                C = certainty
            };

            var entry = new JournalEntry
            {
                entryId = Guid.NewGuid().ToString(),
                realTimestamp = DateTime.Now.ToString("o"),
                rawText = "[manual gameplay lab]",
                emotion = input
            };

            try
            {
                world.InjectEmotion(entry);
                world.WorldTick(1, isCatchUp: false, writeAbsenceLetter: false);
                SaveSystem.SaveWorldState(world.WorldSave);
                SaveSystem.AppendJournalEntry(entry);
                Debug.Log($"[ManualTicking] Input tick complete: {world.WorldSave.gameTime.ToDisplayString()}");
            }
            catch (Exception exception)
            {
                Debug.LogError($"[ManualTicking] Tick failed: {exception.Message}");
            }
        }

        [ContextMenu("Tick One Day Without Input")]
        private void TickOneDayWithoutInput()
        {
            if (!TryGetWorld(out WorldManager world)) return;

            try
            {
                world.WorldTick(1, isCatchUp: false, writeAbsenceLetter: false);
                SaveSystem.SaveWorldState(world.WorldSave);
                Debug.Log($"[ManualTicking] Autonomous tick complete: {world.WorldSave.gameTime.ToDisplayString()}");
            }
            catch (Exception exception)
            {
                Debug.LogError($"[ManualTicking] Tick failed: {exception.Message}");
            }
        }

        private static bool TryGetWorld(out WorldManager world)
        {
            world = WorldManager.Instance;
            if (world != null) return true;

            Debug.LogWarning("[ManualTicking] WorldManager is not ready.");
            return false;
        }
    }
}
