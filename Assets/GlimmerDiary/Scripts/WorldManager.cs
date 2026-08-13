using System;
using System.Collections.Generic;
using UnityEngine;
using GlimmerDiary.Core;
using GlimmerDiary.Data;
using GlimmerDiary.Utils;
using System.Runtime.CompilerServices;

// 场景里唯一挂在 GameObject 上的世界模拟脚本
// GameObject 命名 "WorldManager"，跨场景不销毁
public class WorldManager : MonoBehaviour
{
    private static WorldManager _instance;
    public static WorldManager Instance
    {
        get
        {
            // play 中改脚本会触发域重载：静态字段被清空，但 GameObject（DontDestroyOnLoad）
            // 本身还活着、Awake 也不会重跑——Instance 从此永远为 null，binder/注入器
            // 静默断联且无任何报错。惰性找回让单例自愈。
            if (_instance == null) _instance = FindFirstObjectByType<WorldManager>();
            // 找回本体还不够：域重载同样清空了不参与序列化的子系统对象图，需一并重建。
            if (_instance != null && Application.isPlaying) _instance.InitializeSubsystems();
            return _instance;
        }
        private set => _instance = value;
    }

    public EmotionInertiaSystem   EmotionInertia { get; private set; }
    public NaturalRhythmSystem    NaturalRhythm  { get; private set; }
    public WorldEnvironmentSystem Environment    { get; private set; }
    public TranslationLayer        Translation   { get; private set; }
    public EntityRegistry         Registry       { get; private set; }

    // 测试层通过 _saveData 直接访问（同一程序集内 internal 可见）
    internal WorldSaveData _saveData;
    public   WorldSaveData  WorldSave => _saveData;

    private NarrativeRuleEngine    _ruleEngine;
    private List<NarrativeRuleSO>  _allRules;
    private EntityRelationSystem   _relationSystem;
    private List<EntityRelationSO> _allRelations;
    private AnimalDriveSystem      _driveSystem;
    private BehaviorNarrator       _narrator;
    private AnimalDriveTuning      _driveTuning;
    private EmergentMomentDetector _emergentDetector;
    private VegetationSystem       _vegetationSystem;
    private EraSystem              _eraSystem;
    private VoleTownSystem         _voleTownSystem;
    private StratumSystem          _stratumSystem;
    private WingSystem             _wingSystem;

    // 已迁移到 AnimalDriveSystem 的实体-实体耦合：从关系系统的活动集中剔除
    // （资产保留在 Resources/Relations，仅运行时不再评估其状态效果）
    private static readonly HashSet<string> RetiredRelationIds = new()
    {
        "deer_mouse_anxious",      // → 鹿鼠 anxiety（cause=BirdAbsent）
        "vole_territory_expand",   // → 田鼠 Expand（cause=DeerMouseWithdrew）
        "weaver_habitat_lost",     // → 断枝边沿 → 织巢鸟离场
        "insect_surge_vegetation", // → 织巢鸟不在 → 东侧高地植被衰减
    };


    void Awake()
    {
        // 用底层字段而非属性：Awake 时保持原有"先到先得"语义，不触发惰性查找
        if (_instance != null && _instance != this) { Destroy(gameObject); return; }
        _instance = this;
        DontDestroyOnLoad(gameObject);
        InitializeSubsystems();
    }

