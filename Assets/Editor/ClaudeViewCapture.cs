using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using System.IO;

// Editor-only utility: captures the Scene View or Game View camera to a PNG
// under Assets/Screenshots so Claude (MCP) can inspect the current visuals.
public static class ClaudeViewCapture
{
    const string OutDir = "Assets/Screenshots";

    [MenuItem("Tools/Claude/Capture SceneView")]
    public static void CaptureSceneView()
    {
        var sv = SceneView.lastActiveSceneView;
        if (sv == null || sv.camera == null)
        {
            Debug.LogWarning("[ClaudeViewCapture] No active SceneView.");
            return;
        }
        Capture(sv.camera, Path.Combine(OutDir, "claude_sceneview.png"), 1280, 720);
    }

    // 3/4 overview: spawn a temp camera at an explicit vantage point over the
    // terrain — avoids SceneView camera state entirely, so captures are repeatable.
    [MenuItem("Tools/Claude/Capture Overview")]
    public static void CaptureOverview()
    {
        var terrain = Object.FindFirstObjectByType<TerrainGenerator>();
        Vector3 center = terrain != null ? terrain.transform.position : Vector3.zero;
        float ext = terrain != null ? terrain.width * terrain.scale * 0.5f : 80f;

        var rot = Quaternion.Euler(30f, 225f, 0f);
        Vector3 eye = center + new Vector3(0f, 4f, 0f) - rot * Vector3.forward * (ext * 2.1f);

        var go = new GameObject("~ClaudeTempCam") { hideFlags = HideFlags.HideAndDontSave };
        try
        {
            var cam = go.AddComponent<Camera>();
            cam.transform.SetPositionAndRotation(eye, rot);
            cam.clearFlags = CameraClearFlags.Skybox;
            cam.fieldOfView = 50f;
            cam.nearClipPlane = 0.3f;
            cam.farClipPlane = 2000f;
            Capture(cam, Path.Combine(OutDir, "claude_overview.png"), 1280, 720);
        }
        finally
        {
            Object.DestroyImmediate(go);
        }
    }


    [MenuItem("Tools/Claude/Capture MainCamera")]
    public static void CaptureMainCamera()
    {
        var cam = Camera.main;
        if (cam == null)
        {
            Debug.LogWarning("[ClaudeViewCapture] No MainCamera in scene.");
            return;
        }
        // 用干净的临时相机复制主相机位姿拍摄：手动 cam.Render() 对带
        // URP Additional Data 的相机不可靠（后处理/深度预通道状态不完整）。
        var go = new GameObject("~ClaudeTempCam") { hideFlags = HideFlags.HideAndDontSave };
        try
        {
            var tmp = go.AddComponent<Camera>();
            tmp.CopyFrom(cam);
            tmp.targetTexture = null;
            Capture(tmp, Path.Combine(OutDir, "claude_maincam.png"), 1280, 720);
        }
        finally
        {
            Object.DestroyImmediate(go);
        }
    }

    // play 安全的正午定光（镜像 ClaudeAfterglowVerify.Noon 的光照段，去掉 OpenScene——
    // 那个会退出 play）。用法：先 Step 推帧 → 本菜单压光 → 立刻 Capture（不再 Step，
    // 否则 LightManager 会把光照抢回真实时刻）。
    [MenuItem("Tools/Claude/Noon Light Override (play-safe)")]
    public static void NoonLightOverride()
    {
        var sun = RenderSettings.sun;
        if (sun == null)
        {
            var sgo = GameObject.Find("Directional Light");
            if (sgo != null) sun = sgo.GetComponent<Light>();
        }
        if (sun != null)
        {
            sun.transform.rotation = Quaternion.Euler(60f, 215f, 0f);
            sun.color = new Color(1.0f, 0.95f, 0.85f);
            sun.intensity = 1.15f;
        }
        RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Trilight;
        RenderSettings.ambientSkyColor     = new Color(0.55f, 0.62f, 0.72f);
        RenderSettings.ambientEquatorColor = new Color(0.52f, 0.48f, 0.40f);
        RenderSettings.ambientGroundColor  = new Color(0.28f, 0.23f, 0.17f);
        RenderSettings.fog = true;
        RenderSettings.fogMode = FogMode.Linear;
        Color dayFog = new Color(0.70f, 0.66f, 0.56f);
        RenderSettings.fogColor = dayFog;
        RenderSettings.fogStartDistance = 60f;
        RenderSettings.fogEndDistance = 300f;

        // 批次3B：override 期间 LightManager 停摆，反弹光也要手工摆（正午：反方位全强度）
        var bounceNoon = GameObject.Find("Bounce Light");
        if (bounceNoon != null)
        {
            var bl = bounceNoon.GetComponent<Light>();
            if (bl != null)
            {
                bl.transform.rotation = Quaternion.Euler(30f, 35f, 0f);   // 215+180
                bl.color = new Color(0.50f, 0.45f, 0.36f);
                bl.intensity = 0.25f;
            }
        }
        ClearRainForShot();

        var mat = AssetDatabase.LoadAssetAtPath<Material>("Assets/Materials/Glimmer/SkyGradient.mat");
        if (mat != null)
        {
            mat.SetColor("_SkyZenith", new Color(0.22f, 0.44f, 0.70f));
            mat.SetColor("_SkyMid",    new Color(0.45f, 0.64f, 0.78f));
            mat.SetColor("_HorizonGlowCol", new Color(0.86f, 0.78f, 0.60f));
            mat.SetColor("_SkyHorizon", dayFog);
            mat.SetColor("_GroundCol", dayFog);
            mat.SetColor("_SunWashCol", new Color(0.90f, 0.82f, 0.68f));
            mat.SetFloat("_SunWashAmt", 0.15f);
            mat.SetFloat("_HaloAmt", 0f);
            mat.SetFloat("_AntiGlowAmt", 0f);
            mat.SetFloat("_StarBlend", 0f);
            mat.SetFloat("_MoonGlow", 0f);
            if (sun != null) mat.SetVector("_SunDir", -sun.transform.forward);   // 暂停时 EWC 不写，日轮方位须手工同步
        }
        Debug.Log("[ClaudeViewCapture] Noon light override applied (play-safe)");
    }

