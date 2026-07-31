using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using GlimmerDiary.Data;
using GlimmerDiary.Utils;

namespace GlimmerDiary.Core
{
    // 实体间关系系统：声明式因果链的执行层
    //
    // 评估策略：
    //   单次按优先级有序扫描。关系 A 触发后效果就地生效（在同一次 Evaluate 调用内），
    //   依赖 A 效果的下游关系 B 在本次扫描中即可感知变更 —— 前提是 B.priority < A.priority。
    //   设计约束：上游关系必须设更高的 priority，以确保级联语义正确。
    //
    // 与 NarrativeRuleEngine 的分工：
    //   NarrativeRuleEngine：环境参数 + 实体状态 → 叙事事件（每次日记提交评估一次）
    //   EntityRelationSystem：实体状态 → 实体状态（同一次提交内级联评估）
    public class EntityRelationSystem
    {
        private readonly EntityRegistry             _registry;
        private readonly WorldSaveData              _save;
        private WorldEnvironmentState               _envState;
        private NaturalRhythmState                  _rhythm;
        private readonly Dictionary<string, string> _lastTriggered = new();

        public EntityRelationSystem(EntityRegistry registry, WorldSaveData save)
        {
            _registry = registry;
            _save     = save;
        }

        public void SetEnvironment(WorldEnvironmentState env, NaturalRhythmState rhythm)
        {
            _envState = env;
            _rhythm   = rhythm;
        }

        // 按优先级有序扫描，效果就地生效
        public void Evaluate(List<EntityRelationSO> relations, GameDateTime gameTime)
        {
            foreach (var rel in relations.OrderByDescending(r => r.priority))
            {
                if (!CooldownPassed(rel, gameTime))      continue;
                if (!AllConditionsMet(rel.conditions))   continue;
                ExecuteRelation(rel, gameTime);
            }
        }

        // ── 冷却判断 ─────────────────────────────────
        private bool CooldownPassed(EntityRelationSO rel, GameDateTime now)
        {
            if (!_lastTriggered.TryGetValue(rel.relationId, out var lastDate))
                return true;
            var last = GameDateTime.ParseKey(lastDate);
            return now.ToAbsoluteDays() - last.ToAbsoluteDays() >= rel.cooldownDays;
        }

        // ── 条件判断 ─────────────────────────────────
        private bool AllConditionsMet(List<RuleCondition> conditions)
        {
            foreach (var c in conditions)
                if (!ConditionMet(c)) return false;
            return true;
        }

        private bool ConditionMet(RuleCondition c)
        {
            string actual = GetFieldValue(c.targetType, c.targetId, c.field);
            if (actual == null) return false;

            if (float.TryParse(actual, System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture, out float fA) &&
                float.TryParse(c.value, System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture, out float fE))
            {
                return c.op switch
                {
                    "gt"  => fA >  fE,
                    "gte" => fA >= fE,
                    "lt"  => fA <  fE,
                    "lte" => fA <= fE,
                    "eq"  => Mathf.Approximately(fA, fE),
                    "neq" => !Mathf.Approximately(fA, fE),
                    _     => false
                };
            }

            return c.op switch
            {
                "eq"  => actual == c.value,
                "neq" => actual != c.value,
                _     => false
            };
        }

        // ── 字段读取 ─────────────────────────────────
        private string GetFieldValue(string targetType, string targetId, string field)
        {
            switch (targetType)
            {
                case "animal":
                    var animal = _registry.GetAnimal(targetId);
                    if (animal == null) return null;
                    return field switch
                    {
                        "isPresent"            => animal.isPresent.ToString(),
                        "location"             => animal.location,
                        "locationDisplayName"  => _registry.GetLocation(animal.location)?.displayName
                                                  ?? animal.location,
                        "facingDirection"      => animal.facingDirection,
                        "familyState"          => animal.familyState,
                        "activityRange"        => animal.activityRange.ToString("F2",
                                                      System.Globalization.CultureInfo.InvariantCulture),
                        _                      => null
                    };

                case "plant":
                    var plant = _registry.GetPlant(targetId);
                    if (plant == null) return null;
                    return field switch
                    {
                        "isAlive"               => plant.isAlive.ToString(),
                        "growthStage"           => plant.growthStage.ToString("F2"),
                        "isFlowering"           => plant.isFlowering.ToString(),
                        "permanentDamagesCount" => plant.permanentDamages.Count.ToString(),
                        _                       => null
                    };

                case "location":
                    var loc = _registry.GetLocation(targetId);
                    if (loc == null) return null;
                    return field switch
                    {
                        "waterLevel"        => loc.waterLevel.ToString("F2",
                                                   System.Globalization.CultureInfo.InvariantCulture),
                        "soilMoisture"      => loc.soilMoisture.ToString("F2",
                                                   System.Globalization.CultureInfo.InvariantCulture),
                        "vegetationDensity" => loc.vegetationDensity.ToString("F2",
                                                   System.Globalization.CultureInfo.InvariantCulture),
                        "displayName"       => loc.displayName,
                        _                   => null
                    };

                case "eenv":
                    if (_save.currentEEnv == null) return null;
                    return field switch
                    {
                        "V" => _save.currentEEnv.V.ToString("F2", System.Globalization.CultureInfo.InvariantCulture),
                        "A" => _save.currentEEnv.A.ToString("F2", System.Globalization.CultureInfo.InvariantCulture),
                        "C" => _save.currentEEnv.C.ToString("F2", System.Globalization.CultureInfo.InvariantCulture),
                        "S" => _save.currentEEnv.S.ToString("F2", System.Globalization.CultureInfo.InvariantCulture),
                        _   => null
                    };

                case "time":
                    return field switch
                    {
                        "month"  => _save.gameTime.month.ToString(),
                        "season" => _rhythm?.season.ToString() ?? "",
                        _        => null
                    };

                default: return null;
            }
        }

