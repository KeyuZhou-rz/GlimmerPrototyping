using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

// 残阳验证桥（2026-07-18）：日出/日落两态各拍一轮，从主相机实机位取景。
// 编辑态静态预览（沿用 ClaudeSkyVerify 模式）：把控制器在金色时刻会算出的值
// 直接写上 SkyGradient.mat，拍完恢复 GlimmerVisualSetup.PreviewGoldenHour 基线
// （运行时每帧被控制器覆写，mat 静态值仅供编辑态预览，不留痕）。
// rig 物理：太阳绕 yaw=170° 轴 —— 日出太阳在北（主相机画内），
// 日落太阳在南（镜头背后），北天只剩反日维纳斯带。
public static class ClaudeAfterglowVerify
{
    const string OutDir = "Assets/Screenshots";
    const string SkyMatPath = "Assets/Materials/Glimmer/SkyGradient.mat";

    [MenuItem("Tools/Claude/Afterglow Capture (sunrise)")]
    public static void Sunrise() => Run(sunset: false);

    [MenuItem("Tools/Claude/Afterglow Capture (sunset)")]
    public static void Sunset() => Run(sunset: true);

    // 正午白天相（2026-07-18 新草原色板：深碧天顶/澄蓝中天/暖尘霾）
    [MenuItem("Tools/Claude/Day Capture (noon)")]
    public static void Noon()
    {
        UnityEditor.SceneManagement.EditorSceneManager.OpenScene("Assets/Scenes/PlantGrowthTemplate.unity");
        var mat = AssetDatabase.LoadAssetAtPath<Material>(SkyMatPath);
        if (mat == null) { Debug.LogError("[ClaudeAfterglowVerify] SkyGradient.mat not found"); return; }
        var sun = RenderSettings.sun;
        if (sun == null)
        {
            var go = GameObject.Find("Directional Light");
            if (go != null) sun = go.GetComponent<Light>();
        }
        if (sun == null) { Debug.LogWarning("[ClaudeAfterglowVerify] No sun light found"); return; }

        sun.transform.rotation = Quaternion.Euler(60f, 215f, 0f);
        sun.color = new Color(1.0f, 0.95f, 0.85f);   // 与 SunlightPreset 正午键同值
        sun.intensity = 1.15f;
        RenderSettings.ambientMode = AmbientMode.Trilight;
        RenderSettings.ambientSkyColor     = new Color(0.55f, 0.62f, 0.72f);
        RenderSettings.ambientEquatorColor = new Color(0.52f, 0.48f, 0.40f);
        RenderSettings.ambientGroundColor  = new Color(0.28f, 0.23f, 0.17f);
        RenderSettings.fog = true;
        RenderSettings.fogMode = FogMode.Linear;
        Color dayFog = new Color(0.70f, 0.66f, 0.56f);   // 控制器 sunnyFogColor 同值
        RenderSettings.fogColor = dayFog;
        RenderSettings.fogStartDistance = 60f;
        RenderSettings.fogEndDistance = 300f;

        mat.SetColor("_SkyZenith", new Color(0.22f, 0.44f, 0.70f));
        mat.SetColor("_SkyMid",    new Color(0.45f, 0.64f, 0.78f));
        mat.SetColor("_HorizonGlowCol", new Color(0.86f, 0.78f, 0.60f));
        mat.SetColor("_SkyHorizon", dayFog);
        mat.SetColor("_GroundCol", dayFog);
        mat.SetColor("_SunWashCol", new Color(0.90f, 0.82f, 0.68f));
        mat.SetFloat("_SunWashAmt", 0.15f);
        mat.SetFloat("_HaloAmt", 0f);
        mat.SetFloat("_AntiGlowAmt", 0f);
        mat.SetColor("_SunTint", new Color(1.0f, 0.72f, 0.42f));
        mat.SetVector("_SunDir", -sun.transform.forward);
        mat.SetFloat("_SunGlow", 0.9f);
        mat.SetFloat("_SunDiscStrength", 1f);
        mat.SetFloat("_BandingAmount", 0.55f);
        mat.SetFloat("_StrokeAmount", 0.05f);
        mat.SetFloat("_StarBlend", 0f);

        CaptureFromMainCamera(Path.Combine(OutDir, "sky_noon_maincam.png"));
        CaptureView(Path.Combine(OutDir, "sky_noon_up.png"), Quaternion.Euler(-35f, 0f, 0f));

        GlimmerVisualSetup.PreviewGoldenHour();
        AssetDatabase.SaveAssets();
        Debug.Log("[ClaudeAfterglowVerify] noon captured");
    }

