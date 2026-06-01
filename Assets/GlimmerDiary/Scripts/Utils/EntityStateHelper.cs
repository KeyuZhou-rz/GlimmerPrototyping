using GlimmerDiary.Data;

namespace GlimmerDiary.Utils
{
    public static class EntityStateHelper
    {
        // 变更动物实体的一个字段，并追加历史记录
        // 用法：
        //   EntityStateHelper.ChangeAnimalState(
        //       vole, "location", "lowland", "highland_east",
        //       "flood_level_high", worldTime);
        public static void ChangeAnimalState(
            AnimalEntity entity,
            string field, string fromVal, string toVal,
            string triggeredBy, GameDateTime time)
        {
            switch (field)
            {
                case "location":        entity.location        = toVal;              break;
                case "facingDirection": entity.facingDirection = toVal;              break;
                case "familyState":     entity.familyState     = toVal;              break;
                case "isPresent":       entity.isPresent       = bool.Parse(toVal);  break;
                case "primaryPath":     entity.primaryPath     = toVal;              break;
                case "lastSeenDate":    entity.lastSeenDate    = toVal;              break;
            }

            entity.history.Add(new StateChangeRecord
            {
                date        = time.ToKeyString(),
                field       = field,
                fromValue   = fromVal,
                toValue     = toVal,
                triggeredBy = triggeredBy
            });
        }

        // 变更植物实体的一个字段，并追加历史记录
        public static void ChangePlantState(
            PlantEntity entity,
            string field, string fromVal, string toVal,
            string triggeredBy, GameDateTime time)
        {
            switch (field)
            {
                case "isAlive":       entity.isAlive       = bool.Parse(toVal);   break;
                case "isFlowering":   entity.isFlowering   = bool.Parse(toVal);   break;
                case "growthStage":   entity.growthStage   = float.Parse(toVal);  break;
                case "location":      entity.location      = toVal;               break;
                case "lastFlowerDate":entity.lastFlowerDate= toVal;               break;
            }

            entity.history.Add(new StateChangeRecord
            {
                date        = time.ToKeyString(),
                field       = field,
                fromValue   = fromVal,
                toValue     = toVal,
                triggeredBy = triggeredBy
            });
        }

        // 永久损伤单独记录，不走普通 history（不可逆）
        public static void AddPermanentDamage(
            PlantEntity entity,
            string damageType, string description,
            string triggeredBy, GameDateTime time)
        {
            entity.permanentDamages.Add(new PermanentDamageRecord
            {
                date        = time.ToKeyString(),
                damageType  = damageType,
                description = description,
                triggeredBy = triggeredBy
            });
        }

        // 永久地貌变化单独记录，不走普通 history（不可逆）
        public static void AddPermanentTerrain(
            LocationEntity location,
            string changeType, string description,
            string triggeredBy, GameDateTime time)
        {
            location.permanentChanges.Add(new PermanentTerrainRecord
            {
                date        = time.ToKeyString(),
                changeType  = changeType,
                description = description,
                triggeredBy = triggeredBy
            });
        }
    }
}
