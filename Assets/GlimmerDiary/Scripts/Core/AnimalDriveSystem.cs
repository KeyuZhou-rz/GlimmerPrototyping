using System.Collections.Generic;
using UnityEngine;
using GlimmerDiary.Data;
using GlimmerDiary.Utils;
using System;

namespace GlimmerDiary.Core
{
    // 动物状态系统 —— 内部状态向量 → 行为输出（Layer 2 / WorldSimulator）
    //
    // 设计蓝本：Docs/AnimalStateSystem.md
    //
    // 每 tick（= 一次日记 = 一游戏日）：
    //   1. 对所有动物互读字段 + 内部状态做快照（双缓冲）
    //   2. 每个实体只读快照，计算 next 内部状态 + 主导行为（urgency argmax + 现任加成）
    //   3. 写回行为输出字段（离散变更经 EntityStateHelper 记 history），边沿检测发事件
    //
    // 铁律：Step* / Resolve* 只读 snapshot，绝不读他者本 tick 新值，保证顺序无关；
    //       因果链每 tick 推进一级，天然带 1 日相位滞后。
    //
    // 职责边界（见 Docs §1.2）：
    //   本系统接管"实体 → 实体"连续耦合（替代 4 条 EntityRelation）。
    //   "情绪/环境 → 离散事件"仍由 NarrativeRuleSO 拥有（断枝、候鸟迁来）；
    //   本系统只对其结果做反应（如 permanentDamages 增长 → 织巢鸟离场），绝不竞争。
    public class AnimalDriveSystem
    {
        // ── 可调参数（P5：外提为 AnimalDriveTuning SO；缺省回退默认值）──
        // 名称沿用原常量，构造时从 tuning 注入，使用点零改动。
        private readonly float BASE_DRIVE, INCUMBENT_BONUS, SOFT_BAND;
        private readonly float DM_ANX_NO_BIRD, DM_ANX_CALM_BIRD, DM_FOX_SPIKE, DM_RANGE_LERP, DM_RETREAT_RELIEF;
        private readonly float VOLE_FOOD_DECAY, VOLE_FOOD_REGEN, VOLE_FORAGE_RELIEF, VOLE_EXPAND_RELIEF, VOLE_EXPAND_FOOD, VOLE_DM_RANGE_FREE;
        private readonly float FOX_HUNGER_GAIN, FOX_FORAGE_RELIEF, FOX_SAFETY_RECOVER, FOX_TERR_RECOVER, FOX_PATROL_REASSERT, FOX_TERR_ENCROACH;
        private readonly float BIRD_URGE_SEASON, BIRD_URGE_OFF, BIRD_URGE_BLEAK, BIRD_COMFORT_LERP, BIRD_FOX_DISCOMFORT;
        private readonly float TREE_VITALITY_ALPHA, TREE_FLOWER_GAIN, TREE_RAIN_VITALITY_BIAS;
        private readonly float WET_ACTIVITY_DAMP, FOG_ACTIVITY_DAMP, FOG_ACTIVITY_THRESHOLD, RAIN_HARSH_THRESHOLD;
        private readonly int   WEAVER_RETURN_TICKS;

        static readonly HashSet<string> FOX_TERRITORY = new() { "highland_east", "center" };

        private readonly EntityRegistry _registry;
        private readonly WorldSaveData  _save;
        private WorldEnvironmentState   _env;
        private NaturalRhythmState      _rhythm;

