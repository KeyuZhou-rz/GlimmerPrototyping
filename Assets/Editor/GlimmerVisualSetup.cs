using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

/// <summary>
/// Glimmer 视觉统一一键落地（幂等，可重复执行）：
///   1. BrokenVector 树材质 → Glimmer/Toon（保留原贴图引用）
///   2. 地形 → Glimmer/Terrain 材质（草原高度带调色板），重建网格写入高度
///   3. Rainsystem → RainStreak 材质 + 拉伸公告板参数（修掉"水材质当雨"）
///   4. 全局 Volume（Tonemapping/Bloom/Vignette/ColorAdjustments）+ 相机后处理开关
///   5. 场景里 EmotionWeatherController 的旧序列化雾/雨参数刷成新默认
/// </summary>
public static class GlimmerVisualSetup
{
    const string MatDir = "Assets/Materials/Glimmer";
    const string TerrainMatPath = MatDir + "/Terrain_Glimmer.mat";
    const string RainMatPath = MatDir + "/RainStreak.mat";
    const string ProfilePath = "Assets/Settings/GlimmerPostFX.asset";

    static readonly string[] TreeMatPaths =
    {
        "Assets/ImportedAssets/BrokenVector/LowPolyTreePack/Materials/Normal.mat",
        "Assets/ImportedAssets/BrokenVector/LowPolyTreePack/Materials/Cold.mat",
        "Assets/ImportedAssets/BrokenVector/LowPolyTreePack/Materials/Dry.mat",
        "Assets/ImportedAssets/BrokenVector/LowPolyTreePack/Materials/Fall.mat",
    };

    [MenuItem("Tools/Glimmer/Setup Visual Style")]
    public static void Run()
    {
        if (!AssetDatabase.IsValidFolder(MatDir))
            AssetDatabase.CreateFolder("Assets/Materials", "Glimmer");

        SetupTreeMaterials();
        SetupTerrain();
        SetupRain();
        SetupPostFX();
        SetupWeatherDefaults();
        DisableLSystemVegetation();

        AssetDatabase.SaveAssets();
        EditorSceneManager.MarkAllScenesDirty();
        EditorSceneManager.SaveOpenScenes();
        Debug.Log("[GlimmerVisualSetup] Done — trees/terrain/rain/postfx unified.");
    }

    // ---- 1. 树 ----------------------------------------------------------
    static void SetupTreeMaterials()
    {
        var toon = Shader.Find("Glimmer/Toon");
        if (toon == null) { Debug.LogError("Glimmer/Toon shader not found"); return; }

        foreach (var path in TreeMatPaths)
        {
            var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (mat == null) continue;

            // 换 shader 前后 _BaseMap/_BaseColor 属性名一致，贴图引用自动保留
            if (mat.shader != toon) mat.shader = toon;
            mat.SetFloat("_ShadeBands", 3f);
            mat.SetFloat("_Posterize", 0.6f);
            mat.SetFloat("_AmbientBoost", 1.25f);   // 树冠背光面也要留住体积色
            mat.SetColor("_ShadowTint", new Color(0.52f, 0.58f, 0.66f));
            mat.SetFloat("_RimStrength", 0.16f);
            mat.SetFloat("_RimPower", 3.0f);
            mat.SetFloat("_SwayAmount", 0.012f);   // 极轻微风摆，整树刚体感不破坏
            mat.SetFloat("_SwaySpeed", 1.1f);
            EditorUtility.SetDirty(mat);
        }
        Debug.Log("[GlimmerVisualSetup] Tree materials → Glimmer/Toon");
    }