    // 子系统初始化（Awake 与域重载自愈共用）。
    // play 中改脚本触发域重载：GameObject 本体（DontDestroyOnLoad）与静态 _instance
    // 可以找回，但这些纯 C# 子系统对象不参与序列化，全部被清成 null——
    // 留下一个"空壳"管理器，调用方拿到 Instance 也会在 GetWorldState() 等处 NRE。
    // 惰性重建：发现子系统缺失就从存档重建整张对象图。
    private void InitializeSubsystems()
    {
        if (EmotionInertia != null) return;   // 已初始化（Awake 正常路径外的重复调用短路）

        EmotionInertia = new EmotionInertiaSystem();
        NaturalRhythm  = new NaturalRhythmSystem();
        Environment    = new WorldEnvironmentSystem();
        Translation    = new TranslationLayer();

        _saveData = SaveSystem.LoadWorldState() ?? WorldInitializer.CreateNewWorld();
        // 旧档迁移：soilMoisturePeak 是 07-28 新增字段，旧档读出 0——
        // 用当前湿度兜底（极值 ≥ 现值恒成立），之后由传播循环自然累积
        if (_saveData.locations != null)
            foreach (var loc in _saveData.locations)
                if (loc.soilMoisturePeak < loc.soilMoisture) loc.soilMoisturePeak = loc.soilMoisture;
        EmotionInertia.Restore(_saveData.currentEEnv, _saveData.emotionHistory, _saveData.currentImpulse,
                               _saveData.pendingImpulse, _saveData.pendingImpulseReleaseRealTime);
        // 环境积分态（V1 D1）：droughtDebt 等随档恢复——重启不再靠 catch-up 重算，
        // 深层旱债断电不丢。旧档 null → Restore 空转，积分自然追上。
        Environment.Restore(_saveData.environmentState);
        // 缺席期间到期的 pending 在 catch-up 前先兑现："世界趁你不在想完了"——
        // 随后 WorldTick 的 Relax 按缺席天数自然衰减它，回来不会突然全强爆发。
        EmotionInertia.TryReleasePending();

        Registry = new EntityRegistry();
        Registry.Initialize(_saveData);

        _ruleEngine     = new NarrativeRuleEngine(Registry, _saveData);
        _allRules       = new List<NarrativeRuleSO>(Resources.LoadAll<NarrativeRuleSO>("Rules"));
        _relationSystem = new EntityRelationSystem(Registry, _saveData);
        _allRelations   = new List<EntityRelationSO>(Resources.LoadAll<EntityRelationSO>("Relations"));
        _allRelations.RemoveAll(r => r != null && RetiredRelationIds.Contains(r.relationId));
        _driveTuning    = Resources.Load<AnimalDriveTuning>("Tuning/AnimalDriveTuning");
        _driveSystem    = new AnimalDriveSystem(Registry, _saveData, _driveTuning);
        _vegetationSystem = new VegetationSystem(Registry, _saveData,
                              _driveTuning != null ? _driveTuning.insectVegDecay : 0.003f);
        _narrator       = new BehaviorNarrator(Registry, _saveData);
        var emergentTuning = Resources.Load<EmergentMomentTuning>("Tuning/EmergentMomentTuning");
        _emergentDetector  = new EmergentMomentDetector(Registry, _saveData, emergentTuning);
        _eraSystem         = new EraSystem(_saveData);
        _voleTownSystem    = new VoleTownSystem(_saveData);
        _stratumSystem     = new StratumSystem(_saveData);
        _wingSystem        = new WingSystem(_saveData);
        Debug.Log($"[WorldManager] Rules={_allRules.Count}  Relations={_allRelations.Count} (retired {RetiredRelationIds.Count})  " +
                  $"Tuning={(_driveTuning != null ? _driveTuning.name : "defaults")}");
        Debug.Log($"[WorldManager] SaveDir: {SaveSystem.GetSaveDir()}");

        // 节律对齐到已载入的世界日历（季节/yearProgress 取自 gameTime）
        NaturalRhythm.Tick(_saveData.gameTime);

        // 新世界预跑：玩家到达的应该是一个"已经活过的世界"（Worksheet §0 拍板）——
        // 先跑 30 天中性 WorldTick 再交给玩家。isCatchUp:true 丢弃逐日世界志噪音，
        // worldEvents（append-only）原样保留，成为痕迹的历史来源。
        if (string.IsNullOrEmpty(_saveData.lastTickRealTime))
        {
            Debug.Log($"[WorldManager] 新世界：先跑 {NewWorldPreRunDays} 天中性预跑。");
            // 预跑不是"缺席"——玩家还没到达，不产生缺席信；
            // 预跑也不翻纪元——章节叙事从玩家到达起算，ChapterTurned 锚必有信对应（宪法⑤）
            _eraSystem.Suspended = true;
            _stratumSystem.Suspended = true;   // D9：预跑不掷深层遗物出露签——长眠不结束在玩家到达前
            WorldTick(NewWorldPreRunDays, isCatchUp: true, writeAbsenceLetter: false);
            _eraSystem.Suspended = false;
            _stratumSystem.Suspended = false;
            SaveSystem.SaveWorldState(_saveData);   // 锚定到现在，避免紧接的墙钟 catch-up 重跑同一天
        }

        // 启动 catch-up：按真实墙钟流逝天数把世界静默推进到现在
        int catchUpDays = WallClockDeltaDays();
        if (catchUpDays > 0)
        {
            Debug.Log($"[WorldManager] Catch-up: advancing {catchUpDays} day(s) since last save.");
            WorldTick(catchUpDays, isCatchUp: true);
            SaveSystem.SaveWorldState(_saveData);   // 重新锚定到现在
        }
    }

