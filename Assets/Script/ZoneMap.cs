using UnityEngine;

/// <summary>
/// Layer 3 区域锚点系统：把 Layer 2 的 zone 字符串（riverbank / lowland / center /
/// highland_east / stone_area）映射到场景空间。
///
/// 锚点存归一化地形坐标 (nx, nz) + 世界半径，运行时对地形 MeshCollider 竖直射线落地——
/// 顶点变换数学镜像自 TerrainGenerator.Generate（勿改原文件，同 GlimmerTerrainDecor 的约定）。
///
/// 方位约定：+X = 东（低地→平原→高地），+Z = 北。
/// 语料里的方位词（"东侧高地""从北边绕过去"）依赖此约定成立，改锚点前先想清楚文本。
///
/// 确定性：所有 Sample* 以调用方给的 seed 驱动 System.Random，
/// 同 seed 同结果，与帧序/墙钟/UnityEngine.Random 无关（痕迹跨会话稳定的前提）。
///
/// 手动覆盖：名为 "Anchor_&lt;zoneId&gt;" 的子物体存在时，该 zone 中心以子物体位置为准
/// （Setup 会创建 Anchor_center 占位，将来手摆主树时对齐它即可）。
/// </summary>
public class ZoneMap : MonoBehaviour
{
    [System.Serializable]
    public class ZoneAnchor
    {
        public string zoneId;
        [Range(0f, 1f)] public float nx;
        [Range(0f, 1f)] public float nz;
        public float radiusWorld = 7f;
    }

    [Header("场景引用（Tools/Glimmer/Setup World Traces 自动接线）")]
    public TerrainGenerator terrain;

    [Header("区域锚点（归一化地形坐标）")]
    public ZoneAnchor[] anchors;

    [Header("落地约束")]
    [Tooltip("采样点到河水缘的最小水平距离（世界单位），避免痕迹落进河里")]
    public float riverClearance = 1.0f;

    private MeshCollider _terrainCollider;

    void Reset() { anchors = DefaultAnchors(); }

    /// <summary>
    /// 代码默认锚点表（依据：区域边界 0.35/0.85、山环 0.2；
    /// riverbank 的 nx 会被 Setup 按实际河道曲线重算，这里是兜底值）。
    /// </summary>
    public static ZoneAnchor[] DefaultAnchors() => new[]
    {
        new ZoneAnchor { zoneId = "riverbank",     nx = 0.26f, nz = 0.50f, radiusWorld = 5f  },
        new ZoneAnchor { zoneId = "lowland",       nx = 0.30f, nz = 0.50f, radiusWorld = 6f  },
        new ZoneAnchor { zoneId = "center",        nx = 0.58f, nz = 0.50f, radiusWorld = 10f },
        new ZoneAnchor { zoneId = "highland_east", nx = 0.90f, nz = 0.50f, radiusWorld = 7f  },
        new ZoneAnchor { zoneId = "stone_area",    nx = 0.60f, nz = 0.30f, radiusWorld = 7f  },
    };

    // ── 公开采样 API ─────────────────────────────────────────────

    /// <summary>zone 圆盘内确定性采样一个落地点（避河；12 次重试后兜底锚点中心）。</summary>
    public bool TrySampleZone(string zone, int seed, out Vector3 point)
    {
        point = default;
        if (!TryGetAnchorCenter(zone, out Vector3 center, out float radius)) return false;

        var rng = new System.Random(seed);
        for (int i = 0; i < 12; i++)
        {
            float ang = (float)rng.NextDouble() * Mathf.PI * 2f;
            float r   = Mathf.Sqrt((float)rng.NextDouble()) * radius;   // 圆盘均匀
            if (TryGroundAt(center.x + Mathf.Cos(ang) * r, center.z + Mathf.Sin(ang) * r, out point)
                && FarFromRiver(point))
                return true;
        }
        return TryGroundAt(center.x, center.z, out point);
    }

    /// <summary>两 zone 锚点连线中段（t∈[0.35,0.65]）+ 垂向抖动的落地点——兑现语料的"A和B之间"。</summary>
    public bool TrySampleEdge(string zoneA, string zoneB, int seed, out Vector3 point)
    {
        point = default;
        if (!TryGetAnchorCenter(zoneA, out Vector3 a, out _)) return false;
        if (!TryGetAnchorCenter(zoneB, out Vector3 b, out _)) return false;

        Vector3 dir  = (b - a); dir.y = 0f;
        Vector3 perp = Vector3.Cross(dir.normalized, Vector3.up);

        var rng = new System.Random(seed);
        for (int i = 0; i < 12; i++)
        {
            float t   = Mathf.Lerp(0.35f, 0.65f, (float)rng.NextDouble());
            float off = ((float)rng.NextDouble() * 2f - 1f) * 2.5f;
            Vector3 c = Vector3.Lerp(a, b, t) + perp * off;
            if (TryGroundAt(c.x, c.z, out point) && FarFromRiver(point)) return true;
        }
        return false;
    }

