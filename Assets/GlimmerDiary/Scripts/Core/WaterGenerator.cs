using UnityEngine;

/// <summary>
/// 沿 TerrainGenerator 的河道中心线生成一条低密度水面带（ribbon）。
/// 网格本身是平的，所有高度（波浪）交给 StylizedWater 着色器处理。
/// 放在 TerrainGenerator 所在物体的【子物体】上（local transform 归零），
/// 这样网格与地形落在同一坐标系、自动对齐。
/// </summary>
[RequireComponent(typeof(MeshFilter), typeof(MeshRenderer))]
public class WaterGenerator : MonoBehaviour
{
    [Header("Source")]
    [Tooltip("提供河道曲线与尺寸参数的地形生成器")]
    public TerrainGenerator terrain;

    [Header("Mesh density (relative to terrain)")]
    [Tooltip("纵向（沿 Z/水流）密度占地形的比例，建议 1/3 ~ 1/2，太密动画显碎、太疏缺变化")]
    [Range(0.2f, 0.6f)] public float lengthDensity = 0.4f;
    [Tooltip("横向（跨河宽）分段数")]
    [Range(2, 16)] public int widthSegments = 6;

    [Header("Placement")]
    [Tooltip("水面在地形高度场单位下的水平面高度（河床约 -riverDepth，岸顶约 +bankHeight）")]
    public float waterLevel = -0.3f;
    [Tooltip("水面宽度相对河道宽度的比例（<1 收进河道内，避免露出河床底/穿出岸边）")]
    [Range(0.5f, 1.2f)] public float widthScale = 0.92f;

    [Header("Material")]
    [Tooltip("留空则自动用 Custom/StylizedWater 创建一个材质")]
    public Material waterMaterial;

    [Header("Runtime Water Level (display-only)")]
    [Tooltip("展示层水量 0~1(由绑定层驱动,不是世界状态)。映射到高度场 Y 后经 transform 升降,不重建网格")]
    [Range(0f, 1f)] public float displayLevel01 = 0.5f;
    [Tooltip("level=0 的水面高度(高度场单位)。低于河床最深 -riverDepth(场景 2.06)→ 完全断流")]
    public float dryY = -2.2f;
    [Tooltip("两段线性的拐点水量:低于此值进入退水快跌段(V 形河道:水少时水位对水量更敏感)")]
    [Range(0.05f, 0.95f)] public float kneeLevel = 0.3f;
    [Tooltip("拐点处的水面高度(高度场单位)。锚定:waterLevel≈0.69 时 Y≈1.0 = 当前烘焙视觉")]
    public float kneeY = 0.6f;
    [Tooltip("level=1 的水面高度(高度场单位),比当前烘焙 1.0 略满即可")]
    public float fullY = 1.3f;

    private float _bakedWaterLevel;   // Generate() 时的 waterLevel 快照 = 顶点烘焙基准
    private bool _hasBakedLevel;

    void Start()
    {
        Generate();
    }

