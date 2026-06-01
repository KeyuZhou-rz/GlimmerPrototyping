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
    public WorldSaveData          WorldSave      { get; private set; }
    public EntityRegistry         Registry       { get; private set; }

    void Awake()
    {
        if (Instance != null) { Destroy(gameObject); return; }
        Instance = this;
        DontDestroyOnLoad(gameObject);

        EmotionInertia = new EmotionInertiaSystem();
        NaturalRhythm  = new NaturalRhythmSystem();
        Environment    = new WorldEnvironmentSystem();

        // 加载存档（无存档则新建空世界）
        WorldSave = SaveSystem.LoadWorldState() ?? new WorldSaveData();
        EmotionInertia.Restore(WorldSave.currentEEnv, WorldSave.emotionHistory);

        Registry = new EntityRegistry();
        Registry.Initialize(WorldSave);

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
        NaturalRhythm.Tick();
        Environment.UpdateFromEEnv(EmotionInertia.CurrentEEnv, NaturalRhythm.State);

        // 同步情感状态到存档根节点
        WorldSave.currentEEnv    = EmotionInertia.CurrentEEnv;
        WorldSave.emotionHistory = EmotionInertia.History;

        // 持久化：世界状态覆盖写，日记只追加
        SaveSystem.SaveWorldState(WorldSave);
        SaveSystem.AppendJournalEntry(entry);

        // TODO: 通知叙事规则层检查触发条件（Week 4）
        // TODO: 通知视觉层播放仪式时刻（视觉阶段）
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
        foreach (var a in save.animals)   _animals[a.speciesId]    = a;
        foreach (var p in save.plants)    _plants[p.plantId]       = p;
        foreach (var l in save.locations) _locations[l.locationId] = l;
    }

    public AnimalEntity   GetAnimal  (string id) => _animals.GetValueOrDefault(id);
    public PlantEntity    GetPlant   (string id) => _plants.GetValueOrDefault(id);
    public LocationEntity GetLocation(string id) => _locations.GetValueOrDefault(id);
}
