using System;
using UnityEngine;
using UnityEngine.UI;
using GlimmerDiary.Core;
using GlimmerDiary.Data;

/// <summary>
/// 日记输入（占位实现）：玩家侧闭环的最后一环——J 键开面板写日记，
/// 提交即调 WorldManager.OnJournalSubmitted（catch-up → 情绪注入 → 落盘 → 存档；
/// 2026-08-03 A 方案：不再当场响应式模拟，天气回响经 pending 脉冲延迟几分钟落地）。
/// 情绪五维由 SentimentStub 关键词桩产出（L1 真引擎落地后只换分析实现）。
/// Mountain 式克制：无仪式特效，提交后一行"世界收到了。"三秒渐隐。
/// 形态是占位皮：运行时自建最小 UI（同 ChronicleLetter 模式），设计稿落地后替换。
/// </summary>
public class DiaryInputUI : MonoBehaviour
{
    [Header("占位样式")]
    public Color panelColor = new(0.08f, 0.07f, 0.06f, 0.88f);
    public Color textColor  = new(0.90f, 0.86f, 0.76f, 1f);

    public static bool IsOpen { get; private set; }

    [Header("留意句（2026-08-13 设计变更：撤悬浮小球，改日记边缘语料引导）")]
    [Tooltip("打开日记后第一行浮出的延迟（秒）")]
    public float noticeFirstDelay = 1.2f;
    [Tooltip("停笔多少秒后浮出下一行（秒）")]
    public float noticeIdleDelay  = 5f;
    [Tooltip("一行停留多久（秒）；清单为空则始终安静")]
    public float noticeHoldSeconds = 12f;   // 拍板后调长：它是"想起"，让你把这句话读完再读完一遍

    private CanvasGroup _canvasGroup;
    private InputField  _input;
    private Text        _ack;
    private float       _ackTimer;

    // 留意行状态：只给方向不给位置；纯 L3 派生，不写世界状态、不落档
    private Text _notice;
    private readonly System.Collections.Generic.List<WorldTraceBinder.NoticeInfo> _notices = new();
    private int   _noticeCursor;
    private float _noticeIdle;    // 距下一行浮出的剩余秒数
    private float _noticeHold;    // 当前行剩余停留秒数（>0 = 正在展示）
    private float _noticeAlpha;   // 当前透明度（向目标缓动）

    void Update()
    {
        var kb = UnityEngine.InputSystem.Keyboard.current;
        if (kb != null)
        {
            if (!IsOpen && kb.jKey.wasPressedThisFrame) Open();
            else if (IsOpen && kb.escapeKey.wasPressedThisFrame) Close();
            else if (IsOpen && kb.enterKey.wasPressedThisFrame
                     && (kb.leftCtrlKey.isPressed || kb.rightCtrlKey.isPressed)) Submit();
        }

        // "世界收到了。" 三秒渐隐
        if (_ackTimer > 0f)
        {
            _ackTimer -= Time.deltaTime;
            if (_ack != null)
            {
                var c = _ack.color;
                c.a = Mathf.Clamp01(_ackTimer / 3f);
                _ack.color = c;
            }
        }

        // 留意行：展示中缓显、停留；非展示期缓隐并倒数下一行。清单为空则永远安静——
        // 大多数日子没有新事，没有新事就不说话（它不是通知）。
        if (IsOpen && _notice != null && _notices.Count > 0)
        {
            if (_noticeHold > 0f)
            {
                _noticeHold -= Time.deltaTime;
                _noticeAlpha = Mathf.MoveTowards(_noticeAlpha, 1f, Time.deltaTime / 1.2f);
            }
            else
            {
                _noticeAlpha = Mathf.MoveTowards(_noticeAlpha, 0f, Time.deltaTime / 0.8f);
                _noticeIdle -= Time.deltaTime;
                if (_noticeIdle <= 0f) ShowNextNotice();
            }
            var nc = _notice.color;
            nc.a = _noticeAlpha * 0.65f;   // 永远半透明——它是"想起"，不是"提示"
            _notice.color = nc;
        }
    }

    private void ShowNextNotice()
    {
        var n = _notices[_noticeCursor % _notices.Count];
        _noticeCursor++;
        _notice.text = TraceCaptionBank.PickNotice(n.traceType, n.traceKey, n.zoneId);
        _noticeHold = noticeHoldSeconds;
    }

    // 一动笔，浮现的句子就散（写字的人被自己的句子接走）；停笔几秒后世界再说话
    private void OnTyping(string _)
    {
        _noticeHold = 0f;
        _noticeIdle = noticeIdleDelay;
    }