    // 黄昏覆盖（play-safe）：验证光照细腻轮的黄金时刻效果。
    // 与 Noon 同手法——播放中 binder 每帧按真实时刻回写，暂停时本覆盖保持；
    // 天空/光照参数会在下次 unpause/play 时被驱动层自动复原，无资产污染。
    [MenuItem("Tools/Claude/GoldenHour Light Override (play-safe)")]
    public static void GoldenHourLightOverride()
    {
        var sun = RenderSettings.sun;
        if (sun == null)
        {
            var sgo = GameObject.Find("Directional Light");
            if (sgo != null) sun = sgo.GetComponent<Light>();
        }
        if (sun != null)
        {
            sun.transform.rotation = Quaternion.Euler(12f, 170f, 0f);   // 贴地平线的逆日，yaw 对齐 LightManager.SunDirection（天空太阳方位）
            sun.color = new Color(1.0f, 0.52f, 0.26f);                  // 落日橙
            sun.intensity = 1.25f;
        }
        RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Trilight;
        RenderSettings.ambientSkyColor     = new Color(0.38f, 0.34f, 0.50f);  // 暮蓝紫
        RenderSettings.ambientEquatorColor = new Color(0.72f, 0.50f, 0.36f);  // 地平线暖橙反弹
        RenderSettings.ambientGroundColor  = new Color(0.28f, 0.20f, 0.14f);  // 大地暗赭
        RenderSettings.fog = true;
        RenderSettings.fogMode = FogMode.Linear;
        Color duskFog = new Color(0.58f, 0.42f, 0.34f);
        RenderSettings.fogColor = duskFog;
        RenderSettings.fogStartDistance = 18f;    // 保持修复后的雾公式（Linear 18/130）
        RenderSettings.fogEndDistance = 130f;

        // 批次3B：override 期间 LightManager 停摆，反弹光也要手工摆（黄昏：反方位半强度暖赭）
        var bounceGold = GameObject.Find("Bounce Light");
        if (bounceGold != null)
        {
            var bl = bounceGold.GetComponent<Light>();
            if (bl != null)
            {
                bl.transform.rotation = Quaternion.Euler(30f, 350f, 0f);  // 170+180
                bl.color = new Color(0.55f, 0.38f, 0.26f);
                bl.intensity = 0.13f;
            }
        }
        ClearRainForShot();

        var mat = AssetDatabase.LoadAssetAtPath<Material>("Assets/Materials/Glimmer/SkyGradient.mat");
        if (mat != null)
        {
            mat.SetColor("_SkyZenith", new Color(0.14f, 0.15f, 0.34f));      // 深靛
            mat.SetColor("_SkyMid",    new Color(0.58f, 0.32f, 0.42f));      // 暮紫玫瑰
            mat.SetColor("_HorizonGlowCol", new Color(0.98f, 0.56f, 0.28f)); // 落日橙光带
            mat.SetColor("_SkyHorizon", duskFog);
            mat.SetColor("_GroundCol", duskFog);
            mat.SetColor("_SunWashCol", new Color(0.98f, 0.60f, 0.32f));
            mat.SetFloat("_SunWashAmt", 0.35f);
            // _HaloAmt/_AntiGlowAmt/_StarBlend/_MoonGlow 不动，保留图腾层现状
            if (sun != null) mat.SetVector("_SunDir", -sun.transform.forward);   // 暂停时 EWC 不写，日轮方位须手工同步
        }
        Debug.Log("[ClaudeViewCapture] GoldenHour light override applied (play-safe)");
    }

    // override 拍摄前清掉冻结的雨痕（暂停时粒子不更新，上一时刻的雨会残留在画面里）。
    // play-safe：只动运行时粒子状态，unpause 后由天气系统按实况重新发射。
    static void ClearRainForShot()
    {
        var rain = GameObject.Find("Rainsystem");
        var ps = rain != null ? rain.GetComponent<ParticleSystem>() : null;
        if (ps != null) ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
    }

    // 批次4 调参探针：把 AO 强度推到 2.0 超档（验证采样通路是否活着——
    // 若超档可见斑驳，通路正常、只是强度取舍；若无变化则采样断了）。play-safe：只写材质资产。
    [MenuItem("Tools/Claude/AO Overdrive (play-safe)")]
    public static void AOOverdrive()
    {
        SetAOStrength(2.0f, 2.0f);
    }

    // 恢复烘焙菜单写入的正式强度（地形 1.0 / 草 0.8）。
    [MenuItem("Tools/Claude/AO Restore Baked Strength (play-safe)")]
    public static void AORestore()
    {
        SetAOStrength(1.0f, 0.8f);
    }

    static void SetAOStrength(float terrain, float grass)
    {
        var tm = AssetDatabase.LoadAssetAtPath<Material>("Assets/Materials/Glimmer/Terrain_Glimmer.mat");
        var gm = AssetDatabase.LoadAssetAtPath<Material>("Assets/Materials/Glimmer/Grass_Glimmer.mat");
        if (tm != null)
        {
            var v = tm.GetVector("_TerrainAOBounds"); v.w = terrain;
            tm.SetVector("_TerrainAOBounds", v); EditorUtility.SetDirty(tm);
        }
        if (gm != null)
        {
            var v = gm.GetVector("_TerrainAOBounds"); v.w = grass;
            gm.SetVector("_TerrainAOBounds", v); EditorUtility.SetDirty(gm);
        }
        Debug.Log($"[ClaudeViewCapture] AO strength → terrain={terrain}, grass={grass}");
    }

    // 清空手动情绪注入（play-safe）：MCP 在 play 下不能改组件字段，走菜单绕过。
    // 注入器回到平静值后，E_env 会按惯性慢慢回落——暴雨不是瞬间停的，和世界规则一致。
    [MenuItem("Tools/Claude/Clear Storm Injection (play-safe)")]
    public static void ClearStormInjection()
    {
        var injector = Object.FindFirstObjectByType<GlimmerDiary.Utils.ManualEmotionInjector>();
        if (injector == null)
        {
            Debug.LogWarning("[ClaudeViewCapture] 场景里没找到 ManualEmotionInjector。");
            return;
        }
        injector.valence = 0f;
        injector.arousal = 0.2f;
        Debug.Log("[ClaudeViewCapture] Storm injection cleared (valence=0, arousal=0.2)");
    }