    void Start()
    {
        // 初始化天气快照：只消费无状态信号，不走 UpdateFromEEnv 的有状态积分——
        // 启动不是一天，重启 app 不应让 Soil/Decay 多走一步（Awake catch-up 已按天模拟过）。
        Environment.ConsumeSignals(
            Translation.Translate(EmotionInertia.CurrentEEnv, NaturalRhythm.State, EmotionInertia.Impulse));
    }

    // 空闲心跳：会话内无日记输入时也定期重算节律快照（dayProgress/lightIntensity 跟随真实墙钟），
    // 并重译无状态天气信号（信号 7 苍穹含 lightIntensity 因子，不重译则星空冻结在上次模拟时刻）。
    // 只刷新 NaturalRhythm + 无状态信号，不碰 worldEvents/animals/plants/emotionHistory，不是 SimulatePass。
    [SerializeField, Tooltip("节律心跳间隔（真实秒）。dayProgress 一天走一圈，30 秒的变化量已低于肉眼阈值。")]
    private float rhythmHeartbeatSeconds = 30f;
    private float _rhythmHeartbeatTimer;

    void Update()
    {
        _rhythmHeartbeatTimer += Time.deltaTime;
        if (_rhythmHeartbeatTimer < rhythmHeartbeatSeconds) return;
        _rhythmHeartbeatTimer = 0f;
        NaturalRhythm.Tick(_saveData.gameTime);
        // 延迟消化的泵：pending 脉冲到点释放，当拍（本帧 ConsumeSignals）即重译天气——
        // "世界想了一会儿"之后，回响落在这里被看见。
        EmotionInertia.TryReleasePending();
        Environment.ConsumeSignals(
            Translation.Translate(EmotionInertia.CurrentEEnv, NaturalRhythm.State, EmotionInertia.Impulse));
    }

    // 自主软上限：catch-up 总是按完整墙钟天数推进 gameTime 日历，
    // 但每日重模拟只跑最后 N 天，避免长缺席时启动卡顿（深层历史留给 Phase 3 摘要）
    const int MaxSimulatedCatchupDays = 90;

    // 新世界预跑天数（Worksheet §0：玩家到达一个已经活过的世界）。
    // ≤ MaxSimulatedCatchupDays，预跑的每一天都跑完整 SimulatePass。
    const int NewWorldPreRunDays = 30;

    // 推进一个日历日：gameTime+1 → 情绪向基线回落 → 刷新季节
    private void AdvanceCalendar()
    {
        _saveData.gameTime.Advance(1);
        EmotionInertia.Relax();                 // 自主回落（alpha 0.05）
        NaturalRhythm.Tick(_saveData.gameTime);
    }

