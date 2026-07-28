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

    [Tooltip("痕迹类型（mound/trail/rest…，WorldTraceBinder 的 key 前缀），点击语料按它选模板")]
    public string traceType;

    [Tooltip("痕迹完整键（源记录哈希），语料按它稳定随机——同一条痕迹每次说同一段话")]
    public string traceKey;
}
