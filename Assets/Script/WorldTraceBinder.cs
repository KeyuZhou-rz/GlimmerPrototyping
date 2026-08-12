using System.Collections.Generic;
using UnityEngine;
using GlimmerDiary.Data;

/// <summary>
/// Layer 3 痕迹绑定层：世界状态（只读） → 地面痕迹 prop + 草地压痕。
/// 动物永不被渲染——它们的存在只通过痕迹被感知（脚印/新翻的土/记号/羽毛/压倒的草）。
///
/// 数据源（全部只读，零新增 L2 状态、零 L3 持久化）：
///   worldEvents（append-only） + 各动物 history + 各 location permanentChanges + gameTime
/// 纪律同 WorldAtmosphereBinder：绝不写世界状态、绝不调用 Layer 2 模拟/注入入口、
/// 不导入 GlimmerDiary.Core（WorldManager 在全局命名空间，数据类型来自 GlimmerDiary.Data）。
///
/// 确定性：每条痕迹的身份与位置是源记录内容的纯函数——
/// FNV-1a(记录字段拼接) 做种子 → ZoneMap 确定性采样。重启后 Awake 全量扫描按
/// 当前 gameTime 重推年龄，痕迹位置与寿命完全复现（M4 验收项）。
/// 派生时机：监听 (事件数, 历史总数, 永久变化数, 当前游戏日) 签名，变化才重建——
/// 比增量游标更简单且天然覆盖重启场景；扫描量为几百条记录，代价可忽略。
/// </summary>
public class WorldTraceBinder : MonoBehaviour
{
    [Header("场景引用（Tools/Glimmer/Setup World Traces 自动接线）")]
    public ZoneMap zoneMap;

    [Header("痕迹材质（Setup 烘焙；运行时用 MPB 做年龄着色，不改共享材质）")]
    public Material dirtMaterial;
    public Material featherMaterial;
    public Material markMaterial;
    public Material pressedMaterial;

    [Header("年龄窗口（游戏日）")]
    public int trailMaxAge   = 8;    // T3 脚印串
    public int featherMaxAge = 6;    // T5 羽毛
    public int markMaxAge    = 14;   // T6 记号
    public int restMaxAge    = 5;    // T7 歇息压痕
    // T1 土堆"只留最新 N"的 N 已收敛到 TraceKeyUtil.MoundKeepCount（D5 起与地层入土共用，勿在此另设字段）

    [Header("天气擦除（链1+风 §5.2：effectiveAge = age × (1 + Rainfall×rF + WindSpeed×wF)）")]
    [Tooltip("雨洗因子：全雨时痕迹有效老化 +50%。不改最大寿命，改有效年龄——雨天痕迹老得更快")]
    [Range(0f, 2f)] public float rainTraceFactor = 0.5f;
    [Tooltip("风吹因子：全风时痕迹有效老化 +30%。与雨洗同属「天气擦除痕迹」的信号")]
    [Range(0f, 2f)] public float windTraceFactor = 0.3f;

    [Header("新鲜窗口（游戏日）：有效年龄 ≤ 阈值的变化进留意清单——日记边缘语料的选材")]
    public float freshAgeThreshold = 1.5f;   // 有效年龄 ≤ 此值视为"新鲜"
    [Tooltip("T7 歇息压痕的新鲜窗口单独放宽——压痕比脚印含蓄")]
    public float restFreshAgeThreshold = 3f;
    // 2026-08-13 设计变更（拍板）：悬浮小球标记撤销。新鲜判定口径保留，
    // 产物从"头顶的球"改为 GetFreshNotices() 留意清单 → 日记边缘一行小字。
    // 只给方向不给位置；错过即错过，信补后文。

    [Header("旱痕（§5.5：DroughtDebt>阈值 → 干裂地表，状态驱动仿 T4，回落即撤）")]
    public float droughtCrackThreshold = 0.6f;
    [Tooltip("裂缝簇出现的 zone（水位退缩最明显的区域）")]
    public string[] crackZones = { "lowland", "center" };
    public Color crackColor = new(0.16f, 0.12f, 0.09f);   // 裂缝阴影色，比任何土色都暗

    [Header("T1 水毁态（§5.1：土堆所在 zone 湿度>阈值 → 塌陷湿泥态，不可逆锁存——水毁是痕迹的状态，塌洞是地貌的疤）")]
    public float floodDamageMoisture = 0.8f;
    public Color floodDamagedTint = new(0.22f, 0.17f, 0.13f);   // 泡透的湿泥：更暗偏冷

    [Header("新绒苗（§5.3 蒲公英落种 → 下风区 N 日后冒苗；过龄即撤=长进草里，与痕迹寿命体系一致）")]
    public int sproutDays     = 4;    // 落种后第几天冒苗
    public int sproutLifespan = 15;   // 苗可见总天数（矩阵未定寿命——默认限期；永久苗属设计拍板）
    public Color sproutTint   = new(0.82f, 0.84f, 0.70f);   // 绒白偏青，与枯金草丛拉开

    [Header("年龄着色")]
    public Color dirtFresh   = new(0.30f, 0.22f, 0.16f);   // 湿的新土
    public Color dirtDry     = new(0.45f, 0.36f, 0.27f);
    public Color dirtSettled = new(0.52f, 0.46f, 0.38f);   // 沉降后接近地表色
    public Color printFresh  = new(0.35f, 0.28f, 0.20f);
    public Color printFaded  = new(0.55f, 0.50f, 0.40f);
    public Color featherTint = new(0.88f, 0.86f, 0.78f);
    public Color markFresh   = new(0.28f, 0.22f, 0.18f);
    public Color pressedTint = new(0.55f, 0.48f, 0.33f);   // 压伏草垫：比金色草海明显暗的秸秆棕（远机位可辨）

    // 与 GlimmerGrass.shader 的 _TramplePoints[16] 数组长度耦合——两侧同改
    private const int TRAMPLE_MAX = 16;
    private static readonly int TrampleCountId  = Shader.PropertyToID("_TrampleCount");
    private static readonly int TramplePointsId = Shader.PropertyToID("_TramplePoints");

    private enum TraceType { Mound, CollapsedBurrow, Trail, Feathers, ScentMarks, RestPatch, RangeHalt, EarthCrack, Sprout, VoleTrail, Relic, ExposedRelic, StrangerMarks, DepartureMarks, PasserbyChain }

    /// <summary>一条派生痕迹：键=源记录哈希（身份），场景表现挂在 root 下。</summary>
    private class TraceInstance
    {
        public TraceType type;
        public string key;            // 痕迹完整键（desired 字典键=源记录哈希），点击语料用
        public int seed;
        public GameObject root;
        public readonly List<Renderer> renderers = new();
        // 本痕迹贡献的压痕点（世界 XZ + 半径 + 基础强度，随龄再衰减）
        public readonly List<Vector4> trampleContribs = new();
        public float spawnRealTime;   // 展示层淡入用（真实秒）
    }

    // 留意清单（2026-08-13 设计变更：撤悬浮小球，改语料引导）——
    // 新鲜痕迹的"说法素材"：类型+键+所在区。只给方向不给位置；日记边缘取用。
    public struct NoticeInfo
    {
        public string traceType;   // TypeKey 语料类型串
        public string traceKey;    // 稳定选句用（同一条痕迹同一句话）
        public string zoneId;      // 最近锚点的区（地名式方向）；解析不到为 null
    }

    private readonly Dictionary<string, TraceInstance> _traces = new();
    private readonly List<NoticeInfo> _notices = new();   // 每次 Rebuild 随 fresh 集合重算
    private readonly HashSet<string> _floodDamaged = new();   // T1 水毁锁存（不可逆原则③：一旦泡透不再复原）
    private readonly Vector4[] _trampleArray = new Vector4[TRAMPLE_MAX];
    private readonly List<Vector4> _trampleGather = new();
    private Transform _propRoot;
    private MaterialPropertyBlock _mpb;
    private long _lastSignature = long.MinValue;

    void Awake()
    {
        _mpb = new MaterialPropertyBlock();
        var rootGo = new GameObject("Props");
        rootGo.transform.SetParent(transform, false);
        _propRoot = rootGo.transform;
    }

