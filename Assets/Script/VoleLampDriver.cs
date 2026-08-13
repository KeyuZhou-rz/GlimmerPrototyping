using UnityEngine;

/// <summary>
/// 田鼠灯昼夜驱动（Layer 3，2026-08-13）。
/// 玩家看到的：黄昏太阳贴地平线的几分钟里，路边一点暖光慢慢亮起来；黎明再慢慢熄下去。
/// 激烈天气的夜里灯光会被压暗（与星星被风暴藏起同逻辑）。
///
/// 纪律：
///   只读 —— WorldTraceBinder.ActiveVoleLamps/ElderLamps（灯句柄）+ LightManager.CurrentSunlight01/weatherDim。
///   只写 —— 每盏灯的 Light.intensity 与灯珠 MPB；不碰任何世界状态，不落档。
///   爬不能跳 —— 点亮/熄灭是日照的连续函数（SmoothStep），呼吸慢起伏；
///              不做萤火虫式飞舞/断闪——灯是造物不是活物（无痕原则⑤）。
///   两种灯 —— 镇灯（暖琥珀，呼吸 ±8%/周期~4s）与原在灯 elder（冷月白、更暗、呼吸 ±14%/周期~10s）。
/// 布线：由 WorldTraceBinder.Awake 自动挂同 GameObject，场景里无需（也不要）手挂。
/// </summary>
[RequireComponent(typeof(WorldTraceBinder))]
public class VoleLampDriver : MonoBehaviour
{
    private WorldTraceBinder _binder;
    private LightManager _lightManager;
    private MaterialPropertyBlock _mpb;

    private static readonly int EmissionColorId = Shader.PropertyToID("_EmissionColor");
    private static readonly int BaseColorId     = Shader.PropertyToID("_BaseColor");

    void Awake()
    {
        _binder = GetComponent<WorldTraceBinder>();
        _mpb = new MaterialPropertyBlock();
    }

    void LateUpdate()
    {
        if (_binder == null) return;
        int total = _binder.ActiveVoleLamps.Count + _binder.ElderLamps.Count;
        if (total == 0) return;
        if (_lightManager == null)
        {
            _lightManager = FindFirstObjectByType<LightManager>();
            if (_lightManager == null) return;
        }

        // 夜晚因子：日照 0.10 → 0 之间 SmoothStep 爬升——黄昏数分钟渐亮，黎明反向渐灭，全程连续。
        float night = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.10f, 0f, _lightManager.CurrentSunlight01));
        // 暴雨夜灯减弱（与 EWC stormStarHide 藏星同逻辑）；weatherDim 是坏天气压光因子 0..1
        float weather = 1f - _lightManager.weatherDim * 0.7f;
        float baseLevel = night * weather;

        foreach (var lamp in _binder.ActiveVoleLamps) Drive(lamp, night, baseLevel);
        foreach (var lamp in _binder.ElderLamps) Drive(lamp, night, baseLevel);
    }

    private void Drive(WorldTraceBinder.VoleLamp lamp, float night, float baseLevel)
    {
        if (lamp.light == null || lamp.bead == null) return;   // 痕迹已销毁的残句柄（正常不发生，防御）

        // 原在灯（elder）：更冷更暗更慢——颜色在建灯时已定（冷月白），这里走另一套强度/呼吸参数
        float intensity   = lamp.elder ? _binder.elderIntensity   : _binder.lampIntensity;
        float beadHdr     = lamp.elder ? _binder.elderBeadHdr     : _binder.lampBeadHdr;
        Color emission    = lamp.elder ? _binder.elderBeadEmission : _binder.lampBeadEmission;
        float breathAmp   = lamp.elder ? _binder.elderBreathAmp   : 0.08f;
        float breathSpeed = lamp.elder ? _binder.elderBreathSpeed : 1.5f;

        // 新灯 2 秒淡入（与痕迹展示层淡入同口径）；呼吸相位按种子错开不齐闪
        float fadeIn = Mathf.Clamp01((Time.time - lamp.spawnRealTime) / 2f);
        float breath = 1f + breathAmp * Mathf.Sin(Time.time * breathSpeed + lamp.phase);
        float level = baseLevel * fadeIn * breath;

        lamp.light.intensity = intensity * level;

        // 灯珠：夜里 HDR 吃 Bloom（阈值 1.0）；白天压暗褐，读作熄灭的小造物而非光点
        _mpb.SetColor(EmissionColorId, emission * (beadHdr * level));
        _mpb.SetColor(BaseColorId, Color.Lerp(_binder.lampPostTint, emission, night * 0.35f));
        lamp.bead.SetPropertyBlock(_mpb);
    }
}
