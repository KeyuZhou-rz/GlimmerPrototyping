using System;
using System.Collections.Generic;

namespace GlimmerDiary.Data
{
    // ─────────────────────────────────────────
    // 通用：状态变更记录
    // 每次任何实体发生状态转变，追加一条
    // ─────────────────────────────────────────
    [Serializable]
    public class StateChangeRecord
    {
        public string date;          // 游戏内日期 "Y1-M3-D12"
        public string field;         // 哪个字段变了，如 "location"
        public string fromValue;     // 变更前
        public string toValue;       // 变更后
        public string triggeredBy;   // 触发事件ID，如 "flood_level_high"
        // 记忆双读（V1 D6）：诞生时玩家是否在场（WorldManager 在非 catch-up 拍后统一打戳）。
        // 旧档默认 false=考古语气，语义恰好正确。
        public bool   witnessed;
    }

    // ─────────────────────────────────────────
    // 动物实体
    // ─────────────────────────────────────────
    [Serializable]
    public class AnimalEntity
    {
        public string speciesId;         // "vole" / "fox" / "migratory_bird"
        public bool   isPresent;         // 当前是否在世界中存在
        public string location;          // 当前位置 ID
        public string facingDirection;   // 洞口/活动朝向 "N/S/E/W"
        public string familyState;       // "family" / "solitary" / "unknown"
        public string lastSeenDate;      // 上次出现的游戏内日期
        public string primaryPath;       // 有路径的动物（如狐狸）：主路径 ID
        public float  activityRange = 1.0f; // 0=完全退缩 1=正常活动范围（鹿鼠等使用）

        // 动物状态系统（Layer 2）——内部状态向量 + 行为输出
        // 文本层只读 behavior，不读 internalState（见 Docs/AnimalStateSystem.md）
        public AnimalInternalState internalState;
        public BehaviorOutput      behavior;

        public List<StateChangeRecord> history = new();
    }

    // ─────────────────────────────────────────
    // 植物实体
    // ─────────────────────────────────────────
    [Serializable]
    public class PlantEntity
    {
        public string plantId;           // "baobab_main" / "dandelion_patch_A"
        public string speciesId;         // "baobab" / "dandelion" / "grass"
        public bool   isAlive;
        public string location;          // 所在区域 ID
        public float  growthStage;       // 0.0 ~ 1.0
        public bool   isFlowering;
        public string lastFlowerDate;

        // 植物内部状态（仅 baobab 使用：vitality / branches / floweringReadiness）
        public PlantInternalState internalState;

        // 永久损伤记录（不可逆），每条记录一次折断/枯死事件
        public List<PermanentDamageRecord> permanentDamages = new();
        public List<StateChangeRecord>     history          = new();
    }

    [Serializable]
    public class PermanentDamageRecord
    {
        public string date;
        public string damageType;    // "branch_broken" / "partial_death"
        public string description;   // "东侧第二主枝，断口朝下"
        public string triggeredBy;
    }

    // ─────────────────────────────────────────
    // 地点实体
    // ─────────────────────────────────────────
    [Serializable]
    public class LocationEntity
    {
        public string locationId;        // "lowland" / "riverbank" / "stone_area"
        public string displayName;       // "低洼地" / "河岸" / "石头区"
        public float  waterLevel;        // 0.0 ~ 1.0
        public float  soilMoisture;
        // 历史湿度极值（2026-07-28 C1 拍板）：T1 水毁锁存判"曾经湿到过"而非"现在还湿"——
        // 否则湿度在两次游玩间隙回落，泡透塌掉的土堆会静默"复原"，违反不可逆原则③。
        // 只增不减（水位回落也不降）；写者同 soilMoisture（Propagate/EntityStateHelper/初始化）。
        public float  soilMoisturePeak;
        public float  vegetationDensity;

        // 永久地貌变化（石头裂开、旧洞口废弃等）
        public List<PermanentTerrainRecord> permanentChanges = new();
        public List<StateChangeRecord>      history          = new();
    }

