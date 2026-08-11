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
        // 诊断（2026-08-12 点击失灵排查）：逐闸门打日志，定位后删除
        bool overUI = UnityEngine.EventSystems.EventSystem.current != null &&
                      UnityEngine.EventSystems.EventSystem.current.IsPointerOverGameObject();
        // 点在 UI（信/日记）上不触发推近
        if (overUI) { Debug.Log("[TraceInput] 点击被闸门①挡住：IsPointerOverGameObject=true"); return; }
        // 信或日记面板开着：面板外点击也不推相机（双保险，防穿透）
        if (ChronicleLetter.IsOpen || DiaryInputUI.IsOpen)
        {
            Debug.Log($"[TraceInput] 点击被闸门②挡住：letterOpen={ChronicleLetter.IsOpen} diaryOpen={DiaryInputUI.IsOpen}");
            return;
        }

        Ray ray = stageCamera.ScreenPointToRay(mouse.position.ReadValue());
        if (Physics.Raycast(ray, out var hit, maxRayDistance, traceMask))
        {
            var tc = hit.collider.GetComponentInParent<TraceClickable>();
            if (tc != null)
            {
                Debug.Log($"[TraceInput] 命中痕迹 {hit.collider.name}（type={tc.traceType}）→ 推近+语料");
                pusher.PushTo(tc.focusPoint);
                TraceCaptionUI.Show(tc.traceType, tc.traceKey);   // 推近同时给一句观察（展示层，不进世界志）
            }
            else if (hit.collider.GetComponentInParent<TerrainGenerator>() != null)
            {
                Debug.Log($"[TraceInput] 命中地形 @ {hit.point} → 点草簌动");
                // 触感层：点的是草海/地面——触点周围草簇簌动 + 簌簌声（占位，无素材静默）。
                // 纯表现层回应，不碰世界状态。
                GrassTouchFeedback.Touch(hit.point);
                AmbientAudio.PlayGrassRustle(hit.point);
            }
            else
            {
                Debug.Log($"[TraceInput] 命中 {hit.collider.name}（既非痕迹也非地形，无分支）");
            }
        }
        else
        {
            Debug.Log("[TraceInput] 射线未命中任何碰撞体");
        }
    }
}