        public AnimalDriveSystem(EntityRegistry registry, WorldSaveData save, AnimalDriveTuning tuning = null)
        {
            _registry = registry;
            _save     = save;

            // 缺省（无资产 / 测试直接 new）→ CreateInstance 取字段默认值，与原常量一致
            var t = tuning != null ? tuning : ScriptableObject.CreateInstance<AnimalDriveTuning>();
            BASE_DRIVE = t.baseDrive; INCUMBENT_BONUS = t.incumbentBonus; SOFT_BAND = t.softBand;
            DM_ANX_NO_BIRD = t.dmAnxNoBird; DM_ANX_CALM_BIRD = t.dmAnxCalmBird; DM_FOX_SPIKE = t.dmFoxSpike;
            DM_RANGE_LERP = t.dmRangeLerp; DM_RETREAT_RELIEF = t.dmRetreatRelief;
            VOLE_FOOD_DECAY = t.voleFoodDecay; VOLE_FOOD_REGEN = t.voleFoodRegen; VOLE_FORAGE_RELIEF = t.voleForageRelief;
            VOLE_EXPAND_RELIEF = t.voleExpandRelief; VOLE_EXPAND_FOOD = t.voleExpandFood; VOLE_DM_RANGE_FREE = t.voleDmRangeFree;
            FOX_HUNGER_GAIN = t.foxHungerGain; FOX_FORAGE_RELIEF = t.foxForageRelief; FOX_SAFETY_RECOVER = t.foxSafetyRecover;
            FOX_TERR_RECOVER = t.foxTerrRecover; FOX_PATROL_REASSERT = t.foxPatrolReassert; FOX_TERR_ENCROACH = t.foxTerrEncroach;
            BIRD_URGE_SEASON = t.birdUrgeSeason; BIRD_URGE_OFF = t.birdUrgeOff; BIRD_URGE_BLEAK = t.birdUrgeBleak;
            BIRD_COMFORT_LERP = t.birdComfortLerp; BIRD_FOX_DISCOMFORT = t.birdFoxDiscomfort;
            TREE_VITALITY_ALPHA = t.treeVitalityAlpha; TREE_FLOWER_GAIN = t.treeFlowerGain;
            TREE_RAIN_VITALITY_BIAS = t.treeRainVitalityBias;
            WET_ACTIVITY_DAMP = t.wetActivityDamp; FOG_ACTIVITY_DAMP = t.fogActivityDamp;
            FOG_ACTIVITY_THRESHOLD = t.fogActivityThreshold; RAIN_HARSH_THRESHOLD = t.rainHarshThreshold;
            WEAVER_RETURN_TICKS = t.weaverReturnTicks;
        }

        public void SetEnvironment(WorldEnvironmentState env, NaturalRhythmState rhythm)
        {
            _env    = env;
            _rhythm = rhythm;
        }

        // 季节单一来源：优先读节律快照（每日 AdvanceCalendar 已从 gameTime 刷新），
        // 测试路径 SetEnvironment(null,null) 时回退到同一映射 GetSeason(gameTime.month)。
        // 不得在本系统内用 month 区间重算季节——那是已被此属性收敛掉的分叉副本。
        private Season CurrentSeason =>
            _rhythm != null ? _rhythm.season : NaturalRhythmSystem.GetSeason(_save.gameTime.month);

        private class Snap
        {
            public bool                isPresent;
            public string              location;
            public float               activityRange;
            public AnimalInternalState st;
        }

        public void Tick(GameDateTime time)
        {
            EnsureInitialized();

            // 1. 快照（双缓冲只读副本）
            var snap = new Dictionary<string, Snap>(_save.animals.Count);
            foreach (var a in _save.animals)
            {
                snap[a.speciesId] = new Snap
                {
                    isPresent     = a.isPresent,
                    location      = a.location,
                    activityRange = a.activityRange,
                    st            = a.internalState.Clone()
                };
            }

            // 2. 植物（猴面包树）先处理：vitality/开花 + 断枝边沿 → 织巢鸟离场
            TickTree(snap, time);

            // 3. 动物：从快照算 next + 行为，写回
            foreach (var a in _save.animals)
            {
                switch (a.speciesId)
                {
                    case "deer_mouse":     TickDeerMouse(a, snap, time);     break;
                    case "vole":           TickVole(a, snap, time);          break;
                    case "fox":            TickFox(a, snap, time);           break;
                    case "migratory_bird": TickMigratoryBird(a, snap, time); break;
                    case "weaver_bird":    TickWeaver(a, snap, time);        break;
                    default:               a.behavior.zone = a.location;     break;
                }
            }
        }

