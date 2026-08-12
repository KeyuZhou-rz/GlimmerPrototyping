using UnityEngine;

/// <summary>
/// 环境音占位层（触感层第一批）：项目暂无草/风素材——本组件把接口先立好，
/// 运行时自动从 Resources/Audio/ 按约定命名装载（grass_rustle / gust_wind），
/// 素材丢进该目录即出声，无素材则静默不报错（Mountain 式克制：宁可无声，不要难听）。
/// 3D 定位播放：临时 AudioSource，近距可闻远距消隐（MinDistance 2 / MaxDistance 20），
/// 音量基准 0.3-0.5（雷声 1.0 是例外，不是惯例）。
/// 运行时由 DemoUIBootstrap 创建，场景零接线；未来 FMOD 事件的落点即此类。
/// 昼夜 BGM（2026-08-12）：双常驻循环源（MusicDay/MusicNight），白天度由世界日历
/// dayProgress 在日出/日落过渡带内升降，两源只调音量交叉淡化；跟世界日历，
/// 不受 WorldAtmosphereBinder.debugManualTimeControl（纯光照调试）影响。
/// </summary>
public class AmbientAudio : MonoBehaviour
{
    private static AmbientAudio _instance;

    [Header("留空则运行时从 Resources/Audio/ 按名装载；也可手动指定覆盖")]
    public AudioClip grassRustleClip;
    public AudioClip gustWindClip;
    public AudioClip MusicDay;
    public AudioClip MusicNight;

    [Header("3D 衰减（克制：近距可闻、远距消隐）")]
    public float minDistance = 2f;
    public float maxDistance = 20f;

    [Header("昼夜 BGM（日落/日出过渡带交叉淡化；读世界日历，只读）")]
    [Tooltip("背景音乐基准音量——比音效更低，宁可听不见不要盖过风声")]
    [Range(0f, 1f)] public float musicBaseVolume = 0.2f;
    [Tooltip("过渡带半宽（dayProgress 单位，0.05 ≈ 世界时 ±1.2 小时）：日落 0.75±带宽、日出 0.25±带宽内两曲此消彼长，带外钉死")]
    [Range(0.01f, 0.2f)] public float musicBandHalfWidth = 0.05f;
    [Tooltip("白天度平滑速率——世界时间跳变（缺席补跑回来）时音量也是爬过去，不瞬移")]
    public float musicSmoothingSpeed = 0.3f;

    private AudioSource _daySrc, _nightSrc;   // 双常驻循环源：同时转，只调音量
    private float _daylightWeight;            // 平滑后的白天度 [0,1]
    private bool _musicInitialized;

    void Awake()
    {
        _instance = this;
        if (grassRustleClip == null) grassRustleClip = Resources.Load<AudioClip>("Audio/grass_rustle");
        if (grassRustleClip == null) grassRustleClip = Resources.Load<AudioClip>("Audio/Grass");
        if (gustWindClip   == null) gustWindClip   = Resources.Load<AudioClip>("Audio/gust_wind");
        if (MusicDay       == null) MusicDay       = Resources.Load<AudioClip>("Audio/music_day");
        if (MusicNight     == null) MusicNight     = Resources.Load<AudioClip>("Audio/music_night");
        _daySrc   = EnsureMusicSource(MusicDay,   "bgm_day");
        _nightSrc = EnsureMusicSource(MusicNight, "bgm_night");
    }

    /// <summary>建一个 2D 循环源，音量 0 起播（双源同转，交叉靠调音量）。clip 为空返回 null=静默。</summary>
    private AudioSource EnsureMusicSource(AudioClip clip, string name)
    {
        if (clip == null) return null;
        var go = new GameObject(name);
        go.transform.SetParent(transform, false);
        var src = go.AddComponent<AudioSource>();
        src.clip = clip;
        src.loop = true;
        src.spatialBlend = 0f;    // 背景音乐无方位
        src.volume = 0f;
        src.Play();
        return src;
    }

    void Update()
    {
        if (_daySrc == null && _nightSrc == null) return;
        var wm = WorldManager.Instance;
        if (wm == null) return;
        var rhythm = wm.GetRhythmState();
        if (rhythm == null) return;

        // 白天度：日出带升、日落带降、带外钉死——日落是一个过程，不是一次换台
        float t = rhythm.dayProgress;
        float b = musicBandHalfWidth;
        float rise = Mathf.InverseLerp(0.25f - b, 0.25f + b, t);
        float fall = Mathf.InverseLerp(0.75f - b, 0.75f + b, t);
        float target = rise * (1f - fall);

        if (!_musicInitialized)
        {
            _daylightWeight = target;   // 首帧对齐，不从静默慢爬到当前时刻
            _musicInitialized = true;
        }
        else
        {
            float k = 1f - Mathf.Exp(-musicSmoothingSpeed * Time.deltaTime);
            _daylightWeight = Mathf.Lerp(_daylightWeight, target, k);
        }

        if (_daySrc   != null) _daySrc.volume   = _daylightWeight * musicBaseVolume;
        if (_nightSrc != null) _nightSrc.volume = (1f - _daylightWeight) * musicBaseVolume;
    }

    /// <summary>点草簌簌声（无素材静默）。</summary>
    public static void PlayGrassRustle(Vector3 pos, float volume = 0.4f)
        => PlayAt(pos, _instance != null ? _instance.grassRustleClip : null, volume);

    /// <summary>阵风滚过声（无素材静默）。</summary>
    public static void PlayGust(Vector3 pos, float volume = 0.35f)
        => PlayAt(pos, _instance != null ? _instance.gustWindClip : null, volume);

    /// <summary>在指定世界位置播一次 3D 音效，播完自动销毁。clip 为空静默。</summary>
    public static void PlayAt(Vector3 pos, AudioClip clip, float volume)
    {
        if (clip == null || _instance == null) return;
        var go = new GameObject($"sfx_{clip.name}");
        go.transform.position = pos;
        var src = go.AddComponent<AudioSource>();
        src.clip = clip;
        // Override the legacy 3D setup above: touch feedback is a direct 2D cue.
        src.spatialBlend = 0f;
        src.PlayOneShot(clip, volume);
        Destroy(go, clip.length + 0.1f);
    }
}