    public void Open()
    {
        EnsureUI();
        IsOpen = true;
        _canvasGroup.alpha = 1f;
        _canvasGroup.interactable = true;
        _canvasGroup.blocksRaycasts = true;
        _input.ActivateInputField();   // 打开即聚焦，直接可写

        // 拉取当下值得留意的变化（新鲜痕迹，方向不给位置）；没有就保持安静
        _notices.Clear();
        var binder = FindFirstObjectByType<WorldTraceBinder>();
        if (binder != null) binder.GetFreshNotices(_notices);
        _noticeCursor = 0;
        _noticeIdle = noticeFirstDelay;
        _noticeHold = 0f;
        _noticeAlpha = 0f;
    }

    public void Close()
    {
        IsOpen = false;
        // 关上日记，留意句即散——错过即错过，不落任何"已读"记录
        _noticeHold = 0f;
        _noticeAlpha = 0f;
        if (_notice != null) { var nc = _notice.color; nc.a = 0f; _notice.color = nc; }
        if (_canvasGroup == null) return;
        _canvasGroup.alpha = 0f;
        _canvasGroup.interactable = false;
        _canvasGroup.blocksRaycasts = false;
    }

    public void Submit()
    {
        var wm = WorldManager.Instance;
        if (wm == null || _input == null) return;
        string text = _input.text.Trim();
        if (text.Length == 0) return;

        wm.OnJournalSubmitted(new JournalEntry
        {
            entryId       = Guid.NewGuid().ToString(),
            realTimestamp = DateTime.Now.ToString("o"),
            rawText       = text,
            emotion       = SentimentStub.Analyze(text)
        });

        _input.text = "";
        Close();
        ShowAck();
    }

    private void ShowAck()
    {
        EnsureUI();
        _ackTimer = 3f;
    }

    // ── 占位 UI：运行时自建，零场景接线 ─────────────────────────
    private void EnsureUI()
    {
        if (_canvasGroup != null) return;

        var canvasGo = new GameObject("DiaryInputCanvas");
        canvasGo.transform.SetParent(transform, false);
        var canvas = canvasGo.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 101;   // 信（100）之上
        var scaler = canvasGo.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920, 1080);
        _canvasGroup = canvasGo.AddComponent<CanvasGroup>();
        _canvasGroup.alpha = 0f;
        _canvasGroup.interactable = false;
        _canvasGroup.blocksRaycasts = false;

        Font font = LoadBuiltinFont();

        // 面板：底部居中，比信略矮
        var panelGo = new GameObject("Panel");
        panelGo.transform.SetParent(canvasGo.transform, false);
        var img = panelGo.AddComponent<Image>();
        img.color = panelColor;
        var rt = (RectTransform)panelGo.transform;
        rt.anchorMin = new Vector2(0.5f, 0f);
        rt.anchorMax = new Vector2(0.5f, 0f);
        rt.pivot = new Vector2(0.5f, 0f);
        rt.anchoredPosition = new Vector2(0f, 60f);
        rt.sizeDelta = new Vector2(760f, 260f);

        // 输入框（多行）
        var inputGo = new GameObject("Input");
        inputGo.transform.SetParent(panelGo.transform, false);
        var inputImg = inputGo.AddComponent<Image>();
        inputImg.color = new Color(1f, 1f, 1f, 0.06f);
        _input = inputGo.AddComponent<InputField>();
        _input.lineType = InputField.LineType.MultiLineNewline;
        var irt = (RectTransform)inputGo.transform;
        irt.anchorMin = Vector2.zero;
        irt.anchorMax = Vector2.one;
        irt.offsetMin = new Vector2(24f, 56f);
        irt.offsetMax = new Vector2(-24f, -24f);

        var textGo = new GameObject("Text");
        textGo.transform.SetParent(inputGo.transform, false);
        var body = textGo.AddComponent<Text>();
        body.font = font;
        body.fontSize = 20;
        body.color = textColor;
        body.alignment = TextAnchor.UpperLeft;
        body.supportRichText = false;
        var trt = (RectTransform)textGo.transform;
        trt.anchorMin = Vector2.zero;
        trt.anchorMax = Vector2.one;
        trt.offsetMin = new Vector2(12f, 8f);
        trt.offsetMax = new Vector2(-12f, -8f);
        _input.textComponent = body;

        var phGo = new GameObject("Placeholder");
        phGo.transform.SetParent(inputGo.transform, false);
        var ph = phGo.AddComponent<Text>();
        ph.font = font;
        ph.fontSize = 20;
        ph.color = new Color(textColor.r, textColor.g, textColor.b, 0.35f);
        ph.alignment = TextAnchor.UpperLeft;
        ph.text = "写点什么，世界在听。";
        var prt = (RectTransform)phGo.transform;
        prt.anchorMin = Vector2.zero;
        prt.anchorMax = Vector2.one;
        prt.offsetMin = new Vector2(12f, 8f);
        prt.offsetMax = new Vector2(-12f, -8f);
        _input.placeholder = ph;
        _input.onValueChanged.AddListener(OnTyping);

