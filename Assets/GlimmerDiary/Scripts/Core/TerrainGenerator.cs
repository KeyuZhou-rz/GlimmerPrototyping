using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.TerrainTools;

/// <summary>
/// Procedural, flat-shaded stylized terrain.
/// Layout (looking from -Z toward +Z):
///   X axis  : 左侧低地(lowland) → 中间平原(plains) → 右侧高地(highland)
///   far Z   : 远处群山(distant mountain ridge)
/// Flat shading: non-shared per-triangle vertices + RecalculateNormals.
/// Cliff darkening: per-triangle face normal dot-up → multiply color by cliffDarkMin~1.
/// </summary>
[RequireComponent(typeof(MeshFilter))]
public class TerrainGenerator : MonoBehaviour
{
    [Header("Grid")]
    public int width = 100;
    public int depth = 100;
    public int Resolution = 2;
    public float scale = 1.6f;
    public bool centerMesh = true;

    [Header("Material")]
    public Material terrainMaterial;

    [Header("Region heights (left→right along X)")]
    public float lowlandHeight = 0f;
    public float plainsHeight = 2.5f;
    public float highlandHeight = 4.5f;

    [Header("Region boundaries (normalized X 0..1)")]
    [Range(0f, 1f)] public float lowlandPlainsBoundary = 0.30f;
    [Range(0f, 1f)] public float plainsHighlandBoundary = 0.80f;
    [Tooltip("断崖陡峭度：越小边界越锐利")]
    public float cliffSharpness = 0.02f;
    [Tooltip("边界沿 Z 轴的弯曲幅度，0=笔直")]
    public float boundaryWaviness = 1.3f;

    [Header("Surrounding mountains (ring around all edges)")]
    [Range(0f, 0.5f)] public float PercentageOfMoutains = 0.2f;
    [Range(0.01f, 1f)] public float mountainFalloff = 0.6f;
    public float mountainHeight = 6f;
    public float ridgeFrequency = 2.5f;

    [Header("Detail noise (small in-region undulation)")]
    public float detailAmplitude = 0.45f;
    public float detailFrequency = 0.10f;

    [Header("River (left lowland)")]
    public bool enableRiver = true;
    [Range(0f, 1f)] public float riverCenterX = 0.15f;
    public float riverWidth = 0.08f;
    public float riverDepth = 1.6f;
    public float riverMeanderAmp = 0.04f;
    public float riverMeanderFreq = 1.5f;
    public float riverNoiseAmp = 0.02f;
    public float riverNoiseFreq = 3f;
    public float bankWidth = 0.04f;
    public float bankHeight = 0.6f;

    [Header("Stylization")]
    [Tooltip("启用台地量化，制造低多边形台阶感（参考风格化场景应保持关闭）")]
    public bool useTerrace = false;
    public float terraceStep = 0.9f;
    public float terraceSharpness = 6f;

    [Header("Color Palette")]
    [Tooltip("低地（近水）深林绿")]
    public Color lowlandColor  = new Color(0.12f, 0.28f, 0.10f);
    [Tooltip("平原（草地/稀树）暖草黄绿")]
    public Color plainsColor   = new Color(0.52f, 0.47f, 0.18f);
    [Tooltip("高地 + 山脉 冷岩灰蓝")]
    public Color highlandColor = new Color(0.30f, 0.33f, 0.40f);
    [Tooltip("最陡坡面的暗化系数（0.60~0.70），平坦面始终为 1.0")]
    [Range(0.3f, 1f)] public float cliffDarkMin = 0.65f;

    void Start() { Generate(); }

    // ---- height field ---------------------------------------------------

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

    float MountainMask(float nx, float nz)
    {
        float w = Mathf.Max(0.0001f, PercentageOfMoutains);
        float dist = Mathf.Min(Mathf.Min(nx, 1f - nx), Mathf.Min(nz, 1f - nz));
        if (dist >= w) return 0f;

        float t = 1f - dist / w;
        t = Mathf.Pow(t, mountainFalloff);
        return Mathf.SmoothStep(0f, 1f, t);
    }

    float MountainBand(float nx, float nz)
    {
        float mask = MountainMask(nx, nz);
        if (mask <= 0f) return 0f;

        float n1 = Mathf.PerlinNoise(nx * ridgeFrequency, nz * ridgeFrequency);
        float r1 = 1f - Mathf.Abs(n1 * 2f - 1f);
        r1 *= r1;

        float n2 = Mathf.PerlinNoise(nx * ridgeFrequency * 2.3f + 11.3f, nz * ridgeFrequency * 2.3f + 5.1f);
        float r2 = 1f - Mathf.Abs(n2 * 2f - 1f);
        r2 *= r2;

        return mask * (r1 * 0.7f + r2 * 0.3f) * mountainHeight;
    }

    public float RiverCurve(float nz)
    {
        float meander = Mathf.Sin(nz * Mathf.PI * riverMeanderFreq) * riverMeanderAmp;
        float wiggle  = (Mathf.PerlinNoise(nz * riverNoiseFreq, 7.3f) - 0.5f) * 2f * riverNoiseAmp;
        return riverCenterX + meander + wiggle;
    }

    float RiverDelta(float nx, float nz)
    {
        float center = RiverCurve(nz);
        float d = Mathf.Abs(nx - center);

        float halfW  = Mathf.Max(0.0001f, riverWidth * 0.5f);
        float carveT = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(halfW, 0f, d));
        float carve  = -riverDepth * carveT;

        float bankHalf   = Mathf.Max(0.0001f, bankWidth * 0.5f);
        float bankCenter = halfW + bankHalf;
        float bankT      = Mathf.Clamp01(1f - Mathf.Abs(d - bankCenter) / bankHalf);
        bankT = Mathf.SmoothStep(0f, 1f, bankT);