    // ---- 2. 地形 --------------------------------------------------------
    static void SetupTerrain()
    {
        var shader = Shader.Find("Glimmer/Terrain");
        if (shader == null) { Debug.LogError("Glimmer/Terrain shader not found"); return; }

        var mat = AssetDatabase.LoadAssetAtPath<Material>(TerrainMatPath);
        if (mat == null)
        {
            mat = new Material(shader);
            AssetDatabase.CreateAsset(mat, TerrainMatPath);
        }
        mat.shader = shader;

        // 草原调色板：河岸沙 → 低地草 → 平原草 → 高地干草 → 山岩
        // 明度接近、色相渐移——塞尔达式的和谐来自低对比同族色
        mat.SetColor("_SandColor",     new Color(0.72f, 0.64f, 0.46f));
        mat.SetColor("_LowlandColor",  new Color(0.40f, 0.50f, 0.27f));
        mat.SetColor("_PlainsColor",   new Color(0.48f, 0.53f, 0.27f));
        mat.SetColor("_HighlandColor", new Color(0.56f, 0.53f, 0.31f));
        mat.SetColor("_PeakColor",     new Color(0.50f, 0.46f, 0.42f));
        mat.SetColor("_CliffColor",    new Color(0.42f, 0.36f, 0.30f));
        mat.SetFloat("_BandSoftness", 1.3f);
        mat.SetFloat("_BandNoiseAmp", 0.8f);
        mat.SetFloat("_CliffStart", 0.45f);
        mat.SetFloat("_CliffSharp", 0.18f);
        mat.SetFloat("_FacetVariation", 0.10f);
        mat.SetFloat("_ShadeBands", 3f);
        mat.SetFloat("_Posterize", 0.6f);
        mat.SetFloat("_AmbientBoost", 0.9f);
        mat.SetColor("_ShadowTint", new Color(0.30f, 0.38f, 0.46f));
        EditorUtility.SetDirty(mat);

        var tg = Object.FindFirstObjectByType<TerrainGenerator>();
        if (tg != null)
        {
            tg.terrainMaterial = mat;
            var mr = tg.GetComponent<MeshRenderer>();
            if (mr != null) mr.sharedMaterial = mat;

            // 编辑态重建网格并把区域高度写进材质（Generate 是私有 ContextMenu 方法）
            typeof(TerrainGenerator)
                .GetMethod("Generate", BindingFlags.NonPublic | BindingFlags.Instance)
                ?.Invoke(tg, null);
            EditorUtility.SetDirty(tg);
            Debug.Log("[GlimmerVisualSetup] Terrain material + regenerate OK");
        }
        else Debug.LogWarning("[GlimmerVisualSetup] No TerrainGenerator in scene");
    }

    // ---- 3. 雨 ----------------------------------------------------------
    static void SetupRain()
    {
        var shader = Shader.Find("Glimmer/RainStreak");
        if (shader == null) { Debug.LogError("Glimmer/RainStreak shader not found"); return; }

        var mat = AssetDatabase.LoadAssetAtPath<Material>(RainMatPath);
        if (mat == null)
        {
            mat = new Material(shader);
            AssetDatabase.CreateAsset(mat, RainMatPath);
        }
        mat.shader = shader;
        mat.SetColor("_StreakColor", new Color(0.62f, 0.72f, 0.80f, 0.45f));
        mat.SetFloat("_CoreBoost", 0.35f);
        mat.SetFloat("_EdgeSoft", 0.22f);
        mat.SetFloat("_TipFade", 0.30f);
        EditorUtility.SetDirty(mat);

        var go = GameObject.Find("Rainsystem");
        if (go == null) { Debug.LogWarning("[GlimmerVisualSetup] Rainsystem not found"); return; }

        var ps = go.GetComponent<ParticleSystem>();
        var psr = go.GetComponent<ParticleSystemRenderer>();
        if (ps == null || psr == null) return;

        // 渲染器：拉伸公告板 → 雨丝；材质换掉误用的水材质
        psr.sharedMaterial = mat;
        psr.renderMode = ParticleSystemRenderMode.Stretch;
        psr.lengthScale = 11f;       // 雨丝长度
        psr.velocityScale = 0.018f;  // 快雨略长
        psr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        psr.receiveShadows = false;
        psr.sortMode = ParticleSystemSortMode.None;

        var main = ps.main;
        main.startLifetime = 3.5f;
        main.startSpeed = 14f;          // 运行时由控制器按雨强驱动 12~20
        main.startSize = 0.04f;
        main.startColor = Color.white;
        main.gravityModifier = 0f;
        main.maxParticles = 8000;
        main.simulationSpace = ParticleSystemSimulationSpace.World;

        var emission = ps.emission;
        emission.rateOverTime = 0f;     // 运行时控制器驱动

        // 发射器：贴着天花板的薄片（原来是 79m 厚的体积，半数雨滴生在地下）
        var t = go.transform;
        t.position = new Vector3(t.position.x, 46f, t.position.z);
        t.localScale = new Vector3(t.localScale.x, t.localScale.y, 8f);

        EditorUtility.SetDirty(go);
        Debug.Log("[GlimmerVisualSetup] Rain particle → RainStreak stretch billboard");
    }

