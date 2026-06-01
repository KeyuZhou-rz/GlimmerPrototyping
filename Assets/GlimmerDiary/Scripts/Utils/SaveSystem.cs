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

        // 覆盖写：E_env 当前值 + 历史快照
        public static void SaveWorldState(EmotionVector currentEEnv,
                                          List<EnvEmotionSnapshot> history)
        {
            EnsureDir();
            var data = new WorldSaveData
            {
                savedAt    = DateTime.Now.ToString("o"),
                currentEEnv = currentEEnv,
                envHistory  = history
            };
            File.WriteAllText(WorldStatePath, JsonUtility.ToJson(data, prettyPrint: true));
            Debug.Log($"[SaveSystem] World state saved → {WorldStatePath}");
        }

        public static WorldSaveData LoadWorldState()
        {
            if (!File.Exists(WorldStatePath)) return null;
            var data = JsonUtility.FromJson<WorldSaveData>(
                           File.ReadAllText(WorldStatePath));
            Debug.Log($"[SaveSystem] World state loaded (savedAt: {data.savedAt})");
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
