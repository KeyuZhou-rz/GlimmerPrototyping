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
    }
}