    // 失焦不走帧环境的通用步进截图：EditorApplication.Step() 手动推进 N 帧
    // （play 下 Update/LateUpdate 真实执行）后用主相机位姿拍摄。
    [MenuItem("Tools/Claude/Step + Capture MainCamera")]
    public static void StepAndCaptureMainCamera()
    {
        if (!Application.isPlaying)
        {
            Debug.LogWarning("[ClaudeViewCapture] Step + Capture MainCamera 需要在 play 模式。");
            return;
        }
        for (int i = 0; i < 10; i++) EditorApplication.Step();
        CaptureMainCamera();
    }

    // 失焦不走帧环境下的帧步进验证：EditorApplication.Step() 手动推进 N 帧
    // （play 模式下 Update/LateUpdate 真实执行），再对指定物体拍摄。
    // 用途示例：种子絮链路 binder→EWC.UpdateSeedFluff 需要若干帧才生效。
    [MenuItem("Tools/Claude/Step + Capture Fluff")]
    public static void StepAndCaptureFluff()
    {
        if (!Application.isPlaying)
        {
            Debug.LogWarning("[ClaudeViewCapture] Step + Capture Fluff 需要在 play 模式。");
            return;
        }
        for (int i = 0; i < 10; i++) EditorApplication.Step();

        var go2 = GameObject.Find("SeedFluffSystem");
        var ps = go2 != null ? go2.GetComponent<ParticleSystem>() : null;
        Vector3 focus = ps != null ? ps.transform.position : Vector3.zero;
        if (ps != null)
        {
            Debug.Log($"[ClaudeViewCapture] fluff: rate={ps.emission.rateOverTime.constant:F1} playing={ps.isPlaying}");
            ps.Simulate(4f, true, false);   // 快进铺满（restart=false 保留驱动状态）
            ps.Play(true);
        }

        var go = new GameObject("~ClaudeTempCam") { hideFlags = HideFlags.HideAndDontSave };
        try
        {
            var cam = go.AddComponent<Camera>();
            Vector3 eye = focus + new Vector3(-7f, 3f, 6f);
            cam.transform.SetPositionAndRotation(eye, Quaternion.LookRotation(focus - eye, Vector3.up));
            cam.clearFlags = CameraClearFlags.Skybox;
            cam.fieldOfView = 55f;
            cam.nearClipPlane = 0.3f;
            cam.farClipPlane = 2000f;
            Capture(cam, Path.Combine(OutDir, "claude_fluff.png"), 1280, 720);
        }
        finally
        {
            Object.DestroyImmediate(go);
        }
    }

    // Batch 4 zone 对比验证：同一构参数各拍一张低洼(湿=1.0)与石区(干=0.0)近景。
    // 低空斜视角 + 无雾近距，肉眼应能直接分辨青绿/枯黄。
    [MenuItem("Tools/Claude/Capture Zone Contrast")]
    public static void CaptureZoneContrast()
    {
        CaptureAt(new Vector3(-10.6f, 0f, -8.9f), "claude_zone_lowland.png");
        CaptureAt(new Vector3(37.4f, 0f, -40.9f), "claude_zone_stone.png");
    }

    static void CaptureAt(Vector3 focus, string fileName)
    {
        // 锚点是 XZ 平面坐标，Y 需落地（地形起伏大，固定 Y 会钻进沙丘）
        if (Physics.Raycast(focus + Vector3.up * 100f, Vector3.down, out var hit, 300f))
            focus = hit.point;
        var go = new GameObject("~ClaudeTempCam") { hideFlags = HideFlags.HideAndDontSave };
        try
        {
            var cam = go.AddComponent<Camera>();
            Vector3 eye = focus + new Vector3(-10f, 7f, 10f);
            cam.transform.SetPositionAndRotation(eye, Quaternion.LookRotation(focus - eye, Vector3.up));
            cam.clearFlags = CameraClearFlags.Skybox;
            cam.fieldOfView = 50f;
            cam.nearClipPlane = 0.3f;
            cam.farClipPlane = 2000f;
            Capture(cam, Path.Combine(OutDir, fileName), 1280, 720);
        }
        finally { Object.DestroyImmediate(go); }
    }

    // 光照链路综合探针：反射读 LightManager 私有引用真假、直接调用 SetTimePercent、
    // 手动调用 binder.LateUpdate——一次运行定位"调用链断在哪一环"。
    [MenuItem("Tools/Claude/Probe Light Path")]
    public static void ProbeLightPath()
    {
        const System.Reflection.BindingFlags F = System.Reflection.BindingFlags.NonPublic
                                               | System.Reflection.BindingFlags.Instance;
        var lm = Object.FindFirstObjectByType<LightManager>();
        if (lm == null) { Debug.LogWarning("[LightProbe] no LightManager"); return; }
        var tlm = typeof(LightManager);
        var dl     = tlm.GetField("DirectionalLight", F)?.GetValue(lm) as Light;
        var preset = tlm.GetField("DayNightPreset", F)?.GetValue(lm);
        var baseI  = tlm.GetField("_baseSunIntensity", F)?.GetValue(lm);
        var tod    = tlm.GetField("TimeOfDay", F)?.GetValue(lm);
        Debug.Log($"[LightProbe] lm id={lm.GetInstanceID()} dl={(dl == null ? "NULL" : dl.GetInstanceID().ToString())} " +
                  $"preset={(preset == null ? "NULL" : "ok")} base={baseI} weatherDim={lm.weatherDim:F3} TimeOfDay={tod}");
        if (dl != null)
        {
            float before = dl.intensity;
            lm.SetTimePercent(0.5f);
            Debug.Log($"[LightProbe] direct SetTimePercent(0.5): intensity {before:F3} -> {dl.intensity:F3} " +
                      $"rot={dl.transform.eulerAngles} TimeOfDay={tlm.GetField("TimeOfDay", F)?.GetValue(lm)}");
        }
        var binder = Object.FindFirstObjectByType<WorldAtmosphereBinder>();
        if (binder == null) { Debug.LogWarning("[LightProbe] no binder"); return; }
        Debug.Log($"[LightProbe] binder.lightManager={(binder.lightManager == null ? "NULL" : binder.lightManager.GetInstanceID().ToString())} " +
                  $"same-as-found={(binder.lightManager == lm)}");
        var lu = typeof(WorldAtmosphereBinder).GetMethod("LateUpdate", F);
        if (lu != null)
        {
            lu.Invoke(binder, null);
            Debug.Log($"[LightProbe] manual binder.LateUpdate -> TimeOfDay={tlm.GetField("TimeOfDay", F)?.GetValue(lm)} " +
                      $"intensity={(dl != null ? dl.intensity : -1f):F3}");
        }
    }

