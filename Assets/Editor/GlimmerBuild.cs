using System;
using System.Linq;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;

// Demo 收口构建与无头验证（CLI 闭环，demo 阶段专用）
//
//   Unity.exe -batchmode -quit -projectPath <proj> -executeMethod GlimmerBuild.RunSmoke     -logFile smoke.log
//   Unity.exe -batchmode -quit -projectPath <proj> -executeMethod GlimmerBuild.BuildWindows -logFile build.log
//
// RunSmoke：顺序跑 AnimalDriveSmokeTest 的静态冒烟（编辑态纯逻辑，不依赖
// play/场景组件），数日志里的 [FAIL]，任何 FAIL 或异常 → 退出码 1。
// BuildWindows：场景取自 EditorBuildSettings（不硬编码），产物 Builds/Windows/。
public static class GlimmerBuild
{
    public static void RunSmoke()
    {
        var tests = new (string name, Action run)[]
        {
            ("AnxietyChain",   GlimmerDiary.Editor.AnimalDriveSmokeTest.RunEmergentAnxietyChain),
            ("WeaverChain",    GlimmerDiary.Editor.AnimalDriveSmokeTest.RunWeaverChain),
            ("PipelineFlood",  GlimmerDiary.Editor.AnimalDriveSmokeTest.RunFullPipelineFlood),
            ("LongRunHealth",  GlimmerDiary.Editor.AnimalDriveSmokeTest.RunLongRunHealth),
            ("PipelineBird",   GlimmerDiary.Editor.AnimalDriveSmokeTest.RunFullPipelineBird),
            ("SilentAdvance",  GlimmerDiary.Editor.AnimalDriveSmokeTest.RunSilentAdvance30),
            ("QCDeterministic",GlimmerDiary.Editor.AnimalDriveSmokeTest.RunQuietConvergence_Deterministic),
            ("QCOrganic",      GlimmerDiary.Editor.AnimalDriveSmokeTest.RunQuietConvergence_Organic),
            ("QCRate",         GlimmerDiary.Editor.AnimalDriveSmokeTest.RunQuietConvergence_Rate),
            ("VegetationPest", GlimmerDiary.Editor.AnimalDriveSmokeTest.RunVegetationPest),
            ("SoilMoisture",   GlimmerDiary.Editor.AnimalDriveSmokeTest.RunSoilMoisture),
            ("Translation",    GlimmerDiary.Editor.AnimalDriveSmokeTest.RunTranslationLayer),
            ("EraClock",       GlimmerDiary.Editor.AnimalDriveSmokeTest.RunEraClock),
            ("EraSuspended",   GlimmerDiary.Editor.AnimalDriveSmokeTest.RunEraClockSuspended),
            ("EnvPersistence", GlimmerDiary.Editor.AnimalDriveSmokeTest.RunEnvironmentPersistence),
            ("VoleTown",       GlimmerDiary.Editor.AnimalDriveSmokeTest.RunVoleTown),
            ("AbsenceTone",    GlimmerDiary.Editor.AnimalDriveSmokeTest.RunAbsenceChapterTone),
            ("Strata",         GlimmerDiary.Editor.AnimalDriveSmokeTest.RunStrata),
            ("Exposure",       GlimmerDiary.Editor.AnimalDriveSmokeTest.RunExposure),
            ("Witnessed",      GlimmerDiary.Editor.AnimalDriveSmokeTest.RunWitnessed),
            ("Wings",          GlimmerDiary.Editor.AnimalDriveSmokeTest.RunWings),
            ("WingTraces",     GlimmerDiary.Editor.AnimalDriveSmokeTest.RunWingTraces),
            ("NoticingCaptions", GlimmerDiary.Editor.AnimalDriveSmokeTest.RunNoticingCaptions),
            ("DeepRelics",     GlimmerDiary.Editor.AnimalDriveSmokeTest.RunDeepRelics),
        };

        int fails = 0, exceptions = 0;
        Application.logMessageReceived += OnLog;
        try
        {
            foreach (var (name, run) in tests)
            {
                Debug.Log($"[SMOKE] ── {name} ──");
                try { run(); }
                catch (Exception ex)
                {
                    exceptions++;
                    Debug.LogError($"[SMOKE] {name} 抛异常: {ex.Message}");
                }
            }
        }
        finally
        {
            Application.logMessageReceived -= OnLog;
        }

        fails = _failCount;
        Debug.Log($"[SMOKE] 汇总: {tests.Length} 项跑完，断言 FAIL={fails}，异常={exceptions}");
        if (fails > 0 || exceptions > 0)
            EditorApplication.Exit(1);
    }

    private static int _failCount;
    private static void OnLog(string condition, string stackTrace, LogType type)
    {
        if (condition != null && condition.Contains("[FAIL]")) _failCount++;
    }

    public static void BuildWindows()
    {
        var scenes = EditorBuildSettings.scenes
            .Where(s => s.enabled).Select(s => s.path).ToArray();

        var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
        {
            scenes           = scenes,
            locationPathName = "Builds/Windows/Glimmer_Prototyping.exe",
            target           = BuildTarget.StandaloneWindows64,
            options          = BuildOptions.None
        });

        Debug.Log($"[BUILD] result={report.summary.result} " +
                  $"size={report.summary.totalSize / (1024 * 1024)}MB " +
                  $"time={report.summary.totalTime.TotalMinutes:F1}min " +
                  $"errors={report.summary.totalErrors}");
        if (report.summary.result != BuildResult.Succeeded)
            EditorApplication.Exit(1);
    }
}