    [Serializable]
    public class PermanentTerrainRecord
    {
        public string date;
        public string changeType;    // "rock_split" / "burrow_abandoned"
        public string description;   // "东侧大石裂成两半，裂缝朝北"
        public string triggeredBy;
        // 记忆双读（V1 D6）：玩家是否见证过它的诞生（在线发生 ∨ 被已读信件点名）。
        // 旧档默认 false=考古语气，语义恰好正确（预演期/错过的章节就是没见证过）。
        public bool   witnessed;
    }

    // ─────────────────────────────────────────
    // 田鼠镇小径（V1 D3）：土堆成气候后踩出的路
    // 不挂 LocationEntity——它不是地貌的疤，是"还活着的惯例"：
    // 镇在时每日重现，镇散（lapsed）后按 FadeDays 淡出消失。
    // moundKeys 引用源土堆记录（TraceKeyUtil 口径），位置由 L3 从重算——
    // 记录不存坐标，坐标是派生品。
    // ─────────────────────────────────────────
    [Serializable]
    public class VoleTrailRecord
    {
        // lapsed 后痕迹层的淡出窗口（游戏日）。放 Data 层：binder（L3）与
        // VoleTownSystem（L2）都要读，binder 纪律不导入 Core。
        public const int FadeDays = 20;

        public string formedDateKey;          // ToKeyString
        public string zone;                   // 成形主场（当前恒 "center"，扩张土堆地带）
        public List<string> moundKeys = new();// 连成小径的土堆记录键（成形时快照，按出生日排序）
        public bool   lapsed;                 // 镇散 = 小径停止重现
        public string lapseDateKey;           // 冻结日（淡出起点）
        public int    belowThresholdDays;     // 活跃土堆跌破阈值的连续天数（lapse 判据之一）
        public bool   witnessed;              // 记忆双读（V1 D6）：成形时玩家是否在场
    }

    // ─────────────────────────────────────────
    // 新生地层（V1 D5）：持久型痕迹的终点不再是删除，而是沉降。
    //
    // 入土即建档（本记录），此后深度逐日积分（StratumSystem 唯一写者）：
    //   遗存（relic）  —— 塌矮、色沉、微陷，仍可读（"这是去年的东西了"）
    //   地层（stratum）—— 沉入地下，几乎不可读，直到出露（exposed）
    // 深度本身即记录：埋得深 = 那几年风大/雨多/草盛（速率读 WindSpeed/Rainfall/草密度）。
    // 预算：每区地层对象上限 StratumSystem.MaxPerZone，超限最老的加深降分辨率，记录永不删。
    // ─────────────────────────────────────────
    [Serializable]
    public class StratumRecord
    {
        public string sourceKey;      // 源痕迹键（mound|… / vtrail|… / collapse|… / deeprelic|…，TraceKeyUtil 口径）
        public string kind;           // "mound" / "vtrail" / "collapse" / "deeprelic"（V1 D9 深层遗物）
        public string relicKind;      // 仅 deeprelic："painting" / "stone_circle" / "tool_scatter" / "quern"（旧档 null，兼容）
        public string zone;           // 所在区（lowland/center/…）
        public string buriedDateKey;  // 入土日（ToKeyString）
        public int    chapterOrdinal; // 封闭层断代：入土时已发生的 ChapterTurned 次数
        public string chapterAtBurial;// 入土时所在章节（EraSystem 章节 id，断代语料用）
        public float  depth;          // 当前埋藏深度（积分值，只增不减）
        public bool   exposed;        // 出露（风暴剥蚀/田鼠翻土）——重见天日
        public string exposedDateKey; // 出露日
        public string exposedBy;      // "storm" / "vole_dig"
        public bool   witnessed;      // 记忆双读：入土时从源记录继承

        // 遗存可读深度上限：depth 超过即沉入地层（几乎不可读，直到出露）。
        // 放 Data 层：binder（L3）按它选视觉档，纪律不导入 Core。
        public const float RelicMaxDepth = 1.0f;
        // 每区地层对象渲染上限（预算阀）：超限最老的继续加深、降分辨率，记录永不删。
        public const int   MaxPerZone    = 8;
    }
}
