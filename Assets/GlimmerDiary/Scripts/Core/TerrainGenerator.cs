using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// Procedural, flat-shaded stylized terrain.
/// Layout (looking from -Z toward +Z):
///   X axis  : 左侧低地(lowland) → 中间平原(plains) → 右侧高地(highland)
///   far Z   : 远处群山(distant mountain ridge)
/// Flat shading comes from non-shared per-triangle vertices + RecalculateNormals.
/// </summary>
[RequireComponent(typeof(MeshFilter))]
public class TerrainGenerator : MonoBehaviour
{
    [Header("Grid")]
    public int width = 160;
    public int depth = 160;
    public float scale = 1f;
    public bool centerMesh = true;

    [Header("Region heights (left→right along X)")]
    public float lowlandHeight = 0f;
    public float plainsHeight = 1.5f;
    public float highlandHeight = 4f;

    [Header("Region boundaries (normalized X 0..1)")]
    [Range(0f, 1f)] public float lowlandPlainsBoundary = 0.33f;
    [Range(0f, 1f)] public float plainsHighlandBoundary = 0.66f;
    [Tooltip("断崖陡峭度：越小边界越锐利")]
    public float cliffSharpness = 0.05f;
    [Tooltip("边界沿 Z 轴的弯曲幅度，0=笔直")]
    public float boundaryWaviness = 1f;

    [Header("Distant mountains (far Z)")]
    [Range(0f, 1f)] public float mountainStart = 0.62f;   // nz where the ridge begins to rise
    public float mountainHeight = 9f;
    public float ridgeFrequency = 3.5f;

    [Header("Detail noise (small in-region undulation)")]
    public float detailAmplitude = 0.35f;
    public float detailFrequency = 0.12f;

    [Header("Stylization")]
    [Tooltip("启用台地量化，制造低多边形台阶感")]
    public bool useTerrace = true;
    public float terraceStep = 0.9f;
    public float terraceSharpness = 6f;

    void Start()
    {
        Generate();
    }

    // ---- height field ---------------------------------------------------

