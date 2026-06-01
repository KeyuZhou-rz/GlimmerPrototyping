// 临时验证脚本 — 确认 JSON 存档正确写入后删除
using System.IO;
using UnityEngine;
using GlimmerDiary.Core;
using GlimmerDiary.Data;
using GlimmerDiary.Utils;

public class SaveSystemTester : MonoBehaviour
{
    void Start()
    {
        Debug.Log("=== [SaveSystemTester] 开始测试 ===");

        // 清除上次测试残留，确保追加计数从 0 开始
        string dir = SaveSystem.GetSaveDir();
        string worldPath   = Path.Combine(dir, "world_state.json");
        string journalPath = Path.Combine(dir, "journal_log.json");
        if (File.Exists(worldPath))   File.Delete(worldPath);
        if (File.Exists(journalPath)) File.Delete(journalPath);
        Debug.Log($"[SaveTest] SaveDir: {dir}");

        // 测试 1 — 提交三条模拟日记，验证 E_env 逐步漂移
        Submit("T001", "今天阳光很好，心情不错。",          v:  0.4f, a: 0.5f, t: 0.6f, s: 0.3f, c: 0.7f);
        Submit("T002", "有些疲惫，不想说话。",              v: -0.3f, a: 0.2f, t: 0.5f, s: 0.1f, c: 0.4f);
        Submit("T003", "开始期待明天，感觉会更好。",         v:  0.6f, a: 0.7f, t: 0.8f, s: 0.4f, c: 0.8f);

        VerifyWorldState(expectedHistoryCount: 3);
        VerifyJournalLog(expectedEntryCount: 3);
        VerifyRestore();

        Debug.Log("=== [SaveSystemTester] 测试结束 ===");
    }

    // ── 提交 ──────────────────────────────────────────────
    void Submit(string id, string text, float v, float a, float t, float s, float c)
    {
        var entry = new JournalEntry
        {
            entryId       = id,
            realTimestamp = System.DateTime.Now.ToString("o"),
            rawText       = text,
            emotion       = new EmotionVector { V = v, A = a, T = t, S = s, C = c }
        };
        WorldManager.Instance.OnJournalSubmitted(entry);
        Debug.Log($"[SaveTest] Submitted {id}: \"{text}\"");
    }

    // ── 验证 world_state.json ─────────────────────────────
    void VerifyWorldState(int expectedHistoryCount)
    {
        var data = SaveSystem.LoadWorldState();
        if (data == null)
        {
            Debug.LogError("[SaveTest] FAIL  world_state.json 未生成");
            return;
        }

        bool historyOk = data.emotionHistory != null && data.emotionHistory.Count == expectedHistoryCount;
        string status  = historyOk ? "PASS" : "FAIL";
        Debug.Log(
            $"[SaveTest] {status}  world_state.json\n" +
            $"  gameTime  = {data.gameTime?.ToKeyString()}\n" +
            $"  E_env     = V:{data.currentEEnv.V:F4}  A:{data.currentEEnv.A:F4}  " +
                            $"T:{data.currentEEnv.T:F4}  S:{data.currentEEnv.S:F4}  C:{data.currentEEnv.C:F4}\n" +
            $"  history   = {data.emotionHistory?.Count} 条（预期 {expectedHistoryCount}）"
        );
    }

    // ── 验证 journal_log.json（只追加）────────────────────
    void VerifyJournalLog(int expectedEntryCount)
    {
        var log = SaveSystem.LoadJournalLog();
        if (log == null)
        {
            Debug.LogError("[SaveTest] FAIL  journal_log.json 未生成");
            return;
        }

        bool countOk = log.entries.Count == expectedEntryCount;
        Debug.Log(
            $"[SaveTest] {(countOk ? "PASS" : "FAIL")}  journal_log.json — " +
            $"{log.entries.Count} 条（预期 {expectedEntryCount}）"
        );
        foreach (var e in log.entries)
            Debug.Log($"  → [{e.entryId}] V={e.emotion.V:+0.00;-0.00}  \"{e.rawText}\"");
    }

    // ── 验证读取 + Restore 还原 ────────────────────────────
    void VerifyRestore()
    {
        var saved = SaveSystem.LoadWorldState();
        if (saved == null) { Debug.LogError("[SaveTest] FAIL  LoadWorldState 返回 null"); return; }

        var tempSystem = new EmotionInertiaSystem();
        tempSystem.Restore(saved.currentEEnv, saved.emotionHistory);

        bool vMatch = Mathf.Abs(tempSystem.CurrentEEnv.V - saved.currentEEnv.V) < 0.0001f;
        Debug.Log(
            $"[SaveTest] {(vMatch ? "PASS" : "FAIL")}  Restore — " +
            $"E_env.V={tempSystem.CurrentEEnv.V:F4}  history={tempSystem.History.Count} 条"
        );
    }
}
