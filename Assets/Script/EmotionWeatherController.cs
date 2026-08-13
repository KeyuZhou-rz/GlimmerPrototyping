using UnityEngine;
using System.Collections;

public class EmotionWeatherController : MonoBehaviour
{
    [Header("外部驱动")]
    [Tooltip("勾选时由 WorldAtmosphereBinder 按世界状态每帧覆写下面的滑条；取消勾选即可手动调参（调试用）")]
    public bool allowExternalDrive = true;

    [Header("核心输入(雨量，风力，雷电)")]
    [Range(-1f, 1f)]
    public float rainIntensity = 0f;
    [Range(0f, 1f)]
    public float windIntensity = 0f;
    [Range(0f, 1f)]
    public float thunderIntensity = 0f;
    [Range(0f, 1f)]
    public float starVisibility = 0f;
    [Header("晦明（雾的情绪分量，独立于降雨）")]
    [Range(0f, 1f)]
    public float dimness = 0f;               // 默认 0：未接绑定层的场景雾公式退化回原样

    [Header("侧翼尘霾（V1 D8 风通道：西翼旱情 → 地平线尘霾，参数级零新美术）")]
    [Range(0f, 1f)] public float dustHaze = 0f;        // 外部驱动（WorldAtmosphereBinder），默认 0=无影响
    public Color dustTint = new(0.78f, 0.66f, 0.48f);  // 干燥尘土的黄褐
    [Range(0f, 1f)] public float dustFogColorBlend = 0.5f;   // 满霾时雾色向尘土色的混入上限
    [Range(0f, 1f)] public float dustFogCloseIn = 0.35f;     // 满霾时雾距收拢强度（0=不收）
    [Range(0f, 1f)]
    public float dimnessFogWeight = 0.45f;   // dimness=1 时雾距向暴雨端额外收拢的比例

    [Header("雨水系统设置")]
    public ParticleSystem rainParticleSystem;
    public AudioSource rainAudioSource;
    public float maxRainEmission = 1400f;
    public float minRainSize = 0.05f;       // 细雨雨丝宽度
    public float maxRainSize = 0.09f;       // 暴雨雨丝宽度
    [Header("雨滴风感设置")]
    [Tooltip("世界空间风向（水平分量）。主相机沿 +Z 看，X 分量才是画面里可见的斜雨方向")]
    public Vector3 windDirection = new Vector3(1f, 0f, 0.25f);
    public float maxRainWindForce = 9f;     // 满风时的恒定横向力（把整幕雨吹斜）
    public float maxRainTurbulenceZ = 1.2f; // 满风时的低频摆动幅度
    public float turbulenceFrequency = 0.2f; // 湍流频率：低频=整幕缓摆，高频=雨丝乱抖

    [Header("风力系统")]
    public WindZone sceneWindZone;
    public float maxWindMain = 2.0f;

    [Header("种子絮系统（§5.3 蒲公英：玩家可见的风——风峰日絮从河岸往下风飘）")]
    public ParticleSystem seedFluffParticleSystem;
    [Range(0f, 1f), Tooltip("由 WorldAtmosphereBinder 按世界状态推送（风峰 ∧ 蒲公英在场开花）")]
    public float seedFluffRate = 0f;
    [Tooltip("满率时的发射速率——'一阵絮'不是'一场雨'，稀疏才读得出个别绒点")]
    public float maxFluffEmission = 8f;
    [Tooltip("满风时的横向推力（比雨轻得多：絮是飘不是砸）")]
    public float maxFluffWindForce = 0.45f;


    [Header("雷电系统")]
    public Light lightingLight;
    public AudioSource thunderAudioSource;
    public AudioClip[] thunderClips;
    public float minThunderDelay = 5f;
    public float maxThunderDelay = 30f;

    [Header("雷电视觉细节")]
    public float mainFlashIntensity = 5.0f;
    public float flickerIntensity = 2.0f;
    public int flickerAmount = 3;

    [Header("天空与能见度 (Fog)")]
    [Tooltip("雾色亮度相对天空地平线辉带的比例（雾色=辉带派生，见 UpdateRain 雾色段）")]
    [Range(0f, 1f)] public float fogSkyScale = 0.85f;
    [Tooltip("雾色去饱和量——空气散射比天空颜料灰一档，防黄昏雾橙成霓虹")]
    [Range(0f, 1f)] public float fogSkyGreyAmount = 0.35f;
    [Tooltip("昼夜相位/雾亮度推算用的平行光（留空自动取 RenderSettings.sun）")]
    public Light sunForFogBrightness;
    [Tooltip("坏天气压光的落地者（C 轮）。留空启动时自动查找")]
    public LightManager lightManager;

