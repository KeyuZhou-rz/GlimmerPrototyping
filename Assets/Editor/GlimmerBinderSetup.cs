using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using GlimmerDiary.Ecosystem_nonL;

/// <summary>
/// 幂等接线:确保当前场景有 WorldAtmosphereBinder 根物体,并把场景里现有的
/// 视觉组件(天气/天光/植被/水面)填进它的空引用——已填的引用不动,重复执行无副作用。
/// 背景:AmbientAtmosphereBinding.md 记录 2026-07-02 接线落地,但后续场景重做后
/// binder 物体已不在任何已保存场景中(GUID 全局 grep 为零),故用 Setup 菜单补回。
/// </summary>
public static class GlimmerBinderSetup
{
    [MenuItem("Tools/Glimmer/Setup Atmosphere Binder")]
    public static void Setup()
    {
        var binder = Object.FindFirstObjectByType<WorldAtmosphereBinder>(FindObjectsInactive.Include);
        if (binder == null)
        {
            var go = new GameObject("WorldAtmosphereBinder");
            Undo.RegisterCreatedObjectUndo(go, "Create WorldAtmosphereBinder");
            binder = go.AddComponent<WorldAtmosphereBinder>();
        }

        // 只补空引用;找不到的组件保持 null,binder 运行时会跳过对应通路
        if (binder.weatherController == null)
            binder.weatherController = Object.FindFirstObjectByType<EmotionWeatherController>(FindObjectsInactive.Include);
        if (binder.lightManager == null)
            binder.lightManager = Object.FindFirstObjectByType<LightManager>(FindObjectsInactive.Include);
        if (binder.treePlacement == null)
            binder.treePlacement = Object.FindFirstObjectByType<EcosystemManager>(FindObjectsInactive.Include);
        if (binder.waterSurface == null)
            binder.waterSurface = Object.FindFirstObjectByType<WaterGenerator>(FindObjectsInactive.Include);
        // Batch 4 草色通路：映射表资产（GlimmerVisualSetup.SetupGrassPreset 建/管）
        if (binder.grassPreset == null)
            binder.grassPreset = AssetDatabase.LoadAssetAtPath<GlimmerDiary.Flora.GrassPreset>(
                GlimmerVisualSetup.GrassPresetPath);

        EditorUtility.SetDirty(binder);
        EditorSceneManager.MarkSceneDirty(binder.gameObject.scene);
        EditorSceneManager.SaveOpenScenes();

        Debug.Log("[GlimmerBinderSetup] 接线完成 " +
                  $"weather={(binder.weatherController != null ? "√" : "×")} " +
                  $"light={(binder.lightManager != null ? "√" : "×")} " +
                  $"eco={(binder.treePlacement != null ? "√" : "×")} " +
                  $"water={(binder.waterSurface != null ? "√" : "×")} " +
                  $"grass={(binder.grassPreset != null ? "√" : "×")}");
    }

    // —— 水位映射边界预览(编辑模式,只动 transform,不保存场景即不留痕)——
    // ContextMenu 无法被 MCP 调用,故包一层编辑器菜单用于自动化验证。

    [MenuItem("Tools/Glimmer/Water Preview Dry")]
    public static void WaterDry() => Preview(0f);

    [MenuItem("Tools/Glimmer/Water Preview Flood")]
    public static void WaterFlood() => Preview(1f);

    [MenuItem("Tools/Glimmer/Water Preview Reset")]
    public static void WaterReset()
    {
        var wg = Object.FindFirstObjectByType<WaterGenerator>(FindObjectsInactive.Include);
        if (wg == null) { Debug.LogWarning("[GlimmerBinderSetup] 场景无 WaterGenerator"); return; }
        var lp = wg.transform.localPosition;
        wg.transform.localPosition = new Vector3(lp.x, 0f, lp.z);   // 场景序列化值即 y=0
        wg.displayLevel01 = 0.5f;
        Debug.Log("[GlimmerBinderSetup] Water preview reset → localY=0");
    }

    static void Preview(float level)
    {
        var wg = Object.FindFirstObjectByType<WaterGenerator>(FindObjectsInactive.Include);
        if (wg == null) { Debug.LogWarning("[GlimmerBinderSetup] 场景无 WaterGenerator"); return; }
        wg.SetDisplayLevel01(level);
        Debug.Log($"[GlimmerBinderSetup] Water preview level={level:F2} → localY={wg.transform.localPosition.y:F3}");
    }
}
