using UnityEditor;
using UnityEngine;
using System.IO;

// Play Mode 天气验证：进入播放后把天气拉到指定状态，跑几秒后从主相机截图退出。
// 进入播放会触发域重载，静态字段/事件订阅全部清零 —— 因此用 SessionState 传递
// 任务，[InitializeOnLoadMethod] 在重载后重新挂钩。
public static class GlimmerPlaytest
{
    const string KeyMode = "Glimmer.Playtest.Mode";     // "" | "rain" | "clear"
    const string KeyDeadline = "Glimmer.Playtest.Deadline";

    [MenuItem("Tools/Glimmer/Playtest Rain Capture")]
    public static void PlaytestRain() => Begin("rain");

    [MenuItem("Tools/Glimmer/Playtest Clear Capture")]
    public static void PlaytestClear() => Begin("clear");

    static void Begin(string mode)
    {
        if (EditorApplication.isPlaying) { Debug.LogWarning("Already playing"); return; }
        SessionState.SetString(KeyMode, mode);
        EditorApplication.isPlaying = true;
    }

    [InitializeOnLoadMethod]
    static void Hook()
    {
        EditorApplication.playModeStateChanged += s =>
        {
            if (s != PlayModeStateChange.EnteredPlayMode) return;
            var mode = SessionState.GetString(KeyMode, "");
            if (string.IsNullOrEmpty(mode)) return;

            var wc = Object.FindFirstObjectByType<EmotionWeatherController>();
            var binder = Object.FindFirstObjectByType<WorldAtmosphereBinder>();
            if (binder != null) binder.enabled = false;   // 脱开世界状态驱动，手动设天气
            // 播放验证时停掉自动模拟测试器与昼夜驱动，锁定预览光照看风格
            foreach (var t in Object.FindObjectsByType<MonoBehaviour>(FindObjectsSortMode.None))
            {
                var n = t.GetType().Name;
                if (n == "WorldSimulationTester" || n == "LightManager") t.enabled = false;
            }
            if (mode == "rain") GlimmerVisualSetup.PreviewStorm();
            else                GlimmerVisualSetup.PreviewGoldenHour();

            if (wc != null)
            {
                if (mode == "rain")
                {
                    wc.rainIntensity = -1f;   // 暴雨
                    wc.windIntensity = 0.7f;
                    wc.dimness = 0.6f;
                }
                else
                {
                    wc.rainIntensity = 0.8f;  // 晴
                    wc.windIntensity = 0.15f;
                    wc.dimness = 0f;
                }
                wc.transitionSpeed = 10f;     // 快速到位，缩短等待
            }

            SessionState.SetFloat(KeyDeadline, (float)EditorApplication.timeSinceStartup + 6f);
            EditorApplication.update += Tick;
        };
    }

    static void Tick()
    {
        if (!EditorApplication.isPlaying) { EditorApplication.update -= Tick; return; }
        if (EditorApplication.timeSinceStartup < SessionState.GetFloat(KeyDeadline, 0f)) return;
        EditorApplication.update -= Tick;

        var mode = SessionState.GetString(KeyMode, "");
        SessionState.SetString(KeyMode, "");

        // 编辑器失焦时播放循环几乎不走帧（诊断: 6 秒仅 1 粒子、雾停在晴天值）。
        // 不依赖实时过渡：反射收敛私有平滑场 → 手动跑一次 Update 写雾/发射参数
        // → Simulate 快进雨幕。截图状态与聚焦运行数秒后一致。
        var wc = Object.FindFirstObjectByType<EmotionWeatherController>();
        if (wc != null)
        {
            var t = typeof(EmotionWeatherController);
            const System.Reflection.BindingFlags F =
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance;
            t.GetField("smoothedRainIntensity", F)?.SetValue(wc, Mathf.Clamp01(-wc.rainIntensity));
            t.GetField("smoothedSunIntensity",  F)?.SetValue(wc, Mathf.Clamp01(wc.rainIntensity));
            t.GetField("currentWind",           F)?.SetValue(wc, wc.windIntensity);

            wc.SendMessage("Update");   // 用收敛后的平滑值写雾与粒子参数（dt≈0 不再位移）

            var ps = wc.rainParticleSystem;
            if (ps != null && Mathf.Clamp01(-wc.rainIntensity) > 0.01f)
            {
                if (!ps.isPlaying) ps.Play(true);
                ps.Simulate(6f, true, true);   // 从头快进 6 秒，雨幕铺满视野
                ps.Play(true);                 // 恢复播放状态（Simulate 会暂停）
            }
        }

        // 诊断：确认天气链路真的驱动到了渲染层
        var psD = wc != null ? wc.rainParticleSystem : null;
        Debug.Log($"[GlimmerPlaytest] diag mode={mode} rainIntensity={(wc ? wc.rainIntensity : 99f):F2} " +
                  $"fog={RenderSettings.fog} fogEnd={RenderSettings.fogEndDistance:F0} " +
                  $"particles={(psD ? psD.particleCount : -1)} emitterPos={(psD ? psD.transform.position.ToString() : "null")}");

        var cam = Camera.main;
        if (cam != null && !string.IsNullOrEmpty(mode))
        {
            string outName = mode == "rain" ? "claude_play_rain.png" : "claude_play_clear.png";
            Directory.CreateDirectory("Assets/Screenshots");
            // ScreenCapture 抓真实 Game 视图（含后处理/AA）——验证模糊问题必须走这条路，
            // 临时相机 cam.Render() 会绕过 URP 后处理，看不到用户实际看到的画面。
            ScreenCapture.CaptureScreenshot(Path.Combine("Assets/Screenshots", outName));
            SessionState.SetString(KeyPendingShot, outName);
            SessionState.SetFloat(KeyShotDeadline, (float)EditorApplication.timeSinceStartup + 3f);
            EditorApplication.update += WaitShotThenExit;
            return;   // 截图异步落盘，等它写完再退出播放
        }

        EditorApplication.isPlaying = false;
        AssetDatabase.Refresh();
    }

    const string KeyPendingShot = "Glimmer.Playtest.PendingShot";
    const string KeyShotDeadline = "Glimmer.Playtest.ShotDeadline";

    static void WaitShotThenExit()
    {
        string shot = SessionState.GetString(KeyPendingShot, "");
        bool timeout = EditorApplication.timeSinceStartup > SessionState.GetFloat(KeyShotDeadline, 0f);
        bool exists = !string.IsNullOrEmpty(shot) && File.Exists(Path.Combine("Assets/Screenshots", shot));
        if (!exists && !timeout) return;

        EditorApplication.update -= WaitShotThenExit;
        SessionState.SetString(KeyPendingShot, "");
        if (exists) Debug.Log($"[GlimmerPlaytest] Saved {shot}");
        else Debug.LogWarning("[GlimmerPlaytest] Screenshot timed out");
        EditorApplication.isPlaying = false;
        AssetDatabase.Refresh();
    }
}