    void Update()
    {
        var wm = WorldManager.Instance;
        if (wm == null || wm.WorldSave == null || zoneMap == null) return;
        var save = wm.WorldSave;

        long sig = Signature(wm, save);
        if (sig == _lastSignature) return;
        _lastSignature = sig;

        Rebuild(wm, save);
    }

    void LateUpdate()
    {
        // 每帧推压痕数组：强度 = 随龄衰减(派生时算好) × 展示层淡入（新痕迹 2 秒内爬升，
        // 读作"今天有什么走过"而不是瞬间跳变）
        TraceInstance reservedRest = null;
        float reservedRestStrength = 0f;
        foreach (var t in _traces.Values)
        {
            if (t.type != TraceType.RestPatch || t.trampleContribs.Count < 2) continue;
            float fadeIn = Mathf.Clamp01((Time.time - t.spawnRealTime) / 2f);
            float pairStrength = Mathf.Min(t.trampleContribs[0].w, t.trampleContribs[1].w) * fadeIn;
            if (pairStrength > reservedRestStrength)
            {
                reservedRest = t;
                reservedRestStrength = pairStrength;
            }
        }

        _trampleGather.Clear();
        foreach (var t in _traces.Values)
        {
            if (t.trampleContribs.Count == 0) continue;
            float fadeIn = Mathf.Clamp01((Time.time - t.spawnRealTime) / 2f);
            for (int i = t == reservedRest ? 2 : 0; i < t.trampleContribs.Count; i++)
            {
                var c = t.trampleContribs[i];
                _trampleGather.Add(new Vector4(c.x, c.y, c.z, c.w * fadeIn));
            }
        }

        int reservedCount = reservedRest != null ? 2 : 0;
        int remainingCount = TRAMPLE_MAX - reservedCount;
        // T7 的一对有效歇息点先保留；剩余预算仍按原有强度规则竞争。
        if (_trampleGather.Count > remainingCount)
            _trampleGather.Sort((a, b) => b.w.CompareTo(a.w));

        int n = 0;
        if (reservedRest != null)
        {
            float fadeIn = Mathf.Clamp01((Time.time - reservedRest.spawnRealTime) / 2f);
            for (int i = 0; i < reservedCount; i++)
            {
                var c = reservedRest.trampleContribs[i];
                _trampleArray[n++] = new Vector4(c.x, c.y, c.z, c.w * fadeIn);
            }
        }
        int otherCount = Mathf.Min(_trampleGather.Count, remainingCount);
        for (int i = 0; i < otherCount; i++) _trampleArray[n++] = _trampleGather[i];
        Shader.SetGlobalFloat(TrampleCountId, n);
        Shader.SetGlobalVectorArray(TramplePointsId, _trampleArray);
    }

    void OnDisable()
    {
        Shader.SetGlobalFloat(TrampleCountId, 0f);
    }

    // ── 派生：数据 → 期望痕迹集 → 与场景同步 ─────────────────────

    private long Signature(WorldManager wm, WorldSaveData save)
    {
        long sig = save.worldEvents?.Count ?? 0;
        if (save.animals != null)
            foreach (var a in save.animals) sig = sig * 31 + (a.history?.Count ?? 0);
        if (save.locations != null)
            foreach (var l in save.locations) sig = sig * 31 + (l.permanentChanges?.Count ?? 0);
        sig = sig * 31 + ToDays(save.gameTime);
        // 小径（V1 D3）：成形/lapsed 各触发一次重建（淡出进度由上面的 gameDay 项逐日驱动）
        sig = sig * 31 + (save.voleTrails?.Count ?? 0);
        if (save.voleTrails != null)
            foreach (var tr in save.voleTrails) sig = sig * 31 + (tr.lapsed ? 1 : 0);
        // 地层（V1 D5）：入土/出露各触发一次重建（沉降进度由 gameDay 项逐日驱动）
        sig = sig * 31 + (save.strata?.Count ?? 0);
        if (save.strata != null)
            foreach (var s in save.strata) sig = sig * 31 + (s.exposed ? 1 : 0);
        // 侧翼路通道（V1 D8）：新痕迹成形触发重建（蔓延/横穿/淡出由 gameDay 项逐日驱动）
        sig = sig * 31 + (save.wings?.traces?.Count ?? 0);
        // 鹿鼠活动范围收缩态（实时字段，无历史记录）——跨过阈值也要触发重建
        var dm = wm.Registry?.GetAnimal("deer_mouse");
        sig = sig * 31 + (dm != null && dm.activityRange < 0.5f ? 1 : 0);
        // 天气混入签名（链1+风）：雨/风变化即重算有效年龄；量化避免逐帧抖动
        var env = wm.GetWorldState();
        if (env != null)
        {
            sig = sig * 31 + Mathf.RoundToInt(env.Rainfall * 20f);
            sig = sig * 31 + Mathf.RoundToInt(env.WindSpeed * 20f);
            // 旱债（地裂出现/撤除）与 T1 水毁判定都要随它重建
            sig = sig * 31 + Mathf.RoundToInt(env.DroughtDebt * 20f);
        }
        // 各 zone 湿度量化档：跨过水毁阈值要触发重建（T1 水毁态）
        if (save.locations != null)
            foreach (var l in save.locations) sig = sig * 31 + Mathf.RoundToInt(l.soilMoisture * 20f);
        return sig;
    }