        // ── 鹿鼠：核心传导节点 ─────────────────────────────────────
        private void TickDeerMouse(AnimalEntity a, Dictionary<string, Snap> snap, GameDateTime time)
        {
            var cur = snap[a.speciesId].st; // 鹿鼠内部状态获取
            string myZone = snap[a.speciesId].location; //区域获取

            bool weaverPresent = snap.TryGetValue("weaver_bird", out var w) && w.isPresent;
            bool foxNear = snap.TryGetValue("fox", out var f) && f.isPresent &&
                           ZoneTopology.AreSameOrAdjacent(f.location, myZone);
            float certainty = _save.currentEEnv?.C ?? 0.5f;

            float baseline = Mathf.Lerp(0.15f, 0.55f, 1f - certainty);
            float anx = Mathf.Lerp(cur.anxiety, baseline, 0.10f);
            anx += weaverPresent ? -DM_ANX_CALM_BIRD : DM_ANX_NO_BIRD;
            if (foxNear) anx += DM_FOX_SPIKE;
            anx = Mathf.Clamp01(anx);

            float mod = ActivityModifier(myZone);   // 雨（经本地水位/湿度）+ 雾 → 活动度（§5.1）
            float range = Mathf.Clamp01(Mathf.Lerp(a.activityRange, (1f - anx) * mod, DM_RANGE_LERP));
            a.activityRange = range;

            string drive = Argmax(cur.lastDrive,
                ("Retreat", Smooth(anx, 0.6f)),
                ("Explore", Smooth(1f - anx, 0.7f) * 0.8f * mod),
                ("Routine", BASE_DRIVE),
                out float intensity);

            string cause = CauseFactor.None, causeTarget = "";
            if (drive == "Retreat")
            {
                if (foxNear)             { cause = CauseFactor.FoxNearby; causeTarget = "fox"; }
                else if (!weaverPresent) { cause = CauseFactor.BirdAbsent; causeTarget = "weaver_bird"; }
                else if (certainty < 0.4f) cause = CauseFactor.EmotionBleak;
                anx = Mathf.Clamp01(anx - DM_RETREAT_RELIEF);
            }

            cur.anxiety   = anx;
            cur.lastDrive = drive;
            a.internalState.anxiety   = anx;
            a.internalState.lastDrive = drive;
            WriteBehavior(a, drive, cause, causeTarget, intensity);
        }

