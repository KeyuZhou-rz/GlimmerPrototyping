using UnityEditor;
using UnityEngine;
using GlimmerDiary.Flora;

/// <summary>
/// 草簇系统一键接线（demo2 对齐轮）。独立于 GlimmerVisualSetup 的菜单——
/// 执行顺序约定：Setup Visual Style（地形 Generate）→ Scatter Terrain Decor → Setup Grass。
///   · 幂等建/更新 Grass_Glimmer.mat（Glimmer/Grass, enableInstancing）
///   · 烘焙 GrassTuft 网格资产
///   · find-or-create GrassSystem 节点并接线（terrain / collider / waterY）→ GenerateGrass()
/// </summary>
public static class GlimmerGrassSetup
{
    const string GrassMatPath = "Assets/Materials/Glimmer/Grass_Glimmer.mat";
    const string GrassMeshDir = "Assets/Models/Grass";
    const string GrassMeshPath = GrassMeshDir + "/GrassTuft.asset";
    const string GrassGoName = "GrassSystem";

    [MenuItem("Tools/Glimmer/Setup Grass")]
    public static void SetupGrass()
    {
        var tg = Object.FindFirstObjectByType<TerrainGenerator>();
        if (tg == null) { Debug.LogError("[GrassSetup] No TerrainGenerator in scene"); return; }
        var col = tg.GetComponent<MeshCollider>();
        if (col == null || col.sharedMesh == null)
        {
            Debug.LogError("[GrassSetup] Terrain MeshCollider missing/empty — run Setup Visual Style first");
            return;
        }

        // ---- 材质（instancing 必须勾上，否则 DrawMeshInstanced 静默不画） ----
        var shader = Shader.Find("Glimmer/Grass");
        if (shader == null) { Debug.LogError("[GrassSetup] Glimmer/Grass shader not found"); return; }

        var mat = AssetDatabase.LoadAssetAtPath<Material>(GrassMatPath);
        if (mat == null)
        {
            mat = new Material(shader);
            AssetDatabase.CreateAsset(mat, GrassMatPath);
        }
        mat.shader = shader;
        mat.enableInstancing = true;
        mat.SetColor("_RootColor", new Color(0.62f, 0.60f, 0.40f));   // 干草根
        mat.SetColor("_TipColor", new Color(0.80f, 0.76f, 0.55f));    // 苍白草尖（≈平原带色）
        mat.SetFloat("_ShadeBands", 3f);
        mat.SetFloat("_Posterize", 0.6f);
        mat.SetFloat("_AmbientBoost", 1.0f);
        mat.SetColor("_ShadowTint", new Color(0.30f, 0.24f, 0.38f));   // 黄昏紫罗兰影（与地形一致）
        mat.SetFloat("_RimStrength", 0.06f);
        mat.SetFloat("_RimPower", 3.5f);
        EditorUtility.SetDirty(mat);

        // ---- 草簇网格资产 --------------------------------------------------
        if (!AssetDatabase.IsValidFolder("Assets/Models"))
            AssetDatabase.CreateFolder("Assets", "Models");
        if (!AssetDatabase.IsValidFolder(GrassMeshDir))
            AssetDatabase.CreateFolder("Assets/Models", "Grass");

        var mesh = AssetDatabase.LoadAssetAtPath<Mesh>(GrassMeshPath);
        if (mesh == null)
        {
            mesh = GrassSystem.CreateTuftMesh();
            AssetDatabase.CreateAsset(mesh, GrassMeshPath);
        }

        // ---- GrassSystem 节点接线 -------------------------------------------
        var go = GameObject.Find(GrassGoName);
        if (go == null) go = new GameObject(GrassGoName);
        var grass = go.GetComponent<GrassSystem>();
        if (grass == null) grass = go.AddComponent<GrassSystem>();

        grass.terrain = tg;
        grass.terrainCollider = col;
        grass.waterY = FindWaterY();
        grass.grassMaterial = mat;
        grass.grassBladeMesh = mesh;
        grass.castShadows = false;
        // 密度/尺寸权威值在此接线（组件序列化值可能是旧默认）：
        // demo2 的地毯感需要更饱满的簇——平原密、低地中、高地稀
        grass.maxTufts = 32000;
        grass.densityLowland = 1.0f;
        grass.densityPlains = 1.6f;
        grass.densityHighland = 0.35f;
        grass.minHeight = 0.45f;
        grass.maxHeight = 0.9f;
        grass.GenerateGrass();

        EditorUtility.SetDirty(go);
        AssetDatabase.SaveAssets();
        UnityEditor.SceneManagement.EditorSceneManager.MarkAllScenesDirty();
        Debug.Log($"[GrassSetup] Grass wired (waterY={grass.waterY:F2}) — see GrassSystem log for tuft count");
    }

    /// <summary>水面世界高度（读 Water 对象 renderer bounds；找不到默认 0）。</summary>
    static float FindWaterY()
    {
        var water = GameObject.Find("Water");
        if (water != null)
        {
            var r = water.GetComponent<Renderer>();
            if (r != null) return r.bounds.center.y;
        }
        return 0f;
    }
}
