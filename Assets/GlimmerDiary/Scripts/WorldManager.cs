using UnityEngine;
using GlimmerDiary.Core;
using GlimmerDiary.Data;

// 在场景里新建一个空 GameObject，命名为 "WorldManager"
// 把这个脚本拖到那个 GameObject 上
public class WorldManager : MonoBehaviour
{
    public static WorldManager Instance { get; private set; }

    public EmotionInertiaSystem EmotionInertia { get; private set; }
    public NaturalRhythmSystem NaturalRhythm { get; private set; }
    public WorldEnvironmentSystem Environment { get; private set; }

    void Awake()
    {
        // 单例，跨场景不销毁
        if (Instance != null) { Destroy(gameObject); return; }
        Instance = this;
        DontDestroyOnLoad(gameObject);

        EmotionInertia = new EmotionInertiaSystem();
        NaturalRhythm = new NaturalRhythmSystem();
        Environment = new WorldEnvironmentSystem();
    }

    void Start()
    {
        // 游戏启动时，用当前 E_env 刷新一次环境
        Environment.UpdateFromEEnv(EmotionInertia.CurrentEEnv,
                                    NaturalRhythm.State);
    }

    // 玩家提交日记时调用这个方法（由 UI 层调用，不由视觉层调用）
    public void OnJournalSubmitted(EmotionVector emotion)
    {
        EmotionInertia.Update(emotion);
        NaturalRhythm.Tick();
        Environment.UpdateFromEEnv(EmotionInertia.CurrentEEnv,
                                    NaturalRhythm.State);

        // TODO: 通知叙事规则层检查触发条件（Week 4）
        // TODO: 通知视觉层播放仪式时刻（视觉阶段）
    }

    // 供视觉层随时读取当前世界状态（只读，视觉层不写入）
    public WorldEnvironmentState GetWorldState() => Environment.State;
    public NaturalRhythmState GetRhythmState() => NaturalRhythm.State;
}
