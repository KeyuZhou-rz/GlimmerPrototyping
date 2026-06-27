using System;
using System.IO;
using System.Collections.Generic;
using GlimmerDiary.Data;
using UnityEngine;

namespace GlimmerDiary.Utils
{
    public static class SaveSystem
    {
        static string SaveDir =>
            Path.Combine(Application.persistentDataPath, "GlimmerDiary");

        static string WorldStatePath =>
            Path.Combine(SaveDir, "world_state.json");

        static string JournalLogPath =>
            Path.Combine(SaveDir, "journal_log.json");

        // 覆盖写：整个世界存档根节点
        public static void SaveWorldState(WorldSaveData data)
        {
            EnsureDir();
            // 以「存档时刻」为墙钟锚点（崩溃安全，优于仅 OnApplicationQuit）
            data.lastTickRealTime = DateTime.Now.ToString("o");
            File.WriteAllText(WorldStatePath, JsonUtility.ToJson(data, prettyPrint: true));
            Debug.Log($"[SaveSystem] World state saved → {WorldStatePath}");
        }

        public static WorldSaveData LoadWorldState()
        {
            if (!File.Exists(WorldStatePath)) return null;
            var data = JsonUtility.FromJson<WorldSaveData>(
                           File.ReadAllText(WorldStatePath));
            Debug.Log($"[SaveSystem] World state loaded (gameTime: {data.gameTime?.ToKeyString()})");
            return data;
        }

        // 只追加，永不删除（CLAUDE.md：append-only log）
        public static void AppendJournalEntry(JournalEntry entry)
        {
            EnsureDir();
            JournalLog log = LoadJournalLog() ?? new JournalLog();
            log.entries.Add(entry);
            File.WriteAllText(JournalLogPath, JsonUtility.ToJson(log, prettyPrint: true));
        }

        public static JournalLog LoadJournalLog()
        {
            if (!File.Exists(JournalLogPath)) return null;
            return JsonUtility.FromJson<JournalLog>(
                       File.ReadAllText(JournalLogPath));
        }

        public static string GetSaveDir() => SaveDir;

        static void EnsureDir()
        {
            if (!Directory.Exists(SaveDir))
                Directory.CreateDirectory(SaveDir);
        }
    }
}