        // 留意行：面板顶缘外一行斜体小字——"写下今天之前，你想起白天瞥见的东西"
        var noticeGo = new GameObject("Notice");
        noticeGo.transform.SetParent(panelGo.transform, false);
        _notice = noticeGo.AddComponent<Text>();
        _notice.font = font;
        _notice.fontSize = 15;
        _notice.fontStyle = FontStyle.Italic;
        _notice.color = new Color(textColor.r, textColor.g, textColor.b, 0f);
        _notice.alignment = TextAnchor.MiddleCenter;
        _notice.raycastTarget = false;
        var nrt = (RectTransform)noticeGo.transform;
        nrt.anchorMin = new Vector2(0.5f, 1f);
        nrt.anchorMax = new Vector2(0.5f, 1f);
        nrt.pivot = new Vector2(0.5f, 0f);
        nrt.anchoredPosition = new Vector2(0f, 10f);
        nrt.sizeDelta = new Vector2(720f, 24f);

        // 提交按钮（右下）+ 快捷键提示（左下）
        var btnGo = new GameObject("Submit");
        btnGo.transform.SetParent(panelGo.transform, false);
        var btnImg = btnGo.AddComponent<Image>();
        btnImg.color = new Color(1f, 1f, 1f, 0.10f);
        var btn = btnGo.AddComponent<Button>();
        btn.targetGraphic = btnImg;
        btn.onClick.AddListener(Submit);
        var brt = (RectTransform)btnGo.transform;
        brt.anchorMin = new Vector2(1f, 0f);
        brt.anchorMax = new Vector2(1f, 0f);
        brt.pivot = new Vector2(1f, 0f);
        brt.anchoredPosition = new Vector2(-16f, 12f);
        brt.sizeDelta = new Vector2(96f, 32f);

        var btnTextGo = new GameObject("Label");
        btnTextGo.transform.SetParent(btnGo.transform, false);
        var btnText = btnTextGo.AddComponent<Text>();
        btnText.font = font;
        btnText.fontSize = 16;
        btnText.color = textColor;
        btnText.alignment = TextAnchor.MiddleCenter;
        btnText.text = "提交";
        var btrt = (RectTransform)btnTextGo.transform;
        btrt.anchorMin = Vector2.zero;
        btrt.anchorMax = Vector2.one;
        btrt.offsetMin = Vector2.zero;
        btrt.offsetMax = Vector2.zero;

        var hintGo = new GameObject("Hint");
        hintGo.transform.SetParent(panelGo.transform, false);
        var hint = hintGo.AddComponent<Text>();
        hint.font = font;
        hint.fontSize = 14;
        hint.color = new Color(textColor.r, textColor.g, textColor.b, 0.5f);
        hint.alignment = TextAnchor.LowerLeft;
        hint.text = "Ctrl+Enter 提交 · Esc 合上";
        var hrt = (RectTransform)hintGo.transform;
        hrt.anchorMin = new Vector2(0f, 0f);
        hrt.anchorMax = new Vector2(0f, 0f);
        hrt.pivot = new Vector2(0f, 0f);
        hrt.anchoredPosition = new Vector2(16f, 16f);
        hrt.sizeDelta = new Vector2(320f, 24f);

        // 提交回执：独立小条（面板关闭后仍可见）
        var ackGo = new GameObject("Ack");
        ackGo.transform.SetParent(canvasGo.transform, false);
        _ack = ackGo.AddComponent<Text>();
        _ack.font = font;
        _ack.fontSize = 16;
        _ack.color = new Color(textColor.r, textColor.g, textColor.b, 0f);
        _ack.alignment = TextAnchor.MiddleCenter;
        _ack.text = "世界收到了。";
        var art = (RectTransform)ackGo.transform;
        art.anchorMin = new Vector2(0.5f, 0f);
        art.anchorMax = new Vector2(0.5f, 0f);
        art.pivot = new Vector2(0.5f, 0f);
        art.anchoredPosition = new Vector2(0f, 28f);
        art.sizeDelta = new Vector2(300f, 24f);
    }

    // 内置动态字体：经 OS 回退可渲染中文（TMP 默认字体无 CJK 字形，占位不用 TMP）
    private static Font LoadBuiltinFont()
    {
        try { return Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf"); }
        catch { return Resources.GetBuiltinResource<Font>("Arial.ttf"); }
    }
}
