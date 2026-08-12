using UnityEngine;

/// <summary>
/// 痕迹点击输入（B 方案，Worksheet §6 切片 9）：左键 → 从舞台相机发射线 →
/// 命中 TraceClickable 则 CameraPusher 推近。点在 UI 上不触发。
/// 纯输入层：不读不写世界状态，只转发位置。
/// </summary>
public class TraceInput : MonoBehaviour
{
    [Header("留空自动找 Camera.main 与其上的 CameraPusher")]
    public Camera stageCamera;
    public CameraPusher pusher;

    [Tooltip("占位默认全层；场景整理后可收拢到独立痕迹层")]
    public LayerMask traceMask = ~0;
    public float maxRayDistance = 500f;

    void Awake()
    {
        if (stageCamera == null) stageCamera = Camera.main;
        if (pusher == null && stageCamera != null) pusher = stageCamera.GetComponent<CameraPusher>();
        if (pusher == null) pusher = FindFirstObjectByType<CameraPusher>();
    }

    void Update()
    {
        // 项目启用新 Input System（Player Settings），旧 UnityEngine.Input 会抛异常
        var mouse = UnityEngine.InputSystem.Mouse.current;
        if (mouse == null || !mouse.leftButton.wasPressedThisFrame) return;
        if (stageCamera == null || pusher == null)
        {
            Debug.LogWarning($"[TraceInput] 点击但引用缺失：stageCamera={(stageCamera == null ? "null" : "ok")} pusher={(pusher == null ? "null" : "ok")}");
            return;
        }
        // 点在 uGUI 上不触发推近。
        // 注意（2026-08-13 点击失灵根因）：不能用 IsPointerOverGameObject()——
        // 主相机上挂着 PhysicsRaycaster（mask=全层），新输入模块把"指针射线打到的
        // 第一个物体"（含地形等 3D 碰撞体）都算作 pointerEnter，导致悬停世界恒 true，
        // 全图点击被这道闸门吞掉。这里只认 GraphicRaycaster 的 uGUI 图形命中。
        if (PointerOverUGUI(mouse.position.ReadValue())) return;
        // 信或日记面板开着：面板外点击也不推相机（双保险，防穿透）
        if (ChronicleLetter.IsOpen || DiaryInputUI.IsOpen) return;

        Ray ray = stageCamera.ScreenPointToRay(mouse.position.ReadValue());
        if (Physics.Raycast(ray, out var hit, maxRayDistance, traceMask))
        {
            var tc = hit.collider.GetComponentInParent<TraceClickable>();
            if (tc != null)
            {
                pusher.PushTo(tc.focusPoint);
                TraceCaptionUI.Show(tc.traceType, tc.traceKey);   // 推近同时给一句观察（展示层，不进世界志）
            }
            else if (hit.collider.GetComponentInParent<TerrainGenerator>() != null)
            {
                // 触感层：点的是草海/地面——触点周围草簇簌动 + 簌簌声（占位，无素材静默）。
                // 纯表现层回应，不碰世界状态。
                GrassTouchFeedback.Touch(hit.point);
                AmbientAudio.PlayGrassRustle(hit.point);
            }
        }
    }

    // 指针是否悬停在 uGUI 图形上（只数 GraphicRaycaster 的命中；
    // 场景里没有任何 GraphicRaycaster 时恒 false——信/日记由闸门② IsOpen 兜底）
    private static bool PointerOverUGUI(Vector2 screenPos)
    {
        var es = UnityEngine.EventSystems.EventSystem.current;
        if (es == null) return false;
        var data = new UnityEngine.EventSystems.PointerEventData(es) { position = screenPos };
        var results = new System.Collections.Generic.List<UnityEngine.EventSystems.RaycastResult>();
        es.RaycastAll(data, results);
        foreach (var r in results)
            if (r.module is UnityEngine.UI.GraphicRaycaster) return true;
        return false;
    }
}