        // ── 田鼠：水位威胁 / 食物 / 扩张机会 ───────────────────────
        private void TickVole(AnimalEntity a, Dictionary<string, Snap> snap, GameDateTime time)
        {
            var cur = snap[a.speciesId].st;
            string myZone = snap[a.speciesId].location;

            var home = _registry.GetLocation("lowland");
            float shelter = 1f - (home?.waterLevel ?? 0.4f);

            var hereLoc = _registry.GetLocation(myZone);
            float veg = hereLoc?.vegetationDensity ?? 0.5f;
            float food = cur.foodStock - VOLE_FOOD_DECAY + veg * VOLE_FOOD_REGEN;

            var tree = _registry.GetPlant("baobab_main");
            if (tree != null && tree.isFlowering && myZone == "center") food += 0.05f;
            food = Mathf.Clamp01(food);

            float dmRange = snap.TryGetValue("deer_mouse", out var dm) ? dm.activityRange : 1f;
            bool centerFree = dmRange < VOLE_DM_RANGE_FREE && myZone != "center";
            float exp = cur.expansionPressure * 0.98f;
            if (food < 0.4f) exp += 0.05f;
            if (centerFree)  exp += 0.06f;
            exp = Mathf.Clamp01(exp);

            // 注意：洪水迁移（lowland 水位高 → 田鼠迁往 highland_east）是"环境→实体"，
            // 由 NarrativeRule `vole_relocate_flood` 拥有，本系统不再用 Relocate 移动田鼠，
            // 否则会抢先改 vole.location、使该规则的前置条件 (location==lowland) 失效。
            // shelterSecurity 仍计入状态向量（供叙事/未来用），但不驱动移动。
            float mod = ActivityModifier(myZone);   // 雨（经本地水位/湿度）+ 雾 → 活动度（§5.1；巢穴痕迹另案）
            string drive = Argmax(cur.lastDrive,
                ("Expand",   centerFree ? Smooth(exp, 0.6f) * mod : 0f),
                ("Forage",   Smooth(1f - food, 0.6f) * mod),
                ("Burrow",   BASE_DRIVE),
                out float intensity);

            string cause = CauseFactor.None, causeTarget = "";
            switch (drive)
            {
                case "Forage":
                    food = Mathf.Clamp01(food + VOLE_FORAGE_RELIEF);
                    cause = CauseFactor.Hunger;
                    break;

                case "Expand":
                    MoveAnimal(a, "center", "vole_expansion", time);
                    Emit(WorldEventType.VoleClaimedZone, "vole", "center", "E", time);
                    exp  = Mathf.Clamp01(exp  - VOLE_EXPAND_RELIEF);
                    food = Mathf.Clamp01(food + VOLE_EXPAND_FOOD);
                    cause = CauseFactor.DeerMouseWithdrew; causeTarget = "deer_mouse";
                    break;
            }

            cur.shelterSecurity   = shelter;
            cur.foodStock         = food;
            cur.expansionPressure = exp;
            cur.lastDrive         = drive;
            a.internalState.shelterSecurity   = shelter;
            a.internalState.foodStock         = food;
            a.internalState.expansionPressure = exp;
            a.internalState.lastDrive         = drive;
            WriteBehavior(a, drive, cause, causeTarget, intensity);
        }

        // ── 狐狸：饥饿 / 安全 / 领地稳定 ───────────────────────────
        private void TickFox(AnimalEntity a, Dictionary<string, Snap> snap, GameDateTime time)
        {
            var cur = snap[a.speciesId].st;
            string myZone = snap[a.speciesId].location;
            var eenv = _save.currentEEnv;
            float A = eenv?.A ?? 0.3f;
            float rain = _env?.Rainfall ?? 0f;

            // hunger
            float hunger = Mathf.Clamp01(cur.hunger + FOX_HUNGER_GAIN);

            // safety
            bool birdAtRiver = snap.TryGetValue("migratory_bird", out var b) && b.isPresent && b.location == "riverbank";
            bool foxNearRiver = ZoneTopology.AreSameOrAdjacent(myZone, "riverbank");
            float rbWater  = _registry.GetLocation("riverbank")?.waterLevel ?? 0.5f;
            float lowWater = _registry.GetLocation("lowland")?.waterLevel ?? 0.4f;
            bool waterAbnormal = rbWater > 0.7f || rbWater < 0.1f || lowWater > 0.7f || lowWater < 0.1f;

            float safety = cur.safety;
            if (birdAtRiver && foxNearRiver) safety -= 0.10f;
            if (waterAbnormal)               safety -= 0.05f;
            safety -= 0.10f * Mathf.Max(0f, A - 0.6f);
            if (A < 0.4f && rain < 0.3f)     safety += FOX_SAFETY_RECOVER;
            safety = Mathf.Clamp01(safety);

            // territoryStability
            bool voleInTerritory = snap.TryGetValue("vole", out var v) && v.isPresent && FOX_TERRITORY.Contains(v.location);
            float terr = cur.territoryStability;
            terr += voleInTerritory ? -FOX_TERR_ENCROACH : FOX_TERR_RECOVER;
            terr = Mathf.Clamp01(terr);

            float mod = ActivityModifier(myZone);   // 雨（经本地水位/湿度）+ 雾 → 活动度&&捕猎意愿下降（§5.1）
            string drive = Argmax(cur.lastDrive,
                ("Foraging",  Smooth(hunger, 0.7f) * mod),
                ("Patrol",    Smooth(1f - terr, 0.6f) * mod),
                ("Avoidance", Smooth(1f - safety, 0.7f)),
                ("Rest",      BASE_DRIVE),
                out float intensity);

            string cause = CauseFactor.None, causeTarget = "";
            switch (drive)
            {
                case "Foraging":
                {
                    string target = HighestVegInReach(myZone);
                    float tVeg = _registry.GetLocation(target)?.vegetationDensity ?? 0.5f;
                    if (target != myZone) MoveAnimal(a, target, "fox_forage", time);
                    hunger = Mathf.Clamp01(hunger - FOX_FORAGE_RELIEF * (0.5f + 0.5f * tVeg));
                    cause = CauseFactor.Hunger;
                    break;
                }
                case "Patrol":
                    if (voleInTerritory && v != null && v.location != myZone && FOX_TERRITORY.Contains(v.location))
                        MoveAnimal(a, v.location, "fox_patrol", time);
                    terr = Mathf.Clamp01(terr + FOX_PATROL_REASSERT);
                    if (voleInTerritory) { cause = CauseFactor.RodentExpansion; causeTarget = "vole"; }
                    break;
                case "Avoidance":
                    if (myZone != "highland_east") MoveAnimal(a, "highland_east", "fox_avoid", time);
                    if (waterAbnormal) { cause = CauseFactor.WaterRising; }
                    break;
                case "Rest":
                    // 雨天懒得出门（§5.1 语料可出现）——只在真的下雨/起雾时认领，文本对齐画面
                    if (HarshNow) { cause = CauseFactor.WeatherHarsh; causeTarget = "weather"; }
                    break;
            }

            cur.hunger = hunger; cur.safety = safety; cur.territoryStability = terr; cur.lastDrive = drive;
            a.internalState.hunger = hunger;
            a.internalState.safety = safety;
            a.internalState.territoryStability = terr;
            a.internalState.lastDrive = drive;
            WriteBehavior(a, drive, cause, causeTarget, intensity);
        }