    private void Rebuild(WorldManager wm, WorldSaveData save)
    {
        int today = ToDays(save.gameTime);
        // 链1+风（§5.2 规格原文）：effectiveAge = age × (1 + Rainfall×rainFactor + WindSpeed×windFactor)。
        // Rainfall/WindSpeed 取当前值近似；不改最大寿命，改有效年龄——雨天风天痕迹老得更快。
        var env = wm.GetWorldState();
        float weatherMul = env != null
            ? 1f + env.Rainfall * rainTraceFactor + env.WindSpeed * windTraceFactor
            : 1f;
        _notices.Clear();   // 留意清单随本次重建重算
        var desired = new Dictionary<string, System.Action<TraceInstance>>();
        var fresh = new HashSet<string>();   // 有效年龄 ≤ freshAgeThreshold 的痕迹键（留意句选材）
        var nonClickable = new HashSet<string>();   // 沉入地层档的痕迹：存在但不可点（几乎不可读，直到出露）

        // —— T1/T3/T6：从动物 history 派生；T2 塌洞已由地层接管（D5，见下方 strata 分支）——
        // T1 只从 history 派生（VoleClaimedZone 事件与 vole_expansion 记录同 tick 双发，
        // 单一来源即天然去重）

        if (save.animals != null)
        {
            foreach (var animal in save.animals)
            {
                if (animal.history == null) continue;
                var occur = new Dictionary<string, int>();   // 同键计数（防御性；正常一天一动）
                foreach (var rec in animal.history)
                {
                    if (rec.field != "location") continue;
                    int day = ToDays(ParseKeyDate(rec.date));
                    float age = (today - day) * weatherMul;   // 有效年龄（链1+风）：雨洗风吹老得更快
                    string baseKey = $"{animal.speciesId}|{rec.date}|{rec.fromValue}->{rec.toValue}|{rec.triggeredBy}";
                    occur.TryGetValue(baseKey, out int n);
                    occur[baseKey] = n + 1;
                    string key = n == 0 ? baseKey : $"{baseKey}#{n}";
                    int seed = Fnv1a(key);
                    string from = rec.fromValue, to = rec.toValue, trigger = rec.triggeredBy;

                    // （T1 土堆改走 TraceKeyUtil.EnumVisibleMounds 单一来源，见下方）

                    // T6 狐狸巡逻记号（领地边界，语料："石头区和中央之间…留了几个记号"）
                    if (trigger == "fox_patrol" && age <= markMaxAge)
                    {
                        string sk = "marks|" + key;
                        desired[sk] = t => SpawnMarks(t, from, to, age, seed);
                        if (age <= freshAgeThreshold) fresh.Add(sk);
                    }

                    // T3 脚印串：一切位置迁移都留一串脚印
                    if (age <= trailMaxAge && !string.IsNullOrEmpty(from) && !string.IsNullOrEmpty(to) && from != to)
                    {
                        string tk = "trail|" + key;
                        desired[tk] = t => SpawnTrail(t, from, to, age, seed);
                        if (age <= freshAgeThreshold) fresh.Add(tk);
                    }
                }
            }
        }

        // T1 新翻土堆：地表只留最新 MoundKeepCount 个（EnumVisibleMounds 唯一来源）；
        // 出窗的不是删除，是入土——由下方 strata 分支接续渲染（V1 D5：沉降取代消失）。
        // 水毁态判定（§5.1 第四老化态）口径不变：按土堆所在 zone 的历史湿度极值锁存。
        var visibleMounds = new List<TraceKeyUtil.MoundRecord>();
        TraceKeyUtil.EnumVisibleMounds(save, visibleMounds);
        foreach (var m in visibleMounds)
        {
            float mage = (today - m.birthDay) * weatherMul;
            bool damaged = IsFloodDamaged(save, m.zone, m.fullKey, m.birthDay);
            var mm = m;   // struct 闭包捕获
            // 种子口径与 TryMoundPosition 一致：Fnv1a(去掉 "mound|" 前缀的键)——别换，换则全图土堆挪位
            desired[m.fullKey] = t => SpawnMound(t, mm.trigger, mm.toValue, mage,
                Fnv1a(mm.fullKey.Substring("mound|".Length)), damaged);
            if (mage <= freshAgeThreshold) fresh.Add(m.fullKey);
        }

        // T2 塌陷旧洞（permanentChanges，记录永不删——不可逆原则）。
        // D5 起塌洞出生即入土（StratumSystem 方案 A）：已入土的由下方 strata 分支接管渲染，
        // 这里只兜底尚未入土的（本拍刚塌、StratumSystem 还没跑到的边际拍）。
        if (save.locations != null)
        {
            foreach (var loc in save.locations)
            {
                if (loc.permanentChanges == null) continue;
                foreach (var pc in loc.permanentChanges)
                {
                    if (pc.changeType != "burrow_collapse") continue;
                    string key = $"collapse|{loc.locationId}|{pc.date}|{pc.changeType}";
                    if (FindStratum(save, key) != null) continue;   // 已入土 → 地层分支接管
                    int seed = Fnv1a(key);
                    string zone = loc.locationId;
                    int cday = ToDays(ParseKeyDate(pc.date));
                    desired[key] = t => SpawnCollapsedBurrow(t, zone, seed);
                    // 新出现的塌洞也留意几天——永久地貌的"诞生"同样是值得注意的变化
                    if ((today - cday) * weatherMul <= freshAgeThreshold) fresh.Add(key);
                }
            }
        }

        // —— T5/T7：从 worldEvents 派生 ——
        if (save.worldEvents != null)
        {
            var occur = new Dictionary<string, int>();
            foreach (var e in save.worldEvents)
            {
                string baseKey = $"{e.type}|{e.gameDate}|{e.targetId}|{e.payload}";
                occur.TryGetValue(baseKey, out int n);
                occur[baseKey] = n + 1;
                string key = n == 0 ? baseKey : $"{baseKey}#{n}";
                int seed = Fnv1a(key);
                int day = ToDays(ParseKeyDate(e.gameDate));
                float age = (today - day) * weatherMul;   // 有效年龄（链1+风）

                // T5 羽毛：候鸟离境（payload="reason:fromZone"）/ 织巢鸟离巢（家园=center）
                if (age <= featherMaxAge &&
                    (e.type == WorldEventType.AnimalDeparted || e.type == WorldEventType.WeaverBirdDeparted))
                {
                    string zone = "center";
                    if (e.type == WorldEventType.AnimalDeparted)
                    {
                        int colon = string.IsNullOrEmpty(e.payload) ? -1 : e.payload.IndexOf(':');
                        zone = colon >= 0 && colon < e.payload.Length - 1
                             ? e.payload.Substring(colon + 1) : "riverbank";
                    }
                    string fk = "feathers|" + key;
                    desired[fk] = t => SpawnFeathers(t, zone, age, seed);
                    if (age <= freshAgeThreshold) fresh.Add(fk);
                }

                // T7 歇息压痕：涌现时刻（targetId=zone）——主表达是草被压弯
                if (age < restMaxAge && e.type == WorldEventType.QuietConvergence)
                {
                    string zone = e.targetId;
                    string rk = "rest|" + key;
                    desired[rk] = t => SpawnRestPatch(t, zone, age, seed);
                    if (age <= restFreshAgeThreshold) fresh.Add(rk);
                }

                // 新绒苗：蒲公英落种（§5.3，targetId=下风 zone）——落种 sproutDays 日后冒苗，
                // sproutLifespan 日后"长进草里"撤出。用日历真实年龄（不经 weatherMul：
                // 雨水擦痕迹但不催芽，发芽是日历事）。
                if (e.type == WorldEventType.DandelionSeedsDrifted && !string.IsNullOrEmpty(e.targetId))
                {
                    int rawAge = today - day;
                    if (rawAge >= sproutDays && rawAge <= sproutLifespan)
                    {
                        string sk = "sprout|" + key;
                        desired[sk] = t => SpawnSprout(t, e.targetId, seed);
                        // 冒苗头几天进留意清单——"低洼冒了新苗"正是留意句该说的变化
                        if (rawAge <= sproutDays + freshAgeThreshold) fresh.Add(sk);
                    }
                }
            }
        }

        // T4 鹿鼠退守（实时态，无历史记录可循）：activityRange<0.5 期间，
        // 高地→石头区方向出现一小串在边缘停住的脚印（"脚印在石头边缘停住了"）。
        // 固定种子 → 固定那条路；可见性是实时字段的纯函数，重启自然复现。
        var deerMouse = wm.Registry?.GetAnimal("deer_mouse");
        if (deerMouse != null && deerMouse.isPresent && deerMouse.activityRange < 0.5f)
        {
            const string key = "rangehalt|deer_mouse";
            desired[key] = t => SpawnRangeHalt(t, Fnv1a(key));
            fresh.Add(key);   // 实时态恒新鲜——它出现本身就是"刚发生的变化"
        }

        // 旱痕（§5.5 阈值 2）：debt>0.6 → 干裂地表。状态驱动仿 T4：过线出现，回落即撤。
        // 不进留意清单——裂缝会持续数周，留意句只说"新变化"。
        if ((env?.DroughtDebt ?? 0f) > droughtCrackThreshold && crackZones != null)
        {
            foreach (var zone in crackZones)
            {
                if (string.IsNullOrEmpty(zone)) continue;
                string ck = "cracks|" + zone;
                desired[ck] = t => SpawnCracks(t, zone, Fnv1a(ck));
            }
        }

        // 小径（V1 D3 田鼠镇）：voleTrails 记录 → 土堆之间的压草色片链。
        // 活跃期恒在（镇是"还活着的惯例"）；lapsed 后按 VoleTrailRecord.FadeDays 淡回地表色后撤。
        if (save.voleTrails != null)
        {
            foreach (var tr in save.voleTrails)
            {
                int formDay = ToDays(ParseKeyDate(tr.formedDateKey));
                int lapseAge = tr.lapsed ? today - ToDays(ParseKeyDate(tr.lapseDateKey)) : 0;
                if (tr.lapsed && lapseAge > VoleTrailRecord.FadeDays) continue;   // 淡完即撤
                string vk = $"vtrail|{tr.formedDateKey}";
                var rec = tr;   // 闭包捕获
                desired[vk] = t => SpawnVoleTrail(t, rec, lapseAge);
                // 刚成形那天留意一下（镇诞生是新闻）；之后是常态不再提
                if (!tr.lapsed && (today - formDay) * weatherMul <= freshAgeThreshold) fresh.Add(vk);
            }
        }

        // 新生地层（V1 D5/D6）：入土痕迹的三态渲染——
        //   遗存（depth < RelicMaxDepth）：塌矮、色沉、微陷，可点（旧迹语气）；
        //   地层（更深且未出露）：只剩一点土色异样，不可点——几乎不可读，直到出露；
        //   出露（风暴/田鼠翻出）：半埋挺回地表，可点（记忆/考古双语域），出露当日进留意清单。
        // 记录永不删；每区地层档只画最新 MaxPerZone 件（预算阀），更老的在档继续沉。
        if (save.strata != null && save.strata.Count > 0)
        {
            var zoneBudget = new Dictionary<string, int>();
            var sorted = new List<StratumRecord>(save.strata);
            sorted.Sort((a, b) => string.Compare(b.buriedDateKey, a.buriedDateKey, System.StringComparison.Ordinal));
            foreach (var s in sorted)
            {
                bool deep = !s.exposed && s.depth >= StratumRecord.RelicMaxDepth;
                if (deep)
                {
                    zoneBudget.TryGetValue(s.zone, out int zn);
                    if (zn >= StratumRecord.MaxPerZone) continue;   // 超限不画，档还在
                    zoneBudget[s.zone] = zn + 1;
                }
                var rec = s;   // 闭包捕获
                desired[rec.sourceKey] = t => SpawnStratum(t, rec);
                if (!deep) nonClickable.Remove(rec.sourceKey); else nonClickable.Add(rec.sourceKey);
                if (rec.exposed)
                {
                    // 出露当日指一下（旧物重见天日是新闻）
                    if (today - ToDays(ParseKeyDate(rec.exposedDateKey)) <= freshAgeThreshold)
                        fresh.Add(rec.sourceKey);
                }
                else if (rec.kind == "collapse" &&
                         (today - ToDays(ParseKeyDate(rec.buriedDateKey))) * weatherMul <= freshAgeThreshold)
                    fresh.Add(rec.sourceKey);   // 塌洞诞生的留意行为从 T2 原样保留
            }
        }

        // 侧翼路通道（V1 D8）：陌生脚印数日蔓延入五区 / 离去标记数周淡出 /
        // 过路客链 2~3 日横穿。记录与行为口径在 Data 层（WingTraceRecord 静态量），
        // 这里只画可见的——侧翼永不可抵达，这些印子是"那边"唯一漏进来的东西。
        if (save.wings?.traces != null)
        {
            foreach (var wt in save.wings.traces)
            {
                if (!WingTraceRecord.IsVisible(wt, today)) continue;
                var rec = wt;   // 闭包捕获
                int age = today - ToDays(ParseKeyDate(rec.formedDateKey));
                switch (rec.kind)
                {
                    case "stranger":  desired[rec.id] = t => SpawnStrangerMarks(t, rec, today);  break;
                    case "departure": desired[rec.id] = t => SpawnDepartureMarks(t, rec, age);   break;
                    case "passerby":  desired[rec.id] = t => SpawnPasserbyChain(t, rec, today);  break;
                }
                if (age <= freshAgeThreshold) fresh.Add(rec.id);   // 出现/上路当日留意一下
            }
        }

        // —— 同步：移除消失的，生成新增的 ——
        var stale = new List<string>();
        foreach (var kv in _traces)
            if (!desired.ContainsKey(kv.Key)) stale.Add(kv.Key);
        foreach (var k in stale)
        {
            if (_traces[k].root != null) Destroy(_traces[k].root);
            _traces.Remove(k);
        }

        foreach (var kv in desired)
        {
            if (_traces.ContainsKey(kv.Key))
            {
                // 已存在：销毁重建以应用新年龄的着色/缩放（每游戏日至多一次，代价可忽略）
                var old = _traces[kv.Key];
                float keepSpawnTime = old.spawnRealTime;
                if (old.root != null) Destroy(old.root);
                var again = new TraceInstance { spawnRealTime = keepSpawnTime, key = kv.Key };
                kv.Value(again);
                _traces[kv.Key] = again;
                FinishTrace(again, fresh.Contains(kv.Key), !nonClickable.Contains(kv.Key));
            }
            else
            {
                var t = new TraceInstance { spawnRealTime = Time.time, key = kv.Key };
                kv.Value(t);
                _traces[kv.Key] = t;
                FinishTrace(t, fresh.Contains(kv.Key), !nonClickable.Contains(kv.Key));
            }
        }
    }

