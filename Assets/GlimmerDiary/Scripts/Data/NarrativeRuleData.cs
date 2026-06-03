using System;
using System.Collections.Generic;
using UnityEngine;

namespace GlimmerDiary.Data
{
    [Serializable]
    public class RuleCondition
    {
        public string targetType;  // "location" / "animal" / "plant" / "eenv" / "time"
        public string targetId;    // "lowland" / "vole" / "baobab_main" 等
        public string field;
        public string op;          // "gt" / "lt" / "eq" / "gte" / "lte" / "neq"
        public string value;       // 统一用 string，运行时转型
    }

    [Serializable]
    public class StateChangeInstruction
    {
        public string targetType;
        public string targetId;
        public string field;
        public string toValue;          // 绝对值（useDelta=false 时生效）
        public bool   useDelta;         // true 时以 deltaValue 为增量，忽略 toValue
        public float  deltaValue;       // 增量，正负均可，结果 Clamp01
        public bool   isPermanent;
        public string permanentDescription;
    }

    [Serializable]
    public class TemplateVariable
    {
        public string key;          // 模板占位符，如 "newLocation"
        public string sourceType;   // "animal"/"plant"/"location"/"time"/"fixed"
        public string sourceId;
        public string sourceField;
        public string fixedValue;   // sourceType=="fixed" 时直接用这个值
    }


}
