using System.Collections.Generic;
using UnityEngine;

public class LightManager : MonoBehaviour
{
    [SerializeField, Header("Managed Objects")] private Light DirectionalLight = null;
    [SerializeField] private LightPreset DayNightPreset, LampPreset;
    private List<Light> SpotLights = new List<Light>();

    [SerializeField, Range(0, 1440), Header("Modifiers"), Tooltip("The game's current time of day")] private float TimeOfDay;
    [SerializeField, Tooltip("Angle to rotate the sun")] private float SunDirection = 170f;
    [SerializeField, Tooltip("How fast time will go")] private float TimeMultiplier = 1;
    [SerializeField] private bool ControlLights = true;

    [Header("External Drive")]
    [Tooltip("为 true 时停止内部时钟自增，由外部（WorldAtmosphereBinder）经 SetTimePercent 驱动")]
    public bool driveExternally = false;

    [Header("Weather Dimming")]
    [Tooltip("坏天气压光因子 0..1（EmotionWeatherController 每帧写入）。ambient/光强的落地仍只经本组件——单写者纪律")]
    public float weatherDim = 0f;
    [SerializeField, Range(0f, 3f), Tooltip("压光强度锚点 = GlimmerVisualSetup 的正午基准。序列化而非首帧缓存：play 中域重载/光照 override 会把瞬时值污染成永久锚点")]
    private float baseSunIntensity = 1.15f;
    [SerializeField, Range(0f, 1f), Tooltip("暴雨时太阳强度余量比例（0.65 → 余 35%）")]
    private float sunDimScale = 0.65f;
    [SerializeField, Range(0f, 1f), Tooltip("暴雨时环境光余量比例（0.45 → 余 55%）")]
    private float ambientDimScale = 0.45f;

    [Header("Bounce Fill（反弹补光：太阳反方位的暖色无影平行光）")]
    [SerializeField] private Light BounceLight = null;
    [SerializeField, Range(0f, 1f), Tooltip("反弹强度基准（正午峰值）")]
    private float bounceIntensity = 0.25f;
    [SerializeField, Range(5f, 60f), Tooltip("反弹光仰角（贴地反弹感）")]
    private float bounceElevation = 30f;
    [SerializeField, Range(0f, 1f), Tooltip("暴雨时反弹光衰减比例")]
    private float bounceDimScale = 0.5f;

    [Header("Ambient Trilight（EmotionWeatherController 每帧喂入天空色板派生值）")]
    [Tooltip("朝上表面的环境色（天顶系）。EWC 未接管时用此默认值")]
    public Color ambientSky = new Color(0.55f, 0.62f, 0.72f);
    [Tooltip("侧向表面的环境色（中天/地平线系）")]
    public Color ambientEquator = new Color(0.52f, 0.48f, 0.40f);
    [Tooltip("朝下表面的环境色（土壤反弹系）")]
    public Color ambientGround = new Color(0.28f, 0.23f, 0.17f);

    private const float inverseDayLength = 1f / 1440f;

    /// <summary>当前场景实际日照系数 [0,1]：太阳仰角正弦（地平线以下=0，正午=1）。
    /// 由 UpdateLighting 每次求值时刷新——外部驱动/内部时钟/手动调试拖时间都算数，
    /// 读者（环境音昼夜 BGM）得到的是"此刻眼睛看到的日照"，不是任何钟。
    /// 注意：不含 weatherDim 压光（那是天气不是日照）。</summary>
    public float CurrentSunlight01 { get; private set; }

    /// <summary>
    /// 外部驱动入口：t01 为一天中的时刻（0=午夜，0.5=正午）。
    /// 仅在 driveExternally=true 时由绑定层调用；同步 TimeOfDay 便于 Inspector 观察。
    /// </summary>
    public void SetTimePercent(float t01)
    {
        if (DayNightPreset == null)
            return;

        t01 = Mathf.Repeat(t01, 1f);
        TimeOfDay = t01 * 1440f;
        UpdateLighting(t01);
    }

    /// <summary>
    /// On project start, if controlLights is true, collect all non-directional lights in the current scene and place in a list
    /// </summary>
    private void Start()
    {
        if (ControlLights)
        {
            Light[] lights = FindObjectsOfType<Light>();
            foreach (Light li in lights)
            {
                switch (li.type)
                {
                    case LightType.Disc:
                    case LightType.Point:
                    case LightType.Rectangle:
                    case LightType.Spot:
                        SpotLights.Add(li);
                        break;
                    case LightType.Directional:
                    default:
                        break;
                }
            }
        }
    }

