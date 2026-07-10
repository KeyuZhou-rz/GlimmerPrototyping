using UnityEngine;
using GlimmerDiary.Flora;

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
    public EcosystemManager ecosystemManager;

    [Header("展示层平滑")]
    [Tooltip("指数平滑速率。Layer 2 的值是按天跳变的快照，展示层需要自己的过渡，独立于 E_env 自身的惯性。")]
    public float smoothingSpeed = 0.5f;

    [Header("雷暴门控（审美判断的保守起点）")]
    [Tooltip("风与雨都超过该阈值时雷暴才开始爬升——雷暴要稀有，不能常态化")]
    [Range(0f, 1f)] public float thunderWindThreshold = 0.6f;
    [Range(0f, 1f)] public float thunderRainThreshold = 0.6f;

    // 展示层平滑后的当前值（目标值来自世界状态快照）
    private float _rain;      // [-1 雨, +1 晴]，与 weatherController.rainIntensity 同语义
    private float _wind;      // [0,1]
    private float _thunder;   // [0,1]
    private float _dimness;   // [0,1]
    private float _valence;   // [-1,1]
    private float _arousal;   // [0,1]
    private bool _initialized;

    void Update()
    {
        var wm = WorldManager.Instance;
        if (wm == null) return;

        var env    = wm.GetWorldState();
        var rhythm = wm.GetRhythmState();
        var eEnv   = wm.WorldSave != null ? wm.WorldSave.currentEEnv : null;
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
        float valenceTarget = eEnv != null ? eEnv.V : 0f;
        float arousalTarget = eEnv != null ? eEnv.A : 0.3f;

        // —— 展示层平滑（指数趋近，帧率无关）——
        if (!_initialized)
        {
            // 首帧直接对齐，避免从 0 慢慢爬到当前世界状态
            _rain = rainTarget; _wind = windTarget; _thunder = thunderTarget;
            _dimness = dimnessTarget; _valence = valenceTarget; _arousal = arousalTarget;
            _initialized = true;
        }
        else
        {
            float k = 1f - Mathf.Exp(-smoothingSpeed * Time.deltaTime);
            _rain    = Mathf.Lerp(_rain,    rainTarget,    k);
            _wind    = Mathf.Lerp(_wind,    windTarget,    k);
            _thunder = Mathf.Lerp(_thunder, thunderTarget, k);
            _dimness = Mathf.Lerp(_dimness, dimnessTarget, k);
            _valence = Mathf.Lerp(_valence, valenceTarget, k);
            _arousal = Mathf.Lerp(_arousal, arousalTarget, k);
        }
    }

    void LateUpdate()
    {
        if (!_initialized) return;
        var wm = WorldManager.Instance;
        if (wm == null) return;

        if (weatherController != null && weatherController.allowExternalDrive)
        {
            weatherController.rainIntensity    = _rain;
            weatherController.windIntensity    = _wind;
            weatherController.thunderIntensity = _thunder;
            weatherController.dimness          = _dimness;
        }

        if (lightManager != null)
        {
            // dayProgress 不做平滑：它本身连续微变，平滑反而会在午夜 1→0 回绕处出错
            var rhythm = wm.GetRhythmState();
            lightManager.driveExternally = true;
            lightManager.SetTimePercent(rhythm.dayProgress);
        }

        if (ecosystemManager != null)
        {
            ecosystemManager.SetEmotionState(_valence, _arousal);
        }
    }
}
