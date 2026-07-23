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
    public static WorldManager Instance { get; private set; }

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
        if (Instance != null) { Destroy(gameObject); return; }
        Instance = this;
        DontDestroyOnLoad(gameObject);

        EmotionInertia = new EmotionInertiaSystem();
        NaturalRhythm  = new NaturalRhythmSystem();
        Environment    = new WorldEnvironmentSystem();
        Translation    = new TranslationLayer();

        _saveData = SaveSystem.LoadWorldState() ?? WorldInitializer.CreateNewWorld();
        EmotionInertia.Restore(_saveData.currentEEnv, _saveData.emotionHistory);

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
            // 预跑不是"缺席"——玩家还没到达，不产生缺席信
            WorldTick(NewWorldPreRunDays, isCatchUp: true, writeAbsenceLetter: false);
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
            Translation.Translate(EmotionInertia.CurrentEEnv, NaturalRhythm.State));
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
        Environment.ConsumeSignals(
            Translation.Translate(EmotionInertia.CurrentEEnv, NaturalRhythm.State));
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
        var signals = Translation.Translate(EmotionInertia.CurrentEEnv, NaturalRhythm.State); //通过情绪向量和现有状态输出新世界信号
        Environment.UpdateFromEEnv(EmotionInertia.CurrentEEnv, signals, NaturalRhythm.State); // 新的环境（含旱债积分，需季节基准）
        PropagateEnvironmentToLocations();
        _vegetationSystem.Tick(_saveData.gameTime, Environment.State);   // loc.vegetationDensity 单一写者 + 蒲公英落种；驱动层只读

        _saveData.currentEEnv    = EmotionInertia.CurrentEEnv;
        _saveData.emotionHistory = EmotionInertia.History;

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
            if (d >= simulateFrom) SimulatePass();      // 软上限跳过深层历史
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
                // segment 可能为空但窗内仍有永久事件（TreeBranchBroke 无文案，死端事实）——
                // 此时信只剩点名句，恰是缺席期最重要的归因桥，仍要生成
                var segment = extra > 0
                    ? _saveData.pendingChronicles.GetRange(chronicleMark, extra)
                    : new List<WorldChronicleEntry>();
                letter = AbsenceLetterComposer.Compose(
                    segment,
                    CountWindowEvents(startAbsDays, WorldEventType.TreeBranchBroke),
                    CollectWindowCollapses(startAbsDays),
                    _saveData.gameTime.ToDisplayString());
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

    // 缺席窗口内新增塌洞（burrow_collapse）的 location displayName 列表
    private List<string> CollectWindowCollapses(int startAbsDays)
    {
        var names = new List<string>();
        foreach (var loc in _saveData.locations)
            foreach (var pc in loc.permanentChanges)
                if (pc.changeType == "burrow_collapse"
                    && GameDateTime.ParseKey(pc.date).ToAbsoluteDays() >= startAbsDays)
                    names.Add(loc.displayName);
        return names;
    }

    // 情绪注入：仅写日记时调用，只更新 E_env，不推进日历
    public void InjectEmotion(JournalEntry entry) => EmotionInertia.Update(entry.emotion);

    // 距上次锚点的整天数（同一天为 0）；新世界返回 0
    private int WallClockDeltaDays()
    {
        if (string.IsNullOrEmpty(_saveData.lastTickRealTime)) return 0;
        if (!DateTime.TryParse(_saveData.lastTickRealTime, null,
                System.Globalization.DateTimeStyles.RoundtripKind, out var last)) return 0;
        return Mathf.Max(0, (int)(DateTime.Now.Date - last.Date).TotalDays);
    }

    // 玩家提交日记时的唯一入口（由 UI 层调用）
    // 墙钟 catch-up → 注入情绪 → 一次响应式模拟（不额外推进日历）→ 存档
    public void OnJournalSubmitted(JournalEntry entry)
    {
        int delta = WallClockDeltaDays();
        if (delta > 0) WorldTick(delta, isCatchUp: delta > 1);   // 缺席天数以注入前情绪演化

        InjectEmotion(entry);   // 当天情绪
        SimulatePass();         // 响应式模拟，不额外推进一天

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
        EmotionInertia.Restore(newSave.currentEEnv, newSave.emotionHistory);
        Registry.Initialize(_saveData);
        NaturalRhythm.Tick(_saveData.gameTime);
        // 环境积分器随存档一起归零（Soil/Decay/Vegetation 等有状态字段），
        // 再只消费无状态信号刷新天气快照——重置不是一天，不走 UpdateFromEEnv 积分。
        Environment = new WorldEnvironmentSystem();
        Environment.ConsumeSignals(
            Translation.Translate(EmotionInertia.CurrentEEnv, NaturalRhythm.State));
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
        }
    }

    // 植被→水保持系数（§5.5 第 3 行，矩阵未给数值——初值按"满植被消退约减半"取，playtest 调）
    private const float VegDrainReduction = 0.015f;  // 日消退 −ε×veg（0.03 → 满植被 ≈0.019）
    private const float MinDailyDrain     = 0.005f;  // 消退下限，防满植被时负消退无限积水
    private const float VegSoakBonus      = 0.05f;   // 渗透 +δ×veg

    // 供视觉层随时读取（只读，不写入）
    public WorldEnvironmentState GetWorldState()  => Environment.State;
    public NaturalRhythmState    GetRhythmState() => NaturalRhythm.State;
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
