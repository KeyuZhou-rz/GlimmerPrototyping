using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using GlimmerDiary.Flora;

/// <summary>
/// 幂等生成中央英雄猴面包树（2026-07-18）：BaobabBuilder 手工骨架 →
/// TreeMeshBuilder 网格 → 存 Assets/Meshes/HeroBaobab.mesh → 落地形上
/// (-5, 10)（主相机取景左三分位）。重复执行：网格资产 CopySerialized
/// 原位更新（GUID/引用不动），场景物体原位替换。
/// </summary>
public static class BaobabHeroSetup
{
    const string MeshDir = "Assets/Meshes";
    const string MeshPath = "Assets/Meshes/HeroBaobab.mesh";
    const string TreeMatPath = "Assets/Materials/GlimmerDiary_TreeGrowth.mat";
    static readonly Vector3 SpotXZ = new Vector3(-5f, 0f, 10f);   // 主相机左三分位，远离河道(x≈-35)

    [MenuItem("Tools/Glimmer/Build Central Baobab")]
    public static void Build()
    {
        // —— 骨架 → 网格（无叶纯剪影：leavesPerCluster 0， foliage 点不生成叶）——
        // 全限定名：Assets/Scripts/Lsystem 下有个全局命名空间的静态 v1 同名类，会抢解析
        var segs = BaobabBuilder.BuildSkeleton();
        var builder = new GlimmerDiary.Flora.TreeMeshBuilder();
        var result = builder.Build(
            segs,
            radialSegments: 10,
            baseBulge: 0.5f,        // 干基膨胀（瓶状干的关键加成）
            surfaceNoise: 0.05f,    // 一点表皮起伏，去塑料感
            twistPerSegment: 0f,
            uvTiling: 1.5f,
            leafDistribution: LeafDistribution.BranchTips,
            leavesPerCluster: 0);
        var mesh = result.trunkMesh;
        mesh.name = "HeroBaobab";
        mesh.RecalculateBounds();

        // —— 网格资产：存在则原位更新，保住 GUID 与场景引用 ——
        if (!AssetDatabase.IsValidFolder(MeshDir))
            AssetDatabase.CreateFolder("Assets", "Meshes");
        var existing = AssetDatabase.LoadAssetAtPath<Mesh>(MeshPath);
        Mesh savedMesh;
        if (existing != null)
        {
            EditorUtility.CopySerialized(mesh, existing);
            EditorUtility.SetDirty(existing);
            savedMesh = existing;
        }
        else
        {
            AssetDatabase.CreateAsset(mesh, MeshPath);
            savedMesh = AssetDatabase.LoadAssetAtPath<Mesh>(MeshPath);
        }
        AssetDatabase.SaveAssets();

        // —— 场景落地：贴地形表面（编辑态 MeshCollider 已存在，直接 raycast）——
        var go = GameObject.Find("HeroBaobab");
        if (go == null)
        {
            go = new GameObject("HeroBaobab");
            Undo.RegisterCreatedObjectUndo(go, "Create HeroBaobab");
        }
        Vector3 pos = SpotXZ;
        var ray = new Ray(new Vector3(SpotXZ.x, 200f, SpotXZ.z), Vector3.down);
        if (Physics.Raycast(ray, out RaycastHit hit, 500f))
            pos.y = hit.point.y - 0.15f;   // 根略沉，吃进地形微起伏
        else
            Debug.LogWarning("[BaobabHeroSetup] 地形 raycast 未命中，y 落 0");
        go.transform.SetPositionAndRotation(pos, Quaternion.Euler(0f, 25f, 0f));

        var mf = go.GetComponent<MeshFilter>();
        if (mf == null) mf = go.AddComponent<MeshFilter>();
        mf.sharedMesh = savedMesh;
        var mr = go.GetComponent<MeshRenderer>();
        if (mr == null) mr = go.AddComponent<MeshRenderer>();
        var mat = AssetDatabase.LoadAssetAtPath<Material>(TreeMatPath);
        if (mat != null) mr.sharedMaterial = mat;
        else Debug.LogWarning($"[BaobabHeroSetup] 材质缺失: {TreeMatPath}");

        EditorUtility.SetDirty(go);
        EditorSceneManager.MarkSceneDirty(go.scene);
        EditorSceneManager.SaveOpenScenes();
        Debug.Log($"[BaobabHeroSetup] 猴面包树落地 {pos}，顶点 {savedMesh.vertexCount}，" +
                  $"包围盒 {savedMesh.bounds.size:F1}");
    }

    [MenuItem("Tools/Glimmer/Remove Central Baobab")]
    public static void Remove()
    {
        var go = GameObject.Find("HeroBaobab");
        if (go != null) { Object.DestroyImmediate(go); EditorSceneManager.SaveOpenScenes(); }
        Debug.Log("[BaobabHeroSetup] 已移除（网格资产保留）");
    }
}