    [Header("线性雾可见距离")]
    public float fogLinearSunnyStart = 60f;
    public float fogLinearSunnyEnd = 300f;   // 推出地形对角线(~226m):远山只柔化不溶解
    public float fogLinearStormStart = 30f;
    public float fogLinearStormEnd = 140f;   // 风暴仍重,但中景可读

    [Header("过渡速度")]
    public float transitionSpeed = 2f;

    private float smoothedRainIntensity = 0f;
    private float smoothedSunIntensity = 0f;
    private float currentWind = 0f;

    private float lightingTimer = 0f;
    private bool isFlashing = false;

    [Header("天空盒（Glimmer/SkyGradient — 岩画天空，见 Docs/SkySanRockArt.md）")]
    [Tooltip("SkyGradient.mat。控制器是该材质运行时属性的唯一写者（单写者纪律）")]
    public Material skyboxMaterial;

    // 四相色板（TLD/KRZ 完成度轮）：每相 zenith 天顶 / mid 中天 / glow 地平辉带。
    // 相位权重由真实太阳仰角导出，天空不再依赖"单一色板×乘法压暗"。
    [Header("天空 · 白天（草原碧空:深碧天顶/澄蓝中天/暖尘辉线,2026-07-18 脱离苍白 demo 基调）")]
    public Color daySkyZenith = new Color(0.22f, 0.44f, 0.70f);
    public Color daySkyMid    = new Color(0.45f, 0.64f, 0.78f);
    public Color daySkyGlow   = new Color(0.86f, 0.78f, 0.60f);

    [Header("天空 · 黄昏/黎明（戏剧带·残阳参考：紫天顶/玫瑰中天/热珊瑚辉带）")]
    public Color goldSkyZenith = new Color(0.40f, 0.33f, 0.50f);   // 灰紫(残阳参考上天空)
    public Color goldSkyMid    = new Color(0.82f, 0.44f, 0.50f);   // 玫瑰
    public Color goldSkyGlow   = new Color(1.00f, 0.52f, 0.30f);   // 热珊瑚橙

    [Header("天空 · 夜（深海军，有色相立场）")]
    public Color nightSkyZenith = new Color(0.09f, 0.12f, 0.19f);
    public Color nightSkyMid    = new Color(0.13f, 0.16f, 0.22f);
    public Color nightSkyGlow   = new Color(0.24f, 0.22f, 0.20f);   // airglow 暖灰微光，夜地平线不死黑

    [Header("天空 · 暴雨")]
    public Color stormSkyZenith = new Color(0.20f, 0.23f, 0.28f);
    public Color stormSkyMid    = new Color(0.28f, 0.30f, 0.34f);
    public Color stormSkyGlow   = new Color(0.38f, 0.38f, 0.38f);
    [Range(0f, 3f)] public float sunnySunGlow = 0.9f;
    [Range(0f, 3f)] public float stormSunGlow = 0.15f;

    [Header("天空 · 日侧暖洗（TLD 式方位不对称）")]
    public Color goldenWashColor = new Color(1.0f, 0.60f, 0.38f);
    [Range(0f, 1f)] public float goldenWashStrength = 0.45f;
    public Color dayWashColor = new Color(0.90f, 0.82f, 0.68f);
    [Range(0f, 1f)] public float dayWashStrength = 0.15f;

    [Header("天空 · 残阳晕染（低日角点燃:日侧白热核+珊瑚宽带,反日粉紫维纳斯带）")]
    public Color dayHaloColor  = new Color(0.95f, 0.75f, 0.55f);  // 白天低日角(罕见)
    public Color goldHaloColor = new Color(1.00f, 0.48f, 0.28f);  // 热珊瑚
    [Range(0f, 1f)] public float haloStrength = 0.90f;
    public Color antiGlowColor = new Color(0.80f, 0.42f, 0.52f);  // 粉紫余晖拱
    [Range(0f, 1f)] public float antiGlowStrength = 0.65f;
    [Tooltip("日盘颜料:平时赭橙,低日角白热化(沉日的白热核)")]
    public Color sunTintDay = new Color(1.0f, 0.72f, 0.42f);
    public Color sunTintLow = new Color(1.0f, 0.88f, 0.70f);

