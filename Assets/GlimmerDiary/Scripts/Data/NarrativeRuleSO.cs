using System.Collections.Generic;
using UnityEngine;

namespace GlimmerDiary.Data
{
    [CreateAssetMenu(
        fileName = "Rule_",
        menuName = "GlimmerDiary/Narrative Rule")]
    public class NarrativeRuleSO : ScriptableObject
    {
        public string ruleId;
        public string description;   // 开发备注
        public int    priority;      // 数字越大越优先
        public int    cooldownDays;  // 游戏内天数，防止同一规则连续触发

        [Header("触发条件（全部满足才触发）")]
        public List<RuleCondition> conditions = new();

        [Header("状态变更（触发后执行）")]
        public List<StateChangeInstruction> stateChanges = new();

        [Header("世界志模板（随机选一条）")]
        [TextArea(2, 5)]
        public List<string> textTemplates = new();

        [Header("模板变量来源")]
        public List<TemplateVariable> variables = new();
    }
}
