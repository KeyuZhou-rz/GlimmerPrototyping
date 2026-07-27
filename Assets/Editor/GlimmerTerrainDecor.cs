using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using GlimmerDiary.Environment;

/// <summary>
/// 地形装饰散布（demo2 对齐轮）：程序化巨石/碎石的烘焙与分区散布。
///   Tools/Glimmer/Scatter Terrain Decor — 幂等：烘焙缺失资产 → 重建 TerrainDecor 节点
///   Tools/Glimmer/Clear Terrain Decor   — 移除散布结果
///
/// 设计约束（Docs/Demo2TerrainDetail.md）：
///   · 不改 TerrainGenerator / TreePlacement —— 需要的边界波/水距数学在此镜像一份
///   · 直接 MeshCollider.Raycast 地形碰撞体（不走 Physics.Raycast，避免打中树/水）
///   · 确定性种子，重复执行结果一致
///   · 3 个暖棕材质轮换（不用 MPB，保静态合批）；实例标记 BatchingStatic
/// </summary>
public static class GlimmerTerrainDecor
{
    const int Seed = 20260706;                    // 散布主种子（改这里换布局）
    const string DecorRootName = "TerrainDecor";
    const string MeshDir = "Assets/Models/Rocks";
    const string PrefabDir = "Assets/Prefabs/Rocks";
    const string MatDir = "Assets/Materials/Glimmer";

    // 岩石材质：暖灰棕三档（同族色轮换制造变化；_ShadowTint 与地形一致）
    static readonly (string name, Color color)[] RockMats =
    {
        ("Rock_Glimmer_A", new Color(0.60f, 0.56f, 0.50f)),
        ("Rock_Glimmer_B", new Color(0.53f, 0.49f, 0.43f)),
        ("Rock_Glimmer_C", new Color(0.46f, 0.42f, 0.36f)),
    };

    [MenuItem("Tools/Glimmer/Scatter Terrain Decor")]
    public static void Scatter()
    {
        var tg = Object.FindFirstObjectByType<TerrainGenerator>();
        if (tg == null) { Debug.LogError("[TerrainDecor] No TerrainGenerator in scene"); return; }
        var col = tg.GetComponent<MeshCollider>();
        if (col == null || col.sharedMesh == null)
        {
            Debug.LogError("[TerrainDecor] Terrain MeshCollider missing/empty — run Setup Visual Style first (regenerates terrain)");
            return;
        }

        var mats = BakeRockMaterials();
        var pools = BakeRockPrefabs(mats);   // L/M/S 三档 prefab 池

        // 幂等：删旧重建
        var old = GameObject.Find(DecorRootName);
        if (old != null) Object.DestroyImmediate(old);
        var root = new GameObject(DecorRootName);

        float waterY = FindWaterY();
        var treePositions = CollectTreePositions();
        var rng = new System.Random(Seed);
        int placed = 0;

        // ---- 1. 崖缝巨石簇：区域边界 b1/b2 是天然的岩石带 -------------------
        placed += ScatterCliffSeamClusters(tg, col, pools, root.transform, rng, waterY, treePositions,
            boundary: tg.lowlandPlainsBoundary, anchorCount: 8);
        placed += ScatterCliffSeamClusters(tg, col, pools, root.transform, rng, waterY, treePositions,
            boundary: tg.plainsHighlandBoundary, anchorCount: 6);

        // ---- 2. 山环岩石：外圈山脚散布 L/M ---------------------------------
        placed += ScatterMountainRing(tg, col, pools, root.transform, rng, waterY, treePositions,
            largeCount: 10, mediumCount: 26);

        // ---- 3. 河岸碎石带：S 为主 + 少量 M --------------------------------
        placed += ScatterRiverbank(tg, col, pools, root.transform, rng, waterY,
            smallCount: 120, mediumCount: 12);

        // ---- 4. 平原孤石：稀疏 L/M/S，留出空旷感 ---------------------------
        placed += ScatterPlains(tg, col, pools, root.transform, rng, waterY, treePositions,
            largeCount: 6, mediumCount: 12, smallCount: 80);

        UnityEditor.SceneManagement.EditorSceneManager.MarkAllScenesDirty();
        Debug.Log($"[TerrainDecor] Scattered {placed} rocks under '{DecorRootName}' (seed {Seed})");
    }