    /// <summary>
    /// This method will not run if there is no preset set
    /// On each frame, this will calculate the current time of day factoring game time and the time multiplier (1440 is how many minutes exist in a day 24 x 60)
    /// Then send a time percentage to UpdateLighting, to evaluate according to the set preset, what that time of day should look like
    /// </summary>
    private void Update()
    {
        if (DayNightPreset == null)
            return;

        if (driveExternally)
            return;   // 外部驱动时跳过内部时钟自增，等待 SetTimePercent

        TimeOfDay = TimeOfDay + (Time.deltaTime * TimeMultiplier);
        TimeOfDay = TimeOfDay % 1440;
        UpdateLighting(TimeOfDay * inverseDayLength);
    }

    /// <summary>
    /// Based on the time percentage recieved, set the current scene's render settings and light coloring to the preset
    /// In addition, rotate the directional light (the sun) according to the current time
    /// </summary>
    /// <param name="timePercent"></param>
    private void UpdateLighting(float timePercent)
    {
        // C 轮：坏天气压光（weatherDim 由 EmotionWeatherController 写入）——
        // 雨幕是发光的灰纱，若场景光照不衰减，远山会比雾亮、雾读作"铅灰墙"；
        // 环境光随坏天气下沉后，闪电的 +5 余晖也终于有对比度可言（雷光不可见的另一半解药）。
        // 3A：环境光改 Trilight 三色落地（EWC 喂入天空色板派生值）——旧写法 ambientLight
        // 在 Trilight 模式下不生效（场景序列化即 Trilight），曾整条通路空转。
        if (RenderSettings.ambientMode != UnityEngine.Rendering.AmbientMode.Trilight)
            RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Trilight;
        float ambDim = 1f - weatherDim * ambientDimScale;
        RenderSettings.ambientSkyColor     = ambientSky * ambDim;
        RenderSettings.ambientEquatorColor = ambientEquator * ambDim;
        RenderSettings.ambientGroundColor  = ambientGround * ambDim;
        // 雾由 EmotionWeatherController 统一管理（按职责拆分），此处不再写 fogColor，避免互相覆盖。

        float sunElevRad = ((timePercent * 360f) - 90f) * Mathf.Deg2Rad;
        CurrentSunlight01 = Mathf.Clamp01(Mathf.Sin(sunElevRad));   // 日照单点赋值（AmbientAudio 读）

        //Set the directional light (the sun) according to the time percent
        if (DirectionalLight != null)
        {
            if (DirectionalLight.enabled == true)
            {
                DirectionalLight.intensity = baseSunIntensity * (1f - weatherDim * sunDimScale);
                DirectionalLight.color = DayNightPreset.DirectionalColour.Evaluate(timePercent);
                DirectionalLight.transform.localRotation = Quaternion.Euler(new Vector3((timePercent * 360f) - 90f, SunDirection, 0));
            }
        }

        // 3B：反弹补光——太阳反方位、贴地仰角的暖色无影平行光，托住树/石的阴影侧
        // （基准图的"墙面反弹"层）。颜色取环境光 equator/ground 混合，随天气昼夜自变；
        // 夜里随太阳落山熄灭，暴雨随 weatherDim 同沉。
        if (BounceLight != null)
        {
            float dayFac = Mathf.Clamp01(Mathf.Sin(sunElevRad) * 2.5f);
            BounceLight.transform.localRotation = Quaternion.Euler(bounceElevation, SunDirection + 180f, 0f);
            BounceLight.color = Color.Lerp(ambientGround, ambientEquator, 0.6f);
            BounceLight.intensity = bounceIntensity * dayFac * (1f - weatherDim * bounceDimScale);
        }

        //Go through each spot light, ensure it is active, and set it's color accordingly
        foreach (Light lamp in SpotLights)
        {
            if (lamp != null)
            {
                if (lamp.isActiveAndEnabled && lamp.shadows != LightShadows.None && LampPreset != null)
                {
                    lamp.color = LampPreset.DirectionalColour.Evaluate(timePercent);
                }
            }
        }
    }
}