    // WorldManager 单例探针：play 中域重载后 Instance 是否断链、对象本体是否还活着。
    [MenuItem("Tools/Claude/Probe WorldManager")]
    public static void ProbeWorldManager()
    {
        var all = Object.FindObjectsByType<WorldManager>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        Debug.Log($"[WMProbe] found={all.Length}");
        foreach (var w in all)
            Debug.Log($"[WMProbe] id={w.GetInstanceID()} active={w.gameObject.activeInHierarchy} " +
                      $"scene='{w.gameObject.scene.name}' hideFlags={w.gameObject.hideFlags} enabled={w.enabled}");
        var inst = WorldManager.Instance;
        Debug.Log($"[WMProbe] Instance={(inst == null ? "<null>" : inst.GetInstanceID().ToString())}");
    }

    // Batch 4 草色通路诊断（临时）：① 反射读 binder 私有缓存，确认数据端是否就位；
    // ② 直接把 _GrassColorEnable=1 + 季节色=红 写进 shader 全局并拍摄（不 Step，
    // 避免 binder 下一帧覆盖）——草变红 = shader 链路通、问题在 binder 数据；
    // 不变红 = shader 未重编译/未绑定。
    [MenuItem("Tools/Claude/Probe Grass Globals")]
    public static void ProbeGrassGlobals()
    {
        var binder = Object.FindFirstObjectByType<WorldAtmosphereBinder>(FindObjectsInactive.Include);
        if (binder == null) { Debug.LogWarning("[GrassProbe] 无 WorldAtmosphereBinder"); return; }

        var t = typeof(WorldAtmosphereBinder);
        const System.Reflection.BindingFlags F = System.Reflection.BindingFlags.NonPublic
                                               | System.Reflection.BindingFlags.Instance;
        foreach (var n in new[] { "_initialized", "_grassInitialized", "_grassAnchorsReady",
                                  "_grassDefaultMoisture", "_grassDrought", "_grassDecay" })
        {
            var fi = t.GetField(n, F);
            Debug.Log($"[GrassProbe] {n} = {(fi == null ? "<missing>" : fi.GetValue(binder))}");
        }
        var ids     = t.GetField("_grassZoneIds", F)?.GetValue(binder) as string[];
        var anchors = t.GetField("_grassZoneAnchors", F)?.GetValue(binder) as Vector4[];
        var push    = t.GetField("_grassMoisturePush", F)?.GetValue(binder) as float[];
        if (ids != null)
            for (int i = 0; i < ids.Length; i++)
                Debug.Log($"[GrassProbe] zone[{i}] {ids[i]} anchor={(anchors != null && i < anchors.Length ? anchors[i].ToString("F1") : "?")} " +
                          $"moisturePush={(push != null && i < push.Length ? push[i].ToString("F2") : "?")}");
        var tintFi = t.GetField("_grassSeasonTint", F);
        Debug.Log($"[GrassProbe] seasonTint(cache) = {(tintFi == null ? "<missing>" : tintFi.GetValue(binder))}");

        Debug.Log($"[GrassProbe] global: enable={Shader.GetGlobalFloat("_GrassColorEnable"):F2} " +
                  $"zoneCount={Shader.GetGlobalFloat("_GrassZoneCount"):F0} " +
                  $"defaultMoist={Shader.GetGlobalFloat("_GrassDefaultMoisture"):F2} " +
                  $"droughtAmt={Shader.GetGlobalFloat("_GrassDroughtAmt"):F2} " +
                  $"decay={Shader.GetGlobalFloat("_GrassDecay"):F2} " +
                  $"seasonTint={Shader.GetGlobalColor("_GrassSeasonTint")}");

        var grassMat = AssetDatabase.LoadAssetAtPath<Material>("Assets/Materials/Glimmer/Grass_Glimmer.mat");
        var shader = grassMat != null ? grassMat.shader : Shader.Find("Glimmer/Grass");
        Debug.Log($"[GrassProbe] shader={(shader != null ? shader.name : "<null>")} isSupported={(shader != null && shader.isSupported)}");
        if (shader != null)
        {
            var msgs = ShaderUtil.GetShaderMessages(shader);
            int err = 0;
            foreach (var m in msgs)
                if (m.severity == UnityEditor.Rendering.ShaderCompilerMessageSeverity.Error ||
                    m.severity == UnityEditor.Rendering.ShaderCompilerMessageSeverity.Warning)
                { err++; Debug.LogWarning($"[GrassProbe] shaderMsg[{m.severity}]: {m.message} @ {m.file}:{m.line}"); }
            Debug.Log($"[GrassProbe] shaderMessages err/warn count = {err}");
        }

        // 纯诊断：只读不写。binder LateUpdate 正常推送草色，本菜单不覆盖任何全局。
    }

    [MenuItem("Tools/Glimmer/Wave 0 Player Test")]
    private static void OpenWave0PlayerTest()
    {
        var window = EditorWindow.GetWindow<Wave0PlayerTestWindow>();
        window.titleContent = new GUIContent("Wave 0 Player Test");
        window.minSize = new Vector2(960f, 800f);
        window.Show();
    }

    private sealed class Wave0PlayerTestWindow : EditorWindow
    {
        private enum TestMode { Baseline, SeedFluff, T7Trample, Summer, Winter }

        private const string GrassPresetPath = "Assets/Settings/GrassPreset_Glimmer.asset";
        private const double RenderInterval = 1.0 / 15.0;
        private const uint FluffRandomSeed = 0x57415645;
        private const float PreviewUiHeight = 258f;

        private static readonly int TrampleCountId = Shader.PropertyToID("_TrampleCount");
        private static readonly int TramplePointsId = Shader.PropertyToID("_TramplePoints");
        private static readonly int GrassZoneCountId = Shader.PropertyToID("_GrassZoneCount");
        private static readonly int GrassDefaultMoistureId = Shader.PropertyToID("_GrassDefaultMoisture");
        private static readonly int GrassMoistDryId = Shader.PropertyToID("_GrassMoistDry");
        private static readonly int GrassMoistWetId = Shader.PropertyToID("_GrassMoistWet");
        private static readonly int GrassSeasonTintId = Shader.PropertyToID("_GrassSeasonTint");
        private static readonly int GrassDroughtAmountId = Shader.PropertyToID("_GrassDroughtAmt");
        private static readonly int GrassDecayId = Shader.PropertyToID("_GrassDecay");
        private static readonly int GrassColorEnableId = Shader.PropertyToID("_GrassColorEnable");