    [MenuItem("Tools/Glimmer/Clear Terrain Decor")]
    public static void Clear()
    {
        var old = GameObject.Find(DecorRootName);
        if (old != null)
        {
            Object.DestroyImmediate(old);
            UnityEditor.SceneManagement.EditorSceneManager.MarkAllScenesDirty();
            Debug.Log("[TerrainDecor] Cleared");
        }
    }

    // =====================================================================
    //  资产烘焙（load-if-exists，幂等）
    // =====================================================================
    static Material[] BakeRockMaterials()
    {
        var shader = Shader.Find("Glimmer/Toon");
        var result = new Material[RockMats.Length];
        for (int i = 0; i < RockMats.Length; i++)
        {
            string path = $"{MatDir}/{RockMats[i].name}.mat";
            var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (mat == null)
            {
                mat = new Material(shader);
                AssetDatabase.CreateAsset(mat, path);
            }
            mat.shader = shader;
            mat.SetColor("_BaseColor", RockMats[i].color);
            mat.SetFloat("_ShadeBands", 3f);
            mat.SetFloat("_Posterize", 0.6f);
            mat.SetFloat("_AmbientBoost", 1.15f);   // 背光面留住体积色（同树材质思路），否则岩石读作黑块
            mat.SetColor("_ShadowTint", new Color(0.30f, 0.24f, 0.38f));   // 黄昏紫罗兰影（与地形一致）
            mat.SetFloat("_RimStrength", 0.08f);
            mat.SetFloat("_RimPower", 3.5f);
            mat.SetFloat("_SwayAmount", 0f);   // 岩石绝不摆动
            EditorUtility.SetDirty(mat);
            result[i] = mat;
        }
        return result;
    }

    /// <summary>每档一组 prefab（网格资产先落盘，材质轮换烘进 prefab）。</summary>
    static Dictionary<RockMeshBuilder.RockSize, GameObject[]> BakeRockPrefabs(Material[] mats)
    {
        EnsureFolder("Assets/Models", "Rocks");
        EnsureFolder("Assets/Prefabs", "Rocks");

        var pools = new Dictionary<RockMeshBuilder.RockSize, GameObject[]>();
        var specs = new (RockMeshBuilder.RockSize size, int count, int seedBase)[]
        {
            (RockMeshBuilder.RockSize.Large, 6, 100),
            (RockMeshBuilder.RockSize.Medium, 8, 200),
            (RockMeshBuilder.RockSize.Small, 6, 300),
        };

        foreach (var (size, count, seedBase) in specs)
        {
            var pool = new GameObject[count];
            for (int i = 0; i < count; i++)
            {
                // 网格内容无条件按最新生成代码刷新（同 seed 同形状）；
                // 资产已存在时就地覆写顶点数据，GUID 稳定、prefab 引用不断。
                string meshPath = $"{MeshDir}/Rock_{size}_{i}.asset";
                var fresh = RockMeshBuilder.Build(size, seedBase + i);
                var mesh = AssetDatabase.LoadAssetAtPath<Mesh>(meshPath);
                if (mesh == null)
                {
                    mesh = fresh;
                    AssetDatabase.CreateAsset(mesh, meshPath);   // prefab 引用前必须持久化
                }
                else
                {
                    mesh.Clear();
                    mesh.SetVertices(fresh.vertices);
                    mesh.SetUVs(0, fresh.uv);
                    mesh.SetTriangles(fresh.triangles, 0);
                    mesh.SetNormals(fresh.normals);
                    mesh.RecalculateBounds();
                    EditorUtility.SetDirty(mesh);
                    Object.DestroyImmediate(fresh);
                }

                string prefabPath = $"{PrefabDir}/Rock_{size}_{i}.prefab";
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
                if (prefab == null)
                {
                    var temp = new GameObject($"Rock_{size}_{i}");
                    temp.AddComponent<MeshFilter>().sharedMesh = mesh;
                    temp.AddComponent<MeshRenderer>().sharedMaterial = mats[i % mats.Length];
                    prefab = PrefabUtility.SaveAsPrefabAsset(temp, prefabPath);
                    Object.DestroyImmediate(temp);
                }
                pool[i] = prefab;
            }
            pools[size] = pool;
        }
        AssetDatabase.SaveAssets();
        return pools;
    }

    static void EnsureFolder(string parent, string child)
    {
        // parent 形如 "Assets/Models"（一级子目录），child 为其下再一级
        if (!AssetDatabase.IsValidFolder(parent))
            AssetDatabase.CreateFolder("Assets", parent.Substring("Assets/".Length));
        if (!AssetDatabase.IsValidFolder($"{parent}/{child}"))
            AssetDatabase.CreateFolder(parent, child);
    }

