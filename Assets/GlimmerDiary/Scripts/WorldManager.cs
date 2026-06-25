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
        _narrator       = new BehaviorNarrator(Registry, _saveData);
        Debug.Log($"[WorldManager] Rules={_allRules.Count}  Relations={_allRelations.Count} (retired {RetiredRelationIds.Count})  " +
                  $"Tuning={(_driveTuning != null ? _driveTuning.name : "defaults")}");
        Debug.Log($"[WorldManager] SaveDir: {SaveSystem.GetSaveDir()}");

        // 节律对齐到已载入的世界日历（季节/yearProgress 取自 gameTime）
        NaturalRhythm.Tick(_saveData.gameTime);

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
        Environment.UpdateFromEEnv(EmotionInertia.CurrentEEnv, NaturalRhythm.State);
    }

    // 自主软上限：catch-up 总是按完整墙钟天数推进 gameTime 日历，
    // 但每日重模拟只跑最后 N 天，避免长缺席时启动卡顿（深层历史留给 Phase 3 摘要）
    const int MaxSimulatedCatchupDays = 90;

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
        Environment.UpdateFromEEnv(EmotionInertia.CurrentEEnv, NaturalRhythm.State);
        PropagateEnvironmentToLocations();

        _saveData.currentEEnv    = EmotionInertia.CurrentEEnv;
        _saveData.emotionHistory = EmotionInertia.History;

        // 动物状态系统：内部状态演化 → 行为输出（在文本层之前，让其读到最新行为）
        _driveSystem.SetEnvironment(Environment.State, NaturalRhythm.State);
        _driveSystem.Tick(_saveData.gameTime);

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
    public void WorldTick(int deltaDays, bool isCatchUp = false)
    {
        if (deltaDays <= 0) return;

        int chronicleMark = _saveData.pendingChronicles.Count;   // Phase 3 摘要接缝
        int simulateFrom  = isCatchUp ? Mathf.Max(0, deltaDays - MaxSimulatedCatchupDays) : 0;

        for (int d = 0; d < deltaDays; d++)
        {
            AdvanceCalendar();                          // 始终推进 gameTime（= 墙钟天数）
            if (d >= simulateFrom) SimulatePass();      // 软上限跳过深层历史
        }

        if (isCatchUp)
        {
            // Phase 0：丢弃 catch-up 期间产生的逐日世界志噪音。
            // worldEvents（append-only 永久日志）原样保留。
            // Phase 3 HOOK：把下方丢弃替换为从同一区间聚合的「你离开的这些天…」摘要。
            int extra = _saveData.pendingChronicles.Count - chronicleMark;
            if (extra > 0)
                _saveData.pendingChronicles.RemoveRange(chronicleMark, extra);
        }
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
        Environment.UpdateFromEEnv(EmotionInertia.CurrentEEnv, NaturalRhythm.State);
        _ruleEngine = new NarrativeRuleEngine(Registry, _saveData);
        _ruleEngine.SetEnvironment(Environment.State, NaturalRhythm.State);
        _relationSystem = new EntityRelationSystem(Registry, _saveData);
        _relationSystem.SetEnvironment(Environment.State, NaturalRhythm.State);
        _driveSystem    = new AnimalDriveSystem(Registry, _saveData, _driveTuning);
        _driveSystem.SetEnvironment(Environment.State, NaturalRhythm.State);
        _narrator       = new BehaviorNarrator(Registry, _saveData);
        _narrator.SetEnvironment(Environment.State, NaturalRhythm.State);
    }

    // 将全局环境参数（Rainfall）传播到各地点实体的 waterLevel
    // 放在规则评估之前调用，让规则看到最新的地点状态
    private void PropagateEnvironmentToLocations()
    {
        float rain = Environment.State.Rainfall;
        foreach (var loc in _saveData.locations)
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
            // 固定每日排水 0.03，降雨按各地积水率蓄水
            float delta = rain * accRate - 0.03f;
            loc.waterLevel = Mathf.Clamp01(loc.waterLevel + delta);
        }
    }

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
