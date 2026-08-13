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

        // ⑤ "推进一天"按钮（2026-08-13 拍板：进发布包，给朋友测试用——
        //    与 GlimmerDiary/Debug/Fast Forward 1 Day 同口径）
        CreateTickButton();
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
        hint.text = "J 写日记 · 双击走近 · L 读信";
        var rt = (RectTransform)textGo.transform;
        rt.anchorMin = new Vector2(0f, 1f);
        rt.anchorMax = new Vector2(0f, 1f);
        rt.pivot = new Vector2(0f, 1f);
        rt.anchoredPosition = new Vector2(18f, -12f);
        rt.sizeDelta = new Vector2(480f, 26f);
    }

    // "推进一天"按钮：右下角，可反复按。非 catch-up 逐日模拟（世界志保留，
    // 演示要看信逐封抵达）+ 落盘锚定——与 DebugFastForward.FF(1) 同口径。
    // 2026-08-13 拍板进发布包：给朋友测试用（单向快进，非时间旅行/重置）。
    // GraphicRaycaster 必需：① uGUI Button 没它不接收点击；② TraceInput 闸门①
    // 靠它认出"点在按钮上"，不会顺手把相机也推出去。
    private static void CreateTickButton()
    {
        var canvasGo = new GameObject("DebugTickCanvas");
        var canvas = canvasGo.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 99;   // 信/日记面板之下
        var scaler = canvasGo.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920, 1080);
        canvasGo.AddComponent<GraphicRaycaster>();

        var btnGo = new GameObject("TickButton");
        btnGo.transform.SetParent(canvasGo.transform, false);
        var img = btnGo.AddComponent<Image>();
        img.color = new Color(0.08f, 0.07f, 0.06f, 0.7f);
        var btn = btnGo.AddComponent<Button>();
        btn.targetGraphic = img;
        btn.onClick.AddListener(TickOneDay);
        var brt = (RectTransform)btnGo.transform;
        brt.anchorMin = new Vector2(1f, 0f);
        brt.anchorMax = new Vector2(1f, 0f);
        brt.pivot = new Vector2(1f, 0f);
        brt.anchoredPosition = new Vector2(-16f, 16f);
        brt.sizeDelta = new Vector2(132f, 34f);

        var textGo = new GameObject("Label");
        textGo.transform.SetParent(btnGo.transform, false);
        var label = textGo.AddComponent<Text>();
        try { label.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf"); }
        catch { label.font = Resources.GetBuiltinResource<Font>("Arial.ttf"); }
        label.fontSize = 15;
        label.color = new Color(0.90f, 0.86f, 0.76f, 0.85f);
        label.alignment = TextAnchor.MiddleCenter;
        label.text = "推进一天 ▶";
        var lrt = (RectTransform)textGo.transform;
        lrt.anchorMin = Vector2.zero;
        lrt.anchorMax = Vector2.one;
        lrt.offsetMin = Vector2.zero;
        lrt.offsetMax = Vector2.zero;
    }

    private static void TickOneDay()
    {
        var world = WorldManager.Instance;
        if (world == null || world.WorldSave == null) return;
        world.WorldTick(1, isCatchUp: false, writeAbsenceLetter: false);
        GlimmerDiary.Utils.SaveSystem.SaveWorldState(world.WorldSave);   // 落盘锚定 now
        Debug.Log($"[DebugTick] +1 天 → {world.WorldSave.gameTime.ToDisplayString()}");
    }
}
