using UnityEngine;

/// <summary>
/// 世界点击输入（B 方案，2026-08-13 交互模型修订 v2，拍板）：
///   单击痕迹 → 聚焦 + 一句观察语（点痕迹附近一带也算——吸附代替精准点击）；
///   单击草/地面 → 只有草簌动（一个动作一个结果，不拉相机）；
///   双击任何物体（草/石头/水面）→ 聚焦该处（附近带痕迹则聚到痕迹上）；
///   聚焦中单击旁边/天空 → 回舞台主机位（等一个双击窗口再动，防双击转焦被误吞）。
/// 点在 UI 上不触发。纯输入层：不读不写世界状态，只转发位置。
/// </summary>
public class TraceInput : MonoBehaviour
{
    [Header("留空自动找 Camera.main 与其上的 CameraPusher")]
    public Camera stageCamera;
    public CameraPusher pusher;

    [Tooltip("占位默认全层；场景整理后可收拢到独立痕迹层")]
    public LayerMask traceMask = ~0;
    public float maxRayDistance = 500f;
    [Tooltip("落点半径内有痕迹则吸附到最近那件（撤悬浮球后痕迹命中体小，吸附代替精准点击）")]
    public float traceSnapRadius = 4f;
    [Tooltip("双击判定窗口（秒）：单击=触感/回归，双击=聚焦")]
    public float doubleClickWindow = 0.3f;

    private float _lastClickTime = -10f;
    private Vector2 _lastClickScreen;
    private float _pendingReturn = -1f;   // >0 = 聚焦中的单击，等双击窗口过期再回主机位

    void Awake()
    {
        if (stageCamera == null) stageCamera = Camera.main;
        if (pusher == null && stageCamera != null) pusher = stageCamera.GetComponent<CameraPusher>();
        if (pusher == null) pusher = FindFirstObjectByType<CameraPusher>();
    }

    void Update()
    {
        // 延迟回归到期（该单击没被第二击接住，确认是"点旁边"）
        if (_pendingReturn > 0f)
        {
            _pendingReturn -= Time.deltaTime;
            if (_pendingReturn <= 0f) pusher.ReturnToStage();
        }

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

        Vector2 screenPos = mouse.position.ReadValue();
        bool isDouble = Time.unscaledTime - _lastClickTime < doubleClickWindow
                        && Vector2.Distance(screenPos, _lastClickScreen) < 40f;
        _lastClickTime = Time.unscaledTime;
        _lastClickScreen = screenPos;

        Ray ray = stageCamera.ScreenPointToRay(screenPos);
        var hits = Physics.RaycastAll(ray, maxRayDistance, traceMask);
        System.Array.Sort(hits, (a, b) => a.distance.CompareTo(b.distance));

        // 最近的痕迹命中与"第一个打到的任何东西"各取一（痕迹可能躲在草后）
        TraceClickable tc = null;
        Vector3 hitPoint = default, traceHitPoint = default;
        bool hitAnything = false, hitTerrain = false;
        foreach (var h in hits)
        {
            if (tc == null)
            {
                var c = h.collider.GetComponentInParent<TraceClickable>();
                if (c != null) { tc = c; traceHitPoint = h.point; }
            }
            if (!hitAnything && h.collider.GetComponentInParent<TraceClickable>() == null)
            {
                hitAnything = true;
                hitPoint = h.point;
                hitTerrain = h.collider.GetComponentInParent<TerrainGenerator>() != null;
            }
            if (tc != null && hitAnything) break;
        }

        // ① 点中痕迹（单击即可）→ 聚焦 + 观察语；再点一次同一件＝再读一遍它的说法
        // 就近参照优先用地形落点 hitPoint：大命中盒的顶面先截住射线，h.point 可能落在
        // 盒顶任何一处；hitPoint 才是玩家手指真正指的那个地面位置。
        if (tc != null)
        {
            _pendingReturn = -1f;
            FocusTrace(tc, hitAnything ? hitPoint : traceHitPoint);
            return;
        }

        // ② 点中任何物体（草/地面/石头/水面）
        if (hitAnything)
        {
            // 触感层：点在草海/地面上——单击双击都簌动（触感不挑击数）
            if (hitTerrain)
            {
                GrassTouchFeedback.Touch(hitPoint);
                AmbientAudio.PlayGrassRustle(hitPoint);
            }

            // 吸附：落点附近有痕迹 → 聚到痕迹上（单击双击都算——那一片的重点是那件痕迹）
            var snapped = NearestTrace(hitPoint, traceSnapRadius);
            if (snapped != null)
            {
                _pendingReturn = -1f;
                FocusTrace(snapped, hitPoint);
                return;
            }

            // 双击空地 → 聚焦该处
            if (isDouble)
            {
                _pendingReturn = -1f;
                pusher.PushTo(hitPoint);
                return;
            }

            // 单击空地：聚焦中 → 准备回归（等一个双击窗口，若是双击转焦则取消）
            if (pusher.IsFocused) _pendingReturn = doubleClickWindow;
            return;
        }

        // ③ 点天空：聚焦中单击 → 同样准备回归；双击天空无意义
        if (pusher.IsFocused && !isDouble) _pendingReturn = doubleClickWindow;
    }

    // clickAt = 本次点击的世界落点：链式痕迹（小径）聚焦到链上最近的那段，而非整条链的盒心
    private void FocusTrace(TraceClickable tc, Vector3 clickAt)
    {
        pusher.PushTo(tc.NearestFocus(clickAt));
        TraceCaptionUI.Show(tc.traceType, tc.traceKey);   // 聚焦同时给一句观察（展示层，不进世界志）
    }

    // 落点半径内最近的 TraceClickable（OverlapSphere 兜底——痕迹碰撞体小，不用像素级瞄准）
    // 距离按"离痕迹最近的可见点"算（链式痕迹取链上最近点），长链不再靠盒心抢吸附
    private TraceClickable NearestTrace(Vector3 at, float radius)
    {
        var cols = Physics.OverlapSphere(at, radius, traceMask);
        TraceClickable best = null;
        float bestD = float.MaxValue;
        foreach (var c in cols)
        {
            var tc = c.GetComponentInParent<TraceClickable>();
            if (tc == null) continue;
            float d = (tc.NearestFocus(at) - at).sqrMagnitude;
            if (d < bestD) { bestD = d; best = tc; }
        }
        return best;
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