    // 在当前日历/情绪下跑一次完整模拟（不推进日历日）
    private void SimulatePass()
    {
        // Step 0：翻译层产出无状态信号 1/2/3/7（在天气消费之前）
        var signals = Translation.Translate(EmotionInertia.CurrentEEnv, NaturalRhythm.State, EmotionInertia.Impulse); //通过情绪向量和现有状态输出新世界信号
        Environment.UpdateFromEEnv(EmotionInertia.CurrentEEnv, signals, NaturalRhythm.State); // 新的环境（含旱债积分，需季节基准）
        _saveData.environmentState = Environment.Snapshot();   // 积分态随每日模拟落档（V1 D1）
        PropagateEnvironmentToLocations();
        _vegetationSystem.Tick(_saveData.gameTime, Environment.State);   // loc.vegetationDensity 单一写者 + 蒲公英落种；驱动层只读

        SyncEmotionSnapshot();

        // 动物状态系统：内部状态演化 → 行为输出（在文本层之前，让其读到最新行为）
        _driveSystem.SetEnvironment(Environment.State, NaturalRhythm.State);
        _driveSystem.Tick(_saveData.gameTime);

        // 涌现时刻检测器：只读 behavior + 位置，命中则追加 QuietConvergence 世界事件
        // （在 narrator 之前，让本日叙述同一遍带上它）
        _emergentDetector.Detect(_saveData.gameTime);

        // 文本层：读行为输出 + 世界事件 → 世界志（按 cause 选细节）
        _narrator.SetEnvironment(Environment.State, NaturalRhythm.State);
        _narrator.Narrate(_saveData.gameTime);

        _ruleEngine.SetEnvironment(Environment.State, NaturalRhythm.State);
        _ruleEngine.Evaluate(_allRules, _saveData.gameTime);

        // 实体关系层：在叙事规则之后级联评估（优先级有序，效果就地生效）
        _relationSystem.SetEnvironment(Environment.State, NaturalRhythm.State);
        _relationSystem.Evaluate(_allRelations, _saveData.gameTime);

        // 纪元钟（V1 D2）：每日最后拍板章节——读的是本日全管线跑完后的最新累积状态，
        // 转换即发 ChapterTurned 事件 + 编年史信（不进 narrator，下一天会被其游标静默跳过）
        _eraSystem.Tick(_saveData.gameTime, Environment.State, NaturalRhythm.State, Registry);

        // 田鼠镇（V1 D3）：在纪元钟之后——镇散判据要读当日最新章节。
        // 只读土堆记录写 voleTrails，田鼠 AI 一行不动（§4.3 红线）
        _voleTownSystem.Tick(_saveData.gameTime);

        // 新生地层（V1 D5）：在小径之后——小径当日 lapsed 淡完即入土，隔日边界不跨拍
        _stratumSystem.Tick(_saveData.gameTime, Environment.State, EmotionInertia.CurrentEEnv);

        // 侧翼（V1 D7/D8）：每日最后——读当日世界事件终态（离场→离去标记），
        // 慢变量漂移与掷签全部确定性，不看你的日记
        _wingSystem.Tick(_saveData.gameTime);
    }

    // 自主世界 tick：推进世界 deltaDays，每天模拟一次。与日记无关。
    // writeAbsenceLetter：catch-up 结束后把区间世界志聚合成一封缺席信（矩阵补全 §5.5 第 1 行）；
    // 新世界预跑传 false——预跑不是缺席。
    public void WorldTick(int deltaDays, bool isCatchUp = false, bool writeAbsenceLetter = true)
    {
        if (deltaDays <= 0) return;

        int chronicleMark = _saveData.pendingChronicles.Count;   // 缺席信接缝
        int startAbsDays  = _saveData.gameTime.ToAbsoluteDays(); // 缺席窗口起点（含）
        int simulateFrom  = isCatchUp ? Mathf.Max(0, deltaDays - MaxSimulatedCatchupDays) : 0;

        for (int d = 0; d < deltaDays; d++)
        {
            AdvanceCalendar();                          // 始终推进 gameTime（= 墙钟天数）
            if (d >= simulateFrom)
            {
                var wMark = WitnessSnapshot(_saveData); // 记忆双读：拍前计数快照
                SimulatePass();
                // 非 catch-up 拍 = 玩家在场——本拍新生的记录统一戳 witnessed（V1 D6）。
                // 集中在此打戳（而非各创建点）：创建点散在驱动/规则/纪元/地层各处，
                // 单点打戳未来新系统零接入成本。
                if (!isCatchUp) WitnessStampNew(_saveData, wMark);
            }
        }

        if (isCatchUp)
        {
            // 逐日世界志噪音仍丢弃；worldEvents（append-only 永久日志）原样保留。
            // 缺席信：从同一区间按 salience 聚合 ≤5 条 + 点名窗内新增永久痕迹，
            // 一封信顶替整段（AbsenceLetterComposer 纯函数，不值得写信时返回 null）。
            int extra = _saveData.pendingChronicles.Count - chronicleMark;
            WorldChronicleEntry letter = null;
            if (writeAbsenceLetter)
            {
                // segment 可能为空但窗内仍有永久事件——此时信只剩点名句，
                // 恰是缺席期最重要的归因桥，仍要生成
                var segment = extra > 0
                    ? _saveData.pendingChronicles.GetRange(chronicleMark, extra)
                    : new List<WorldChronicleEntry>();
                letter = AbsenceLetterComposer.Compose(
                    segment,
                    CountWindowEvents(startAbsDays, WorldEventType.TreeBranchBroke),
                    CollectWindowCollapses(startAbsDays, out var collapseKeys),
                    _saveData.gameTime.ToDisplayString(),
                    rhythm: NaturalRhythm.State,
                    chapterCrossed: WindowHasChapterTurn(startAbsDays),
                    collapseWitnessKeys: collapseKeys);
            }
            if (extra > 0)
                _saveData.pendingChronicles.RemoveRange(chronicleMark, extra);
            if (letter != null)
                _saveData.pendingChronicles.Insert(chronicleMark, letter);
        }
    }