    // ── 各类型痕迹的生成 ─────────────────────────────────────────

    /// <summary>
    /// T1 新翻土堆：三阶段老化（湿→干→沉降）+ 第四态「水毁」（§5.1，2026-07-21 定稿）。
    /// 扩张土在石头区与中央之间（语料："石头区和中央之间…新翻的土"）；
    /// 洪水搬家的新洞口开在目的地 zone（规则可能指向 highland_east 等，跟数据走）。
    /// 水毁：泡透塌成湿泥——更矮、摊开（边缘糊）、色暗偏冷；锁存不可逆。
    /// </summary>
    private void SpawnMound(TraceInstance t, string trigger, string toZone, float age, int seed, bool damaged)
    {
        t.type = TraceType.Mound; t.seed = seed;
        bool ok = trigger == "vole_expansion"
            ? zoneMap.TrySampleEdge("center", "stone_area", seed, out Vector3 p)
            : zoneMap.TrySampleZone(string.IsNullOrEmpty(toZone) ? "lowland" : toZone, seed, out p);
        if (!ok) return;

        Color c = age <= 3 ? dirtFresh
                : age <= 12 ? Color.Lerp(dirtFresh, dirtDry, (age - 3) / 9f)
                : dirtSettled;
        float flatten = age > 12 ? 0.6f : 1f;   // 沉降后变矮
        var scale = new Vector3(1f, flatten, 1f);
        if (damaged)
        {
            c     = floodDamagedTint;
            scale = new Vector3(1.15f, 0.42f, 1.15f);   // 塌陷摊开：比沉降态更矮、边缘糊
        }

        t.root = NewRoot($"Mound_{seed:X8}", p);
        var rng = new System.Random(seed);
        AddProp(t, TraceKit.Mound, dirtMaterial, c, p + Vector3.up * 0.01f,
                Quaternion.Euler(0f, (float)rng.NextDouble() * 360f, 0f),
                scale);
        // 新土周围草被扒开：小半径弱压痕，随龄衰减（水毁后湿泥与草已交融，无压痕）
        if (age <= 12 && !damaged)
            t.trampleContribs.Add(new Vector4(p.x, p.z, 0.7f, 0.55f * (1f - age / 12f)));
    }

    // 水毁判定（T1 第四老化态）：① zone 当前湿度越线 → 锁存（活体路径）；
    // ② 该 zone 在土堆出生后发生过洪水搬家（vole_relocate_flood from=zone）→
    //   搬家的原因就是把这片泡透了——土堆被泡过（跨 session 确定性，覆盖 catch-up 后湿度已回落的情形）。
    // 与 T2 塌洞分工：水毁是痕迹的状态，塌洞是地貌的疤。
    private bool IsFloodDamaged(WorldSaveData save, string zone, string moundKey, int moundBirthDay)
    {
        if (_floodDamaged.Contains(moundKey)) return true;

        if (save.locations != null)
            foreach (var loc in save.locations)
                // 路径①：判"曾经湿到过"（极值）而非"现在还湿"——湿度在两次游玩间隙回落
                // 不再让泡透的土堆复原（C1，07-28 拍板；极值由 Propagate/规则路径维护）
                if (loc.locationId == zone
                    && (loc.soilMoisture > floodDamageMoisture || loc.soilMoisturePeak > floodDamageMoisture))
                {
                    _floodDamaged.Add(moundKey);
                    return true;
                }

        if (save.animals != null)
            foreach (var a in save.animals)
            {
                if (a.speciesId != "vole" || a.history == null) continue;
                foreach (var rec in a.history)
                    if (rec.field == "location" && rec.triggeredBy == "vole_relocate_flood"
                        && rec.fromValue == zone
                        && ToDays(ParseKeyDate(rec.date)) >= moundBirthDay)
                    {
                        _floodDamaged.Add(moundKey);
                        return true;
                    }
            }
        return false;
    }

