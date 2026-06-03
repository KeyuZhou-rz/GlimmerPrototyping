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
        // 实体关系资产（已废弃 — DEPRECATED）
        //
        // 这 4 条"实体 → 实体"耦合（weaver_habitat_lost / deer_mouse_anxious /
        // insect_surge_vegetation / vole_territory_expand）已由 AnimalDriveSystem
        // 的状态向量传导接管，其文案由 BehaviorNarrator 按 cause 产出。
        // WorldManager.RetiredRelationIds 在加载时把它们从关系系统活动集剔除。
        // 现有 .asset 仍保留在 Assets/Resources/Relations 供参考，但不应再重新生成。
        // 详见 Docs/AnimalStateSystem.md §1.2 / §10。
        //
        // 注意：NarrativeRule（情绪/环境 → 离散事件，如断枝、候鸟迁来）未受影响，
        //       仍由 CreateAllRules 生成、NarrativeRuleEngine 评估。
        // ──────────────────────────────────────────────
        [MenuItem("GlimmerDiary/Create All Entity Relations (DEPRECATED)")]
        public static void CreateAllRelations_Deprecated()
        {
            Debug.LogWarning(
                "[WorldRuleCreator] “Create All Entity Relations” 已废弃：这 4 条实体耦合已迁移到 " +
                "AnimalDriveSystem + BehaviorNarrator，并被 WorldManager.RetiredRelationIds 在运行时剔除。" +
                "未生成任何资产。详见 Docs/AnimalStateSystem.md。");
        }

        // ── 动物状态系统调参资产（P5）────────────────
        // 生成默认 AnimalDriveTuning 到 Resources/Tuning，WorldManager 启动时自动加载。
        // 不存在时不覆盖；缺失该资产时 AnimalDriveSystem 回退到字段默认值。
        [MenuItem("GlimmerDiary/Create Animal Drive Tuning")]
        public static void CreateAnimalDriveTuning()
        {
            const string dir = "Assets/Resources/Tuning";
            if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);

            string path = $"{dir}/AnimalDriveTuning.asset";
            if (File.Exists(path))
            {
                Debug.LogWarning($"[WorldRuleCreator] 已存在，未覆盖：{path}");
                return;
            }

            var so = ScriptableObject.CreateInstance<AnimalDriveTuning>();
            AssetDatabase.CreateAsset(so, path);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log($"[WorldRuleCreator] Created: {path}（在 Inspector 调参，运行时即时生效）");
        }

        // ── 工具 ─────────────────────────────────────
        private static void Save(NarrativeRuleSO rule, string fileName)
        {
            string path = $"{RulesPath}/{fileName}.asset";
            AssetDatabase.CreateAsset(rule, path);
            Debug.Log($"[WorldRuleCreator] Created: {path}");
        }
    }
}
#endif