    // 缺席窗口内某类 worldEvents 的数量（gameDate 为 ToKeyString，按绝对日序过滤）
    private int CountWindowEvents(int startAbsDays, string eventType)
    {
        int n = 0;
        foreach (var e in _saveData.worldEvents)
            if (e.type == eventType
                && GameDateTime.ParseKey(e.gameDate).ToAbsoluteDays() >= startAbsDays)
                n++;
        return n;
    }

    // 缺席窗口内新增塌洞（burrow_collapse）的 location displayName 列表；
    // out keys：同批塌洞的身份键（collapse|loc|date|type）——信件点名入 witnessKeys（V1 D6 记忆双读）
    private List<string> CollectWindowCollapses(int startAbsDays, out List<string> keys)
    {
        var names = new List<string>();
        keys = new List<string>();
        foreach (var loc in _saveData.locations)
            foreach (var pc in loc.permanentChanges)
                if (pc.changeType == "burrow_collapse"
                    && GameDateTime.ParseKey(pc.date).ToAbsoluteDays() >= startAbsDays)
                {
                    names.Add(loc.displayName);
                    keys.Add($"collapse|{loc.locationId}|{pc.date}|{pc.changeType}");
                }
        return names;
    }

    // 缺席窗口内是否翻过纪元章节（D4）——翻过则缺席信整封换编年史语气
    private bool WindowHasChapterTurn(int startAbsDays)
    {
        foreach (var e in _saveData.worldEvents)
            if (e.type == WorldEventType.ChapterTurned
                && GameDateTime.ParseKey(e.gameDate).ToAbsoluteDays() >= startAbsDays)
                return true;
        return false;
    }

    // ── 记忆双读（V1 D6）────────────────────────────────────────
    // witnessed 的唯一写入路径：①非 catch-up 拍后统一打戳（在场见证）；
    // ②MarkWitnessed——信件被阅读时把信里点名的痕迹翻真（读信知道了它，也算见证）。

    // 拍前计数快照（public static：冒烟管线镜像 SimulatePass 时复用，同 PropagateRainfallToLocations 口径）
    public static int[] WitnessSnapshot(WorldSaveData s)
    {
        var list = new List<int>
        {
            s.worldEvents?.Count ?? 0,
            s.voleTrails?.Count ?? 0
        };
        if (s.animals != null)   foreach (var a in s.animals)   list.Add(a.history?.Count ?? 0);
        if (s.locations != null) foreach (var l in s.locations) list.Add(l.permanentChanges?.Count ?? 0);
        return list.ToArray();
    }

    // 把快照之后新生的记录戳 witnessed=true（顺序必须与 WitnessSnapshot 一致）
    public static void WitnessStampNew(WorldSaveData s, int[] before)
    {
        int i = 0;
        int evFrom = before[i++];
        if (s.worldEvents != null)
            for (int k = evFrom; k < s.worldEvents.Count; k++) s.worldEvents[k].witnessed = true;
        int vtFrom = before[i++];
        if (s.voleTrails != null)
            for (int k = vtFrom; k < s.voleTrails.Count; k++) s.voleTrails[k].witnessed = true;
        if (s.animals != null)
            foreach (var a in s.animals)
            {
                int from = before[i++];
                if (a.history != null)
                    for (int k = from; k < a.history.Count; k++) a.history[k].witnessed = true;
            }
        if (s.locations != null)
            foreach (var l in s.locations)
            {
                int from = before[i++];
                if (l.permanentChanges != null)
                    for (int k = from; k < l.permanentChanges.Count; k++) l.permanentChanges[k].witnessed = true;
            }
    }

