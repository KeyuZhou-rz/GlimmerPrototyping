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
    public Color stormFogColor = new Color(0.16f, 0.19f, 0.24f);
    public Color sunnyFogColor = new Color(0.58f, 0.66f, 0.72f);
    [Tooltip("夜晚雾亮度跟随的平行光（留空自动取 RenderSettings.sun）")]
    public Light sunForFogBrightness;
    [Range(0f, 1f), Tooltip("光照全灭时雾保留的亮度比例——夜里雾应沉入夜色而非发白")]
    public float nightFogFloor = 0.18f;

    [Header("线性雾可见距离")]
    public float fogLinearSunnyStart = 60f;
    public float fogLinearSunnyEnd = 380f;
    public float fogLinearStormStart = 18f;
    public float fogLinearStormEnd = 130f;

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

    [Header("天空盒")]
    public Material skyboxMaterial;

    // 晴天的颜色
    public Color sunnySkyColor = new Color(0.5f,0.5f,0.5f,1.0f);
    public Color stormSkyColor = new Color(0.2f, 0.2f, 0.2f, 1f);

    //厚度
    public float sunnyAtmosphere = 1.0f;
    public float stormAtmosphere = 3.0f;


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

        // 雾色跟随昼夜：夜里太阳沉下后雾同步压暗，否则黑夜地平线上浮起灰白带
        Color fogCol = Color.Lerp(sunnyFogColor, stormFogColor, fogT);
        var sun = sunForFogBrightness != null ? sunForFogBrightness : RenderSettings.sun;
        if (sun != null)
        {
            float sunUp = Mathf.Clamp01(Vector3.Dot(-sun.transform.forward, Vector3.up) * 2f);
            float dayLight = Mathf.Clamp01(sun.intensity) * sunUp;
            fogCol *= Mathf.Lerp(nightFogFloor, 1f, dayLight);
        }
        RenderSettings.fogColor = fogCol;

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
        if (skyboxMaterial != null)
        {
            // 这里的 smoothedRainIntensity (0~1) 代表了坏天气的程度
            // 0 = 晴天, 1 = 暴雨

            // 1. 混合颜色：从 晴天色 变到 阴天色
            Color currentSkyColor = Color.Lerp(sunnySkyColor, stormSkyColor, smoothedRainIntensity);
            skyboxMaterial.SetColor("_SkyTint", currentSkyColor);
            skyboxMaterial.SetColor("_GroundColor", currentSkyColor);

            // 2. 混合大气厚度：从 通透 变到 浑浊
            float currentAtmosphere = Mathf.Lerp(sunnyAtmosphere, stormAtmosphere, smoothedRainIntensity);
            skyboxMaterial.SetFloat("_AtmosphereThickness", currentAtmosphere);

            // 3. (可选) 控制曝光度：阴天暗一点
            float currentExposure = Mathf.Lerp(1.3f, 0.8f, smoothedRainIntensity);
            skyboxMaterial.SetFloat("_Exposure", currentExposure);
        }
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