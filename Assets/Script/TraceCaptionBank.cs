using System.Collections.Generic;

/// <summary>
/// 痕迹点击语料库（Layer 3 纯文本，不读世界状态）。
/// 语气继承 BehaviorNarrator 的小报体：克制、拟公文腔、永远"未作任何说明"，
/// 只描述眼前所见，暗示而不点破，永不暴露数值。
/// 点击是"此刻的观察"而非某日世界志——不带日期，不进 pendingChronicles。
/// 选句按痕迹键稳定随机：同一条痕迹每次点都说同一段话（它是那条痕迹的"说法"）。
/// </summary>
public static class TraceCaptionBank
{
    private static readonly Dictionary<string, string[]> Bank = new()
    {
        ["mound"] = new[]
        {
            "这堆土是新翻的。{who}方面对此未作任何说明。",
            "土还是湿的。施工时间不明，施工方没有留名。",
            "此处刚经历过一次未经申报的挖掘。",
        },
        ["collapse"] = new[]
        {
            "洞塌了。没有申报，没有解释，只有一圈新土。",
            "这里发生过一次未经批准的拆除。现场保持原样。",
        },
        ["vtrail"] = new[]
        {
            // 田鼠镇小径（V1 D3）——占位各 1 条，设计者并行线扩到 ≥3
            "土堆之间踩出了一条路。天天走，走着走着就成了路。",
            "这条路没有名字。使用它的那几位从不登记。",
        },
        ["trail"] = new[]
        {
            "一串脚印从这里经过。脚步不急，目的地不明。",
            "有东西路过。数量、姓名、来意，均未透露。",
        },
        ["feathers"] = new[]
        {
            "几根羽毛。提前离境的那位，对此未作任何说明。",
            "羽毛落在了这里。飞走的那位没有回来认领。",
        },
        ["marks"] = new[]
        {
            "狐狸就边界事宜进行了例行巡察。这是会议纪要。",
            "此处已被标记。标记的含义，只有标记者清楚。",
        },
        ["rest"] = new[]
        {
            "草倒向两边。昨夜有两位在此歇脚，彼此都没有声张。",
            "这片草被压出两个位置。使用者天没亮就离开了。",
        },
        ["rangehalt"] = new[]
        {
            "脚印到这里停住了。再往前的路，鹿鼠不打算评论。",
            "足迹在石头边缘消失。这是一次安静的撤退。",
        },
        ["cracks"] = new[]
        {
            "地裂了。干旱方面对此事未作任何说明。",
            "裂缝还张着。水什么时候回来，没有日程表。",
        },
        ["sprout"] = new[]
        {
            "新苗冒出来了。风把种子送到这里，就没有了下文。",
            "蒲公英在此落户。租期未定。",
        },
    };

    private const string Fallback = "这里发生过一点事情。详情不明。";

    /// <summary>按痕迹键稳定选句；类型未知或模板缺失时回退通用句。
    /// voleWho：田鼠称谓档（"田鼠/它们/镇子"，V1 D3 称谓漂移）——模板里的 {who} 由它替换；
    /// 语料库本身仍不读世界状态，称谓由调用方（TraceCaptionUI）注入。</summary>
    public static string Pick(string traceType, string traceKey, string voleWho = "田鼠")
    {
        if (!string.IsNullOrEmpty(traceType) && Bank.TryGetValue(traceType, out var lines) && lines.Length > 0)
        {
            int idx = StableIndex(traceKey, lines.Length);
            return lines[idx].Replace("{who}", voleWho ?? "田鼠");
        }
        return Fallback;
    }

    // 稳定哈希（FNV-1a 思路，同 WorldTraceBinder：绝不用 string.GetHashCode）
    private static int StableIndex(string key, int modulo)
    {
        if (string.IsNullOrEmpty(key)) return 0;
        unchecked
        {
            uint hash = 2166136261;
            foreach (char ch in key)
            {
                hash = (hash ^ (byte)(ch & 0xFF)) * 16777619;
                hash = (hash ^ (byte)(ch >> 8)) * 16777619;
            }
            return (int)(hash % (uint)modulo);
        }
    }
}
