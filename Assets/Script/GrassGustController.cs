using UnityEngine;
using GlimmerDiary.Flora;

/// <summary>
/// 阵风草浪（触感层第一批）：风大的时候，偶尔一道摆动更猛的风带滚过整片草海——
/// 像草原上真实掠过的一阵大风。纯 L3 表现层：只读世界风场（WindSpeed），
/// 写 shader global _GrassGustBand（x=带中心沿风向投影, y=半宽, z=强度增益），
/// 本组件是其唯一写者；结束与 OnDisable 归零防残留。
/// 触发概率 ∝ WindSpeed²（机制互联：世界的风决定阵风，不是纯随机表演），
/// 冷却 20-40 秒防密集。扫掠轴取 GrassSystem.windDirection（风摆方向是 MPB，
/// shader global 读不到——必须同轴，否则风带走向与可见摆动脱节）。
/// 运行时由 DemoUIBootstrap 创建，场景零接线。
/// </summary>
public class GrassGustController : MonoBehaviour
{
    [Header("触发（读世界风场）")]
    [Tooltip("WindSpeed=1 时每秒触发概率；实际概率 ∝ 风速²")]
    public float gustChancePerSecond = 0.02f;
    public Vector2 cooldownRange = new Vector2(20f, 40f);

    [Header("风带形状")]
    public float bandHalfWidth = 20f;     // 米
    public float bandStrength  = 1.6f;    // 带内风摆增益倍数
    public Vector2 sweepDurationRange = new Vector2(6f, 10f);

    private static readonly int GrassGustBandID = Shader.PropertyToID("_GrassGustBand");

    private GrassSystem _grass;
    private float _cooldown;
    private bool  _sweeping;
    private float _sweepAge, _sweepDuration;
    private float _projMin, _projMax;    // 地形范围在风向上的投影区间
    private Vector2 _windXZ;             // 归一化扫掠轴
    private Vector3 _bandCenterWorld;    // 音频落点用（带中心的世界位置）

    void Start()
    {
        _grass = FindFirstObjectByType<GrassSystem>();
        _cooldown = cooldownRange.x;   // 开场先安静一会儿
    }

    void LateUpdate()
    {
        if (_grass == null || _grass.terrainCollider == null)
        {
            _grass = FindFirstObjectByType<GrassSystem>();
            if (_grass == null) return;
        }

        if (_sweeping) AdvanceSweep();
        else           MaybeTrigger();
    }

    private void MaybeTrigger()
    {
        _cooldown -= Time.deltaTime;
        if (_cooldown > 0f) return;

        var wm = WorldManager.Instance;
        if (wm == null) return;
        float wind = wm.GetWorldState().WindSpeed;
        if (UnityEngine.Random.value < wind * wind * gustChancePerSecond * Time.deltaTime)
            BeginSweep();
    }

    private void BeginSweep()
    {
        _windXZ = _grass.windDirection;
        if (_windXZ.sqrMagnitude < 1e-6f) _windXZ = Vector2.right;
        _windXZ.Normalize();

        // 地形包围盒四角投影到风向上，取扫掠起止（逆风向往下风向滚）
        Bounds bb = _grass.terrainCollider.bounds;
        _projMin = float.MaxValue; _projMax = float.MinValue;
        for (int i = 0; i < 4; i++)
        {
            var c = new Vector2(i % 2 == 0 ? bb.min.x : bb.max.x,
                                i < 2     ? bb.min.z : bb.max.z);
            float p = Vector2.Dot(c, _windXZ);
            _projMin = Mathf.Min(_projMin, p);
            _projMax = Mathf.Max(_projMax, p);
        }
        _projMin -= bandHalfWidth;   // 从草海外滚进来、滚出去，避免两端突然闪现
        _projMax += bandHalfWidth;

        _sweeping      = true;
        _sweepAge      = 0f;
        _sweepDuration = UnityEngine.Random.Range(sweepDurationRange.x, sweepDurationRange.y);
        _cooldown      = UnityEngine.Random.Range(cooldownRange.x, cooldownRange.y);

        // 阵风声（占位：无素材时 AmbientAudio 静默）
        Vector2 c2 = new Vector2(bb.center.x, bb.center.z);
        _bandCenterWorld = new Vector3(c2.x, bb.center.y, c2.y);
        AmbientAudio.PlayGust(_bandCenterWorld);
    }

    private void AdvanceSweep()
    {
        _sweepAge += Time.deltaTime;
        float t = _sweepAge / _sweepDuration;
        if (t >= 1f)
        {
            _sweeping = false;
            Shader.SetGlobalVector(GrassGustBandID, Vector4.zero);
            return;
        }
        float center   = Mathf.Lerp(_projMin, _projMax, t);
        float strength = Mathf.Sin(t * Mathf.PI) * bandStrength;   // 滚入渐强、滚出渐弱
        Shader.SetGlobalVector(GrassGustBandID,
            new Vector4(center, bandHalfWidth, strength, 0f));
    }

    void OnDisable()
    {
        _sweeping = false;
        Shader.SetGlobalVector(GrassGustBandID, Vector4.zero);
    }
}
