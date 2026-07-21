using System.Collections;
using UnityEngine;

/// <summary>
/// B 方案运镜（Worksheet §0 拍板）：固定机位 + 点击痕迹推近——KRZ 舞台感。
/// 相机平时钉死在舞台机位；PushTo(focus) 沿当前视线方向推近、停留、原路返回。
/// 纯展示层：只动相机 transform，不读不写任何世界状态。
/// </summary>
public class CameraPusher : MonoBehaviour
{
    [Header("推近参数（占位默认值，待 playtest 调）")]
    public float pushDistance = 6f;     // 推近后距目标多远
    public float pushDuration = 1.2f;   // 去程/回程各多久
    public float holdDuration = 2.5f;   // 停留多久
    public AnimationCurve ease = AnimationCurve.EaseInOut(0f, 0f, 1f, 1f);

    private Vector3 _homePos;
    private Quaternion _homeRot;
    private Coroutine _co;

    void Awake()
    {
        // 舞台机位 = 场景里摆好的初始 transform（固定机位由场景设置保证）
        _homePos = transform.position;
        _homeRot = transform.rotation;
    }

    public void PushTo(Vector3 focus)
    {
        if (_co != null) StopCoroutine(_co);
        _co = StartCoroutine(PushRoutine(focus));
    }

    private IEnumerator PushRoutine(Vector3 focus)
    {
        Vector3 dir = focus - transform.position;
        if (dir.sqrMagnitude < 1e-4f) { _co = null; yield break; }
        dir.Normalize();
        Vector3 targetPos = focus - dir * pushDistance;
        Quaternion targetRot = Quaternion.LookRotation(focus - targetPos, Vector3.up);

        yield return Blend(_homePos, targetPos, _homeRot, targetRot, pushDuration);
        yield return new WaitForSeconds(holdDuration);
        yield return Blend(targetPos, _homePos, targetRot, _homeRot, pushDuration);
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
