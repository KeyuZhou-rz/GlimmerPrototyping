using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using GlimmerDiary.Data;
using GlimmerDiary.Utils;

namespace GlimmerDiary.Core
{
    public class NarrativeRuleEngine
    {
        private readonly EntityRegistry             _registry;
        private readonly WorldSaveData              _save;
        private WorldEnvironmentState               _envState;
        private NaturalRhythmState                  _rhythm;
        private readonly Dictionary<string, string> _lastTriggered = new();
        private bool                                _cooldownsLoaded;

        public NarrativeRuleEngine(EntityRegistry registry, WorldSaveData save)
        {
            _registry = registry;
            _save     = save;
        }

        public void SetEnvironment(WorldEnvironmentState env, NaturalRhythmState rhythm)
        {
            _envState = env;
            _rhythm   = rhythm;
        }

        // 每次玩家提交日记后调用
        public void Evaluate(List<NarrativeRuleSO> rules, GameDateTime gameTime)
        {
            EnsureCooldownsLoaded();
            var triggered = new List<NarrativeRuleSO>();
            foreach (var rule in rules)
            {
                if (!CooldownPassed(rule, gameTime))    continue;
                if (!AllConditionsMet(rule.conditions)) continue;
                triggered.Add(rule);
            }

            foreach (var rule in triggered
                         .OrderByDescending(r => r.priority)
                         .Take(2))
            {
                ExecuteRule(rule, gameTime);
            }
        }

        // ── 冷却判断 ─────────────────────────────────
        // 冷却持久化（矩阵补全 §5 横切注记）：存档 ruleCooldowns 是唯一持久形态，
        // 内存字典只是它的运行时缓存。本引擎是该存档字段的唯一写者。
        private void EnsureCooldownsLoaded()
        {
            if (_cooldownsLoaded) return;
            _cooldownsLoaded = true;
            _lastTriggered.Clear();
            foreach (var rec in _save.ruleCooldowns)
                _lastTriggered[rec.ruleId] = rec.lastFiredDateKey;
        }

        private void PersistCooldown(string ruleId, string dateKey)
        {
            var rec = _save.ruleCooldowns.Find(r => r.ruleId == ruleId);
            if (rec == null)
                _save.ruleCooldowns.Add(new RuleCooldownRecord { ruleId = ruleId, lastFiredDateKey = dateKey });
            else
                rec.lastFiredDateKey = dateKey;
        }