        // ── 候鸟：迁徙冲动 + 栖息舒适度（迁来由 NarrativeRule 拥有，本系统管离去） ──
        private void TickMigratoryBird(AnimalEntity a, Dictionary<string, Snap> snap, GameDateTime time)
        {
            var cur = snap[a.speciesId].st;
            Season season = CurrentSeason;
            bool autumn = season == Season.Autumn;
            bool spring = season == Season.Spring;
            float V = _save.currentEEnv?.V ?? 0f;

            // 迁来 → 离去边沿：刚迁来（rule 翻转 isPresent）时重置冲动
            bool present = a.isPresent;

            float urge = cur.migrationUrge;
            urge += (autumn || spring) ? BIRD_URGE_SEASON : -BIRD_URGE_OFF;
            if (V < -0.2f) urge += BIRD_URGE_BLEAK;
            urge = Mathf.Clamp01(urge);

            float rbWater = _registry.GetLocation("riverbank")?.waterLevel ?? 0.5f;
            bool moderate = rbWater >= 0.4f && rbWater <= 0.6f;
            float comfort = Mathf.Lerp(cur.settlementComfort, moderate ? 0.8f : 0.3f, BIRD_COMFORT_LERP);
            bool foxAtRiver = snap.TryGetValue("fox", out var f) && f.isPresent && f.location == "riverbank";
            if (foxAtRiver) comfort -= BIRD_FOX_DISCOMFORT;
            comfort = Mathf.Clamp01(comfort);

            string drive, cause = CauseFactor.None, causeTarget = "";
            float mod = ActivityModifier(a.location);   // 雨（经河岸水位/湿度）+ 雾 → 活动频率下降（§5.1）
            float intensity = BASE_DRIVE * mod;

            if (!present)
            {
                drive = "Away";   // 不在场：迁来交给 NarrativeRule，本系统不翻 isPresent
            }
            else if (urge > 0.8f)
            {
                drive = "Depart";
                DepartBird(a, "migration_urge", time);
            }
            else if (comfort < 0.3f && urge > 0.4f)
            {
                drive = "EarlyDepart";
                if (foxAtRiver) { cause = CauseFactor.FoxNearby; causeTarget = "fox"; }
                DepartBird(a, "early_depart", time);
            }
            else
            {
                drive = "Settle";
                comfort = Mathf.Clamp01(comfort + 0.03f * mod);
                // 语料提醒（§5.1）——只在真的下雨/起雾时认领，文本对齐画面
                if (HarshNow) { cause = CauseFactor.WeatherHarsh; causeTarget = "weather"; }
            }

            cur.migrationUrge = urge; cur.settlementComfort = comfort; cur.lastDrive = drive;
            a.internalState.migrationUrge = urge;
            a.internalState.settlementComfort = comfort;
            a.internalState.lastDrive = drive;
            WriteBehavior(a, drive, cause, causeTarget, intensity);
        }

