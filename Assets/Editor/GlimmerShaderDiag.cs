using UnityEditor;
using UnityEngine;

// 诊断：打印 Glimmer 着色器的编译错误（如有）
public static class GlimmerShaderDiag
{
    [MenuItem("Tools/Glimmer/Diagnose Shaders")]
    public static void Diagnose()
    {
        string[] names = { "Glimmer/Toon", "Glimmer/Terrain", "Glimmer/RainStreak", "Custom/StylizedWater" };
        foreach (var n in names)
        {
            var sh = Shader.Find(n);
            if (sh == null) { Debug.LogError($"[Diag] {n}: NOT FOUND"); continue; }

            int count = ShaderUtil.GetShaderMessageCount(sh);
            if (count == 0)
            {
                Debug.Log($"[Diag] {n}: OK, supported={ShaderUtil.ShaderHasError(sh) == false}, passCount via supported subshader");
                continue;
            }
            var msgs = ShaderUtil.GetShaderMessages(sh);
            foreach (var m in msgs)
                Debug.LogError($"[Diag] {n}: {m.severity} line {m.line}: {m.message} ({m.file})");
        }
    }
}
