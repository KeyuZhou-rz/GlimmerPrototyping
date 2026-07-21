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
}
