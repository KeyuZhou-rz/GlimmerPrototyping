#if UNITY_EDITOR
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;
using GlimmerDiary.Data;

namespace GlimmerDiary.Editor
{
    public static class WorldRuleCreator
    {
        private const string RulesPath = "Assets/Resources/Rules";

        [MenuItem("GlimmerDiary/Create All Narrative Rules")]
        public static void CreateAllRules()
        {
            if (!Directory.Exists(RulesPath))
                Directory.CreateDirectory(RulesPath);

            CreateVoleFlood();
            CreateVoleBurrowAbandoned();
            CreateFoxPathDeviation();
            CreateBaobabBranchBroken();
            CreateMigratoryBirdArrival();

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log("[WorldRuleCreator] 5 narrative rules created in Assets/Resources/Rules/");
        }

        // ── 1. 田鼠因水位迁移 ────────────────────────
        private static void CreateVoleFlood()
        {
            var rule = ScriptableObject.CreateInstance<NarrativeRuleSO>();
            rule.ruleId       = "vole_relocate_flood";
            rule.description  = "低洼地水位过高，田鼠迁往东侧高地";
            rule.priority     = 80;
            rule.cooldownDays = 14;

            rule.conditions = new List<RuleCondition>
            {
                new RuleCondition { targetType="location", targetId="lowland",
                    field="waterLevel", op="gt", value="0.65" },
                new RuleCondition { targetType="animal", targetId="vole",
                    field="isPresent", op="eq", value="True" },
                new RuleCondition { targetType="animal", targetId="vole",
                    field="location", op="eq", value="lowland" }
            };

            rule.stateChanges = new List<StateChangeInstruction>
            {
                new StateChangeInstruction
                {
                    targetType="animal", targetId="vole",
                    field="location", toValue="highland_east", isPermanent=false
                }
            };

            rule.textTemplates = new List<string>
            {
                "{date} {sky} 低洼处的田鼠洞进了水。新洞口在{newLocation}，朝{facing}。",
                "{date} {sky} 田鼠一家把家搬到了{newLocation}。旧洞口还开着，没来得及填。"
            };

            rule.variables = new List<TemplateVariable>
            {
                new TemplateVariable
                {
                    key="newLocation", sourceType="location",
                    sourceId="highland_east", sourceField="displayName"
                },
                new TemplateVariable
                {
                    key="facing", sourceType="animal",
                    sourceId="vole", sourceField="facingDirection"
                }
            };

            Save(rule, "Rule_VoleFlood");
        }

        // ── 2. 旧洞口废弃（永久地貌）────────────────
        private static void CreateVoleBurrowAbandoned()
        {
            var rule = ScriptableObject.CreateInstance<NarrativeRuleSO>();
            rule.ruleId       = "vole_burrow_abandoned";
            rule.description  = "田鼠已迁走，低洼地旧洞口废弃";
            rule.priority     = 60;
            rule.cooldownDays = 30;

            rule.conditions = new List<RuleCondition>
            {
                new RuleCondition { targetType="animal", targetId="vole",
                    field="location", op="eq", value="highland_east" },
                new RuleCondition { targetType="location", targetId="lowland",
                    field="waterLevel", op="gt", value="0.5" }
            };

            rule.stateChanges = new List<StateChangeInstruction>
            {
                new StateChangeInstruction
                {
                    targetType="location", targetId="lowland",
                    field="burrow_collapse", isPermanent=true,
                    permanentDescription="田鼠旧洞口被落叶盖住，入口塌了一半"
                }
            };

            rule.textTemplates = new List<string>
            {
                "{date} {sky} 低地的旧洞口被落叶盖了一半。",
                "{date} {sky} 低地那边没有新翻的土了。"
            };

            rule.variables = new List<TemplateVariable>();
            Save(rule, "Rule_VoleBurrowAbandoned");
        }

        // ── 3. 狐狸改变路径 ──────────────────────────
        private static void CreateFoxPathDeviation()
        {
            var rule = ScriptableObject.CreateInstance<NarrativeRuleSO>();
            rule.ruleId       = "fox_path_deviation";
            rule.description  = "E_env唤醒度高，狐狸改变行为朝向";
            rule.priority     = 70;
            rule.cooldownDays = 10;

            rule.conditions = new List<RuleCondition>
            {
                new RuleCondition { targetType="eenv", targetId="",
                    field="A", op="gt", value="0.6" },
                new RuleCondition { targetType="animal", targetId="fox",
                    field="isPresent", op="eq", value="True" }
            };

            rule.stateChanges = new List<StateChangeInstruction>
            {
                new StateChangeInstruction
                {
                    targetType="animal", targetId="fox",
                    field="facingDirection", toValue="E", isPermanent=false
                }
            };

            rule.textTemplates = new List<string>
            {
                "{date} {sky} 狐狸比平时早出现在{location}，走了几圈又回去了。",
                "{date} {sky} 狐狸的脚印今天绕开了{location}，从北边绕过去的。"
            };

            rule.variables = new List<TemplateVariable>
            {
                new TemplateVariable
                {
                    // locationDisplayName：引擎自动将 locationId 转为 displayName
                    key="location", sourceType="animal",
                    sourceId="fox", sourceField="locationDisplayName"
                }
            };

            Save(rule, "Rule_FoxPathDeviation");
        }