    /// <summary>T2 塌陷旧洞：出生即沉降态，永不移除。</summary>
    private void SpawnCollapsedBurrow(TraceInstance t, string zone, int seed)
    {
        t.type = TraceType.CollapsedBurrow; t.seed = seed;
        if (!zoneMap.TrySampleZone(zone, seed, out Vector3 p)) return;

        t.root = NewRoot($"Collapse_{seed:X8}", p);
        var rng = new System.Random(seed);
        AddProp(t, TraceKit.MoundCollapsed, dirtMaterial, dirtSettled * 0.9f,
                p + Vector3.up * 0.01f,
                Quaternion.Euler(0f, (float)rng.NextDouble() * 360f, 0f), Vector3.one);
    }

    /// <summary>
    /// 新生地层（V1 D5）：入土痕迹的三态渲染，全部复用现有 prefab 的 scale/tint/下沉，零新美术。
    ///   遗存：随深度塌矮、色沉向地表、微微下陷——"这是去年的东西了"；
    ///   地层：只剩一片比地表略沉的色片（FinishTrace 不挂点击——几乎不可读，直到出露）；
    ///   出露：半埋挺回地表，色略新（刚翻上来的土），可点。
    /// 位置连续性：mound 原地重解（原土堆坐标）；collapse/vtrail 用 源键种子+zone 采样——
    /// 与出生视觉同一个点，玩家看到的是"同一个东西沉下去了"，不是别处冒出来的新东西。
    /// </summary>
    private void SpawnStratum(TraceInstance t, StratumRecord rec)
    {
        int seed = Fnv1a(rec.sourceKey);
        t.seed = seed;
        bool deep = !rec.exposed && rec.depth >= StratumRecord.RelicMaxDepth;
        t.type = rec.exposed ? TraceType.ExposedRelic : TraceType.Relic;

        Vector3 p;
        if (rec.kind == "mound" && TryMoundPosition(rec.sourceKey, out p)) { /* 原地沉降 */ }
        else if (!zoneMap.TrySampleZone(rec.zone, seed, out p)) return;

        float relicT = Mathf.Clamp01(rec.depth / StratumRecord.RelicMaxDepth);
        var rng = new System.Random(seed);
        var yaw = Quaternion.Euler(0f, (float)rng.NextDouble() * 360f, 0f);
        t.root = NewRoot($"Stratum_{seed:X8}", p);

        if (deep)
        {
            // 地层档：一点土色异样（比地表略沉），不挂压痕
            AddProp(t, TraceKit.PressedOval, dirtMaterial, dirtSettled * 0.92f,
                    p + Vector3.up * 0.008f, yaw, new Vector3(0.9f, 1f, 0.9f));
            return;
        }

        if (rec.exposed)
        {
            // 出露档：半埋挺起——比遗存高、色略新
            Color c = Color.Lerp(dirtSettled, dirtDry, 0.35f);
            switch (rec.kind)
            {
                case "mound":
                    AddProp(t, TraceKit.Mound, dirtMaterial, c, p + Vector3.up * 0.01f, yaw,
                            new Vector3(1f, 0.55f, 1f));
                    break;
                case "collapse":
                    AddProp(t, TraceKit.MoundCollapsed, dirtMaterial, c * 0.95f, p + Vector3.up * 0.01f, yaw,
                            Vector3.one * 0.85f);
                    break;
                default:   // vtrail：重新露出的一小段踩实路面
                    for (int i = 0; i < 3; i++)
                    {
                        var off = new Vector3((float)(rng.NextDouble() - 0.5) * 2.4f, 0f,
                                              (float)(rng.NextDouble() - 0.5) * 2.4f);
                        AddProp(t, TraceKit.PressedOval, pressedMaterial, c,
                                p + off + Vector3.up * 0.01f, yaw, new Vector3(0.9f, 1f, 0.9f));
                    }
                    break;
            }
            return;
        }

        // 遗存档：随深度塌矮/色沉/下陷
        Color rc = Color.Lerp(dirtSettled, dirtSettled * 0.85f, relicT);
        switch (rec.kind)
        {
            case "mound":
                AddProp(t, TraceKit.Mound, dirtMaterial, rc,
                        p + Vector3.up * (0.01f - 0.03f * relicT), yaw,
                        new Vector3(1f, Mathf.Lerp(0.6f, 0.3f, relicT), 1f));
                break;
            case "collapse":
                AddProp(t, TraceKit.MoundCollapsed, dirtMaterial, rc * 0.95f,
                        p + Vector3.up * (0.01f - 0.025f * relicT), yaw,
                        Vector3.one * Mathf.Lerp(0.95f, 0.7f, relicT));
                break;
            default:   // vtrail：淡回草里的一小段路
                Color vc = Color.Lerp(pressedTint * 0.92f, dirtSettled, 0.4f + 0.5f * relicT);
                for (int i = 0; i < 3; i++)
                {
                    var off = new Vector3((float)(rng.NextDouble() - 0.5) * 2.4f, 0f,
                                          (float)(rng.NextDouble() - 0.5) * 2.4f);
                    AddProp(t, TraceKit.PressedOval, pressedMaterial, vc,
                            p + off + Vector3.up * (0.01f - 0.01f * relicT), yaw,
                            new Vector3(Mathf.Lerp(0.9f, 0.6f, relicT), 1f, Mathf.Lerp(0.9f, 0.6f, relicT)));
                }
                break;
        }
    }

    // 地层记录查询（T2 兜底分支判"是否已入土"用；binder 不导入 Core，直接读存档）
    private static StratumRecord FindStratum(WorldSaveData save, string sourceKey)
    {
        if (save?.strata == null) return null;
        foreach (var s in save.strata)
            if (s.sourceKey == sourceKey) return s;
        return null;
    }

    // ── 侧翼路通道（V1 D8）─────────────────────────────────────
    // 西→东五区链：三类侧翼痕迹共用这条空间骨架（"从西边的草里来，到东边的山里去"）
    private static readonly string[] WingPath = { "riverbank", "lowland", "center", "stone_area", "highland_east" };

    /// <summary>陌生脚印：西缘起每天更深一段（段数口径在 WingTraceRecord.StrangerSegments），
    /// 12 日淡完。每段两枚相邻脚印，朝向下一段——"它们每天都更深一点"。</summary>
    private void SpawnStrangerMarks(TraceInstance t, WingTraceRecord rec, int today)
    {
        t.type = TraceType.StrangerMarks; t.seed = Fnv1a(rec.id);
        int segments = WingTraceRecord.StrangerSegments(rec, today);
        int age = today - ToDays(ParseKeyDate(rec.formedDateKey));
        float life = 1f - age / (float)WingTraceRecord.StrangerFadeDays;
        Color c = Color.Lerp(printFaded, printFresh, Mathf.Clamp01(life));

        t.root = null;
        for (int i = 0; i < segments && i < WingPath.Length; i++)
        {
            if (!zoneMap.TrySampleZone(WingPath[i], t.seed + i * 977, out Vector3 p)) continue;
            if (t.root == null) t.root = NewRoot($"Stranger_{t.seed:X8}", p);
            Vector3 dir = WingPathDirection(i);
            Quaternion yaw = Quaternion.LookRotation(dir, Vector3.up);
            Vector3 perp = Vector3.Cross(dir, Vector3.up);
            for (int f = 0; f < 2; f++)
            {
                Vector3 fp = p + dir * (f * 0.35f) + perp * (f == 0 ? 0.09f : -0.09f);
                if (zoneMap.TryGroundAt(fp.x, fp.z, out Vector3 g)) fp = g;
                AddProp(t, TraceKit.Footprint, dirtMaterial, c, fp + Vector3.up * 0.02f, yaw,
                        Vector3.one * 0.95f);
            }
        }
    }