        return carve + bankHeight * bankT;
    }

    float GetHeight(float x, float z)
    {
        float nx = x / (float)width;
        float nz = z / (float)depth;

        float h = RegionProfile(nx, nz) + MountainBand(nx, nz);
        float detail = (Mathf.PerlinNoise(x * detailFrequency, z * detailFrequency) - 0.5f) * 2f * detailAmplitude;
        h += detail * (1f - MountainMask(nx, nz) * 0.5f);

        if (enableRiver) h += RiverDelta(nx, nz);
        if (useTerrace)  h  = Terrace(h);
        return h;
    }

    float Terrace(float h)
    {
        float step = Mathf.Max(0.01f, terraceStep);
        float floored = Mathf.Floor(h / step) * step;
        float frac = Mathf.Repeat(h, step) / step;
        return floored + Mathf.Pow(frac, terraceSharpness) * step;
    }

    // ---- colour ---------------------------------------------------------

    // 三个高度区间：低地 / 平原 / 高地+山脉，断崖通过坡度暗化单独体现。
    Color GetZoneColor(float height)
    {
        float tHighland = plainsHeight;
        float tPlains   = (lowlandHeight + plainsHeight) * 0.5f;

        if (height >= tHighland) return highlandColor;
        if (height >= tPlains)   return plainsColor;
        return lowlandColor;
    }

    // 面法线 Y 分量：1=水平，0=垂直断崖。映射到 [cliffDarkMin, 1] 作为乘数。
    float SlopeDark(Vector3 a, Vector3 b, Vector3 c)
    {
        Vector3 n = Vector3.Cross(b - a, c - a);
        float len = n.magnitude;
        if (len < 1e-6f) return 1f;
        float slopeY = Mathf.Abs(n.y / len);
        return Mathf.Lerp(cliffDarkMin, 1f, slopeY);
    }

    // ---- mesh build -----------------------------------------------------

    [ContextMenu("Regenerate")]
    void Generate()
    {
        Mesh mesh = new Mesh { name = "Procedural Terrain" };

        int quadX = Mathf.CeilToInt((float)width / Resolution);
        int quadZ = Mathf.CeilToInt((float)depth / Resolution);
        int vertexCount = quadX * quadZ * 6;
        mesh.indexFormat = vertexCount > 65535 ? IndexFormat.UInt32 : IndexFormat.UInt16;

        Vector3[] vertices  = new Vector3[vertexCount];
        Vector2[]   uv2s    = new Vector2[vertexCount];
        Vector2[] uvs       = new Vector2[vertexCount];
        int[]     triangles = new int[vertexCount];

        Vector3 offset = centerMesh ? new Vector3(width * 0.5f, 0f, depth * 0.5f) : Vector3.zero;
        int index = 0;

        float globalMax = float.MinValue, globalMin = float.MaxValue;

        for (int z = 0; z < depth; z += Resolution)
        {
            for (int x = 0; x < width; x += Resolution)
            {
                int xR = Mathf.Min(x + Resolution, width);
                int zR = Mathf.Min(z + Resolution, depth);

                float y00 = GetHeight(x,  z);
                if(y00 > globalMax)
                    globalMax = y00;
                if(y00 < globalMin)
                    globalMin = y00;
                
                float y10 = GetHeight(xR, z);
                if(y10 > globalMax)
                    globalMax = y10;
                if(y10 < globalMin)
                    globalMin = y10;

                float y01 = GetHeight(x,  zR);
                if(y01 > globalMax)
                    globalMax = y01;
                if(y01 < globalMin)
                    globalMin = y01;

                float y11 = GetHeight(xR, zR);
                if(y11 > globalMax)
                    globalMax = y11;
                if(y11 < globalMin)
                    globalMin = y11;
                


                Vector3 v00 = (new Vector3(x,  y00, z)  - offset) * scale;
                Vector3 v10 = (new Vector3(xR, y10, z)  - offset) * scale;
                Vector3 v01 = (new Vector3(x,  y01, zR) - offset) * scale;
                Vector3 v11 = (new Vector3(xR, y11, zR) - offset) * scale;


                // 三角形 1: v00, v01, v10
                vertices[index]     = v00; uvs[index]     = new Vector2((float)x  / width, (float)z  / depth);
                vertices[index + 1] = v01; uvs[index + 1] = new Vector2((float)x  / width, (float)zR / depth);
                vertices[index + 2] = v10; uvs[index + 2] = new Vector2((float)xR / width, (float)z  / depth);

                // 三角形 2: v10, v01, v11
                vertices[index + 3] = v10; uvs[index + 3] = new Vector2((float)xR / width, (float)z  / depth);
                vertices[index + 4] = v01; uvs[index + 4] = new Vector2((float)x  / width, (float)zR / depth);
                vertices[index + 5] = v11; uvs[index + 5] = new Vector2((float)xR / width, (float)zR / depth);

                for (int i = 0; i < 6; i++) triangles[index + i] = index + i;
                index += 6;
            }
        }


        mesh.vertices  = vertices;
        mesh.SetUVs(1, uv2s);
        mesh.uv        = uvs;
        mesh.triangles = triangles;
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();
        GetComponent<MeshFilter>().mesh = mesh;

        // 生成完毕后 把高度数据写入shader
        terrainMaterial.SetFloat("_lowlandHeight", lowlandHeight * scale);
        terrainMaterial.SetFloat("_plainHeight",   plainsHeight * scale);
        terrainMaterial.SetFloat("_highlandHeight", highlandHeight * scale);


    }

    static Color ApplyAlpha(Color c) { c.a = 1f; return c; }
}