        // ── 4. 猴面包树折枝（永久事件）──────────────
        private static void CreateBaobabBranchBroken()
        {
            var rule = ScriptableObject.CreateInstance<NarrativeRuleSO>();
            rule.ruleId       = "baobab_branch_broken";
            rule.description  = "高唤醒+强负效价，猴面包树折枝（不可逆）";
            rule.priority     = 100;
            rule.cooldownDays = 60;

            rule.conditions = new List<RuleCondition>
            {
                new RuleCondition { targetType="eenv", targetId="",
                    field="A", op="gt", value="0.7" },
                new RuleCondition { targetType="eenv", targetId="",
                    field="V", op="lt", value="-0.4" },
                new RuleCondition { targetType="plant", targetId="baobab_main",
                    field="isAlive", op="eq", value="True" }
            };

            rule.stateChanges = new List<StateChangeInstruction>
            {
                new StateChangeInstruction
                {
                    targetType="plant", targetId="baobab_main",
                    field="branch_broken", isPermanent=true,
                    permanentDescription="主枝东侧第一根侧枝断落，断口朝下"
                }
            };

            rule.textTemplates = new List<string>
            {
                "{date} {sky} 猴面包树东侧的那根枝断了。断口朝下。",
                "{date} {sky} 昨夜的风折断了猴面包树靠东的一根枝。它还挂在那里，没有落地。"
            };

            rule.variables = new List<TemplateVariable>();
            Save(rule, "Rule_BaobabBranchBroken");
        }

        // ── 5. 候鸟迁来 ──────────────────────────────
        private static void CreateMigratoryBirdArrival()
        {
            var rule = ScriptableObject.CreateInstance<NarrativeRuleSO>();
            rule.ruleId       = "migratory_bird_arrival";
            rule.description  = "秋季且E_env正效价，候鸟出现在河岸";
            rule.priority     = 75;
            rule.cooldownDays = 90;

            rule.conditions = new List<RuleCondition>
            {
                new RuleCondition { targetType="time", targetId="",
                    field="month", op="gte", value="9" },
                new RuleCondition { targetType="time", targetId="",
                    field="month", op="lte", value="11" },
                new RuleCondition { targetType="eenv", targetId="",
                    field="V", op="gt", value="0.2" },
                new RuleCondition { targetType="animal", targetId="migratory_bird",
                    field="isPresent", op="eq", value="False" }
            };

            rule.stateChanges = new List<StateChangeInstruction>
            {
                new StateChangeInstruction
                {
                    targetType="animal", targetId="migratory_bird",
                    field="isPresent", toValue="True", isPermanent=false
                },
                new StateChangeInstruction
                {
                    targetType="animal", targetId="migratory_bird",
                    field="location", toValue="riverbank", isPermanent=false
                }
            };

            rule.textTemplates = new List<string>
            {
                "{date} {sky} 河岸边今天多了几个影子。候鸟来了。",
                "{date} {sky} 芦苇丛里有了新的声音。它们是昨晚到的，还是前天，说不清楚。"
            };

            rule.variables = new List<TemplateVariable>();
            Save(rule, "Rule_MigratoryBirdArrival");
        }

        // ──────────────────────────────────────────────
        // 实体关系资产
        // ──────────────────────────────────────────────
        private const string RelationsPath = "Assets/Resources/Relations";

        [MenuItem("GlimmerDiary/Create All Entity Relations")]
        public static void CreateAllRelations()
        {
            if (!Directory.Exists(RelationsPath))
                Directory.CreateDirectory(RelationsPath);

            CreateWeaverHabitatLost();
            CreateDeerMouseAnxious();
            CreateInsectSurgeVegetation();
            CreateVoleTerritoryExpand();

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log("[WorldRuleCreator] 4 entity relations created in Assets/Resources/Relations/");
        }

        // ── R1. 猴面包树断枝 → 织巢鸟离开 (priority=100) ──
        private static void CreateWeaverHabitatLost()
        {
            var rel = ScriptableObject.CreateInstance<EntityRelationSO>();
            rel.relationId    = "weaver_habitat_lost";
            rel.description   = "猴面包树有永久损伤且织巢鸟在场 → 织巢鸟离开";
            rel.priority      = 100;
            rel.cooldownDays  = 365;

            rel.conditions = new List<RuleCondition>
            {
                new RuleCondition { targetType="plant",  targetId="baobab_main",
                    field="permanentDamagesCount", op="gt", value="0" },
                new RuleCondition { targetType="animal", targetId="weaver_bird",
                    field="isPresent", op="eq", value="True" }
            };

            rel.effects = new List<StateChangeInstruction>
            {
                new StateChangeInstruction { targetType="animal", targetId="weaver_bird",
                    field="isPresent", toValue="False" }
            };

            rel.textTemplates = new List<string>
            {
                "{date} {sky} 织巢鸟的巢随那根枝倒下了。它们围着树转了一圈，然后往东南飞走了。",
                "{date} {sky} 断枝上那个球形的巢，昨天还在，今天不见了。织巢鸟走了。"
            };

            rel.variables = new List<TemplateVariable>();
            SaveRelation(rel, "Relation_WeaverHabitatLost");
        }