    /// <summary>
    /// 沿 from→to 方向取一串落地点（脚印串）。t 从 0.30 均匀推进到 0.70，
    /// 每点带小幅侧向抖动。返回实际落地成功的点数；dirXZ = 行进方向（脚印朝向用）。
    /// </summary>
    public int SampleTrail(string from, string to, int seed, int count,
                           System.Collections.Generic.List<Vector3> outPoints, out Vector3 dirXZ)
    {
        outPoints.Clear();
        dirXZ = Vector3.forward;
        if (count <= 0) return 0;
        if (!TryGetAnchorCenter(from, out Vector3 a, out _)) return 0;
        if (!TryGetAnchorCenter(to,   out Vector3 b, out _)) return 0;

        Vector3 dir = b - a; dir.y = 0f;
        if (dir.sqrMagnitude < 1e-4f) return 0;
        dirXZ = dir.normalized;
        Vector3 perp = Vector3.Cross(dirXZ, Vector3.up);

        var rng = new System.Random(seed);
        for (int i = 0; i < count; i++)
        {
            float t   = Mathf.Lerp(0.30f, 0.70f, count == 1 ? 0.5f : i / (float)(count - 1));
            float off = ((float)rng.NextDouble() * 2f - 1f) * 0.5f;
            Vector3 c = Vector3.Lerp(a, b, t) + perp * off;
            if (TryGroundAt(c.x, c.z, out Vector3 p) && FarFromRiver(p))
                outPoints.Add(p);
        }
        return outPoints.Count;
    }

    /// <summary>zone 锚点中心的世界坐标（已落地）。子物体 "Anchor_&lt;zoneId&gt;" 存在时优先。</summary>
    public bool TryGetAnchorCenter(string zone, out Vector3 center, out float radius)
    {
        center = default; radius = 0f;
        var anchor = FindAnchor(zone);
        if (anchor == null) return false;
        radius = anchor.radiusWorld;

        // 手动覆盖：XZ 用子物体位置，Y 重新落地（容忍手摆时没贴地）
        var manual = transform.Find($"Anchor_{zone}");
        if (manual != null)
        {
            if (TryGroundAt(manual.position.x, manual.position.z, out center)) return true;
            center = manual.position;
            return true;
        }

        Vector3 w = NormalizedToWorldXZ(anchor.nx, anchor.nz);
        if (TryGroundAt(w.x, w.z, out center)) return true;
        center = w;
        return true;
    }

    /// <summary>归一化 (nx,nz) → 世界 XZ（Y=0）。镜像 TerrainGenerator.Generate 的顶点变换。</summary>
    public Vector3 NormalizedToWorldXZ(float nx, float nz)
    {
        var tg = Terrain;
        if (tg == null) return Vector3.zero;
        Vector3 offsetGrid = tg.centerMesh
            ? new Vector3(tg.width * 0.5f, 0f, tg.depth * 0.5f)
            : Vector3.zero;
        var local = new Vector3((nx * tg.width - offsetGrid.x) * tg.scale, 0f,
                                (nz * tg.depth - offsetGrid.z) * tg.scale);
        return tg.transform.TransformPoint(local);
    }

    /// <summary>世界 XZ → 只对地形碰撞体竖直射线取地面点（不走 Physics.Raycast，避免打中树/石/水）。</summary>
    public bool TryGroundAt(float worldX, float worldZ, out Vector3 point)
    {
        point = default;
        var col = TerrainCollider;
        if (col == null) return false;
        var ray = new Ray(new Vector3(worldX, 200f, worldZ), Vector3.down);
        if (col.Raycast(ray, out RaycastHit hit, 400f))
        {
            point = hit.point;
            return true;
        }
        return false;
    }

    /// <summary>水面世界高度（读场景 "Water" 对象 renderer bounds；找不到取 -∞ 即不设限）。</summary>
    public float WaterY
    {
        get
        {
            var water = GameObject.Find("Water");
            var r = water != null ? water.GetComponent<Renderer>() : null;
            return r != null ? r.bounds.center.y : float.NegativeInfinity;
        }
    }

    /// <summary>到河水缘的水平距离 ≥ riverClearance（镜像 TreePlacement.DistanceToWater 的数学）。</summary>
    public bool FarFromRiver(Vector3 p)
    {
        var tg = Terrain;
        if (tg == null || !tg.enableRiver) return true;

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
        return distGrid * tg.scale >= riverClearance;
    }

    // ── 内部 ────────────────────────────────────────────────────

    public TerrainGenerator Terrain
    {
        get
        {
            if (terrain == null) terrain = FindFirstObjectByType<TerrainGenerator>();
            return terrain;
        }
    }

    private MeshCollider TerrainCollider
    {
        get
        {
            if (_terrainCollider == null && Terrain != null)
                _terrainCollider = Terrain.GetComponent<MeshCollider>();
            return _terrainCollider != null && _terrainCollider.sharedMesh != null ? _terrainCollider : null;
        }
    }

    private ZoneAnchor FindAnchor(string zone)
    {
        if (anchors == null || string.IsNullOrEmpty(zone)) return null;
        foreach (var a in anchors)
            if (a != null && a.zoneId == zone) return a;
        return null;
    }
}
