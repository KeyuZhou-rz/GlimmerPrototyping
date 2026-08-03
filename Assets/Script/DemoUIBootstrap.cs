using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.UI;

/// <summary>
/// Demo UI 自举（零场景接线）：场景加载后自动就位三样东西——
/// ① DiaryInputUI（日记面板，J 键）；
/// ② EventSystem + InputSystemUIInputModule（新输入系统下 uGUI 交互必需；
///    也让 TraceInput 的 IsPointerOverGameObject 防穿透判定真正生效）；
/// ③ 角落常驻提示行（占位引导，Mountain 式淡小、无压力）。
/// build 与编辑器 play 同样生效；场景 YAML 零改动。
/// </summary>
public static class DemoUIBootstrap
{
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Boot()
    {
        // ① 日记面板
        if (Object.FindFirstObjectByType<DiaryInputUI>() == null)
            new GameObject("DemoUI").AddComponent<DiaryInputUI>();

        // ② EventSystem（占位 UI 此前无交互需求，场景从未配过）
        if (EventSystem.current == null)
        {
            var es = new GameObject("EventSystem");
            es.AddComponent<EventSystem>();
            es.AddComponent<InputSystemUIInputModule>();
        }

        // ④ 触感层第一批（2026-08-03，纯 L3）：点草簌动 / 阵风草浪 / 环境音占位
        var touch = new GameObject("TouchLayer");
        touch.AddComponent<GrassTouchFeedback>();
        touch.AddComponent<GrassGustController>();
        touch.AddComponent<AmbientAudio>();

        // ③ 常驻提示行
        CreateHint();
    }

    private static void CreateHint()
    {
        var canvasGo = new GameObject("DemoHintCanvas");
        var canvas = canvasGo.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 99;   // 信/日记面板之下
        var scaler = canvasGo.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920, 1080);

        var textGo = new GameObject("Hint");
        textGo.transform.SetParent(canvasGo.transform, false);
        var hint = textGo.AddComponent<Text>();
        try { hint.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf"); }
        catch { hint.font = Resources.GetBuiltinResource<Font>("Arial.ttf"); }
        hint.fontSize = 15;
        hint.color = new Color(0.90f, 0.86f, 0.76f, 0.45f);
        hint.alignment = TextAnchor.UpperLeft;
        hint.text = "J 写日记 · 点亮斑细看 · L 读信";
        var rt = (RectTransform)textGo.transform;
        rt.anchorMin = new Vector2(0f, 1f);
        rt.anchorMax = new Vector2(0f, 1f);
        rt.pivot = new Vector2(0f, 1f);
        rt.anchoredPosition = new Vector2(18f, -12f);
        rt.sizeDelta = new Vector2(480f, 26f);
    }
}