    // 信件阅读回执：把点名键对应的源记录与地层记录 witnessed 翻真（ChronicleLetter 经此写入）。
    // static：冒烟管线无 WorldManager 实例也可验证同一逻辑（同 PropagateRainfallToLocations 口径）。
    public static void MarkWitnessed(WorldSaveData save, List<string> keys)
    {
        if (save == null || keys == null) return;
        foreach (var key in keys)
        {
            // collapse|{locationId}|{date}|{changeType}
            var parts = key.Split('|');
            if (parts.Length == 4 && parts[0] == "collapse" && save.locations != null)
                foreach (var loc in save.locations)
                {
                    if (loc.locationId != parts[1] || loc.permanentChanges == null) continue;
                    foreach (var pc in loc.permanentChanges)
                        if (pc.date == parts[2] && pc.changeType == parts[3]) pc.witnessed = true;
                }
            // 可能已先入土后被点名——同一身份键同步翻
            if (save.strata != null)
                foreach (var s in save.strata)
                    if (s.sourceKey == key) s.witnessed = true;
        }
    }

    // 地层语料上下文透传：relic/exposed 点击语料要 witnessed 档 + 断代句。
    // L3 文本层不导入 Core，经此读（同 GetVoleAppellation 管线）。
    public bool TryGetStratumContext(string sourceKey, out bool witnessed, out string layerPhrase)
    {
        var s = _stratumSystem?.FindStratum(sourceKey);
        witnessed  = s?.witnessed ?? false;
        layerPhrase = s != null ? StratumSystem.LayerPhrase(s) : null;
        return s != null;
    }

    // 侧翼尘霾透传（V1 D8 风通道）：西翼旱情烈度 0..1——L3 大气绑定层经此读，
    // 不导入 Core 纪律不变。侧翼数据本身的持有/演进归 WingSystem。
    public float GetWingDust01() => WingSystem.WestDrought01(_saveData);

    // 情绪注入：仅写日记时调用，只更新 E_env（+ 登记 pending 脉冲），不推进日历
    public void InjectEmotion(JournalEntry entry) => EmotionInertia.Update(entry.emotion);

    // 情绪相关存档快照的唯一写者（currentEEnv/currentImpulse/emotionHistory + pending 两字段）。
    // SimulatePass 与 OnJournalSubmitted 共用——写日记路径不再跑 SimulatePass（2026-08-03 A 方案），
    // 但不走这里注入的情绪就只活内存里，关 app 即丢。
    private void SyncEmotionSnapshot()
    {
        _saveData.currentEEnv    = EmotionInertia.CurrentEEnv;
        _saveData.currentImpulse = EmotionInertia.Impulse;
        _saveData.emotionHistory = EmotionInertia.History;
        _saveData.pendingImpulse = EmotionInertia.PendingImpulse;
        _saveData.pendingImpulseReleaseRealTime =
            EmotionInertia.PendingImpulse != null
                ? EmotionInertia.PendingReleaseRealTime.ToString("o")
                : null;
    }

    // 距上次锚点的整天数（同一天为 0）；新世界返回 0
    private int WallClockDeltaDays()
    {
        if (string.IsNullOrEmpty(_saveData.lastTickRealTime)) return 0;
        if (!DateTime.TryParse(_saveData.lastTickRealTime, null,
                System.Globalization.DateTimeStyles.RoundtripKind, out var last)) return 0;
        return Mathf.Max(0, (int)(DateTime.Now.Date - last.Date).TotalDays);
    }

    // 玩家提交日记时的唯一入口（由 UI 层调用）
    // 墙钟 catch-up → 注入情绪 → 落盘快照 → 存档。
    // 2026-08-03 A 方案：不再跑 SimulatePass——世界只在日历日边界演化，
    // 日记不当场多演一天；天气回响经 pending 脉冲延迟几分钟后由空闲心跳落地。
    public void OnJournalSubmitted(JournalEntry entry)
    {
        int delta = WallClockDeltaDays();
        if (delta > 0) WorldTick(delta, isCatchUp: delta > 1);   // 缺席天数以注入前情绪演化

        InjectEmotion(entry);   // 当天情绪（E_env 即时，脉冲挂 pending）
        SyncEmotionSnapshot();  // 注入立即落盘——不跑 SimulatePass 后这是唯一快照时机

        SaveSystem.SaveWorldState(_saveData);   // 内部会盖上 lastTickRealTime = now
        SaveSystem.AppendJournalEntry(entry);

        // TODO: 通知视觉层播放仪式时刻（视觉阶段）
    }

