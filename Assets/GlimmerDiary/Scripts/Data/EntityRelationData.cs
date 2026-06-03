using System.Collections.Generic;
using UnityEngine;

namespace GlimmerDiary.Data
{
    // 实体间关系声明。
    // 与 NarrativeRuleSO 的区别在于评估方式：
    //   - NarrativeRuleEngine：每次日记提交时评估一次
    //   - EntityRelationSystem：单次优先级有序扫描，效果就地生效
    //     高优先级关系先触发，下游关系在同一帧内看到已变更的状态
    //
    // priority 约定：上游关系必须高于依赖它的下游关系
    [CreateAssetMenu(
        fileName = "Relation_",
        menuName = "GlimmerDiary/Entity Relation")]
    public class EntityRelationSO : ScriptableObject
    {
        public string relationId;
        public string description;
        public int    priority;      // 数字越大越先执行，确保级联顺序
        public int    cooldownDays;  // 游戏内天数，控制世界志生成频率

        [Header("触发条件（全部满足才执行）")]
        public List<RuleCondition> conditions = new();

        [Header("效果（触发后执行，效果就地生效供下游感知）")]
        public List<StateChangeInstruction> effects = new();

        [Header("世界志（可选，留空则静默变更）")]
        [TextArea(2, 5)]
        public List<string> textTemplates = new();

        [Header("模板变量来源")]
        public List<TemplateVariable> variables = new();
    }
}