    // 沿 X 的低地→平原→高地剖面；边界沿 Z 弯曲，避免笔直生硬。
    float RegionProfile(float nx, float nz)
    {
        float wave =
            (Mathf.Sin(nz * Mathf.PI * 2.0f) * 0.05f +
             Mathf.Sin(nz * Mathf.PI * 5.3f) * 0.025f +
             Mathf.Sin(nz * Mathf.PI * 11.7f) * 0.012f) * boundaryWaviness;

        float b1 = lowlandPlainsBoundary + wave;
        float b2 = plainsHighlandBoundary + wave * 0.8f;
        float s = Mathf.Max(0.0001f, cliffSharpness);

        float blend1 = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(b1 - s, b1 + s, nx));
        float blend2 = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(b2 - s, b2 + s, nx));

        return lowlandHeight
             + (plainsHeight - lowlandHeight) * blend1
             + (highlandHeight - plainsHeight) * blend2;
    }

    // 0 in the front half, rising ridged peaks toward the far Z edge.
    float MountainMask(float nz)
    {
        return Mathf.SmoothStep(mountainStart, 1.0f, nz);
    }

    float MountainBand(float nx, float nz)
    {
        float mask = MountainMask(nz);
        if (mask <= 0f) return 0f;

        // Ridged noise = 1 - |2n-1|, squared for sharper crests. Two octaves.
        float n1 = Mathf.PerlinNoise(nx * ridgeFrequency, nz * ridgeFrequency * 0.5f);
        float r1 = 1f - Mathf.Abs(n1 * 2f - 1f);
        r1 *= r1;

        float n2 = Mathf.PerlinNoise(nx * ridgeFrequency * 2.3f + 11.3f, nz * ridgeFrequency + 5.1f);
        float r2 = 1f - Mathf.Abs(n2 * 2f - 1f);
        r2 *= r2;

        float ridge = r1 * 0.7f + r2 * 0.3f;
        return mask * ridge * mountainHeight;
    }

    float GetHeight(float x, float z)
    {
        float nx = x / width;
        float nz = z / depth;

        float h = RegionProfile(nx, nz) + MountainBand(nx, nz);

        // 小幅细节，山体上减弱以免破坏山脊轮廓
        float detail = (Mathf.PerlinNoise(x * detailFrequency, z * detailFrequency) - 0.5f) * 2f * detailAmplitude;
        h += detail * (1f - MountainMask(nz) * 0.5f);

        if (useTerrace) h = Terrace(h);
        return h;
    }

    // 软台阶量化：平台 + 短斜坡，低多边形风格。
    float Terrace(float h)
    {
        float step = Mathf.Max(0.01f, terraceStep);
        float floored = Mathf.Floor(h / step) * step;
        float frac = Mathf.Repeat(h, step) / step;
        float blend = Mathf.Pow(frac, terraceSharpness);
        return floored + blend * step;
    }

    // ---- colour palette (by absolute height, tracks the config) ---------

    Color GetZoneColor(float height)
    {
        float tPlains = (lowlandHeight + plainsHeight) * 0.5f;
        float tHighland = (plainsHeight + highlandHeight) * 0.5f;
        float tRock = highlandHeight + mountainHeight * 0.25f;
        float tSnow = highlandHeight + mountainHeight * 0.70f;

        if (height >= tSnow) return new Color(0.92f, 0.94f, 0.96f); // 雪顶
        if (height >= tRock) return new Color(0.50f, 0.50f, 0.53f); // 岩石山腰
        if (height >= tHighland) return new Color(0.55f, 0.45f, 0.30f); // 高地棕褐
        if (height >= tPlains) return new Color(0.45f, 0.62f, 0.28f); // 平原草色
        return new Color(0.18f, 0.45f, 0.22f);                        // 低地深绿
    }

    // ---- mesh build -----------------------------------------------------

    [ContextMenu("Regenerate")]
    void Generate()
    {
        Mesh mesh = new Mesh { name = "Procedural Terrain" };

        // 每格 2 个三角形 = 6 个不共享顶点（flat shading 的关键）
        int totalVertices = width * depth * 6;

        // 顶点数超过 16-bit 上限时切到 32-bit 索引
        mesh.indexFormat = totalVertices > 65535 ? IndexFormat.UInt32 : IndexFormat.UInt16;

        Vector3[] vertices = new Vector3[totalVertices];
        Color[] colors = new Color[totalVertices];
        int[] triangles = new int[totalVertices];

        Vector3 offset = centerMesh ? new Vector3(width * 0.5f, 0f, depth * 0.5f) : Vector3.zero;
        int index = 0;

        for (int z = 0; z < depth; z++)
        {
            for (int x = 0; x < width; x++)
            {
                float y00 = GetHeight(x, z);
                float y10 = GetHeight(x + 1, z);
                float y01 = GetHeight(x, z + 1);
                float y11 = GetHeight(x + 1, z + 1);

                float centerHeight = (y00 + y10 + y01 + y11) * 0.25f;
                Color zoneColor = GetZoneColor(centerHeight);

                Vector3 v00 = (new Vector3(x, y00, z) - offset) * scale;
                Vector3 v10 = (new Vector3(x + 1, y10, z) - offset) * scale;
                Vector3 v01 = (new Vector3(x, y01, z + 1) - offset) * scale;
                Vector3 v11 = (new Vector3(x + 1, y11, z + 1) - offset) * scale;

                // 三角形 1
                vertices[index] = v00;
                vertices[index + 1] = v01;
                vertices[index + 2] = v10;
                // 三角形 2
                vertices[index + 3] = v10;
                vertices[index + 4] = v01;
                vertices[index + 5] = v11;

                for (int i = 0; i < 6; i++)
                {
                    colors[index + i] = zoneColor;     // 同色纯平，不渐变
                    triangles[index + i] = index + i;
                }

                index += 6;
            }
        }

        mesh.vertices = vertices;
        mesh.colors = colors;
        mesh.triangles = triangles;

        // 不共享顶点 + 此调用 → 每个三角面得到垂直于面的法线，硬面光影
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();

        GetComponent<MeshFilter>().mesh = mesh;
    }
}
