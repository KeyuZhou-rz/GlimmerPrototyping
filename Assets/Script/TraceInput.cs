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

        Ray ray = stageCamera.ScreenPointToRay(mouse.position.ReadValue());
        if (Physics.Raycast(ray, out var hit, maxRayDistance, traceMask))
        {
            var tc = hit.collider.GetComponentInParent<TraceClickable>();
            if (tc != null) pusher.PushTo(tc.focusPoint);
        }
    }
}
