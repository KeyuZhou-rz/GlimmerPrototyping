using UnityEngine;
using GlimmerDiary.Ecosystem_nonL;
/// <summary>
/// Layer 3 绑定层：世界状态（只读） → 视觉组件公开字段。
/// 每帧读 WorldManager 的只读入口，算目标值并做一层展示层平滑，
/// LateUpdate 写进 EmotionWeatherController / LightManager / EcosystemManager。
/// 纪律：绝不写世界状态、绝不调用任何 Layer 2 模拟/注入入口；
/// 不导入 GlimmerDiary.Core（返回值一律用 var 承接）。
/// </summary>
public class WorldAtmosphereBinder : MonoBehaviour
{
    [Header("绑定目标（场景现有组件，缺省则跳过对应通路）")]
    public EmotionWeatherController weatherController;
    public LightManager lightManager;
    public EcosystemManager treePlacement;

    [Header("展示层平滑")]
    [Tooltip("指数平滑速率。Layer 2 的值是按天跳变的快照，展示层需要自己的过渡，独立于 E_env 自身的惯性。")]
    public float smoothingSpeed = 0.5f;

    [Header("雷暴门控（审美判断的保守起点）")]
    [Tooltip("风与雨都超过该阈值时雷暴才开始爬升——雷暴要稀有，不能常态化")]
    [Range(0f, 1f)] public float thunderWindThreshold = 0.45f;
    [Range(0f, 1f)] public float thunderRainThreshold = 0.45f;

    [Header("水面升降(riverbank 水量 → Water transform)")]
    public WaterGenerator waterSurface;
    [Tooltip("驱动水面的 location id(河道空间上贴着河岸区)")]
    public string waterSourceLocationId = "riverbank";
    [Tooltip("水位是日积分慢变量,比天气 0.5 慢一个量级(τ≈20s):水面垂直位移比颜色更扎眼,必须爬不能跳")]
    public float waterSmoothingSpeed = 0.05f;

    [Header("种子絮（§5.3：风峰 ∧ 蒲公英在场开花 → 河岸絮飘=玩家可见的风）")]
    [Tooltip("与 VegetationSystem 落种阈值同源数值——L3 展示层阈值，各自独立调")]
    [Range(0f, 1f)] public float fluffWindThreshold = 0.7f;

    [Header("侧翼尘霾（V1 D8 风通道：西翼旱情 → 地平线尘霾）")]
    [Tooltip("尘霾是周粒度慢变量——展示层平滑比天气再慢一档，霾是'挂上去的'不是'飘过来的'")]
    public float dustSmoothingSpeed = 0.08f;

    [Header("草色通路（Batch 4：{DecayLevel, season, zone 湿度} → 草色，映射表=GrassPreset）")]
    public GlimmerDiary.Flora.GrassPreset grassPreset;
    [Tooltip("草色(慢变量)的展示层平滑速率——土壤湿度按天变,颜色爬不能跳")]
    public float grassSmoothingSpeed = 0.15f;
    [Tooltip("调试：强制季节（-1=跟随世界日历, 0=冬, 1=春, 2=夏, 3=秋）")]
    public int debugSeasonOverride = -1;
    [Tooltip("调试：交还时间控制权——勾选后本绑定层不再驱动光照时刻，时间改由 DayNightAndLightController(LightManager) 自己的内部时钟走（TimeOfDay 可直接拖、TimeMultiplier 可加速），便于脱离世界日历调光")]
    public bool debugManualTimeControl = false;

    // 展示层平滑后的当前值（目标值来自世界状态快照）
    private float _rain;      // [-1 雨, +1 晴]，与 weatherController.rainIntensity 同语义
    private float _wind;      // [0,1]
    private float _thunder;   // [0,1]
    private float _dimness;   // [0,1]
    private float _starVis;   // [0,1]
    private bool _initialized;
    private float _waterLevel;       // [0,1] 平滑后的水量
    private bool _waterInitialized;  // 独立首帧对齐:location 可能晚于全局状态就绪
    private float _fluff;            // [0,1] 种子絮速率（平滑后）
    private float _dust;             // [0,1] 侧翼尘霾（西翼旱情，平滑后）

    // —— 草色通路（Batch 4）——
    private const int GrassMaxZones = 8;   // 与 shader _GrassZoneAnchors[8] 耦合——两侧同改
    private ZoneMap _zoneMap;              // 惰性查找（场景已有，Setup World Traces 建）
    private Vector4[] _grassZoneAnchors;   // xy=世界XZ中心, z=半径（一次性缓存，锚点不动）
    private string[] _grassZoneIds;
    private float[] _grassMoisture;        // 平滑后的分区土壤湿度
    private float[] _grassMoisturePush;    // 推送用复用数组（免 GC）
    private bool _grassAnchorsReady;
    private float _grassDefaultMoisture;   // 平滑后全局 SoilMoisture
    private float _grassDrought;           // 平滑后旱枯黄量（已按 preset 区间映射）
    private float _grassDecay;             // 平滑后 DecayLevel