        private TestMode _mode = TestMode.Baseline;
        private TestMode? _requestedMode;
        private Camera _sourceCamera;
        private Camera _previewCamera;
        private GameObject _previewCameraObject;
        private RenderTexture _previewTexture;
        private ParticleSystem _fluff;
        private GameObject _fluffObject;
        private Material _fluffMaterial;
        private GlimmerDiary.Flora.GrassPreset _grassPreset;
        private Vector3 _t7Focus;
        private Vector4[] _t7Points;
        private string _error;
        private bool _ready;
        private bool _renderRequested;
        private bool _callbacksRegistered;
        private double _lastUpdateTime;
        private double _lastRenderTime;

        private string DiagnosticText =>
            $"Mode={_mode} | TempCamera={(_previewCamera != null ? 1 : 0)} | " +
            $"TempFluff={(_fluff != null ? 1 : 0)} | Particles={(_fluff != null ? _fluff.particleCount : 0)} | " +
            $"RT={(_previewTexture != null ? $"{_previewTexture.width}x{_previewTexture.height}" : "none")}";

        private void OnEnable()
        {
            RegisterCallbacks();
            _requestedMode = TestMode.Baseline;
            _lastUpdateTime = EditorApplication.timeSinceStartup;
        }

        private void OnDisable()
        {
            UnregisterCallbacks();
            CleanupTemporaryObjects();
        }