    // =====================================================================
    //  分区散布
    // =====================================================================

    /// <summary>崖缝簇：沿区域边界（含边界波）取锚点，每锚 1L + 2-4M + 4-8S 交叠成体量。</summary>
    static int ScatterCliffSeamClusters(TerrainGenerator tg, MeshCollider col,
        Dictionary<RockMeshBuilder.RockSize, GameObject[]> pools, Transform parent,
        System.Random rng, float waterY, List<Vector3> trees, float boundary, int anchorCount)
    {
        int placed = 0;
        var anchors = new List<float>();   // nz 列表，锚点间距 ≥ 0.06
        int guard = 0;
        while (anchors.Count < anchorCount && guard++ < 400)
        {
            float nz = Mathf.Lerp(0.06f, 0.94f, (float)rng.NextDouble());
            bool tooClose = false;
            foreach (var a in anchors) if (Mathf.Abs(a - nz) < 0.06f) { tooClose = true; break; }
            if (!tooClose) anchors.Add(nz);
        }

        float worldSpan = tg.width * tg.scale;
        foreach (float nz in anchors)
        {
            // 锚点贴着崖缝，略偏下坡侧（nx 小的一侧是低区，崖脚岩石更自然）
            float nxAnchor = boundary + BoundaryWave(tg, nz) + Mathf.Lerp(-0.018f, 0.012f, (float)rng.NextDouble());

            // 1 块 L 做体量核心
            if (TrySampleGround(tg, col, nxAnchor, nz, out var p, out _)
                && p.y > waterY + 0.1f && !NearTree(p, trees))
            {
                PlaceRock(PickPrefab(pools, RockMeshBuilder.RockSize.Large, rng), p, rng, 1.6f, 3.0f, parent);
                placed++;
            }

            // 周围 M/S 交叠（低模岩块互相穿插读作一整块岩体，同 demo2）
            int mCount = 2 + rng.Next(3);
            int sCount = 4 + rng.Next(5);
            for (int i = 0; i < mCount + sCount; i++)
            {
                bool medium = i < mCount;
                float r = (medium ? Mathf.Lerp(1.2f, 3.0f, (float)rng.NextDouble())
                                  : Mathf.Lerp(1.0f, 4.5f, (float)rng.NextDouble())) / worldSpan;
                float ang = (float)rng.NextDouble() * Mathf.PI * 2f;
                float nx = nxAnchor + Mathf.Cos(ang) * r;
                float nz2 = nz + Mathf.Sin(ang) * r;
                if (!TrySampleGround(tg, col, nx, nz2, out var q, out _)) continue;
                if (q.y < waterY + 0.05f || NearTree(q, trees)) continue;

                if (medium)
                    PlaceRock(PickPrefab(pools, RockMeshBuilder.RockSize.Medium, rng), q, rng, 0.8f, 1.5f, parent);
                else
                    PlaceRock(PickPrefab(pools, RockMeshBuilder.RockSize.Small, rng), q, rng, 0.3f, 0.7f, parent);
                placed++;
            }
        }
        return placed;
    }

    /// <summary>山环：外圈 [0.04, ring*0.8] 距边带内散布，落在山脚坡上。</summary>
    static int ScatterMountainRing(TerrainGenerator tg, MeshCollider col,
        Dictionary<RockMeshBuilder.RockSize, GameObject[]> pools, Transform parent,
        System.Random rng, float waterY, List<Vector3> trees, int largeCount, int mediumCount)
    {
        int placed = 0;
        float ring = tg.PercentageOfMoutains;
        int attempts = (largeCount + mediumCount) * 30;
        int l = 0, m = 0;

        for (int i = 0; i < attempts && (l < largeCount || m < mediumCount); i++)
        {
            float nx = Mathf.Lerp(0.02f, 0.98f, (float)rng.NextDouble());
            float nz = Mathf.Lerp(0.02f, 0.98f, (float)rng.NextDouble());
            float distEdge = Mathf.Min(Mathf.Min(nx, 1f - nx), Mathf.Min(nz, 1f - nz));
            if (distEdge < 0.04f || distEdge > ring * 0.8f) continue;   // 山脚带

            if (!TrySampleGround(tg, col, nx, nz, out var p, out _)) continue;
            if (p.y < waterY + 0.1f || NearTree(p, trees)) continue;

            if (l < largeCount)
            {
                PlaceRock(PickPrefab(pools, RockMeshBuilder.RockSize.Large, rng), p, rng, 1.6f, 2.8f, parent);
                l++; placed++;
            }
            else
            {
                PlaceRock(PickPrefab(pools, RockMeshBuilder.RockSize.Medium, rng), p, rng, 0.8f, 1.4f, parent);
                m++; placed++;
            }
        }
        return placed;
    }

