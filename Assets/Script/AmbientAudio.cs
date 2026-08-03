using UnityEngine;

/// <summary>
/// 环境音占位层（触感层第一批）：项目暂无草/风素材——本组件把接口先立好，
/// 运行时自动从 Resources/Audio/ 按约定命名装载（grass_rustle / gust_wind），
/// 素材丢进该目录即出声，无素材则静默不报错（Mountain 式克制：宁可无声，不要难听）。
/// 3D 定位播放：临时 AudioSource，近距可闻远距消隐（MinDistance 2 / MaxDistance 20），
/// 音量基准 0.3-0.5（雷声 1.0 是例外，不是惯例）。
/// 运行时由 DemoUIBootstrap 创建，场景零接线；未来 FMOD 事件的落点即此类。
/// </summary>
public class AmbientAudio : MonoBehaviour
{
    private static AmbientAudio _instance;

    [Header("留空则运行时从 Resources/Audio/ 按名装载；也可手动指定覆盖")]
    public AudioClip grassRustleClip;
    public AudioClip gustWindClip;

    [Header("3D 衰减（克制：近距可闻、远距消隐）")]
    public float minDistance = 2f;
    public float maxDistance = 20f;

    void Awake()
    {
        _instance = this;
        if (grassRustleClip == null) grassRustleClip = Resources.Load<AudioClip>("Audio/grass_rustle");
        if (gustWindClip   == null) gustWindClip   = Resources.Load<AudioClip>("Audio/gust_wind");
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
        src.spatialBlend = 1f;                    // 全 3D
        src.rolloffMode = AudioRolloffMode.Logarithmic;
        src.minDistance = _instance.minDistance;
        src.maxDistance = _instance.maxDistance;
        src.PlayOneShot(clip, volume);
        Destroy(go, clip.length + 0.1f);
    }
}
