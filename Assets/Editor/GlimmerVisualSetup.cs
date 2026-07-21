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
///   5. 场景里 EmotionWeatherController 的旧序列化雾/雨参数刷成新默认（含 rainIntensity/starVisibility 残留清零）
///   6. WorldAtmosphereBinder 防丢（场景重做后 binder 曾丢失、7 条绑定断电，2026-07；并入一键落地）
/// </summary>
public static class GlimmerVisualSetup
{
    const string MatDir = "Assets/Materials/Glimmer";
    const string TerrainMatPath = MatDir + "/Terrain_Glimmer.mat";
    const string RainMatPath = MatDir + "/RainStreak.mat";
    const string SkyMatPath = MatDir + "/SkyGradient.mat";
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
        SetupSky();
        SetupPostFX();
        SetupWeatherDefaults();
        DisableLSystemVegetation();
        GlimmerBinderSetup.Setup();   // 6. 大气绑定器防丢（幂等：有则补空引用，无则创建）

        AssetDatabase.SaveAssets();
        EditorSceneManager.MarkAllScenesDirty();
        EditorSceneManager.SaveOpenScenes();
        Debug.Log("[GlimmerVisualSetup] Done — trees/terrain/rain/postfx unified.");
    }

    // ---- 材质关键字空间修复（"State comes from an incompatible keyword space" 报错用） ----
    // 成因：shader 换/改关键字后，材质里序列化的关键字状态 blob 与新 keywordSpace 不匹配
    // （如 Setup 把 BrokenVector 树材质从 URP Lit 换到 Glimmer/Toon，旧状态残留）。
    // 做法：删掉不在 shader 关键字空间里的关键字，并重赋 shader 强制重建内部状态。
    [MenuItem("Tools/Glimmer/Repair Shader Keywords")]
    public static void RepairShaderKeywords()
    {
        int touched = 0;
        foreach (var guid in AssetDatabase.FindAssets("t:Material"))
        {
            var path = AssetDatabase.GUIDToAssetPath(guid);
            if (!path.StartsWith("Assets/")) continue;   // 包缓存材质随包还原，不写
            var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (mat == null || mat.shader == null) continue;

            // 1) 删掉不在 shader 关键字空间里的关键字
            var space = mat.shader.keywordSpace;
            var kws = mat.shaderKeywords;
            var kept = new System.Collections.Generic.List<string>(kws.Length);
            bool stripped = false;
            foreach (var k in kws)
            {
                bool valid = false;
                foreach (var lk in space.keywords)
                    if (lk.name == k) { valid = true; break; }
                if (valid) kept.Add(k); else stripped = true;
            }
            if (stripped) mat.shaderKeywords = kept.ToArray();

            // 2) 无命名关键字 ≠ 状态干净：state blob 可能仍按旧 keywordSpace 尺寸序列化
            //    （"state size mismatch" 的真正来源）——重赋 shader 强制重建内部状态
            var sh = mat.shader;
            mat.shader = sh;
            EditorUtility.SetDirty(mat);
            touched++;
            if (stripped)
                Debug.Log($"[RepairShaderKeywords] {path}: stripped {kws.Length - kept.Count} stale keyword(s)");
        }
        AssetDatabase.SaveAssets();
        Debug.Log($"[RepairShaderKeywords] done, touched {touched} material(s).");
    }

    // 关键字空间漂移的根治：重导 shader 重建其 keywordSpace，并让所有依赖材质随之重算状态。
    // （材质侧重赋 shader 不清内部 state blob 时，只剩这条路。）
    [MenuItem("Tools/Glimmer/Reimport Glimmer Shaders")]
    public static void ReimportGlimmerShaders()
    {
        int n = 0;
        foreach (var guid in AssetDatabase.FindAssets("t:Shader", new[] { "Assets/Shaders", "Assets/ImportedAssets" }))
        {
            var path = AssetDatabase.GUIDToAssetPath(guid);
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);
            n++;
        }
        Debug.Log($"[ReimportGlimmerShaders] reimported {n} shader(s).");
    }

    // 诊断：列出挂在大关键字空间（≥60）shader 上的全部材质，定位 "67 vs 66" 报错来源
    [MenuItem("Tools/Glimmer/Diagnose Keyword Spaces")]
    public static void DiagnoseKeywordSpaces()
    {
        foreach (var guid in AssetDatabase.FindAssets("t:Material"))
        {
            var path = AssetDatabase.GUIDToAssetPath(guid);
            var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (mat == null || mat.shader == null) continue;
            int kc = (int)mat.shader.keywordSpace.keywordCount;
            if (kc >= 60)
                Debug.Log($"[KwDiag] kc={kc} mat={path} shader={AssetDatabase.GetAssetPath(mat.shader)} enabledKw={mat.shaderKeywords.Length}");
        }
        Debug.Log("[KwDiag] done");
    }

    // 逐一点名诊断：遍历内存中全部材质（含场景内嵌/运行时，FindAssets 扫不到的），
    // 每个先打日志再重赋 shader 触发状态迁移——blob 不匹配的那个会在它自己的日志行后
    // 立刻抛 "incompatible keyword space"，控制台里报错上面那行日志就是元凶。
    [MenuItem("Tools/Glimmer/Diagnose Keyword Poke")]
    public static void DiagnoseKeywordPoke()
    {
        int n = 0;
        foreach (var mat in Resources.FindObjectsOfTypeAll<Material>())
        {
            if (mat == null || mat.shader == null) continue;
            string path = AssetDatabase.GetAssetPath(mat);
            Debug.Log($"[KwPoke] {(string.IsNullOrEmpty(path) ? "<embedded/runtime>" : path)} :: {mat.name} :: shader={mat.shader.name}");
            var sh = mat.shader;
            mat.shader = sh;
            n++;
        }
        Debug.Log($"[KwPoke] poked {n} material(s).");
    }

    // 定点修复：mat.shader = 同 shader 是 no-op（Unity 去重），必须 null 往返才真重建关键字状态。
    [MenuItem("Tools/Glimmer/Repair Two Culprits")]
    public static void RepairTwoCulprits()
    {
        foreach (var p in new[]
        {
            "Assets/ImportedAssets/BrokenVector/LowPolyTreePack/Materials/Normal.mat",
            "Assets/Materials/Glimmer/Terrain_Glimmer.mat",
        })
        {
            var mat = AssetDatabase.LoadAssetAtPath<Material>(p);
            if (mat == null) { Debug.LogWarning($"[RepairTwo] not found: {p}"); continue; }
            mat.shaderKeywords = new string[0];
            var sh = mat.shader;
            mat.shader = null;      // null 往返：强制彻底重建内部关键字状态
            mat.shader = sh;
            EditorUtility.SetDirty(mat);
            Debug.Log($"[RepairTwo] rebuilt {p}");
        }
        AssetDatabase.SaveAssets();
    }

    // 硬修复：大关键字空间（≥60）shader 的材质，用同 shader 新建干净材质 + 手动复制暴露属性
    // （不复制关键字 blob）+ CopySerialized 覆盖原对象——GUID/引用不动，state blob 按当前
    // keywordSpace 尺寸重建。专治 "state size mismatch (67 vs 66)"（URP Lit 66 → GlimmerToon 67）。
    [MenuItem("Tools/Glimmer/Repair Keyword State Hard")]
    public static void RepairKeywordStateHard()
    {
        int n = 0;
        foreach (var guid in AssetDatabase.FindAssets("t:Material", new[] { "Assets" }))
        {
            var path = AssetDatabase.GUIDToAssetPath(guid);
            var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (mat == null || mat.shader == null) continue;
            if (mat.shader.keywordSpace.keywordCount < 60) continue;   // 只处理受害组

            var shader = mat.shader;
            var fresh = new Material(shader);
            int pc = ShaderUtil.GetPropertyCount(shader);
            for (int i = 0; i < pc; i++)
            {
                string name = ShaderUtil.GetPropertyName(shader, i);
                switch (ShaderUtil.GetPropertyType(shader, i))
                {
                    case ShaderUtil.ShaderPropertyType.Color:  fresh.SetColor(name, mat.GetColor(name)); break;
                    case ShaderUtil.ShaderPropertyType.Vector: fresh.SetVector(name, mat.GetVector(name)); break;
                    case ShaderUtil.ShaderPropertyType.Float:
                    case ShaderUtil.ShaderPropertyType.Range:  fresh.SetFloat(name, mat.GetFloat(name)); break;
                    case ShaderUtil.ShaderPropertyType.TexEnv:
                        fresh.SetTexture(name, mat.GetTexture(name));
                        fresh.SetTextureOffset(name, mat.GetTextureOffset(name));
                        fresh.SetTextureScale(name, mat.GetTextureScale(name));
                        break;
                }
            }
            fresh.renderQueue = mat.renderQueue;
            fresh.shaderKeywords = mat.shaderKeywords;   // 名字级保留仍有效的关键字

            EditorUtility.CopySerialized(fresh, mat);    // 干净状态覆盖，GUID/引用不动
            Object.DestroyImmediate(fresh);
            EditorUtility.SetDirty(mat);
            n++;
            Debug.Log($"[KwHard] rebuilt {path}");
        }
        AssetDatabase.SaveAssets();
        Debug.Log($"[KwHard] done, rebuilt {n} material(s).");
    }

    // ---- 存档重置（T8 预跑验证用；带时间戳备份，可恢复） -----------------
    // AppData 默认隐藏，免去找目录：一键把 world_state.json 改名备份，
    // 下次进 Play 即触发"新世界 30 天预跑"；恢复时把 .bak 改名回 world_state.json。
    [MenuItem("Tools/Glimmer/Reset World Save (Backup + Delete)")]
    public static void ResetWorldSave()
    {
        string dir  = System.IO.Path.Combine(Application.persistentDataPath, "GlimmerDiary");
        string save = System.IO.Path.Combine(dir, "world_state.json");
        if (!System.IO.File.Exists(save))
        {
            Debug.Log("[ResetWorldSave] 无存档文件，本来就是新世界（目录：" + dir + "）");
            return;
        }
        string bak = save + "." + System.DateTime.Now.ToString("yyyyMMdd-HHmmss") + ".bak";
        System.IO.File.Move(save, bak);
        Debug.Log($"[ResetWorldSave] 存档已移作备份 → {bak}\n下次进 Play 触发新世界 30 天预跑；恢复时把该 .bak 改回 world_state.json");
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

        // 旱季草原调色板（demo2 对齐轮 · B1 落地）：苍白低饱和同族色，
        // 色带彼此靠近，暖冷对比交给光照；岩石用暖灰棕（用户明确排除蓝灰岩）
        mat.SetColor("_SandColor",     new Color(0.80f, 0.73f, 0.58f));  // 苍白骨沙
        mat.SetColor("_LowlandColor",  new Color(0.66f, 0.63f, 0.44f));  // 干稻草
        mat.SetColor("_PlainsColor",   new Color(0.72f, 0.68f, 0.50f));  // 苍白干草
        mat.SetColor("_HighlandColor", new Color(0.70f, 0.63f, 0.47f));  // 日晒褪色
        mat.SetColor("_PeakColor",     new Color(0.55f, 0.51f, 0.45f));  // 暖灰棕
        mat.SetColor("_CliffColor",    new Color(0.38f, 0.34f, 0.29f));  // 暖深棕
        mat.SetFloat("_BandSoftness", 1.4f);
        mat.SetFloat("_BandNoiseAmp", 0.7f);
        mat.SetFloat("_CliffStart", 0.42f);
        mat.SetFloat("_CliffSharp", 0.16f);
        mat.SetFloat("_FacetVariation", 0.10f);
        mat.SetFloat("_ShadeBands", 3f);
        mat.SetFloat("_Posterize", 0.6f);
        mat.SetFloat("_AmbientBoost", 0.95f);
        mat.SetColor("_ShadowTint", new Color(0.34f, 0.40f, 0.50f));
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

    // ---- 3b. 天空（桑人岩画渐变，Docs/SkySanRockArt.md） -------------------
    static void SetupSky()
    {
        var shader = Shader.Find("Glimmer/SkyGradient");
        if (shader == null) { Debug.LogError("Glimmer/SkyGradient shader not found"); return; }

        var mat = AssetDatabase.LoadAssetAtPath<Material>(SkyMatPath);
        if (mat == null)
        {
            mat = new Material(shader);
            AssetDatabase.CreateAsset(mat, SkyMatPath);
        }
        mat.shader = shader;

        // 静态基线 = 白天相（运行时属性全部由 EmotionWeatherController 覆写；
        // 这里写一份合理编辑态值，保证不进 Play 也能看到岩画天空而非品红/黑空）
        mat.SetColor("_SkyZenith", new Color(0.36f, 0.46f, 0.56f));    // 尘蓝苍白
        mat.SetColor("_SkyMid", new Color(0.56f, 0.60f, 0.62f));
        mat.SetColor("_HorizonGlowCol", new Color(0.78f, 0.74f, 0.66f));
        mat.SetColor("_SkyHorizon", new Color(0.60f, 0.65f, 0.72f));   // ≈ sunnyFogColor，天地一体
        mat.SetColor("_GroundCol", new Color(0.60f, 0.65f, 0.72f));    // 雾色派生
        mat.SetFloat("_GlowHeight", 0.14f);
        mat.SetFloat("_MidHeight", 0.50f);
        mat.SetFloat("_Exposure", 1.0f);
        mat.SetFloat("_BandingAmount", 0.55f);
        mat.SetColor("_SunWashCol", new Color(0.90f, 0.82f, 0.68f));
        mat.SetFloat("_SunWashAmt", 0.15f);
        mat.SetColor("_SunTint", new Color(1.0f, 0.72f, 0.42f));
        mat.SetFloat("_SunSize", 5f);
        mat.SetFloat("_SunGlow", 0.9f);
        mat.SetFloat("_SunDiscStrength", 1f);
        mat.SetFloat("_SunEdgeRagged", 0.45f);
        // 日轮图腾（静态雕刻参数，不随天气变；天气只调 _SunGlow/_SunDiscStrength）
        mat.SetFloat("_TotemRayCount", 18f);
        mat.SetFloat("_TotemRayLen", 2.8f);
        mat.SetFloat("_CarveShadow", 0.30f);
        mat.SetVector("_SunDir", new Vector4(-0.35f, 0.55f, -0.76f, 0f)); // 编辑态默认≈Golden Hour 方位
        // 岩面颗粒（白天克制档）+ 卷云笔触（相位驱动，编辑态=白天档）
        mat.SetFloat("_GrainAmount", 0.028f);
        mat.SetFloat("_GrainScale", 90f);
        mat.SetFloat("_MottleScale", 2.3f);
        mat.SetFloat("_StrokeAmount", 0.05f);
        // 撒灰夜空：编辑态默认 0（白天），Playtest 夜景由控制器渐显
        mat.SetFloat("_StarBlend", 0f);
        mat.SetColor("_StarColorA", new Color(0.92f, 0.90f, 0.84f));
        mat.SetColor("_StarColorB", new Color(0.85f, 0.42f, 0.28f));
        mat.SetFloat("_StarDensity", 14f);
        mat.SetFloat("_StarSize", 0.10f);
        mat.SetColor("_AshColor", new Color(0.72f, 0.70f, 0.66f));
        mat.SetFloat("_AshStrength", 0.35f);
        mat.SetFloat("_AshWidth", 0.22f);
        mat.SetFloat("_SkyRotSpeed", 0.06f);
        // 点描撒灰 + 暗裂谷（图腾化精修轮）
        mat.SetFloat("_StippleDensity", 110f);
        mat.SetFloat("_StippleSize", 0.15f);
        mat.SetFloat("_StippleStrength", 1.0f);
        mat.SetFloat("_RiftWidth", 0.38f);
        mat.SetFloat("_RiftDepth", 0.60f);
        mat.SetFloat("_RiftWander", 0.10f);
        EditorUtility.SetDirty(mat);

        // 赋给场景（sky.mat 留盘备份不动）+ 接线控制器
        RenderSettings.skybox = mat;
        var wc = Object.FindFirstObjectByType<EmotionWeatherController>();
        if (wc != null && wc.skyboxMaterial != mat)
        {
            wc.skyboxMaterial = mat;
            EditorUtility.SetDirty(wc);
        }
        Debug.Log("[GlimmerVisualSetup] Skybox → Glimmer/SkyGradient (San rock-art sky)");
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
        ca.saturation.Override(0f);       // 苍白草原调色板下 +8 会把稻草推回绿黄（E6 校准）
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

        wc.stormFogColor = new Color(0.20f, 0.22f, 0.26f);
        wc.sunnyFogColor = new Color(0.60f, 0.65f, 0.72f);   // 空气蓝灰，=_SkyHorizon 基线
        wc.fogLinearSunnyStart = 60f;
        wc.fogLinearSunnyEnd = 300f;     // 推出地形对角线，远山只柔化不溶解
        wc.fogLinearStormStart = 30f;
        wc.fogLinearStormEnd = 140f;
        wc.dimnessFogWeight = 0.45f;

        wc.maxRainEmission = 2200f;
        wc.minRainSize = 0.05f;
        wc.maxRainSize = 0.09f;
        wc.maxRainWindForce = 9f;
        wc.maxRainTurbulenceZ = 1.2f;
        wc.turbulenceFrequency = 0.2f;

        // 清序列化残留：这两个滑条在 allowExternalDrive 下由 binder 每帧覆写，
        // 但残留值（rainIntensity=-1 永久暴雨、starVisibility=1 恒满天星）会在
        // binder 缺席/未进 Play 时读作假默认。中性归零，驱动权交还 binder。
        wc.allowExternalDrive = true;
        wc.rainIntensity = 0f;
        wc.starVisibility = 0f;

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
        RenderSettings.fogColor = new Color(0.60f, 0.65f, 0.72f);
        RenderSettings.fogStartDistance = 60f;
        RenderSettings.fogEndDistance = 300f;
        PreviewSky(sun, new Color(0.60f, 0.65f, 0.72f), 0f);
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
        RenderSettings.fogColor = new Color(0.20f, 0.22f, 0.26f);
        RenderSettings.fogStartDistance = 30f;
        RenderSettings.fogEndDistance = 140f;
        PreviewSky(sun, new Color(0.20f, 0.22f, 0.26f), 1f);
        SceneView.RepaintAll();
        Debug.Log("[GlimmerVisualSetup] Storm preview lighting set");
    }

    /// <summary>
    /// 编辑态天空预览：把预览光照对应相位的天空色板写进 SkyGradient.mat。
    /// 与 EmotionWeatherController.UpdateSkybox 同一套四相映射
    /// （badT 0=晴 1=暴雨；金色时刻≈golden 相），保证编辑态预览=运行时基线；
    /// 进 Play 后控制器接管全部属性。
    /// </summary>
    static void PreviewSky(Light sun, Color fogColor, float badT)
    {
        var mat = AssetDatabase.LoadAssetAtPath<Material>(SkyMatPath);
        if (mat == null) return;

        // golden 相色板（Preview Golden Hour 的太阳仰角 38° 实际偏 day，
        // 但预览的意义是看最富的状态 → 直接给 golden 相）
        Color zenith = Color.Lerp(new Color(0.30f, 0.38f, 0.46f), new Color(0.20f, 0.23f, 0.28f), badT);
        Color mid    = Color.Lerp(new Color(0.62f, 0.52f, 0.48f), new Color(0.28f, 0.30f, 0.34f), badT);
        Color glow   = Color.Lerp(new Color(0.95f, 0.62f, 0.38f), new Color(0.38f, 0.38f, 0.38f), badT);
        mat.SetColor("_SkyZenith", zenith);
        mat.SetColor("_SkyMid", mid);
        mat.SetColor("_HorizonGlowCol", glow);
        mat.SetColor("_SkyHorizon", fogColor);
        mat.SetColor("_GroundCol", fogColor);
        mat.SetColor("_SunWashCol", new Color(1.0f, 0.60f, 0.38f));
        mat.SetFloat("_SunWashAmt", 0.45f * (1f - badT));
        mat.SetFloat("_BandingAmount", 0.55f * (1f - badT * 0.5f));
        mat.SetFloat("_StrokeAmount", Mathf.Lerp(0.10f, 0.16f, badT));
        if (sun != null) mat.SetVector("_SunDir", -sun.transform.forward);
        mat.SetFloat("_SunGlow", Mathf.Lerp(0.9f, 0.15f, badT));
        mat.SetFloat("_SunDiscStrength", 1f - badT * 0.9f);
        mat.SetFloat("_StarBlend", 0f);
        if (RenderSettings.skybox != mat) RenderSettings.skybox = mat;
    }
}