    // 测试用重载：直接提交情绪向量，不写日记存档
    public void OnJournalSubmitted(EmotionVector emotion)
    {
        OnJournalSubmitted(new JournalEntry
        {
            entryId       = Guid.NewGuid().ToString(),
            realTimestamp = DateTime.Now.ToString("o"),
            rawText       = "[test]",
            emotion       = emotion
        });
    }

    // 测试用：重置为指定存档（清空情绪历史、实体状态、规则引擎）
    public void ReinitializeWithSave(WorldSaveData newSave)
    {
        _saveData = newSave;
        EmotionInertia = new EmotionInertiaSystem();
        EmotionInertia.Restore(newSave.currentEEnv, newSave.emotionHistory, newSave.currentImpulse,
                               newSave.pendingImpulse, newSave.pendingImpulseReleaseRealTime);
        Registry.Initialize(_saveData);
        NaturalRhythm.Tick(_saveData.gameTime);
        // 环境积分器随存档一起恢复（V1 D1：有档则续，无档归零后由积分追上），
        // 再只消费无状态信号刷新天气快照——重置不是一天，不走 UpdateFromEEnv 积分。
        Environment = new WorldEnvironmentSystem();
        Environment.Restore(newSave.environmentState);
        Environment.ConsumeSignals(
            Translation.Translate(EmotionInertia.CurrentEEnv, NaturalRhythm.State, EmotionInertia.Impulse));
        _ruleEngine = new NarrativeRuleEngine(Registry, _saveData);
        _ruleEngine.SetEnvironment(Environment.State, NaturalRhythm.State);
        _relationSystem = new EntityRelationSystem(Registry, _saveData);
        _relationSystem.SetEnvironment(Environment.State, NaturalRhythm.State);
        _driveSystem    = new AnimalDriveSystem(Registry, _saveData, _driveTuning);
        _driveSystem.SetEnvironment(Environment.State, NaturalRhythm.State);
        _vegetationSystem = new VegetationSystem(Registry, _saveData,
                              _driveTuning != null ? _driveTuning.insectVegDecay : 0.003f);
        _narrator       = new BehaviorNarrator(Registry, _saveData);
        _narrator.SetEnvironment(Environment.State, NaturalRhythm.State);
        _emergentDetector = new EmergentMomentDetector(Registry, _saveData,
            Resources.Load<EmergentMomentTuning>("Tuning/EmergentMomentTuning"));
        _eraSystem        = new EraSystem(_saveData);
        _voleTownSystem   = new VoleTownSystem(_saveData);
    }

    // 将全局环境参数（Rainfall）传播到各地点实体的 waterLevel / soilMoisture（单写者）
    // 放在规则评估之前调用，让规则看到最新的地点状态
    // 信号 1 Wetness 已由翻译层落地（State.Rainfall = signals.Wetness）；本传播函数仍经 State.Rainfall
    // 消费，改读 signals.Wetness 的消费者迁移延后。soilMoisture 读者（植被）待翻译层复合式决定。
    private void PropagateEnvironmentToLocations() =>
        PropagateRainfallToLocations(_saveData, Environment.State.Rainfall);

