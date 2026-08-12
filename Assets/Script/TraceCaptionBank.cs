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
        // ── 新生地层（V1 D5/D6）：同一对象按玩家传记分两语域 ——
        // 记忆语气（见证过它的诞生：在线发生 ∨ 被已读信件点名）
        ["relic_mem"] = new[]
        {
            "它们住过这里。那时你刚来。",
            "这堆土你见过的——{layer}。如今沉下去半截了。",
        },
        // 考古语气（未见证：预演期、你错过的章节）
        ["relic_arch"] = new[]
        {
            "一处旧迹。{layer}的东西，比你早。",
            "这里沉过什么。比那棵死树老，比这条河年轻。",
        },
        // 出露（风暴/田鼠翻出地表）——同分两语域
        ["exposed_mem"] = new[]
        {
            "它被翻出来了。{layer}你看着它落下去的，如今又回来了。",
        },
        ["exposed_arch"] = new[]
        {
            "它被翻出来了。{layer}埋下去的——那时候还没有你。",
        },
        // ── 侧翼路通道（V1 D8）：方向句占位各 1~2 条，设计者并行线扩写 ──
        ["stranger"] = new[]
        {
            "这串脚印你不认识。从西边来的，一天比一天深。",
            "不是村里任何一位的脚型。从西边的草里来的，还在往里走。",
        },
        ["departure"] = new[]
        {
            "脚印向东，出了石头区就没有再回来。",
            "它往东边去了。最后几枚印子已经走到舞台边上。",
        },
        ["passerby"] = new[]
        {
            "一串脚印从西边的草里来，到东边的山里去。它没打算留下。",
            "连夜赶路的脚印，不停，不绕，不看风景。",
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
    /// 语料库本身仍不读世界状态，称谓由调用方（TraceCaptionUI）注入。
    /// relic/exposed 按 witnessed 分记忆/考古两语域（V1 D6 记忆双读）——
    /// 同一对象，读法由玩家的传记决定；layerPhrase 是断代句（{layer} 占位）。</summary>
    public static string Pick(string traceType, string traceKey, string voleWho = "田鼠",
                              bool witnessed = false, string layerPhrase = null)
    {
        string type = traceType;
        // 记忆双读：relic/exposed 按见证标记选语域
        if (type == "relic" || type == "exposed")
            type += witnessed ? "_mem" : "_arch";
        if (!string.IsNullOrEmpty(type) && Bank.TryGetValue(type, out var lines) && lines.Length > 0)
        {
            int idx = StableIndex(traceKey, lines.Length);
            return lines[idx]
                .Replace("{who}", voleWho ?? "田鼠")
                .Replace("{layer}", layerPhrase ?? "很久以前");
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