    // ---- 4. 后处理 ------------------------------------------------------
    static void SetupPostFX()
    {
        var profile = AssetDatabase.LoadAssetAtPath<VolumeProfile>(ProfilePath);
        if (profile == null)
        {
            profile = ScriptableObject.CreateInstance<VolumeProfile>();
            AssetDatabase.CreateAsset(profile, ProfilePath);
        }

        T GetOrAdd<T>() where T : VolumeComponent
        {
            if (profile.TryGet<T>(out var c)) return c;
            c = profile.Add<T>(false);
            AssetDatabase.AddObjectToAsset(c, profile);
            return c;
        }

        var tone = GetOrAdd<Tonemapping>();
        tone.active = true;
        tone.mode.Override(TonemappingMode.Neutral);

        var bloom = GetOrAdd<Bloom>();
        bloom.active = true;
        bloom.intensity.Override(0.22f);
        bloom.threshold.Override(1.6f);   // 只让星光/闪电泛光，地形高光不参与
        bloom.scatter.Override(0.5f);

        var vig = GetOrAdd<Vignette>();
        vig.active = true;
        vig.intensity.Override(0.22f);
        vig.smoothness.Override(0.42f);

        var ca = GetOrAdd<ColorAdjustments>();
        ca.active = true;
        ca.saturation.Override(8f);
        ca.contrast.Override(8f);
        ca.postExposure.Override(0f);     // 提亮交给光照，不走后处理（曝光+泛光=糊）

        EditorUtility.SetDirty(profile);

        var volGo = GameObject.Find("GlimmerPostFX");
        if (volGo == null) volGo = new GameObject("GlimmerPostFX");
        var vol = volGo.GetComponent<Volume>();
        if (vol == null) vol = volGo.AddComponent<Volume>();
        vol.isGlobal = true;
        vol.priority = 10f;
        vol.sharedProfile = profile;
        EditorUtility.SetDirty(volGo);

        var cam = Camera.main;
        if (cam != null)
        {
            var data = cam.GetUniversalAdditionalCameraData();
            data.renderPostProcessing = true;
            // 关 SMAA：低多边形硬边下它把细枝/雨丝抹成雾感（playmode 诡异模糊的来源），
            // 几何风格本身不怕锯齿，宁可锐利。
            data.antialiasing = AntialiasingMode.None;
            EditorUtility.SetDirty(cam);
        }
        Debug.Log("[GlimmerVisualSetup] PostFX volume + camera post-processing on");
    }

    // ---- 5. 天气组件旧序列化值 → 新调色板 --------------------------------
    static void SetupWeatherDefaults()
    {
        var wc = Object.FindFirstObjectByType<EmotionWeatherController>();
        if (wc == null) { Debug.LogWarning("[GlimmerVisualSetup] No EmotionWeatherController"); return; }

        wc.stormFogColor = new Color(0.16f, 0.19f, 0.24f);
        wc.sunnyFogColor = new Color(0.58f, 0.66f, 0.72f);
        wc.fogLinearSunnyStart = 60f;
        wc.fogLinearSunnyEnd = 380f;
        wc.fogLinearStormStart = 18f;
        wc.fogLinearStormEnd = 130f;
        wc.dimnessFogWeight = 0.45f;

        wc.maxRainEmission = 2200f;
        wc.minRainSize = 0.05f;
        wc.maxRainSize = 0.09f;
        wc.maxRainWindForce = 9f;
        wc.maxRainTurbulenceZ = 1.2f;
        wc.turbulenceFrequency = 0.2f;

        EditorUtility.SetDirty(wc);
        Debug.Log("[GlimmerVisualSetup] Weather serialized values refreshed");
    }