        private void DepartBird(AnimalEntity a, string reason, GameDateTime time)
        {
            string from = a.location;
            EntityStateHelper.ChangeAnimalState(a, "isPresent", "True", "False", reason, time);
            a.internalState.migrationUrge = 0.2f;
            Emit(WorldEventType.AnimalDeparted, "migratory_bird", "", reason + ":" + from, time);
        }

        // ── 织巢鸟：事件驱动（断枝离场 / 树恢复归巢） ──────────────
        private void TickWeaver(AnimalEntity a, Dictionary<string, Snap> snap, GameDateTime time)
        {
            // 离场逻辑在 TickTree 里随断枝边沿触发；这里只处理归巢
            if (!a.isPresent)
            {
                var tree = _registry.GetPlant("baobab_main");
                var ts = tree?.internalState;
                if (ts != null && ts.vitality > 0.5f && ts.ticksSinceBranchBreak > WEAVER_RETURN_TICKS)
                {
                    EntityStateHelper.ChangeAnimalState(a, "isPresent", "False", "True", "weaver_return", time);
                    EntityStateHelper.ChangeAnimalState(a, "location", a.location, "center", "weaver_return", time);
                    Emit(WorldEventType.WeaverBirdReturned, "weaver_bird", "baobab_main", "", time);
                }
            }
            a.behavior.zone  = a.location;
            a.behavior.drive = a.isPresent ? "Nest" : "Away";
            a.behavior.cause = CauseFactor.None;
            // 雨/雾 → 活动频率下降（§5.1"同"）；织巢鸟无状态向量，intensity 是唯一活动信号
            a.behavior.intensity = a.isPresent ? 0.3f * ActivityModifier(a.location) : 0f;
        }