    /// <summary>河岸碎石带：贴着水缘两侧的 S 石 + 少量 M。</summary>
    static int ScatterRiverbank(TerrainGenerator tg, MeshCollider col,
        Dictionary<RockMeshBuilder.RockSize, GameObject[]> pools, Transform parent,
        System.Random rng, float waterY, int smallCount, int mediumCount)
    {
        if (!tg.enableRiver) return 0;
        int placed = 0;
        int total = smallCount + mediumCount;
        int attempts = total * 12;
        int s = 0, m = 0;

        for (int i = 0; i < attempts && (s < smallCount || m < mediumCount); i++)
        {
            float nz = Mathf.Lerp(0.08f, 0.92f, (float)rng.NextDouble());
            float nxCenter = tg.RiverCurve(nz);
            float side = rng.Next(2) == 0 ? -1f : 1f;
            // 水缘外 0.5~4 世界单位的窄带
            float offset = tg.riverWidth * 0.5f + Mathf.Lerp(0.003f, 0.025f, (float)rng.NextDouble());
            float nx = nxCenter + side * offset;

            if (!TrySampleGround(tg, col, nx, nz, out var p, out _)) continue;
            if (p.y < waterY + 0.02f) continue;   // 不落进水里

            if (s < smallCount)
            {
                PlaceRock(PickPrefab(pools, RockMeshBuilder.RockSize.Small, rng), p, rng, 0.25f, 0.6f, parent);
                s++; placed++;
            }
            else
            {
                PlaceRock(PickPrefab(pools, RockMeshBuilder.RockSize.Medium, rng), p, rng, 0.7f, 1.1f, parent);
                m++; placed++;
            }
        }
        return placed;
    }

    /// <summary>平原孤石：内圈平缓处稀疏散布，保留草原空旷感。</summary>
    static int ScatterPlains(TerrainGenerator tg, MeshCollider col,
        Dictionary<RockMeshBuilder.RockSize, GameObject[]> pools, Transform parent,
        System.Random rng, float waterY, List<Vector3> trees, int largeCount, int mediumCount, int smallCount)
    {
        int placed = 0;
        float ring = tg.PercentageOfMoutains;
        int total = largeCount + mediumCount + smallCount;
        int attempts = total * 15;
        int l = 0, m = 0, s = 0;

        for (int i = 0; i < attempts && placed < total; i++)
        {
            float nx = Mathf.Lerp(0.02f, 0.98f, (float)rng.NextDouble());
            float nz = Mathf.Lerp(0.02f, 0.98f, (float)rng.NextDouble());
            float distEdge = Mathf.Min(Mathf.Min(nx, 1f - nx), Mathf.Min(nz, 1f - nz));
            if (distEdge < ring) continue;                          // 避开山环

            if (!TrySampleGround(tg, col, nx, nz, out var p, out var n)) continue;
            if (Vector3.Angle(n, Vector3.up) > 22f) continue;       // 平缓地
            if (p.y < waterY + 0.1f || NearTree(p, trees)) continue;
            if (DistanceToWater(tg, p) < 4f) continue;              // 河岸带交给专属散布

            if (l < largeCount)
            { PlaceRock(PickPrefab(pools, RockMeshBuilder.RockSize.Large, rng), p, rng, 1.4f, 2.4f, parent); l++; }
            else if (m < mediumCount)
            { PlaceRock(PickPrefab(pools, RockMeshBuilder.RockSize.Medium, rng), p, rng, 0.7f, 1.3f, parent); m++; }
            else
            { PlaceRock(PickPrefab(pools, RockMeshBuilder.RockSize.Small, rng), p, rng, 0.25f, 0.55f, parent); s++; }
            placed++;
        }
        return placed;
    }

    // =====================================================================
    //  几何与查询助手
    // =====================================================================

    static GameObject PickPrefab(Dictionary<RockMeshBuilder.RockSize, GameObject[]> pools,
        RockMeshBuilder.RockSize size, System.Random rng)
    {
        var pool = pools[size];
        return pool[rng.Next(pool.Length)];
    }

