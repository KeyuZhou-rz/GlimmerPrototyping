using System;
using UnityEngine;
using UnityEngine.Rendering;

public class TerrainGenerator : MonoBehaviour
{
    public int width = 10;
    public int depth = 10;
    public float scale = 1f;

    // 定义你的地块类型映射
    private Color GetZoneColor(float height)
    {
        if (height > 1.5f) return Color.white;      // 雪地
        if (height > 1f) return Color.gray;       // 岩石
        return new Color(0.2f, 0.8f, 0.2f);         // 草地
    }

    void Start()
    {
        GenerateFlatTerrain();
    }

    float GetHeight(float x, float z)
{
    float t = x / (float)width;
    float tz = z / (float)depth;

    // === Z轴波浪：让边界线沿Z轴弯曲 ===
    // 用多层不同频率的sin叠加，模拟自然曲线（不用PerlinNoise是为了更可控）
    float wave = 
        Mathf.Sin(tz * Mathf.PI * 2.0f) * 0.08f +   // 大波，周期=整个Z轴
        Mathf.Sin(tz * Mathf.PI * 5.3f) * 0.04f +   // 中波
        Mathf.Sin(tz * Mathf.PI * 11.7f) * 0.02f;   // 小波（锯齿感）
    // wave 范围大约 -0.14 ~ +0.14，单位是归一化x坐标的偏移

    // === 平台边界（带Z方向波浪偏移）===
    float boundary1 = 0.35f + wave;
    float boundary2 = 0.65f + wave * 0.8f;  // 两条边界线波浪幅度略不同，更自然
    float sharpness = 0.04f;                // 控制断崖陡峭度

    float blend1 = Mathf.SmoothStep(0f, 1f,
        Mathf.InverseLerp(boundary1 - sharpness, boundary1 + sharpness, t));
    float blend2 = Mathf.SmoothStep(0f, 1f,
        Mathf.InverseLerp(boundary2 - sharpness, boundary2 + sharpness, t));

    // === 平台高度 ===
    float platformHeight = Mathf.Lerp(0f, 1.5f, blend1)
                         + Mathf.Lerp(0f, 2.0f, blend2);

    // === 平台内部细节（各平台独立起伏，幅度要小于平台高度差）===
    float detail = Mathf.PerlinNoise(x * 0.1f, z * 0.1f) * 0.25f;

    return platformHeight + detail;
}

    float Terrace(float h)
    {
        float step = 1.2f;
        float sharpness = 8f;

        float floored = Mathf.Floor(h / step) * step;
        float frac = (h % step) / step;

        float blend = Mathf.Pow(frac, sharpness);
        return floored + blend * step;
    }

    void GenerateFlatTerrain()
    {
        Mesh mesh = new Mesh();
        mesh.name = "Procedural Terrain";

        // 每个格子2个三角形 = 6个顶点
        int totalVertices = width * depth * 6;
        Vector3[] vertices = new Vector3[totalVertices];
        Color[] colors = new Color[totalVertices];
        int[] triangles = new int[totalVertices];

        int index = 0;

        // 遍历二维网格
        for (int z = 0; z < depth; z++)
        {
            for (int x = 0; x < width; x++)
            {
                float y00, y01, y10, y11;

                y00 = GetHeight(x, z);
                y10 = GetHeight(x + 1, z);
                y01 = GetHeight(x, z + 1);
                y11 = GetHeight(x + 1, z + 1);
                
                // 1. 计算当前方块四个角的高度 (使用柏林噪声模拟)
                /*
                float y00 = Mathf.PerlinNoise(x * 0.1f, z * 0.1f) * 3f;
                float y10 = Mathf.PerlinNoise((x + 1) * 0.1f, z * 0.1f) * 3f;
                float y01 = Mathf.PerlinNoise(x * 0.1f, (z + 1) * 0.1f) * 3f;
                float y11 = Mathf.PerlinNoise((x + 1) * 0.1f, (z + 1) * 0.1f) * 3f;
            */
                // 2. 决定当前方块的 Zone 数据 (这里取中心高度作为判定标准)
                float centerHeight = (y00 + y10 + y01 + y11) / 4f;
                Color zoneColor = GetZoneColor(centerHeight);
                

                // 3. 构建两个不共享顶点的三角形 (Triangle 1 & Triangle 2)
                Vector3 v00 = new Vector3(x, y00, z) * scale;
                Vector3 v10 = new Vector3(x + 1, y10, z) * scale;
                Vector3 v01 = new Vector3(x, y01, z + 1) * scale;
                Vector3 v11 = new Vector3(x + 1, y11, z + 1) * scale;

                // --- 三角形 1 ---
                vertices[index] = v00;
                vertices[index + 1] = v01;
                vertices[index + 2] = v10;
                
                // 将相同的颜色写入这3个顶点，确保颜色纯粹不渐变
                colors[index] = zoneColor;
                colors[index + 1] = zoneColor;
                colors[index + 2] = zoneColor;

                triangles[index] = index;
                triangles[index + 1] = index + 1;
                triangles[index + 2] = index + 2;

                // --- 三角形 2 ---
                vertices[index + 3] = v10;
                vertices[index + 4] = v01;
                vertices[index + 5] = v11;

                // 同样写入颜色
                colors[index + 3] = zoneColor;
                colors[index + 4] = zoneColor;
                colors[index + 5] = zoneColor;

                triangles[index + 3] = index + 3;
                triangles[index + 4] = index + 4;
                triangles[index + 5] = index + 5;

                index += 6;
            }
        }

        // 4. 将数据塞入 Mesh
        mesh.vertices = vertices;
        mesh.colors = colors;
        mesh.triangles = triangles;

        // 重新计算法线，Unity会自动为不共享的顶点生成垂直于面的法线 (这就是Flat Shading的光影来源)
        mesh.RecalculateNormals();

        // 绑定到组件
        GetComponent<MeshFilter>().mesh = mesh;
    }
}