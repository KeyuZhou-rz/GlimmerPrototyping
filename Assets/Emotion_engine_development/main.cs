using GlimmerDiary.Diary;
using UnityEngine;
public class Main : MonoBehaviour
{
    private MyData mydata;
    void Start()
    {
        mydata = new MyData();
        mydata.Load();
        Testcode(mydata);
    }
    public void Testcode(MyData myData)
    {
        myData.Load();

        // ── Verification cases from design doc ────────────────────────────────
        TestAnalyze("今天很开心！");                 // expect valence≈0.8, arousal≈0.6
        TestAnalyze("I feel so sad and tired.");   // expect valence≈-0.7, arousal≈0.2
        TestAnalyze("Not bad, feeling okay.");     // expect valence≈0.1~0.3 (negation)
        TestAnalyze("我很愤怒，非常焦虑！");          // expect valence≈-0.8, arousal≈0.8
        TestAnalyze("Hello world");                // expect default (0, 0.2)

        // ── Full pipeline test (add → analyze → update → remove) ─────────────
        var entry = DiaryEntry.Create("今天很开心，遇到了很多有趣的事情！");
        myData.AddEntry(entry);
        Debug.Log("Entry added.");

        entry = EmotionAnalyzer.AnalyzeEntry(entry);
        myData.UpdateEntry(entry);
        Debug.Log($"Analyzed — Valence: {entry.valence:F2}, Arousal: {entry.arousal:F2}, isAnalyzed: {entry.isAnalyzed}");

        myData.RemoveEntry(entry.id);
        Debug.Log("Entry removed.");
    }

    private static void TestAnalyze(string text)
    {
        var (v, a) = EmotionAnalyzer.Analyze(text);
        Debug.Log($"[EmotionAnalyzer] \"{text}\" → valence={v:F2}, arousal={a:F2}");
    }
}