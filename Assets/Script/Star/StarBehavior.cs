using UnityEngine;

public class StarBehavior : MonoBehaviour
{
    [Header("旋转设置")]
    public float rotationSpeed = 50.0f; // 旋转速度

    [Header("闪烁设置 (呼吸感)")]
    public float minSize = 0.8f; // 最小缩放倍数
    public float maxSize = 1.2f; // 最大缩放倍数
    public float twinkleSpeed = 2.0f; // 闪烁频率

    // 为了让每个星星闪烁得不一样，我们需要一个随机偏移量
    private float randomOffset;
    private Vector3 initialScale; // 记住最开始的大小

    void Start()
    {
        // 记录星星刚开始放置时的大小
        initialScale = transform.localScale;
        // 给每个星星一个随机的起始点，这样它们不会同时变大变小
        randomOffset = Random.Range(0f, 100f);
    }

    void Update()
    {
        // 1. 自转逻辑
        // 绕着 Y 轴 (Up) 旋转
        transform.Rotate(Vector3.up * rotationSpeed * Time.deltaTime);

        // 2. 闪烁逻辑 (利用 PingPong 函数在两个数值间往返)
        // Time.time + randomOffset 确保每个星星节奏不同
        float scaleProgress = Mathf.PingPong((Time.time + randomOffset) * twinkleSpeed, 1.0f);

        // Lerp 是 "线性插值"，它根据 scaleProgress (0到1) 在 minSize 和 maxSize 之间取值
        float currentScaleMultiplier = Mathf.Lerp(minSize, maxSize, scaleProgress);

        // 应用新的大小
        transform.localScale = initialScale * currentScaleMultiplier;
    }
}