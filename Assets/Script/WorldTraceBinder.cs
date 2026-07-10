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
    public int moundKeepCount = 6;   // T1 沉降土堆只留最新 N 个（T2 永久塌洞不受限）

    [Header("年龄着色")]
    public Color dirtFresh   = new(0.30f, 0.22f, 0.16f);   // 湿的新土
    public Color dirtDry     = new(0.45f, 0.36f, 0.27f);
    public Color dirtSettled = new(0.52f, 0.46f, 0.38f);   // 沉降后接近地表色
    public Color printFresh  = new(0.35f, 0.28f, 0.20f);
    public Color printFaded  = new(0.55f, 0.50f, 0.40f);
    public Color featherTint = new(0.88f, 0.86f, 0.78f);
    public Color markFresh   = new(0.28f, 0.22f, 0.18f);
    public Color pressedTint = new(0.70f, 0.66f, 0.45f);

    // 与 GlimmerGrass.shader 的 _TramplePoints[16] 数组长度耦合——两侧同改
    private const int TRAMPLE_MAX = 16;
    private static readonly int TrampleCountId  = Shader.PropertyToID("_TrampleCount");
    private static readonly int TramplePointsId = Shader.PropertyToID("_TramplePoints");

    private enum TraceType { Mound, CollapsedBurrow, Trail, Feathers, ScentMarks, RestPatch, RangeHalt }

    /// <summary>一条派生痕迹：键=源记录哈希（身份），场景表现挂在 root 下。</summary>
    private class TraceInstance
    {
        public TraceType type;
        public int seed;
        public int birthDay;          // ToDays(记录日期)；RangeHalt 用 -1（实时态）
        public GameObject root;
        public readonly List<Renderer> renderers = new();
        // 本痕迹贡献的压痕点（世界 XZ + 半径 + 基础强度，随龄再衰减）
        public readonly List<Vector4> trampleContribs = new();
        public float spawnRealTime;   // 展示层淡入用（真实秒）
    }

    private readonly Dictionary<string, TraceInstance> _traces = new();
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
        _trampleGather.Clear();
        foreach (var t in _traces.Values)
        {
            if (t.trampleContribs.Count == 0) continue;
            float fadeIn = Mathf.Clamp01((Time.time - t.spawnRealTime) / 2f);
            foreach (var c in t.trampleContribs)
                _trampleGather.Add(new Vector4(c.x, c.y, c.z, c.w * fadeIn));
        }
        // 超上限时保留强度最高的
        if (_trampleGather.Count > TRAMPLE_MAX)
            _trampleGather.Sort((a, b) => b.w.CompareTo(a.w));

        int n = Mathf.Min(_trampleGather.Count, TRAMPLE_MAX);
        for (int i = 0; i < n; i++) _trampleArray[i] = _trampleGather[i];
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
        // 鹿鼠活动范围收缩态（实时字段，无历史记录）——跨过阈值也要触发重建
        var dm = wm.Registry?.GetAnimal("deer_mouse");
        sig = sig * 31 + (dm != null && dm.activityRange < 0.5f ? 1 : 0);
        return sig;
    }

    private void Rebuild(WorldManager wm, WorldSaveData save)
    {
        int today = ToDays(save.gameTime);
        var desired = new Dictionary<string, System.Action<TraceInstance>>();
        var birthDays = new Dictionary<string, int>();

        // —— T1/T2/T3/T6：从动物 history 与 location permanentChanges 派生 ——
        // T1 只从 history 派生（VoleClaimedZone 事件与 vole_expansion 记录同 tick 双发，
        // 单一来源即天然去重）
        var moundKeys = new List<string>();   // (key, birthDay) 用于"只留最新 N"裁剪

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
                    int age = today - day;
                    string baseKey = $"{animal.speciesId}|{rec.date}|{rec.fromValue}->{rec.toValue}|{rec.triggeredBy}";
                    occur.TryGetValue(baseKey, out int n);
                    occur[baseKey] = n + 1;
                    string key = n == 0 ? baseKey : $"{baseKey}#{n}";
                    int seed = Fnv1a(key);
                    string from = rec.fromValue, to = rec.toValue, trigger = rec.triggeredBy;

                    // T1 新翻土堆：田鼠扩张/搬家
                    if (trigger == "vole_expansion" || trigger == "vole_relocate_flood")
                    {
                        string mk = "mound|" + key;
                        moundKeys.Add(mk);
                        birthDays[mk] = day;
                        desired[mk] = t => SpawnMound(t, trigger, to, age, seed);
                    }

                    // T6 狐狸巡逻记号（领地边界，语料："石头区和中央之间…留了几个记号"）
                    if (trigger == "fox_patrol" && age <= markMaxAge)
                    {
                        string sk = "marks|" + key;
                        birthDays[sk] = day;
                        desired[sk] = t => SpawnMarks(t, from, to, age, seed);
                    }

                    // T3 脚印串：一切位置迁移都留一串脚印
                    if (age <= trailMaxAge && !string.IsNullOrEmpty(from) && !string.IsNullOrEmpty(to) && from != to)
                    {
                        string tk = "trail|" + key;
                        birthDays[tk] = day;
                        desired[tk] = t => SpawnTrail(t, from, to, age, seed);
                    }
                }
            }
        }

        // T1 裁剪：土堆沉降后保留，但总数只留最新 moundKeepCount 个
        if (moundKeys.Count > moundKeepCount)
        {
            moundKeys.Sort((a, b) => birthDays[b].CompareTo(birthDays[a]));
            for (int i = moundKeepCount; i < moundKeys.Count; i++)
                desired.Remove(moundKeys[i]);
        }

        // T2 塌陷旧洞（permanentChanges，永不移除——不可逆原则）
        if (save.locations != null)
        {
            foreach (var loc in save.locations)
            {
                if (loc.permanentChanges == null) continue;
                foreach (var pc in loc.permanentChanges)
                {
                    if (pc.changeType != "burrow_collapse") continue;
                    string key = $"collapse|{loc.locationId}|{pc.date}|{pc.changeType}";
                    int seed = Fnv1a(key);
                    string zone = loc.locationId;
                    birthDays[key] = ToDays(ParseKeyDate(pc.date));
                    desired[key] = t => SpawnCollapsedBurrow(t, zone, seed);
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
                int age = today - day;

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
                    birthDays[fk] = day;
                    desired[fk] = t => SpawnFeathers(t, zone, age, seed);
                }

                // T7 歇息压痕：涌现时刻（targetId=zone）——主表达是草被压弯
                if (age <= restMaxAge && e.type == WorldEventType.QuietConvergence)
                {
                    string zone = e.targetId;
                    string rk = "rest|" + key;
                    birthDays[rk] = day;
                    desired[rk] = t => SpawnRestPatch(t, zone, age, seed);
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
            birthDays[key] = today;
            desired[key] = t => SpawnRangeHalt(t, Fnv1a(key));
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
                var again = new TraceInstance { spawnRealTime = keepSpawnTime };
                kv.Value(again);
                _traces[kv.Key] = again;
            }
            else
            {
                var t = new TraceInstance { spawnRealTime = Time.time };
                kv.Value(t);
                _traces[kv.Key] = t;
            }
        }
    }

    // ── 各类型痕迹的生成 ─────────────────────────────────────────

    /// <summary>
    /// T1 新翻土堆：三阶段老化（湿→干→沉降）。
    /// 扩张土在石头区与中央之间（语料："石头区和中央之间…新翻的土"）；
    /// 洪水搬家的新洞口开在目的地 zone（规则可能指向 highland_east 等，跟数据走）。
    /// </summary>
    private void SpawnMound(TraceInstance t, string trigger, string toZone, int age, int seed)
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

        t.root = NewRoot($"Mound_{seed:X8}", p);
        var rng = new System.Random(seed);
        AddProp(t, TraceKit.Mound, dirtMaterial, c, p + Vector3.up * 0.01f,
                Quaternion.Euler(0f, (float)rng.NextDouble() * 360f, 0f),
                new Vector3(1f, flatten, 1f));
        // 新土周围草被扒开：小半径弱压痕，随龄衰减
        if (age <= 12)
            t.trampleContribs.Add(new Vector4(p.x, p.z, 0.7f, 0.55f * (1f - age / 12f)));
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

    /// <summary>T3 脚印串：沿 from→to 边 4-6 片小椭圆，左右交替，随龄缩小褪色。</summary>
    private void SpawnTrail(TraceInstance t, string from, string to, int age, int seed)
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

        t.root = NewRoot($"Trail_{seed:X8}", pts[0]);
        for (int i = 0; i < pts.Count; i++)
        {
            Vector3 p = pts[i] + perp * ((i % 2 == 0) ? 0.09f : -0.09f);   // 左右脚交替
            AddProp(t, TraceKit.Footprint, dirtMaterial, c, p + Vector3.up * 0.02f, yaw,
                    Vector3.one * scale);
        }
        // 新鲜足迹压出一条浅沟：路径中点一个压痕
        if (age <= 3 && pts.Count > 1)
        {
            Vector3 mid = pts[pts.Count / 2];
            t.trampleContribs.Add(new Vector4(mid.x, mid.z, 1.1f, 0.45f * (1f - age / 3f)));
        }
    }

    /// <summary>T5 羽毛：离境 zone 内 2-3 片，随龄压平褪色。</summary>
    private void SpawnFeathers(TraceInstance t, string zone, int age, int seed)
    {
        t.type = TraceType.Feathers; t.seed = seed;
        if (!zoneMap.TrySampleZone(zone, seed, out Vector3 c0)) return;

        var rng = new System.Random(seed);
        int count = 2 + rng.Next(2);
        float life = 1f - age / (float)featherMaxAge;
        Color c = Color.Lerp(featherTint * 0.8f, featherTint, life);

        t.root = NewRoot($"Feathers_{seed:X8}", c0);
        for (int i = 0; i < count; i++)
        {
            float ang = (float)rng.NextDouble() * Mathf.PI * 2f;
            float r   = 0.3f + (float)rng.NextDouble() * 1.2f;
            if (!zoneMap.TryGroundAt(c0.x + Mathf.Cos(ang) * r, c0.z + Mathf.Sin(ang) * r, out Vector3 p))
                continue;
            // 随龄压平：新落的羽毛翘一点，久了贴平
            float tilt = Mathf.Lerp(2f, 14f, life);
            var rot = Quaternion.Euler(tilt, (float)rng.NextDouble() * 360f, 0f);
            AddProp(t, TraceKit.Feather, featherMaterial, c, p + Vector3.up * 0.02f, rot, Vector3.one);
        }
    }

    /// <summary>T6 狐狸记号：巡逻边上 1-3 个暗斑，慢淡出。</summary>
    private void SpawnMarks(TraceInstance t, string from, string to, int age, int seed)
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

    /// <summary>T7 歇息压痕：两片相挨的压草椭圆（"两个影子挨得近了些"），主表达靠 trample。</summary>
    private void SpawnRestPatch(TraceInstance t, string zone, int age, int seed)
    {
        t.type = TraceType.RestPatch; t.seed = seed;
        if (!zoneMap.TrySampleZone(zone, seed, out Vector3 p0)) return;

        var rng = new System.Random(seed);
        float ang = (float)rng.NextDouble() * Mathf.PI * 2f;
        var offset = new Vector3(Mathf.Cos(ang), 0f, Mathf.Sin(ang)) * 1.2f;
        if (!zoneMap.TryGroundAt(p0.x + offset.x, p0.z + offset.z, out Vector3 p1))
            p1 = p0 + offset;

        float life = 1f - age / (float)restMaxAge;
        Color c = Color.Lerp(pressedTint * 0.92f, pressedTint, life);   // 淡出趋近草色

        t.root = NewRoot($"Rest_{seed:X8}", p0);
        AddProp(t, TraceKit.PressedOval, pressedMaterial, c, p0 + Vector3.up * 0.015f,
                Quaternion.Euler(0f, ang * Mathf.Rad2Deg, 0f), Vector3.one);
        AddProp(t, TraceKit.PressedOval, pressedMaterial, c, p1 + Vector3.up * 0.015f,
                Quaternion.Euler(0f, ang * Mathf.Rad2Deg + 25f, 0f), Vector3.one * 0.85f);

        // 两片压痕是这一刻的主视觉：强度最高，随龄衰减
        float s = 0.9f * life;
        t.trampleContribs.Add(new Vector4(p0.x, p0.z, 1.5f, s));
        t.trampleContribs.Add(new Vector4(p1.x, p1.z, 1.3f, s * 0.9f));
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

    // ── prop 组装 ────────────────────────────────────────────────

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

    /// <summary>FNV-1a 32 位。绝不用 string.GetHashCode()——它逐进程随机化，痕迹会每次启动乱跳。</summary>
    private static int Fnv1a(string s)
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

    /// <summary>解析 "Y1-M3-D12"（镜像 NarrativeRuleEngine.ParseDate；容错返回第 1 天）。</summary>
    private static GameDateTime ParseKeyDate(string key)
    {
        var d = new GameDateTime();
        if (string.IsNullOrEmpty(key)) return d;
        var parts = key.Split('-');
        if (parts.Length != 3) return d;
        int.TryParse(parts[0].TrimStart('Y'), out d.year);
        int.TryParse(parts[1].TrimStart('M'), out d.month);
        int.TryParse(parts[2].TrimStart('D'), out d.day);
        return d;
    }

    private static int ToDays(GameDateTime d) =>
        d == null ? 0 : (d.year - 1) * 360 + (d.month - 1) * 30 + d.day;
}