    /// <summary>离去标记：你的动物自东缘离去——从 highland_east 区缘向东（舞台之外）
    /// 一串远行的脚印，21 日淡完。东方位角不假设坐标轴：由 riverbank→highland_east 锚点连线推。</summary>
    private void SpawnDepartureMarks(TraceInstance t, WingTraceRecord rec, int age)
    {
        t.type = TraceType.DepartureMarks; t.seed = Fnv1a(rec.id);
        if (!zoneMap.TryGetAnchorCenter("highland_east", out Vector3 c0, out float radius)) return;
        Vector3 east = WingEastDirection();
        float life = 1f - age / (float)WingTraceRecord.DepartureFadeDays;
        Color c = Color.Lerp(printFaded, printFresh, Mathf.Clamp01(life));

        t.root = NewRoot($"Depart_{t.seed:X8}", c0);
        Quaternion yaw = Quaternion.LookRotation(east, Vector3.up);
        Vector3 perp = Vector3.Cross(east, Vector3.up);
        for (int i = 0; i < 4; i++)
        {
            Vector3 p = c0 + east * (radius * 0.6f + i * 0.7f) + perp * (i % 2 == 0 ? 0.09f : -0.09f);
            if (!zoneMap.TryGroundAt(p.x, p.z, out Vector3 g)) break;   // 走出地形就不画了
            AddProp(t, TraceKit.Footprint, dirtMaterial, c, g + Vector3.up * 0.02f, yaw,
                    Vector3.one * 0.95f);
        }
    }

    /// <summary>过路客链：五个锚点连成一条横穿线，按 WingTraceRecord.PasserbyProgress01
    /// 逐日截断——第 0 天只在西缘一两枚，最后一天抵东缘，次日整条撤（不停留，印子始终新鲜）。</summary>
    private void SpawnPasserbyChain(TraceInstance t, WingTraceRecord rec, int today)
    {
        t.type = TraceType.PasserbyChain; t.seed = Fnv1a(rec.id);
        float progress = WingTraceRecord.PasserbyProgress01(rec, today);

        var anchors = new List<Vector3>();
        foreach (var z in WingPath)
            if (zoneMap.TryGetAnchorCenter(z, out Vector3 ac, out _)) anchors.Add(ac);
        if (anchors.Count < 2) return;

        const int N = 9;
        int visible = Mathf.Max(2, Mathf.CeilToInt(progress * (N - 1)) + 1);
        var rng = new System.Random(t.seed);
        t.root = NewRoot($"Passer_{t.seed:X8}", anchors[0]);
        for (int i = 0; i < visible; i++)
        {
            float segF = (float)i / (N - 1) * (anchors.Count - 1);
            int seg = Mathf.Min((int)segF, anchors.Count - 2);
            Vector3 dir = anchors[seg + 1] - anchors[seg]; dir.y = 0f;
            dir = dir.sqrMagnitude > 1e-4f ? dir.normalized : Vector3.forward;
            Vector3 perp = Vector3.Cross(dir, Vector3.up);
            Vector3 p = Vector3.Lerp(anchors[seg], anchors[seg + 1], segF - seg)
                      + perp * ((float)(rng.NextDouble() - 0.5) * 0.5f)   // 不是阅兵直线
                      + dir * (i % 2 == 0 ? 0.09f : -0.09f);
            if (!zoneMap.TryGroundAt(p.x, p.z, out Vector3 g)) continue;
            AddProp(t, TraceKit.Footprint, dirtMaterial, printFresh, g + Vector3.up * 0.02f,
                    Quaternion.LookRotation(dir, Vector3.up), Vector3.one * 0.95f);
        }
    }

    // 第 seg 段的前进方向（锚点 seg → seg+1；末段沿用前一段方向）
    private Vector3 WingPathDirection(int seg)
    {
        int next = Mathf.Min(seg + 1, WingPath.Length - 1);
        if (zoneMap.TryGetAnchorCenter(WingPath[seg], out Vector3 a, out _)
            && zoneMap.TryGetAnchorCenter(WingPath[next], out Vector3 b, out _))
        {
            var d = b - a; d.y = 0f;
            if (d.sqrMagnitude > 1e-4f) return d.normalized;
        }
        return Vector3.forward;
    }

    // 东 = riverbank→highland_east 锚点连线方向（数据驱动，不假设世界坐标轴）
    private Vector3 WingEastDirection()
    {
        if (zoneMap.TryGetAnchorCenter("riverbank", out Vector3 w, out _)
            && zoneMap.TryGetAnchorCenter("highland_east", out Vector3 e, out _))
        {
            var d = e - w; d.y = 0f;
            if (d.sqrMagnitude > 1e-4f) return d.normalized;
        }
        return Vector3.right;
    }

    /// <summary>T3 脚印串：沿 from→to 边 4-6 片小椭圆，左右交替，随龄缩小褪色。</summary>
    private void SpawnTrail(TraceInstance t, string from, string to, float age, int seed)
    {
        t.type = TraceType.Trail; t.seed = seed;
        var rng = new System.Random(seed);
        int count = 4 + rng.Next(3);
        var pts = new List<Vector3>();
        if (zoneMap.SampleTrail(from, to, seed, count, pts, out Vector3 dir) == 0) return;

        float life = 1f - age / (float)trailMaxAge;
        Color c = Color.Lerp(printFaded, printFresh, life);
        float scale = Mathf.Lerp(0.6f, 1f, life);
        Quaternion yaw = Quaternion.LookRotation(dir, Vector3.up);
        Vector3 perp = Vector3.Cross(dir, Vector3.up);
        float trampleLife = age <= 3 ? 1f - age / 3f : 0f;

        t.root = NewRoot($"Trail_{seed:X8}", pts[0]);
        for (int i = 0; i < pts.Count; i++)
        {
            Vector3 p = pts[i] + perp * ((i % 2 == 0) ? 0.09f : -0.09f);   // 左右脚交替
            AddProp(t, TraceKit.Footprint, dirtMaterial, c, p + Vector3.up * 0.02f, yaw,
                    Vector3.one * scale);
            if (trampleLife > 0f)
                t.trampleContribs.Add(new Vector4(p.x, p.z, 1.1f, 0.9f * trampleLife));
        }
    }

    /// <summary>T5 羽毛：离境 zone 内 2-3 片，随龄压平褪色。</summary>
    private void SpawnFeathers(TraceInstance t, string zone, float age, int seed)
    {
        t.type = TraceType.Feathers; t.seed = seed;
        if (!zoneMap.TrySampleZone(zone, seed, out Vector3 c0)) return;

        var rng = new System.Random(seed);
        int count = 3 + rng.Next(3);
        float life = 1f - age / (float)featherMaxAge;
        Color fresh = Color.Lerp(featherTint, Color.white, 0.35f);
        Color c = Color.Lerp(featherTint * 0.86f, fresh, life);

        t.root = NewRoot($"Feathers_{seed:X8}", c0);
        for (int i = 0; i < count; i++)
        {
            float ang = (float)rng.NextDouble() * Mathf.PI * 2f;
            float r   = 0.25f + (float)rng.NextDouble() * 0.85f;
            if (!zoneMap.TryGroundAt(c0.x + Mathf.Cos(ang) * r, c0.z + Mathf.Sin(ang) * r, out Vector3 p))
                continue;
            // 随龄压平：新落的羽毛翘一点，久了贴平
            float tilt = Mathf.Lerp(4f, 22f, life);
            float scale = Mathf.Lerp(1.1f, 1.35f, (float)rng.NextDouble());
            var rot = Quaternion.Euler(tilt, (float)rng.NextDouble() * 360f, 0f);
            AddProp(t, TraceKit.Feather, featherMaterial, c, p + Vector3.up * 0.025f, rot,
                    Vector3.one * scale);
        }
    }

    /// <summary>T6 狐狸记号：巡逻边上 1-3 个暗斑，慢淡出。</summary>
    private void SpawnMarks(TraceInstance t, string from, string to, float age, int seed)
    {
        t.type = TraceType.ScentMarks; t.seed = seed;
        var rng = new System.Random(seed);
        int count = 1 + rng.Next(3);
        float life = 1f - age / (float)markMaxAge;
        Color c = Color.Lerp(dirtSettled, markFresh, life);

        t.root = null;
        for (int i = 0; i < count; i++)
        {
            if (!zoneMap.TrySampleEdge(from, to, seed + i * 977, out Vector3 p)) continue;
            if (t.root == null) t.root = NewRoot($"Marks_{seed:X8}", p);
            AddProp(t, TraceKit.Mark, markMaterial, c, p + Vector3.up * 0.015f,
                    Quaternion.Euler(0f, (float)rng.NextDouble() * 360f, 0f),
                    Vector3.one * Mathf.Lerp(0.8f, 1.1f, (float)rng.NextDouble()));
        }
    }

