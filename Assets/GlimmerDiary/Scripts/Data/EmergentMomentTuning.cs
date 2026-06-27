using UnityEngine;

namespace GlimmerDiary.Data
{
    // 涌现时刻（QuietConvergence）的可调参数
    //
    // 资产建议放在 Assets/Resources/Tuning/EmergentMomentTuning.asset，
    // 由 WorldManager 在加载时 Resources.Load 注入 EmergentMomentDetector。
    // 缺省时（无资产 / 测试直接 new）→ ScriptableObject.CreateInstance 取字段默认值。
    //
    // 与 AnimalDriveTuning 分开：检测器是只读旁路系统，不属于驱动系统的调参面。
    // 质量全在调参（概率 / 阈值 / 冷却）——外提以免每次调手感都重编译。
    [CreateAssetMenu(menuName = "GlimmerDiary/Emergent Moment Tuning", fileName = "EmergentMomentTuning")]
    public class EmergentMomentTuning : ScriptableObject
    {
        [Header("触发前提（WouldFire 谓词，无随机）")]
        [Range(0f, 1f)] public float calmA        = 0.35f;  // E_env.A 低于此 = 平静
        [Range(0f, 1f)] public float warmVitality = 0.65f;  // 猴面包树 vitality 高于此 = 暖沉积

        [Header("稀有性")]
        [Range(1, 360)] public int cooldownDays = 90;       // 两次涌现时刻最小间隔（游戏日）

        [Header("概率门（是概率不是保证）")]
        [Range(0f, 1f)] public float baseP   = 0.12f;       // 平日触发概率
        [Range(0f, 1f)] public float winterP = 0.30f;       // 最长夜（冬季）触发概率
    }
}
