using System;
using System.Collections.Generic;

namespace GlimmerDiary.Data
{
    // ─────────────────────────────────────────────────────────────
    // 动物内部状态向量（扁平 union）
    //
    // 为什么扁平：存档走 JsonUtility，不支持多态 / [SerializeReference]。
    // 5 个物种字段总量很小，合并到一个 [Serializable] 类最稳、可单测。
    // 每个物种只读写属于自己的字段（见 AnimalStateSystem.md §5）。
    // 新增维度 = 这里加一个字段；文本层只读 BehaviorOutput，零改动。
    // ─────────────────────────────────────────────────────────────
    [Serializable]
    public class AnimalInternalState
    {
        public bool initialized;          // 旧存档惰性初始化标记

        // —— fox 狐狸 ——
        public float hunger;              // 0-1 随时间增长
        public float safety;              // 0-1 候鸟在场/水位异常/高唤醒 → 降
        public float territoryStability;  // 0-1 田鼠位置变化 → 降

        // —— vole 田鼠 ——
        public float shelterSecurity;     // 0-1 = 1 - home.waterLevel
        public float foodStock;           // 0-1 随时间消耗
        public float expansionPressure;   // 0-1 食物低/邻区腾空 → 升

        // —— deer_mouse 鹿鼠 ——（activityRange 复用 AnimalEntity 现有字段）
        public float anxiety;             // 0-1 织巢鸟不在/确定性低/狐狸临近 → 升

        // —— migratory_bird 候鸟 ——
        public float migrationUrge;       // 0-1 月份驱动 + 长期负效价加速
        public float settlementComfort;   // 0-1 河岸水位适中 → 高，狐狸常来 → 降

        // —— 通用：现任行为加成 ——
        public string lastDrive = "";

        public AnimalInternalState Clone()
        {
            return new AnimalInternalState
            {
                initialized        = initialized,
                hunger             = hunger,
                safety             = safety,
                territoryStability = territoryStability,
                shelterSecurity    = shelterSecurity,
                foodStock          = foodStock,
                expansionPressure  = expansionPressure,
                anxiety            = anxiety,
                migrationUrge      = migrationUrge,
                settlementComfort  = settlementComfort,
                lastDrive          = lastDrive
            };
        }
    }

    // ─────────────────────────────────────────────────────────────
    // 成因标签（文本层按此查表选"最相关环境细节"，见 §6）
    // 用字符串常量而非 enum：可扩展、JsonUtility 友好、跨物种共享
    // 选取偏置：优先跨实体项（FoxNearby / BirdAbsent / DeerMouseWithdrew / RodentExpansion）
    // ─────────────────────────────────────────────────────────────
    public static class CauseFactor
    {
        public const string None             = "None";
        public const string Hunger           = "Hunger";
        public const string WaterRising      = "WaterRising";
        public const string LowVitality      = "LowVitality";
        public const string SeasonShift      = "SeasonShift";
        public const string EmotionBleak     = "EmotionBleak";
        public const string WeatherHarsh     = "WeatherHarsh";   // 雨/雾压低了活动（§5.1 语料提醒）
        // 跨实体项（优先）
        public const string FoxNearby        = "FoxNearby";
        public const string BirdAbsent       = "BirdAbsent";
        public const string DeerMouseWithdrew= "DeerMouseWithdrew";
        public const string RodentExpansion  = "RodentExpansion";
    }

    // ─────────────────────────────────────────────────────────────
    // 行为输出：文本层唯一读取的接口（不读内部状态数值）
    // ─────────────────────────────────────────────────────────────
    [Serializable]
    public class BehaviorOutput
    {
        public string drive         = "";      // 物种 *Drive 枚举名
        public string cause         = "None";  // CauseFactor 枚举名
        public string causeTargetId = "";      // 成因指向实体/zone，如 "vole" / "riverbank"
        public float  intensity;               // 主导压力值 0-1，措辞强弱用
        public string zone          = "";      // 当前 location 副本，文本层一站读取
    }

    // ─────────────────────────────────────────────────────────────
    // 猴面包树内部状态（仅 baobab 使用）
    // ─────────────────────────────────────────────────────────────
    [Serializable]
    public class BranchState
    {
        public string id;                 // "E-1" / "W-2"
        public string dir;                // 朝向 "E/W/N/S"
        public float  integrity = 1f;     // 0-1，归零 → 断枝事件
    }

    [Serializable]
    public class PlantInternalState
    {
        public bool  initialized;
        public float vitality;                       // E_env.V 长期趋势的积分
        public List<BranchState> branches = new();   // 每根主枝独立 integrity
        public float floweringReadiness;             // 开花后归零
        public int   ticksSinceBranchBreak;          // 织巢鸟归巢判据
        public int   knownDamageCount;               // 已知永久损伤数（边沿检测断枝事件）
    }

    // ─────────────────────────────────────────────────────────────
    // 世界事件（离散跃迁，append-only；命名遵循 CLAUDE.md 过去时约定）
    // ─────────────────────────────────────────────────────────────
    public static class WorldEventType
    {
        public const string TreeBranchBroke     = "TreeBranchBroke";
        public const string TreeFlowered        = "TreeFlowered";
        public const string AnimalArrived        = "AnimalArrived";
        public const string AnimalDeparted       = "AnimalDeparted";
        public const string WeaverBirdDeparted   = "WeaverBirdDeparted";
        public const string WeaverBirdReturned   = "WeaverBirdReturned";
        public const string VoleClaimedZone      = "VoleClaimedZone";
        public const string QuietConvergence     = "QuietConvergence";  // 涌现时刻：宿敌/邻里罕见地挨着歇息
        public const string DandelionSeedsDrifted = "DandelionSeedsDrifted";  // 风峰日落种：targetId=下风 zone
    }

    [Serializable]
    public class WorldEvent
    {
        public string type;       // EventType.* 过去时
        public string sourceId;
        public string targetId;
        public string gameDate;
        public string payload;    // 如断枝方向 "E-2"
    }
}
