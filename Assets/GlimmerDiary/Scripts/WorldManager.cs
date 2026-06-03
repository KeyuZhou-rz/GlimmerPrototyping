using System;
using System.Collections.Generic;
using UnityEngine;
using GlimmerDiary.Core;
using GlimmerDiary.Data;
using GlimmerDiary.Utils;

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
        _driveSystem    = new AnimalDriveSystem(Registry, _saveData);
        _narrator       = new BehaviorNarrator(Registry, _saveData);
        Debug.Log($"[WorldManager] Rules={_allRules.Count}  Relations={_allRelations.Count} (retired {RetiredRelationIds.Count})");
        Debug.Log($"[WorldManager] SaveDir: {SaveSystem.GetSaveDir()}");
    }

    void Start()
    {
        Environment.UpdateFromEEnv(EmotionInertia.CurrentEEnv, NaturalRhythm.State);
    }

    // 玩家提交日记时的唯一入口（由 UI 层调用）
    public void OnJournalSubmitted(JournalEntry entry)
    {
        EmotionInertia.Update(entry.emotion);
        _saveData.gameTime.Advance(1);
        NaturalRhythm.Tick();
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

        SaveSystem.SaveWorldState(_saveData);
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
        Environment.UpdateFromEEnv(EmotionInertia.CurrentEEnv, NaturalRhythm.State);
        _ruleEngine = new NarrativeRuleEngine(Registry, _saveData);
        _ruleEngine.SetEnvironment(Environment.State, NaturalRhythm.State);
        _relationSystem = new EntityRelationSystem(Registry, _saveData);
        _relationSystem.SetEnvironment(Environment.State, NaturalRhythm.State);
        _driveSystem    = new AnimalDriveSystem(Registry, _saveData);
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