    private static readonly int GrassZoneCountID      = Shader.PropertyToID("_GrassZoneCount");
    private static readonly int GrassZoneAnchorsID    = Shader.PropertyToID("_GrassZoneAnchors");
    private static readonly int GrassZoneMoistureID   = Shader.PropertyToID("_GrassZoneMoisture");
    private static readonly int GrassDefaultMoistID   = Shader.PropertyToID("_GrassDefaultMoisture");
    private static readonly int GrassMoistDryID       = Shader.PropertyToID("_GrassMoistDry");
    private static readonly int GrassMoistWetID       = Shader.PropertyToID("_GrassMoistWet");
    private static readonly int GrassSeasonTintID     = Shader.PropertyToID("_GrassSeasonTint");
    private static readonly int GrassDroughtTintID    = Shader.PropertyToID("_GrassDroughtTint");
    private static readonly int GrassDroughtAmtID     = Shader.PropertyToID("_GrassDroughtAmt");
    private static readonly int GrassDecayID          = Shader.PropertyToID("_GrassDecay");
    private static readonly int GrassDecayDesatID     = Shader.PropertyToID("_GrassDecayDesat");
    private static readonly int GrassDecayDarkenID    = Shader.PropertyToID("_GrassDecayDarken");
    private static readonly int GrassColorEnableID    = Shader.PropertyToID("_GrassColorEnable");

