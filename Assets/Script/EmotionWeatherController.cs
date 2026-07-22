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
    public Color stormFogColor = new Color(0.20f, 0.22f, 0.26f);
    public Color sunnyFogColor = new Color(0.70f, 0.66f, 0.56f);   // 暖尘霾(草原正午的空气:尘埃暖意,非奶白)
    [Tooltip("夜晚雾亮度跟随的平行光（留空自动取 RenderSettings.sun）")]
    public Light sunForFogBrightness;
    [Range(0f, 1f), Tooltip("光照全灭时雾保留的亮度比例——夜里雾应沉入夜色而非发白")]
    public float nightFogFloor = 0.18f;

    [Header("线性雾可见距离")]
    public float fogLinearSunnyStart = 60f;
    public float fogLinearSunnyEnd = 300f;   // 推出地形对角线(~226m):远山只柔化不溶解
    public float fogLinearStormStart = 30f;
    public float fogLinearStormEnd = 140f;   // 风暴仍重,但中景可读

    [Header("对外输出（植物生长）")]
    [Range(0f, 1f)] public float currentWaterSaturation;
    [Range(0f, 1f)] public float currentSunlightIntensity;

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

    [Header("夜雾色相（夜里雾随天空一起入蓝，不是压暗的棕灰）")]
    public Color nightFogColor = new Color(0.095f, 0.11f, 0.145f);

    [Tooltip("坏天气把星空遮掉的比例：暴雨云层下不该满天星")]
    [Range(0f, 1f)] public float stormStarHide = 0.85f;

    // UpdateRain 每帧算好的雾色/昼夜因子，UpdateSkybox 复用（同一帧内先 Rain 后 Skybox）
    private Color _fogColThisFrame;
    private float _dayLightThisFrame = 1f;


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


        // 初始雾设置：与 UpdateRain 的公式同源（fogT=0 晴天端）
        RenderSettings.fog = true;
        RenderSettings.fogMode = FogMode.Linear;
        RenderSettings.fogColor = sunnyFogColor;
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
        // 坏天气程度 = 雨强 ⊕ 晦明（dimness 按权重折算，只影响雾，不影响雨粒子）
        float fogT = Mathf.Clamp01(smoothedRainIntensity + dimness * dimnessFogWeight);

        float fogStart = Mathf.Lerp(fogLinearSunnyStart, fogLinearStormStart, fogT);
        float fogEnd   = Mathf.Lerp(fogLinearSunnyEnd,   fogLinearStormEnd,   fogT);
        fogStart = Mathf.Clamp(fogStart, 0f, fogEnd - 0.01f);

        RenderSettings.fogMode          = FogMode.Linear;
        RenderSettings.fogStartDistance = fogStart;
        RenderSettings.fogEndDistance   = fogEnd;

        // 雾色跟随昼夜：夜里雾滑向海军灰（换色相，不是压暗的棕灰——
        // 压值不换相的夜雾会把整个地平线染成脏米色）
        Color fogCol = Color.Lerp(sunnyFogColor, stormFogColor, fogT);
        var sun = sunForFogBrightness != null ? sunForFogBrightness : RenderSettings.sun;
        float dayLight = 1f;
        if (sun != null)
        {
            float sunUp = Mathf.Clamp01(Vector3.Dot(-sun.transform.forward, Vector3.up) * 2f);
            dayLight = Mathf.Clamp01(sun.intensity) * sunUp;
            fogCol = Color.Lerp(nightFogColor, fogCol, Mathf.SmoothStep(0f, 1f, dayLight));
        }
        RenderSettings.fogColor = fogCol;

        // 供同帧 UpdateSkybox 复用：地平线色=雾色（远山溶解）、昼夜因子（星空/压暗）
        _fogColThisFrame = fogCol;
        _dayLightThisFrame = dayLight;

        currentWaterSaturation = smoothedRainIntensity;
        currentSunlightIntensity = smoothedSunIntensity;
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

        // 坏天气程度与雾同源：雨强 ⊕ 晦明（天空和雾必须一起变脏，否则天地脱节）
        float badT = Mathf.Clamp01(smoothedRainIntensity + dimness * dimnessFogWeight);

        // —— 相位权重：真实太阳仰角 → 夜/黄昏/白天三相（黄昏=两者之外的余量）——
        var sun = sunForFogBrightness != null ? sunForFogBrightness : RenderSettings.sun;
        float sunY = sun != null ? -sun.transform.forward.y : 0.5f;
        float wNight = 1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(-0.12f, 0.02f, sunY));
        float wDay = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.10f, 0.35f, sunY));
        float wGold = Mathf.Clamp01(1f - wNight - wDay);

        // —— 三停色板相位混合，再向暴雨端拉 ——
        Color zenith = daySkyZenith * wDay + goldSkyZenith * wGold + nightSkyZenith * wNight;
        Color mid    = daySkyMid    * wDay + goldSkyMid    * wGold + nightSkyMid    * wNight;
        Color glow   = daySkyGlow   * wDay + goldSkyGlow   * wGold + nightSkyGlow   * wNight;
        zenith = Color.Lerp(zenith, stormSkyZenith, badT);
        mid    = Color.Lerp(mid,    stormSkyMid,    badT);
        glow   = Color.Lerp(glow,   stormSkyGlow,   badT);

        skyboxMaterial.SetColor("_SkyZenith", zenith);
        skyboxMaterial.SetColor("_SkyMid", mid);
        skyboxMaterial.SetColor("_HorizonGlowCol", glow);
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
        float stroke = strokeDay * wDay + strokeGolden * wGold + strokeNight * wNight;
        stroke = Mathf.Lerp(stroke, strokeStorm, badT);
        skyboxMaterial.SetFloat("_StrokeAmount", stroke);

        // 颜料日盘跟随真实太阳：方向每帧写入；坏天气图腾收敛、日盘隐入云层
        if (sun != null)
            skyboxMaterial.SetVector("_SunDir", -sun.transform.forward);
        skyboxMaterial.SetFloat("_SunGlow", Mathf.Lerp(sunnySunGlow, stormSunGlow, badT));
        skyboxMaterial.SetFloat("_SunDiscStrength", (1f - badT * 0.9f) * Mathf.Clamp01(_dayLightThisFrame * 4f));

        // 撒灰星穹：夜相渐显，暴雨云层遮蔽大半
        float starBlend = allowExternalDrive ? starVisibility : wNight * (1f - badT * stormStarHide);
        skyboxMaterial.SetFloat("_StarBlend", starBlend);

        // —— 月轮图腾：方向按时间推算（无第二盏灯），相位取自世界日历 ——
        // 世界历每月 30 天 ≈ 一朔望月：每月 1 日朔（月亮消失）、15/16 日望。
        // 轨迹 = 太阳轨道面时间平移 phase：朔时月与日同升落（全暗不可见），
        // 望时日落月升，上弦黄昏挂西天 —— 月相与升起时刻自洽，无需第二盏灯
        float moonPhase = 0.5f;   // 无 WorldManager 的场景（调参/验证）：恒望
        float dayProg = -1f;
        var wm = WorldManager.Instance;
        if (wm != null)
        {
            var rhythm = wm.GetRhythmState();
            if (rhythm != null) dayProg = rhythm.dayProgress;
            var save = wm.WorldSave;
            if (save != null && save.gameTime != null)
                moonPhase = ((save.gameTime.ToAbsoluteDays() - 1) % 30) / 30f;
        }
        if (dayProg < 0f) dayProg = (float)System.DateTime.Now.TimeOfDay.TotalDays;
        // 轨道面 yaw 跟随真实太阳灯（LightManager.SunDirection 的单一事实来源）
        float moonYaw = (sun != null) ? sun.transform.localEulerAngles.y : 170f;
        float moonPitch = Mathf.Repeat(dayProg + moonPhase, 1f) * 360f - 90f;
        Vector3 moonDir = -(Quaternion.Euler(moonPitch, moonYaw, 0f) * Vector3.forward);
        skyboxMaterial.SetVector("_MoonDir", moonDir);
        skyboxMaterial.SetFloat("_MoonPhase", moonPhase);
        // 夜里满月亮度、黄昏残留一弯、暴雨云层遮蔽（与星穹同一遮蔽系数）
        skyboxMaterial.SetFloat("_MoonGlow", moonGlowStrength
            * Mathf.Clamp01(wNight * 1.25f) * (1f - badT * stormStarHide));
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