    // ---- 5b. 关停 L-System 植被（用户已弃用，暂用 prefab 树） ---------------
    static void DisableLSystemVegetation()
    {
        // EcosystemManager 在运行时生成 L-System 白树（Baobab/Acacia），
        // 用户已明确放弃 L-System，场景植被以 BrokenVector prefab 为准。
        var eco = Object.FindFirstObjectByType<GlimmerDiary.Flora.EcosystemManager>();
        if (eco != null && eco.enabled)
        {
            eco.enabled = false;
            EditorUtility.SetDirty(eco.gameObject);
            Debug.Log("[GlimmerVisualSetup] EcosystemManager disabled (L-System vegetation off)");
        }

        // 场景里已有的 Baobab_0 残留也一并隐藏
        var baobab = GameObject.Find("Baobab_0");
        if (baobab != null && baobab.activeSelf)
        {
            baobab.SetActive(false);
            EditorUtility.SetDirty(baobab);
        }
    }

    // ---- 预览光照（编辑态看效果用；运行时 LightManager 会接管） -----------
    [MenuItem("Tools/Glimmer/Preview Golden Hour")]
    public static void PreviewGoldenHour()
    {
        var sun = RenderSettings.sun;
        if (sun == null)
        {
            var go = GameObject.Find("Directional Light");
            if (go != null) sun = go.GetComponent<Light>();
        }
        if (sun == null) { Debug.LogWarning("No sun light found"); return; }

        sun.transform.rotation = Quaternion.Euler(38f, 215f, 0f);
        sun.color = new Color(1.0f, 0.87f, 0.68f);
        sun.intensity = 1.15f;
        RenderSettings.ambientMode = AmbientMode.Trilight;
        RenderSettings.ambientSkyColor = new Color(0.55f, 0.62f, 0.72f);
        RenderSettings.ambientEquatorColor = new Color(0.48f, 0.46f, 0.40f);
        RenderSettings.ambientGroundColor = new Color(0.26f, 0.22f, 0.18f);
        RenderSettings.fog = true;
        RenderSettings.fogMode = FogMode.Linear;
        RenderSettings.fogColor = new Color(0.58f, 0.66f, 0.72f);
        RenderSettings.fogStartDistance = 60f;
        RenderSettings.fogEndDistance = 380f;
        SceneView.RepaintAll();
        Debug.Log("[GlimmerVisualSetup] Golden hour preview lighting set");
    }

    [MenuItem("Tools/Glimmer/Preview Storm")]
    public static void PreviewStorm()
    {
        var sun = RenderSettings.sun;
        if (sun == null)
        {
            var go = GameObject.Find("Directional Light");
            if (go != null) sun = go.GetComponent<Light>();
        }
        if (sun != null)
        {
            sun.transform.rotation = Quaternion.Euler(55f, 200f, 0f);
            sun.color = new Color(0.62f, 0.68f, 0.76f);
            sun.intensity = 0.45f;
        }
        RenderSettings.ambientMode = AmbientMode.Trilight;
        RenderSettings.ambientSkyColor = new Color(0.34f, 0.39f, 0.46f);
        RenderSettings.ambientEquatorColor = new Color(0.26f, 0.29f, 0.33f);
        RenderSettings.ambientGroundColor = new Color(0.12f, 0.13f, 0.15f);
        RenderSettings.fog = true;
        RenderSettings.fogMode = FogMode.Linear;
        RenderSettings.fogColor = new Color(0.16f, 0.19f, 0.24f);
        RenderSettings.fogStartDistance = 18f;
        RenderSettings.fogEndDistance = 130f;
        SceneView.RepaintAll();
        Debug.Log("[GlimmerVisualSetup] Storm preview lighting set");
    }
}
