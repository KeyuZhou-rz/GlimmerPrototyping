using System.Collections.Generic;

namespace GlimmerDiary.Data
{
    // 痕迹记录键工具（L2/L3 共享，Data 层）。
    //
    // 存在理由：土堆痕迹的身份键由 WorldTraceBinder（L3）在派生时生成，
    // 而 VoleTownSystem（L2）成形小径时要引用"同一批土堆"——键的构造必须只有一份，
    // 否则两侧各自拼字符串，格式漂移后小径就指向不存在的土堆。
    //
    // 键口径（与 WorldTraceBinder 动物 history 循环一致）：
    //   baseKey = "{speciesId}|{date}|{from}->{to}|{triggeredBy}"
    //   同 baseKey 重复出现时追加 "#n"（n 从 1 起）——occurrence 计数按 baseKey 独立，
    //   只数土堆记录与数全部 location 记录结果相同（baseKey 含 trigger，互不干扰）。
    public static class TraceKeyUtil
    {
        /// <summary>地表同时保留的土堆上限（"只留最新 N"）——binder 裁剪与地层入土共用此数，勿各写一份。</summary>
        public const int MoundKeepCount = 6;

        /// <summary>FNV-1a 32 位。绝不用 string.GetHashCode()——它逐进程随机化，痕迹会每次启动乱跳。</summary>
        public static int Fnv1a(string s)
        {
            unchecked
            {
                uint hash = 2166136261;
                foreach (char ch in s)
                {
                    hash = (hash ^ (byte)(ch & 0xFF)) * 16777619;
                    hash = (hash ^ (byte)(ch >> 8))   * 16777619;
                }
                return (int)hash;
            }
        }

        /// <summary>一条土堆源记录：fullKey 含 "mound|" 前缀（= binder 的 desired 键）。</summary>
        public struct MoundRecord
        {
            public string fullKey;    // "mound|vole|Y1-M9-D3|lowland->center|vole_expansion[#n]"
            public string zone;       // 扩张按 "center"；搬家按目的地（同 binder SpawnMound 口径）
            public int    birthDay;   // ToAbsoluteDays
            public string trigger;    // vole_expansion / vole_relocate_flood（binder 重解坐标用）
            public string toValue;    // 搬家目的地（同上）
            public bool   witnessed;  // 记忆双读：源记录诞生时玩家是否在场（入土继承）
        }

        /// <summary>
        /// 枚举 vole history 里的全部土堆源记录（不限活跃窗口——活跃过滤由调用方做）。
        /// 与 WorldTraceBinder T1 分支同一数据源、同一键格式、同一 zone 口径。
        /// </summary>
        public static void EnumVoleMoundRecords(WorldSaveData save, List<MoundRecord> outList)
        {
            outList.Clear();
            if (save?.animals == null) return;
            foreach (var a in save.animals)
            {
                if (a.speciesId != "vole" || a.history == null) continue;
                var occur = new Dictionary<string, int>();
                foreach (var rec in a.history)
                {
                    if (rec.field != "location") continue;
                    if (rec.triggeredBy != "vole_expansion" && rec.triggeredBy != "vole_relocate_flood") continue;
                    string baseKey = $"{a.speciesId}|{rec.date}|{rec.fromValue}->{rec.toValue}|{rec.triggeredBy}";
                    occur.TryGetValue(baseKey, out int n);
                    occur[baseKey] = n + 1;
                    string key = n == 0 ? baseKey : $"{baseKey}#{n}";
                    outList.Add(new MoundRecord
                    {
                        fullKey   = "mound|" + key,
                        zone      = rec.triggeredBy == "vole_expansion" ? "center"
                                  : (string.IsNullOrEmpty(rec.toValue) ? "lowland" : rec.toValue),
                        birthDay  = GameDateTime.ParseKey(rec.date).ToAbsoluteDays(),
                        trigger   = rec.triggeredBy,
                        toValue   = rec.toValue,
                        witnessed = rec.witnessed
                    });
                }
            }
        }

        /// <summary>
        /// 地表可见土堆集（最新 MoundKeepCount 个，按出生日降序）——唯一实现。
        /// binder 只渲染这批；不在批里的 = 已出窗，由 StratumSystem 入土（新生地层 D5）。
        /// </summary>
        public static void EnumVisibleMounds(WorldSaveData save, List<MoundRecord> outList)
        {
            EnumVoleMoundRecords(save, outList);
            outList.Sort((a, b) => b.birthDay.CompareTo(a.birthDay));
            if (outList.Count > MoundKeepCount)
                outList.RemoveRange(MoundKeepCount, outList.Count - MoundKeepCount);
        }
        /// <summary>
        /// 活跃土堆数（窗口内 + lowland/center）——唯一实现。
        /// EraSystem（纪元钟定居/镇判据）与 VoleTownSystem（小径成形）都经这里，勿另抄窗口过滤。
        /// </summary>
        public static int CountActiveMounds(WorldSaveData save, GameDateTime now, int windowDays)
        {
            var all = new List<MoundRecord>();
            EnumVoleMoundRecords(save, all);
            int today = now.ToAbsoluteDays(), n = 0;
            foreach (var m in all)
                if (today - m.birthDay <= windowDays && (m.zone == "lowland" || m.zone == "center"))
                    n++;
            return n;
        }
    }
}