        // ── R2. 织巢鸟离开 → 鹿鼠活动范围收缩 (priority=90) ──
        private static void CreateDeerMouseAnxious()
        {
            var rel = ScriptableObject.CreateInstance<EntityRelationSO>();
            rel.relationId    = "deer_mouse_anxious";
            rel.description   = "织巢鸟不在场 → 鹿鼠失去安全信号，activityRange收缩";
            rel.priority      = 90;
            rel.cooldownDays  = 365;

            rel.conditions = new List<RuleCondition>
            {
                new RuleCondition { targetType="animal", targetId="weaver_bird",
                    field="isPresent", op="eq", value="False" },
                new RuleCondition { targetType="animal", targetId="deer_mouse",
                    field="activityRange", op="gt", value="0.4" }
            };

            rel.effects = new List<StateChangeInstruction>
            {
                new StateChangeInstruction { targetType="animal", targetId="deer_mouse",
                    field="activityRange", toValue="0.3" }
            };

            rel.textTemplates = new List<string>
            {
                "{date} {sky} 东侧的鹿鼠比以前少见了。",
                "{date} {sky} 鹿鼠只在洞口附近活动，不再往东走了。"
            };

            rel.variables = new List<TemplateVariable>();
            SaveRelation(rel, "Relation_DeerMouseAnxious");
        }

        // ── R3. 织巢鸟离开 → 东侧高地植被密度缓慢下降 (priority=85) ──
        // 静默效果，不生成世界志；cooldownDays=7 每周触发一次，持续衰退
        private static void CreateInsectSurgeVegetation()
        {
            var rel = ScriptableObject.CreateInstance<EntityRelationSO>();
            rel.relationId    = "insect_surge_vegetation";
            rel.description   = "织巢鸟离场 → 虫害使东侧高地植被密度每周降 0.02（静默）";
            rel.priority      = 85;
            rel.cooldownDays  = 7;

            rel.conditions = new List<RuleCondition>
            {
                new RuleCondition { targetType="animal",   targetId="weaver_bird",
                    field="isPresent", op="eq", value="False" },
                new RuleCondition { targetType="location", targetId="highland_east",
                    field="vegetationDensity", op="gt", value="0.10" }
            };

            rel.effects = new List<StateChangeInstruction>
            {
                new StateChangeInstruction { targetType="location", targetId="highland_east",
                    field="vegetationDensity", useDelta=true, deltaValue=-0.02f }
            };

            // 静默：textTemplates 留空，仅在植被跌至关键阈值时由叙事规则生成世界志
            rel.textTemplates = new List<string>();
            rel.variables     = new List<TemplateVariable>();
            SaveRelation(rel, "Relation_InsectSurgeVegetation");
        }

        // ── R4. 鹿鼠退缩 → 田鼠向东试探领地 (priority=80) ──
        private static void CreateVoleTerritoryExpand()
        {
            var rel = ScriptableObject.CreateInstance<EntityRelationSO>();
            rel.relationId    = "vole_territory_expand";
            rel.description   = "鹿鼠活动范围收缩 → 田鼠向东试探";
            rel.priority      = 80;
            rel.cooldownDays  = 365;

            rel.conditions = new List<RuleCondition>
            {
                new RuleCondition { targetType="animal", targetId="deer_mouse",
                    field="activityRange", op="lt", value="0.4" },
                new RuleCondition { targetType="animal", targetId="vole",
                    field="isPresent", op="eq", value="True" }
            };

            rel.effects = new List<StateChangeInstruction>
            {
                new StateChangeInstruction { targetType="animal", targetId="vole",
                    field="facingDirection", toValue="E" }
            };

            rel.textTemplates = new List<string>
            {
                "{date} {sky} 低地那边出现了新的土堆，朝东。田鼠在试探。",
                "{date} {sky} 田鼠往东多走了一段，停了一会儿，又回来了。"
            };

            rel.variables = new List<TemplateVariable>();
            SaveRelation(rel, "Relation_VoleTerritoryExpand");
        }

        // ── 工具 ─────────────────────────────────────
        private static void Save(NarrativeRuleSO rule, string fileName)
        {
            string path = $"{RulesPath}/{fileName}.asset";
            AssetDatabase.CreateAsset(rule, path);
            Debug.Log($"[WorldRuleCreator] Created: {path}");
        }

        private static void SaveRelation(EntityRelationSO rel, string fileName)
        {
            string path = $"{RelationsPath}/{fileName}.asset";
            AssetDatabase.CreateAsset(rel, path);
            Debug.Log($"[WorldRuleCreator] Created: {path}");
        }
    }
}
#endif
