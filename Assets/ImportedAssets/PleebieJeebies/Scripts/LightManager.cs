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

    private const float inverseDayLength = 1f / 1440f;

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
        RenderSettings.ambientLight = DayNightPreset.AmbientColour.Evaluate(timePercent) * (1f - weatherDim * ambientDimScale);
        // 雾由 EmotionWeatherController 统一管理（按职责拆分），此处不再写 fogColor，避免互相覆盖。

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