    void Update()
    {
        var wm = WorldManager.Instance;
        if (wm == null) return;

        var env    = wm.GetWorldState();
        var rhythm = wm.GetRhythmState();
        if (env == null || rhythm == null) return;

        // —— 目标值计算（映射表见 Docs/AmbientAtmosphereBinding.md）——
        // Rainfall [0,1] → rainIntensity [-1 雨, +1 晴]：符号相反，需要翻转。
        // 晴端封顶 0.5（2026-07-27）：原 1-2R 映射要到 Rainfall>0.5 才落第一滴雨，
        // 中度情绪在天上完全不可读；现在 Rainfall≈1/3 起就有细雨，满雨仍是 -1。
        float rainTarget = Mathf.Lerp(0.5f, -1f, env.Rainfall);
        float windTarget = env.WindSpeed;

        // 雷暴：风×雨乘积，双阈值门控；两者都到 0.6 时从 0 开始爬升，全满时为 1
        float thunderTarget = 0f;
        if (env.WindSpeed > thunderWindThreshold && env.Rainfall > thunderRainThreshold)
        {
            float gate = thunderWindThreshold * thunderRainThreshold;
            thunderTarget = Mathf.Clamp01((env.WindSpeed * env.Rainfall - gate) / (1f - gate));
        }

        float dimnessTarget = env.FogDensity;
        float starVisTarget = env.StarVisibility;
        // 侧翼尘霾（D8）：西翼旱情烈度——旱不在你这里，但霾挂在你看得见的地平线上
        float dustTarget = wm.GetWingDust01();

        // 种子絮（§5.3 风的实体化）：风峰 ∧ 蒲公英在场开花 → 絮飘。
        // 与落种（L2 VegetationSystem）读同一 WindSpeed 但互不依赖——
        // 低洼太干不落种时絮照飘："风把絮吹走了，什么也没留下"也是可读的。
        float fluffTarget = 0f;
        var dandelion = wm.Registry.GetPlant("dandelion_riverbank");
        if (dandelion != null && dandelion.isAlive && dandelion.isFlowering
            && env.WindSpeed > fluffWindThreshold)
            fluffTarget = Mathf.InverseLerp(fluffWindThreshold, 1f, env.WindSpeed);

        // —— 展示层平滑（指数趋近，帧率无关）——
        if (!_initialized)
        {
            // 首帧直接对齐，避免从 0 慢慢爬到当前世界状态
            _rain = rainTarget; _wind = windTarget; _thunder = thunderTarget;
            _dimness = dimnessTarget;
            _starVis = starVisTarget;
            _fluff   = fluffTarget;
            _dust    = dustTarget;
            _initialized = true;
        }
        else
        {
            float k = 1f - Mathf.Exp(-smoothingSpeed * Time.deltaTime);
            float kd = 1f - Mathf.Exp(-dustSmoothingSpeed * Time.deltaTime);
            _rain    = Mathf.Lerp(_rain,    rainTarget,    k);
            _wind    = Mathf.Lerp(_wind,    windTarget,    k);
            _thunder = Mathf.Lerp(_thunder, thunderTarget, k);
            _dimness = Mathf.Lerp(_dimness, dimnessTarget, k);
            _starVis = Mathf.Lerp(_starVis, starVisTarget, k);
            _fluff   = Mathf.Lerp(_fluff,   fluffTarget,   k);
            _dust    = Mathf.Lerp(_dust,    dustTarget,    kd);
        }

        // —— 水位通路(独立首帧对齐;loc 缺失则静默跳过,保持现值不驱动向假默认)——
        var loc = wm.Registry.GetLocation(waterSourceLocationId);
        if (loc != null)
        {
            float waterTarget = loc.waterLevel;   // [0,1],L2 日积分慢变量
            if (!_waterInitialized)
            {
                _waterLevel = waterTarget;
                _waterInitialized = true;
            }
            else
            {
                float kw = 1f - Mathf.Exp(-waterSmoothingSpeed * Time.deltaTime);
                _waterLevel = Mathf.Lerp(_waterLevel, waterTarget, kw);
            }
        }

        // —— 草色通路（Batch 4：{DecayLevel, season, zone 湿度} → 草色）——
        // 读 soilMoisture（雨→水链＋捂水耦合）/DecayLevel/DroughtDebt/yearProgress；
        // 写 shader 全局（LateUpdate）——全是展示层状态，世界状态一个字节不动。
        if (grassPreset != null)
        {
            EnsureGrassAnchors();

            float kg = 1f - Mathf.Exp(-grassSmoothingSpeed * Time.deltaTime);

            // 分区土壤湿度（推送前过 preset 映射曲线——低端陡＝石边草雨量计灵敏度）
            if (_grassAnchorsReady)
            {
                for (int i = 0; i < _grassZoneIds.Length; i++)
                {
                    var l = wm.Registry.GetLocation(_grassZoneIds[i]);
                    float t = l != null ? l.soilMoisture : 0f;
                    if (!_grassInitialized) _grassMoisture[i] = t;
                    else _grassMoisture[i] = Mathf.Lerp(_grassMoisture[i], t, kg);
                    _grassMoisturePush[i] = grassPreset.moistureToGreen.Evaluate(_grassMoisture[i]);
                }
            }

            float defaultMoistTarget = env.SoilMoisture;
            float droughtTarget = Mathf.InverseLerp(
                grassPreset.droughtStart, grassPreset.droughtFull, env.DroughtDebt)
                * grassPreset.droughtMaxBlend;
            float decayTarget = env.DecayLevel;

            if (!_grassInitialized)
            {
                _grassDefaultMoisture = grassPreset.moistureToGreen.Evaluate(defaultMoistTarget);
                _grassDrought = droughtTarget;
                _grassDecay = decayTarget;
                _grassInitialized = true;
            }
            else
            {
                _grassDefaultMoisture = Mathf.Lerp(_grassDefaultMoisture,
                    grassPreset.moistureToGreen.Evaluate(defaultMoistTarget), kg);
                _grassDrought = Mathf.Lerp(_grassDrought, droughtTarget, kg);
                _grassDecay   = Mathf.Lerp(_grassDecay,   decayTarget,   kg);
            }

            // 季节 tint：yearProgress 连续混四季（冬至点=0，季中在 0.125/0.375/0.625/0.875），无硬切
            Color[] st = _seasonTints ??= new[]
            {
                grassPreset.seasonWinterTint, grassPreset.seasonSpringTint,
                grassPreset.seasonSummerTint, grassPreset.seasonAutumnTint
            };
            // preset 热改时刷新缓存引用（颜色是值类型，每帧读最新值）
            st[0] = grassPreset.seasonWinterTint; st[1] = grassPreset.seasonSpringTint;
            st[2] = grassPreset.seasonSummerTint; st[3] = grassPreset.seasonAutumnTint;

            // debugSeasonOverride ≥0 时强制单季
            if (debugSeasonOverride >= 0)
            {
                int ov = Mathf.Clamp(debugSeasonOverride, 0, 3);
                _grassSeasonTint = st[ov];
            }
            else
            {
                float tt = rhythm.yearProgress * 4f - 0.5f;
                int si = Mathf.FloorToInt(tt);
                float sf = tt - si;
                _grassSeasonTint = Color.Lerp(
                    st[((si % 4) + 4) % 4], st[(((si + 1) % 4) + 4) % 4], sf);
            }
        }
    }

    // zone 锚点一次性缓存（锚点是场景静态物；ZoneMap 惰性查找）
    private bool _grassInitialized;
    private Color _grassSeasonTint = Color.white;
    private Color[] _seasonTints;

