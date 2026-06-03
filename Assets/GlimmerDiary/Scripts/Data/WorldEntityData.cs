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
    }
}