    /// <summary>T7 歇息压痕：两片相挨的压草椭圆（"两个影子挨得近了些"）。
    /// 远机位（60-113m 掠射平视）下草形变本身不可读——主信号是贴地淡色椭圆片，
    /// trample 形变/枯黄只做走近后的辅助层。</summary>
    private void SpawnRestPatch(TraceInstance t, string zone, float age, int seed)
    {
        t.type = TraceType.RestPatch; t.seed = seed;
        if (!zoneMap.TrySampleZone(zone, seed, out Vector3 p0)) return;

        var rng = new System.Random(seed);
        float ang = (float)rng.NextDouble() * Mathf.PI * 2f;
        var offset = new Vector3(Mathf.Cos(ang), 0f, Mathf.Sin(ang)) * 2.5f;
        if (!zoneMap.TryGroundAt(p0.x + offset.x, p0.z + offset.z, out Vector3 p1))
            p1 = p0 + offset;

        float life = 1f - age / (float)restMaxAge;
        t.root = NewRoot($"Rest_{seed:X8}", p0);

        // 贴地色片：PressedOval 网格（rx0.85/rz0.60）放大到 ~4.4m 宽——
        // 舞台机位 93m 处约 50px，余光可辨"那里有一片不一样的颜色"。
        // 新鲜时是压伏草的淡秸秆色，随龄沉回地表色（与 trample 强度同寿命）。
        Color c = Color.Lerp(dirtSettled, pressedTint, Mathf.Clamp01(life));
        float ovalScale = 3.4f;
        float yaw0 = (float)rng.NextDouble() * 360f;
        AddProp(t, TraceKit.PressedOval, pressedMaterial, c, p0 + Vector3.up * 0.02f,
                Quaternion.Euler(0f, yaw0, 0f), Vector3.one * ovalScale);
        AddProp(t, TraceKit.PressedOval, pressedMaterial, c, p1 + Vector3.up * 0.02f,
                Quaternion.Euler(0f, yaw0 + 40f, 0f), Vector3.one * ovalScale);

        // 草内辅助层：半径 5.5m——远机位实测 r=2.5 在 60-113m 完全不可读，
        // r=8 又像空地；5.5 兼顾舞台存在感与近看尺度（07-28 舞台长焦标定）
        float s = 0.9f * Mathf.Sqrt(Mathf.Clamp01(life));
        t.trampleContribs.Add(new Vector4(p0.x, p0.z, 5.5f, s));
        t.trampleContribs.Add(new Vector4(p1.x, p1.z, 5.5f, s));
    }

    /// <summary>T4 鹿鼠退守（实时态）：高地→石头区一小串脚印，在边缘停住。</summary>
    private void SpawnRangeHalt(TraceInstance t, int seed)
    {
        t.type = TraceType.RangeHalt; t.seed = seed;
        var pts = new List<Vector3>();
        if (zoneMap.SampleTrail("highland_east", "stone_area", seed, 4, pts, out Vector3 dir) == 0) return;

        Quaternion yaw = Quaternion.LookRotation(dir, Vector3.up);
        Vector3 perp = Vector3.Cross(dir, Vector3.up);
        t.root = NewRoot($"RangeHalt_{seed:X8}", pts[0]);
        for (int i = 0; i < pts.Count; i++)
        {
            Vector3 p = pts[i] + perp * ((i % 2 == 0) ? 0.07f : -0.07f);
            // 鹿鼠脚印比田鼠/狐狸的更小
            AddProp(t, TraceKit.Footprint, dirtMaterial, printFresh, p + Vector3.up * 0.02f, yaw,
                    Vector3.one * 0.7f);
        }
    }

    /// <summary>
    /// 小径（V1 D3）：镇成形时快照的土堆键 → 逐键重解位置（SpawnMound 同款采样）→
    /// 相邻点之间铺 PressedOval 色片链。活跃期色恒定（每日重现不老化）；
    /// lapsed 后按 FadeDays 把秸秆棕淡回地表色——镇散了，路慢慢长回草里。
    /// 不走 _TramplePoints（16 点预算与 shader 耦合，小径只走色片层）。
    /// </summary>
    private void SpawnVoleTrail(TraceInstance t, VoleTrailRecord rec, int lapseAge)
    {
        t.type = TraceType.VoleTrail;
        var pts = new List<Vector3>();
        foreach (var mk in rec.moundKeys)
            if (TryMoundPosition(mk, out Vector3 p)) pts.Add(p);
        if (pts.Count < 2) return;

        // 活跃期：压伏草的秸秆棕（比 T7 略沉——路是常年踩的，不是歇一晚的）；
        // lapsed：按淡出进度沉回地表色
        Color live = pressedTint * 0.92f;
        Color c = rec.lapsed
            ? Color.Lerp(live, dirtSettled, Mathf.Clamp01(lapseAge / (float)VoleTrailRecord.FadeDays))
            : live;

        int seed = Fnv1a($"vtrail|{rec.formedDateKey}");
        t.seed = seed;
        var rng = new System.Random(seed);
        t.root = NewRoot($"VoleTrail_{seed:X8}", pts[0]);

        for (int i = 0; i < pts.Count - 1; i++)
        {
            Vector3 a = pts[i], b = pts[i + 1];
            float len = Vector3.Distance(a, b);
            int steps = Mathf.Max(1, Mathf.RoundToInt(len / 2.2f));   // ~2.2m 一片
            for (int s = 0; s <= steps; s++)
            {
                Vector3 p = Vector3.Lerp(a, b, s / (float)steps);
                // 沿路微 jitter（种子固定 → 每次重建位置复现）
                p.x += ((float)rng.NextDouble() - 0.5f) * 0.8f;
                p.z += ((float)rng.NextDouble() - 0.5f) * 0.8f;
                if (zoneMap.TryGroundAt(p.x, p.z, out Vector3 g)) p = g;
                AddProp(t, TraceKit.PressedOval, pressedMaterial, c, p + Vector3.up * 0.015f,
                        Quaternion.Euler(0f, (float)rng.NextDouble() * 360f, 0f),
                        Vector3.one * Mathf.Lerp(1.3f, 1.7f, (float)rng.NextDouble()));
            }
        }
    }

    // 小径土堆键 → 世界坐标：解析 "mound|vole|{date}|{from}->{to}|{trigger}[#n]"，
    // 用与 SpawnMound 完全相同的采样（种子=Fnv1a(去前缀键)），位置逐帧可复现。
    private bool TryMoundPosition(string moundKey, out Vector3 p)
    {
        p = default;
        if (string.IsNullOrEmpty(moundKey) || !moundKey.StartsWith("mound|")) return false;
        string key = moundKey.Substring("mound|".Length);
        int seed = Fnv1a(key);
        // 键尾段即 trigger（occurrence 后缀 "#n" 只影响种子，键段切分不受影响：
        // 含 #n 时 trigger 段形如 "vole_expansion#1"，比较时剥掉）
        int lastBar = key.LastIndexOf('|');
        if (lastBar < 0 || lastBar == key.Length - 1) return false;
        string trigger = key.Substring(lastBar + 1);
        int hash = trigger.IndexOf('#');
        if (hash >= 0) trigger = trigger.Substring(0, hash);

        if (trigger == "vole_expansion")
            return zoneMap.TrySampleEdge("center", "stone_area", seed, out p);

        // vole_relocate_flood：目的地 zone（from->to 段取 to）
        string to = "lowland";
        int arrow = key.IndexOf("->", System.StringComparison.Ordinal);
        if (arrow >= 0)
        {
            int end = key.IndexOf('|', arrow);
            string seg = end > arrow ? key.Substring(arrow + 2, end - arrow - 2)
                                     : key.Substring(arrow + 2);
            if (!string.IsNullOrEmpty(seg)) to = seg;
        }
        return zoneMap.TrySampleZone(to, seed, out p);
    }