        private bool CooldownPassed(NarrativeRuleSO rule, GameDateTime now)
        {
            if (!_lastTriggered.TryGetValue(rule.ruleId, out var lastDate))
                return true;
            var last = GameDateTime.ParseKey(lastDate);
            return now.ToAbsoluteDays() - last.ToAbsoluteDays() >= rule.cooldownDays;
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

        // ── 字段读取（统一入口）─────────────────────
        private string GetFieldValue(string targetType, string targetId, string field)
        {
            switch (targetType)
            {
                case "animal":
                    var animal = _registry.GetAnimal(targetId);
                    if (animal == null) return null;
                    return field switch
                    {
                        "isPresent"           => animal.isPresent.ToString(),
                        "location"            => animal.location,
                        "locationDisplayName" => _registry.GetLocation(animal.location)?.displayName
                                                 ?? animal.location,
                        "facingDirection"     => animal.facingDirection,
                        "familyState"         => animal.familyState,
                        "activityRange"       => animal.activityRange.ToString("F2", System.Globalization.CultureInfo.InvariantCulture),
                        _                     => null
                    };

                case "plant":
                    var plant = _registry.GetPlant(targetId);
                    if (plant == null) return null;
                    return field switch
                    {
                        "isAlive"              => plant.isAlive.ToString(),
                        "growthStage"          => plant.growthStage.ToString("F2"),
                        "isFlowering"          => plant.isFlowering.ToString(),
                        "permanentDamagesCount"=> plant.permanentDamages.Count.ToString(),
                        _                      => null
                    };

                case "location":
                    var loc = _registry.GetLocation(targetId);
                    if (loc == null) return null;
                    return field switch
                    {
                        "waterLevel"        => loc.waterLevel.ToString("F2"),
                        "soilMoisture"      => loc.soilMoisture.ToString("F2"),
                        "vegetationDensity" => loc.vegetationDensity.ToString("F2"),
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
                        "T" => _save.currentEEnv.T.ToString("F2", System.Globalization.CultureInfo.InvariantCulture),
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

        // ── 规则执行 ─────────────────────────────────
        private void ExecuteRule(NarrativeRuleSO rule, GameDateTime gameTime)
        {
            foreach (var change in rule.stateChanges)
                ApplyStateChange(change, rule.ruleId, gameTime);

            string template = rule.textTemplates[
                UnityEngine.Random.Range(0, rule.textTemplates.Count)];
            string text = FillTemplate(template, rule.variables, gameTime);

            _save.pendingChronicles.Add(new WorldChronicleEntry
            {
                entryId      = Guid.NewGuid().ToString(),
                gameDate     = gameTime.ToDisplayString(),
                eventId      = rule.ruleId,
                text         = text,
                hasBeenShown = false
            });

            _lastTriggered[rule.ruleId] = gameTime.ToKeyString();
            PersistCooldown(rule.ruleId, gameTime.ToKeyString());
            Debug.Log($"[NarrativeRuleEngine] Triggered: {rule.ruleId} → {text}");
        }

        // ── 状态变更执行 ──────────────────────────────
        private void ApplyStateChange(
            StateChangeInstruction change,
            string triggeredBy, GameDateTime gameTime)
        {
            // 若 useDelta=true，用当前值加上增量计算目标值
            string toValue = ResolveToValue(change);

            switch (change.targetType)
            {
                case "animal":
                    var animal = _registry.GetAnimal(change.targetId);
                    if (animal == null) break;
                    string oldAnimal = GetFieldValue("animal", change.targetId, change.field) ?? "";
                    EntityStateHelper.ChangeAnimalState(
                        animal, change.field, oldAnimal, toValue, triggeredBy, gameTime);
                    break;

                case "plant":
                    var plant = _registry.GetPlant(change.targetId);
                    if (plant == null) break;
                    if (change.isPermanent)
                        EntityStateHelper.AddPermanentDamage(
                            plant, change.field, change.permanentDescription, triggeredBy, gameTime);
                    else
                    {
                        string oldPlant = GetFieldValue("plant", change.targetId, change.field) ?? "";
                        EntityStateHelper.ChangePlantState(
                            plant, change.field, oldPlant, toValue, triggeredBy, gameTime);
                    }
                    break;

                case "location":
                    var loc = _registry.GetLocation(change.targetId);
                    if (loc == null) break;
                    if (change.isPermanent)
                        EntityStateHelper.AddPermanentTerrain(
                            loc, change.field, change.permanentDescription, triggeredBy, gameTime);
                    else
                    {
                        string oldLoc = GetFieldValue("location", change.targetId, change.field) ?? "";
                        EntityStateHelper.ChangeLocationState(
                            loc, change.field, oldLoc, toValue, triggeredBy, gameTime);
                    }
                    break;
            }
        }

        // useDelta=true 时读取当前值并加上增量，否则直接返回 toValue
        private string ResolveToValue(StateChangeInstruction change)
        {
            if (!change.useDelta) return change.toValue;
            string current = GetFieldValue(change.targetType, change.targetId, change.field);
            if (float.TryParse(current,
                    System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture,
                    out float f))
            {
                return UnityEngine.Mathf.Clamp01(f + change.deltaValue)
                           .ToString("F2", System.Globalization.CultureInfo.InvariantCulture);
            }
            return change.toValue;
        }

        // ── 模板填充 ─────────────────────────────────
        private string FillTemplate(
            string template, List<TemplateVariable> variables, GameDateTime gameTime)
        {
            string result = template;
            result = result.Replace("{date}",   gameTime.ToDisplayString());
            result = result.Replace("{season}", _rhythm?.season.ToString() ?? "");
            result = result.Replace("{sky}",    SkyPhrase.Pick(gameTime, _envState, _rhythm));

            foreach (var v in variables)
            {
                string val = v.sourceType == "fixed"
                    ? v.fixedValue
                    : GetFieldValue(v.sourceType, v.sourceId, v.sourceField) ?? "";
                result = result.Replace("{" + v.key + "}", val);
            }
            return result;
        }

    }
}