    [Header("天空 · 月轮图腾（方向/相位按时间推算，一月=一朔望月）")]
    [Range(0f, 2f), Tooltip("满月亮度基准；黄昏自动衰减，暴雨按 stormStarHide 遮蔽")]
    public float moonGlowStrength = 1.0f;

    [Header("天空 · 色带与笔触（相位驱动）")]
    [Range(0f, 1f), Tooltip("色带量化强度；storm 相自动减半防等值线感")]
    public float bandingAmount = 0.55f;
    [Tooltip("笔触斑驳强度 per 相：day/golden/night/storm")]
    public float strokeDay = 0.05f;
    public float strokeGolden = 0.10f;
    public float strokeNight = 0.02f;
    public float strokeStorm = 0.16f;

    [Header("夜雾色相（已废弃删除：雾色现由天空色板派生，夜相位由 nightSkyGlow 表达）")]
    // （字段已移除——场景 YAML 里的残留序列化值会被 Unity 静默忽略，无副作用）

    [Tooltip("坏天气把星空遮掉的比例：暴雨云层下不该满天星")]
    [Range(0f, 1f)] public float stormStarHide = 0.85f;

    [Header("环境光 · 三色派生（经 LightManager 落地 Trilight）")]
    [Tooltip("朝下表面的土壤反弹色（黄昏/夜晚自动随天色板变暗）")]
    public Color ambientGroundTint = new Color(0.35f, 0.27f, 0.18f);
    [Range(0f, 2f)] public float ambientSkyBoost = 1.20f;     // 天顶色抬亮：朝上表面吃天色
    [Range(0f, 2f)] public float ambientEquatorBoost = 1.10f; // 中天/地平线混合：侧向表面
    [Range(0f, 2f)] public float ambientGroundBoost = 0.70f;  // 地面反弹压暗：朝下表面

    // UpdateRain 每帧算好的雾色/昼夜因子，UpdateSkybox 复用（同一帧内先 Rain 后 Skybox）
    private Color _fogColThisFrame;
    private float _dayLightThisFrame = 1f;

    // ComputeSkyColors 每帧一次（UpdateRain 头部调用）：天空相位色板与坏天气度，
    // 雾色派生（UpdateRain）与天空写入（UpdateSkybox）共用同一份——天雾同色恒等式的载体
    private float _badTThisFrame;
    private float _wNightThisFrame, _wDayThisFrame, _wGoldThisFrame;
    private Color _zenithThisFrame, _midThisFrame, _glowThisFrame;