    static void Run(bool sunset)
    {
        UnityEditor.SceneManagement.EditorSceneManager.OpenScene("Assets/Scenes/PlantGrowthTemplate.unity");
        var mat = AssetDatabase.LoadAssetAtPath<Material>(SkyMatPath);
        if (mat == null) { Debug.LogError("[ClaudeAfterglowVerify] SkyGradient.mat not found"); return; }

        var sun = RenderSettings.sun;
        if (sun == null)
        {
            var go = GameObject.Find("Directional Light");
            if (go != null) sun = go.GetComponent<Light>();
        }
        if (sun == null) { Debug.LogWarning("[ClaudeAfterglowVerify] No sun light found"); return; }

        // 日出：太阳仰角 4°、方位北（画内）；日落：仰角 -1°（刚沉）、方位南（背后）
        sun.transform.rotation = sunset ? Quaternion.Euler(179f, 170f, 0f)
                                        : Quaternion.Euler(4f, 170f, 0f);
        sun.color = sunset ? new Color(1.0f, 0.55f, 0.35f) : new Color(1.0f, 0.72f, 0.50f);
        sun.intensity = 0.95f;

        RenderSettings.ambientMode = AmbientMode.Trilight;
        RenderSettings.ambientSkyColor     = new Color(0.45f, 0.40f, 0.55f);
        RenderSettings.ambientEquatorColor = new Color(0.55f, 0.40f, 0.35f);
        RenderSettings.ambientGroundColor  = new Color(0.25f, 0.18f, 0.14f);
        RenderSettings.fog = true;
        RenderSettings.fogMode = FogMode.Linear;
        Color duskFog = new Color(0.58f, 0.44f, 0.40f);
        RenderSettings.fogColor = duskFog;
        RenderSettings.fogStartDistance = 40f;
        RenderSettings.fogEndDistance = 250f;

        // 控制器金色时刻公式落值（wGold=1, elevMask=1, badT=0）
        mat.SetColor("_SkyZenith", new Color(0.40f, 0.33f, 0.50f));
        mat.SetColor("_SkyMid",    new Color(0.82f, 0.44f, 0.50f));
        mat.SetColor("_HorizonGlowCol", new Color(1.00f, 0.52f, 0.30f));
        mat.SetColor("_SkyHorizon", duskFog);
        mat.SetColor("_GroundCol", duskFog);
        mat.SetColor("_SunWashCol", new Color(1.0f, 0.60f, 0.38f));
        mat.SetFloat("_SunWashAmt", 0.45f);
        mat.SetColor("_HaloCol", new Color(1.0f, 0.48f, 0.28f));
        mat.SetFloat("_HaloAmt", 0.90f);
        mat.SetColor("_AntiGlowCol", new Color(0.80f, 0.42f, 0.52f));
        mat.SetFloat("_AntiGlowAmt", 0.65f);
        mat.SetColor("_SunTint", new Color(1.0f, 0.88f, 0.70f));
        mat.SetVector("_SunDir", -sun.transform.forward);
        mat.SetFloat("_SunGlow", 0.9f);
        mat.SetFloat("_SunDiscStrength", 1f);
        mat.SetFloat("_BandingAmount", 0.55f);
        mat.SetFloat("_StrokeAmount", 0.10f);
        mat.SetFloat("_StarBlend", 0f);

        string tag = sunset ? "sunset" : "sunrise";
        CaptureFromMainCamera(Path.Combine(OutDir, $"sky_{tag}_maincam.png"));
        // 仰望北天（日出的日侧宽带 / 日落的维纳斯带都在北）
        CaptureView(Path.Combine(OutDir, $"sky_{tag}_up.png"), Quaternion.Euler(-18f, 0f, 0f));
        // 日落加拍一张朝南（镜头背后的太阳本尊，验证晕染宽带没有漏到反日侧）
        if (sunset)
            CaptureView(Path.Combine(OutDir, "sky_sunset_sunside.png"), Quaternion.Euler(-10f, 180f, 0f));

        // 恢复基线，别把黄昏态留在盘上
        GlimmerVisualSetup.PreviewGoldenHour();
        AssetDatabase.SaveAssets();
        Debug.Log($"[ClaudeAfterglowVerify] {tag} captured");
    }

    // 主相机实机位（玩家真实取景）
    static void CaptureFromMainCamera(string path)
    {
        var mainGo = GameObject.Find("Main Camera");
        if (mainGo == null) { Debug.LogWarning("[ClaudeAfterglowVerify] Main Camera not found"); return; }
        var t = mainGo.transform;
        CaptureAt(path, t.position, t.rotation, 60f);
    }

    // 站在地形中央看向指定方向（负 pitch = 仰望天空）
    static void CaptureView(string path, Quaternion rot)
    {
        var terrain = Object.FindFirstObjectByType<TerrainGenerator>();
        Vector3 center = terrain != null ? terrain.transform.position : Vector3.zero;
        CaptureAt(path, center + new Vector3(0f, 12f, 0f), rot, 50f);
    }

    static void CaptureAt(string path, Vector3 eye, Quaternion rot, float fov)
    {
        var go = new GameObject("~ClaudeTempCam") { hideFlags = HideFlags.HideAndDontSave };
        try
        {
            var cam = go.AddComponent<Camera>();
            cam.transform.SetPositionAndRotation(eye, rot);
            cam.clearFlags = CameraClearFlags.Skybox;
            cam.fieldOfView = fov;
            cam.nearClipPlane = 0.3f;
            cam.farClipPlane = 2000f;

            Directory.CreateDirectory(OutDir);
            var rt = new RenderTexture(1280, 720, 24, RenderTextureFormat.ARGB32);
            var prevActive = RenderTexture.active;
            try
            {
                cam.targetTexture = rt;
                cam.Render();
                RenderTexture.active = rt;
                var tex = new Texture2D(1280, 720, TextureFormat.RGB24, false);
                tex.ReadPixels(new Rect(0, 0, 1280, 720), 0, 0);
                tex.Apply();
                File.WriteAllBytes(path, tex.EncodeToPNG());
                Object.DestroyImmediate(tex);
                Debug.Log($"[ClaudeAfterglowVerify] Saved {path}");
            }
            finally
            {
                cam.targetTexture = null;
                RenderTexture.active = prevActive;
                rt.Release();
                Object.DestroyImmediate(rt);
            }
        }
        finally
        {
            Object.DestroyImmediate(go);
        }
    }
}