    /// <summary>旱痕：debt 过线期间 zone 内 2-3 条地裂（状态驱动，回落即撤；同一网格 yaw/缩放打散）。</summary>
    private void SpawnCracks(TraceInstance t, string zone, int seed)
    {
        t.type = TraceType.EarthCrack; t.seed = seed;
        if (!zoneMap.TrySampleZone(zone, seed, out Vector3 c0)) return;

        var rng = new System.Random(seed);
        int count = 2 + rng.Next(2);
        t.root = NewRoot($"Cracks_{seed:X8}", c0);
        for (int i = 0; i < count; i++)
        {
            float ang = (float)rng.NextDouble() * Mathf.PI * 2f;
            float r   = (float)rng.NextDouble() * 2.2f;
            if (!zoneMap.TryGroundAt(c0.x + Mathf.Cos(ang) * r, c0.z + Mathf.Sin(ang) * r, out Vector3 p))
                continue;
            AddProp(t, TraceKit.EarthCrack, dirtMaterial, crackColor, p + Vector3.up * 0.012f,
                    Quaternion.Euler(0f, (float)rng.NextDouble() * 360f, 0f),
                    Vector3.one * Mathf.Lerp(0.9f, 1.6f, (float)rng.NextDouble()));
        }
    }

    /// <summary>新绒苗：落种 zone 内一小丛 2-3 棵（限期内存在，过龄即撤="长进草里"）。</summary>
    private void SpawnSprout(TraceInstance t, string zone, int seed)
    {
        t.type = TraceType.Sprout; t.seed = seed;
        if (!zoneMap.TrySampleZone(zone, seed, out Vector3 c0)) return;

        var rng = new System.Random(seed);
        int count = 2 + rng.Next(2);
        t.root = NewRoot($"Sprout_{seed:X8}", c0);
        for (int i = 0; i < count; i++)
        {
            float ang = (float)rng.NextDouble() * Mathf.PI * 2f;
            float r   = (float)rng.NextDouble() * 1.4f;
            if (!zoneMap.TryGroundAt(c0.x + Mathf.Cos(ang) * r, c0.z + Mathf.Sin(ang) * r, out Vector3 p))
                continue;
            AddProp(t, TraceKit.Sprout, featherMaterial, sproutTint, p + Vector3.up * 0.01f,
                    Quaternion.Euler(0f, (float)rng.NextDouble() * 360f, 0f),
                    Vector3.one * Mathf.Lerp(0.8f, 1.3f, (float)rng.NextDouble()));
        }
    }

    // ── prop 组装 ────────────────────────────────────────────────

    // 生成后收尾——挂可点击 collider（B 方案推近的命中体）；新鲜痕迹记入留意清单
    //（日记边缘语料的选材：类型+键+区，只给方向不给位置）。
    // clickable=false（沉入地层档）：不挂命中体不进清单——几乎不可读，直到出露。
    private void FinishTrace(TraceInstance t, bool isFresh, bool clickable = true)
    {
        if (t.root == null) return;
        if (!clickable) return;

        // 命中体：包住全部子 prop 的盒（加高加一点，扁平脚印也好点）
        var b = new Bounds(t.root.transform.position, Vector3.one * 0.5f);
        foreach (var r in t.renderers) b.Encapsulate(r.bounds);
        if (t.type == TraceType.RestPatch)
        {
            foreach (var c in t.trampleContribs)
            {
                var patchBounds = new Bounds(
                    new Vector3(c.x, t.root.transform.position.y, c.y),
                    new Vector3(c.z * 2f, 0.5f, c.z * 2f));
                b.Encapsulate(patchBounds);
            }
        }
        var col = t.root.AddComponent<BoxCollider>();
        col.center = t.root.transform.InverseTransformPoint(b.center);
        col.size = Vector3.Max(b.size, new Vector3(1.2f, 0.8f, 1.2f));
        var click = t.root.AddComponent<TraceClickable>();
        click.focusPoint = b.center;
        click.traceType = TypeKey(t.type);
        click.traceKey = t.key;

        if (isFresh)
            _notices.Add(new NoticeInfo
            {
                traceType = click.traceType,
                traceKey  = t.key,
                zoneId    = NearestZoneId(b.center)
            });
    }

    /// <summary>当前新鲜痕迹的留意清单（日记边缘语料用）。
    /// 只读派生——不写世界状态、不进编年史、不落档；关上日记即散，错过即错过。</summary>
    public void GetFreshNotices(List<NoticeInfo> into)
    {
        into.Clear();
        into.AddRange(_notices);
    }

    // 位置 → 最近锚点的 zoneId（地名式方向："石头那边"而非坐标）
    private string NearestZoneId(Vector3 at)
    {
        if (zoneMap == null || zoneMap.anchors == null) return null;
        string best = null;
        float bestD = float.MaxValue;
        foreach (var a in zoneMap.anchors)
        {
            if (a == null || string.IsNullOrEmpty(a.zoneId)) continue;
            if (!zoneMap.TryGetAnchorCenter(a.zoneId, out Vector3 c, out _)) continue;
            float d = (c - at).sqrMagnitude;
            if (d < bestD) { bestD = d; best = a.zoneId; }
        }
        return best;
    }

    // TraceType → 语料类型串（与 key 前缀同名，TraceCaptionBank 按它选模板）
    private static string TypeKey(TraceType type) => type switch
    {
        TraceType.Mound          => "mound",
        TraceType.CollapsedBurrow=> "collapse",
        TraceType.Trail          => "trail",
        TraceType.Feathers       => "feathers",
        TraceType.ScentMarks     => "marks",
        TraceType.RestPatch      => "rest",
        TraceType.RangeHalt      => "rangehalt",
        TraceType.EarthCrack     => "cracks",
        TraceType.Sprout         => "sprout",
        TraceType.VoleTrail      => "vtrail",
        TraceType.Relic          => "relic",
        TraceType.ExposedRelic   => "exposed",
        TraceType.StrangerMarks  => "stranger",
        TraceType.DepartureMarks => "departure",
        TraceType.PasserbyChain  => "passerby",
        _                        => "trace",
    };

    private GameObject NewRoot(string name, Vector3 at)
    {
        var go = new GameObject(name);
        go.transform.SetParent(_propRoot, false);
        go.transform.position = at;
        return go;
    }

    private void AddProp(TraceInstance t, Mesh mesh, Material mat, Color tint,
                         Vector3 pos, Quaternion rot, Vector3 scale)
    {
        if (t.root == null) return;
        var go = new GameObject(mesh.name);
        go.transform.SetParent(t.root.transform, false);
        go.transform.SetPositionAndRotation(pos, rot);
        go.transform.localScale = scale;

        go.AddComponent<MeshFilter>().sharedMesh = mesh;
        var mr = go.AddComponent<MeshRenderer>();
        mr.sharedMaterial = mat != null ? mat : FallbackMaterial();
        mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;   // 贴地小件不投影
        _mpb.Clear();
        _mpb.SetColor("_BaseColor", tint);
        mr.SetPropertyBlock(_mpb);
        t.renderers.Add(mr);
    }

    private static Material _fallback;
    private static Material FallbackMaterial()
    {
        if (_fallback == null)
        {
            var shader = Shader.Find("Glimmer/Toon");
            _fallback = new Material(shader != null ? shader : Shader.Find("Universal Render Pipeline/Lit"));
        }
        return _fallback;
    }

    // ── 确定性工具 ───────────────────────────────────────────────

    /// <summary>FNV-1a 32 位（实现收敛到 GlimmerDiary.Data.TraceKeyUtil，L2 小径成形与 L3 派生共用一颗哈希）。绝不用 string.GetHashCode()——它逐进程随机化，痕迹会每次启动乱跳。</summary>
    private static int Fnv1a(string s) => TraceKeyUtil.Fnv1a(s);

    // 日期解析/日序公式收敛到 GameDateTime（单一来源），此处只留空值容错
    private static GameDateTime ParseKeyDate(string key) => GameDateTime.ParseKey(key);

    private static int ToDays(GameDateTime d) => d == null ? 0 : d.ToAbsoluteDays();
}