        // ── 猴面包树：vitality / 开花 / 断枝边沿 → 织巢鸟离场 ──
        // 虫害植被衰减已迁至 VegetationSystem（loc.vegetationDensity 单一写者）。
        private void TickTree(Dictionary<string, Snap> snap, GameDateTime time)
        {
            var tree = _registry.GetPlant("baobab_main");
            if (tree?.internalState == null) return;
            var st = tree.internalState;
            float V = _save.currentEEnv?.V ?? 0f;

            // vitality：E_env.V 长期积分 + 本地湿度偏置（§5.1 雨→猴面包树：
            // 雨经 center 水位/湿度间接作用——湿季恢复加速、旱季减速。先数据，视觉等 MPB）
            float vNorm = (V + 1f) * 0.5f;
            var treeLoc = _registry.GetLocation("center");
            float moisture = treeLoc != null
                ? Mathf.Clamp01((treeLoc.waterLevel + treeLoc.soilMoisture) * 0.5f)
                : 0.5f;
            float vitTarget = Mathf.Clamp01(vNorm + (moisture - 0.5f) * 2f * TREE_RAIN_VITALITY_BIAS);
            st.vitality = Mathf.Clamp01(st.vitality + TREE_VITALITY_ALPHA * (vitTarget - st.vitality));

            st.ticksSinceBranchBreak++;

            // 断枝边沿：permanentDamages 由 NarrativeRule 增加，本系统检测增量
            if (tree.permanentDamages.Count > st.knownDamageCount)
            {
                st.knownDamageCount = tree.permanentDamages.Count;
                st.ticksSinceBranchBreak = 0;
                Emit(WorldEventType.TreeBranchBroke, "baobab_main", "weaver_bird", "", time);

                // 替代 Relation_WeaverHabitatLost：巢损毁 → 织巢鸟离场
                var weaver = _registry.GetAnimal("weaver_bird");
                if (weaver != null && weaver.isPresent)
                {
                    EntityStateHelper.ChangeAnimalState(weaver, "isPresent", "True", "False", "nest_destroyed", time);
                    Emit(WorldEventType.WeaverBirdDeparted, "weaver_bird", "baobab_main", "", time);
                }
            }

            // 开花：vitality 高且春季 → 累积；越阈触发
            bool spring = CurrentSeason == Season.Spring;
            if (st.vitality > 0.6f && spring)
                st.floweringReadiness += TREE_FLOWER_GAIN;
            if (st.floweringReadiness >= 1f && !tree.isFlowering)
            {
                EntityStateHelper.ChangePlantState(tree, "isFlowering", "False", "True", "flowering", time);
                EntityStateHelper.ChangePlantState(tree, "lastFlowerDate", tree.lastFlowerDate, time.ToKeyString(), "flowering", time);
                st.floweringReadiness = 0f;
                Emit(WorldEventType.TreeFlowered, "baobab_main", "", "", time);
            }
            // 虫害植被衰减已迁至 VegetationSystem（loc.vegetationDensity 单一写者），
            // 在 drive.Tick 之前运行；TickTree 不再写任何 location 字段。
        }

        // ── 工具 ───────────────────────────────────────────────────

        // 天气→活动度（Worksheet §5.1 已拍板）：雨不直连动物，经"所在 zone 的水位/湿度"
        // 间接作用（雨→M6 水位传播→动物每 tick 读相关值，结构与其他焦虑值相仿）；
        // 雾为全局信号直读。返回乘数 ∈ [1-damp, 1]，乘在活动类 drive 的 urgency /
        // activityRange / intensity 上。纯调制因子，每 tick 重算，无自身积分状态。
        // 读：loc.waterLevel/soilMoisture（写者 WorldManager）、env.FogDensity（写者环境系统）。
        private float ActivityModifier(string zone)
        {
            if (_env == null) return 1f;
            float mod = 1f;
            var loc = _registry.GetLocation(zone);
            if (loc != null)
            {
                float wet = Mathf.Clamp01((loc.waterLevel + loc.soilMoisture) * 0.5f);
                mod *= Mathf.Lerp(1f, 1f - WET_ACTIVITY_DAMP, wet);
            }
            if (_env.FogDensity > FOG_ACTIVITY_THRESHOLD)
                mod *= 1f - FOG_ACTIVITY_DAMP;
            return mod;
        }

        // 语料门控：只有"现在正在下雨/起雾"才允许天气语料——文本必须与画面里的天气对齐
        private bool HarshNow =>
            _env != null && (_env.Rainfall > RAIN_HARSH_THRESHOLD || _env.FogDensity > FOG_ACTIVITY_THRESHOLD);

        private float Smooth(float x, float k) =>
            Mathf.SmoothStep(0f, 1f, Mathf.Clamp01((x - (k - SOFT_BAND)) / (2f * SOFT_BAND)));

        private string Argmax(string incumbent,
            (string name, float p) a, (string name, float p) b, (string name, float p) c,
            out float winning)
            => Argmax(incumbent, out winning, a, b, c);

        private string Argmax(string incumbent,
            (string name, float p) a, (string name, float p) b, (string name, float p) c, (string name, float p) d,
            out float winning)
            => Argmax(incumbent, out winning, a, b, c, d);

