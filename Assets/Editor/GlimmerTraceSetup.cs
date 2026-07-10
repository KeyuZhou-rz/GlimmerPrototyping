using UnityEditor;
using UnityEngine;

/// <summary>
/// 痕迹系统一键接线（痕迹对齐轮）。
/// 执行顺序约定：Setup Visual Style → Scatter Terrain Decor → Setup Grass → Setup World Traces。
///   · 幂等烘焙 4 个痕迹材质（Glimmer/Toon 无贴图；运行时 MPB 着色）
///   · find-or-create "WorldTraces" 节点：ZoneMap + WorldTraceBinder 接线
///   · 按地形参数自动放 5 个 zone 锚点；落点撞山裙/水面时向中心 nudge 并 log
///   · 建 "Anchor_center" 占位子物体（将来手摆主树对齐它）
///   · Debug Trample Point 菜单：编辑态往草上打一个测试压痕点（M2 验证用）
/// </summary>
public static class GlimmerTraceSetup
{
    const string RootName = "WorldTraces";
    const string MatDir = "Assets/Materials/Glimmer";

    static readonly (string name, string field, Color color)[] TraceMats =
    {
        ("Trace_Dirt_Glimmer",    "dirtMaterial",    new Color(0.40f, 0.32f, 0.24f)),
        ("Trace_Feather_Glimmer", "featherMaterial", new Color(0.88f, 0.86f, 0.78f)),
        ("Trace_Mark_Glimmer",    "markMaterial",    new Color(0.30f, 0.24f, 0.19f)),
        ("Trace_Pressed_Glimmer", "pressedMaterial", new Color(0.70f, 0.66f, 0.45f)),
    };

    [MenuItem("Tools/Glimmer/Setup World Traces")]
    public static void Setup()
    {
        var tg = Object.FindFirstObjectByType<TerrainGenerator>();
        if (tg == null) { Debug.LogError("[TraceSetup] No TerrainGenerator in scene"); return; }
        var col = tg.GetComponent<MeshCollider>();
        if (col == null || col.sharedMesh == null)
        {
            Debug.LogError("[TraceSetup] Terrain MeshCollider missing/empty — run Setup Visual Style first");
            return;
        }

        // ---- 材质烘焙（load-if-exists，幂等） ----
        var shader = Shader.Find("Glimmer/Toon");
        if (shader == null) { Debug.LogError("[TraceSetup] Glimmer/Toon shader not found"); return; }

        var mats = new Material[TraceMats.Length];
        for (int i = 0; i < TraceMats.Length; i++)
        {
            string path = $"{MatDir}/{TraceMats[i].name}.mat";
            var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (mat == null)
            {
                mat = new Material(shader);
                AssetDatabase.CreateAsset(mat, path);
            }
            mat.shader = shader;
            mat.SetColor("_BaseColor", TraceMats[i].color);
            mat.SetFloat("_ShadeBands", 3f);
            mat.SetFloat("_Posterize", 0.6f);
            mat.SetFloat("_AmbientBoost", 1.1f);   // 贴地小件背光面别读成黑块（同岩石思路）
            mat.SetColor("_ShadowTint", new Color(0.34f, 0.40f, 0.50f));
            mat.SetFloat("_RimStrength", 0.05f);
            mat.SetFloat("_RimPower", 3.5f);
            mat.SetFloat("_SwayAmount", 0f);       // 痕迹绝不摆动
            EditorUtility.SetDirty(mat);
            mats[i] = mat;
        }

        // ---- WorldTraces 节点接线 ----
        var go = GameObject.Find(RootName);
        if (go == null) go = new GameObject(RootName);

        var zoneMap = go.GetComponent<ZoneMap>();
        if (zoneMap == null) zoneMap = go.AddComponent<ZoneMap>();
        zoneMap.terrain = tg;
        zoneMap.anchors = ZoneMap.DefaultAnchors();
        ValidateAnchors(zoneMap, tg, col);

        var binder = go.GetComponent<WorldTraceBinder>();
        if (binder == null) binder = go.AddComponent<WorldTraceBinder>();
        binder.zoneMap = zoneMap;
        binder.dirtMaterial    = mats[0];
        binder.featherMaterial = mats[1];
        binder.markMaterial    = mats[2];
        binder.pressedMaterial = mats[3];

        // ---- center 锚点占位（将来手摆主树对齐它） ----
        // 位置直接按序列化锚点表推——不能用 TryGetAnchorCenter，它会优先读到这个子物体本身。
        // 已手摆过（不在父物体原点）则不动；仍在原点视为从未定位，重新落位。
        var anchor = go.transform.Find("Anchor_center");
        if (anchor == null)
        {
            anchor = new GameObject("Anchor_center").transform;
            anchor.SetParent(go.transform, false);
        }
        // XZ 都在原点 = 从未定位（含上一版把它错落在 (0,y,0) 的情况）
        if (Mathf.Abs(anchor.localPosition.x) < 0.01f && Mathf.Abs(anchor.localPosition.z) < 0.01f)
        {
            foreach (var a in zoneMap.anchors)
            {
                if (a.zoneId != "center") continue;
                Vector3 w = zoneMap.NormalizedToWorldXZ(a.nx, a.nz);
                var ray = new Ray(new Vector3(w.x, 200f, w.z), Vector3.down);
                anchor.position = col.Raycast(ray, out RaycastHit hit, 400f) ? hit.point : w;
                break;
            }
        }

        EditorUtility.SetDirty(go);
        AssetDatabase.SaveAssets();
        UnityEditor.SceneManagement.EditorSceneManager.MarkAllScenesDirty();
        Debug.Log("[TraceSetup] WorldTraces wired (ZoneMap + WorldTraceBinder). " +
                  "痕迹在 play 模式随世界状态出现；编辑态无压痕是正确行为（无活世界）。");
    }