        // ── 关系执行 ─────────────────────────────────
        private void ExecuteRelation(EntityRelationSO rel, GameDateTime gameTime)
        {
            // 效果就地生效，下游关系在本次扫描中感知到
            foreach (var effect in rel.effects)
                ApplyEffect(effect, rel.relationId, gameTime);

            // 世界志（可选）
            if (rel.textTemplates != null && rel.textTemplates.Count > 0)
            {
                string template = rel.textTemplates[
                    UnityEngine.Random.Range(0, rel.textTemplates.Count)];
                string text = FillTemplate(template, rel.variables, gameTime);

                _save.pendingChronicles.Add(new WorldChronicleEntry
                {
                    entryId      = Guid.NewGuid().ToString(),
                    gameDate     = gameTime.ToDisplayString(),
                    eventId      = rel.relationId,
                    text         = text,
                    hasBeenShown = false
                });

                Debug.Log($"[EntityRelationSystem] {rel.relationId} → {text}");
            }
            else
            {
                Debug.Log($"[EntityRelationSystem] {rel.relationId} (silent)");
            }

            _lastTriggered[rel.relationId] = gameTime.ToKeyString();
        }

        // ── 效果执行 ─────────────────────────────────
        private void ApplyEffect(
            StateChangeInstruction effect,
            string triggeredBy, GameDateTime gameTime)
        {
            string toValue = ResolveToValue(effect);

            switch (effect.targetType)
            {
                case "animal":
                    var animal = _registry.GetAnimal(effect.targetId);
                    if (animal == null) break;
                    string oldAnimal = GetFieldValue("animal", effect.targetId, effect.field) ?? "";
                    EntityStateHelper.ChangeAnimalState(
                        animal, effect.field, oldAnimal, toValue, triggeredBy, gameTime);
                    break;

                case "plant":
                    var plant = _registry.GetPlant(effect.targetId);
                    if (plant == null) break;
                    if (effect.isPermanent)
                        EntityStateHelper.AddPermanentDamage(
                            plant, effect.field, effect.permanentDescription, triggeredBy, gameTime);
                    else
                    {
                        string oldPlant = GetFieldValue("plant", effect.targetId, effect.field) ?? "";
                        EntityStateHelper.ChangePlantState(
                            plant, effect.field, oldPlant, toValue, triggeredBy, gameTime);
                    }
                    break;

                case "location":
                    var loc = _registry.GetLocation(effect.targetId);
                    if (loc == null) break;
                    if (effect.isPermanent)
                        EntityStateHelper.AddPermanentTerrain(
                            loc, effect.field, effect.permanentDescription, triggeredBy, gameTime);
                    else
                    {
                        string oldLoc = GetFieldValue("location", effect.targetId, effect.field) ?? "";
                        EntityStateHelper.ChangeLocationState(
                            loc, effect.field, oldLoc, toValue, triggeredBy, gameTime);
                    }
                    break;
            }
        }

        private string ResolveToValue(StateChangeInstruction effect)
        {
            if (!effect.useDelta) return effect.toValue;
            string current = GetFieldValue(effect.targetType, effect.targetId, effect.field);
            if (float.TryParse(current, System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture, out float f))
            {
                return Mathf.Clamp01(f + effect.deltaValue)
                           .ToString("F2", System.Globalization.CultureInfo.InvariantCulture);
            }
            return effect.toValue;
        }

        // ── 模板填充 ─────────────────────────────────
        private string FillTemplate(
            string template, List<TemplateVariable> variables, GameDateTime gameTime)
        {
            string result = template;
            result = result.Replace("{date}",   gameTime.ToDisplayString());
            result = result.Replace("{season}", _rhythm?.season.ToString() ?? "");
            result = result.Replace("{sky}",    SkyPhrase.Pick(gameTime, _envState, _rhythm));

            if (variables != null)
            {
                foreach (var v in variables)
                {
                    string val = v.sourceType == "fixed"
                        ? v.fixedValue
                        : GetFieldValue(v.sourceType, v.sourceId, v.sourceField) ?? "";
                    result = result.Replace("{" + v.key + "}", val);
                }
            }
            return result;
        }

    }
}
