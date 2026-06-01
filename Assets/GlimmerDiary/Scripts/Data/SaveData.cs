using System;
using System.Collections.Generic;

namespace GlimmerDiary.Data
{
    // WorldSaveData 已移至 WorldStateData.cs（结构更完整的版本）
    [Serializable]
    public class JournalLog
    {
        public List<JournalEntry> entries = new List<JournalEntry>();
    }
}
