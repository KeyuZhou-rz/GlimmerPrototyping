using System.Text;
using UnityEngine;
using UnityEngine.UI;
using GlimmerDiary.Data;
using GlimmerDiary.Utils;

/// <summary>
/// 信（占位实现）：pendingChronicles 的第一个展示消费者（Worksheet §6 切片 7 / §4 A5）。
/// Mountain 式：无红点、无通知、无压力——玩家按 L 主动打开，一次最多读 maxEntriesPerOpen 条。
/// 读完的条目 hasBeenShown 翻 true 并迁入 shownChronicles（本组件是这两个字段的唯一写者）。
/// 纪律：只消费世界志，不碰任何模拟字段；"已读回执"是展示层簿记，不是世界状态写入。
/// 形态是占位皮：运行时自建最小 UI，设计稿落地后替换 EnsureUI 或接入序列化引用即可。
/// </summary>
public class ChronicleLetter : MonoBehaviour
{
    [Header("每次打开最多显示的条数（占位默认 3）")]
    public int maxEntriesPerOpen = 3;

    [Header("可选：设计稿落地后接正式 UI；留空则运行时自建占位面板")]
    public CanvasGroup canvasGroup;
    public Text bodyText;

    [Header("占位样式")]
    public Color panelColor = new(0.08f, 0.07f, 0.06f, 0.85f);
    public Color textColor  = new(0.90f, 0.86f, 0.76f, 1f);

    private bool _open;

    // 点击穿透守卫（TraceInput 读）：信开着时场景点击不触发推近
    public static bool IsOpen { get; private set; }

    void Update()
    {
        // 项目启用新 Input System（Player Settings），旧 UnityEngine.Input 会抛异常
        var kb = UnityEngine.InputSystem.Keyboard.current;
        if (kb != null && kb.lKey.wasPressedThisFrame) Toggle();
    }

    public void Toggle() { if (_open) Close(); else Open(); }

    public void Open()
    {
        var wm = WorldManager.Instance;
        var save = wm != null ? wm.WorldSave : null;
        if (save == null) return;
        _open = true;
        IsOpen = true;
        EnsureUI();

        int n = Mathf.Min(maxEntriesPerOpen, save.pendingChronicles.Count);
        string body;
        if (n == 0)
        {
            body = "这几天世界很安静。";
        }
        else
        {
            var sb = new StringBuilder();
            for (int i = 0; i < n; i++)
            {
                var e = save.pendingChronicles[i];
                e.hasBeenShown = true;              // 已读回执：本组件是唯一写者
                save.shownChronicles.Add(e);
                // 记忆双读（V1 D6）：信里点名过的痕迹，读信即"见证"——
                // witnessed 的写入经 WorldManager 静态入口（本组件不直接碰模拟记录）
                WorldManager.MarkWitnessed(save, e.witnessKeys);
                if (i > 0) sb.Append('\n');
                sb.AppendLine(e.text);
            }
            save.pendingChronicles.RemoveRange(0, n);
            SaveSystem.SaveWorldState(save);        // 回执持久化（顺带重新锚定墙钟，与常规存档一致）
            body = sb.ToString();
        }

        bodyText.text = body;
        canvasGroup.alpha = 1f;
        canvasGroup.interactable = true;
        canvasGroup.blocksRaycasts = true;
    }

    public void Close()
    {
        _open = false;
        IsOpen = false;
        if (canvasGroup == null) return;
        canvasGroup.alpha = 0f;
        canvasGroup.interactable = false;
        canvasGroup.blocksRaycasts = false;
    }

    // ── 占位 UI：运行时自建，零场景接线 ─────────────────────────
    private void EnsureUI()
    {
        if (canvasGroup != null && bodyText != null) return;

        var canvasGo = new GameObject("ChronicleLetterCanvas");
        canvasGo.transform.SetParent(transform, false);
        var canvas = canvasGo.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 100;
        var scaler = canvasGo.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920, 1080);
        canvasGroup = canvasGo.AddComponent<CanvasGroup>();
        canvasGroup.alpha = 0f;
        canvasGroup.interactable = false;
        canvasGroup.blocksRaycasts = false;

        var panelGo = new GameObject("Panel");
        panelGo.transform.SetParent(canvasGo.transform, false);
        var img = panelGo.AddComponent<Image>();
        img.color = panelColor;
        var rt = (RectTransform)panelGo.transform;
        rt.anchorMin = new Vector2(0.5f, 0f);
        rt.anchorMax = new Vector2(0.5f, 0f);
        rt.pivot = new Vector2(0.5f, 0f);
        rt.anchoredPosition = new Vector2(0f, 60f);
        rt.sizeDelta = new Vector2(760f, 320f);

        var textGo = new GameObject("Body");
        textGo.transform.SetParent(panelGo.transform, false);
        bodyText = textGo.AddComponent<Text>();
        bodyText.font = LoadBuiltinFont();
        bodyText.fontSize = 22;
        bodyText.color = textColor;
        bodyText.alignment = TextAnchor.UpperLeft;
        bodyText.horizontalOverflow = HorizontalWrapMode.Wrap;
        bodyText.verticalOverflow = VerticalWrapMode.Overflow;
        var trt = (RectTransform)textGo.transform;
        trt.anchorMin = Vector2.zero;
        trt.anchorMax = Vector2.one;
        trt.offsetMin = new Vector2(28f, 24f);
        trt.offsetMax = new Vector2(-28f, -40f);

        var hintGo = new GameObject("Hint");
        hintGo.transform.SetParent(panelGo.transform, false);
        var hint = hintGo.AddComponent<Text>();
        hint.font = bodyText.font;
        hint.fontSize = 14;
        hint.color = new Color(textColor.r, textColor.g, textColor.b, 0.5f);
        hint.alignment = TextAnchor.LowerRight;
        hint.text = "L 合上";
        var hrt = (RectTransform)hintGo.transform;
        hrt.anchorMin = new Vector2(1f, 0f);
        hrt.anchorMax = new Vector2(1f, 0f);
        hrt.pivot = new Vector2(1f, 0f);
        hrt.anchoredPosition = new Vector2(-16f, 12f);
        hrt.sizeDelta = new Vector2(120f, 24f);
    }

    // 内置动态字体：经 OS 回退可渲染中文（TMP 默认字体无 CJK 字形，占位不用 TMP）
    private static Font LoadBuiltinFont()
    {
        try { return Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf"); }
        catch { return Resources.GetBuiltinResource<Font>("Arial.ttf"); }
    }
}