    [ContextMenu("Regenerate Water")]
    public void Generate()
    {
        if (terrain == null) terrain = GetComponentInParent<TerrainGenerator>();
        if (terrain == null)
        {
            Debug.LogWarning("[WaterGenerator] 未指定 TerrainGenerator，无法生成水面。");
            return;
        }

        int w = terrain.width;
        int d = terrain.depth;
        float scale = terrain.scale;
        Vector3 offset = terrain.centerMesh ? new Vector3(w * 0.5f, 0f, d * 0.5f) : Vector3.zero;

        // 记录烘焙基准:顶点以当前 waterLevel 生成,之后的升降都是相对它的 transform 偏移
        _bakedWaterLevel = waterLevel;
        _hasBakedLevel = true;

        // 纵向分段数 = 地形深度 * 密度比例（地形每格一段，这里更稀疏）
        int zSeg = Mathf.Max(2, Mathf.RoundToInt(d * lengthDensity));
        int xSeg = Mathf.Max(2, widthSegments);

        int vertsPerRow = xSeg + 1;
        int vertCount = (zSeg + 1) * vertsPerRow;

        Vector3[] vertices = new Vector3[vertCount];
        Vector2[] uvs = new Vector2[vertCount];
        int[] triangles = new int[zSeg * xSeg * 6];

        float halfWidthN = terrain.riverWidth * widthScale * 0.5f;   // 归一化半宽
        int vi = 0;

        for (int zi = 0; zi <= zSeg; zi++)
        {
            float fz = zi / (float)zSeg;          // 0..1 沿 Z
            float z = fz * d;
            float centerN = terrain.RiverCurve(fz);    // 河道中心线（归一化 X）
            float centerX = centerN * w;
            float halfWidthUnits = halfWidthN * w;

            for (int xi = 0; xi <= xSeg; xi++)
            {
                float fx = xi / (float)xSeg;       // 0..1 跨河
                float xUnits = centerX + (fx - 0.5f) * 2f * halfWidthUnits;

                vertices[vi] = (new Vector3(xUnits, waterLevel, z) - offset) * scale;
                // uv.x = 岸边因子（0 中心 → 1 两岸），uv.y = 沿流向参数
                uvs[vi] = new Vector2(Mathf.Abs(fx - 0.5f) * 2f, fz);
                vi++;
            }
        }

        int ti = 0;
        for (int zi = 0; zi < zSeg; zi++)
        {
            for (int xi = 0; xi < xSeg; xi++)
            {
                int row0 = zi * vertsPerRow + xi;
                int row1 = (zi + 1) * vertsPerRow + xi;

                triangles[ti++] = row0;
                triangles[ti++] = row1;
                triangles[ti++] = row0 + 1;

                triangles[ti++] = row0 + 1;
                triangles[ti++] = row1;
                triangles[ti++] = row1 + 1;
            }
        }

        Mesh mesh = new Mesh { name = "Procedural Water" };
        mesh.indexFormat = vertCount > 65535
            ? UnityEngine.Rendering.IndexFormat.UInt32
            : UnityEngine.Rendering.IndexFormat.UInt16;
        mesh.vertices = vertices;
        mesh.uv = uvs;
        mesh.triangles = triangles;
        mesh.RecalculateNormals();   // 法线最终由着色器导数重算，这里仅占位
        mesh.RecalculateBounds();

        GetComponent<MeshFilter>().mesh = mesh;

        if (waterMaterial == null)
        {
            Shader s = Shader.Find("Custom/StylizedWater");
            if (s != null) waterMaterial = new Material(s);
        }
        if (waterMaterial != null)
            GetComponent<MeshRenderer>().sharedMaterial = waterMaterial;

        // 重建后基准可能已变(编辑器 Regenerate),立即按当前展示水量重新对齐,防止跳回烘焙高度
        ApplyDisplayLevel();
    }

    /// <summary>
    /// 绑定层入口(Layer 3 展示,只动 transform,不碰世界状态):
    /// 0~1 水量 → 高度场 Y(两段线性) → localPosition.y 偏移。
    /// </summary>
    public void SetDisplayLevel01(float level01)
    {
        displayLevel01 = Mathf.Clamp01(level01);
        ApplyDisplayLevel();
    }

    private void ApplyDisplayLevel()
    {
        if (terrain == null) terrain = GetComponentInParent<TerrainGenerator>();
        if (terrain == null) return;

        // 两段线性:kneeLevel 以上是"健康水位"缓变段,以下是退水快跌段(细流→断流约 10 天可见)
        float l = displayLevel01;
        float targetY = (l >= kneeLevel)
            ? Mathf.Lerp(kneeY, fullY, (l - kneeLevel) / (1f - kneeLevel))
            : Mathf.Lerp(dryY, kneeY, l / kneeLevel);

        // 顶点烘焙在 waterLevel(高度场单位,已 ×scale);未 Generate 过(编辑模式预览)则以当前字段为基准
        float baked = _hasBakedLevel ? _bakedWaterLevel : waterLevel;
        var lp = transform.localPosition;
        lp.y = (targetY - baked) * terrain.scale;
        transform.localPosition = lp;
    }

    [ContextMenu("Water: Preview Dry (level=0)")]
    private void PreviewDry() => SetDisplayLevel01(0f);

    [ContextMenu("Water: Preview Flood (level=1)")]
    private void PreviewFlood() => SetDisplayLevel01(1f);
}
