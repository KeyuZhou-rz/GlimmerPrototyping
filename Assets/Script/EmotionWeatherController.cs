using UnityEngine;
using System.Collections;

public class EmotionWeatherController : MonoBehaviour
{
    [Header("核心输入(雨量，风力，雷电)")]
    [Range(-1f, 1f)]
    public float rainIntensity = 0f;
    [Range(0f, 1f)]
    public float windIntensity = 0f;
    [Range(0f, 1f)]
    public float thunderIntensity = 0f;

    [Header("雨水系统设置")]
    public ParticleSystem rainParticleSystem;
    public AudioSource rainAudioSource;
    public float maxRainEmission = 1000f;
    public float maxRainSize = 0.1f;
    [Header("雨滴湍流设置")]
    public float maxRainTurbulenceY = 3f;  // Y轴随机飘移（上下）
    public float maxRainTurbulenceZ = 3f;  // Z轴随机飘移（前后）- 主要倾斜方向
    public float turbulenceFrequency = 10f; // 湍流频率

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
    public Color stormFogColor = new Color(0.1f, 0.1f, 0.2f);
    public Color sunnyFogColor = new Color(0.5f, 0.8f, 1.0f);
    public float stormFogDensity = 0.005f;
    public float sunnyFogDensity = 0.001f;

    [Header("线性雾可见距离")]
    public float fogLinearSunnyStart = 100f;
    public float fogLinearSunnyEnd = 3000f;
    public float fogLinearStormStart = 50f;
    public float fogLinearStormEnd = 800f;

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


        // 初始雾设置
        RenderSettings.fog = true;
        RenderSettings.fogMode = FogMode.Linear;
        RenderSettings.fogColor = Color.Lerp(stormFogColor, sunnyFogColor, 0.5f);
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
            main.startSize = Mathf.Lerp(0.1f, maxRainSize, smoothedRainIntensity);

            // 应用风力湍流效果 - 随机飘移
            var noise = rainParticleSystem.noise;
            noise.enabled = true;
            noise.separateAxes = true;

            // X轴不受影响（因为视角固定在X轴）
            noise.strengthX = 0f;

            // Y轴轻微随机飘移（上下）
            float turbulenceY = Mathf.Lerp(0f, maxRainTurbulenceY, currentWind);
            noise.strengthY = new ParticleSystem.MinMaxCurve(turbulenceY * 0.5f, turbulenceY);

            // Z轴主要飘移方向（前后）- 模拟风吹效果
            float turbulenceZ = Mathf.Lerp(0f, maxRainTurbulenceZ, currentWind);
            noise.strengthZ = new ParticleSystem.MinMaxCurve(turbulenceZ * 0.7f, turbulenceZ);

            // 湍流频率
            noise.frequency = turbulenceFrequency;
            noise.damping = true;
            noise.scrollSpeed = 1f;
            noise.quality = ParticleSystemNoiseQuality.High;

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
        RenderSettings.fogDensity = Mathf.Min(0.01f, 0.015f * smoothedRainIntensity);
        
        RenderSettings.fogColor = Color.Lerp(stormFogColor, sunnyFogColor, RenderSettings.fogDensity);
        /*
        float fogStart = Mathf.Lerp(fogLinearSunnyStart, fogLinearStormStart, smoothedRainIntensity);
        float fogEnd = Mathf.Lerp(fogLinearSunnyEnd, fogLinearStormEnd, smoothedRainIntensity);
        fogStart = Mathf.Clamp(fogStart, 0f, fogEnd - 0.01f);

        RenderSettings.fogStartDistance = fogStart;
        RenderSettings.fogEndDistance = fogEnd;
        RenderSettings.fogDensity = Mathf.Lerp(sunnyFogDensity, stormFogDensity * 0.5f, smoothedRainIntensity);
        */
        RenderSettings.fogMode = FogMode.ExponentialSquared;
        


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