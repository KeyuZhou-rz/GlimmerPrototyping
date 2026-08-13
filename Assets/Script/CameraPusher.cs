using System.Collections;
using UnityEngine;

/// <summary>
/// B 方案运镜（Worksheet §0 拍板）：固定机位 + 点击聚焦——KRZ 舞台感。
/// 2026-08-13 交互模型修订（拍板）：世界上任何可点之物（草、痕迹）点击即聚焦并**停留**，
/// 不再数秒自动回主机位；聚焦中点"旁边"（空地/天空）才回主机位，点另一件东西则转焦。
/// 纯展示层：只动相机 transform，不读不写任何世界状态。
/// </summary>
public class CameraPusher : MonoBehaviour
{
    [Header("推近参数（占位默认值，待 playtest 调）")]
    public float pushDistance = 6f;     // 聚焦后距目标多远
    public float pushDuration = 1.2f;   // 去程/回程各多久
    public AnimationCurve ease = AnimationCurve.EaseInOut(0f, 0f, 1f, 1f);

    [Header("聚焦中滚轮俯仰（2026-08-13 拍板）")]
    [Tooltip("每格滚轮的俯仰步进（度）")]
    public float pitchPerNotchDeg = 10f;
    [Tooltip("俯仰下限：平行（0°=与聚焦点同高平视），不允许低于平行")]
    public float pitchMinDeg = 0f;
    [Tooltip("俯仰上限：正俯视 90°")]
    public float pitchMaxDeg = 90f;
    [Tooltip("俯仰平滑（指数趋近速率，越大越跟手）")]
    public float pitchSmooth = 12f;

    /// <summary>是否正停在某个聚焦点上（等"点旁边"召回）。输入层据此决定本次点击是转焦还是回归。</summary>
    public bool IsFocused { get; private set; }
    /// <summary>当前聚焦点（IsFocused=false 时为上一处，仅调试参考）。</summary>
    public Vector3 FocusPoint { get; private set; }

    private Vector3 _homePos;
    private Quaternion _homeRot;
    private Coroutine _co;

    // 滚轮俯仰状态：仰角（度，0=平视聚焦点 / 90=正俯视）+ 与聚焦点的距离（推近时锁定）
    private float _pitchCur, _pitchTarget, _focusDist;

    void Awake()
    {
        // 舞台机位 = 场景里摆好的初始 transform（固定机位由场景设置保证）
        _homePos = transform.position;
        _homeRot = transform.rotation;
    }

    // 聚焦停留期间：滚轮绕聚焦点俯仰（钳 [pitchMinDeg, pitchMaxDeg]，默认 0° 平行 ~ 90° 正俯视）。
    // 只在停稳后响应（_co==null）；运镜途中滚轮不插手。信/日记开着时不抢滚轮。
    void LateUpdate()
    {
        if (!IsFocused || _co != null) return;
        if (ChronicleLetter.IsOpen || DiaryInputUI.IsOpen) return;
        var mouse = UnityEngine.InputSystem.Mouse.current;
        if (mouse == null) return;

        float wheel = mouse.scroll.ReadValue().y;
        if (Mathf.Abs(wheel) > 0.01f)
            _pitchTarget = Mathf.Clamp(_pitchTarget + wheel / 120f * pitchPerNotchDeg,
                                       pitchMinDeg, pitchMaxDeg);   // 新输入系统一格≈120

        if (Mathf.Abs(_pitchTarget - _pitchCur) < 0.001f) return;
        _pitchCur = Mathf.Lerp(_pitchCur, _pitchTarget, 1f - Mathf.Exp(-pitchSmooth * Time.deltaTime));
        ApplyPitch(_pitchCur);
    }

    // 保持与聚焦点的距离不变，把相机抬/压到指定仰角并始终注视聚焦点（方位角不动）
    private void ApplyPitch(float elevDeg)
    {
        Vector3 off = transform.position - FocusPoint;
        Vector3 flat = new Vector3(off.x, 0f, off.z);
        if (flat.sqrMagnitude < 1e-4f) flat = Vector3.Scale(-transform.forward, new Vector3(1f, 0f, 1f));   // 正上方时方位取当前朝向
        if (flat.sqrMagnitude < 1e-4f) flat = Vector3.forward;
        flat.Normalize();
        float el = elevDeg * Mathf.Deg2Rad;
        Vector3 newOff = (flat * Mathf.Cos(el) + Vector3.up * Mathf.Sin(el)) * _focusDist;
        transform.SetPositionAndRotation(FocusPoint + newOff,
            Quaternion.LookRotation(FocusPoint - (FocusPoint + newOff), Vector3.up));
    }

    /// <summary>聚焦到 focus 并停留（不自动返回）。聚焦中再调即转焦——从当前位置平滑过去。</summary>
    public void PushTo(Vector3 focus)
    {
        if (_co != null) StopCoroutine(_co);
        IsFocused = true;
        FocusPoint = focus;
        _co = StartCoroutine(GoTo(focus));
    }

    /// <summary>回舞台主机位（"点旁边"的召回）。</summary>
    public void ReturnToStage()
    {
        if (_co != null) StopCoroutine(_co);
        IsFocused = false;
        _co = StartCoroutine(ReturnRoutine());
    }

    private IEnumerator GoTo(Vector3 focus)
    {
        Vector3 from = transform.position;
        Vector3 dir = focus - from;
        if (dir.sqrMagnitude < 1e-4f) { _co = null; yield break; }
        dir.Normalize();
        Vector3 targetPos = focus - dir * pushDistance;
        Quaternion targetRot = Quaternion.LookRotation(focus - targetPos, Vector3.up);

        yield return Blend(from, targetPos, transform.rotation, targetRot, pushDuration);

        // 停稳后接管俯仰：锁定当前距离，仰角以落位姿态为起点（低于下限先抬回平行）
        Vector3 off = targetPos - focus;
        _focusDist = off.magnitude;
        float elev = _focusDist > 1e-4f
            ? Mathf.Asin(Mathf.Clamp(off.y / _focusDist, -1f, 1f)) * Mathf.Rad2Deg
            : pitchMinDeg;
        _pitchTarget = Mathf.Clamp(elev, pitchMinDeg, pitchMaxDeg);
        _pitchCur = _pitchTarget;
        if (_pitchTarget > elev + 0.001f) ApplyPitch(_pitchCur);   // 落位低于平行：抬回平行起步

        _co = null;   // 停在聚焦点——没有计时器，等玩家自己决定看多久
    }

    private IEnumerator ReturnRoutine()
    {
        yield return Blend(transform.position, _homePos, transform.rotation, _homeRot, pushDuration);
        _co = null;
    }

    private IEnumerator Blend(Vector3 p0, Vector3 p1, Quaternion r0, Quaternion r1, float dur)
    {
        for (float t = 0f; t < dur; t += Time.deltaTime)
        {
            float k = ease.Evaluate(Mathf.Clamp01(t / dur));
            transform.SetPositionAndRotation(Vector3.Lerp(p0, p1, k), Quaternion.Slerp(r0, r1, k));
            yield return null;
        }
        transform.SetPositionAndRotation(p1, r1);
    }
}