    /// <summary>
    /// 相位权重（真实太阳仰角 → 夜/黄昏/白天）× 四相色板混合 → 暴雨端拉。
    /// 是雾色与天空色的共同单一来源。
    /// </summary>
    private void ComputeSkyColors()
    {
        _badTThisFrame = Mathf.Clamp01(smoothedRainIntensity + dimness * dimnessFogWeight);

        var sun = sunForFogBrightness != null ? sunForFogBrightness : RenderSettings.sun;
        float sunY = sun != null ? -sun.transform.forward.y : 0.5f;
        _wNightThisFrame = 1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(-0.12f, 0.02f, sunY));
        _wDayThisFrame  = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.10f, 0.35f, sunY));
        _wGoldThisFrame = Mathf.Clamp01(1f - _wNightThisFrame - _wDayThisFrame);

        Color zenith = daySkyZenith * _wDayThisFrame + goldSkyZenith * _wGoldThisFrame + nightSkyZenith * _wNightThisFrame;
        Color mid    = daySkyMid    * _wDayThisFrame + goldSkyMid    * _wGoldThisFrame + nightSkyMid    * _wNightThisFrame;
        Color glow   = daySkyGlow   * _wDayThisFrame + goldSkyGlow   * _wGoldThisFrame + nightSkyGlow   * _wNightThisFrame;
        _zenithThisFrame = Color.Lerp(zenith, stormSkyZenith, _badTThisFrame);
        _midThisFrame    = Color.Lerp(mid,    stormSkyMid,    _badTThisFrame);
        _glowThisFrame   = Color.Lerp(glow,   stormSkyGlow,   _badTThisFrame);

        // C 轮：坏天气压光因子交给 LightManager 落地（ambient/光强的写入仍只经 LightManager——单写者纪律）
        if (lightManager == null) lightManager = FindFirstObjectByType<LightManager>();
        if (lightManager != null)
        {
            lightManager.weatherDim = _badTThisFrame;

            // 3A：三色环境光派生——与天空/雾同一色板来源，天-雾-环境光三色一体。
            // sky=天顶色（朝上表面吃天色），equator=中天偏地平线（侧向），ground=土壤反弹（朝下）
            lightManager.ambientSky     = _zenithThisFrame * ambientSkyBoost;
            lightManager.ambientEquator = Color.Lerp(_midThisFrame, _glowThisFrame, 0.45f) * ambientEquatorBoost;
            lightManager.ambientGround  = Color.Lerp(_fogColThisFrame, ambientGroundTint, 0.55f) * ambientGroundBoost;
        }
    }


    void Awake()
    {
        // 开局打雷根因（2026-08-13）：场景里雷声源勾着 PlayOnAwake 且 m_Resource 直接挂了
        // 雷片段（雨声源同款挂着雨循环）——场景加载即炸一声雷，与天气完全无关。
        // Start 再摘旗为时已晚（音频在场景加载期已起播）。这里 Awake 即停即摘旗做双保险：
        // 此后雷声只由 UpdateThunder→ThunderPlay 按 thunderIntensity 触发，
        // 雨声由 UpdateRain 按雨量 Play/Stop（雨天回归时音量从 0 爬入，反而更自然）。
        if (thunderAudioSource != null)
        {
            thunderAudioSource.playOnAwake = false;
            if (thunderAudioSource.isPlaying) thunderAudioSource.Stop();
        }
        if (rainAudioSource != null)
        {
            rainAudioSource.playOnAwake = false;
            if (rainAudioSource.isPlaying) rainAudioSource.Stop();
        }
    }

    void Start()
    {
        if (lightingLight != null)
        {
            lightingLight.enabled = false;
            lightingLight.intensity = 0f;
        }

        // 初始化雷电音频源
        if (thunderAudioSource != null)
        {
            thunderAudioSource.playOnAwake = false;
            thunderAudioSource.loop = false;
            // 确保音量和空间混合设置正确
            if (thunderAudioSource.volume == 0f)
            {
                thunderAudioSource.volume = 1f;
            }
        }
        else
        {
            Debug.LogWarning("Thunder AudioSource is not assigned!");
        }

        // 验证音频片段
        if (thunderClips == null || thunderClips.Length == 0)
        {
            Debug.LogWarning("No thunder audio clips assigned!");
        }

        lightingTimer = Random.Range(minThunderDelay, maxThunderDelay);

        // 雨粒子初始化
        if (rainParticleSystem != null)
        {
            var emission = rainParticleSystem.emission;
            emission.SetBursts(new ParticleSystem.Burst[0]);
            emission.rateOverTime = 0f;

            var main = rainParticleSystem.main;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.loop = true;

            main.maxParticles = Mathf.Max(main.maxParticles, 10000);

            if (!rainParticleSystem.isPlaying)
                rainParticleSystem.Play();
        }

        // 种子絮粒子初始化（同雨：清空突发、速率归零、世界空间）
        if (seedFluffParticleSystem != null)
        {
            var fEmission = seedFluffParticleSystem.emission;
            fEmission.SetBursts(new ParticleSystem.Burst[0]);
            fEmission.rateOverTime = 0f;

            var fMain = seedFluffParticleSystem.main;
            fMain.simulationSpace = ParticleSystemSimulationSpace.World;
            fMain.loop = true;

            if (!seedFluffParticleSystem.isPlaying)
                seedFluffParticleSystem.Play();
        }


        // 初始雾设置：晴天端占位（首帧 UpdateRain 即按天空色板派生覆写）
        RenderSettings.fog = true;
        RenderSettings.fogMode = FogMode.Linear;
        RenderSettings.fogColor = daySkyGlow * 0.85f;
        RenderSettings.fogStartDistance = fogLinearSunnyStart;
        RenderSettings.fogEndDistance = fogLinearSunnyEnd;
    }

    void Update()
    {
        UpdateWeather();
    }

    private void UpdateWeather()
    {
        UpdateRain();
        UpdateWind();
        UpdateThunder();
        UpdateSkybox();
        UpdateSeedFluff();
    }

    // -----------------------------
    //         种子絮系统
    // -----------------------------
    // 驱动：seedFluffRate 由 binder 推送（风峰 ∧ 蒲公英在场开花）；风向复用
    // 雨的风向常量——世界里风只有一个方向，絮飘向与"落种 lowland"的叙事同向。
    private void UpdateSeedFluff()
    {
        if (seedFluffParticleSystem == null) return;

        var emission = seedFluffParticleSystem.emission;
        emission.rateOverTime = Mathf.Lerp(0f, maxFluffEmission, seedFluffRate);

        // 整幕同向缓推（同 UpdateRain 的风力模式，力度小一个量级）
        var force = seedFluffParticleSystem.forceOverLifetime;
        bool wantForce = currentWind > 0.01f;
        force.enabled = wantForce;
        if (wantForce)
        {
            Vector3 wind = windDirection.sqrMagnitude > 1e-4f
                ? new Vector3(windDirection.x, 0f, windDirection.z).normalized
                : Vector3.right;
            float f = Mathf.Lerp(0f, maxFluffWindForce, currentWind);
            force.space = ParticleSystemSimulationSpace.World;
            force.x = wind.x * f;
            force.y = 0f;
            force.z = wind.z * f;
        }

        // 低频缓摆：絮是浮的——比雨更慢更柔，且允许上下浮动
        var noise = seedFluffParticleSystem.noise;
        bool wantNoise = currentWind > 0.1f;
        noise.enabled = wantNoise;
        if (wantNoise)
        {
            noise.separateAxes = true;
            noise.strengthX = new ParticleSystem.MinMaxCurve(0.5f * currentWind);
            noise.strengthY = new ParticleSystem.MinMaxCurve(0.25f * currentWind);
            noise.strengthZ = new ParticleSystem.MinMaxCurve(0.3f * currentWind);
            noise.frequency = 0.12f;
            noise.damping = true;
            noise.scrollSpeed = 0.2f;
            noise.quality = ParticleSystemNoiseQuality.Medium;
        }

        if (seedFluffRate > 0.01f && !seedFluffParticleSystem.isPlaying)
            seedFluffParticleSystem.Play();
        else if (seedFluffRate <= 0.01f && seedFluffParticleSystem.isPlaying)
            seedFluffParticleSystem.Stop();
    }

    // -----------------------------
    //         雨系统
    // -----------------------------
    private void UpdateRain()
    {
        float targetRain = Mathf.Clamp01(-rainIntensity);
        float targetSun = Mathf.Clamp01(rainIntensity);

        smoothedRainIntensity = Mathf.MoveTowards(smoothedRainIntensity, targetRain, transitionSpeed * Time.deltaTime);
        smoothedSunIntensity = Mathf.MoveTowards(smoothedSunIntensity, targetSun, transitionSpeed * Time.deltaTime);

        if (rainParticleSystem != null)
        {
            var emission = rainParticleSystem.emission;
            var main = rainParticleSystem.main;

            emission.rateOverTime = Mathf.Lerp(0f, maxRainEmission, smoothedRainIntensity);
            // 雨丝宽度：小雨细、暴雨略粗；长度由渲染器 lengthScale 拉伸控制
            main.startSize = Mathf.Lerp(minRainSize, maxRainSize, smoothedRainIntensity);
            // 下落速度：小雨徐、暴雨急（真实雨终速 ~9m/s，风格化取 12~20）
            main.startSpeed = Mathf.Lerp(12f, 20f, smoothedRainIntensity);

            // 恒定风力：自然的雨是整幕同向倾斜，而不是逐滴乱抖
            var force = rainParticleSystem.forceOverLifetime;
            bool wantForce = currentWind > 0.01f;
            force.enabled = wantForce;
            if (wantForce)
            {
                Vector3 wind = windDirection.sqrMagnitude > 1e-4f
                    ? new Vector3(windDirection.x, 0f, windDirection.z).normalized
                    : Vector3.right;
                float f = Mathf.Lerp(0f, maxRainWindForce, currentWind);
                force.space = ParticleSystemSimulationSpace.World;
                force.x = wind.x * f;
                force.y = 0f;
                force.z = wind.z * f;
            }

            // 低频湍流：风向平面内一层缓摆（阵风感）。频率必须低，高频会让雨丝抖成噪点
            var noise = rainParticleSystem.noise;
            bool wantNoise = currentWind > 0.15f;
            noise.enabled = wantNoise;
            if (wantNoise)
            {
                float turb = Mathf.Lerp(0f, maxRainTurbulenceZ, currentWind);
                noise.separateAxes = true;
                noise.strengthX = new ParticleSystem.MinMaxCurve(turb);
                noise.strengthY = 0f;   // 雨不该上下飘
                noise.strengthZ = new ParticleSystem.MinMaxCurve(turb * 0.4f);
                noise.frequency = turbulenceFrequency;
                noise.damping = true;
                noise.scrollSpeed = 0.35f;
                noise.quality = ParticleSystemNoiseQuality.Medium;
            }

            if (smoothedRainIntensity > 0.01f && !rainParticleSystem.isPlaying)
                rainParticleSystem.Play();
            else if (smoothedRainIntensity <= 0.01f && rainParticleSystem.isPlaying)
                rainParticleSystem.Stop();
        }

        if (rainAudioSource != null)
        {
            float targetVolume = smoothedRainIntensity;
            rainAudioSource.volume = Mathf.MoveTowards(rainAudioSource.volume, targetVolume, transitionSpeed * Time.deltaTime);

            if (targetVolume > 0.01f && !rainAudioSource.isPlaying)
                rainAudioSource.Play();
            else if (targetVolume <= 0.01f && rainAudioSource.isPlaying)
                rainAudioSource.Stop();
        }
        // —— 雾：统一线性雾 ——
        // 天空相位色板先行（每帧一次）：雾色与天空色都从它派生，天雾同色成为恒等式
        ComputeSkyColors();

        // 坏天气程度 = 雨强 ⊕ 晦明（dimness 按权重折算，只影响雾，不影响雨粒子）
        float fogT = _badTThisFrame;

        float fogStart = Mathf.Lerp(fogLinearSunnyStart, fogLinearStormStart, fogT);
        float fogEnd   = Mathf.Lerp(fogLinearSunnyEnd,   fogLinearStormEnd,   fogT);

        // 侧翼尘霾（D8）：与本地天气正交——大晴天也可以挂霾（旱在西翼，不在你这里）。
        // 雾距向近端收拢一档：远山被霾吃掉，地平线"那边有事情"但读不出是什么。
        if (dustHaze > 0.001f)
        {
            fogStart *= Mathf.Lerp(1f, 0.65f, dustHaze * dustFogCloseIn);
            fogEnd   *= Mathf.Lerp(1f, 0.60f, dustHaze * dustFogCloseIn);
        }
        fogStart = Mathf.Clamp(fogStart, 0f, fogEnd - 0.01f);

        RenderSettings.fogMode          = FogMode.Linear;
        RenderSettings.fogStartDistance = fogStart;
        RenderSettings.fogEndDistance   = fogEnd;

        // 雾色派生自本帧天空地平线辉带（2026-07-27 B 轮）：
        // 空气透视的本质是空气发光(airlight)——远山应变亮地溶进光亮的空气，
        // 独立雾色常量必在某个相位脱节（旧雨雾亮度 0.22 的"铅灰墙"即此根因）。
        // 派生路径：辉带色 → 去饱和一档（空气比颜料灰）→ 压暗一档（雾不亮过天空）。
        Color glow = _glowThisFrame;
        float lum = glow.r * 0.299f + glow.g * 0.587f + glow.b * 0.114f;
        Color fogCol = Color.Lerp(glow, new Color(lum, lum, lum), fogSkyGreyAmount) * fogSkyScale;
        // 尘霾偏色：向尘土黄褐混入，随天空亮度缩放——夜里不发光，黄昏最显（残阳照尘）
        if (dustHaze > 0.001f)
            fogCol = Color.Lerp(fogCol, dustTint * lum, dustHaze * dustFogColorBlend);
        RenderSettings.fogColor = fogCol;

        // 昼夜因子改为纯几何（太阳在哪），不被 C 轮天气压光影响；供 UpdateSkybox 日盘/星空用
        var sun = sunForFogBrightness != null ? sunForFogBrightness : RenderSettings.sun;
        float dayLight = sun != null ? Mathf.Clamp01(Vector3.Dot(-sun.transform.forward, Vector3.up) * 2f) : 1f;

        // 供同帧 UpdateSkybox 复用：地平线色=雾色（远山溶解）、昼夜因子（星空/压暗）
        _fogColThisFrame = fogCol;
        _dayLightThisFrame = dayLight;
    }

    // -----------------------------
    //         风系统 - 全新可视化
    // -----------------------------
    private void UpdateWind()
    {
        currentWind = Mathf.MoveTowards(currentWind, windIntensity, transitionSpeed * Time.deltaTime);

        if (sceneWindZone != null)
        {
            sceneWindZone.windMain = Mathf.Lerp(0f, maxWindMain, currentWind);
        }

    }
    // -----------------------------
    //         雷电系统
    // -----------------------------
    private void UpdateThunder()
    {
        float t = Mathf.Clamp01(thunderIntensity);

        if (t <= 0.01f)
        {
            return;
        }

        lightingTimer -= Time.deltaTime;

        if (lightingTimer <= 0f && !isFlashing)
        {
            StartCoroutine(LightningFlash());

            float currentDelay = Mathf.Lerp(maxThunderDelay, minThunderDelay, t);
            lightingTimer = currentDelay * Random.Range(0.8f, 1.2f);
        }
    }

    private IEnumerator LightningFlash()
    {
        isFlashing = true;

        // 先播放声音
        ThunderPlay();

        // 视觉效果
        if (lightingLight != null)
        {
            lightingLight.enabled = true;

            // 主闪光
            lightingLight.intensity = mainFlashIntensity;
            yield return new WaitForSeconds(Random.Range(0.05f, 0.15f));

            // 回落
            lightingLight.intensity = mainFlashIntensity * 0.2f;
            yield return new WaitForSeconds(Random.Range(0.05f, 0.1f));

            // 震颤余光
            int flickerCount = Mathf.RoundToInt(Mathf.Lerp(1, flickerAmount, thunderIntensity));

            for (int i = 0; i < flickerCount; i++)
            {
                lightingLight.enabled = true;
                lightingLight.intensity = Random.Range(flickerIntensity * 0.3f, flickerIntensity);
                yield return new WaitForSeconds(Random.Range(0.02f, 0.05f));

                lightingLight.enabled = false;
                yield return new WaitForSeconds(Random.Range(0.02f, 0.1f));
            }
        }
        else
        {
            yield return new WaitForSeconds(1.0f);
        }

        // 清理状态
        if (lightingLight != null)
        {
            lightingLight.enabled = false;
        }

        isFlashing = false;
    }

    private void UpdateSkybox()
    {
        if (skyboxMaterial == null) return;

        // 相位色板与坏天气度在同帧 UpdateRain 头部已由 ComputeSkyColors 算好（单一来源）
        float badT = _badTThisFrame;
        float wDay = _wDayThisFrame;
        float wGold = _wGoldThisFrame;
        var sun = sunForFogBrightness != null ? sunForFogBrightness : RenderSettings.sun;
        float sunY = sun != null ? -sun.transform.forward.y : 0.5f;

        skyboxMaterial.SetColor("_SkyZenith", _zenithThisFrame);
        skyboxMaterial.SetColor("_SkyMid", _midThisFrame);
        skyboxMaterial.SetColor("_HorizonGlowCol", _glowThisFrame);
        // 雾线停 = 本帧雾色（UpdateRain 已含昼夜色相）→ 远山溶进天空；
        // 地平线以下同雾色（无限远地面在线性雾里就是雾色）
        skyboxMaterial.SetColor("_SkyHorizon", _fogColThisFrame);
        skyboxMaterial.SetColor("_GroundCol", _fogColThisFrame);

        // —— 日侧暖洗：白天淡暖、黄昏拉满；夜/暴雨自动归零 ——
        Color washCol = wGold > wDay ? goldenWashColor : dayWashColor;
        float washAmt = (dayWashStrength * wDay + goldenWashStrength * wGold) * (1f - badT);
        skyboxMaterial.SetColor("_SunWashCol", washCol);
        skyboxMaterial.SetFloat("_SunWashAmt", washAmt);

        // —— 残阳晕染:太阳贴地平线才燃(|sunY|<~0.2),金相拉满,坏天气熄灭 ——
        //    日出入画(北);日落太阳在镜头背后,北天只剩反日维纳斯带 ——
        float elevMask = 1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.015f, 0.22f, Mathf.Abs(sunY)));
        skyboxMaterial.SetColor("_HaloCol", Color.Lerp(dayHaloColor, goldHaloColor, wGold));
        skyboxMaterial.SetFloat("_HaloAmt", elevMask * (0.30f + 0.70f * wGold) * haloStrength * (1f - badT));
        skyboxMaterial.SetColor("_AntiGlowCol", antiGlowColor);
        skyboxMaterial.SetFloat("_AntiGlowAmt", elevMask * antiGlowStrength * (1f - badT));
        // 日盘颜料:低日角白热化(沉日的白热核)
        skyboxMaterial.SetColor("_SunTint", Color.Lerp(sunTintDay, sunTintLow, elevMask));

        // —— 色带与笔触：storm 低对比色板上色带减半防等值线感 ——
        skyboxMaterial.SetFloat("_BandingAmount", bandingAmount * (1f - badT * 0.5f));
        float stroke = strokeDay * wDay + strokeGolden * wGold + strokeNight * _wNightThisFrame;
        stroke = Mathf.Lerp(stroke, strokeStorm, badT);
        skyboxMaterial.SetFloat("_StrokeAmount", stroke);

        // 颜料日盘跟随真实太阳：方向每帧写入；坏天气图腾收敛、日盘隐入云层
        if (sun != null)
            skyboxMaterial.SetVector("_SunDir", -sun.transform.forward);
        skyboxMaterial.SetFloat("_SunGlow", Mathf.Lerp(sunnySunGlow, stormSunGlow, badT));
        skyboxMaterial.SetFloat("_SunDiscStrength", (1f - badT * 0.9f) * Mathf.Clamp01(_dayLightThisFrame * 4f));

        // 撒灰星穹：夜相渐显，暴雨云层遮蔽大半
        float starBlend = allowExternalDrive ? starVisibility : _wNightThisFrame * (1f - badT * stormStarHide);
        skyboxMaterial.SetFloat("_StarBlend", starBlend);

        // —— 月轮图腾：方向 = 太阳的中心对称点，相位取自世界日历 ——
        // 轨迹与太阳同角速度、方位中心对称（日落月升、月落日升）——直接镜像
        // 真实太阳灯，不再经 dayProgress 推算（play 无 tick 时节律冻结，
        // 推算轨迹会把月亮钉死在一个位置）。相位只改圆缺、不改位置：
        // 世界历每月 30 天 ≈ 一朔望月，每月 1 日朔（消失）、15/16 日望
        float moonPhase = 0.5f;   // 无 WorldManager 的场景（调参/验证）：恒望
        var wm = WorldManager.Instance;
        if (wm != null)
        {
            var save = wm.WorldSave;
            if (save != null && save.gameTime != null)
                moonPhase = ((save.gameTime.ToAbsoluteDays() - 1) % 30) / 30f;
        }
        // 排查结论（07-31）：显隐由下方 _MoonGlow 控制，相位只改圆缺、不改位置，
        // 与显隐无关——此前"先勿动"的注释段已恢复为正式实现。
        // 中心对称：_SunDir = -sun.forward → 月亮取 sun.forward，永远悬在太阳正对面
        Vector3 moonDir = (sun != null) ? sun.transform.forward : Vector3.down;
        skyboxMaterial.SetVector("_MoonDir", moonDir);
        skyboxMaterial.SetFloat("_MoonPhase", moonPhase);
        // 夜里满月亮度、黄昏残留一弯、暴雨云层遮蔽（与星穹同一遮蔽系数）
        skyboxMaterial.SetFloat("_MoonGlow", moonGlowStrength
            * Mathf.Clamp01(_wNightThisFrame * 1.25f) * (1f - badT * stormStarHide));
    }
    private void ThunderPlay()
    {
        // 增加详细的调试和错误处理
        if (thunderAudioSource == null)
        {
            Debug.LogError("Thunder AudioSource is null!");
            return;
        }

        if (thunderClips == null || thunderClips.Length == 0)
        {
            Debug.LogError("No thunder clips assigned!");
            return;
        }

        // 确保AudioSource是活动的
        if (!thunderAudioSource.gameObject.activeInHierarchy)
        {
            Debug.LogError("Thunder AudioSource GameObject is not active!");
            return;
        }

        // 确保AudioSource组件是启用的
        if (!thunderAudioSource.enabled)
        {
            Debug.LogWarning("Thunder AudioSource component was disabled, enabling it now.");
            thunderAudioSource.enabled = true;
        }

        // 随机选择一个音频片段
        AudioClip clip = thunderClips[Random.Range(0, thunderClips.Length)];

        if (clip == null)
        {
            Debug.LogError("Selected thunder clip is null!");
            return;
        }

        // 播放音效
        Debug.Log($"Playing thunder sound: {clip.name}");
        thunderAudioSource.PlayOneShot(clip, 1f);
    }


    [ContextMenu("手动触发雷电")]
    public void ManualTriggerThunder()
    {
        if (!isFlashing)
        {
            StartCoroutine(LightningFlash());
        }
    }

    [ContextMenu("测试雷电音频")]
    public void TestThunderAudio()
    {
        Debug.Log("Testing thunder audio...");
        ThunderPlay();
    }
}
