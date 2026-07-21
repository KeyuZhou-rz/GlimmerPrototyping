using UnityEngine;
using GlimmerDiary.Ecosystem_nonL;
/// <summary>
/// Layer 3 绑定层：世界状态（只读） → 视觉组件公开字段。
/// 每帧读 WorldManager 的只读入口，算目标值并做一层展示层平滑，
/// LateUpdate 写进 EmotionWeatherController / LightManager / EcosystemManager。
/// 纪律：绝不写世界状态、绝不调用任何 Layer 2 模拟/注入入口；
/// 不导入 GlimmerDiary.Core（返回值一律用 var 承接）。
/// </summary>
public class WorldAtmosphereBinder : MonoBehaviour
{
    [Header("绑定目标（场景现有组件，缺省则跳过对应通路）")]
    public EmotionWeatherController weatherController;
    public LightManager lightManager;
    public EcosystemManager treePlacement;

    [Header("展示层平滑")]
    [Tooltip("指数平滑速率。Layer 2 的值是按天跳变的快照，展示层需要自己的过渡，独立于 E_env 自身的惯性。")]
    public float smoothingSpeed = 0.5f;

    [Header("雷暴门控（审美判断的保守起点）")]
    [Tooltip("风与雨都超过该阈值时雷暴才开始爬升——雷暴要稀有，不能常态化")]
    [Range(0f, 1f)] public float thunderWindThreshold = 0.6f;
    [Range(0f, 1f)] public float thunderRainThreshold = 0.6f;

    [Header("水面升降(riverbank 水量 → Water transform)")]
    public WaterGenerator waterSurface;
    [Tooltip("驱动水面的 location id(河道空间上贴着河岸区)")]
    public string waterSourceLocationId = "riverbank";
    [Tooltip("水位是日积分慢变量,比天气 0.5 慢一个量级(τ≈20s):水面垂直位移比颜色更扎眼,必须爬不能跳")]
    public float waterSmoothingSpeed = 0.05f;

    // 展示层平滑后的当前值（目标值来自世界状态快照）
    private float _rain;      // [-1 雨, +1 晴]，与 weatherController.rainIntensity 同语义
    private float _wind;      // [0,1]
    private float _thunder;   // [0,1]
    private float _dimness;   // [0,1]
    private float _starVis;   // [0,1]
    private bool _initialized;
    private float _waterLevel;       // [0,1] 平滑后的水量
    private bool _waterInitialized;  // 独立首帧对齐:location 可能晚于全局状态就绪

    void Update()
    {
        var wm = WorldManager.Instance;
        if (wm == null) return;

        var env    = wm.GetWorldState();
        var rhythm = wm.GetRhythmState();
        if (env == null || rhythm == null) return;

        // —— 目标值计算（映射表见 Docs/AmbientAtmosphereBinding.md）——
        // Rainfall [0,1] → rainIntensity [-1 雨, +1 晴]：符号相反，需要翻转
        float rainTarget = 1f - 2f * env.Rainfall;
        float windTarget = env.WindSpeed;

        // 雷暴：风×雨乘积，双阈值门控；两者都到 0.6 时从 0 开始爬升，全满时为 1
        float thunderTarget = 0f;
        if (env.WindSpeed > thunderWindThreshold && env.Rainfall > thunderRainThreshold)
        {
            float gate = thunderWindThreshold * thunderRainThreshold;
            thunderTarget = Mathf.Clamp01((env.WindSpeed * env.Rainfall - gate) / (1f - gate));
        }

        float dimnessTarget = env.FogDensity;
        float starVisTarget = env.StarVisibility;

        // —— 展示层平滑（指数趋近，帧率无关）——
        if (!_initialized)
        {
            // 首帧直接对齐，避免从 0 慢慢爬到当前世界状态
            _rain = rainTarget; _wind = windTarget; _thunder = thunderTarget;
            _dimness = dimnessTarget;
            _starVis = starVisTarget;
            _initialized = true;
        }
        else
        {
            float k = 1f - Mathf.Exp(-smoothingSpeed * Time.deltaTime);
            _rain    = Mathf.Lerp(_rain,    rainTarget,    k);
            _wind    = Mathf.Lerp(_wind,    windTarget,    k);
            _thunder = Mathf.Lerp(_thunder, thunderTarget, k);
            _dimness = Mathf.Lerp(_dimness, dimnessTarget, k);
            _starVis = Mathf.Lerp(_starVis, starVisTarget, k);
        }

        // —— 水位通路(独立首帧对齐;loc 缺失则静默跳过,保持现值不驱动向假默认)——
        var loc = wm.Registry.GetLocation(waterSourceLocationId);
        if (loc != null)
        {
            float waterTarget = loc.waterLevel;   // [0,1],L2 日积分慢变量
            if (!_waterInitialized)
            {
                _waterLevel = waterTarget;
                _waterInitialized = true;
            }
            else
            {
                float kw = 1f - Mathf.Exp(-waterSmoothingSpeed * Time.deltaTime);
                _waterLevel = Mathf.Lerp(_waterLevel, waterTarget, kw);
            }
        }
    }

    void LateUpdate()
    {
        
        if (!_initialized) return;
        var wm = WorldManager.Instance;
        if (wm == null) return;
        var env = wm.GetWorldState();
        if (weatherController != null && weatherController.allowExternalDrive)
        {
            weatherController.rainIntensity    = _rain;
            weatherController.windIntensity    = _wind;
            weatherController.thunderIntensity = _thunder;
            weatherController.dimness          = _dimness;
            weatherController.starVisibility   = _starVis;
        }

        if (lightManager != null)
        {
            // dayProgress 不做平滑：它本身连续微变，平滑反而会在午夜 1→0 回绕处出错
            var rhythm = wm.GetRhythmState();
            lightManager.driveExternally = true;
            lightManager.SetTimePercent(rhythm.dayProgress);
        }

        if (treePlacement != null)
        {
            if (env != null)
                treePlacement.SetRainfall(env.Rainfall);
        }

        if (waterSurface != null && _waterInitialized)
            waterSurface.SetDisplayLevel01(_waterLevel);
    }
}
