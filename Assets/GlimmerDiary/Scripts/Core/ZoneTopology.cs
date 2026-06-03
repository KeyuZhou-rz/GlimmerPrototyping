using System;
using System.Collections.Generic;

namespace GlimmerDiary.Core
{
    // 分区邻接图（静态世界拓扑，不进存档）
    //
    // [riverbank 河岸]──[lowland 低洼地]──[center 草原中央]──[highland_east 东侧高地]
    //                                          │
    //                                    [stone_area 石头区]
    public static class ZoneTopology
    {
        private static readonly Dictionary<string, string[]> Adj = new()
        {
            ["riverbank"]     = new[] { "lowland" },
            ["lowland"]       = new[] { "riverbank", "center" },
            ["center"]        = new[] { "lowland", "highland_east", "stone_area" },
            ["highland_east"] = new[] { "center" },
            ["stone_area"]    = new[] { "center" },
        };

        public static IReadOnlyList<string> Neighbors(string zone) =>
            zone != null && Adj.TryGetValue(zone, out var n) ? n : Array.Empty<string>();

        public static bool AreAdjacent(string a, string b) =>
            a != null && Adj.TryGetValue(a, out var n) && Array.IndexOf(n, b) >= 0;

        // 同区或相邻（狐狸临近判定等用）
        public static bool AreSameOrAdjacent(string a, string b) =>
            a != null && (a == b || AreAdjacent(a, b));

        public static bool IsZone(string zone) => zone != null && Adj.ContainsKey(zone);
    }
}
