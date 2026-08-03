using UnityEngine;

/// <summary>
/// 点草簌动（触感层第一批）：玩家点击草海某处，触点周围的草簇朝外颤动一阵，
/// 像手指拨过草丛。纯 L3 表现层——不读不写世界状态，只把点击位置翻译成
/// shader global _GrassTouch（xy=世界XZ, z=半径, w=强度），本组件是其唯一写者。
/// 强度 ~1.8s 指数衰减归零；OnDisable 清零防残留（WorldAtmosphereBinder 同款教训）。
/// 运行时由 DemoUIBootstrap 创建，场景零接线。
/// </summary>
public class GrassTouchFeedback : MonoBehaviour
{
    private static GrassTouchFeedback _instance;

    [Tooltip("影响半径（米）——触点周围这圈草会颤动")]
    public float touchRadius = 3f;
    [Tooltip("颤动衰减时长（秒）——强度按指数在这段时间内基本散尽")]
    public float decaySeconds = 1.8f;

    private static readonly int GrassTouchID = Shader.PropertyToID("_GrassTouch");

    private Vector3 _touchPos;
    private float   _strength;
    private float   _age;

    /// <summary>点击落点触发一次簌动。未初始化（无组件）时静默忽略。</summary>
    public static void Touch(Vector3 worldPos)
    {
        if (_instance == null) _instance = FindFirstObjectByType<GrassTouchFeedback>();
        if (_instance == null) return;
        _instance._touchPos = worldPos;
        _instance._strength = 1f;
        _instance._age      = 0f;
    }

    void LateUpdate()
    {
        if (_strength > 0f)
        {
            _age += Time.deltaTime;
            // 指数衰减：decaySeconds 时余 ~37%，三倍时长后 <1% 直接归零
            _strength = Mathf.Exp(-_age / Mathf.Max(0.05f, decaySeconds * 0.5f));
            if (_strength < 0.01f) _strength = 0f;
        }
        Shader.SetGlobalVector(GrassTouchID,
            new Vector4(_touchPos.x, _touchPos.z, touchRadius, _strength));
    }

    void OnDisable() => Shader.SetGlobalVector(GrassTouchID, Vector4.zero);
}