    [MenuItem("Tools/Glimmer/Clear World Traces")]
    public static void Clear()
    {
        var old = GameObject.Find(RootName);
        if (old != null)
        {
            Object.DestroyImmediate(old);
            UnityEditor.SceneManagement.EditorSceneManager.MarkAllScenesDirty();
            Debug.Log("[TraceSetup] Cleared");
        }
        Shader.SetGlobalFloat("_TrampleCount", 0f);
    }

    // ---- M2 验证辅助：编辑态打一个测试压痕点（优先 Anchor_center，便于取景） ----
    [MenuItem("Tools/Glimmer/Debug Trample Point (scene center)")]
    public static void DebugTramplePoint()
    {
        var anchor = GameObject.Find("Anchor_center");
        var tg = Object.FindFirstObjectByType<TerrainGenerator>();
        Vector3 p = anchor != null ? anchor.transform.position
                  : tg != null ? tg.transform.position : Vector3.zero;
        var arr = new Vector4[16];
        arr[0] = new Vector4(p.x, p.z, 3f, 1f);
        Shader.SetGlobalVectorArray("_TramplePoints", arr);
        Shader.SetGlobalFloat("_TrampleCount", 1f);
        SceneView.RepaintAll();
        Debug.Log($"[TraceSetup] Debug trample point at ({p.x:F1}, {p.z:F1}) r=3 s=1 — " +
                  "run 'Clear Debug Trample' to remove");
    }

    [MenuItem("Tools/Glimmer/Clear Debug Trample")]
    public static void ClearDebugTrample()
    {
        Shader.SetGlobalFloat("_TrampleCount", 0f);
        SceneView.RepaintAll();
        Debug.Log("[TraceSetup] Debug trample cleared");
    }

    /// <summary>
    /// 锚点体检：riverbank 按实际河道曲线贴水缘定位（东岸，语料"河岸"），
    /// 其余锚点落地失败/贴河/坡太陡时向地图中心 nudge 直到合格，写回并 log。
    /// </summary>
    static void ValidateAnchors(ZoneMap zoneMap, TerrainGenerator tg, MeshCollider col)
    {
        foreach (var a in zoneMap.anchors)
        {
            float nx = a.nx, nz = a.nz;

            // riverbank：水缘外侧一小段（河道中心 + 半宽 + 岸带 + 余量）
            if (a.zoneId == "riverbank" && tg.enableRiver)
                nx = tg.RiverCurve(nz) + tg.riverWidth * 0.5f + tg.bankWidth + 0.02f;

            for (int step = 0; step < 8; step++)
            {
                Vector3 w = zoneMap.NormalizedToWorldXZ(nx, nz);
                var ray = new Ray(new Vector3(w.x, 200f, w.z), Vector3.down);
                if (col.Raycast(ray, out RaycastHit hit, 400f)
                    && zoneMap.FarFromRiver(hit.point)
                    && Vector3.Angle(hit.normal, Vector3.up) < 35f)   // 别把锚点放在山坡上
                    break;
                nx = Mathf.Lerp(nx, 0.5f, 0.12f);
                nz = Mathf.Lerp(nz, 0.5f, 0.12f);
            }
            if (!Mathf.Approximately(nx, a.nx) || !Mathf.Approximately(nz, a.nz))
            {
                Debug.Log($"[TraceSetup] anchor '{a.zoneId}' ({a.nx:F2},{a.nz:F2}) → ({nx:F2},{nz:F2})");
                a.nx = nx; a.nz = nz;
            }
        }
    }
}