        private void OnGUI()
        {
            EditorGUILayout.LabelField("Wave 0 玩家测试（仅 Edit Mode）", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox(
                "依次选择阶段并只观察窗口内画面。窗口不会运行世界模拟、改场景、改主相机或写出图片。",
                MessageType.Info);

            bool playBlocked = Application.isPlaying || EditorApplication.isPlayingOrWillChangePlaymode;
            using (new EditorGUI.DisabledScope(playBlocked))
            {
                EditorGUILayout.BeginHorizontal();
                if (GUILayout.Button("Baseline")) RequestMode(TestMode.Baseline);
                if (GUILayout.Button("Seed Fluff")) RequestMode(TestMode.SeedFluff);
                if (GUILayout.Button("T7 Trample")) RequestMode(TestMode.T7Trample);
                if (GUILayout.Button("Summer")) RequestMode(TestMode.Summer);
                if (GUILayout.Button("Winter")) RequestMode(TestMode.Winter);
                EditorGUILayout.EndHorizontal();
            }

            EditorGUILayout.Space(4f);
            EditorGUILayout.LabelField("玩家验收问题", EditorStyles.boldLabel);
            EditorGUILayout.LabelField("Baseline：主舞台是否正常，且没有测试痕迹或测试粒子？", EditorStyles.wordWrappedLabel);
            EditorGUILayout.LabelField("Seed Fluff：主镜头里能否看见种子絮，而且它们不像发光球？", EditorStyles.wordWrappedLabel);
            EditorGUILayout.LabelField("T7 Trample：你能否在两秒内指出两片被压倒的草？", EditorStyles.wordWrappedLabel);
            EditorGUILayout.LabelField("Summer / Winter：不看按钮标签时，你能否区分夏草与冬草？", EditorStyles.wordWrappedLabel);

            EditorGUILayout.Space(4f);
            EditorGUILayout.LabelField("诊断（供 MCP 核对）", EditorStyles.miniBoldLabel);
            EditorGUILayout.SelectableLabel(DiagnosticText, EditorStyles.miniLabel, GUILayout.Height(18f));

            string visibleError = playBlocked
                ? "错误：Wave 0 玩家测试只能在 Edit Mode 运行。Editor 正在运行或准备进入 Play，预览已停止并清理。"
                : _error;
            if (!string.IsNullOrEmpty(visibleError))
                EditorGUILayout.HelpBox(visibleError, MessageType.Error);

            Rect previewRect = GUILayoutUtility.GetRect(320f, 4096f, 180f, 4096f, GUILayout.ExpandWidth(true), GUILayout.ExpandHeight(true));
            EditorGUI.DrawRect(previewRect, new Color(0.035f, 0.035f, 0.035f, 1f));
            if (_previewTexture != null)
                GUI.DrawTexture(previewRect, _previewTexture, ScaleMode.ScaleToFit, false);
            else if (!playBlocked && string.IsNullOrEmpty(_error))
                GUI.Label(previewRect, "等待 EditorApplication.update 生成内存预览...", EditorStyles.centeredGreyMiniLabel);
        }

        private void RequestMode(TestMode mode)
        {
            _requestedMode = mode;
            _renderRequested = true;
        }

        private void RegisterCallbacks()
        {
            if (_callbacksRegistered) return;
            EditorApplication.update += OnEditorUpdate;
            EditorApplication.playModeStateChanged += OnPlayModeStateChanged;
            EditorApplication.quitting += OnEditorQuitting;
            AssemblyReloadEvents.beforeAssemblyReload += OnBeforeAssemblyReload;
            _callbacksRegistered = true;
        }

        private void UnregisterCallbacks()
        {
            if (!_callbacksRegistered) return;
            EditorApplication.update -= OnEditorUpdate;
            EditorApplication.playModeStateChanged -= OnPlayModeStateChanged;
            EditorApplication.quitting -= OnEditorQuitting;
            AssemblyReloadEvents.beforeAssemblyReload -= OnBeforeAssemblyReload;
            _callbacksRegistered = false;
        }

        private void OnPlayModeStateChanged(PlayModeStateChange state)
        {
            if (state != PlayModeStateChange.ExitingEditMode && state != PlayModeStateChange.EnteredPlayMode) return;
            _error = "错误：Wave 0 玩家测试只能在 Edit Mode 运行；预览已停止并清理。";
            _ready = false;
            CleanupTemporaryObjects();
            Repaint();
        }

        private void OnEditorQuitting()
        {
            CleanupTemporaryObjects();
        }

        private void OnBeforeAssemblyReload()
        {
            CleanupTemporaryObjects();
        }

        private void OnEditorUpdate()
        {
            double now = EditorApplication.timeSinceStartup;
            float delta = _lastUpdateTime > 0.0 ? (float)System.Math.Min(System.Math.Max(now - _lastUpdateTime, 0.0), 0.1) : 0f;
            _lastUpdateTime = now;

            if (Application.isPlaying || EditorApplication.isPlayingOrWillChangePlaymode)
            {
                _error = "错误：Wave 0 玩家测试只能在 Edit Mode 运行；预览已停止并清理。";
                _ready = false;
                CleanupTemporaryObjects();
                Repaint();
                return;
            }

            if (_requestedMode.HasValue)
            {
                TestMode next = _requestedMode.Value;
                _requestedMode = null;
                ActivateMode(next);
            }

            if (!_ready) return;

            if (_fluff != null && delta > 0f)
                _fluff.Simulate(delta, true, false, true);

            int wantedWidth = Mathf.Clamp(Mathf.RoundToInt(position.width - 16f), 320, 1920);
            int wantedHeight = Mathf.Clamp(Mathf.RoundToInt(position.height - PreviewUiHeight), 180, 1080);
            if (EnsurePreviewTexture(wantedWidth, wantedHeight))
                _renderRequested = true;

            if (!_renderRequested && now - _lastRenderTime < RenderInterval) return;

            try
            {
                RenderPreview();
                _error = null;
                _lastRenderTime = now;
                _renderRequested = false;
            }
            catch (System.Exception ex)
            {
                _error = "预览渲染失败：" + ex.Message;
                _ready = false;
            }
            Repaint();
        }

        private void ActivateMode(TestMode mode)
        {
            _ready = false;
            _error = null;
            DestroyFluff();

            if (!TryGetUniqueMainCamera(out Camera mainCamera, out string cameraError))
            {
                _error = cameraError;
                Repaint();
                return;
            }

            EnsurePreviewCamera(mainCamera);
            _mode = mode;

            if (mode == TestMode.T7Trample && !PrepareT7())
            {
                Repaint();
                return;
            }

            if (mode == TestMode.SeedFluff && !CreateFluffClone())
            {
                Repaint();
                return;
            }

            if (mode == TestMode.Summer || mode == TestMode.Winter)
            {
                WorldAtmosphereBinder atmosphereBinder = Object.FindFirstObjectByType<WorldAtmosphereBinder>(FindObjectsInactive.Include);
                _grassPreset = atmosphereBinder != null ? atmosphereBinder.grassPreset : null;
                if (_grassPreset == null)
                    _grassPreset = AssetDatabase.LoadAssetAtPath<GlimmerDiary.Flora.GrassPreset>(GrassPresetPath);
                if (_grassPreset == null)
                {
                    _error = "场景 Binder 未连接且找不到草色 preset：" + GrassPresetPath;
                    Repaint();
                    return;
                }
            }

            _ready = true;
            _renderRequested = true;
            Repaint();
        }

        private bool TryGetUniqueMainCamera(out Camera mainCamera, out string error)
        {
            mainCamera = null;
            error = null;
            int count = 0;
            Camera[] cameras = Object.FindObjectsByType<Camera>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
            foreach (Camera camera in cameras)
            {
                if (camera == null || !camera.enabled || !camera.gameObject.activeInHierarchy || camera.gameObject.tag != "MainCamera")
                    continue;
                mainCamera = camera;
                count++;
            }

            if (count == 1) return true;
            error = count == 0
                ? "场景中没有唯一的活动 MainCamera；Wave 0 预览不会运行。"
                : $"场景中有 {count} 个活动 MainCamera；请保留唯一主相机后再测试。";
            mainCamera = null;
            return false;
        }

        private void EnsurePreviewCamera(Camera mainCamera)
        {
            if (_previewCamera != null && _sourceCamera == mainCamera) return;
            DestroyPreviewCamera();

            _sourceCamera = mainCamera;
            _previewCameraObject = new GameObject("~Wave0PlayerTestCamera")
            {
                hideFlags = HideFlags.HideAndDontSave
            };
            _previewCamera = _previewCameraObject.AddComponent<Camera>();
            _previewCamera.enabled = false;

            SyncPreviewCamera();
        }

        private void SyncPreviewCamera()
        {
            if (_sourceCamera == null || _previewCamera == null) return;
            _previewCamera.CopyFrom(_sourceCamera);
            _previewCamera.enabled = false;
            _previewCamera.targetTexture = null;
            _previewCamera.transform.SetPositionAndRotation(_sourceCamera.transform.position, _sourceCamera.transform.rotation);

            if (_mode == TestMode.T7Trample)
            {
                Vector3 horizontal = _sourceCamera.transform.position - _t7Focus;
                horizontal.y = 0f;
                if (horizontal.sqrMagnitude < 1e-4f)
                {
                    horizontal = -_sourceCamera.transform.forward;
                    horizontal.y = 0f;
                }
                horizontal.Normalize();
                Vector3 pushedPosition = _t7Focus + horizontal * 5.196f + Vector3.up * 3f;
                _previewCamera.transform.SetPositionAndRotation(
                    pushedPosition,
                    Quaternion.LookRotation(_t7Focus - pushedPosition, Vector3.up));
            }
        }

        private bool PrepareT7()
        {
            ZoneMap zoneMap = Object.FindFirstObjectByType<ZoneMap>(FindObjectsInactive.Include);
            if (zoneMap == null || !zoneMap.TryGetAnchorCenter("center", out Vector3 center, out _))
            {
                _error = "T7 无法运行：找不到 ZoneMap center 锚点。";
                return false;
            }

            Vector3 candidate = center + new Vector3(-8f, 0f, -5f);
            if (!zoneMap.TryGroundAt(candidate.x, candidate.z, out _t7Focus))
            {
                _error = "T7 无法运行：center 偏移点无法落到地形。";
                return false;
            }

            Vector3 separation = Vector3.right;
            Vector3 cameraRight = _sourceCamera.transform.right;
            cameraRight.y = 0f;
            if (cameraRight.sqrMagnitude > 1e-4f) separation = cameraRight.normalized;

            _t7Points = new Vector4[16];
            Vector3 a = _t7Focus - separation * 0.8f;
            Vector3 b = _t7Focus + separation * 0.8f;
            _t7Points[0] = new Vector4(a.x, a.z, 1.1f, 0.9f);
            _t7Points[1] = new Vector4(b.x, b.z, 1.1f, 0.9f);
            return true;
        }

        private bool CreateFluffClone()
        {
            GameObject sourceObject = GameObject.Find("SeedFluffSystem");
            ParticleSystem source = sourceObject != null ? sourceObject.GetComponent<ParticleSystem>() : null;
            ParticleSystemRenderer sourceRenderer = sourceObject != null ? sourceObject.GetComponent<ParticleSystemRenderer>() : null;
            ZoneMap zoneMap = Object.FindFirstObjectByType<ZoneMap>(FindObjectsInactive.Include);
            if (source == null || sourceRenderer == null || sourceRenderer.sharedMaterial == null)
            {
                _error = "Seed Fluff 无法运行：场景 SeedFluffSystem 或其 Renderer 材质缺失。";
                return false;
            }
            if (zoneMap == null || !zoneMap.TryGetAnchorCenter("riverbank", out Vector3 riverbank, out _))
            {
                _error = "Seed Fluff 无法运行：找不到 ZoneMap riverbank 锚点。";
                return false;
            }

            _fluffObject = new GameObject("~Wave0SeedFluff")
            {
                hideFlags = HideFlags.HideAndDontSave
            };
            _fluffObject.transform.position = riverbank + Vector3.up * 3f;
            _fluff = _fluffObject.AddComponent<ParticleSystem>();
            _fluff.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            _fluff.useAutoRandomSeed = false;
            _fluff.randomSeed = FluffRandomSeed;

            ParticleSystem.MainModule sourceMain = source.main;
            ParticleSystem.MainModule main = _fluff.main;
            main.duration = sourceMain.duration;
            main.loop = true;
            main.prewarm = false;
            main.playOnAwake = false;
            main.startLifetime = sourceMain.startLifetime;
            main.startSpeed = sourceMain.startSpeed;
            main.startSize = sourceMain.startSize;
            main.startColor = sourceMain.startColor;
            main.gravityModifier = sourceMain.gravityModifier;
            main.maxParticles = Mathf.Max(400, sourceMain.maxParticles);
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.scalingMode = sourceMain.scalingMode;

            ParticleSystem.EmissionModule emission = _fluff.emission;
            emission.enabled = true;
            emission.SetBursts(new ParticleSystem.Burst[0]);
            const float testWind = 0.9f;
            EmotionWeatherController weather = Object.FindFirstObjectByType<EmotionWeatherController>(FindObjectsInactive.Include);
            WorldAtmosphereBinder binder = Object.FindFirstObjectByType<WorldAtmosphereBinder>(FindObjectsInactive.Include);
            float threshold = binder != null ? binder.fluffWindThreshold : 0.7f;
            float maxEmission = weather != null ? weather.maxFluffEmission : 8f;
            emission.rateOverTime = maxEmission * Mathf.InverseLerp(threshold, 1f, testWind);

            ParticleSystem.ShapeModule shape = _fluff.shape;
            shape.enabled = true;
            shape.shapeType = ParticleSystemShapeType.Box;
            shape.scale = source.shape.scale;

            Vector3 wind = weather != null ? weather.windDirection : new Vector3(1f, 0f, 0.25f);
            wind.y = 0f;
            wind = wind.sqrMagnitude > 1e-4f ? wind.normalized : Vector3.right;
            float forceMagnitude = (weather != null ? weather.maxFluffWindForce : 0.45f) * testWind;
            ParticleSystem.ForceOverLifetimeModule force = _fluff.forceOverLifetime;
            force.enabled = true;
            force.space = ParticleSystemSimulationSpace.World;
            force.x = wind.x * forceMagnitude;
            force.y = 0f;
            force.z = wind.z * forceMagnitude;

            ParticleSystem.NoiseModule noise = _fluff.noise;
            noise.enabled = true;
            noise.separateAxes = true;
            noise.strengthX = 0.5f * testWind;
            noise.strengthY = 0.25f * testWind;
            noise.strengthZ = 0.3f * testWind;
            noise.frequency = 0.12f;
            noise.damping = true;
            noise.scrollSpeed = 0.2f;
            noise.quality = ParticleSystemNoiseQuality.Medium;

            ParticleSystemRenderer renderer = _fluff.GetComponent<ParticleSystemRenderer>();
            _fluffMaterial = new Material(sourceRenderer.sharedMaterial)
            {
                name = "~Wave0SeedFluffMaterial",
                hideFlags = HideFlags.HideAndDontSave
            };
            renderer.sharedMaterial = _fluffMaterial;
            renderer.renderMode = sourceRenderer.renderMode;
            renderer.alignment = sourceRenderer.alignment;
            renderer.sortMode = sourceRenderer.sortMode;
            renderer.sortingFudge = sourceRenderer.sortingFudge;
            renderer.normalDirection = sourceRenderer.normalDirection;
            renderer.pivot = sourceRenderer.pivot;
            renderer.minParticleSize = sourceRenderer.minParticleSize;
            renderer.maxParticleSize = sourceRenderer.maxParticleSize;
            renderer.shadowCastingMode = sourceRenderer.shadowCastingMode;
            renderer.receiveShadows = sourceRenderer.receiveShadows;
            renderer.sortingLayerID = sourceRenderer.sortingLayerID;
            renderer.sortingOrder = sourceRenderer.sortingOrder;

            _fluff.Simulate(7f, true, true, true);
            return true;
        }

        private bool EnsurePreviewTexture(int width, int height)
        {
            if (_previewTexture != null && _previewTexture.width == width && _previewTexture.height == height)
                return false;

            DestroyPreviewTexture();
            _previewTexture = new RenderTexture(width, height, 24, RenderTextureFormat.ARGB32)
            {
                name = "~Wave0PlayerTestRT",
                hideFlags = HideFlags.HideAndDontSave,
                antiAliasing = 1,
                useMipMap = false,
                autoGenerateMips = false
            };
            _previewTexture.Create();
            return true;
        }

        private void RenderPreview()
        {
            if (!TryGetUniqueMainCamera(out Camera mainCamera, out string cameraError))
                throw new System.InvalidOperationException(cameraError);
            EnsurePreviewCamera(mainCamera);
            SyncPreviewCamera();
            if (_previewTexture == null) throw new System.InvalidOperationException("内存 RenderTexture 尚未创建。");

            if (_mode == TestMode.T7Trample)
                RenderWithT7Globals();
            else if (_mode == TestMode.Summer || _mode == TestMode.Winter)
                RenderWithSeasonGlobals(_mode == TestMode.Summer ? _grassPreset.seasonSummerTint : _grassPreset.seasonWinterTint);
            else
                RenderCameraToTexture();
        }

        private void RenderWithT7Globals()
        {
            float oldCount = Shader.GetGlobalFloat(TrampleCountId);
            Vector4[] oldPoints = Shader.GetGlobalVectorArray(TramplePointsId);
            if (oldPoints == null || oldPoints.Length == 0)
                oldPoints = new Vector4[16];
            try
            {
                Shader.SetGlobalVectorArray(TramplePointsId, _t7Points);
                Shader.SetGlobalFloat(TrampleCountId, 2f);
                RenderCameraToTexture();
            }
            finally
            {
                Shader.SetGlobalVectorArray(TramplePointsId, oldPoints);
                Shader.SetGlobalFloat(TrampleCountId, oldCount);
            }
        }

        private void RenderWithSeasonGlobals(Color seasonTint)
        {
            float oldColorEnable = Shader.GetGlobalFloat(GrassColorEnableId);
            float oldZoneCount = Shader.GetGlobalFloat(GrassZoneCountId);
            float oldDefaultMoisture = Shader.GetGlobalFloat(GrassDefaultMoistureId);
            Color oldMoistDry = Shader.GetGlobalColor(GrassMoistDryId);
            Color oldMoistWet = Shader.GetGlobalColor(GrassMoistWetId);
            Color oldSeasonTint = Shader.GetGlobalColor(GrassSeasonTintId);
            float oldDroughtAmount = Shader.GetGlobalFloat(GrassDroughtAmountId);
            float oldDecay = Shader.GetGlobalFloat(GrassDecayId);

            try
            {
                float neutralMoisture = _grassPreset.moistureToGreen != null
                    ? _grassPreset.moistureToGreen.Evaluate(0.5f)
                    : 0.5f;
                Shader.SetGlobalFloat(GrassColorEnableId, 1f);
                Shader.SetGlobalFloat(GrassZoneCountId, 0f);
                Shader.SetGlobalFloat(GrassDefaultMoistureId, neutralMoisture);
                Shader.SetGlobalColor(GrassMoistDryId, _grassPreset.moistureDryTint);
                Shader.SetGlobalColor(GrassMoistWetId, _grassPreset.moistureWetTint);
                Shader.SetGlobalColor(GrassSeasonTintId, seasonTint);
                Shader.SetGlobalFloat(GrassDroughtAmountId, 0f);
                Shader.SetGlobalFloat(GrassDecayId, 0f);
                RenderCameraToTexture();
            }
            finally
            {
                Shader.SetGlobalFloat(GrassColorEnableId, oldColorEnable);
                Shader.SetGlobalFloat(GrassZoneCountId, oldZoneCount);
                Shader.SetGlobalFloat(GrassDefaultMoistureId, oldDefaultMoisture);
                Shader.SetGlobalColor(GrassMoistDryId, oldMoistDry);
                Shader.SetGlobalColor(GrassMoistWetId, oldMoistWet);
                Shader.SetGlobalColor(GrassSeasonTintId, oldSeasonTint);
                Shader.SetGlobalFloat(GrassDroughtAmountId, oldDroughtAmount);
                Shader.SetGlobalFloat(GrassDecayId, oldDecay);
            }
        }

        private void RenderCameraToTexture()
        {
            if (GraphicsSettings.currentRenderPipeline != null)
            {
                var request = new RenderPipeline.StandardRequest
                {
                    destination = _previewTexture,
                    mipLevel = 0,
                    slice = 0,
                    face = CubemapFace.Unknown
                };
                if (!RenderPipeline.SupportsRenderRequest(_previewCamera, request))
                    throw new System.NotSupportedException("当前 Scriptable Render Pipeline 不支持相机 RenderRequest。");
                RenderPipeline.SubmitRenderRequest(_previewCamera, request);
                return;
            }

            RenderTexture oldTarget = _previewCamera.targetTexture;
            try
            {
                _previewCamera.targetTexture = _previewTexture;
                _previewCamera.Render();
            }
            finally
            {
                _previewCamera.targetTexture = oldTarget;
            }
        }

        private void CleanupTemporaryObjects()
        {
            DestroyFluff();
            DestroyPreviewCamera();
            DestroyPreviewTexture();
            _ready = false;
        }

        private void DestroyFluff()
        {
            if (_fluff != null)
                _fluff.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            _fluff = null;
            if (_fluffObject != null)
                Object.DestroyImmediate(_fluffObject);
            _fluffObject = null;
            if (_fluffMaterial != null)
                Object.DestroyImmediate(_fluffMaterial);
            _fluffMaterial = null;
        }

        private void DestroyPreviewCamera()
        {
            if (_previewCamera != null) _previewCamera.targetTexture = null;
            _previewCamera = null;
            _sourceCamera = null;
            if (_previewCameraObject != null)
                Object.DestroyImmediate(_previewCameraObject);
            _previewCameraObject = null;
        }

        private void DestroyPreviewTexture()
        {
            if (_previewCamera != null && _previewCamera.targetTexture == _previewTexture)
                _previewCamera.targetTexture = null;
            if (_previewTexture != null)
            {
                if (_previewTexture.IsCreated()) _previewTexture.Release();
                Object.DestroyImmediate(_previewTexture);
            }
            _previewTexture = null;
        }
    }

    static void Capture(Camera cam, string path, int w, int h)
    {
        Directory.CreateDirectory(OutDir);

        var rt = new RenderTexture(w, h, 24, RenderTextureFormat.ARGB32);
        var prevTarget = cam.targetTexture;
        var prevActive = RenderTexture.active;

        try
        {
            cam.targetTexture = rt;
            cam.Render();

            RenderTexture.active = rt;
            var tex = new Texture2D(w, h, TextureFormat.RGB24, false);
            tex.ReadPixels(new Rect(0, 0, w, h), 0, 0);
            tex.Apply();

            File.WriteAllBytes(path, tex.EncodeToPNG());
            Object.DestroyImmediate(tex);
            Debug.Log($"[ClaudeViewCapture] Saved {path}");
        }
        finally
        {
            cam.targetTexture = prevTarget;
            RenderTexture.active = prevActive;
            rt.Release();
            Object.DestroyImmediate(rt);
            AssetDatabase.Refresh();
        }
    }
}
