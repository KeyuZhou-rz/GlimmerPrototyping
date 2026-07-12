using System.IO;
using UnityEditor;
using UnityEngine;

// 批处理模式验证桥（Claude 无头验证用）：
//   Setup Visual Style → 金色时刻/夜晚/暴雨 三态各拍一张 overview。
// 夜/暴雨态只改 SkyGradient.mat 运行时属性做静态预览，拍完恢复金色时刻基线。
public static class ClaudeSkyVerify
{
    const string OutDir = "Assets/Screenshots";
    const string SkyMatPath = "Assets/Materials/Glimmer/SkyGradient.mat";

    [MenuItem("Tools/Claude/Sky Verify (3 states)")]
    public static void Run()
    {
        UnityEditor.SceneManagement.EditorSceneManager.OpenScene("Assets/Scenes/PlantGrowthTemplate.unity");
        GlimmerVisualSetup.Run();

        // —— 金色时刻（编辑态基线）——
        GlimmerVisualSetup.PreviewGoldenHour();
        CaptureOverview(Path.Combine(OutDir, "sky_golden.png"));
        // 朝日盘方向：PreviewGoldenHour 太阳 Euler(38,215) → 日盘在方位 35°、仰角 38°
        CaptureView(Path.Combine(OutDir, "sky_golden_sun.png"), Quaternion.Euler(-28f, 35f, 0f));
        // 背日侧：验证日侧暖洗 vs 背日冷沉的方位不对称（TLD 特征）
        CaptureView(Path.Combine(OutDir, "sky_golden_antisun.png"), Quaternion.Euler(-12f, 215f, 0f));

        // —— 正午（白天相：尘蓝苍白色板 + 色带）——
        var sunNoon = RenderSettings.sun;
        if (sunNoon != null)
        {
            sunNoon.transform.rotation = Quaternion.Euler(75f, 215f, 0f);
            sunNoon.color = new Color(1.0f, 0.96f, 0.90f);
            sunNoon.intensity = 1.15f;
        }
        PreviewDaySky();
        CaptureOverview(Path.Combine(OutDir, "sky_noon.png"));
        CaptureView(Path.Combine(OutDir, "sky_noon_up.png"), Quaternion.Euler(-65f, 35f, 0f));

        var mat = AssetDatabase.LoadAssetAtPath<Material>(SkyMatPath);
        var sun = RenderSettings.sun;

        // —— 夜晚：太阳沉底、天色乘 nightFloor、星穹全开 ——
        if (mat != null)
        {
            if (sun != null)
            {
                sun.transform.rotation = Quaternion.Euler(-30f, 215f, 0f);
                sun.color = new Color(0.70f, 0.74f, 0.85f);
                sun.intensity = 0.08f;
            }
            RenderSettings.ambientSkyColor = new Color(0.10f, 0.12f, 0.16f);
            RenderSettings.ambientEquatorColor = new Color(0.07f, 0.08f, 0.11f);
            RenderSettings.ambientGroundColor = new Color(0.03f, 0.04f, 0.05f);
            // 夜相色板：深海军三停 + 海军灰雾（与控制器 nightFogColor 同值）
            Color nightFog = new Color(0.095f, 0.11f, 0.145f);
            RenderSettings.fogColor = nightFog;
            mat.SetColor("_SkyZenith", new Color(0.09f, 0.12f, 0.19f));
            mat.SetColor("_SkyMid", new Color(0.13f, 0.16f, 0.22f));
            mat.SetColor("_HorizonGlowCol", new Color(0.24f, 0.22f, 0.20f));  // airglow
            mat.SetColor("_SkyHorizon", nightFog);
            mat.SetColor("_GroundCol", nightFog);
            mat.SetFloat("_SunWashAmt", 0f);
            mat.SetFloat("_StrokeAmount", 0.02f);
            mat.SetFloat("_SunDiscStrength", 0f);
            mat.SetFloat("_SunGlow", 0f);
            mat.SetFloat("_StarBlend", 1f);
            CaptureOverview(Path.Combine(OutDir, "sky_night.png"));
            // 仰望星穹：验证撒灰银河带 + 双色星点（overview 平视角看不到天顶）
            CaptureView(Path.Combine(OutDir, "sky_night_up.png"), Quaternion.Euler(-55f, 160f, 0f));
        }

        // —— 暴雨 ——
        GlimmerVisualSetup.PreviewStorm();
        CaptureOverview(Path.Combine(OutDir, "sky_storm.png"));

        // 恢复基线，别把夜/暴雨态留在盘上
        GlimmerVisualSetup.PreviewGoldenHour();
        AssetDatabase.SaveAssets();
        Debug.Log("[ClaudeSkyVerify] done");
    }

