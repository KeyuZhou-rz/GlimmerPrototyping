using System;
using System.Runtime.InteropServices.WindowsRuntime;
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
    // 低分辨率 + 较大 scale = 更大的刻面（low-poly 块面感），而非细碎网格。
    public int width = 100;
    public int depth = 100;
    public float scale = 1.6f;
    public bool centerMesh = true;

    [Header("Region heights (left→right along X)")]
    public float lowlandHeight = 0f;
    public float plainsHeight = 2.5f;
    public float highlandHeight = 4.5f;

    [Header("Region boundaries (normalized X 0..1)")]
    [Range(0f, 1f)] public float lowlandPlainsBoundary = 0.30f;   // 低地占左 30%
    [Range(0f, 1f)] public float plainsHighlandBoundary = 0.80f;  // 平原占中 50%（最大），高地占右 20%（最小）
    [Tooltip("断崖陡峭度：越小边界越锐利")]
    public float cliffSharpness = 0.02f;
    [Tooltip("边界沿 Z 轴的弯曲幅度，0=笔直")]
    public float boundaryWaviness = 1.3f;

    [Header("Surrounding mountains (ring around all edges)")]
    [Tooltip("环绕山脉的边缘宽度（归一化 0..0.5）：越大山带越宽、平原越小")]
    [Range(0f, 0.5f)] public float PercentageOfMoutains = 0.2f;
    [Tooltip("环带内缘融入平原的柔和度：越大过渡越平缓")]
    [Range(0.01f, 1f)] public float mountainFalloff = 0.6f;
    public float mountainHeight = 6f;
    public float ridgeFrequency = 2.5f;                   // 频率降低 → 山体更宽缓、不尖碎

    [Header("Detail noise (small in-region undulation)")]
    public float detailAmplitude = 0.45f;   // 起伏更明显，草地呈柔和波浪而非平板
    public float detailFrequency = 0.10f;

    [Header("River (left lowland)")]
    public bool enableRiver = true;
    [Tooltip("河道中心线的基准归一化 X（0=最左），落在左侧低地内")]
    [Range(0f, 1f)] public float riverCenterX = 0.15f;
    [Tooltip("河道宽度（归一化）：横向凹陷的总宽")]
    public float riverWidth = 0.08f;
    [Tooltip("河床最深处相对地面的下凹深度")]
    public float riverDepth = 1.6f;
    [Tooltip("低频正弦弯曲的幅度（河道整体蜿蜒）")]
    public float riverMeanderAmp = 0.04f;
    [Tooltip("低频正弦弯曲的频率（沿 Z 方向的弯曲次数）")]
    public float riverMeanderFreq = 1.5f;
    [Tooltip("Perlin 扰动幅度：叠加细碎自然弯曲")]
    public float riverNoiseAmp = 0.02f;
    [Tooltip("Perlin 扰动频率")]
    public float riverNoiseFreq = 3f;
    [Tooltip("河道两侧堤岸隆起的宽度（归一化）")]
    public float bankWidth = 0.04f;
    [Tooltip("堤岸隆起的高度")]
    public float bankHeight = 0.6f;

    [Header("Stylization")]
    // 台地量化会产生 Minecraft 式硬台阶；风格化 low-poly 要的是平滑刻面，默认关闭。
    [Tooltip("启用台地量化，制造低多边形台阶感（参考风格化场景应保持关闭）")]
    public bool useTerrace = false;
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

    // 环绕地形的山脉遮罩：取到四条边中最近一条的距离，越靠边越接近 1。
    // 山带宽度 = PercentageOfMoutains，内缘用 mountainFalloff 控制过渡软硬。
    float MountainMask(float nx, float nz)
    {
        float w = Mathf.Max(0.0001f, PercentageOfMoutains);
        float dist = Mathf.Min(Mathf.Min(nx, 1f - nx), Mathf.Min(nz, 1f - nz));
        if (dist >= w) return 0f;

        float t = 1f - dist / w;                 // 0=内缘 → 1=最外圈
        t = Mathf.Pow(t, mountainFalloff);       // falloff 越大，内侧抬升越平缓
        return Mathf.SmoothStep(0f, 1f, t);
    }

    float MountainBand(float nx, float nz)
    {
        float mask = MountainMask(nx, nz);
        if (mask <= 0f) return 0f;

        // Ridged noise = 1 - |2n-1|, squared for sharper crests. Two octaves.
        // nx/nz 对称取样，让四周山体走向一致、不分前后。
        float n1 = Mathf.PerlinNoise(nx * ridgeFrequency, nz * ridgeFrequency);
        float r1 = 1f - Mathf.Abs(n1 * 2f - 1f);
        r1 *= r1;

        float n2 = Mathf.PerlinNoise(nx * ridgeFrequency * 2.3f + 11.3f, nz * ridgeFrequency * 2.3f + 5.1f);
        float r2 = 1f - Mathf.Abs(n2 * 2f - 1f);
        r2 *= r2;

        float ridge = r1 * 0.7f + r2 * 0.3f;
        return mask * ridge * mountainHeight;
    }

    // 河道中心线随 Z 推进的归一化 X 位置：低频正弦蜿蜒 + Perlin 细扰。
    // public：供水面网格 (WaterGenerator) 沿同一条曲线生成。
    public float RiverCurve(float nz)
    {
        float meander = Mathf.Sin(nz * Mathf.PI * riverMeanderFreq) * riverMeanderAmp;
        float wiggle  = (Mathf.PerlinNoise(nz * riverNoiseFreq, 7.3f) - 0.5f) * 2f * riverNoiseAmp;
        return riverCenterX + meander + wiggle;
    }

    // 河道对地形的净高度修正：中心下凹（河床）+ 两侧环带抬升（堤岸隆起）。
    float RiverDelta(float nx, float nz)
    {
        float center = RiverCurve(nz);
        float d = Mathf.Abs(nx - center);                 // 到中心线的横向距离

        // 河床下凹：边缘 0 → 中心最深，用 SmoothStep 平滑过渡
        float halfW  = Mathf.Max(0.0001f, riverWidth * 0.5f);
        float carveT = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(halfW, 0f, d));
        float carve  = -riverDepth * carveT;

        // 堤岸隆起：位于河道外侧的环带（halfW → halfW+bankWidth），中部最高
        float bankHalf   = Mathf.Max(0.0001f, bankWidth * 0.5f);
        float bankCenter = halfW + bankHalf;
        float bankT      = Mathf.Clamp01(1f - Mathf.Abs(d - bankCenter) / bankHalf);
        bankT = Mathf.SmoothStep(0f, 1f, bankT);
        float bank = bankHeight * bankT;

        return carve + bank;
    }

    float GetHeight(float x, float z)
    {
        float nx = x / (float)width;
        float nz = z / (float)depth;


        float h = RegionProfile(nx, nz) + MountainBand(nx, nz);
        // 小幅细节，山体上减弱以免破坏山脊轮廓
        float detail = (Mathf.PerlinNoise(x * detailFrequency, z * detailFrequency) - 0.5f) * 2f * detailAmplitude;
        h += detail * (1f - MountainMask(nx, nz) * 0.5f);

        if (enableRiver) h += RiverDelta(nx, nz);

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

    // 确定性整数哈希 → [0,1)，用于逐格随机种子
    static float Hash01(int x, int z)
    {
        uint h = (uint)(x * 73856093) ^ (uint)(z * 19349663);
        h = (h ^ (h >> 13)) * 1274126177u;
        return (h & 0xFFFFFF) / (float)0x1000000;
    }

    // ---- colour palette (by absolute height, tracks the config) ---------

    Color GetZoneColor(float height)
    {
        float tPlains = (lowlandHeight + plainsHeight) * 0.5f;
        float tHighland = (plainsHeight + highlandHeight) * 0.5f;
        float tRock = highlandHeight + mountainHeight * 0.25f;
        float tSnow = highlandHeight + mountainHeight * 0.70f;

        // 参考风格化 low-poly：没有棕褐高地、没有白雪顶——只有鲜绿草地 + 冷调蓝灰岩石。
        if (height >= tSnow) return new Color(0.50f, 0.55f, 0.60f);    // 岩峰（浅冷灰，非白雪）
        if (height >= tRock) return new Color(0.32f, 0.38f, 0.45f);    // 板岩山体（蓝灰）
        if (height >= tHighland) return new Color(0.24f, 0.44f, 0.22f); // 高地林绿（偏暗）
        if (height >= tPlains) return new Color(0.34f, 0.56f, 0.25f);   // 平原鲜绿草
        return new Color(0.17f, 0.40f, 0.21f);                          // 近水低地深绿
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
        Vector2[] uvs = new Vector2[totalVertices];
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

                // 逐格随机种子（着色器用于逐面亮度/冷暖抖动）
                Vector2 seed = new Vector2(Hash01(x, z), Hash01(x + 131, z + 57));

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
                    uvs[index + i] = seed;             // 同一格 6 顶点共享种子
                    triangles[index + i] = index + i;
                }

                index += 6;
            }
        }

        mesh.vertices = vertices;
        mesh.colors = colors;
        mesh.uv = uvs;
        mesh.triangles = triangles;

        // 不共享顶点 + 此调用 → 每个三角面得到垂直于面的法线，硬面光影
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();

        GetComponent<MeshFilter>().mesh = mesh;
    }
}