    private void EnsureGrassAnchors()
    {
        if (_grassAnchorsReady) return;
        if (_zoneMap == null) _zoneMap = FindFirstObjectByType<ZoneMap>();
        if (_zoneMap == null || _zoneMap.anchors == null || _zoneMap.anchors.Length == 0) return;

        int n = Mathf.Min(_zoneMap.anchors.Length, GrassMaxZones);
        var anchors = new Vector4[n];
        var ids = new string[n];
        int count = 0;
        foreach (var a in _zoneMap.anchors)
        {
            if (count >= n) break;
            if (a == null || string.IsNullOrEmpty(a.zoneId)) continue;
            if (!_zoneMap.TryGetAnchorCenter(a.zoneId, out Vector3 center, out float radius)) continue;
            anchors[count] = new Vector4(center.x, center.z, radius, 0f);
            ids[count] = a.zoneId;
            count++;
        }
        if (count == 0) return;

        _grassZoneAnchors = anchors;
        _grassZoneIds = ids;
        _grassMoisture = new float[count];
        _grassMoisturePush = new float[count];
        _grassAnchorsReady = true;
        // 锚点静态：推一次即可（play 退出后由 OnDisable 的 enable=0 兜底，陈旧数据不可见）
        Shader.SetGlobalVectorArray(GrassZoneAnchorsID, _grassZoneAnchors);
        Shader.SetGlobalFloat(GrassZoneCountID, count);
    }

    void LateUpdate()
    {

        if (!_initialized) return;
        var wm = WorldManager.Instance;
        if (wm == null) return;
        var env = wm.GetWorldState();
        if (weatherController != null && weatherController.allowExternalDrive)
        {
            weatherController.rainIntensity    = _rain;
            weatherController.windIntensity    = _wind;
            weatherController.thunderIntensity = _thunder;
            weatherController.dimness          = _dimness;
            weatherController.starVisibility   = _starVis;
            weatherController.seedFluffRate    = _fluff;
            weatherController.dustHaze         = _dust;   // 侧翼尘霾（D8 风通道）
        }

        if (lightManager != null)
        {
            if (debugManualTimeControl)
            {
                // 调试模式：交还内部时钟（TimeOfDay/TimeMultiplier 由 Inspector 直接控制），
                // 世界日历照常走，只是光照不再跟它——只影响视觉，不动世界状态。
                lightManager.driveExternally = false;
            }
            else
            {
                // dayProgress 不做平滑：它本身连续微变，平滑反而会在午夜 1→0 回绕处出错
                var rhythm = wm.GetRhythmState();
                lightManager.driveExternally = true;
                lightManager.SetTimePercent(rhythm.dayProgress);
            }
        }

        if (treePlacement != null)
        {
            if (env != null)
                treePlacement.SetRainfall(env.Rainfall);
        }

        if (waterSurface != null && _waterInitialized)
            waterSurface.SetDisplayLevel01(_waterLevel);

        // —— 草色通路推送（Batch 4）：shader 全局,单写者即本 binder ——
        if (grassPreset != null && _grassInitialized)
        {
            Shader.SetGlobalFloat(GrassColorEnableID, 1f);
            if (_grassAnchorsReady)
                Shader.SetGlobalFloatArray(GrassZoneMoistureID, _grassMoisturePush);
            Shader.SetGlobalFloat(GrassDefaultMoistID, _grassDefaultMoisture);
            Shader.SetGlobalColor(GrassMoistDryID, grassPreset.moistureDryTint);
            Shader.SetGlobalColor(GrassMoistWetID, grassPreset.moistureWetTint);
            Shader.SetGlobalColor(GrassSeasonTintID, _grassSeasonTint);
            Shader.SetGlobalColor(GrassDroughtTintID, grassPreset.droughtTint);
            Shader.SetGlobalFloat(GrassDroughtAmtID, _grassDrought);
            Shader.SetGlobalFloat(GrassDecayID, _grassDecay);
            Shader.SetGlobalFloat(GrassDecayDesatID, grassPreset.decayDesaturate);
            Shader.SetGlobalFloat(GrassDecayDarkenID, grassPreset.decayDarken);
        }
    }

    void OnDisable()
    {
        // SetGlobal* 在编辑器里跨 play 残留——退出 play/失活时把通路关掉，
        // 编辑态观感回到 shader 默认（enable=0 → 四级全旁路），陈旧 zone 数据不可见。
        Shader.SetGlobalFloat(GrassColorEnableID, 0f);
        Shader.SetGlobalFloat(GrassZoneCountID, 0f);
        Shader.SetGlobalFloat(GrassDroughtAmtID, 0f);
        Shader.SetGlobalFloat(GrassDecayID, 0f);
    }
}
