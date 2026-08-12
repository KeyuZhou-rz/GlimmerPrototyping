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

    /// <summary>是否正停在某个聚焦点上（等"点旁边"召回）。输入层据此决定本次点击是转焦还是回归。</summary>
    public bool IsFocused { get; private set; }
    /// <summary>当前聚焦点（IsFocused=false 时为上一处，仅调试参考）。</summary>
    public Vector3 FocusPoint { get; private set; }

    private Vector3 _homePos;
    private Quaternion _homeRot;
    private Coroutine _co;

    void Awake()
    {
        // 舞台机位 = 场景里摆好的初始 transform（固定机位由场景设置保证）
        _homePos = transform.position;
        _homeRot = transform.rotation;
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
