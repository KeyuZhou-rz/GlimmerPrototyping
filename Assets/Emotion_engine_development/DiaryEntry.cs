using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
namespace GlimmerDiary.Diary
{
    [Serializable]
    public struct DiaryEntry
    {
        public string id;
        public long timestamp;
        public string rawText;
        public float valence;
        public float arousal;
        public bool isAnalyzed;
        
        public static DiaryEntry Create(string text)
        {
            return new DiaryEntry
            {
                id = Guid.NewGuid().ToString("N").Substring(0, 16),
                timestamp = DateTimeOffset.Now.ToUnixTimeSeconds(),
                rawText = text,
                valence = 0f,
                arousal = 0f,
                isAnalyzed = false
            };
        }
    }
    public interface IDataProvider
    {
        void AddEntry(DiaryEntry entry);
        DiaryEntry GetEntryById(string id);
        void UpdateEntry(DiaryEntry entry);
        void RemoveEntry(string id);
        int GetCount();
        List<DiaryEntry> GetAllEntries();
    }
    
    /// <summary>
    /// 全局数据对象，实现IDataProvider接口
    /// </summary>
    public class MyData : IDataProvider
    {
        private List<DiaryEntry> _entries = new List<DiaryEntry>();
        private Dictionary<string, int> _index = new Dictionary<string, int>();
        
        public void AddEntry(DiaryEntry entry)
        {
            _entries.Add(entry);
            _index[entry.id] = _entries.Count - 1;
            DataStorage.Save(_entries);
        }
        
        public DiaryEntry GetEntryById(string id)
        {
            if (_index.TryGetValue(id, out int i))
            {
                return _entries[i];
            }
            return default;
        }
        
        public void UpdateEntry(DiaryEntry entry)
        {
            if (_index.TryGetValue(entry.id, out int i))
            {
                _entries[i] = entry;
                DataStorage.Save(_entries);
            }
        }
        
        public void RemoveEntry(string id)
        {
            if (_index.TryGetValue(id, out int i))
            {
                _entries.RemoveAt(i);
                RebuildIndex();
                DataStorage.Save(_entries);
            }
        }
        
        public int GetCount() => _entries.Count;
        
        public List<DiaryEntry> GetAllEntries() => _entries;
        
        public void Load()
        {
            _entries = DataStorage.Load();
            RebuildIndex();
        }
        
        private void RebuildIndex()
        {
            _index.Clear();
            for (int i = 0; i < _entries.Count; i++)
            {
                _index[_entries[i].id] = i;
            }
        }
    }

    public static class DataStorage
    {
        private static string FilePath => Path.Combine(Application.persistentDataPath, "diary.json");
        public static void Save(List<DiaryEntry> entries)
        {
            var wrapper = new Wrapper { entries = entries };
            string json = JsonUtility.ToJson(wrapper, true);
            File.WriteAllText(FilePath, json);


        }

        public static List<DiaryEntry> Load()
        {
            if (!File.Exists(FilePath))
            {
                return new List<DiaryEntry>();
            }
            string json = File.ReadAllText(FilePath);
            var wrapper = JsonUtility.FromJson<Wrapper>(json);
            return wrapper?.entries ?? new List<DiaryEntry>();

        }

        [System.Serializable]
        private class Wrapper
        {
            public List<DiaryEntry> entries;
        }
    }
}