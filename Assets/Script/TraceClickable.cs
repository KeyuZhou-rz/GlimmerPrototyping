using UnityEngine;

/// <summary>
/// 可点击痕迹（B 方案运镜的命中组件，Worksheet §6 切片 9）。
/// 由 WorldTraceBinder 在生成痕迹根物体时挂上；focusPoint = 痕迹所在位置。
/// 红线：标记/点击只指向场景位置，永不暴露任何数值。
/// </summary>
public class TraceClickable : MonoBehaviour
{
    [Tooltip("点击推近的注视点（= 痕迹所在位置）")]
    public Vector3 focusPoint;

    [Tooltip("链式痕迹（田鼠镇小径）：沿路采样点。非空时聚焦/吸附取链上离点击处最近的点，而非整条链的盒心")]
    public Vector3[] focusChain;

    [Tooltip("痕迹类型（mound/trail/rest…，WorldTraceBinder 的 key 前缀），点击语料按它选模板")]
    public string traceType;

    [Tooltip("痕迹完整键（源记录哈希），语料按它稳定随机——同一条痕迹每次说同一段话")]
    public string traceKey;

    /// <summary>离 at 最近的注视点：普通痕迹=focusPoint；链式痕迹=折线上最近点（投影钳在段内）。</summary>
    public Vector3 NearestFocus(Vector3 at)
    {
        if (focusChain == null || focusChain.Length == 0) return focusPoint;
        if (focusChain.Length == 1) return focusChain[0];
        Vector3 best = focusChain[0];
        float bestD = float.MaxValue;
        for (int i = 0; i < focusChain.Length - 1; i++)
        {
            Vector3 a = focusChain[i], ab = focusChain[i + 1] - a;
            float t = ab.sqrMagnitude < 1e-6f ? 0f
                    : Mathf.Clamp01(Vector3.Dot(at - a, ab) / ab.sqrMagnitude);
            Vector3 p = a + ab * t;
            float d = (p - at).sqrMagnitude;
            if (d < bestD) { bestD = d; best = p; }
        }
        return best;
    }
}
