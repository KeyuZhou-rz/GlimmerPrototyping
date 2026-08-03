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
        if (stageCamera == null || pusher == null) return;
        // 点在 UI（信/日记）上不触发推近
        if (UnityEngine.EventSystems.EventSystem.current != null &&
            UnityEngine.EventSystems.EventSystem.current.IsPointerOverGameObject()) return;
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
}
