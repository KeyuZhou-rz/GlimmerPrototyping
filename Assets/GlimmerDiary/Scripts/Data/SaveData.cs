using System;
using System.Collections.Generic;

namespace GlimmerDiary.Data
{
    [Serializable]
    public class WorldSaveData
    {
        public string version = "1.0";
        public string savedAt;
        public EmotionVector currentEEnv;
        public List<EnvEmotionSnapshot> envHistory;
    }

    [Serializable]
    public class JournalLog
    {
        public List<JournalEntry> entries = new List<JournalEntry>();
    }
}