        private string Argmax(string incumbent, out float winning, params(string name, float p)[] drives)
        {
            float bestP = Mathf.NegativeInfinity;
            string best = drives[0].name;
            foreach (var (name, p) in drives)
            {
                float term = p + (name == incumbent ? INCUMBENT_BONUS : 0f); 

                if (term > bestP)
                {
                    bestP = term;
                    best = name;
                }
            }

            winning = Mathf.Clamp01(bestP);
            return best;
        }


        // 当前 zone 及其邻居中植被最高者（狐狸觅食目标）
        private string HighestVegInReach(string zone)
        {
            string best = zone; float bestV = _registry.GetLocation(zone)?.vegetationDensity ?? 0f;
            foreach (var n in ZoneTopology.Neighbors(zone))
            {
                float v = _registry.GetLocation(n)?.vegetationDensity ?? 0f;
                if (v > bestV) { bestV = v; best = n; }
            }
            return best;
        }

        private void MoveAnimal(AnimalEntity a, string toZone, string triggeredBy, GameDateTime time)
        {
            if (a.location == toZone) return;
            string from = a.location;
            EntityStateHelper.ChangeAnimalState(a, "location", from, toZone, triggeredBy, time);
            a.lastSeenDate = time.ToKeyString();
        }

        private void Emit(string type, string sourceId, string targetId, string payload, GameDateTime time)
        {
            _save.worldEvents.Add(new WorldEvent
            {
                type = type, sourceId = sourceId, targetId = targetId,
                gameDate = time.ToKeyString(), payload = payload
            });
            Debug.Log($"[WorldEvent] {type}  {sourceId}->{targetId}  {payload}");
        }

        private static void WriteBehavior(AnimalEntity a, string drive, string cause, string causeTarget, float intensity)
        {
            a.behavior.drive         = drive;
            a.behavior.cause         = cause;
            a.behavior.causeTargetId = causeTarget;
            a.behavior.intensity     = intensity;
            a.behavior.zone          = a.location;
        }

        // ── 惰性初始化（旧存档兼容） ───────────────────────────────
        private void EnsureInitialized()
        {
            foreach (var a in _save.animals)
            {
                if (a.internalState == null || !a.internalState.initialized)
                    a.internalState = InitFor(a.speciesId);
                a.behavior ??= new BehaviorOutput { zone = a.location };
            }

            var tree = _registry.GetPlant("baobab_main");
            if (tree != null && (tree.internalState == null || !tree.internalState.initialized))
                tree.internalState = InitTree(tree.permanentDamages?.Count ?? 0);
        }

        private static AnimalInternalState InitFor(string speciesId)
        {
            var s = new AnimalInternalState { initialized = true };
            switch (speciesId)
            {
                case "fox":
                    s.hunger = 0.3f; s.safety = 0.8f; s.territoryStability = 0.8f;
                    break;
                case "vole":
                    s.shelterSecurity = 0.6f; s.foodStock = 0.7f; s.expansionPressure = 0.1f;
                    break;
                case "deer_mouse":
                    s.anxiety = 0.3f;
                    break;
                case "migratory_bird":
                    s.migrationUrge = 0.2f; s.settlementComfort = 0.6f;
                    break;
            }
            return s;
        }

        private static PlantInternalState InitTree(int existingDamage)
        {
            return new PlantInternalState
            {
                initialized           = true,
                vitality              = 0.6f,
                floweringReadiness    = 0f,
                ticksSinceBranchBreak = 9999,
                knownDamageCount      = existingDamage,
                branches = new List<BranchState>
                {
                    new BranchState { id = "E-1", dir = "E", integrity = 1f },
                    new BranchState { id = "E-2", dir = "E", integrity = 1f },
                    new BranchState { id = "W-1", dir = "W", integrity = 1f },
                    new BranchState { id = "N-1", dir = "N", integrity = 1f },
                }
            };
        }
    }
}
