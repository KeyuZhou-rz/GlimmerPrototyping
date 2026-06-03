using UnityEngine;

namespace GlimmerDiary.Data
{
    // 动物状态系统的可调参数（P5：从 AnimalDriveSystem 的硬编码常量外提）
    //
    // 资产建议放在 Assets/Resources/Tuning/AnimalDriveTuning.asset，
    // 由 WorldManager 在加载时 Resources.Load 注入 AnimalDriveSystem。
    // 缺省时（无资产 / 测试直接 new），系统回退到本类字段初始值，与原常量一致。
    //
    // 字段含义见 Docs/AnimalStateSystem.md §5 / 附录 A。速率单位均为"每 tick（每游戏日）"。
    [CreateAssetMenu(menuName = "GlimmerDiary/Animal Drive Tuning", fileName = "AnimalDriveTuning")]
    public class AnimalDriveTuning : ScriptableObject
    {
        [Header("通用：行为仲裁")]
        [Range(0f, 1f)] public float baseDrive      = 0.20f;  // 默认行为基线 urgency
        [Range(0f, 1f)] public float incumbentBonus = 0.10f;  // 现任行为加成（抗抖动）
        [Range(0.01f, 0.5f)] public float softBand   = 0.10f; // 软阈值过渡半带宽

        [Header("鹿鼠 deer_mouse")]
        [Range(0f, 0.3f)] public float dmAnxNoBird     = 0.08f; // 织巢鸟不在 → anxiety/d
        [Range(0f, 0.3f)] public float dmAnxCalmBird    = 0.06f; // 织巢鸟在场 → anxiety/d 下降
        [Range(0f, 0.6f)] public float dmFoxSpike        = 0.25f; // 狐狸临近 → anxiety 突增
        [Range(0f, 1f)]   public float dmRangeLerp        = 0.30f; // activityRange 向(1-anx)逼近率
        [Range(0f, 0.3f)] public float dmRetreatRelief    = 0.05f; // Retreat 负反馈

        [Header("田鼠 vole")]
        [Range(0f, 0.3f)] public float voleFoodDecay    = 0.05f; // foodStock/d 消耗
        [Range(0f, 0.3f)] public float voleFoodRegen     = 0.04f; // 按植被补充系数
        [Range(0f, 1f)]   public float voleForageRelief   = 0.30f; // Forage 回补
        [Range(0f, 1f)]   public float voleExpandRelief   = 0.40f; // Expand 释放扩张压力
        [Range(0f, 1f)]   public float voleExpandFood     = 0.20f; // Expand 新领地食物
        [Range(0f, 1f)]   public float voleDmRangeFree    = 0.60f; // 鹿鼠 range 低于此 → center 腾空

        [Header("狐狸 fox")]
        [Range(0f, 0.3f)] public float foxHungerGain    = 0.06f; // hunger/d
        [Range(0f, 1f)]   public float foxForageRelief    = 0.40f; // 觅食回补（× 植被系数）
        [Range(0f, 0.2f)] public float foxSafetyRecover   = 0.03f; // 平静 → safety/d 回升
        [Range(0f, 0.2f)] public float foxTerrRecover     = 0.02f; // 长期稳定 → territory/d 回升
        [Range(0f, 0.5f)] public float foxPatrolReassert  = 0.15f; // Patrol 重新宣示
        [Range(0f, 0.5f)] public float foxTerrEncroach    = 0.15f; // 田鼠入侵 → territory/d 下降

        [Header("候鸟 migratory_bird")]
        [Range(0f, 0.2f)] public float birdUrgeSeason   = 0.05f; // 迁徙季 → urge/d
        [Range(0f, 0.2f)] public float birdUrgeOff       = 0.02f; // 非季 → urge/d 回落
        [Range(0f, 0.2f)] public float birdUrgeBleak     = 0.03f; // 长期负效价 → urge/d 加速
        [Range(0f, 1f)]   public float birdComfortLerp    = 0.20f; // settlementComfort 逼近率
        [Range(0f, 0.5f)] public float birdFoxDiscomfort  = 0.10f; // 狐狸在河岸 → comfort 下降

        [Header("猴面包树 baobab")]
        [Range(0f, 0.2f)] public float treeVitalityAlpha = 0.03f; // vitality 积分率
        [Range(0f, 0.3f)] public float treeFlowerGain     = 0.05f; // 花期 → readiness/d
        [Range(1, 120)]   public int   weaverReturnTicks   = 30;    // 织巢鸟归巢所需无断枝 tick
        [Range(0f, 0.05f)] public float insectVegDecay     = 0.003f; // 织巢鸟不在 → 东侧植被/d 衰减
    }
}
