#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;
using GlimmerDiary.Utils;

namespace GlimmerDiary.Editor
{
    // V1 清单 D1 咽喉项：一键快进 N 天（十日计划全部长程开发与 §8 演示剧本都靠它）。
    //
    // 单向快进、仅编辑器 play 模式、不进任何发布构建（本文件在 Editor 程序集）。
    // 走 WorldTick(isCatchUp: false) 非缺席路径——逐日世界志保留：
    // 演示剧本要看雨季信/编年史信逐封抵达，catch-up 分支会把整段聚合成缺席信。
    //
    // gameTime 快进到超前墙钟也无碍：下次启动 WallClockDeltaDays 已 clamp ≥0，
    // 世界只是"提前活到了那天"，日历不会倒流。
    public static class DebugFastForward
    {
        [MenuItem("GlimmerDiary/Debug/Fast Forward 1 Day")]
        private static void FF1() => FF(1);

        [MenuItem("GlimmerDiary/Debug/Fast Forward 7 Days")]
        private static void FF7() => FF(7);

        [MenuItem("GlimmerDiary/Debug/Fast Forward 30 Days")]
        private static void FF30() => FF(30);

        private static void FF(int days)
        {
            if (!Application.isPlaying)
            {
                Debug.LogWarning("[FastForward] 仅 play 模式可用（世界跑在 WorldManager 实例上）。");
                return;
            }
            var world = WorldManager.Instance;
            if (world == null)
            {
                Debug.LogWarning("[FastForward] WorldManager 未就绪。");
                return;
            }
            world.WorldTick(days, isCatchUp: false, writeAbsenceLetter: false);
            SaveSystem.SaveWorldState(world.WorldSave);   // 落盘锚定 now
            Debug.Log($"[FastForward] 快进 {days} 天 → {world.WorldSave.gameTime.ToDisplayString()} " +
                      $"（纪元: {world.WorldSave.eraState?.chapter ?? "n/a"}，待读信 {world.WorldSave.pendingChronicles.Count} 封）");
        }
    }
}
#endif