    // 白天相预览（正午截图用）：与控制器 day 色板同值
    static void PreviewDaySky()
    {
        var mat = AssetDatabase.LoadAssetAtPath<Material>(SkyMatPath);
        if (mat == null) return;
        Color dayFog = new Color(0.66f, 0.62f, 0.55f);
        RenderSettings.fogColor = dayFog;
        RenderSettings.ambientSkyColor = new Color(0.55f, 0.62f, 0.72f);
        RenderSettings.ambientEquatorColor = new Color(0.48f, 0.46f, 0.40f);
        RenderSettings.ambientGroundColor = new Color(0.26f, 0.22f, 0.18f);
        mat.SetColor("_SkyZenith", new Color(0.36f, 0.46f, 0.56f));
        mat.SetColor("_SkyMid", new Color(0.56f, 0.60f, 0.62f));
        mat.SetColor("_HorizonGlowCol", new Color(0.78f, 0.74f, 0.66f));
        mat.SetColor("_SkyHorizon", dayFog);
        mat.SetColor("_GroundCol", dayFog);
        mat.SetColor("_SunWashCol", new Color(0.90f, 0.82f, 0.68f));
        mat.SetFloat("_SunWashAmt", 0.15f);
        mat.SetFloat("_BandingAmount", 0.55f);
        mat.SetFloat("_StrokeAmount", 0.05f);
        var sun = RenderSettings.sun;
        if (sun != null) mat.SetVector("_SunDir", -sun.transform.forward);
        mat.SetFloat("_SunGlow", 0.9f);
        mat.SetFloat("_SunDiscStrength", 1f);
        mat.SetFloat("_StarBlend", 0f);
    }

    // 同 ClaudeViewCapture.CaptureOverview 的取景，但路径可指定
    static void CaptureOverview(string path)
    {
        var terrain = Object.FindFirstObjectByType<TerrainGenerator>();
        Vector3 center = terrain != null ? terrain.transform.position : Vector3.zero;
        float ext = terrain != null ? terrain.width * terrain.scale * 0.5f : 80f;

        var rot = Quaternion.Euler(30f, 225f, 0f);
        Vector3 eye = center + new Vector3(0f, 4f, 0f) - rot * Vector3.forward * (ext * 2.1f);
        CaptureAt(path, eye, rot);
    }

    // 站在地形中央看向指定方向（负 pitch = 仰望天空）
    static void CaptureView(string path, Quaternion rot)
    {
        var terrain = Object.FindFirstObjectByType<TerrainGenerator>();
        Vector3 center = terrain != null ? terrain.transform.position : Vector3.zero;
        CaptureAt(path, center + new Vector3(0f, 12f, 0f), rot);
    }

    static void CaptureAt(string path, Vector3 eye, Quaternion rot)
    {
        var go = new GameObject("~ClaudeTempCam") { hideFlags = HideFlags.HideAndDontSave };
        try
        {
            var cam = go.AddComponent<Camera>();
            cam.transform.SetPositionAndRotation(eye, rot);
            cam.clearFlags = CameraClearFlags.Skybox;
            cam.fieldOfView = 50f;
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
                Debug.Log($"[ClaudeSkyVerify] Saved {path}");
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