    /// <summary>实例化 + 随机 yaw/微倾 + 落座下沉 15-30% + BatchingStatic。</summary>
    static void PlaceRock(GameObject prefab, Vector3 groundPoint, System.Random rng,
        float scaleMin, float scaleMax, Transform parent)
    {
        var go = (GameObject)PrefabUtility.InstantiatePrefab(prefab, parent);
        float s = Mathf.Lerp(scaleMin, scaleMax, (float)rng.NextDouble());
        go.transform.localScale = Vector3.one * s;
        go.transform.rotation = Quaternion.Euler(
            Mathf.Lerp(-8f, 8f, (float)rng.NextDouble()),
            (float)rng.NextDouble() * 360f,
            Mathf.Lerp(-8f, 8f, (float)rng.NextDouble()));

        var mesh = go.GetComponent<MeshFilter>().sharedMesh;
        Bounds b = mesh.bounds;
        float bottomLift = -b.min.y * s;                                        // 底面贴地
        float sink = b.size.y * s * Mathf.Lerp(0.15f, 0.30f, (float)rng.NextDouble());
        go.transform.position = groundPoint + Vector3.up * (bottomLift - sink); // 下沉入土

        GameObjectUtility.SetStaticEditorFlags(go, StaticEditorFlags.BatchingStatic);
    }

    /// <summary>归一化 (nx,nz) → 世界 XZ → 只对地形碰撞体竖直射线取地面点。</summary>
    static bool TrySampleGround(TerrainGenerator tg, MeshCollider col, float nx, float nz,
        out Vector3 point, out Vector3 normal)
    {
        point = Vector3.zero; normal = Vector3.up;
        if (nx < 0.005f || nx > 0.995f || nz < 0.005f || nz > 0.995f) return false;

        // TerrainGenerator.Generate 顶点变换的镜像：grid → (grid-offset)*scale → world
        Vector3 offsetGrid = tg.centerMesh
            ? new Vector3(tg.width * 0.5f, 0f, tg.depth * 0.5f)
            : Vector3.zero;
        var local = new Vector3((nx * tg.width - offsetGrid.x) * tg.scale, 0f,
                                (nz * tg.depth - offsetGrid.z) * tg.scale);
        Vector3 world = tg.transform.TransformPoint(local);

        var ray = new Ray(new Vector3(world.x, 200f, world.z), Vector3.down);
        if (col.Raycast(ray, out RaycastHit hit, 400f))
        {
            point = hit.point; normal = hit.normal;
            return true;
        }
        return false;
    }

    /// <summary>边界波形——镜像自 TerrainGenerator.RegionProfile（勿改原文件）。</summary>
    static float BoundaryWave(TerrainGenerator tg, float nz) =>
        (Mathf.Sin(nz * Mathf.PI * 2.0f) * 0.05f +
         Mathf.Sin(nz * Mathf.PI * 5.3f) * 0.025f +
         Mathf.Sin(nz * Mathf.PI * 11.7f) * 0.012f) * tg.boundaryWaviness;

    /// <summary>到河水缘的水平世界距离——镜像自 TreePlacement.DistanceToWater（勿动原文件）。</summary>
    static float DistanceToWater(TerrainGenerator tg, Vector3 p)
    {
        if (!tg.enableRiver) return float.PositiveInfinity;

        Vector3 offsetGrid = tg.centerMesh
            ? new Vector3(tg.width * 0.5f, 0f, tg.depth * 0.5f)
            : Vector3.zero;
        Vector3 local = tg.transform.InverseTransformPoint(p);
        float gx = local.x / tg.scale + offsetGrid.x;
        float gz = local.z / tg.scale + offsetGrid.z;

        float nz = gz / tg.depth;
        float centerGridX = tg.RiverCurve(nz) * tg.width;
        float halfWidthGrid = tg.riverWidth * 0.5f * tg.width;

        float distGrid = Mathf.Max(0f, Mathf.Abs(gx - centerGridX) - halfWidthGrid);
        return distGrid * tg.scale;
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

    /// <summary>手摆 BrokenVector 树 2.5m 内不落石，避免穿插树干。</summary>
    static List<Vector3> CollectTreePositions()
    {
        var list = new List<Vector3>();
        foreach (var t in Object.FindObjectsByType<Transform>(FindObjectsSortMode.None))
            if (t.name.StartsWith("Tree Type")) list.Add(t.position);
        return list;
    }

    static bool NearTree(Vector3 p, List<Vector3> trees)
    {
        foreach (var t in trees)
        {
            float dx = p.x - t.x, dz = p.z - t.z;
            if (dx * dx + dz * dz < 2.5f * 2.5f) return true;
        }
        return false;
    }
}
