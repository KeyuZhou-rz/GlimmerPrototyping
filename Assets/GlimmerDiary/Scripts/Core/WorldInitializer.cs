using System.Collections.Generic;
using GlimmerDiary.Data;

namespace GlimmerDiary.Core
{
    public static class WorldInitializer
    {
        public static WorldSaveData CreateNewWorld()
        {
            return new WorldSaveData
            {
                gameTime          = new GameDateTime { year = 1, month = 9, day = 1 },
                currentEEnv       = EmotionVector.Neutral(),
                emotionHistory    = new List<EnvEmotionSnapshot>(),
                animals           = CreateAnimals(),
                plants            = CreatePlants(),
                locations         = CreateLocations(),
                pendingChronicles = new List<WorldChronicleEntry>(),
                shownChronicles   = new List<WorldChronicleEntry>()
            };
        }

        // ── 动物 ──────────────────────────────────────
        private static List<AnimalEntity> CreateAnimals()
        {
            return new List<AnimalEntity>
            {
                new AnimalEntity
                {
                    speciesId       = "vole",
                    isPresent       = true,
                    location        = "lowland",
                    facingDirection = "S",
                    familyState     = "family",
                    lastSeenDate    = "Y1-M9-D1",
                    primaryPath     = "",
                    history         = new List<StateChangeRecord>()
                },
                new AnimalEntity
                {
                    speciesId       = "fox",
                    isPresent       = true,
                    location        = "highland_east",
                    facingDirection = "W",
                    familyState     = "solitary",
                    lastSeenDate    = "Y1-M9-D1",
                    primaryPath     = "path_east_to_river",
                    history         = new List<StateChangeRecord>()
                },
                // 候鸟：初始不在场，秋季迁来
                new AnimalEntity
                {
                    speciesId       = "migratory_bird",
                    isPresent       = false,
                    location        = "",
                    facingDirection = "",
                    familyState     = "unknown",
                    lastSeenDate    = "",
                    primaryPath     = "",
                    history         = new List<StateChangeRecord>()
                },
                // 织巢鸟：住在猴面包树枝上，猴面包树断枝后离开
                new AnimalEntity
                {
                    speciesId       = "weaver_bird",
                    isPresent       = true,
                    location        = "center",
                    facingDirection = "N",
                    familyState     = "pair",
                    lastSeenDate    = "Y1-M9-D1",
                    primaryPath     = "",
                    activityRange   = 1.0f,
                    history         = new List<StateChangeRecord>()
                },
                // 鹿鼠：住在猴面包树根附近，靠织巢鸟的叫声判断安全
                new AnimalEntity
                {
                    speciesId       = "deer_mouse",
                    isPresent       = true,
                    location        = "highland_east",
                    facingDirection = "W",
                    familyState     = "solitary",
                    lastSeenDate    = "Y1-M9-D1",
                    primaryPath     = "",
                    activityRange   = 0.8f,  // 初始活动范围正常但略保守
                    history         = new List<StateChangeRecord>()
                }
            };
        }

        // ── 植物 ──────────────────────────────────────
        private static List<PlantEntity> CreatePlants()
        {
            return new List<PlantEntity>
            {
                new PlantEntity
                {
                    // 猴面包树：世界核心地标，growthStage 0.6 = 已成树但未封顶
                    plantId          = "baobab_main",
                    speciesId        = "baobab",
                    isAlive          = true,
                    location         = "center",
                    growthStage      = 0.6f,
                    isFlowering      = false,
                    lastFlowerDate   = "",
                    permanentDamages = new List<PermanentDamageRecord>(),
                    history          = new List<StateChangeRecord>()
                },
                new PlantEntity
                {
                    // 蒲公英群落：河岸附近，种子事件的主要来源
                    plantId          = "dandelion_riverbank",
                    speciesId        = "dandelion",
                    isAlive          = true,
                    location         = "riverbank",
                    growthStage      = 0.8f,
                    isFlowering      = true,
                    lastFlowerDate   = "Y1-M8-D20",
                    permanentDamages = new List<PermanentDamageRecord>(),
                    history          = new List<StateChangeRecord>()
                },
                new PlantEntity
                {
                    // 石头区边缘杂草：低密度，可被情绪历史扩展或收缩
                    plantId          = "grass_stone_edge",
                    speciesId        = "grass",
                    isAlive          = true,
                    location         = "stone_area",
                    growthStage      = 0.4f,
                    isFlowering      = false,
                    lastFlowerDate   = "",
                    permanentDamages = new List<PermanentDamageRecord>(),
                    history          = new List<StateChangeRecord>()
                }
            };
        }

        // ── 地点 ──────────────────────────────────────
        // 空间关系：[河岸]─[低洼地]─[草原中央]─[东侧高地]
        //                               │
        //                           [石头区]
        private static List<LocationEntity> CreateLocations()
        {
            var locations = new List<LocationEntity>
            {
                new LocationEntity
                {
                    locationId        = "center",
                    displayName       = "草原中央",
                    waterLevel        = 0.3f,
                    soilMoisture      = 0.5f,
                    vegetationDensity = 0.6f,
                    permanentChanges  = new List<PermanentTerrainRecord>(),
                    history           = new List<StateChangeRecord>()
                },
                new LocationEntity
                {
                    locationId        = "lowland",
                    displayName       = "低洼地",
                    waterLevel        = 0.4f,   // 正常水位，田鼠安全
                    soilMoisture      = 0.65f,
                    vegetationDensity = 0.7f,
                    permanentChanges  = new List<PermanentTerrainRecord>(),
                    history           = new List<StateChangeRecord>()
                },
                new LocationEntity
                {
                    locationId        = "riverbank",
                    displayName       = "河岸",
                    waterLevel        = 0.5f,
                    soilMoisture      = 0.8f,
                    vegetationDensity = 0.75f,
                    permanentChanges  = new List<PermanentTerrainRecord>(),
                    history           = new List<StateChangeRecord>()
                },
                new LocationEntity
                {
                    locationId        = "highland_east",
                    displayName       = "东侧高地",
                    waterLevel        = 0.1f,
                    soilMoisture      = 0.35f,
                    vegetationDensity = 0.45f,
                    permanentChanges  = new List<PermanentTerrainRecord>(),
                    history           = new List<StateChangeRecord>()
                },
                new LocationEntity
                {
                    locationId        = "stone_area",
                    displayName       = "石头区",
                    waterLevel        = 0.05f,
                    soilMoisture      = 0.2f,
                    vegetationDensity = 0.25f,
                    permanentChanges  = new List<PermanentTerrainRecord>(),
                    history           = new List<StateChangeRecord>()
                }
            };
            // 湿度极值锚定初始湿度（T1 水毁锁存："曾经湿到过"从世界出生当天记起）
            foreach (var l in locations) l.soilMoisturePeak = l.soilMoisture;
            return locations;
        }
    }
}
