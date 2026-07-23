using UnityEditor;
using UnityEngine;
using System.IO;

// Editor-only utility: captures the Scene View or Game View camera to a PNG
// under Assets/Screenshots so Claude (MCP) can inspect the current visuals.
public static class ClaudeViewCapture
{
    const string OutDir = "Assets/Screenshots";

    [MenuItem("Tools/Claude/Capture SceneView")]
    public static void CaptureSceneView()
    {
        var sv = SceneView.lastActiveSceneView;
        if (sv == null || sv.camera == null)
        {
            Debug.LogWarning("[ClaudeViewCapture] No active SceneView.");
            return;
        }
        Capture(sv.camera, Path.Combine(OutDir, "claude_sceneview.png"), 1280, 720);
    }

    // 3/4 overview: spawn a temp camera at an explicit vantage point over the
    // terrain — avoids SceneView camera state entirely, so captures are repeatable.
    [MenuItem("Tools/Claude/Capture Overview")]
    public static void CaptureOverview()
    {
        var terrain = Object.FindFirstObjectByType<TerrainGenerator>();
        Vector3 center = terrain != null ? terrain.transform.position : Vector3.zero;
        float ext = terrain != null ? terrain.width * terrain.scale * 0.5f : 80f;

        var rot = Quaternion.Euler(30f, 225f, 0f);
        Vector3 eye = center + new Vector3(0f, 4f, 0f) - rot * Vector3.forward * (ext * 2.1f);

        var go = new GameObject("~ClaudeTempCam") { hideFlags = HideFlags.HideAndDontSave };
        try
        {
            var cam = go.AddComponent<Camera>();
            cam.transform.SetPositionAndRotation(eye, rot);
            cam.clearFlags = CameraClearFlags.Skybox;
            cam.fieldOfView = 50f;
            cam.nearClipPlane = 0.3f;
            cam.farClipPlane = 2000f;
            Capture(cam, Path.Combine(OutDir, "claude_overview.png"), 1280, 720);
        }
        finally
        {
            Object.DestroyImmediate(go);
        }
    }


    [MenuItem("Tools/Claude/Capture MainCamera")]
    public static void CaptureMainCamera()
    {
        var cam = Camera.main;
        if (cam == null)
        {
            Debug.LogWarning("[ClaudeViewCapture] No MainCamera in scene.");
            return;
        }
        // 用干净的临时相机复制主相机位姿拍摄：手动 cam.Render() 对带
        // URP Additional Data 的相机不可靠（后处理/深度预通道状态不完整）。
        var go = new GameObject("~ClaudeTempCam") { hideFlags = HideFlags.HideAndDontSave };
        try
        {
            var tmp = go.AddComponent<Camera>();
            tmp.CopyFrom(cam);
            tmp.targetTexture = null;
            Capture(tmp, Path.Combine(OutDir, "claude_maincam.png"), 1280, 720);
        }
        finally
        {
            Object.DestroyImmediate(go);
        }
    }

    // 失焦不走帧环境下的帧步进验证：EditorApplication.Step() 手动推进 N 帧
    // （play 模式下 Update/LateUpdate 真实执行），再对指定物体拍摄。
    // 用途示例：种子絮链路 binder→EWC.UpdateSeedFluff 需要若干帧才生效。
    [MenuItem("Tools/Claude/Step + Capture Fluff")]
    public static void StepAndCaptureFluff()
    {
        if (!Application.isPlaying)
        {
            Debug.LogWarning("[ClaudeViewCapture] Step + Capture Fluff 需要在 play 模式。");
            return;
        }
        for (int i = 0; i < 10; i++) EditorApplication.Step();

        var go2 = GameObject.Find("SeedFluffSystem");
        var ps = go2 != null ? go2.GetComponent<ParticleSystem>() : null;
        Vector3 focus = ps != null ? ps.transform.position : Vector3.zero;
        if (ps != null)
        {
            Debug.Log($"[ClaudeViewCapture] fluff: rate={ps.emission.rateOverTime.constant:F1} playing={ps.isPlaying}");
            ps.Simulate(4f, true, false);   // 快进铺满（restart=false 保留驱动状态）
            ps.Play(true);
        }

        var go = new GameObject("~ClaudeTempCam") { hideFlags = HideFlags.HideAndDontSave };
        try
        {
            var cam = go.AddComponent<Camera>();
            Vector3 eye = focus + new Vector3(-7f, 3f, 6f);
            cam.transform.SetPositionAndRotation(eye, Quaternion.LookRotation(focus - eye, Vector3.up));
            cam.clearFlags = CameraClearFlags.Skybox;
            cam.fieldOfView = 55f;
            cam.nearClipPlane = 0.3f;
            cam.farClipPlane = 2000f;
            Capture(cam, Path.Combine(OutDir, "claude_fluff.png"), 1280, 720);
        }
        finally
        {
            Object.DestroyImmediate(go);
        }
    }

    static void Capture(Camera cam, string path, int w, int h)
    {
        Directory.CreateDirectory(OutDir);

        var rt = new RenderTexture(w, h, 24, RenderTextureFormat.ARGB32);
        var prevTarget = cam.targetTexture;
        var prevActive = RenderTexture.active;

        try
        {
            cam.targetTexture = rt;
            cam.Render();

            RenderTexture.active = rt;
            var tex = new Texture2D(w, h, TextureFormat.RGB24, false);
            tex.ReadPixels(new Rect(0, 0, w, h), 0, 0);
            tex.Apply();

            File.WriteAllBytes(path, tex.EncodeToPNG());
            Object.DestroyImmediate(tex);
            Debug.Log($"[ClaudeViewCapture] Saved {path}");
        }
        finally
        {
            cam.targetTexture = prevTarget;
            RenderTexture.active = prevActive;
            rt.Release();
            Object.DestroyImmediate(rt);
            AssetDatabase.Refresh();
        }
    }
}
