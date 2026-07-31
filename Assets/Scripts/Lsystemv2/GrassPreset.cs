using UnityEngine;

namespace GlimmerDiary.Flora
{
    /// <summary>
    /// Authoritative tuning map for grass appearance and environmental color response.
    /// </summary>
    [CreateAssetMenu(fileName = "GrassPreset", menuName = "GlimmerDiary/Flora/Grass Preset")]
    public class GrassPreset : ScriptableObject
    {
        public string grassName = "Savanna Grass";

        [Header("Appearance")]
        public Color baseColor = new Color(0.7f, 0.6f, 0.3f);
        public Color tipColor = new Color(0.9f, 0.8f, 0.5f);
        public Gradient seasonalColorGradient;

        [Header("Shape")]
        [Range(0.1f, 2f)] public float heightMin = 0.3f;
        [Range(0.1f, 3f)] public float heightMax = 1.2f;
        [Range(0.01f, 0.2f)] public float bladeWidth = 0.05f;
        [Range(0f, 1f)] public float curvature = 0.3f;

        [Header("Wind Response")]
        [Range(0f, 1f)] public float windSensitivity = 0.7f;
        [Range(0.1f, 5f)] public float swaySpeed = 1.5f;

        [Header("Emotion Mapping")]
        [Tooltip("How grass responds to positive/negative emotion")]
        public AnimationCurve valenceToHealth;
        public AnimationCurve arousalToMotion;

        [Header("Batch 4 · 湿度 → 草色（逐簇，按 zone 土壤湿度）")]
        [Tooltip("干透时的草色偏向（乘算）——straw 金")]
        public Color moistureDryTint = new Color(1.06f, 0.98f, 0.72f);
        [Tooltip("饱水时的草色偏向（乘算）——青绿")]
        public Color moistureWetTint = new Color(0.66f, 1.00f, 0.58f);
        [Tooltip("湿度→绿度映射。低端做陡＝石边草雨量计灵敏度（少量雨即泛青），不改 L2 水文速率")]
        public AnimationCurve moistureToGreen = new AnimationCurve(
            new Keyframe(0f, 0f, 0f, 4f), new Keyframe(0.15f, 0.45f),
            new Keyframe(0.5f, 0.8f), new Keyframe(1f, 1f));

        [Header("Batch 4 · 季节 → 草色（yearProgress 连续混色，乘算）")]
        public Color seasonSpringTint = new Color(0.80f, 1.10f, 0.55f, 1f);
        public Color seasonSummerTint = new Color(0.825f, 1.03f, 0.55f, 1f);
        public Color seasonAutumnTint = new Color(1.18f, 0.85f, 0.50f, 1f);
        public Color seasonWinterTint = new Color(1.10f, 0.88f, 0.70f, 1f);

        [Header("Batch 4 · 旱枯黄（droughtDebt → 枯黄混入，Batch 2 欠账）")]
        public Color droughtTint = new Color(0.92f, 0.76f, 0.42f);
        [Tooltip("debt 响应区间起点（与 Batch 2 地裂阈值 0.3 同源）")]
        public float droughtStart = 0.3f;
        [Tooltip("debt 响应区间终点（到此全枯）")]
        public float droughtFull = 0.85f;
        [Range(0f, 1f)] public float droughtMaxBlend = 0.65f;

        [Header("Batch 4 · 衰败（DecayLevel → 去饱和＋压暗）")]
        [Range(0f, 1f)] public float decayDesaturate = 0.7f;
        [Range(0f, 1f)] public float decayDarken = 0.35f;
    }
}