    // 静态入口：EditMode 管线镜像（AnimalDriveSmokeTest）直接复用，
    // 速率表只此一份——不得在测试里手抄副本。
    public static void PropagateRainfallToLocations(WorldSaveData save, float rain)
    {
        // 侧翼河通道（V1 D7）：今日抵达的上游来水汇总。WingSystem 只往队列里放记录，
        // 水位入账只在这里——loc.waterLevel 单写者纪律不破。
        float upstreamInflow = 0f;
        if (save.wings?.upstreamRains != null)
        {
            int todayAbs = save.gameTime.ToAbsoluteDays();
            foreach (var r in save.wings.upstreamRains)
                if (GameDateTime.ParseKey(r.arriveDateKey).ToAbsoluteDays() == todayAbs)
                    upstreamInflow += r.amount;
        }

        foreach (var loc in save.locations)
        {
            // 各地点积水速率不同：低洼地最慢排水，东侧高地最快
            float accRate = loc.locationId switch
            {
                "lowland"       => 0.30f,
                "riverbank"     => 0.22f,
                "center"        => 0.15f,
                "highland_east" => 0.10f,
                "stone_area"    => 0.08f,
                _               => 0.15f
            };
            // 每日排水 0.03——植被捂水（§5.5 第 3 行：生命→环境第一条反向耦合）：
            // 茂密区水退更慢（满植被约减半），下限防负消退=无限积水。
            // loc.waterLevel 单写者即本函数，读 vegetationDensity 是同写者内部读。
            float drain = Mathf.Max(0.03f - VegDrainReduction * loc.vegetationDensity, MinDailyDrain);
            float delta = rain * accRate - drain;
            loc.waterLevel = Mathf.Clamp01(loc.waterLevel + delta);

            // 上游来水（与本地降雨正交的另一条来路）：riverbank 迎水，lowland 承接漫流。
            // "你这里一滴雨没下，河水却涨了"——两种水走同一个水位场，无标记无特例。
            if (upstreamInflow > 0f)
            {
                if (loc.locationId == "riverbank")
                    loc.waterLevel = Mathf.Clamp01(loc.waterLevel + upstreamInflow * WingSystem.RiverbankInflowRate);
                else if (loc.locationId == "lowland")
                    loc.waterLevel = Mathf.Clamp01(loc.waterLevel + upstreamInflow * WingSystem.LowlandInflowRate);
            }

            // 土壤湿度：比水位更慢的蓄水库；按区渗透率不同（沙石地渗透快、保水差）
            float soakRate = loc.locationId switch
            {
                "lowland"       => 0.15f,
                "riverbank"     => 0.12f,
                "center"        => 0.10f,
                "highland_east" => 0.07f,
                "stone_area"    => 0.04f,
                _               => 0.10f
            };
            // 固定每日蒸发 0.02；植被提渗透（同 §5.5 第 3 行，与排水减缓同向）
            loc.soilMoisture = Mathf.Clamp01(loc.soilMoisture + rain * (soakRate + VegSoakBonus * loc.vegetationDensity) - 0.02f);
            // 湿度极值只增不减（T1 水毁锁存的数据源，见 LocationEntity.soilMoisturePeak 注释）
            if (loc.soilMoisture > loc.soilMoisturePeak) loc.soilMoisturePeak = loc.soilMoisture;
        }
    }

    // 植被→水保持系数（§5.5 第 3 行，矩阵未给数值——初值按"满植被消退约减半"取，playtest 调）
    private const float VegDrainReduction = 0.015f;  // 日消退 −ε×veg（0.03 → 满植被 ≈0.019）
    private const float MinDailyDrain     = 0.005f;  // 消退下限，防满植被时负消退无限积水
    private const float VegSoakBonus      = 0.05f;   // 渗透 +δ×veg

    // 供视觉层随时读取（只读，不写入）
    public WorldEnvironmentState GetWorldState()  => Environment.State;
    public NaturalRhythmState    GetRhythmState() => NaturalRhythm.State;

    // 田鼠称谓档（V1 D3 称谓漂移）透传：L3 文本层不导入 Core，经此读档位词
    public string GetVoleAppellation() => VoleTownSystem.VoleAppellation(_saveData);
}

// 通过 ID 快速访问实体，避免每次遍历列表
public class EntityRegistry
{
    private readonly Dictionary<string, AnimalEntity>   _animals   = new();
    private readonly Dictionary<string, PlantEntity>    _plants    = new();
    private readonly Dictionary<string, LocationEntity> _locations = new();

    public void Initialize(WorldSaveData save)
    {
        _animals.Clear();
        _plants.Clear();
        _locations.Clear();
        foreach (var a in save.animals)   _animals[a.speciesId]    = a;
        foreach (var p in save.plants)    _plants[p.plantId]       = p;
        foreach (var l in save.locations) _locations[l.locationId] = l;
    }

    public AnimalEntity   GetAnimal  (string id) => _animals.GetValueOrDefault(id);
    public PlantEntity    GetPlant   (string id) => _plants.GetValueOrDefault(id);
    public LocationEntity GetLocation(string id) => _locations.GetValueOrDefault(id);
}
