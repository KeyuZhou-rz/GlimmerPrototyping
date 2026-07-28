using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 痕迹点击字幕（Layer 3 纯展示）：点痕迹推近时，屏幕下缘浮出一行观察语。
/// 纪律：语料来自 TraceCaptionBank（静态文本），不读世界状态、不进 pendingChronicles
/// ——点击是"此刻的观察"，世界志是世界状态的产物，两者不混。
/// 占位皮：运行时自建最小 UI（同 ChronicleLetter.EnsureUI 手法），设计稿落地后换皮。
/// </summary>
public class TraceCaptionUI : MonoBehaviour
{
    [Header("节奏（秒）")]
    public float fadeIn  = 0.5f;
    public float hold    = 3.2f;
    public float fadeOut = 0.9f;

    [Header("占位样式")]
    public Color textColor = new(0.92f, 0.88f, 0.78f, 1f);
    public Color shadowColor = new(0f, 0f, 0f, 0.55f);

    private static TraceCaptionUI _instance;

    private CanvasGroup _group;
    private Text _text;
    private float _timer = -1f;   // <0 = 闲置

    /// <summary>点痕迹时调用：显示一句该痕迹的观察语。</summary>
    public static void Show(string traceType, string traceKey)
    {
        if (_instance == null)
        {
            var go = new GameObject("TraceCaptionUI");
            _instance = go.AddComponent<TraceCaptionUI>();
        }
        _instance.ShowInternal(TraceCaptionBank.Pick(traceType, traceKey));
    }

    private void ShowInternal(string caption)
    {
        EnsureUI();
        _text.text = caption;
        _timer = 0f;
    }

    void Update()
    {
        if (_timer < 0f || _group == null) return;
        _timer += Time.unscaledDeltaTime;
        float total = fadeIn + hold + fadeOut;
        if (_timer >= total)
        {
            _timer = -1f;
            _group.alpha = 0f;
            return;
        }
        if (_timer < fadeIn) _group.alpha = _timer / fadeIn;
        else if (_timer < fadeIn + hold) _group.alpha = 1f;
        else _group.alpha = 1f - (_timer - fadeIn - hold) / fadeOut;
    }

    // ── 占位 UI：屏幕下缘单行，不挡射线（点字幕不会误触痕迹点击） ──
    private void EnsureUI()
    {
        if (_group != null && _text != null) return;

        var canvasGo = new GameObject("TraceCaptionCanvas");
        canvasGo.transform.SetParent(transform, false);
        var canvas = canvasGo.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 90;   // 信（100）之下，各管各的
        var scaler = canvasGo.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920, 1080);
        _group = canvasGo.AddComponent<CanvasGroup>();
        _group.alpha = 0f;
        _group.interactable = false;
        _group.blocksRaycasts = false;

        var textGo = new GameObject("Caption");
        textGo.transform.SetParent(canvasGo.transform, false);
        _text = textGo.AddComponent<Text>();
        _text.font = LoadBuiltinFont();
        _text.fontSize = 24;
        _text.fontStyle = FontStyle.Italic;
        _text.color = textColor;
        _text.alignment = TextAnchor.MiddleCenter;
        _text.horizontalOverflow = HorizontalWrapMode.Wrap;
        var shadow = textGo.AddComponent<Shadow>();
        shadow.effectColor = shadowColor;
        shadow.effectDistance = new Vector2(1.5f, -1.5f);
        var rt = (RectTransform)textGo.transform;
        rt.anchorMin = new Vector2(0.5f, 0f);
        rt.anchorMax = new Vector2(0.5f, 0f);
        rt.pivot = new Vector2(0.5f, 0f);
        rt.anchoredPosition = new Vector2(0f, 44f);
        rt.sizeDelta = new Vector2(1400f, 60f);
    }

    // 内置动态字体：经 OS 回退可渲染中文（TMP 默认字体无 CJK 字形，占位不用 TMP）
    private static Font LoadBuiltinFont()
    {
        try { return Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf"); }
        catch { return Resources.GetBuiltinResource<Font>("Arial.ttf"); }
    }
}
